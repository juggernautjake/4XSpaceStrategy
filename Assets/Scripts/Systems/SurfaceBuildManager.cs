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
    /// Everything about "may this KIND of thing be built on this world at all" — ownership, settlement,
    /// tech, uniqueness, and the classes that are grown or upgraded into rather than placed.
    ///
    /// Split out of CanPlace so the DRAWN path can ask the same questions. Without it a painted
    /// footprint bypassed every one of these: a fusion reactor before researching fusion, a second
    /// shipyard, a capitol built from scratch, a factory on a world you do not own. Geometry is not
    /// included, because a drawn building has no authored footprint to test.
    public static bool CanPlaceType(CelestialBody b, SurfaceBuildingType t, out string why)
    {
        why = null;
        var info = SurfaceBuildingDatabase.Get(t);

        if (b == null) { why = "no world"; return false; }
        if (info == null) { why = "unknown structure"; return false; }
        if (b.owner != FactionManager.Player) { why = "this world isn't yours — claim it first"; return false; }
        if (!b.Surveyed) { why = "survey this world first"; return false; }

        if (!b.settled && !GameMode.DevMode)
        {
            why = b.habitability >= Colony.FoundThreshold
                ? "nobody lives here yet — settle it with a colony ship"
                : $"nobody lives here — terraform to {Colony.FoundThreshold:F0}% (now {b.habitability:F0}%), then settle it";
            return false;
        }

        if (!string.IsNullOrEmpty(info.requiredTech) && !GameMode.DevMode && !TechManager.IsResearched(info.requiredTech))
        {
            var tech = TechDatabase.Get(info.requiredTech);
            why = $"research {(tech != null ? tech.name : info.requiredTech)} first";
            return false;
        }

        if ((info.uniquePerWorld || (OneOfEachPerWorld && !info.allowMultiple)) && CountOf(b, t) > 0)
        { why = $"this world already has a {info.name.ToLower()}"; return false; }

        if (t == SurfaceBuildingType.PlanetCapitol)
        { why = "upgrade this world's Colony Ship Base into a capitol instead"; return false; }
        if (t == SurfaceBuildingType.ColonyShipBase && !GameMode.DevMode)
        { why = "a colony ship becomes this when it settles a world"; return false; }
        if (CityGrowth.IsSettlement(t) && !GameMode.DevMode)
        { why = "settlements grow on their own as the colony's population rises"; return false; }
        if (t == SurfaceBuildingType.SurfaceShipyard && b.shipyardLevel >= 1)
        { why = "this world already has a shipyard — upgrade its tier from the Production tab"; return false; }

        return true;
    }

    public static bool CanPlace(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation, out string why)
    {
        why = null;
        var info = SurfaceBuildingDatabase.Get(t);

        if (b == null) { why = "no world"; return false; }
        if (b.owner != FactionManager.Player) { why = "this world isn't yours — claim it first"; return false; }
        if (!b.Surveyed) { why = "survey this world first"; return false; }

        // SETTLED, not merely owned. Infrastructure needs people to build and run it, and a claim is a
        // flag on a rock — the home world's moons are yours from turn one and have nobody on them.
        // Checking ownership alone let you cover an airless moon in farms and factories staffed by
        // nobody, which is the same hole that gave those moons free cities.
        if (!b.settled && !GameMode.DevMode)
        {
            why = b.habitability >= Colony.FoundThreshold
                ? "nobody lives here yet — settle it with a colony ship"
                : $"nobody lives here — terraform to {Colony.FoundThreshold:F0}% (now {b.habitability:F0}%), then settle it";
            return false;
        }

        // TECH. The one gate that isn't about this world at all — a fusion reactor is unbuildable
        // everywhere until somebody works out fusion. Mirrors TerraformManager's check, down to naming
        // the technology rather than its id, because "research F2 first" is not a sentence.
        if (!string.IsNullOrEmpty(info.requiredTech) && !GameMode.DevMode && !TechManager.IsResearched(info.requiredTech))
        {
            var tech = TechDatabase.Get(info.requiredTech);
            why = $"research {(tech != null ? tech.name : info.requiredTech)} first";
            return false;
        }

        // ONE OF EACH per world, for now: place a second and it's refused. (Some types — the capitol,
        // the shipyard — are inherently unique; the rest are capped here while the surface economy is
        // still being tuned. Relax this by dropping the OneOfEachPerWorld check.)
        //
        // Power infrastructure opts out via allowMultiple. A tuning cap on how many mines a world may
        // have is one thing; applied to relays it would reduce the power grid to a single node and
        // delete the entire mechanic, so infrastructure meant to be chained is exempt by construction.
        // Note the shape: uniquePerWorld is absolute and allowMultiple cannot override it, because a
        // second capitol is wrong for reasons that have nothing to do with economy tuning.
        if ((info.uniquePerWorld || (OneOfEachPerWorld && !info.allowMultiple)) && CountOf(b, t) > 0)
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

        // Power, from the placing player's point of view: what it will feed the grid, what it will take
        // out of it, and how far it will carry it. Quoted here rather than only on the card because
        // this is the readout under the cursor, and "will this reach?" is a question about a SPOT.
        //
        // The lookup walks the whole FOOTPRINT, exactly as the real connection rule does. Asking about
        // the origin cell alone would tell you "no grid here" for a four-tile plant whose origin sits
        // one tile off the light — and then power it fully the moment you placed it anyway.
        if (info.powerRange > 0f) parts.Add($"lights {info.powerRange:0.#} tiles");
        if (info.powerDraw > 0f || info.powerStorage > 0f)
        {
            var net = PowerGrid.NetForFootprint(b, t, x, y, rotation);
            string what = info.powerDraw > 0f ? $"draws {info.powerDraw:0.0}" : $"banks {info.powerStorage:0}";
            // A capacitor gets the same warning as a consumer. It draws nothing, so it would otherwise
            // sit off the grid quietly doing nothing at all, with the card still cheerfully quoting the
            // bank it isn't providing.
            parts.Add(net == null
                ? $"<color=#FF6659>{what} — no grid reaches here</color>"
                : net.Dead
                    ? $"<color=#FFBF4D>{what} — Grid {net.index} has no plant on it</color>"
                    : $"{what} · Grid {net.index}");
        }

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
        PowerGrid.Invalidate();   // this may have just joined two grids into one

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

    // ============================================================================================
    // PLACE A DRAWN FOOTPRINT
    //
    // The completion half of a build job: the cells were painted by the player, validated and paid for
    // when the job was queued, and the building goes up now that the work is done. So no cost is taken
    // here — charging again at completion would charge twice for one building.
    //
    // Efficiency is averaged over the cells ACTUALLY drawn and locked in, exactly as it always was for
    // an authored footprint: a mine drawn across a rich seam pays forever, and one drawn across dead
    // rock is a permanent mistake. Drawing does not change that rule, it just lets the player choose
    // which ground the rule applies to.
    // ============================================================================================
    public static PlacedBuilding PlaceDrawn(CelestialBody b, SurfaceBuildingType t, List<Vector2Int> cells)
    {
        if (b?.surface == null || cells == null || cells.Count == 0) return null;

        var info = SurfaceBuildingDatabase.Get(t);
        if (info == null) return null;

        // Re-checked at COMPLETION, not just at queue time. A build takes real time, and the ground can
        // change underneath it — another building finishing on the same tiles, an earthquake, a
        // terraforming project flooding the site. Refusing here is better than two buildings on one cell.
        var occupied = Occupied(b);
        foreach (var c in cells)
        {
            if (!InBounds(b, c.x, c.y)) return null;
            if (!CellBuildable(b, info, c.x, c.y, out _)) return null;
            if (occupied.Contains(c)) return null;
        }

        if (b.placedBuildings == null) b.placedBuildings = new List<PlacedBuilding>();

        // A building that does not CARE about terrain is fully efficient anywhere — the same guard
        // EfficiencyAt carries. Without it every index-agnostic class (habitat, factory, spaceport,
        // storage, shipyard, and every reactor) would be born at efficiency 0 and produce nothing
        // forever, because efficiency is locked in at placement. A fusion reactor lighting a grid with
        // zero generation is the kind of bug that reads as "the power system is broken".
        float eff = 1f;
        if (info.index != SurfaceIndexKind.None)
        {
            float sum = 0f;
            foreach (var c in cells) sum += SurfaceIndex.Get(b, info.index, c.x, c.y);
            eff = Mathf.Clamp01(cells.Count > 0 ? sum / cells.Count : 0f);
        }

        var p = new PlacedBuilding { type = (int)t, rotation = 0, efficiency = eff };
        p.SetDrawnShape(cells);
        b.placedBuildings.Add(p);

        PowerGrid.Invalidate();      // this may have just joined two grids into one
        SurfaceLabor.Invalidate();   // ...and it may have just added to the workforce

        foreach (var c in cells)
            if (InBounds(b, c.x, c.y)) b.surface.tiles[c.x, c.y].occupied = true;

        if (t == SurfaceBuildingType.SurfaceShipyard)
        {
            b.shipyardLevel = Mathf.Max(1, b.shipyardLevel);
            UnitManager.Instance?.NotifyBuildChanged();
        }

        return p;
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
        PowerGrid.Invalidate();
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

    /// Make sure a settled world has its seat of government standing on the surface grid.
    ///
    /// A colony ship grounds itself into a Colony Ship Base when it settles a world, so worlds you
    /// colonise get one for free. TWO kinds of world never go through that path:
    ///
    ///   THE HOME WORLD — declared settled at generation (GalaxyGenerator), with people, a shipyard and
    ///                    a laboratory, but never a single structure on its surface grid.
    ///   OLD SAVES      — written before there was anything to place.
    ///
    /// That was harmless right up until the capitol started carrying the colony's founding reactor. Now
    /// a settled world with no seat is a settled world with NO POWER: every mine and factory on the
    /// empire's best world running at the unpowered floor, for reasons the player can't see and didn't
    /// cause. Hence an invariant rather than a fix in one place — "a settled world has a capitol" is
    /// true by construction, wherever the world came from.
    ///
    /// It places the CAPITOL, not a ship base: a world that has been settled since before the game
    /// began is an established world, not a landing site, and there is no grounded hull to represent.
    public static bool EnsureColonySeat(CelestialBody b)
    {
        if (b?.surface == null || !b.settled) return false;
        if (CountOf(b, SurfaceBuildingType.PlanetCapitol) > 0) return false;
        if (CountOf(b, SurfaceBuildingType.ColonyShipBase) > 0) return false;
        if (!FindSpot(b, SurfaceBuildingType.PlanetCapitol, out int x, out int y))
        {
            // Nowhere dry and clear to put it — an all-ocean world, or one built out to the waterline.
            // Say so: silently returning false would leave a settled world at the unpowered floor with
            // no capitol, no explanation, and nothing in the Power tab pointing at the cause.
            Debug.LogWarning($"EnsureColonySeat: no room for a capitol on {b.name} — it will have no " +
                             $"founding reactor, so its industry runs at the unpowered floor until a plant is built.");
            return false;
        }
        return ForcePlace(b, SurfaceBuildingType.PlanetCapitol, x, y, 0);
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
        PowerGrid.Invalidate();   // the new type may generate, draw or relay differently
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
        // A tier buys a node real REACH (powerRange scales with LevelMult), so this can join two grids
        // exactly as placing a new node between them would.
        PowerGrid.Invalidate();

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

    /// Demolish a structure. A voluntary teardown refunds most of its cost (the materials are still
    /// standing there); a structure LOST — flattened by an earthquake — refunds nothing, so callers that
    /// destroy rather than dismantle pass refund:false.
    public static void Demolish(CelestialBody b, PlacedBuilding p, bool refund = true)
    {
        if (b == null || p == null || b.placedBuildings == null) return;
        if (!b.placedBuildings.Remove(p)) return;

        foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            if (InBounds(b, c.x, c.y)) b.surface.tiles[c.x, c.y].occupied = false;

        // Losing a relay out of the middle of a chain is what splits one grid back into two — but only
        // once the derivation runs again, so it must not be allowed to answer from this frame's cache.
        PowerGrid.Invalidate();

        // Tearing down the world's shipyard takes its build power out of the pool with it.
        if (p.Type == SurfaceBuildingType.SurfaceShipyard && CountOf(b, SurfaceBuildingType.SurfaceShipyard) == 0)
        {
            b.shipyardLevel = 0;
            UnitManager.Instance?.NotifyBuildChanged();
        }

        if (refund && !GameMode.DevMode)
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
        // Power settles FIRST. Each grid spends its generation on its own load, banks or exports the
        // surplus, and records what fraction of demand it actually met — and that fraction is what
        // everything standing on it produces with. Ticking this after the outputs would pay every
        // building on last frame's supply, which is wrong on exactly the frames that matter: the one
        // where you switch a reactor on, and the one where you lose it.
        PowerGrid.Tick(b, dt);

        foreach (var p in On(b))
        {
            var info = p.Info;
            float eff = p.OutputMult * PowerGrid.PowerFactor(b, p);   // siting x tech level x power
            if (info.metalPerSec > 0f) PlayerEconomy.Add(ResourceType.Metal, info.metalPerSec * eff * TechEffects.OreYieldMult * dt);

            // A generator's output belongs to its GRID, and PowerGrid.Tick has already spent it on that
            // grid's load and sent whatever was left to the stockpile. Paying it out again here would
            // double-count it. Only a producer that no grid reaches is paid directly — it has nowhere
            // to put its power but the empire's books. (Every real plant lights its own ground, so in
            // practice this branch is for anything a future edit adds without a powerRange.)
            if (info.energyPerSec > 0f && PowerGrid.NetOf(b, p) == null)
                PlayerEconomy.Add(ResourceType.Energy, info.energyPerSec * p.OutputMult * (1f + AdjacencyBonus(b, p)) * dt);

            if (info.waterPerSec > 0f) PlayerEconomy.Add(ResourceType.Water, info.waterPerSec * eff * dt);
        }
    }

    public static float ResearchPerSec(CelestialBody b)
    {
        float sum = 0f;
        foreach (var p in On(b)) sum += p.Info.researchPerSec * p.OutputMult * PowerGrid.PowerFactor(b, p);
        return sum;
    }

    public static float PopGrowthPerSec(CelestialBody b)
    {
        float sum = 0f;
        foreach (var p in On(b)) sum += p.Info.popGrowthPerSec * p.OutputMult * PowerGrid.PowerFactor(b, p);
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
