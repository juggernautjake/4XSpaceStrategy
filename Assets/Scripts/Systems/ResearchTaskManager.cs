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

    public void StartResearch(CelestialBody body, PointOfInterest poi)
    {
        if (body == null || poi == null || !poi.IsResearchable || IsResearching(poi)) return;
        active.Add(new ResearchTask { body = body, poi = poi, duration = Mathf.Max(3f, poi.researchDuration) });
    }

    void Update()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var t = active[i];
            t.elapsed += Time.unscaledDeltaTime;   // consistent, independent of time-scale
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
            ResearchManager.ForceResearch(poi.relatedOre);
            extra = $"  Unlocked ore: {OreDatabase.Get(poi.relatedOre).displayName}.";
        }
        if (poi.type == POIType.Mystery) ResearchManager.AwardExploration();

        string title = poi.type == POIType.Mystery
            ? (string.IsNullOrEmpty(poi.revealTitle) ? "Anomaly" : poi.revealTitle)
            : poi.title;
        string report = (string.IsNullOrEmpty(poi.reportText) ? "Research complete." : poi.reportText) + extra;

        NotificationManager.Instance?.Push($"Research complete: {title}", report, () => FocusReport(body, title, report), NotifKind.Research);
        DetailedSurfaceWindow.Instance?.RefreshIfShowing(body);
    }

    void FocusReport(CelestialBody body, string title, string report)
    {
        if (body != null && body.visualObject != null)
            CameraController.Focus(body.visualObject.transform.position);
        if (body != null)
        {
            PlanetUI.Instance?.Show(body);
            DetailedSurfaceWindow.Instance?.Open(body);
        }
        NotificationManager.Instance?.Push($"Report: {title}", report, null);
    }
}
