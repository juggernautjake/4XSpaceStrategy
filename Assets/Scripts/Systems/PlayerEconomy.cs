using System;
using System.Collections.Generic;
using UnityEngine;

// The player's resource stockpile (Metal / Energy / Water), used to build ships. Starting resources
// are set from the home planet type, the chosen species, and the difficulty level.
public static class PlayerEconomy
{
    static readonly Dictionary<ResourceType, float> stock = new Dictionary<ResourceType, float>();

    public static event Action OnChanged;

    public static float Get(ResourceType t) => stock.TryGetValue(t, out var v) ? v : 0f;

    public static void Add(ResourceType t, float amount)
    {
        stock[t] = Get(t) + amount;
        OnChanged?.Invoke();
    }

    public static bool CanAfford(int metal, int energy) => Get(ResourceType.Metal) >= metal && Get(ResourceType.Energy) >= energy;

    public static bool Spend(int metal, int energy)
    {
        if (!CanAfford(metal, energy)) return false;
        stock[ResourceType.Metal] = Get(ResourceType.Metal) - metal;
        stock[ResourceType.Energy] = Get(ResourceType.Energy) - energy;
        OnChanged?.Invoke();
        return true;
    }

    // Starting resources: base by home planet type, scaled by difficulty + a species flavour bonus.
    public static void NewGame(CelestialBody home, Species species)
    {
        stock.Clear();

        float metal = 220, energy = 160, water = 120;
        if (home != null)
        {
            switch (home.type)
            {
                case CelestialBodyType.OceanPlanet:   water += 220; energy += 60; break;
                case CelestialBodyType.VolcanicPlanet: metal += 220; energy += 160; water -= 40; break;
                case CelestialBodyType.IcePlanet:     water += 200; metal += 40; break;
                case CelestialBodyType.RockyPlanet:   metal += 120; energy += 60; water += 60; break;
                case CelestialBodyType.BarrenPlanet:  metal += 160; break;
                default:                              metal += 80; energy += 80; break;
            }
        }

        // Species flavour.
        if (species != null)
        {
            if (species.name == "Pyrothians") { metal += 120; energy += 100; }
            else if (species.name == "Aquarii") { water += 160; }
            else if (species.name == "Cryithn") { water += 120; metal += 40; }
            else if (species.name == "Sylvans") { energy += 100; water += 80; }
            else { metal += 60; energy += 60; }
        }

        float mult = GameConfig.ResourceMult * GameConfig.HomeResourceBonus;
        stock[ResourceType.Metal] = Mathf.Max(0f, metal * mult);
        stock[ResourceType.Energy] = Mathf.Max(0f, energy * mult);
        stock[ResourceType.Water] = Mathf.Max(0f, water * mult);
        OnChanged?.Invoke();
    }

    public static string Summary() =>
        $"Metal {Get(ResourceType.Metal):F0}   Energy {Get(ResourceType.Energy):F0}   Water {Get(ResourceType.Water):F0}";

    // Save/load.
    public static void Import(float metal, float energy, float water)
    {
        stock[ResourceType.Metal] = metal; stock[ResourceType.Energy] = energy; stock[ResourceType.Water] = water;
        OnChanged?.Invoke();
    }
}
