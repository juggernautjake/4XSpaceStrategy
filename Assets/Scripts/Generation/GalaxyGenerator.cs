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
        var galaxy = Begin(gen, systemCount, avgPlanets);
        for (int i = 0; i < count; i++) AddSystem(galaxy, gen, i, count);
        Finish(galaxy, homeSpecies, count);
        return galaxy;
    }

    public static int ClampSystems(int systemCount) => Mathf.Clamp(systemCount, 1, 12);

    /// Phase 1 — the empty galaxy: its name, its seed, its core.
    public static Galaxy Begin(SolarSystemGenerator gen, int systemCount, int avgPlanets)
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
        return galaxy;
    }

    /// Phase 2 — one star system, with all its worlds. This is the expensive part and the reason the
    /// loading bar exists: it is called once per system so the caller can yield between them.
    public static void AddSystem(Galaxy galaxy, SolarSystemGenerator gen, int i, int systemCount)
    {
        var bodies = gen.GenerateSystem();
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

        ForceHomeWorld(home, homeSpecies);
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
    static void ForceHomeWorld(StarSystemData home, Species species)
    {
        // A pleasant single sun so the home always has a stable habitable zone.
        var starType = Random.value < 0.5f ? StarType.G : StarType.K;
        home.stars = new List<StarData> { StarDatabase.Get(starType) };
        home.stars[0].name = home.name;
        home.isBlackHole = false;
        home.combinedStar = home.stars[0];
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
        htp.heat = Mathf.Lerp(0.55f, 1.7f, Mathf.Clamp01(species.idealTemp));
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
        planet.name = "Homeworld";
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

    // The body type the species is happiest on (highest affinity).
    static CelestialBodyType BestTypeFor(Species s)
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
