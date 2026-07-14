using System.Collections.Generic;
using UnityEngine;

public class SolarSystemGenerator : MonoBehaviour
{
    public int minBodies = 2;
    public int maxBodies = 6;

    public StarType currentStarType;
    public StarData currentStar;     // physical data for the rolled star (light/heat/HZ)

    static readonly string[] NamePrefixes =
    { "Kepler", "Cygnus", "Vega", "Tau Ceti", "Draconis", "Helios", "Orion", "Lyra",
      "Aquila", "Nyx", "Erebus", "Rhea", "Ymir", "Talos", "Zephyr", "Kestrel" };

    static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    int _idCounter;

    public List<CelestialBody> GenerateSystem()
    {
        _idCounter = 0;
        List<CelestialBody> system = new();

        currentStarType = RollStarType();
        currentStar = StarDatabase.Get(currentStarType);

        int bodyCount = Random.Range(minBodies, maxBodies + 1);
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

        return system;
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
