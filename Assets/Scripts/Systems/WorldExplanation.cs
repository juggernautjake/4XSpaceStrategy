using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ============================================================================================
// WHY IS THIS WORLD LIKE THIS? (2026-10-11)
//
// "Please make sure that for every planet there is some kind of explanation/description available for
// why it has the features and conditions that it does. What all factors are impacting why a planet is a
// lava planet, or a gas giant, or a frozen ocean, or have high pressure... It should be a basic
// description. Of course doing research/surveying a planet will give way more info."
//
// ONE builder, three layers, gated by what the player has paid to learn (Survey.LevelOf):
//
//   BASIC        always, once the world can be seen at all (SystemPresence.Revealed): what kind of world
//                it is and the main reasons, in plain words — what a telescope can tell you.
//   LEVEL 1      the surface survey's measurements, each with its cause: the temperature split into
//                starlight / air / surface / internal heat, the air pressure built up from mass, magnetic
//                field, outgassing, stellar wind and heat, the water and its state, the magnetic field
//                and why, plates and their odds, tidal lock and the tide that caused it.
//   LEVEL 2      the deep causes: geology and hotspots, where its mineral and geothermal ground comes
//                from, life and what allows or forbids it, storms and their drivers, and the terraforming
//                outlook for the player's species.
//
// NO CANNED TEXT. Every sentence is chosen by, and every number read from, the same rule the game uses
// to build or run the world: WorldClassifier.Physics reports WHICH branch decided the type (ClassCause),
// PlanetTemperature splits the temperature, AtmosphereRules the pressure, BiosphereRules the water and
// life, WorldModifiers the tide and the storms, TectonicsRules the odds of plates, GeothermalMap the
// geology. If a world's numbers change (terraforming, the Dev sandbox), the explanation changes with them.
//
// CACHED per body against a signature of everything it reads, so a panel can bind it to a live label
// and pay for the text only when something actually changed.
// ============================================================================================
public static class WorldExplanation
{
    const string Dim = "#9FB4C8";
    const string Locked = "#7E8B9C";

    // ---- The one call the UI makes ------------------------------------------------------------

    /// The whole explanation for this world at the player's current survey level, as rich text. Empty for
    /// a world the player cannot see (fog of war).
    public static string ForDisplay(CelestialBody b)
    {
        if (b == null || !SystemPresence.Revealed(b)) return "";
        int level = Survey.LevelOf(b);
        int sig = Signature(b, level);
        if (cache.TryGetValue(b, out var e) && e.sig == sig) return e.text;

        var sb = new StringBuilder(1024);
        sb.Append(Basic(b));
        if (level >= 1)
        {
            sb.Append("\n\n<b>Survey level 1 — measured</b>");
            foreach (var line in Measured(b)) sb.Append('\n').Append(line);
        }
        if (level >= 2)
        {
            sb.Append("\n\n<b>Survey level 2 — deep causes</b>");
            foreach (var line in Deep(b)) sb.Append('\n').Append(line);
        }
        string more = WhatSurveyReveals(level);
        if (more.Length > 0) sb.Append("\n\n").Append(more);

        string text = sb.ToString();
        cache[b] = new Entry { sig = sig, text = text };
        return text;
    }

    /// The basic description alone — for a hover tooltip on the world's type.
    public static string Tooltip(CelestialBody b)
        => b == null || !SystemPresence.Revealed(b) ? "" : Basic(b);

    /// What the next survey level would add, or "" once everything is known.
    public static string WhatSurveyReveals(int level)
    {
        if (level <= 0)
            return $"<color={Locked}><b>Survey to learn more.</b>\n" +
                   "• Level 1 (surface survey): its measured temperature and what makes it — starlight, air, " +
                   "internal heat — its air pressure and why, its water, magnetic field, plates and tidal lock.\n" +
                   "• Level 2 (index survey): its geology and hotspots, where its minerals and geothermal heat " +
                   "come from, life, storms, and the terraforming outlook.</color>";
        if (level == 1)
            return $"<color={Locked}>Complete the level-2 index survey to learn its geology and hotspots, where " +
                   "its minerals and geothermal heat come from, what allows or forbids life, its storms, and the " +
                   "terraforming outlook.</color>";
        return "";
    }

    // ---- Shared facts --------------------------------------------------------------------------

    static StarData StarOf(CelestialBody b)
    {
        if (b == null) return null;
        if (b.hostStar != null) return b.hostStar;
        if (b.system != null) return b.system.combinedStar;
        return null;
    }

    static float RelOf(CelestialBody b, StarData star)
        => star == null ? 1f : InsolationRules.RelOf(star, b.distanceFromStar);

    /// °C from starlight alone at a heat value — PlanetTemperature's own law with nothing added.
    static float StarlightC(float heat)
        => PlanetTemperature.ReferenceKelvin * Mathf.Sqrt(Mathf.Max(0.00001f, heat)) - 273.15f;

    /// The starlight the world was born with (terraforming moves terrainParams, never naturalParams).
    static float NaturalHeat(CelestialBody b)
        => b.naturalParams.heat > 0.0001f ? b.naturalParams.heat : b.terrainParams.heat;

    static float Water(CelestialBody b) => WorldModifiers.WaterLevel(b);

    static bool IsHomeworld(CelestialBody b) => b.habitabilityLocked || HomeworldOnboarding.IsOption(b);

    static string Pct(float f) => $"{Mathf.Clamp01(f) * 100f:0}%";
    static string Signed(float c) => c >= 0f ? $"+{c:0}" : $"{c:0}";
    static string Earths(float m) => $"{MassRules.Format(m)} Earth{(Mathf.Abs(m - 1f) < 0.05f ? "" : "s")}";

    static string Article(string word)
    {
        if (string.IsNullOrEmpty(word)) return "a";
        char c = char.ToLowerInvariant(word[0]);
        return (c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u') ? "An" : "A";
    }

    // ---- BASIC ---------------------------------------------------------------------------------

    /// What kind of world it is and the main reasons, in plain words. No measurements beyond what a
    /// telescope gives (its size and orbit), so it is honest before any survey.
    public static string Basic(CelestialBody b)
    {
        if (b == null) return "";
        var star = StarOf(b);
        float rel = RelOf(b, star);
        bool isMoon = b.parentBody != null;
        WorldClassifier.Physics(b, rel, isMoon, out var cause);
        string kind = TerraformDiagnosis.Pretty(b);

        var sb = new StringBuilder(512);
        sb.Append(Article(kind)).Append(' ').Append(kind).Append(". ");
        sb.Append(Why(b, star, rel, cause));
        if (isMoon) sb.Append(' ').Append(MoonNote(b));
        if (IsHomeworld(b)) sb.Append(' ').Append(HomeNote(b, star));
        return sb.ToString().Trim();
    }

    /// Where it orbits, and what that does to it — the clause most explanations start from.
    static string Orbit(CelestialBody b, StarData star, float rel)
    {
        string who = b.parentBody != null ? $"Its planet, {b.parentBody.name}, orbits" : "It orbits";
        if (star == null) return who + " an uncharted star";
        if (star.isBlackHole) return who + " a black hole, which gives off no light — its only warmth is the faint glow of the black hole's accretion disc";
        string zone = star.hasHabitableZone ? "habitable zone" : "liquid-water band";
        string sun = star.hasHabitableZone ? "its star's" : $"its {star.type}-type giant's";
        if (rel < WorldClassifier.ScorchingRel)
            return who + $" very close to its star, far inside {sun} {zone}, where starlight alone heats the ground far past the boiling point of water";
        if (rel < StarDatabase.HzInnerRel)
            return who + $" inside the inner edge of {sun} {zone}, where starlight alone heats the ground past the boiling point of water";
        if (rel <= StarDatabase.HzOuterRel)
            return StarlightC(NaturalHeat(b)) >= 0f
                ? who + $" in the warm part of {sun} {zone}"
                : who + $" on the cold outer part of {sun} {zone}, where starlight alone is below freezing";
        if (rel < WorldClassifier.FrostLineRel)
            return who + $" beyond the outer edge of {sun} {zone}, where starlight is too weak to keep water liquid";
        return who + " far out, past the frost line, where starlight is too weak to keep water liquid";
    }

    /// Why the air is thin, in words: small, unshielded, or both.
    static string ThinAirWhy(CelestialBody b)
    {
        bool small = b.mass < 1f;
        bool noField = !b.hasMagneticField;
        if (b.mass <= WorldClassifier.AsteroidMassCeil) return "it is too small to hold air";
        if (small && noField) return "it is small and has no magnetic field to shield its air from the stellar wind";
        if (noField) return "it has no magnetic field to shield its air from the stellar wind";
        if (small) return "it is small, and its gravity holds little air";
        return "its air is thin";
    }

    static string Why(CelestialBody b, StarData star, float rel, WorldClassifier.ClassCause cause)
    {
        string orbit = Orbit(b, star, rel);
        float classC = WorldClassifier.ClassifyC(b);
        float water = Water(b);

        switch (b.type)
        {
            case CelestialBodyType.GasGiant:
            {
                string s = $"At {Earths(b.mass)} it is far too massive to be a rocky world — anything over " +
                           $"{WorldClassifier.GasGiantMassFloor:0} Earths pulls in a deep envelope of hydrogen and helium and has no solid surface. ";
                if (star != null && star.isBlackHole) return s + orbit + ".";
                if (rel >= WorldClassifier.FrostLineRel)
                    return s + "It formed out past the frost line, where ice let its core grow fast enough to capture that gas.";
                return s + "Giants form out past the frost line; this one migrated inward to where it orbits now" +
                       (rel < StarDatabase.HzInnerRel ? ", and its cloud tops bake in the starlight." : ".");
            }

            case CelestialBodyType.Asteroid:
            {
                string s = $"At {Earths(b.mass)} it is too small for its gravity to pull it round or to hold any air.";
                if (b.beltId != 0)
                {
                    bool frost = rel > WorldClassifier.FrostLineRel * 0.75f && rel < WorldClassifier.FrostLineRel * 1.45f;
                    s += frost
                        ? " It is one rock in a belt near the frost line, where the material was too stirred up to gather into a planet."
                        : " It is one rock in a belt of rubble that never gathered into a planet.";
                }
                return s;
            }

            case CelestialBodyType.Moon:
            {
                string s = $"At {Earths(b.mass)} it is too small to hold an atmosphere. {orbit}";
                if (classC > 90f) return s + ", so its bare rock bakes in the sunlight.";
                if (classC < 0f && water >= WorldClassifier.LandWater)
                    return s + ", and with no air, the water it has can only exist as ice frozen into its surface.";
                return s + "; with no air, its bare rock simply takes whatever warmth its star gives.";
            }

            case CelestialBodyType.VolcanicPlanet:
            {
                bool molten = PlanetTemperature.BodyAverageCelsius(b) >= WorldClassifier.MagmaMinC;
                string heat = molten
                    ? "Heat from inside, as much as its star, has melted its surface into fields of lava."
                    : "Heat from inside adds to what its star gives.";
                if (b.hasTectonics)
                    return $"{orbit}, and its crust is broken into moving plates that vent magma across the whole surface. {heat}";
                return $"{orbit}. Even without moving plates, hot mantle plumes vent heat across its surface. {heat}";
            }

            case CelestialBodyType.IcePlanet:
            {
                const string albedo = " Bright ice reflects sunlight, which keeps it colder still.";
                if (star != null && star.isBlackHole) return orbit + ", so its water is frozen solid." + albedo;
                if (rel >= StarDatabase.HzInnerRel && rel <= StarDatabase.HzOuterRel)
                {
                    string air = b.atmospheres < AtmosphereRules.LifeFloor ? $"its air is thin ({ThinAirWhy(b)})" : "its air";
                    return $"{orbit}, and {air} holds too little heat to lift it above freezing, so its seas froze over." + albedo;
                }
                return $"{orbit}, so its water is a frozen shell." + albedo;
            }

            case CelestialBodyType.OceanPlanet:
            {
                string sea = water >= 0.95f ? "almost all of its surface is open sea" : "most of its surface is sea, broken only by island chains";
                return $"{orbit}, where starlight and its air keep water liquid — and {sea}. {LifeSentence(b, star)}";
            }

            case CelestialBodyType.BarrenPlanet:
            {
                if (AtmosphereRules.IsRunawayGreenhouse(b))
                    return $"{orbit}. Heavy enough to keep its air, it ran away into a runaway greenhouse: its oceans boiled " +
                           "into the sky, the steam trapped more heat, and when the water was gone to space a crushing " +
                           "carbon-dioxide sky was left. It is far hotter than its starlight alone would make it.";
                switch (cause)
                {
                    case WorldClassifier.ClassCause.InnerNoPlates:
                    {
                        string stripped = AtmosphereRules.InnerOrbitRetention(rel) < 0.999f
                            ? ", and this close in the stellar wind has stripped away most of the air it could have held" : "";
                        return $"{orbit}. With no moving plates to vent magma it is not volcanic — simply a baked rock{stripped}.";
                    }
                    case WorldClassifier.ClassCause.ZoneAirless:
                        return $"{orbit}, warm enough for liquid water — but {ThinAirWhy(b)}, and air that thin cannot keep " +
                               "water liquid, so its water escaped to space.";
                    case WorldClassifier.ClassCause.ZoneFrozenDry:
                    case WorldClassifier.ClassCause.OuterRock:
                        return $"{orbit}, so it is frozen — and it has too little water to be called an ice world.";
                }
                if (b.atmospheres >= 4f && classC > 60f)
                    return $"{orbit}. A thick, poisonous sky traps the heat over its baked, lifeless ground.";
                if (classC > 90f) return $"{orbit}, so its surface is baked and lifeless.";
                if (classC < WorldClassifier.FrozenLineC(b)) return $"{orbit}, so it is frozen and lifeless.";
                return $"{orbit}. It has too little air and water for anything to happen on its surface.";
            }

            case CelestialBodyType.RockyPlanet:
            default:
            {
                if (cause == WorldClassifier.ClassCause.ZoneTooHot && !b.biosphereActive)
                    return $"{orbit}. It is hotter than water's boiling point at its pressure, so its seas have boiled away. {LifeSentence(b, star)}";
                string name = WorldClassifier.Describe(b);
                if (!b.biosphereActive)
                {
                    if (water < WorldClassifier.LandWater)
                        return $"{orbit}. Liquid water is possible here, but it has very little — and {WhyNoLife(b, star)}.";
                    return $"{orbit}, where its water stays liquid — but {WhyNoLife(b, star)}.";
                }
                string s = $"{orbit}, where starlight and its air keep water liquid — and life has taken hold.";
                switch (name)
                {
                    case "tundra world": return s + " It runs cold, close to freezing on average, so much of its land is tundra and ice.";
                    case "swamp world": return s + " Warm, wet lowlands sit just above the water, waterlogged rather than drowned.";
                    case "desert world": return s + " With little surface water and a hot climate, most of its land is desert.";
                    case "savanna world": return s + " Dry but mild, its land is grassland and scrub more than forest.";
                    case "continental world": return s + " Broad continents are well watered by the seas between them.";
                    case "ocean world": return s + " Nearly all of its surface is sea.";
                    case "archipelago world": return s + " Most of its surface is sea, broken by chains of islands.";
                    default: return s + " Land and sea are in balance.";
                }
            }
        }
    }

    static string LifeSentence(CelestialBody b, StarData star)
        => b.biosphereActive ? "Life has taken hold." : $"No life has started: {WhyNoLife(b, star)}.";

    /// Why there is no biosphere, as a clause — read off BiosphereRules' own gates in their own order.
    static string WhyNoLife(CelestialBody b, StarData star)
    {
        if (star != null && !SolarSystemGenerator.StarAllowsLife(star))
            return star.isBlackHole ? "nothing can live without light"
                                    : $"its {star.type}-type star burns too fiercely, and for too short a time, for life to begin";
        if (b.type != CelestialBodyType.RockyPlanet && b.type != CelestialBodyType.OceanPlanet)
            return "only temperate rocky and ocean worlds start with life";
        if (!BiosphereRules.HasAtmosphere(b)) return "its air is too thin for life";
        if (!BiosphereRules.HasEnoughWaterLevel(b)) return "there is too little water for life to take hold";
        float c = PlanetTemperature.BodyAverageCelsius(b);
        if (c > BiosphereRules.MaxLiquidC) return $"at {c:0} °C it is too hot for life to begin (life starts below {BiosphereRules.MaxLiquidC:0} °C)";
        if (c < BiosphereRules.MinLiquidC) return $"at {c:0} °C it is too cold for life to begin";
        if (!BiosphereRules.HasLiquidWater(b)) return "its water is not liquid";
        return "no life has started here yet — Microbial Seeding could begin it";
    }

    static string MoonNote(CelestialBody b)
    {
        var host = b.parentBody;
        if (host == null) return "";
        if (host.type == CelestialBodyType.GasGiant)
            return $"As a moon of the gas giant {host.name}, it gets the same starlight as its planet; the giant's tides do not heat its surface noticeably.";
        return $"As a moon of {host.name}, it gets the same starlight as its planet.";
    }

    static string HomeNote(CelestialBody b, StarData star)
    {
        var sp = SpeciesManager.Current;
        if (sp == null) return "";
        float ideal = Habitability.IdealCelsius(sp);
        float c = PlanetTemperature.BodyAverageCelsius(b);
        bool atIdeal = Mathf.Abs(c - ideal) <= 8f;
        string air = b.atmospheres >= sp.minAtmospheres && b.atmospheres <= sp.maxAtmospheres
            ? $"its air is within the {sp.minAtmospheres:0.#}-{sp.maxAtmospheres:0.#} atmospheres they breathe"
            : $"its air is outside the {sp.minAtmospheres:0.#}-{sp.maxAtmospheres:0.#} atmospheres they breathe";
        return $"It was shaped as a home for the {sp.name}: its orbit was chosen so its climate is " +
               (atIdeal ? $"their ideal (about {ideal:0} °C)" : $"as close to their ideal {ideal:0} °C as its sun allows") +
               $", and {air}.";
    }

    // ---- LEVEL 1: measured ---------------------------------------------------------------------

    /// The surface survey's measurements, each with its cause. One line per topic.
    public static List<string> Measured(CelestialBody b)
    {
        var lines = new List<string>();
        if (b == null) return lines;
        var star = StarOf(b);
        float rel = RelOf(b, star);

        lines.Add(TemperatureLine(b, star, rel));
        lines.Add(AirLine(b, rel));
        string field = FieldLine(b);
        if (field.Length > 0) lines.Add(field);
        string water = WaterLine(b, rel);
        if (water.Length > 0) lines.Add(water);
        string plates = PlatesLine(b);
        if (plates.Length > 0) lines.Add(plates);
        if (b.tidallyLocked)
            lines.Add($"• <b>Tidally locked.</b> One face always looks at its star. Here its star raises a tide about " +
                      $"{WorldModifiers.TideStrength(star, b.distanceFromStar):0.#}× the Sun's on Earth — strong enough to have stopped its spin.");
        if (!string.IsNullOrEmpty(b.climateException))
            lines.Add($"• <b>Why it breaks the zone's rule:</b> {b.climateException}.");
        return lines;
    }

    static string TemperatureLine(CelestialBody b, StarData star, float rel)
    {
        float avg = PlanetTemperature.BodyAverageCelsius(b);
        float sun = StarlightC(b.terrainParams.heat);
        float air = PlanetTemperature.GreenhouseCelsius(b);
        float type = PlanetTemperature.TypeModifierC(b.type);
        float inside = PlanetTemperature.InternalCelsius(b);

        var sb = new StringBuilder(256);
        sb.Append($"• <b>Temperature:</b> averages {avg:0} °C. Starlight gives {sun:0} °C");
        if (Mathf.Abs(air) >= 0.5f)
            sb.Append(AtmosphereRules.IsRunawayGreenhouse(b) ? $"; its runaway greenhouse traps {Signed(air)} °C" : $"; its air traps {Signed(air)} °C");
        if (Mathf.Abs(type) >= 0.5f)
        {
            string what = b.type == CelestialBodyType.IcePlanet ? "bright ice reflects sunlight away"
                        : b.type == CelestialBodyType.VolcanicPlanet ? "lava and vents"
                        : b.type == CelestialBodyType.GasGiant ? "measured at its cold cloud tops"
                        : "its surface";
            sb.Append($"; {what}: {Signed(type)} °C");
        }
        if (inside >= 0.5f) sb.Append($"; heat from inside: {Signed(inside)} °C");
        sb.Append('.');

        if (star != null && star.isBlackHole)
            sb.Append($" <color={Dim}>(A black hole's accretion glow gives about {InsolationRules.BlackHoleKelvin - 273.15f:0} °C, wherever a world orbits.)</color>");
        else if (rel < StarDatabase.HzInnerRel)
            sb.Append($" <color={Dim}>(Inside the zone's inner edge, starlight never gives less than {InsolationRules.InnerFloorC:0} °C.)</color>");
        else if (rel > StarDatabase.HzOuterRel)
            sb.Append($" <color={Dim}>(Beyond the zone, starlight never gives more than {InsolationRules.OuterCeilingC:0} °C.)</color>");

        float natural = StarlightC(NaturalHeat(b));
        if (Mathf.Abs(sun - natural) >= 1f)
            sb.Append($" Engineering has moved its starlight figure {Signed(sun - natural)} °C from the natural {natural:0} °C.");

        if (b.type != CelestialBodyType.GasGiant)
        {
            float swing = PlanetTemperature.LatitudeSwingC(b.atmospheres);
            sb.Append($" Equator to poles: ±{swing:0} °C");
            sb.Append(b.atmospheres < 1f ? " — thin air carries little heat around." : b.atmospheres >= 4f ? " — a thick sky evens it out." : ".");
            if (b.tidallyLocked)
            {
                float day = PlanetTemperature.TidalOffsetC(b, 0.5f, 0.5f);
                float night = PlanetTemperature.TidalOffsetC(b, 0f, 0.5f);
                sb.Append($" Locked to its star, its day side reaches about {avg + day:0} °C and its night side falls to about {avg + night:0} °C.");
            }
        }
        return sb.ToString();
    }

    static string AirLine(CelestialBody b, float rel)
    {
        if (b.type == CelestialBodyType.GasGiant)
            return $"• <b>Air:</b> a gas giant's atmosphere is the planet itself — {b.atmospheres:0} atmospheres of gas with no surface beneath.";
        if (b.type == CelestialBodyType.Asteroid || b.atmospheres <= 0.01f)
            return b.mass <= WorldClassifier.AsteroidMassCeil
                ? "• <b>Air:</b> none — too small to hold any."
                : $"• <b>Air:</b> none — {ThinAirWhy(b)}.";
        if (AtmosphereRules.IsRunawayGreenhouse(b))
            return $"• <b>Air:</b> {b.atmospheres:0} atmospheres — a runaway greenhouse. Gravity alone could hold only about " +
                   $"{AtmosphereRules.Ceiling(b):0.#}; the rest is the carbon its oceans once kept locked in rock, released when they boiled away.";

        var sb = new StringBuilder(256);
        sb.Append($"• <b>Air:</b> {b.atmospheres:0.#} atmospheres ({AtmosphereRules.Describe(b)}). ");
        sb.Append($"Its {Earths(b.mass)} of gravity can hold about {Mathf.Max(0f, b.mass):0.#}");
        sb.Append(b.hasMagneticField
            ? ", and its magnetic field shields that air from the stellar wind"
            : $", but with no magnetic field the stellar wind strips {Pct(1f - AtmosphereRules.NoFieldPenalty)} of it");
        float bonus = AtmosphereRules.TectonicBonus(b);
        if (bonus > 0.05f) sb.Append($"; gas vented by its moving plates adds {bonus:0.#}");
        float wind = AtmosphereRules.InnerOrbitRetention(rel);
        if (wind < 0.999f) sb.Append($"; this close to its star the stellar wind lets it keep only {Pct(wind)}");
        float heatKeep = AtmosphereRules.HeatRetention(Mathf.Max(0f, b.mass), b.terrainParams.heat);
        if (heatKeep < 0.999f) sb.Append($"; its heat boils off another {Pct(1f - heatKeep)}");
        sb.Append('.');
        if (b.atmospheres < AtmosphereRules.LifeFloor)
            sb.Append($" Under {AtmosphereRules.LifeFloor:0.#} atmospheres no liquid water or life can last.");
        return sb.ToString();
    }

    static string FieldLine(CelestialBody b)
    {
        if (b.type == CelestialBodyType.GasGiant || b.type == CelestialBodyType.Asteroid) return "";
        float period = RotationRules.RotationPeriodDays(b.spinSpeed);
        string turns = float.IsInfinity(period) ? "it does not turn" : $"it turns once every {period:0.#} days";
        if (b.hasMagneticField)
            return $"• <b>Magnetic field:</b> yes — {turns}, fast enough to stir its molten core into a dynamo.";
        if (b.tidallyLocked)
            return "• <b>Magnetic field:</b> none — locked to its star, it does not turn, so its core runs no dynamo.";
        if (b.mass < MassRules.TerrestrialMin)
            return $"• <b>Magnetic field:</b> none — at {Earths(b.mass)} it is too small to have kept a molten core.";
        string braked = b.parentBody != null ? " — braked by its planet's tides" : "";
        return $"• <b>Magnetic field:</b> none — {turns}{braked}, too slowly to stir a dynamo (it would need under " +
               $"{RotationRules.RotationPeriodDays(RotationRules.MagneticFieldSpin):0} days).";
    }

    static string WaterLine(CelestialBody b, float rel)
    {
        if (b.type == CelestialBodyType.GasGiant || b.type == CelestialBodyType.Asteroid) return "";
        float water = Water(b);
        if (water < 0.01f) return "• <b>Water:</b> none on the surface.";
        float avg = PlanetTemperature.BodyAverageCelsius(b);
        BiosphereRules.LiquidRange(b, out float freeze, out float boil);
        string inner = rel < StarDatabase.HzInnerRel ? " Inside the zone's inner edge, its young star's heat drove most of its water off long ago." : "";

        if (AtmosphereRules.IsRunawayGreenhouse(b))
            return $"• <b>Water:</b> a trace ({Pct(water)}) — the runaway boiled its oceans into the sky and lost them to space.";
        if (avg < WorldClassifier.FrozenLineC(b))
            return $"• <b>Water:</b> {Pct(water)} of the surface, frozen — the world averages {avg:0} °C.{inner}";
        if (b.atmospheres < AtmosphereRules.LifeFloor)
            return $"• <b>Water:</b> {Pct(water)} — air this thin cannot keep water liquid, so most of it escaped to space.{inner}";
        if (avg > boil)
            return $"• <b>Water:</b> {Pct(water)}, boiled to steam — at {b.atmospheres:0.#} atmospheres water boils at {boil:0} °C.{inner}";
        return $"• <b>Water:</b> {Pct(water)} of the surface, liquid — at {b.atmospheres:0.#} atmospheres it stays liquid from {freeze:0} to {boil:0} °C.{inner}";
    }

    static string PlatesLine(CelestialBody b)
    {
        if (b.type == CelestialBodyType.GasGiant || b.type == CelestialBodyType.Asteroid) return "";
        float chance = TectonicsRules.Chance(b.type, b.mass);
        if (b.hasTectonics)
            return $"• <b>Plates:</b> active — about {Pct(chance)} of worlds its size have them. They fold mountains, open rifts and vent heat and gas along their margins.";
        if (AtmosphereRules.IsRunawayGreenhouse(b))
            return "• <b>Plates:</b> none — when its oceans boiled away nothing was left to lubricate the crust, and it seized into one stagnant lid.";
        if (!TectonicsRules.Possible(b.type, b.mass))
            return "• <b>Plates:</b> none — too small to keep its interior hot enough to move plates.";
        return $"• <b>Plates:</b> none — only about {Pct(chance)} of worlds its size have them.";
    }

    // ---- LEVEL 2: deep causes ------------------------------------------------------------------

    /// The deep survey's causes: geology, minerals, life, storms, terraforming.
    public static List<string> Deep(CelestialBody b)
    {
        var lines = new List<string>();
        if (b == null) return lines;
        var star = StarOf(b);

        if (b.type == CelestialBodyType.GasGiant)
        {
            int storms = GasGiantStorms.Spots(b, out _);
            lines.Add(storms > 0
                ? $"• <b>Storms:</b> {storms} great storm{(storms == 1 ? "" : "s")} ride its cloud belts, each a swelling of the belt it sits in."
                : "• <b>Storms:</b> no great storms ride its belts at the moment.");
            lines.Add("• <b>Terraforming:</b> a gas giant has no surface to terraform.");
            return lines;
        }
        if (b.type == CelestialBodyType.Asteroid)
        {
            lines.Add("• <b>Geology:</b> frozen through long ago — no plates, no plumes, no geothermal heat.");
            return lines;
        }

        lines.Add(GeologyLine(b));
        string minerals = MineralLine(b);
        if (minerals.Length > 0) lines.Add(minerals);
        lines.Add(LifeLine(b, star));
        string weather = WeatherLine(b);
        if (weather.Length > 0) lines.Add(weather);
        lines.Add(TerraformLine(b));
        return lines;
    }

    static string GeologyLine(CelestialBody b)
    {
        bool plates = b.hasTectonics;
        float hot = GeothermalMap.HotspotIntensity(b);
        var sb = new StringBuilder(256);
        sb.Append($"• <b>Geology:</b> {GeothermalMap.Label(b)}.");
        if (!plates && hot <= 0.001f)
        {
            sb.Append(" Nothing stirs below, so it has no geothermal energy to tap.");
            return sb.ToString();
        }
        if (plates) sb.Append(" Its geothermal ground runs along the plate margins, where the crust is torn and heated.");
        if (hot > 0.001f)
        {
            sb.Append(" Mantle plumes rise under its hotspots");
            bool frozen = b.type == CelestialBodyType.IcePlanet || PlanetTemperature.BodyAverageCelsius(b) < 0f;
            sb.Append(frozen && hot >= WorldModifiers.GeologyActiveIntensity
                ? "; on its frozen surface the strongest vent water and ice — cryovolcanoes — instead of lava."
                : ".");
        }
        float inside = PlanetTemperature.InternalCelsius(b);
        if (inside >= 0.5f) sb.Append($" Together they add {inside:0} °C to its surface temperature.");
        return sb.ToString();
    }

    static string MineralLine(CelestialBody b)
    {
        var parts = new List<string>();
        if (WorldModifiers.Has(b, WorldModifier.VastMineralDeposits))
        {
            string why = IsHomeworld(b) || b.birthrightClaim
                ? "this starting world was settled for its rich mineral ground"
                : b.hasTectonics ? "plate collisions have pushed ore-bearing rock up across wide areas"
                : "a rare abundance of ore-bearing rock in what it formed from";
            parts.Add($"<b>Vast Mineral Deposits</b> — {why}");
        }
        if (WorldModifiers.Has(b, WorldModifier.HighQualityMinerals))
        {
            string why = WorldModifiers.GeologicallyActive(b)
                ? "hot fluids circulating through its active crust concentrated metals into rich seams"
                : "rich seams laid down long ago, in a crust that is quiet now";
            parts.Add($"<b>High Quality Minerals</b> — {why}");
        }
        if (parts.Count == 0) return "";
        return "• " + string.Join("; ", parts) + ".";
    }

    static string LifeLine(CelestialBody b, StarData star)
    {
        if (b.biosphereActive)
        {
            float c = PlanetTemperature.BodyAverageCelsius(b);
            string s = $"• <b>Life:</b> active. It thrives because its water is liquid at {c:0} °C (life wants " +
                       $"{BiosphereRules.MinLiquidC:0}-{BiosphereRules.MaxLiquidC:0} °C), {Pct(Water(b))} of the surface is water, " +
                       $"and its {b.atmospheres:0.#} atmospheres hold that water down.";
            if (WorldModifiers.Has(b, WorldModifier.FertileWorld))
                s += $" <b>Fertile World</b> — close to {WorldModifiers.FertileIdealLowC:0}-{WorldModifiers.FertileIdealHighC:0} °C with about half its surface under water, its fertile ground is unusually rich.";
            return s;
        }
        string line = $"• <b>Life:</b> none — {WhyNoLife(b, star)}.";
        float cap = BiosphereRules.Ceiling(b);
        if (cap > 0.01f && (b.type == CelestialBodyType.RockyPlanet || b.type == CelestialBodyType.OceanPlanet))
            line += $" Seeded, it could grow to {Pct(cap)} lushness.";
        return line;
    }

    static string WeatherLine(CelestialBody b)
    {
        if (!WorldModifiers.Has(b, WorldModifier.ExtremeWeather)) return "";
        var d = WorldModifiers.WeatherDrivers(b);
        var why = new List<string>();
        if ((d & WorldModifiers.WeatherDriver.TidalLock) != 0) why.Add("a day side that never cools beside a night side that never warms");
        if ((d & WorldModifiers.WeatherDriver.FastSpin) != 0) why.Add("a fast spin that whips its winds into jets");
        if ((d & WorldModifiers.WeatherDriver.WarmSeas) != 0) why.Add("warm seas pouring moisture into the sky");
        if ((d & WorldModifiers.WeatherDriver.DeepSky) != 0) why.Add("a deep, heavy atmosphere");
        if (why.Count == 0)
            why.Add($"the contrast between a warm equator and cold poles (±{PlanetTemperature.LatitudeSwingC(b.atmospheres):0} °C)");
        return $"• <b>Extreme Weather</b> — its storms are driven by {string.Join(", and ", why)}.";
    }

    static string TerraformLine(CelestialBody b)
    {
        var sp = SpeciesManager.Current;
        if (sp == null) return "";
        var issues = TerraformDiagnosis.Analyze(b, sp);
        var sb = new StringBuilder(256);
        sb.Append($"• <b>Terraforming for the {sp.name}:</b> ");
        if (issues == null || issues.Count == 0)
        {
            sb.Append("nothing to fix — it already suits them.");
        }
        else
        {
            var sorted = new List<TerraformIssue>(issues);
            sorted.Sort((x, y) => y.severity.CompareTo(x.severity));
            var names = new List<string>();
            for (int i = 0; i < sorted.Count && names.Count < 3; i++)
                if (sorted[i] != null) names.Add(TerraformDiagnosis.Describe(sorted[i].problem).ToLowerInvariant());
            sb.Append("its main problems are ").Append(string.Join(", ", names)).Append('.');
        }
        sb.Append($" The most air it can keep today is {AtmosphereRules.SustainableCeiling(b):0.#} atmospheres.");
        return sb.ToString();
    }

    // ---- Cache ---------------------------------------------------------------------------------

    struct Entry { public int sig; public string text; }
    static readonly Dictionary<CelestialBody, Entry> cache = new Dictionary<CelestialBody, Entry>();

    /// Everything the text reads, folded into one number. Cheap enough to compute every frame.
    static int Signature(CelestialBody b, int level)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + level;
            h = h * 31 + SpeciesManager.CurrentIndex;
            h = h * 31 + (int)b.type;
            h = h * 31 + b.mass.GetHashCode();
            h = h * 31 + b.atmospheres.GetHashCode();
            h = h * 31 + b.terrainParams.heat.GetHashCode();
            h = h * 31 + b.naturalParams.heat.GetHashCode();
            h = h * 31 + b.terrainParams.seaLevel.GetHashCode();
            h = h * 31 + b.terrainParams.moisture.GetHashCode();
            h = h * 31 + b.terrainSeed.GetHashCode();
            h = h * 31 + b.spinSpeed.GetHashCode();
            h = h * 31 + b.distanceFromStar.GetHashCode();
            h = h * 31 + b.worldModifierFlags;
            h = h * 31 + (b.biosphereActive ? 1 : 0) + (b.hasTectonics ? 2 : 0) + (b.tidallyLocked ? 4 : 0)
                       + (b.hasMagneticField ? 8 : 0) + (b.habitabilityLocked ? 16 : 0) + (b.birthrightClaim ? 32 : 0);
            h = h * 31 + (b.name != null ? b.name.GetHashCode() : 0);
            h = h * 31 + (b.parentBody != null && b.parentBody.name != null ? b.parentBody.name.GetHashCode() : 0);
            h = h * 31 + (b.climateException != null ? b.climateException.GetHashCode() : 0);
            h = h * 31 + (b.terraformProjects != null ? b.terraformProjects.Count : 0);
            return h;
        }
    }
}
