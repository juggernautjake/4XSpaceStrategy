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

    // Recovering a schematic. This now happens by EXCAVATING ruins (a paid, timed research project on
    // the site — see ResearchTaskManager), not merely by flying past and surveying the world. Finding
    // ruins tells you they're there; digging them up is what gets you the precursor technology.
    public static void Recover(int count)
    {
        if (count <= 0) return;
        SchematicsFound += count;
        TechManager.NotifyChanged();   // refresh the Ancients gates in the research UI
        OnChanged?.Invoke();
    }

    // Called when a world's survey completes: it REVEALS the ruins and tells you what they might hold,
    // but hands over nothing. The schematics are in the ground until someone digs them out.
    public static void SurveyBody(CelestialBody b)
    {
        if (b == null || b.pointsOfInterest == null) return;
        if (countedBodies.Contains(b.id)) return;
        countedBodies.Add(b.id);

        int found = 0;
        foreach (var poi in b.pointsOfInterest) if (poi.type == POIType.AncientRuins && !poi.explored) found++;
        if (found <= 0) return;

        SimpleAudio.Instance?.PlayNotify(NotifKind.Discovery);
        NotificationManager.Instance?.Push($"Precursor ruins found on {b.name}",
            $"The survey located {found} set(s) of ancient ruins. Excavating them on the surface map is slow and costs research points — " +
            "but a major dig is the only way to recover a precursor schematic and open the Ancients research branch.",
            null, NotifKind.Discovery);
        OnChanged?.Invoke();
    }

    public static void Reset() { SchematicsFound = 0; countedBodies.Clear(); OnChanged?.Invoke(); }

    // ---- Save / load ----
    public static int Export() => SchematicsFound;
    public static void Import(int count) { SchematicsFound = Mathf.Max(0, count); countedBodies.Clear(); OnChanged?.Invoke(); }
}
