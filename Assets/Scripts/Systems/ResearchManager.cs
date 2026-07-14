using System;
using System.Collections.Generic;
using UnityEngine;

// Tracks which ores the player has discovered (encountered in the field) and which have been
// fully researched (unlocking their uses / refining notes). Research spends research points, which
// are earned by discovering ores and exploring anomalies. State is part of the save file.
public static class ResearchManager
{
    static readonly HashSet<OreType> discovered = new HashSet<OreType>();
    static readonly HashSet<OreType> researched = new HashSet<OreType>();

    public static int ResearchPoints { get; private set; }

    public static event Action OnChanged;

    const int StartingPoints = 120;
    const int PointsPerDiscovery = 10;
    const int PointsPerExploration = 25;

    public static void NewGame()
    {
        discovered.Clear();
        researched.Clear();
        ResearchPoints = GameConfig.StartingResearchPoints;   // difficulty-scaled
        OnChanged?.Invoke();
    }

    public static bool IsDiscovered(OreType ore) => discovered.Contains(ore);
    public static bool IsResearched(OreType ore) => researched.Contains(ore);

    public static IEnumerable<OreType> Discovered => discovered;

    // Returns true if this was a newly discovered ore.
    public static bool Discover(OreType ore)
    {
        if (ore == OreType.None) return false;
        if (discovered.Add(ore))
        {
            ResearchPoints += PointsPerDiscovery;
            var info = OreDatabase.Get(ore);
            NotificationManager.Instance?.Push($"New ore discovered: {info.displayName}", info.description, null, NotifKind.Discovery);
            OnChanged?.Invoke();
            return true;
        }
        return false;
    }

    public static bool CanResearch(OreType ore)
    {
        return ore != OreType.None
            && discovered.Contains(ore)
            && !researched.Contains(ore)
            && ResearchPoints >= OreDatabase.Get(ore).researchCost;
    }

    public static bool Research(OreType ore)
    {
        if (!CanResearch(ore)) return false;
        ResearchPoints -= OreDatabase.Get(ore).researchCost;
        researched.Add(ore);
        OnChanged?.Invoke();
        return true;
    }

    // Complete research for free. Reserve this for things the player has genuinely already paid for in
    // another currency — finishing a timed field-research task at an anomaly, say. Facilities studying
    // ore samples must go through TryResearchSample instead, so their work costs points.
    public static void ForceResearch(OreType ore)
    {
        if (ore == OreType.None) return;
        discovered.Add(ore);
        researched.Add(ore);
        OnChanged?.Invoke();
    }

    // A research facility (a research ship, an orbital laboratory, or a colony's research centre)
    // studying an ore sample brought back from the field. This is REAL work and it is charged for:
    // the facility spends research points, and if the bank can't cover this ore's cost the sample sits
    // in the hold until it can. Rarer, higher-tier ores cost far more to crack.
    //
    // Returns false when it couldn't be afforded, so the caller can keep hold of the sample.
    public static bool TryResearchSample(OreType ore)
    {
        if (ore == OreType.None) return true;          // nothing to do; don't strand the sample
        discovered.Add(ore);
        if (researched.Contains(ore)) return true;     // already known — the sample is redundant

        int cost = OreDatabase.Get(ore).researchCost;
        if (ResearchPoints < cost) return false;       // can't afford it yet; hold the sample

        ResearchPoints -= cost;
        researched.Add(ore);
        var info = OreDatabase.Get(ore);
        NotificationManager.Instance?.Push($"Researched: {info.displayName}",
            $"{cost} research points spent. {info.uses}", null, NotifKind.Research);
        OnChanged?.Invoke();
        return true;
    }

    /// Can a facility afford to study this ore right now?
    public static bool CanAffordSample(OreType ore)
        => ore == OreType.None || researched.Contains(ore) || ResearchPoints >= OreDatabase.Get(ore).researchCost;

    public static void AwardExploration()
    {
        ResearchPoints += PointsPerExploration;
        OnChanged?.Invoke();
    }

    public static void AddPoints(int p)
    {
        ResearchPoints += p;
        OnChanged?.Invoke();
    }

    // ---- Save/Load bridge ----
    public static List<int> ExportDiscovered() { var l = new List<int>(); foreach (var o in discovered) l.Add((int)o); return l; }
    public static List<int> ExportResearched() { var l = new List<int>(); foreach (var o in researched) l.Add((int)o); return l; }

    public static void Import(List<int> disc, List<int> res, int points)
    {
        discovered.Clear();
        researched.Clear();
        if (disc != null) foreach (var i in disc) discovered.Add((OreType)i);
        if (res != null) foreach (var i in res) researched.Add((OreType)i);
        ResearchPoints = points;
        OnChanged?.Invoke();
    }
}
