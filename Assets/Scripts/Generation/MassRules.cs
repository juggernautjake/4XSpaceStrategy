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

    // A moon's mass from its HOST planet's mass: at most 40% of the host, weighted so reaching that cap is
    // rare (most moons are small). The cap was 50%, which left a gas giant's big moons reading almost as
    // large as the planet on the map; 40% pulls moons ~20% smaller on average so a moon clearly reads as a
    // satellite of its world, not a twin. Quantized to the request's scheme — whole numbers at 1+, first-
    // decimal below 1. E.g. a size-11 gas giant caps its moons at floor(11*0.4)=4; a mass-1 planet at 0.4.
    public static float ForMoon(float hostMass)
    {
        float maxRaw = hostMass * 0.4f;
        float max = maxRaw >= 1f ? Mathf.Floor(maxRaw) : Mathf.Round(maxRaw * 10f) / 10f;
        if (max < 0.1f) max = 0.1f;                       // even a tiny host gets a 0.1 moon

        float r = Random.value; r *= r;                   // bias toward the small end; the cap is rare
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

    // ---- Visual size -----------------------------------------------------------------------------

    // The rendered DIAMETER of a body, straight from its mass.
    //
    // This used to run through SurfaceSize, and that was the bug: SurfaceSize rounds to an int and clamps
    // at 3, so every body under mass ~1 produced the same integer, and the MoonScaleMin floor then flattened
    // everything under mass ~2.3 to one identical size on top of that. A 0.5 moon and a 0.6 moon rendered
    // pixel-for-pixel the same, and so did a 0.1 and a 2.0.
    //
    // Reading mass DIRECTLY keeps the value continuous, so every difference in mass shows up as some
    // difference in size. The square root is what makes the small end readable: mass is a volume-like
    // quantity, so a linear map spends almost all its range on the giants and crushes the moons into
    // nothing. sqrt spreads it — 0.1 -> 0.32, 0.5 -> 0.71, 1 -> 1, 10 -> 3.16 — so neighbouring small
    // moons stay ~10% apart in size, which is visible, while a gas giant is still an order of magnitude
    // bigger than the smallest moon.
    //
    // Moons use a smaller coefficient than planets, so a moon reads as a satellite rather than a twin even
    // when its mass is close to its host's.
    // Reduced from 0.75 / 0.55.
    //
    // These feed OrbitSafety, which reserves each body a non-overlapping BAND of orbital radius sized
    // from its rendered disc — so making bodies bigger pushes every orbit outward, and the system stops
    // fitting on screen. The switch to mass-derived sizing roughly doubled a typical mass-2 planet (the
    // old formula floored it at 0.6; sqrt-based gave 1.06), and the orbits moved out to match. These
    // values put a typical world back near its old size while keeping the continuous scaling that makes
    // small moons distinguishable.
    public const float PlanetDiameterPerRootMass = 0.48f;
    public const float MoonDiameterPerRootMass = 0.34f;

    public static float VisualDiameter(float mass, bool isMoon)
    {
        // Saves written before Mass existed can carry 0; callers back-fill from surfaceSize, but guard
        // anyway so a missing mass renders as something rather than a zero-size dot.
        if (mass <= 0.0001f) mass = 0.1f;
        float d = Mathf.Sqrt(mass) * (isMoon ? MoonDiameterPerRootMass : PlanetDiameterPerRootMass);
        // Floors low enough that they never bind for a real body (the smallest mass is 0.1), so they are
        // a guard against bad data rather than the thing deciding how big small moons look.
        return Mathf.Max(isMoon ? 0.10f : 0.18f, d);
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
