using UnityEngine;

// Physically-motivated orbital defaults (Kepler's third law), tuned and CLAMPED for a stable,
// Stellaris-like feel: inner bodies orbit faster than outer ones, heavier stars pull faster orbits,
// moons key off their planet's mass — but nothing is ever fast enough to look chaotic or "fly off".
// These only set the DEFAULTS at generation; the dev can override every value afterward.
public static class OrbitalMechanics
{
    // Star mass in solar masses by spectral class.
    public static float StarMass(StarType t)
    {
        switch (t)
        {
            case StarType.O: return 30f;
            case StarType.B: return 8f;
            case StarType.A: return 2f;
            case StarType.F: return 1.3f;
            case StarType.G: return 1f;
            case StarType.K: return 0.7f;
            case StarType.M: default: return 0.4f;
        }
    }

    // Planet/moon "mass" proxy from its surface size (radius): mass ~ radius^3, normalised so a
    // size-10 world ~= 1 mass unit.
    public static float BodyMass(CelestialBody b)
    {
        float r = Mathf.Max(1f, b.surfaceSize) / 10f;
        return r * r * r;
    }

    const float SpeedK = 460f;    // maps sqrt(mass / r^3) into a pleasant deg/sec range
    const float MinDeg = 0.4f;    // never fully stops
    const float MaxDeg = 30f;     // never whips around
    const float GlobalSpeedScale = 0.5f;  // base orbits run at 50% (the time slider still scales on top)

    // Default angular speed (deg/sec) for a body orbiting a primary of the given mass at radius r.
    public static float AngularSpeedDeg(float primaryMass, float radius)
    {
        radius = Mathf.Max(1f, radius);
        float w = SpeedK * Mathf.Sqrt(primaryMass / (radius * radius * radius));
        return Mathf.Clamp(w, MinDeg, MaxDeg) * GlobalSpeedScale;
    }

    public static float PlanetAngularSpeed(StarData star, float radius)
        => AngularSpeedDeg(star != null && star.mass > 0f ? star.mass : StarMass(star != null ? star.type : StarType.G), radius);

    // Moons orbit 15% slower on average than a strict Kepler default (a calmer, more readable sky).
    public static float MoonAngularSpeed(CelestialBody planet, float radius)
        => 0.85f * AngularSpeedDeg(Mathf.Max(0.2f, BodyMass(planet)), radius);

    // Flavour value for the info panel (relative units).
    public static float OrbitalVelocity(float primaryMass, float radius)
        => 30f * Mathf.Sqrt(primaryMass / Mathf.Max(1f, radius));

    /// Orbital period, in seconds of game time — which is DAYS on the game calendar (GameCalendar), so a
    /// world with a 90-second orbit has a 90-day year. The readouts run it through GameCalendar.Duration.
    public static float PeriodSeconds(float angularSpeedDeg)
        => angularSpeedDeg > 0.001f ? 360f / angularSpeedDeg : 0f;

    /// AXIAL SPIN IS NO LONGER ROLLED HERE — see RotationRules.Roll.
    ///
    /// This gave smaller worlds a faster spin, which was a reasonable placeholder while rotation was
    /// decoration. It is not any more: rotation is what drives a world's magnetic field, and a rule that
    /// said "smaller is faster" handed every moon and asteroid a magnetosphere and denied one to every
    /// gas giant — the exact inverse of the truth. RotationRules rolls two populations (spun-up and
    /// tidally braked) with the odds weighted by mass, which is the mechanism this was standing in for.
    ///
    /// Kept because it is a fair one-line answer to "what should this thing's spin be" for any future
    /// caller that has no CelestialBody to hand, and because deleting it would be a diff with no reader.
    /// Nothing in generation calls it.
    public static float Spin(CelestialBody b, float variance)
        => Mathf.Clamp(60f / Mathf.Sqrt(Mathf.Max(1f, b.surfaceSize)) * variance, 3f, 40f);
}
