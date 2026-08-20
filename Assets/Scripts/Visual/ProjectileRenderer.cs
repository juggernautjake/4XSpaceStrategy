using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// SHOTS IN FLIGHT
//
// Every round fired in the game is one entry in one list, updated by one loop, drawn by one pooled
// renderer. There is no per-projectile GameObject churn and no Instantiate in the firing path: a
// battle that fires two hundred rounds a second would otherwise spend its entire frame budget
// allocating and collecting quads.
//
// ---- TWO KINDS OF SHOT, AND THE DIFFERENCE IS `projectileSpeed` -------------------------------
//
//   INSTANT (speed 0)   A beam or a railgun slug. There is no travel: the damage has already landed
//                       by the time anything is drawn, and what we draw is the AFTERIMAGE — a line
//                       from muzzle to target that fades over a fraction of a second. Drawing it any
//                       other way would be a lie about when the hit happened.
//
//   TRAVELLING          A bolt, a plasma ball, a missile. It has a position, a velocity, and — if the
//                       weapon has a turn rate — it steers toward its target every frame. Damage lands
//                       when it arrives, so a target that dies first leaves its inbound rounds flying
//                       on to nothing, which is correct and is also the reason a missile volley can be
//                       wasted.
//
// ---- HOMING, AND WHY IT IS RATE-LIMITED RATHER THAN EXACT -------------------------------------
//
// A missile that simply set its velocity at its target every frame would be unmissable and would look
// like it was on a string. It turns at `turnRate` degrees per second instead, so it leads, overshoots
// a hard-manoeuvring target and comes back around — and a fast enough ship genuinely outruns it. That
// single number is the whole difference between "a homing weapon" and "a hitscan weapon with a delay".
//
// ---- WHY THE ART IS PROCEDURAL ----------------------------------------------------------------
//
// Same rule as every other visual in this project: no imported asset is load-bearing. A bolt is a
// quad with a generated additive texture, a beam is a LineRenderer, and both take their colour and
// width from the WEAPON (see Weaponry) rather than from a table here — so adding a weapon needs no
// change to this file at all.
// ============================================================================================
public class ProjectileRenderer : MonoBehaviour
{
    public static ProjectileRenderer Instance;

    /// A round in flight, or the fading afterimage of an instant one.
    class Shot
    {
        public WeaponInfo weapon;
        public Unit shooter;                 // for credit on the kill; may die mid-flight
        public Unit target;                  // may die mid-flight, leaving the round to fly on
        public Vector3 pos, vel;
        public Vector3 endPoint;             // instant shots: where the line stops
        public float damage;
        public float life, maxLife;
        public bool instant;
        public bool dead;

        public Transform tr;                 // pooled quad (travelling) — null for instant shots
        public Light light;                  // pooled point light, so the round lights what it passes
        public float phaseOffset;            // per-shot offset so a volley pulses out of step, not as one strobe
        public LineRenderer beam;            // pooled line (instant) — null for travelling shots
    }

    readonly List<Shot> shots = new List<Shot>();
    readonly Stack<Transform> quadPool = new Stack<Transform>();
    readonly Stack<LineRenderer> beamPool = new Stack<LineRenderer>();
    readonly Stack<Light> lightPool = new Stack<Light>();

    Material boltMat;
    Material beamMat;
    Texture2D boltTex;

    /// How long an instant shot's afterimage hangs about. Short enough to read as a flash rather than
    /// a rope, long enough to survive a frame drop.
    const float InstantLife = 0.13f;

    /// A travelling round gives up after this long even if its target vanished — otherwise a missile
    /// whose target died flies to the edge of the galaxy and stays in the list forever.
    const float MaxFlight = 6f;

    /// How close a travelling round has to get to count as a hit. Ships are drawn well under a unit
    /// across at fighting zoom, so this is generous on purpose: a round that visually clips the hull
    /// should land, and one that needed sub-pixel accuracy would read as a miss that wasn't.
    const float HitRadius = 0.55f;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("ProjectileRenderer");
        go.AddComponent<ProjectileRenderer>();
        // Shots vanish at galaxy zoom along with the hulls firing them — see MapTierVisibility.
        go.AddComponent<MapTierVisibility>();
    }

    void Awake()
    {
        Instance = this;
        boltTex = BoltTexture();
        boltMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = boltTex };
        beamMat = new Material(Shader.Find("Sprites/Default"));
    }

    /// How many rounds are in the air. The combat manager uses this to stop a pathological fight from
    /// filling the list; the UI uses it for nothing, which is why it is the only thing exposed.
    public int LiveCount => shots.Count;

    // ============================================================================================
    // FIRING
    // ============================================================================================

    /// Fire one round. `from`/`to` are world positions; the round is drawn from `from` and lands on
    /// `target`. Returns immediately — nothing here blocks and nothing allocates a GameObject unless
    /// the pool is empty.
    public void Fire(Unit shooter, Unit target, WeaponInfo w, Vector3 from, Vector3 to, float damage)
    {
        if (w == null) return;

        var s = new Shot
        {
            weapon = w, shooter = shooter, target = target,
            pos = from, endPoint = to, damage = damage,
            instant = w.projectileSpeed <= 0.01f,
            phaseOffset = Random.Range(0f, Mathf.PI * 2f)
        };

        if (s.instant)
        {
            s.maxLife = InstantLife;
            s.beam = RentBeam();
            s.beam.startColor = s.beam.endColor = w.colour * w.glow;
            s.beam.widthMultiplier = w.width;
            s.beam.SetPosition(0, from);
            s.beam.SetPosition(1, to);
        }
        else
        {
            Vector3 dir = (to - from);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            s.vel = dir * w.projectileSpeed;
            s.maxLife = MaxFlight;
            s.tr = RentQuad();
            var mr = s.tr.GetComponent<MeshRenderer>();
            mr.material.color = w.colour * w.glow;
            s.tr.localScale = new Vector3(w.width * 2f, w.length, 1f);
        }

        // A real light on the round, so a shot actually illuminates what it passes and what it hits.
        // Range is tied to the weapon's own glow and width, so a heavy plasma bolt throws light and a
        // point-defence needle barely does — one number per weapon, no extra table.
        s.light = RentLight();
        if (s.light != null)
        {
            s.light.color = w.colour;
            s.light.range = Mathf.Clamp(w.width * 26f + w.glow * 1.6f, 0.6f, 6f);
            s.light.intensity = Mathf.Clamp(w.glow * 2.2f, 0.6f, 7f);
            s.light.transform.position = from;
            s.light.enabled = true;
        }

        shots.Add(s);
        SimpleAudio.Instance?.PlayWeapon(w.cls, from);

        // The muzzle lights up on the SAME call that creates the round, so the flash and the shot can
        // never drift apart. Deriving it from the weapon's cooldown instead would re-time it against a
        // different clock and show a flash with no bullet the moment anything hitched.
        UnitModelRenderer.Instance?.LightsOf(shooter)?.FlashMuzzle(from, w.colour);
    }

    // ============================================================================================
    // THE ONE LOOP
    // ============================================================================================
    void Update()
    {
        // Frame time, not game time: a shot's flight is an ANIMATION. Pausing the game should stop the
        // simulation, and it does — CombatManager reads the scaled clock and stops firing — but a round
        // already in the air finishing its arc at 5x speed would just look broken.
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var s = shots[i];
            s.life += dt;

            if (s.instant)
            {
                // Fade the afterimage out over its life. Nothing moves; only the alpha falls.
                float k = 1f - Mathf.Clamp01(s.life / s.maxLife);
                if (s.beam != null)
                {
                    Color c = s.weapon.colour * s.weapon.glow;
                    c.a = k;
                    s.beam.startColor = s.beam.endColor = c;
                }
                // The flash lights the gap it crosses. Sat at the midpoint because a beam is lit along
                // its whole length and one point light is a great deal cheaper than a line of them.
                if (s.light != null)
                {
                    s.light.transform.position = Vector3.Lerp(s.pos, s.endPoint, 0.5f);
                    s.light.intensity = Mathf.Clamp(s.weapon.glow * 2.2f, 0.6f, 7f) * k;
                }
                if (s.life >= s.maxLife) { Retire(s); shots.RemoveAt(i); }
                continue;
            }

            // ---- Steering, for anything with a turn rate ----
            Vector3 aim = TargetPos(s);
            if (s.weapon.turnRate > 0f && s.target != null && !s.target.IsDestroyed)
            {
                Vector3 want = aim - s.pos;
                if (want.sqrMagnitude > 1e-6f)
                {
                    // RotateTowards, not Slerp-to-target: the cap is what makes the missile miss a
                    // sharp turn and come back around, which is the entire behaviour being bought.
                    s.vel = Vector3.RotateTowards(s.vel, want.normalized * s.vel.magnitude,
                                                  s.weapon.turnRate * Mathf.Deg2Rad * dt, 0f);
                }
            }

            s.pos += s.vel * dt;

            if (s.tr != null)
            {
                s.tr.position = s.pos;
                if (s.vel.sqrMagnitude > 1e-6f)
                    s.tr.rotation = Quaternion.LookRotation(Vector3.forward, s.vel.normalized);
            }

            // The light rides along with the round, so hulls it passes are lit as it goes by and the
            // one it is about to hit brightens as it closes.
            if (s.light != null) s.light.transform.position = s.pos;

            // ---- plasma breathes ------------------------------------------------------------------
            //
            // A plasma bolt is a contained ball of energy, and a perfectly steady sprite reads as a
            // painted slug instead. Pulsing the bolt AND its light together sells it as something
            // barely held together.
            //
            // On the fleet beat, like the running lights and the drive plumes — so everything glowing
            // in a battle is breathing to one clock rather than each on its own unrelated cycle. The
            // per-shot offset keeps a volley from pulsing in unison, which would read as a strobe.
            if (s.weapon.cls == WeaponClass.PlasmaCannon)
            {
                float pulse = 0.82f + 0.18f * Mathf.Sin(FleetClock.Beats * Mathf.PI * 4f + s.phaseOffset);
                if (s.tr != null)
                    s.tr.localScale = new Vector3(s.weapon.width * 2f * pulse, s.weapon.length * pulse, 1f);
                if (s.light != null)
                    s.light.intensity = Mathf.Clamp(s.weapon.glow * 2.2f, 0.6f, 7f) * pulse;
            }

            // ---- Arrival ----
            bool arrived = s.target != null && !s.target.IsDestroyed &&
                           Vector3.Distance(s.pos, aim) <= HitRadius;

            // A dumb-fire round that reached where it was aimed has arrived even if the target moved —
            // that is precisely what "no tracking" means, and it is why a pulse laser misses.
            if (!arrived && s.weapon.turnRate <= 0f && Vector3.Distance(s.pos, s.endPoint) <= HitRadius)
            {
                bool stillThere = s.target != null && !s.target.IsDestroyed &&
                                  Vector3.Distance(aim, s.endPoint) <= HitRadius * 2.2f;
                if (stillThere) arrived = true;
                else { CombatManager.Instance?.ReportMiss(s.shooter); Retire(s); shots.RemoveAt(i); continue; }
            }

            if (arrived)
            {
                CombatManager.Instance?.ResolveHit(s.shooter, s.target, s.weapon, s.damage, s.pos);
                Retire(s); shots.RemoveAt(i); continue;
            }

            if (s.life >= s.maxLife || s.dead) { Retire(s); shots.RemoveAt(i); }
        }
    }

    /// Where a shot is currently aiming: its target's live position, or the point it was fired at if
    /// the target is gone.
    Vector3 TargetPos(Shot s)
    {
        if (s.target == null || s.target.IsDestroyed) return s.endPoint;
        var t = UnitVisuals.TransformOf(s.target);
        return t != null ? t.position : s.endPoint;
    }

    /// Delete every round aimed at this unit that a point-defence mount managed to catch. Returns how
    /// many were destroyed, so the caller can decide whether it was worth a sound.
    public int InterceptIncoming(Unit protectedUnit, Vector3 screenCentre, float radius, int maxKills)
    {
        int killed = 0;
        for (int i = shots.Count - 1; i >= 0 && killed < maxKills; i--)
        {
            var s = shots[i];
            if (s.instant || !s.weapon.interceptable) continue;   // beams and slugs cannot be caught
            if (s.target != protectedUnit) continue;
            if (Vector3.Distance(s.pos, screenCentre) > radius) continue;

            ExplosionRenderer.Instance?.Burst(s.pos, 0.35f, s.weapon.colour);
            Retire(s); shots.RemoveAt(i); killed++;
        }
        return killed;
    }

    /// Forget everything in the air. Called when a galaxy is replaced — the shots reference Units that
    /// are about to stop existing.
    public void ClearAll()
    {
        for (int i = shots.Count - 1; i >= 0; i--) Retire(shots[i]);
        shots.Clear();
    }

    // ============================================================================================
    // POOLING
    // ============================================================================================
    void Retire(Shot s)
    {
        if (s.tr != null) { s.tr.gameObject.SetActive(false); quadPool.Push(s.tr); s.tr = null; }
        if (s.beam != null) { s.beam.enabled = false; beamPool.Push(s.beam); s.beam = null; }
        if (s.light != null) { s.light.enabled = false; lightPool.Push(s.light); s.light = null; }
    }

/// A pooled point light for a round in flight.
    ///
    /// HARD-CAPPED, and it has to be. Real-time lights are the one thing here that is not nearly free:
    /// URP culls per object and a battle putting two hundred rounds in the air would hand the renderer
    /// two hundred lights to sort. Past the cap this returns null and the round simply flies unlit —
    /// which nobody notices in a firefight already lit by the ones that got a light.
    /// Real point lights on rounds in flight. ON, alongside ShipLights.Enabled, now that the fleet is
    /// in — a bolt that lights the hull it passes is most of what sells a firefight, and there are
    /// hulls to light. Capped at MaxLiveLights below, which is what makes this affordable at all.
    public static bool DynamicLights = true;

    const int MaxLiveLights = 14;
    int lightsMade;

    Light RentLight()
    {
        if (!DynamicLights) return null;
        if (lightPool.Count > 0) return lightPool.Pop();
        if (lightsMade >= MaxLiveLights) return null;
        lightsMade++;
        var go = new GameObject("ShotLight");
        go.transform.SetParent(transform, false);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.shadows = LightShadows.None;      // a shot casting shadows is invisible and expensive
        l.renderMode = LightRenderMode.ForcePixel;
        l.enabled = false;
        return l;
    }

    Transform RentQuad()
    {
        if (quadPool.Count > 0)
        {
            var t = quadPool.Pop();
            t.gameObject.SetActive(true);
            return t;
        }
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Bolt";
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        go.transform.SetParent(transform, false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(boltMat);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    LineRenderer RentBeam()
    {
        if (beamPool.Count > 0)
        {
            var b = beamPool.Pop();
            b.enabled = true;
            return b;
        }
        var go = new GameObject("Beam");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.numCapVertices = 2;
        lr.material = new Material(beamMat);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    /// A soft round blob, brightest in the middle. One texture serves every weapon because colour is
    /// applied through the material tint — so a green pulse and an orange plasma ball are the same
    /// four kilobytes.
    static Texture2D BoltTexture()
    {
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;          // 0 centre, 1 edge
                float a = Mathf.Clamp01(1f - d);
                a = a * a;                                              // tighten the core
                byte v = (byte)Mathf.Clamp(a * 255f, 0f, 255f);
                px[y * N + x] = new Color32(255, 255, 255, v);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
