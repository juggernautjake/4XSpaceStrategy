using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// THE FLEET COMMAND BAR
//
// Everything under the hood — six formations, six protocols, patrol routes, rally points, the
// squadron roster verbs — was reachable only from the keyboard or not at all. A formation the player
// cannot choose is not a feature, and "press Ctrl+Alt+4 to detach" is not a discoverable one.
//
// So: a strip along the bottom of the screen, visible whenever ships are selected, carrying every one
// of those controls with a tooltip on each. It appears with a selection and goes away with it, which
// keeps it out of the way of a player who is not commanding anything at that moment.
//
// ---- IT ACTS ON THE SELECTION'S SQUADRON, AND SAYS WHEN THERE ISN'T ONE --------------------
//
// Formation and protocol are properties of a SQUADRON, not of a loose handful of ships, so the bar
// needs to know which squadron is being commanded. It takes the squadron of the current selection
// when they all share one, and when they do not it says so and offers the one button that fixes it.
// The alternative — quietly applying a formation to the first squadron it found — would be a control
// that silently does something other than what it appears to do.
// ============================================================================================
public class FleetCommandBar : MonoBehaviour
{
    public static FleetCommandBar Instance;

    GameObject root;
    // `row` is whichever of the two is currently being filled — Rebuild switches it partway down, so
    // Section() and Icon() need no idea which row they are writing into.
    RectTransform rowTop, rowBottom, row;
    TMP_Text header;

    /// Rebuilt when the selection changes rather than every frame — it is a dozen buttons, and
    /// rebuilding those sixty times a second to show the same dozen buttons would be silly.
    bool dirty = true;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("FleetCommandBar");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<FleetCommandBar>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        root = UIFactory.Panel(parent, "FleetCommandBar", new Color(0.06f, 0.09f, 0.13f, 0.94f)).gameObject;
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(1060, 122);
        rt.anchoredPosition = new Vector2(0, 8);

        header = UIFactory.Text(root.transform, "", 11, UITheme.Accent, TextAlignmentOptions.Left);
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(-16, 16);
        hrt.anchoredPosition = new Vector2(0, -3);
        header.raycastTarget = false;

        // ---- TWO ROWS ----
        //
        // Twenty-five controls at 46 pixels is about 1,400 pixels of bar, which is most of a 1080p
        // screen and all of a smaller one. Splitting them puts the things you reach for under fire —
        // the squadron slots and the battle orders — on the top row, and the standing choices you set
        // once and leave — formation, protocol, patrol, fleet — underneath. That is also the right
        // grouping by urgency, so the split earns its keep rather than merely fitting.
        rowTop = MakeRow("RowTop", -18f);
        rowBottom = MakeRow("RowBottom", -66f);
        row = rowTop;

        UnitSelection.OnChanged += () => dirty = true;
        ControlGroups.OnChanged += () => dirty = true;
        Squadrons.OnChanged += () => dirty = true;
        Fleets.OnChanged += () => dirty = true;
        CombatOrders.OnChanged += () => dirty = true;

        root.SetActive(false);
    }

    RectTransform MakeRow(string name, float top)
    {
        var go = UIFactory.NewUI(root.transform, name);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(8, 0); rt.offsetMax = new Vector2(-8, 0);
        rt.sizeDelta = new Vector2(-16, 46);
        rt.anchoredPosition = new Vector2(0, top);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;
        return rt;
    }

    void Update()
    {
        bool want = UnitSelection.Selected.Count > 0;
        if (root.activeSelf != want) { root.SetActive(want); dirty = true; }
        if (!want || !dirty) return;
        dirty = false;
        Rebuild();
    }

    /// The squadron every selected ship belongs to, or 0 when they are split across squadrons or
    /// belong to none. Zero is not an error — it is the state the "Form squadron" button exists for.
    static int SelectedSquadron()
    {
        var sel = UnitSelection.Selected;
        if (sel.Count == 0) return 0;
        int first = ControlGroups.GroupOf(sel[0]);
        if (first == 0) return 0;
        for (int i = 1; i < sel.Count; i++)
            if (ControlGroups.GroupOf(sel[i]) != first) return 0;
        return first;
    }

    void Rebuild()
    {
        for (int i = rowTop.childCount - 1; i >= 0; i--) Destroy(rowTop.GetChild(i).gameObject);
        for (int i = rowBottom.childCount - 1; i >= 0; i--) Destroy(rowBottom.GetChild(i).gameObject);
        row = rowTop;

        var sel = new List<Unit>(UnitSelection.Selected);
        int g = SelectedSquadron();

        header.text = g >= 1
            ? $"<b>{Squadrons.NameOf(g)}</b>  ·  {sel.Count} selected  ·  " +
              $"{Squadrons.Of(g).formation} · {Squadrons.Of(g).protocol}" +
              (Squadrons.Of(g).Patrolling ? $" · patrolling {Squadrons.Of(g).patrol.Count} points" : "")
            : $"{sel.Count} ship(s) selected — <color=#9FB4C8>not one squadron, so formation and " +
              "protocol are unavailable. Form one first.</color>";

        // ---- roster ----
        Section("SQUADRON");

        for (int n = 1; n <= ControlGroups.Count; n++)
        {
            int slot = n;
            int count = ControlGroups.Members(slot).Count;
            var chip = Btn(count > 0 ? $"{slot}\n<size=8>{count}</size>" : $"{slot}", 30f, () =>
            {
                var members = ControlGroups.Members(slot);
                if (members.Count > 0) UnitSelection.Set(members);
            });
            Tint(chip, slot == g ? new Color(0.20f, 0.42f, 0.58f)
                     : count > 0 ? new Color(0.13f, 0.20f, 0.27f)
                                 : new Color(0.09f, 0.12f, 0.16f));
            UIFactory.Tooltip(chip, count > 0
                ? $"{Squadrons.NameOf(slot)} — {count} ship(s).\nClick to select. Key: {slot}"
                : $"Squadron {slot} is empty.\nSelect ships and press Ctrl+{slot} to bind them here.");
        }

        Icon("Act_Form", "Form", 46f, () => ControlGroups.Split(sel), null,
             "FORM SQUADRON — break the selected ships out into a squadron of their own, in the first " +
             "free slot.\n\nThis is how you split a few ships out of a bigger group, and it is what " +
             "unlocks formation and protocol for them.\n\n<color=#8FA3B5>Key: Ctrl+M</color>");

        if (g >= 1)
        {
            Icon("Act_Detach", "Detach", 46f, () => ControlGroups.Detach(sel), null,
                 "DETACH — take the selected ships OUT of their squadron, leaving the rest of it " +
                 "intact.\n\nSelect one ship to break just that ship off.\n\n" +
                 "<color=#8FA3B5>Key: Ctrl+Alt+N</color>");
            Icon("Act_Disband", "Disband", 46f, () => ControlGroups.Disband(g), null,
                 $"DISBAND — empty {Squadrons.NameOf(g)} completely.\n\nIts ships stay selected and " +
                 "keep flying; they simply belong to no squadron, and its standing orders — formation, " +
                 "protocol, patrol route, rally point — are forgotten.");
        }

        // ============================================================================================
        // BATTLE — available to ANY selection, squadron or not
        //
        // Deliberately above the squadron-only early-out below. Concentrating fire, holding position
        // and breaking off are orders you give to whatever you have selected, and requiring the player
        // to form a squadron first would mean the most urgent controls in the game are the ones behind
        // the most setup. Formation and protocol genuinely are squadron properties; these are not.
        // ============================================================================================
        Section("BATTLE");

        var focus = FocusTargetOf(sel);
        bool anyFocused = CombatOrders.AnyFocused(sel);
        bool anyArmed = AnyArmed(sel);

        // Focus fire is set by right-clicking a hostile, because picking a target is a thing you do ON
        // THE MAP — there is no way to name a specific enemy from a bar. So the button reports and
        // clears rather than sets, and its tooltip is where the verb is taught.
        Icon("Order_FocusFire", "Focus", 46f, null, anyFocused ? "focused" : null,
             anyArmed
                 ? (focus != null
                     ? $"CONCENTRATING FIRE on <b>{focus.name}</b>.\n\n" +
                       "Every selected ship shoots this one target while it is in range, instead of " +
                       "picking its own. Concentration is usually the whole fight: six ships on one " +
                       "target remove a sixth of the enemy's guns each time one dies, where six ships " +
                       "on six targets kill nothing for most of the engagement.\n\n" +
                       "<color=#8FA3B5>Right-click another hostile to switch, or press T to put the " +
                       "selection on whatever is nearest. The order lapses on its own if the target " +
                       "dies or leaves range.</color>"
                     : "No target designated — every ship is picking its own.\n\n" +
                       "<b>Right-click an enemy ship</b> to concentrate the selection's fire on it, or " +
                       "press <b>T</b> to take whatever is under the cursor — and failing that, the " +
                       "nearest hostile anything selected can actually reach.\n\n" +
                       "<color=#8FA3B5>The automatic targeter is threat-weighted and per ship, so a " +
                       "squadron facing two identical cruisers naturally splits between them. That is " +
                       "the situation this order exists for.</color>")
                 : "Nothing selected can shoot.")
            .SetEnabled(false);

        Icon("Order_EngageAtWill", "At will", 46f,
             () => CombatOrders.ReleaseSelection(sel), !anyFocused ? "on" : null,
             "ENGAGE AT WILL — cancel the focus order and let every ship pick its own target again.\n\n" +
             "The automatic choice is the most dangerous thing in reach, weighted so a screen of cheap " +
             "hulls in front of a capital ship does not soak the fire meant for it.\n\n" +
             "<color=#8FA3B5>Key: Y</color>")
            .SetEnabled(anyFocused);

        bool holding = CombatOrders.AnyHolding(sel);
        Icon("Order_HoldPosition", "Hold", 46f,
             () => CombatOrders.SetHold(sel, !holding), holding ? "on" : null,
             holding
                 ? "HOLDING POSITION. Click to release.\n\n" +
                   "These ships will not be moved by their squadron's standing orders — no intercept, " +
                   "no closing on an escort, no walking a patrol route.\n\n" +
                   "<color=#8FA3B5>They are still fighting, and still flying evasively. Holding is not " +
                   "the same as holding fire.\n\nKey: H</color>"
                 : "HOLD POSITION — stop here and stay here.\n\n" +
                   "Cancels whatever these ships were doing and stops their squadron's AI from moving " +
                   "them. For a picket, a chokepoint, or anything you have parked deliberately.\n\n" +
                   "<color=#8FA3B5>They keep shooting, they keep jinking, and any move order you give " +
                   "releases the hold.\n\nKey: H</color>");

        Icon("Order_Withdraw", "Withdraw", 52f, () =>
        {
            int n = CombatOrders.Withdraw(sel);
            if (n == 0)
                NotificationManager.Instance?.Push("Nowhere to withdraw to",
                    "These ships have no rally point and their owner holds no world to run to. Set a " +
                    "rally point, or take somewhere first.", null, NotifKind.Danger);
        }, null,
             "WITHDRAW — break off and run for safety, now.\n\n" +
             "To the squadron's rally point if it has one, otherwise the nearest world you hold — a " +
             "settled one for preference, since a colony can rearm and repair and a beacon on an empty " +
             "rock cannot.");

        if (g < 1) return;   // the rest of the bar commands a squadron, and there isn't one

        row = rowBottom;

        // ---- formation ----
        Section("FORMATION");
        foreach (FleetFormationKind f in System.Enum.GetValues(typeof(FleetFormationKind)))
        {
            var kind = f;
            // Hovering DRAWS it on the map before it is committed to — see FormationPreview. A tooltip
            // can describe a wedge and the icon diagrams one, but neither says what YOUR eleven ships
            // will look like standing in it.
            Icon(FormIcon(kind), ShortLabel(kind), 46f, () => Squadrons.SetFormation(g, kind),
                 Squadrons.Of(g).formation == kind ? "on" : null, FormationTip(kind))
                .OnHover(() => FormationPreview.Instance?.Show(g, kind),
                         () => FormationPreview.Instance?.Hide());
        }

        // ---- protocol ----
        Section("PROTOCOL");
        foreach (SquadronProtocol p in System.Enum.GetValues(typeof(SquadronProtocol)))
        {
            var kind = p;
            Icon(ProtIcon(kind), ShortLabel(kind), 46f, () => Squadrons.SetProtocol(g, kind),
                 Squadrons.Of(g).protocol == kind ? "on" : null, ProtocolTip(kind));
        }

        // ---- standing orders ----
        Section("ORDERS");
        var o = Squadrons.Of(g);

        Icon("Order_Patrol", o.Patrolling ? "Stop" : "Patrol", 46f,
             () => { if (o.Patrolling) Squadrons.ClearPatrol(g); else PatrolTool.Instance?.Arm(g); },
             o.Patrolling ? "on" : null,
             o.Patrolling
                 ? $"PATROLLING {o.patrol.Count} points. Click to cancel — the squadron holds where it is."
                 : "PATROL — lay down a route: click two or more points in space, then right-click to " +
                   "finish.\n\nThe squadron walks the route until you cancel it, keeping whatever " +
                   "protocol it is under: an aggressive patrol hunts, an evade-and-report patrol is a " +
                   "picket line.");

        Icon("Order_Rally", o.hasRally ? "Clear" : "Rally", 46f,
             () => { if (o.hasRally) Squadrons.ClearRally(g); else RallyTool.Instance?.Arm(g); },
             o.hasRally ? "on" : null,
             o.hasRally
                 ? "A rally point is set. Click to forget it."
                 : "RALLY — set the point this squadron runs to.\n\nWhere an Evade-and-Report squadron " +
                   "breaks off to, where a ship withdrawing below its hull threshold heads, and where " +
                   "the Withdraw order sends it.");

        // ---- fleet ----
        //
        // Only when this squadron is actually in one. A fleet section on a squadron that belongs to no
        // fleet would be four disabled buttons explaining that they are disabled.
        int fleet = Fleets.FleetOf(g);
        if (fleet >= 1)
        {
            Section("FLEET");
            var ships = Fleets.Ships(fleet);
            var squads = Fleets.SquadronsIn(fleet);

            Icon("Unit_Fleet", "Select", 46f, () => UnitSelection.Set(ships), null,
                 $"<b>{Fleets.NameOf(fleet)}</b> — {squads.Count} squadron(s), {ships.Count} ship(s).\n\n" +
                 "Select the whole fleet.");

            var fTarget = CombatOrders.FleetTarget(fleet);
            Icon("Order_FocusFire", "All fire", 46f,
                 () => { if (focus != null) CombatOrders.FocusFleet(fleet, focus); },
                 fTarget != null ? "on" : null,
                 focus != null
                     ? $"Order the ENTIRE fleet onto <b>{focus.name}</b> — every squadron in it, not " +
                       "just the ships you have selected."
                     : "Designate a target first: right-click an enemy ship with these ships selected, " +
                       "then press this to put the whole fleet on it.");

            Icon("Order_Withdraw", "Fleet out", 52f, () =>
            {
                int n = CombatOrders.Withdraw(ships);
                if (n == 0)
                    NotificationManager.Instance?.Push("Nowhere to withdraw to",
                        "This fleet has no rally point and you hold no world it can run to.",
                        null, NotifKind.Danger);
            }, null,
                 $"Break off the WHOLE of {Fleets.NameOf(fleet)} — every squadron in it — and run for " +
                 "safety.\n\nEach squadron heads for its own rally point if it has one, otherwise the " +
                 "nearest world you hold.");
        }
    }

    // ---- battle helpers ----------------------------------------------------------------------------

    /// The target the selection is concentrating on, if they agree on one. Disagreement returns null
    /// rather than picking a winner — a bar that reported one of three different orders as though it
    /// were the order would be lying about the state of the fleet.
    static Unit FocusTargetOf(List<Unit> sel)
    {
        Unit first = null;
        for (int i = 0; i < sel.Count; i++)
        {
            var t = CombatOrders.FocusFor(sel[i]);
            if (t == null) continue;
            if (first == null) first = t;
            else if (first != t) return null;
        }
        return first;
    }

    static bool AnyArmed(List<Unit> sel)
    {
        for (int i = 0; i < sel.Count; i++)
        {
            var u = sel[i];
            if (u?.Info != null && u.Info.attack > 0) return true;
        }
        return false;
    }

    // ---- little builders --------------------------------------------------------------------------

    void Section(string label)
    {
        var t = UIFactory.Text(row, label, 8, new Color(0.55f, 0.66f, 0.78f), TextAlignmentOptions.Center);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 34; le.flexibleWidth = 0;
        t.raycastTarget = false;
    }

    /// An icon control. `state` non-null lights it up; null leaves it idle.
    ///
    /// The label stays UNDER the icon rather than being replaced by it. An icon alone is faster to
    /// recognise once learned and impossible to learn from, and this bar has twenty-two of them — so
    /// the word is there for the first week and the shape is there forever after.
    CmdIcon Icon(string iconName, string label, float width, System.Action onClick,
                 string state, string tip)
        => CmdIcon.Make(row, iconName, label, width, onClick, tip).SetOn(state != null);

    static string FormIcon(FleetFormationKind k) => k switch
    {
        FleetFormationKind.LineAbreast => "Form_LineAbreast",
        FleetFormationKind.LineAstern => "Form_LineAstern",
        FleetFormationKind.Echelon => "Form_Echelon",
        FleetFormationKind.Screen => "Form_Screen",
        FleetFormationKind.Globe => "Form_Globe",
        FleetFormationKind.Free => "Form_Free",
        _ => "Form_Wedge",
    };

    static string ProtIcon(SquadronProtocol p) => p switch
    {
        SquadronProtocol.Aggressive => "Prot_Aggressive",
        SquadronProtocol.HoldFire => "Prot_HoldFire",
        SquadronProtocol.EvadeAndReport => "Prot_EvadeAndReport",
        SquadronProtocol.Escort => "Prot_Escort",
        SquadronProtocol.WithdrawIfHurt => "Prot_WithdrawIfHurt",
        _ => "Prot_Defensive",
    };

    /// One short word under the icon. The old labels were two stacked lines at eight point, which is
    /// where "Withdraw\nif hurt" came from; with a diagram above it, one word is enough to anchor it.
    static string ShortLabel(FleetFormationKind k) => k switch
    {
        FleetFormationKind.LineAbreast => "Abreast",
        FleetFormationKind.LineAstern => "Astern",
        FleetFormationKind.Echelon => "Echelon",
        FleetFormationKind.Screen => "Screen",
        FleetFormationKind.Globe => "Globe",
        FleetFormationKind.Free => "Free",
        _ => "Wedge",
    };

    static string ShortLabel(SquadronProtocol p) => p switch
    {
        SquadronProtocol.Aggressive => "Attack",
        SquadronProtocol.HoldFire => "Hold fire",
        SquadronProtocol.EvadeAndReport => "Evade",
        SquadronProtocol.Escort => "Escort",
        SquadronProtocol.WithdrawIfHurt => "Wounded",
        _ => "Defend",
    };

    GameObject Btn(string label, float width, System.Action onClick, string tip = null)
    {
        var b = UIFactory.Button(row, label, onClick, 40f);
        var le = b.gameObject.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.flexibleWidth = 0;
        var txt = b.GetComponentInChildren<TMP_Text>();
        if (txt != null) { txt.fontSize = 10; txt.alignment = TextAlignmentOptions.Center; }
        if (tip != null) UIFactory.Tooltip(b.gameObject, tip);
        return b.gameObject;
    }

    static void Tint(GameObject go, Color c)
    {
        var img = go.GetComponent<Image>();
        if (img != null) img.color = c;
    }

    // ---- the words ---------------------------------------------------------------------------------
    //
    // Every tooltip says what the ships will DO, not what the option is called. "Screen" means nothing;
    // "your cheapest ships form an arc in front of the expensive ones" is a decision a player can make.

    static string ShortName(FleetFormationKind k) => k switch
    {
        FleetFormationKind.LineAbreast => "Line\nabreast",
        FleetFormationKind.LineAstern => "Line\nastern",
        FleetFormationKind.Echelon => "Echelon",
        FleetFormationKind.Screen => "Screen",
        FleetFormationKind.Globe => "Globe",
        FleetFormationKind.Free => "Free",
        _ => "Wedge",
    };

    static string FormationTip(FleetFormationKind k) => k switch
    {
        FleetFormationKind.Wedge =>
            "WEDGE — the all-round default. Leader at the point, pairs sweeping back behind it. " +
            "Good for getting somewhere without committing to a shape.",
        FleetFormationKind.LineAbreast =>
            "LINE ABREAST — one rank, every ship bearing forward. OFFENSIVE: the most guns you can " +
            "bring on what is in front of you, and the most hulls exposed to it.",
        FleetFormationKind.LineAstern =>
            "LINE ASTERN — single file, narrow frontage. For running a contested lane where only the " +
            "ship in front meets what is down it.",
        FleetFormationKind.Echelon =>
            "ECHELON — a diagonal stair to starboard. Every ship clear of the one ahead's line of " +
            "fire, for coming at something off the flank.",
        FleetFormationKind.Screen =>
            "SCREEN — your CHEAPEST ships form an arc in FRONT, and the expensive ones sit behind it. " +
            "Sorted by what a ship costs to lose, so colony ships, terraformers and science vessels — " +
            "which have no guns at all — go furthest back.",
        FleetFormationKind.Globe =>
            "GLOBE — escorts on a shell all the way around the valuable ships, above and below as well " +
            "as beside. DEFENSIVE: no open bearing, at the cost of concentrating nothing.",
        FleetFormationKind.Free =>
            "FREE — no formation. Every ship flies its own line. Fastest to disperse, and the right " +
            "answer for a squadron of one.",
        _ => "",
    };

    static string ShortName(SquadronProtocol p) => p switch
    {
        SquadronProtocol.Aggressive => "Aggressive",
        SquadronProtocol.HoldFire => "Hold\nfire",
        SquadronProtocol.EvadeAndReport => "Evade &\nreport",
        SquadronProtocol.Escort => "Escort",
        SquadronProtocol.WithdrawIfHurt => "Withdraw\nif hurt",
        _ => "Defensive",
    };

    static string ProtocolTip(SquadronProtocol p) => p switch
    {
        SquadronProtocol.Defensive =>
            "DEFENSIVE — hold station and engage whatever comes to you. Does not chase. The default, " +
            "and the right one for anything guarding a place rather than looking for a fight.",
        SquadronProtocol.Aggressive =>
            "AGGRESSIVE — closes on any hostile it detects and engages. It stops just short of the " +
            "target, inside weapons range. What a fighter wing is for.",
        SquadronProtocol.HoldFire =>
            "HOLD FIRE — never shoots first. How you slip a fleet past something you cannot beat. " +
            "Point defence still runs: it will not start a fight, but it will still swat a missile " +
            "already in the air.",
        SquadronProtocol.EvadeAndReport =>
            "EVADE AND REPORT — the scout's protocol. On contact it breaks off, runs for its rally " +
            "point (or home if none is set), and raises the alarm naming what it saw and where. " +
            "A scout's job is to come back.",
        SquadronProtocol.Escort =>
            "ESCORT — holds station on another squadron and screens it, closing up whenever the gap " +
            "opens.",
        SquadronProtocol.WithdrawIfHurt =>
            "WITHDRAW IF HURT — any ship below about a third of its hull leaves the squadron and heads " +
            "for safety. Per ship, not per squadron: one wounded cruiser goes home and the rest fight " +
            "on.",
        _ => "",
    };
}
