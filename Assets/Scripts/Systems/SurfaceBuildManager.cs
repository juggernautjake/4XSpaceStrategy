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
    // THERE IS NO LONGER A ONE-OF-EACH CAP.
    //
    // A world used to hold exactly one of every structure — a testing measure from before the surface
    // economy existed, kept behind a `OneOfEachPerWorld` constant. It is gone, and its removal is not a
    // tuning change so much as the precondition for the whole drawing mechanic: a cap of one makes
    // "extend the farm you already have" and "site a second mine on the better seam" both impossible,
    // and it made the adjacency merging in this file unreachable by construction.
    //
    // The genuinely unique classes are still unique, via SurfaceBuildingInfo.uniquePerWorld — a second
    // capitol is wrong for reasons that have nothing to do with economy tuning, and that flag says so
    // per-class rather than as a blanket rule over everything.

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

        // A COLONY SHIP LANDING IS THE EXCEPTION TO TWO RULES HERE, and it has to be, because it is the
        // event that makes them true. The world is not settled — placing this hull is what settles it —
        // and the class is normally unbuildable further down precisely so a player cannot conjure one
        // from the build menu. While a landing is genuinely pending on THIS world, that one placement of
        // that one class is exactly what the game is asking the player for. See ColonyLanding.
        bool landing = ColonyLanding.AwaitingOn(b) && t == SurfaceBuildingType.ColonyShipBase;

        if (!b.settled && !landing && !GameMode.DevMode)
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

        if (info.uniquePerWorld && CountOf(b, t) > 0)
        { why = $"this world already has a {info.name.ToLower()}"; return false; }

        // SOLAR IS OFF THE MENU UNDER A THICK SKY.
        //
        // Each atmosphere above Earth-normal costs 20% of a panel's output, so at 5 the number is zero —
        // not "poor", zero. Offering a building that cannot produce anything is worse than hiding it: the
        // player pays, waits out the build, and gets a structure that does nothing, with no explanation.
        //
        // Phrased with the threshold in it because this is REVERSIBLE for NEW builds: thinning the air
        // back under the line puts solar on the menu again, which is the sort of consequence that makes
        // an atmosphere project worth running. Deliberately NOT promising it improves arrays already
        // standing — PlacedBuilding.efficiency is frozen at placement, so it does not, and saying so
        // would be a lie the player could check.
        if (t == SurfaceBuildingType.SolarArray && !SurfaceIndex.SolarViable(b) && !GameMode.DevMode)
        {
            why = $"the sky is too thick — at {b.atmospheres:0.#} atmospheres no sunlight reaches the ground. " +
                  $"Thin it below {SurfaceIndex.SolarDeadAtmospheres:0.#} and panels can be built here again";
            return false;
        }

        if (t == SurfaceBuildingType.PlanetCapitol)
        { why = "upgrade this world's Colony Ship Base into a capitol instead"; return false; }
        if (t == SurfaceBuildingType.ColonyShipBase && !landing && !GameMode.DevMode)
        { why = "a colony ship becomes this when it settles a world"; return false; }
        if (CityGrowth.IsSettlement(t) && !GameMode.DevMode)
        { why = "settlements grow on their own as the colony's population rises"; return false; }
        if (t == SurfaceBuildingType.SurfaceShipyard && b.shipyardLevel >= 1)
        { why = "this world already has a shipyard — upgrade its tier from the Production tab"; return false; }

        return true;
    }

    public static bool CanPlace(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation, out string why)
        => CanPlace(b, t, x, y, rotation, out why, false);

    /// As above, with the option to skip the "a relay must start from power" gate.
    ///
    /// Exactly one caller passes true: the node-chain drag, which is committing a RUN of pylons where
    /// each is supplied by the one before it. Those pylons do not exist yet, so the live grid cannot
    /// answer for them and the chain has to vouch for itself (see PlanetViewWindow's CommitDraw). Every
    /// other gate still applies — the exemption is narrow on purpose, because a general "skip the
    /// checks" flag is how a placement path quietly stops enforcing anything.
    public static bool CanPlace(CelestialBody b, SurfaceBuildingType t, int x, int y, int rotation,
                                out string why, bool ignoreNodePower)
    {
        why = null;
        var info = SurfaceBuildingDatabase.Get(t);

        if (b == null) { why = "no world"; return false; }
        if (b.owner != FactionManager.Player) { why = "this world isn't yours — claim it first"; return false; }
        if (!b.Surveyed) { why = "survey this world first"; return false; }

        // The landing exception, exactly as in CanPlaceType above — see the note there. Both methods
        // carry the same gates, so both need it; a landing legal in one and refused by the other would
        // show the player a placeable ghost that the click then silently declines to place.
        bool landing = ColonyLanding.AwaitingOn(b) && t == SurfaceBuildingType.ColonyShipBase;

        // SETTLED, not merely owned. Infrastructure needs people to build and run it, and a claim is a
        // flag on a rock — the home world's moons are yours from turn one and have nobody on them.
        // Checking ownership alone let you cover an airless moon in farms and factories staffed by
        // nobody, which is the same hole that gave those moons free cities.
        if (!b.settled && !landing && !GameMode.DevMode)
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

        // Only the genuinely unique classes are capped — see the note at the top of this file on why the
        // blanket one-of-each rule is gone.
        if (info.uniquePerWorld && CountOf(b, t) > 0)
        { why = $"this world already has a {info.name.ToLower()}"; return false; }

        // A Planet Capitol isn't built from scratch: it's what a Colony Ship Base becomes. Placing one
        // directly would leave the grounded ship sitting next to it with nothing to do.
        if (t == SurfaceBuildingType.PlanetCapitol)
        { why = "upgrade this world's Colony Ship Base into a capitol instead"; return false; }
        if (t == SurfaceBuildingType.ColonyShipBase && !landing && !GameMode.DevMode)
        { why = "a colony ship becomes this when it settles a world"; return false; }

        // Settlements/towns/cities are GROWN by the population (CityGrowth), never placed. You get one
        // capital; the rest is people housing themselves.
        if (CityGrowth.IsSettlement(t) && !GameMode.DevMode)
        { why = "settlements grow on their own as the colony's population rises"; return false; }

        // A world's shipyard already exists (the capital's birthright yard, say) — don't allow a second.
        if (t == SurfaceBuildingType.SurfaceShipyard && b.shipyardLevel >= 1)
        { why = "this world already has a shipyard — upgrade its tier from the Production tab"; return false; }

        // A RELAY HAS TO START FROM POWER. Checked here, per placement, rather than in CanPlaceType,
        // because unlike every other gate in that method this one is about the SPOT rather than about
        // the world: a node is perfectly buildable on this planet, just not in that particular desert.
        // See PowerGrid.CanPlantNodeAt.
        if (t == SurfaceBuildingType.PowerNode && !ignoreNodePower && !GameMode.DevMode &&
            !PowerGrid.CanPlantNodeAt(b, SurfaceBuildingDatabase.Footprint(t, x, y, rotation), out why))
            return false;

        var occupied = Occupied(b);

        // Ground a QUEUED job is holding is not free either, even though nothing stands on it yet.
        // `Occupied` only knows about buildings that exist, which is the right answer for it and the
        // wrong one here: a half-built factory's site is spoken for. Without this a fixed-footprint
        // structure — a spaceport, a shipyard — could be dropped straight on top of a construction site,
        // and the drawn building underneath it would reach completion, find its ground taken and refund
        // itself. The player would have watched a build run to 100% and then evaporate.
        var pending = SurfaceBuildQueue.PendingCells(b);

        foreach (var c in SurfaceBuildingDatabase.Footprint(t, x, y, rotation))
        {
            if (!CellBuildable(b, info, c.x, c.y, out why)) return false;
            if (occupied.Contains(c)) { why = "something is already built here"; return false; }
            if (pending.Contains(c)) { why = "another project is already going up here"; return false; }
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
        // Only if it actually PROJECTS. powerRange survives on classes that no longer light anything
        // beyond their own footprint, and quoting "lights 3 tiles" for a switchyard that lights none
        // would be the card contradicting the map.
        if (PowerGrid.Projects(info)) parts.Add($"lights {info.powerRange:0.#} tiles");
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

        SyncFacilityTiers(b);

        SimpleAudio.Instance?.PlayClick();
        return true;
    }

    // ============================================================================================
    // A WORLD'S SHIPYARD AND LABORATORY TIERS ARE PROPERTIES OF WHAT IS STANDING ON IT
    //
    // `shipyardLevel` and `researchCenterLevel` are read all over the game — build power, research
    // capacity, which hulls may be laid down, how many ore samples a world can study. They used to be
    // raised by an ABSTRACT facility: a word added to a list by a button, with nothing on the map. That
    // whole system is gone (see the note in Building.cs), so the tiers now follow the structures.
    //
    // ONE PLACE, not four. Place, place-drawn, upgrade and demolish each used to raise or clear the yard
    // tier by hand, and a fifth call site that forgot would leave a world claiming a shipyard it no
    // longer has. This is that one place, and it now covers the laboratory too.
    //
    // ---- THE TWO LADDERS ARE NOT THE SAME LADDER, and this must not conflate them ----
    //
    // A PlacedBuilding's `level` runs 1..3 (PlacedBuilding.MaxLevel) and is the STRUCTURE's tech tier.
    // A world's shipyardLevel runs 1..5 (Colony.MaxShipyardLevel) and is the FACILITY's tier, upgraded
    // from the Orbit tab through ColonyManager's own cost table. Copying one into the other would cap a
    // level-5 yard at 3 and silently delete two upgrades the player paid for.
    //
    // So the rule is deliberately weak in one direction and strong in the other:
    //   A STRUCTURE EXISTS  -> the world has at least tier 1, and at least the structure's own tier.
    //                          Never lowered, so the facility ladder above 3 is untouched.
    //   THE LAST ONE GOES   -> the tier is cleared, but only on a world whose tier came from a structure
    //                          in the first place (see hadSurfaceFacility). A save written before these
    //                          buildings existed carries the numbers and no structures, and clearing
    //                          strictly from the map would strip the capital of both on load.
    // ============================================================================================
    public static void SyncFacilityTiers(CelestialBody b)
    {
        if (b == null) return;

        int yard = 0, lab = 0;
        foreach (var p in On(b))
        {
            if (p.Type == SurfaceBuildingType.SurfaceShipyard) yard = Mathf.Max(yard, p.level);
            if (p.Type == SurfaceBuildingType.ResearchCenter) lab = Mathf.Max(lab, p.level);
        }

        int wasYard = b.shipyardLevel, wasLab = b.researchCenterLevel;

        if (yard > 0)
        {
            b.shipyardLevel = Mathf.Clamp(Mathf.Max(b.shipyardLevel, yard), 1, Colony.MaxShipyardLevel);
            NoteSurfaceFacility(b, SurfaceBuildingType.SurfaceShipyard);
        }
        else if (HadSurfaceFacility(b, SurfaceBuildingType.SurfaceShipyard)) b.shipyardLevel = 0;

        if (lab > 0)
        {
            b.researchCenterLevel = Mathf.Clamp(Mathf.Max(b.researchCenterLevel, lab), 1, Colony.MaxResearchCenterLevel);
            NoteSurfaceFacility(b, SurfaceBuildingType.ResearchCenter);
        }
        else if (HadSurfaceFacility(b, SurfaceBuildingType.ResearchCenter)) b.researchCenterLevel = 0;

        if (b.shipyardLevel != wasYard) UnitManager.Instance?.NotifyBuildChanged();
        if (b.researchCenterLevel != wasLab) TechManager.NotifyChanged();
    }

    // Worlds whose tier came from a structure they have since lost. Without this the "leave a declared
    // tier alone" rule above would also leave a DEMOLISHED one alone, and tearing a shipyard down would
    // keep its build power in the empire pool forever.
    //
    // A HashSet rather than a field on CelestialBody: it is a fact about this session's edits, not about
    // what the world is, and adding a serialized field to the most over-subscribed type in the project
    // to record "you used to have a shipyard here" is not worth a save-format change. A world missing
    // from it on load simply keeps whatever tier the save recorded, which is the correct answer.
    static readonly HashSet<(CelestialBody, SurfaceBuildingType)> hadSurfaceFacility
        = new HashSet<(CelestialBody, SurfaceBuildingType)>();

    static bool HadSurfaceFacility(CelestialBody b, SurfaceBuildingType t)
        => hadSurfaceFacility.Contains((b, t));

    static void NoteSurfaceFacility(CelestialBody b, SurfaceBuildingType t)
    {
        if (t == SurfaceBuildingType.SurfaceShipyard || t == SurfaceBuildingType.ResearchCenter)
            hadSurfaceFacility.Add((b, t));
    }

    /// Drop the "used to have one" record. Called when the galaxy these worlds belong to is replaced.
    public static void ForgetFacilityHistory() => hadSurfaceFacility.Clear();

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

        SyncFacilityTiers(b);

        return p;
    }

    // ============================================================================================
    // TWO OF THE SAME THING, TOUCHING, ARE ONE THING
    //
    // Draw a farm onto the edge of a farm and you have one farm, not two farms that happen to be
    // adjacent. That is the rule, and it is worth being precise about why it is a rule rather than a
    // convenience.
    //
    // WITHOUT IT the surface fills up with records. A player who extends their farmland four times ends
    // up with five PlacedBuildings, five entries in the built-here list, five efficiency figures to read
    // and compare, five things to select, and five separate applications of every per-building rule the
    // game has. The map shows one continuous field and the data says five farms. Every future feature
    // that asks a question about "a building" — an adjacency bonus, an upkeep, a worker requirement —
    // then has to decide which of those five it means, and there is no right answer.
    //
    // WITH IT, "one building" means what it looks like on the map, and extending is a first-class action
    // rather than a workaround.
    //
    // ---- WHAT MERGING ACTUALLY DOES TO THE NUMBERS ----
    //
    // EFFICIENCY IS AREA-WEIGHTED, not averaged and not replaced. A twenty-tile farm at 80% that gains
    // two tiles of 20% ground should barely move (to ~74%), and a two-tile farm at 80% that gains twenty
    // tiles of 20% ground should collapse (to ~25%). A plain average of the two figures gives 50% in
    // both cases, which is wrong in opposite directions and would make extending a good building onto
    // poor ground a catastrophe while extending a bad one onto good ground a free fix.
    //
    // THE LEVEL AND CONDITION ARE THE SURVIVOR'S. New tiles join an existing structure; they do not
    // upgrade it and they do not repair it. A level-3 farm that grows stays level 3.
    //
    // CHAIN MERGES ARE HANDLED. A drawn strip can bridge two standing farms that were not touching each
    // other, and then all three are one building — so this absorbs EVERY same-type neighbour of the
    // final shape, not just the one the player was aiming at.
    // ============================================================================================

    /// Every standing building of type `t` that touches any of `cells` edge-to-edge.
    public static List<PlacedBuilding> AdjacentSameType(CelestialBody b, SurfaceBuildingType t,
                                                        IEnumerable<Vector2Int> cells)
    {
        var found = new List<PlacedBuilding>();
        if (b == null || cells == null) return found;

        // The neighbourhood of the drawn shape, built once. Asking "is any cell of that building next to
        // any cell of my shape" the other way round is O(shape x building) per building; this is O(shape)
        // once and then O(building) per building.
        var near = new HashSet<Vector2Int>();
        foreach (var c in cells)
        {
            near.Add(c + Vector2Int.up);
            near.Add(c + Vector2Int.down);
            near.Add(c + Vector2Int.left);
            near.Add(c + Vector2Int.right);
        }

        foreach (var p in On(b))
        {
            if (p.Type != t) continue;
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
                if (near.Contains(c)) { found.Add(p); break; }
        }
        return found;
    }

    /// Can this class merge at all?
    ///
    /// ONLY FREE-DRAWN CLASSES, which is a stronger rule than it first looks and is forced by the shape
    /// families. A merged footprint is the union of two shapes, and a union is not generally a member of
    /// the family either of them belonged to:
    ///
    ///   SQUARE      a 3x3 reactor with two tiles stuck on the side is not a square. Offering the merge
    ///               would mean either breaking the class's own shape rule or refusing the extension
    ///               after the player had drawn it — and "you may extend this, but only in ways that
    ///               happen to remain a perfect square" is not a rule anyone can hold in their head.
    ///   RECTANGLE   the same, for the same reason.
    ///   FIXED       a 3x3 spaceport that is now 3x3 plus a lump is not a spaceport.
    ///   NODECHAIN   a pylon is a mast. Two pylons side by side are two relays, and fusing them into one
    ///               two-tile relay would silently halve a chain's node count and its reach.
    ///
    /// Free is exactly the family with no shape constraint beyond "one connected piece", which a union
    /// of two touching connected pieces always is. So the merge is closed over Free and over nothing
    /// else — and Free is the resource-generator case the whole extend-your-farm idea is about anyway.
    ///
    /// The GROWN settlements are excluded separately: CityGrowth places them one at a time and fusing
    /// them would turn a spreading town into a single enormous "Settlement" record whose next growth
    /// tick has nothing recognisable to upgrade. The unique classes, because there is only ever one.
    public static bool CanMerge(SurfaceBuildingType t)
    {
        var info = SurfaceBuildingDatabase.Get(t);
        if (info == null) return false;
        if (info.uniquePerWorld) return false;
        if (info.drawMode != BuildDrawMode.Free) return false;
        if (CityGrowth.IsSettlement(t)) return false;
        return true;
    }

    /// Fold `cells` — and every same-type building they touch — into one structure, and return it.
    ///
    /// `into` is the building the player aimed at, if they were expanding a specific one; null lets the
    /// adjacency search pick. Either way the SURVIVOR is the largest participant, so the merged building
    /// keeps the identity of the thing that most of it already was.
    public static PlacedBuilding AbsorbInto(CelestialBody b, SurfaceBuildingType t,
                                            List<Vector2Int> cells, PlacedBuilding into)
    {
        if (b?.surface == null || cells == null || cells.Count == 0) return null;

        var neighbours = AdjacentSameType(b, t, cells);

        // The building the player was pointing at counts even if the final shape drifted away from it —
        // but only if it is still standing and really is adjacent, which AdjacentSameType has just
        // decided. A stale `into` (demolished mid-build) simply drops out here.
        if (into != null && !neighbours.Contains(into)) into = null;

        if (neighbours.Count == 0) return null;   // nothing to merge with; caller places a new building

        // The survivor: the biggest, so the merged record inherits the identity of the bulk of itself.
        // Ties broken by the first in the list, which is stable because On(b) is a stable list.
        PlacedBuilding keep = into;
        foreach (var p in neighbours)
            if (keep == null || p.TileCount > keep.TileCount) keep = p;

        // ---- Gather every cell the merged building will occupy ----
        var merged = new List<Vector2Int>();
        var seen = new HashSet<Vector2Int>();

        // The survivor's own cells first, so its origin stays its origin (SetDrawnShape takes the first
        // cell as the origin, and everything that draws a marker or reads a position uses it).
        foreach (var c in SurfaceBuildingDatabase.Footprint(keep))
            if (seen.Add(c)) merged.Add(c);

        // Area-weighted efficiency, accumulated as (efficiency x tiles) over every participant.
        float effWeighted = keep.efficiency * keep.TileCount;
        int effTiles = keep.TileCount;

        foreach (var p in neighbours)
        {
            if (p == keep) continue;
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
                if (seen.Add(c)) merged.Add(c);
            effWeighted += p.efficiency * p.TileCount;
            effTiles += p.TileCount;

            // BANKED CHARGE MOVES WITH THE TILES. Two capacitor banks merging into one is the same
            // hardware in the same place — deleting the absorbed one's charge would mean a player who
            // joined up their bank storage watched a chunk of their reserve vanish for tidying. Clamped
            // below, because the survivor's capacity may be smaller than the sum of the two banks.
            keep.stored += p.stored;

            b.placedBuildings.Remove(p);
        }

        // The newly drawn cells, with their own efficiency read from the ground they sit on.
        var info = SurfaceBuildingDatabase.Get(t);
        float newEff = 1f;
        if (info.index != SurfaceIndexKind.None)
        {
            float sum = 0f;
            foreach (var c in cells) sum += SurfaceIndex.Get(b, info.index, c.x, c.y);
            newEff = Mathf.Clamp01(sum / cells.Count);
        }

        int added = 0;
        foreach (var c in cells) if (seen.Add(c)) { merged.Add(c); added++; }
        effWeighted += newEff * added;
        effTiles += added;

        keep.SetDrawnShape(merged);
        keep.efficiency = effTiles > 0 ? Mathf.Clamp01(effWeighted / effTiles) : keep.efficiency;

        // The charge carried over from absorbed banks, capped at what the survivor can actually hold.
        // (Zero for everything that is not a capacitor, so this is a no-op for every other class.)
        float cap = keep.Info.powerStorage * keep.LevelMult;
        if (keep.stored > cap) keep.stored = cap;

        foreach (var c in merged)
            if (InBounds(b, c.x, c.y)) b.surface.tiles[c.x, c.y].occupied = true;

        // The merged building is bigger, so it generates, draws and reaches differently — and the
        // buildings that were absorbed are gone, which on its own can change the grid's topology.
        PowerGrid.Invalidate();
        SurfaceLabor.Invalidate();
        SyncFacilityTiers(b);

        // The selection may have been pointing at one of the records that no longer exists.
        SurfaceSelection.Validate();

        return keep;
    }

    /// Where a NEW building of this type could be grown onto something already standing.
    ///
    /// Every empty, buildable cell touching a same-type structure — the "you may extend this one instead
    /// of starting another" highlight that appears the moment a class is picked in the build tray. It is
    /// the same question Placement Mode's guidance grids answer once you have started drawing, asked
    /// before you have started.
    public static HashSet<Vector2Int> ExpansionSites(CelestialBody b, SurfaceBuildingType t)
    {
        var sites = new HashSet<Vector2Int>();
        if (b?.surface == null || !CanMerge(t)) return sites;

        var info = SurfaceBuildingDatabase.Get(t);
        var occupied = Occupied(b);
        var pending = SurfaceBuildQueue.PendingCells(b);

        foreach (var p in On(b))
        {
            if (p.Type != t) continue;
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            {
                TryExpansionSite(b, info, c + Vector2Int.up, occupied, pending, sites);
                TryExpansionSite(b, info, c + Vector2Int.down, occupied, pending, sites);
                TryExpansionSite(b, info, c + Vector2Int.left, occupied, pending, sites);
                TryExpansionSite(b, info, c + Vector2Int.right, occupied, pending, sites);
            }
        }
        return sites;
    }

    static void TryExpansionSite(CelestialBody b, SurfaceBuildingInfo info, Vector2Int c,
                                 HashSet<Vector2Int> occupied, HashSet<Vector2Int> pending,
                                 HashSet<Vector2Int> into)
    {
        if (!InBounds(b, c.x, c.y)) return;
        if (occupied.Contains(c) || pending.Contains(c)) return;
        if (!CellBuildable(b, info, c.x, c.y, out _)) return;
        into.Add(c);
    }

    /// The standing building of type `t` that this cell would extend, or null.
    /// Used to turn a click on an expansion site into an expansion of the right structure.
    public static PlacedBuilding ExpansionTargetAt(CelestialBody b, SurfaceBuildingType t, Vector2Int cell)
    {
        if (!CanMerge(t)) return null;
        var touching = AdjacentSameType(b, t, new[] { cell });
        // The biggest, matching AbsorbInto's survivor rule — so the building the player is told they are
        // extending is the one the merge will actually keep.
        PlacedBuilding best = null;
        foreach (var p in touching) if (best == null || p.TileCount > best.TileCount) best = p;
        return best;
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
        SyncFacilityTiers(b);
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

    // ============================================================================================
    // A DECLARED FACILITY GETS A REAL BUILDING
    //
    // The same invariant EnsureColonySeat enforces for the seat of government, applied to the two
    // facilities that used to exist only as numbers. The home world is DECLARED to have a shipyard and a
    // laboratory at generation — that is what lets the player build a ship and research anything on turn
    // one — and until now neither had a structure anywhere on the map. You could not look at your
    // capital and see its yard, could not select it, could not lose it, and could not choose where it
    // sat. The Production tab said "Shipyard: Level 1" and that was the whole of it.
    //
    // So: if a world claims a tier and has no structure carrying it, put the structure down. This is not
    // a grant — it is the missing half of one that already happened — and from here on the world has a
    // building that can be selected, damaged and demolished like any other, which is the whole point.
    //
    // Also the repair path for every save written before this: an old capital loads with its numbers,
    // gets its yard and campus placed on the first spare ground, and from then on behaves like a world
    // that built them.
    //
    // THE WORLD'S TIER IS NOT TOUCHED. SyncFacilityTiers only ever raises it (see the note there, on why
    // the 1..3 structure ladder and the 1..5 facility ladder are not the same ladder), so a capital with
    // a level-4 yard gets a building for it and keeps the 4.
    public static void EnsureFoundingFacilities(CelestialBody b)
    {
        if (b?.surface == null || !b.settled) return;
        EnsureFacility(b, SurfaceBuildingType.SurfaceShipyard, b.shipyardLevel);
        EnsureFacility(b, SurfaceBuildingType.ResearchCenter, b.researchCenterLevel);
    }

    static void EnsureFacility(CelestialBody b, SurfaceBuildingType t, int level)
    {
        if (level < 1) return;                 // the world doesn't claim one
        if (CountOf(b, t) > 0) return;         // ...and if it does, it already has one standing

        if (!FindSpot(b, t, out int x, out int y))
        {
            // No room. Unlike the capitol this is not a crisis — the tier stays declared and everything
            // that reads it keeps working — so this is a warning about a missing MODEL, not about a
            // broken colony. Said out loud anyway, because "my shipyard has no building" is otherwise
            // indistinguishable from a bug.
            Debug.LogWarning($"EnsureFoundingFacilities: no room on {b.name} for its {SurfaceBuildingDatabase.Get(t).name} " +
                             $"— the world keeps tier {level}, but there is no structure on the map for it.");
            return;
        }

        if (!ForcePlace(b, t, x, y, 0)) return;

        // The structure comes up at the closest tier it can express to the world's. A level-5 yard's
        // building is a level-3 building, because that is the top of the structure ladder — the world
        // keeps its 5, and the model on the map is simply the biggest one there is.
        var placed = FirstOf(b, t);
        if (placed != null) placed.level = Mathf.Clamp(level, 1, PlacedBuilding.MaxLevel);
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

        // A shipyard's tier IS the world's shipyard tier, and a campus's is its laboratory tier —
        // upgrading the structure upgrades the facility.
        SyncFacilityTiers(b);

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

        // Tearing down the world's shipyard takes its build power out of the empire pool with it, and
        // tearing down its campus takes the research capacity. Both fall out of the re-derivation.
        SyncFacilityTiers(b);

        if (refund && !GameMode.DevMode)
        {
            var info = p.Info;
            PlayerEconomy.Add(ResourceType.Metal, ColonyManager.DiscCost(info.costMetal) * 0.6f);
            PlayerEconomy.Add(ResourceType.Energy, ColonyManager.DiscCost(info.costEnergy) * 0.6f);
        }
    }

    // ============================================================================================
    // TAKING TILES OFF A BUILDING
    //
    // Demolition used to be all-or-nothing: a building came down whole. Now that a building can be
    // twenty tiles the player drew and extended over an hour, that is far too blunt — the useful verb is
    // "take those four tiles back", not "lose the farm".
    //
    // WHICH MAKES SPLITTING POSSIBLE, and splitting is the interesting case. Remove the waist of an
    // hourglass-shaped farm and what is left is two farms that do not touch. There is no honest way to
    // keep calling that one building: its footprint would be disconnected, which every other rule in
    // this file forbids, and the merge rule that produced it in the first place says two pieces that do
    // not touch are two buildings.
    //
    // So a split really does produce several buildings — and because that is a consequence the player
    // cannot see coming from the tiles they clicked, the UI asks a second time before doing it (see
    // PlanetViewWindow's demolition confirm). This file's job is to make the outcome PREDICTABLE:
    // WouldSplitInto answers the question before the fact, using exactly the same flood fill the
    // demolition itself uses, so the number in the warning is the number of buildings you get.
    // ============================================================================================

    /// The connected pieces `cells` would fall into, moving only edge-to-edge. One piece = no split.
    public static List<List<Vector2Int>> ConnectedPieces(IEnumerable<Vector2Int> cells)
    {
        var remaining = new HashSet<Vector2Int>(cells);
        var pieces = new List<List<Vector2Int>>();

        while (remaining.Count > 0)
        {
            Vector2Int start = default;
            foreach (var c in remaining) { start = c; break; }

            var piece = new List<Vector2Int>();
            var stack = new Stack<Vector2Int>();
            stack.Push(start);
            remaining.Remove(start);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                piece.Add(cur);
                TryPop(remaining, stack, cur + Vector2Int.up);
                TryPop(remaining, stack, cur + Vector2Int.down);
                TryPop(remaining, stack, cur + Vector2Int.left);
                TryPop(remaining, stack, cur + Vector2Int.right);
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    static void TryPop(HashSet<Vector2Int> remaining, Stack<Vector2Int> stack, Vector2Int c)
    {
        if (remaining.Remove(c)) stack.Push(c);
    }

    /// How many separate buildings `p` would become if `removing` were taken off it.
    ///
    /// 0 means nothing would be left — the whole structure comes down. 1 is the ordinary case. 2 or more
    /// is the split the player has to be warned about.
    public static int WouldSplitInto(PlacedBuilding p, HashSet<Vector2Int> removing)
    {
        if (p == null) return 0;
        var left = new List<Vector2Int>();
        foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            if (removing == null || !removing.Contains(c)) left.Add(c);
        if (left.Count == 0) return 0;
        return ConnectedPieces(left).Count;
    }

    /// Take `removing` off the buildings it covers, splitting or destroying them as the geometry demands.
    ///
    /// Returns how many tiles actually came down, so the caller can refund and report honestly.
    ///
    /// REFUNDS ARE PER TILE, at the same 60% a whole teardown gives back. A building's price scales
    /// super-linearly with its size (BuildScaling.CostMultiplier), so refunding a fixed fraction of the
    /// authored cost per tile would pay back the cheap first tile's price for a tile that was bought at
    /// the expensive end of the curve. Instead the refund is the DIFFERENCE between what the building
    /// costs at its current size and what it costs at its new one, which is exactly the marginal price
    /// of the tiles being removed and cannot be farmed in either direction.
    public static int DemolishCells(CelestialBody b, HashSet<Vector2Int> removing, bool refund = true)
    {
        if (b?.surface == null || removing == null || removing.Count == 0) return 0;

        // Which buildings are touched, resolved up front: the loop below mutates b.placedBuildings.
        var touched = new List<PlacedBuilding>();
        foreach (var p in On(b))
        {
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
                if (removing.Contains(c)) { touched.Add(p); break; }
        }
        if (touched.Count == 0) return 0;

        int removed = 0;
        float refundMetal = 0f, refundEnergy = 0f;

        foreach (var p in touched)
        {
            var own = SurfaceBuildingDatabase.Footprint(p);
            var keep = new List<Vector2Int>();
            int lost = 0;
            foreach (var c in own)
                if (removing.Contains(c)) lost++;
                else keep.Add(c);

            if (lost == 0) continue;
            removed += lost;

            var info = p.Info;
            if (refund && !GameMode.DevMode)
            {
                float before = BuildScaling.CostMultiplier(own.Count);
                float after = keep.Count > 0 ? BuildScaling.CostMultiplier(keep.Count) : 0f;
                float delta = Mathf.Max(0f, before - after);
                refundMetal += ColonyManager.DiscCost(info.costMetal) * delta * DemolishRefund;
                refundEnergy += ColonyManager.DiscCost(info.costEnergy) * delta * DemolishRefund;
            }

            // Free the ground either way.
            foreach (var c in own)
                if (removing.Contains(c) && InBounds(b, c.x, c.y)) b.surface.tiles[c.x, c.y].occupied = false;

            if (keep.Count == 0)
            {
                // Nothing left: the structure is gone. Demolish() rather than a bare Remove, so the
                // facility tiers, the power grid and the shipyard pool all learn about it — but with
                // refund:false, because this method has already accounted for every tile.
                Demolish(b, p, refund: false);
                continue;
            }

            var pieces = ConnectedPieces(keep);

            // The largest surviving piece stays THIS building — same record, so its level, condition,
            // banked charge and its place in every list are preserved. Anything else becomes a new
            // structure of the same class, inheriting the level and condition but starting fresh as its
            // own record, which is what a piece that no longer touches the original actually is.
            pieces.Sort((x, y) => y.Count.CompareTo(x.Count));

            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];

                // Efficiency is re-read from the ground each piece actually sits on rather than
                // inherited. The old figure was the average over a footprint that no longer exists, and
                // the whole point of splitting is that the pieces are in different places.
                float eff = 1f;
                if (info.index != SurfaceIndexKind.None)
                {
                    float sum = 0f;
                    foreach (var c in piece) sum += SurfaceIndex.Get(b, info.index, c.x, c.y);
                    eff = Mathf.Clamp01(sum / piece.Count);
                }

                if (i == 0)
                {
                    p.SetDrawnShape(piece);
                    p.efficiency = eff;
                    float cap = info.powerStorage * p.LevelMult;
                    if (p.stored > cap) p.stored = cap;      // a smaller bank holds less
                }
                else
                {
                    var split = new PlacedBuilding
                    {
                        type = p.type,
                        rotation = 0,
                        efficiency = eff,
                        level = p.level,
                        health = p.health
                    };
                    split.SetDrawnShape(piece);
                    b.placedBuildings.Add(split);
                }
            }
        }

        if (refund && !GameMode.DevMode)
        {
            PlayerEconomy.Add(ResourceType.Metal, refundMetal);
            PlayerEconomy.Add(ResourceType.Energy, refundEnergy);
        }

        PowerGrid.Invalidate();
        SurfaceLabor.Invalidate();
        SyncFacilityTiers(b);
        SurfaceSelection.Validate();
        return removed;
    }

    /// What a voluntary teardown gives back, as a fraction of what was paid. The materials are still
    /// standing there; you just don't get all of them back off a demolition site.
    public const float DemolishRefund = 0.6f;

    /// What tearing `removing` off this world would refund. Quoted on the confirm panel, and derived by
    /// the same arithmetic DemolishCells uses so the figure shown is the figure paid.
    public static void DemolishRefundFor(CelestialBody b, HashSet<Vector2Int> removing,
                                         out int metal, out int energy)
    {
        metal = energy = 0;
        if (b == null || removing == null || removing.Count == 0 || GameMode.DevMode) return;

        float m = 0f, e = 0f;
        foreach (var p in On(b))
        {
            var own = SurfaceBuildingDatabase.Footprint(p);
            int lost = 0;
            foreach (var c in own) if (removing.Contains(c)) lost++;
            if (lost == 0) continue;

            var info = p.Info;
            float before = BuildScaling.CostMultiplier(own.Count);
            float after = own.Count - lost > 0 ? BuildScaling.CostMultiplier(own.Count - lost) : 0f;
            float delta = Mathf.Max(0f, before - after);
            m += ColonyManager.DiscCost(info.costMetal) * delta * DemolishRefund;
            e += ColonyManager.DiscCost(info.costEnergy) * delta * DemolishRefund;
        }
        metal = Mathf.RoundToInt(m);
        energy = Mathf.RoundToInt(e);
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
