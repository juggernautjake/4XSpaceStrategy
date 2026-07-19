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
                // "~1/3 of the time... more likely for the larger planets" (request's own words, and its
                // own note that this ratio is a starting guess to adjust later). surfaceSize now derives
                // from Mass (MassRules.SurfaceSize); the chance spans a band
                // centred close to 1/3 rather than a flat 33% for every size, so bigger worlds really are
                // more likely, and small ones are less likely without ever being impossible.
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
