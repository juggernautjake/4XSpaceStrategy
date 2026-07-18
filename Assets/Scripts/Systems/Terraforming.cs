using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// TERRAFORMING — what is actually WRONG with a world, and the specific engineering projects that
// fix it.
//
// Two layers, deliberately separated:
//
//   1. PROJECTS raise a world's CEILING. Each is a one-off, expensive, tech-gated piece of
//      planetary engineering that addresses one diagnosed problem: hauling in comets, melting the
//      ice caps, hanging orbital shades, restarting a dead core, spinning the planet up, nudging
//      its orbit outward. They cost resources and take time, and each lifts the maximum
//      habitability the world can ever reach.
//
//   2. TERRAFORMERS then do the grind of raising habitability TOWARD that ceiling (ColonyManager
//      .TickTerraform). That part was already here; projects are what decide how high it can go.
//
// This is why a 56% world might only reach 69% today and 87% once you have researched more: the
// ceiling is the sum of the projects you can actually perform.
//
// Everything is SPECIES- and PLANET-TYPE-dependent. "Too cold" means too cold *for the Pyrothians*,
// who want a furnace; the Cryithn have the opposite problem on the same world. A world with no
// liquid water is a crisis for the Aquarii and an irrelevance to the silicon-based Pyrothians.
// ============================================================================================

// What is wrong with a world, from the current species' point of view.
public enum TerraformProblem
{
    TooHot, TooCold, NoWater, NoAtmosphere, ToxicAtmosphere, NoBiosphere,
    NoMagnetosphere, DayTooLong, DayTooShort, OrbitTooClose, OrbitTooFar,
    NoSurface, UnstableAxis, LowGravity,
    // The inverses. Every fault above has a mirror image for a species with the opposite biology:
    // an ocean world is a drowning hazard to a silicate race, and a thick sky is a crushing one.
    TooMuchWater, AtmosphereTooThick,
    // The catch-all: the world is simply the wrong KIND of place for this species, and no amount of
    // adjusting its temperature or its air will change that. Only remodelling the world itself will.
    WrongWorldType
}

// One diagnosed fault. IMPORTANT: append only — the ordinal is serialized on the body.
public enum TerraformProjectType
{
    // Water
    HaulWater, MeltIceCaps, TapAquifers, CometBombardment,
    // Air
    SeedAtmosphere, ScrubAtmosphere, OxygenSeeding,
    // Life
    MicrobialSeeding, PlantForests,
    // Temperature
    OrbitalMirrors, OrbitalShades, CoreCooling,
    // Deep planetary engineering
    CoreIgnition, MagneticShield,
    SpinUp, SpinDown, AxialCorrection,
    OrbitShiftOut, OrbitShiftIn,
    CaptureMoon, RemoveMoon,
    GasGiantShell, GravityAnchors,
    // Removing what a species doesn't want, and rebuilding a world into the kind of place it does.
    HydrosphereVenting, CrustalSequestration, AtmosphericThinning, WorldRemodelling
}

public class TerraformIssue
{
    public TerraformProblem problem;
    public float severity;      // 0..1 — how badly this world suffers from it
    public string detail;       // human-readable, species-specific
}

// A single piece of planetary engineering.
public class TerraformProjectInfo
{
    public TerraformProjectType type;
    public string name;
    public string description;
    public TerraformProblem solves;

    public string requiredTech;         // tech id that unlocks it (null = available from the start)
    public int costMetal, costEnergy, costWater;
    public float duration;              // seconds of work
    public float ceilingGain;           // habitability points added to this world's ceiling

    // Which worlds this project even makes sense on. Null = any body the problem was diagnosed on.
    public System.Func<CelestialBody, Species, bool> applies;

    public TerraformProjectInfo(TerraformProjectType t, string n, TerraformProblem solves, string tech,
                                int m, int e, int w, float duration, float gain, string desc)
    {
        type = t; name = n; this.solves = solves; requiredTech = tech;
        costMetal = m; costEnergy = e; costWater = w;
        this.duration = duration; ceilingGain = gain; description = desc;
    }
}

// ---------------------------------------------------------------------------------------------
// Whether a world can grow (or start with) a BioSphere at all. Species-INDEPENDENT — this is about the
// planet's own physical state (water, temperature), not whether any particular species wants it there.
// ---------------------------------------------------------------------------------------------
public static class BiosphereRules
{
    // Heuristic liquid-water band. No evaporation/vapour-pressure model exists anywhere in this codebase,
    // so this is a plain "warm enough, not boiling away" Celsius window rather than a physically exact
    // curve — good enough to gate Microbial Seeding and the sandbox's BioSphere slider.
    public const float MinLiquidC = 0f, MaxLiquidC = 50f;
    public const float MinWaterLevel = 0.15f;   // needs real coverage, not a token puddle

    // Below this, CelestialBody.atmosphereThickness reads as "too thin to hold onto anything living"
    // (a vacuum world, or a small moon that never got one — see AtmosphereRules). Slice 3 originally left
    // this gate out because no atmosphere attribute existed yet; it does now.
    public const float MinAtmosphere = 0.12f;

    public static bool HasLiquidWaterClimate(CelestialBody b)
    {
        if (b == null) return false;
        float c = PlanetTemperature.BodyAverageCelsius(b);
        return c >= MinLiquidC && c <= MaxLiquidC;
    }

    public static bool HasEnoughWaterLevel(CelestialBody b) =>
        b != null && PlanetTerrainGenerator.WaterLevelFromElevation(b.terrainParams.elevation) >= MinWaterLevel;

    public static bool HasAtmosphere(CelestialBody b) => b != null && b.atmosphereThickness >= MinAtmosphere;

    // What a world generates WITH. Being in the liquid-water band isn't enough on its own — Rocky/Ocean
    // worlds that roll into it start alive, everything else (barren/ice/volcanic/gas/moons/asteroids)
    // starts sterile even if it happens to sit in the band, because meeting the band later through
    // terraforming should NOT be enough by itself (that's the whole reason Microbial Seeding exists).
    public static bool GeneratesWithBiosphere(CelestialBody b) =>
        b != null &&
        (b.type == CelestialBodyType.RockyPlanet || b.type == CelestialBodyType.OceanPlanet) &&
        HasLiquidWaterClimate(b) && HasEnoughWaterLevel(b) && HasAtmosphere(b);

    // Can this world's BioSphere value grow (or stay) above a bare floor right now?
    public static bool CanSustainBiosphere(CelestialBody b) =>
        b != null && b.biosphereActive && HasLiquidWaterClimate(b) && HasEnoughWaterLevel(b) && HasAtmosphere(b);

    // Null = Microbial Seeding would succeed; otherwise the reason it's likely to fail, surfaced by
    // TerraformManager.CanStart so the project button warns before the player spends resources on it.
    public static string MicrobialSeedingWarning(CelestialBody b)
    {
        if (b == null) return "no world selected";
        if (b.biosphereActive) return "already has an active biosphere";
        if (!HasAtmosphere(b)) return "atmosphere too thin to hold a biosphere";
        if (!HasEnoughWaterLevel(b)) return "not enough water level for life to take hold";
        if (!HasLiquidWaterClimate(b)) return "too hot or too cold for liquid water";
        return null;
    }

    // Null = the sandbox's BioSphere slider is free to reach full lushness right now; otherwise the
    // reason it's capped. Distinct from MicrobialSeedingWarning above: that one explains why STARTING a
    // biosphere would fail, this one explains why an EXISTING (or not-yet-existing) one can't grow —
    // "already active" isn't a blocker here, it's the first requirement.
    public static string WhyCapped(CelestialBody b)
    {
        if (b == null) return "no world selected";
        if (!b.biosphereActive) return "this world has no active biosphere yet — Microbial Seeding starts one on a barren world";
        if (!HasAtmosphere(b)) return "atmosphere too thin";
        if (!HasEnoughWaterLevel(b)) return "not enough water level";
        if (!HasLiquidWaterClimate(b)) return "too hot or too cold for liquid water";
        return null;
    }
}

// ---------------------------------------------------------------------------------------------
// Diagnosis: read a world through the current species' biology and list what's wrong with it.
// ---------------------------------------------------------------------------------------------
public static class TerraformDiagnosis
{
    // Does this species actually need liquid water? The silicon-based Pyrothians famously don't, so
    // a bone-dry world simply isn't a problem for them.
    public static bool NeedsWater(Species s) => s != null && s.Affinity(CelestialBodyType.OceanPlanet) >= 0.3f;

    // Does it need a breathable atmosphere and living soil? Photosynthetic and carbon-based races do.
    public static bool NeedsBiosphere(Species s) => s != null && s.Affinity(CelestialBodyType.RockyPlanet) >= 0.45f;

    public static List<TerraformIssue> Analyze(CelestialBody b, Species s)
    {
        var list = new List<TerraformIssue>();
        if (b == null || s == null) return list;
        var star = b.hostStar;
        if (star == null) return list;

        // ---- Starlight: too close or too far for THIS species' preferred band ----
        if (Habitability.GetZone(star, s, out float inner, out float outer))
        {
            float d = b.distanceFromStar;
            float half = Mathf.Max(0.001f, (outer - inner) * 0.5f);

            if (d < inner)
            {
                float over = (inner - d) / half;
                list.Add(new TerraformIssue
                {
                    problem = TerraformProblem.TooHot,
                    severity = Mathf.Clamp01(over),
                    detail = $"Receives far too much starlight for {s.name} — surface runs hot."
                });
                // Beyond a point no amount of shading helps; the planet has to be moved.
                if (over > 0.85f)
                    list.Add(new TerraformIssue
                    {
                        problem = TerraformProblem.OrbitTooClose,
                        severity = Mathf.Clamp01((over - 0.85f) / 1.5f),
                        detail = $"Orbits well inside the band {s.name} can tolerate. Shades alone cannot fix this."
                    });
            }
            else if (d > outer)
            {
                float over = (d - outer) / half;
                list.Add(new TerraformIssue
                {
                    problem = TerraformProblem.TooCold,
                    severity = Mathf.Clamp01(over),
                    detail = $"Too little starlight for {s.name} — the surface is frozen."
                });
                if (over > 0.85f)
                    list.Add(new TerraformIssue
                    {
                        problem = TerraformProblem.OrbitTooFar,
                        severity = Mathf.Clamp01((over - 0.85f) / 1.5f),
                        detail = $"Orbits well outside {s.name}'s band. Mirrors alone cannot fix this."
                    });
            }
        }

        // ---- Water ----
        // The stored Water resource number can drift out of step with what the world actually IS — so a
        // visibly-blue OCEAN planet must never read as "no water". An ocean world is liquid water by
        // definition; treat it as saturated regardless of the stored amount. (An ICE world has water too,
        // but frozen and useless until melted, so it deliberately still reads as needing water — that's
        // what keeps "Melt the Ice Caps" on offer for it.)
        float water = b.resources != null ? b.resources.Get(ResourceType.Water) : 0f;
        bool oceanWorld = b.type == CelestialBodyType.OceanPlanet;
        float effWater = oceanWorld ? Mathf.Max(water, 400f) : water;
        if (NeedsWater(s) && effWater < 140f && b.type != CelestialBodyType.GasGiant)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.NoWater,
                severity = Mathf.Clamp01(1f - effWater / 140f),
                detail = $"Almost no accessible water ({effWater:F0} units). {s.name} cannot live without it."
            });

        // The mirror image: to a silicate, heat-loving race an ocean world is not a paradise, it is a
        // drowning hazard. The same planet the Aquarii would score in the eighties is worthless to the
        // Pyrothians until the water is gone.
        if (s.PrefersDry && b.type != CelestialBodyType.GasGiant &&
            (oceanWorld || water > 260f))
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.TooMuchWater,
                severity = Mathf.Clamp01(effWater / 420f + (oceanWorld ? 0.45f : 0f)),
                detail = $"Drowned in water ({effWater:F0} units). {s.name} needs it arid — the hydrosphere has to go."
            });

        // ---- Atmosphere ----
        if (b.type == CelestialBodyType.BarrenPlanet || b.type == CelestialBodyType.Asteroid || b.type == CelestialBodyType.Moon)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.NoAtmosphere,
                severity = 0.9f,
                detail = "Effectively airless — no atmosphere to breathe or hold in heat."
            });
        else if (b.type == CelestialBodyType.IcePlanet)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.NoAtmosphere,
                severity = 0.5f,
                detail = "A thin, frozen-out atmosphere; most of the volatiles are locked in the ice."
            });

        if (b.type == CelestialBodyType.VolcanicPlanet)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.ToxicAtmosphere,
                severity = 0.8f,
                detail = "A choking sulphur-and-ash atmosphere that must be scrubbed and replaced."
            });

        // A very large world holds onto a crushing envelope of gas — the opposite problem to an airless
        // rock, and it has to be bled off rather than built up.
        if (b.surfaceSize >= 14 && b.type != CelestialBodyType.GasGiant)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.AtmosphereTooThick,
                severity = Mathf.Clamp01((b.surfaceSize - 14f) / 8f + 0.4f),
                detail = "Massive enough to hold a crushing atmosphere — the pressure alone would kill a landing party."
            });

        // ---- Living soil ----
        if (NeedsBiosphere(s) && b.type != CelestialBodyType.GasGiant && b.type != CelestialBodyType.OceanPlanet)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.NoBiosphere,
                severity = 0.6f,
                detail = $"Sterile ground — no soil, no ecology. {s.name} needs a living biosphere."
            });

        // ---- No surface at all ----
        if (b.type == CelestialBodyType.GasGiant)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.NoSurface,
                severity = 1f,
                detail = "A gas giant: there is no ground to stand on. Only a shellworld could change that."
            });

        // ---- Magnetosphere: small dead worlds get their air stripped away again ----
        if (b.type == CelestialBodyType.BarrenPlanet || b.type == CelestialBodyType.Asteroid || b.type == CelestialBodyType.Moon)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.NoMagnetosphere,
                severity = 0.7f,
                detail = "A dead core and no magnetic field — stellar wind will strip any atmosphere you make."
            });

        // ---- Rotation ----
        float spin = Mathf.Abs(b.spinSpeed);
        if (spin < 3f)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.DayTooLong,
                severity = Mathf.Clamp01(1f - spin / 3f),
                detail = "Turns so slowly that one face bakes while the other freezes."
            });
        else if (spin > 45f)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.DayTooShort,
                severity = Mathf.Clamp01((spin - 45f) / 45f),
                detail = "Spins violently fast — punishing storms and a brutal day/night cycle."
            });

        // ---- Axis ----
        if (Mathf.Abs(b.inclination) > 28f)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.UnstableAxis,
                severity = Mathf.Clamp01((Mathf.Abs(b.inclination) - 28f) / 40f),
                detail = "A wildly tilted axis gives savage, unpredictable seasons."
            });

        // ---- Wrong kind of world entirely ----
        // Temperature and air can be adjusted; what a world fundamentally IS cannot — not without
        // rebuilding it. This is the fault behind the big species-perspective swings: an ocean world
        // reads 86% to the Aquarii and 24% to the Pyrothians purely because of this affinity, and no
        // amount of shades or scrubbers touches it. Only remodelling the world does.
        float aff = s.Affinity(b.type);
        if (aff < Habitability.HabitableAffinity && b.type != CelestialBodyType.GasGiant)
        {
            var want = s.BestType();
            if (want != b.type)
                list.Add(new TerraformIssue
                {
                    problem = TerraformProblem.WrongWorldType,
                    severity = Mathf.Clamp01((Habitability.HabitableAffinity - aff) / Habitability.HabitableAffinity),
                    detail = $"A {Pretty(b.type)} is the wrong kind of world for {s.name} (affinity {aff * 100f:F0}%). " +
                             $"They need {Pretty(want)}, which means rebuilding the world itself."
                });
        }

        // ---- Gravity ----
        if (b.surfaceSize <= 4)
            list.Add(new TerraformIssue
            {
                problem = TerraformProblem.LowGravity,
                severity = Mathf.Clamp01((5f - b.surfaceSize) / 4f),
                detail = "Too small to hold decent gravity — or an atmosphere — on its own."
            });

        return list;
    }

    public static bool Has(List<TerraformIssue> issues, TerraformProblem p)
    {
        foreach (var i in issues) if (i.problem == p) return true;
        return false;
    }

    // How badly a world suffers from a given fault (0 = not at all). Drives how long a project takes
    // and what it costs: dragging a world from 24% to livable is far more work than nudging one at 76%.
    public static float SeverityOf(CelestialBody b, Species s, TerraformProblem p)
    {
        foreach (var i in Analyze(b, s)) if (i.problem == p) return i.severity;
        return 0f;
    }

    public static string Pretty(CelestialBodyType t)
    {
        switch (t)
        {
            case CelestialBodyType.RockyPlanet: return "temperate rocky world";
            case CelestialBodyType.OceanPlanet: return "ocean world";
            case CelestialBodyType.IcePlanet: return "frozen world";
            case CelestialBodyType.VolcanicPlanet: return "volcanic world";
            case CelestialBodyType.BarrenPlanet: return "barren world";
            case CelestialBodyType.GasGiant: return "gas giant";
            case CelestialBodyType.Moon: return "moon";
            default: return t.ToString();
        }
    }

    public static string Describe(TerraformProblem p)
    {
        switch (p)
        {
            case TerraformProblem.TooHot: return "Too hot";
            case TerraformProblem.TooCold: return "Too cold";
            case TerraformProblem.NoWater: return "No water";
            case TerraformProblem.NoAtmosphere: return "No atmosphere";
            case TerraformProblem.ToxicAtmosphere: return "Toxic atmosphere";
            case TerraformProblem.NoBiosphere: return "No biosphere";
            case TerraformProblem.NoMagnetosphere: return "No magnetic field";
            case TerraformProblem.DayTooLong: return "Rotates too slowly";
            case TerraformProblem.DayTooShort: return "Rotates too fast";
            case TerraformProblem.OrbitTooClose: return "Orbit too close to the star";
            case TerraformProblem.OrbitTooFar: return "Orbit too far from the star";
            case TerraformProblem.NoSurface: return "No solid surface";
            case TerraformProblem.UnstableAxis: return "Unstable axial tilt";
            case TerraformProblem.LowGravity: return "Gravity too low";
            default: return p.ToString();
        }
    }
}

// ---------------------------------------------------------------------------------------------
// The project catalogue.
// ---------------------------------------------------------------------------------------------
public static class TerraformProjectDatabase
{
    static TerraformProjectInfo[] _all;

    public static TerraformProjectInfo[] All { get { if (_all == null) Build(); return _all; } }
    public static TerraformProjectInfo Get(TerraformProjectType t) { if (_all == null) Build(); return _all[(int)t]; }

    static void Build()
    {
        _all = new TerraformProjectInfo[System.Enum.GetValues(typeof(TerraformProjectType)).Length];

        void P(TerraformProjectInfo p, System.Func<CelestialBody, Species, bool> applies = null)
        { p.applies = applies; _all[(int)p.type] = p; }

        // ================= WATER =================
        P(new TerraformProjectInfo(TerraformProjectType.HaulWater, "Water Convoy", TerraformProblem.NoWater, "X7",
            240, 300, 0, 55f, 9f,
            "Tanker fleets siphon water from an ocean world or ice moon elsewhere in the system and ship it here, load by load. Slow and expensive, but it works anywhere."));

        P(new TerraformProjectInfo(TerraformProjectType.MeltIceCaps, "Melt the Ice Caps", TerraformProblem.NoWater, "X3",
            160, 420, 0, 40f, 12f,
            "Orbital heat lances melt the polar caps and buried glaciers, flooding the basins and thickening the air in one stroke. Only works where the water is already here, frozen."),
            (b, s) => b.type == CelestialBodyType.IcePlanet ||
                      (b.resources != null && b.resources.Get(ResourceType.Water) >= 60f));

        P(new TerraformProjectInfo(TerraformProjectType.TapAquifers, "Tap the Deep Aquifers", TerraformProblem.NoWater, "X8",
            300, 220, 0, 45f, 10f,
            "Drilling rigs punch kilometres into the crust to reach fossil water trapped underground and pump it to the surface. Needs a real planetary crust to drill."),
            (b, s) => b.type == CelestialBodyType.RockyPlanet || b.type == CelestialBodyType.BarrenPlanet ||
                      b.type == CelestialBodyType.VolcanicPlanet);

        P(new TerraformProjectInfo(TerraformProjectType.CometBombardment, "Cometary Bombardment", TerraformProblem.NoWater, "X7",
            420, 520, 0, 70f, 14f,
            "Redirect ice comets from the outer system and drop them onto the world. Brutal, spectacular, and it delivers water AND atmosphere at once — but it takes decades to settle."));

        // ================= AIR =================
        P(new TerraformProjectInfo(TerraformProjectType.SeedAtmosphere, "Atmospheric Processors", TerraformProblem.NoAtmosphere, "X2",
            280, 340, 120, 50f, 12f,
            "Vast processor towers crack rock and ice into gas, building a breathable envelope from the planet's own material."));

        P(new TerraformProjectInfo(TerraformProjectType.ScrubAtmosphere, "Atmospheric Scrubbing", TerraformProblem.ToxicAtmosphere, "X4",
            320, 380, 80, 55f, 13f,
            "Fleets of scrubber platforms strip out sulphur, ash and heavy metals, cracking the poison into something that can be breathed."));

        P(new TerraformProjectInfo(TerraformProjectType.OxygenSeeding, "Oxygen Cascade", TerraformProblem.NoAtmosphere, "X14",
            220, 300, 160, 45f, 9f,
            "Engineered algal blooms flood the new air with oxygen, finishing what the processors started."),
            (b, s) => b.resources != null && b.resources.Get(ResourceType.Water) >= 80f);

        // ================= LIFE =================
        P(new TerraformProjectInfo(TerraformProjectType.MicrobialSeeding, "Microbial Seeding", TerraformProblem.NoBiosphere, "X14",
            140, 180, 100, 35f, 7f,
            "Extremophile bacteria are scattered across the surface to break dead rock into the first true soil. The unglamorous foundation of every living world."));

        P(new TerraformProjectInfo(TerraformProjectType.PlantForests, "Forest Seeding", TerraformProblem.NoBiosphere, "X14",
            180, 160, 240, 60f, 11f,
            "Once there is soil and water, seed the whole world: grasslands, then canopy. The forests take over the work of making air and hold the climate steady for good."),
            (b, s) => b.resources != null && b.resources.Get(ResourceType.Water) >= 100f);

        // ================= TEMPERATURE =================
        P(new TerraformProjectInfo(TerraformProjectType.OrbitalMirrors, "Orbital Mirror Swarm", TerraformProblem.TooCold, "X6",
            360, 260, 0, 50f, 12f,
            "A swarm of gossamer mirrors hangs in orbit and focuses extra starlight onto the surface, warming a frozen world by degrees a year."));

        P(new TerraformProjectInfo(TerraformProjectType.OrbitalShades, "Orbital Shade Array", TerraformProblem.TooHot, "X6",
            360, 260, 0, 50f, 12f,
            "Statite shades parked between the world and its star throw a permanent partial eclipse across it, bleeding away the excess heat."));

        P(new TerraformProjectInfo(TerraformProjectType.CoreCooling, "Core Heat Extraction", TerraformProblem.TooHot, "X8",
            520, 460, 200, 80f, 14f,
            "Drill to the mantle and run titanic heat exchangers to bleed the planet's own furnace into space. The only way to calm a world whose heat comes from below rather than from its star."),
            (b, s) => b.type == CelestialBodyType.VolcanicPlanet);

        // ================= DEEP PLANETARY ENGINEERING =================
        P(new TerraformProjectInfo(TerraformProjectType.CoreIgnition, "Core Ignition", TerraformProblem.NoMagnetosphere, "X11",
            700, 900, 0, 110f, 15f,
            "Restart a dead world's core with deep fusion charges, setting the iron turning again. A working dynamo means a magnetic field, and a magnetic field means the atmosphere you build will stay."));

        P(new TerraformProjectInfo(TerraformProjectType.MagneticShield, "Magnetospheric Shield", TerraformProblem.NoMagnetosphere, "X11",
            420, 620, 0, 60f, 10f,
            "A superconducting station at the world's sunward Lagrange point casts an artificial magnetic umbrella over the whole planet. Cheaper than restarting a core, but it must be maintained forever."));

        P(new TerraformProjectInfo(TerraformProjectType.SpinUp, "Rotational Acceleration", TerraformProblem.DayTooLong, "X10",
            640, 780, 0, 95f, 12f,
            "Mass drivers fire along the equator for years on end, spinning a sluggish world up until it has real days and nights instead of a baked face and a frozen one."));

        P(new TerraformProjectInfo(TerraformProjectType.SpinDown, "Rotational Braking", TerraformProblem.DayTooShort, "X10",
            640, 780, 0, 95f, 12f,
            "The same mass drivers run in reverse, bleeding off a violent world's spin until its storms die down to something survivable."));

        P(new TerraformProjectInfo(TerraformProjectType.AxialCorrection, "Axial Correction", TerraformProblem.UnstableAxis, "X10",
            580, 700, 0, 85f, 9f,
            "Carefully aimed asteroid impacts and thruster arrays right a badly tilted world, trading savage seasons for mild ones."));

        P(new TerraformProjectInfo(TerraformProjectType.OrbitShiftOut, "Orbital Migration — Outward", TerraformProblem.OrbitTooClose, "X9",
            1200, 1500, 0, 160f, 20f,
            "The largest thing your civilization can attempt: swing asteroids past the planet for centuries, stealing momentum until its whole orbit walks outward, away from the fire. Nothing else can save a world this close to its star."));

        P(new TerraformProjectInfo(TerraformProjectType.OrbitShiftIn, "Orbital Migration — Inward", TerraformProblem.OrbitTooFar, "X9",
            1200, 1500, 0, 160f, 20f,
            "Gravity tugs drag a frozen exile inward over generations until it finally sits in the light. The single most expensive act of engineering there is."));

        P(new TerraformProjectInfo(TerraformProjectType.CaptureMoon, "Capture a Moon", TerraformProblem.UnstableAxis, "X13",
            760, 640, 0, 100f, 8f,
            "Wrangle a captured asteroid into a stable orbit. Its tides stir the oceans and its pull locks the planet's axis steady for good."),
            (b, s) => b.moons == null || b.moons.Count == 0);

        P(new TerraformProjectInfo(TerraformProjectType.RemoveMoon, "Lunar Disassembly", TerraformProblem.UnstableAxis, "X13",
            900, 1100, 0, 130f, 10f,
            "One of this world's moons is wrecking it — hammering tides, dragging the axis around. Take the moon apart and mine the pieces."),
            (b, s) => b.moons != null && b.moons.Count >= 3);

        P(new TerraformProjectInfo(TerraformProjectType.GravityAnchors, "Gravity Anchors", TerraformProblem.LowGravity, "X12",
            820, 980, 0, 105f, 10f,
            "Degenerate-matter anchors sunk into the crust pull surface gravity up to something a body can live in — and let the world hold onto its air."));

        P(new TerraformProjectInfo(TerraformProjectType.GasGiantShell, "Shellworld Construction", TerraformProblem.NoSurface, "X12",
            2400, 2800, 400, 240f, 30f,
            "Build a solid shell around a gas giant and stand on THAT. An entire artificial world with a hundred times the land of a planet — the final act of a mature civilization."),
            (b, s) => b.type == CelestialBodyType.GasGiant);

        // ================= TAKING AWAY WHAT A SPECIES DOESN'T WANT =================
        P(new TerraformProjectInfo(TerraformProjectType.HydrosphereVenting, "Hydrosphere Venting", TerraformProblem.TooMuchWater, "X15",
            520, 780, 0, 85f, 14f,
            "Orbital lasers crack the oceans into hydrogen and oxygen and let the solar wind carry them off. Drowned world in, dry land out — and the water recovered on the way is yours to keep."),
            (b, s) => b.type == CelestialBodyType.OceanPlanet || b.type == CelestialBodyType.IcePlanet ||
                      (b.resources != null && b.resources.Get(ResourceType.Water) > 260f));

        P(new TerraformProjectInfo(TerraformProjectType.CrustalSequestration, "Crustal Sequestration", TerraformProblem.TooMuchWater, "X15",
            420, 460, 0, 65f, 10f,
            "Rather than throw the water away, drive it into the crust as mineral hydrates. Gentler and cheaper than venting an ocean into space, and it leaves the water where it can be recovered later."));

        P(new TerraformProjectInfo(TerraformProjectType.AtmosphericThinning, "Atmospheric Thinning", TerraformProblem.AtmosphereTooThick, "X15",
            380, 520, 0, 60f, 9f,
            "Bleed off a crushing atmosphere until the pressure is something a body can stand up in."));

        // The catch-all, and the most expensive thing on this list short of a shellworld: don't adjust
        // the world, rebuild it. Turns a drowned planet into a furnace for the Pyrothians, or a dead
        // rock into an ocean for the Aquarii. This is what makes a 24% world reachable at all.
        P(new TerraformProjectInfo(TerraformProjectType.WorldRemodelling, "Planetary Remodelling", TerraformProblem.WrongWorldType, "X16",
            1600, 1900, 0, 200f, 26f,
            "Rebuild the world into the kind of place your species actually wants: boil off its seas or fill them, ignite its volcanism or quench it, freeze it or thaw it. The single greatest act of terraforming there is, and the only answer to a world that is simply the wrong sort of place."));
    }

    // Every project that addresses a problem this world actually has, that suits this kind of world.
    // `techGated` false ignores research, which is how the UI shows what you COULD reach with more of it.
    public static List<TerraformProjectInfo> For(CelestialBody b, Species s, bool techGated)
    {
        var result = new List<TerraformProjectInfo>();
        if (b == null || s == null) return result;
        var issues = TerraformDiagnosis.Analyze(b, s);

        foreach (var p in All)
        {
            if (p == null) continue;
            if (TerraformProjects.IsDone(b, p.type)) continue;
            if (!TerraformDiagnosis.Has(issues, p.solves)) continue;
            if (p.applies != null && !p.applies(b, s)) continue;
            if (techGated && !string.IsNullOrEmpty(p.requiredTech) && !TechManager.IsResearched(p.requiredTech)) continue;
            result.Add(p);
        }
        return result;
    }
}

// ---------------------------------------------------------------------------------------------
// Per-world record of completed projects, and what they do to the ceiling.
// ---------------------------------------------------------------------------------------------
public static class TerraformProjects
{
    public static bool IsDone(CelestialBody b, TerraformProjectType t)
        => b != null && b.terraformProjects != null && b.terraformProjects.Contains((int)t);

    public static void MarkDone(CelestialBody b, TerraformProjectType t)
    {
        if (b == null) return;
        if (b.terraformProjects == null) b.terraformProjects = new List<int>();
        if (!b.terraformProjects.Contains((int)t)) b.terraformProjects.Add((int)t);
    }

    // What the completed projects have added to this world's ceiling.
    public static float CeilingBonus(CelestialBody b)
    {
        if (b == null || b.terraformProjects == null) return 0f;
        float sum = 0f;
        foreach (int id in b.terraformProjects)
        {
            if (id < 0 || id >= TerraformProjectDatabase.All.Length) continue;
            var info = TerraformProjectDatabase.All[id];
            if (info != null) sum += info.ceilingGain;
        }
        return sum;
    }

    // The ceiling this world could reach if you finished every project you have ALREADY researched.
    public static float ReachableCeiling(CelestialBody b, Species s)
        => Colony.TerraformCeiling(b) + SumGains(TerraformProjectDatabase.For(b, s, true));

    // The ceiling if you also researched everything the project catalogue has to offer — the answer to
    // "is this world worth investing in at all?".
    public static float PotentialCeiling(CelestialBody b, Species s)
        => Colony.TerraformCeiling(b) + SumGains(TerraformProjectDatabase.For(b, s, false));

    static float SumGains(List<TerraformProjectInfo> ps)
    {
        float sum = 0f;
        foreach (var p in ps) sum += p.ceilingGain;
        return sum;
    }

    // How fast your civilization can reshape a world. This is the whole difficulty curve of
    // terraforming: a young empire hauling water by tanker is painfully slow, while a level-10 empire
    // with the full Expansion tree and precursor ecoforming rebuilds a planet in about a minute.
    // Empire level alone is worth up to 4x, and the researched speed technologies multiply on top.
    public static float SpeedFactor()
    {
        float byLevel = Mathf.Lerp(1f, 4f, Mathf.Clamp01((EmpireTech.Level - 1) / 10f));
        return Mathf.Max(0.2f, TechEffects.TerraformSpeedMult * byLevel);
    }

    // Bigger worlds are more work: every project scales with the planet you're rebuilding.
    public static float SizeScale(CelestialBody b) => b == null ? 1f : Mathf.Clamp(b.surfaceSize / 8f, 0.6f, 2.2f);

    // How bad the fault is scales the job. A world that barely suffers from a problem is a quick fix;
    // one that suffers from it badly is a long, expensive haul. This is what makes dragging a 24% world
    // up to livable so much more work than finishing off one that already sits at 76%.
    public static float SeverityScale(TerraformProjectInfo p, CelestialBody b)
    {
        var s = SpeciesManager.Current;
        if (s == null || p == null) return 1f;
        float sev = TerraformDiagnosis.SeverityOf(b, s, p.solves);
        return Mathf.Lerp(0.45f, 1.9f, Mathf.Clamp01(sev));
    }

    // Every cost and duration below is scaled by BOTH the size of the world and how badly it suffers.
    static float WorkScale(TerraformProjectInfo p, CelestialBody b) => SizeScale(b) * SeverityScale(p, b);

    public static int MetalCost(TerraformProjectInfo p, CelestialBody b)
        => Mathf.RoundToInt(p.costMetal * WorkScale(p, b) * TechEffects.BuildCostMult);
    public static int EnergyCost(TerraformProjectInfo p, CelestialBody b)
        => Mathf.RoundToInt(p.costEnergy * WorkScale(p, b) * TechEffects.BuildCostMult);
    public static int WaterCost(TerraformProjectInfo p, CelestialBody b)
        => Mathf.RoundToInt(p.costWater * WorkScale(p, b));
    public static float Duration(TerraformProjectInfo p, CelestialBody b)
        => p.duration * WorkScale(p, b) / SpeedFactor();
}
