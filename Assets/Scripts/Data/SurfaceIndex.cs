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

    public static float Get(CelestialBody b, SurfaceIndexKind kind, int x, int y)
    {
        if (b?.surface == null || x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) return 0f;
        var f = Field(b, x, y);
        var t = b.surface.tiles[x, y];

        switch (kind)
        {
            case SurfaceIndexKind.Mineral: return Mineral(f, t);
            case SurfaceIndexKind.Heat: return Heat(b, f);
            case SurfaceIndexKind.Fertile: return Fertile(f);
            case SurfaceIndexKind.Wind: return Wind(f);
            case SurfaceIndexKind.Solar: return Solar(b, f);
            case SurfaceIndexKind.Water: return Water(b, f, x, y);
            default: return 0f;
        }
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
    static float Fertile(PlanetTerrainGenerator.Sample f)
    {
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

    // ---- WIND: where turbines pay ----
    // Exposure. High, open ground and coastlines get the wind; forests and valleys are sheltered.
    // Big temperature swings drive weather, so poles and coasts (land next to sea) are gusty.
    static float Wind(PlanetTerrainGenerator.Sample f)
    {
        float exposure = f.elevation * 0.5f + f.ridge * 0.18f;
        float open = 1f - Shelter(f.terrain);
        float thermal = Mathf.Abs(f.temperature - 0.5f) * 0.5f;   // hot/cold extremes stir the air
        float polar = f.latitude * 0.28f;                          // roaring forties

        float v = exposure * 0.42f + open * 0.24f + thermal * 0.2f + polar * 0.24f;
        if (f.water) v += 0.18f;                                   // nothing to break the wind at sea
        return Mathf.Clamp01(v * 1.25f);
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

    // ---- SOLAR: where panels pay ----
    // Cloudless, sunlit, near the equator. Moisture means cloud, so dry ground is bright ground — which
    // is exactly why deserts and savanna win. Altitude helps (thinner air above you).
    static float Solar(CelestialBody b, PlanetTerrainGenerator.Sample f)
    {
        float sunAngle = 1f - f.latitude * 0.75f;                  // the sun is low at the poles
        float clear = Mathf.Clamp01(1f - f.moisture * 1.15f);      // moisture = cloud
        float altitude = f.elevation * 0.2f;

        // How much light the planet gets AT ALL. terrainParams.heat is set from its distance to its
        // star, so a far, cold world's sunniest desert still can't match a close one's.
        float insolation = Mathf.Clamp01(b.terrainParams.heat / 1.4f);

        float v = (sunAngle * 0.45f + clear * 0.4f + altitude) * Mathf.Lerp(0.45f, 1.15f, insolation);
        if (f.terrain == TerrainType.Storm) v *= 0.25f;            // permanent cloud
        return Mathf.Clamp01(v);
    }

    // ---- WATER: where hydro pays ----
    // Hydro needs flowing water, which means water AND a height gradient — a river down a mountain, not
    // a flat sea. So: open water nearby, and relief to drop it through.
    static float Water(CelestialBody b, PlanetTerrainGenerator.Sample f, int x, int y)
    {
        float nearby = WaterNeighbours(b, x, y);                   // 0..1 fraction of adjacent sea
        if (nearby <= 0f && !f.water) return 0.02f;                // no water at all: nothing to dam

        float relief = Mathf.Clamp01(f.elevation * 0.7f + f.ridge * 0.5f);
        float flow = f.terrain == TerrainType.River ? 1f
                   : f.terrain == TerrainType.Lake ? 0.75f
                   : nearby;

        float v = flow * 0.55f + relief * 0.45f;
        if (f.water && relief < 0.2f) v *= 0.4f;                   // flat open sea: no head to work with
        return Mathf.Clamp01(v * 1.2f);
    }

    static float WaterNeighbours(CelestialBody b, int x, int y)
    {
        int wet = 0, total = 0;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= b.surface.width || ny >= b.surface.height) continue;
                total++;
                if (PlanetTerrainGenerator.IsWater(b.surface.tiles[nx, ny].type)) wet++;
            }
        return total > 0 ? wet / (float)total : 0f;
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
    static readonly Dictionary<(int, SurfaceIndexKind), Stats> statsCache = new Dictionary<(int, SurfaceIndexKind), Stats>();

    static Stats GetStats(CelestialBody b, SurfaceIndexKind k)
    {
        var key = (b.id, k);
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
    public static void InvalidateStats(CelestialBody b)
    {
        if (b == null) return;
        foreach (var k in All) statsCache.Remove((b.id, k));
    }

    public static void InvalidateAll() => statsCache.Clear();

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
        => k != SurfaceIndexKind.None && Get(b, k, x, y) >= TopFractionThreshold(b, k, fraction);

    public static float Best(CelestialBody b, SurfaceIndexKind k)
        => b?.surface == null || k == SurfaceIndexKind.None ? 0f : GetStats(b, k).max;

    // ---- Presentation ----
    public static string Name(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Mineral Index";
            case SurfaceIndexKind.Heat: return "Heat Index";
            case SurfaceIndexKind.Fertile: return "Fertile Index";
            case SurfaceIndexKind.Wind: return "Wind Index";
            case SurfaceIndexKind.Solar: return "Solar Index";
            case SurfaceIndexKind.Water: return "Hydro Index";
            default: return "None";
        }
    }

    public static string Describe(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Broken, raised crust — mountains, canyons and exposed seams. Mines want the highest they can get.";
            case SurfaceIndexKind.Heat: return "Heat in the CRUST, not the air: volcanoes and geyser fields. A volcano on an ice world is still that world's best geothermal site.";
            case SurfaceIndexKind.Fertile: return "Warm AND wet AND flat — farmland needs all three at once, not any one of them.";
            case SurfaceIndexKind.Wind: return "Exposure: high open ground, coasts and polar latitudes. Forests and canyons are sheltered.";
            case SurfaceIndexKind.Solar: return "Cloudless equatorial sun. Dry ground is bright ground, which is why deserts win — and a world far from its star is dim everywhere.";
            case SurfaceIndexKind.Water: return "Flowing water: rivers and coasts WITH relief to drop through. A flat open sea has no head to work with.";
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
            case SurfaceIndexKind.Mineral: c = Color.Lerp(new Color(0.20f, 0.13f, 0.07f), new Color(0.78f, 0.52f, 0.24f), t); break;
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

    // Minerals you can see from orbit; everything else needs someone on the ground.
    public static bool Unlocked(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return false;
        if (GameMode.DevMode) return true;
        if (!b.Surveyed) return false;
        return k == SurfaceIndexKind.Mineral || b.deepSurveyed;
    }

    public static string LockReason(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return "no world selected";
        if (!b.Surveyed) return "survey this world first";
        if (k != SurfaceIndexKind.Mineral && !b.deepSurveyed)
            return "needs a deep survey — send a research ship to study this world";
        return null;
    }
}
