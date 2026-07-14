using System.Collections.Generic;
using UnityEngine;

// What a colony can DO, regardless of which of the two building systems provided it.
//
// THE PROBLEM THIS SOLVES
// A world's infrastructure lives in two lists that didn't know about each other:
//   * CelestialBody.buildings      — abstract colony facilities (BuildingType.Farm, PowerPlant, ...)
//     queued from the Production tab and built over time.
//   * CelestialBody.placedBuildings — concrete structures placed on the surface grid (SurfaceBuildingType
//     .Farm, .SolarArray, ...) in the Planet View.
// They model the same ideas twice. A Farm exists in both; a PowerPlant and a Solar Array are the same
// answer to the same question. But Satisfaction only ever read `buildings`, so a world covered in
// surface farms still counted as having NO food and its people went hungry on paper while standing in
// a wheat field. Placing infrastructure on the map did nothing for the society that lived on it.
//
// This is the single place that answers "does this colony have food / power / research / housing", and
// it counts BOTH. Everything that used to test buildings.Contains(...) asks here instead, so the two
// systems finally describe one colony.
//
// Counts, not booleans, where it matters: two farms feed a colony better than one. That's what makes
// developing the surface worth doing rather than a box to tick.
public static class ColonyFacilities
{
    // ---- Food ----
    /// Everything feeding this colony, from either system.
    public static int FoodSources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = b.buildings.Contains((int)BuildingType.Farm) ? 1 : 0;
        foreach (var p in SurfaceBuildManager.On(b))
            if (p.Type == SurfaceBuildingType.Farm) n++;
        return n;
    }

    /// How well fed the colony is, 0..1. One source feeds it; more is a surplus with diminishing returns.
    public static float FoodLevel(CelestialBody b)
    {
        int n = FoodSources(b);
        if (n <= 0) return 0f;
        return Mathf.Clamp01(0.6f + (n - 1) * 0.2f);
    }

    // ---- Power ----
    /// Anything generating power: the abstract plant, or any of the surface generators. They're all the
    /// same answer to "are the lights on?".
    public static int PowerSources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = b.buildings.Contains((int)BuildingType.PowerPlant) ? 1 : 0;
        foreach (var p in SurfaceBuildManager.On(b))
            if (IsPower(p.Type)) n++;
        return n;
    }

    public static bool IsPower(SurfaceBuildingType t)
        => t == SurfaceBuildingType.SolarArray || t == SurfaceBuildingType.WindFarm
        || t == SurfaceBuildingType.GeothermalPlant || t == SurfaceBuildingType.HydroPlant;

    public static float PowerLevel(CelestialBody b)
    {
        int n = PowerSources(b);
        if (n <= 0) return 0f;
        return Mathf.Clamp01(0.6f + (n - 1) * 0.2f);
    }

    // ---- Research ----
    public static int ResearchSources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = b.researchCenterLevel >= 1 ? b.researchCenterLevel : 0;
        foreach (var p in SurfaceBuildManager.On(b))
            if (p.Type == SurfaceBuildingType.ResearchOutpost) n += p.level;
        return n;
    }

    // ---- Industry: places to work ----
    public static int IndustrySources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = b.buildings.Contains((int)BuildingType.Mine) ? 1 : 0;
        if (b.shipyardLevel >= 1) n += b.shipyardLevel;
        foreach (var p in SurfaceBuildManager.On(b))
            if (p.Type == SurfaceBuildingType.Mine || p.Type == SurfaceBuildingType.Factory ||
                p.Type == SurfaceBuildingType.Refinery || p.Type == SurfaceBuildingType.Spaceport ||
                p.Type == SurfaceBuildingType.SurfaceShipyard) n += p.level;
        return n;
    }

    // ---- Housing ----
    public static int HousingSources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = b.buildings.Contains((int)BuildingType.City) ? 1 : 0;
        foreach (var p in SurfaceBuildManager.On(b))
            if (IsHousing(p.Type)) n++;
        return n;
    }

    public static bool IsHousing(SurfaceBuildingType t)
        => t == SurfaceBuildingType.Habitat || t == SurfaceBuildingType.PlanetCapitol
        || t == SurfaceBuildingType.ColonyShipBase || CityGrowth.IsSettlement(t);

    /// Total structures on this world, both systems — the "develop infrastructure" objective counts
    /// what you actually built, wherever you built it.
    public static int TotalStructures(CelestialBody b)
    {
        if (b == null) return 0;
        return b.buildings.Count + SurfaceBuildManager.On(b).Count;
    }

    // ---- Unified listing, for the Production tab ----
    /// One row in a colony's infrastructure list, from either system.
    public struct Entry
    {
        public string name;
        public string detail;      // tier / siting, whatever is true of this one
        public Color color;
        public bool onSurface;     // true = a placed structure; false = an abstract colony facility
        public PlacedBuilding placed;   // set when onSurface
        public BuildingType building;   // set when !onSurface
    }

    /// Everything built on this world, both systems, in one list — so the Production tab can show the
    /// colony as it actually is rather than half of it.
    public static List<Entry> All(CelestialBody b)
    {
        var list = new List<Entry>();
        if (b == null) return list;

        foreach (int id in b.buildings)
        {
            var t = (BuildingType)id;
            var info = BuildingDatabase.Get(t);
            string tier = t == BuildingType.Shipyard ? $"Level {b.shipyardLevel}"
                        : t == BuildingType.ResearchCenter ? $"Level {b.researchCenterLevel}"
                        : "colony facility";
            list.Add(new Entry
            {
                name = info.name, detail = tier, color = UITheme.SubText,
                onSurface = false, building = t
            });
        }

        foreach (var p in SurfaceBuildManager.On(b))
        {
            var info = p.Info;
            string d = info.index == SurfaceIndexKind.None
                ? $"Lv{p.level} · ({p.x},{p.y})"
                : $"Lv{p.level} · ({p.x},{p.y}) · {p.efficiency * 100f:F0}% sited";
            list.Add(new Entry
            {
                name = info.name, detail = d, color = info.color,
                onSurface = true, placed = p
            });
        }
        return list;
    }
}
