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
        g.cellSize = new Vector2(52, 60);
        g.spacing = new Vector2(6, 6);
        g.padding = new RectOffset(4, 4, 4, 4);

        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged += RefreshIfShowing;
        UnitSelection.OnChanged += RefreshIfShowing;

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

    void Refresh()
    {
        for (int i = grid.childCount - 1; i >= 0; i--) Destroy(grid.GetChild(i).gameObject);

        foreach (var u in body.units)
            CreateIcon(u);
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
        irt.anchorMin = new Vector2(0.15f, 0.3f); irt.anchorMax = new Vector2(0.85f, 0.95f);
        irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
        raw.raycastTarget = false;

        var label = UIFactory.Text(item.transform, u.name, 9, UITheme.Text, TextAlignmentOptions.Center);
        var lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 0.28f);
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

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

    void OnDestroy()
    {
        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged -= RefreshIfShowing;
        UnitSelection.OnChanged -= RefreshIfShowing;
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
