using System.Collections.Generic;
using UnityEngine;

// Tracks ancient "schematics" recovered from precursor Ancient Ruins. Surveying a world that holds
// ruins recovers its schematics, which gate the secret Ancients research branch — the door to unique
// precursor technologies. Part of the save file.
public static class AncientLore
{
    public static int SchematicsFound { get; private set; }
    public static event System.Action OnChanged;

    static readonly HashSet<int> countedBodies = new HashSet<int>();

    // Called when a world's survey completes; awards any ancient schematics it holds (once per world).
    public static void SurveyBody(CelestialBody b)
    {
        if (b == null || b.pointsOfInterest == null) return;
        if (countedBodies.Contains(b.id)) return;
        countedBodies.Add(b.id);

        int found = 0;
        foreach (var poi in b.pointsOfInterest) if (poi.type == POIType.AncientRuins) found++;
        if (found <= 0) return;

        SchematicsFound += found;
        SimpleAudio.Instance?.PlayNotify(NotifKind.Discovery);
        NotificationManager.Instance?.Push($"Ancient schematics recovered on {b.name}",
            $"You uncovered {found} precursor schematic(s) in the ruins. Total recovered: {SchematicsFound}. " +
            "New secrets are within reach in the Ancients research branch.", null, NotifKind.Discovery);
        TechManager.NotifyChanged();   // refresh research-window gates
        OnChanged?.Invoke();
    }

    public static void Reset() { SchematicsFound = 0; countedBodies.Clear(); OnChanged?.Invoke(); }

    // ---- Save / load ----
    public static int Export() => SchematicsFound;
    public static void Import(int count) { SchematicsFound = Mathf.Max(0, count); countedBodies.Clear(); OnChanged?.Invoke(); }
}
