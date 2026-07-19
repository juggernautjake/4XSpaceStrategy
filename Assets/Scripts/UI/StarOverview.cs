using UnityEngine;

// The one way to open a system's full Overview — the tabbed Inspector, on the Star target, which lists
// every sun of a cluster together (see InspectorStarTabs: "SUNS OF THIS SYSTEM").
//
// Two call sites want this: a sun in the detailed system view (single click), and a system proxy in the
// galaxy overview (double click). The galaxy-view path is the one that genuinely needed it — clicking a
// system out there opened only the lightweight summary, so the per-sun breakdown was unreachable from
// the galaxy map entirely.
public static class StarOverview
{
    /// Open the Overview for a whole system, showing every sun in it.
    public static void Open(StarSystemData sys)
    {
        if (sys == null) return;

        // Focus first: the Overview reads from the focused system for its zone and body lists, so opening
        // the window before the focus moves would show the previous system's worlds for a frame.
        GameManager.Instance?.SetFocus(sys);
        if (SystemContext.Zone != null)
            SystemContext.Zone.Retarget(sys.combinedStar, sys.pivot, sys.bodies);

        InspectorWindow.Instance?.Inspect(InspectorTarget.Of(sys.combinedStar, sys), resetTrail: true);
    }

    /// Open the Overview from one specific sun. Identical result — the Overview is always the whole
    /// cluster, never a single star — but kept separate so call sites read as what the player clicked.
    public static void OpenFromStar(StarData star, StarSystemData sys)
    {
        if (sys != null) { Open(sys); return; }
        if (star != null) InspectorWindow.Instance?.Inspect(InspectorTarget.Of(star, null), resetTrail: true);
    }
}
