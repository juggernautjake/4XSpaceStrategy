using UnityEngine;

// SINGLE SOURCE OF TRUTH for terrain colours. Every TerrainType must have a case here.
// PlanetGridVisualizer and SurfaceTileUI both delegate here so colours can never disagree
// (that mismatch was the old "magenta tile" bug: MagmaField was missing from this map).
public static class TerrainColorMap
{
    public static Color Get(TerrainType type)
    {
        switch (type)
        {
            // Core
            case TerrainType.Plains:        return new Color(0.45f, 0.72f, 0.36f);
            case TerrainType.Mountains:     return new Color(0.50f, 0.50f, 0.52f);
            case TerrainType.Forest:        return new Color(0.12f, 0.52f, 0.20f);
            case TerrainType.Ice:           return new Color(0.74f, 0.90f, 1.00f);
            case TerrainType.MagmaField:    return new Color(1.00f, 0.42f, 0.10f); // <- was missing -> magenta
            case TerrainType.Volcano:       return new Color(0.85f, 0.18f, 0.08f);
            case TerrainType.Desert:        return new Color(0.90f, 0.80f, 0.45f);
            case TerrainType.Ocean:         return new Color(0.16f, 0.36f, 0.72f);
            case TerrainType.Island:        return new Color(0.34f, 0.72f, 0.52f);
            case TerrainType.Crater:        return new Color(0.42f, 0.42f, 0.46f);
            case TerrainType.Barren:        return new Color(0.62f, 0.58f, 0.52f);

            // Temperate / life-bearing
            case TerrainType.Grassland:     return new Color(0.52f, 0.76f, 0.34f);
            case TerrainType.Jungle:        return new Color(0.09f, 0.44f, 0.16f);
            case TerrainType.Swamp:         return new Color(0.34f, 0.42f, 0.24f);
            case TerrainType.Savanna:       return new Color(0.76f, 0.72f, 0.34f);
            case TerrainType.Steppe:        return new Color(0.66f, 0.68f, 0.40f);
            case TerrainType.Tundra:        return new Color(0.62f, 0.66f, 0.62f);
            case TerrainType.Taiga:         return new Color(0.22f, 0.44f, 0.34f);
            case TerrainType.Hills:         return new Color(0.48f, 0.62f, 0.34f);
            case TerrainType.Highlands:     return new Color(0.56f, 0.56f, 0.44f);
            case TerrainType.Beach:         return new Color(0.90f, 0.86f, 0.62f);
            case TerrainType.Lake:          return new Color(0.24f, 0.48f, 0.78f);
            case TerrainType.River:         return new Color(0.30f, 0.54f, 0.82f);
            case TerrainType.Reef:          return new Color(0.30f, 0.66f, 0.72f);

            // Cold
            case TerrainType.Snow:          return new Color(0.92f, 0.95f, 0.98f);
            case TerrainType.Glacier:       return new Color(0.64f, 0.82f, 0.92f);
            case TerrainType.FrozenSea:     return new Color(0.58f, 0.74f, 0.86f);

            // Hot / dry
            case TerrainType.Dunes:         return new Color(0.86f, 0.74f, 0.42f);
            case TerrainType.SaltFlat:      return new Color(0.88f, 0.88f, 0.84f);
            case TerrainType.Canyon:        return new Color(0.66f, 0.40f, 0.26f);
            case TerrainType.Badlands:      return new Color(0.58f, 0.38f, 0.28f);
            case TerrainType.Wasteland:     return new Color(0.56f, 0.50f, 0.40f);

            // Volcanic / hostile
            case TerrainType.AshWaste:      return new Color(0.34f, 0.30f, 0.30f);
            case TerrainType.ObsidianFlat:  return new Color(0.16f, 0.14f, 0.18f);
            case TerrainType.LavaRock:      return new Color(0.30f, 0.20f, 0.18f);
            case TerrainType.GeyserField:   return new Color(0.72f, 0.78f, 0.72f);
            case TerrainType.CrackedGround: return new Color(0.46f, 0.34f, 0.28f);

            // Exotic / mineral
            case TerrainType.CrystalField:  return new Color(0.60f, 0.80f, 0.86f);
            case TerrainType.MetallicCrust: return new Color(0.52f, 0.54f, 0.60f);
            case TerrainType.GasClouds:     return new Color(0.80f, 0.72f, 0.52f);
            case TerrainType.Storm:         return new Color(0.60f, 0.56f, 0.66f);
        }

        return Color.magenta; // Should never happen now; a magenta tile means a missing case above.
    }

    // Short human-readable descriptor shown in tooltips / info panels.
    public static string Describe(TerrainType type)
    {
        switch (type)
        {
            case TerrainType.MagmaField:    return "Molten rock fields, extreme heat.";
            case TerrainType.Volcano:       return "Active volcano venting lava and ash.";
            case TerrainType.Ocean:         return "Deep liquid water.";
            case TerrainType.Ice:           return "Frozen surface of water or gases.";
            case TerrainType.Mountains:     return "High rocky peaks.";
            case TerrainType.Forest:        return "Dense vegetation cover.";
            case TerrainType.Jungle:        return "Hot, humid, thick growth.";
            case TerrainType.Desert:        return "Arid sand and rock.";
            case TerrainType.Crater:        return "Impact scar in the surface.";
            case TerrainType.CrystalField:  return "Exposed crystalline formations.";
            case TerrainType.MetallicCrust: return "Metal-rich surface plating.";
            case TerrainType.GeyserField:   return "Hydrothermal vents and steam.";
            case TerrainType.Barren:        return "Bare, lifeless ground.";
            default:                        return type.ToString();
        }
    }
}
