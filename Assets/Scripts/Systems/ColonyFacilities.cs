using System.Collections.Generic;
using UnityEngine;

// What a colony can DO — does it have food, power, research, industry, somewhere to live.
//
// THIS USED TO RECONCILE TWO BUILDING SYSTEMS. A world's infrastructure lived in two lists that did not
// know about each other: `CelestialBody.buildings`, the abstract facilities queued from a Production
// tab, and `placedBuildings`, the structures standing on the surface grid. They modelled the same ideas
// twice — a Farm existed in both — and Satisfaction read only the first, so a world covered in surface
// farms counted as having NO food and its people went hungry on paper while standing in a wheat field.
//
// THE ABSTRACT SYSTEM IS GONE. Nothing adds to `buildings` any more except the City marker; every
// facility a colony has is a structure you can point at on the map. So this no longer reconciles
// anything — it is simply the one place that reads the surface and answers the five questions, and
// everything that used to test buildings.Contains(...) asks here instead.
//
// Deliberately NOT reading the legacy list even for old saves. A save from before the surface grid has
// `buildings` entries and no structures, and honouring them would keep exactly the ghost the whole
// change is removing: a colony that claims to have a mine with nothing anywhere on it. Such a world
// reads as undeveloped, which is what it is — the ground is empty and the player can now see that and
// fix it, rather than being told everything is fine by a list.
//
// Counts, not booleans, where it matters: two farms feed a colony better than one. That's what makes
// developing the surface worth doing rather than a box to tick.
public static class ColonyFacilities
{
    // ---- Food ----
    /// Everything feeding this colony.
    public static int FoodSources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = 0;
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
    /// Anything generating power — the answer to "are the lights on?".
    public static int PowerSources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = 0;
        foreach (var p in SurfaceBuildManager.On(b))
            if (IsPower(p.Type)) n++;
        return n;
    }

    /// Does this class MAKE power? Asked of the database rather than listed by hand.
    ///
    /// This was a hardcoded list of four — solar, wind, geothermal, hydro — written before the
    /// Electrical category existed. Every generator added since was invisible to it: a world running on
    /// a fusion reactor and three combustion plants reported "no power" to Satisfaction and took the
    /// unrest for it, which is a hard bug to even suspect, because the Power tab next door showed a
    /// perfectly healthy grid. Reading energyPerSec means a generator added tomorrow counts on the day
    /// it is added.
    ///
    /// The seats of government are excluded on purpose. Both carry a founding reactor (see
    /// SurfaceBuildingDatabase.Reactor) so every settled world would otherwise always report at least
    /// one power source — and "this colony has power" would be true by definition and mean nothing.
    public static bool IsPower(SurfaceBuildingType t)
    {
        if (t == SurfaceBuildingType.ColonyShipBase || t == SurfaceBuildingType.PlanetCapitol) return false;
        if (CityGrowth.IsSettlement(t)) return false;   // a city's own lights are not a power industry
        var info = SurfaceBuildingDatabase.Get(t);
        return info != null && info.energyPerSec > 0f;
    }

    public static float PowerLevel(CelestialBody b)
    {
        int n = PowerSources(b);
        if (n <= 0) return 0f;
        return Mathf.Clamp01(0.6f + (n - 1) * 0.2f);
    }

    // ---- Research ----
    // The world's laboratory tier plus every field station. The tier itself is derived from the campus
    // standing on the surface now (SurfaceBuildManager.SyncFacilityTiers), so this is two readings of
    // the map rather than one of the map and one of a list.
    public static int ResearchSources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = b.researchCenterLevel >= 1 ? b.researchCenterLevel : 0;
        foreach (var p in SurfaceBuildManager.On(b))
            if (p.Type == SurfaceBuildingType.ResearchOutpost) n += p.level;
        return n;
    }

    // ---- Industry: places to work ----
    // The shipyard is counted through the structure rather than through b.shipyardLevel, which would
    // have double-counted it now that the tier is derived FROM that structure.
    public static int IndustrySources(CelestialBody b)
    {
        if (b == null) return 0;
        int n = 0;
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
        int n = 0;
        foreach (var p in SurfaceBuildManager.On(b))
            if (IsHousing(p.Type)) n++;
        return n;
    }

    public static bool IsHousing(SurfaceBuildingType t)
        => t == SurfaceBuildingType.Habitat || t == SurfaceBuildingType.PlanetCapitol
        || t == SurfaceBuildingType.ColonyShipBase || CityGrowth.IsSettlement(t);

    /// Total structures standing on this world — what the "develop infrastructure" objective counts.
    public static int TotalStructures(CelestialBody b)
        => b == null ? 0 : SurfaceBuildManager.On(b).Count;

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
