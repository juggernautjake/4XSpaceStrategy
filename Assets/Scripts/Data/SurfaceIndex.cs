using System.Collections.Generic;
using UnityEngine;

// The map overlays you survey a world for. Each is a 0..1 score per surface tile that says how well a
// given kind of building would do there.
// NOTE: `Geothermal` was called `Heat`. It is the SAME overlay, merged with the plate-tectonics view
// (see GeothermalMap) — one index for "where is the crust hot and moving", because two separate systems
// describing the same ground could and did disagree with each other.
public enum SurfaceIndexKind { None, Mineral, Geothermal, Fertile, Wind, Solar, Water }

// ============================================================================================
// PER-TILE SURVEY INDEXES
//
// DERIVED FROM THE TERRAIN ITSELF, never stored and never invented.
//
// PlanetTerrainGenerator already builds a coherent field per tile — elevation, moisture, temperature
// (which falls off with latitude and scales with the planet's distance from its star), ridge — and
// CLASSIFIES the biome from it. These indexes read that same field back. That's what makes the results
// make sense rather than look random:
//
//   * An ocean is cooler than a desert on the same world because the ONE temperature value that made
//     one an ocean and the other a desert is the same value the Heat index reports.
//   * Poles are cold, equators are hot, because temperature is (1 - latitude) weighted.
//   * A world close to its star is hotter EVERYWHERE, because BiasHeat scales terrainParams.heat by
//     distance — so its coldest tile can still out-produce another world's hottest.
//   * Mountains are windy and mineral-rich because elevation and ridge are high there.
//
// So there are two different questions, and the UI answers both:
//   ABSOLUTE — "what will this actually yield?"  -> Get()
//   RELATIVE — "where on THIS world is best?"    -> Percentile()/TopFraction(), which is why a cold
//              world still highlights its ten hottest tiles even though they're all poor.
//
// Costs nothing to save, survives a reload untouched, and a world re-rolled from the same seed reads
// identically — the same guarantee the terrain already makes.
// ============================================================================================
public static class SurfaceIndex
{
    public static readonly SurfaceIndexKind[] All =
    {
        SurfaceIndexKind.Mineral, SurfaceIndexKind.Geothermal, SurfaceIndexKind.Fertile,
        SurfaceIndexKind.Wind, SurfaceIndexKind.Solar, SurfaceIndexKind.Water
    };

    // ============================================================================================
    // AN INDEX A WORLD HAS NO USE FOR IS NOT SHOWN AT ALL
    //
    // Three of the six describe something a world may simply not have. A world with no plates and no
    // hotspots has no geothermal ground; a world with no water has no hydrology; a sterile world has no
    // soil. Those indexes are not "low" on such a world — they are ABSENT, and there is nothing a
    // survey could ever reveal.
    //
    // They used to be listed anyway, greyed or empty. That is worse than it sounds, because a level-2
    // survey reads the indexes IN ORDER and spends real time on each: a research ship parked over a dry,
    // dead rock spent a third of its sweep mapping the hydrology and the farmland of a world that has
    // neither, and then reported nothing, twice. The player pays in time for an answer that was known
    // before the ship arrived.
    //
    // REMOVED, NOT GREYED — and that is the opposite of the doctrine everywhere else in this codebase
    // (see InspectorWindow, which greys a tab rather than hiding it so the player can see the road
    // ahead). The distinction is that greying answers "not yet"; there is no yet here. Nothing the
    // player can do reveals the hydrology of a world with no water, so a greyed row would be a
    // permanent advertisement for a thing that will never happen.
    //
    // ...UNLESS THE WORLD CHANGES. This is a live question, re-asked every time it is drawn, never
    // cached. Terraform water onto a dry world and the Hydro Index appears; seed a biosphere and the
    // Fertility Index appears with it. The index never stopped existing — the world simply grew a use
    // for it, which is exactly the moment the player should be told it is there.
    // ============================================================================================

    /// Does this world have anything for this index to describe? False means it is not offered at all.
    public static bool Present(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return false;

        // ---- A GAS GIANT HAS NO GROUND, SO IT HAS NO INDEXES --------------------------------------
        //
        // Above the Dev Mode override, and that is deliberate: this is not "the player has not found
        // out yet", it is that the question does not apply. There is no crust to mine, no soil to farm,
        // no seabed and no plate boundary a thousand kilometres above a metallic-hydrogen ocean. The
        // generator will happily produce numbers for all six, because it works off elevation and
        // moisture fields that a giant does have — and every one of those numbers would be a fiction
        // the sandbox would then display with total confidence.
        //
        // A giant is still level-1 surveyable. Its cloud bands are a real map of a real thing.
        if (b.type == CelestialBodyType.GasGiant) return false;

        if (GameMode.DevMode) return true;   // the sandbox shows everything; that is what a sandbox is

        switch (k)
        {
            // ---- SOLAR CAN BE ABSENT ---------------------------------------------------------------
            //
            // Not "is poor" — is ABSENT. An index whose every answer is "no" is a survey pass spent to
            // learn nothing, an icon on the bar that does nothing, and a slot in the running order that
            // makes every other index finish later. Now that Solar covers the whole surface, absent means
            // no tile anywhere reaches the floor — too far from the star, or under too much air, for even
            // the highest peak to clear 40%. SolarViable is the one test, shared with the build menu.
            case SurfaceIndexKind.Solar:
                return SolarViable(b);

            // No plates and no plumes: there is no heat in this crust and no boundary to draw.
            case SurfaceIndexKind.Geothermal:
                return GeothermalMap.Active(b);

            // Read off the WATER FIELD rather than off the sea-level parameter, because the question is
            // "is there water on the ground", and a world can carry a nominal water level whose every
            // drop is frozen out or boiled off (see PlanetTerrainGenerator's climate coherence).
            case SurfaceIndexKind.Water:
                return HasAnyWater(b);

            // Soil needs life. No biosphere, no farmland, at any temperature and any moisture.
            case SurfaceIndexKind.Fertile:
                return b.biosphereActive;

            // Minerals, wind and sun are properties of any solid body — thin air and a dim star make
            // them POOR, which is a real answer worth surveying for, not an absent one.
            default:
                return true;
        }
    }

    /// The indexes worth offering on this world, in the usual order. The survey reads this rather than
    /// `All`, so a sweep never spends a pass on something that cannot exist here.
    public static SurfaceIndexKind[] PresentOn(CelestialBody b)
    {
        var list = new List<SurfaceIndexKind>(All.Length);
        foreach (var k in All) if (Present(b, k)) list.Add(k);
        return list.ToArray();
    }

    /// Is there any surface water at all? One tile is enough — a world with a single lake has a
    /// hydrology worth mapping.
    ///
    /// CACHED, because `Present` is asked while drawing and this walks the whole grid. On a 640x320 gas
    /// giant that is two hundred thousand tiles, and a panel that asked it once a frame would cost more
    /// than the terrain did. Keyed on the terrain seed and invalidated by InvalidateStats, exactly like
    /// the water field and the per-world bands — so terraforming water onto a dry world drops the entry
    /// and the Hydro Index appears the moment the surface is rebuilt.
    static readonly Dictionary<CelestialBody, (float seed, bool wet)> waterPresence
        = new Dictionary<CelestialBody, (float, bool)>();

    static bool HasAnyWater(CelestialBody b)
    {
        if (b?.surface == null) return false;
        if (waterPresence.TryGetValue(b, out var c) && Mathf.Approximately(c.seed, b.terrainSeed))
            return c.wet;

        bool wet = false;
        var tiles = b.surface.tiles;
        for (int y = 0; y < b.surface.height && !wet; y++)
            for (int x = 0; x < b.surface.width; x++)
            {
                var t = tiles[x, y];
                if (t == null) continue;
                // `type` rather than `ground`: what is ON the surface now is what a hydrology survey
                // reads. Ice counts — frozen water is still water, and thawing it is a terraforming
                // project rather than a discovery.
                if (PlanetTerrainGenerator.IsWater(t.type) || t.type == TerrainType.FrozenSea ||
                    t.type == TerrainType.Glacier || t.type == TerrainType.Ice) { wet = true; break; }
            }

        waterPresence[b] = (b.terrainSeed, wet);
        return wet;
    }

    // ---- The shared field ----
    // Read straight from the generator, so the index and the pixel you're looking at can never disagree.
    static PlanetTerrainGenerator.Sample Field(CelestialBody b, int x, int y)
    {
        float u = (x + 0.5f) / Mathf.Max(1, b.surface.width);
        float v = (y + 0.5f) / Mathf.Max(1, b.surface.height);
        return PlanetTerrainGenerator.SampleNormalized(b, u, v, b.terrainParams, 4);
    }

    // ============================================================================================
    // CONSOLIDATION — why an index is no longer a wash over a whole world
    //
    // The raw formulas below score every tile, and on most worlds most tiles score SOMETHING. That made
    // the whole continent mineable, farmable and harvestable: not because any of it was good, but because
    // "a bit of everything everywhere" is what a smooth field over a smooth terrain produces. A resource
    // map that says yes everywhere is not a map, and it removes the only reason to go and look somewhere
    // else.
    //
    // So the raw score is no longer what the game reads. Every world gets, per index, two numbers:
    //
    //   COVERAGE — what fraction of its tiles are allowed into the usable band at all. The best 12% of a
    //              world's ground for minerals is its mineral country; the other 88% reads zero however
    //              respectable its raw score was. This is the lever that makes a resource a PLACE.
    //   CEILING  — how high that band may climb, which is where ABSOLUTE quality survives. A world with
    //              no volcanism has a heat ceiling under the floor, so its best 9% is still nothing: the
    //              consolidation concentrates what a world has, it does not invent what it hasn't.
    //
    // The band runs from the index's Floor (70% for minerals, 40% for the rest) to the ceiling, and everything under the cutoff is compressed
    // below the floor — where it is drawn nowhere, produces nothing, and refuses placement. That is the
    // whole of "doing away with the bottom 69%".
    //
    // The cutoff is a PERCENTILE of this world's own raw distribution, so a world's own best ground is
    // always what gets promoted; the ceiling then decides whether being this world's best is worth
    // anything. Those are two genuinely different questions and both have to be asked.
    // ============================================================================================

    // ============================================================================================
    // THE FLOOR IS PER INDEX NOW — 40% for most, 70% for minerals
    //
    // Every index used to share one floor at 70%, which made all six read the same way: a few bright
    // patches and nothing else. That is right for MINERALS — a necessary resource that should push the
    // player outward, toward richer ground and richer worlds — and too blunt for the rest. Hydro,
    // Solar, Weather, Fertility and Geothermal are gradients, and a 40-60% site is a real, if poor,
    // option: a plate line's modest heat, a far world's weak sun, the dry edge of a river valley.
    //
    // So below the floor an index yields nothing and refuses placement, exactly as before; the floor is
    // simply lower for everything that is not a mineral seam.
    // ============================================================================================

    /// The MINERAL floor. It was the universal floor; now it is the strict one.
    public const float ShowFloor = 0.70f;

    /// The floor for every other index: the absolute lowest any index reads before it counts as nothing.
    public const float BaseFloor = 0.40f;

    /// The SOLAR floor, lower than the rest (2026-10-09) so a thick-aired world, which loses ten points of
    /// sunlight per atmosphere, can still sometimes run a solar array.
    public const float SolarFloor = 0.20f;

    /// Below this an index YIELDS nothing, is not drawn, and refuses to be built on.
    public static float Floor(SurfaceIndexKind k)
        => k == SurfaceIndexKind.Mineral ? ShowFloor
         : k == SurfaceIndexKind.Solar ? SolarFloor
         : BaseFloor;

    /// A plate margin reads exactly this (GeothermalMap.PlateLineBase). It used to be a drawing floor of
    /// its own, under the yield floor; with Geothermal's floor at 40 the two are now the same number, and
    /// a plate line is a real — if low-yield — geothermal site.
    public const float PlateLineFloor = 0.40f;

    /// The lowest value of this index that gets painted on the map at all. The same as Floor: a value
    /// that is drawn is a value that yields.
    public static float DrawFloor(SurfaceIndexKind k) => Floor(k);

    /// How many 10% bands sit between this index's floor and 100: three for minerals, six for the rest.
    public static int Steps(SurfaceIndexKind k)
        => Mathf.Max(1, Mathf.RoundToInt((1f - Floor(k)) / BandStep));

    /// The usable band is read in steps of this, so a glance at the map sorts good ground from very good
    /// ground without reading a single number. See Band / Highlight.
    public const float BandStep = 0.10f;

    /// The raw score, before consolidation: what the terrain here would be worth if every tile counted.
    /// Kept separate because the consolidation needs the whole world's distribution of these, and asking
    /// Get for it would be circular.
    public static float Raw(CelestialBody b, SurfaceIndexKind kind, int x, int y)
    {
        if (b?.surface == null || x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) return 0f;

        // These two are read off the world and the tile alone — no noise sample needed, and a sample is
        // the expensive part of every other index.
        if (kind == SurfaceIndexKind.Solar) return Solar(b, x, y);
        if (kind == SurfaceIndexKind.Water) return Water(b, x, y);

        var f = Field(b, x, y);
        var t = b.surface.tiles[x, y];

        float u = (x + 0.5f) / Mathf.Max(1, b.surface.width);
        float v = (y + 0.5f) / Mathf.Max(1, b.surface.height);

        switch (kind)
        {
            case SurfaceIndexKind.Mineral: return Mineral(f, t);
            case SurfaceIndexKind.Geothermal: return Geothermal(b, f, u, v);
            case SurfaceIndexKind.Fertile: return Fertile(b, f);
            case SurfaceIndexKind.Wind: return Wind(b, f, x, y);
            default: return 0f;
        }
    }

    // ============================================================================================
    // THE ONE INDEX THAT IS NOT CONSOLIDATED
    //
    // Every other index is a relative judgement — "how good is this ground FOR THIS WORLD" — so it is
    // put through a per-world percentile band that promotes the best few percent and buries the rest
    // (see the consolidation note above). That is right for minerals, farmland, wind and sun: there is
    // no absolute scale on which a mineral score of 0.62 means a specific thing.
    //
    // Geothermal is different, and it became different when it merged with the plate map. Its numbers
    // are SPECIFIED: a plate line reads 40, the radiated band around a high-activity fault bottoms out
    // at 70, a volcanic vent is 97 or above. Those are the values that decide where mountains rise,
    // where quakes do damage and where volcanoes are — four systems agreeing on one field. Running them
    // through a percentile remap would rescale every one of them to a distribution, so a quiet world's
    // best plate line would be promoted to 100 and a genuinely volcanic world's vents would be
    // compressed, and the number on the readout would stop meaning the thing every other system
    // computed from it.
    //
    // HYDRO AND SOLAR JOINED IT. Both are now specified the same way — Hydro by distance from the water
    // (100 on it, 90s beside it, falling to 40 six tiles out), Solar by the star, the air and the height
    // of the ground — so both are read straight through for the same reason.
    static bool IsAbsolute(SurfaceIndexKind k)
        => k == SurfaceIndexKind.Geothermal || k == SurfaceIndexKind.Water || k == SurfaceIndexKind.Solar;

    /// What the game actually reads: the raw score, consolidated into this world's usable band.
    public static float Get(CelestialBody b, SurfaceIndexKind kind, int x, int y)
    {
        if (b?.surface == null || kind == SurfaceIndexKind.None) return 0f;
        if (x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) return 0f;

        if (IsAbsolute(kind)) return Mathf.Clamp01(Raw(b, kind, x, y));

        var fld = FieldFor(b, kind);
        if (fld == null || fld.rawMax <= 0.0001f) return 0f;

        float raw = Raw(b, kind, x, y);

        // A world whose ceiling never reaches the floor has no usable ground for this index at all. Its
        // numbers are still ORDERED and still honest — an airless world's windiest ridge really is its
        // windiest ridge, and reads maybe 28% — because the survey readout has to be able to say "this
        // world tops out at 28%, that is why nothing is highlighted" rather than showing a blank map with
        // no explanation on it.
        float floor = Floor(kind);
        if (fld.ceiling <= floor)
            return fld.ceiling * Mathf.InverseLerp(fld.rawMin, fld.rawMax, raw);

        if (raw >= fld.cutoff)
        {
            // The CURVE decides how the band is filled, which is a separate question from how high it
            // goes. Above 1 the top of the band is thin — most usable ground sits near the floor and the
            // 90s are a find; below 1 the band is top-heavy, which is what a Fertile World or a world of
            // High Quality Minerals is.
            float q = Mathf.Pow(Mathf.InverseLerp(fld.cutoff, fld.rawMax, raw), fld.curve);
            return Mathf.Lerp(floor, fld.ceiling, q);
        }

        // Under the cutoff. Compressed below the floor, order preserved, so the readout can still tell
        // you how far off a tile is instead of flatly reporting nothing.
        return (floor - 0.01f) * Mathf.InverseLerp(fld.rawMin, fld.cutoff, raw);
    }

    /// What this tile actually YIELDS, which is zero under the index's floor — no resource at all,
    /// rather than a small one.
    public static float Productive(SurfaceIndexKind k, float indexValue) => indexValue < Floor(k) ? 0f : indexValue;

    /// Which 10% band a value sits in, 0 (the floor) .. 1 (the top band). Only meaningful at or above
    /// the floor.
    public static float Band(SurfaceIndexKind k, float v)
    {
        float floor = Floor(k);
        if (v < floor) return 0f;
        int steps = Steps(k);
        int i = Mathf.Clamp(Mathf.FloorToInt((v - floor) / BandStep + 0.0001f), 0, steps - 1);
        return steps > 1 ? i / (float)(steps - 1) : 1f;
    }

    // ============================================================================================
    // THE PER-WORLD BAND
    //
    // One scan of the world per index, cached exactly like the water field and the stats below, and for
    // the same reasons: it costs nothing to save, it survives a reload, and a world re-rolled from the
    // same seed reads identically.
    // ============================================================================================
    class IndexBand
    {
        public float rawMin, rawMax;   // this world's own range for this index
        public float cutoff;           // the raw value at which the usable band begins
        public float ceiling;          // how high that band may climb, 0..1
        public float curve = 1f;       // how the band fills — see BandCurve. Cached with the rest so the
                                       // four can never describe two different versions of the world
    }

    static readonly Dictionary<(CelestialBody, SurfaceIndexKind), IndexBand> bands
        = new Dictionary<(CelestialBody, SurfaceIndexKind), IndexBand>();

    static IndexBand FieldFor(CelestialBody b, SurfaceIndexKind k)
    {
        var key = (b, k);
        if (bands.TryGetValue(key, out var f)) return f;

        int w = b.surface.width, h = b.surface.height;
        var vals = new float[w * h];
        int i = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                vals[i++] = Raw(b, k, x, y);

        System.Array.Sort(vals);

        float coverage = Mathf.Clamp(Coverage(b, k), 0.005f, 0.9f);
        int idx = Mathf.Clamp(Mathf.FloorToInt((1f - coverage) * (vals.Length - 1)), 0, vals.Length - 1);

        f = new IndexBand
        {
            rawMin = vals[0],
            rawMax = vals[vals.Length - 1],
            cutoff = vals[idx],
            ceiling = Ceiling(b, k, vals[vals.Length - 1]),
            curve = BandCurve(b, k)
        };

        // A world where the cutoff and the maximum coincide — a plateau, or a mostly-flat field — would
        // divide by zero in the remap and paint the whole band at the floor. Nudge the cutoff below the
        // max so the band has somewhere to climb.
        if (f.cutoff >= f.rawMax) f.cutoff = f.rawMax * 0.999f - 0.0001f;

        bands[key] = f;
        return f;
    }

    // ---- COVERAGE: how much of a world an index is allowed to claim ----------------------------
    //
    // Deliberately small. These are the numbers that decide whether a resource is a place you go to or a
    // property of the ground you happen to be standing on, and the whole point of the change is the
    // former. Solar and Weather take theirs from the atmosphere instead — see the two functions below.
    const float MineralCoverage = 0.12f;
    // Geothermal has no coverage budget of its own — it is read absolutely (see IsAbsolute), so the
    // consolidation never asks. Kept so the switch below is total rather than falling through to a
    // default that would silently start meaning something.
    const float GeothermalCoverage = 0.09f;
    // Wider than it was, because the floor dropped from 70 to 40: the same coverage would now put a
    // world's whole farm belt in its 40s. The CURVE (BandCurve) is what keeps the 90s rare.
    const float FertileCoverage = 0.22f;
    /// ...and on a Fertile World, a great deal more of it.
    const float FertileWorldCoverage = 0.45f;
    /// Vast Mineral Deposits: mineral country over a third of the world instead of an eighth.
    const float VastMineralCoverage = 0.35f;
    const float WaterCoverage   = 0.15f;

    static float Coverage(CelestialBody b, SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral:
                return WorldModifiers.Has(b, WorldModifier.VastMineralDeposits) ? VastMineralCoverage : MineralCoverage;
            case SurfaceIndexKind.Geothermal: return GeothermalCoverage;
            case SurfaceIndexKind.Fertile:
                return WorldModifiers.Has(b, WorldModifier.FertileWorld) ? FertileWorldCoverage : FertileCoverage;
            case SurfaceIndexKind.Water: return WaterCoverage;
            case SurfaceIndexKind.Wind:
                // Extreme Weather: storms over far more of the world.
                return WorldModifiers.Has(b, WorldModifier.ExtremeWeather)
                    ? Mathf.Min(0.85f, WeatherCoverage(b) * 1.4f + 0.08f)
                    : WeatherCoverage(b);
            default: return 0.12f;
        }
    }

    // ---- CEILING: how good this world's best ground is allowed to be ---------------------------
    //
    // The half of the answer coverage cannot give. A world's best 9% is always its best 9%, and on a
    // world with no volcanism that is still cold rock — so heat's ceiling is read off how good the best
    // raw score anywhere on it actually was, and lands under the floor, and the map correctly shows
    // nothing. `dead` is the raw score at which a world has none of this at all; `good` is the raw score
    // at which it has as much as anything can.
    static float Ceiling(CelestialBody b, SurfaceIndexKind k, float rawMax)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral:
            {
                float q = Quality(rawMax, 0.30f, 0.62f);
                // High Quality Minerals: whatever seams this world has, they are near the top of the scale.
                // Only where it HAS seams worth the name — the modifier concentrates what is there, it does
                // not invent mineral country on a world with none.
                // Vast Mineral Deposits is a promise that the world is WIDELY mineable, so its band must
                // clear the floor — a badge over a world with no usable seams would be a lie. FIRST, so a
                // world carrying both modifiers then gets High Quality's lift on top: the prize world.
                if (WorldModifiers.Has(b, WorldModifier.VastMineralDeposits)) q = Mathf.Max(q, 0.82f);
                if (WorldModifiers.Has(b, WorldModifier.HighQualityMinerals) && q >= ShowFloor * 0.8f)
                    q = Mathf.Max(q, 0.97f);
                return q;
            }
            case SurfaceIndexKind.Geothermal: return Quality(rawMax, 0.28f, 0.62f);
            case SurfaceIndexKind.Fertile:
            {
                // A Fertile World reaches 100; anything else is capped by how close its climate is to the
                // 20-22 °C optimum and whether it has the water to go with it.
                if (WorldModifiers.Has(b, WorldModifier.FertileWorld)) return 1f;
                return Mathf.Min(FertileClimateCeiling(b), Quality(rawMax, 0.30f, 0.68f));
            }
            case SurfaceIndexKind.Water: return Quality(rawMax, 0.30f, 0.70f);
            case SurfaceIndexKind.Solar: return 1f;   // absolute — see Solar; never consolidated
            // Capped by the AIR before anything about the ground is considered — an airless world has no
            // weather whatever its ridges look like. Extreme Weather lifts the cap on any world that has
            // air to be violent with.
            case SurfaceIndexKind.Wind:
            {
                float air = WeatherAirCeiling(b);
                float c = Mathf.Min(air, Quality(rawMax, 0.22f, 0.60f));
                return air > 0f && WorldModifiers.Has(b, WorldModifier.ExtremeWeather) ? Mathf.Max(c, 0.97f) : c;
            }
            default: return 0f;
        }
    }

    static float Quality(float rawMax, float dead, float good)
        => Mathf.Clamp01(Mathf.InverseLerp(dead, good, rawMax));

    /// How the usable band fills between floor and ceiling — see Get.
    static float BandCurve(CelestialBody b, SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral:
                return WorldModifiers.Has(b, WorldModifier.HighQualityMinerals) ? 0.55f : 1f;
            // Ordinary farmland is mostly middling: a plant biome no longer means 90%+. On a Fertile World
            // the band turns top-heavy, and the 90s are what most of its farmland reads.
            case SurfaceIndexKind.Fertile:
                return WorldModifiers.Has(b, WorldModifier.FertileWorld) ? 0.25f : 1.8f;   // ~half its farmland 90+ vs ~10% (tools/index-rules-check.mjs)
            case SurfaceIndexKind.Wind:
                return WorldModifiers.Has(b, WorldModifier.ExtremeWeather) ? 0.7f : 1f;
            default: return 1f;
        }
    }

    /// How high a world's fertility can go at all, from its climate. The closer its average temperature
    /// to 20-22 °C the higher the cap; 40% or more surface water adds to it. A world 25 °C off the
    /// optimum still has farmland — it just never gets past the 50s.
    static float FertileClimateCeiling(CelestialBody b)
    {
        float c = PlanetTemperature.BodyAverageCelsius(b);
        float off = Mathf.Max(0f, Mathf.Abs(c - 21f) - 1f);
        float temp = 1f - Mathf.Clamp01(off / 25f);
        float ceiling = Mathf.Lerp(0.55f, 0.88f, temp);
        if (WorldModifiers.WaterLevel(b) >= WorldModifiers.FertileWaterMin) ceiling += 0.06f;
        return Mathf.Clamp01(ceiling);
    }

    // ---- MINERAL: where a mine pays ----
    // Ore comes up where the crust is broken and raised. A real deposit on the tile beats any of it.
    static float Mineral(PlanetTerrainGenerator.Sample f, TerrainTile t)
    {
        if (f.water) return 0.03f;                       // you can't sink a shaft into an ocean

        float v = f.ridge * 0.55f                        // broken ground exposes seams
                + f.elevation * 0.30f                    // uplift brings them within reach
                + BiomeMineral(f.terrain) * 0.35f;

        if (t != null && t.HasOre) v = Mathf.Max(v, 0.6f + t.oreRichness * 0.4f);
        return Mathf.Clamp01(v);
    }

    /// How much ore a biome is worth before elevation and broken ground are folded in.
    ///
    /// EVERY terrain is listed. It used to name twelve of forty-one and let the rest fall to 0.12,
    /// which is not a default so much as a silence: a salt flat, a lava field and a jungle all
    /// yielded the same, and nothing in the game could tell you why. A terrain nobody scored is a
    /// terrain the player cannot reason about.
    static float BiomeMineral(TerrainType t)
    {
        switch (t)
        {
            // ---- the ore terrains ----
            case TerrainType.MetallicCrust: return 1.0f;
            case TerrainType.CrystalField: return 0.95f;
            case TerrainType.Mountains: return 0.8f;
            case TerrainType.Canyon: case TerrainType.Badlands: return 0.65f;
            case TerrainType.Highlands: case TerrainType.Crater: return 0.6f;
            case TerrainType.LavaRock: case TerrainType.ObsidianFlat: return 0.55f;
            // Evaporites. A dry lake bed is one of the richest mineral surfaces there is — potash,
            // borates, lithium — and it read as barren dirt purely because nobody had listed it.
            case TerrainType.SaltFlat: return 0.5f;
            case TerrainType.Volcano: return 0.5f;
            case TerrainType.CryoVolcano: return 0.4f;
            case TerrainType.Hills: return 0.45f;
            case TerrainType.MagmaField: return 0.45f;
            // Hot springs deposit their load at the surface: sulphur, cinnabar, metal salts.
            case TerrainType.GeyserField: case TerrainType.CrackedGround: return 0.4f;
            case TerrainType.Barren: case TerrainType.Wasteland: return 0.35f;
            case TerrainType.Regolith: return 0.28f;            // loose dust over the rock
            case TerrainType.AshWaste: return 0.3f;
            case TerrainType.Island: return 0.25f;              // volcanic in origin

            // ---- living ground: soil over rock, little of it reachable ----
            // Bog iron is a real ore and the only one you dig out of a wetland, so a swamp beats the
            // other soils without ever competing with a mountain.
            case TerrainType.Swamp: return 0.2f;
            case TerrainType.Reef: return 0.18f;                // carbonate
            case TerrainType.Beach: return 0.15f;               // placer deposits in the sand
            case TerrainType.Steppe: case TerrainType.Savanna: return 0.14f;
            case TerrainType.Tundra: return 0.13f;
            case TerrainType.Plains: case TerrainType.Grassland: return 0.1f;
            case TerrainType.Forest: case TerrainType.Jungle: case TerrainType.Taiga: return 0.1f;
            case TerrainType.Desert: case TerrainType.Dunes: return 0.1f;

            // ---- buried, drowned or not solid at all ----
            case TerrainType.Snow: case TerrainType.Ice: case TerrainType.Glacier: return 0.06f;
            case TerrainType.Ocean: case TerrainType.Lake: case TerrainType.River:
            case TerrainType.FrozenSea: return 0.04f;
            case TerrainType.GasClouds: case TerrainType.Storm: return 0.02f;
        }
        return 0.12f;
    }

    // ============================================================================================
    // GEOTHERMAL: the merged index — where the crust is HOT AND MOVING
    //
    // This was the Heat Index, and separately there was a plate-tectonics overlay. They were two
    // pictures of one thing. The Heat Index was "heat in the CRUST, not the air" from the day it was
    // written; the tectonics overlay drew the fault lines that heat comes out of. Keeping them apart
    // meant a world could show a red fault line running across ground the Heat Index called cold, and a
    // geothermal plant sited by one map could be wrong according to the other.
    //
    // The field itself lives in GeothermalMap and is shared with the terrain generator (which folds the
    // ground up along the same margins), the earthquakes (which only damage what stands on highlighted
    // ground) and the temperature model (a world's internal heat). Whatever this overlay paints red is
    // the ground that rose, the ground that shakes, and the ground the plant wants.
    //
    // READ STRAIGHT THROUGH, uniquely among the indexes — see IsAbsolute below. The numbers here are
    // specified in absolute terms (40 along a plate line, 70 in the radiated band, 97+ at a volcanic
    // vent), so putting them through the per-world percentile consolidation every other index uses would
    // rescale exactly the values that were chosen to mean something.
    // ============================================================================================
    static float Geothermal(CelestialBody b, PlanetTerrainGenerator.Sample f, float u, float v)
    {
        float geo = GeothermalMap.At(b, u, v);

        // ---- WHAT IS ON THE TILE IS EVIDENCE, WEIGHTED BY WHETHER THIS WORLD HAS A LIVE INTERIOR ----
        //
        // "Geothermal Index still seems to take into account the surface heat."
        //
        // It did, and this line was how. A volcano or a geyser field IS direct evidence of heat under
        // that exact spot, which is why the reading is raised rather than replaced — but CrustHeat is
        // keyed on TERRAIN TYPE, and terrain type is decided partly by how hot the tile is, and how hot
        // the tile is includes STARLIGHT. So the chain ran:
        //
        //     world orbits close to its sun  ->  every tile clears the magma threshold
        //         ->  every tile classifies as MagmaField  ->  CrustHeat returns 0.95
        //             ->  the Geothermal index reads 95 on every tile of the map
        //
        // and none of that involved the world's interior at any point. The screenshot is a volcanic
        // world reading a flat 95 from pole to pole.
        //
        // PlanetTerrainGenerator now refuses to call ground molten unless the world's internal heat
        // clears MagmaInternalMinC, which breaks the chain at its source. This is the second half:
        // surface evidence is scaled by how geothermally active the world actually is, so a volcano on a
        // world with a live core still reads ~100 and the same tile on a cold, sun-baked rock reads
        // near zero. Which is the truth — there is no heat down there to build a plant on.
        geo = Mathf.Max(geo, CrustHeat(f.terrain) * GeothermalMap.WorldIntensity(b));

        // A deep sea floor bleeds its heat into the water above it long before a plant could take any.
        // Applied last, so it cuts the finished figure rather than one of its inputs — a fault under an
        // ocean is still a fault, it is just not a building site.
        if (f.water) geo *= 0.55f;

        // ---- AND NOTHING IS FLOORED BACK UP TO KEEP A LINE VISIBLE --------------------------------
        //
        // This used to end with `if (dry >= PlateLineFloor) geo = Max(geo, PlateLineFloor)`, which
        // pushed every submerged plate margin back up to 0.40 so that the overlay — which starts
        // painting at 0.40 — would still draw something there. The plate line was being drawn out of
        // the HEAT FIELD, so the heat field had to be bent to carry it.
        //
        // It does not any more: PlanetViewWindow.PaintPlateLines reads the plate raster directly, and
        // a boundary is drawn because it is a boundary rather than because the ground under it clears
        // a threshold. So this number goes back to being only what it says it is.
        //
        // Which makes it honest again. A margin under deep ocean genuinely reads 0.22, not 0.40, and
        // the overlay now shows exactly that: the red line runs across the sea floor with cool ground
        // under it, instead of a warm smear that existed to hold the line up.
        return Mathf.Clamp01(geo);
    }

    static float CrustHeat(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Volcano: return 1.0f;
            case TerrainType.CryoVolcano: return 0.95f;     // pressure, not heat — still a vent
            case TerrainType.MagmaField: return 0.95f;
            case TerrainType.GeyserField: return 0.92f;
            case TerrainType.LavaRock: return 0.72f;
            case TerrainType.AshWaste: return 0.55f;
            case TerrainType.CrackedGround: return 0.5f;    // fissures = accessible heat
            case TerrainType.ObsidianFlat: return 0.48f;
            // Islands are volcanoes with their feet in the water — hot rock at shallow depth is the
            // reason Iceland runs on geothermal, and it was worth nothing here.
            case TerrainType.Island: return 0.4f;
            case TerrainType.Mountains: case TerrainType.Highlands: return 0.2f;   // some tectonism
            case TerrainType.Canyon: case TerrainType.Badlands: return 0.12f;      // exposed section
            case TerrainType.Hills: return 0.1f;

            // Ice sits ON crust and insulates it, so what matters is that there is no accessible heat
            // at the surface — not that the surface is cold.
            case TerrainType.Ice: case TerrainType.Glacier: case TerrainType.Snow:
            case TerrainType.FrozenSea: return 0.02f;
            case TerrainType.GasClouds: case TerrainType.Storm: return 0f;         // no crust at all
        }
        return 0.05f;
    }

    // ---- FERTILE: where farmland pays ----
    // Crops want warmth, water and flat ground — all three, which is why this multiplies rather than
    // adds. A soaking tundra and a warm desert are both useless; you need the overlap.
    static float Fertile(CelestialBody b, PlanetTerrainGenerator.Sample f)
    {
        // NO BIOSPHERE, NO FERTILITY — ZERO, not "a bit". Fertility is not a property of dirt: it is soil,
        // and soil is the product of things having lived and died in it. A sterile world has warm, flat,
        // even damp ground and nothing whatever to farm, so the index reads nothing at all until a
        // biosphere exists (Microbial Seeding is the project that starts one — see BiosphereRules).
        //
        // It used to score a dead world on warmth and flatness alone. The moisture flooring in
        // SampleNormalized held the number down on Rocky worlds, but only there and only partly, so
        // barren, ice and volcanic worlds surveyed as usefully farmable and a Rocky one still read ~25%
        // on ground where nothing could grow.
        if (b == null || !b.biosphereActive) return 0f;

        if (f.water) return 0.02f;

        // A temperate optimum: too cold OR too hot both kill it.
        float warmth = 1f - Mathf.Abs(f.temperature - 0.62f) / 0.62f;
        warmth = Mathf.Clamp01(warmth);

        float wet = Mathf.Clamp01(f.moisture * 1.25f);
        float flat = Mathf.Clamp01(1f - f.ridge * 0.9f);          // you can't plough a mountainside

        float v = warmth * 0.45f + wet * 0.35f + flat * 0.2f;
        v *= Mathf.Lerp(0.35f, 1f, BiomeFertile(f.terrain));      // the biome confirms or vetoes it
        return Mathf.Clamp01(v * 1.35f);
    }

    /// How much food a biome yields.
    ///
    /// THE BIG HOLE HERE WAS WATER. Ocean, Lake, River and Reef were all unlisted and took 0.08 —
    /// less than tundra — so an ocean world was a starvation world and a river was worth no more than
    /// the desert beside it. Every real civilisation grew up on a floodplain or a coast, and the
    /// Aquarii are literally the fertility species living on ocean worlds; the number said otherwise.
    ///
    /// Fisheries and floodplains now score at the top of the table, which is also what makes ocean
    /// worlds worth settling and gives the Aquarii somewhere to be good at their own signature.
    static float BiomeFertile(TerrainType t)
    {
        switch (t)
        {
            // ---- the breadbaskets ----
            case TerrainType.Grassland: return 1.0f;
            case TerrainType.Plains: return 0.92f;
            case TerrainType.River: return 0.9f;                // floodplain silt, renewed every year
            case TerrainType.Jungle: return 0.85f;
            case TerrainType.Forest: return 0.8f;
            case TerrainType.Reef: return 0.78f;                // the most productive water there is
            case TerrainType.Lake: return 0.72f;
            case TerrainType.Swamp: return 0.7f;
            case TerrainType.Island: return 0.6f;
            case TerrainType.Ocean: return 0.55f;               // fisheries, not farms
            case TerrainType.Savanna: case TerrainType.Steppe: return 0.5f;
            case TerrainType.Taiga: return 0.45f;
            case TerrainType.Hills: return 0.4f;
            case TerrainType.Beach: return 0.35f;

            // ---- thin living ----
            case TerrainType.Highlands: return 0.18f;
            case TerrainType.Tundra: return 0.15f;
            // Weathered volcanic ash is famously good ground once it has had time to break down —
            // the reason people farm the slopes of active volcanoes.
            case TerrainType.AshWaste: return 0.13f;
            case TerrainType.FrozenSea: return 0.12f;           // fished through the ice
            case TerrainType.GeyserField: return 0.1f;          // warm ground, mineral-poisoned
            case TerrainType.Canyon: return 0.1f;
            case TerrainType.Mountains: return 0.08f;
            case TerrainType.Snow: return 0.06f;

            // ---- effectively dead ----
            case TerrainType.Desert: case TerrainType.Dunes: case TerrainType.SaltFlat: return 0.05f;
            case TerrainType.Crater: case TerrainType.CrackedGround: return 0.05f;
            case TerrainType.Badlands: case TerrainType.Wasteland: case TerrainType.Barren: return 0.04f;
            case TerrainType.Regolith: return 0.03f;
            case TerrainType.LavaRock: case TerrainType.ObsidianFlat: return 0.02f;
            case TerrainType.CrystalField: case TerrainType.MetallicCrust: return 0.02f;
            case TerrainType.Ice: case TerrainType.Glacier: return 0.01f;
            case TerrainType.Volcano: case TerrainType.MagmaField: case TerrainType.CryoVolcano: return 0.01f;
            case TerrainType.GasClouds: case TerrainType.Storm: return 0f;
        }
        return 0.08f;
    }

    // ============================================================================================
    // WEATHER: where turbines pay — STORM ZONES, with a reason for being where they are
    //
    // Formerly the Wind Index. The air sets how high it can go and how much of the world it can claim
    // (WeatherAirCeiling / WeatherCoverage, below), exactly as before. What changed is WHERE the patches
    // land.
    //
    // They used to be blobs of noise: patches of windy country with no cause, which read as random
    // because they were. Now they are driven by the things that actually make weather:
    //
    //   THE EQUATOR. A world's rotation pools its air and its heat there; that is where the convection
    //   is and where the storms are born. Most weather gravitates to the equatorial belt.
    //   ALTITUDE DIFFERENCE. Air forced up a mountain front, or spilling down off a plateau, is wind.
    //   TEMPERATURE DIFFERENCE. Land and sea heat at different rates, so a coastline is a permanent
    //   thermal contrast — the sea breeze, scaled up to a planet.
    //
    // The two contrasts are measured per tile, smoothed into STORM ZONES a few tiles across (bigger in
    // thicker air, bigger again on an Extreme Weather world), and weighted by the equatorial belt. The
    // terrain terms then decide which ground inside a zone is buildable — flat and open still beats a
    // sheltered valley.
    // ============================================================================================
    static float Wind(CelestialBody b, PlanetTerrainGenerator.Sample f, int x, int y)
    {
        if (WeatherAirCeiling(b) <= 0f) return 0f;

        float flat = Mathf.Clamp01(1f - f.ridge * 1.15f);          // a turbine wants a plain, not a peak
        float open = 1f - Shelter(f.terrain);                      // and nothing upwind of it
        float exposure = f.elevation * 0.18f;                      // a little height still helps

        float terrain = Mathf.Clamp01((flat * 0.45f + open * 0.35f + exposure) * 1.15f);

        // Open water still scores — a coast is genuinely the windiest ground there is — but at well under
        // half, because nothing can be built on it and a hotspot spent out at sea is a hotspot wasted.
        if (f.water) terrain *= 0.45f;

        var storm = StormFieldFor(b);
        float s = storm == null ? 0f : storm[y * b.surface.width + x];
        return Mathf.Clamp01(terrain * (1f - StormWeight) + s * StormWeight);
    }

    /// How much of a tile's raw score is the storm field rather than the ground under it. Mostly storm:
    /// the storm decides where the weather IS, the ground only which part of it you can build on.
    const float StormWeight = 0.65f;

    static readonly Dictionary<CelestialBody, float[]> stormFields = new Dictionary<CelestialBody, float[]>();

    static float[] StormFieldFor(CelestialBody b)
    {
        if (b?.surface == null) return null;
        if (stormFields.TryGetValue(b, out var f)) return f;
        f = BuildStormField(b);
        stormFields[b] = f;
        return f;
    }

    /// The storm field, 0..1 per tile. Read off STORED tile data (elevation and wetness), so it costs one
    /// pass over the grid and no noise sampling at all.
    static float[] BuildStormField(CelestialBody b)
    {
        int w = b.surface.width, h = b.surface.height, n = w * h;
        var relief = new float[n];
        var coast = new float[n];
        float reliefMax = 0f;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = b.surface.tiles[x, y];
                if (t == null) continue;
                bool wet = PlanetTerrainGenerator.IsWater(t.type);
                float diff = 0f;
                bool shore = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int ny = y + dy;
                        if (ny < 0 || ny >= h) continue;                 // latitude does not wrap
                        int nx = ((x + dx) % w + w) % w;                 // longitude does
                        var o = b.surface.tiles[nx, ny];
                        if (o == null) continue;
                        diff = Mathf.Max(diff, Mathf.Abs(o.elevation - t.elevation));
                        if (PlanetTerrainGenerator.IsWater(o.type) != wet) shore = true;
                    }
                int i = y * w + x;
                relief[i] = diff;
                coast[i] = shore ? 1f : 0f;
                reliefMax = Mathf.Max(reliefMax, diff);
            }

        // Altitude contrast and land/sea contrast, each on its own 0..1 scale so neither drowns the other.
        var contrast = new float[n];
        float inv = reliefMax > 0.0001f ? 1f / reliefMax : 0f;
        for (int i = 0; i < n; i++) contrast[i] = relief[i] * inv * 0.6f + coast[i] * 0.4f;

        // Smoothed into ZONES. A raw contrast map is a hairline along every coast and ridge; a storm is a
        // region. Thicker air makes bigger systems, and Extreme Weather bigger still.
        int radius = Mathf.RoundToInt(Mathf.Lerp(2f, 4f, Mathf.InverseLerp(0.5f, 4f, b.atmospheres)));
        if (WorldModifiers.Has(b, WorldModifier.ExtremeWeather)) radius += 2;
        radius = Mathf.Clamp(radius, 1, Mathf.Max(1, h / 6));
        BoxBlur(contrast, w, h, radius);
        BoxBlur(contrast, w, h, radius);

        float cmax = 0f;
        for (int i = 0; i < n; i++) cmax = Mathf.Max(cmax, contrast[i]);
        float cinv = cmax > 0.0001f ? 1f / cmax : 0f;

        var field = new float[n];
        float eqExp = EquatorialExponent(b);
        for (int y = 0; y < h; y++)
        {
            // 0 at the equator, 1 at the poles. The belt is broad — the tropics, not a line — and broader
            // still under thick air.
            float lat = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
            float equatorial = Mathf.Pow(1f - lat, eqExp);
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                field[i] = equatorial * (0.4f + 0.6f * contrast[i] * cinv);
            }
        }
        return field;
    }

    /// Separable box blur, in place. Longitude wraps, latitude clamps — the map's own topology.
    static void BoxBlur(float[] a, int w, int h, int r)
    {
        if (r <= 0) return;
        var tmp = new float[a.Length];
        float norm = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += a[y * w + ((x + k) % w + w) % w];
                tmp[y * w + x] = sum * norm;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                a[y * w + x] = sum * norm;
            }
    }

    // ---- What the air does to the weather -------------------------------------------------------

    /// Air below this and there is nothing to move at all.
    const float TraceAir = 0.15f;

    /// The highest the Weather index may read on this world.
    ///
    /// Calibrated against the request's own two anchors: "if there is little to no atmosphere the weather
    /// index might only get to 30% at most and therefore will not be counted", and a Terran-default world
    /// should still get real hotspots.
    ///
    /// A POWER CURVE below Earth-normal, not a straight line. Linear from the trace floor put a
    /// 0.3-atmosphere world at 20% and did not cross the usable floor until well past 0.7 — so the whole
    /// thin half of the range was flat, featureless nothing, and the pressure at which weather becomes
    /// harvestable sat in an arbitrary place. The 0.55 exponent lands the anchors where they were asked
    /// for: 0.3 atm reads about 35%, the floor is crossed around 0.68 — just under the pressure at which
    /// a world can hold liquid water at all, which is a satisfying place for it — and one Earth
    /// atmosphere reaches 92%, comfortably inside the band with room above for the thicker worlds, which
    /// are worse to live on and better to harvest.
    public static float WeatherAirCeiling(CelestialBody b)
    {
        if (b == null || b.atmospheres <= TraceAir) return 0f;
        float a = b.atmospheres;
        if (a < 1f) return 0.92f * Mathf.Pow(Mathf.InverseLerp(TraceAir, 1f, a), 0.55f);
        return Mathf.Lerp(0.92f, 1f, Mathf.Clamp01(Mathf.InverseLerp(1f, 4f, a)));
    }

    /// How much of a world is windy enough to matter. Climbs hard with pressure and saturates around six
    /// atmospheres, past which more air does not make the storms meaningfully worse — it is already as
    /// bad as a turbine can survive.
    ///
    /// RAISED 2026-10-09: a 2.8-atmosphere world drew its weather belt over about a fifth of the surface,
    /// and the request asked for about three fifths at that pressure. The curve now reaches 0.60 at three
    /// atmospheres (2.8 reads ~0.56) and tops out at 0.72; an Earth-normal world is ~0.18.
    static float WeatherCoverage(CelestialBody b)
    {
        if (b == null) return 0f;
        float a = b.atmospheres;
        if (a < 1f) return Mathf.Lerp(0.02f, 0.18f, Mathf.InverseLerp(TraceAir, 1f, a));
        if (a < 3f) return Mathf.Lerp(0.18f, 0.60f, Mathf.InverseLerp(1f, 3f, a));
        return Mathf.Lerp(0.60f, 0.72f, Mathf.Clamp01(Mathf.InverseLerp(3f, 6f, a)));
    }

    /// How sharply the storm belt is pinned to the equator. Thin air keeps weather in the tropics; thick
    /// air spreads it toward the poles, so a wide coverage is a wide BELT rather than the same narrow
    /// belt with scattered patches bolted on.
    static float EquatorialExponent(CelestialBody b)
        => Mathf.Lerp(1.5f, 0.6f, Mathf.Clamp01(Mathf.InverseLerp(1f, 4f, b != null ? b.atmospheres : 0f)));

    /// How much weather this world has AT ALL, 0..1 — kept as the one number the readouts quote.
    public static float WeatherSeverity(CelestialBody b) => WeatherAirCeiling(b);

    /// Plain-language severity, for the Survey status line. It now says outright when a world's ceiling
    /// is under the usable floor, because "near-calm" was being read as "a poor site" when it meant
    /// "there is no site here and there never will be".
    public static string WeatherLabel(CelestialBody b)
    {
        float s = WeatherAirCeiling(b);
        if (s <= 0f) return "airless — no weather at all";
        if (s < Floor(SurfaceIndexKind.Wind)) return $"too thin to harvest — tops out near {s * 100f:F0}%";
        if (s < 0.85f) return "breezy — workable hotspots";
        if (s < 0.95f) return "stormy";
        return "violent";
    }

    /// How much a biome BLOCKS the wind. 1 is fully sheltered and useless for turbines; 0 is bare
    /// exposure and the best ground there is.
    ///
    /// Counter-intuitive on purpose, and it is why mountains score zero: this feeds the WIND index,
    /// so "sheltered" is a penalty. Most of the map used to sit on a flat 0.3, which flattened the
    /// one decision wind farms offer — a windswept ridge and the forest below it paid the same.
    static float Shelter(TerrainType t)
    {
        switch (t)
        {
            // ---- blocked ----
            case TerrainType.Jungle: case TerrainType.Forest: case TerrainType.Taiga: return 0.85f;
            case TerrainType.Canyon: return 0.7f;
            case TerrainType.Crater: return 0.65f;              // the rim does the sheltering
            case TerrainType.Swamp: return 0.6f;
            case TerrainType.Hills: return 0.5f;
            case TerrainType.River: return 0.45f;               // down in its own valley
            case TerrainType.CrackedGround: return 0.4f;
            case TerrainType.CrystalField: return 0.35f;
            case TerrainType.GeyserField: return 0.3f;         // low mounds, not much cover

            // ---- open ----
            case TerrainType.Island: return 0.25f;
            case TerrainType.Beach: return 0.2f;
            case TerrainType.MetallicCrust: case TerrainType.LavaRock: case TerrainType.Volcano:
            case TerrainType.CryoVolcano: return 0.2f;
            case TerrainType.Plains: case TerrainType.Grassland:
            case TerrainType.Savanna: case TerrainType.Steppe: return 0.15f;
            case TerrainType.AshWaste: case TerrainType.MagmaField: return 0.15f;
            case TerrainType.Desert: case TerrainType.Dunes: case TerrainType.SaltFlat:
            case TerrainType.Badlands: case TerrainType.Wasteland: case TerrainType.Barren:
            case TerrainType.Regolith: return 0.1f;
            case TerrainType.Tundra: case TerrainType.Snow: case TerrainType.Glacier:
            case TerrainType.Ice: case TerrainType.ObsidianFlat: return 0.1f;

            // ---- nothing to hide behind at all ----
            case TerrainType.Ocean: case TerrainType.Lake: case TerrainType.FrozenSea: return 0.05f;
            case TerrainType.Reef: return 0.08f;
            case TerrainType.Mountains: case TerrainType.Highlands: return 0f;
            case TerrainType.GasClouds: case TerrainType.Storm: return 0f;
        }
        return 0.3f;
    }

    // ============================================================================================
    // SOLAR: back to the fundamentals — the star, the air, and the height of the ground
    //
    // Solar is no longer a map of hotspots. Sunlight falls on the WHOLE surface of a turning world, so
    // the whole surface carries a Solar Index, and three things decide what it reads:
    //
    //   1. THE STAR. Distance sets the world's maximum. Up to 100% at the innermost orbit, about 55% at
    //      the centre of the habitable zone, the mid-40s at its outer edge, and lower still beyond it.
    //      (The habitable zone here is the liquid-water zone, not any one species' preference.)
    //   2. THE AIR. Every atmosphere takes 10 points off that maximum, so a 46% world under one
    //      atmosphere reads 36% at sea level. Airless worlds keep all of it — which is what makes a bare
    //      rock close to its star a genuine solar prize.
    //   3. THE GROUND. Every 1,500 m above the datum adds 10 points, every 1,500 m below takes 10 off.
    //      Mountains and high plateaus are the panel sites; craters and basins are not. Tiles under water
    //      read at the sea surface (0 m), since a panel there would float rather than sit on the seabed.
    //
    // A TIDALLY LOCKED world is the exception to "the whole surface": its substellar hemisphere (the
    // middle of the map) has the sun forever, and the far hemisphere has none at all.
    //
    // Read ABSOLUTELY, like Geothermal and Hydro — the numbers are specified, not relative.
    // ============================================================================================
    static float Solar(CelestialBody b, int x, int y)
    {
        var t = b.surface.tiles[x, y];
        if (t == null) return 0f;

        // Proportional, not stepped: 750 m is half a step. Stepping would put a hard 10-point cliff at
        // every 1,500 m contour; proportional reads the same at the steps and smoothly between them.
        float metres = PlanetTerrainGenerator.IsWater(t.type) ? 0f : PlanetTerrainGenerator.ElevationMetres(b, t.elevation);
        float v = SolarSurfaceMax(b) + SolarPerStep * (metres / SolarElevationStep);

        if (b.tidallyLocked) v *= SolarDaySide(b, x, y);
        return Mathf.Clamp01(v);
    }

    /// Metres of elevation per step, and what one step is worth.
    public const float SolarElevationStep = 1500f;
    public const float SolarPerStep = 0.10f;

    /// What each atmosphere costs the maximum.
    public const float SolarLossPerAtmosphere = 0.10f;

    // The distance curve's anchors, as multiples of the star's reference distance (PlacementRings'
    // innermost ring, the habitable zone's centre and its outer edge).
    const float SolarInnerRel = 0.36f;
    const float SolarInnerMax = 1.00f;
    const float SolarHzCentreMax = 0.55f;
    const float SolarHzOuterMax = 0.46f;
    /// How fast the maximum keeps falling beyond the habitable zone.
    const float SolarOuterFalloff = 1.2f;

    /// The maximum Solar Index this world's ORBIT allows, before its air or any terrain — 0..1.
    public static float SolarRegionMax(CelestialBody b)
    {
        if (b == null) return 0f;
        if (b.hostStar != null && b.hostStar.isBlackHole) return 0f;

        float rel = WorldClassifier.RelOf(b);
        float hzC = (StarDatabase.HzInnerRel + StarDatabase.HzOuterRel) * 0.5f;
        float hzO = StarDatabase.HzOuterRel;

        if (rel <= SolarInnerRel) return SolarInnerMax;
        if (rel <= hzC)
        {
            // Logarithmic in distance, so the fall is even per ring rather than all at the inner end.
            float t = Mathf.InverseLerp(Mathf.Log(SolarInnerRel), Mathf.Log(hzC), Mathf.Log(rel));
            return Mathf.Lerp(SolarInnerMax, SolarHzCentreMax, t);
        }
        if (rel <= hzO) return Mathf.Lerp(SolarHzCentreMax, SolarHzOuterMax, Mathf.InverseLerp(hzC, hzO, rel));
        return SolarHzOuterMax * Mathf.Pow(hzO / rel, SolarOuterFalloff);
    }

    /// What the atmosphere takes off the maximum: 10 points per atmosphere.
    public static float SolarAirLoss(CelestialBody b)
        => b == null ? 0f : Mathf.Max(0f, b.atmospheres) * SolarLossPerAtmosphere;

    /// The Solar Index of flat ground at the datum on this world: the orbit's maximum less the air.
    public static float SolarSurfaceMax(CelestialBody b)
        => Mathf.Max(0f, SolarRegionMax(b) - SolarAirLoss(b));

    /// 0..1 how much sun a tile of a TIDALLY LOCKED world gets: full across the day hemisphere, centred
    /// on the middle of the map (the face OrbitController keeps turned to the star), fading over the last
    /// few degrees to the terminator, and nothing at all on the night side.
    public static float SolarDaySide(CelestialBody b, int x, int y)
    {
        float u = (x + 0.5f) / Mathf.Max(1, b.surface.width);
        float v = (y + 0.5f) / Mathf.Max(1, b.surface.height);
        float lon = (u - 0.5f) * 2f * Mathf.PI;
        float lat = (v - 0.5f) * Mathf.PI;
        float cosZ = Mathf.Cos(lat) * Mathf.Cos(lon);
        return Mathf.Clamp01(cosZ * 4f);
    }

    /// Is solar worth anything ANYWHERE on this world — does even its best ground clear the floor? False
    /// means panels are not offered and the index is not surveyed. Re-asked live, so thinning the air
    /// or moving the orbit brings it back.
    ///
    /// Answered from the world's HIGHEST POINT rather than from the sorted per-tile stats: this is asked by
    /// Present on every survey tick, and the stats are a full scan-and-sort that terraforming throws away
    /// every few seconds. The highest ground is the best solar ground by construction, so the answer is
    /// the same — and it stays live while an orbit migration or an atmosphere project is still running.
    /// (On a tidally locked world the peak may be on the night side; it then answers "yes" for a world
    /// whose lit half might fall just short, which only means the survey reads a map that is mostly dark.)
    public static bool SolarViable(CelestialBody b)
    {
        if (b?.surface == null) return false;
        float best = SolarSurfaceMax(b) + SolarPerStep * (HighestMetres(b) / SolarElevationStep);
        return best >= Floor(SurfaceIndexKind.Solar);
    }

    static readonly Dictionary<CelestialBody, (float seed, float metres)> highest
        = new Dictionary<CelestialBody, (float, float)>();

    /// The world's highest standing ground in metres, water reading as its surface (0 m). Cached against the
    /// terrain seed, and dropped by InvalidateStats with everything else that depends on the surface.
    static float HighestMetres(CelestialBody b)
    {
        if (highest.TryGetValue(b, out var c) && Mathf.Approximately(c.seed, b.terrainSeed)) return c.metres;
        float best = float.MinValue;
        for (int y = 0; y < b.surface.height; y++)
            for (int x = 0; x < b.surface.width; x++)
            {
                var t = b.surface.tiles[x, y];
                if (t == null) continue;
                float m = PlanetTerrainGenerator.IsWater(t.type) ? 0f : PlanetTerrainGenerator.ElevationMetres(b, t.elevation);
                if (m > best) best = m;
            }
        if (best == float.MinValue) best = 0f;
        highest[b] = (b.terrainSeed, best);
        return best;
    }

    // ============================================================================================
    // WATER (HYDRO): the water itself, and the land it reaches
    //
    // Water tiles read 100%. The index used to EXCLUDE them — it was written for buildings that need
    // water beside them, like the Steam Turbine — which left no room for anything that could one day
    // stand ON the water. Nothing may be built on water yet (that is still SurfaceBuildManager's rule,
    // and a technology to lift it is for later); the index just stops pretending the water is dry.
    //
    // From the shore it radiates SIX tiles inland and falls off steeply at the end:
    //
    //     tile 1   94-98      tile 4   60-79
    //     tile 2   90-93      tile 5   50-59
    //     tile 3   80-89      tile 6   40-49
    //
    // Where a tile sits inside its range depends on how big the body of water is — a sea supplies its
    // shore better than a pond — with a little per-tile variation so a coastline is not one flat colour.
    // Read ABSOLUTELY: these numbers are the specification.
    // ============================================================================================

    /// How far inland the Hydro Index reaches, in tiles.
    public const int HydroReach = 6;

    /// Each ring's range, nearest first.
    static readonly Vector2[] HydroRings =
    {
        new Vector2(0.94f, 0.98f), new Vector2(0.90f, 0.93f), new Vector2(0.80f, 0.89f),
        new Vector2(0.60f, 0.79f), new Vector2(0.50f, 0.59f), new Vector2(0.40f, 0.49f),
    };

    static float Water(CelestialBody b, int x, int y)
    {
        if (IsWaterAt(b, x, y)) return 1f;

        var field = WaterFieldFor(b);
        if (field == null) return 0f;

        int i = y * b.surface.width + x;
        int id = field.nearestBody[i];
        if (id < 0) return 0f;                                     // no water anywhere on this world

        // Chamfer distance rounded to whole tiles, so a diagonal neighbour (1.41) is ring 1 like an
        // orthogonal one — "grids away" the way a player counts them.
        int ring = Mathf.Max(1, Mathf.RoundToInt(field.distance[i]));
        if (ring > HydroReach) return 0f;

        var range = HydroRings[ring - 1];
        float size = Mathf.Clamp01(Mathf.Sqrt(field.bodySize[id]) / 15f);
        float q = size * 0.8f + Survey.Hash01(b, x, y) * 0.2f;
        return Mathf.Lerp(range.x, range.y, q);
    }

    // ============================================================================================
    // THE WATER DISTANCE FIELD
    //
    // For every land tile: how far it is from open water, and which BODY of water that is — because the
    // answer depends on how big that body is, and the nearest water is not always the biggest.
    //
    // A CHAMFER DISTANCE TRANSFORM rather than a per-tile search. The obvious implementation asks each
    // tile "how far to the nearest water" and scans outward, which is O(reach^2) per tile and is asked
    // for every tile of every overlay repaint and every efficiency calculation. Two sweeps over the grid
    // — one forward, one back — produce the whole field in O(w*h), and the diagonal step costing
    // sqrt(2) puts it within a couple of percent of true Euclidean distance, which is close enough that
    // no player will ever see the difference between the two.
    //
    // LONGITUDE WRAPS, so the sweeps run twice: a single forward-and-back pair cannot propagate a
    // distance the long way around the seam. Two pairs is enough for any feature narrower than the map,
    // which every water body is.
    // ============================================================================================
    class WaterField
    {
        public float[] distance;     // tiles to the nearest water, per cell
        public int[] nearestBody;    // which connected body that water belongs to, or -1
        public int[] bodySize;       // tiles in each body
    }

    // KEYED ON THE BODY OBJECT, NOT b.id. `id` is not unique across a galaxy — SolarSystemGenerator
    // restarts its counter for every system — so two worlds in different systems share one. The
    // reference is exact and collision-free (CelestialBody overrides neither Equals nor GetHashCode).
    static readonly Dictionary<CelestialBody, WaterField> waterFields
        = new Dictionary<CelestialBody, WaterField>();

    static WaterField WaterFieldFor(CelestialBody b)
    {
        if (b?.surface == null) return null;
        if (waterFields.TryGetValue(b, out var f)) return f;
        f = BuildWaterField(b);
        waterFields[b] = f;
        return f;
    }

    static WaterField BuildWaterField(CelestialBody b)
    {
        int w = b.surface.width, h = b.surface.height;
        int n = w * h;

        var field = new WaterField
        {
            distance = new float[n],
            nearestBody = new int[n]
        };

        // ---- Label the connected bodies ----
        var label = new int[n];
        for (int i = 0; i < n; i++) label[i] = -1;
        var sizes = new List<int>();
        var stack = new Stack<int>();

        for (int start = 0; start < n; start++)
        {
            if (label[start] >= 0) continue;
            if (!IsWaterAt(b, start % w, start / w)) continue;

            int id = sizes.Count;
            int count = 0;
            label[start] = id;
            stack.Push(start);

            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                count++;
                int cx = cur % w, cy = cur / w;

                // Orthogonal only, and longitude wraps — the same connectivity every other rule in the
                // project uses, so "one body of water" means the same thing here as everywhere else.
                PushWater(b, label, stack, id, cx + 1, cy);
                PushWater(b, label, stack, id, cx - 1, cy);
                PushWater(b, label, stack, id, cx, cy + 1);
                PushWater(b, label, stack, id, cx, cy - 1);
            }
            sizes.Add(count);
        }

        field.bodySize = sizes.ToArray();

        // ---- The distance transform ----
        for (int i = 0; i < n; i++)
        {
            bool wet = label[i] >= 0;
            field.distance[i] = wet ? 0f : float.MaxValue;
            field.nearestBody[i] = wet ? label[i] : -1;
        }

        if (sizes.Count == 0)
        {
            for (int i = 0; i < n; i++) field.distance[i] = 0f;   // no water: the field means nothing
            return field;
        }

        const float D1 = 1f, D2 = 1.41421356f;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Relax(field, w, h, x, y, x - 1, y, D1);
                    Relax(field, w, h, x, y, x, y - 1, D1);
                    Relax(field, w, h, x, y, x - 1, y - 1, D2);
                    Relax(field, w, h, x, y, x + 1, y - 1, D2);
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    Relax(field, w, h, x, y, x + 1, y, D1);
                    Relax(field, w, h, x, y, x, y + 1, D1);
                    Relax(field, w, h, x, y, x + 1, y + 1, D2);
                    Relax(field, w, h, x, y, x - 1, y + 1, D2);
                }
        }

        return field;
    }

    static bool IsWaterAt(CelestialBody b, int x, int y)
    {
        var t = b.surface.tiles[x, y];
        return t != null && PlanetTerrainGenerator.IsWater(t.type);
    }

    static void PushWater(CelestialBody b, int[] label, Stack<int> stack, int id, int x, int y)
    {
        int w = b.surface.width, h = b.surface.height;
        if (y < 0 || y >= h) return;                 // latitude does not wrap; the poles are edges
        x = ((x % w) + w) % w;                       // longitude does
        int i = y * w + x;
        if (label[i] >= 0 || !IsWaterAt(b, x, y)) return;
        label[i] = id;
        stack.Push(i);
    }

    /// One step of the chamfer sweep: can (nx,ny) offer (x,y) a shorter route to water?
    static void Relax(WaterField f, int w, int h, int x, int y, int nx, int ny, float cost)
    {
        if (ny < 0 || ny >= h) return;
        nx = ((nx % w) + w) % w;
        int from = ny * w + nx, to = y * w + x;
        if (f.distance[from] == float.MaxValue) return;
        float d = f.distance[from] + cost;
        if (d < f.distance[to]) { f.distance[to] = d; f.nearestBody[to] = f.nearestBody[from]; }
    }

    // ============================================================================
    // RELATIVE RANKING — "where on THIS world is best?"
    //
    // Absolute yield alone can't answer that: on a frozen world EVERY tile is a poor geothermal site,
    // and a fixed threshold would highlight nothing at all. So the "best places" highlight is a
    // PERCENTILE of this planet's own distribution — its ten hottest tiles are its ten hottest tiles
    // whether or not they're any good in absolute terms. The yield readout tells you the hard truth
    // separately.
    // ============================================================================
    class Stats { public float[] sorted; public float min, max; }

    // KEYED ON THE BODY OBJECT, NOT b.id — the same correction the water field above carries, and for
    // the same reason. `id` restarts at 0 for every system SolarSystemGenerator makes, so the third
    // world of system 1 and the third world of system 7 shared a cache entry: whichever was surveyed
    // first decided what "the best ground on this world" meant for both of them, and the second world's
    // overlays highlighted tiles chosen from a distribution belonging to a planet in another star
    // system. PowerGrid hit this exact bug and documents it at length.
    static readonly Dictionary<(CelestialBody, SurfaceIndexKind), Stats> statsCache
        = new Dictionary<(CelestialBody, SurfaceIndexKind), Stats>();

    static Stats GetStats(CelestialBody b, SurfaceIndexKind k)
    {
        var key = (b, k);
        if (statsCache.TryGetValue(key, out var s)) return s;

        int w = b.surface.width, h = b.surface.height;
        var vals = new float[w * h];
        int i = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                vals[i++] = Get(b, k, x, y);

        System.Array.Sort(vals);
        s = new Stats { sorted = vals, min = vals[0], max = vals[vals.Length - 1] };
        statsCache[key] = s;
        return s;
    }

    /// Drop cached distributions for a world — call when its terrain actually changes (terraforming,
    /// planetary remodelling), or the overlays would describe the world it used to be.
    ///
    /// The water field goes with them. It is derived from which tiles are wet, and terraforming is
    /// precisely the thing that floods and drains them — a stale field would keep supplying hydro to a
    /// desert that used to be a coast, and deny it to a sea that has just appeared.
    /// The consolidation bands go too, and they are the ones that matter most here. A band holds the
    /// world's raw distribution and the cutoff drawn from it, so terraforming a desert into a coast
    /// without dropping it would leave every hydro site on the world decided by the desert's numbers —
    /// the map would keep highlighting the driest ground on a world that now has a sea in it.
    public static void InvalidateStats(CelestialBody b)
    {
        if (b == null) return;
        foreach (var k in All) { statsCache.Remove((b, k)); bands.Remove((b, k)); }
        waterFields.Remove(b);
        // The storm zones are built from which tiles are wet and how high they stand — both things a
        // terraform changes.
        stormFields.Remove(b);
        highest.Remove(b);
        // ...and whether this world has any water on it at all, which decides whether the Hydro Index is
        // OFFERED (see Present). Terraforming water onto a dry world has to make the index appear, and
        // it appears the moment this entry goes.
        waterPresence.Remove(b);
        // The geothermal field caches a world's plume intensity and plate motion, and both are keyed on
        // its terrain seed and type — exactly the two things a remodel or a reseed changes. Dropped here
        // so the overlay, the earthquakes and the temperature model all stop describing the world this
        // one used to be at the same moment the other overlays do.
        GeothermalMap.Invalidate(b);

        // A gas giant's great spots are memoised against its seed too, and the memo is a single entry —
        // so a reseed that happens to leave the seed comparing equal (a resize) would keep the old
        // spots. Dropped here alongside everything else keyed on the same two facts.
        GasGiantStorms.Invalidate();
    }

    public static void InvalidateAll()
    {
        statsCache.Clear();
        waterFields.Clear();
        stormFields.Clear();
        highest.Clear();
        waterPresence.Clear();
        bands.Clear();
        GeothermalMap.InvalidateAll();
        GasGiantStorms.Invalidate();
    }

    /// Where this tile ranks on this world, 0 (worst) .. 1 (best).
    public static float Percentile(CelestialBody b, SurfaceIndexKind k, int x, int y)
    {
        if (b?.surface == null || k == SurfaceIndexKind.None) return 0f;
        var s = GetStats(b, k);
        float v = Get(b, k, x, y);
        int lo = 0, hi = s.sorted.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (s.sorted[mid] < v) lo = mid + 1; else hi = mid; }
        return s.sorted.Length > 1 ? lo / (float)(s.sorted.Length - 1) : 1f;
    }

    /// The value at which the top `fraction` of this world's tiles begins (0.1 = the best 10%).
    public static float TopFractionThreshold(CelestialBody b, SurfaceIndexKind k, float fraction)
    {
        if (b?.surface == null || k == SurfaceIndexKind.None) return 0f;
        var s = GetStats(b, k);
        int idx = Mathf.Clamp(Mathf.FloorToInt((1f - fraction) * (s.sorted.Length - 1)), 0, s.sorted.Length - 1);
        return s.sorted[idx];
    }

    /// Is this tile in the best `fraction` of this world for this index?
    public static bool IsTopFraction(CelestialBody b, SurfaceIndexKind k, int x, int y, float fraction)
    {
        if (k == SurfaceIndexKind.None || b?.surface == null) return false;

        // AN INDEX THAT IS ZERO EVERYWHERE HAS NO BEST TILES — the best 10% of nothing is nothing.
        //
        // Without this, the threshold on such a world is itself 0 and the `>=` below is true for every
        // tile, so the best-sites overlay lights up the entire map. Fertile on a world with no biosphere
        // is exactly that case now that it reads a flat zero: the index correctly says "you cannot farm
        // here" while the overlay said "farm anywhere".
        if (Best(b, k) <= 0f) return false;

        return Get(b, k, x, y) >= TopFractionThreshold(b, k, fraction);
    }

    public static float Best(CelestialBody b, SurfaceIndexKind k)
        => b?.surface == null || k == SurfaceIndexKind.None ? 0f : GetStats(b, k).max;

    // ============================================================================================
    // WHAT AN OVERLAY ACTUALLY DRAWS — one line now, because Get already did the work
    //
    // This used to carry the whole consolidation rule: a relative top quarter, an absolute 50% floor, a
    // median cut, and two bands of brightness fitted between them. All of that has moved into Get, where
    // it belongs — a tile's value now IS its usability, so the drawing rule is simply "is it over the
    // floor", and there is no longer any way for the map, the yield numbers, the placement gate and the
    // cursor readout to disagree about whether a tile counts. They ask one question and it has one answer.
    //
    // `t` is the tile's BAND rather than a continuous ramp position. Every 10% is a step, each brighter
    // than the last, so the quality distribution is legible at a glance from the map alone: you can see
    // which patch is the 90s and which is merely the 70s without zooming in to read a number. That is the
    // point of banding rather than fading — a smooth gradient over textured terrain is unreadable, and
    // three or four discrete steps are not.
    // ============================================================================================

    /// Should this tile be drawn for this index, and in which band (0 the floor .. 1 the top)?
    public static bool Shown(CelestialBody b, SurfaceIndexKind k, int x, int y, out float t)
        => ShownFor(b, k, Get(b, k, x, y), out t);

    /// As above, for a value already read — the overlay walks every tile and must not pay for Get twice
    /// (each call re-samples the terrain noise field).
    public static bool ShownFor(CelestialBody b, SurfaceIndexKind k, float v, out float t)
    {
        t = 0f;
        if (b?.surface == null || k == SurfaceIndexKind.None) return false;
        if (v < DrawFloor(k)) return false;

        // Each 10% step above the index's own floor is its own band, so a plate margin at 40 paints at
        // the dimmest end of the ramp and the volcanic 90s stay the brightest thing on the map.
        t = Band(k, v);
        return true;
    }

    // ---- Presentation ----
    public static string Name(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Mineral Index";
            case SurfaceIndexKind.Geothermal: return "Geothermal Index";
            case SurfaceIndexKind.Fertile: return "Fertile Index";
            case SurfaceIndexKind.Wind: return "Weather Index";
            case SurfaceIndexKind.Solar: return "Solar Index";
            case SurfaceIndexKind.Water: return "Hydro Index";
            default: return "None";
        }
    }

    /// The index's name without the word "Index" on the end — for refusal messages and anywhere else a
    /// sentence has to read like a sentence. "needs valid Mineral ground" beats "needs valid Mineral
    /// Index ground".
    public static string ShortName(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Mineral";
            case SurfaceIndexKind.Geothermal: return "Geothermal";
            case SurfaceIndexKind.Fertile: return "Fertile";
            case SurfaceIndexKind.Wind: return "Weather";
            case SurfaceIndexKind.Solar: return "Solar";
            case SurfaceIndexKind.Water: return "Hydro";
            default: return "";
        }
    }

    public static string Describe(SurfaceIndexKind k)
    {
        switch (k)
        {
            // ONE OR TWO SENTENCES, ALL SIX (2026-10-09). These were paragraphs explaining how the
            // generator chose each tile, and players don't read paragraphs: the colours on the map are
            // the explanation, and the reasoning lives in the code that computes them.
            case SurfaceIndexKind.Mineral: return "Rich mineral ground. Brighter means better mining.";
            case SurfaceIndexKind.Geothermal: return "Subsurface pressure: plate lines, hotspots and vents. Brighter ground powers geothermal plants better.";
            case SurfaceIndexKind.Fertile: return "Farmland. Brighter ground grows more food.";
            case SurfaceIndexKind.Wind: return "Storm zones. Brighter ground turns wind farms faster.";
            case SurfaceIndexKind.Solar: return "Sunlight reaching the ground. Brighter, higher ground suits solar arrays.";
            case SurfaceIndexKind.Water: return "Water and the land beside it. Brighter ground suits hydro plants and steam turbines.";
            default: return "";
        }
    }

    /// The hover text for the index toggle icons on the map: why the highlighted ground is worth
    /// building on, not how the generator chose it. One sentence.
    public static string Why(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Build mines here: the brightest ground holds the most ore.";
            case SurfaceIndexKind.Geothermal: return "Build geothermal plants here: the brightest ground has the most pressure to tap. Quakes hit the hottest ground.";
            case SurfaceIndexKind.Fertile: return "Build farmland here: the brightest ground grows the most food.";
            case SurfaceIndexKind.Wind: return "Build wind farms here: the brightest ground has the strongest winds.";
            case SurfaceIndexKind.Solar: return "Build solar arrays here: the brightest ground gets the most sunlight.";
            case SurfaceIndexKind.Water: return "Build hydro plants and steam turbines here: the brightest ground has the most water to hand.";
            default: return "";
        }
    }

    /// The colour ramp for each overlay. Alpha rises with the score so weak tiles fade and the good
    /// patches are what your eye lands on.
    public static Color Ramp(SurfaceIndexKind k, float t)
    {
        t = Mathf.Clamp01(t);
        Color c;
        switch (k)
        {
            // Brighter at the top than the old muddy tan, which was the one ramp whose best ground read as
            // dirt rather than as a find. Orange, and clearly not Heat's red.
            case SurfaceIndexKind.Mineral: c = Color.Lerp(new Color(0.28f, 0.16f, 0.06f), new Color(1.00f, 0.60f, 0.16f), t); break;
            case SurfaceIndexKind.Geothermal: c = Color.Lerp(new Color(0.85f, 0.45f, 0.10f), new Color(1.00f, 0.10f, 0.05f), t); break;
            case SurfaceIndexKind.Fertile: c = Color.Lerp(new Color(0.05f, 0.22f, 0.08f), new Color(0.30f, 1.00f, 0.25f), t); break;
            // PURPLE. It was a slate-to-whitish blue, which failed twice over: a pale desaturated blue
            // barely separates from the terrain underneath it, and it was near enough to Water's
            // ramp that the two overlays read as the same map. Purple is the one hue nothing else here
            // uses — Mineral is brown, Heat orange-red, Fertile green, Solar yellow, Water blue — so a
            // glance at the colour is enough to know which overlay you're looking at.
            case SurfaceIndexKind.Wind: c = Color.Lerp(new Color(0.16f, 0.05f, 0.28f), new Color(0.80f, 0.36f, 1.00f), t); break;
            case SurfaceIndexKind.Solar: c = Color.Lerp(new Color(0.40f, 0.34f, 0.10f), new Color(1.00f, 0.95f, 0.40f), t); break;
            // Saturation RISES with the score, rather than falling. This ran navy -> pale sky blue, so
            // the best ground got the weakest, most washed-out colour on the map — the ramp was reading
            // as "more index = whiter", which is the opposite of intensity. Now weak ground is a muted
            // grey-blue that sinks into the terrain and strong ground is a deep, fully saturated blue
            // that sits on top of it. Alpha (below) climbs alongside, so the two reinforce instead of
            // fighting.
            case SurfaceIndexKind.Water: c = Color.Lerp(new Color(0.34f, 0.44f, 0.56f), new Color(0.00f, 0.34f, 1.00f), t); break;
            default: return new Color(0, 0, 0, 0);
        }
        c.a = Mathf.Lerp(0.12f, 0.88f, t);
        return c;
    }

    // ============================================================================================
    // THE BANDS, AND WHY EACH ONE HAS ITS OWN EDGE
    //
    // `t` is the tile's 10% band, not a position on a continuous ramp — 70s, 80s, 90s, 100 — and each
    // step is drawn brighter and more opaque than the one below it. A continuous fade cannot be read off
    // textured terrain: you can see roughly where an index is strong, but not where one grade ends and
    // the next begins, which is exactly the question you are asking when choosing between two patches.
    // Three or four discrete steps are legible at a glance.
    //
    // AND EACH BAND IS OUTLINED IN ITS OWN COLOUR, brighter than its own fill. The old overlay drew one
    // outline around the whole highlighted region, which said where the good ground stopped but nothing
    // about its INSIDE — a 95% core and a 72% fringe were one shape with one border. Now the 90s patch
    // has its own bright edge inside the 80s patch's, so the quality distribution reads as contour lines:
    // find the innermost, brightest ring, and that is where to build. The numbers under the cursor are
    // then for confirming a choice rather than for making one.
    // ============================================================================================

    // ============================================================================================
    // HOW OPAQUE A HIGHLIGHT MAY GET, AND WHY IT IS NOW MUCH LESS
    //
    // The fills used to reach 94% — nearly solid — which was right when exactly one index could be up
    // at a time. It stopped being right the moment several could (see IndexIconBar): at 94% the last
    // overlay composited simply painted over everything under it, so a second index was not additional
    // information, it was a replacement for the first.
    //
    // 40% is the number that makes overlap MEAN something. Two washes at 40% resolve to a colour that
    // is visibly neither of them — mineral orange under fertile green reads as a distinct third thing
    // in the region where both are strong — and the terrain stays legible under all of it, which is
    // what a highlight is for. Three at once is still readable; four is a decision the player is
    // allowed to make badly.
    //
    // The OUTLINES are deliberately not reduced with the fills. They are hairlines one texel wide, they
    // carry the band boundaries, and at 40% they stop separating from the ground entirely — the thing
    // that makes a patch read as a place with an edge rather than as a smudge. So the fills got quiet
    // and the edges stayed loud, which is also what makes several overlapping indexes tellable apart:
    // the washes blend, the borders do not.
    // ============================================================================================
    public const float HighlightAlphaMax = 0.40f;

    /// Extra opacity the top band gains over HighlightAlphaMax, scaled in by band (see Highlight).
    public const float HighlightTopBoost = 0.15f;

    /// The fill for a tile the overlay has decided to draw (see Shown), in the band `t`.
    public static Color Highlight(SurfaceIndexKind k, float t)
    {
        t = Mathf.Clamp01(t);
        var c = Ramp(k, Mathf.Lerp(0.5f, 1f, t));
        // THE BEST GROUND IS THE BRIGHTEST (2026-10-09). The fill moves toward its band's own OUTLINE
        // colour as the band rises, and the upper bands gain up to 15
        // points of opacity over the base ceiling — so from map zoom the highest-quality patch is the
        // brightest, most solid thing on the overlay rather than a wash the same weight as the fringe.
        // Capped at 70% of the way, so even the top band's fill stays a shade under its own outline and
        // the edge still separates from the ground it rings.
        var edge = Outline(k, t);
        c = Color.Lerp(c, edge, t * 0.7f);
        c.a = Mathf.Lerp(HighlightAlphaMax * 0.55f, HighlightAlphaMax + HighlightTopBoost, t);
        return c;
    }

    /// The line drawn around a band of highlighted ground: the same hue, lifted past anything that band's
    /// fill can reach, and fully opaque. Every band gets one, so the edges nest.
    public static Color Outline(SurfaceIndexKind k, float t)
    {
        var c = Outline(k);
        // The lowest band's edge is already brighter than its fill; the top band's is brightest of all,
        // so a 100% patch is unmistakably the brightest thing on the map. Lifted toward white rather than
        // just made more opaque — an outline that only gains alpha stops separating from its own fill
        // once the fill is near-opaque, which is exactly what the top band is.
        return Color.Lerp(new Color(c.r * 0.82f, c.g * 0.82f, c.b * 0.82f, 1f),
                          Color.Lerp(c, Color.white, 0.35f), Mathf.Clamp01(t));
    }

    /// The index's outline colour at full strength — for legends, swatches and status text, where there
    /// is no band to speak of.
    ///
    /// This is what makes a patch READ AS A PLACE. A translucent wash over textured terrain has no edge —
    /// you can see roughly where it is strong but not where it stops, which is exactly the question when
    /// you are about to draw a footprint. An outline in the index's own colour also keeps the map legible
    /// with two overlays up: the shape is bounded in purple, so it is weather, whatever is under it.
    public static Color Outline(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return new Color(1.00f, 0.68f, 0.22f, 1f);   // bright orange
            case SurfaceIndexKind.Geothermal:    return new Color(1.00f, 0.30f, 0.20f, 1f);   // bright red
            case SurfaceIndexKind.Fertile: return new Color(0.48f, 1.00f, 0.38f, 1f);   // bright green
            case SurfaceIndexKind.Wind:    return new Color(0.88f, 0.52f, 1.00f, 1f);   // bright purple
            case SurfaceIndexKind.Solar:   return new Color(1.00f, 0.97f, 0.42f, 1f);   // bright yellow
            case SurfaceIndexKind.Water:   return new Color(0.38f, 0.76f, 1.00f, 1f);   // bright blue
            default:                       return new Color(1f, 1f, 1f, 1f);
        }
    }

    // Minerals you can see from orbit; everything else needs someone on the ground.
    // ============================================================================================
    // WHICH TIER EACH OVERLAY BELONGS TO
    //
    // The six indexes are the backbone of the research ladder, split 1 - 2 - 2 - 1. The pairing is not
    // alphabetical, it follows the DECISION each tier lets you make:
    //
    //   Survey (0)          Mineral            — should I claim this? You can see seams from orbit.
    //   Deep Research I     Heat + Fertile     — where do things GO? These two decide where a geothermal
    //                                            plant and a farm belong, so they arrive together.
    //   Deep Research II    Wind + Solar       — how do I POWER it? The power-siting pair.
    //   Deep Research III   Water              — the last one, with the late-game secrets.
    // ============================================================================================
    public static int RequiredLevel(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return 0;
            case SurfaceIndexKind.Geothermal:
            case SurfaceIndexKind.Fertile: return 1;
            case SurfaceIndexKind.Wind:
            case SurfaceIndexKind.Solar: return 2;
            case SurfaceIndexKind.Water: return 3;
            default: return 0;
        }
    }

    /// Is there anything of this index to look at yet?
    ///
    /// The gate is the LEVEL-2 SURVEY now, not an empire-tech tier. An index opens the moment a science
    /// ship starts reading it and fills in as it works — see Survey.RevealOf, which is what the overlay
    /// asks how much of it to draw. `RequiredLevel` above survives as the running ORDER (it is what
    /// SurfaceIndex.All is sorted by), not as a lock.
    public static bool Unlocked(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return false;
        if (GameMode.DevMode) return true;
        if (!b.Surveyed) return false;
        return Survey.RevealOf(b, k).started;
    }

    /// Why an overlay is locked. A greyed control that will not say what is missing is a dead end, and
    /// the answer is now always the same shape: this world has not been read that far yet, and the way
    /// to change that is a science ship.
    public static string LockReason(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return "no world selected";
        if (!b.Surveyed) return "survey this world first";
        if (Survey.RevealOf(b, k).started) return null;

        // THIS WORLD's running order and THIS WORLD's count — a dry, sterile rock surveys four indexes,
        // not six, so telling the player that Solar is "#5 of 6" when it is the fourth and last thing
        // the ship will look at is a promise about the wrong amount of waiting.
        int slot = Survey.IndexSlot(b, k);
        if (slot < 0) return $"{Name(k)} has nothing to read on this world";
        int total = Survey.PresentCount(b);

        var now = Survey.CurrentIndex(b);
        return now == SurfaceIndexKind.None
            ? $"send a research ship — it reads the indexes in order, and {Name(k)} is #{slot + 1} of {total}"
            : $"a research ship is on the {Name(now)} index; {Name(k)} is #{slot + 1} of {total}";
    }
}
