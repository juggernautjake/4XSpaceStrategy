using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE SYSTEM FOG OF WAR, KEPT CURRENT (2026-10-11)
//
// VisibilityService.Unexplored decides WHAT is fogged; this decides WHEN that answer is pushed at the
// visuals. A system stops being unexplored when a player ship crosses its detection threshold — an
// event nothing announces (SystemPresence works it out by looking) — so this polls a few times a
// second, remembers each system's last answer, and re-applies only the systems whose answer changed.
// That is what makes a system appear the moment a scout arrives, with no reload.
//
// Dev Mode flips every answer at once; it is handled by the same comparison (Known is always true in
// Dev Mode), plus an immediate pass on GameMode.OnChanged so the toggle does not wait for the poll.
//
// A new galaxy (new game or load) resets the memory, and each system is re-applied once on first
// sight (see Poll) so a fleet restored after the visuals were built is honoured.
// ============================================================================================
public class SystemFog : MonoBehaviour
{
    public static SystemFog Instance;

    const float PollSeconds = 0.25f;
    float nextPoll;
    Galaxy trackedFor;
    readonly Dictionary<StarSystemData, bool> lastKnown = new Dictionary<StarSystemData, bool>();

    public static void Create()
    {
        if (Instance != null) return;
        Instance = new GameObject("SystemFog").AddComponent<SystemFog>();
        DontDestroyOnLoad(Instance.gameObject);
    }

    void OnEnable() { GameMode.OnChanged += ApplyNow; }
    void OnDisable() { GameMode.OnChanged -= ApplyNow; }

    /// Re-evaluate every system right now, rather than on the next poll.
    public void ApplyNow()
    {
        nextPoll = 0f;
        Poll(true);
    }

    void Update()
    {
        nextPoll -= Time.unscaledDeltaTime;
        if (nextPoll > 0f) return;
        nextPoll = PollSeconds;
        Poll(false);
    }

    void Poll(bool forceApply)
    {
        var g = SystemContext.Galaxy;
        if (g == null || g.systems == null) { lastKnown.Clear(); trackedFor = null; return; }

        bool fresh = !ReferenceEquals(g, trackedFor);
        if (fresh) { lastKnown.Clear(); trackedFor = g; }

        bool any = false;
        foreach (var sys in g.systems)
        {
            if (sys == null) continue;
            // Not visualized yet (the stepped build is still running): nothing to apply to, and nothing
            // to remember — VisualizeGalaxy's own ApplyAll fogs it when its visuals exist.
            if (sys.pivot == null) { lastKnown.Remove(sys); continue; }

            bool known = !VisibilityService.Unexplored(sys);
            bool had;
            bool seen = lastKnown.TryGetValue(sys, out had);
            lastKnown[sys] = known;

            // Applied on FIRST SIGHT as well as on a change. ApplyAll fogged the visuals with whatever the
            // answer was when they were built, and on a load the fleet can be restored after that —
            // so the answer may already have moved on by the time this first looks. Applying once per
            // system per galaxy closes that gap; after that only a change costs anything.
            if (!forceApply && seen && had == known) continue;
            foreach (var b in sys.AllBodies()) VisibilityService.Apply(b);
            any = true;
        }
        if (any) VisibilityService.ApplyUnits();
    }
}
