using UnityEngine;

// ============================================================================================
// ATMOSPHERE — measured in ATMOSPHERES, where 1 is Earth-normal.
//
// This used to be a 0..1 "thickness" abstraction rolled from surfaceSize and type. It is now a real
// quantity driven by MASS, because that is what actually holds gas onto a world: a body's ceiling is
// one atmosphere per unit of mass, modified by whether it has a magnetic field to stop the stellar
// wind stripping it, whether active tectonics are outgassing more, and how hot it is.
//
//   * A mass-6 world with a magnetic field holds ~6 atmospheres.
//   * The same world with no field holds ~3 — no shield, half the air.
//   * A mass-1 world with a field holds ~1; without, ~0.5.
//   * Gas giants run to their mass, ~7..13.
//   * Asteroids hold nothing at all, ever.
//
// WHY THICKNESS STILL EXISTS. Roughly thirty call sites read atmosphereThickness as a 0..1 value —
// the greenhouse term, the terrain classifier, the survey indexes. Rather than rewrite all of them
// against a new unit, thickness is now DERIVED from atmospheres (CelestialBody.atmosphereThickness),
// and the divisor below is calibrated so nothing that was tuned against the old numbers moves.
// ============================================================================================
public static class AtmosphereRules
{
    /// Atmospheres that map to a fully-saturated 0..1 thickness.
    ///
    /// NOT a free parameter. Under the old size-driven model a mass-3 world — the default planet mass —
    /// got 0.15 + surfaceSize*0.03 = 0.42 thickness. Three atmospheres over this divisor gives 0.43, so
    /// the greenhouse, the biome classifier and the homeworld biosphere fix all keep the tuning they
    /// were built against. Saturating at 7 is also the right SHAPE: a greenhouse effect does not keep
    /// climbing linearly once the air is already opaque to infrared.
    public const float ThicknessReference = 7f;

    /// Below this there is no liquid surface water and no biosphere, whatever the other sliders say.
    /// The spec's hard floor: "0.6 Atmosphere is the absolute lowest Threshold for Water and BioSphere".
    public const float LifeFloor = 0.6f;

    /// No magnetic field halves what a world can hold. The field is what stops the stellar wind
    /// stripping the upper atmosphere — Mars against Earth, in one number.
    public const float NoFieldPenalty = 0.5f;

    /// Mass at or above which a world is likely to have a working dynamo.
    public const float FieldMassThreshold = 2f;

    // Tectonic outgassing, in whole atmospheres.
    public const float TectonicBonusMin = 1f;
    public const float TectonicBonusMax = 2f;

    // Kept because TectonicsRules and the moon rules still gate on "is this a large moon" for reasons
    // that have nothing to do with air (plate activity, terrain variety). Atmosphere itself no longer
    // uses it — mass decides that now, for moons exactly as for planets, which is what lets a big moon
    // of a gas giant end up thicker-aired than Earth.
    public const float LargeMoonSurfaceSize = 9f;

    // ---- Magnetic field ------------------------------------------------------------------------

    /// Rolled once at generation. Mass is the driver: a big body keeps its core molten and turning, a
    /// small one froze solid long ago.
    public static bool RollMagneticField(CelestialBodyType type, float mass)
    {
        // A gas giant's field is enormous and never in question; an asteroid has no core to speak of.
        if (type == CelestialBodyType.GasGiant) return true;
        if (type == CelestialBodyType.Asteroid) return false;

        float chance = mass >= FieldMassThreshold
            ? Mathf.Lerp(0.55f, 0.90f, Mathf.InverseLerp(FieldMassThreshold, 8f, mass))
            : Mathf.Lerp(0.02f, 0.20f, Mathf.InverseLerp(0.1f, FieldMassThreshold, mass));

        return Random.value < chance;
    }

    // ---- Ceiling -------------------------------------------------------------------------------

    /// Extra atmospheres a tectonically active world outgasses, 1..2.
    ///
    /// DETERMINISTIC, from the body's own terrainSeed rather than Random.value. The ceiling is read
    /// every time the UI draws a readout or terraforming checks a limit; a fresh roll each call would
    /// make a world's maximum atmosphere flicker frame to frame. Keying it to terrainSeed also means it
    /// survives save/load for free (the seed is serialized) and re-rolls when the sandbox's Randomize
    /// re-rolls the seed, which is the behaviour a dev sandbox wants.
    public static float TectonicBonus(CelestialBody b)
    {
        if (b == null || !b.hasTectonics) return 0f;
        return Mathf.Lerp(TectonicBonusMin, TectonicBonusMax, Hash01(b.terrainSeed));
    }

    /// A stable 0..1 from a seed. Sin-based rather than a hash of the bits, to match how the rest of
    /// the generation code derives repeatable variation from terrainSeed.
    static float Hash01(float seed)
    {
        float v = Mathf.Sin(seed * 12.9898f + 78.233f) * 43758.5453f;
        return Mathf.Clamp01(v - Mathf.Floor(v));
    }

    /// The most atmosphere this world could hold, before heat is taken into account.
    public static float Ceiling(CelestialBody b)
    {
        if (b == null) return 0f;
        if (b.type == CelestialBodyType.Asteroid) return 0f;

        float baseline = Mathf.Max(0f, b.mass);
        if (!b.hasMagneticField) baseline *= NoFieldPenalty;
        return baseline + TectonicBonus(b);
    }

    /// The same ceiling, from loose values — for generation, which is deciding these attributes in the
    /// order mass -> field -> tectonics and does not have a finished body to hand yet.
    public static float Ceiling(CelestialBodyType type, float mass, bool magneticField, float tectonicBonus)
    {
        if (type == CelestialBodyType.Asteroid) return 0f;
        float baseline = Mathf.Max(0f, mass);
        if (!magneticField) baseline *= NoFieldPenalty;
        return baseline + Mathf.Max(0f, tectonicBonus);
    }

    // ---- Boil-off ------------------------------------------------------------------------------

    /// What fraction of its ceiling a world of this mass actually keeps at this heat.
    ///
    /// Heat is the terrain slider, 1.0 being temperate. Nothing boils off at or below temperate. Above
    /// it, gravity is what decides whether the world holds on: a heavy world tolerates real heat
    /// without losing anything, a light one starts venting almost immediately.
    ///
    /// The tolerance curve is anchored to the spec: "Larger Mass celestial bodies (4 and up) can have a
    /// higher temperature (closer to 2.2 on the slider) and not lose Atmosphere" — so mass 4 tolerates
    /// 1.25 of excess heat, which is exactly heat 2.25.
    public static float HeatRetention(float mass, float heat)
    {
        float excess = Mathf.Max(0f, heat - 1f);
        if (excess <= 0f) return 1f;

        float tolerated = Mathf.Lerp(0.20f, 1.25f, Mathf.InverseLerp(1f, 4f, mass));
        float over = Mathf.Max(0f, excess - tolerated);
        if (over <= 0f) return 1f;

        // Never all the way to zero from heat alone — a hot world keeps a residue (Venus is hotter than
        // Mercury and has vastly MORE air, because it is heavier). Falling to nothing would also make
        // "no atmosphere" the single most common outcome on any warm inner world, which is wrong.
        float loss = Mathf.Clamp01(over / 0.8f) * 0.85f;
        return 1f - loss;
    }

    // ---- Generation ----------------------------------------------------------------------------

    /// What a freshly-generated body is born with: its ceiling, cut by heat, with a little variance so
    /// two identical worlds aren't identical. The spec's own "give or take".
    public static float RollAtmospheres(CelestialBodyType type, float mass, bool magneticField,
                                        float tectonicBonus, float heat)
    {
        float ceiling = Ceiling(type, mass, magneticField, tectonicBonus);
        if (ceiling <= 0f) return 0f;

        float kept = ceiling * HeatRetention(mass, heat) * Random.Range(0.85f, 1.0f);
        return Quantize(kept);
    }

    /// One decimal. Atmospheres are a headline statistic shown next to Mass, and "3.4 atmospheres" reads
    /// as a fact where "3.3871293" reads as a bug.
    public static float Quantize(float atmospheres) =>
        Mathf.Max(0f, Mathf.Round(atmospheres * 10f) / 10f);

    // ---- Readouts ------------------------------------------------------------------------------

    /// Can this world hold liquid surface water and living things at all? The 0.6 floor.
    public static bool SupportsLife(CelestialBody b) => b != null && b.atmospheres >= LifeFloor;

    /// How much of a world's surface water survives its atmosphere.
    ///
    /// Water does not vanish the instant the air thins past the floor — it fades out across the last
    /// stretch, so a world sitting just under the line is visibly drying rather than abruptly bone dry.
    /// At zero atmosphere there is no liquid water at all.
    public static float WaterRetention(float atmospheres)
    {
        if (atmospheres >= LifeFloor) return 1f;
        return Mathf.Clamp01(atmospheres / LifeFloor);
    }

    /// Dry out a world whose air is too thin to keep liquid water on its surface, and sterilise it.
    ///
    /// The spec's rule: below 0.6 atmospheres water and biosphere both start to disappear. Applied at
    /// generation AFTER the atmosphere is rolled, so a thin-aired world does not ship with oceans on it
    /// that physics says should have boiled into space long ago.
    ///
    /// Water level is stored as terrain SEA LEVEL, so this pulls sea level down rather than editing a
    /// water field that does not exist — going through PlanetTerrainGenerator's own conversion so there
    /// is exactly one definition of what "water level" means.
    public static void ApplyWaterLoss(CelestialBody b)
    {
        if (b == null) return;

        float keep = WaterRetention(b.atmospheres);
        if (keep >= 1f) return;

        var p = b.terrainParams;
        float water = PlanetTerrainGenerator.WaterLevelFromSeaLevel(p.SeaLevelOrNeutral);
        p.seaLevel = PlanetTerrainGenerator.SeaLevelFromWaterLevel(water * keep);
        b.terrainParams = p;

        // No air, no life. A world that lost its oceans lost whatever was living in them.
        if (b.atmospheres < LifeFloor) b.biosphereActive = false;
    }

    public static string Describe(CelestialBody b)
    {
        if (b == null) return "unknown";
        float a = b.atmospheres;
        if (a <= 0.01f) return "vacuum";
        if (a < 0.3f) return "trace";
        if (a < LifeFloor) return "wisp-thin";
        if (a < 0.9f) return "thin";
        if (a < 1.6f) return "Earth-normal";
        if (a < 3f) return "heavy";
        if (a < 5f) return "thick";
        if (a < 9f) return "crushing";
        return "gas-giant deep";
    }
}
