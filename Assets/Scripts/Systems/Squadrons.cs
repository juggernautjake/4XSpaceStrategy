using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// SQUADRONS — a control group that remembers what it is
//
// ControlGroups gave the number keys their RTS meaning: bind a selection to 1-9, press the number to
// get it back. That is a shortcut, and a shortcut has no opinions — the moment a fleet is supposed to
// hold a FORMATION, obey a standing PROTOCOL or walk a PATROL, those are facts about a standing
// group rather than about whichever ships happen to be highlighted, and they need somewhere to live.
//
// So a squadron is a control group plus that state. The membership list still lives in ControlGroups
// (ids, pruned on read, saved with the game); everything a squadron KNOWS lives here, keyed by the
// same 1-9 slot and saved alongside it.
//
// ---- MEMBERSHIP IS EXCLUSIVE ---------------------------------------------------------------
//
// A ship belongs to at most one squadron, and binding it into a new one takes it out of its old one.
// Classic RTS control groups overlap freely and that is fine when a group is only a shortcut — but
// "the squadron's formation" has no answer for a ship in two squadrons at once, and neither does
// "the squadron's protocol", or which rally point a damaged ship runs to. Every question this file
// exists to answer inherits the ambiguity, so the ambiguity goes.
//
// It also makes the roster verbs mean something: DETACH takes ships out of whatever they are in, and
// SPLIT promotes a sub-selection into a slot of its own — which is exactly "select a few ships inside
// a big group and make them their own group", asked for by name.
// ============================================================================================

/// How a squadron arranges itself under way. Ordinals are SERIALIZED — append only.
public enum FleetFormationKind
{
    Wedge,       // leader ahead, pairs sweeping back. The all-round default
    LineAbreast, // one rank, everything bearing forward. Offensive
    LineAstern,  // single file. Narrow frontage down a contested lane
    Echelon,     // a diagonal stagger, for coming in off the flank
    Screen,      // cheap hulls arc AHEAD of the valuable ones. Composition-aware
    Globe,       // escorts on a shell around the capitals. Defensive
    Free         // no formation; every ship flies its own line
}

/// What a squadron does when it meets something, without being told again. Append only.
public enum SquadronProtocol
{
    Defensive,      // engage what comes to you, do not chase. The default
    Aggressive,     // engage anything detected, and pursue
    HoldFire,       // never initiate — for slipping past what you cannot beat
    EvadeAndReport, // on contact: break off, run for the rally point, raise the alarm
    Escort,         // hold station on another squadron and screen it
    WithdrawIfHurt  // detach below a hull threshold and head for a friendly world
}

/// How a patrol walks its waypoints. Append only.
public enum PatrolMode { Loop, PingPong }

public class SquadronOrders
{
    public string name = "";
    public FleetFormationKind formation = FleetFormationKind.Wedge;
    public SquadronProtocol protocol = SquadronProtocol.Defensive;

    /// Below this fraction of full hull a WithdrawIfHurt ship breaks off. Ignored by other protocols.
    public float withdrawAt = 0.35f;

    /// The squadron this one escorts (1-9), or 0. Only meaningful under Escort.
    public int escorting = 0;

    /// Where a shaken or newly built ship goes. Null until set.
    public bool hasRally = false;
    public Vector3 rally = Vector3.zero;

    /// The patrol route, in order. Empty when the squadron is not patrolling.
    public List<Vector3> patrol = new List<Vector3>();
    public PatrolMode patrolMode = PatrolMode.Loop;

    /// Which waypoint the squadron is heading for, and which way it is walking the list (PingPong).
    public int patrolLeg = 0;
    public int patrolDir = 1;

    public bool Patrolling => patrol != null && patrol.Count >= 2;
}

public static class Squadrons
{
    public const int Count = ControlGroups.Count;   // the same 1-9 slots

    static readonly SquadronOrders[] orders = new SquadronOrders[Count + 1];

    public static event System.Action OnChanged;

    static Squadrons()
    {
        for (int i = 0; i <= Count; i++) orders[i] = new SquadronOrders();
    }

    public static bool Valid(int g) => g >= 1 && g <= Count;

    public static SquadronOrders Of(int g) => Valid(g) ? orders[g] : orders[0];

    /// The squadron a unit belongs to (0 = none). Membership still lives in ControlGroups.
    public static int Of(Unit u) => ControlGroups.GroupOf(u);

    public static SquadronOrders OrdersFor(Unit u)
    {
        int g = Of(u);
        return Valid(g) ? orders[g] : null;
    }

    /// Is this ship under orders not to shoot first?
    ///
    /// Only ever true for a ship in a squadron set to Hold Fire — a ship with no squadron has no
    /// standing orders and fights on CombatManager's ordinary proximity rule, which is the behaviour
    /// everything in the game had before squadrons existed and must remain the default.
    ///
    /// It suppresses INITIATING only. A ship holding fire still runs its point defence, because
    /// declining to start a fight and declining to swat a missile already in the air are not the same
    /// decision, and no player choosing "slip past without provoking them" is choosing to be hit.
    public static bool HoldingFire(Unit u)
        => OrdersFor(u)?.protocol == SquadronProtocol.HoldFire;

    /// The squadron's display name — the one the player typed, or "Squadron 3".
    public static string NameOf(int g)
        => !Valid(g) ? "" : (string.IsNullOrWhiteSpace(orders[g].name) ? $"Squadron {g}" : orders[g].name);

    public static void Rename(int g, string name)
    {
        if (!Valid(g)) return;
        orders[g].name = name ?? "";
        OnChanged?.Invoke();
    }

    public static void SetFormation(int g, FleetFormationKind f)
    {
        if (!Valid(g)) return;
        orders[g].formation = f;
        OnChanged?.Invoke();
    }

    public static void SetProtocol(int g, SquadronProtocol p)
    {
        if (!Valid(g)) return;
        orders[g].protocol = p;
        OnChanged?.Invoke();
    }

    public static void SetRally(int g, Vector3 pos)
    {
        if (!Valid(g)) return;
        orders[g].hasRally = true;
        orders[g].rally = pos;
        OnChanged?.Invoke();
    }

    public static void ClearRally(int g)
    {
        if (!Valid(g)) return;
        orders[g].hasRally = false;
        OnChanged?.Invoke();
    }

    public static void SetPatrol(int g, List<Vector3> route, PatrolMode mode)
    {
        if (!Valid(g)) return;
        var o = orders[g];
        o.patrol = route != null ? new List<Vector3>(route) : new List<Vector3>();
        o.patrolMode = mode;
        o.patrolLeg = 0;
        o.patrolDir = 1;
        OnChanged?.Invoke();
    }

    public static void ClearPatrol(int g)
    {
        if (!Valid(g)) return;
        orders[g].patrol.Clear();
        orders[g].patrolLeg = 0;
        orders[g].patrolDir = 1;
        OnChanged?.Invoke();
    }

    /// Wipe a squadron's standing orders — called when its slot is emptied, so slot 3 does not hand a
    /// brand-new fleet the patrol route and the withdraw threshold the LAST fleet in slot 3 was given.
    public static void ResetSlot(int g)
    {
        if (!Valid(g)) return;
        orders[g] = new SquadronOrders();
        OnChanged?.Invoke();
    }

    public static void Reset()
    {
        for (int i = 0; i <= Count; i++) orders[i] = new SquadronOrders();
        OnChanged?.Invoke();
    }

    // ---- Save / load ---------------------------------------------------------------------------

    public static List<SquadronOrdersDTO> Export()
    {
        var list = new List<SquadronOrdersDTO>();
        for (int g = 1; g <= Count; g++)
        {
            var o = orders[g];
            bool interesting = !string.IsNullOrEmpty(o.name)
                || o.formation != FleetFormationKind.Wedge
                || o.protocol != SquadronProtocol.Defensive
                || o.hasRally || o.patrol.Count > 0 || o.escorting != 0;
            if (!interesting) continue;

            var d = new SquadronOrdersDTO
            {
                group = g,
                name = o.name,
                formation = (int)o.formation,
                protocol = (int)o.protocol,
                withdrawAt = o.withdrawAt,
                escorting = o.escorting,
                hasRally = o.hasRally,
                rallyX = o.rally.x, rallyY = o.rally.y, rallyZ = o.rally.z,
                patrolMode = (int)o.patrolMode,
                patrolLeg = o.patrolLeg,
                patrolDir = o.patrolDir,
            };
            foreach (var p in o.patrol) { d.patrolX.Add(p.x); d.patrolY.Add(p.y); d.patrolZ.Add(p.z); }
            list.Add(d);
        }
        return list;
    }

    public static void Import(List<SquadronOrdersDTO> dtos)
    {
        for (int i = 0; i <= Count; i++) orders[i] = new SquadronOrders();
        if (dtos != null)
            foreach (var d in dtos)
            {
                if (!Valid(d.group)) continue;
                var o = orders[d.group];
                o.name = d.name ?? "";
                o.formation = (FleetFormationKind)Mathf.Clamp(d.formation, 0, (int)FleetFormationKind.Free);
                o.protocol = (SquadronProtocol)Mathf.Clamp(d.protocol, 0, (int)SquadronProtocol.WithdrawIfHurt);
                o.withdrawAt = Mathf.Clamp01(d.withdrawAt <= 0f ? 0.35f : d.withdrawAt);
                o.escorting = d.escorting;
                o.hasRally = d.hasRally;
                o.rally = new Vector3(d.rallyX, d.rallyY, d.rallyZ);
                o.patrolMode = (PatrolMode)Mathf.Clamp(d.patrolMode, 0, (int)PatrolMode.PingPong);
                o.patrolLeg = d.patrolLeg;
                o.patrolDir = d.patrolDir == 0 ? 1 : d.patrolDir;
                o.patrol.Clear();
                int n = Mathf.Min(d.patrolX.Count, Mathf.Min(d.patrolY.Count, d.patrolZ.Count));
                for (int i = 0; i < n; i++) o.patrol.Add(new Vector3(d.patrolX[i], d.patrolY[i], d.patrolZ[i]));
            }
        OnChanged?.Invoke();
    }
}
