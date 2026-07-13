using UnityEngine;

public static class PlanetTerrainGenerator
{
    public static PlanetSurface GenerateSurface(CelestialBody body)
    {
        int width = body.surfaceSize * 2;
        int height = body.surfaceSize;

        PlanetSurface surface = new PlanetSurface(width, height);

        // Use body type + random seed for unique planets
        int seed = body.GetHashCode() + Random.Range(0, 10000);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TerrainType type = GenerateTerrainByNoise(body.type, x, y, width, height, seed);
                surface.tiles[x, y] = new TerrainTile(type);
            }
        }

        Debug.Log($"Generated rectangular surface for {body.type} ({width}x{height})");
        return surface;
    }

    static TerrainType GenerateTerrainByNoise(CelestialBodyType planetType, int x, int y, int width, int height, int seed)
    {
        float seedOffset = seed * 0.001f;
        float scale = 0.08f;

        float elevation = Mathf.PerlinNoise(x * scale + seedOffset, y * scale + seedOffset * 1.1f);
        float moisture = Mathf.PerlinNoise((x + 100) * scale * 1.5f + seedOffset, (y + 100) * scale * 1.5f + seedOffset);
        float heat = Mathf.PerlinNoise((x + 200) * scale * 0.8f + seedOffset * 1.2f, (y + 200) * scale * 0.8f + seedOffset);
        float ridges = Mathf.PerlinNoise((x + 300) * scale * 2f + seedOffset, (y + 300) * scale * 2f + seedOffset);

        // === Gas Giants ===
        if (planetType == CelestialBodyType.GasGiant)
            return TerrainType.Barren;

        // === Volcanic Planets ===
        if (planetType == CelestialBodyType.VolcanicPlanet)
        {
            float volcanicHeat = heat + Mathf.PerlinNoise((x + 500) * 0.22f, (y + 500) * 0.22f) * 0.65f;
            float equatorBias = Mathf.Abs(y - height / 2f) / (height / 2f);

            if (volcanicHeat > 0.87f && Random.value < (0.35f - equatorBias * 0.25f))
                return TerrainType.Volcano;

            if (volcanicHeat > 0.70f)
                return TerrainType.MagmaField;

            if (ridges > 0.62f)
                return TerrainType.Mountains;

            return TerrainType.Barren;
        }

        // === Ice Planets - NO volcanoes or magma ===
        if (planetType == CelestialBodyType.IcePlanet)
        {
            if (elevation < 0.3f) return TerrainType.Barren;
            if (Random.value < 0.02f) return TerrainType.Crater;
            return TerrainType.Ice;
        }

        // === Ocean Planets - Very rare volcanoes, NO magma ===
        if (planetType == CelestialBodyType.OceanPlanet)
        {
            if (elevation > 0.7f) return TerrainType.Island;
            if (heat > 0.92f && Random.value < 0.06f) return TerrainType.Volcano; // Very rare
            return TerrainType.Ocean;
        }

        // === Moons & Asteroids - NO volcanoes or magma ===
        if (planetType == CelestialBodyType.Moon || planetType == CelestialBodyType.Asteroid)
        {
            if (Random.value < 0.22f) return TerrainType.Crater;
            if (Random.value < 0.10f) return TerrainType.Ice;
            return TerrainType.Barren;
        }

        // === Normal planets - Strict rules ===
        float normalHeat = heat + Mathf.PerlinNoise((x + 500) * 0.2f, (y + 500) * 0.2f) * 0.4f;

        if (normalHeat > 0.93f && Random.value < 0.08f)
            return TerrainType.Volcano;

        if (normalHeat > 0.85f && Random.value < 0.12f)
            return TerrainType.MagmaField;

        // Mountains
        if (ridges > 0.82f && planetType != CelestialBodyType.OceanPlanet)
            return TerrainType.Mountains;

        // Rocky Planets
        if (planetType == CelestialBodyType.RockyPlanet)
        {
            if (ridges > 0.75f) return TerrainType.Mountains;
            if (moisture > 0.6f) return TerrainType.Forest;
            return TerrainType.Plains;
        }

        return TerrainType.Barren;
    }

    // Editor
    // New method that accepts slider values for live testing
    public static PlanetSurface GenerateSurfaceWithParams(
        CelestialBody body,
        float noiseScale,
        float elevationStrength,
        float moistureStrength,
        float heatStrength,
        float ridgeStrength)
    {
        int width = body.surfaceSize * 2;
        int height = body.surfaceSize;

        PlanetSurface surface = new PlanetSurface(width, height);

        int seed = body.GetHashCode() + Random.Range(0, 10000);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TerrainType type = GenerateTerrainByNoiseWithParams(
                    body.type, x, y, width, height, seed,
                    noiseScale, elevationStrength, moistureStrength, heatStrength, ridgeStrength
                );

                surface.tiles[x, y] = new TerrainTile(type);
            }
        }

        Debug.Log("Generated terrain with custom parameters from editor");
        return surface;
    }

    // Helper method that uses the slider values
    static TerrainType GenerateTerrainByNoiseWithParams(
        CelestialBodyType planetType, int x, int y, int width, int height, int seed,
        float noiseScale, float elevationStr, float moistureStr, float heatStr, float ridgeStr)
    {
        float seedOffset = seed * 0.001f;

        float elevation = Mathf.PerlinNoise(x * noiseScale + seedOffset, y * noiseScale + seedOffset * 1.1f) * elevationStr;
        float moisture = Mathf.PerlinNoise((x + 100) * noiseScale * 1.5f + seedOffset, (y + 100) * noiseScale * 1.5f + seedOffset) * moistureStr;
        float heat = Mathf.PerlinNoise((x + 200) * noiseScale * 0.8f + seedOffset * 1.2f, (y + 200) * noiseScale * 0.8f + seedOffset) * heatStr;
        float ridges = Mathf.PerlinNoise((x + 300) * noiseScale * 2f + seedOffset, (y + 300) * noiseScale * 2f + seedOffset) * ridgeStr;

        // Use the same strict logic as the main method
        if (planetType == CelestialBodyType.GasGiant)
            return TerrainType.Barren;

        if (planetType == CelestialBodyType.VolcanicPlanet)
        {
            float volcanicHeat = heat + Mathf.PerlinNoise((x + 500) * 0.22f, (y + 500) * 0.22f) * 0.65f;
            float equatorBias = Mathf.Abs(y - height / 2f) / (height / 2f);

            if (volcanicHeat > 0.87f && Random.value < (0.35f - equatorBias * 0.25f))
                return TerrainType.Volcano;

            if (volcanicHeat > 0.70f)
                return TerrainType.MagmaField;

            if (ridges > 0.62f)
                return TerrainType.Mountains;

            return TerrainType.Barren;
        }

        float normalHeat = heat + Mathf.PerlinNoise((x + 500) * 0.2f, (y + 500) * 0.2f) * 0.4f;

        if (normalHeat > 0.93f && Random.value < 0.08f)
            return TerrainType.Volcano;

        if (normalHeat > 0.85f && Random.value < 0.12f)
            return TerrainType.MagmaField;

        if (ridges > 0.82f && planetType != CelestialBodyType.OceanPlanet)
            return TerrainType.Mountains;

        if (planetType == CelestialBodyType.IcePlanet)
        {
            if (elevation < 0.3f) return TerrainType.Barren;
            if (Random.value < 0.02f) return TerrainType.Crater;
            return TerrainType.Ice;
        }

        if (planetType == CelestialBodyType.OceanPlanet)
        {
            if (elevation > 0.7f) return TerrainType.Island;
            return TerrainType.Ocean;
        }

        if (planetType == CelestialBodyType.RockyPlanet)
        {
            if (ridges > 0.75f) return TerrainType.Mountains;
            if (moisture > 0.6f) return TerrainType.Forest;
            return TerrainType.Plains;
        }

        if (planetType == CelestialBodyType.Moon || planetType == CelestialBodyType.Asteroid)
        {
            if (Random.value < 0.22f) return TerrainType.Crater;
            if (Random.value < 0.10f) return TerrainType.Ice;
            return TerrainType.Barren;
        }

        return TerrainType.Barren;
    }

}