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
// SHIP LIGHTS — running lights, thrusters, and muzzle flash for one hull
//
// Attached to a model by UnitModelRenderer as it builds it. Everything is procedural: quads with the
// project's existing additive soft-dot material, placed from the hull's own bounds. No imported
// asset is load-bearing, which is the same rule the rest of the visuals follow — a ship with no
// lights rig still flies, it is just dark.
//
// ---- WHERE THE LIGHTS GO, WITHOUT ANY PER-SHIP AUTHORING -------------------------------------
//
// A hundred and forty hulls cannot have their lamp positions placed by hand, so positions come from
// the mesh's bounding box in SHIP SPACE: bow +Z, dorsal +Y, starboard +X.
//
// Careful: the model root carries `modelRotation`, so the root's own local axes are the MESH's axes,
// not the game's. Ship space is recovered by rotating through the inverse of that correction — which
// is why `bow`, `up` and `right` below are computed rather than assumed to be Vector3.forward etc.
// Getting this wrong puts the engines on the nose of every hull whose import needed a correction.
// ============================================================================================
public class ShipLights : MonoBehaviour
{
    // ---- tuning ------------------------------------------------------------------------------

    /// The whole rig: ON.
    ///
    /// It was off while the art was landing, because running lights, drive plumes and muzzle flashes
    /// on top of a fleet of borrowed placeholder hulls is decoration on top of a stand-in — you cannot
    /// judge either. The condition for turning it back on was "when the fleet is in and the hulls can
    /// carry it", and the Aquarii are in: twenty-nine of twenty-nine, oriented and flying nose-first.
    ///
    /// A civilization whose art has not landed yet still flies borrowed hulls and now lights them too.
    /// That is the right trade: the lights are placed from each hull's own BOUNDS rather than from an
    /// authored rig, so they sit correctly on whatever mesh is actually there.
    public static bool Enabled = true;

    /// Running lights: ALLOWED, and each race decides (RaceVfxProfile.navLights).
    ///
    /// They were off for a while. They were built at 8.5% of hull length, which sounds small and is
    /// not — a ship is drawn at 0.09-0.40 world units, so each lamp came out a substantial fraction of
    /// the whole vessel and the fleet read as a string of fairy lights with a ship somewhere behind it.
    ///
    /// They are back because they are now sized as pinpricks (NavSizeFactor, capped by NavWidthCap),
    /// and because not every race carries them: Terran and Cryithn hulls are engineered things and
    /// read better lit, while a living Aquarii shrimp or a Pyrothian lava hull with an aviation strobe
    /// on its tail reads as a toy. This flag is the master switch over all of that.
    public static bool ShowNavLights = true;

    const int   MaxNavLights   = 7;
    const float NavSizeFactor  = 0.028f;   // of hull length — a third of what it was
    /// ...and never more than this fraction of the beam. Length alone over-sizes the lamps on a long,
    /// slender hull, where 2.8% of the length can be a large share of the width and the lamp stops
    /// being a point ON the hull and becomes a blob beside it.
    const float NavWidthCap    = 0.10f;
    const float ThrusterFactor = 0.30f;    // of hull width
    const float MuzzleLife     = 0.09f;    // seconds — matched to the shot leaving the barrel

    class Lamp
    {
        public Transform tr;
        public Material mat;
        public Color colour;
        public float period, phase;
        public bool constant;      // never blinks; breathes instead
        public float baseScale;

        // ---- drive plume only ----
        /// 0 is the nozzle itself; 1..n are plume segments trailing aft behind it.
        public int plumeIndex;
        /// Where this segment sits at idle. At full throttle it is pushed further aft from here, which
        /// is what makes the flame LENGTHEN with speed rather than merely brighten.
        public Vector3 restPos;
        /// Direction to push it, i.e. astern. Stored per lamp so the plume follows the hull's own
        /// axis rather than a world direction.
        public Vector3 aft;
    }

    readonly List<Lamp> lamps = new List<Lamp>();
    readonly List<Lamp> thrusters = new List<Lamp>();

    Unit unit;
    Vector3 bow, up, right;        // ship space, expressed in this transform's local space
    float hullLength, hullWidth;

    /// Current drive output, 0..1. Eased toward the target so engines spool rather than snap.
    float throttle;
    float throttleTarget;

    // Muzzle flashes are pooled per ship: a dreadnought firing eight mounts should not allocate.
    readonly List<Lamp> muzzlePool = new List<Lamp>();
    readonly List<float> muzzleLife = new List<float>();

    /// This hull's race effects, read once at build. F10 rebuilds every model, so a reloaded profile
    /// still reaches every ship without this having to watch for changes.
    RaceVfxProfile vfx;

    // ---- the hull's own emissive glow ----
    //
    // One entry per (renderer, material slot) whose material actually emits. Driven through a
    // MaterialPropertyBlock rather than a material copy: the hull materials are SHARED between every
    // ship of the same look (HullMaterials), and the pulse is per ship — it follows this ship's
    // throttle and sits on its own phase — so writing it into the material would make the whole fleet
    // throb as one. The LOD levels are listed too; they borrow the base's materials but each renderer
    // carries its own block.
    struct Glow
    {
        public Renderer renderer;
        public int slot;
        public int property;
        public Vector4 authored;   // the emissive as imported, so the multiplier never compounds
    }

    readonly List<Glow> glows = new List<Glow>();
    static MaterialPropertyBlock glowBlock;
    float glowPhase;
    float glowApplied = -1f;

    // ============================================================================================
    // BUILD
    // ============================================================================================

    public void Init(Unit u, Quaternion modelRotation, Color ownerColour)
    {
        if (!Enabled) return;   // bare hulls for now; see the Enabled flag
        unit = u;

        // Ship space, in this transform's local frame. See the header note: the root is already
        // rotated by modelRotation, so the bow is NOT local +Z.
        Quaternion inv = Quaternion.Inverse(modelRotation);
        bow   = inv * Vector3.forward;
        up    = inv * Vector3.up;
        right = inv * Vector3.right;

        Bounds b = LocalBounds();
        if (b.size.sqrMagnitude < 1e-6f) return;

        hullLength = Mathf.Abs(Vector3.Dot(b.size, bow));
        hullWidth  = Mathf.Abs(Vector3.Dot(b.size, right));
        if (hullLength < 1e-4f) hullLength = b.size.magnitude;
        if (hullWidth  < 1e-4f) hullWidth  = hullLength * 0.4f;

        vfx = RaceVfx.For(u);

        // BEFORE any lamp exists, so the glow list is the hull's renderers and nothing of ours.
        CollectGlow();

        BuildNavLights(b, ownerColour);
        BuildThrusters(b);
    }

    // ---- hull glow ---------------------------------------------------------------------------
    //
    // The generated hulls carry an emissive texture at an emissive factor of exactly 1, and bloom's
    // threshold is 1.05 (PostFxController) — so as imported, no hull glow in the fleet ever blooms,
    // and lava and bioluminescence look like paint. The race's multiplier is what lifts it over.
    //
    // A material with NO authored emission is left alone: multiplying black is black, and inventing a
    // glow the art does not have would light the whole hull evenly, which reads as a rendering fault.
    void CollectGlow()
    {
        glows.Clear();
        if (vfx == null || !vfx.AffectsHullGlow) return;   // the art as imported; leave it batchable

        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                int prop = HullMaterials.EmissiveId(m);
                if (prop < 0) continue;

                // GetVector, not GetColor. A colour property is stored LINEAR, and GetColor/SetColor
                // convert through gamma on the way — which would turn a multiplier of 3 into roughly
                // 11 by the time it reached the shader. The raw vector is the linear value, and scaling
                // it is exactly "three times as bright".
                Vector4 e = m.GetVector(prop);
                if (Mathf.Max(e.x, Mathf.Max(e.y, e.z)) <= 1e-4f) continue;

                // URP/Lit only draws `_EmissionColor` while its keyword is on, so a URP material with the
                // keyword OFF does not emit, whatever colour the property holds (a non-black value can sit
                // there unused). Switching the keyword on would invent exactly the even,
                // whole-hull glow the note above rules out, so such a material is left as the art has it.
                if (HullMaterials.NeedsEmissionKeyword(prop) && !m.IsKeywordEnabled("_EMISSION")) continue;

                glows.Add(new Glow { renderer = r, slot = i, property = prop, authored = e });
            }
        }

        if (glows.Count == 0) return;
        if (glowBlock == null) glowBlock = new MaterialPropertyBlock();

        // On the quarter-beat grid like every other light, so a fleet's hulls swell together in
        // syncopation rather than each on a phase of its own.
        int seed = unit != null ? unit.id : 0;
        glowPhase = FleetClock.Phases[Mathf.Abs(seed) % FleetClock.Phases.Length];

        ApplyGlow(GlowFactor());
    }

    /// The multiplier this frame: the race's base level, dipping with its pulse, hotter under power.
    float GlowFactor()
    {
        float k = vfx.emissiveMultiplier;
        if (vfx.emissivePulseDepth > 0.001f)
            k *= FleetClock.Breathe(RaceVfx.SnapBeats(vfx.emissivePulseBeats), glowPhase,
                                    Mathf.Clamp01(vfx.emissivePulseDepth));
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
            // Alpha is not brightness; only the colour channels scale.
            glowBlock.SetVector(g.property, new Vector4(e.x * factor, e.y * factor, e.z * factor, e.w));
            g.renderer.SetPropertyBlock(glowBlock, g.slot);
        }
    }

    /// Bounds of every renderer under this object, in THIS transform's local space.
    Bounds LocalBounds()
    {
        var rends = GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds acc = default;
        foreach (var r in rends)
        {
            if (r == null) continue;
            // World bounds mapped back into local space. Coarser than walking vertices and correct
            // for a hierarchy with sub-meshes at their own transforms — the same trade
            // ShipMeshManifest makes for the same reason.
            var wb = r.bounds;
            var lc = transform.InverseTransformPoint(wb.center);
            var le = transform.InverseTransformVector(wb.extents);
            var lb = new Bounds(lc, new Vector3(Mathf.Abs(le.x), Mathf.Abs(le.y), Mathf.Abs(le.z)) * 2f);
            if (!any) { acc = lb; any = true; } else acc.Encapsulate(lb);
        }
        return any ? acc : default;
    }

    // ---- running lights ----------------------------------------------------------------------
    //
    // Aviation convention, because it is what reads as "a vehicle" instantly: red to port, green to
    // starboard, white strobes fore and aft. On top of that a couple of lamps in the OWNER's colour,
    // which is the cheapest possible way to tell two fleets apart in a fight and matches the badge
    // the ship already carries.
    void BuildNavLights(Bounds b, Color ownerColour)
    {
        if (!ShowNavLights) return;
        if (vfx != null && !vfx.navLights) return;   // this race flies dark; see ShowNavLights
        var p = vfx ?? RaceVfx.ForCiv(null);

        // Sized off the hull's OWN bounds, in the root's local space — the same space the lamps are
        // parented in, so FitTo's scale applies to both and the ratio holds at any class size.
        float size = Mathf.Min(hullLength * NavSizeFactor, hullWidth * NavWidthCap);

        // The race's strobe tempo, with the three rates kept as the 1 : 2 : 6 ladder the rig was
        // designed on and every one snapped to a legal FleetClock period.
        float fast = RaceVfx.SnapBeats(p.navBlinkBeats);
        float tail = RaceVfx.SnapBeats(p.navBlinkBeats * 2f);
        float slow = RaceVfx.SnapBeats(p.navBlinkBeats * 6f);
        float halfW = hullWidth * 0.5f;
        float halfL = hullLength * 0.5f;
        Vector3 c = b.center;

        // Deterministic per-unit variation: two ships of the same class should not blink in lockstep
        // like a chorus line, but the SAME ship must pick the same pattern every time it is rebuilt.
        int seed = unit != null ? unit.GetHashCode() : 0;
        var rng = new System.Random(seed);
        float Pick(float[] a) => a[rng.Next(a.Length)];

        // port / starboard — constant, the way real ones are
        Add(c - right * halfW + up * size * 0.4f, size * 0.85f, p.navPort, 2f, 0f, constant: true);
        Add(c + right * halfW + up * size * 0.4f, size * 0.85f, p.navStarboard, 2f, 0f, constant: true);

        // dorsal and tail strobes — the race's strobe colour, the fastest things on the hull
        Add(c + up * (Vector3.Dot(b.extents, up)) , size * 0.9f, p.navStrobe, fast, Pick(FleetClock.Phases));
        Add(c - bow * halfL + up * size * 0.5f,     size * 0.8f, Color.Lerp(p.navStrobe, Color.white, 0.3f), tail, Pick(FleetClock.Phases));

        // bow beacon — slow, so a ship reads bow-first at a glance. Kept warm whatever the race, since
        // it is the one lamp whose job is "which end is the front", and that wants to read the same on
        // every hull in the game.
        Add(c + bow * halfL, size * 0.7f, new Color(1f, 0.85f, 0.5f), slow, Pick(FleetClock.Phases));

        // owner-colour beacons, one dorsal one ventral
        var oc = ownerColour; oc.a = 1f;
        Add(c + up * halfW * 0.5f - bow * halfL * 0.25f, size * 0.75f, oc, Pick(FleetClock.Periods), Pick(FleetClock.Phases));
        Add(c - up * halfW * 0.5f + bow * halfL * 0.15f, size * 0.65f, oc, Pick(FleetClock.Periods), Pick(FleetClock.Phases));
    }

    void Add(Vector3 localPos, float scale, Color colour, float period, float phase, bool constant = false)
    {
        if (lamps.Count >= MaxNavLights) return;
        var lamp = MakeQuad("NavLight", localPos, scale, colour);
        lamp.period = period; lamp.phase = phase; lamp.constant = constant;
        lamps.Add(lamp);
    }

    // ---- thrusters ---------------------------------------------------------------------------
    //
    // Placed on the stern plane, spread across the beam. Brightness and length track speed, which is
    // the thing the request was really about: a ship under way should look like it is under power,
    // and a parked one should not.
    // ---- how a flame is made out of round sprites ------------------------------------------------
    //
    // A drive plume wants to be a long tapered cone, and the obvious way to draw one — a single quad
    // stretched along the hull's axis — does not survive being looked at. A camera-facing billboard
    // cannot also be axis-aligned, and a quad that IS axis-aligned vanishes when viewed edge-on, which
    // at this game's top-down-ish angles is most of the time.
    //
    // So the plume is built from a few round billboards in a line astern, each smaller, dimmer and
    // further out than the last. It reads as a tapered flame from every angle, costs four sprites, and
    // reuses the billboard and material every other light here already uses.
    const int PlumeSegments = 4;

    void BuildThrusters(Bounds b)
    {
        Vector3 stern = b.center - bow * (hullLength * 0.5f);
        float size = hullWidth * ThrusterFactor;
        Vector3 aft = -bow;

        // Bigger hulls get more nozzles. Purely cosmetic, but a dreadnought with one exhaust looks
        // like a bigger fighter rather than a bigger ship.
        int n = hullLength > 0.30f ? 4 : hullLength > 0.20f ? 3 : hullLength > 0.12f ? 2 : 1;

        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : (i / (float)(n - 1)) * 2f - 1f;   // -1..1 across the beam
            Vector3 nozzle = stern + right * (t * hullWidth * 0.30f);

            for (int s = 0; s <= PlumeSegments; s++)
            {
                // The core is white-hot and the tail cools toward the drive colour, because that is
                // what a real exhaust does and it is most of what sells a plume as hot rather than as
                // a blue smear. The two ends are the race's (RaceVfx): a Terran drive cools to blue,
                // a Pyrothian one to furnace orange.
                float k = s / (float)PlumeSegments;
                Color c = vfx != null
                    ? Color.Lerp(vfx.thrusterCore, vfx.thrusterTail, k)
                    : Color.Lerp(new Color(0.85f, 0.95f, 1f), new Color(0.25f, 0.55f, 1f), k);

                Vector3 pos = nozzle + aft * (hullLength * 0.06f * s);
                var lamp = MakeQuad(s == 0 ? "Thruster" : "Plume", pos, size * (1f - 0.16f * s), c);
                lamp.period = 0f;
                lamp.plumeIndex = s;
                lamp.restPos = pos;
                lamp.aft = aft;
                thrusters.Add(lamp);
            }
        }
    }

    Lamp MakeQuad(string name, Vector3 localPos, float scale, Color colour)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);              // lights must never eat a click meant for the hull
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * scale;

        var mat = SpaceMaterials.Additive(colour);
        mat.mainTexture = SpaceMaterials.SoftDot();
        var r = go.GetComponent<Renderer>();
        r.material = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        go.AddComponent<FaceCamera>();

        return new Lamp { tr = go.transform, mat = mat, colour = colour, baseScale = scale };
    }

    // ============================================================================================
    // DRIVE
    // ============================================================================================

    /// Told by UnitModelRenderer each frame how hard this hull is working, 0..1.
    public void SetThrottle(float t) => throttleTarget = Mathf.Clamp01(t);

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // Spool. Asymmetric on purpose: engines light quickly and die away slowly, which is how
        // anything with mass behaves and is the difference between "thrusters" and "a lamp on a timer".
        float rate = throttleTarget > throttle ? 3.5f : 1.4f;
        throttle = Mathf.MoveTowards(throttle, throttleTarget, rate * dt);

        // ---- hull glow ----
        // Only for a race whose glow MOVES; a static one was written once in CollectGlow. And only
        // when the value has changed enough to see — a property block write per renderer per frame is
        // cheap, but not free, and a slow 4-beat breath spends most frames changing by nothing.
        if (glows.Count > 0 && !vfx.HullGlowIsStatic)
        {
            float g = GlowFactor();
            if (Mathf.Abs(g - glowApplied) > 0.01f) ApplyGlow(g);
        }

        // ---- running lights ----
        for (int i = 0; i < lamps.Count; i++)
        {
            var l = lamps[i];
            if (l.tr == null) continue;
            float k = l.constant
                ? FleetClock.Breathe(l.period <= 0f ? 2f : l.period, l.phase)
                : FleetClock.Strobe(l.period, l.phase);
            // A blinking lamp is never fully black: a dark lamp on a dark hull in dark space simply
            // vanishes, and the ship loses the outline the lights were there to give it.
            float a = Mathf.Lerp(0.12f, 1f, k);
            SetLamp(l, a, 1f + k * 0.35f);
        }

        // ---- thrusters ----
        for (int i = 0; i < thrusters.Count; i++)
        {
            var t = thrusters[i];
            if (t.tr == null) continue;

            // A slight flicker keeps a plume from looking like a decal, and it is locked to the beat
            // like everything else so it never fights the running lights. How deep it dips is the
            // race's: 0.1 (the old fixed value) is a steady drive, and a Pyrothian one gutters.
            float fl = vfx != null ? Mathf.Clamp01(vfx.thrusterFlicker) : 0.1f;
            float flicker = (1f - fl) + fl * Mathf.Sin(FleetClock.Beats * Mathf.PI * 2f * 3f + i);
            float k = throttle * flicker;

            // Segments further down the plume need MORE throttle before they light, so the flame grows
            // out of the nozzle as the ship accelerates instead of the whole cone fading up together.
            // At idle only the nozzle glows; at full burn the tail reaches its full length.
            float seg = t.plumeIndex / (float)PlumeSegments;
            float reach = Mathf.Clamp01((throttle - seg * 0.55f) / 0.45f);
            float bright = k * reach;

            if (t.plumeIndex > 0)
                t.tr.localPosition = t.restPos + t.aft * (hullLength * 0.10f * t.plumeIndex * throttle);

            SetLamp(t, bright, 0.35f + bright * 1.25f);
        }

        // ---- muzzle flashes ----
        for (int i = muzzlePool.Count - 1; i >= 0; i--)
        {
            muzzleLife[i] -= dt;
            var m = muzzlePool[i];
            if (muzzleLife[i] <= 0f) { if (m.tr != null) m.tr.gameObject.SetActive(false); continue; }
            float k = muzzleLife[i] / MuzzleLife;
            SetLamp(m, k, 0.6f + k * 1.6f);
        }
    }

    static void SetLamp(Lamp l, float alpha, float scaleMul)
    {
        var c = l.colour; c.a = Mathf.Clamp01(alpha);
        // Additive: brightness is carried by the colour itself, not just alpha, so a dim lamp reads
        // dim rather than merely transparent.
        l.mat.color = new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
        l.tr.localScale = Vector3.one * (l.baseScale * scaleMul);
    }

    // ============================================================================================
    // GUNFIRE
    //
    // Called the instant a round is created, from the same call that spawns it — so the flash and the
    // projectile cannot drift apart. Anything that re-derived the timing from a cooldown would.
    // ============================================================================================
    public void FlashMuzzle(Vector3 worldPos, Color colour)
    {
        if (!Enabled) return;
        Lamp free = null;
        for (int i = 0; i < muzzlePool.Count; i++)
            if (muzzleLife[i] <= 0f) { free = muzzlePool[i]; muzzleLife[i] = MuzzleLife; break; }

        if (free == null)
        {
            if (muzzlePool.Count >= 8) return;                     // a hard ceiling; flashes are cheap but not free
            free = MakeQuad("Muzzle", Vector3.zero, hullWidth * 0.35f, colour);
            muzzlePool.Add(free);
            muzzleLife.Add(MuzzleLife);
        }

        free.colour = colour;
        free.tr.gameObject.SetActive(true);
        free.tr.position = worldPos;
    }

    void OnDestroy()
    {
        foreach (var l in lamps)     if (l?.mat != null) Destroy(l.mat);
        foreach (var l in thrusters) if (l?.mat != null) Destroy(l.mat);
        foreach (var l in muzzlePool) if (l?.mat != null) Destroy(l.mat);
    }
}
