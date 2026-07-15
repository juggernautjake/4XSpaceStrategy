using UnityEngine;

// Where a ship is actually drawn.
//
// A unit is rendered by EXACTLY ONE of two systems: UnitModelRenderer if its class has a mesh, and
// UnitTokenRenderer (a billboarded icon) otherwise — UnitTokenRenderer.Rebuild skips anything
// UsesModel() claims, precisely so nothing is drawn twice.
//
// That split is an implementation detail of rendering, but "point the camera at this ship" is a
// perfectly ordinary thing to want, and without somewhere to ask, every caller would have to know about
// both renderers and the rule for which one owns what. They'd each get it slightly wrong, and they'd all
// break the day a third renderer appears. One question, one place to ask it.
public static class UnitVisuals
{
    /// The transform this unit is drawn at, or null if it isn't on screen (destroyed, or not yet built).
    public static Transform TransformOf(Unit u)
    {
        if (u == null) return null;

        // Models first: UsesModel is the authority on which renderer owns a unit, and the token renderer
        // defers to it.
        var m = UnitModelRenderer.Instance != null ? UnitModelRenderer.Instance.TransformOf(u) : null;
        if (m != null) return m;

        return UnitTokenRenderer.Instance != null ? UnitTokenRenderer.Instance.TransformOf(u) : null;
    }
}
