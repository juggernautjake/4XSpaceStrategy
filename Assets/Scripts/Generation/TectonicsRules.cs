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

    public static bool Roll(CelestialBodyType type, int surfaceSize)
    {
        switch (type)
        {
            // No solid crust to fracture (gas giant), or too small to hold onto internal heat (asteroid).
            case CelestialBodyType.GasGiant:
            case CelestialBodyType.Asteroid:
                return false;

            case CelestialBodyType.Moon:
                if (surfaceSize < LargeMoonSurfaceSize) return false;
                // Large moons get a modest, size-scaled chance rather than terrestrial planets' full odds.
                return Random.value < Mathf.Lerp(0.10f, 0.30f, Mathf.InverseLerp(LargeMoonSurfaceSize, 13f, surfaceSize));

            default:
                // Spec §2: tectonics on terrestrial worlds ~1/5 of the time, "more likely for the larger
                // planets" (and flagged as a starting guess to tune later). surfaceSize derives from Mass
                // (MassRules.SurfaceSize), so the chance is size-scaled rather than flat: ~0.20 at the small
                // end — a typical Earth-mass world, which is the spec's 1/5 — climbing toward 0.55 for the
                // largest, so bigger worlds really are more likely without small ones ever being impossible.
                float sizeFactor = Mathf.InverseLerp(5f, 23f, surfaceSize);
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
