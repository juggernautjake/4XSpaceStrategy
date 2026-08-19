using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// MASS — and the SOLAR SYSTEM MASS budget every system is built out of.
//
// The scale is EARTH-RELATIVE now: 1 Mass is one Earth. That single change is what makes every other
// number in here readable without a conversion table.
//
//   TERRESTRIAL   0.6 .. 4, to one decimal place. Defaults to 1 — an Earth — and rolls around it.
//   GAS GIANT     10 .. 40, in multiples of 5, clustered around 25. Jupiter is really ~318 Earths;
//                 30 stands in for it, because the point of the number is to be reasoned about.
//   ASTEROID      0.5 and below. Not a size class so much as a verdict: too little gravity to be a
//                 world, and (unless it is orbiting a planet, in which case it is a moon) that is what
//                 it gets called.
//
// ---- THE BUDGET ----------------------------------------------------------------------------
//
// A system does not get a planet count. It gets a MASS ALLOWANCE — 100 per solar mass of its star or
// stars — and it spends that allowance outward from the star until it runs out. Planets, their moons
// and every asteroid come out of the same pot, so a binary really does get to build a bigger system
// than a lone red dwarf, and a lone red dwarf really does end up with a handful of small worlds.
//
// A CEILING, NOT A TARGET. The generator is never required to spend the allowance and frequently
// doesn't: a system that rolls three modest terrestrials and stops has simply stopped. What the budget
// guarantees is the other direction — that nothing is generated past it.
// ============================================================================================
public static class MassRules
{
    // ---- The scheme ------------------------------------------------------------------------------

    public const float TerrestrialMin = 0.6f;
    public const float TerrestrialDefault = 1f;      // an Earth
    public const float TerrestrialMax = 4f;

    public const float GasGiantMin = 10f;
    public const float GasGiantMax = 40f;
    public const float GasGiantStep = 5f;            // giants come in multiples of five

    public const float AsteroidMin = 0.1f;
    /// INCLUSIVE. "Any CB with Mass of 0.5 and below that is not orbiting a planet is an asteroid."
    public const float AsteroidMax = 0.5f;

    // ---- The budget ------------------------------------------------------------------------------

    /// Solar System Mass per solar mass of star. One Sun-like star funds a hundred.
    public const float SsmPerSolarMass = 100f;

    /// Floor and ceiling on a system's allowance.
    ///
    /// The floor keeps the dimmest red dwarf from funding literally nothing — an empty system is not
    /// interesting, it is a bug the player cannot tell from one. The ceiling is aimed at the black hole
    /// (mass 14, and therefore 1400 unclamped) and at the O-type giants: a system with fourteen hundred
    /// mass to spend would lay out three dozen bodies and lay them out past where the camera can frame
    /// them. Four hundred is about four gas giants and a scatter of rock, which is already a big system.
    public const float BudgetMin = 45f, BudgetMax = 400f;

    // MEASURED (Node port of this file plus SolarSystemGenerator's lane loop, 4,000 systems each):
    //
    //   star            budget  spent      planets  giants  terrestrial  belts  moons   avg giant
    //   M dwarf  0.3       45     44 (97%)    6.6     1.5      5.1        1.9     8.4      20.7
    //   K dwarf  0.7       70     65 (93%)    7.7     2.3      5.5        1.6    10.1      22.8
    //   G / Sol  1.0      100     85 (85%)    8.6     3.0      5.6        1.2    11.7      23.7
    //   F        1.4      140     98 (70%)    9.0     3.4      5.6        1.0    12.3      24.6
    //   binary   1.7      170    100 (59%)    9.1     3.4      5.6        0.9    12.3      24.9
    //
    // A Sun-like system therefore lands on eight or nine planets, three of them giants, one belt — which
    // is our own solar system, arrived at from a mass allowance rather than from a target. A red dwarf
    // spends 97% of a much smaller allowance and gets a huddle of small worlds. The bright stars stop
    // short of their allowance because MaxLanes runs out first, which is the right way round: the budget
    // is a ceiling, and running into a different limit before reaching it is not a failure.

    /// What this system may spend, in total, on planets + moons + asteroids.
    public static float SystemBudget(StarData star)
    {
        float solar = star != null ? Mathf.Max(0.05f, star.mass) : 1f;
        return Mathf.Clamp(SsmPerSolarMass * solar, BudgetMin, BudgetMax);
    }

    public static float SystemBudget(List<StarData> stars)
    {
        if (stars == null || stars.Count == 0) return SsmPerSolarMass;
        float solar = 0f;
        foreach (var s in stars) if (s != null) solar += Mathf.Max(0.05f, s.mass);
        return Mathf.Clamp(SsmPerSolarMass * solar, BudgetMin, BudgetMax);
    }

    // ---- Quantization ----------------------------------------------------------------------------

    /// Terrestrial masses are whole numbers or one decimal place: 0.6, 1, 1.1, 2.4, 4.
    public static float QuantizeTerrestrial(float m)
        => Mathf.Clamp(Mathf.Round(m * 10f) / 10f, TerrestrialMin, TerrestrialMax);

    /// Gas giants are multiples of five, between ten and forty.
    public static float QuantizeGiant(float m)
        => Mathf.Clamp(Mathf.Round(m / GasGiantStep) * GasGiantStep, GasGiantMin, GasGiantMax);

    /// Asteroids: one decimal, 0.1 to 0.5.
    public static float QuantizeAsteroid(float m)
        => Mathf.Clamp(Mathf.Round(m * 10f) / 10f, AsteroidMin, AsteroidMax);

    /// Moons: one decimal, no upper class of their own — a moon's ceiling is its host's budget.
    public static float QuantizeMoon(float m)
        => Mathf.Max(AsteroidMin, Mathf.Round(m * 10f) / 10f);

    /// The largest giant that fits inside `cap`, rounded DOWN to the step so it can never exceed it.
    /// Returns 0 when no legal giant fits at all — the caller must fall back to a terrestrial.
    public static float GiantCeiling(float cap)
    {
        float c = Mathf.Floor(cap / GasGiantStep) * GasGiantStep;
        if (c < GasGiantMin) return 0f;
        return Mathf.Min(c, GasGiantMax);
    }

    // ---- Rolls -----------------------------------------------------------------------------------

    /// Triangular 0..1: peaked in the middle, tapering to both ends. Two uniform rolls averaged — the
    /// cheapest honest bell there is, and the shape both mass rolls want.
    ///
    /// MEASURED distributions this produces (200k rolls each):
    ///   gas giants   10:1.4%  15:11%  20:22%  25:31%  30:22%  35:11%  40:1.4%
    ///                — centred on 25, the midpoint, which is the request's "mostly around the average".
    ///   terrestrial  0.7:6%  0.8:13%  0.9:19%  1.0:13%  then a long tail to 4
    ///                — over half of all terrestrial worlds within 30% of an Earth.
    static float Bell() => (Random.value + Random.value) * 0.5f;

    /// A gas giant's mass, capped by what is left in the budget.
    ///
    /// The triangular roll is what puts most giants near 25 — "should mostly generate around the avg
    /// between the two numbers" — while still reaching 10 and 40 often enough that a system's giants
    /// differ from each other. Returns 0 when the cap cannot fund even the smallest giant.
    public static float RollGasGiant(float cap)
    {
        float ceiling = GiantCeiling(cap);
        if (ceiling <= 0f) return 0f;
        float m = QuantizeGiant(Mathf.Lerp(GasGiantMin, GasGiantMax, Bell()));
        return Mathf.Min(m, ceiling);
    }

    /// A terrestrial world's mass.
    ///
    /// `bandMax` is what this ORBITAL BAND allows — small close in, where there was never enough solid
    /// material, and the full range further out. `cap` is what the budget can still afford. The roll is
    /// centred on ONE — the request's "terrestrial planets should default to 1 Mass" expressed as a
    /// distribution rather than a constant — and clipped to whichever of the two limits binds.
    public static float RollTerrestrial(float bandMax, float cap)
    {
        float hi = Mathf.Clamp(Mathf.Min(bandMax, cap), TerrestrialMin, TerrestrialMax);
        if (hi <= TerrestrialMin) return TerrestrialMin;

        // TWO-SIDED AND ASYMMETRIC, because the band is. Earth sits 0.4 above the floor and up to 3 below
        // the ceiling, so a single symmetric spread cannot fit: sized to reach the ceiling it puts half
        // its mass below the floor, and the clamp then piles that half onto 0.6.
        //
        // That is not a rounding detail, it is the whole distribution. Measured (Node port, 200k rolls):
        // a symmetric spread produced 39% of terrestrial worlds at exactly the 0.6 floor and only 3.3% at
        // Earth mass — so "terrestrial planets default to 1 Mass" came out meaning "terrestrial planets
        // are almost always the smallest thing allowed", which is the opposite of the request.
        //
        // Stretching each side to its own limit fixes it exactly: the roll still peaks at 1 (the
        // triangular's mode), still reaches both ends, and neither end is a pile-up caused by a clamp.
        float t = Bell() * 2f - 1f;                              // -1 .. +1, peaked at 0
        float m = t < 0f
            ? TerrestrialDefault + t * (TerrestrialDefault - TerrestrialMin)
            : TerrestrialDefault + t * (hi - TerrestrialDefault);
        return Mathf.Clamp(QuantizeTerrestrial(m), TerrestrialMin, hi);
    }

    /// One asteroid.
    public static float RollAsteroid(float cap)
    {
        float hi = Mathf.Clamp(cap, AsteroidMin, AsteroidMax);
        return Mathf.Clamp(QuantizeAsteroid(Random.Range(AsteroidMin, AsteroidMax)), AsteroidMin, hi);
    }

    // ---- Moons -----------------------------------------------------------------------------------

    /// How much mass a host may spend on ITS ENTIRE MOON SYSTEM.
    ///
    /// The request's two rules, in one place: a terrestrial planet may spend half its own mass, a gas
    /// giant a tenth of its. That asymmetry is doing real work — a mass-4 super-Earth gets a 2.0
    /// allowance and could have one large moon, while a mass-40 giant gets 4.0 and typically spreads it
    /// across several. It is also, again, a CEILING: most planets spend a fraction of it and some spend
    /// none at all.
    public static float MoonBudget(float hostMass)
    {
        if (hostMass <= 0f) return 0f;
        return hostMass >= WorldClassifier.GasGiantMassFloor ? hostMass / 10f : hostMass * 0.5f;
    }

    /// Roll one moon's mass out of what is left of its host's moon allowance.
    ///
    /// Biased toward the small end, so a host that is going to have several moons still has budget left
    /// for the later ones and a single-moon world is not automatically a double planet. Returns 0 when
    /// the remaining allowance cannot fund even the smallest moon, which is the signal to stop.
    public static float RollMoon(float remaining)
    {
        if (remaining < AsteroidMin) return 0f;
        float r = Random.value; r *= r;                       // squared: the cap is rare
        float m = QuantizeMoon(Mathf.Lerp(AsteroidMin, remaining, r));
        return m > remaining ? QuantizeMoon(remaining) : m;
    }

    // ---- Derived quantities ----------------------------------------------------------------------

    /// The abstract SIZE CLASS for a body of this mass — the number a dozen rules layers gate on
    /// (AtmosphereRules, TectonicsRules, OrbitSafety, claim cost, population, spin, terraform severity).
    ///
    /// SIX PER UNIT OF MASS, not three, and that is a recalibration rather than a taste change. Under
    /// the old Earth-is-2 scale an Earth-like world came out at surfaceSize 6, and every threshold in
    /// the game was tuned against that. Earth is 1 now, so the coefficient doubles and every one of
    /// those thresholds keeps meaning exactly what it meant. A 4-mass super-Earth reads 24, a gas giant
    /// saturates the 32 ceiling, and a small moon sits on the floor.
    ///
    /// NOT the grid resolution, despite the name — MapMetrics derives the surface grid from `mass`
    /// directly (WidthForMass). This bounds a size CLASS.
    public static int SurfaceSize(float mass) => Mathf.Clamp(Mathf.RoundToInt(mass * 6f), 3, 32);

    /// Inverse of the above, for back-filling Mass on a save written before Mass existed.
    public static float FromSurfaceSize(int surfaceSize)
    {
        float m = surfaceSize / 6f;
        return m < 0.1f ? 0.1f : Mathf.Round(m * 10f) / 10f;
    }

    // ---- Visual size -----------------------------------------------------------------------------

    /// The rendered DIAMETER of a body, straight from its mass.
    ///
    /// A CUBE ROOT, not a square root, and now that gas giants run to 40 the difference matters. Mass is
    /// a volume, so at constant density diameter goes as the cube root of it — that is not a curve
    /// chosen to look right, it is the actual relationship, and it is what keeps a 40-mass giant about
    /// three and a half times an Earth's diameter instead of six and a half. Since these feed
    /// OrbitSafety, which reserves each body a band of orbital radius sized from its rendered disc, the
    /// square root would have pushed every outer orbit out far enough that a system with two giants in
    /// it no longer fit on screen.
    ///
    /// It also spreads the SMALL end better, which is the other thing this has to do: 0.1 -> 0.46 of an
    /// Earth's diameter rather than 0.32, so neighbouring small moons stay visibly different sizes.
    ///
    /// Moons use a smaller coefficient than planets, so a moon reads as a satellite rather than a twin
    /// even when its mass is a large fraction of its host's.
    public const float PlanetDiameterPerCubeRootMass = 0.62f;
    public const float MoonDiameterPerCubeRootMass = 0.44f;

    const float OneThird = 1f / 3f;

    public static float VisualDiameter(float mass, bool isMoon)
    {
        // Saves written before Mass existed can carry 0; callers back-fill from surfaceSize, but guard
        // anyway so a missing mass renders as something rather than a zero-size dot.
        if (mass <= 0.0001f) mass = 0.1f;
        float d = Mathf.Pow(mass, OneThird) * (isMoon ? MoonDiameterPerCubeRootMass : PlanetDiameterPerCubeRootMass);
        // Floors low enough that they never bind for a real body (the smallest mass is 0.1), so they are
        // a guard against bad data rather than the thing deciding how big small moons look.
        return Mathf.Max(isMoon ? 0.10f : 0.18f, d);
    }

    // ---- Readout ---------------------------------------------------------------------------------

    /// How the Mass Value reads to the player. Whole worlds and gas giants as integers, anything
    /// carrying a real fraction to one decimal — so an Earth reads "1", a super-Earth "2.4", a giant
    /// "30" and a pebble "0.3".
    public static string Format(float mass)
        => Mathf.Approximately(mass, Mathf.Round(mass)) ? mass.ToString("0") : mass.ToString("0.0");
}
