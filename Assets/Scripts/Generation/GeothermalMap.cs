using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE GEOTHERMAL FIELD — one system where there used to be two.
//
// The Heat Index and the Tectonics overlay were separate features that were always describing the same
// thing: where the crust is hot and moving. The Heat Index was "heat in the crust, not the air" from the
// beginning; the tectonics overlay drew the fault lines that heat comes OUT of. Keeping them apart meant
// a world could show a red fault line running through ground the Heat Index called cold, and a
// geothermal plant sited by one map could be wrong according to the other. They are merged here, and the
// merged thing is called the GEOTHERMAL INDEX.
//
// WHAT IT IS: a single 0..1 field over a world's surface, derived (never stored, like every other index
// and like the plate geometry itself) from two sources that are added together by taking whichever is
// stronger:
//
//   FAULT LINES   — on a world with plates. A plate margin reads 40% by default; the harder the two
//                   plates are working against each other the higher it climbs, up to 100% where they
//                   drive head-on. A high-activity margin RADIATES: the heat spreads up to three tiles
//                   either side and does not fall below 70% inside that band, so an active fault is a
//                   region of country to site around rather than a one-tile line to stand on.
//
//   HOTSPOTS      — on any world, with plates or without. Focused plumes of mantle heat: a small 90%+
//                   core, an 80%+ ring around it, a 70%+ skirt around that, and nothing at all in
//                   between them. This is where volcanoes are, and it is why a world with no plates at
//                   all can still be covered in them.
//
// WHAT READS IT
//   * SurfaceIndex.Geothermal — the survey overlay and the geothermal plant's siting index.
//   * PlanetTerrainGenerator  — elevation. Convergent margins push the ground UP, rifts drop it, and a
//                               volcanic hotspot core is the highest ground on a plate-less world.
//   * EarthquakeManager       — a quake only damages what is standing on highlighted ground.
//   * PlanetTemperature       — a world's internal heat, which is what makes a molten world molten.
//
// All four read THIS, so they cannot disagree with each other: the red on the map is the ground that
// shakes, the ground that rose, and the ground the plant wants.
// ============================================================================================
public static class GeothermalMap
{
    // ---- The numbers the request specifies, as named constants ------------------------------------

    /// What a plate margin reads with no particular stress on it. "The continental plate lines should
    /// generate lower index values by default 40."
    public const float PlateLineBase = 0.40f;

    /// The floor of the radiated band around a MAXIMALLY active fault. "In these high activity areas,
    /// the radiated Heat Index Minimum value should be 70."
    public const float RadiatedFloor = 0.70f;

    /// How far a maximally active fault radiates, in tiles either side. "up to 3 in each direction for
    /// very high activity fault lines".
    public const float RadiateTiles = 3f;

    /// ...and how far an inactive one does. Not zero: the drawn fault line is one to three tiles wide
    /// itself, so a margin has to colour the tiles it is actually on.
    const float QuietRadiateTiles = 1.1f;

    // MEASURED (Node port of FaultAt) — every number the request specifies, checked:
    //
    //   activity   d=0   d=0.5   d=1   d=1.5   d=2   d=2.5   d=3
    //   0.00        40      23      1       0     0       0     0    a quiet margin: the line, and little else
    //   0.50        70      65     53      41    35       0     0
    //   1.00       100      98     92      85    78      72     0    head-on: the full band
    //
    //   quiet margin on the line ......... 40    (spec: "default 40")
    //   head-on margin on the line ....... 100   (spec: "very high ... up to 100")
    //   minimum anywhere inside the band .. 70.0 (spec: "radiated minimum value should be 70")
    //   width of the >=70 band ........... 3.01 tiles either side (spec: "up to 3 in each direction")

    /// At and above this the ground is a volcano. The request's "in the 97-100 range on Geothermal
    /// Index" — the vent itself, not the mountain it sits on.
    public const float VolcanoIndex = 0.97f;

    // ---- Hotspots ---------------------------------------------------------------------------------

    // ============================================================================================
    // THE HOTSPOT SHAPING — calibrated against the noise field's MEASURED distribution
    //
    // The underlying field is PlanetTerrainGenerator.WorldNoise: three octaves of Perlin FBm through the
    // seam-preserving wrap. Measured (Node port, six worlds of 200x100), it has mean 0.500 and standard
    // deviation 0.140 — so it is a bell, not a uniform, and the tail matters enormously:
    //
    //     80th pct 0.618     95th 0.735     99th 0.820     99.9th 0.892     max ~0.978
    //
    // GETTING THIS WRONG IS SILENT. The first cut of this code thresholded at 0.63 and sharpened with an
    // exponent of 1.7 out of a nominal 0..1 range, which reads as reasonable and is not: measured against
    // the real field, a MAXIMALLY volcanic world came out with four tiles above 70% and NO TILE ANYWHERE
    // above 97%. Volcanoes would simply never have generated, on any world, ever — and nothing would have
    // reported an error, because every individual number in the formula was in range.
    //
    // So both ends are anchored to measured percentiles instead of to 0 and 1:
    //
    //   TOP is 0.90, near the 99.9th percentile — a value the field genuinely reaches a few dozen times
    //       on a world, which is what a mantle plume's core should be.
    //   CUT slides with the world's intensity, from 0.76 (a quiet world: a couple of warm patches) to
    //       0.56 (a covered-in-volcanoes world). This is the lever that decides HOW MUCH ground is hot.
    //   CEILING also slides, 0.70 to 1.00, reaching the top at intensity 0.75. This is the lever that
    //       decides HOW HOT it gets — and therefore whether the world has volcanoes at all, since a vent
    //       needs 97%. Two levers rather than one because "a few faint hotspots" and "a handful of very
    //       hot ones" are different worlds and one number cannot say both.
    //   FOCUS is BELOW 1, which is the opposite of what it looks like it should be. An exponent over 1
    //       compresses the top of the range and made every band the same size — one flat blob with no
    //       taper. Under 1 it expands the top, so the bands nest properly.
    //
    // MEASURED with these values (tiles on a 200x100 world, at >=70 / >=80 / >=90 / >=97):
    //
    //     intensity 0.05    21 /   0 /  0 /  0     a couple of warm patches, no volcanism
    //     intensity 0.4    105 /  39 /  0 /  0     real geothermal ground, still no vents
    //     intensity 0.6    226 /  94 / 29 /  0     hot country
    //     intensity 0.75   381 / 175 / 63 / 22     the first world with actual volcanoes
    //     intensity 1.0    546 / 237 / 76 / 25     covered in them
    //
    // Every band strictly smaller than the one below it, at every intensity — which is the request's
    // "focus most of the highest percentage areas together (90+), and then taper off into 80+, and then
    // 70+", as a property of the curve rather than three hand-drawn rings.
    const float HotspotCutQuiet = 0.76f, HotspotCutBusy = 0.56f;
    const float HotspotTop = 0.90f;
    const float HotspotCeilQuiet = 0.70f, HotspotCeilBusy = 1.00f;

    /// Intensity at which the ceiling reaches 1 — and therefore the point above which a world can have
    /// volcanoes at all. Below it a world has geothermal ground and no vents, which is a real and useful
    /// distinction: somewhere to put a plant, nowhere that erupts.
    const float HotspotCeilingKnee = 0.75f;

    /// Sharpening applied across the cut..top window. BELOW 1 — see the note above.
    const float HotspotFocus = 0.75f;

    /// Roughly how many plumes fit around the equator. Fewer than the survey overlays' weather/solar
    /// blobs (which are weather systems): a mantle plume is a small, sharply bounded thing.
    const float HotspotBlobsMin = 7f, HotspotBlobsMax = 16f;

    /// Noise salt, so the hotspot field is not a copy of any other field sampled from the same seed.
    const float HotspotSalt = 61.7f;

    /// Below this a world simply has no hotspots. Set so that a little under half of all worlds have
    /// none at all — the request wants worlds that are covered in volcanoes AND worlds that have none,
    /// and a distribution with no dead zone gives every world a few.
    const float HotspotDeadCut = 0.42f;

    // ============================================================================================
    // THE FIELD
    // ============================================================================================

    /// The Geothermal Index at a normalized surface position, 0..1.
    ///
    /// Call this when you do NOT already have a tectonics sample in hand. If you do — the terrain
    /// generator does, it takes one per tile anyway — use the overload below instead; TectonicsMap.Sample
    /// is the most expensive call in world generation and taking it twice per tile doubles that cost for
    /// nothing.
    public static float At(CelestialBody b, float u, float v)
    {
        if (b == null) return 0f;
        if (!TectonicsMap.Active(b)) return HotspotAt(b, u, v);
        var hit = TectonicsMap.Sample(b, u, v);
        return At(b, u, v, hit);
    }

    /// The Geothermal Index, given a tectonics sample already taken at the same point.
    public static float At(CelestialBody b, float u, float v, in TectonicsMap.Hit hit)
        => Combine(b, hit, HotspotAt(b, u, v));

    /// The Geothermal Index from BOTH halves already in hand.
    ///
    /// Exists for the terrain generator, which computes the hotspot field itself (it needs it separately,
    /// to raise the ground under a vent) and would otherwise pay for it twice per tile. The hotspot field
    /// is six Perlin lookups, and the terrain bake runs this once for every cell of every world in the
    /// galaxy — so "twice per tile" is a measurable share of the whole load.
    public static float Combine(CelestialBody b, in TectonicsMap.Hit hit, float hotspot)
    {
        if (b == null) return 0f;
        float fault = TectonicsMap.Active(b) ? FaultAt(b, hit) : 0f;

        // WHICHEVER IS STRONGER, not the sum. A volcanic hotspot sitting on top of a fault is one hot
        // place, not two, and adding them would push every plate world's margins straight into the
        // volcano band wherever a plume happened to cross one.
        return Mathf.Clamp01(Mathf.Max(fault, hotspot));
    }

    // ---- Faults ----------------------------------------------------------------------------------

    /// How hard this fault is working, 0..1. The number that decides both how hot the line itself reads
    /// and how far the heat spreads from it.
    ///
    /// Three kinds of margin, and they are NOT equally hot:
    ///   CONVERGENT — two plates driving into each other. The hottest thing a crust does: subduction,
    ///                melting, a volcanic arc. Counts at full weight, which is what puts a head-on
    ///                collision at the request's "up to 100".
    ///   SHEAR      — two plates grinding past each other. Nearly as active, and the request names it
    ///                alongside collision, but it vents less than it shakes — so a shade under.
    ///   DIVERGENT  — a rift. Genuinely hot (this is where new crust is made) but spread thin over a
    ///                wide, low, quiet margin rather than concentrated, so under half.
    public static float FaultActivity(in TectonicsMap.Hit hit)
    {
        float converge = Mathf.Max(0f, hit.convergence);
        float rift = Mathf.Max(0f, -hit.convergence);
        float act = Mathf.Max(converge, hit.shear * 0.85f);
        act = Mathf.Max(act, rift * 0.45f);
        return Mathf.Clamp01(act);
    }

    /// The fault contribution at a point, from a sample already taken.
    static float FaultAt(CelestialBody b, in TectonicsMap.Hit hit)
    {
        if (hit.plateB < 0) return 0f;                       // a one-plate world has no margins
        float d = hit.distanceTiles;
        if (float.IsInfinity(d) || d >= float.MaxValue * 0.5f) return 0f;

        float act = FaultActivity(hit);

        // ON THE LINE: the request's 40 by default, climbing to 100 where the plates drive together.
        float onLine = Mathf.Lerp(PlateLineBase, 1f, act);

        // AT THE EDGE OF THE RADIATED BAND: nothing for a quiet margin, the request's 70 for a maximally
        // active one. Interpolating the FLOOR by activity — rather than applying a flat 70 everywhere and
        // then gating it — is what makes "the radiated minimum is 70 in high activity areas" true without
        // also making it true in low-activity ones.
        float edge = Mathf.Lerp(0f, RadiatedFloor, act);

        // ...over this many tiles. An active margin reaches three; a quiet one barely leaves its own
        // drawn line.
        float reach = Mathf.Lerp(QuietRadiateTiles, RadiateTiles, act);
        if (d >= reach) return 0f;

        float t = 1f - d / reach;                            // 1 on the line, 0 at the edge of the band

        // Smoothstepped, so the band has a soft rim rather than a hard ring the eye reads as an outline
        // of its own. (The overlay draws its own outlines per 10% band — see SurfaceIndex.Outline.)
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(edge, onLine, t);
    }

    // ---- Hotspots --------------------------------------------------------------------------------

    /// The hotspot contribution at a point, 0..1. Zero everywhere on a world that rolled no plumes.
    public static float HotspotAt(CelestialBody b, float u, float v)
    {
        float intensity = HotspotIntensity(b);
        if (intensity <= 0.001f) return 0f;

        float cut = Mathf.Lerp(HotspotCutQuiet, HotspotCutBusy, intensity);
        float ceiling = Mathf.Lerp(HotspotCeilQuiet, HotspotCeilBusy,
                                   Mathf.Clamp01(intensity / HotspotCeilingKnee));

        float raw = PlanetTerrainGenerator.WorldNoise(b, u, v, HotspotBlobs(b), HotspotSalt, 3);
        float t = Mathf.InverseLerp(cut, HotspotTop, raw);
        if (t <= 0f) return 0f;

        return Mathf.Clamp01(Mathf.Pow(t, HotspotFocus) * ceiling);
    }

    /// How volcanic this world is, 0 (no plumes at all) .. 1 (covered in them). Deterministic from the
    /// body's own terrain seed, so it costs nothing to save and survives a reload — and re-rolls when
    /// the Dev sandbox re-rolls the seed, which is what a sandbox wants.
    ///
    /// The TYPE ceiling is what stops a gas giant having volcanoes and lets a furnace world be nothing
    /// but. Everything under the ceiling is the world's own roll, so two volcanic worlds still differ.
    public static float HotspotIntensity(CelestialBody b)
    {
        if (b == null) return 0f;
        if (intensity.TryGetValue(b, out var c) && Mathf.Approximately(c.seed, b.terrainSeed) && c.type == b.type)
            return c.value;

        float value = ComputeIntensity(b);
        intensity[b] = new Intensity { seed = b.terrainSeed, type = b.type, value = value };
        return value;
    }

    static float ComputeIntensity(CelestialBody b)
    {
        float ceiling = TypeCeiling(b.type);
        if (ceiling <= 0f) return 0f;

        float h = Hash01(b.terrainSeed * 3.117f + b.id * 19.77f + 5.5f);

        // A DEAD ZONE AT THE BOTTOM, not a taper to zero. Without it every world has a few plumes, and
        // "this world has no geothermal ground at all" — which the survey readout has to be able to say —
        // stops being a thing that ever happens.
        if (h < HotspotDeadCut) return 0f;
        float roll = Mathf.InverseLerp(HotspotDeadCut, 1f, h);

        // Plate worlds get quieter plumes: most of their crustal heat is already accounted for along the
        // margins, and letting the two run at full strength together buries the fault lines the request
        // wants to be able to read on the map. Never zero, though — Earth has Hawaii AND the Ring of Fire.
        float platePenalty = b.hasTectonics ? 0.72f : 1f;

        return Mathf.Clamp01(roll * ceiling * platePenalty);
    }

    /// The most geothermal a world of this type is allowed to be.
    static float TypeCeiling(CelestialBodyType t)
    {
        switch (t)
        {
            case CelestialBodyType.GasGiant:
            case CelestialBodyType.Asteroid: return 0f;       // no crust to vent through
            case CelestialBodyType.VolcanicPlanet: return 1f;
            case CelestialBodyType.RockyPlanet:
            case CelestialBodyType.OceanPlanet:
            case CelestialBodyType.BarrenPlanet: return 1f;
            case CelestialBodyType.IcePlanet: return 0.8f;    // Enceladus: cryovolcanism is real
            case CelestialBodyType.Moon: return 0.7f;         // ...and so is Io
            default: return 0.8f;
        }
    }

    /// Plume size: fewer blobs around the equator means bigger ones. A strongly volcanic world gets MORE
    /// plumes rather than bigger ones, which is what "many surface volcanoes" looks like as opposed to
    /// "one enormous scorched region".
    static float HotspotBlobs(CelestialBody b)
        => Mathf.Lerp(HotspotBlobsMin, HotspotBlobsMax, HotspotIntensity(b));

    // ---- Whole-world readouts ---------------------------------------------------------------------

    /// How geothermally active this world is overall, 0..1 — the single number the temperature model and
    /// the survey status line quote. The stronger of its plume activity and its plate motion.
    public static float WorldIntensity(CelestialBody b)
    {
        if (b == null) return 0f;
        float hot = HotspotIntensity(b);
        float plates = PlateMotion(b);
        return Mathf.Clamp01(Mathf.Max(hot, plates));
    }

    /// The strongest plate motion on this world, 0..1 — zero if it has no plates. Cached with the
    /// layout it is read from, since the layout itself is cached and a scan of a dozen plates per tile
    /// would otherwise be paid a few hundred thousand times per world during generation.
    public static float PlateMotion(CelestialBody b)
    {
        if (!TectonicsMap.Active(b)) return 0f;
        if (motion.TryGetValue(b, out var c) && Mathf.Approximately(c.seed, b.terrainSeed)) return c.value;

        var layout = TectonicsMap.Get(b);
        float max = 0f;
        if (layout?.plates != null)
            foreach (var p in layout.plates) max = Mathf.Max(max, p.strength);

        motion[b] = new Intensity { seed = b.terrainSeed, type = b.type, value = Mathf.Clamp01(max) };
        return Mathf.Clamp01(max);
    }

    /// Does this world have any geothermal ground worth drawing at all?
    public static bool Active(CelestialBody b)
        => b != null && (TectonicsMap.Active(b) || HotspotIntensity(b) > 0.001f);

    /// Plain-language activity, for the Survey status line.
    public static string Label(CelestialBody b)
    {
        if (b == null) return "unknown";
        bool plates = TectonicsMap.Active(b);
        float hot = HotspotIntensity(b);

        if (!plates && hot <= 0.001f) return "geologically dead — no plates, no hotspots";
        if (plates && hot > 0.5f) return "active plates and strong hotspots — volcanic";
        if (plates) return "active plate margins";
        if (hot > 0.75f) return "no plates, but heavily volcanic — hotspots everywhere";
        if (hot > 0.35f) return "no plates — scattered volcanic hotspots";
        return "no plates — a few faint hotspots";
    }

    // ---- Caches ----------------------------------------------------------------------------------

    struct Intensity { public float seed; public CelestialBodyType type; public float value; }

    static readonly Dictionary<CelestialBody, Intensity> intensity = new Dictionary<CelestialBody, Intensity>();
    static readonly Dictionary<CelestialBody, Intensity> motion = new Dictionary<CelestialBody, Intensity>();

    /// Drop a world's cached values — call whenever its terrain genuinely changes (a reseed, a remodel,
    /// a type change). Same discipline as SurfaceIndex.InvalidateStats, and called from the same places.
    public static void Invalidate(CelestialBody b)
    {
        if (b == null) return;
        intensity.Remove(b);
        motion.Remove(b);
    }

    public static void InvalidateAll()
    {
        intensity.Clear();
        motion.Clear();
    }

    static float Hash01(float seed)
    {
        float v = Mathf.Sin(seed * 12.9898f + 78.233f) * 43758.5453f;
        return Mathf.Clamp01(v - Mathf.Floor(v));
    }
}
