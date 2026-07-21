using System;

// Toggles between GAME mode (play as a player: fog-of-war, no sandbox tools, ships pay resources)
// and DEV mode (see & control everything: all worlds revealed, orbit/terrain editors available,
// free building).
public static class GameMode
{
    public static bool DevMode = false;   // false = normal game mode

    public static event Action OnChanged;

    // Dev Mode is reversible: what the player had before it went on is what they get back when it goes
    // off. The ordering below is the whole trick, and both halves have to sit OUTSIDE the OnChanged
    // event — DevCheats tops the economy up to a million from its own OnChanged handler, and which
    // handler runs first is just subscription order.
    //
    //   - Capture while DevMode is still FALSE. The flag is what DevCheats.TopUp and
    //     PlayerEconomy.Capacity read; once either has run, the real numbers are already gone.
    //   - Restore once DevMode is already FALSE, so nothing refills in behind it.
    public static void SetDev(bool on)
    {
        if (on == DevMode) return;   // no double-capture, which would snapshot the cheated numbers
        if (on) DevCheats.CaptureBaseline();
        DevMode = on;
        if (!on) DevCheats.RestoreBaseline();
        OnChanged?.Invoke();
    }
    public static void Toggle() { SetDev(!DevMode); }
}
