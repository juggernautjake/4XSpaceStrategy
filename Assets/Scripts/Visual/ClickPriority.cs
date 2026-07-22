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
    public static bool TryClickUnitUnderCursor(GameObject bodyVisual = null)
    {
        var cam = Camera.main;
        if (cam == null) return false;

        var ray = cam.ScreenPointToRay(Input.mousePosition);

        // THE BODY'S OWN DISC ALWAYS WINS.
        //
        // The first version of this handed the click to any ship under the cursor, which overshot: a
        // ship parked near a world made the WORLD unclickable, because the ship's pick box (floored to
        // 0.8 units so tiny hulls are hittable at all) reached across the planet you were aiming at.
        //
        // The rule that actually matches intent: if the ray passes through the body as DRAWN — its real
        // rendered sphere, not the inflated pick volume — the player is pointing at the world and gets
        // the world. Only outside that disc, in the halo of pick volume where nothing is drawn, does a
        // ship win. So clicking the planet selects the planet, clicking the ship beside it selects the
        // ship, and the ambiguous ring between them resolves toward the smaller, deliberate target.
        if (bodyVisual != null && RayHitsDrawnSphere(ray, bodyVisual)) return false;

        // RaycastAll, not Raycast: the whole point is to see PAST the body that won the nearest-hit
        // test and find out whether something small is standing inside it.
        var hits = Physics.RaycastAll(ray);
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

    /// Does the ray pass through the body as it is actually DRAWN?
    ///
    /// Measured from the renderer's own bounds rather than from its collider, and that distinction is
    /// the entire point: the collider is deliberately several times larger than the art so a distant
    /// world stays easy to hit. Asking the collider "is the cursor on the planet" would answer yes over
    /// a wide ring of empty space, which is what made ships beside a world steal its clicks — and,
    /// before that, what made the world steal theirs.
    static bool RayHitsDrawnSphere(Ray ray, GameObject go)
    {
        var rend = go.GetComponentInChildren<Renderer>();
        if (rend == null) return false;

        Vector3 c = rend.bounds.center;
        // The body's own sphere, not the bounds of everything hanging off it. An atmosphere shell is
        // drawn noticeably wider than the surface, and counting it would put the "planet" edge out in
        // the haze where the world does not really look solid.
        float r = Mathf.Min(rend.bounds.extents.x, Mathf.Min(rend.bounds.extents.y, rend.bounds.extents.z));
        if (r <= 0.0001f) return false;

        // Standard point-to-line distance: the closest approach of the ray to the sphere's centre.
        Vector3 toC = c - ray.origin;
        float along = Vector3.Dot(toC, ray.direction);
        if (along < 0f) return false;                       // the body is behind the camera
        float perpSq = toC.sqrMagnitude - along * along;
        return perpSq <= r * r;
    }
}
