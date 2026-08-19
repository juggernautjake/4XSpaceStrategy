using System;
using System.Collections.Generic;

// Which units are currently selected (shared by the 3D tokens, the fleet window and the send flow).
public static class UnitSelection
{
    static readonly List<Unit> selected = new List<Unit>();

    public static IReadOnlyList<Unit> Selected => selected;
    public static event Action OnChanged;

    public static bool IsSelected(Unit u) => selected.Contains(u);

    public static void Select(Unit u, bool additive)
    {
        if (u == null) return;
        if (!additive) selected.Clear();
        if (!selected.Contains(u)) selected.Add(u);
        else if (additive) selected.Remove(u);   // toggle off when additive
        OnChanged?.Invoke();
    }

    public static void SelectOnly(Unit u) { selected.Clear(); if (u != null) selected.Add(u); OnChanged?.Invoke(); }

    public static void Set(List<Unit> units) { selected.Clear(); if (units != null) selected.AddRange(units); OnChanged?.Invoke(); }

    public static void Clear() { selected.Clear(); OnChanged?.Invoke(); }

    /// Drop ONE unit from the selection, leaving the rest alone.
    ///
    /// The case this exists for is a ship dying mid-battle. Removal used to call Clear(), so losing a
    /// single fighter out of a selected fleet of twelve deselected the other eleven — in the middle of
    /// a fight, at the exact moment the player was about to give them an order. Silent if the unit was
    /// not selected, so callers can use it unconditionally.
    public static void Deselect(Unit u)
    {
        if (u == null) return;
        if (selected.Remove(u)) OnChanged?.Invoke();
    }

    // Selected units that share the given location (used to send a fleet from one place).
    public static List<Unit> SelectedAt(CelestialBody body)
    {
        var r = new List<Unit>();
        foreach (var u in selected) if (u.location == body) r.Add(u);
        return r;
    }
}
