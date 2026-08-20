using UnityEngine;

// ============================================================================================
// MASS, AND WHAT IT COSTS TO CHANGE YOUR MIND
//
// Ships used to slide along their course on an eased lerp. Burn, coast and brake were already there,
// so a ship left slowly and arrived slowly — but its HEADING was free. Order a dreadnought at full
// speed to reverse and it pivoted on the spot like a compass needle and set off the other way, which
// is the single thing that most makes a fleet read as icons being dragged rather than as ships.
//
// What a real reversal looks like is the thing being modelled here: the ship cannot turn and cannot
// stop instantly, so it carries on the way it was going while it hauls its nose round, swings WIDE,
// bleeds off the speed it had, and only then builds speed the other way. A scout does that in a
// couple of seconds and a mega-station takes most of a minute, and the difference between them is the
// whole point.
//
// ---- WHERE THE NUMBERS COME FROM -----------------------------------------------------------
//
// From stats each class already has, rather than from a new table nobody would keep in step:
//
//   MASS      hull integrity. It is already authored per class across a 375:1 range — a probe at 8
//             and a mega-station at 3000 — and it already means "how much ship is there". Using it
//             means a hull that is buffed to be tougher also becomes harder to throw around, which is
//             the right coupling and one nobody has to remember to maintain.
//
//   TURN      falls with the square root of mass and rises with the class's speed rating. Square root
//             rather than linear because a ship's moment of inertia does not grow as fast as its
//             tonnage — doubling a hull's mass does not halve its agility — and a linear law made the
//             capitals unusable rather than ponderous.
//
//   THRUST    expressed as the time to reach full speed, which also scales on the square root of
//             mass: a second for a probe, nine for a dreadnought, a quarter of a minute for the
//             largest station under tow.
//
// ---- TURNING HARD COSTS SPEED, AND SPEED COSTS TURN ----------------------------------------
//
// Two couplings do most of the work, and both are the reason the wide turn appears without anyone
// scripting a wide turn:
//
//   * a ship's turn rate FALLS as it goes faster, so the faster it is travelling the wider the arc it
//     can hold; and
//   * a ship will not hold full thrust while it is pointing far off its course, so a big heading error
//     makes it coast and brake.
//
// Put together: order a reversal, and the ship brakes because it is pointing the wrong way, turns
// slowly at first because it is still fast, turns harder as it slows, and comes out of the turn
// pointing the right way with speed to rebuild. Nothing in the code says "arc"; the arc is what those
// two rules produce.
//
// ---- THIS IS DRAWING, NOT SIMULATION -------------------------------------------------------
//
// Arrival time is the simulation's, unchanged — UnitManager still decides when a fleet gets there and
// combat, range and the save still read that. This steers the DRAWN hull toward the position the sim
// has already decided on, and the lag is capped so a ship can trail its own marker through a turn
// without ever losing it. A fleet that took its own sweet time to arrive would be a fleet whose ETA
// lied, and the ETA is what the player plans with.
// ============================================================================================
public static class ShipPhysics
{
    /// Mass, in units where a probe is about 1. Hull integrity over the probe's, floored so nothing is
    /// weightless and a divide is always safe.
    public static float Mass(Unit u)
    {
        var i = u?.Info;
        if (i == null) return 1f;
        float m = Mathf.Max(1f, i.health) / 8f;

        // A station under tow is dead weight: no manoeuvring thrust of its own, and the biggest of them
        // is a small moon being moved. Half again on top of the hull it already has.
        if (i.isStation) m *= 1.5f;
        return m;
    }

    /// Degrees per second this hull can swing its nose through when it is not moving.
    ///
    /// Clamped at both ends on purpose. The floor keeps the largest structures turning at all rather
    /// than appearing frozen; the ceiling stops the lightest hulls spinning fast enough to strobe.
    public static float BaseTurnRate(Unit u)
    {
        var i = u?.Info;
        if (i == null) return 60f;
        float speed = Mathf.Max(1, i.speed);
        return Mathf.Clamp(220f * speed / (10f * Mathf.Sqrt(Mass(u))), 7f, 190f);
    }

    /// Seconds from rest to full speed.
    public static float SpoolTime(Unit u)
        => Mathf.Clamp(0.8f * Mathf.Sqrt(Mass(u)), 0.9f, 15f);

    /// World units per second per second.
    public static float Acceleration(Unit u, float topSpeed)
        => Mathf.Max(0.01f, topSpeed / SpoolTime(u));

    /// How much a hull's turn rate is throttled by how fast it is already going.
    ///
    /// This is the coupling that produces the wide turn. At rest a ship turns at its full rate; at
    /// speed it turns at a fraction of it, so the arc it can hold opens out exactly as it would for
    /// anything with momentum. The constant is per UNIT OF SPEED, and ship speeds here run in the
    /// low tens of world units per second.
    const float TurnSpeedPenalty = 0.11f;

    public static float TurnRateAt(Unit u, float speed)
        => BaseTurnRate(u) / (1f + Mathf.Max(0f, speed) * TurnSpeedPenalty);

    /// How much thrust a ship is willing to hold while its nose is `degreesOff` from where it wants to
    /// go. Full ahead when it is pointing the right way; nothing at all — and braking instead — once
    /// it is more than a right angle out, because thrusting then would only take it further wrong.
    public static float ThrottleFor(float degreesOff)
    {
        if (degreesOff >= 90f) return 0f;
        return Mathf.Clamp01(1f - degreesOff / 90f);
    }

    /// How hard a ship brakes when it is pointing the wrong way, as a multiple of its acceleration.
    /// Retro-thrust and manoeuvring jets do less than the main drive, so under one.
    public const float BrakeFactor = 0.75f;

    /// The fastest this hull may still be going `distance` from its marker and still be able to stop
    /// on it: v = sqrt(2 a d).
    ///
    /// THIS IS WHAT STOPS SHIPS ORBITING THEIR OWN DESTINATION. The first version simply eased the
    /// target speed down as the gap closed, which is not the same thing at all — a hull arrived with
    /// speed it could not shed, could not turn tightly enough at that speed to stay on the marker, and
    /// so sailed past and came round again. Simulated over a minute it drew flower petals: a fighter
    /// looped its destination four times before settling, and a frigate five.
    ///
    /// Deriving the limit from the braking distance instead means a ship is never travelling faster
    /// than it can undo, so it settles rather than circling. It is also the honest version of what a
    /// pilot does, which is why it looks right as well as behaving.
    public static float ApproachSpeed(float accel, float distance)
        => Mathf.Sqrt(2f * Mathf.Max(0.0001f, accel * BrakeFactor) * Mathf.Max(0f, distance));
}
