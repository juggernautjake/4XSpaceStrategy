using System.Collections.Generic;
using UnityEngine;

// Placing structures on a planet's surface grid.
//
// Placement is the packing puzzle: a footprint must be in bounds, on dry ground, and clear of anything
// already standing. Where you put it is not cosmetic — a building's efficiency is the average of its
// driving survey index across its own cells, locked in the moment you place it. A mine on a seam pays
// forever; a mine on dead rock is a permanent mistake.
public static class SurfaceBuildManager
{
    // ---- Queries ----
    public static List<PlacedBuilding> On(CelestialBody b)
        => b != null && b.placedBuildings != null ? b.placedBuildings : new List<PlacedBuilding>();

    /// The building occupying a cell, or null.
    public static PlacedBuilding At(CelestialBody b, int x, int y)
    {
        foreach (var p in On(b))
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
                if (c.x == x && c.y == y) return p;
        return null;
    }

    /// Every cell currently covered by a structure.
    public static HashSet<Vector2Int> Occupied(CelestialBody b)
    {
        var set = new HashSet<Vector2Int>();
        foreach (var p in On(b))
            foreach (var c in SurfaceBuildingDatabase.Footprint(p)) set.Add(c);
        return set;
    }

    // ---- Validity ----
    /// Can this cell hold part of a structure at all?
    public static bool CellBuildable(CelestialBody b, SurfaceBuildingInfo info, int x, int y, out string why)
    {
        why = null;
        if (b == null || b.surface == null) { why = "no surface data"; return false; }
        if (x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) { why = "off the edge of the map"; return false; }

        var tile = b.surface.tiles[x, y];
        if (tile == null) { why = "no ground here"; return false; }
        if (!info.allowsWater && PlanetTerrainGenerator.IsWater(tile.type)) { why = "can't build on water"; return false; }
        return true;
    }

    /// Can the whole footprint go down here?
    public static bool CanPlace(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation, out string why)
    {
        why = null;
        var info = SurfaceBuildingDatabase.Get(t);

        if (b == null) { why = "no world"; return false; }
        if (b.owner != FactionManager.Player) { why = "this world isn't yours"; return false; }
        if (!b.Surveyed) { why = "survey this world first"; return false; }

        var occupied = Occupied(b);
        foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, rotation))
        {
            if (!CellBuildable(b, info, c.x, c.y, out why)) return false;
            if (occupied.Contains(c)) { why = "something is already built here"; return false; }
        }

        int m = ColonyManager.DiscCost(info.costMetal), e = ColonyManager.DiscCost(info.costEnergy);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(m, e)) { why = $"need {m} metal, {e} energy"; return false; }
        return true;
    }

    /// How well a structure would perform here: the average of its driving index across its footprint.
    /// This is the number the whole placement puzzle is about, so the UI shows it live under the ghost.
    public static float EfficiencyAt(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation)
    {
        var info = SurfaceBuildingDatabase.Get(t);
        if (info.index == SurfaceIndexKind.None) return 1f;   // terrain-agnostic: always full output

        var cells = SurfaceBuildingDatabase.Footprint(t, x, y, rotation);
        if (cells.Count == 0) return 0f;
        float sum = 0f;
        foreach (var c in cells) sum += SurfaceIndex.Get(b, info.index, c.x, c.y);
        return Mathf.Clamp01(sum / cells.Count);
    }

    // ---- Mutation ----
    public static bool Place(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation)
    {
        if (!CanPlace(b, t, x, y, rotation, out _)) return false;
        var info = SurfaceBuildingDatabase.Get(t);

        int m = ColonyManager.DiscCost(info.costMetal), e = ColonyManager.DiscCost(info.costEnergy);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(m, e)) return false;

        if (b.placedBuildings == null) b.placedBuildings = new List<PlacedBuilding>();
        float eff = EfficiencyAt(b, t, x, y, rotation);
        b.placedBuildings.Add(new PlacedBuilding { type = (int)t, x = x, y = y, rotation = rotation, efficiency = eff });

        // Mark the ground so the terrain viewer and anything else reading `occupied` agrees with us.
        foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, rotation))
            if (InBounds(b, c.x, c.y)) b.surface.tiles[c.x, c.y].occupied = true;

        SimpleAudio.Instance?.PlayClick();
        return true;
    }

    /// Demolish a structure and refund most of its cost — the materials are still standing there.
    public static void Demolish(CelestialBody b, PlacedBuilding p)
    {
        if (b == null || p == null || b.placedBuildings == null) return;
        if (!b.placedBuildings.Remove(p)) return;

        foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            if (InBounds(b, c.x, c.y)) b.surface.tiles[c.x, c.y].occupied = false;

        if (!GameMode.DevMode)
        {
            var info = p.Info;
            PlayerEconomy.Add(ResourceType.Metal, ColonyManager.DiscCost(info.costMetal) * 0.6f);
            PlayerEconomy.Add(ResourceType.Energy, ColonyManager.DiscCost(info.costEnergy) * 0.6f);
        }
    }

    static bool InBounds(CelestialBody b, int x, int y)
        => b?.surface != null && x >= 0 && y >= 0 && x < b.surface.width && y < b.surface.height;

    // ---- Economy ----
    // What a world's surface structures contribute per second, each scaled by how well it was sited.
    // Called from ColonyManager's colony tick.
    public static void TickOutput(CelestialBody b, float dt)
    {
        foreach (var p in On(b))
        {
            var info = p.Info;
            float eff = Mathf.Clamp01(p.efficiency);
            if (info.metalPerSec > 0f) PlayerEconomy.Add(ResourceType.Metal, info.metalPerSec * eff * TechEffects.OreYieldMult * dt);
            if (info.energyPerSec > 0f) PlayerEconomy.Add(ResourceType.Energy, info.energyPerSec * eff * dt);
            if (info.waterPerSec > 0f) PlayerEconomy.Add(ResourceType.Water, info.waterPerSec * eff * dt);
        }
    }

    public static float ResearchPerSec(CelestialBody b)
    {
        float sum = 0f;
        foreach (var p in On(b)) sum += p.Info.researchPerSec * Mathf.Clamp01(p.efficiency);
        return sum;
    }

    public static float PopGrowthPerSec(CelestialBody b)
    {
        float sum = 0f;
        foreach (var p in On(b)) sum += p.Info.popGrowthPerSec * Mathf.Clamp01(p.efficiency);
        return sum;
    }

    /// How densely developed a world is — the fraction of its buildable land under structures.
    public static float Density(CelestialBody b)
    {
        if (b?.surface == null) return 0f;
        int buildable = 0;
        for (int x = 0; x < b.surface.width; x++)
            for (int y = 0; y < b.surface.height; y++)
                if (!PlanetTerrainGenerator.IsWater(b.surface.tiles[x, y].type)) buildable++;
        if (buildable == 0) return 0f;
        return Mathf.Clamp01(Occupied(b).Count / (float)buildable);
    }

    public static string EfficiencyLabel(float e)
    {
        if (e >= 0.85f) return "Excellent";
        if (e >= 0.65f) return "Good";
        if (e >= 0.45f) return "Fair";
        if (e >= 0.25f) return "Poor";
        return "Terrible";
    }

    public static Color EfficiencyColor(float e) => Habitability.ScoreColor(e * 100f);
}
