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
        OnChanged?.Invoke();
    }
    public static void Toggle() { SetDev(!DevMode); }
}
