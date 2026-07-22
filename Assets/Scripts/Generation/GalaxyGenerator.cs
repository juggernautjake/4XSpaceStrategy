using System.Collections.Generic;
using UnityEngine;

// Builds a whole galaxy of static star systems arranged around a central supermassive object.
// Guarantees the home system has a planet that is >=85% habitable for the player's species (the
// starting home world), owned by the player along with its 1-3 moons. Everything else starts owned
// by NPC factions or unclaimed.
public static class GalaxyGenerator
{
    /// Generate a whole galaxy in one call. Unchanged behaviour — it is now expressed as the three
    /// phases below so a loading screen can drive the same work a step at a time and report real
    /// progress. Anything that does not need progress should keep calling this.
    public static Galaxy Generate(SolarSystemGenerator gen, int systemCount, int avgPlanets, Species homeSpecies)
    {
        int count = ClampSystems(systemCount);
        var galaxy = Begin(gen, avgPlanets);
        for (int i = 0; i < count; i++) AddSystem(galaxy, gen, i, count);
        Finish(galaxy, homeSpecies, count);
        return galaxy;
    }

    public static int ClampSystems(int systemCount) => Mathf.Clamp(systemCount, 1, 12);

    /// Phase 1 — the empty galaxy: its name, its seed, its core.
    public static Galaxy Begin(SolarSystemGenerator gen, int avgPlanets)
    {
        avgPlanets = Mathf.Clamp(avgPlanets, 1, 8);
        gen.minBodies = Mathf.Max(1, avgPlanets - 1);
        gen.maxBodies = avgPlanets + 2;

        NameGenerator.Reset();   // fresh unique-name registry for this galaxy

        var galaxy = new Galaxy();
        galaxy.name = NameGenerator.GalaxyName();
        galaxy.visualSeed = Random.Range(1, 1000000);
        galaxy.center = StarDatabase.BlackHole();
        galaxy.center.visualScale = 6f;
        // The core is named FOR its galaxy — "The Aureth Veil Core" — so the widest zoom reads as one
        // named place rather than a generic black hole floating in a generic galaxy.
        galaxy.center.name = $"{galaxy.name} Core";
        galaxy.centerPosition = Vector3.zero;

        // The home cluster, rolled here so the loading screen can show the real sun(s) while the galaxy
        // builds. Almost always one star; occasionally a bound binary or (rarer still) a trinary.
        galaxy.homeStars = RollHomeCluster();
        return galaxy;
    }

    // Almost always a single sun; occasionally a binary, rarely a trinary — same low rate shape as an
    // ordinary system's cluster roll (RollStarSystem/RollClusterStarType), but every home sun stays a
    // pleasant G or K. That restriction is what keeps the difficulty's habitability guarantee (below,
    // ForceHomeWorld) holding for 2 or 3 suns exactly as it does for 1: StarDatabase.Combine's summed
    // luminosity for up to three G/K stars (max ~3x a single G) never approaches the O/B range where
    // hasHabitableZone would be a lie, so the cradle never needs to be capped back down to binary.
    static List<StarData> RollHomeCluster()
    {
        float c = Random.value;
        int count = c < 0.04f ? 3 : (c < 0.14f ? 2 : 1);   // ~4% trinary, ~10% binary, ~86% single
        var list = new List<StarData>(count);
        for (int i = 0; i < count; i++)
            list.Add(StarDatabase.Get(Random.value < 0.5f ? StarType.G : StarType.K));
        return list;
    }

    /// Phase 2 — one star system, with all its worlds. This is the expensive part and the reason the
    /// loading bar exists: it is called once per system so the caller can yield between them.
    public static void AddSystem(Galaxy galaxy, SolarSystemGenerator gen, int i, int systemCount)
    {
        var it = AddSystemStepped(galaxy, gen, i, systemCount);
        while (it.MoveNext()) { }
    }

    /// The same, yielding after each world so a loading screen gets frames to animate in. One
    /// implementation: AddSystem above just drains this.
    public static System.Collections.IEnumerator AddSystemStepped(
        Galaxy galaxy, SolarSystemGenerator gen, int i, int systemCount)
    {
        var inner = gen.GenerateSystemStepped();
        while (inner.MoveNext()) yield return inner.Current;
        Finalise(galaxy, gen, gen.lastSystem, i, systemCount);
    }

    static void Finalise(Galaxy galaxy, SolarSystemGenerator gen, List<CelestialBody> bodies,
                         int i, int systemCount)
    {
        var sys = new StarSystemData
        {
            name = gen.currentSystemName,
            stars = new List<StarData>(gen.stars),
            isBlackHole = gen.isBlackHole,
            combinedStar = gen.currentStar,
            bodies = bodies,
            galaxyPosition = SpiralPosition(i, systemCount)
        };
        LinkBodies(sys);
        galaxy.systems.Add(sys);
    }

    /// Phase 3 — everything that can only be done once every system exists: the home world, ownership,
    /// habitability, and the things scattered across the finished galaxy.
    public static void Finish(Galaxy galaxy, Species homeSpecies, int systemCount)
    {
        galaxy.homeIndex = 0;
        var home = galaxy.systems[0];
        home.isHome = true;
        home.galaxyPosition = SpiralPosition(0, systemCount);

        ForceHomeWorld(home, homeSpecies, galaxy.homeStars);
        AssignOwnership(galaxy);
        Recompute(galaxy);

        // Record every body's generated orbit so Dev Mode can reset a planet (or a whole system) back to
        // where it started after the orbit editor moves it. Done last, once every orbit is final — the home
        // world is re-homed by ForceHomeWorld, and EnforceSystem may nudge radii before this point.
        foreach (var sys in galaxy.systems)
            foreach (var b in sys.AllBodies())
                b.naturalOrbitRadius = b.orbitRadius;

        // Hide ancient derelict stations at odd orbits, THEN scatter the ten Vael fragments across worlds
        // AND those derelicts (so some fragments drift out at a black hole or in dead space, not just on a moon).
        DerelictGen.Populate(galaxy);
        AncientClues.SeedGalaxy(galaxy);
    }

    // Golden-angle spiral so systems spread out and don't overlap.
    //
    // Distances are DELIBERATELY large — the inner ring starts 900 units out and each system adds 260.
    // The old layout put the home system 170 units from the galactic core and stepped 95 per system,
    // which packed twelve systems into a disc barely a thousand units across. Two things went wrong with
    // that. Interstellar space did not read as empty: at galaxy view the systems sat almost shoulder to
    // shoulder, so pulling back gained you nothing and the map felt like one crowded cluster rather than
    // a galaxy. And the central black hole, which has to be drawn large enough to read as the thing the
    // whole galaxy turns around, had no room to be drawn at all without swallowing the nearest systems.
    //
    // Everything downstream is derived from GalaxyRadius() rather than hardcoded — the camera's framing
    // height, the render-tier boundaries, the proxy sizes and the zoom ceiling — so widening the layout
    // rescales the entire zoom ladder with it and no constant elsewhere needs touching.
    const float InnerRadius = 1400f;
    const float RadiusStep = 420f;

    static Vector3 SpiralPosition(int i, int count)
    {
        // Home sits on the inner ring like everything else, rather than at a hardcoded offset near the
        // core — that offset is what used to put it inside the black hole's own halo.
        //
        // The inner ring is 1400 units out and the step is 420, which is wide enough that the spacing
        // survives every zoom level rather than only reading well at one. Two things squeeze it: the
        // galactic core's accretion glow, which is drawn large on purpose and grows with the zoom ramp,
        // and the system proxies themselves, which grow to hold a constant on-screen size. At the old
        // 900/260 the innermost systems were inside the core's halo at wide zoom and their proxies were
        // beginning to crowd each other. Pushing the ring out and widening the step keeps clear air in
        // both directions at every height.
        float angle = i * 2.399963f;                     // golden angle (radians)
        float radius = InnerRadius + i * RadiusStep;
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    static void LinkBodies(StarSystemData sys)
    {
        foreach (var b in sys.bodies)
        {
            b.hostStar = sys.combinedStar;
            b.system = sys;
            foreach (var m in b.moons) { m.hostStar = sys.combinedStar; m.system = sys; }
        }
    }

    // Ensures a >=85%-habitable home world for the species, with 1-3 moons, all player-owned.
    static void ForceHomeWorld(StarSystemData home, Species species, List<StarData> homeStars)
    {
        // The cluster (1-3 suns) comes from the plan rolled in Begin, NOT from a fresh roll here — that is
        // what makes the star(s) the loading screen showed and the star(s) the player actually gets the
        // same objects. StarCluster.Layout (the renderer's own geometry) treats stars[0]/[1] as the inner
        // pair for a trinary, so the list order from RollHomeCluster is kept as-is; only the NAMES below
        // are ranked, exactly like every other multi-star system (SolarSystemGenerator.NameStars).
        home.stars = homeStars != null && homeStars.Count > 0
            ? new List<StarData>(homeStars)
            : new List<StarData> { StarDatabase.Get(StarType.G) };

        if (home.stars.Count == 1)
        {
            home.stars[0].name = home.name;
        }
        else
        {
            var byMass = new List<StarData>(home.stars);
            byMass.Sort((a, b) => b.mass.CompareTo(a.mass));   // most massive first
            for (int rank = 0; rank < byMass.Count; rank++)
                byMass[rank].name = $"{home.name} {(char)('A' + rank)}";
        }

        home.isBlackHole = false;
        // Combine handles 1-3 suns identically (it's the same call an ordinary multi-star system makes),
        // so the >=85% guarantee below never has to know whether the home cradle is single, binary or
        // trinary: hasHabitableZone comes back true either way, and the zone/orbit-safety code downstream
        // already reads the combined star rather than any one sun.
        home.combinedStar = StarDatabase.Combine(home.stars);
        home.combinedStar.name = home.name;
        LinkBodies(home);

        if (home.bodies.Count == 0) home.bodies.Add(new CelestialBody(CelestialBodyType.RockyPlanet));
        Habitability.GetZone(home.combinedStar, species, out float inner, out float outer);
        float center = (inner + outer) * 0.5f;

        // Promote the planet ALREADY nearest the habitable zone, and convert it where it stands.
        //
        // This used to grab bodies[0] — the innermost planet — and teleport it out to the zone centre,
        // ignoring the spacing the layout had just computed. It landed the home world on top of
        // whatever already orbited there, which is exactly how planets ended up overlapping and their
        // orbit rings crossing. Its habitability is force-set by difficulty below and locked, so it does
        // not actually need to sit at the centre — only to be the right KIND of world.
        var planet = home.bodies[0];
        float bestD = float.MaxValue;
        foreach (var b in home.bodies)
        {
            float d = Mathf.Abs(b.distanceFromStar - center);
            if (d < bestD) { bestD = d; planet = b; }
        }

        planet.type = BestTypeFor(species);
        // The home world is a comfortable Earth-to-super-Earth (Mass 2-4, Earth being 2); its grid size
        // derives from that (MassRules.SurfaceSize → ~6-12 cells-per-side unit).
        planet.mass = Random.Range(2, 5);
        planet.surfaceSize = MassRules.SurfaceSize(planet.mass);
        planet.atmosphereThickness = AtmosphereRules.ForBody(planet.type, planet.surfaceSize);
        planet.hasTectonics = TectonicsRules.Roll(planet.type, planet.surfaceSize);
        planet.orbitPhase = Random.Range(0f, 360f);
        planet.spinSpeed = OrbitalMechanics.Spin(planet, Random.Range(0.7f, 1.3f));
        SeedTerrain(planet);
        // The home world's climate matches the species' preferred temperature, so a Pyrothian home
        // reads hot and a Cryithn home reads frozen — the race's biology visibly shapes its cradle.
        var htp = planet.terrainParams;
        htp.heat = CradleHeat(species, planet);
        planet.terrainParams = htp;
        // Re-capture AFTER the override: this is the home world's natural climate, not what SeedTerrain
        // rolled a moment ago. It's already the species' ideal — a cradle needs no terraforming — and
        // recording it as anything else would mean terraforming it "improved" it away from itself.
        TerraformVisuals.CaptureNatural(planet);
        // The home world is generated exactly like any other body as far as its biosphere goes — a
        // species whose ideal cradle is Rocky/Ocean and within the liquid-water band gets one for free,
        // same rule as everywhere else (see BiosphereRules). Set before GenerateSurface so the baked
        // terrain reflects it immediately rather than shipping sterile-looking on turn one.
        planet.biosphereActive = BiosphereRules.GeneratesWithBiosphere(planet);
        planet.surface = PlanetTerrainGenerator.GenerateSurface(planet);
        OreGenerator.Populate(planet);
        planet.resources = new ResourceDeposit();
        ResourceGenerator.GenerateResources(planet);
        // KEEPS ITS GENERATED NAME. This used to overwrite it with the literal string "Homeworld",
        // which is why the loading screen's reveal caption read "Homeworld — your homeworld" and the
        // welcome message read "Welcome to Homeworld". Every other planet in the galaxy is named by
        // NameGenerator.PlanetName from its system and index; the player's own world is the last one
        // that should be the exception. It is already flagged as home by system.isHome and by being the
        // player's first colony — it does not also need to be called it.
        planet.hostStar = home.combinedStar;
        planet.system = home;

        // Moons: usually 1, sometimes 2, rarely 3.
        int moonCount = Random.value < 0.6f ? 1 : (Random.value < 0.75f ? 2 : 3);
        planet.moons.Clear();

        // Continue the system's id sequence rather than leaving these at the default 0.
        //
        // `id` is not decoration: it is the parent reference in the save file, the cache key for
        // TectonicsMap, part of the (body, kind) key for SurfaceIndex, and now the seed for a body's grid
        // size jitter. Three home moons all sharing id 0 — and colliding with whichever body genuinely
        // holds id 0 — is a real aliasing bug in every one of those, and it has been latent here because
        // ForceHomeWorld builds its moons by hand instead of going through the generator.
        int nextId = 0;
        foreach (var b in home.AllBodies()) nextId = Mathf.Max(nextId, b.id);
        nextId++;
        // Start clear of the home world's own surface rather than at a fixed 2.6, which a large world
        // (surfaceSize up to 16) came close to touching.
        float moonR = Mathf.Max(0.6f, planet.surfaceSize * 0.08f) * 0.5f + 0.3f + 0.9f;
        for (int m = 0; m < moonCount; m++)
        {
            var moon = new CelestialBody(CelestialBodyType.Moon)
            {
                id = nextId++,
                name = $"Homeworld-{(char)('a' + m)}"
            };
            moon.mass = MassRules.ForMoon(planet.mass);
            moon.surfaceSize = MassRules.SurfaceSize(moon.mass);
            moon.atmosphereThickness = AtmosphereRules.ForBody(moon.type, moon.surfaceSize);
            moon.hasTectonics = TectonicsRules.Roll(moon.type, moon.surfaceSize);
            SeedTerrain(moon);
            // BEFORE GenerateSurface. The grid size is capped at half the host's (MapMetrics.SurfW), and
            // that cap reads parentBody — so setting it afterwards meant the moon was BUILT at its
            // uncapped size and then RENDERED and reported at the capped one. Two grids that disagree,
            // which is the exact bug MapMetrics exists to prevent.
            moon.parentBody = planet;
            moon.surface = PlanetTerrainGenerator.GenerateSurface(moon);
            OreGenerator.Populate(moon);
            ResourceGenerator.GenerateResources(moon);
            moon.orbitRadius = moonR;
            moon.orbitSpeed = OrbitalMechanics.MoonAngularSpeed(planet, moonR);
            moon.spinSpeed = OrbitalMechanics.Spin(moon, Random.Range(0.7f, 1.3f));
            moon.orbitPhase = Random.Range(0f, 360f);
            moon.distanceFromStar = planet.distanceFromStar;
            moon.hostStar = home.combinedStar;
            moon.system = home;
            // Claimed by BIRTHRIGHT — ours from the start, and fully surveyed — but NOT yet settled
            // (no city). They may or may not be habitable; you terraform them (easier here) then found
            // a city to develop them.
            moon.owner = FactionManager.Player;
            moon.birthrightClaim = true;
            moon.visited = true;
            moon.explorationProgress = 1f;
            planet.moons.Add(moon);
            moonR += 0.6f + Random.Range(1.6f, 2.6f);   // clear both moons' discs, not just "some gap"
        }

        planet.owner = FactionManager.Player;
        planet.birthrightClaim = true;
        // The ONLY world that starts settled. Its moons are claimed by the same birthright but stay bare
        // rock until you terraform and settle them — a claim is a flag, not a population.
        planet.settled = true;
        // The capital is an established world, not a landing site: about a million people, adjusted for
        // how the species breeds and how long it lives (see Population.HomeStart).
        planet.cities = 1;
        planet.population = Population.HomeStart(species);
        if (!planet.buildings.Contains((int)BuildingType.City)) planet.buildings.Add((int)BuildingType.City);
        planet.shipyardLevel = 1;          // the capital always has a working (level-1) shipyard
        planet.researchCenterLevel = 1;    // ...and its founding laboratory, so research can start at all
        planet.explorationProgress = 1f;   // home world is fully known from the start
        // ...and its capitol, which is a real structure on the surface grid rather than an abstraction.
        // It carries the colony's founding reactor, so this is also what lights the home world's power
        // grid — without it the capital would open with every mine and factory unpowered. Every OTHER
        // settled world gets its seat from the colony ship that grounded itself there; this one was
        // simply declared settled, so it has to be given one. (See SurfaceBuildManager.EnsureColonySeat.)
        SurfaceBuildManager.EnsureColonySeat(planet);

        // Extra starting resources by difficulty.
        var keys = new List<ResourceType>(planet.resources.resources.Keys);
        foreach (var k in keys) planet.resources.resources[k] *= GameConfig.HomeResourceBonus;

        // Difficulty sets the home world's habitability (Easy=100, Medium=90-99, Hard=80-89), locked
        // so it isn't recomputed away.
        planet.isHabitable = true;
        planet.habitability = GameConfig.HomeHabitability();
        planet.habitabilityLocked = true;

        // ForceHomeWorld resizes the home world and rebuilds its moon system AFTER the system was laid
        // out, so its band is a different shape than the layout reserved for it. Re-enforce, or a big
        // home world with three moons quietly reaches into its neighbour.
        OrbitSafety.EnforceSystem(home.bodies, home.combinedStar);

        if (!OrbitSafety.Validate(home.bodies, out string problem))
            Debug.LogWarning($"[OrbitSafety] home system {home.name}: {problem}");
    }

    // ============================================================================================
    // THE CRADLE'S CLIMATE — SOLVED FOR, NOT ASSIGNED
    //
    // This used to be `heat = Lerp(0.55, 1.7, idealTemp)`, and that produced home worlds that were too
    // hot to be alive. `heat` is calibrated so heat = 1 reads as Earth's ~15°C, but the temperature the
    // rest of the game reads (PlanetTemperature.BaseCelsius) then adds GREENHOUSE WARMING on top —
    // atmosphereThickness x 45°C — and the lerp never knew about it.
    //
    // A home world's atmosphere comes from its size (AtmosphereRules), and its size is rolled from
    // mass 2-4, so the greenhouse term is +15°C, +19°C or +23°C depending on the roll. For a Terran
    // cradle that put the average at 47°C, 51°C or 55°C against a 50°C liquid-water ceiling — so two
    // rolls out of three generated a homeworld too hot for a biosphere, and the third cleared it by
    // less than 3°C. A Sylvan cradle (idealTemp 0.55) never cleared it at all, which is a strange fate
    // for the photosynthetic species. BiosphereRules.GeneratesWithBiosphere then correctly refused,
    // and the world generated sterile.
    //
    // The other three gates were never the problem: sea level generates in 0.36-0.62 against a 0.15
    // floor, atmosphere is 0.33-0.51 against a 0.12 floor, and the type is Rocky or Ocean by
    // construction. Only temperature failed, and it failed silently.
    //
    // So: pick the temperature the cradle should HAVE and solve for the heat that produces it on this
    // particular world, through the same law everything else reads. Bigger home worlds now get a lower
    // heat to offset their thicker air, and all three sizes land on the same comfortable climate.
    // ============================================================================================
    static float CradleHeat(Species species, CelestialBody planet)
    {
        float ideal = Mathf.Clamp01(species.idealTemp);

        // Only the cradles that are MEANT to be alive are pinned to the liquid-water band. A Pyrothian
        // furnace and a Cryithn ice world never generate with a biosphere whatever their temperature
        // (GeneratesWithBiosphere gates on type), so forcing them into a temperate band would only strip
        // the character out of their worlds for no gain — they keep the original curve.
        if (!CradleWantsLife(species)) return Mathf.Lerp(0.55f, 1.7f, ideal);

        // Inside the band with real margin at both ends, because heat is not frozen after generation:
        // terraforming moves it, and the Dev sandbox moves it further. A cradle that started 1°C inside
        // the ceiling would fall out of it the first time anything nudged the world.
        return PlanetTemperature.HeatForCelsius(
            CradleTargetCelsius(species), planet.atmosphereThickness, planet.type);
    }

    /// Is this species' own cradle one that is MEANT to be alive — a Rocky or Ocean world?
    ///
    /// Gated on the SPECIES, not on the world being judged, and the distinction is load-bearing. A
    /// Pyrothian furnace and a Cryithn ice world never generate with a biosphere whatever their
    /// temperature (BiosphereRules gates on type), so their cradles keep the raw heat curve and their
    /// character. Ask this about the BODY instead and a Cryithn terraforming a rocky world gets pinned
    /// to a Terran's +11°C rather than their own −11°C — the wrong species' answer, on their own world.
    public static bool CradleWantsLife(Species species)
    {
        var t = BestTypeFor(species);
        return t == CelestialBodyType.RockyPlanet || t == CelestialBodyType.OceanPlanet;
    }

    /// The temperature a species' cradle is aimed at, in °C — shared with TerraformVisuals so that
    /// terraforming a world toward this species' ideal converges on the SAME climate the cradle was
    /// built with, rather than on a second, greenhouse-blind one that drifts away from it.
    public static float CradleTargetCelsius(Species species)
        => Mathf.Lerp(BiosphereRules.MinLiquidC + 5f, BiosphereRules.MaxLiquidC - 8f,
                      Mathf.Clamp01(species != null ? species.idealTemp : 0.5f));

    // The body type the species is happiest on (highest affinity).
    /// Public because "what kind of world is this species' cradle" is a question terraforming asks too
    /// (through CradleWantsLife), and two answers to it would drift apart.
    public static CelestialBodyType BestTypeFor(Species s)
    {
        CelestialBodyType[] candidates =
        {
            CelestialBodyType.OceanPlanet, CelestialBodyType.RockyPlanet, CelestialBodyType.IcePlanet,
            CelestialBodyType.VolcanicPlanet, CelestialBodyType.BarrenPlanet
        };
        CelestialBodyType best = CelestialBodyType.RockyPlanet;
        float bestA = -1f;
        foreach (var t in candidates)
        {
            float a = s.Affinity(t);
            if (a > bestA) { bestA = a; best = t; }
        }
        return best;
    }

    // For now nobody else claims anything: the player owns only the home planet + its moons.
    static void AssignOwnership(Galaxy galaxy)
    {
        var home = galaxy.Home;
        if (home != null) home.owner = FactionManager.Player;
        // Every other body/system stays unclaimed (null owner) until captured.
    }

    static void Recompute(Galaxy galaxy)
    {
        var species = SpeciesManager.Current;
        foreach (var sys in galaxy.systems)
            foreach (var b in sys.AllBodies())
            {
                if (b.habitabilityLocked) { b.terraformability = Habitability.Terraformability(b.hostStar, species, b); continue; }
                b.isHabitable = Habitability.IsHabitable(b.hostStar, species, b.type, b.distanceFromStar);
                b.habitability = Habitability.Rate(b.hostStar, species, b.type, b.distanceFromStar);
                b.terraformability = Habitability.Terraformability(b.hostStar, species, b);

                // Home moons are "easier to terraform": guarantee their ceiling reaches livability so
                // you can always make them habitable and settle them.
                if (b.birthrightClaim)
                    b.terraformability = Mathf.Max(b.terraformability, UnitManager.ColonizeMinHabitability + 20f);
            }
    }

    static void SeedTerrain(CelestialBody body)
    {
        body.terrainSeed = Random.Range(0f, 10000f);
        body.continentFrequency = Mathf.Clamp(body.surfaceSize * 0.32f, 2.5f, 8f);
        TerrainVariance.Apply(body);
        if (body.hasTectonics) TectonicsRules.BoostRidge(body);   // active plates fold up more mountains
        // The climate nature gave it. Terraforming lerps FROM here (TerraformVisuals), so it has to be
        // captured before anything moves it.
        TerraformVisuals.CaptureNatural(body);
    }
}
