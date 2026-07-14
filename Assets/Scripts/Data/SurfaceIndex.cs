using UnityEngine;

// The map overlays you survey a world for. Each is a 0..1 score per surface tile that tells you where
// a given kind of building actually wants to go.
public enum SurfaceIndexKind { None, Mineral, Heat, Fertile, Weather }

// Per-tile survey indexes.
//
// These are DERIVED, never stored: a tile's mineral wealth, heat, fertility and weather all fall out of
// its biome, its ore, and a stable hash of its position and the world's terrain seed. That means they
// cost nothing to save, they survive a reload untouched, and a world re-rolled from the same seed reads
// the same way — the same guarantee the terrain itself already makes.
//
// Gating: a basic survey reveals the Mineral index (you can see the seams from orbit). Heat, Fertility
// and Weather need a DEEP survey — a research ship actually working the world — which is what makes
// "come back with a science vessel" worth doing.
public static class SurfaceIndex
{
    // Stable per-tile noise in 0..1. Deterministic from the world's seed and the tile's position, so it
    // never changes and never needs saving.
    static float Hash(CelestialBody b, int x, int y, int salt)
    {
        unchecked
        {
            int h = (int)b.terrainSeed;
            h = h * 73856093 ^ x * 19349663 ^ y * 83492791 ^ salt * 2971215073;
            h &= 0x7fffffff;
            return (h % 10007) / 10007f;
        }
    }

    // Smooth blobs rather than per-tile static, so a rich area reads as a REGION you can fit a building
    // into rather than a spray of unrelated pixels. That matters: the whole point of the overlays is to
    // find a good patch to place a multi-tile footprint on.
    static float Blob(CelestialBody b, int x, int y, int salt, float scale)
    {
        float fx = (x + b.terrainSeed * 0.37f) * scale;
        float fy = (y + b.terrainSeed * 0.71f + salt * 13.7f) * scale;
        return Mathf.Clamp01(Mathf.PerlinNoise(fx, fy));
    }

    public static float Get(CelestialBody b, SurfaceIndexKind kind, int x, int y)
    {
        switch (kind)
        {
            case SurfaceIndexKind.Mineral: return Mineral(b, x, y);
            case SurfaceIndexKind.Heat: return Heat(b, x, y);
            case SurfaceIndexKind.Fertile: return Fertile(b, x, y);
            case SurfaceIndexKind.Weather: return Weather(b, x, y);
            default: return 0f;
        }
    }

    // ---- Mineral: where a mine pays ----
    // Driven by the rock itself, and hugely by an actual ore deposit sitting on the tile.
    public static float Mineral(CelestialBody b, int x, int y)
    {
        var t = TileAt(b, x, y);
        if (t == null) return 0f;
        float v = BiomeMineral(t.type);
        if (t.HasOre) v = Mathf.Max(v, 0.55f + t.oreRichness * 0.45f);   // a real seam beats any biome
        v += (Blob(b, x, y, 1, 0.22f) - 0.5f) * 0.45f;
        return Mathf.Clamp01(v);
    }

    static float BiomeMineral(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.MetallicCrust: return 0.95f;
            case TerrainType.CrystalField: return 0.9f;
            case TerrainType.Mountains: return 0.8f;
            case TerrainType.Highlands: return 0.65f;
            case TerrainType.Canyon: return 0.6f;
            case TerrainType.Crater: return 0.6f;
            case TerrainType.Badlands: return 0.55f;
            case TerrainType.LavaRock:
            case TerrainType.ObsidianFlat: return 0.55f;
            case TerrainType.Hills: return 0.5f;
            case TerrainType.Barren:
            case TerrainType.Wasteland: return 0.4f;
            case TerrainType.Ocean:
            case TerrainType.Lake:
            case TerrainType.River:
            case TerrainType.FrozenSea:
            case TerrainType.Reef: return 0.05f;
            default: return 0.25f;
        }
    }

    // ---- Heat: where a geothermal plant pays ----
    // Volcanoes and geyser fields are the jackpot — which is exactly why they're worth surveying for.
    public static float Heat(CelestialBody b, int x, int y)
    {
        var t = TileAt(b, x, y);
        if (t == null) return 0f;
        float v = BiomeHeat(t.type);
        v += (Blob(b, x, y, 2, 0.18f) - 0.5f) * 0.4f;
        return Mathf.Clamp01(v);
    }

    static float BiomeHeat(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Volcano: return 1.0f;
            case TerrainType.MagmaField: return 0.95f;
            case TerrainType.GeyserField: return 0.9f;
            case TerrainType.LavaRock: return 0.75f;
            case TerrainType.AshWaste: return 0.6f;
            case TerrainType.ObsidianFlat: return 0.55f;
            case TerrainType.CrackedGround: return 0.5f;
            case TerrainType.Desert:
            case TerrainType.Dunes: return 0.35f;
            case TerrainType.SaltFlat: return 0.3f;
            case TerrainType.Ice:
            case TerrainType.Snow:
            case TerrainType.Glacier:
            case TerrainType.FrozenSea: return 0.02f;
            case TerrainType.Tundra: return 0.08f;
            default: return 0.2f;
        }
    }

    // ---- Fertile: where farmland pays ----
    public static float Fertile(CelestialBody b, int x, int y)
    {
        var t = TileAt(b, x, y);
        if (t == null) return 0f;
        float v = BiomeFertile(t.type);
        v += (Blob(b, x, y, 3, 0.25f) - 0.5f) * 0.4f;
        return Mathf.Clamp01(v);
    }

    static float BiomeFertile(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Grassland: return 0.95f;
            case TerrainType.Plains: return 0.85f;
            case TerrainType.Jungle: return 0.8f;
            case TerrainType.Forest: return 0.75f;
            case TerrainType.Swamp: return 0.7f;
            case TerrainType.Taiga: return 0.5f;
            case TerrainType.Steppe: return 0.45f;
            case TerrainType.Savanna: return 0.45f;
            case TerrainType.Beach: return 0.35f;
            case TerrainType.Hills: return 0.4f;
            case TerrainType.Highlands: return 0.25f;
            case TerrainType.Tundra: return 0.15f;
            case TerrainType.Desert:
            case TerrainType.Dunes:
            case TerrainType.SaltFlat:
            case TerrainType.Badlands:
            case TerrainType.Wasteland: return 0.05f;
            case TerrainType.Ocean:
            case TerrainType.Lake:
            case TerrainType.River: return 0.0f;
            default: return 0.1f;
        }
    }

    // ---- Weather: sun and wind, for solar arrays and turbines ----
    // Dry open biomes are sunny; exposed high ground and coasts are windy. One index, because in
    // practice you're asking the same question: "is this a good spot for ambient power?"
    public static float Weather(CelestialBody b, int x, int y)
    {
        var t = TileAt(b, x, y);
        if (t == null) return 0f;
        float v = Mathf.Max(Sun(t.type), Wind(t.type));
        v += (Blob(b, x, y, 4, 0.2f) - 0.5f) * 0.35f;
        return Mathf.Clamp01(v);
    }

    // Cloudless, dry, open ground — the savanna/desert bias asked for.
    public static float Sun(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Desert:
            case TerrainType.Dunes:
            case TerrainType.SaltFlat: return 0.95f;
            case TerrainType.Savanna: return 0.8f;
            case TerrainType.Badlands:
            case TerrainType.Wasteland: return 0.7f;
            case TerrainType.Steppe: return 0.6f;
            case TerrainType.Barren:
            case TerrainType.Crater: return 0.6f;
            case TerrainType.Plains: return 0.5f;
            case TerrainType.Glacier:
            case TerrainType.Snow: return 0.45f;   // bright, but a low sun
            case TerrainType.Jungle:
            case TerrainType.Swamp:
            case TerrainType.Storm: return 0.1f;   // permanent cloud
            default: return 0.3f;
        }
    }

    // Exposure: high ground and open water/coast get the wind.
    public static float Wind(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Storm: return 1.0f;
            case TerrainType.Highlands:
            case TerrainType.Mountains: return 0.85f;
            case TerrainType.Hills: return 0.6f;
            case TerrainType.Beach: return 0.65f;
            case TerrainType.Ocean:
            case TerrainType.FrozenSea: return 0.7f;
            case TerrainType.Steppe:
            case TerrainType.Tundra: return 0.55f;
            case TerrainType.Dunes: return 0.5f;
            case TerrainType.Forest:
            case TerrainType.Jungle: return 0.15f;   // sheltered
            default: return 0.3f;
        }
    }

    static TerrainTile TileAt(CelestialBody b, int x, int y)
    {
        if (b == null || b.surface == null || b.surface.tiles == null) return null;
        if (x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) return null;
        return b.surface.tiles[x, y];
    }

    // ---- Presentation ----
    public static string Name(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Mineral Index";
            case SurfaceIndexKind.Heat: return "Heat Index";
            case SurfaceIndexKind.Fertile: return "Fertile Index";
            case SurfaceIndexKind.Weather: return "Weather Index";
            default: return "None";
        }
    }

    public static string Describe(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Where mines yield the most per tick. Ore seams and mountain rock score highest.";
            case SurfaceIndexKind.Heat: return "Where geothermal plants run hottest. Volcanoes and geyser fields are the prize.";
            case SurfaceIndexKind.Fertile: return "Where farmland feeds the most people. Grassland and plains score highest.";
            case SurfaceIndexKind.Weather: return "Sun and wind for ambient power. Dry open ground is sunny; ridges and coasts are windy.";
            default: return "";
        }
    }

    // The colour ramps requested: brown for minerals, orange→red for heat, dark→vibrant green for
    // fertility, and a pale sky-blue→white for weather. Alpha rises with the score so weak tiles fade
    // out and the good patches are what your eye lands on.
    public static Color Ramp(SurfaceIndexKind k, float t)
    {
        t = Mathf.Clamp01(t);
        Color c;
        switch (k)
        {
            case SurfaceIndexKind.Mineral:
                c = Color.Lerp(new Color(0.20f, 0.13f, 0.07f), new Color(0.72f, 0.48f, 0.22f), t); break;
            case SurfaceIndexKind.Heat:
                c = Color.Lerp(new Color(0.85f, 0.45f, 0.10f), new Color(1.00f, 0.10f, 0.05f), t); break;
            case SurfaceIndexKind.Fertile:
                c = Color.Lerp(new Color(0.05f, 0.22f, 0.08f), new Color(0.30f, 1.00f, 0.25f), t); break;
            case SurfaceIndexKind.Weather:
                c = Color.Lerp(new Color(0.25f, 0.45f, 0.65f), new Color(0.85f, 0.97f, 1.00f), t); break;
            default:
                return new Color(0, 0, 0, 0);
        }
        c.a = Mathf.Lerp(0.12f, 0.88f, t);
        return c;
    }

    // Can this index be read yet? Minerals show from orbit; the rest need a deep survey by a research
    // ship, which is the reason to bring one back to a world you've already mapped.
    public static bool Unlocked(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return false;
        if (GameMode.DevMode) return true;
        if (!b.Surveyed) return false;
        return k == SurfaceIndexKind.Mineral || b.deepSurveyed;
    }

    public static string LockReason(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return "no world selected";
        if (!b.Surveyed) return "survey this world first";
        if (k != SurfaceIndexKind.Mineral && !b.deepSurveyed)
            return "needs a deep survey — send a research ship to study this world";
        return null;
    }
}
