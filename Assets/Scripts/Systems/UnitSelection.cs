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

    // Selected units that share the given location (used to send a fleet from one place).
    public static List<Unit> SelectedAt(CelestialBody body)
    {
        var r = new List<Unit>();
        foreach (var u in selected) if (u.location == body) r.Add(u);
        return r;
    }
}
