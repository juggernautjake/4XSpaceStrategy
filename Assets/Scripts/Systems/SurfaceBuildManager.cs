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
    // While the surface economy is being tuned, a world may hold only one of each structure. Flip this
    // off to allow duplicates (the genuinely unique ones — capitol, shipyard — stay capped either way
    // via SurfaceBuildingInfo.uniquePerWorld).
    public const bool OneOfEachPerWorld = true;

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

    public static int CountOf(CelestialBody b, SurfaceBuildingType t)
    {
        int n = 0;
        foreach (var p in On(b)) if (p.Type == t) n++;
        return n;
    }

    public static PlacedBuilding FirstOf(CelestialBody b, SurfaceBuildingType t)
    {
        foreach (var p in On(b)) if (p.Type == t) return p;
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

        // ONE OF EACH per world, for now: place a second and it's refused. (Some types — the capitol,
        // the shipyard — are inherently unique; the rest are capped here while the surface economy is
        // still being tuned. Relax this by dropping the OneOfEachPerWorld check.)
        if ((OneOfEachPerWorld || info.uniquePerWorld) && CountOf(b, t) > 0)
        { why = $"this world already has a {info.name.ToLower()}"; return false; }

        // A Planet Capitol isn't built from scratch: it's what a Colony Ship Base becomes. Placing one
        // directly would leave the grounded ship sitting next to it with nothing to do.
        if (t == SurfaceBuildingType.PlanetCapitol)
        { why = "upgrade this world's Colony Ship Base into a capitol instead"; return false; }
        if (t == SurfaceBuildingType.ColonyShipBase && !GameMode.DevMode)
        { why = "a colony ship becomes this when it settles a world"; return false; }

        // Settlements/towns/cities are GROWN by the population (CityGrowth), never placed. You get one
        // capital; the rest is people housing themselves.
        if (CityGrowth.IsSettlement(t) && !GameMode.DevMode)
        { why = "settlements grow on their own as the colony's population rises"; return false; }

        // A world's shipyard already exists (the capital's birthright yard, say) — don't allow a second.
        if (t == SurfaceBuildingType.SurfaceShipyard && b.shipyardLevel >= 1)
        { why = "this world already has a shipyard — upgrade its tier from the Production tab"; return false; }

        var occupied = Occupied(b);
        foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, rotation))
        {
            if (!CellBuildable(b, info, c.x, c.y, out why)) return false;
            if (occupied.Contains(c)) { why = "something is already built here"; return false; }
        }

        // SITING REQUIREMENT. Some things aren't merely inefficient on the wrong ground, they're
        // pointless: a geothermal plant on cold rock produces nothing. Checked against the footprint's
        // averaged index, so a plant half-on a volcano still counts.
        if (info.minIndex > 0f && info.index != SurfaceIndexKind.None && !GameMode.DevMode)
        {
            float here = EfficiencyAt(b, t, x, y, rotation);
            if (here < info.minIndex)
            {
                float best = SurfaceIndex.Best(b, info.index);
                why = best >= info.minIndex
                    ? $"{SurfaceIndex.Name(info.index)} only {here * 100f:F0}% here — needs {info.minIndex * 100f:F0}%. Try the highlighted sites."
                    : $"{SurfaceIndex.Name(info.index)} only {here * 100f:F0}% here — needs {info.minIndex * 100f:F0}%. " +
                      $"This world's best is {best * 100f:F0}%: nowhere on it will support one.";
                return false;
            }
        }

        int m = ColonyManager.DiscCost(info.costMetal), e = ColonyManager.DiscCost(info.costEnergy);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(m, e)) { why = $"need {m} metal, {e} energy"; return false; }
        return true;
    }

    /// What a structure would actually PRODUCE at this spot, per second, at tech level 1 — the number
    /// the hover readout quotes. Distinct from the index: the index is the ground, this is the payoff.
    public static string PredictedYield(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation)
    {
        var info = SurfaceBuildingDatabase.Get(t);
        float eff = EfficiencyAt(b, t, x, y, rotation);

        var parts = new List<string>();
        if (info.metalPerSec > 0f) parts.Add($"{info.metalPerSec * eff * TechEffects.OreYieldMult:0.00} metal/s");
        if (info.energyPerSec > 0f) parts.Add($"{info.energyPerSec * eff:0.00} energy/s");
        if (info.waterPerSec > 0f) parts.Add($"{info.waterPerSec * eff:0.00} water/s");
        if (info.researchPerSec > 0f) parts.Add($"{info.researchPerSec * eff:0.00} research/s");
        if (info.popGrowthPerSec > 0f) parts.Add($"{info.popGrowthPerSec * eff:0.0} growth/s");
        if (info.storageCapacity > 0f) parts.Add($"+{info.storageCapacity:0} storage");
        if (parts.Count == 0) return "no direct output";
        return string.Join(" · ", parts);
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

        // A surface shipyard IS the world's shipyard: placing it gives the world tier 1, which is what
        // adds its build power to the empire pool and lets the Production tab upgrade it from there.
        if (t == SurfaceBuildingType.SurfaceShipyard)
        {
            b.shipyardLevel = Mathf.Max(1, b.shipyardLevel);
            UnitManager.Instance?.NotifyBuildChanged();
        }

        SimpleAudio.Instance?.PlayClick();
        return true;
    }

    /// Place a structure with no cost and no checks. Used when the game itself puts something down —
    /// a colony ship grounding itself as the new colony's base.
    public static bool ForcePlace(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation)
    {
        if (b?.surface == null) return false;
        var occupied = Occupied(b);
        var info = SurfaceBuildingDatabase.Get(t);
        foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, rotation))
        {
            if (!CellBuildable(b, info, c.x, c.y, out _)) return false;
            if (occupied.Contains(c)) return false;
        }

        if (b.placedBuildings == null) b.placedBuildings = new List<PlacedBuilding>();
        b.placedBuildings.Add(new PlacedBuilding
        { type = (int)t, x = x, y = y, rotation = rotation, efficiency = EfficiencyAt(b, t, x, y, rotation) });
        foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, rotation))
            if (InBounds(b, c.x, c.y)) b.surface.tiles[c.x, c.y].occupied = true;
        return true;
    }

    /// Find somewhere a footprint fits, scanning outward from the middle of the map. Used to ground a
    /// colony ship somewhere sensible without asking the player to place it.
    public static bool FindSpot(CelestialBody b, SurfaceBuildingType t, out int fx, out int fy)
    {
        fx = fy = -1;
        if (b?.surface == null) return false;
        var info = SurfaceBuildingDatabase.Get(t);
        var occupied = Occupied(b);
        int w = b.surface.width, h = b.surface.height;

        // Spiral-ish: try the centre first so a colony grows outward from its landing site.
        int cx = w / 2, cy = h / 2;
        for (int r = 0; r < Mathf.Max(w, h); r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;   // ring only
                    int x = cx + dx, y = cy + dy;
                    bool ok = true;
                    foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, 0))
                    {
                        if (!CellBuildable(b, info, c.x, c.y, out _) || occupied.Contains(c)) { ok = false; break; }
                    }
                    if (ok) { fx = x; fy = y; return true; }
                }
        return false;
    }

    // ---- Upgrades ----
    // A Colony Ship Base becomes a Planet Capitol in place: same footprint, so it never has to find
    // room, and the colony visibly graduates from "a parked ship" to "a seat of government".
    public static bool CanUpgrade(CelestialBody b, PlacedBuilding p, out string why)
    {
        why = null;
        if (p == null || !p.Info.upgradesTo.HasValue) { why = "nothing to upgrade into"; return false; }
        int m = ColonyManager.DiscCost(p.Info.upgradeMetal), e = ColonyManager.DiscCost(p.Info.upgradeEnergy);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(m, e)) { why = $"need {m} metal, {e} energy"; return false; }
        return true;
    }

    public static bool Upgrade(CelestialBody b, PlacedBuilding p)
    {
        if (!CanUpgrade(b, p, out _)) return false;
        var info = p.Info;
        int m = ColonyManager.DiscCost(info.upgradeMetal), e = ColonyManager.DiscCost(info.upgradeEnergy);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(m, e)) return false;

        var to = info.upgradesTo.Value;
        p.type = (int)to;
        p.efficiency = EfficiencyAt(b, to, p.x, p.y, p.rotation);
        SimpleAudio.Instance?.PlayNotify(NotifKind.Discovery);
        NotificationManager.Instance?.Push($"{SurfaceBuildingDatabase.Get(to).name} completed on {b.name}",
            SurfaceBuildingDatabase.Get(to).description, null, NotifKind.Discovery);
        return true;
    }

    // ---- Tech levels ----
    // Upgrading a structure in place: more output, more hit points. Each tier costs more than the last,
    // so a level-3 building is a real investment rather than a formality.
    public static void LevelUpCost(PlacedBuilding p, out int metal, out int energy)
    {
        var info = p.Info;
        float mult = 0.8f + p.level * 0.5f;                 // Lv1->2 costs 1.3x base, Lv2->3 costs 1.8x
        metal = Mathf.RoundToInt(ColonyManager.DiscCost(info.costMetal) * mult);
        energy = Mathf.RoundToInt(ColonyManager.DiscCost(info.costEnergy) * mult);
    }

    public static bool CanUpgradeLevel(CelestialBody b, PlacedBuilding p, out string why)
    {
        why = null;
        if (p == null) { why = "nothing selected"; return false; }
        if (!p.CanUpgrade) { why = "already at max tech level"; return false; }
        if (b == null || b.owner != FactionManager.Player) { why = "this world isn't yours"; return false; }
        LevelUpCost(p, out int m, out int e);
        if (!GameMode.DevMode && !PlayerEconomy.CanAfford(m, e)) { why = $"need {m} metal, {e} energy"; return false; }
        return true;
    }

    public static bool UpgradeLevel(CelestialBody b, PlacedBuilding p)
    {
        if (!CanUpgradeLevel(b, p, out _)) return false;
        LevelUpCost(p, out int m, out int e);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(m, e)) return false;

        p.level = Mathf.Clamp(p.level + 1, 1, PlacedBuilding.MaxLevel);
        p.health = 1f;   // a rebuilt structure comes back in full repair

        // A shipyard's tier IS the world's shipyard tier — upgrading the structure upgrades the yard.
        if (p.Type == SurfaceBuildingType.SurfaceShipyard)
        {
            b.shipyardLevel = Mathf.Max(b.shipyardLevel, p.level);
            UnitManager.Instance?.NotifyBuildChanged();
        }

        SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
        return true;
    }

    // ---- Adjacency ----
    // A power plant next to a Power Distribution hub runs better. This is checked LIVE rather than baked
    // into efficiency, so building a hub later rewards the plants already standing around it.
    public static float AdjacencyBonus(CelestialBody b, PlacedBuilding p)
    {
        if (p == null || p.Info.energyPerSec <= 0f) return 0f;

        var mine = new HashSet<Vector2Int>(SurfaceBuildingDatabase.Footprint(p));
        float best = 0f;
        foreach (var other in On(b))
        {
            if (other == p || other.Info.adjacencyPowerBonus <= 0f) continue;
            foreach (var c in SurfaceBuildingDatabase.Footprint(other))
            {
                if (mine.Contains(new Vector2Int(c.x + 1, c.y)) || mine.Contains(new Vector2Int(c.x - 1, c.y)) ||
                    mine.Contains(new Vector2Int(c.x, c.y + 1)) || mine.Contains(new Vector2Int(c.x, c.y - 1)))
                { best = Mathf.Max(best, other.Info.adjacencyPowerBonus); break; }
            }
        }
        return best;
    }

    /// Demolish a structure and refund most of its cost — the materials are still standing there.
    public static void Demolish(CelestialBody b, PlacedBuilding p)
    {
        if (b == null || p == null || b.placedBuildings == null) return;
        if (!b.placedBuildings.Remove(p)) return;

        foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            if (InBounds(b, c.x, c.y)) b.surface.tiles[c.x, c.y].occupied = false;

        // Tearing down the world's shipyard takes its build power out of the pool with it.
        if (p.Type == SurfaceBuildingType.SurfaceShipyard && CountOf(b, SurfaceBuildingType.SurfaceShipyard) == 0)
        {
            b.shipyardLevel = 0;
            UnitManager.Instance?.NotifyBuildChanged();
        }

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
            float eff = p.OutputMult;   // siting x tech level
            if (info.metalPerSec > 0f) PlayerEconomy.Add(ResourceType.Metal, info.metalPerSec * eff * TechEffects.OreYieldMult * dt);
            // Power plants get their Power Distribution bonus applied live, so a hub built later still
            // rewards the generators already sitting around it.
            if (info.energyPerSec > 0f)
                PlayerEconomy.Add(ResourceType.Energy, info.energyPerSec * eff * (1f + AdjacencyBonus(b, p)) * dt);
            if (info.waterPerSec > 0f) PlayerEconomy.Add(ResourceType.Water, info.waterPerSec * eff * dt);
        }
    }

    public static float ResearchPerSec(CelestialBody b)
    {
        float sum = 0f;
        foreach (var p in On(b)) sum += p.Info.researchPerSec * p.OutputMult;
        return sum;
    }

    public static float PopGrowthPerSec(CelestialBody b)
    {
        float sum = 0f;
        foreach (var p in On(b)) sum += p.Info.popGrowthPerSec * p.OutputMult;
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
