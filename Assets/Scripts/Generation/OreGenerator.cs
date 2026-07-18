using System.Collections.Generic;
using UnityEngine;

// Seeds mineral-rich tiles across a planet surface. Which ores can appear depends on the planet
// type; rarer (higher-tier) ores are gated hard so exotic minerals stay special.
public static class OreGenerator
{
    // How likely a given biome is to host an ore at all.
    static float TerrainAffinity(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.CrystalField:  return 0.42f;
            case TerrainType.MetallicCrust: return 0.36f;
            case TerrainType.Volcano:       return 0.28f;
            case TerrainType.MagmaField:    return 0.20f;
            case TerrainType.Mountains:     return 0.18f;
            case TerrainType.Canyon:        return 0.16f;
            case TerrainType.ObsidianFlat:  return 0.15f;
            case TerrainType.LavaRock:      return 0.15f;
            case TerrainType.Crater:        return 0.14f;
            case TerrainType.Badlands:      return 0.12f;
            case TerrainType.Highlands:     return 0.10f;
            case TerrainType.Glacier:       return 0.09f;
            case TerrainType.GasClouds:     return 0.06f;  // Helium-3 skimming
            case TerrainType.Storm:         return 0.05f;
            case TerrainType.Ice:           return 0.05f;
            case TerrainType.FrozenSea:     return 0.04f;
            case TerrainType.Ocean:
            case TerrainType.Reef:
            case TerrainType.Lake:
            case TerrainType.River:         return 0.0f;   // underwater: skip
            default:                        return 0.02f;
        }
    }

    // Per-planet ore pools.
    static List<OreType> PoolFor(CelestialBodyType type)
    {
        switch (type)
        {
            case CelestialBodyType.VolcanicPlanet:
                return new List<OreType> { OreType.Ferralite, OreType.Titanex, OreType.Pyronium, OreType.Uranite, OreType.Adamantine, OreType.Aurelium };
            case CelestialBodyType.IcePlanet:
                return new List<OreType> { OreType.Cryonite, OreType.Argenite, OreType.Luminite, OreType.Quantite, OreType.Ferralite };
            case CelestialBodyType.OceanPlanet:
                return new List<OreType> { OreType.Cuprion, OreType.Ferralite, OreType.Aurelium };
            case CelestialBodyType.BarrenPlanet:
                return new List<OreType> { OreType.Ferralite, OreType.Titanex, OreType.Platinode, OreType.Adamantine, OreType.Argenite };
            case CelestialBodyType.GasGiant:
                return new List<OreType> { OreType.Helium3 };
            case CelestialBodyType.Moon:
            case CelestialBodyType.Asteroid:
                return new List<OreType> { OreType.Ferralite, OreType.Titanex, OreType.Platinode, OreType.Neutronium, OreType.Xenocryst, OreType.Adamantine };
            case CelestialBodyType.RockyPlanet:
            default:
                return new List<OreType> { OreType.Ferralite, OreType.Cuprion, OreType.Titanex, OreType.Aurelium, OreType.Argenite, OreType.Uranite };
        }
    }

    // Acceptance chance once an ore is picked, by tier — keeps exotics rare.
    static float TierAcceptance(int tier)
    {
        switch (tier)
        {
            case 1: return 1.00f;
            case 2: return 0.75f;
            case 3: return 0.45f;
            case 4: return 0.25f;
            default: return 0.10f;
        }
    }

    // A tectonically active world folds richer deposits up from depth — real fault lines would make this
    // a per-tile "near the boundary" bonus (see the request's "rare or high quality mineral deposits are
    // more common there"), but no fault-line geometry exists yet (Advanced Planet Generation slice), so
    // this is an honest whole-world proxy: more likely to have ore at all, and more likely for it to be
    // a higher tier, everywhere on the planet rather than concentrated at boundaries specifically.
    const float TectonicAffinityMul = 1.35f, TectonicTierMul = 1.25f;

    public static void Populate(CelestialBody body)
    {
        if (body.surface == null) return;
        var pool = PoolFor(body.type);
        if (pool.Count == 0) return;

        float affinityMul = body.hasTectonics ? TectonicAffinityMul : 1f;
        float tierMul = body.hasTectonics ? TectonicTierMul : 1f;

        var surface = body.surface;
        for (int x = 0; x < surface.width; x++)
        {
            for (int y = 0; y < surface.height; y++)
            {
                var tile = surface.tiles[x, y];
                if (tile == null) continue;

                if (Random.value >= Mathf.Min(1f, TerrainAffinity(tile.type) * affinityMul)) continue;

                // Weighted pick: common ores (low tier) more likely to be selected.
                OreType picked = WeightedPick(pool);
                var info = OreDatabase.Get(picked);

                if (Random.value > Mathf.Min(1f, TierAcceptance(info.tier) * tierMul)) continue; // gated out -> stays plain

                tile.ore = picked;
                tile.oreRichness = Mathf.Clamp01(Random.Range(0.3f, 1f));
            }
        }
    }

    static OreType WeightedPick(List<OreType> pool)
    {
        float total = 0f;
        foreach (var o in pool) total += Mathf.Max(1, 7 - OreDatabase.Get(o).tier);
        float r = Random.value * total;
        foreach (var o in pool)
        {
            r -= Mathf.Max(1, 7 - OreDatabase.Get(o).tier);
            if (r <= 0f) return o;
        }
        return pool[0];
    }

    // Distinct ores present on a body (used for save summaries / codex hints).
    public static HashSet<OreType> OresOnBody(CelestialBody body)
    {
        var set = new HashSet<OreType>();
        if (body.surface == null) return set;
        for (int x = 0; x < body.surface.width; x++)
            for (int y = 0; y < body.surface.height; y++)
            {
                var t = body.surface.tiles[x, y];
                if (t != null && t.HasOre) set.Add(t.ore);
            }
        return set;
    }
}
