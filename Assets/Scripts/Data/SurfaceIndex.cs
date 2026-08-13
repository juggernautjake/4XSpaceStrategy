using System.Collections.Generic;
using UnityEngine;

// The map overlays you survey a world for. Each is a 0..1 score per surface tile that says how well a
// given kind of building would do there.
public enum SurfaceIndexKind { None, Mineral, Heat, Fertile, Wind, Solar, Water }

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
        SurfaceIndexKind.Mineral, SurfaceIndexKind.Heat, SurfaceIndexKind.Fertile,
        SurfaceIndexKind.Wind, SurfaceIndexKind.Solar, SurfaceIndexKind.Water
    };

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
    // The band runs from ShowFloor (70%) to the ceiling, and everything under the cutoff is compressed
    // below the floor — where it is drawn nowhere, produces nothing, and refuses placement. That is the
    // whole of "doing away with the bottom 69%".
    //
    // The cutoff is a PERCENTILE of this world's own raw distribution, so a world's own best ground is
    // always what gets promoted; the ceiling then decides whether being this world's best is worth
    // anything. Those are two genuinely different questions and both have to be asked.
    // ============================================================================================

    /// Below this an index is drawn nowhere, yields nothing, and refuses to be built on.
    public const float ShowFloor = 0.70f;

    /// The usable band is read in steps of this, so a glance at the map sorts good ground from very good
    /// ground without reading a single number. See Band / Highlight.
    public const float BandStep = 0.10f;

    /// The raw score, before consolidation: what the terrain here would be worth if every tile counted.
    /// Kept separate because the consolidation needs the whole world's distribution of these, and asking
    /// Get for it would be circular.
    public static float Raw(CelestialBody b, SurfaceIndexKind kind, int x, int y)
    {
        if (b?.surface == null || x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) return 0f;
        var f = Field(b, x, y);
        var t = b.surface.tiles[x, y];

        float u = (x + 0.5f) / Mathf.Max(1, b.surface.width);
        float v = (y + 0.5f) / Mathf.Max(1, b.surface.height);

        switch (kind)
        {
            case SurfaceIndexKind.Mineral: return Mineral(f, t);
            case SurfaceIndexKind.Heat: return Heat(b, f);
            case SurfaceIndexKind.Fertile: return Fertile(b, f);
            case SurfaceIndexKind.Wind: return Wind(b, f, u, v);
            case SurfaceIndexKind.Solar: return Solar(b, f, u, v);
            case SurfaceIndexKind.Water: return Water(b, f, x, y);
            default: return 0f;
        }
    }

    /// What the game actually reads: the raw score, consolidated into this world's usable band.
    public static float Get(CelestialBody b, SurfaceIndexKind kind, int x, int y)
    {
        if (b?.surface == null || kind == SurfaceIndexKind.None) return 0f;
        if (x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) return 0f;

        var fld = FieldFor(b, kind);
        if (fld == null || fld.rawMax <= 0.0001f) return 0f;

        float raw = Raw(b, kind, x, y);

        // A world whose ceiling never reaches the floor has no usable ground for this index at all. Its
        // numbers are still ORDERED and still honest — an airless world's windiest ridge really is its
        // windiest ridge, and reads maybe 28% — because the survey readout has to be able to say "this
        // world tops out at 28%, that is why nothing is highlighted" rather than showing a blank map with
        // no explanation on it.
        if (fld.ceiling <= ShowFloor)
            return fld.ceiling * Mathf.InverseLerp(fld.rawMin, fld.rawMax, raw);

        if (raw >= fld.cutoff)
            return Mathf.Lerp(ShowFloor, fld.ceiling, Mathf.InverseLerp(fld.cutoff, fld.rawMax, raw));

        // Under the cutoff. Compressed below the floor, order preserved, so the readout can still tell
        // you how far off a tile is instead of flatly reporting nothing.
        return (ShowFloor - 0.01f) * Mathf.InverseLerp(fld.rawMin, fld.cutoff, raw);
    }

    /// What this tile actually YIELDS, which is zero under the floor. The request in one line: below 70%
    /// an index provides no resource at all, rather than a small one.
    public static float Productive(float indexValue) => indexValue < ShowFloor ? 0f : indexValue;

    /// Which 10% band a value sits in, 0 (the floor) .. 1 (100%). Only meaningful at or above the floor.
    public static float Band(float v)
    {
        if (v < ShowFloor) return 0f;
        int steps = Mathf.Max(1, Mathf.RoundToInt((1f - ShowFloor) / BandStep));   // 70..100 in tens = 3
        int i = Mathf.Clamp(Mathf.FloorToInt((v - ShowFloor) / BandStep), 0, steps - 1);
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
            ceiling = Ceiling(b, k, vals[vals.Length - 1])
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
    const float HeatCoverage    = 0.09f;
    const float FertileCoverage = 0.15f;
    const float WaterCoverage   = 0.15f;

    static float Coverage(CelestialBody b, SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return MineralCoverage;
            case SurfaceIndexKind.Heat: return HeatCoverage;
            case SurfaceIndexKind.Fertile: return FertileCoverage;
            case SurfaceIndexKind.Water: return WaterCoverage;
            case SurfaceIndexKind.Solar: return SolarCoverage(b);
            case SurfaceIndexKind.Wind: return WeatherCoverage(b);
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
            case SurfaceIndexKind.Mineral: return Quality(rawMax, 0.30f, 0.62f);
            case SurfaceIndexKind.Heat: return Quality(rawMax, 0.28f, 0.62f);
            case SurfaceIndexKind.Fertile: return Quality(rawMax, 0.30f, 0.68f);
            case SurfaceIndexKind.Water: return Quality(rawMax, 0.30f, 0.70f);
            // Both of these are capped by the AIR before anything about the ground is considered — an
            // airless world has no weather whatever its ridges look like, and its panels are unbeatable
            // whatever its moisture field says.
            case SurfaceIndexKind.Solar: return Mathf.Min(SolarAirCeiling(b), Quality(rawMax, 0.25f, 0.65f));
            case SurfaceIndexKind.Wind: return Mathf.Min(WeatherAirCeiling(b), Quality(rawMax, 0.22f, 0.60f));
            default: return 0f;
        }
    }

    static float Quality(float rawMax, float dead, float good)
        => Mathf.Clamp01(Mathf.InverseLerp(dead, good, rawMax));

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

    static float BiomeMineral(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.MetallicCrust: return 1.0f;
            case TerrainType.CrystalField: return 0.95f;
            case TerrainType.Mountains: return 0.8f;
            case TerrainType.Canyon: case TerrainType.Badlands: return 0.65f;
            case TerrainType.Highlands: case TerrainType.Crater: return 0.6f;
            case TerrainType.LavaRock: case TerrainType.ObsidianFlat: return 0.55f;
            case TerrainType.Hills: return 0.45f;
            case TerrainType.Barren: case TerrainType.Wasteland: return 0.35f;
            default: return 0.12f;
        }
    }

    // ---- HEAT: where a geothermal plant pays ----
    // Two separate sources, and keeping them apart is what makes this read correctly:
    //   SURFACE heat — the sun. Latitude and distance from the star. A desert is hot, a pole is not.
    //   GEOTHERMAL heat — the crust. Volcanoes and geysers, which are hot even at a frozen pole.
    // A geothermal plant mostly cares about the second, which is why a volcano on an ice world is still
    // the best site on it.
    static float Heat(CelestialBody b, PlanetTerrainGenerator.Sample f)
    {
        float crust = CrustHeat(f.terrain);

        // A volcanic world is hot underneath everywhere, not just at its vents.
        float planetCrust = b.type == CelestialBodyType.VolcanicPlanet ? 0.45f : 0.06f;

        // Deep ocean bleeds heat away; you don't build a geothermal plant on the sea floor.
        float waterPenalty = f.water ? 0.55f : 1f;

        float v = Mathf.Max(crust, planetCrust) * 0.72f     // the crust dominates
                + f.temperature * 0.28f;                    // the sun contributes a little
        return Mathf.Clamp01(v * waterPenalty);
    }

    static float CrustHeat(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Volcano: return 1.0f;
            case TerrainType.MagmaField: return 0.95f;
            case TerrainType.GeyserField: return 0.92f;
            case TerrainType.LavaRock: return 0.72f;
            case TerrainType.AshWaste: return 0.55f;
            case TerrainType.CrackedGround: return 0.5f;    // fissures = accessible heat
            case TerrainType.ObsidianFlat: return 0.48f;
            case TerrainType.Mountains: case TerrainType.Highlands: return 0.2f;   // some tectonism
            default: return 0.05f;
        }
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

    static float BiomeFertile(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Grassland: return 1.0f;
            case TerrainType.Plains: return 0.92f;
            case TerrainType.Jungle: return 0.85f;
            case TerrainType.Forest: return 0.8f;
            case TerrainType.Swamp: return 0.7f;
            case TerrainType.Savanna: case TerrainType.Steppe: return 0.5f;
            case TerrainType.Taiga: return 0.45f;
            case TerrainType.Beach: return 0.35f;
            case TerrainType.Hills: return 0.4f;
            case TerrainType.Tundra: return 0.15f;
            case TerrainType.Highlands: return 0.18f;
            case TerrainType.Desert: case TerrainType.Dunes: case TerrainType.SaltFlat: return 0.05f;
            case TerrainType.Badlands: case TerrainType.Wasteland: case TerrainType.Barren: return 0.04f;
            default: return 0.08f;
        }
    }

    // ============================================================================================
    // WEATHER: where turbines pay — as HOTSPOTS, sized and counted by the air
    //
    // Formerly the Wind Index. Weather is impossible without air, so this is a fact about the whole
    // planet before it is a fact about any tile — but the old version expressed that as a flat MULTIPLIER
    // on every tile, which produced the two failures the request is about:
    //
    //   A THIN-AIRED WORLD went uniformly dim rather than blank. Multiplying a 0.6 tile by a 0.2 severity
    //   gives 12%, and 12% everywhere is a map that says "a bit windy all over" about a world where a
    //   turbine would never turn. It should top out under the floor and be visibly not worth surveying.
    //   A THICK-AIRED WORLD went uniformly bright, for the same reason in reverse — and the whole map
    //   being viable is the thing that makes siting meaningless.
    //
    // So the air now sets a CEILING and a COVERAGE instead, and the map comes out as discrete hotspots:
    // patches of genuinely windy country in a world that is otherwise calm. The thicker the air the more
    // of them there are and the bigger each one is, which is the request exactly — and a world under
    // about half an atmosphere never gets a ceiling over the floor, so its weather map is honestly empty.
    //
    // The terrain terms decide WHERE those patches land, and they changed too. It used to be
    // `elevation * 0.5` plus a blanket bonus at sea, which put every hotspot on a mountain top or out on
    // open water — neither of which you can put a wind farm on. Turbines want FLAT, OPEN ground, so
    // flatness leads now and open water is penalised rather than rewarded: the coverage budget is spent
    // on land you can actually build on, which is the "I would like some on the land" half of the ask.
    // ============================================================================================
    static float Wind(CelestialBody b, PlanetTerrainGenerator.Sample f, float u, float v)
    {
        if (WeatherAirCeiling(b) <= 0f) return 0f;

        float flat = Mathf.Clamp01(1f - f.ridge * 1.15f);          // a turbine wants a plain, not a peak
        float open = 1f - Shelter(f.terrain);                      // and nothing upwind of it
        float thermal = Mathf.Abs(f.temperature - 0.5f) * 0.5f;    // hot/cold extremes stir the air
        float polar = f.latitude * 0.30f;                          // roaring forties
        float exposure = f.elevation * 0.18f;                      // a little height still helps

        float terrain = Mathf.Clamp01((flat * 0.34f + open * 0.22f + thermal * 0.18f
                                       + polar * 0.16f + exposure) * 1.22f);

        // Open water still scores — a coast is genuinely the windiest ground there is — but at well under
        // half, because nothing can be built on it and a hotspot spent out at sea is a hotspot wasted.
        if (f.water) terrain *= 0.45f;

        return Mathf.Clamp01(terrain * (1f - HotspotWeight) + HotspotField(b, u, v, WeatherBlobs(b), 17.3f) * HotspotWeight);
    }

    /// How much of a tile's raw score comes from the hotspot field rather than from its terrain.
    ///
    /// Not zero and not one, and both ends are wrong for a reason. At zero the "hotspots" are just the
    /// top of a terrain gradient, which on a smooth field is a handful of ragged slivers rather than
    /// patches. At one they are blobs of noise laid over a world with no regard for what is under them,
    /// so a wind farm's best site could be a sheltered canyon. Just under half means the patches are
    /// blob-SHAPED but land on ground that deserves them.
    const float HotspotWeight = 0.45f;

    /// The hotspot field itself: a seamless low-frequency noise whose blob size is set by the caller.
    /// `blobs` is roughly how many patches fit around the equator, so a small number gives a few big
    /// regions and a large one a fine scatter.
    static float HotspotField(CelestialBody b, float u, float v, float blobs, float salt)
        => PlanetTerrainGenerator.WorldNoise(b, u, v, blobs, salt, 3);

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
    static float WeatherCoverage(CelestialBody b)
    {
        if (b == null) return 0f;
        float a = b.atmospheres;
        if (a < 1f) return Mathf.Lerp(0.02f, 0.14f, Mathf.InverseLerp(TraceAir, 1f, a));
        return Mathf.Lerp(0.14f, 0.42f, Mathf.Clamp01(Mathf.InverseLerp(1f, 6f, a)));
    }

    /// ...and how big each patch is. Fewer blobs around the equator means bigger blobs, so this FALLS as
    /// the air thickens: "the thicker the atmosphere the larger the hotspots are".
    static float WeatherBlobs(CelestialBody b)
        => b == null ? 18f : Mathf.Lerp(18f, 5f, Mathf.Clamp01(Mathf.InverseLerp(TraceAir, 6f, b.atmospheres)));

    /// How much weather this world has AT ALL, 0..1 — kept as the one number the readouts quote.
    public static float WeatherSeverity(CelestialBody b) => WeatherAirCeiling(b);

    /// Plain-language severity, for the Survey status line. It now says outright when a world's ceiling
    /// is under the usable floor, because "near-calm" was being read as "a poor site" when it meant
    /// "there is no site here and there never will be".
    public static string WeatherLabel(CelestialBody b)
    {
        float s = WeatherAirCeiling(b);
        if (s <= 0f) return "airless — no weather at all";
        if (s < ShowFloor) return $"too thin to harvest — tops out near {s * 100f:F0}%";
        if (s < 0.85f) return "breezy — workable hotspots";
        if (s < 0.95f) return "stormy";
        return "violent";
    }

    static float Shelter(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Jungle: case TerrainType.Forest: case TerrainType.Taiga: return 0.85f;
            case TerrainType.Swamp: return 0.6f;
            case TerrainType.Canyon: return 0.7f;
            case TerrainType.Mountains: case TerrainType.Highlands: return 0f;
            default: return 0.3f;
        }
    }

    // ============================================================================================
    // SOLAR: where panels pay — and the poles stop being free
    //
    // Cloudless and high, as before: moisture means cloud, so dry ground is bright ground.
    //
    // THE POLAR PREFERENCE NOW DEPENDS ON THE AIR, which is the correction. A panel at the pole of a
    // world with any axial tilt sees continuous summer daylight for months, and on a world with almost no
    // atmosphere that is decisive — hours beat angle, and there is nothing in the way. Put an atmosphere
    // over it and it reverses: polar sunlight arrives at a shallow angle and has to cross an enormous
    // slant path of air to get to the ground, so the thicker the air the worse the poles are and the
    // more the equator wins. The preference is therefore LERPED by pressure rather than fixed, which is
    // the request's "make solar less viable at the poles the thicker the atmosphere".
    //
    // Pressure no longer multiplies the tile score. It used to, and that is what produced the behaviour
    // the request objects to: a thick-aired world's best possible solar tile was capped at a third, so
    // its map was uniformly dim and panels were simply off the table there. Air now sets the CEILING and
    // the COVERAGE instead (see below), so a thick-aired world still has genuine 70-100% hotspots — there
    // are just few of them and they are small, and finding one is the point.
    // ============================================================================================
    static float Solar(CelestialBody b, PlanetTerrainGenerator.Sample f, float u, float v)
    {
        float air = AirNorm(b);
        // Thin air: the poles win outright. Thick air: they are the worst ground on the world.
        float polar = Mathf.Lerp(0.35f + f.latitude * 0.65f, 1f - f.latitude * 0.80f, air);
        float clear = Mathf.Clamp01(1f - f.moisture * 1.15f);      // moisture = cloud
        float altitude = f.elevation * 0.2f;

        // How much light the planet gets AT ALL. terrainParams.heat is set from its distance to its
        // star, so a far, cold world's sunniest desert still can't match a close one's.
        float insolation = Mathf.Clamp01(b.terrainParams.heat / 1.4f);

        float terrain = (polar * 0.45f + clear * 0.4f + altitude) * Mathf.Lerp(0.45f, 1.15f, insolation);
        if (f.terrain == TerrainType.Storm) terrain *= 0.25f;      // permanent cloud
        terrain = Mathf.Clamp01(terrain);

        return Mathf.Clamp01(terrain * (1f - HotspotWeight) + HotspotField(b, u, v, SolarBlobs(b), 4.9f) * HotspotWeight);
    }

    /// A world's air pressure on a 0..1 scale, saturating at the dead line. The one number the solar
    /// terms, ceiling, coverage and blob size are all read off, so they can never disagree about how
    /// thick "thick" is.
    static float AirNorm(CelestialBody b)
        => b == null ? 0f : Mathf.Clamp01(b.atmospheres / SolarDeadAtmospheres);

    /// The highest the Solar index may read here. Thin air is a genuine advantage — nothing between the
    /// panel and the star — and thick air costs a little off the top, but never enough to put the ceiling
    /// under the floor: a thick-aired world's few hotspots are still worth building on, which is the whole
    /// change. What thick air really costs is COVERAGE.
    static float SolarAirCeiling(CelestialBody b) => Mathf.Lerp(1f, 0.88f, AirNorm(b));

    /// How much of a world is sunny enough to matter. Nearly half of an airless rock; a sixth of an
    /// Earth-like world; a scattered few percent of a thick one.
    static float SolarCoverage(CelestialBody b)
    {
        if (b == null) return 0.16f;
        float a = b.atmospheres;
        if (a < 1f) return Mathf.Lerp(0.45f, 0.16f, Mathf.Clamp01(a));
        return Mathf.Lerp(0.16f, 0.04f, Mathf.Clamp01(Mathf.InverseLerp(1f, 4f, a)));
    }

    /// ...and how big each patch is. RISES with pressure — more blobs around the equator means smaller
    /// blobs — so a thin-aired world's sun comes in a few huge regions and a thick one's in small spots.
    static float SolarBlobs(CelestialBody b)
        => b == null ? 9f : Mathf.Lerp(4f, 17f, AirNorm(b));

    /// What a world's air pressure does to solar output, as a multiplier on a 1.0 baseline — the number
    /// the Survey readout quotes so a player can see WHY a thick world's map is nearly empty.
    ///
    /// No longer applied per tile (see Solar above): the index's ceiling and coverage carry pressure now,
    /// and multiplying here as well would charge a thick world for its air twice. Kept because it is
    /// still the honest headline figure, and because the build menu's dead-line gate is solved against it.
    ///
    /// Above Earth-normal, output falls linearly and reaches EXACTLY ZERO at the dead line: 2 atm -> 75%,
    /// 3 -> 50%, 4 -> 25%, 5 -> 0. Below Earth pressure it runs the other way, gaining 10 points per 0.1
    /// atmosphere under, so a 0.5-atm world runs at 150% and an airless one at 200%.
    ///
    /// Returned UNCLAMPED above 1 on purpose. The bonus is real and the caller decides what to do with it.
    public static float SolarPressureFactor(float atmospheres)
    {
        if (atmospheres >= 1f)
            return Mathf.Max(0f, 1f - (atmospheres - 1f) / (SolarDeadAtmospheres - 1f));
        return 1f + (1f - atmospheres) * 1.0f;
    }

    /// Atmospheres at which solar output reaches zero, and so the point past which panels are not
    /// offered at all. SolarPressureFactor is solved against this, so the two can never drift apart.
    public const float SolarDeadAtmospheres = 5f;

    /// Is solar worth building on this world? False above the dead line — and true again the moment
    /// terraforming brings the air back down to 4, which is what makes thinning an atmosphere a
    /// strategic move rather than a cosmetic one.
    public static bool SolarViable(CelestialBody b) =>
        b != null && b.atmospheres < SolarDeadAtmospheres;

    // ============================================================================================
    // WATER: where anything that needs water pays
    //
    // THE OLD VERSION SCORED THE WATER ITSELF, which made it useless for the one thing it is for.
    // It read the 3x3 neighbourhood, so only a tile touching the sea got any credit at all, and the
    // highest numbers on the map were on open water — where nothing can be built. A Steam Turbine
    // needs water to raise steam with; it does not need to be standing IN it. The practical effect was
    // that the whole Electrical category's wettest buildings had to be jammed onto the shoreline in a
    // single-tile ring, and once that ring was full the world had no more sites.
    //
    // SO WATER PROJECTS, exactly as a power plant projects a grid. Every connected body of water throws
    // an influence outward across the land around it, and the two numbers that matter both scale with
    // HOW BIG THAT BODY IS:
    //
    //   REACH      how far inland it carries. A pond wets its own doorstep; an ocean supplies a
    //              hinterland. Scaled by the SQUARE ROOT of area, because that is the body's linear
    //              size — a lake of four times the area is twice as wide, and twice as wide is the
    //              honest reading of "twice the water". Linear in area would let one sea cover a
    //              hemisphere.
    //   STRENGTH   how good the best site near it is. Also root-scaled, and saturating: a pond is a
    //              poor site however close you stand to it.
    //
    // ---- THE BUFFER, and why the shore is not the best place ----
    //
    // The request is explicit: leave a gap so a row of turbines does not have to be crammed onto the
    // waterline. So the shore is VIABLE but deliberately not optimal (ShoreFraction of the peak), the
    // peak sits one tile further in, and it falls away from there to nothing at the reach. You can
    // build on the beach; you do slightly better a step back from it, and you can keep building
    // inland for a long way before it stops being worth it.
    //
    // Measured against a rendered map at 160x80 with a plausible spread of bodies: about half the land
    // has some hydro, a fifth of it clears 50%, and under 5% clears 80% — so the good ground is
    // findable and finite rather than everywhere or nowhere.
    // ============================================================================================

    /// The shortest reach any body has, in tiles, and the longest — an ocean does not supply a planet.
    const float WaterReachMin = 3f, WaterReachMax = 14f;

    /// Tiles of reach per unit of sqrt(area).
    const float WaterReachPerRoot = 0.8f;

    /// Distance out to which the shoreline discount applies. 1 = only the tiles touching the water.
    const int WaterBuffer = 1;

    /// What the shore gets, as a fraction of the peak. Under 1 so the best ground is a step inland.
    const float WaterShoreFraction = 0.72f;

    static float Water(CelestialBody b, PlanetTerrainGenerator.Sample f, int x, int y)
    {
        if (f.water) return 0.02f;                                 // you cannot build on it

        var field = WaterFieldFor(b);
        if (field == null) return 0.02f;

        int i = y * b.surface.width + x;
        int id = field.nearestBody[i];
        if (id < 0) return 0.02f;                                  // no water anywhere on this world

        float d = field.distance[i];
        float reach = ReachOf(field.bodySize[id]);
        if (d > reach) return 0.02f;

        float peak = StrengthOf(field.bodySize[id]);

        float v;
        if (d <= WaterBuffer) v = peak * WaterShoreFraction;
        else
        {
            float t = (d - (WaterBuffer + 1)) / Mathf.Max(0.001f, reach - (WaterBuffer + 1));
            v = peak * (1f - Mathf.Clamp01(t));
        }

        // RELIEF IS A TIEBREAK NOW, NOT A GATE. It used to be nearly half the score, which is right for
        // a hydro DAM (you need head to drop the water through) and wrong for everything else that
        // reads this index — a boiler hall wants water, not a waterfall. Kept as a modest bonus so a
        // hilly shore still beats a flat marsh, and so the Hydro Plant's own minIndex gate still tends
        // to land it somewhere with a gradient.
        float relief = Mathf.Clamp01(f.elevation * 0.7f + f.ridge * 0.5f);
        v *= Mathf.Lerp(0.85f, 1.15f, relief);

        return Mathf.Clamp01(v);
    }

    static float ReachOf(int tiles)
        => Mathf.Min(WaterReachMax, WaterReachMin + Mathf.Sqrt(Mathf.Max(1, tiles)) * WaterReachPerRoot);

    static float StrengthOf(int tiles)
        => Mathf.Min(1f, 0.35f + Mathf.Sqrt(Mathf.Max(1, tiles)) / 22f);

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
    }

    public static void InvalidateAll()
    {
        statsCache.Clear();
        waterFields.Clear();
        bands.Clear();
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
        if (v < ShowFloor) return false;
        t = Band(v);
        return true;
    }

    // ---- Presentation ----
    public static string Name(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Mineral Index";
            case SurfaceIndexKind.Heat: return "Heat Index";
            case SurfaceIndexKind.Fertile: return "Fertile Index";
            case SurfaceIndexKind.Wind: return "Weather Index";
            case SurfaceIndexKind.Solar: return "Solar Index";
            case SurfaceIndexKind.Water: return "Hydro Index";
            default: return "None";
        }
    }

    public static string Describe(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Broken, raised crust — mountains, canyons and exposed seams, which is why the richest ground follows the plate margins. Concentrated into a few districts rather than spread over the whole crust.";
            case SurfaceIndexKind.Heat: return "Heat in the CRUST, not the air: volcanoes and geyser fields. A volcano on an ice world is still that world's best geothermal site — but a world with no volcanism has no geothermal ground at all, however cracked its mountains look.";
            case SurfaceIndexKind.Fertile: return "Warm AND wet AND flat — farmland needs all three at once, not any one of them. Needs a LIVING world above all: no biosphere, no soil, and the index reads nothing.";
            case SurfaceIndexKind.Wind: return "Needs AIR, and enough of it. Under about two-thirds of an atmosphere a world never reaches a workable figure anywhere. Above that it comes as HOTSPOTS — flat, open, exposed country — and the thicker the air the more of them there are and the larger each one gets.";
            case SurfaceIndexKind.Solar: return "Dry, high ground, and long days. On a thin-aired world the poles win outright and the sun is good over huge stretches; thicken the air and the poles become the worst ground on the planet and the good sites shrink to a scattered few. A world far from its star is dim everywhere.";
            case SurfaceIndexKind.Water: return "How much water is within reach. Every lake and sea supplies the land AROUND it — the bigger the body, the further inland it carries and the better the best sites near it. The shore itself is good; one step back from it is better.";
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
            case SurfaceIndexKind.Heat: c = Color.Lerp(new Color(0.85f, 0.45f, 0.10f), new Color(1.00f, 0.10f, 0.05f), t); break;
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

    /// The fill for a tile the overlay has decided to draw (see Shown), in the band `t`.
    public static Color Highlight(SurfaceIndexKind k, float t)
    {
        t = Mathf.Clamp01(t);
        var c = Ramp(k, Mathf.Lerp(0.5f, 1f, t));
        c.a = Mathf.Lerp(0.52f, 0.94f, t);
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
            case SurfaceIndexKind.Heat:    return new Color(1.00f, 0.30f, 0.20f, 1f);   // bright red
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
            case SurfaceIndexKind.Heat:
            case SurfaceIndexKind.Fertile: return 1;
            case SurfaceIndexKind.Wind:
            case SurfaceIndexKind.Solar: return 2;
            case SurfaceIndexKind.Water: return 3;
            default: return 0;
        }
    }

    public static bool Unlocked(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return false;
        if (GameMode.DevMode) return true;
        if (!b.Surveyed) return false;
        return b.researchLevel >= RequiredLevel(k);
    }

    /// Why an overlay is locked — and it names the TIER, because "needs a deep survey" was useless once
    /// there was more than one of them. A greyed control that will not say what is missing is a dead end.
    public static string LockReason(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return "no world selected";
        if (!b.Surveyed) return "survey this world first";

        int need = RequiredLevel(k);
        if (b.researchLevel >= need) return null;

        return $"needs {DeepResearch.Name(need)} — send a research ship to study this world";
    }
}
