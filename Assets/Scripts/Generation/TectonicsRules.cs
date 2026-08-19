using UnityEngine;

// Whether a body has active plate tectonics, rolled once at generation from its Size and Type.
//
// This is the FIRST-CLASS ATTRIBUTE only. It does not yet build the fault-line overlay, bias
// mountain/volcano placement along fault lines specifically, drive earthquake events, or interact with
// the Mineral overlay — those all need real fault-line GEOMETRY (which plates, which edges, which
// direction each is pushing) that doesn't exist yet. What's here is the one piece that's safe and
// self-contained without that geometry: an active world folds up more mountains overall, everywhere.
public static class TectonicsRules
{
    // Same "large moon" line AtmosphereRules uses — a moon needs real mass to have working plates too.
    public const float LargeMoonSurfaceSize = AtmosphereRules.LargeMoonSurfaceSize;

    /// The smallest moon that can have working plates, in MASS. Derived from the surface-size line the
    /// atmosphere rules already draw, so the two agree by construction rather than by two constants
    /// that happen to match today: LargeMoonSurfaceSize is 9, and size is six per unit of mass.
    public const float LargeMoonMass = LargeMoonSurfaceSize / 6f;   // 1.5 Earths

    /// TAKES MASS, not surfaceSize.
    ///
    /// It used to take the size class and gate on it, and that was fine while size and mass were the
    /// same statement scaled by three. They are not the same statement any more: size saturates at 32,
    /// so every gas giant from 10 to 40 reports the identical class, and the terrestrial range now spans
    /// 0.6 to 4 where it used to span 1 to 7. Reading mass directly means the odds are stated in the
    /// same units the request states them in, and the thresholds below say what they mean.
    public static bool Roll(CelestialBodyType type, float mass)
    {
        switch (type)
        {
            // No solid crust to fracture (gas giant), or too small to hold onto internal heat (asteroid).
            case CelestialBodyType.GasGiant:
            case CelestialBodyType.Asteroid:
                return false;

            case CelestialBodyType.Moon:
                if (mass < LargeMoonMass) return false;
                // Large moons get a modest, mass-scaled chance rather than terrestrial planets' full odds.
                return Random.value < Mathf.Lerp(0.10f, 0.30f, Mathf.InverseLerp(LargeMoonMass, 4f, mass));

            default:
                // Spec §2: tectonics on terrestrial worlds ~1/5 of the time, "more likely for the larger
                // planets". Recalibrated to the Earth-relative scale: ~0.20 at an Earth mass — which IS
                // the spec's one-in-five, now stated at the world it was meant to describe — climbing
                // toward 0.55 at the 4-Earth ceiling, so bigger worlds really are more likely without
                // small ones ever being impossible.
                float sizeFactor = Mathf.InverseLerp(MassRules.TerrestrialDefault, MassRules.TerrestrialMax, mass);
                float chance = Mathf.Lerp(0.20f, 0.55f, sizeFactor);
                return Random.value < chance;
        }
    }

    // How much extra mountain-building an active world gets. Applied to the shared terrain noise's ridge
    // amplitude, which is what every biome classifier (Terran/Volcanic/Ice/Barren/Airless) already reads
    // to decide Mountains/Highlands/Canyon/CrackedGround — so one bump here shows up as more rugged
    // ground across whatever type the world happens to be, without touching each classifier separately.
    const float RidgeBoost = 1.4f;

    public static void BoostRidge(CelestialBody body)
    {
        var p = body.terrainParams;
        p.ridge = Mathf.Min(2f, p.ridge * RidgeBoost);   // 2f matches the sandbox/terraform ridge ceiling
        body.terrainParams = p;
    }
}
