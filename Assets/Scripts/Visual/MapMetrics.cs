using UnityEngine;

// Single source of truth for how big a body's surface maps render. Both the low-res mini map and the
// high-res detailed map derive their pixels-per-tile from MiniTile(), and the detailed map is exactly
// DetailFactor times the mini map — so for every pixel on the mini map there are a consistent number
// of pixels on the detailed map, for every body, at every size.
public static class MapMetrics
{
    // Pixels-per-tile are CONSTANT for every body — only the number of tiles (and thus the overall
    // map size) changes with the world's size. So a big world just shows a bigger map, never bigger
    // tiles. The detailed map uses a fixed larger tile, keeping a constant mini:detailed ratio too.
    public const float MiniTilePx = 12f;
    public const float DetailFactor = 2f;

    // Surface grid dimensions (matches PlanetTerrainGenerator: width = 2*size, height = size).
    public static int SurfW(int surfaceSize) => Mathf.Max(4, surfaceSize * 2);
    public static int SurfH(int surfaceSize) => Mathf.Max(2, surfaceSize);

    public static float MiniTile(int surfaceSize) => MiniTilePx;                 // same for all bodies
    public static float DetailTile(int surfaceSize) => MiniTilePx * DetailFactor; // same for all bodies
}
