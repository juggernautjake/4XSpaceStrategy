using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// A reference viewer for every terrain tile type. A scrollable list on the left (each row a colour
// swatch + name); click or hover a row and the right pane shows a LARGE swatch of that tile's colour
// plus its description and the conditions that generate it (elevation, temperature, moisture, world
// types) — read from TileCatalog. Purely informational; opens from the HUD's "Tiles" button.
public class TileCatalogWindow : MonoBehaviour
{
    public static TileCatalogWindow Instance;

    GameObject root;
    Image bigSwatch;
    TMP_Text detailText;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("TileCatalogWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<TileCatalogWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Tile Catalogue", new Vector2(760, 580), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        // Left half: the scrollable list of every tile type.
        var listHolder = UIFactory.NewUI(content, "ListHolder").GetComponent<RectTransform>();
        listHolder.anchorMin = new Vector2(0, 0); listHolder.anchorMax = new Vector2(0.5f, 1);
        listHolder.offsetMin = new Vector2(0, 0); listHolder.offsetMax = new Vector2(-6, 0);
        UIFactory.ScrollView(listHolder, out RectTransform listCol);

        foreach (var e in TileCatalog.All) BuildRow(listCol, e);

        // Right half: the detail pane — a big colour swatch over the selected tile's stats.
        var detail = UIFactory.NewUI(content, "Detail").GetComponent<RectTransform>();
        detail.anchorMin = new Vector2(0.5f, 0); detail.anchorMax = new Vector2(1, 1);
        detail.offsetMin = new Vector2(6, 0); detail.offsetMax = new Vector2(0, 0);
        var dv = detail.gameObject.AddComponent<VerticalLayoutGroup>();
        dv.padding = new RectOffset(10, 10, 10, 10); dv.spacing = 8;
        dv.childControlWidth = true; dv.childControlHeight = true;
        dv.childForceExpandWidth = true; dv.childForceExpandHeight = false;
        dv.childAlignment = TextAnchor.UpperLeft;

        var swatchGo = UIFactory.NewUI(detail, "BigSwatch");
        var sle = swatchGo.AddComponent<LayoutElement>(); sle.preferredHeight = 150; sle.minHeight = 150;
        bigSwatch = swatchGo.AddComponent<Image>();
        var outline = swatchGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.5f); outline.effectDistance = new Vector2(1.5f, -1.5f);

        detailText = UIFactory.WrapText(detail, "", UITheme.SmallSize, UITheme.Text);
        detailText.alignment = TextAlignmentOptions.TopLeft;
        var dtle = detailText.gameObject.AddComponent<LayoutElement>(); dtle.flexibleHeight = 1;

        Select(TileCatalog.All.Length > 0 ? TileCatalog.All[0].type : TerrainType.Ocean);
        root.SetActive(false);
    }

    void BuildRow(Transform parent, TileCatalog.Entry e)
    {
        var row = UIFactory.NewUI(parent, "Row_" + e.type);
        UIFactory.AddLayout(row, 26);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8; h.padding = new RectOffset(4, 6, 2, 2);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        var bg = row.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.04f);
        var btn = row.AddComponent<Button>();
        btn.targetGraphic = bg;
        var cap = e.type;
        btn.onClick.AddListener(() => Select(cap));
        row.AddComponent<TileCatalogHover>().Init(this, cap);

        var sw = UIFactory.NewUI(row.transform, "Swatch");
        var swle = sw.AddComponent<LayoutElement>();
        swle.preferredWidth = 22; swle.minWidth = 22; swle.flexibleWidth = 0;
        var swImg = sw.AddComponent<Image>();
        swImg.color = TerrainColorMap.Get(e.type);
        swImg.raycastTarget = false;

        var label = UIFactory.Text(row.transform,
            $"<b>{Nice(e.type)}</b>  <size=10><color=#9FB4C8>{e.category}</color></size>",
            UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        label.raycastTarget = false;
        var lle = label.gameObject.AddComponent<LayoutElement>(); lle.flexibleWidth = 1;
    }

    public void Select(TerrainType t)
    {
        var e = TileCatalog.Get(t);
        if (bigSwatch != null) bigSwatch.color = TerrainColorMap.Get(t);
        if (detailText != null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<size=20><b>{Nice(t)}</b></size>");
            sb.AppendLine($"<color=#9FB4C8>{e.category}</color>\n");
            sb.AppendLine($"{e.desc}\n");
            sb.AppendLine($"<color=#8FD0FF>Elevation:</color>  {e.elevation}");
            sb.AppendLine($"<color=#8FD0FF>Temperature:</color>  {e.temperature}");
            sb.AppendLine($"<color=#8FD0FF>Moisture:</color>  {e.moisture}");
            sb.AppendLine($"<color=#8FD0FF>Found on:</color>  {e.worlds}");
            detailText.text = sb.ToString();
        }
    }

    // "FrozenSea" -> "Frozen Sea", "GasClouds" -> "Gas Clouds".
    static string Nice(TerrainType t)
    {
        string s = t.ToString();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    public void Toggle()
    {
        if (root == null) return;
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show) root.GetComponent<RectTransform>().SetAsLastSibling();
    }
}

// Hovering a catalogue row previews that tile in the detail pane (in addition to clicking it).
public class TileCatalogHover : MonoBehaviour, IPointerEnterHandler
{
    TileCatalogWindow win;
    TerrainType type;
    public void Init(TileCatalogWindow w, TerrainType t) { win = w; type = t; }
    public void OnPointerEnter(PointerEventData e) { if (win != null) win.Select(type); }
}
