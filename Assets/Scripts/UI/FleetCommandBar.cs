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
    RectTransform row;
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
        rt.sizeDelta = new Vector2(1040, 74);
        rt.anchoredPosition = new Vector2(0, 8);

        header = UIFactory.Text(root.transform, "", 11, UITheme.Accent, TextAlignmentOptions.Left);
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(-16, 16);
        hrt.anchoredPosition = new Vector2(0, -3);
        header.raycastTarget = false;

        var rowGo = UIFactory.NewUI(root.transform, "Row");
        row = rowGo.GetComponent<RectTransform>();
        row.anchorMin = new Vector2(0, 0); row.anchorMax = new Vector2(1, 1);
        row.offsetMin = new Vector2(8, 6); row.offsetMax = new Vector2(-8, -20);
        var h = rowGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 5; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        UnitSelection.OnChanged += () => dirty = true;
        ControlGroups.OnChanged += () => dirty = true;
        Squadrons.OnChanged += () => dirty = true;
        Fleets.OnChanged += () => dirty = true;

        root.SetActive(false);
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
        for (int i = row.childCount - 1; i >= 0; i--) Destroy(row.GetChild(i).gameObject);

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

        Btn("Form\n<size=8>Ctrl+M</size>", 46f, () => ControlGroups.Split(sel),
            "Break the selected ships out into a squadron of their own, in the first free slot.\n" +
            "This is how you split a few ships out of a bigger group.");

        if (g >= 1)
        {
            Btn("Detach\n<size=8>Ctrl+Alt+N</size>", 56f, () => ControlGroups.Detach(sel),
                "Take the selected ships OUT of their squadron, leaving the rest of it intact.\n" +
                "Select one ship to break just that ship off.");
            Btn("Disband", 50f, () => ControlGroups.Disband(g),
                $"Empty {Squadrons.NameOf(g)} completely. Its ships stay selected and keep flying; " +
                "they simply belong to no squadron, and its standing orders are forgotten.");
        }

        if (g < 1) return;   // the rest of the bar commands a squadron, and there isn't one

        // ---- formation ----
        Section("FORMATION");
        foreach (FleetFormationKind f in System.Enum.GetValues(typeof(FleetFormationKind)))
        {
            var kind = f;
            var b = Btn(ShortName(kind), 62f, () => Squadrons.SetFormation(g, kind));
            if (Squadrons.Of(g).formation == kind) Tint(b, new Color(0.20f, 0.42f, 0.58f));
            UIFactory.Tooltip(b, FormationTip(kind));
        }

        // ---- protocol ----
        Section("PROTOCOL");
        foreach (SquadronProtocol p in System.Enum.GetValues(typeof(SquadronProtocol)))
        {
            var kind = p;
            var b = Btn(ShortName(kind), 62f, () => Squadrons.SetProtocol(g, kind));
            if (Squadrons.Of(g).protocol == kind) Tint(b, new Color(0.20f, 0.42f, 0.58f));
            UIFactory.Tooltip(b, ProtocolTip(kind));
        }

        // ---- standing orders ----
        Section("ORDERS");
        var o = Squadrons.Of(g);

        if (o.Patrolling)
            Btn("Stop\npatrol", 48f, () => Squadrons.ClearPatrol(g),
                "Cancel the patrol route. The squadron holds where it is.");
        else
            Btn("Patrol", 48f, () => PatrolTool.Instance?.Arm(g),
                "Lay down a patrol route: click two or more points in space, then right-click to " +
                "finish. The squadron walks the route until you cancel it, and keeps whatever " +
                "protocol it is under while it does — an aggressive patrol hunts, an evade-and-report " +
                "patrol is a picket line.");

        Btn(o.hasRally ? "Clear\nrally" : "Rally", 48f,
            () => { if (o.hasRally) Squadrons.ClearRally(g); else RallyTool.Instance?.Arm(g); },
            o.hasRally
                ? "Forget this squadron's rally point."
                : "Set the point this squadron runs to: where an Evade-and-Report squadron breaks off " +
                  "to, and where a ship that withdraws below its hull threshold heads for.");
    }

    // ---- little builders --------------------------------------------------------------------------

    void Section(string label)
    {
        var t = UIFactory.Text(row, label, 8, new Color(0.55f, 0.66f, 0.78f), TextAlignmentOptions.Center);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 34; le.flexibleWidth = 0;
        t.raycastTarget = false;
    }

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
