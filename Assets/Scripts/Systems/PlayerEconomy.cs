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

    // How much of each resource the empire can hold at once. Storage Depots (and a world's capitol)
    // raise this; income above the ceiling is thrown away. That's what makes warehousing a real
    // decision rather than decoration — you cannot bank for a mega-station without somewhere to put it.
    //
    // Memoized per frame: it walks every owned world, and the economy tick asks for it constantly.
    public const float BaseCapacity = 3000f;
    static float capCache = -1f; static int capFrame = -1;

    public static float Capacity(ResourceType t)
    {
        if (capFrame == Time.frameCount && capCache >= 0f) return capCache;
        float cap = BaseCapacity;
        if (SystemContext.Galaxy != null)
            foreach (var b in SystemContext.AllBodies())
            {
                if (b == null || b.owner != FactionManager.Player || b.placedBuildings == null) continue;
                // Scales with tech level, like every other surface output. A depot's siting is
                // irrelevant (storageCapacity has no index), so this is purely its tier.
                foreach (var p in b.placedBuildings) cap += p.Info.storageCapacity * p.LevelMult;
            }
        capCache = cap; capFrame = Time.frameCount;
        return cap;
    }

    public static bool AtCapacity(ResourceType t) => Get(t) >= Capacity(t) - 0.01f;

    public static void Add(ResourceType t, float amount)
    {
        // Only INCOME is capped. A refund or a negative adjustment must always land in full, or
        // cancelling a build at a full stockpile would quietly destroy the refund.
        float v = Get(t) + amount;
        if (amount > 0f) v = Mathf.Min(v, Capacity(t));
        stock[t] = v;
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

    // Shows the ceiling alongside the stock, and warns when a resource is capped out — otherwise
    // "my income stopped counting" would be invisible.
    public static string Summary()
    {
        float cap = Capacity(ResourceType.Metal);
        return $"{One("Metal", ResourceType.Metal, cap)}   {One("Energy", ResourceType.Energy, cap)}   {One("Water", ResourceType.Water, cap)}";
    }

    static string One(string label, ResourceType t, float cap)
    {
        float v = Get(t);
        bool full = v >= cap - 0.01f;
        return full
            ? $"<color=#FFBF4D>{label} {v:F0}/{cap:F0} FULL</color>"
            : $"{label} {v:F0}<size=10><color=#7C8CA0>/{cap:F0}</color></size>";
    }

    // Save/load.
    public static void Import(float metal, float energy, float water)
    {
        stock[ResourceType.Metal] = metal; stock[ResourceType.Energy] = energy; stock[ResourceType.Water] = water;
        OnChanged?.Invoke();
    }
}
