using System;

// Toggles between GAME mode (play as a player: fog-of-war, no sandbox tools, ships pay resources)
// and DEV mode (see & control everything: all worlds revealed, orbit/terrain editors available,
// free building).
public static class GameMode
{
    public static bool DevMode = false;   // false = normal game mode

    public static event Action OnChanged;

    public static void SetDev(bool on) { DevMode = on; OnChanged?.Invoke(); }
    public static void Toggle() { SetDev(!DevMode); }
}
