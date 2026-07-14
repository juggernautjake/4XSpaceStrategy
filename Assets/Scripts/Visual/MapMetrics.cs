using UnityEngine;

// Single source of truth for how big a body's surface maps render. Both the low-res mini map and the
// high-res detailed map derive their pixels-per-tile from MiniTile(), and the detailed map is exactly
// DetailFactor times the mini map — so for every pixel on the mini map there are a consistent number
// of pixels on the detailed map, for every body, at every size.
public static class MapMetrics
{
    public const float MiniTileMax = 26f;   // chunky tiles for small worlds
    public const float MiniTileMin = 7f;
    public const float MiniMaxW = 560f;      // mini map caps here (keeps huge worlds on-screen)
    public const float MiniMaxH = 320f;
    public const float DetailFactor = 2.2f;  // detailed map = this * mini map, always

    // Surface grid dimensions (matches PlanetTerrainGenerator: width = 2*size, height = size).
    public static int SurfW(int surfaceSize) => Mathf.Max(4, surfaceSize * 2);
    public static int SurfH(int surfaceSize) => Mathf.Max(2, surfaceSize);

    // Pixels per tile on the mini map for a body of this surface size.
    public static float MiniTile(int surfaceSize)
    {
        float t = Mathf.Min(MiniTileMax, MiniMaxW / SurfW(surfaceSize), MiniMaxH / SurfH(surfaceSize));
        return Mathf.Clamp(t, MiniTileMin, MiniTileMax);
    }

    public static float DetailTile(int surfaceSize) => MiniTile(surfaceSize) * DetailFactor;
}
