using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THINGS BLOWING UP — and every other short-lived spark, puff and flash in a fight
//
// One pooled particle list, one loop. Everything a battle throws off goes through here:
//
//   BURST / FLAK   a round swatted out of the air by point defence: a hot pop, a ring and a few
//                  sparks. Silent — there can be dozens a second.
//   IMPACT         a shot landing on a HULL (shields down): a flash in the weapon's colour, a spray of
//                  streaking sparks, a glowing scorch that rides on the hull for a moment, a few dark
//                  specks of debris — and, for a heavy hit, a real point light.
//   SHIELD SPLASH  a shot stopped by a shield: a bright splash on the bubble's surface (the ripple
//                  itself is drawn by the bubble's own shader, ShieldBubble).
//   SHATTER        a shield collapsing: hard-edged pixel shards thrown off the shell.
//   DEATH          a ship coming apart, in stages: a white flash and a light, a fireball that cools
//                  white -> yellow -> orange -> smoke, debris chunks with ember trails, an expanding
//                  shockwave ring in the ecliptic, and a pall of smoke. Scaled by the hull. A STATION
//                  goes up in a longer sequence of secondary blasts before the final one.
//   SMOKE / SPARK  the small change: damage trails off a hurt hull, missile exhaust, beam scorch.
//
// ---- WHY THIS IS NOT A PARTICLE SYSTEM -------------------------------------------------------
//
// A ParticleSystem per explosion is an Instantiate per death and a prefab whose settings live in the
// editor instead of in this file. These are pooled quads drawn with FxKit's shared materials and
// per-renderer colour blocks, capped at MaxLive, with every size floored in PIXELS so an effect
// never vanishes at system zoom (see FxKit.Floor). Zero allocation per frame once warm.
//
// The cooling palette is a real one: white -> yellow -> orange -> red -> smoke is what hot things do as
// they lose energy, which is why it reads as heat rather than a coloured circle.
// ============================================================================================
public class ExplosionRenderer : MonoBehaviour
{
    public static ExplosionRenderer Instance;

    enum Look { Glow, Streak, Ring, Shard, Smoke, Chunk }

    class Puff
    {
        public Renderer r;
        public Transform tr;
        public Look look;
        public Vector3 pos, vel;
        public float life, maxLife;
        public float startScale, endScale, minPx;
        public Color a, b;            // colour at birth and at death (ignored when `ramp`)
        public bool ramp;             // cooling ramp instead of a -> b
        public float alpha;           // peak opacity
        public float spin, angle;
        public float drag;
        public float stretch;         // streak length as a multiple of width
        public bool flat;             // lies in the ecliptic (shockwave rings) instead of facing the camera
        public Transform follow;      // rides on a hull (scorch marks)
        public Vector3 followLocal;
        public float emberEvery, emberT;   // chunks that shed embers as they tumble
    }

    readonly List<Puff> live = new List<Puff>(512);
    readonly Stack<Puff>[] pool = new Stack<Puff>[6];

    /// A hard ceiling on live quads. A fleet action that killed forty ships in one second would
    /// otherwise queue a thousand of these and cost more than the battle did. Sparks and smoke are
    /// dropped first (Spawn refuses low-priority work past SoftCap).
    const int MaxLive = 640, SoftCap = 480;

    // ---- lights for the big moments ---------------------------------------------------------------
    class Flash { public Light light; public float life, maxLife, peak; }
    readonly List<Flash> flashes = new List<Flash>();
    const int MaxFlashLights = 6;

    // ---- the station sequence: blasts scheduled into the next couple of seconds ----------------------
    struct Pending { public float at; public Vector3 pos; public float size; public bool final; public bool station; }
    readonly List<Pending> pending = new List<Pending>();

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("ExplosionRenderer");
        go.AddComponent<ExplosionRenderer>();
        go.AddComponent<MapTierVisibility>();
    }

    void Awake()
    {
        Instance = this;
        for (int i = 0; i < pool.Length; i++) pool[i] = new Stack<Puff>();
    }

    // ============================================================================================
    // THE EVENTS
    // ============================================================================================

    /// A round swatted down by point defence. Cheap, silent, and over immediately.
    public void Burst(Vector3 at, float scale, Color colour)
    {
        if (!FxKit.SystemView) return;
        Glow(at, Vector3.zero, 0.18f, scale * 0.4f, scale * 1.2f, 6f, Hot(colour, 1.6f), colour * 0.3f, 1f);
        Spawn(Look.Ring, at, Vector3.zero, 0.22f, scale * 0.2f, scale * 1.4f, 7f, Hot(colour, 1.2f), colour * 0.2f, 0.8f, false, 0f, 0f, true);
        for (int i = 0; i < 4; i++)
            Spark(at, Random.onUnitSphere * Random.Range(3f, 6f), colour, 0.16f, scale * 0.12f);
    }

    /// A flak-style airburst — point defence catching a heavy round (plasma, torpedo). Bigger, with a
    /// little smoke left hanging where it went off.
    public void Flak(Vector3 at, Color colour)
    {
        if (!FxKit.SystemView) return;
        Burst(at, 0.55f, colour);
        Smoke(at, 0.35f, new Color(0.35f, 0.33f, 0.32f), 0.45f, 0.9f);
    }

    /// A shot landing on a hull that survives it. `severity` is how much of the hull it took (0..1),
    /// which sizes the flash and decides whether it earns a light. `hull` (may be null) is the drawn
    /// hull, so the scorch can ride on it rather than hang in space as the ship moves on.
    public void Impact(Vector3 at, Color weaponColour, float severity = 0.05f, Transform hull = null, Vector3 inbound = default)
    {
        if (!FxKit.SystemView) return;
        severity = Mathf.Clamp01(severity);
        float s = Mathf.Lerp(0.22f, 0.7f, Mathf.Sqrt(severity));

        // The flash, in the weapon's colour so the player can read what is hitting them.
        Glow(at, Vector3.zero, 0.16f, s * 0.6f, s * 1.5f, 10f, Hot(weaponColour, 2.2f), weaponColour * 0.4f, 1f);
        // A white-hot pinpoint at the centre.
        Glow(at, Vector3.zero, 0.09f, s * 0.25f, s * 0.5f, 5f, new Color(2.2f, 2.1f, 1.9f), Color.white, 1f);

        // Sparks, thrown back the way the round came and outward.
        Vector3 back = inbound.sqrMagnitude > 1e-6f ? -inbound.normalized : Vector3.zero;
        int sparks = 5 + Mathf.RoundToInt(severity * 10f);
        for (int i = 0; i < sparks; i++)
        {
            Vector3 dir = (Random.onUnitSphere + back * 0.8f).normalized;
            Spark(at, dir * Random.Range(2.5f, 7f), Color.Lerp(weaponColour, new Color(1f, 0.85f, 0.5f), 0.5f), Random.Range(0.18f, 0.34f), s * 0.16f);
        }

        // The scorch: a dull orange glow left ON the hull, fading over half a second.
        if (hull != null)
        {
            var p = Glow(at, Vector3.zero, 0.55f, s * 0.35f, s * 0.25f, 3f, new Color(1.6f, 0.55f, 0.15f), new Color(0.3f, 0.05f, 0f), 0.9f);
            if (p != null) { p.follow = hull; p.followLocal = hull.InverseTransformPoint(at); }
        }

        // Debris: a few dark specks.
        int bits = 2 + Mathf.RoundToInt(severity * 5f);
        for (int i = 0; i < bits; i++)
            Spawn(Look.Chunk, at, Random.onUnitSphere * Random.Range(1.2f, 3.5f), Random.Range(0.4f, 0.8f),
                  s * 0.09f, s * 0.05f, 2f, new Color(0.25f, 0.22f, 0.2f), new Color(0.12f, 0.1f, 0.1f), 1f, false, 2.2f, 0f, false);

        // A heavy hit lights the scene for a moment — a real light, independent of where the camera is.
        if (severity > 0.12f) Light(at, Color.Lerp(weaponColour, Color.white, 0.4f), Mathf.Lerp(2f, 6f, severity), s * 8f, 0.25f);
    }

    /// A shot stopped by a shield: a bright splash on the shell. The ripple ring is the bubble's own.
    public void ShieldSplash(Vector3 at, Color shieldColour, float strength)
    {
        if (!FxKit.SystemView) return;
        float s = Mathf.Lerp(0.18f, 0.5f, Mathf.Clamp01(strength));
        Glow(at, Vector3.zero, 0.22f, s * 0.5f, s * 1.3f, 8f, Hot(shieldColour, 2f), shieldColour * 0.2f, 1f);
        Spawn(Look.Ring, at, Vector3.zero, 0.3f, s * 0.2f, s * 1.1f, 6f, Hot(shieldColour, 1.4f), shieldColour * 0.1f, 0.9f, false, 0f, 0f, false);
        for (int i = 0; i < 3; i++)
            Spark(at, Random.onUnitSphere * Random.Range(1.5f, 3.5f), shieldColour, 0.2f, s * 0.1f);
    }

    /// A shield collapsing: pixel shards flung off the shell, a ring, and a sharp flash.
    public void ShieldShatter(Vector3 centre, float radius, Color shieldColour)
    {
        if (!FxKit.SystemView) return;
        radius = Mathf.Max(0.05f, radius);
        Glow(centre, Vector3.zero, 0.25f, radius * 1.2f, radius * 2.6f, 18f, Hot(shieldColour, 1.8f), shieldColour * 0.1f, 0.8f);
        Spawn(Look.Ring, centre, Vector3.zero, 0.45f, radius, radius * 3.2f, 16f, Hot(shieldColour, 1.5f), shieldColour * 0.1f, 1f, false, 0f, 0f, false);
        for (int i = 0; i < 18; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            Spawn(Look.Shard, centre + dir * radius, dir * Random.Range(1.2f, 3.2f) * Mathf.Max(1f, radius * 4f),
                  Random.Range(0.45f, 0.8f), radius * Random.Range(0.18f, 0.32f), radius * 0.05f, 3f,
                  Hot(shieldColour, 1.6f), shieldColour * 0.15f, 1f, false, 1.4f, 0f, false);
        }
        Light(centre, shieldColour, 3f, radius * 12f, 0.3f);
    }

    /// One drifting puff of smoke. Damage trails, missile exhaust, the pall after a death.
    public void Smoke(Vector3 at, float size, Color colour, float alpha = 0.55f, float life = 1.2f)
    {
        if (!FxKit.SystemView || live.Count >= SoftCap) return;
        Spawn(Look.Smoke, at, Random.insideUnitSphere * 0.25f, life * Random.Range(0.8f, 1.2f), size * 0.5f, size * 1.6f, 2.5f,
              colour, colour * 0.6f, alpha, false, 1.5f, 0f, false);
    }

    /// A single hot spark streaking along `vel`.
    public void Spark(Vector3 at, Vector3 vel, Color colour, float life, float width)
    {
        if (!FxKit.SystemView || live.Count >= SoftCap) return;
        Spawn(Look.Streak, at, vel, life, width, width * 0.4f, 1.5f, Hot(colour, 1.8f), colour * 0.3f, 1f, false, 3.2f, 4.5f, false);
    }

    /// A bright pinpoint glint — a beam charging at the muzzle, a railgun capacitor letting go.
    public void Glint(Vector3 at, Color colour, float size, float life)
    {
        if (!FxKit.SystemView) return;
        Glow(at, Vector3.zero, life, size * 0.3f, size, 5f, Hot(colour, 2.5f), colour * 0.5f, 1f);
        Spawn(Look.Streak, at, Vector3.zero, life, size * 0.12f, size * 0.1f, 2f, Hot(colour, 2f), colour * 0.4f, 1f, false, 0f, 9f, false);
    }

    /// A ship coming apart. `size` is the hull's scale (CombatManager.HullScale, 0.4..5). A station
    /// goes up in a sequence: a run of secondary blasts round the structure, then the big one.
    public void Death(Vector3 at, float size, bool station = false)
    {
        size = Mathf.Clamp(size, 0.35f, 6f);
        if (station)
        {
            int n = Mathf.RoundToInt(Mathf.Lerp(4f, 9f, Mathf.InverseLerp(0.4f, 5f, size)));
            float span = Mathf.Lerp(1.4f, 2.6f, Mathf.InverseLerp(0.4f, 5f, size));
            for (int i = 0; i < n; i++)
                pending.Add(new Pending
                {
                    at = Time.time + span * (i / (float)n) + Random.Range(0f, 0.12f),
                    pos = at + Random.insideUnitSphere * size * 0.35f,
                    size = size * Random.Range(0.25f, 0.45f),
                    station = true,
                });
            pending.Add(new Pending { at = Time.time + span + 0.15f, pos = at, size = size * 1.25f, final = true, station = true });
            return;
        }
        Detonate(at, size, true);
    }

    /// The death proper: every stage, now.
    void Detonate(Vector3 at, float size, bool full)
    {
        if (!FxKit.SystemView) return;

        // 1. FLASH — white, huge, gone in a tenth of a second, and a light that floods the area.
        Glow(at, Vector3.zero, 0.12f, size * 1.2f, size * 2.6f, 40f, new Color(3f, 2.9f, 2.6f), Color.white, 1f);
        Light(at, new Color(1f, 0.85f, 0.6f), Mathf.Lerp(4f, 9f, size / 6f), size * 9f, full ? 0.6f : 0.35f);

        // 2. FIREBALL — a core and a slower shell, cooling through the ramp as they swell.
        Ramp(at, Vector3.zero, 0.85f, size * 0.5f, size * 3.2f, 26f);
        Ramp(at, Vector3.zero, 1.25f, size * 0.2f, size * 4.4f, 20f);
        if (!full) return;

        // 3. DEBRIS — tumbling chunks thrown outward, each shedding embers as it goes.
        float k = Mathf.InverseLerp(0.35f, 6f, size);
        int chunks = Mathf.RoundToInt(Mathf.Lerp(6f, 16f, k));
        for (int i = 0; i < chunks; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y *= 0.4f;                              // mostly in the plane the game is played on
            float speed = Random.Range(2.5f, 8f) * Mathf.Lerp(0.8f, 1.6f, k);
            var p = Spawn(Look.Chunk, at, dir.normalized * speed, Random.Range(0.9f, 1.6f), size * Random.Range(0.08f, 0.16f),
                          size * 0.05f, 2.5f, new Color(0.32f, 0.28f, 0.25f), new Color(0.1f, 0.09f, 0.09f), 1f, false, 1.4f, 0f, false);
            if (p != null) { p.emberEvery = 0.06f; p.emberT = Random.Range(0f, 0.06f); }
        }
        // ...and a spray of fast sparks.
        int sparks = Mathf.RoundToInt(Mathf.Lerp(10f, 26f, k));
        for (int i = 0; i < sparks; i++)
            Spark(at, Random.onUnitSphere * Random.Range(5f, 12f) * Mathf.Lerp(0.8f, 1.5f, k),
                  new Color(1f, 0.75f, 0.35f), Random.Range(0.3f, 0.6f), size * 0.08f);

        // 4. SHOCKWAVE — a ring lying in the ecliptic, racing outward and thinning.
        Spawn(Look.Ring, at, Vector3.zero, 0.7f, size * 0.4f, size * 5f, 30f, new Color(1.6f, 1.3f, 1f), new Color(0.4f, 0.25f, 0.15f), 0.9f, false, 0f, 0f, true);

        // 5. SMOKE — the pall that hangs where the ship was.
        for (int i = 0; i < 4; i++)
            Smoke(at + Random.insideUnitSphere * size * 0.3f, size * 1.1f, new Color(0.22f, 0.2f, 0.2f), 0.5f, 2.2f);
    }

    // ============================================================================================
    // SPAWNING
    // ============================================================================================

    Puff Glow(Vector3 at, Vector3 vel, float life, float from, float to, float minPx, Color a, Color b, float alpha)
        => Spawn(Look.Glow, at, vel, life, from, to, minPx, a, b, alpha, false, 0f, 0f, false);

    Puff Ramp(Vector3 at, Vector3 vel, float life, float from, float to, float minPx)
        => Spawn(Look.Glow, at, vel, life, from, to, minPx, Color.white, Color.white, 1f, true, 0f, 0f, false);

    Puff Spawn(Look look, Vector3 at, Vector3 vel, float life, float from, float to, float minPx,
               Color a, Color b, float alpha, bool ramp, float drag, float stretch, bool flat)
    {
        if (live.Count >= MaxLive) return null;
        var p = Rent(look);
        p.pos = at; p.vel = vel;
        p.life = 0f; p.maxLife = Mathf.Max(0.02f, life);
        p.startScale = from; p.endScale = to; p.minPx = minPx;
        p.a = a; p.b = b; p.ramp = ramp; p.alpha = alpha;
        p.spin = Random.Range(-160f, 160f); p.angle = Random.Range(0f, 360f);
        p.drag = drag; p.stretch = stretch; p.flat = flat;
        p.follow = null; p.followLocal = Vector3.zero;
        p.emberEvery = 0f; p.emberT = 0f;
        p.tr.gameObject.SetActive(true);
        live.Add(p);
        Draw(p, 0f);
        return p;
    }

    /// A pooled point light that flares and dies away. Hard-capped: lights are the one thing here that
    /// is not nearly free.
    void Light(Vector3 at, Color c, float intensity, float range, float life)
    {
        if (!ProjectileRenderer.DynamicLights) return;
        Flash f = null;
        foreach (var x in flashes) if (x.life >= x.maxLife) { f = x; break; }
        if (f == null)
        {
            if (flashes.Count >= MaxFlashLights)
            {
                // Steal the weakest one rather than drop the new event — the newest blast matters most.
                f = flashes[0];
                for (int i = 1; i < flashes.Count; i++)
                    if (flashes[i].peak * (1f - flashes[i].life / flashes[i].maxLife) < f.peak * (1f - f.life / f.maxLife)) f = flashes[i];
            }
            else
            {
                var go = new GameObject("FxLight");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                f = new Flash { light = l };
                flashes.Add(f);
            }
        }
        f.light.transform.position = at;
        f.light.color = c;
        f.light.range = Mathf.Clamp(range, 0.5f, 40f);
        f.peak = intensity;
        f.light.intensity = intensity;
        f.light.enabled = true;
        f.life = 0f; f.maxLife = Mathf.Max(0.05f, life);
    }

    // ============================================================================================
    // THE LOOP
    // ============================================================================================
    void Update()
    {
        // Frame time: an explosion is an animation, not a simulation. (Time.deltaTime is still zero
        // while paused, so a paused game freezes its fireballs mid-bloom rather than skipping them.)
        float dt = Time.deltaTime;

        // ---- galaxy zoom ----
        // MapTierVisibility switches every child off at galaxy zoom and back ON when the camera
        // returns — pooled quads included, which would bring every retired puff back for a frame
        // in whatever state it was retired in. So: drop everything on the way out, and re-hide the
        // pool on the way back in.
        bool sys = FxKit.SystemView;
        if (sys != wasSystem)
        {
            wasSystem = sys;
            if (!sys) ClearAll();
            else HidePooled();
        }
        if (dt <= 0f) return;

        // ---- the station sequence ----
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (Time.time < pending[i].at) continue;
            var e = pending[i];
            pending.RemoveAt(i);
            if (e.final)
            {
                Detonate(e.pos, e.size, true);
                // A second, wider ring a beat behind the first: a station is a bigger thing letting go.
                Spawn(Look.Ring, e.pos, Vector3.zero, 1.1f, e.size * 0.6f, e.size * 9f, 34f, new Color(1.2f, 1f, 0.8f), new Color(0.3f, 0.2f, 0.1f), 0.7f, false, 0f, 0f, true);
                { var a = SimpleAudio.Instance; if (a != null) a.PlayShipDestroyed(e.size); }
            }
            else
            {
                Detonate(e.pos, e.size, false);
                for (int k = 0; k < 5; k++)
                    Spark(e.pos, Random.onUnitSphere * Random.Range(3f, 8f), new Color(1f, 0.7f, 0.3f), 0.4f, e.size * 0.1f);
                { var a = SimpleAudio.Instance; if (a != null) a.PlayImpact(e.pos, 60f); }
            }
        }

        for (int i = live.Count - 1; i >= 0; i--)
        {
            var p = live[i];
            p.life += dt;
            if (p.life >= p.maxLife || p.tr == null) { Retire(p); live.RemoveAt(i); continue; }

            if (p.follow != null) p.pos = p.follow.TransformPoint(p.followLocal);
            else
            {
                p.pos += p.vel * dt;
                if (p.drag > 0f) p.vel *= 1f - Mathf.Min(0.9f, p.drag * dt);
            }
            p.angle += p.spin * dt;

            // Tumbling wreckage sheds embers.
            if (p.emberEvery > 0f)
            {
                p.emberT -= dt;
                if (p.emberT <= 0f && live.Count < SoftCap)
                {
                    p.emberT = p.emberEvery;
                    Glow(p.pos, p.vel * 0.1f, 0.35f, p.startScale * 0.5f, p.startScale * 0.15f, 1.5f,
                         new Color(1.8f, 0.7f, 0.2f), new Color(0.4f, 0.05f, 0f), 0.9f);
                }
            }

            Draw(p, Mathf.Clamp01(p.life / p.maxLife));
        }

        for (int i = 0; i < flashes.Count; i++)
        {
            var f = flashes[i];
            if (f.life >= f.maxLife) continue;
            f.life += dt;
            float k = Mathf.Clamp01(f.life / f.maxLife);
            f.light.intensity = f.peak * (1f - k) * (1f - k);
            if (k >= 1f) f.light.enabled = false;
        }
    }

    void Draw(Puff p, float k)
    {
        float scale = Mathf.Lerp(p.startScale, p.endScale, EaseOut(k));
        scale = FxKit.Floor(p.pos, scale, p.minPx * (1f - 0.5f * k));

        Color c = p.ramp ? CoolingRamp(k) : Color.Lerp(p.a, p.b, k);
        float fade = p.look == Look.Smoke ? Mathf.Sin(Mathf.Min(1f, k * 1.6f) * Mathf.PI * 0.5f + 0.0001f) * (1f - k)
                                           : 1f - k * k;
        float alpha = p.alpha * fade;

        p.tr.position = p.pos;
        if (p.flat)
        {
            p.tr.rotation = Quaternion.Euler(90f, p.angle, 0f);
            p.tr.localScale = new Vector3(scale, scale, 1f);
        }
        else if (p.look == Look.Streak && p.stretch > 0f)
        {
            float len = scale * p.stretch * (p.vel.sqrMagnitude > 1e-4f ? Mathf.Clamp(p.vel.magnitude * 0.25f, 0.6f, 2.5f) : 1f);
            p.tr.rotation = FxKit.Streak(p.vel.sqrMagnitude > 1e-6f ? p.vel : Vector3.up);
            p.tr.localScale = new Vector3(scale, len, 1f);
        }
        else
        {
            p.tr.rotation = FxKit.Billboard(p.angle);
            p.tr.localScale = new Vector3(scale, scale, 1f);
        }

        if (p.look == Look.Smoke || p.look == Look.Chunk)
        {
            // Alpha-blended: darkening, not glowing.
            c.a = Mathf.Clamp01(alpha);
            FxKit.SetColour(p.r, c);
        }
        else
        {
            // Additive: the colour carries the brightness, alpha scales it.
            c.a = Mathf.Clamp01(alpha);
            if (p.ramp) c = new Color(c.r * 2.2f, c.g * 2.0f, c.b * 1.8f, c.a);
            FxKit.SetColour(p.r, c);
        }
    }

    /// White, yellow, orange, red, smoke — what a hot thing looks like as it loses energy.
    static Color CoolingRamp(float k)
    {
        if (k < 0.15f) return Color.Lerp(new Color(1f, 1f, 1f), new Color(1f, 0.95f, 0.65f), k / 0.15f);
        if (k < 0.40f) return Color.Lerp(new Color(1f, 0.95f, 0.65f), new Color(1f, 0.60f, 0.18f), (k - 0.15f) / 0.25f);
        if (k < 0.70f) return Color.Lerp(new Color(1f, 0.60f, 0.18f), new Color(0.85f, 0.22f, 0.10f), (k - 0.40f) / 0.30f);
        return Color.Lerp(new Color(0.85f, 0.22f, 0.10f), new Color(0.18f, 0.16f, 0.16f), (k - 0.70f) / 0.30f);
    }

    /// The colour, pushed past bloom's threshold so the core of a flash actually glows.
    static Color Hot(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

    static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    bool wasSystem = true;

    void HidePooled()
    {
        foreach (var stack in pool)
            foreach (var p in stack)
                if (p.tr != null && p.tr.gameObject.activeSelf) p.tr.gameObject.SetActive(false);
    }

    public void ClearAll()
    {
        for (int i = live.Count - 1; i >= 0; i--) Retire(live[i]);
        live.Clear();
        pending.Clear();
        foreach (var f in flashes) { f.life = f.maxLife; if (f.light != null) f.light.enabled = false; }
    }

    // ============================================================================================
    // POOLING
    // ============================================================================================
    Puff Rent(Look look)
    {
        var stack = pool[(int)look];
        if (stack.Count > 0) return stack.Pop();

        Material mat;
        switch (look)
        {
            case Look.Streak: mat = FxKit.SharedAdditive(FxKit.StreakTex()); break;
            case Look.Ring: mat = FxKit.SharedAdditive(FxKit.RingTex()); break;
            case Look.Shard: mat = FxKit.SharedAdditive(FxKit.ShardTex()); break;
            case Look.Smoke: mat = FxKit.SharedBlend(FxKit.SmokeTex()); break;
            case Look.Chunk: mat = FxKit.SharedBlend(FxKit.ShardTex()); break;
            default: mat = FxKit.SharedAdditive(FxKit.Dot()); break;
        }
        var r = FxKit.MakeQuad(transform, "Fx" + look, mat);
        return new Puff { r = r, tr = r.transform, look = look };
    }

    void Retire(Puff p)
    {
        p.follow = null;
        if (p.tr != null) p.tr.gameObject.SetActive(false);
        if (p.tr != null) pool[(int)p.look].Push(p);
    }
}
