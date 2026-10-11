using System;

// Toggles between GAME mode (play as a player: fog-of-war, no sandbox tools, ships pay resources)
// and DEV mode (see & control everything: all worlds revealed, orbit/terrain editors available,
// free building).
public static class GameMode
{
    public static bool DevMode = false;   // false = normal game mode

    public static event Action OnChanged;

    // Switching modes does NOT touch the economy. Both modes share one stockpile, and anything the
    // player granted themselves from the Dev panel is theirs to keep on the way back out — see
    // DevCheats.
    //
    // The granted TECH TREE is the one exception, because it is an explicit switch rather than a
    // resource: leaving Dev Mode turns it off, restoring exactly the technologies and research queue
    // from before it went on. Done after the flag has already cleared, so nothing re-grants behind it.
    public static void SetDev(bool on)
    {
        if (on == DevMode) return;
        DevMode = on;
        if (!on) DevCheats.SetAllTech(false);
        // Dev Mode builds instantly, so switching it on finishes whatever is already going up — buildings,
        // hulls and terraforming projects — through their normal completion paths. Before OnChanged, so
        // the panels that rebuild on the switch already see the finished state.
        if (on) FinishAllConstruction();
        OnChanged?.Invoke();
    }
    public static void Toggle() { SetDev(!DevMode); }

    // ============================================================================================
    // DEV MODE BUILDS INSTANTLY (2026-10-11)
    //
    // Free was already Dev Mode's rule; now it is instant as well. Each queue completes its own jobs the
    // moment they are queued (SurfaceBuildQueue.FinishNow, UnitManager.CompleteDevBuilds,
    // TerraformManager.CompleteDevProjects, ColonyManager.CompleteDevConstruction), and this finishes
    // what was already queued when the switch went on. Paused jobs (buildings, hulls, projects) are left
    // paused; unpausing one in Dev Mode completes it.
    // ============================================================================================

    /// What every build-time readout says in Dev Mode, in place of the duration.
    public const string InstantBuildLabel = "Dev: instant build";

    /// A build time for display: the duration normally, "Dev: instant build" in Dev Mode. The one
    /// wording for every card, catalogue row and queue so they cannot disagree.
    public static string BuildTimeText(float seconds)
        => DevMode ? InstantBuildLabel : GameCalendar.Duration(seconds);

    /// Finish every construction job in the game now (Dev Mode only).
    public static void FinishAllConstruction()
    {
        if (!DevMode) return;
        SurfaceBuildQueue.FinishAllNow();
        if (UnitManager.Instance != null) UnitManager.Instance.CompleteDevBuilds();
        if (TerraformManager.Instance != null) TerraformManager.Instance.CompleteDevProjects();
        if (ColonyManager.Instance != null) ColonyManager.Instance.CompleteDevConstruction();
    }
}
