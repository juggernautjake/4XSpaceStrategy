using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WORLD CONSISTENCY — does every world make sense where it orbits?
//
// "Mirfak II, despite being very near to the sun, has an average surface temperature of 35 C. The
// habitability is 60% and there is greenery on this planet. Mirfak III is actually hotter. Planets
// that are predetermined types still need to make sense where they are placed within a solar system."
//
// What had happened: the home system was generated around a red dwarf, and ForceHomeWorld then swapped
// the species' G-type sun in underneath it (fixed at the source — SolarSystemGenerator.forcedStars).
// The red dwarf's temperate world kept its grassland while orbiting inside the new sun's runaway zone.
// Nothing downstream noticed, because nothing downstream ever asked the question this class asks.
//
// It runs once, at the end of GalaxyGenerator.Finish, over every body in the galaxy, and checks the two
// facts the request turns on:
//
//   STARLIGHT    a world's heat must be one its orbit could give it — inside InsolationRules.HeatBand,
//                with a little slack (HeatSlack) for the outward nudges OrbitSafety makes after the
//                climate is rolled. This is the rule Mirfak II broke: heat 1.1 (30 °C from starlight)
//                on an orbit whose floor is 1.86 (120 °C).
//
//   THE ZONE     outside the habitable zone, no temperate type (Rocky/Ocean), no living biosphere and,
//                for liquid-water species, no rating that calls the world a home — UNLESS its actual
//                temperature (starlight already validated, plus its greenhouse and internal heat) really
//                is in the liquid band. That exception is recorded on the body (climateException) with
//                the reason, so a warm world outside the green ring is always explained.
//
//   THE RUNAWAY  a Venus-deep sky (AtmosphereRules.IsRunawayGreenhouse) belongs inside the zone's inner
//                edge and nowhere else. There it is recorded as the world's climateException (its sky,
//                not its starlight, is why it reads 300-750 °C); at or beyond the edge it is a problem
//                and the world is re-derived from its orbit. (2026-10-10)
//
// And for the home system: every starting option must lie inside the star's habitable zone — or, for a
// species whose climate is hotter than the zone gives, inside Habitability.CradleZone (2026-10-10).
//
// REPAIR, THEN LOG. A natural world that breaks a rule is re-derived from its orbit
// (SolarSystemGenerator.ReconcileClimate); a forced one — the cradle, a second starting option — has its
// heat clamped into its orbit's band, since its placement already chose a suitable orbit. Violations are
// logged in development builds (Debug.isDebugBuild, which includes the editor), repaired in every build.
// `Problem` is pure and is what the EditMode tests (Assets/Tests/Editor) assert against.
// ============================================================================================
public static class WorldConsistency
{
    /// How far (a fraction of heat) a world may sit outside its orbit's band before it counts. OrbitSafety
    /// pushes a crowded world outward after its climate was rolled; a 3% heat difference is a 1.5%
    /// change of orbit, which is the scale of those pushes and nowhere near a change of regime.
    public const float HeatSlack = 0.03f;

    /// A rating at or above this, for a liquid-water species, says "this is a home". Outside the zone
    /// that needs an explanation.
    public const float HomelyRating = 50f;

    /// What, if anything, about `b` contradicts its orbit around `star`. Null if nothing does. If the
    /// world breaks the zone rule for a real physical reason, the reason comes back in `exception` and
    /// the result is null. Pure: reads the body, changes nothing.
    public static string Problem(CelestialBody b, StarData star, Species species, out string exception)
    {
        exception = null;
        if (b == null || star == null || star.isBlackHole) return null;

        float rel = InsolationRules.RelOf(star, b.distanceFromStar);
        float heat = b.terrainParams.heat;
        if (InsolationRules.OrbitMismatch(heat, rel) > HeatSlack)
        {
            InsolationRules.HeatBand(rel, out float lo, out float hi);
            return $"starlight heat {heat:0.00} is outside what its orbit gives ({lo:0.00}-{hi:0.00} at {rel:0.00}x the Earth-warmth distance)";
        }

        if (!star.hasHabitableZone) return null;
        if (b.type == CelestialBodyType.GasGiant || b.type == CelestialBodyType.Asteroid) return null;

        // A RUNAWAY GREENHOUSE (AtmosphereRules.RollRunawayGreenhouse) is a fact about the runaway
        // regime: inside the zone's inner edge it is the recorded reason the world is hotter than its
        // starlight, and anywhere else it is a sky no orbit there could have built.
        if (AtmosphereRules.IsRunawayGreenhouse(b))
        {
            if (rel >= StarDatabase.HzInnerRel)
                return $"a {b.atmospheres:0}-atmosphere runaway greenhouse at {rel:0.00}x the Earth-warmth distance, where no runaway happens";
            exception = $"runaway greenhouse: {b.atmospheres:0} atmospheres add {PlanetTemperature.GreenhouseCelsius(b):0} °C " +
                        $"to its starlight ({PlanetTemperature.BodyAverageCelsius(b):0} °C)";
            return null;
        }

        if (StarDatabase.InZone(star, b.distanceFromStar)) return null;

        bool temperateType = b.type == CelestialBodyType.RockyPlanet || b.type == CelestialBodyType.OceanPlanet;
        bool living = b.biosphereActive;
        bool homely = species != null && GalaxyGenerator.CradleWantsLife(species) && b.habitability >= HomelyRating;
        if (!temperateType && !living && !homely) return null;

        float c = PlanetTemperature.BodyAverageCelsius(b);
        if (BiosphereRules.HasLiquidWaterClimate(b))
        {
            // Its starlight checked out above, so this warmth is physics, not paint: say whose.
            float internalC = PlanetTemperature.InternalCelsius(b);
            float greenhouseC = PlanetTemperature.GreenhouseCelsius(b);
            exception = internalC >= greenhouseC
                ? $"liquid at {c:0} °C outside the habitable zone: internal heat adds {internalC:0} °C"
                : $"liquid at {c:0} °C outside the habitable zone: a {b.atmospheres:0.#}-atmosphere greenhouse adds {greenhouseC:0} °C";
            return null;
        }

        string what = living ? "a living biosphere"
                    : temperateType ? $"a temperate world type ({b.type})"
                    : $"{b.habitability:0}% habitability";
        string side = rel < StarDatabase.HzInnerRel ? "inside" : "beyond";
        return $"{what} {side} the habitable zone at {c:0} °C";
    }

    /// How many problems the last whole-galaxy Enforce found (and repaired where it could). Zero is the
    /// generator getting everything right first time; the tests report it.
    public static int LastFound { get; private set; }

    /// Check, repair and log every system in the galaxy. Returns how many problems were found.
    public static int Enforce(Galaxy galaxy, Species species)
    {
        LastFound = 0;
        if (galaxy == null || galaxy.systems == null) return 0;
        int found = 0;
        foreach (var sys in galaxy.systems)
            if (sys != null) found += Enforce(sys, species);
        LastFound = found;
        return found;
    }

    public static int Enforce(StarSystemData sys, Species species)
    {
        int found = 0;
        foreach (var b in sys.AllBodies())
        {
            if (b == null) continue;
            var star = b.hostStar != null ? b.hostStar : sys.combinedStar;
            string why = Problem(b, star, species, out string exception);
            b.climateException = exception;
            if (why == null) continue;

            found++;
            Repair(b, star);
            if (!b.habitabilityLocked && species != null)
            {
                b.isHabitable = Habitability.IsHabitable(star, species, b.type, b.distanceFromStar);
                b.habitability = Habitability.Rate(star, species, b);
                b.terraformability = Habitability.Terraformability(star, species, b);
            }

            string after = Problem(b, star, species, out exception);
            b.climateException = exception;
            Log($"{sys.name}: {b.name} — {why}. " + (after == null ? "Re-derived from its orbit." : $"STILL WRONG after repair: {after}"));
        }

        if (sys.isHome)
            foreach (var o in HomeworldOnboarding.Options)
            {
                if (o == null || o.system != sys) continue;
                var star = o.hostStar != null ? o.hostStar : sys.combinedStar;
                // The star's band, or — for a species whose climate is hotter than that band gives — the
                // inner reach of its own band (Habitability.CradleZone: a Pyrothian cradle on ring 3).
                if (Habitability.InCradleZone(star, species, o.distanceFromStar)) continue;
                found++;
                Log($"{sys.name}: starting option {o.name} lies outside the habitable zone ({o.distanceFromStar:0.0} against {star.hzInner:0.0}-{star.hzOuter:0.0}).");
            }

        return found;
    }

    /// Put one world right. A world chosen for its climate keeps its type and has its heat clamped into
    /// what its orbit gives; any other world is re-derived from its orbit outright.
    static void Repair(CelestialBody b, StarData star)
    {
        bool forced = b.habitabilityLocked || HomeworldOnboarding.IsOption(b);
        if (!forced)
        {
            SolarSystemGenerator.ReconcileClimate(b, star);
            return;
        }

        var p = b.terrainParams;
        p.heat = InsolationRules.ClampToOrbit(p.heat, star, b.distanceFromStar);
        b.terrainParams = p;
        TerraformVisuals.CaptureNatural(b);
        b.biosphereActive = BiosphereRules.GeneratesWithBiosphere(b);
        b.surface = PlanetTerrainGenerator.GenerateSurface(b);
    }

    static void Log(string message)
    {
        if (Debug.isDebugBuild) Debug.LogWarning($"[WorldConsistency] {message}");
    }
}
