using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// THE ORDER OF BATTLE
//
// Three tiers, one list: FLEET holds squadrons, SQUADRON holds ships, and every row on every tier
// carries a condition bar. Click a row to select everything under it — a fleet selects all its
// squadrons' ships, a squadron selects its ships, a ship selects itself.
//
// ---- WHY THE BAR IS WEIGHTED BY HULL AND NOT AVERAGED --------------------------------------
//
// One dreadnought at 20% and nine intact probes AVERAGE to 92%, which is a reassuring number for a
// force that is a wreck escorted by ten pounds of instruments. Summing hit points on both sides of
// the fraction answers what a player is actually asking a roster: how much of this is still there.
// See Fleets.ConditionOf.
//
// ---- WHY TIERS COLLAPSE ---------------------------------------------------------------------
//
// A fleet of four squadrons of six is twenty-four ships, and drawn flat that is a wall nobody reads.
// Fleets and squadrons start collapsed to their headline — name, strength, condition — and open on
// click, so the panel opens on the shape of the force rather than on its inventory.
// ============================================================================================
public class FleetRosterPanel : MonoBehaviour
{
    public static FleetRosterPanel Instance;

    GameObject root;
    RectTransform list;
    TMP_Text titleText;

    readonly HashSet<int> openFleets = new HashSet<int>();
    readonly HashSet<int> openSquadrons = new HashSet<int>();

    /// Rebuilt on a timer as well as on events, because CONDITION changes without any event firing —
    /// a ship taking fire does not raise OnUnitsChanged, and a roster whose bars only move when a ship
    /// is built or destroyed is a roster that lies for the whole of every battle.
    const float RefreshInterval = 0.5f;
    float refreshIn;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("FleetRosterPanel");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<FleetRosterPanel>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Order of Battle", new Vector2(360, 460), out root, out titleText);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16, -16);

        UIFactory.ScrollView(content, out list);
        UIFactory.VerticalLayout(list, 3);

        ControlGroups.OnChanged += RefreshIfShowing;
        Squadrons.OnChanged += RefreshIfShowing;
        Fleets.OnChanged += RefreshIfShowing;
        UnitSelection.OnChanged += RefreshIfShowing;

        root.SetActive(false);
    }

    public void Toggle() { if (root.activeSelf) root.SetActive(false); else Open(); }

    public void Open()
    {
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
        Refresh();
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        refreshIn -= Time.unscaledDeltaTime;
        if (refreshIn > 0f) return;
        refreshIn = RefreshInterval;
        Refresh();
    }

    void RefreshIfShowing() { if (root != null && root.activeSelf) Refresh(); }

    void Refresh()
    {
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        var claimed = new HashSet<int>();
        int totalShips = 0;

        // ---- fleets, and the squadrons under them ----
        for (int f = 1; f <= Fleets.Count; f++)
        {
            var squads = Fleets.SquadronsIn(f);
            if (squads.Count == 0) continue;

            var ships = Fleets.Ships(f);
            totalShips += ships.Count;

            bool open = openFleets.Contains(f);
            AddRow(0, $"{(open ? "▼" : "▶")} <b>{Fleets.NameOf(f)}</b>", ships.Count,
                   Fleets.ConditionOf(ships),
                   $"{Fleets.NameOf(f)} — {squads.Count} squadron(s), {ships.Count} ship(s).\n" +
                   "Click the name to select the whole fleet; click the arrow to open it.",
                   () => UnitSelection.Set(ships),
                   () => { if (!openFleets.Remove(f)) openFleets.Add(f); Refresh(); },
                   Magazines.GroupSupply(ships), "Unit_Fleet");

            foreach (int g in squads)
            {
                claimed.Add(g);
                if (open) AddSquadron(g, 1);
            }
        }

        // ---- squadrons in no fleet ----
        for (int g = 1; g <= Squadrons.Count; g++)
        {
            if (claimed.Contains(g)) continue;
            var members = ControlGroups.Members(g);
            if (members.Count == 0) continue;
            totalShips += members.Count;
            AddSquadron(g, 0);
        }

        titleText.text = totalShips > 0 ? $"Order of Battle ({totalShips})" : "Order of Battle";

        if (totalShips == 0)
            UIFactory.WrapText(list, "<color=#7F8FA3>No squadrons yet. Select some ships and press " +
                                     "Ctrl+1 to bind your first.</color>", UITheme.SmallSize, UITheme.Text);
    }

    void AddSquadron(int g, int indent)
    {
        var members = ControlGroups.Members(g);
        if (members.Count == 0) return;

        var o = Squadrons.Of(g);
        bool open = openSquadrons.Contains(g);

        AddRow(indent, $"{(open ? "▼" : "▶")} <b>{g}</b> {Squadrons.NameOf(g)}", members.Count,
               Fleets.ConditionOf(members),
               $"{Squadrons.NameOf(g)} — {members.Count} ship(s).\n" +
               $"Formation: {o.formation}\nProtocol: {o.protocol}\n\n" +
               $"Click the name to select it; press {g} anywhere to recall it.",
               () => UnitSelection.Set(members),
               () => { if (!openSquadrons.Remove(g)) openSquadrons.Add(g); Refresh(); },
               Magazines.GroupSupply(members), "Unit_Squadron");

        if (!open) return;

        foreach (var u in members)
        {
            var ship = u;
            var focus = CombatOrders.FocusFor(ship);
            string orders =
                (focus != null ? $"\n<color=#FF8A7A>Concentrating fire on {focus.name}</color>" : "") +
                (CombatOrders.Holding(ship) ? "\n<color=#FFBF4D>Holding position</color>" : "");

            AddRow(indent + 1, ship.name, 0, ship.HealthFraction,
                   $"{ship.name} — {ship.Info.name}\n" +
                   $"Hull {ship.Health:F0} / {ship.EffectiveHealth}\n" +
                   $"Rank {ship.RankName}{orders}\n\n" +
                   Magazines.SupplyReport(ship) +
                   "\n\nClick to select and open its panel.",
                   () => { UnitSelection.SelectOnly(ship); UnitInfoPanel.Instance?.Show(ship); },
                   null,
                   Magazines.CarriesOrdnance(ship) ? Magazines.AmmoFraction(ship) : -1f,
                   "Unit_Ship");
        }
    }

    /// One row: an indent, a label, an optional count, and a condition bar. `onExpand` is null for a
    /// leaf, which is what makes ships un-openable without a separate row type.
    void AddRow(int indent, string label, int count, float condition, string tip,
                System.Action onSelect, System.Action onExpand, float supply = -1f,
                string icon = null)
    {
        var row = UIFactory.NewUI(list, "Row");
        var bg = row.AddComponent<Image>();
        bg.color = indent == 0 ? new Color(0.10f, 0.16f, 0.22f, 0.9f)
                 : indent == 1 ? new Color(0.08f, 0.12f, 0.17f, 0.8f)
                               : new Color(0.06f, 0.09f, 0.13f, 0.7f);
        UIFactory.AddLayout(row, 26f);

        float left = 6 + indent * 14;

        // ---- FLEET, SQUADRON, SHIP — the mark says which ----
        //
        // Indentation already says the depth, but only RELATIVE to the row above: a squadron row
        // scrolled to the top of the panel looks exactly like a fleet row. The mark says what the row
        // is on its own, without the reader having to go and find its parent. One hull, three or six,
        // so the scale carries the meaning and no legend is needed.
        if (!string.IsNullOrEmpty(icon))
        {
            var kg = UIFactory.NewUI(row.transform, "Kind");
            var kind = kg.AddComponent<RawImage>();
            kind.raycastTarget = false;
            kind.texture = Resources.Load<Texture2D>($"SpaceAssets/CommandIcons/Cmd_{icon}");
            kind.color = new Color(0.62f, 0.76f, 0.90f, 0.95f);
            var mrt = kind.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0, 0.5f);
            mrt.pivot = new Vector2(0, 0.5f);
            mrt.sizeDelta = new Vector2(14, 14);
            mrt.anchoredPosition = new Vector2(left, 0);
            left += 18f;
        }

        var text = UIFactory.Text(row.transform, count > 0 ? $"{label}  <size=9>({count})</size>" : label,
                                  indent == 0 ? 12 : 11, UITheme.Text, TextAlignmentOptions.Left);
        var trt = text.rectTransform;
        trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(left, 6); trt.offsetMax = new Vector2(-96, 0);
        text.raycastTarget = false;

        // ---- the condition bar ----
        var track = UIFactory.NewUI(row.transform, "Bar");
        var timg = track.AddComponent<Image>();
        timg.color = new Color(0, 0, 0, 0.45f);
        timg.raycastTarget = false;
        var krt = track.GetComponent<RectTransform>();
        krt.anchorMin = new Vector2(1, 0.5f); krt.anchorMax = new Vector2(1, 0.5f);
        krt.pivot = new Vector2(1, 0.5f);
        krt.sizeDelta = new Vector2(84, 9);
        // Lifted clear when a supply strip is going under it, so the pair reads as one stacked
        // readout rather than two bars fighting for the same few pixels.
        krt.anchoredPosition = new Vector2(-6, supply >= 0f ? 3f : 0f);

        var fill = UIFactory.NewUI(track.transform, "Fill");
        var fimg = fill.AddComponent<Image>();
        fimg.color = Fleets.ConditionColor(condition);
        fimg.raycastTarget = false;
        var frt = fill.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(Mathf.Clamp01(condition), 1);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

        // ---- the supply strip ----
        //
        // A second, thinner bar tucked under the condition bar, and drawn ONLY for hulls that carry
        // ordnance — a colony ship with an empty ammunition bar would be reporting a problem it can
        // never have. Deliberately a different shape as well as a different colour: a fleet is read at
        // a glance and two bars of the same weight side by side get confused with each other.
        if (supply >= 0f)
        {
            var stripTrack = UIFactory.NewUI(row.transform, "Supply");
            var simg = stripTrack.AddComponent<Image>();
            simg.color = new Color(0, 0, 0, 0.45f);
            simg.raycastTarget = false;
            var srt = stripTrack.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(1, 0.5f); srt.anchorMax = new Vector2(1, 0.5f);
            srt.pivot = new Vector2(1, 0.5f);
            srt.sizeDelta = new Vector2(84, 4);
            srt.anchoredPosition = new Vector2(-6, -8);

            var sfill = UIFactory.NewUI(stripTrack.transform, "Fill");
            var sfimg = sfill.AddComponent<Image>();
            // Amber for ordnance, and it goes red only when a mount is genuinely empty rather than
            // merely low — a warship at 20% has one more salvo and should not be flagged as beaten.
            sfimg.color = supply <= 0.001f ? new Color(0.90f, 0.30f, 0.25f)
                                           : new Color(1.00f, 0.72f, 0.30f);
            sfimg.raycastTarget = false;
            var sfrt = sfill.GetComponent<RectTransform>();
            sfrt.anchorMin = new Vector2(0, 0);
            sfrt.anchorMax = new Vector2(Mathf.Clamp01(supply), 1);
            sfrt.offsetMin = Vector2.zero; sfrt.offsetMax = Vector2.zero;
        }

        var pct = UIFactory.Text(row.transform, $"{condition * 100f:F0}%", 9, UITheme.Text,
                                 TextAlignmentOptions.Right);
        var prt = pct.rectTransform;
        prt.anchorMin = new Vector2(1, 0.5f); prt.anchorMax = new Vector2(1, 0.5f);
        prt.pivot = new Vector2(1, 0.5f);
        prt.sizeDelta = new Vector2(40, 12);
        prt.anchoredPosition = new Vector2(-96, 0);
        pct.raycastTarget = false;

        var btn = row.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() =>
        {
            // The left edge is the disclosure arrow, the rest of the row is the selection. One row,
            // two jobs, and no second control to hunt for at this size.
            bool onArrow = onExpand != null &&
                           Input.mousePosition.x - row.GetComponent<RectTransform>().position.x < left + 18f;
            if (onArrow) onExpand();
            else onSelect?.Invoke();
        });

        UIFactory.Tooltip(row, tip);
    }
}
