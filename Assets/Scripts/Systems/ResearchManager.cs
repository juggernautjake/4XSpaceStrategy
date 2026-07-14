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
        ResearchPoints = StartingPoints;
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

    // Complete research for free (e.g. finishing a timed field-research task).
    public static void ForceResearch(OreType ore)
    {
        if (ore == OreType.None) return;
        discovered.Add(ore);
        researched.Add(ore);
        OnChanged?.Invoke();
    }

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
