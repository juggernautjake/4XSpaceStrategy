using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// FLEETS — a tier above squadrons
//
// A squadron is the unit of ORDERS: one formation, one protocol, one course. That is the right size
// for a thing you fly, and the wrong size for a thing you own. By the mid-game a player has a home
// guard, a strike force and a survey arm, each of them several squadrons, and asking them to keep
// track of which of the nine numbers belongs to which is asking them to hold the org chart in their
// head.
//
// So a fleet is a NAMED BAG OF SQUADRONS and nothing more. It gives no orders of its own — a fleet
// order would immediately contradict the squadron orders underneath it, and then there would be two
// answers to "what formation is this ship flying in". What it gives is a place to read the whole
// force at once, and a handle for selecting it (see FleetRosterPanel).
//
// MEMBERSHIP IS EXCLUSIVE at this tier too, for the same reason it is at the squadron tier: a
// squadron in two fleets makes the roster a lie about strength, since its ships would be counted
// twice in a readout whose entire job is telling the player how much force they have.
// ============================================================================================
public static class Fleets
{
    /// Six is enough to organise nine squadrons without the list itself becoming the problem.
    public const int Count = 6;

    class Fleet
    {
        public string name = "";
        public readonly List<int> squadrons = new List<int>();
    }

    static readonly Fleet[] fleets = new Fleet[Count + 1];   // 1..Count; [0] unused

    public static event System.Action OnChanged;

    static Fleets()
    {
        for (int i = 0; i <= Count; i++) fleets[i] = new Fleet();
    }

    public static bool Valid(int f) => f >= 1 && f <= Count;

    public static string NameOf(int f)
        => !Valid(f) ? "" : (string.IsNullOrWhiteSpace(fleets[f].name) ? $"Fleet {f}" : fleets[f].name);

    /// The name the player actually typed, empty when they have not — as against NameOf, which falls
    /// back to "Fleet 2". The rename prompt wants this one: pre-filling the field with the fallback
    /// would make every unnamed fleet look as though it had already been named.
    public static string RawNameOf(int f) => Valid(f) ? fleets[f].name : "";

    public static void Rename(int f, string name)
    {
        if (!Valid(f)) return;
        fleets[f].name = name ?? "";
        OnChanged?.Invoke();
    }

    /// The squadron numbers in this fleet, in ascending order.
    public static List<int> SquadronsIn(int f)
    {
        var l = new List<int>();
        if (!Valid(f)) return l;
        foreach (int g in fleets[f].squadrons) l.Add(g);
        l.Sort();
        return l;
    }

    /// Which fleet a squadron belongs to, or 0.
    public static int FleetOf(int squadron)
    {
        for (int f = 1; f <= Count; f++)
            if (fleets[f].squadrons.Contains(squadron)) return f;
        return 0;
    }

    public static void Assign(int fleet, int squadron)
    {
        if (!Valid(fleet) || !Squadrons.Valid(squadron)) return;
        Detach(squadron);                       // exclusive: it leaves whatever it was in
        fleets[fleet].squadrons.Add(squadron);
        OnChanged?.Invoke();
    }

    public static void Detach(int squadron)
    {
        for (int f = 1; f <= Count; f++) fleets[f].squadrons.Remove(squadron);
        OnChanged?.Invoke();
    }

    public static void Disband(int fleet)
    {
        if (!Valid(fleet)) return;
        fleets[fleet].squadrons.Clear();
        fleets[fleet].name = "";
        OnChanged?.Invoke();
    }

    /// The first fleet with nothing in it, or 0 when they are all in use.
    public static int FirstFree()
    {
        for (int f = 1; f <= Count; f++) if (fleets[f].squadrons.Count == 0) return f;
        return 0;
    }

    /// Every living ship under this fleet, across all its squadrons.
    public static List<Unit> Ships(int fleet)
    {
        var all = new List<Unit>();
        foreach (int g in SquadronsIn(fleet)) all.AddRange(ControlGroups.Members(g));
        return all;
    }

    public static void Reset()
    {
        for (int i = 0; i <= Count; i++) { fleets[i].squadrons.Clear(); fleets[i].name = ""; }
        OnChanged?.Invoke();
    }

    // ============================================================================================
    // CONDITION
    //
    // Weighted by hull, not averaged across ships, and the difference matters. A fleet of one
    // dreadnought at 20% and nine intact probes averages to 92% "healthy" and is in fact a wreck
    // escorted by ten pounds of instruments. Summing the actual hit points on both sides of the
    // fraction gives the number a player is really asking for: how much of this force is still there.
    // ============================================================================================

    public static float ConditionOf(IReadOnlyList<Unit> ships)
    {
        if (ships == null || ships.Count == 0) return 0f;
        float have = 0f, max = 0f;
        foreach (var u in ships)
        {
            if (u == null || u.IsDestroyed) continue;
            have += u.Health;
            max += Mathf.Max(1, u.EffectiveHealth);
        }
        return max <= 0f ? 0f : Mathf.Clamp01(have / max);
    }

    public static float ConditionOfSquadron(int squadron) => ConditionOf(ControlGroups.Members(squadron));
    public static float ConditionOfFleet(int fleet) => ConditionOf(Ships(fleet));

    /// The colour a condition bar is drawn in. Green through amber to red, with the amber band wide
    /// enough to be a warning rather than a moment — a fleet at 60% is worth noticing before it is at
    /// 20% and past helping.
    public static Color ConditionColor(float f)
    {
        f = Mathf.Clamp01(f);
        return f > 0.6f ? Color.Lerp(new Color(0.85f, 0.72f, 0.20f), new Color(0.35f, 0.80f, 0.40f),
                                     Mathf.InverseLerp(0.6f, 1f, f))
                        : Color.Lerp(new Color(0.85f, 0.25f, 0.25f), new Color(0.85f, 0.72f, 0.20f),
                                     Mathf.InverseLerp(0f, 0.6f, f));
    }

    // ---- Save / load ---------------------------------------------------------------------------

    public static List<FleetDTO> Export()
    {
        var l = new List<FleetDTO>();
        for (int f = 1; f <= Count; f++)
        {
            if (fleets[f].squadrons.Count == 0 && string.IsNullOrEmpty(fleets[f].name)) continue;
            l.Add(new FleetDTO { fleet = f, name = fleets[f].name, squadrons = new List<int>(fleets[f].squadrons) });
        }
        return l;
    }

    public static void Import(List<FleetDTO> dtos)
    {
        for (int i = 0; i <= Count; i++) { fleets[i].squadrons.Clear(); fleets[i].name = ""; }
        if (dtos != null)
            foreach (var d in dtos)
            {
                if (!Valid(d.fleet)) continue;
                fleets[d.fleet].name = d.name ?? "";
                if (d.squadrons != null)
                    foreach (int g in d.squadrons)
                        if (Squadrons.Valid(g) && !fleets[d.fleet].squadrons.Contains(g))
                            fleets[d.fleet].squadrons.Add(g);
            }
        OnChanged?.Invoke();
    }
}
