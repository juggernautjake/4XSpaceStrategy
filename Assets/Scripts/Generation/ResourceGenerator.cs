using System.Collections.Generic;
using UnityEngine;

// Bulk surface resources (Metal / Energy / Water) per body. Now covers every body type so
// nothing generates empty. Amounts scale with the difficulty resource multiplier.
public static class ResourceGenerator
{
    public static void GenerateResources(CelestialBody body)
    {
        switch (body.type)
        {
            case CelestialBodyType.GasGiant:
                body.resources.Add(ResourceType.Energy, Random.Range(30, 80));
                break;

            case CelestialBodyType.RockyPlanet:
                body.resources.Add(ResourceType.Metal, Random.Range(20, 60));
                body.resources.Add(ResourceType.Water, Random.Range(5, 30));
                break;

            case CelestialBodyType.IcePlanet:
                body.resources.Add(ResourceType.Water, Random.Range(40, 90));
                body.resources.Add(ResourceType.Metal, Random.Range(5, 20));
                break;

            case CelestialBodyType.VolcanicPlanet:
                body.resources.Add(ResourceType.Metal, Random.Range(40, 100));
                body.resources.Add(ResourceType.Energy, Random.Range(10, 40));
                break;

            case CelestialBodyType.OceanPlanet:
                body.resources.Add(ResourceType.Water, Random.Range(60, 120));
                body.resources.Add(ResourceType.Metal, Random.Range(5, 25));
                break;

            case CelestialBodyType.BarrenPlanet:
                body.resources.Add(ResourceType.Metal, Random.Range(15, 45));
                break;

            case CelestialBodyType.Asteroid:
                body.resources.Add(ResourceType.Metal, Random.Range(5, 25));
                break;

            case CelestialBodyType.Moon:
                body.resources.Add(ResourceType.Metal, Random.Range(5, 20));
                body.resources.Add(ResourceType.Water, Random.Range(0, 15));
                break;
        }

        float mult = GameConfig.ResourceMult;
        if (mult != 1f)
        {
            var keys = new List<ResourceType>(body.resources.resources.Keys);
            foreach (var k in keys) body.resources.resources[k] *= mult;
        }
    }
}
