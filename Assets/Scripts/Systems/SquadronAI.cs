using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// STANDING ORDERS — what a squadron does when nobody is watching
//
// CombatManager's rule is that ships fight when they are near something they hate, and that rule is
// load-bearing: it means combat happens whether or not the player is looking at it. What it does NOT
// decide is where a squadron puts itself, and that is the whole of the difference between a fighter
// wing and a scout.
//
// This is that layer. Every squadron carries a protocol (see Squadrons), and once a second this asks
// each one what it can see and lets the protocol answer. Nothing here fires a gun — it issues the
// same move orders the player issues, through the same UnitManager entry points, so a squadron acting
// on its own initiative behaves exactly like one being flown by hand and there is no second movement
// system to keep in step with the first.
//
// ---- WHY ONCE A SECOND ---------------------------------------------------------------------
//
// Contact is a question about distances of tens of units between fleets that cross them in tens of
// seconds. Asking sixty times a second buys nothing and costs a full O(ships x hostiles) sweep every
// frame. Asking once a second is still four times faster than a scout can cross its own sensor range.
//
// ---- WHY IT ONLY EVER ORDERS THE WHOLE SQUADRON --------------------------------------------
//
// Because a squadron that half-charges and half-runs is neither, and because the formation code
// upstream assumes a fleet shares one course. The single exception is WithdrawIfHurt, which is a
// per-ship decision by definition — one wounded cruiser going home is exactly the case it exists for
// — and it DETACHES that ship from the squadron before sending it, so the ship it leaves behind does
// not keep a station in a formation it is no longer flying in.
// ============================================================================================
public class SquadronAI : MonoBehaviour
{
    public static SquadronAI Instance;

    /// How often standing orders are re-evaluated, in seconds of game time.
    const float ThinkInterval = 1.0f;

    /// How far a squadron notices a hostile. Deliberately WIDER than the longest weapon in the game
    /// (CombatManager.MaxEngagementRange is 48), because both the protocols that react to contact need
    /// to react BEFORE contact: an aggressive wing wants to close on something that has not shot yet,
    /// and a scout that only notices trouble once it is being hit has already failed.
    const float SensorRange = 74f;

    /// How near an aggressive squadron tries to get. Inside weapons range, not on top of the target —
    /// it only has to close enough for CombatManager to take over.
    const float InterceptStandoff = 18f;

    /// A squadron will not re-issue the same reaction inside this many seconds. Without it an
    /// aggressive wing re-orders itself at every think while its target drifts, and never actually
    /// completes a move.
    const float ReactionCooldown = 6f;

    readonly float[] nextReaction = new float[Squadrons.Count + 1];

    /// Which squadrons have already raised the alarm about their current contact, so an Evade-and-
    /// Report picket reports ONCE per encounter rather than once a second all the way home.
    readonly bool[] reported = new bool[Squadrons.Count + 1];

    float thinkIn;

    public static void Create()
    {
        if (Instance != null) return;
        Instance = new GameObject("SquadronAI").AddComponent<SquadronAI>();
    }

    void Awake() { Instance = this; }

    public void Reset()
    {
        for (int i = 0; i <= Squadrons.Count; i++) { nextReaction[i] = 0f; reported[i] = false; }
        thinkIn = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int g = 1; g <= Squadrons.Count; g++)
            if (nextReaction[g] > 0f) nextReaction[g] -= dt;

        thinkIn -= dt;
        if (thinkIn > 0f) return;
        thinkIn = ThinkInterval;

        var um = UnitManager.Instance;
        if (um == null) return;

        for (int g = 1; g <= Squadrons.Count; g++)
        {
            var members = ControlGroups.Members(g);
            if (members.Count == 0) { reported[g] = false; continue; }

            var orders = Squadrons.Of(g);
            Think(um, g, members, orders);
        }
    }

    void Think(UnitManager um, int g, List<Unit> members, SquadronOrders orders)
    {
        // WithdrawIfHurt is checked first and separately: it is per-ship, and a ship that is leaving
        // should leave whatever else the squadron is in the middle of.
        if (orders.protocol == SquadronProtocol.WithdrawIfHurt)
            WithdrawWounded(um, g, members, orders);

        Vector3 centre = CentreOf(um, members);
        Unit contact = NearestHostile(um, members, centre, out float contactRange);

        if (contact == null)
        {
            reported[g] = false;
            // Nothing in sight: fall through to the standing job, which is the patrol if there is one.
            AdvancePatrol(um, g, members, orders);
            return;
        }

        switch (orders.protocol)
        {
            case SquadronProtocol.Aggressive:
                Intercept(um, g, members, contact);
                break;

            case SquadronProtocol.EvadeAndReport:
                EvadeAndReport(um, g, members, orders, contact, contactRange);
                break;

            case SquadronProtocol.Escort:
                HoldStationOnEscort(um, g, members, orders);
                break;

            // Defensive and HoldFire both stand their ground. Defensive lets CombatManager do its work
            // when the enemy closes; HoldFire additionally refuses to shoot first (see HoldingFire).
            // Neither moves, which is the point of both.
            default:
                AdvancePatrol(um, g, members, orders);
                break;
        }
    }

    // ---- The protocols ---------------------------------------------------------------------------

    void Intercept(UnitManager um, int g, List<Unit> members, Unit contact)
    {
        if (nextReaction[g] > 0f) return;

        // Already closing on it? Leave it alone. Re-issuing the order every few seconds resets the
        // burn each time and the squadron crawls.
        Vector3 target = um.UnitPos(contact);
        bool closing = false;
        foreach (var u in members)
            if (u.status == UnitStatus.Traveling && Vector3.Distance(u.travelTo, target) < InterceptStandoff * 2f)
            { closing = true; break; }
        if (closing) return;

        // Stop short of the target rather than flying into it — close enough for the guns, which is all
        // an intercept has to achieve.
        Vector3 from = CentreOf(um, members);
        Vector3 dir = target - from;
        Vector3 stop = dir.magnitude <= InterceptStandoff ? from
                                                          : target - dir.normalized * InterceptStandoff;

        var able = Movable(members);
        if (able.Count == 0) return;

        um.IssueMovePoint(able, stop, false);
        nextReaction[g] = ReactionCooldown;
    }

    void EvadeAndReport(UnitManager um, int g, List<Unit> members, SquadronOrders orders,
                        Unit contact, float range)
    {
        // Report first — the alarm is the point of the protocol, and it goes up even if the squadron
        // turns out to be cornered and cannot run.
        if (!reported[g])
        {
            reported[g] = true;
            string what = contact.Info != null ? contact.Info.name : "hostile";
            string who = contact.owner != null ? contact.owner.name : "an unknown power";
            NotificationManager.Instance?.Push(
                $"{Squadrons.NameOf(g)} — contact",
                $"Sighted a {what} of {who} at {range:F0} units. Breaking off and returning as ordered.",
                null, NotifKind.Danger);
            SimpleAudio.Instance?.PlayClick();
        }

        if (nextReaction[g] > 0f) return;

        var able = Movable(members);
        if (able.Count == 0) return;

        // Run for the rally point if one was set, and home if not. Home is the honest default: a scout
        // with nowhere named to run to should come back rather than pick a direction.
        if (orders.hasRally) um.IssueMovePoint(able, orders.rally, false);
        else um.SendUnitsHome(able);

        nextReaction[g] = ReactionCooldown;
    }

    void HoldStationOnEscort(UnitManager um, int g, List<Unit> members, SquadronOrders orders)
    {
        if (!Squadrons.Valid(orders.escorting) || orders.escorting == g) return;
        if (nextReaction[g] > 0f) return;

        var ward = ControlGroups.Members(orders.escorting);
        if (ward.Count == 0) return;

        Vector3 wardPos = CentreOf(um, ward);
        Vector3 here = CentreOf(um, members);

        // Close only when the gap has actually opened. An escort that re-orders itself every second is
        // an escort that never arrives.
        if (Vector3.Distance(here, wardPos) < InterceptStandoff) return;

        var able = Movable(members);
        if (able.Count == 0) return;

        um.IssueMovePoint(able, wardPos, false);
        nextReaction[g] = ReactionCooldown;
    }

    void WithdrawWounded(UnitManager um, int g, List<Unit> members, SquadronOrders orders)
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            var u = members[i];
            if (u == null || u.IsDestroyed) continue;
            if (u.HealthFraction > Mathf.Clamp01(orders.withdrawAt)) continue;
            if (u.status == UnitStatus.Traveling) continue;    // already going somewhere

            // Out of the squadron before it is sent, so the formation stops holding a station for a
            // ship that is no longer flying in it.
            var one = new List<Unit> { u };
            ControlGroups.Detach(one);
            members.RemoveAt(i);

            if (orders.hasRally) um.IssueMovePoint(one, orders.rally, false);
            else um.SendUnitsHome(one);

            NotificationManager.Instance?.Push(
                $"{u.name} withdrawing",
                $"Down to {(u.HealthFraction * 100f):F0}% hull — detached from {Squadrons.NameOf(g)} " +
                "and heading for safety, as its protocol orders.",
                null, NotifKind.Danger);
        }
    }

    // ---- Patrol ----------------------------------------------------------------------------------
    //
    // A patrol is not an order in the queue, it is a STANDING state: the squadron is given the next
    // waypoint whenever it has stopped moving, so the route runs until it is cancelled rather than
    // falling off the end of a queue. Loop walks the list and wraps; ping-pong turns round at each end.

    void AdvancePatrol(UnitManager um, int g, List<Unit> members, SquadronOrders orders)
    {
        if (!orders.Patrolling) return;

        // Still under way? Then it is still walking the current leg.
        foreach (var u in members) if (u.status == UnitStatus.Traveling) return;

        var able = Movable(members);
        if (able.Count == 0) return;

        int n = orders.patrol.Count;
        orders.patrolLeg = Mathf.Clamp(orders.patrolLeg, 0, n - 1);

        Vector3 leg = orders.patrol[orders.patrolLeg];

        // Already standing on this waypoint — step to the next one instead of re-ordering a zero-length
        // move, which would otherwise leave the patrol stuck on its first leg forever.
        if (Vector3.Distance(CentreOf(um, members), leg) < 2f)
        {
            StepLeg(orders, n);
            leg = orders.patrol[orders.patrolLeg];
        }

        um.IssueMovePoint(able, leg, false);
        StepLeg(orders, n);
    }

    static void StepLeg(SquadronOrders o, int n)
    {
        if (n <= 1) { o.patrolLeg = 0; return; }

        if (o.patrolMode == PatrolMode.Loop)
        {
            o.patrolLeg = (o.patrolLeg + 1) % n;
            return;
        }

        // Ping-pong: turn round at each end rather than wrapping, so the route is walked back the way
        // it came and a picket line is covered in both directions.
        int next = o.patrolLeg + o.patrolDir;
        if (next >= n) { o.patrolDir = -1; next = n - 2; }
        else if (next < 0) { o.patrolDir = 1; next = Mathf.Min(1, n - 1); }
        o.patrolLeg = Mathf.Clamp(next, 0, n - 1);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    static List<Unit> Movable(List<Unit> members)
    {
        var l = new List<Unit>();
        foreach (var u in members)
            if (u != null && !u.IsDestroyed && !u.Info.isStation) l.Add(u);
        return l;
    }

    static Vector3 CentreOf(UnitManager um, List<Unit> members)
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (var u in members) { if (u == null) continue; sum += um.UnitPos(u); n++; }
        return n > 0 ? sum / n : Vector3.zero;
    }

    /// The nearest hostile any member of the squadron can see, and how far it is from the squadron's
    /// centre. Nearest rather than most dangerous: this decides whether to REACT, and a scout should
    /// run from the first thing it sees rather than wait for something scarier.
    static Unit NearestHostile(UnitManager um, List<Unit> members, Vector3 centre, out float range)
    {
        range = float.MaxValue;
        Unit best = null;
        if (members.Count == 0) return null;

        var probe = members[0];
        foreach (var o in um.Units)
        {
            if (o == null || o.IsDestroyed) continue;
            if (o.hideReason != HideReason.None) continue;
            if (!CombatManager.AreHostile(probe, o)) continue;

            float d = Vector3.Distance(centre, um.UnitPos(o));
            if (d > SensorRange || d >= range) continue;
            range = d; best = o;
        }
        return best;
    }
}
