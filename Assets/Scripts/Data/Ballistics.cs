using UnityEngine;

// ============================================================================================
// WHERE TO SHOOT, AND HOW A ROUND GETS THERE
//
// Everything about a shot that is MATHS rather than drawing lives here, so that the projectile
// renderer is a renderer and the combat manager is a bookkeeper. It has no Unity dependency past
// Vector3 and Mathf, which is what lets tools/ballistics-check.mjs be a faithful line-for-line port
// and render the trajectories as a picture — there is no Unity in this environment, so a flight model
// nobody can look at is a flight model nobody has checked.
//
// ---- THE BUG THIS FILE EXISTS TO FIX ----------------------------------------------------------
//
// Every gun in the game used to fire AT WHERE THE TARGET WAS. That is fine for a beam and a railgun,
// which arrive the frame they are fired, and it is catastrophic for everything else. Run the numbers
// that were actually shipping: a pulse bolt crosses a 30-unit gap at 62 u/s in 0.48 seconds, and a
// fighter moving at 12 u/s covers 5.8 units in that time. Hulls are drawn a THIRD of a unit across.
// So the round arrived seventeen hull-widths behind a target that was flying in a straight line, and
// the only reason anything ever died was a fallback that counted a round landing within 1.2 units of
// its aimpoint as a hit anyway.
//
// In other words: the travel time was drawn but never simulated, and the hit was decided by a
// forgiving radius rather than by aim. Leading the target — LeadAim below — is what makes travel time
// mean something, and it is the difference between "projectiles are an effect" and "projectiles are
// a mechanic". It also creates the counterplay for free: a target that CHANGES its velocity after the
// trigger is pulled makes the solution wrong, so manoeuvring genuinely dodges.
//
// ---- THE THREE GUIDANCE LAWS AND WHY THERE ARE THREE ------------------------------------------
//
//   UNGUIDED   Aim once, fly straight. Where it lands is decided at the muzzle.
//   PURSUIT    Point at the target, always. This is what "homing" means to most people and it is the
//              WORST of the three: a pursuit missile flies to where the target IS, which means it
//              arrives from behind in a tail chase and spends the whole flight out-turning its own
//              overcorrection. Against anything crossing its path it loses.
//   PRONAV     Proportional navigation. Steer to hold the line of sight STILL rather than to point at
//              the target. It is what every real missile since the 1950s does, and the reason is worth
//              stating plainly: if the bearing to something is not changing, you are going to collide
//              with it. Driving the LOS rate to zero produces a lead-pursuit intercept automatically,
//              without ever solving for where the target will be — which matters, because the target's
//              future is not knowable when it is allowed to manoeuvre.
//
// The gameplay payoff of ProNav over pursuit is not that it hits more. It is that a ProNav missile
// flies a visibly INTELLIGENT curve — it cuts the corner, it commits early, and when it is beaten it
// is beaten by a target that forced it into a turn it did not have the acceleration for. That is a
// readable defeat. A pursuit missile that misses just looks broken.
//
// ---- WHAT ACTUALLY STOPS A MISSILE ------------------------------------------------------------
//
// Not its turn rate in degrees. Its TURN RADIUS, which is v^2/a — quadratic in speed. This is the
// single most important consequence in the file, and it is why missiles are described by an
// acceleration limit rather than by a degrees-per-second figure:
//
//     a fast missile cannot corner.
//
// Double a missile's speed and it needs four times the lateral thrust to hold the same arc. So the
// fast interceptor is the one that overshoots a nimble fighter, and the slow torpedo is the one that
// will follow a capital ship around a moon all day. Nobody has to author that trade-off; it falls
// out of one square.
//
// And past that: FUEL. A missile that has burned out cannot turn at all, because in vacuum turning
// IS thrusting — there is no air to bite on. So a burned-out round flies dead straight to wherever it
// was pointed and then dissipates, which is exactly what the player sees when they run from a volley
// and it sails past behind them. Nothing scripts that either.
// ============================================================================================

/// How a round decides where to go once it has left the tube.
public enum GuidanceLaw
{
    /// Aim at launch, fly straight. The solution is computed once, by LeadAim, and everything after
    /// that is the target's problem.
    Unguided,

    /// Point at the target every frame. Cheap, dumb, and the reason it is here at all is that a
    /// short-ranged interceptor with more thrust than it knows what to do with genuinely behaves this
    /// way, and it reads correctly on a point-defence needle crossing four units.
    Pursuit,

    /// Proportional navigation. Steers to null the line-of-sight rate. See the header.
    Proportional,
}

/// What a mount consumes when it fires.
public enum AmmoKind
{
    /// Reactor charge. Finite in the moment and infinite over an hour: a ship can empty its capacitor
    /// in an alpha strike and then has to wait for it, but it never has to go home. This is what makes
    /// an energy fleet strategically free and tactically rationed.
    Energy,

    /// Physical rounds in a magazine. Finite, full stop. When they are gone the mount is silent until
    /// the ship is somewhere that can hand it more — which is the entire reason a missile fleet has a
    /// supply line and a laser fleet does not.
    Ordnance,
}

public static class Ballistics
{
    // ============================================================================================
    // THE FIRING SOLUTION
    // ============================================================================================

    /// Time until a round leaving `from` at `speed` meets a target at `targetPos` travelling at
    /// `targetVel`. Returns a negative number when there is no solution.
    ///
    /// This is the intercept quadratic, and the shape of it is the shape of the mechanic. We need the
    /// time t at which the round and the target are in the same place:
    ///
    ///     | (P - S) + Vt*t | = s*t
    ///
    /// Square both sides and collect:
    ///
    ///     (Vt.Vt - s^2) t^2  +  2 (D.Vt) t  +  D.D  =  0        where D = P - S
    ///
    /// The leading coefficient is the interesting one. `Vt.Vt - s^2` is negative whenever the round is
    /// faster than the target, which makes the parabola open downward and GUARANTEES exactly one
    /// positive root — a faster round can always catch a target that flies straight, from any angle.
    /// When the target is faster the sign flips, and there are then either two solutions (a head-on or
    /// crossing shot, take the earlier) or none at all (it is simply running away faster than the round
    /// can follow). That last case is not an error to paper over: it is a target outrunning a weapon,
    /// and the honest response is for the gun to decline the shot.
    public static float InterceptTime(Vector3 from, Vector3 targetPos, Vector3 targetVel, float speed)
    {
        if (speed <= 0.001f) return 0f;              // instant weapons arrive now, by definition

        Vector3 d = targetPos - from;
        float a = Vector3.Dot(targetVel, targetVel) - speed * speed;
        float b = 2f * Vector3.Dot(d, targetVel);
        float c = Vector3.Dot(d, d);

        // Target speed almost exactly equals round speed: the quadratic degenerates to a line and the
        // usual formula divides by ~zero. Rare, but it happens the instant anyone tunes a missile to
        // the speed of the hull it is chasing, and a NaN aimpoint sends the round to the origin.
        if (Mathf.Abs(a) < 0.0001f)
        {
            if (Mathf.Abs(b) < 0.0001f) return -1f;
            float tLin = -c / b;
            return tLin > 0f ? tLin : -1f;
        }

        float disc = b * b - 4f * a * c;
        if (disc < 0f) return -1f;                   // cannot be caught

        float root = Mathf.Sqrt(disc);
        float t1 = (-b + root) / (2f * a);
        float t2 = (-b - root) / (2f * a);

        // The EARLIEST positive time. Both roots are real intercepts when the target is faster — the
        // second is the one where the round catches it on the far side of a long stern chase, which is
        // a solution the maths is happy with and no gunner would ever take.
        float best = -1f;
        if (t1 > 0f) best = t1;
        if (t2 > 0f && (best < 0f || t2 < best)) best = t2;
        return best;
    }

    /// Where to point the gun. Falls back to the target's present position when there is no intercept,
    /// so a hopeless shot still goes somewhere sensible rather than nowhere.
    ///
    /// `hasSolution` is the part callers care about: a mount with no solution is a mount that should
    /// hold its fire rather than waste a round, and CombatManager uses it for exactly that.
    public static Vector3 LeadAim(Vector3 from, Vector3 targetPos, Vector3 targetVel, float speed,
                                  out bool hasSolution, out float flightTime)
    {
        flightTime = InterceptTime(from, targetPos, targetVel, speed);
        hasSolution = flightTime >= 0f;
        if (!hasSolution) { flightTime = 0f; return targetPos; }
        return targetPos + targetVel * flightTime;
    }

    /// Convenience form for callers that only want the point.
    public static Vector3 LeadAim(Vector3 from, Vector3 targetPos, Vector3 targetVel, float speed)
        => LeadAim(from, targetPos, targetVel, speed, out _, out _);

    // ============================================================================================
    // THE SAME SOLUTION, BUT HONEST ABOUT THE MOTOR
    //
    // Everything above assumes the round travels at one speed for the whole flight. That is exactly
    // true for a bolt and exactly false for a missile, which leaves the tube at 6 u/s and does not
    // reach 34 for another second and a third — and over a short engagement, most of the flight IS
    // the boost phase.
    //
    // Getting this wrong is not a rounding error. Against a target crossing at 24 u/s at forty units,
    // the cruise-speed solution says intercept in 0.94 seconds and aims at a point the round cannot
    // possibly reach in that time; the target sails a further twenty units past the aimpoint while the
    // missile is still building speed toward it. The round then finds its target at an enormous
    // bearing off the nose, trips its own seeker, and goes ballistic having barely turned — which is
    // precisely what tools/ballistics-check.mjs showed: a total control effort of 25 degrees on an
    // engagement that should have been a hard, committed turn.
    //
    // The fix is a fixed-point iteration rather than anything clever. Solve at cruise, ask how far the
    // round can ACTUALLY get in that long, use that as the average speed, solve again. Three passes is
    // well past convergence for any profile in the game.
    // ============================================================================================

    /// How far a round has travelled `t` seconds after launch, integrating the boost curve.
    ///
    /// SpeedAt eases as k^2, so the integral of the boost phase is v0*t + (v1-v0)*t^3/(3T^2) — worth
    /// writing out because the alternative, sampling it numerically, would be called from the firing
    /// loop of every mount in every fleet.
    public static float DistanceCovered(WeaponInfo w, float t)
    {
        if (w == null || t <= 0f) return 0f;
        if (w.boostTime <= 0.0001f) return w.projectileSpeed * t;

        float T = w.boostTime, v0 = w.launchSpeed, v1 = w.projectileSpeed;
        if (t <= T) return v0 * t + (v1 - v0) * (t * t * t) / (3f * T * T);

        float dBoost = v0 * T + (v1 - v0) * T / 3f;
        return dBoost + v1 * (t - T);
    }

    /// The single speed that would have covered the same ground in the same time.
    public static float EffectiveSpeed(WeaponInfo w, float t)
    {
        if (w == null) return 0f;
        if (t <= 0.0001f) return Mathf.Max(0.01f, w.launchSpeed > 0f ? w.launchSpeed : w.projectileSpeed);
        return Mathf.Max(0.01f, DistanceCovered(w, t) / t);
    }

    /// How much further the round can travel by time `t` than it needs to. Zero at the intercept.
    static float Reach(WeaponInfo w, Vector3 d, Vector3 targetVel, float t)
        => DistanceCovered(w, t) - (d + targetVel * t).magnitude;

    /// Intercept time for a specific mount, accounting for its motor. Negative if it cannot be done
    /// inside the round's lifetime.
    ///
    /// ---- WHY THIS IS A BISECTION AND NOT THE QUADRATIC ----
    ///
    /// Because there is no closed form once the round's speed varies. The first attempt fed the
    /// cruise-speed solution back through an average-speed estimate a few times, hoping it would
    /// settle. It does not settle — it OSCILLATES, and it oscillates worst exactly where it matters.
    /// A missile boosting from 6 to 34 averages about 11 u/s over its first second, which for an 11
    /// u/s target makes the quadratic's leading coefficient almost exactly zero: the solution jumps to
    /// twenty seconds, the next pass sees an average of 33 u/s and jumps back to one, and whichever
    /// phase the loop happens to stop on is the answer. Stopping on the wrong one aimed twenty units
    /// off a target the previous code hit cleanly.
    ///
    /// So solve the real equation instead. `Reach` is negative at launch and positive once the round
    /// has out-run the target's line; twenty-four halvings pin the crossing to well under a
    /// millisecond, and the whole thing is a few dozen multiplies in a path that runs a handful of
    /// times a second per ship.
    public static float InterceptTimeFor(WeaponInfo w, Vector3 from, Vector3 targetPos, Vector3 targetVel)
    {
        if (w == null) return -1f;
        if (w.projectileSpeed <= 0.001f) return 0f;                    // instant
        if (w.boostTime <= 0.0001f)                                    // constant speed: closed form
            return InterceptTime(from, targetPos, targetVel, w.projectileSpeed);

        Vector3 d = targetPos - from;
        float hi = w.Lifetime;
        if (Reach(w, d, targetVel, hi) < 0f) return -1f;               // never catches it in its life

        float lo = 0f;
        for (int i = 0; i < 24; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (Reach(w, d, targetVel, mid) >= 0f) hi = mid; else lo = mid;
        }
        return hi;
    }

    /// Where to point this mount, accounting for its motor. The form CombatManager actually calls.
    public static Vector3 LeadAimFor(WeaponInfo w, Vector3 from, Vector3 targetPos, Vector3 targetVel,
                                     out bool hasSolution, out float flightTime)
    {
        flightTime = InterceptTimeFor(w, from, targetPos, targetVel);
        hasSolution = flightTime >= 0f;
        if (!hasSolution) { flightTime = 0f; return targetPos; }
        return targetPos + targetVel * flightTime;
    }

    // ============================================================================================
    // DISPERSION — WHY A SHOT MISSES EVEN WITH A PERFECT SOLUTION
    // ============================================================================================

    /// How far off a mount's aim actually is, in degrees, against this target at this range.
    ///
    /// A perfect firing solution fired by a perfect gun would mean every unguided round in the game
    /// hit every time, which is worse than the old always-miss bug rather than better. Three things
    /// spoil the shot, and all three are things the player can influence:
    ///
    ///   BASE       the mount's own precision. A spinal railgun is machined; a pulse array is sprayed.
    ///   RANGE      pointing error grows with how far it has to reach. Linear in the fraction of the
    ///              mount's reach being used, so everything is at its best up close.
    ///   CROSSING   the component of the target's motion ACROSS the line of fire, not along it. This
    ///              is the one that makes flying sideways a defence and flying straight at a gun a bad
    ///              idea — and it is why a fighter's speed protects it and a station's zero speed does
    ///              not. Closing speed contributes nothing, because a target coming straight at you is
    ///              not going anywhere you have to lead.
    public static float DispersionDegrees(WeaponInfo w, Vector3 from, Vector3 targetPos, Vector3 targetVel)
    {
        if (w == null) return 0f;

        Vector3 d = targetPos - from;
        float dist = d.magnitude;
        if (dist < 0.01f) return w.spreadDeg;

        Vector3 los = d / dist;
        // Strip the along-sight component; what is left is pure crossing motion.
        Vector3 crossing = targetVel - los * Vector3.Dot(targetVel, los);

        float rangeTerm = 1f + w.spreadRangeFactor * Mathf.Clamp01(dist / Mathf.Max(0.01f, w.range));
        float crossTerm = crossing.magnitude * w.spreadCrossFactor;

        return w.spreadDeg * rangeTerm + crossTerm;
    }

    /// Rotate an aim direction by a random amount inside a cone. Uses Unity's RNG, which is seeded per
    /// session — combat is not part of the deterministic save and never has been.
    public static Vector3 ApplyDispersion(Vector3 dir, float degrees)
    {
        if (degrees <= 0.0001f || dir.sqrMagnitude < 1e-8f) return dir;
        dir = dir.normalized;

        // A perpendicular to rotate about, chosen so it is never parallel to the aim.
        Vector3 axis = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        // Uniform over the cone's SOLID angle rather than over the angle, or shots cluster in the
        // middle and the spread figure means something other than what it says.
        float ang = degrees * Mathf.Sqrt(Random.value);
        Vector3 tilted = Quaternion.AngleAxis(ang, axis) * dir;
        return Quaternion.AngleAxis(Random.value * 360f, dir) * tilted;
    }

    // ============================================================================================
    // TURNING — THE LIMIT THAT DECIDES EVERY CHASE
    // ============================================================================================

    /// The tightest circle a body moving at `speed` can hold under `lateralAccel` of side thrust.
    ///
    ///     r = v^2 / a
    ///
    /// Quadratic in speed, which is the whole story of missile design in one line. It is also the
    /// number to compare against a target's own turn radius: a round that cannot turn tighter than the
    /// thing it is chasing will be shaken off, no matter how much faster it is.
    public static float TurnRadius(float speed, float lateralAccel)
        => lateralAccel <= 0.0001f ? float.PositiveInfinity : (speed * speed) / lateralAccel;

    /// Degrees per second a body can swing its velocity through, given a lateral acceleration budget.
    ///
    ///     omega = a / v
    ///
    /// This is why a missile is authored as an acceleration and not as a turn rate: the turn rate is a
    /// CONSEQUENCE of how fast it happens to be going at the time. A missile still on the rail can
    /// pirouette; the same missile at full burn can barely lean. The `turnRate` on the weapon caps it,
    /// standing in for the fact that a fin or a thruster ring has a mechanical limit too.
    public static float TurnRateAt(WeaponInfo w, float speed)
    {
        if (w == null || w.lateralAccel <= 0.0001f) return 0f;
        float fromThrust = speed > 0.01f
            ? (w.lateralAccel / speed) * Mathf.Rad2Deg
            : w.turnRate;                          // at a standstill the thrust limit is meaningless
        return w.turnRate > 0f ? Mathf.Min(fromThrust, w.turnRate) : fromThrust;
    }

    // ============================================================================================
    // GUIDANCE
    // ============================================================================================

    /// The direction a guided round wants to be flying, given where its target is and how both are
    /// moving. Returns a unit vector. Nothing here integrates; the caller owns the clock.
    ///
    /// The two laws share a signature deliberately: swapping a weapon between them is one field in
    /// Weaponry, and nothing here or in the renderer changes.
    public static Vector3 GuidanceDirection(GuidanceLaw law, Vector3 pos, Vector3 vel,
                                            Vector3 targetPos, Vector3 targetVel, float navConstant,
                                            float dt)
    {
        Vector3 r = targetPos - pos;
        float range = r.magnitude;
        if (range < 0.0001f) return vel.sqrMagnitude > 1e-8f ? vel.normalized : Vector3.forward;

        if (law != GuidanceLaw.Proportional)
            return r / range;                        // pursuit: straight at it, and good luck

        // ---- proportional navigation ------------------------------------------------------------
        //
        //     omega = (R x Vrel) / (R.R)      the rate the line of sight is rotating, rad/s
        //     Vc    = -(R.Vrel) / |R|         closing speed; negative means it is getting away
        //     a_cmd = N * Vc * (omega x Rhat) an acceleration perpendicular to the sight line
        //
        // The command is a pure LATERAL acceleration — it never asks the round to speed up or slow
        // down, which is correct, because a rocket motor points backwards and a missile has no way to
        // choose its speed once it is lit.
        Vector3 vRel = targetVel - vel;
        Vector3 rHat = r / range;
        Vector3 omega = Vector3.Cross(r, vRel) / (range * range);
        float closing = -Vector3.Dot(r, vRel) / range;

        // Opening rather than closing: the target is pulling away, and the LOS-rate term would steer to
        // hold a bearing the round can no longer make good on. Fall back to pursuit so it at least
        // chases, and let fuel decide how that ends.
        if (closing <= 0.01f) return rHat;

        Vector3 accel = Vector3.Cross(omega, rHat) * (navConstant * closing);

        // ---- and this is why `dt` is a parameter -----------------------------------------------
        //
        // `accel` is an ACCELERATION and `vel` is a VELOCITY. The first version of this line added
        // them directly, which is dimensionally meaningless and happened to look fine at cruise —
        // where the two magnitudes are coincidentally similar — while being catastrophic at launch,
        // where the round is doing 6 u/s and the command is 15 u/s^2. There, `vel + accel` is
        // dominated by the acceleration term, so the requested heading came out very nearly
        // PERPENDICULAR to the round's flight and every missile in the game slewed hard sideways off
        // the rail. It failed loudly enough to catch — the crossing-target panel in
        // tools/ballistics-check.mjs went from a clean intercept to a ten-unit miss — but only once
        // the seeker cone was tightened enough to notice.
        //
        // Multiplying by the timestep makes it a velocity INCREMENT, which is what it always should
        // have been. It also means the requested turn is naturally small, so the RotateTowards clamp
        // in StepGuided stops being the thing that decides every turn and goes back to being what it
        // is meant to be: a limit that bites only when guidance asks for more than the motor has.
        if (vel.sqrMagnitude < 0.0001f) return rHat;
        Vector3 want = vel + accel * dt;
        return want.sqrMagnitude > 1e-8f ? want.normalized : rHat;
    }

    /// Is the target inside the seeker's field of view?
    ///
    /// A seeker looks out of the nose through a cone. Once the target leaves that cone the round is
    /// blind, and a blind round does not reacquire — which is the mechanic behind breaking a lock by
    /// turning hard ACROSS a missile's path rather than by running from it. A cone of 360 is a weapon
    /// that cannot be shaken; nothing in the game has one.
    public static bool InSeekerCone(Vector3 vel, Vector3 pos, Vector3 targetPos, float coneDeg)
    {
        if (coneDeg >= 179.9f) return true;
        if (vel.sqrMagnitude < 1e-8f) return true;
        Vector3 r = targetPos - pos;
        if (r.sqrMagnitude < 1e-8f) return true;
        return Vector3.Angle(vel, r) <= coneDeg;
    }

    /// The same test, but caged until the round has armed. THIS is the one to call.
    ///
    /// The distinction exists because of one specific self-inflicted wound. A cold-launched round is
    /// ejected tens of degrees off the bore on purpose — that bad initial heading is what produces the
    /// opening curve of a launch. Test the cone against it on frame one and every missile in the game
    /// declares its target out of view and goes ballistic before its motor has even lit.
    ///
    /// A real seeker is caged until the round is clear of the ship for exactly this reason. Guidance is
    /// commanded from launch either way; only the GIVE-UP test waits.
    public static bool SeekerHasLock(WeaponInfo w, float age, Vector3 vel, Vector3 pos, Vector3 targetPos)
    {
        if (w == null) return false;

        // Caged until the motor is up to speed. See WeaponInfo.seekerArmTime.
        if (age < w.seekerArmTime) return true;

        // ---- and released again in the last moments ---------------------------------------------
        //
        // Close in, the line of sight to a crossing target sweeps faster than ANY round can turn, and
        // that is geometry rather than evasion: at eight units from a target crossing at 24 u/s the
        // bearing changes at three radians a second, against a missile that can swing its nose at
        // 2.6. So the target slides out of the cone in the final half-second of every single
        // successful intercept, and a seeker that gave up there would break its lock on shots it was
        // about to land.
        //
        // It did exactly that. Before this test existed, two of the four engagements in
        // tools/ballistics-check.mjs failed with the round going ballistic a second before impact —
        // one of them missing by 1.4 units after flying a textbook intercept the whole way in. Worse,
        // widening the cone did not help at all, because the bearing genuinely runs through 180
        // degrees as the round goes past. That is what said the cone was the wrong thing to be
        // adjusting.
        //
        // A real weapon has the same problem and answers it the same way: inside the terminal phase
        // the fuse governs and the seeker stops having a vote. A third of a second of flight is the
        // window here, which scales itself sensibly — a fast missile commits from further out than a
        // slow torpedo, exactly as it should.
        float terminal = w.projectileSpeed * SeekerTerminalSeconds;
        if ((targetPos - pos).sqrMagnitude <= terminal * terminal) return true;

        return InSeekerCone(vel, pos, targetPos, w.seekerConeDeg);
    }

    /// How close to impact the seeker stops being consulted, expressed as seconds of flight.
    public const float SeekerTerminalSeconds = 0.35f;

    // ============================================================================================
    // THE MOTOR
    // ============================================================================================

    /// What a round's motor is doing at `age` seconds after launch. Three phases, and the transitions
    /// between them are the whole visual character of a missile.
    ///
    ///   BOOST    Ejected slowly, then the motor lights and it piles on speed. This is why a missile
    ///            leaves the tube looking lazy and is terrifying two seconds later.
    ///   SUSTAIN  At top speed, still burning, still able to turn.
    ///   COAST    Burned out. Holds its speed forever — vacuum — but cannot turn AT ALL, because in
    ///            space you steer by thrusting and there is nothing left to thrust with.
    ///
    /// Returns the fraction of full thrust available, 0 to 1. Turn authority is scaled by it, which is
    /// what makes burnout a real event rather than a timer expiring quietly.
    public static float ThrustFraction(WeaponInfo w, float age)
    {
        if (w == null || w.fuelTime <= 0f) return 1f;     // no motor modelled: always in control
        if (age >= w.fuelTime) return 0f;
        if (age < w.boostTime) return 1f;

        // A short taper at the end of the burn rather than a cliff, so a missile going dry visibly
        // loses its grip on the turn instead of snapping straight.
        float left = w.fuelTime - age;
        return Mathf.Clamp01(left / Mathf.Max(0.15f, w.burnoutTaper));
    }

    /// How fast a round should be going at `age` seconds. Boost ramps from the launch speed to the
    /// cruise speed; after that it holds, because nothing out here slows it down.
    public static float SpeedAt(WeaponInfo w, float age)
    {
        if (w == null) return 0f;
        if (w.boostTime <= 0.0001f) return w.projectileSpeed;
        if (age >= w.boostTime) return w.projectileSpeed;
        float k = Mathf.Clamp01(age / w.boostTime);
        // Ease rather than ramp linearly: a motor's thrust is roughly constant but the round is
        // shedding propellant mass the whole time, so it accelerates HARDER as it empties.
        return Mathf.Lerp(w.launchSpeed, w.projectileSpeed, k * k);
    }

    /// One step of a guided round's flight. Returns the new velocity; the caller integrates position.
    ///
    /// The ordering matters, and is the reason this is one function rather than three:
    ///   1. the motor decides what speed we should be doing and how much authority we have;
    ///   2. guidance decides where we would LIKE to point;
    ///   3. the turn is clamped to what that authority can actually deliver at this speed.
    ///
    /// Step 3 is where a missile loses. It never fails to work out the right answer — it fails to be
    /// able to turn that hard, which is a defeat the player can see coming and fly around.
    public static Vector3 StepGuided(WeaponInfo w, Vector3 pos, Vector3 vel,
                                     Vector3 targetPos, Vector3 targetVel, Vector3 midcoursePoint,
                                     float age, bool hasLock, float dt)
    {
        if (w == null || dt <= 0f) return vel;

        float speed = SpeedAt(w, age);
        Vector3 heading = vel.sqrMagnitude > 1e-8f ? vel.normalized : Vector3.forward;
        float thrust = ThrustFraction(w, age);

        if (w.guidance == GuidanceLaw.Unguided || thrust <= 0.001f) return heading * speed;

        Vector3 want;
        if (age < w.seekerArmTime)
        {
            // ============================================================================
            // MIDCOURSE — fly the firing solution, not the target
            //
            // Proportional navigation is a TERMINAL law. It steers by nulling the rate the sight
            // line is rotating, and both terms it multiplies — closing speed and that rate — are
            // small when the round is slow and the target is far away. Put a missile 38 degrees off
            // the bore at 8 u/s and thirty-eight units out, and ProNav asks for about twenty degrees
            // a second of correction from a round that is mechanically capable of a hundred and
            // fifty. It takes two seconds to undo the cold launch, by which time the flight is over.
            //
            // Measured, not guessed: with ProNav running from launch, the engagements in
            // tools/ballistics-check.mjs came out with EIGHT DEGREES of total control effort and
            // eighteen-unit misses. The round was not being defeated; it was never being commanded.
            //
            // So the round flies the precomputed intercept point until its seeker arms, which is what
            // a real weapon does and what the aim point was already being carried for. Steering at a
            // fixed point is a strong, unambiguous command at any speed and any range, so the launch
            // transient is gone within a few tenths of a second, and ProNav inherits a round that is
            // fast, pointed the right way, and close enough for its own terms to be large.
            // ============================================================================
            Vector3 leg = midcoursePoint - pos;
            want = leg.sqrMagnitude > 1e-8f ? leg.normalized : heading;
        }
        else if (hasLock)
        {
            want = GuidanceDirection(w.guidance, pos, vel, targetPos, targetVel, w.navConstant, dt);
        }
        else
        {
            return heading * speed;      // blind: hold what it has and coast
        }

        float maxTurn = TurnRateAt(w, speed) * thrust * Mathf.Deg2Rad * dt;
        heading = Vector3.RotateTowards(heading, want, maxTurn, 0f);
        if (heading.sqrMagnitude < 1e-8f) heading = want;
        heading.Normalize();

        return heading * speed;
    }

    // ============================================================================================
    // WHAT THE FIRING SIDE NEEDS TO KNOW BEFORE PULLING THE TRIGGER
    // ============================================================================================

    /// Roughly, can this mount's round reach that target before it runs out of life?
    ///
    /// Deliberately optimistic — it assumes a straight run at cruise speed and ignores the distance
    /// spent turning. A pessimistic estimate would have missile boats sulking at the edge of their own
    /// range and refusing perfectly good shots, and the honest cost of being wrong here is one wasted
    /// missile, which is a cost the player can watch happen and learn from.
    public static bool CanReach(WeaponInfo w, Vector3 from, Vector3 targetPos, Vector3 targetVel)
    {
        if (w == null) return false;
        if (w.projectileSpeed <= 0.001f) return true;                 // instant

        float t = InterceptTimeFor(w, from, targetPos, targetVel);
        if (t < 0f) return false;                                     // outruns it outright

        float life = w.trackTime > 0f ? w.trackTime : MaxDumbFlight;
        return t <= life;
    }

    /// How long a round with no motor model is allowed to exist. A dumb bolt that missed everything
    /// has to stop being in the list eventually.
    public const float MaxDumbFlight = 6f;

    /// The last stretch of a round's life, over which it fades out rather than blinking off.
    ///
    /// A missile that simply vanished at its track limit read as a rendering bug every single time.
    /// Guttering out over half a second reads as the motor dying, which is what it is.
    public const float DissipateTime = 0.55f;
}
