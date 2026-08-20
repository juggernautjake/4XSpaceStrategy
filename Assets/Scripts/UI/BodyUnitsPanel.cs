using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// When you click a body, this shows the ships currently there as selectable icons. Click an icon to
// select that ship and open its panel (to research, explore, colonize, send, etc.).
public class BodyUnitsPanel : MonoBehaviour
{
    public static BodyUnitsPanel Instance;

    GameObject root;
    RectTransform grid;
    TMP_Text titleText;
    CelestialBody body;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("BodyUnitsPanel");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<BodyUnitsPanel>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Ships Here", new Vector2(380, 200), out root, out titleText);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(16, 16);

        grid = UIFactory.NewUI(content, "Grid").GetComponent<RectTransform>();
        UIFactory.Stretch(grid);
        var g = grid.gameObject.AddComponent<GridLayoutGroup>();
        // Cell is tall enough that the bottom name band clears 20px (0.31 * 66 ≈ 20.5px) so the label
        // isn't clipped — UISanity flagged the old 0.28 * 60 ≈ 17px band as cutting off descenders.
        g.cellSize = new Vector2(52, 66);
        g.spacing = new Vector2(6, 6);
        g.padding = new RectOffset(4, 4, 4, 4);

        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged += RefreshIfShowing;
        UnitSelection.OnChanged += RefreshIfShowing;
        ControlGroups.OnChanged += RefreshIfShowing;   // re-stamp the badges when a group is rebound
        Squadrons.OnChanged += RefreshIfShowing;       // ...and re-title the sections when orders change

        root.SetActive(false);
    }

    public void ShowFor(CelestialBody b)
    {
        body = b;
        if (b == null || b.units == null || b.units.Count == 0) { root.SetActive(false); return; }
        titleText.text = $"Ships at {b.name} ({b.units.Count})";
        Refresh();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    void RefreshIfShowing()
    {
        if (root == null || !root.activeSelf) return;
        if (body == null || body.units == null || body.units.Count == 0) { root.SetActive(false); return; }
        Refresh();
    }

    // ============================================================================================
    // GROUPED BY SQUADRON, NOT ONE FLAT SHELF OF ICONS
    //
    // Twenty ships over a contested world used to arrive here as twenty identical tiles in reading
    // order, with a small group number stamped in the corner of each. That answers "what is here" and
    // not the question actually being asked, which is "what is here AS WHAT" — a defence squadron, a
    // survey wing and three unassigned freighters are three different facts, and the flat shelf made
    // the player reconstruct them by squinting at corner badges.
    //
    // So each squadron present gets a header naming it, its strength, and the standing orders it is
    // under, and the header SELECTS THE WHOLE SQUADRON — which is the thing a player wants to do
    // roughly every time they look at this panel. Ships in no squadron collect under "Unassigned" at
    // the bottom, where they read as what they are: loose hulls nobody has organised yet.
    // ============================================================================================
    void Refresh()
    {
        for (int i = grid.childCount - 1; i >= 0; i--) Destroy(grid.GetChild(i).gameObject);

        // Bucket by squadron, keeping the panel's existing order inside each bucket.
        var bySquadron = new System.Collections.Generic.SortedDictionary<int, System.Collections.Generic.List<Unit>>();
        foreach (var u in body.units)
        {
            int g = ControlGroups.GroupOf(u);
            // Unassigned sorts last: a squadron is a decision the player made, and the loose hulls are
            // the leftovers. int.MaxValue rather than 0, which would sort them first.
            int key = g >= 1 ? g : int.MaxValue;
            if (!bySquadron.TryGetValue(key, out var list))
                bySquadron[key] = list = new System.Collections.Generic.List<Unit>();
            list.Add(u);
        }

        foreach (var kv in bySquadron)
        {
            bool assigned = kv.Key != int.MaxValue;
            CreateSectionHeader(assigned ? kv.Key : 0, kv.Value);
            foreach (var u in kv.Value) CreateIcon(u);
        }
    }

    /// One squadron's heading, spanning the grid. Clicking it selects the squadron.
    void CreateSectionHeader(int group, System.Collections.Generic.List<Unit> members)
    {
        var row = UIFactory.NewUI(grid, group >= 1 ? $"Squadron{group}" : "Unassigned");
        var bg = row.AddComponent<Image>();
        bg.color = new Color(0.10f, 0.16f, 0.22f, 0.85f);

        // A header is a band, not a tile — opt it out of the grid's cell size.
        var le = row.AddComponent<LayoutElement>();
        le.ignoreLayout = false;
        var g = grid.GetComponent<GridLayoutGroup>();
        if (g != null) le.preferredWidth = g.cellSize.x;

        string title, tip;
        if (group >= 1)
        {
            var o = Squadrons.Of(group);
            title = $"<b>{group}</b> {Squadrons.NameOf(group)}  <size=8>({members.Count})</size>";
            tip = $"{Squadrons.NameOf(group)} — {members.Count} ship(s) here.\n" +
                  $"Formation: {o.formation}\nProtocol: {o.protocol}\n\n" +
                  $"Click to select the whole squadron. Press {group} anywhere to recall it.";
        }
        else
        {
            title = $"<b>Unassigned</b>  <size=8>({members.Count})</size>";
            tip = $"{members.Count} ship(s) here that belong to no squadron.\n\n" +
                  "Click to select them all, then Ctrl+1..9 to bind them into one.";
        }

        var label = UIFactory.Text(row.transform, title, 10, UITheme.Accent, TextAlignmentOptions.Left);
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(4, 0); lrt.offsetMax = new Vector2(-4, 0);
        label.raycastTarget = false;

        var captured = new System.Collections.Generic.List<Unit>(members);
        var btn = row.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() =>
        {
            UnitSelection.Set(captured);
            if (captured.Count > 0)
            {
                SimpleAudio.Instance?.PlayUnitSelect(captured[0].type);
                UnitInfoPanel.Instance?.Show(captured[0]);
            }
        });

        UIFactory.Tooltip(row, tip);
    }

    void CreateIcon(Unit u)
    {
        var item = UIFactory.NewUI(grid, "Unit");
        var img = item.AddComponent<Image>();
        img.color = UnitSelection.IsSelected(u) ? new Color(0.2f, 0.4f, 0.55f) : new Color(0, 0, 0, 0.25f);

        var iconGo = UIFactory.NewUI(item.transform, "Icon");
        var raw = iconGo.AddComponent<RawImage>();
        raw.texture = UnitIconRenderer.Get(u.type);
        var irt = raw.rectTransform;
        irt.anchorMin = new Vector2(0.15f, 0.34f); irt.anchorMax = new Vector2(0.85f, 0.96f);
        irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
        raw.raycastTarget = false;

        var label = UIFactory.Text(item.transform, u.name, 9, UITheme.Text, TextAlignmentOptions.Center);
        var lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 0.31f);
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

        AddGroupBadge(item.transform, u);

        var btn = item.AddComponent<Button>();
        btn.targetGraphic = img;
        var captured = u;
        btn.onClick.AddListener(() =>
        {
            bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            UnitSelection.Select(captured, add);
            SimpleAudio.Instance?.PlayUnitSelect(captured.type);
            UnitInfoPanel.Instance?.Show(captured);
        });

        item.AddComponent<UnitIconHover>().Init(captured);
    }

    // The little number in the corner showing which control group this ship belongs to. Bind a group
    // with Ctrl+N and every one of its ships is stamped here, so you can see at a glance what "3" is.
    public static void AddGroupBadge(Transform parent, Unit u)
    {
        int g = ControlGroups.GroupOf(u);
        if (g <= 0) return;

        var badge = UIFactory.Panel(parent, "GroupBadge", new Color(0.10f, 0.45f, 0.75f, 0.95f));
        var brt = badge.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
        brt.pivot = new Vector2(0f, 1f);
        brt.sizeDelta = new Vector2(14, 14);
        brt.anchoredPosition = new Vector2(1, -1);
        badge.raycastTarget = false;

        var t = UIFactory.Text(badge.transform, g.ToString(), 9, Color.white, TextAlignmentOptions.Center);
        UIFactory.Stretch(t.rectTransform);
    }

    void OnDestroy()
    {
        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged -= RefreshIfShowing;
        UnitSelection.OnChanged -= RefreshIfShowing;
        ControlGroups.OnChanged -= RefreshIfShowing;
    }
}

// Hover tooltip for a unit icon.
public class UnitIconHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    Unit unit;
    public void Init(Unit u) { unit = u; }
    public void OnPointerEnter(PointerEventData e) { if (unit != null) TooltipManager.Instance.ShowAtCursor(unit.HoverText()); }
    public void OnPointerExit(PointerEventData e) { TooltipManager.Instance.Hide(); }
}
