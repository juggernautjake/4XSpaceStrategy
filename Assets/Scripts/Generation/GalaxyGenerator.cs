using System.Collections.Generic;
using UnityEngine;

// Builds a whole galaxy of static star systems arranged around a central supermassive object.
// Guarantees the home system has a planet that is >=85% habitable for the player's species (the
// starting home world), owned by the player along with its 1-3 moons. Everything else starts owned
// by NPC factions or unclaimed.
public static class GalaxyGenerator
{
    public static Galaxy Generate(SolarSystemGenerator gen, int systemCount, int avgPlanets, Species homeSpecies)
    {
        systemCount = Mathf.Clamp(systemCount, 1, 12);
        avgPlanets = Mathf.Clamp(avgPlanets, 1, 8);
        gen.minBodies = Mathf.Max(1, avgPlanets - 1);
        gen.maxBodies = avgPlanets + 2;

        NameGenerator.Reset();   // fresh unique-name registry for this galaxy

        var galaxy = new Galaxy();
        galaxy.center = StarDatabase.BlackHole();
        galaxy.center.visualScale = 6f;
        galaxy.center.name = "Galactic Core";
        galaxy.centerPosition = Vector3.zero;

        for (int i = 0; i < systemCount; i++)
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

        galaxy.homeIndex = 0;
        var home = galaxy.systems[0];
        home.isHome = true;
        home.galaxyPosition = SpiralPosition(0, systemCount);

        ForceHomeWorld(home, homeSpecies);
        AssignOwnership(galaxy);
        Recompute(galaxy);
        return galaxy;
    }

    // Golden-angle spiral so systems spread out and don't overlap.
    static Vector3 SpiralPosition(int i, int count)
    {
        if (i == 0) return new Vector3(0, 0, 170f);      // home a bit off-centre
        float angle = i * 2.399963f;                     // golden angle (radians)
        float radius = 200f + i * 95f;
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius + 170f);
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
        var planet = home.bodies[0];

        Habitability.GetZone(home.combinedStar, species, out float inner, out float outer);
        float center = (inner + outer) * 0.5f;

        planet.type = BestTypeFor(species);
        planet.surfaceSize = Random.Range(11, 17);
        planet.distanceFromStar = center;
        planet.orbitRadius = center;
        planet.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(home.combinedStar, center);
        planet.orbitPhase = Random.Range(0f, 360f);
        planet.spinSpeed = OrbitalMechanics.Spin(planet, Random.Range(0.7f, 1.3f));
        SeedTerrain(planet);
        // The home world's climate matches the species' preferred temperature, so a Pyrothian home
        // reads hot and a Cryithn home reads frozen — the race's biology visibly shapes its cradle.
        var htp = planet.terrainParams;
        htp.heat = Mathf.Lerp(0.55f, 1.7f, Mathf.Clamp01(species.idealTemp));
        planet.terrainParams = htp;
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
        float moonR = 2.6f;
        for (int m = 0; m < moonCount; m++)
        {
            var moon = new CelestialBody(CelestialBodyType.Moon) { name = $"Homeworld-{(char)('a' + m)}" };
            moon.surfaceSize = Random.Range(4, 12);
            SeedTerrain(moon);
            moon.surface = PlanetTerrainGenerator.GenerateSurface(moon);
            OreGenerator.Populate(moon);
            ResourceGenerator.GenerateResources(moon);
            moon.orbitRadius = moonR;
            moon.orbitSpeed = OrbitalMechanics.MoonAngularSpeed(planet, moonR);
            moon.spinSpeed = OrbitalMechanics.Spin(moon, Random.Range(0.7f, 1.3f));
            moon.orbitPhase = Random.Range(0f, 360f);
            moon.distanceFromStar = planet.distanceFromStar;
            moon.parentBody = planet;
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
            moonR += Random.Range(1.6f, 2.6f);
        }

        planet.owner = FactionManager.Player;
        planet.birthrightClaim = true;
        planet.shipyardLevel = 1;          // the capital always has a working (level-1) shipyard
        planet.researchCenterLevel = 1;    // ...and its founding laboratory, so research can start at all
        planet.explorationProgress = 1f;   // home world is fully known from the start

        // Extra starting resources by difficulty.
        var keys = new List<ResourceType>(planet.resources.resources.Keys);
        foreach (var k in keys) planet.resources.resources[k] *= GameConfig.HomeResourceBonus;

        // Difficulty sets the home world's habitability (Easy=100, Medium=90-99, Hard=80-89), locked
        // so it isn't recomputed away.
        planet.isHabitable = true;
        planet.habitability = GameConfig.HomeHabitability();
        planet.habitabilityLocked = true;
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
    }
}
