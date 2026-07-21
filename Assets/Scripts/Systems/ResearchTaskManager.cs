using System.Collections.Generic;
using UnityEngine;

// A single in-progress field-research job.
public class ResearchTask
{
    public CelestialBody body;
    public PointOfInterest poi;
    public float duration;
    public float elapsed;
    public float Progress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
    public float Remaining => Mathf.Max(0f, duration - elapsed);
}

// Runs timed research on points of interest. Progress advances in real time (so it's consistent
// regardless of simulation speed). On completion it unlocks results, plays a chime, and raises a
// clickable notification with a report that jumps the camera to the site.
public class ResearchTaskManager : MonoBehaviour
{
    public static ResearchTaskManager Instance;
    readonly List<ResearchTask> active = new List<ResearchTask>();

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("ResearchTaskManager").AddComponent<ResearchTaskManager>();
    }

    void Awake() { Instance = this; }

    public bool IsResearching(PointOfInterest poi)
    {
        foreach (var t in active) if (t.poi == poi) return true;
        return false;
    }

    // The active task shown on a given body's viewer (first one found).
    public ResearchTask GetActiveFor(CelestialBody body)
    {
        foreach (var t in active) if (t.body == body) return t;
        return null;
    }

    /// Whether this site can be studied right now, and why not if it can't.
    public bool CanStart(CelestialBody body, PointOfInterest poi, out string reason)
    {
        reason = null;
        if (body == null || poi == null) { reason = "nothing here"; return false; }
        if (!poi.surveyed) { reason = "not charted yet — deep-survey this world first"; return false; }
        if (!poi.IsResearchable) { reason = "already studied"; return false; }
        if (IsResearching(poi)) { reason = "already under study"; return false; }
        if (!GameMode.DevMode && ResearchManager.ResearchPoints < poi.researchPointCost)
        { reason = $"needs {poi.researchPointCost} research points (have {ResearchManager.ResearchPoints})"; return false; }
        return true;
    }

    // Studying a site COSTS research points up front — the field team, the equipment, the analysis —
    // and pays back more than it cost when it completes. That's what makes exploring worth doing.
    public void StartResearch(CelestialBody body, PointOfInterest poi)
    {
        if (!CanStart(body, poi, out _)) return;
        if (!GameMode.DevMode) ResearchManager.AddPoints(-poi.researchPointCost);
        float duration = Mathf.Max(3f, poi.researchDuration) * GameConfig.ResearchTimeMult; // difficulty
        active.Add(new ResearchTask { body = body, poi = poi, duration = duration });
    }

    void Update()
    {
        TechManager.Tick(Time.deltaTime);   // advance the timed tech-research queue

        for (int i = active.Count - 1; i >= 0; i--)
        {
            var t = active[i];
            t.elapsed += Time.deltaTime;   // scales with game speed like all other in-game loading
            if (t.elapsed >= t.duration)
            {
                Complete(t);
                active.RemoveAt(i);
            }
        }
    }

    void Complete(ResearchTask t)
    {
        var poi = t.poi;
        var body = t.body;
        poi.explored = true;

        string extra = "";
        if (poi.relatedOre != OreType.None)
        {
            // The site's own ore comes free: the excavation IS the research, and it was paid for up front.
            ResearchManager.ForceResearch(poi.relatedOre);
            extra = $"  Unlocked ore: {OreDatabase.Get(poi.relatedOre).displayName}.";
        }

        // The payoff. Each site pays back more than the points it cost to study — that margin is the
        // entire economic reason to explore rather than sit at home.
        // The site's own figure, falling back to the per-type default for anything that lost its value
        // (a very old save, or a POI built without one).
        int reward = poi.researchReward > 0 ? poi.researchReward : UnitManager.POIReward(poi);
        ResearchManager.AddPoints(reward);
        extra += $"  <color=#8FD0FF>+{reward} research points</color> (cost {poi.researchPointCost}).";

        // Precursor ruins are the only source of ancient schematics, which gate the Ancients tree.
        if (poi.yieldsSchematic)
        {
            AncientLore.Recover(1);
            extra += $"  <color=#4DE8D8>Recovered an ancient schematic ({AncientLore.SchematicsFound} total)</color> — precursor technology is opening up.";
        }

        string title = poi.type == POIType.Mystery
            ? (string.IsNullOrEmpty(poi.revealTitle) ? "Anomaly" : poi.revealTitle)
            : poi.title;
        string report = (string.IsNullOrEmpty(poi.reportText) ? "Research complete." : poi.reportText) + extra;

        NotificationManager.Instance?.Push($"Research complete: {title}", report, () => FocusReport(body, title, report), NotifKind.Research);
        PlanetViewWindow.Instance?.RefreshIfShowing(body);
    }

    void FocusReport(CelestialBody body, string title, string report)
    {
        if (body != null && body.visualObject != null)
            CameraController.Focus(body.visualObject.transform.position);
        if (body != null)
        {
            PlanetUI.Instance?.Show(body);
            PlanetViewWindow.Instance?.ShowFor(body);
        }
        NotificationManager.Instance?.Push($"Report: {title}", report, null);
    }
}
