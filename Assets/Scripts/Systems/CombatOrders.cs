using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// TELLING SHIPS WHAT TO SHOOT
//
// Combat has always been entirely automatic. A warship inside weapons range of something it hates
// picks the most dangerous thing it can reach and opens fire, and the player's only input is where
// they sent it. That rule is deliberate and it stays — see CombatManager's header for why an explicit
// attack order would make escorting pointless and ancient defences impossible.
//
// But "the player cannot MAKE a fight happen" and "the player cannot influence a fight that is
// already happening" are two different statements, and only the first one was intended. Automatic
// target selection with no override means the one decision that decides most engagements — which
// enemy dies first — belongs to a heuristic.
//
// ---- WHY CONCENTRATION IS THE DECISION -------------------------------------------------------
//
// Damage output is proportional to hulls alive. Six ships shooting six different targets destroy
// nothing for most of the engagement and take six ships' worth of return fire the whole way; six
// ships shooting ONE target remove a sixth of the enemy's guns every time one dies. That difference
// compounds, and it is the whole of tactical combat in a game like this.
//
// The auto-targeter cannot do it. PickTarget is per ship and threat-weighted, so a squadron facing
// two identical cruisers naturally SPLITS — each ship independently picks whichever is marginally
// closer to it. It is doing exactly what it was told and producing the worst available outcome.
//
// ---- WHAT THIS IS AND IS NOT ------------------------------------------------------------------
//
// It is an OVERRIDE, not a replacement. A ship with no focus order behaves precisely as it always
// has. A focus order is consulted first and falls straight through to the automatic behaviour when
// the designated target is dead, out of range, or no longer hostile — because a ship that sits idle
// holding fire at something it cannot reach is worse than one that never took the order.
//
// Three levels, most specific first: a ship's own order beats its squadron's, and a squadron's beats
// its fleet's. That ordering is what lets a player say "the whole fleet on the dreadnought, except
// this one frigate which is chasing the transport" without inventing exceptions.
//
// ---- AND WHY NONE OF IT IS SAVED --------------------------------------------------------------
//
// Same reasoning as CombatManager's cooldowns and Magazines' load state: this is keyed on live Unit
// references, and a dictionary of them held across a galaxy replacement would keep dead ships alive
// forever. A save restores a fleet with no focus orders, which is the correct default — a designated
// target from twenty minutes ago is not information anybody wants restored.
// ============================================================================================
public static class CombatOrders
{
    // ---- focus ---------------------------------------------------------------------------------
    static readonly Dictionary<Unit, Unit> shipFocus = new Dictionary<Unit, Unit>();
    static readonly Dictionary<int, Unit> squadronFocus = new Dictionary<int, Unit>();
    static readonly Dictionary<int, Unit> fleetFocus = new Dictionary<int, Unit>();

    // ---- hold position -------------------------------------------------------------------------
    static readonly HashSet<Unit> holding = new HashSet<Unit>();

    /// Raised whenever anything here changes, so the command bar can re-tint without polling.
    public static System.Action OnChanged;

    static void Changed() => OnChanged?.Invoke();

    // ============================================================================================
    // ASKING
    // ============================================================================================

    /// What this ship has been told to shoot, or null for "use your judgement".
    ///
    /// Resolved most-specific-first and VALIDATED on the way out rather than pruned on the way in. A
    /// target can die, be destroyed by somebody else, change hands or leave the map at any moment, and
    /// an order that outlived its target has to evaporate silently rather than leave a ship aiming at
    /// a null reference.
    public static Unit FocusFor(Unit shooter)
    {
        if (shooter == null) return null;

        if (shipFocus.TryGetValue(shooter, out var own) && Valid(shooter, own)) return own;

        int g = ControlGroups.GroupOf(shooter);
        if (g >= 1 && squadronFocus.TryGetValue(g, out var sq) && Valid(shooter, sq)) return sq;

        int f = Fleets.FleetOf(g);
        if (f >= 1 && fleetFocus.TryGetValue(f, out var fl) && Valid(shooter, fl)) return fl;

        return null;
    }

    /// Is this still something `shooter` could legitimately be told to shoot?
    static bool Valid(Unit shooter, Unit target)
        => target != null && !target.IsDestroyed && CombatManager.AreHostile(shooter, target);

    /// The designated target for one squadron, for the UI to display. Not validated — the UI wants to
    /// show a struck-through name when the target has died, rather than silently forgetting it.
    public static Unit SquadronTarget(int g)
        => g >= 1 && squadronFocus.TryGetValue(g, out var t) ? t : null;

    public static Unit FleetTarget(int f)
        => f >= 1 && fleetFocus.TryGetValue(f, out var t) ? t : null;

    public static Unit ShipTarget(Unit u)
        => u != null && shipFocus.TryGetValue(u, out var t) ? t : null;

    /// Does anything in this selection have a focus order of its own?
    public static bool AnyFocused(IReadOnlyList<Unit> sel)
    {
        if (sel == null) return false;
        for (int i = 0; i < sel.Count; i++) if (FocusFor(sel[i]) != null) return true;
        return false;
    }

    // ============================================================================================
    // ORDERING
    // ============================================================================================

    /// Every selected ship concentrates on one target, individually.
    ///
    /// Per SHIP rather than per squadron, because a selection is whatever the player has dragged a box
    /// around and may be half of one squadron and a third of another. Writing the order onto the
    /// squadron would silently command ships the player did not select.
    public static void FocusSelection(IReadOnlyList<Unit> sel, Unit target)
    {
        if (sel == null || target == null) return;
        for (int i = 0; i < sel.Count; i++)
        {
            var u = sel[i];
            if (u == null || u.IsDestroyed) continue;
            if (!CombatManager.AreHostile(u, target)) continue;   // never order a ship to shoot a friend
            shipFocus[u] = target;
        }
        Changed();
    }

    public static void FocusSquadron(int g, Unit target)
    {
        if (g < 1 || target == null) return;
        squadronFocus[g] = target;
        // The squadron's order supersedes anything its members chose individually. Leaving stale
        // per-ship orders in place would make "the whole squadron on that dreadnought" quietly mean
        // "all of it except the two ships I gave separate orders to ten minutes ago".
        foreach (var u in ControlGroups.Members(g)) shipFocus.Remove(u);
        Changed();
    }

    public static void FocusFleet(int f, Unit target)
    {
        if (f < 1 || target == null) return;
        fleetFocus[f] = target;
        foreach (int g in Fleets.SquadronsIn(f))
        {
            squadronFocus.Remove(g);
            foreach (var u in ControlGroups.Members(g)) shipFocus.Remove(u);
        }
        Changed();
    }

    /// Back to picking their own targets.
    public static void ReleaseSelection(IReadOnlyList<Unit> sel)
    {
        if (sel == null) return;
        for (int i = 0; i < sel.Count; i++)
        {
            var u = sel[i];
            if (u == null) continue;
            shipFocus.Remove(u);
            int g = ControlGroups.GroupOf(u);
            if (g >= 1)
            {
                squadronFocus.Remove(g);
                int f = Fleets.FleetOf(g);
                if (f >= 1) fleetFocus.Remove(f);
            }
        }
        Changed();
    }

    // ============================================================================================
    // HOLD POSITION
    // ============================================================================================

    /// Ships told to stay exactly where they are.
    ///
    /// Distinct from "has no orders": a ship with nothing to do will still be moved by its squadron's
    /// AI — an aggressive squadron chases contacts, an escort closes the gap on its ward, a patrol
    /// walks its route. Holding is the instruction that stops all of that, which is what you want on a
    /// picket, on a ship holding a chokepoint, or on anything you have deliberately parked inside a
    /// minefield of somebody else's making.
    ///
    /// It does NOT stop the ship firing. A held ship is stationary, not passive — HoldFire is the
    /// protocol for passive, and the two are separate on purpose because "stay there" and "do not
    /// shoot" are different orders and a player will want each without the other.
    public static bool Holding(Unit u) => u != null && holding.Contains(u);

    public static void SetHold(IReadOnlyList<Unit> sel, bool hold)
    {
        if (sel == null) return;
        var um = UnitManager.Instance;
        for (int i = 0; i < sel.Count; i++)
        {
            var u = sel[i];
            if (u == null || u.IsDestroyed) continue;
            if (hold)
            {
                holding.Add(u);
                // Holding means holding NOW, not after finishing the trip it was already on.
                um?.StopAll(u);
            }
            else holding.Remove(u);
        }
        Changed();
    }

    /// Any explicit player move order releases the hold.
    ///
    /// Called from UnitManager's order path rather than from every button that might issue one. A
    /// player who right-clicks a destination has unambiguously changed their mind about the ship
    /// staying put, and making them clear the hold first would be a modal state they did not ask for.
    public static void ReleaseHold(IReadOnlyList<Unit> group)
    {
        if (group == null) return;
        bool any = false;
        for (int i = 0; i < group.Count; i++)
            if (group[i] != null && holding.Remove(group[i])) any = true;
        if (any) Changed();
    }

    public static bool AnyHolding(IReadOnlyList<Unit> sel)
    {
        if (sel == null) return false;
        for (int i = 0; i < sel.Count; i++) if (Holding(sel[i])) return true;
        return false;
    }

    // ============================================================================================
    // WITHDRAW
    // ============================================================================================

    /// Break off and run. Returns how many ships were actually sent somewhere.
    ///
    /// Where to is answered in the order a commander would answer it: the squadron's own rally point
    /// if one is set, otherwise the nearest world the empire holds, otherwise nowhere — and "nowhere"
    /// is reported rather than silently doing nothing, because a withdraw order that quietly fails is
    /// the worst possible outcome of pressing a button labelled withdraw.
    public static int Withdraw(IReadOnlyList<Unit> sel)
    {
        var um = UnitManager.Instance;
        if (um == null || sel == null || sel.Count == 0) return 0;

        int sent = 0;
        for (int i = 0; i < sel.Count; i++)
        {
            var u = sel[i];
            if (u == null || u.IsDestroyed) continue;

            holding.Remove(u);
            one.Clear(); one.Add(u);

            int g = ControlGroups.GroupOf(u);
            var o = g >= 1 ? Squadrons.Of(g) : null;

            if (o != null && o.hasRally)
            {
                um.IssueMovePoint(one, o.rally, false);
                sent++;
                continue;
            }

            var home = NearestHold(u);
            if (home != null) { um.IssueMove(one, home, false); sent++; }
        }
        if (sent > 0) Changed();
        return sent;
    }

    static readonly List<Unit> one = new List<Unit>(1);

    /// The closest world this ship's owner actually holds. Settled first, then merely claimed — a
    /// colony can rearm and repair a ship and a beacon on an empty rock cannot, so a settled world
    /// further away is still the better answer.
    static CelestialBody NearestHold(Unit u)
    {
        // SystemContext.AllBodies(), not SystemContext.Galaxy: a Galaxy is a bag of SYSTEMS, and each
        // system holds planets which in turn hold moons. AllBodies is the walk that flattens all three
        // levels, and it is what every other sweep over the map uses.
        if (SystemContext.Galaxy == null || u == null) return null;
        var bodies = SystemContext.AllBodies();

        Vector3 p = CombatManager.PosOf(u);
        CelestialBody bestSettled = null, bestClaimed = null;
        float dS = float.MaxValue, dC = float.MaxValue;

        foreach (var b in bodies)
        {
            if (b == null || b.owner == null || b.owner != u.owner) continue;
            if (b.visualObject == null) continue;
            float d = Vector3.SqrMagnitude(b.visualObject.transform.position - p);
            if (b.settled) { if (d < dS) { dS = d; bestSettled = b; } }
            else            { if (d < dC) { dC = d; bestClaimed = b; } }
        }
        return bestSettled ?? bestClaimed;
    }

    // ============================================================================================
    // HOUSEKEEPING
    // ============================================================================================

    /// A ship died. Forget it, whether it was doing the shooting or being shot at.
    ///
    /// Called from CombatManager.Destroy rather than swept for periodically, because a dictionary
    /// keyed on a dead Unit is exactly the leak this whole file's header warns about — and a squadron
    /// still holding a focus order on a corpse would fall through to auto-targeting anyway, so the
    /// entry is pure cost.
    public static void Forget(Unit u)
    {
        if (u == null) return;
        bool any = shipFocus.Remove(u) | holding.Remove(u);

        // And anything aimed AT it. Iterating to find them is fine: these dictionaries hold one entry
        // per squadron and fleet with a standing order, which is single digits.
        any |= DropTarget(shipFocus, u);
        any |= DropTargetInt(squadronFocus, u);
        any |= DropTargetInt(fleetFocus, u);
        if (any) Changed();
    }

    static bool DropTarget(Dictionary<Unit, Unit> map, Unit dead)
    {
        stale.Clear();
        foreach (var kv in map) if (kv.Value == dead) stale.Add(kv.Key);
        foreach (var k in stale) map.Remove(k);
        return stale.Count > 0;
    }

    static bool DropTargetInt(Dictionary<int, Unit> map, Unit dead)
    {
        staleInt.Clear();
        foreach (var kv in map) if (kv.Value == dead) staleInt.Add(kv.Key);
        foreach (var k in staleInt) map.Remove(k);
        return staleInt.Count > 0;
    }

    static readonly List<Unit> stale = new List<Unit>();
    static readonly List<int> staleInt = new List<int>();

    /// Forget everything. Called when a galaxy is replaced.
    public static void ResetAll()
    {
        shipFocus.Clear();
        squadronFocus.Clear();
        fleetFocus.Clear();
        holding.Clear();
        Changed();
    }
}
