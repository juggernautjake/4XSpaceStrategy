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

    // ============================================================================================
    // WHAT A SHIP COSTS TO LOSE — the number the screening formations sort on
    //
    // "Cheaper ships in front of the more expensive ones" needs a definition of expensive, and the
    // build cost alone is not it. Three things make a hull worth protecting and only one of them is
    // the price:
    //
    //   what it cost      metal + energy, the obvious half
    //   what it took      a hull needing a level-3 yard and Empire Level 9 cannot simply be rebuilt
    //                     the same afternoon; the Mega-Station is not 1500 metal, it is a campaign
    //   whether it can    a colony ship, a terraformer and a science vessel have NO attack. Putting
    //   fight back        them on the outside of a formation is not a trade, it is a donation
    //
    // The result is a rank, not a currency. All that is ever asked of it is the ORDER, so the weights
    // only have to get the ordering right: scouts and fighters cheap, freighters and labs dear,
    // capitals dearest.
    // ============================================================================================
    public static float ProtectionValue(Unit u)
    {
        var i = u?.Info;
        if (i == null) return 0f;

        float v = i.costMetal + i.costEnergy;

        // Hard to replace: each gate above the first is worth roughly another half of the hull again.
        v *= 1f + 0.5f * Mathf.Max(0, i.minShipyardLevel - 1);
        v *= 1f + 0.12f * Mathf.Max(0, i.minEmpireLevel - 1);

        // Cannot defend itself. Doubled rather than nudged, because an unarmed hull on the exposed
        // face of a formation contributes nothing there and dies for it.
        if (i.attack <= 0) v *= 2f;

        // A station under tow is the most helpless thing in the fleet: no guns that bear while it is
        // moving, and the slowest hull present.
        if (i.isStation) v *= 1.5f;

        return v;
    }

    /// Where to draw this ship, relative to the fleet's shared position on its course.
    ///
    /// `courseDir` is the direction of travel; `progress` is 0..1 through the trip. Returns zero for a
    /// lone ship, for a fleet at either end of its crossing, and whenever the course is degenerate.
    public static Vector3 Offset(Unit u, Vector3 courseDir, float progress)
    {
        if (u == null) return Vector3.zero;

        int count = Mathf.Max(1, u.formationCount);
        if (count <= 1) return Vector3.zero;

        var kind = Squadrons.OrdersFor(u)?.formation ?? FleetFormationKind.Wedge;
        if (kind == FleetFormationKind.Free) return Vector3.zero;

        if (courseDir.sqrMagnitude < 0.000001f) return Vector3.zero;
        Vector3 fwd = courseDir.normalized;

        // A course straight up or down leaves no sensible "right" — fall back to world X, which is only
        // reachable if a fleet is sent vertically and is better than a zero-length cross product.
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        right = right.sqrMagnitude < 0.0001f ? Vector3.right : right.normalized;
        Vector3 up = Vector3.Cross(fwd, right).normalized;

        // Stations are laid out in STEPS and converted to world units here, so one number sets the
        // scale of every formation and it is the one that matters: the size of the biggest hull present.
        float step = u.formationSpacing > 0.001f ? u.formationSpacing : LateralStep;

        Station(kind, SlotOf(u), count, out float lateral, out float back, out float lift);

        // Form up out of the anchorage, hold through the coast, close up into the destination.
        float spread = Mathf.Min(
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / FormUpFrac)),
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - progress) / CloseUpFrac)));

        return (right * lateral - fwd * back + up * lift) * step * spread;
    }

    // ============================================================================================
    // THE FORMATIONS — a slot, in steps, for each shape
    //
    // `back` is measured ASTERN, so a negative back puts a ship ahead of the formation's centre —
    // which is the whole point of a screen. `lift` is above or below the plane of the course, and only
    // Globe uses it: at the shallow angle the 4X camera looks down from, vertical spread reads as
    // depth rather than as height, so spending it anywhere else buys nothing and costs legibility.
    //
    // Slots arrive sorted CHEAPEST FIRST (see Assign), which is what makes Screen and Globe do what
    // was asked without either of them having to know a thing about ship classes.
    // ============================================================================================
    static void Station(FleetFormationKind kind, int slot, int count,
                        out float lateral, out float back, out float lift)
    {
        lateral = back = lift = 0f;
        if (slot <= 0 && kind != FleetFormationKind.Screen && kind != FleetFormationKind.Globe) return;

        switch (kind)
        {
            case FleetFormationKind.LineAbreast:
            {
                // One rank, everything bearing forward. Extra ships form ranks behind rather than
                // widening past the cap.
                Pair(slot, out int rank, out int pair, out float side);
                lateral = side * pair;
                back = rank * RankDepth;
                break;
            }

            case FleetFormationKind.LineAstern:
            {
                // Single file. Deliberately uncapped in depth — a column IS long, and the ship at the
                // front is the one that meets what is down the lane.
                back = slot * 0.9f;
                break;
            }

            case FleetFormationKind.Echelon:
            {
                // A diagonal stair to starboard: every ship clear of the one ahead's wake and of its
                // line of fire.
                lateral = slot * 0.8f;
                back = slot * 0.8f;
                break;
            }

            case FleetFormationKind.Screen:
            {
                // The cheaper HALF forms an arc ahead of the formation; everything dear sits behind it.
                int screen = Mathf.Clamp(count / 2, 1, 8);
                if (slot < screen)
                {
                    // Spread across the front, curving back at the wingtips so the arc is convex
                    // toward whatever it is meeting.
                    float t = screen == 1 ? 0f : (slot / (float)(screen - 1)) * 2f - 1f;   // -1..1
                    lateral = t * 2.2f;
                    back = -1.8f + Mathf.Abs(t) * 1.0f;
                }
                else
                {
                    // The protected body, in a compact block a clear gap behind the screen.
                    Pair(slot - screen, out int rank, out int pair, out float side);
                    lateral = side * pair * 0.9f;
                    back = 1.2f + rank * RankDepth + pair * 0.3f;
                }
                break;
            }

            case FleetFormationKind.Globe:
            {
                // Escorts on a shell, the valuable ships inside it. Two thirds to the shell, because a
                // shell with holes in it is not a shell.
                int shell = Mathf.Clamp((count * 2) / 3, 1, 12);
                if (slot < shell)
                {
                    float ang = slot * Mathf.PI * 2f / shell;
                    lateral = Mathf.Sin(ang) * 2.0f;
                    back = Mathf.Cos(ang) * 2.0f;
                    // Alternate above and below so the shell has a third dimension without needing
                    // twice as many ships to close it.
                    lift = ((slot % 2 == 0) ? 0.7f : -0.7f) * Mathf.Abs(Mathf.Sin(ang * 0.5f));
                }
                else
                {
                    Pair(slot - shell, out int rank, out int pair, out float side);
                    lateral = side * pair * 0.7f;
                    back = rank * 0.8f;
                }
                break;
            }

            default:   // Wedge
            {
                Pair(slot, out int rank, out int pair, out float side);
                lateral = side * pair;
                back = pair * SweepBack + rank * RankDepth;
                break;
            }
        }
    }

    /// Rank and file for the pair-based shapes: slot 0 alone at the point, then 1,2 abreast a step
    /// wider, 3,4 wider still, until the rank is full and the next ships start another behind it.
    static void Pair(int slot, out int rank, out int pair, out float side)
    {
        slot = Mathf.Max(0, slot);
        rank = slot / RankWidth;
        int inRank = slot % RankWidth;
        pair = (inRank + 1) / 2;                       // 0, then 1,1, 2,2, 3,3...
        side = (inRank % 2 == 1) ? -1f : 1f;           // odd to port, even to starboard
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
    ///
    /// SLOTS ARE HANDED OUT CHEAPEST FIRST, which is the whole of "the less expensive ships group up
    /// in front of the larger and more expensive ones". Doing it here rather than inside each shape
    /// means Screen and Globe get it for nothing, and Wedge and Line Astern get it too — the ship at
    /// the point of a wedge and the ship at the head of a column are the exposed ones either way.
    ///
    /// A squadron of one CLASS sorts to a stable tie and lays out in geometric order, which is right:
    /// there is nothing to protect, and a screen of dreadnoughts by dreadnoughts is theatre.
    public static void Assign(System.Collections.Generic.List<Unit> group)
    {
        if (group == null) return;

        var order = new System.Collections.Generic.List<Unit>();
        foreach (var u in group) if (u != null) order.Add(u);

        // Ties broken on id so the order is stable from one order to the next — a squadron that
        // reshuffled its stations every time it was told to move would churn for no reason.
        order.Sort((a, b) =>
        {
            int c = ProtectionValue(a).CompareTo(ProtectionValue(b));
            return c != 0 ? c : a.id.CompareTo(b.id);
        });

        // One spacing for the whole fleet, from its LARGEST hull, so nothing in it interpenetrates.
        float widest = LateralStep;
        foreach (var u in order)
        {
            var e = UnitModelLibrary.For(u.type);
            if (e != null) widest = Mathf.Max(widest, e.size * 1.15f);
        }

        for (int i = 0; i < order.Count; i++)
        {
            order[i].formationSlot = i;
            order[i].formationCount = order.Count;
            order[i].formationSpacing = widest;
        }
    }
}
