using System.Collections.Generic;
using UnityEngine;

public class SolarSystemGenerator : MonoBehaviour
{
    public int minBodies = 0;
    public int maxBodies = 5;
    public StarType currentStarType;

    public List<CelestialBody> GenerateSystem()
    {
        List<CelestialBody> system = new();

        currentStarType = RollStarType(); //Determines type of star

        int bodyCount = Random.Range(minBodies, maxBodies + 1);

        for (int i = 0; i < bodyCount; i++)
        {
            float orbitPercent = (float)i / (bodyCount - 1);
            CelestialBodyType type = RollBodyByDistance(orbitPercent, currentStarType);

            CelestialBody body = new(type);
            body.surfaceSize = RollSurfaceSize(type, currentStarType); // Fixed
            body.surface = PlanetTerrainGenerator.GenerateSurface(body);
            ResourceGenerator.GenerateResources(body);

            int moonCount = 0;

            if (type == CelestialBodyType.GasGiant)
                moonCount = Random.Range(0, 4);

            else if (
                type == CelestialBodyType.RockyPlanet ||
                type == CelestialBodyType.VolcanicPlanet ||
                type == CelestialBodyType.IcePlanet
            )
                moonCount = Random.Range(0, 2);

            else if (type == CelestialBodyType.BarrenPlanet || type == CelestialBodyType.OceanPlanet)
                moonCount = Random.Range(0, 1);

            for (int m = 0; m < moonCount; m++)
            {
                CelestialBody moon = new(CelestialBodyType.Moon);
                moon.surfaceSize = Random.Range(5, 9); // Smaller
                moon.surface = PlanetTerrainGenerator.GenerateSurface(moon);
                body.moons.Add(moon);
            }

            system.Add(body);
        }

        return system;
    }

    CelestialBodyType RollBodyByDistance(float d, StarType star)
    {
        if (d < 0.15f)
            return CelestialBodyType.VolcanicPlanet;

        if (d < 0.30f)
            return CelestialBodyType.RockyPlanet;

        if (d < 0.55f)
        {
            return star == StarType.M
                ? CelestialBodyType.OceanPlanet
                : CelestialBodyType.RockyPlanet;
        }

        if (d < 0.75f)
            return CelestialBodyType.IcePlanet;

        if (Random.value < 0.7f)
            return CelestialBodyType.GasGiant;

        return CelestialBodyType.BarrenPlanet;
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

    // In RollSurfaceSize
    int RollSurfaceSize(CelestialBodyType type, StarType starType)
    {
        switch (type)
        {
            case CelestialBodyType.GasGiant:
                return Random.Range(16, 24); // Large gas giants
            case CelestialBodyType.IcePlanet:
                return Random.Range(9, 16);
            case CelestialBodyType.OceanPlanet:
                return Random.Range(10, 18); // Larger water worlds
            case CelestialBodyType.RockyPlanet:
                return Random.Range(8, 15);
            case CelestialBodyType.VolcanicPlanet:
                return Random.Range(9, 14);
            case CelestialBodyType.BarrenPlanet:
                return Random.Range(7, 13);
            case CelestialBodyType.Moon:
                return Random.Range(4, 9);
            default:
                return 10;
        }
    }

    // New method for variety
    public float GetOrbitSpeed(CelestialBodyType type, float radius)
    {
        float baseSpeed = 30f / radius; // Kepler-like (farther = slower)
        if (type == CelestialBodyType.GasGiant) baseSpeed *= 0.7f;
        if (type == CelestialBodyType.Moon) baseSpeed *= 3f; // Faster relative to parent
        return baseSpeed;
    }


    CelestialBodyType RollBodyType()
    {
        float roll = Random.value;

        if (roll < 0.18f) return CelestialBodyType.GasGiant;
        if (roll < 0.30f) return CelestialBodyType.RockyPlanet;
        if (roll < 0.45f) return CelestialBodyType.IcePlanet;
        if (roll < 0.55f) return CelestialBodyType.OceanPlanet;
        if (roll < 0.70f) return CelestialBodyType.VolcanicPlanet;
        if (roll < 0.85f) return CelestialBodyType.BarrenPlanet;
        return CelestialBodyType.Asteroid;
    }

}