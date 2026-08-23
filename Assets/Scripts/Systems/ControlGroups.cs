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

    // ---- Members, and why it is written the way it is -------------------------------------------
    //
    // Scratch collections reused across calls rather than allocated per call. This is the hottest
    // read in the fleet code: FormationPreview asks for it EVERY FRAME while a formation button is
    // hovered, SquadronAI asks once a second for all nine squadrons, and the command bar asks nine
    // times whenever the selection changes.
    //
    // The old shape was a nested loop — for each member id, scan the whole fleet — which is
    // members x units. Ten ships against a late-game fleet of two hundred is two thousand comparisons
    // to answer a question about ten ships, sixty times a second. One pass with a set lookup is
    // units + members, and allocates nothing but the list it returns.
    static readonly HashSet<int> wanted = new HashSet<int>();
    static readonly Dictionary<int, Unit> found = new Dictionary<int, Unit>();

    /// The living members of a group, in the order they were bound to it.
    ///
    /// Group order, NOT fleet order, and that is load-bearing: PatrolTool and the roster both read
    /// members[0], and a list that reshuffled itself every time a ship was built somewhere else would
    /// quietly move the reference point they hang off.
    public static List<Unit> Members(int group)
    {
        var list = new List<Unit>();
        if (group < 1 || group > Count || UnitManager.Instance == null) return list;

        var ids = groups[group];
        if (ids.Count == 0) return list;

        wanted.Clear();
        foreach (int id in ids) wanted.Add(id);

        found.Clear();
        foreach (var u in UnitManager.Instance.Units)
            if (u != null && wanted.Contains(u.id)) found[u.id] = u;

        foreach (int id in ids)
            if (found.TryGetValue(id, out var u)) list.Add(u);
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

    /// Order of battle — the fleet / squadron / ship roster with its condition bars.
    public const KeyCode RosterKey = KeyCode.O;

    /// Promote the current selection into a squadron of its own, in the first free slot.
    public const KeyCode SplitKey = KeyCode.M;

    // ============================================================================================
    // THE BATTLE KEYS
    //
    // Designating a target was right-click only, which is fine for choosing WHICH enemy and useless
    // for the commonest case by far: the fighting has started, something is shooting at you, and you
    // want everything you have on it NOW. Hunting for the right hull with the mouse while the fleet
    // dies is not a decision, it is a dexterity test.
    //
    // So T takes the hostile under the cursor if there is one, and otherwise the nearest one anything
    // in the selection can reach. That second half is the important one — it makes T a single key
    // meaning "concentrate on the obvious threat", which is what a commander wants nine times in ten.
    //
    // T, Y and H were free. WASD is the camera, QR and F are already spoken for, O opens the roster
    // and M splits a squadron.
    // ============================================================================================

    /// Concentrate the selection's fire — on the hostile under the cursor, or the nearest one.
    public const KeyCode FocusKey = KeyCode.T;

    /// Release it: every ship picks its own target again.
    public const KeyCode AtWillKey = KeyCode.Y;

    /// Hold position, and release it.
    public const KeyCode HoldKey = KeyCode.H;

    void Update()
    {
        // NOT WHILE THE PLAYER IS TYPING. Every key below is a bare letter or digit, so naming a
        // squadron "Third Fleet" would otherwise recall squadron 3, and naming a save file "Home
        // Guard" would hold the selection's position and open the roster on the way past.
        if (UIFactory.IsTypingInField()) return;

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        if (Input.GetKeyDown(RosterKey) && !ctrl && !alt)
        {
            FleetRosterPanel.Instance?.Toggle();
            return;
        }

        if (!ctrl && !alt)
        {
            if (Input.GetKeyDown(FocusKey)) { FocusNearest(); return; }
            if (Input.GetKeyDown(AtWillKey)) { EngageAtWill(); return; }
            if (Input.GetKeyDown(HoldKey)) { ToggleHold(); return; }
        }

        // Ctrl+M: take whatever is selected and make it its own squadron. The common case is "these
        // four out of that twelve are going somewhere else", and it is one keystroke because it is
        // common, not because it is important.
        if (ctrl && Input.GetKeyDown(SplitKey)) { SplitSelection(); return; }

        for (int g = 1; g <= ControlGroups.Count; g++)
        {
            if (!Input.GetKeyDown(Digits[g])) continue;

            // Ctrl+Alt  detach these ships from whatever squadron they are in
            // Ctrl      bind the selection to this squadron, replacing it
            // Ctrl+Shift add the selection to this squadron
            // Shift     add this squadron to the selection
            // (plain)   select this squadron and fly the camera to it
            if (ctrl && alt) DetachFrom(g);
            else if (ctrl && shift) AddToGroup(g);
            else if (ctrl) BindGroup(g);
            else RecallGroup(g, shift);
            return;   // one group action per frame
        }
    }

    // ---- battle ------------------------------------------------------------------------------

    void FocusNearest()
    {
        var sel = UnitSelection.Selected;
        if (sel.Count == 0) return;

        // Only the ships that could actually shoot it. Ordering a colony ship to concentrate fire is
        // a message the game should not send.
        var shooters = new List<Unit>();
        foreach (var u in sel)
            if (u != null && !u.IsDestroyed && u.Info != null && u.Info.attack > 0) shooters.Add(u);
        if (shooters.Count == 0) return;

        // Under the cursor first — an explicit choice beats a guess, every time.
        var target = ClickPriority.UnitUnderCursor();
        if (target != null && (target.IsDestroyed || !CombatManager.AreHostile(shooters[0], target)))
            target = null;

        target ??= NearestHostileTo(shooters);

        if (target == null)
        {
            NotificationManager.Instance?.Push("Nothing to concentrate on",
                "No hostile in reach of the selected ships. Point at one to designate it explicitly.",
                null, NotifKind.Info);
            return;
        }

        CombatOrders.FocusSelection(shooters, target);
        SimpleAudio.Instance?.PlayClick();
        NotificationManager.Instance?.Push("Concentrating fire",
            $"{shooters.Count} ship(s) on {target.name}.", null, NotifKind.Info);
    }

    /// The closest thing any of these ships hates, within the reach of its own guns.
    ///
    /// Bounded by the SHOOTER's range rather than by a fixed radius, so pressing the key never
    /// designates something across the system that nothing can hit — which would produce a fleet
    /// standing there with a standing order it cannot act on.
    static Unit NearestHostileTo(List<Unit> shooters)
    {
        var um = UnitManager.Instance;
        if (um == null) return null;

        Unit best = null;
        float bestSq = float.MaxValue;

        foreach (var s in shooters)
        {
            float reach = Weaponry.MaxRange(Weaponry.For(s.type));
            if (reach <= 0f) continue;
            Vector3 p = CombatManager.PosOf(s);

            foreach (var o in um.Units)
            {
                if (o == null || o.IsDestroyed || o == s) continue;
                if (!CombatManager.AreHostile(s, o)) continue;
                float d = Vector3.SqrMagnitude(CombatManager.PosOf(o) - p);
                if (d > reach * reach || d >= bestSq) continue;
                bestSq = d; best = o;
            }
        }
        return best;
    }

    void EngageAtWill()
    {
        var sel = UnitSelection.Selected;
        if (sel.Count == 0 || !CombatOrders.AnyFocused(sel)) return;
        CombatOrders.ReleaseSelection(sel);
        SimpleAudio.Instance?.PlayClick();
        NotificationManager.Instance?.Push("Engaging at will",
            "Every selected ship picks its own target again.", null, NotifKind.Info);
    }

    void ToggleHold()
    {
        var sel = UnitSelection.Selected;
        if (sel.Count == 0) return;
        bool holding = CombatOrders.AnyHolding(sel);
        CombatOrders.SetHold(sel, !holding);
        SimpleAudio.Instance?.PlayClick();
        NotificationManager.Instance?.Push(holding ? "Released" : "Holding position",
            holding ? $"{sel.Count} ship(s) will take squadron orders again."
                    : $"{sel.Count} ship(s) will stay put. They keep firing.",
            null, NotifKind.Info);
    }

    void AddToGroup(int g)
    {
        var sel = UnitSelection.Selected;
        if (sel.Count == 0) return;
        ControlGroups.AddTo(g, sel);
        SimpleAudio.Instance?.PlayClick();
        NotificationManager.Instance?.Push($"Squadron {g} reinforced",
            $"{sel.Count} ship(s) joined {Squadrons.NameOf(g)}.", null, NotifKind.Info);
    }

    void DetachFrom(int g)
    {
        var sel = UnitSelection.Selected;
        if (sel.Count == 0) return;
        ControlGroups.Detach(sel);
        SimpleAudio.Instance?.PlayClick();
        NotificationManager.Instance?.Push("Detached",
            $"{sel.Count} ship(s) now belong to no squadron.", null, NotifKind.Info);
    }

    void SplitSelection()
    {
        var sel = UnitSelection.Selected;
        if (sel.Count == 0)
        {
            NotificationManager.Instance?.Push("Nothing to split",
                "Select the ships you want to break out, then press Ctrl+M.", null, NotifKind.Info);
            return;
        }

        var list = new List<Unit>(sel);
        int g = ControlGroups.Split(list);
        if (g == 0)
        {
            NotificationManager.Instance?.Push("No free squadron",
                "All nine squadrons are in use. Disband one first.", null, NotifKind.Danger);
            return;
        }

        SimpleAudio.Instance?.PlayClick();
        NotificationManager.Instance?.Push($"Squadron {g} formed",
            $"{list.Count} ship(s) broken out into {Squadrons.NameOf(g)}.", null, NotifKind.Info);
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
