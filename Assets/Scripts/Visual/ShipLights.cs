using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE FLEET CLOCK — why every light in the game blinks off one number
//
// The brief was "some lights blink faster than others, but it should all feel in sync and in
// rhythm". Those two things sound contradictory and are not: it is exactly what a drummer does.
// A hi-hat on eighths and a kick on quarters run at different rates and are obviously together,
// because both are counting the same bar.
//
// So there is ONE beat for the whole game, and every blinking thing on every ship is given a period
// that is an INTEGER RATIO of it — half a beat, one beat, two, three — and a phase offset QUANTISED
// to a quarter of a beat. Two consequences fall out for free:
//
//   * Periods that are integer ratios share a common multiple, so the whole fleet realigns on a
//     downbeat over and over instead of drifting apart. That recurring coincidence is what the eye
//     reads as "in time".
//   * Quantised offsets mean lights stagger without ever landing between beats. They syncopate; they
//     never sound like a mistake.
//
// The tempting alternative — give each light a random period around some average — is what makes
// most games' running lights look like static. Every light is then its own unrelated cycle, nothing
// ever coincides, and the eye reads noise. It is one line of code cheaper and it is the whole
// difference between a fleet that looks powered and a fleet that looks broken.
//
// The clock is deliberately unscaled by game speed. Blinkers are equipment, not simulation: a beacon
// does not flash faster because the player pressed fast-forward, and tying it to Time.timeScale makes
// the whole fleet strobe when the clock runs up.
// ============================================================================================
public static class FleetClock
{
    /// Seconds per beat. ~92 BPM — slow enough to read as deliberate machinery rather than an alarm.
    public const float Beat = 0.65f;

    /// How many beats since the game started. Unscaled on purpose (see above).
    public static float Beats => Time.unscaledTime / Beat;

    /// The legal blink periods, in beats. Integers and simple fractions ONLY — this list is the
    /// reason the fleet stays in time, so anything added here must divide or multiply the beat evenly.
    public static readonly float[] Periods = { 0.5f, 1f, 2f, 3f, 4f };

    /// Legal phase offsets, in beats. Quarter-beat grid.
    public static readonly float[] Phases = { 0f, 0.25f, 0.5f, 0.75f };

    /// A strobe: bright at the top of its period, then a fast decay. `period` and `phase` are in
    /// beats; `duty` is the fraction of the period the flash occupies.
    ///
    /// Returns 0..1. The decay is squared rather than linear because a linear fade reads as a lamp
    /// being turned down by hand, and a strobe is a capacitor emptying.
    public static float Strobe(float period, float phase, float duty = 0.18f)
    {
        float t = Mathf.Repeat((Beats + phase) / period, 1f);
        if (t > duty) return 0f;
        float k = t / duty;
        return (1f - k) * (1f - k);
    }

    /// A slow sine breathe for the lights that never fully go out — reactor glow, standby lamps.
    /// Locked to the same beat so it swells with everything else.
    public static float Breathe(float period, float phase, float depth = 0.25f)
    {
        float t = Mathf.Repeat((Beats + phase) / period, 1f);
        return 1f - depth + depth * Mathf.Sin(t * Mathf.PI * 2f) * 0.5f + depth * 0.5f;
    }
}

// ============================================================================================
// SHIP LIGHTS — everything a hull does that is light rather than mesh
//
// Attached to a model by UnitModelRenderer as it builds it. One component per drawn hull owns:
//
//   DRIVE PLUMES   at the hull's real engine bells (ShipHardpoints, baked from the mesh), falling back
//                  to the stern of the bounds. In the race's colours (RaceVfx), sized off the nozzle,
//                  lengthening and brightening with throttle, flaring white-hot while the drive spools up.
//   RUNNING LIGHTS aviation lamps for the races that carry them (Terran, Cryithn), rim chase lights on
//                  their stations. On the FleetClock grid, so a fleet blinks in rhythm.
//   HULL GLOW      the art's own emissive, lifted over bloom's threshold and pulsed per race through a
//                  MaterialPropertyBlock: Pyrothian lava throbs and gutters, Aquarii and Sylvan breathe.
//   GUNS           the baked gun hardpoints, so a round leaves a real point on the hull facing its
//                  target (TryMuzzle), with a muzzle flash that rides the hull and a recoil kick.
//   DAMAGE         smoke, sparks and a guttering fire from points on the hull as its health drops.
//   SHIELD         the ShieldBubble round the hull, and the point on it a round strikes.
//
// ---- WHERE THINGS GO, WITHOUT PER-SHIP AUTHORING -------------------------------------------------
//
// Ship space (bow +Z, dorsal +Y, starboard +X) is recovered from the model root, which already carries
// `modelRotation` — so the bow is NOT the root's local +Z. Baked hardpoints are in the root's own local
// frame (Unity mesh space; see ShipHardpoints) and are used as-is.
//
// ---- COST ---------------------------------------------------------------------------------------
//
// Every sprite here shares FxKit's one additive material and takes its colour through a property
// block: a fleet of forty is forty times a couple of dozen quads and ZERO material copies. Nothing is
// allocated per frame. Sprites are billboarded here rather than by a component each. Detail drops with
// the hull's size ON SCREEN: below a few pixels nothing is drawn (ShipMarkers' glyph has the job), at
// a dozen pixels only the plumes are, and the lamps, damage and muzzle detail come in as it grows.
//
// ---- FOG OF WAR ---------------------------------------------------------------------------------
//
// ConcealBinding switches off what it finds under a hidden hull, but a sprite made or re-shown after
// that would slip past it. So this asks VisibilityService.ShownToPlayer a few times a second and draws
// nothing at all while the answer is no.
// ============================================================================================
public class ShipLights : MonoBehaviour
{
    // ---- switches ----------------------------------------------------------------------------

    /// The whole rig. Off leaves bare hulls (the guns still report their hardpoints).
    public static bool Enabled = true;

    /// Running lights: allowed, and each race decides (RaceVfxProfile.navLights). Sized as pinpricks
    /// (NavSizeFactor, capped by NavWidthCap) — at 8.5% of hull length they read as fairy lights.
    public static bool ShowNavLights = true;

    /// Smoke, sparks and fire off a damaged hull.
    public static bool DamageEffects = true;

    // ---- tuning ------------------------------------------------------------------------------

    const int   MaxNavLights   = 10;
    const float NavSizeFactor  = 0.028f;   // of hull length
    const float NavWidthCap    = 0.10f;    // ...and never more than this share of the beam
    const int   MaxNozzles     = 6;
    const int   PlumeDots      = 3;
    const int   MaxMuzzles     = 6;

    /// Below this many pixels of hull on screen nothing is drawn; below the second only the plumes are.
    const float HidePx = 2.5f, DetailPx = 12f;

    // ---- one billboard sprite ------------------------------------------------------------------
    class Fx
    {
        public Transform tr;
        public Renderer r;
        public Color colour;
        public float baseScale;     // ROOT-LOCAL units
        public float minPx;         // floor on screen, in pixels
        public float period, phase; // FleetClock beats (lamps)
        public bool constant;       // breathes instead of strobing
        public bool on;             // active state, tracked so SetActive is only called on a change
    }

    class Nozzle
    {
        public Vector3 local;       // root-local
        public float radius;        // root-local
        public float glow;          // 0..1 how hot the bell is in the art; brightens its plume
        public Fx core, streak;
        public Fx[] dots;
    }

    class Muzzle
    {
        public Fx glow, streak;
        public Vector3 local, dirLocal;
        public float life, maxLife, size;
    }

    readonly List<Fx> lamps = new List<Fx>();
    readonly List<Nozzle> nozzles = new List<Nozzle>();
    readonly List<Muzzle> muzzles = new List<Muzzle>();

    Unit unit;
    RaceVfxProfile vfx;
    Vector3 bow, up, right;           // ship space, in this transform's local frame
    Bounds hullBounds;                // root-local, hull only
    float hullLength, hullWidth;      // root-local
    bool isStation;
    bool built;

    /// The hull's own renderers (every LOD level), captured BEFORE any effect existed — for the ship
    /// pixelation layer and anything else that must touch the hull and nothing of ours.
    public Renderer[] HullRenderers { get; private set; } = new Renderer[0];

    public Unit Unit => unit;

    // ---- drive ----
    float throttle, throttleTarget, flare;

    // ---- guns ----
    Vector3[] gunLocal = new Vector3[0];
    Vector3[] gunDir = new Vector3[0];
    int gunCursor;
    static readonly int[] goodGuns = new int[16];

    // ---- recoil (moves the hull's own parts, never the root the renderer positions) ----
    Transform[] hullParts = new Transform[0];
    Vector3[] hullPartRest = new Vector3[0];
    Vector3 recoilWorld;

    // ---- damage ----
    Vector3[] damagePoints = new Vector3[0];
    float smokeT, sparkT;
    Fx fire;
    int firePoint;

    // ---- shield ----
    ShieldBubble bubble;

    // ---- visibility ----
    float shownCheck;
    bool shown = true;
    bool drawnLastFrame;

    // ---- hull glow (see CollectGlow) ----
    struct Glow
    {
        public Renderer renderer;
        public int slot;
        public int property;
        public Vector4 authored;   // the emissive as imported, so the multiplier never compounds
    }

    readonly List<Glow> glows = new List<Glow>();
    static MaterialPropertyBlock glowBlock;
    float glowPhase, glowSeed;
    float glowApplied = -1f;

    // ============================================================================================
    // BUILD
    // ============================================================================================

    public void Init(Unit u, Quaternion modelRotation, Color ownerColour) => Init(u, modelRotation, ownerColour, null);

    /// `meshPath` is the Resources path the hull was loaded from, which is how the baked hardpoints are
    /// found. Null (or a hull nobody baked) falls back to the bounds.
    public void Init(Unit u, Quaternion modelRotation, Color ownerColour, string meshPath)
    {
        unit = u;
        isStation = u != null && u.Info != null && u.Info.isStation;

        // The hull's own renderers and direct children, before anything of ours is parented here.
        HullRenderers = GetComponentsInChildren<Renderer>(true);
        int childCount = transform.childCount;
        hullParts = new Transform[childCount];
        hullPartRest = new Vector3[childCount];
        for (int i = 0; i < childCount; i++)
        {
            hullParts[i] = transform.GetChild(i);
            hullPartRest[i] = hullParts[i].localPosition;
        }

        Quaternion inv = Quaternion.Inverse(modelRotation);
        bow   = inv * Vector3.forward;
        up    = inv * Vector3.up;
        right = inv * Vector3.right;

        hullBounds = LocalBounds();
        if (hullBounds.size.sqrMagnitude < 1e-6f) return;

        hullLength = Mathf.Abs(Vector3.Dot(hullBounds.size, bow));
        hullWidth  = Mathf.Abs(Vector3.Dot(hullBounds.size, right));
        if (hullLength < 1e-4f) hullLength = hullBounds.size.magnitude;
        if (hullWidth  < 1e-4f) hullWidth  = hullLength * 0.4f;

        vfx = RaceVfx.For(u);
        var hp = ShipHardpoints.For(meshPath, modelRotation);

        BuildGuns(hp);
        BuildDamagePoints(hp);

        // The shield goes on whether or not the rig is enabled: it is the mechanic's only picture.
        bubble = ShieldBubble.Attach(transform, u, hullBounds, vfx.shieldColour);

        // The pixelation layer wants the hull renderers whatever else is switched off.
        ShipPixelation.Track(HullRenderers);

        if (!Enabled) return;
        built = true;

        // BEFORE any lamp exists, so the glow list is the hull's renderers and nothing of ours.
        CollectGlow();

        if (isStation) BuildStationLights(ownerColour);
        else
        {
            BuildNavLights(ownerColour);
            BuildThrusters(hp);
        }
    }

    /// Bounds of every renderer under this object, in THIS transform's local space.
    Bounds LocalBounds()
    {
        bool any = false;
        Bounds acc = default;
        foreach (var r in HullRenderers)
        {
            if (r == null) continue;
            var wb = r.bounds;
            var lc = transform.InverseTransformPoint(wb.center);
            var le = transform.InverseTransformVector(wb.extents);
            var lb = new Bounds(lc, new Vector3(Mathf.Abs(le.x), Mathf.Abs(le.y), Mathf.Abs(le.z)) * 2f);
            if (!any) { acc = lb; any = true; } else acc.Encapsulate(lb);
        }
        return any ? acc : default;
    }

    // ---- guns ----------------------------------------------------------------------------------
    //
    // The baked points: bow tip, flanks, a dorsal pair, the keel; a station's rim. A hull nobody baked
    // gets four from its bounds — the bow, both beams and the dorsal spine — which is still better than
    // every round leaving the centre of the ship.
    void BuildGuns(ShipHardpoints.Entry hp)
    {
        if (hp != null && hp.guns != null && hp.guns.Length > 0)
        {
            int n = Mathf.Min(hp.guns.Length, goodGuns.Length);
            gunLocal = new Vector3[n];
            gunDir = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                gunLocal[i] = ShipHardpoints.V(hp.guns[i].p);
                Vector3 d = ShipHardpoints.V(hp.guns[i].d);
                gunDir[i] = d.sqrMagnitude > 1e-6f ? d.normalized : bow;
            }
            return;
        }

        Vector3 c = hullBounds.center;
        float hl = hullLength * 0.5f, hw = hullWidth * 0.5f;
        float hh = Mathf.Abs(Vector3.Dot(hullBounds.extents, up));
        gunLocal = new[] { c + bow * hl, c + right * hw, c - right * hw, c + up * hh };
        gunDir   = new[] { bow, right, -right, up };
    }

    void BuildDamagePoints(ShipHardpoints.Entry hp)
    {
        var pts = new List<Vector3>();
        foreach (var g in gunLocal) pts.Add(Vector3.Lerp(hullBounds.center, g, 0.8f));
        if (hp != null && hp.nozzles != null)
            foreach (var nz in hp.nozzles) pts.Add(Vector3.Lerp(hullBounds.center, ShipHardpoints.V(nz.p), 0.85f));

        // A few deterministic points inside the hull's box as well, so the damage is not only ever at
        // the gun ports. Seeded per unit so the same ship always burns in the same places.
        var rng = new System.Random(unit != null ? unit.id * 31 + 7 : 7);
        for (int i = 0; i < 4; i++)
        {
            Vector3 e = hullBounds.extents * 0.6f;
            pts.Add(hullBounds.center + new Vector3(
                ((float)rng.NextDouble() * 2f - 1f) * e.x,
                ((float)rng.NextDouble() * 2f - 1f) * e.y,
                ((float)rng.NextDouble() * 2f - 1f) * e.z));
        }
        damagePoints = pts.ToArray();
        firePoint = damagePoints.Length > 0 ? rng.Next(damagePoints.Length) : 0;
    }

    // ---- hull glow -----------------------------------------------------------------------------
    //
    // The generated hulls carry an emissive texture at an emissive factor of exactly 1, and bloom's
    // threshold is 1.05 in GAMMA space (PostFxController; URP converts it, ~1.11 linear in this
    // linear-colour project) — so as imported no hull glow in the fleet ever blooms, and lava and
    // bioluminescence look like paint. The race's multiplier is what lifts it over.
    //
    // A material with NO live emission is left alone: multiplying black is black, and inventing a glow
    // the art does not have would light the whole hull evenly, which reads as a rendering fault.
    void CollectGlow()
    {
        glows.Clear();
        if (vfx == null || !vfx.AffectsHullGlow) return;   // the art as imported; leave it batchable

        foreach (var r in HullRenderers)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                int prop = HullMaterials.EmissiveId(m);
                if (prop < 0) continue;

                // GetVector, not GetColor: a colour is stored LINEAR and Get/SetColor convert through
                // gamma, which would turn a multiplier of 3 into about 11. The raw vector is the value.
                Vector4 e = m.GetVector(prop);
                if (Mathf.Max(e.x, Mathf.Max(e.y, e.z)) <= 1e-4f) continue;

                // Only where the shader is actually drawing it: URP/Lit needs `_EMISSION`, glTFast's
                // graphs `_EMISSIVE` (set by its importer whenever the glTF has an emissive map or a
                // non-black factor). A property sitting there unused must not be "lifted".
                if (!HullMaterials.EmissionLive(m, prop)) continue;

                glows.Add(new Glow { renderer = r, slot = i, property = prop, authored = e });
            }
        }

        if (glows.Count == 0) return;
        if (glowBlock == null) glowBlock = new MaterialPropertyBlock();

        int seed = unit != null ? unit.id : 0;
        glowPhase = FleetClock.Phases[Mathf.Abs(seed) % FleetClock.Phases.Length];
        glowSeed = (Mathf.Abs(seed) % 97) * 0.731f;

        ApplyGlow(GlowFactor());
    }

    /// The multiplier this frame: the race's level, its slow pulse, its ember flicker, and its
    /// throttle term (a reactor hull burns hotter under power).
    float GlowFactor()
    {
        float k = vfx.emissiveMultiplier;
        if (vfx.emissivePulseDepth > 0.001f)
            k *= FleetClock.Breathe(RaceVfx.SnapBeats(vfx.emissivePulseBeats), glowPhase,
                                    Mathf.Clamp01(vfx.emissivePulseDepth));
        if (vfx.emissiveFlicker > 0.001f)
            // Perlin, not white noise: lava gutters, it does not strobe.
            k *= 1f - Mathf.Clamp01(vfx.emissiveFlicker) * Mathf.PerlinNoise(Time.unscaledTime * 2.3f, glowSeed);
        if (vfx.emissiveThrottleBoost > 0.001f)
            k *= 1f + vfx.emissiveThrottleBoost * throttle;
        return k;
    }

    void ApplyGlow(float factor)
    {
        glowApplied = factor;
        for (int i = 0; i < glows.Count; i++)
        {
            var g = glows[i];
            if (g.renderer == null) continue;
            var e = g.authored;
            glowBlock.Clear();
            glowBlock.SetVector(g.property, new Vector4(e.x * factor, e.y * factor, e.z * factor, e.w));
            g.renderer.SetPropertyBlock(glowBlock, g.slot);
        }
    }

    // ---- running lights --------------------------------------------------------------------------
    //
    // Aviation convention, because it reads as "a vehicle" instantly: red to port, green to starboard,
    // white strobes fore and aft, plus two lamps in the OWNER's colour so two fleets in a fight can be
    // told apart.
    void BuildNavLights(Color ownerColour)
    {
        if (!ShowNavLights) return;
        if (vfx != null && !vfx.navLights) return;   // this race flies dark
        var p = vfx ?? RaceVfx.ForCiv(null);

        float size = Mathf.Min(hullLength * NavSizeFactor, hullWidth * NavWidthCap);
        float fast = RaceVfx.SnapBeats(p.navBlinkBeats);
        float tail = RaceVfx.SnapBeats(p.navBlinkBeats * 2f);
        float slow = RaceVfx.SnapBeats(p.navBlinkBeats * 6f);
        float halfW = hullWidth * 0.5f;
        float halfL = hullLength * 0.5f;
        Vector3 c = hullBounds.center;

        // Deterministic per unit: two ships of a class should not blink in lockstep, but the SAME ship
        // must pick the same pattern every time it is rebuilt.
        var rng = new System.Random(unit != null ? unit.id * 7919 + 13 : 0);
        float Pick(float[] a) => a[rng.Next(a.Length)];

        Lamp(c - right * halfW + up * size * 0.4f, size * 0.85f, p.navPort, 2f, 0f, true);
        Lamp(c + right * halfW + up * size * 0.4f, size * 0.85f, p.navStarboard, 2f, 0f, true);
        Lamp(c + up * Vector3.Dot(hullBounds.extents, up), size * 0.9f, p.navStrobe, fast, Pick(FleetClock.Phases), false);
        Lamp(c - bow * halfL + up * size * 0.5f, size * 0.8f, Color.Lerp(p.navStrobe, Color.white, 0.3f), tail, Pick(FleetClock.Phases), false);
        Lamp(c + bow * halfL, size * 0.7f, new Color(1f, 0.85f, 0.5f), slow, Pick(FleetClock.Phases), false);

        var oc = ownerColour; oc.a = 1f;
        Lamp(c + up * halfW * 0.5f - bow * halfL * 0.25f, size * 0.75f, oc, Pick(FleetClock.Periods), Pick(FleetClock.Phases), false);
        Lamp(c - up * halfW * 0.5f + bow * halfL * 0.15f, size * 0.65f, oc, Pick(FleetClock.Periods), Pick(FleetClock.Phases), false);
    }

    // ---- station lights ----------------------------------------------------------------------------
    //
    // A station has no bow and no drive, so it gets a CHASE: lamps on the rim hardpoints firing one after
    // another round the ring, a quarter-beat apart, like the beacons on a mast. A race that flies dark
    // gets no lamps here either; its station's own glow is its light (CollectGlow).
    void BuildStationLights(Color ownerColour)
    {
        if (!ShowNavLights) return;
        if (vfx != null && !vfx.navLights) return;
        var p = vfx ?? RaceVfx.ForCiv(null);

        float size = Mathf.Max(hullLength, hullWidth) * 0.022f;
        int rim = 0;
        for (int i = 0; i < gunLocal.Length && rim < 8; i++)
        {
            // Rim mounts face outward in the station's plane; the dorsal and keel ones do not.
            if (Mathf.Abs(Vector3.Dot(gunDir[i], up)) > 0.7f) continue;
            Lamp(gunLocal[i], size, rim % 2 == 0 ? p.navStrobe : ownerColour, 2f, FleetClock.Phases[rim % 4] + (rim / 4) * 1f, false);
            rim++;
        }
        // A crown beacon, slow and warm.
        Lamp(hullBounds.center + up * Vector3.Dot(hullBounds.extents, up), size * 1.3f, new Color(1f, 0.55f, 0.35f), 4f, 0f, false);
    }

    void Lamp(Vector3 localPos, float scale, Color colour, float period, float phase, bool constant)
    {
        if (lamps.Count >= MaxNavLights) return;
        var f = MakeFx("NavLight", FxKit.Dot(), localPos, scale, colour);
        f.period = period; f.phase = phase; f.constant = constant;
        f.minPx = 1.5f;
        lamps.Add(f);
    }

    // ---- thrusters -------------------------------------------------------------------------------
    //
    // A plume is a white-hot core on the bell, a few round billboards trailing astern (each smaller,
    // further and cooler), and one streak stretched along the hull's axis as it lies on screen. The
    // dots carry it when the camera looks down the axis (where a streak collapses), the streak when
    // it looks across it. Core cools to tail in the race's colours.
    void BuildThrusters(ShipHardpoints.Entry hp)
    {
        if (hp != null && hp.nozzles != null && hp.nozzles.Length > 0)
        {
            // Biggest bells first, so a ship with a dozen thruster ports keeps its main engines.
            var order = new List<ShipHardpoints.Nozzle>(hp.nozzles);
            order.Sort((a, b) => b.r.CompareTo(a.r));
            for (int i = 0; i < order.Count && i < MaxNozzles; i++)
                AddNozzle(ShipHardpoints.V(order[i].p), Mathf.Max(order[i].r, hullWidth * 0.04f), Mathf.Clamp01(order[i].glow));
            return;
        }

        // Fallback: the stern plane, spread across the beam.
        Vector3 stern = hullBounds.center - bow * (hullLength * 0.5f);
        // Bounds are in root-local units, which are mesh units; the class sizes above are WORLD units,
        // so the nozzle count is decided off the world length.
        float worldLen = hullLength * Mathf.Abs(transform.lossyScale.x);
        int n = worldLen > 0.30f ? 4 : worldLen > 0.20f ? 3 : worldLen > 0.12f ? 2 : 1;
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : (i / (float)(n - 1)) * 2f - 1f;
            AddNozzle(stern + right * (t * hullWidth * 0.30f), hullWidth * 0.12f, 0.5f);
        }
    }

    void AddNozzle(Vector3 local, float radius, float glow)
    {
        var nz = new Nozzle { local = local, radius = radius, glow = glow, dots = new Fx[PlumeDots] };
        Color core = vfx != null ? vfx.thrusterCore : new Color(0.85f, 0.95f, 1f);
        Color tail = vfx != null ? vfx.thrusterTail : new Color(0.25f, 0.55f, 1f);

        nz.streak = MakeFx("Plume", FxKit.StreakTex(), local, radius, Color.Lerp(core, tail, 0.45f));
        nz.streak.minPx = 1.5f;
        for (int s = 0; s < PlumeDots; s++)
        {
            float k = (s + 1) / (float)(PlumeDots + 1);
            nz.dots[s] = MakeFx("PlumeDot", FxKit.Dot(), local, radius * 2f * (1f - 0.2f * s), Color.Lerp(core, tail, k));
            nz.dots[s].minPx = 1f;
        }
        nz.core = MakeFx("Thruster", FxKit.Dot(), local, radius * 2.4f, core);
        nz.core.minPx = 2f;
        nozzles.Add(nz);
    }

    Fx MakeFx(string name, Texture tex, Vector3 localPos, float scale, Color colour)
    {
        var r = FxKit.MakeQuad(transform, name, FxKit.SharedAdditive(tex));
        var tr = r.transform;
        tr.localPosition = localPos;
        tr.localScale = Vector3.one * scale;
        r.gameObject.SetActive(false);
        return new Fx { tr = tr, r = r, colour = colour, baseScale = scale, on = false };
    }

    // ============================================================================================
    // THE API EVERYTHING ELSE CALLS
    // ============================================================================================

    /// Told by UnitModelRenderer each frame how hard this hull is working, 0..1.
    public void SetThrottle(float t) => throttleTarget = Mathf.Clamp01(t);

    /// The hull's length in world units right now.
    public float WorldLength => hullLength * Mathf.Abs(transform.lossyScale.x);

    /// The centre of the hull in world space.
    public Vector3 CentreWorld => transform.TransformPoint(hullBounds.center);

    /// The gun this round leaves from: one of the baked hardpoints that faces the target, taken in turn
    /// mount by mount so a broadside reads as several guns. Always succeeds on a built rig; false only
    /// for a hull too degenerate to have bounds.
    public bool TryMuzzle(Vector3 targetWorld, int mount, out Vector3 muzzleWorld)
    {
        muzzleWorld = transform.position;
        int n = gunLocal.Length;
        if (n == 0 || hullLength <= 0f) return false;

        int good = 0, best = 0;
        float bestScore = -2f;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = transform.TransformPoint(gunLocal[i]);
            Vector3 d = transform.TransformDirection(gunDir[i]);
            Vector3 to = targetWorld - p;
            float score = to.sqrMagnitude > 1e-8f && d.sqrMagnitude > 1e-8f ? Vector3.Dot(d.normalized, to.normalized) : 0f;
            if (score > bestScore) { bestScore = score; best = i; }
            if (score > 0.25f && good < goodGuns.Length) goodGuns[good++] = i;
        }
        int pick = good > 0 ? goodGuns[(Mathf.Abs(mount) + gunCursor++) % good] : best;
        if (gunCursor > 1 << 20) gunCursor = 0;
        muzzleWorld = transform.TransformPoint(gunLocal[pick]);
        return true;
    }

    /// The muzzle flash for a round leaving `worldPos`. Rides the hull for its short life.
    public void FlashMuzzle(Vector3 worldPos, Color colour) => FlashMuzzle(worldPos, colour, Vector3.zero, 1f);

    /// `dir` (world, may be zero) points the flash's streak down the barrel; `size` scales it (1 is a
    /// light gun, 2.5 a railgun or a plasma cannon).
    public void FlashMuzzle(Vector3 worldPos, Color colour, Vector3 dir, float size)
    {
        if (!built || !shown) return;
        Muzzle free = null;
        float oldest = float.MaxValue;
        Muzzle oldestM = null;
        for (int i = 0; i < muzzles.Count; i++)
        {
            var m = muzzles[i];
            if (m.life >= m.maxLife) { free = m; break; }
            if (m.maxLife - m.life < oldest) { oldest = m.maxLife - m.life; oldestM = m; }
        }
        if (free == null)
        {
            if (muzzles.Count < MaxMuzzles)
            {
                free = new Muzzle
                {
                    glow = MakeFx("Muzzle", FxKit.Dot(), Vector3.zero, 1f, colour),
                    streak = MakeFx("MuzzleStreak", FxKit.StreakTex(), Vector3.zero, 1f, colour),
                };
                muzzles.Add(free);
            }
            else free = oldestM;
        }
        if (free == null) return;

        free.local = transform.InverseTransformPoint(worldPos);
        free.dirLocal = dir.sqrMagnitude > 1e-8f ? transform.InverseTransformDirection(dir.normalized) : Vector3.zero;
        free.size = Mathf.Clamp(size, 0.4f, 3f);
        free.life = 0f;
        free.maxLife = 0.07f + 0.035f * free.size;
        free.glow.colour = colour;
        free.streak.colour = colour;
    }

    /// A round leaving shoves the hull back along the shot. `fraction` is of the hull's length.
    public void Recoil(Vector3 shotDirWorld, float fraction)
    {
        if (fraction <= 0f || shotDirWorld.sqrMagnitude < 1e-8f || isStation) return;
        Vector3 kick = -shotDirWorld.normalized * (fraction * WorldLength);
        recoilWorld += kick;
        float cap = WorldLength * 0.15f;
        if (recoilWorld.sqrMagnitude > cap * cap) recoilWorld = recoilWorld.normalized * cap;
    }

    /// A round struck the shield. Lights the bubble's hit pattern and ripple and returns the point on
    /// the shell it struck, for the splash. `inbound` is the round's direction of travel.
    public Vector3 ShieldStruck(Vector3 inbound, float strength, bool collapsed)
    {
        if (bubble != null) return bubble.Hit(inbound, strength, collapsed);
        Vector3 back = inbound.sqrMagnitude > 1e-8f ? -inbound.normalized : Vector3.up;
        return CentreWorld + back * WorldLength * 0.65f;
    }

    /// Where a round coming from `fromWorld` meets this ship's shield, if it has one up. Asked before a
    /// beam is drawn, so the beam stops on the shell rather than on the hull it never reached.
    public bool ShieldContact(Vector3 fromWorld, out Vector3 point)
    {
        point = CentreWorld;
        if (unit == null || unit.MaxShield <= 0 || unit.Shield <= 0.01f) return false;
        point = bubble != null ? bubble.SurfacePoint(fromWorld) : CentreWorld + (fromWorld - CentreWorld).normalized * WorldLength * 0.65f;
        return true;
    }

    /// A point on the hull's skin facing `fromWorld` — where a round that got through lands, so the
    /// scorch sits on the side that was hit rather than in the middle of the ship.
    public Vector3 HullContact(Vector3 fromWorld)
    {
        Vector3 c = CentreWorld;
        Vector3 d = fromWorld - c;
        if (d.sqrMagnitude < 1e-8f) return c;
        return c + d.normalized * WorldLength * 0.32f;
    }

    // ============================================================================================
    // THE FRAME
    // ============================================================================================

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // Spool. Engines light quickly and die away slowly — anything with mass does.
        float rate = throttleTarget > throttle ? 3.5f : 1.4f;
        float gap = throttleTarget - throttle;
        throttle = Mathf.MoveTowards(throttle, throttleTarget, rate * dt);
        // FLARE: while the drive is still climbing toward what it was asked for, it burns hot and long.
        flare = Mathf.MoveTowards(flare, Mathf.Clamp01(gap * 3f), (gap > 0f ? 6f : 2f) * dt);

        TickRecoil(dt);

        if (!built) return;

        shownCheck -= Time.unscaledDeltaTime;
        if (shownCheck <= 0f) { shownCheck = 0.25f; shown = unit == null || VisibilityService.ShownToPlayer(unit); }

        if (!shown || !FxKit.SystemView) { HideAll(); return; }

        // ---- hull glow (a race whose glow moves), only for a hull somebody can see ----
        if (glows.Count > 0 && !vfx.HullGlowIsStatic)
        {
            float g = GlowFactor();
            if (Mathf.Abs(g - glowApplied) > 0.01f) ApplyGlow(g);
        }

        var cam = FxKit.Cam;
        if (cam == null) return;
        Quaternion camRot = cam.transform.rotation;
        float lossy = Mathf.Max(1e-5f, Mathf.Abs(transform.lossyScale.x));
        float wpp = FxKit.WorldPerPixel(transform.position);
        float px = hullLength * lossy / Mathf.Max(1e-6f, wpp);

        if (px < HidePx) { HideAll(); return; }
        drawnLastFrame = true;
        bool detail = px >= DetailPx;

        // ---- running lights ----
        for (int i = 0; i < lamps.Count; i++)
        {
            var l = lamps[i];
            if (!detail) { Show(l, false); continue; }
            float k = l.constant
                ? FleetClock.Breathe(l.period <= 0f ? 2f : l.period, l.phase)
                : FleetClock.Strobe(l.period, l.phase);
            // Never fully black: a dark lamp on a dark hull in dark space loses the outline it was for.
            float a = Mathf.Lerp(0.12f, 1f, k);
            Draw(l, l.tr.localPosition, l.colour * (1f + k * 0.8f), a, 1f + k * 0.35f, camRot, lossy, wpp);
        }

        // ---- drive plumes ----
        Vector3 aft = -bow;
        Vector3 aftWorld = transform.TransformDirection(aft);
        float fl = vfx != null ? Mathf.Clamp01(vfx.thrusterFlicker) : 0.1f;
        Color coreC = vfx != null ? vfx.thrusterCore : new Color(0.85f, 0.95f, 1f);
        Color tailC = vfx != null ? vfx.thrusterTail : new Color(0.25f, 0.55f, 1f);
        for (int i = 0; i < nozzles.Count; i++)
        {
            var nz = nozzles[i];
            float flicker = (1f - fl) + fl * Mathf.Sin(FleetClock.Beats * Mathf.PI * 6f + i * 1.7f);
            float power = Mathf.Clamp01(throttle + flare * 0.6f);
            float bright = power * flicker * Mathf.Lerp(0.85f, 1.25f, nz.glow);
            float r = nz.radius;

            // Core: always a little alive (station-keeping), white-hot under power and more so in a flare.
            float coreA = Mathf.Clamp01(0.25f + bright);
            Color coreCol = Color.Lerp(coreC, Color.white, flare * 0.6f) * (1.2f + bright * 1.6f + flare * 1.2f);
            Draw(nz.core, nz.local + aft * r * 0.25f, coreCol, coreA, 0.55f + bright * 0.7f + flare * 0.4f, camRot, lossy, wpp);

            // Length: grows out of the bell as the drive spools, longest in a flare.
            float len = r * (1.5f + 8f * throttle + 5f * flare);
            float reach = Mathf.Clamp01((throttle + flare) * 1.4f);
            for (int s = 0; s < nz.dots.Length; s++)
            {
                float k = (s + 1) / (float)(nz.dots.Length + 1);
                float lit = Mathf.Clamp01((reach - k * 0.6f) / 0.4f) * bright;
                Color c = Color.Lerp(coreC, tailC, k) * (1f + bright);
                Draw(nz.dots[s], nz.local + aft * len * k, c, lit, (1f - 0.18f * s) * (0.6f + 0.6f * bright), camRot, lossy, wpp, lit < 0.02f);
            }

            // The streak, laid along the hull's axis as it lies on screen.
            if (nz.streak != null)
            {
                float lit = Mathf.Clamp01(reach * bright);
                bool off = lit < 0.02f;
                Show(nz.streak, !off);
                if (!off)
                {
                    var t = nz.streak.tr;
                    t.localPosition = nz.local + aft * len * 0.5f;
                    t.rotation = FxKit.Streak(aftWorld);
                    float w = Mathf.Max(r * 1.4f * lossy, 1.5f * wpp) / lossy;
                    float l = Mathf.Max(len * lossy, 3f * wpp) / lossy;
                    t.localScale = new Vector3(w, l, 1f);
                    Color c = Color.Lerp(coreC, tailC, 0.5f) * (0.9f + bright);
                    c.a = lit * 0.9f;
                    FxKit.SetColour(nz.streak.r, c);
                }
            }
        }

        // ---- muzzle flashes ----
        for (int i = 0; i < muzzles.Count; i++)
        {
            var m = muzzles[i];
            if (m.life >= m.maxLife) { Show(m.glow, false); Show(m.streak, false); continue; }
            m.life += Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(m.life / m.maxLife);
            float s = WorldLength * 0.22f * m.size / lossy;
            m.glow.baseScale = s; m.glow.minPx = 4f + 2f * m.size;
            Draw(m.glow, m.local, m.glow.colour * (2.2f + m.size), k, 0.6f + (1f - k) * 0.8f, camRot, lossy, wpp);

            bool hasDir = m.dirLocal.sqrMagnitude > 0.5f;
            Show(m.streak, hasDir && k > 0.05f);
            if (hasDir && k > 0.05f)
            {
                var t = m.streak.tr;
                float l = Mathf.Max(s * 2.2f * lossy, (6f + 3f * m.size) * wpp) / lossy;
                float w = Mathf.Max(s * 0.5f * lossy, 2f * wpp) / lossy;
                t.localPosition = m.local + m.dirLocal * l * 0.45f;
                t.rotation = FxKit.Streak(transform.TransformDirection(m.dirLocal));
                t.localScale = new Vector3(w, l * (0.6f + 0.4f * k), 1f);
                Color c = m.streak.colour * (2f + m.size); c.a = k;
                FxKit.SetColour(m.streak.r, c);
            }
        }

        TickDamage(dt, detail, camRot, lossy, wpp);
    }

    /// Billboard one sprite at `local`, with its pixel floor.
    void Draw(Fx f, Vector3 local, Color colour, float alpha, float scaleMul, Quaternion camRot, float lossy, float wpp, bool hide = false)
    {
        if (hide || alpha <= 0.005f) { Show(f, false); return; }
        Show(f, true);
        f.tr.localPosition = local;
        f.tr.rotation = camRot;
        float world = Mathf.Max(f.baseScale * scaleMul * lossy, f.minPx * wpp);
        f.tr.localScale = Vector3.one * (world / lossy);
        colour.a = Mathf.Clamp01(alpha);
        FxKit.SetColour(f.r, colour);
    }

    static void Show(Fx f, bool on)
    {
        if (f == null || f.on == on || f.tr == null) return;
        f.on = on;
        f.tr.gameObject.SetActive(on);
    }

    void HideAll()
    {
        if (!drawnLastFrame) return;
        drawnLastFrame = false;
        for (int i = 0; i < lamps.Count; i++) Show(lamps[i], false);
        for (int i = 0; i < nozzles.Count; i++)
        {
            var nz = nozzles[i];
            Show(nz.core, false); Show(nz.streak, false);
            for (int s = 0; s < nz.dots.Length; s++) Show(nz.dots[s], false);
        }
        for (int i = 0; i < muzzles.Count; i++) { Show(muzzles[i].glow, false); Show(muzzles[i].streak, false); }
        Show(fire, false);
    }

    // ---- recoil ------------------------------------------------------------------------------------
    //
    // The hull's own parts move, not the root: the root's position belongs to UnitModelRenderer, which
    // rewrites it every frame and would either erase the kick or, depending on update order, double it.
    void TickRecoil(float dt)
    {
        if (hullParts.Length == 0) return;
        if (recoilWorld.sqrMagnitude < 1e-10f)
        {
            if (recoilApplied) { SetParts(Vector3.zero); recoilApplied = false; }
            return;
        }
        // A stiff spring back to rest: most of the kick is gone in a tenth of a second.
        recoilWorld *= Mathf.Exp(-14f * Mathf.Max(dt, 0f));
        if (recoilWorld.sqrMagnitude < 1e-10f) recoilWorld = Vector3.zero;
        SetParts(transform.InverseTransformVector(recoilWorld));
        recoilApplied = true;
    }

    bool recoilApplied;

    void SetParts(Vector3 localOffset)
    {
        for (int i = 0; i < hullParts.Length; i++)
            if (hullParts[i] != null) hullParts[i].localPosition = hullPartRest[i] + localOffset;
    }

    // ---- damage ------------------------------------------------------------------------------------
    //
    // Under 70% hull a ship starts to trail smoke from points on its hull; under 45% it throws sparks;
    // under 30% one of those points is on fire, a guttering orange glow. All of it is rate-limited and
    // handed to ExplosionRenderer's pooled puffs, so a burning fleet is a bounded number of quads.
    void TickDamage(float dt, bool detail, Quaternion camRot, float lossy, float wpp)
    {
        if (!DamageEffects || unit == null || damagePoints.Length == 0)
        {
            if (fire != null) Show(fire, false);
            return;
        }
        float hf = unit.HealthFraction;
        if (hf > 0.70f || !detail) { if (fire != null) Show(fire, false); return; }

        var er = ExplosionRenderer.Instance;
        float sev = Mathf.Clamp01(1f - hf / 0.70f);
        float len = WorldLength;

        smokeT -= dt;
        if (dt > 0f && smokeT <= 0f && er != null)
        {
            smokeT = Mathf.Lerp(0.6f, 0.12f, sev) * Random.Range(0.7f, 1.3f);
            Vector3 p = transform.TransformPoint(damagePoints[Random.Range(0, damagePoints.Length)]);
            float grey = Mathf.Lerp(0.30f, 0.16f, sev);
            er.Smoke(p, len * Mathf.Lerp(0.25f, 0.45f, sev), new Color(grey, grey * 0.96f, grey * 0.94f), Mathf.Lerp(0.35f, 0.6f, sev), Mathf.Lerp(0.9f, 1.6f, sev));
        }

        if (hf < 0.45f)
        {
            sparkT -= dt;
            if (dt > 0f && sparkT <= 0f && er != null)
            {
                sparkT = Mathf.Lerp(0.5f, 0.15f, sev) * Random.Range(0.6f, 1.4f);
                Vector3 p = transform.TransformPoint(damagePoints[Random.Range(0, damagePoints.Length)]);
                int n = Random.Range(2, 5);
                for (int i = 0; i < n; i++)
                    er.Spark(p, Random.onUnitSphere * Random.Range(0.6f, 1.6f), new Color(1f, 0.7f, 0.3f), Random.Range(0.2f, 0.4f), len * 0.05f);
            }
        }

        if (hf < 0.30f)
        {
            if (fire == null)
            {
                fire = MakeFx("HullFire", FxKit.Dot(), damagePoints[firePoint], hullWidth * 0.35f, new Color(1f, 0.45f, 0.1f));
                fire.minPx = 3f;
            }
            float flick = 0.55f + 0.45f * Mathf.PerlinNoise(Time.time * 7f, unit.id * 0.13f);
            Draw(fire, damagePoints[firePoint], new Color(1.9f, 0.7f, 0.15f) * flick, flick, 0.7f + 0.5f * flick, camRot, lossy, wpp);
        }
        else if (fire != null) Show(fire, false);
    }
}
