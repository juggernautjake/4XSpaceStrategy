using UnityEngine;

// ============================================================================================
// WHAT A DRAWN BUILDING COSTS AND PRODUCES
//
// You paint the tiles a building occupies, and every number about it scales with how many you painted.
// The two curves below are the whole economy of that decision, and they are deliberately different.
//
//   COST, BUILD TIME, UPKEEP   each successive tile is 5% dearer than the last
//   OUTPUT                     each successive tile yields 10% more than the last
//
// So for N tiles, summing the series:
//
//   costMultiplier(N)   = N + 0.05·N(N−1)/2
//   outputMultiplier(N) = N + 0.10·N(N−1)/2
//
//   N        1      2      3      4      6       10
//   cost    1.00   2.05   3.15   4.30   6.75   12.25
//   output  1.00   2.10   3.30   4.60   7.50   14.50
//
// WHY OUTPUT RISES FASTER THAN COST, when the stated goal is that mega-farms should NOT be easy.
// Because cost is not the brake — Labor and TIME are. A ten-tile farm out-produces ten one-tile farms
// and costs less than ten of them, which is the reward for committing to it; but it also ties up ten
// Labor for a build that takes twelve times as long as a single tile. While it is going up, nothing
// else on that world is. That is a real decision, whereas making it simply expensive would just make
// it something you do later with the same shrug.
//
// Every constant here is meant to be re-tuned by eye once it is running. They are in ONE place so that
// is a five-second change rather than an archaeology expedition.
// ============================================================================================
public static class BuildScaling
{
    /// Extra cost, build time and upkeep per additional tile, as a fraction of the first tile.
    public const float CostStep = 0.05f;

    /// Extra output per additional tile. Higher than CostStep on purpose — see the note above.
    public const float OutputStep = 0.10f;

    /// Labor a tile ties up while it is being built, before any per-building multiplier.
    public const float BaseLaborPerTile = 1f;

    /// How much a build slows per unit of Labor it is short. A project starved of half its workforce
    /// does not stop — it crawls, and the queue behind it keeps its place.
    public const float MissingLaborTimePenalty = 0.35f;

    // The triangular-number term both curves share: 0, 1, 3, 6, 10 … for N = 1, 2, 3, 4, 5.
    static float Steps(int tiles) => tiles <= 1 ? 0f : tiles * (tiles - 1) * 0.5f;

    /// Multiplier on a building's authored cost, build time and upkeep for a footprint of `tiles`.
    public static float CostMultiplier(int tiles)
    {
        tiles = Mathf.Max(1, tiles);
        return tiles + CostStep * Steps(tiles);
    }

    /// Multiplier on a building's authored output for a footprint of `tiles`.
    public static float OutputMultiplier(int tiles)
    {
        tiles = Mathf.Max(1, tiles);
        return tiles + OutputStep * Steps(tiles);
    }

    /// Total Labor a building of this type and size occupies while under construction.
    ///
    /// Linear in tiles, unlike cost — one tile is one tile's worth of work whether it is the first or
    /// the fortieth. What makes a big building expensive in Labor is simply that there is more of it.
    public static float LaborFor(SurfaceBuildingType type, int tiles)
        => Mathf.Max(1f, tiles) * LaborPerTile(type);

    /// Labor per tile for a given class. Higher for the things that are genuinely harder to build —
    /// a reactor is not a field.
    ///
    /// Routed through TechEffects so research can move it later without every call site changing: the
    /// request explicitly asks for this number to be modifiable up or down by bonuses.
    public static float LaborPerTile(SurfaceBuildingType type)
    {
        float baseCost = BaseLaborPerTile;
        switch (type)
        {
            case SurfaceBuildingType.FissionReactor:
            case SurfaceBuildingType.FusionReactor:
                baseCost = 3f; break;
            case SurfaceBuildingType.SteamTurbine:
            case SurfaceBuildingType.CombustionPlant:
            case SurfaceBuildingType.Spaceport:
            case SurfaceBuildingType.SurfaceShipyard:
                baseCost = 2f; break;
            case SurfaceBuildingType.PowerNode:
                baseCost = 0.5f; break;   // a relay is a mast, not a building
        }
        return Mathf.Max(0.1f, baseCost * Mathf.Max(0.1f, TechEffects.BuildCostMult));
    }

    /// How much longer a build takes when the workforce is short.
    ///
    /// `have` below `want` stretches the remaining work rather than blocking it, so a queue never
    /// deadlocks: something always progresses, and freed Labor speeds up whatever is next in line.
    public static float TimeFactorFor(float want, float have)
    {
        if (want <= 0.001f) return 1f;
        float missing = Mathf.Max(0f, want - Mathf.Max(0f, have));
        return 1f + missing * MissingLaborTimePenalty;
    }
}
