using UnityEngine;

// Generates a planet's surface from a resolution-independent, deterministic noise field.
//
// The whole point: SampleNormalized(u,v) returns the same terrain for the same (u,v) no matter how
// many tiles/pixels you sample. So the low-res grid viewer and the high-res detailed map render the
// SAME continents and oceans — the detailed view just samples more densely (and with extra octaves)
// to reveal finer coastlines and features. Both are driven by body.terrainSeed + continentFrequency.
public static class PlanetTerrainGenerator
{
    public struct NoiseParams
    {
        public float scale;      // frequency multiplier (feature density)
        public float elevation, moisture, heat, ridge; // amplitude multipliers
        public static NoiseParams Default => new NoiseParams
        { scale = 1f, elevation = 1f, moisture = 1f, heat = 1f, ridge = 1f };
    }

    public struct Sample
    {
        public TerrainType terrain;
        public float shade;      // 0..1 per-pixel brightness jitter
        public float elevation;  // 0..1
        public bool water;
    }

    // ---- Low-res grid (used by the classic tile viewer and gameplay) ----
    public static PlanetSurface GenerateSurface(CelestialBody body)
    {
        return Build(body, NoiseParams.Default, 4);
    }

    public static PlanetSurface GenerateSurfaceWithParams(
        CelestialBody body, float noiseScale, float elevationStrength,
        float moistureStrength, float heatStrength, float ridgeStrength)
    {
        // The editor sliders historically ran ~0..1; reinterpret as sensible multipliers.
        var p = new NoiseParams
        {
            scale = Mathf.Clamp(noiseScale <= 0f ? 1f : (noiseScale < 0.35f ? noiseScale * 12f : noiseScale), 0.3f, 4f),
            elevation = Mathf.Max(0.1f, elevationStrength),
            moisture = Mathf.Max(0.1f, moistureStrength),
            heat = Mathf.Max(0.1f, heatStrength),
            ridge = Mathf.Max(0.1f, ridgeStrength)
        };
        return Build(body, p, 4);
    }

    static PlanetSurface Build(CelestialBody body, NoiseParams p, int octaves)
    {
        int width = Mathf.Max(4, body.surfaceSize * 2);
        int height = Mathf.Max(2, body.surfaceSize);

        PlanetSurface surface = new PlanetSurface(width, height);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                Sample s = SampleNormalized(body, u, v, p, octaves);
                surface.tiles[x, y] = new TerrainTile(s.terrain, s.shade);
            }
        return surface;
    }

    // ---- The shared, resolution-independent sampler ----
    public static Sample SampleNormalized(CelestialBody body, float u, float v, NoiseParams p, int octaves)
    {
        float seed = body.terrainSeed;
        float freq = Mathf.Max(1f, body.continentFrequency) * p.scale;

        // 2:1 map aspect -> stretch u so continents stay roughly square.
        float fx = u * freq * 2f;
        float fy = v * freq;

        float elevation = FBm(fx + seed, fy + seed * 1.3f, octaves) * p.elevation;
        float moisture  = FBm(fx * 1.3f + seed + 31f, fy * 1.3f + seed + 17f, octaves) * p.moisture;
        float ridge     = FBm(fx * 2.2f + seed + 91f, fy * 2.2f + seed + 53f, octaves) * p.ridge;

        float lat = Mathf.Abs(v - 0.5f) * 2f;                 // 0 equator, 1 pole
        float heatNoise = FBm(fx * 0.9f + seed + 11f, fy * 0.9f + seed + 7f, 2);
        float temperature = Mathf.Clamp01(((1f - lat) * 0.75f + heatNoise * 0.45f) * p.heat);

        float fine = Mathf.PerlinNoise(fx * 6f + seed, fy * 6f + seed);

        TerrainType t = Classify(body.type, elevation, moisture, temperature, ridge, lat);

        return new Sample { terrain = t, shade = fine, elevation = elevation, water = IsWater(t) };
    }

    public static bool IsWater(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Ocean:
            case TerrainType.Lake:
            case TerrainType.River:
            case TerrainType.Reef:
            case TerrainType.FrozenSea:
                return true;
            default:
                return false;
        }
    }

    static float FBm(float x, float y, int octaves)
    {
        float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return norm > 0f ? sum / norm : 0f;
    }

    // ---- Biome classification (deterministic; identical logic at any resolution) ----
    static TerrainType Classify(CelestialBodyType planet, float elev, float moist, float temp, float ridge, float lat)
    {
        switch (planet)
        {
            case CelestialBodyType.GasGiant:       return GasGiant(lat, elev, moist);
            case CelestialBodyType.VolcanicPlanet: return Volcanic(elev, temp, ridge, lat);
            case CelestialBodyType.IcePlanet:      return Ice(elev, moist, ridge, lat);
            case CelestialBodyType.OceanPlanet:    return OceanWorld(elev, temp, lat);
            case CelestialBodyType.BarrenPlanet:   return Barren(elev, ridge);
            case CelestialBodyType.Moon:
            case CelestialBodyType.Asteroid:       return Airless(elev, ridge);
            case CelestialBodyType.RockyPlanet:
            default:                               return Terran(elev, moist, temp, ridge);
        }
    }

    static TerrainType GasGiant(float lat, float elev, float moist)
    {
        float band = Mathf.Repeat((lat + moist * 0.3f) * 6f, 1f);
        if (elev > 0.78f) return TerrainType.Storm;      // great-spot style storm
        return band < 0.5f ? TerrainType.GasClouds : TerrainType.Storm;
    }

    static TerrainType Volcanic(float elev, float temp, float ridge, float lat)
    {
        float hot = temp + (1f - lat) * 0.2f;
        if (hot > 0.9f && ridge > 0.7f) return TerrainType.Volcano;
        if (hot > 0.78f) return TerrainType.MagmaField;
        if (ridge > 0.72f) return TerrainType.Mountains;
        if (elev > 0.62f)  return TerrainType.LavaRock;
        if (elev < 0.32f)  return TerrainType.ObsidianFlat;
        if (temp > 0.6f)   return TerrainType.AshWaste;
        if (ridge > 0.55f) return TerrainType.CrackedGround;
        return TerrainType.GeyserField;
    }

    static TerrainType Ice(float elev, float moist, float ridge, float lat)
    {
        if (ridge > 0.8f)  return TerrainType.Mountains;
        if (elev > 0.72f)  return TerrainType.Glacier;
        if (elev < 0.3f)   return TerrainType.FrozenSea;
        if (moist > 0.72f) return TerrainType.CrystalField;
        if (lat < 0.25f && elev > 0.5f) return TerrainType.Snow;
        return TerrainType.Ice;
    }

    static TerrainType OceanWorld(float elev, float temp, float lat)
    {
        if (elev > 0.80f) return TerrainType.Mountains;
        if (elev > 0.70f) return TerrainType.Island;
        if (elev > 0.64f) return TerrainType.Beach;
        if (lat > 0.85f)  return TerrainType.FrozenSea;
        if (elev < 0.40f && temp > 0.6f) return TerrainType.Reef;
        return TerrainType.Ocean;
    }

    static TerrainType Barren(float elev, float ridge)
    {
        if (ridge > 0.82f) return TerrainType.Mountains;
        if (ridge > 0.7f)  return TerrainType.Canyon;
        if (elev > 0.66f)  return TerrainType.Highlands;
        if (elev < 0.3f)   return TerrainType.SaltFlat;
        if (ridge > 0.5f)  return TerrainType.Badlands;
        if (elev > 0.55f)  return TerrainType.MetallicCrust;
        return TerrainType.Wasteland;
    }

    static TerrainType Airless(float elev, float ridge)
    {
        if (ridge > 0.85f) return TerrainType.Highlands;
        if (elev > 0.7f)   return TerrainType.MetallicCrust;
        if (elev < 0.28f)  return TerrainType.Crater;
        if (ridge > 0.72f) return TerrainType.CrystalField;
        if (elev < 0.4f)   return TerrainType.Ice;
        return TerrainType.Barren;
    }

    static TerrainType Terran(float elev, float moist, float temp, float ridge)
    {
        if (elev < 0.36f) return TerrainType.Ocean;
        if (elev < 0.40f) return TerrainType.Beach;

        if (ridge > 0.82f) return TerrainType.Mountains;
        if (elev > 0.74f)  return TerrainType.Highlands;
        if (elev > 0.66f)  return TerrainType.Hills;

        if (temp < 0.28f)
        {
            if (moist > 0.55f) return TerrainType.Taiga;
            return (elev > 0.5f) ? TerrainType.Snow : TerrainType.Tundra;
        }

        if (temp < 0.62f)
        {
            if (elev < 0.44f && moist > 0.7f) return TerrainType.Swamp;
            if (moist > 0.62f) return TerrainType.Forest;
            if (moist > 0.4f)  return TerrainType.Grassland;
            if (moist > 0.25f) return TerrainType.Plains;
            return TerrainType.Steppe;
        }

        if (moist > 0.66f) return TerrainType.Jungle;
        if (moist > 0.42f) return TerrainType.Savanna;
        if (moist > 0.25f) return TerrainType.Plains;
        if (moist > 0.14f) return TerrainType.Dunes;
        return TerrainType.Desert;
    }
}
