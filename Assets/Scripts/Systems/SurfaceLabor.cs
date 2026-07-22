using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// LABOR — a world's workforce, and what it is currently busy with
//
// Modelled on FacilityPower.BuildPower, which already does exactly this job for shipyards: a pool that
// projects draw from while they run and hand back when they finish. Same shape on purpose — the player
// has already learned it from the shipyard stocks, and two different answers to "why is this waiting"
// would be two things to learn.
//
// DERIVED, NEVER MAINTAINED. The maximum is recomputed from the buildings standing on a world, the way
// PowerGrid recomputes its grids: there is no running total to drift, nothing to reconcile when a
// building is demolished or damaged, and a world loaded from a save is correct without having stored
// anything. Memoized per (world, frame), because the Build tab reads it every frame while a queue ticks.
//
// WHAT GENERATES IT
//   Planetary Capitol   2, plus 1 per upgrade level — the seat of government organises the work
//   Housing (city etc.) 0.5 each — people
//   Storage Depot       1 PER TILE — depots are logistics, and a bigger depot moves more
//
// WHAT SPENDS IT
//   Every build project holds laborPerTile x tiles from the moment it is confirmed until it completes,
//   is cancelled, or is paused. A project short of Labor does not stop — it takes longer (see
//   BuildScaling.TimeFactorFor), so a queue always progresses and freed Labor flows to the next thing.
// ============================================================================================
public static class SurfaceLabor
{
    // ---- Generation rates ------------------------------------------------------------------------
    public const float CapitolBase = 2f;        // at level 1
    public const float CapitolPerLevel = 1f;    // each upgrade beyond that
    public const float HousingEach = 0.5f;
    public const float DepotPerTile = 1f;

    // Memoized per world per frame — see the class note.
    static readonly Dictionary<CelestialBody, float> maxCache = new Dictionary<CelestialBody, float>();
    static int cachedFrame = -1;

    /// Everything this world's buildings can put to work.
    public static float Max(CelestialBody b)
    {
        if (b?.placedBuildings == null) return 0f;

        if (cachedFrame != Time.frameCount) { maxCache.Clear(); cachedFrame = Time.frameCount; }
        if (maxCache.TryGetValue(b, out float cached)) return cached;

        float total = 0f;
        foreach (var p in b.placedBuildings)
        {
            if (p == null) continue;

            // A damaged building works proportionally less. `health` already scales every other output
            // on this world (PlacedBuilding.OutputMult), and a half-collapsed depot organising a full
            // shift would be the one exception.
            float condition = Mathf.Clamp01(p.health);

            switch (p.Type)
            {
                case SurfaceBuildingType.PlanetCapitol:
                    total += (CapitolBase + CapitolPerLevel * Mathf.Max(0, p.level - 1)) * condition;
                    break;

                case SurfaceBuildingType.ColonyShipBase:
                    // The capitol before it is a capitol — a grounded colony ship IS the seat of
                    // government until it is upgraded into one.
                    total += CapitolBase * condition;
                    break;

                case SurfaceBuildingType.Settlement:
                case SurfaceBuildingType.Town:
                case SurfaceBuildingType.City:
                case SurfaceBuildingType.Habitat:
                    total += HousingEach * condition;
                    break;

                case SurfaceBuildingType.StorageDepot:
                    // PER TILE, unlike the rest: a depot's whole contribution is throughput, and a
                    // bigger depot really does move more.
                    total += DepotPerTile * p.TileCount * condition;
                    break;
            }
        }

        maxCache[b] = total;
        return total;
    }

    /// What is currently held by projects under way on this world.
    public static float Used(CelestialBody b)
    {
        var q = SurfaceBuildQueue.For(b);
        if (q == null) return 0f;

        float used = 0f;
        foreach (var job in q)
            if (job != null && job.HoldsLabor) used += job.labor;
        return used;
    }

    /// Free right now. Never negative — a world whose depots were destroyed mid-build is over-committed,
    /// and the honest reading of that is "nothing spare", not a negative workforce.
    public static float Free(CelestialBody b) => Mathf.Max(0f, Max(b) - Used(b));

    /// Anything that changes what is standing on a world invalidates the memo.
    public static void Invalidate() => cachedFrame = -1;

    /// Short readout for the top of the Build tab — the same shape the shipyard shows build power in.
    public static string Summary(CelestialBody b)
    {
        float max = Max(b), used = Used(b);
        return $"{used:0.#} / {max:0.#} Labor";
    }
}
