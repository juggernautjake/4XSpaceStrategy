using System.Collections.Generic;
using UnityEngine;

// Numbered fleet control groups, the way an RTS player expects them:
//
//   Ctrl + 1..9   bind the current selection to that group (replacing whatever was there)
//   1..9          select that group and fly the camera to it
//   Shift + 1..9  add that group to the current selection without moving the camera
//
// Groups survive ships dying (dead units are pruned on read) and are saved with the game.
public static class ControlGroups
{
    public const int Count = 9;

    // Group N holds unit ids rather than references, so a unit destroyed and its slot reused can't
    // resurrect a stale entry, and the groups serialize trivially.
    static readonly List<int>[] groups = new List<int>[Count + 1];   // index 1..9; [0] unused

    public static event System.Action OnChanged;

    static ControlGroups()
    {
        for (int i = 0; i <= Count; i++) groups[i] = new List<int>();
    }

    public static void Clear()
    {
        for (int i = 0; i <= Count; i++) groups[i].Clear();
        OnChanged?.Invoke();
    }

    // ---- The roster verbs ------------------------------------------------------------------------
    //
    // MEMBERSHIP IS EXCLUSIVE: a ship sits in at most one group, and joining a new one leaves the old.
    // Classic control groups overlap freely, which is fine while a group is only a selection shortcut.
    // It stops being fine the moment a group carries a FORMATION and a PROTOCOL (see Squadrons): "the
    // squadron's formation" has no answer for a ship in two squadrons, and neither does which rally
    // point it runs to when it is hurt.
    //
    // Exclusivity is also what gives the verbs below their meaning. Detach takes ships out of whatever
    // they are in; Split promotes a sub-selection into a slot of its own — which is exactly "select a
    // few ships inside a bigger group and make them their own group".

    static bool Eligible(Unit u) => u != null && u.owner == FactionManager.Player;

    /// Take these units out of every group. Returns the groups that changed.
    static void RemoveFromAll(IReadOnlyList<Unit> units, HashSet<int> touched)
    {
        if (units == null) return;
        foreach (var u in units)
        {
            if (u == null) continue;
            for (int g = 1; g <= Count; g++)
                if (groups[g].Remove(u.id)) touched.Add(g);
        }
    }

    /// Replace group N with these units. The classic Ctrl+N.
    public static void Assign(int group, IReadOnlyList<Unit> units)
    {
        if (group < 1 || group > Count) return;
        var touched = new HashSet<int> { group };
        RemoveFromAll(units, touched);
        groups[group].Clear();
        if (units != null)
            foreach (var u in units)
                if (Eligible(u) && !groups[group].Contains(u.id))
                    groups[group].Add(u.id);
        PruneEmptied(touched);
        OnChanged?.Invoke();
    }

    /// Add these units to group N, leaving its existing members alone.
    public static void AddTo(int group, IReadOnlyList<Unit> units)
    {
        if (group < 1 || group > Count || units == null) return;
        var touched = new HashSet<int> { group };
        RemoveFromAll(units, touched);
        foreach (var u in units)
            if (Eligible(u) && !groups[group].Contains(u.id))
                groups[group].Add(u.id);
        PruneEmptied(touched);
        OnChanged?.Invoke();
    }

    /// Take these units out of whatever group they are in, leaving the rest of it intact. This is how
    /// a single ship is broken off a fleet: select it, detach it.
    public static void Detach(IReadOnlyList<Unit> units)
    {
        if (units == null) return;
        var touched = new HashSet<int>();
        RemoveFromAll(units, touched);
        PruneEmptied(touched);
        OnChanged?.Invoke();
    }

    /// Promote these units into the first free slot and return it, or 0 if every slot is taken.
    public static int Split(IReadOnlyList<Unit> units)
    {
        if (units == null || units.Count == 0) return 0;
        for (int g = 1; g <= Count; g++)
            if (groups[g].Count == 0) { Assign(g, units); return g; }

        // Every slot is full. A slot whose ships are ALL in this selection is about to be emptied by
        // the split anyway, so it is free in every sense that matters — take it rather than refusing.
        var ids = new HashSet<int>();
        foreach (var u in units) if (u != null) ids.Add(u.id);
        for (int g = 1; g <= Count; g++)
        {
            bool whollyContained = groups[g].Count > 0;
            foreach (int id in groups[g]) if (!ids.Contains(id)) { whollyContained = false; break; }
            if (whollyContained) { Assign(g, units); return g; }
        }
        return 0;
    }

    /// Empty a group, leaving its ships unassigned.
    public static void Disband(int group)
    {
        if (group < 1 || group > Count) return;
        groups[group].Clear();
        Squadrons.ResetSlot(group);
        OnChanged?.Invoke();
    }

    /// A slot that has just lost its last member forgets its standing orders too, so slot 3 does not
    /// hand the next fleet bound there the patrol route the last one was given.
    static void PruneEmptied(HashSet<int> touched)
    {
        foreach (int g in touched)
            if (g >= 1 && g <= Count && groups[g].Count == 0) Squadrons.ResetSlot(g);
    }

    // The living members of a group.
    public static List<Unit> Members(int group)
    {
        var list = new List<Unit>();
        if (group < 1 || group > Count || UnitManager.Instance == null) return list;
        foreach (int id in groups[group])
            foreach (var u in UnitManager.Instance.Units)
                if (u.id == id) { list.Add(u); break; }
        return list;
    }

    public static bool IsEmpty(int group) => group >= 1 && group <= Count && groups[group].Count == 0;

    // Which group a unit belongs to (0 = none). Used to stamp the little number on its icon. If a unit
    // somehow sits in two groups, the lowest wins — that's the one the player will reach for.
    public static int GroupOf(Unit u)
    {
        if (u == null) return 0;
        for (int g = 1; g <= Count; g++)
            if (groups[g].Contains(u.id)) return g;
        return 0;
    }

    // ---- Save / load ----
    public static List<ControlGroupDTO> Export()
    {
        var l = new List<ControlGroupDTO>();
        for (int g = 1; g <= Count; g++)
            if (groups[g].Count > 0)
                l.Add(new ControlGroupDTO { group = g, unitIds = new List<int>(groups[g]) });
        return l;
    }

    public static void Import(List<ControlGroupDTO> dtos)
    {
        for (int i = 0; i <= Count; i++) groups[i].Clear();
        if (dtos != null)
            foreach (var d in dtos)
                if (d.group >= 1 && d.group <= Count && d.unitIds != null)
                    groups[d.group].AddRange(d.unitIds);
        OnChanged?.Invoke();
    }
}

// Listens for the number keys and drives ControlGroups. Lives on its own object so the bindings work
// no matter which window has focus.
public class ControlGroupInput : MonoBehaviour
{
    public static ControlGroupInput Instance;

    static readonly KeyCode[] Digits =
    {
        KeyCode.None,
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
        KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9
    };

    public static void Create()
    {
        if (Instance != null) return;
        Instance = new GameObject("ControlGroupInput").AddComponent<ControlGroupInput>();
    }

    void Awake() { Instance = this; }

    void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        for (int g = 1; g <= ControlGroups.Count; g++)
        {
            if (!Input.GetKeyDown(Digits[g])) continue;

            if (ctrl) BindGroup(g);
            else RecallGroup(g, shift);
            return;   // one group action per frame
        }
    }

    void BindGroup(int g)
    {
        var sel = UnitSelection.Selected;
        if (sel.Count == 0)
        {
            NotificationManager.Instance?.Push($"Group {g} not set", "Select some ships first, then press Ctrl+" + g + ".", null, NotifKind.Info);
            return;
        }
        ControlGroups.Assign(g, sel);
        SimpleAudio.Instance?.PlayClick();
        NotificationManager.Instance?.Push($"Group {g} assigned",
            $"{sel.Count} ship(s) bound to group {g}. Press {g} to select them and jump the camera to them.", null, NotifKind.Info);
    }

    void RecallGroup(int g, bool additive)
    {
        var members = ControlGroups.Members(g);
        if (members.Count == 0) return;

        if (additive)
        {
            foreach (var u in members) UnitSelection.Select(u, true);
            return;
        }

        UnitSelection.Set(members);
        SimpleAudio.Instance?.PlayUnitSelect(members[0].type);
        FocusOn(members);
    }

    // Fly the camera to the group's centre of mass, so pressing the number both selects the fleet and
    // takes you to it.
    static void FocusOn(List<Unit> members)
    {
        var um = UnitManager.Instance;
        if (um == null || members.Count == 0) return;

        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (var u in members) { sum += um.UnitPos(u); n++; }
        if (n == 0) return;

        CameraController.Focus(sum / n);
    }
}
