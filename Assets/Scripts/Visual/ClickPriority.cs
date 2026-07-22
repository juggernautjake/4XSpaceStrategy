using UnityEngine;

// ============================================================================================
// A SHIP ALWAYS BEATS THE THING IT IS PARKED AT
//
// THE BUG. Bodies carry a deliberately OVERSIZED pick sphere so they stay easy to click as you zoom
// out — EnsureClickCollider floors it at 1.5 world units, and ClickColliderScaler then adds up to
// twelve more as the camera pulls back. A docked ship sits only ~2.5-4 units from its planet's centre
// (see UnitTokenRenderer's ring offset), so at anything past the closest zoom the ship is INSIDE the
// planet's pick sphere.
//
// Unity's OnMouseDown fires on the nearest hit, and the nearest hit is the front SURFACE of that huge
// sphere — which is closer to the camera than the ship floating inside it. So the planet won, the
// token's OnMouseDown never ran at all, and clicking a docked ship selected the world it was parked at.
// The old comment in ClickColliderScaler ("nearest-to-camera still wins, so overlaps resolve sensibly")
// was true of the geometry and wrong about the outcome.
//
// THE FIX. Ships are small, deliberate targets; bodies are large, forgiving ones. When the cursor is
// over both, the small deliberate one is what the player meant. So the body's click handler asks here
// FIRST, and hands the click over if a ship is under the cursor.
//
// It has to hand over rather than merely decline: because the body won the nearest-hit test, the ship's
// own OnMouseDown is never going to fire, so bailing out would select nothing at all.
// ============================================================================================
public static class ClickPriority
{
    /// Consume this click if a ship is under the cursor. Returns true if it did — the caller must then
    /// do nothing else, because the ship has already been selected.
    ///
    /// Called from the click handlers of things that are BIGGER than ships and easier to hit by
    /// accident: planets, moons and stars.
    public static bool TryClickUnitUnderCursor()
    {
        var cam = Camera.main;
        if (cam == null) return false;

        // RaycastAll, not Raycast: the whole point is to see PAST the body that won the nearest-hit
        // test and find out whether something small is standing inside it.
        var hits = Physics.RaycastAll(cam.ScreenPointToRay(Input.mousePosition));
        if (hits == null || hits.Length == 0) return false;

        UnitToken bestToken = null;
        UnitModelClick bestModel = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null) continue;

            // GetComponentInParent, because a model's collider can sit on a child mesh of the hull.
            var token = col.GetComponentInParent<UnitToken>();
            var model = token == null ? col.GetComponentInParent<UnitModelClick>() : null;
            if (token == null && model == null) continue;

            // Still nearest-wins AMONG SHIPS — two ships docked at the same world resolve the ordinary
            // way, by which one is actually in front.
            if (hits[i].distance >= bestDistance) continue;

            bestDistance = hits[i].distance;
            bestToken = token;
            bestModel = model;
        }

        // Report what the handler ACTUALLY did, not merely that one was called. Both refuse the click
        // while a fleet is mid-order (FleetMovementController.IsTargeting) — and claiming to have
        // consumed a click that nothing handled would swallow it, leaving the player clicking a star
        // with a ship in front of it and getting no response at all.
        if (bestToken != null) return bestToken.HandleClick();
        if (bestModel != null) return bestModel.HandleClick();
        return false;
    }
}
