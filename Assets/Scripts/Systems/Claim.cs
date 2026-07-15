using System.Collections.Generic;
using UnityEngine;

/// Where a world stands with you. Ordered: each stage implies the ones before it.
public enum WorldStage
{
    Unexplored,   // you know it's there and nothing else
    Surveyed,     // you've mapped it, but it's nobody's
    Claimed,      // legally yours. Nobody lives there.
    Settled,      // people live here
    Foreign       // somebody else's
}

// ============================================================================================
// CLAIMING AND SETTLING
//
// Two stages, deliberately separate, because they answer different questions and have different costs:
//
//   CLAIM   — "this world is mine." Survey it, have a ship there, plant a beacon, pay for it. Habitability
//             is IRRELEVANT: claiming a lifeless rock is the normal case, and the point. A claim is what
//             lets you spend the next hour terraforming a world without a rival taking it out from under
//             you.
//   SETTLE  — "people live here." Requires the world to actually be liveable, which for almost every
//             world means terraforming a claim you already hold. This is the real commitment.
//
// The order matters and it's the whole shape of the mid-game: claim the rock, terraform the rock, settle
// the rock. Anything that lets you skip to the end — a free city on a moon you happen to own — collapses
// that into nothing.
//
// WHY THIS EXISTS AS A FILE
// "Is this world settled?" used to be answered by asking about its side effects: cities > 0, or a City in
// `buildings`. Those are things that HAPPEN when you settle, not the fact of settling, and each caller
// picked a different one. ColonyManager.TickColony ran for every owner==Player body and opened with
// `if (b.cities < 1) b.cities = 1;` — so the home world's moons, which are Player-owned by birthright and
// meant to be bare rock, were handed a city on the first tick. Free, instant, no ship, no habitability
// check, no cost. The other half of the system asked politely for a colony ship and 40% habitability
// while this handed the same thing out for nothing.
// ============================================================================================
public static class Claim
{
    public static bool IsClaimed(CelestialBody b) => b != null && b.owner != null;
    public static bool IsMine(CelestialBody b) => b != null && b.owner == FactionManager.Player;
    public static bool IsSettled(CelestialBody b) => b != null && b.settled;

    public static WorldStage StageOf(CelestialBody b)
    {
        if (b == null) return WorldStage.Unexplored;
        if (b.owner != null && b.owner != FactionManager.Player) return WorldStage.Foreign;
        if (b.settled) return WorldStage.Settled;
        if (b.owner == FactionManager.Player) return WorldStage.Claimed;
        if (b.explorationProgress >= 1f) return WorldStage.Surveyed;
        return WorldStage.Unexplored;
    }

    public static string StageLabel(WorldStage s)
    {
        switch (s)
        {
            case WorldStage.Foreign: return "Foreign";
            case WorldStage.Settled: return "Settled";
            case WorldStage.Claimed: return "Claimed";
            case WorldStage.Surveyed: return "Surveyed";
            default: return "Unexplored";
        }
    }

    public static Color StageColor(WorldStage s)
    {
        switch (s)
        {
            case WorldStage.Foreign: return UITheme.Bad;
            case WorldStage.Settled: return UITheme.Good;
            case WorldStage.Claimed: return UITheme.Accent;
            case WorldStage.Surveyed: return UITheme.SubText;
            default: return UITheme.SubText;
        }
    }

    // ---- Claiming ----

    /// Empire tech level needed to claim this world.
    ///
    /// PER-WORLD, and this is what makes claiming a decision rather than a formality: a temperate rock is
    /// a flag and a beacon, but holding a claim on a gas giant or a world whose surface melts lead takes
    /// equipment you haven't invented yet. So the map opens up as you research, and the worlds you can
    /// take early are the ones near the top of your habitability range.
    ///
    /// Driven by how hostile the world is TO YOUR SPECIES (habitability already reads through their
    /// eyes), plus a flat surcharge for body types that are simply hard to stand on.
    public static int RequiredTechLevel(CelestialBody b)
    {
        if (b == null) return 1;
        if (b.birthrightClaim) return 1;

        // 0% habitability -> level 5; at/above the colonisation floor -> level 1.
        float hostile = 1f - Mathf.Clamp01(b.habitability / Mathf.Max(1f, Colony.FoundThreshold));
        int lvl = 1 + Mathf.RoundToInt(hostile * 4f);

        switch (b.type)
        {
            case CelestialBodyType.GasGiant: lvl += 3; break;   // nothing to plant a beacon ON
            case CelestialBodyType.VolcanicPlanet: lvl += 1; break;
            case CelestialBodyType.Asteroid: lvl += 1; break;
        }
        return Mathf.Clamp(lvl, 1, EmpireTech.BaseMaxLevel);
    }

    /// Metal cost of the claim beacon. Scales with the world — staking a gas giant is not staking a rock.
    public static int BeaconMetal(CelestialBody b)
        => b == null ? 0 : ColonyManager.DiscCost(30 + Mathf.RoundToInt(b.surfaceSize * 2.5f));

    public static int BeaconEnergy(CelestialBody b)
        => b == null ? 0 : ColonyManager.DiscCost(20 + Mathf.RoundToInt(b.surfaceSize * 1.5f));

    /// Everything that must be true to claim this world, each with its own plain-language state.
    public static List<ColonyObjective> ClaimConditions(CelestialBody b)
    {
        var list = new List<ColonyObjective>();
        if (b == null) return list;

        if (b.birthrightClaim)
        {
            list.Add(new ColonyObjective { label = "Homeworld birthright", done = true, detail = "yours from the start" });
            return list;
        }

        list.Add(new ColonyObjective
        {
            label = "Survey the world",
            done = b.explorationProgress >= 1f,
            detail = $"{b.explorationProgress * 100f:F0}%"
        });

        list.Add(new ColonyObjective
        {
            label = "Unclaimed",
            done = b.owner == null,
            detail = b.owner == null ? "nobody's" : FactionManager.OwnerLabel(b.owner)
        });

        bool ship = ShipPresent(b);
        list.Add(new ColonyObjective
        {
            label = "A ship in orbit",
            done = ship,
            detail = ship ? "present" : "send any ship here"
        });

        int need = RequiredTechLevel(b);
        list.Add(new ColonyObjective
        {
            label = $"Empire level {need}",
            done = EmpireTech.Level >= need,
            detail = EmpireTech.Level >= need ? $"level {EmpireTech.Level}" : $"level {EmpireTech.Level} — {Hostility(b)}"
        });

        int m = BeaconMetal(b), e = BeaconEnergy(b);
        bool afford = GameMode.DevMode || PlayerEconomy.CanAfford(m, e);
        list.Add(new ColonyObjective
        {
            label = "Claim beacon",
            done = afford,
            detail = $"{m} metal, {e} energy"
        });

        return list;
    }

    static string Hostility(CelestialBody b)
    {
        if (b.type == CelestialBodyType.GasGiant) return "no surface to stand a beacon on";
        if (b.habitability <= 5f) return "utterly hostile";
        if (b.habitability < Colony.FoundThreshold * 0.5f) return "deeply hostile";
        return "hostile";
    }

    public static bool CanClaim(CelestialBody b, out string reason)
    {
        reason = null;
        if (b == null) { reason = "no world"; return false; }
        if (b.owner == FactionManager.Player) { reason = "already yours"; return false; }

        foreach (var c in ClaimConditions(b))
            if (!c.done) { reason = $"{c.label.ToLowerInvariant()} — {c.detail}"; return false; }

        return true;
    }

    /// Stake the claim. Costs the beacon; grants ownership and nothing else.
    public static bool DoClaim(CelestialBody b)
    {
        if (!CanClaim(b, out _)) return false;

        int m = BeaconMetal(b), e = BeaconEnergy(b);
        if (!GameMode.DevMode && !PlayerEconomy.Spend(m, e)) return false;

        b.owner = FactionManager.Player;
        b.claimingFaction = null;
        b.visited = true;

        var target = b;
        SimpleAudio.Instance?.PlayNotify(NotifKind.Victory);
        NotificationManager.Instance?.Push($"{b.name} claimed",
            b.habitability >= Colony.FoundThreshold
                ? "The world is yours and already liveable — send a colony ship to settle it."
                : $"The world is yours. At {b.habitability:F0}% habitability nobody can live here yet — terraform it to " +
                  $"{Colony.FoundThreshold:F0}% before settling.",
            () => { if (target.visualObject != null) CameraController.Instance?.FocusAndZoom(target.visualObject.transform, target.surfaceSize, true); },
            NotifKind.Victory);
        return true;
    }

    /// Any of your ships at this world.
    public static bool ShipPresent(CelestialBody b)
    {
        if (b?.units == null) return false;
        foreach (var u in b.units) if (u != null && u.owner == FactionManager.Player) return true;
        return false;
    }

    // ---- Settling ----

    /// Everything that must be true before people can live here.
    public static List<ColonyObjective> SettleConditions(CelestialBody b)
    {
        var list = new List<ColonyObjective>();
        if (b == null) return list;

        list.Add(new ColonyObjective
        {
            label = "Claimed by you",
            done = b.owner == FactionManager.Player,
            detail = b.owner == FactionManager.Player ? "yours" : "claim it first"
        });

        // The gate the whole thing turns on. A claim is a flag; a settlement needs air.
        bool liveable = b.habitability >= Colony.FoundThreshold;
        list.Add(new ColonyObjective
        {
            label = $"Habitable ({Colony.FoundThreshold:F0}%+)",
            done = liveable,
            detail = liveable ? $"{b.habitability:F0}%"
                   : Colony.CanReachLivable(b)
                       ? $"{b.habitability:F0}% — terraform it (ceiling {Colony.TerraformCeiling(b):F0}%)"
                       : $"{b.habitability:F0}% — can never be made liveable for your species"
        });

        bool colony = ColonyShipPresent(b);
        list.Add(new ColonyObjective
        {
            label = "Colony ship in orbit",
            done = colony,
            detail = colony ? "ready to land" : "send one"
        });

        return list;
    }

    public static bool CanSettle(CelestialBody b, out string reason)
    {
        reason = null;
        if (b == null) { reason = "no world"; return false; }
        if (b.settled) { reason = "already settled"; return false; }

        foreach (var c in SettleConditions(b))
            if (!c.done) { reason = $"{c.label.ToLowerInvariant()} — {c.detail}"; return false; }

        return true;
    }

    public static bool ColonyShipPresent(CelestialBody b)
    {
        if (b?.units == null) return false;
        foreach (var u in b.units)
            if (u != null && u.owner == FactionManager.Player && u.Info.canColonize) return true;
        return false;
    }
}
