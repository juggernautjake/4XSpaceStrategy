using UnityEngine;

// Gives every world its own terrain "personality" by randomizing its noise parameters within safe
// bounds. Combined with the per-body random terrainSeed, this ensures no two planets or moons look
// the same (different coastlines, mountain density, wetness and temperature bias).
public static class TerrainVariance
{
    public static void Apply(CelestialBody body)
    {
        var p = PlanetTerrainGenerator.NoiseParams.Default;
        p.scale     = Random.Range(0.75f, 1.55f);   // feature density (continent size)
        p.elevation = Random.Range(0.80f, 1.35f);   // how much RELIEF: flat plains vs deep valleys
        // Where the sea sits, rolled per world. Elevation used to double as this — a world's water came
        // out of its relief roll — so separating them left every world that does not roll an explicit
        // water level (volcanic, ice, asteroid, most moons, every non-temperate band) sharing one
        // identical sea level. This puts the per-world variety back where it now belongs.
        p.seaLevel  = Random.Range(0.36f, 0.62f);
        p.moisture  = Random.Range(0.65f, 1.45f);   // dry vs lush
        p.heat      = Random.Range(0.65f, 1.45f);   // biome temperature bias
        p.ridge     = Random.Range(0.60f, 1.50f);   // mountain ruggedness
        body.terrainParams = p;
    }
}
