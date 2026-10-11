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
//   * Gas giants run to their mass, 10..40.
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

    /// THE FIELD IS NO LONGER ROLLED HERE — see RotationRules.GeneratesField.
    ///
    /// It used to be a coin flip weighted by mass, and that was a stand-in for the real mechanism: a
    /// dynamo needs a conducting fluid core AND rotation to stir it, and rotation is the half that
    /// varies between worlds that are otherwise identical. Venus and Earth are nearly the same size and
    /// made of nearly the same things; Earth turns once a day and has a magnetosphere, Venus takes 243
    /// days and has essentially none. Mass still decides most of it — a bigger world is likelier to have
    /// spun up and less likely to have been tidally braked — but through the rotation roll rather than
    /// instead of it, which is what makes the Dev panel's rotation slider mean something.
    ///
    /// Kept as a thin forward rather than deleted: `FieldMassThreshold` below is still read by the save
    /// loader's migration path for files written before either mechanism existed, and leaving a function
    /// that answers the same question two different ways next to it is how the two drift apart.
    public static bool RollMagneticField(CelestialBodyType type, float mass)
        => RotationRules.GeneratesField(type, mass, RotationRules.Roll(mass, type == CelestialBodyType.Moon));

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

    /// The most atmosphere this world could hold, before HEAT boil-off is taken into account — but AFTER
    /// the orbital capacity cut, because that is a hard limit rather than a transient loss (see below).
    public static float Ceiling(CelestialBody b)
    {
        if (b == null) return 0f;
        if (b.type == CelestialBodyType.Asteroid) return 0f;

        float baseline = Mathf.Max(0f, b.mass);
        if (!b.hasMagneticField) baseline *= NoFieldPenalty;

        // Spec §3: a body orbiting CLOSER than the habitable zone has its atmosphere CAPACITY lowered,
        // harder the nearer the star — the stellar wind and radiation of a close orbit strip air faster
        // than temperature alone. Unlike heat boil-off (transient, applied at roll time and reversible by
        // cooling the world), this is a standing cap: it is the limit terraforming reads, and it is what
        // stops a scorched inner world being pumped back up to its full mass-based air without first moving
        // its orbit outward. InnerOrbitRetention is 1 at and beyond the zone's inner edge, so temperate and
        // outer worlds are unaffected.
        return (baseline + TectonicBonus(b)) * InnerOrbitRetention(WorldClassifier.RelOf(b));
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

    // ---- What terraforming can actually reach ---------------------------------------------------

    /// The most air a world can HOLD AS IT STANDS TODAY: its ceiling, cut by the heat it is actually at.
    ///
    /// This is the limit TERRAFORMING reads, and it is deliberately stricter than Ceiling. Ceiling is the
    /// standing structural cap — mass, magnetic field, tectonics, orbital distance. Heat is the reversible
    /// one: generation applies it at roll time (RollAtmospheres), so a hot world is already born with less
    /// air than its ceiling. Without the same cut here, an atmosphere project could pump that world
    /// straight back up to a ceiling physics says it cannot keep — the identical bug the inner-orbit
    /// review fix closed, one term over.
    ///
    /// Being reversible is the point: shading a world cools it, which RAISES this number, which is what
    /// makes "hang the shades, THEN build the air" a real dependency rather than flavour text.
    public static float SustainableCeiling(CelestialBody b)
    {
        if (b == null) return 0f;
        return Ceiling(b) * HeatRetention(Mathf.Max(0f, b.mass), b.terrainParams.heat);
    }

    /// How much room is left between the air a world HAS and the air it can keep. Zero means an
    /// atmosphere project would deliver nothing at all, whatever it cost.
    public static float Headroom(CelestialBody b)
        => b == null ? 0f : Mathf.Max(0f, SustainableCeiling(b) - b.atmospheres);

    /// Less headroom than this and a project is not worth starting — it is below the one-decimal
    /// resolution atmospheres are quantized and displayed at, so the readout would not even move.
    public const float MinUsefulGain = 0.1f;

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

    /// The same roll, then cut for a world orbiting CLOSER TO THE STAR than the habitable zone.
    ///
    /// The spec's step 3: "If a celestial body is closer to the sun than the habitable zone, the Maximum
    /// Atmosphere capacity will be lowered drastically the closer it is." Heat retention already thins a
    /// hot world's air, but this is a separate, stronger effect — the stellar wind and radiation of a
    /// close orbit strip an atmosphere faster than temperature alone would (Mercury holds essentially
    /// none despite not being that much hotter than a thick-aired Venus further out).
    ///
    /// `rel` is distance / the star's Earth-warmth distance. At or beyond the zone's inner edge there is
    /// no penalty; inside it, capacity falls off toward the star.
    public static float RollAtmospheres(CelestialBodyType type, float mass, bool magneticField,
                                        float tectonicBonus, float heat, float rel)
    {
        float a = RollAtmospheres(type, mass, magneticField, tectonicBonus, heat);
        return Quantize(a * InnerOrbitRetention(rel));
    }

    /// Fraction of atmosphere a world keeps against the stellar wind at its orbital distance. 1 at and
    /// beyond the habitable zone's inner edge; falling toward the star inside it.
    public static float InnerOrbitRetention(float rel)
    {
        const float ZoneInner = StarDatabase.HzInnerRel;   // the zone's own edge, shared everywhere
        if (rel >= ZoneInner) return 1f;
        return Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(rel / ZoneInner));
    }

    // ============================================================================================
    // THE RUNAWAY GREENHOUSE — why an inner world can have MORE air, not less (2026-10-10)
    //
    // "An inner planet that close to its sun should be a very hot world. Venus-like probably."
    //
    // Everything above thins an inner world's air: heat boils it off (HeatRetention), the stellar wind
    // strips it (InnerOrbitRetention), and thin air loses its water (ApplyWaterLoss). That is Mercury,
    // and it is right for most of them. But it made Mercury the ONLY thing inside the zone — every inner
    // world came out scorched or volcanic, and the game only calls a sky toxic past four atmospheres,
    // which no small hot world kept.
    //
    // Venus is the other outcome of the same orbit. A world big enough to hang on to its volatiles,
    // inside the moist-greenhouse limit, boils its oceans into the sky; the water vapour traps more heat,
    // which boils more water, and the loop only stops when the seas are gone — leaving the carbon dioxide
    // that would otherwise have been locked back into rock by liquid water as a sky ninety atmospheres
    // deep. So this is not a thicker version of the ceiling above: it is the world's whole carbon
    // inventory in the air, which is why it is rolled OUTRIGHT, not through Ceiling.
    //
    //   WHERE    inside the zone's inner edge only (rel < HzInnerRel) — the runaway regime InsolationRules
    //            already models. Rings 2-3 GreenhouseChance; ring 1 (inside ScorchingRel) far less,
    //            because that close the young star's wind takes the steam before it can build a lid.
    //            Never in or beyond the zone: there the oceans stay oceans and the carbon goes into rock.
    //   WHO      a terrestrial body of at least GreenhouseMinMass: a small world loses the hydrogen AND
    //            the heavier gases (Mars). Planets and large moons alike.
    //   WHAT     30-90 atmospheres (PlanetTemperature.DeepGreenhouseC turns it into 300-750 °C), the
    //            seas gone to space (a token of water at most, and it is steam), no biosphere, and NO
    //            PLATE TECTONICS — with the water gone the crust has nothing to lubricate subduction, and
    //            the world seizes into a stagnant lid, as Venus has. That is also why it classifies as a
    //            BARREN world rather than a volcanic one, and is named "greenhouse world" (WorldClassifier).
    //
    // DERIVED, NEVER FLAGGED. IsRunawayGreenhouse reads the pressure, which is saved; there is no new
    // field to migrate, and a world thinned below the line by terraforming simply stops being one.
    // ============================================================================================

    /// The least mass that can keep a runaway sky. Mars-class worlds lose it; Venus is 0.82.
    public const float GreenhouseMinMass = 0.7f;

    /// Chance of a runaway greenhouse for a qualifying world on rings 2-3 (ScorchingRel .. HzInnerRel).
    public const float GreenhouseChance = 0.42f;

    /// ...and right by the star (rel at or inside ScorchingRel, ring 1), falling to GreenhouseChanceFloor
    /// at GreenhouseInnermostRel.
    public const float GreenhouseChanceRingOne = 0.16f;
    public const float GreenhouseChanceFloor = 0.06f;
    public const float GreenhouseInnermostRel = 0.25f;

    /// The pressure a runaway world is born with, atmospheres. Venus is the top.
    public const float GreenhouseAtmospheresMin = 30f;
    public const float GreenhouseAtmospheresMax = 90f;

    /// At or above this a terrestrial world's sky IS a runaway greenhouse. Well clear of anything a mass
    /// can hold (the Ceiling tops out near six) and of the Pyrothians' seven, and well under the 30 a
    /// runaway is born with, so terraforming has to bleed off most of one before it stops counting.
    public const float GreenhouseThresholdAtmospheres = 20f;

    /// Does this body carry a runaway-greenhouse sky? Derived from its pressure; never a gas giant.
    public static bool IsRunawayGreenhouse(CelestialBody b)
        => b != null && b.type != CelestialBodyType.GasGiant && b.type != CelestialBodyType.Asteroid
           && b.atmospheres >= GreenhouseThresholdAtmospheres;

    /// The chance a world of `mass` at `rel` (distance / the star's Earth-warmth distance) runs away.
    /// Zero at and beyond the zone's inner edge. Pure.
    public static float RunawayGreenhouseChance(float mass, float rel)
    {
        if (mass < GreenhouseMinMass || rel >= StarDatabase.HzInnerRel) return 0f;
        if (rel > WorldClassifier.ScorchingRel) return GreenhouseChance;
        return Mathf.Lerp(GreenhouseChanceFloor, GreenhouseChanceRingOne,
                          Mathf.InverseLerp(GreenhouseInnermostRel, WorldClassifier.ScorchingRel, rel));
    }

    /// Roll the runaway for a freshly generated (or re-derived) body and, if it happens, apply it: the
    /// deep sky, the seas boiled off, no biosphere, a stagnant lid. Returns whether it happened.
    ///
    /// Call AFTER RollAtmospheres (it replaces that figure) and BEFORE the classifier, which then names
    /// the world Barren from its lack of plates. Draws from UnityEngine.Random only for bodies that can
    /// run away at all, so the generation stream of every other world is untouched.
    public static bool RollRunawayGreenhouse(CelestialBody b, float rel)
    {
        if (b == null || b.type == CelestialBodyType.GasGiant || b.type == CelestialBodyType.Asteroid) return false;
        float chance = RunawayGreenhouseChance(b.mass, rel);
        if (chance <= 0f || Random.value >= chance) return false;

        b.atmospheres = Quantize(Random.Range(GreenhouseAtmospheresMin, GreenhouseAtmospheresMax));

        // The oceans went into the sky and then to space. A token at most, under BiosphereRules'
        // MinWaterLevel so nothing can count it as a sea — and at these temperatures it is steam anyway
        // (the deep greenhouse puts even ring 3 well over water's boiling point at this pressure).
        var p = b.terrainParams;
        p.seaLevel = Mathf.Min(p.SeaLevelOrNeutral, 0.04f);
        b.terrainParams = p;

        b.biosphereActive = false;
        b.hasTectonics = false;     // the stagnant lid — see the header
        return true;
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

        // FROZEN WATER IS ICE, AND ICE DOES NOT BOIL OFF. A cold, airless world keeps its water as a
        // frozen shell (Europa, Pluto) — the "thin air strips the oceans" rule only applies where the
        // water would be LIQUID and could actually evaporate. Without this, every cold outer world with a
        // big water roll dried to bare rock the instant its (naturally thin) air fell under the floor, so
        // ice worlds could barely generate. Type-independent temperature, for the same reason
        // WorldClassifier uses it — the type is not settled yet when this runs.
        float c = PlanetTemperature.BaseCelsius(b.terrainParams.heat, b.atmosphereThickness, CelestialBodyType.RockyPlanet);
        if (c < 0f) return;

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
        // Past anything a mass can hold, on a world with ground under it, the only way there is a runaway.
        if (b.type != CelestialBodyType.GasGiant) return IsRunawayGreenhouse(b) ? "runaway greenhouse" : "crushing";
        return "gas-giant deep";
    }
}
