using System;
using UnityEngine;

// Which surface structure is currently selected.
//
// Deliberately shared state rather than a field on the window: the map and the infrastructure list are
// two views of one selection, and clicking either must move the marker in the other. Mirrors how
// UnitSelection already works for ships.
public static class SurfaceSelection
{
    public static CelestialBody Body { get; private set; }
    public static PlacedBuilding Selected { get; private set; }

    public static event Action OnChanged;

    public static bool IsSelected(PlacedBuilding p) => p != null && Selected == p;

    public static void Select(CelestialBody body, PlacedBuilding p)
    {
        if (Body == body && Selected == p) return;
        Body = body;
        Selected = p;
        OnChanged?.Invoke();
    }

    public static void Clear()
    {
        if (Body == null && Selected == null) return;
        Body = null; Selected = null;
        OnChanged?.Invoke();
    }

    /// Drop the selection if it refers to something that no longer exists (demolished, or a different
    /// world). Cheap enough to call every frame.
    public static void Validate()
    {
        if (Selected == null) return;
        if (Body == null || Body.placedBuildings == null || !Body.placedBuildings.Contains(Selected))
            Clear();
    }
}
