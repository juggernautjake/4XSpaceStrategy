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

    // ---- Surface grid dimensions, derived from MASS ----------------------------------------------
    //
    // Width is ~100 cells per unit of mass and height is always half of it, so every world is 2:1 and a
    // body's map size is a direct read of how big the body is:
    //
    //     mass 0.1 -> 10x5      mass 0.5 -> 50x25     mass 1 -> 100x50
    //     mass 2   -> 200x100   mass 4   -> 400x200
    //
    // WHY THIS REPLACED surfaceSize. The old form was `clamp(surfaceSize * 12, 96, 384)`, and the lower
    // clamp was doing almost all the work: surfaceSize is `round(mass * 3)`, so every body under mass ~2.7
    // produced a width under 96 and got flattened to the SAME 96x48 grid. A mass-0.3 moon, a mass-1 moon
    // and a mass-2 planet were all issued identical maps, and a moon was routinely the same size as the
    // planet it orbited.
    //
    // Deliberately NOT routed through surfaceSize any more. That value is load-bearing for a dozen
    // unrelated systems calibrated against its 3..32 range — atmosphere and tectonics gates, orbit
    // spacing, claim cost, population, spin, terraforming severity — so widening it to track mass would
    // have moved all of them at once. Mass is the honest input; surfaceSize keeps its old meaning.
    const float CellsPerMass = 100f;

    // Where the linear mapping stops and compresses.
    //
    // Below this the numbers are exact. Above it they taper, and that is a deliberate limit rather than
    // a taste call: the eager-generation path, the per-cell ore data in the save file, and several
    // O(width*height) passes (SurfaceBuildManager.FindSpot, CityGrowth.FindSettlementSpot, the Survey and
    // Power overlays) all have cliffs somewhere past this point. A mass-13 gas giant at a literal 1300x650
    // is 845,000 cells: ~40 MB of TerrainTile objects, ~25,000 saved ore cells, and a FindSpot that runs
    // into the billions of iterations on every load.
    //
    // 400 is chosen because it is where today's shipped ceiling already sits (384), so everything at or
    // below it is known-affordable. Raise KneeWidth/MaxWidth once those call sites are fixed — they are
    // the only thing holding the top end down.
    const float KneeWidth = 400f;
    const float MaxWidth = 640f;

    // A 10x5 world is legitimate (mass 0.1) and nothing below it is, so this is a floor against bad data
    // rather than a design choice.
    const int MinWidth = 10;

    /// Grid width for a body of this mass. Pure function of mass — every caller agrees by construction.
    public static int WidthForMass(float mass)
    {
        if (mass <= 0.0001f) mass = 0.1f;
        float raw = mass * CellsPerMass;

        // Soft knee: exact up to KneeWidth, then a square-root taper so bigger worlds still read as
        // bigger without the cell count running away.
        float w = raw <= KneeWidth
            ? raw
            : KneeWidth + Mathf.Sqrt(raw - KneeWidth) * 8f;

        return Mathf.Clamp(Mathf.RoundToInt(w), MinWidth, Mathf.RoundToInt(MaxWidth));
    }

    /// Grid dimensions for a body. Height is always half the width — every world is 2:1.
    ///
    /// The +/-6% variance is keyed on the body's ID, and the choice of key matters more than it looks.
    /// These dimensions are recomputed independently by the generator, the texture renderer and three UI
    /// readouts, so the key has to be both STABLE and IMMUTABLE.
    ///
    /// terrainSeed was the obvious candidate and is the wrong one: the Dev sandbox can reroll it live
    /// ("Randomize"), and RegenerateTerrain then allocates a differently-sized surface without re-stamping
    /// `occupied` from the placed buildings or culling anything now out of bounds. Rerolling a 640-wide
    /// world to 602 would leave every structure past x=602 floating off-grid, buildable straight over, and
    /// silently dropped on the next save. `id` never changes for the life of a body.
    public static int SurfW(CelestialBody b)
    {
        if (b == null) return MinWidth;
        int baseW = WidthForMass(b.mass);

        // Cheap integer hash of the id -> -1..1. Multiplied by a large odd constant so consecutive ids
        // scatter instead of producing a visible ramp of sizes across a system.
        uint hash = (uint)(b.id * 2654435761u);
        float unit = (hash % 10007u) / 10007f;
        float jitter = (unit * 2f - 1f) * 0.06f;
        int w = Mathf.RoundToInt(baseW * (1f + jitter));

        // A moon is never more than half its host's map, mirroring how its MASS is capped at 40% of the
        // host. Mass alone very nearly guarantees this already; the clamp makes it exact, including for
        // hand-edited bodies in the sandbox where mass can be set freely.
        if (b.parentBody != null)
        {
            int hostW = Mathf.RoundToInt(WidthForMass(b.parentBody.mass));
            w = Mathf.Min(w, Mathf.Max(MinWidth, hostW / 2));
        }

        return Mathf.Clamp(w, MinWidth, Mathf.RoundToInt(MaxWidth));
    }

    public static int SurfH(CelestialBody b) => Mathf.Max(MinWidth / 2, SurfW(b) / 2);

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
