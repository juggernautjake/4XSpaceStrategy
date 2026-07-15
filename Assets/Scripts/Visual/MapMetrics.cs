using UnityEngine;

// ============================================================================================
// Single source of truth for a body's surface grid and how it renders.
//
// THE ONE-TO-ONE RULE
// There is exactly ONE grid. The terrain is rendered one texel per cell, and a building's footprint is
// measured in those same cells — so a 1x1 structure covers precisely one terrain pixel.
//
// This used to be two grids that disagreed. PlanetTerrainGenerator built the surface at
// `surfaceSize * 2` cells, while SurfaceTextureRenderer.Build drew the terrain at `surfaceSize * 2 * 6`
// texels — SIX TIMES finer. Both looked reasonable in isolation, and the comment here even claimed to
// match the generator, but the consequence was that the smallest thing you could build was six terrain
// pixels wide and six tall: thirty-six times the area of the ground it was drawn on. Every structure
// looked like a city block dropped on a continent.
//
// Making the two agree is the whole fix, and it has to be the GRID that moves up to the render's
// resolution rather than the render moving down to the grid's. Coarsening the terrain to match the
// buildings would match them by throwing away every coastline and feature the detail render exists to
// resolve — the map would be the same map, and a much worse one.
// ============================================================================================
public static class MapMetrics
{
    // How many cells the grid gets per unit of surfaceSize, over and above the old 2-per-size. This is
    // the number that used to live only inside SurfaceTextureRenderer.Build as a bare `* 6`.
    public const int Subdiv = 6;

    // Surface grid dimensions. PlanetTerrainGenerator builds exactly this, and every map renders exactly
    // this, so a cell and a texel are the same thing everywhere.
    //
    // The clamp bounds the cost: a grid is width*height cells and every one is a TerrainTile, so an
    // unbounded 6x on a large world would be a real memory and generation bill for detail nobody can
    // see. 384x192 is the ceiling.
    public static int SurfW(int surfaceSize) => Mathf.Clamp(Mathf.Max(4, surfaceSize) * 2 * Subdiv, 96, 384);
    public static int SurfH(int surfaceSize) => Mathf.Max(48, SurfW(surfaceSize) / 2);

    // ---- Pixels per cell ----
    // A cell is now Subdiv times smaller than it used to be, so these are Subdiv times smaller to match.
    // The maps therefore come out exactly the SAME SIZE on screen as before — a 40x20 world drawn at
    // 42px per cell and a 240x120 world drawn at 7px per cell are both 1680px wide. Same map, same
    // dimensions, six times the resolution.
    public const float MiniTilePx = 24f / Subdiv;    // 4px
    public const float DetailFactor = 1.75f;         // detailed cell = 7px

    public static float MiniTile(int surfaceSize) => MiniTilePx;                  // same for all bodies
    public static float DetailTile(int surfaceSize) => MiniTilePx * DetailFactor; // same for all bodies
}
