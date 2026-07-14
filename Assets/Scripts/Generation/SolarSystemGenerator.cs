using System.Collections.Generic;
using UnityEngine;

public class SolarSystemGenerator : MonoBehaviour
{
    public int minBodies = 2;
    public int maxBodies = 6;

    public StarType currentStarType;
    public StarData currentStar;     // combined physical data for the cluster (light/heat/HZ/orbits)
    public List<StarData> stars = new List<StarData>();  // 1-3 suns (or a single black hole)
    public bool isBlackHole;

    static readonly string[] NamePrefixes =
    { "Kepler", "Cygnus", "Vega", "Tau Ceti", "Draconis", "Helios", "Orion", "Lyra",
      "Aquila", "Nyx", "Erebus", "Rhea", "Ymir", "Talos", "Zephyr", "Kestrel" };

    static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    int _idCounter;

    public List<CelestialBody> GenerateSystem()
    {
        _idCounter = 0;
        List<CelestialBody> system = new();

        RollStarSystem();

        // Body count honours minBodies/maxBodies (the galaxy generator sets these from "avg planets").
        int lo = Mathf.Max(1, minBodies);
        int hi = Mathf.Max(lo, maxBodies);
        int bodyCount = Mathf.Clamp(Random.Range(lo, hi + 1), 1, 10);
        string systemName = NamePrefixes[Random.Range(0, NamePrefixes.Length)];

        float currentRadius = Random.Range(7f, 10f);

        for (int i = 0; i < bodyCount; i++)
        {
            float orbitPercent = bodyCount > 1 ? (float)i / (bodyCount - 1) : 0.5f;
            CelestialBodyType type = RollBodyByDistance(orbitPercent, currentStarType);

            CelestialBody body = MakeBody(type);
            body.name = $"{systemName} {Roman[Mathf.Min(i, Roman.Length - 1)]}";

            // Orbital layout (data-authoritative so save/load & sandbox can round-trip it).
            body.distanceFromStar = currentRadius;
            body.orbitRadius = currentRadius;
            body.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(currentStar, currentRadius);
            body.spinSpeed = OrbitalMechanics.Spin(body, Random.Range(0.7f, 1.3f));
            body.orbitPhase = Random.Range(0f, 360f);
            body.orbitDirection = Random.value < 0.9f ? 1 : -1;
            body.inclination = Random.Range(-7f, 7f);
            body.eccentricity = Random.Range(0f, 0.14f);

            ApplyHabitability(body);
            POIGenerator.Populate(body);

            // Moons
            int moonCount = RollMoonCount(type);
            float moonR = 2.6f;
            for (int m = 0; m < moonCount; m++)
            {
                CelestialBody moon = new(CelestialBodyType.Moon) { id = _idCounter++ };
                moon.name = $"{body.name}-{(char)('a' + m)}";
                moon.surfaceSize = Random.Range(4, 9);
                SeedTerrain(moon);
                moon.surface = PlanetTerrainGenerator.GenerateSurface(moon);
                OreGenerator.Populate(moon);
                ResourceGenerator.GenerateResources(moon);

                moon.orbitRadius = moonR;
                moon.orbitSpeed = OrbitalMechanics.MoonAngularSpeed(body, moonR);
                moon.spinSpeed = OrbitalMechanics.Spin(moon, Random.Range(0.7f, 1.3f));
                moon.orbitPhase = Random.Range(0f, 360f);
                moon.orbitDirection = Random.value < 0.85f ? 1 : -1;
                moon.inclination = Random.Range(-15f, 15f);
                moon.eccentricity = Random.Range(0f, 0.2f);
                moon.distanceFromStar = body.distanceFromStar; // shares the planet's solar distance
                moon.parentBody = body;
                ApplyHabitability(moon);
                POIGenerator.Populate(moon);

                body.moons.Add(moon);
                moonR += Random.Range(1.6f, 2.6f);
            }

            system.Add(body);

            // Step outward, with a little jitter so systems don't look uniform.
            currentRadius += 5f + body.surfaceSize * 0.25f + Random.Range(0f, 3.5f);
        }

        // Lean towards a living world: make sure at least one planet sits in the habitable zone.
        EnsureHabitableWorld(system);

        return system;
    }

    // If no life-friendly planet already sits in the (default-species) habitable zone, convert the
    // nearest planet into one and place it inside the zone.
    void EnsureHabitableWorld(List<CelestialBody> system)
    {
        if (system.Count == 0 || currentStar == null || !currentStar.hasHabitableZone) return;
        if (!Habitability.GetZone(currentStar, SpeciesManager.Current, out float inner, out float outer)) return;

        foreach (var b in system)
            if (b.distanceFromStar >= inner && b.distanceFromStar <= outer &&
                (b.type == CelestialBodyType.RockyPlanet || b.type == CelestialBodyType.OceanPlanet))
                return; // already have a habitable-zone world

        float center = (inner + outer) * 0.5f;
        CelestialBody best = null; float bestD = float.MaxValue;
        foreach (var b in system)
        {
            float d = Mathf.Abs(b.distanceFromStar - center);
            if (d < bestD) { bestD = d; best = b; }
        }
        if (best == null) return;

        best.distanceFromStar = Random.Range(inner, outer);
        best.orbitRadius = best.distanceFromStar;
        best.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(currentStar, best.orbitRadius);

        bool cool = currentStarType == StarType.M || currentStarType == StarType.K;
        best.type = cool ? CelestialBodyType.OceanPlanet : CelestialBodyType.RockyPlanet;
        SeedTerrain(best);
        best.surface = PlanetTerrainGenerator.GenerateSurface(best);
        OreGenerator.Populate(best);
        best.resources = new ResourceDeposit();
        ResourceGenerator.GenerateResources(best);

        ApplyHabitability(best);
        POIGenerator.Populate(best);

        foreach (var m in best.moons) { m.distanceFromStar = best.distanceFromStar; ApplyHabitability(m); }
    }

    CelestialBody MakeBody(CelestialBodyType type)
    {
        CelestialBody body = new(type) { id = _idCounter++ };
        body.surfaceSize = RollSurfaceSize(type, currentStarType);
        SeedTerrain(body);
        body.surface = PlanetTerrainGenerator.GenerateSurface(body);
        OreGenerator.Populate(body);
        ResourceGenerator.GenerateResources(body);
        return body;
    }

    // Stable terrain identity — must be set before generating any surface so both the low-res grid
    // and the high-res detailed map sample the same continents.
    static void SeedTerrain(CelestialBody body)
    {
        body.terrainSeed = Random.Range(0f, 10000f);
        body.continentFrequency = Mathf.Clamp(body.surfaceSize * 0.32f, 2.5f, 8f);
    }

    void ApplyHabitability(CelestialBody body)
    {
        var species = SpeciesManager.Current;
        body.isHabitable = Habitability.InZone(currentStar, species, body.distanceFromStar);
        body.habitability = Habitability.Rate(currentStar, species, body.type, body.distanceFromStar);
    }

    int RollMoonCount(CelestialBodyType type)
    {
        switch (type)
        {
            case CelestialBodyType.GasGiant: return Random.Range(0, 5);
            case CelestialBodyType.RockyPlanet:
            case CelestialBodyType.VolcanicPlanet:
            case CelestialBodyType.IcePlanet: return Random.Range(0, 3);
            case CelestialBodyType.BarrenPlanet:
            case CelestialBodyType.OceanPlanet: return Random.Range(0, 2);
            default: return 0;
        }
    }

    CelestialBodyType RollBodyByDistance(float d, StarType star)
    {
        // Cooler stars push the "warm" band inward, so ocean/rocky worlds can sit closer.
        bool coolStar = (star == StarType.M || star == StarType.K);

        if (d < 0.12f)
            return Random.value < 0.7f ? CelestialBodyType.VolcanicPlanet : CelestialBodyType.BarrenPlanet;

        if (d < 0.30f)
            return Random.value < 0.5f ? CelestialBodyType.RockyPlanet : CelestialBodyType.VolcanicPlanet;

        if (d < 0.55f)
        {
            float r = Random.value;
            if (r < 0.4f) return CelestialBodyType.RockyPlanet;
            if (r < 0.7f) return coolStar ? CelestialBodyType.RockyPlanet : CelestialBodyType.OceanPlanet;
            return CelestialBodyType.BarrenPlanet;
        }

        if (d < 0.75f)
            return Random.value < 0.6f ? CelestialBodyType.IcePlanet : CelestialBodyType.RockyPlanet;

        // Outer system
        float o = Random.value;
        if (o < 0.55f) return CelestialBodyType.GasGiant;
        if (o < 0.8f)  return CelestialBodyType.IcePlanet;
        if (o < 0.92f) return CelestialBodyType.BarrenPlanet;
        return CelestialBodyType.Asteroid;
    }

    // Rolls the centre of the system: almost always a single sun, occasionally binary/ternary,
    // very rarely a black hole.
    void RollStarSystem()
    {
        stars = new List<StarData>();

        if (Random.value < 0.02f)   // very rare black hole
        {
            isBlackHole = true;
            stars.Add(StarDatabase.BlackHole());
            currentStar = stars[0];
            currentStarType = currentStar.type;
            return;
        }

        isBlackHole = false;
        float c = Random.value;
        int count = c < 0.04f ? 3 : (c < 0.16f ? 2 : 1);   // ~4% ternary, ~12% binary, ~84% single
        for (int i = 0; i < count; i++) stars.Add(StarDatabase.Get(RollStarType()));

        currentStar = StarDatabase.Combine(stars);
        currentStarType = currentStar.type;
    }

    StarType RollStarType()
    {
        float roll = Random.value;
        if (roll < 0.45f) return StarType.M;
        if (roll < 0.65f) return StarType.K;
        if (roll < 0.80f) return StarType.G;
        if (roll < 0.90f) return StarType.F;
        if (roll < 0.96f) return StarType.A;
        if (roll < 0.99f) return StarType.B;
        return StarType.O;
    }

    int RollSurfaceSize(CelestialBodyType type, StarType starType)
    {
        switch (type)
        {
            case CelestialBodyType.GasGiant:       return Random.Range(16, 24);
            case CelestialBodyType.IcePlanet:      return Random.Range(9, 16);
            case CelestialBodyType.OceanPlanet:    return Random.Range(10, 18);
            case CelestialBodyType.RockyPlanet:    return Random.Range(8, 15);
            case CelestialBodyType.VolcanicPlanet: return Random.Range(9, 14);
            case CelestialBodyType.BarrenPlanet:   return Random.Range(7, 13);
            case CelestialBodyType.Moon:           return Random.Range(4, 9);
            case CelestialBodyType.Asteroid:       return Random.Range(4, 7);
            default:                               return 10;
        }
    }
}
