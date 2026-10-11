using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE INDUSTRIAL CHAIN — what mines, refineries and factories are FOR (2026-10-11)
//
// "Please work on the logic and purposes of the factories, refineries, metal mines and all of that
// kind of thing."
//
// Before this, all three were the same building with different numbers: each paid a flat metal/s
// (1.6 / 1.2 / 1.1) and nothing connected them. Now they are a chain, and each link does something
// the others cannot:
//
//   MINE       EXTRACTS. Metal out of the ground: 1.6 metal/s per tile-equivalent x the Mineral Index of
//              its footprint (locked in at placement) x size curve x tech level x condition x power x
//              synergy x Industry research. The only building that turns ore into metal, and the only one
//              that must sit on a seam.
//
//   REFINERY   PROCESSES what the mines dig. It no longer needs ore under it (it reads no index: a smelter
//              needs ore NEXT to it, not beneath it), so it never competes with a mine for seam tiles.
//              Its own output is small (0.4 metal/s, slag and regolith reclaim); its value is the +25% it
//              adds to every mine it touches (up to +50%), and the +25% each touching mine feeds back into
//              it. A refinery tile serves 2 mine tiles, shared across every mine it touches (SurfaceSynergy).
//              Worth it exactly when seam ground is scarce — which, at a 70% Mineral floor, it always is.
//
//   FACTORY    MANUFACTURES. It turns metal and energy into INDUSTRY, and Industry is construction speed
//              on its own world: every point makes this world's build queue progress 15% faster, up to
//              +100% (double speed). It eats 0.25 metal/s per point while something is actually being
//              built here and nothing when the queue is idle; if the stockpile runs dry, it slows with it.
//              Industry per factory = 1 x its output multiplier (size, tech, condition, power, synergy), so
//              a 3-tile factory is ~3.3 Industry, ~+50% build speed for ~0.8 metal/s while building.
//              Fed by refineries (+20% each, up to +40%), depots (+10%) and housing (+15%).
//
//   Supporting cast: a STORAGE DEPOT next to industry is +10% (logistics); HOUSING next to industry is up
//   to +15% (workers next door); Research Lab and Research Centre touching is up to +30%; Power
//   Distribution next to plants is +30%, as before. All of it from SurfaceSynergy, the one function.
//
// POWER is what ties it together: every link draws power that scales with its drawn size (PowerGrid.
// DrawOf), so a bigger industrial district needs more generation, and a short grid slows the buildings at
// the ends of its chains first (see PowerGrid's header).
//
// BALANCE against the opening: starting floor 900 metal / 650 energy / 300 water, storage ceiling 3000
// base. Mine/Farm/Lab draws were cut per tile (0.8 -> 0.4, 0.4 -> 0.15, 1.2 -> 0.5) because draw now
// scales with size; the Combustion Plant went 1.0 -> 1.2/s so the onboarding's farm + 3-tile plant + mine
// + lab + shipyard grid balances (~4.4 made against ~3.9 drawn at typical sizes).
//
// TRACKING: everything that reports production reads SurfaceOutput below — the economy tick, the world's
// books (WorldEconomy, the Overview strip), the building card. Buildings under construction are queue jobs,
// not PlacedBuildings, so they count for nothing until they stand. Nothing here is saved: synergy, hops and
// Industry are derived from what stands on the map, so a save from before this loads and simply re-derives.
// ============================================================================================

/// The production formulas, once. Every reader of "what does this building make" goes through here.
public static class SurfaceOutput
{
    /// Siting x size x tech x condition x power x synergy: what a consumer-side output is scaled by.
    public static float Run(CelestialBody b, PlacedBuilding p)
        => p.OutputMult * PowerGrid.PowerFactor(b, p) * (1f + SurfaceSynergy.Bonus(b, p));

    public static float Metal(CelestialBody b, PlacedBuilding p)
        => p?.Info == null || p.Info.metalPerSec <= 0f ? 0f : p.Info.metalPerSec * Run(b, p) * TechEffects.OreYieldMult;

    public static float Water(CelestialBody b, PlacedBuilding p)
        => p?.Info == null || p.Info.waterPerSec <= 0f ? 0f : p.Info.waterPerSec * Run(b, p);

    /// Research before the empire-wide ResearchRateMult, which ColonyManager applies on top.
    public static float Research(CelestialBody b, PlacedBuilding p)
        => p?.Info == null || p.Info.researchPerSec <= 0f ? 0f : p.Info.researchPerSec * Run(b, p);

    /// A generator's output. Plants are not throttled by the grid they feed; the switchyard is synergy.
    public static float Energy(CelestialBody b, PlacedBuilding p)
        => p?.Info == null || p.Info.energyPerSec <= 0f ? 0f
         : p.Info.energyPerSec * p.OutputMult * (1f + SurfaceSynergy.Bonus(b, p));

    /// Storage ceiling this building adds. A drawn depot bigger than its authored footprint holds
    /// proportionally more (it used to hold the same whatever size it was drawn); never less than before.
    public static float Storage(PlacedBuilding p)
    {
        var info = p?.Info;
        if (info == null || info.storageCapacity <= 0f) return 0f;
        float size = 1f;
        if (p.HasDrawnShape && info.shape != null && info.shape.Length > 0)
            size = Mathf.Max(1f, p.TileCount / (float)info.shape.Length);
        return info.storageCapacity * p.LevelMult * size;
    }
}

/// Factories, and the construction speed they buy. See the header.
public static class Industry
{
    public const float PerFactory = 1f;        // Industry per unit of a factory's output multiplier
    public const float SpeedPerPoint = 0.15f;  // build speed per Industry point
    public const float MaxSpeedBonus = 1.0f;   // +100%: double speed at most
    public const float MetalPerPoint = 0.25f;  // metal/s per (useful) point, only while building

    static readonly Dictionary<CelestialBody, float> fed = new Dictionary<CelestialBody, float>();
    static readonly Dictionary<CelestialBody, float> pointsMemo = new Dictionary<CelestialBody, float>();
    static int pointsFrame = -1;

    public static void Invalidate() { pointsMemo.Clear(); pointsFrame = -1; }

    /// A new galaxy: forget last game's feed state.
    public static void Reset() { fed.Clear(); Invalidate(); }

    public static bool IsFactory(PlacedBuilding p) => p != null && p.Type == SurfaceBuildingType.Factory;

    /// Industry one factory produces right now.
    public static float PointsOf(CelestialBody b, PlacedBuilding p)
        => IsFactory(p) ? PerFactory * SurfaceOutput.Run(b, p) : 0f;

    /// Industry across a world.
    public static float Points(CelestialBody b)
    {
        if (b?.placedBuildings == null) return 0f;
        if (pointsFrame != Time.frameCount) { pointsMemo.Clear(); pointsFrame = Time.frameCount; }
        if (pointsMemo.TryGetValue(b, out float v)) return v;
        v = 0f;
        foreach (var p in b.placedBuildings) if (IsFactory(p)) v += PointsOf(b, p);
        pointsMemo[b] = v;
        return v;
    }

    /// Points past the speed cap do nothing, so they are not fed either.
    public static float UsefulPoints(CelestialBody b) => Mathf.Min(Points(b), MaxSpeedBonus / SpeedPerPoint);

    /// Is anything being built on this world right now (an unpaused job in its queue)?
    public static bool Busy(CelestialBody b)
    {
        var q = SurfaceBuildQueue.Peek(b);
        if (q == null) return false;
        foreach (var job in q) if (job != null && !job.paused) return true;
        return false;
    }

    /// The fraction of their metal the factories got on the last tick (1 when idle).
    public static float Fed(CelestialBody b) => b != null && fed.TryGetValue(b, out float f) ? f : 1f;

    /// The bonus the factories here would give a build, fully fed.
    public static float PotentialBonus(CelestialBody b) => Mathf.Min(MaxSpeedBonus, Points(b) * SpeedPerPoint);

    /// What construction on this world runs at: 1 + bonus x fed. 1 when nothing is building.
    public static float SpeedMult(CelestialBody b) => Busy(b) ? 1f + PotentialBonus(b) * Fed(b) : 1f;

    /// Metal/s the factories ask for right now.
    public static float MetalDemand(CelestialBody b) => Busy(b) ? UsefulPoints(b) * MetalPerPoint : 0f;

    /// Feed the factories. Called from SurfaceBuildManager.TickOutput, before the build queue ticks.
    public static void Tick(CelestialBody b, float dt)
    {
        if (b == null || dt <= 0f) return;
        float need = MetalDemand(b) * dt;
        if (need <= 0f) { fed[b] = 1f; return; }
        float have = Mathf.Max(0f, PlayerEconomy.Get(ResourceType.Metal));
        float take = Mathf.Min(need, have);
        if (take > 0f) PlayerEconomy.Add(ResourceType.Metal, -take);
        fed[b] = Mathf.Clamp01(take / need);
    }

    /// One line on what a class is FOR in the chain, for the card and the placement readout.
    public static string RoleLine(SurfaceBuildingInfo info)
    {
        if (info == null) return null;
        switch (info.type)
        {
            case SurfaceBuildingType.Mine:
                return "<color=#C9A94D>Extracts metal. Touch a Refinery: +25% (up to +50%).</color>";
            case SurfaceBuildingType.Refinery:
                return "<color=#C9A94D>Smelts the ore of the mines it touches: +25% each (1 tile serves 2 mine tiles).</color>";
            case SurfaceBuildingType.Factory:
                return $"<color=#C9A94D>Makes Industry: +{SpeedPerPoint * 100f:0}% build speed here per point, up to " +
                       $"+{MaxSpeedBonus * 100f:0}%; eats {MetalPerPoint:0.##} metal/s per point while building.</color>";
            default: return null;
        }
    }
}
