using UnityEngine;

// ============================================================================================
// A FLEET THAT FLIES AS A FLEET
//
// Every ship given the same move order gets the same `travelFrom`, the same `travelTo` and the same
// `travelDuration`, so UnitManager.UnitPos returns the SAME POINT for all of them for the whole
// crossing. Eight ships sent to a world are eight meshes occupying one position: they interpenetrate
// the entire way, and what arrives looks like a single ship that got heavier.
//
// That is a display problem and it is fixed here rather than in the simulation. Where a fleet's ships
// sit RELATIVE TO EACH OTHER is a rendering question — nothing in combat, range, arrival or the save
// depends on it — so the sim keeps its one clean lerp per fleet and the renderers ask this for the
// offset to draw each hull at.
//
// ---- THE SHAPE ------------------------------------------------------------------------------
//
// A shallow WEDGE, laid out in the plane the camera looks down on:
//
//                              ^  course
//              7   5   3   1   0   2   4   6   8
//                                  ^ slot 0 leads
//
// Odd slots go to port, even to starboard, each pair a step wider and a step further back. That
// reads as a formation from the 4X camera's high angle, it never puts one hull directly behind
// another where it would be hidden, and it degenerates correctly: a single ship is slot 0 and flies
// exactly where it always did.
//
// Spacing is measured against what these things are DRAWN at — hulls are 0.09 to 0.40 world units
// (UnitModelLibrary) — so a third of a unit between stations is a clear gap rather than a parade.
//
// ---- FORM UP, AND CLOSE UP ------------------------------------------------------------------
//
// The offset is not constant across the trip. It ramps in over the first stretch and back out over
// the last, so a fleet leaves its anchorage as a knot, spreads into formation for the crossing, and
// draws back together as it brakes into its destination — where the arrival ring (UnitModelRenderer's
// parked case) takes over and fans it out again around the world.
//
// The ramp-out matters more than the ramp-in: without it a fleet arrives at a world still spread
// across a third of a unit and then SNAPS into the arrival ring the frame the order completes.
// ============================================================================================
public static class FleetFormation
{
    /// Lateral gap between neighbouring stations, in world units. Hulls are drawn at 0.09-0.40 units
    /// (UnitModelLibrary), so this clears everything but a pair of dreadnoughts flying wingtip to
    /// wingtip, and those are never numerous.
    const float LateralStep = 0.30f;

    /// How far back each successive pair sits, as a fraction of the lateral step. Under 1 so the wedge
    /// is shallow — a deep V puts the trailing ships far enough back to read as a separate group.
    const float SweepBack = 0.5f;

    /// Fraction of the trip spent spreading out of the anchorage, and drawing back in at the far end.
    const float FormUpFrac = 0.10f, CloseUpFrac = 0.16f;

    /// Ships per rank, INCLUDING the leader — so a rank is the leader plus two pairs, and the fleet is
    /// never more than two lateral steps wide however many ships are in it.
    ///
    /// THIS IS WHY THE WIDTH IS CAPPED RATHER THAN GROWING. A wedge that keeps widening is the obvious
    /// shape and it is the wrong one at this scale: SystemVisualizer draws a planet 0.6 to 2.2 world
    /// units across, so a twelve-ship fleet fanned into a single rank spans wider than the world it is
    /// flying to and stops reading as one formation. Extra ships go BEHIND instead, which costs nothing
    /// on a camera looking down at a shallow angle.
    const int RankWidth = 5;

    /// Gap between ranks, in lateral steps.
    const float RankDepth = 1.4f;

    /// The station this unit holds. Falls back to the unit id when no order assigned one — which
    /// happens for a save reloaded mid-flight, and spreads the fleet just as well.
    static int SlotOf(Unit u) => u == null ? 0 : (u.formationSlot >= 0 ? u.formationSlot : Mathf.Abs(u.id));

    /// Where to draw this ship, relative to the fleet's shared position on its course.
    ///
    /// `courseDir` is the direction of travel; `progress` is 0..1 through the trip. Returns zero for a
    /// lone ship, for a fleet at either end of its crossing, and whenever the course is degenerate.
    public static Vector3 Offset(Unit u, Vector3 courseDir, float progress)
    {
        if (u == null) return Vector3.zero;

        int count = Mathf.Max(1, u.formationCount);
        if (count <= 1) return Vector3.zero;

        int slot = SlotOf(u);
        if (slot == 0) return Vector3.zero;         // the leader flies the course itself

        if (courseDir.sqrMagnitude < 0.000001f) return Vector3.zero;
        Vector3 fwd = courseDir.normalized;

        // A course straight up or down leaves no sensible "right" — fall back to world X, which is only
        // reachable if a fleet is sent vertically and is better than a zero-length cross product.
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        right = right.sqrMagnitude < 0.0001f ? Vector3.right : right.normalized;

        // Rank and file. Slot 0 leads; 1,2 are the first pair, 3,4 the second, and so on until the rank
        // is full, at which point the next ships start a second rank one step further back.
        int rank = slot / RankWidth;
        int inRank = slot % RankWidth;

        int pair = (inRank + 1) / 2;                       // 0 for the leader, then 1,1,2,2,3,3...
        float side = (inRank % 2 == 1) ? -1f : 1f;         // odd to port, even to starboard

        float lateral = side * pair * LateralStep;
        float back = (pair * SweepBack + rank * RankDepth) * LateralStep;

        // Form up out of the anchorage, hold through the coast, close up into the destination.
        float spread = Mathf.Min(
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / FormUpFrac)),
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - progress) / CloseUpFrac)));

        return (right * lateral - fwd * back) * spread;
    }

    // ============================================================================================
    // THE ANCHORAGE — where ships sit when they are parked at a world
    //
    // Both renderers fanned their ships around one circle of FIXED radius: angle = index * 2pi / count.
    // That is exactly right up to about six ships and falls apart above it, because the circumference
    // stays put while the number of ships sharing it grows. A world with twenty ships over it packs
    // them at a tenth of a unit apart — hulls are up to 0.40 across — so a defended world reads as a
    // solid ring of overlapping geometry rather than as a fleet.
    //
    // So the anchorage GROWS. Ships are spaced along the arc, and when a ring is full the next ship
    // starts a wider one, each ring offset half a slot so the rings do not line up into spokes.
    // ============================================================================================

    /// Where the `index`-th of `count` ships parked at a body sits, relative to that body's centre.
    /// `radius` is the innermost standoff and `spacing` the gap to keep between neighbours; both are in
    /// whatever units the caller works in, so the 3D hulls and the galaxy-scale tokens can each ask in
    /// their own scale.
    public static Vector3 AnchorOffset(int index, int count, float radius, float spacing)
    {
        if (count <= 1 || index <= 0 && count == 1) return new Vector3(radius, 0f, 0f);

        index = Mathf.Max(0, index);
        radius = Mathf.Max(0.01f, radius);
        spacing = Mathf.Max(0.01f, spacing);

        // How many fit on the innermost ring at the spacing asked for. At least three, or a small fleet
        // would stack itself into rings for no reason.
        int perRing = Mathf.Max(3, Mathf.FloorToInt(2f * Mathf.PI * radius / spacing));

        int ring = index / perRing;
        int slot = index % perRing;

        // Each ring out is wider, and half a slot around, so ships never line up radially behind one
        // another where the outer one hides the inner.
        float r = radius * (1f + ring * 0.55f);
        float ang = (slot + (ring % 2) * 0.5f) * (Mathf.PI * 2f / perRing);

        return new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
    }

    /// Hand out stations to a fleet that has just been given a move order. Called once per order, not
    /// per frame — the slot has to stay put for the whole crossing or the formation churns.
    public static void Assign(System.Collections.Generic.List<Unit> group)
    {
        if (group == null) return;
        for (int i = 0; i < group.Count; i++)
        {
            if (group[i] == null) continue;
            group[i].formationSlot = i;
            group[i].formationCount = group.Count;
        }
    }
}
