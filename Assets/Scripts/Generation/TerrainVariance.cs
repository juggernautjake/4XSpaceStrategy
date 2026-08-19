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
        // How strongly this world's relief is ACCENTUATED — not how much relief it has. The shape comes
        // from its plates and its volcanism now (see PlanetTerrainGenerator's elevation pipeline); this
        // only decides whether that shape reads as rolling country or as a dramatic one. Around 1 either
        // way, so no world is flattened out of recognition or exaggerated into spikes.
        p.elevation = Random.Range(0.80f, 1.35f);
        // Where the sea sits, rolled per world. Elevation used to double as this — a world's water came
        // out of its relief roll — so separating them left every world that does not roll an explicit
        // water level (volcanic, ice, asteroid, most moons, every non-temperate band) sharing one
        // identical sea level. This puts the per-world variety back where it now belongs.
        p.seaLevel  = Random.Range(0.36f, 0.62f);
        p.moisture  = Random.Range(0.65f, 1.45f);   // dry vs lush
        p.heat      = Random.Range(0.65f, 1.45f);   // biome temperature bias

        // RIDGE IS NO LONGER ROLLED. It used to be a per-world "ruggedness" multiplier on an independent
        // mountain-building noise field, and that field is gone: how broken a piece of ground is is now
        // DERIVED from how the geology raised it (PlanetTerrainGenerator.RidgeFromRelief). Rolling a
        // per-world multiplier on top would put the arbitrariness straight back — a world whose plates
        // built modest hills would have them promoted to mountains because a number said 1.5.
        //
        // The field itself stays at 1 rather than being removed: it is in the save format and in every
        // world's captured natural params, and a terraforming project that flattens or raises a whole
        // world is a sensible future use for exactly this multiplier.
        p.ridge     = 1f;
        body.terrainParams = p;
    }
}
