using UnityEngine;

// A body's MASS VALUE — the player-facing measure of how big a world is, and the thing the grid/visual
// SIZE is now derived from. Deliberately kept to easy numbers (the request's ask):
//
//   * An Earth-like terrestrial world is ~2. Terrestrial worlds run 1..7, gas giants 7..13.
//   * Values of 1 and up are WHOLE numbers (no fractions to reason about).
//   * Moons and asteroids sit below the planets, in first-decimal steps only: 0.1, 0.2 … 0.9.
//   * An asteroid is 0.1..0.4. A moon's mass comes from its host planet: at most half the host, and it's
//     rare to actually reach that half.
//
// Stars already carry their own mass (StarData.mass, in solar masses) — that's left alone; this is the
// small-body / planet scale.
public static class MassRules
{
    // The mass a freshly-generated body of this TYPE is born with. Ranges chosen so that SurfaceSize()
    // below reproduces roughly the world sizes the game had before (gas giants biggest, and Earth-like
    // rocky worlds around 2). Moons are NOT rolled here — they derive from their host (see ForMoon).
    public static float ForType(CelestialBodyType type)
    {
        switch (type)
        {
            case CelestialBodyType.GasGiant:       return Random.Range(7, 14);      // 7..13, ~10 typical
            case CelestialBodyType.OceanPlanet:    return Random.Range(2, 8);       // 2..7
            case CelestialBodyType.IcePlanet:      return Random.Range(2, 8);       // 2..7
            case CelestialBodyType.RockyPlanet:    return Random.Range(1, 7);       // 1..6 (Earth = 2)
            case CelestialBodyType.VolcanicPlanet: return Random.Range(1, 6);       // 1..5
            case CelestialBodyType.BarrenPlanet:   return Random.Range(1, 6);       // 1..5
            case CelestialBodyType.Asteroid:       return Random.Range(1, 5) * 0.1f; // 0.1..0.4
            default:                               return 2f;                        // fallback (incl. bare Moon)
        }
    }

    // A moon's mass from its HOST planet's mass: at most half the host, weighted so reaching that half is
    // rare (most moons are small). Quantized to the request's scheme — whole numbers at 1+, first-decimal
    // below 1. E.g. a size-11 gas giant caps its moons at floor(11/2)=5; a mass-1 planet caps its moons at
    // 0.5.
    public static float ForMoon(float hostMass)
    {
        float maxRaw = hostMass * 0.5f;
        float max = maxRaw >= 1f ? Mathf.Floor(maxRaw) : Mathf.Round(maxRaw * 10f) / 10f;
        if (max < 0.1f) max = 0.1f;                       // even a tiny host gets a 0.1 moon

        float r = Random.value; r *= r;                   // bias toward the small end; half-the-host is rare
        float raw = Mathf.Lerp(0.1f, max, r);
        float m = raw >= 1f ? Mathf.Floor(raw) : Mathf.Round(raw * 10f) / 10f;
        return Mathf.Clamp(m, 0.1f, max);
    }

    // The grid / visual SIZE for a body of this mass — the same surfaceSize the whole engine already sizes
    // maps, orbits and atmosphere from (MapMetrics, OrbitSafety, AtmosphereRules …), now a function of Mass
    // rather than an independent roll. Deliberately PROPORTIONAL (no base offset): Earth-like ~6, gas giants
    // (mass 7..13) ~21..32, tiny moons/asteroids at the floor. Proportionality matters because the moon /
    // tectonics rules gate on surfaceSize thresholds (e.g. LargeMoonSurfaceSize = 9 ⇔ mass 3), so a body's
    // size in cells must track its mass honestly rather than being flattened by an offset. Clamped to the
    // range MapMetrics resolves.
    public static int SurfaceSize(float mass)
    {
        return Mathf.Clamp(Mathf.RoundToInt(mass * 3f), 3, 32);
    }

    // Inverse of SurfaceSize, for backfilling Mass on a save written before Mass existed: the world's size
    // is known, so recover the mass that would have produced it. Quantized to the same scheme.
    public static float FromSurfaceSize(int surfaceSize)
    {
        float m = surfaceSize / 3f;
        if (m < 0.1f) m = 0.1f;
        return m >= 1f ? Mathf.Round(m) : Mathf.Round(m * 10f) / 10f;
    }

    // How the Mass Value reads to the player: whole worlds as integers, small bodies to one decimal.
    public static string Format(float mass) => mass >= 1f ? mass.ToString("0") : mass.ToString("0.0");
}
