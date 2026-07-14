using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// The expanded, high-detail surface map. Shows the same continents as the low-res grid (both sample
// the same noise field) but far more finely, plus points of interest. Hovering the map shows the
// terrain under the cursor; hovering a POI shows its details; clicking a mystery explores it.
public class DetailedSurfaceWindow : MonoBehaviour
{
    public static DetailedSurfaceWindow Instance;

    GameObject root;
    RectTransform rootRT;
    TMP_Text titleText;
    RawImage map;
    RectTransform mapRT;
    RectTransform markerLayer;
    CelestialBody body;

    const float MapW = 660f, MapH = 330f;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("DetailedSurfaceWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<DetailedSurfaceWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Detailed Surface", new Vector2(MapW + 40, MapH + 130), out root, out titleText);
        rootRT = root.GetComponent<RectTransform>();
        rootRT.anchoredPosition = new Vector2(60, 0);

        var mapGO = UIFactory.NewUI(content, "Map");
        map = mapGO.AddComponent<RawImage>();
        mapRT = map.rectTransform;
        mapRT.anchorMin = new Vector2(0.5f, 1f); mapRT.anchorMax = new Vector2(0.5f, 1f);
        mapRT.pivot = new Vector2(0.5f, 1f);
        mapRT.sizeDelta = new Vector2(MapW, MapH);
        mapRT.anchoredPosition = new Vector2(0, -4);
        var mapOutline = mapGO.AddComponent<Outline>();
        mapOutline.effectColor = UITheme.AccentDim;

        markerLayer = UIFactory.NewUI(mapGO.transform, "Markers").GetComponent<RectTransform>();
        UIFactory.Stretch(markerLayer);
        markerLayer.GetComponent<RectTransform>();

        var probe = mapGO.AddComponent<MapHoverProbe>();
        probe.Init(this, mapRT);

        // Legend
        var legend = UIFactory.Text(content, LegendText(), UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.TopLeft);
        var lrt = legend.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 0);
        lrt.pivot = new Vector2(0.5f, 0); lrt.sizeDelta = new Vector2(0, 74); lrt.anchoredPosition = new Vector2(0, 4);

        root.SetActive(false);
    }

    static string LegendText()
    {
        return "<color=#4DFF6E>C</color> Settlement   <color=#B98CFF>R</color> Ancient Ruins   " +
               "<color=#8FD0FF>M</color> Special Resource   <color=#FFD24D>?</color> Mystery (click to explore)\n" +
               "Hover the map for terrain. Shapes match the small viewer — this view just shows more detail.";
    }

    public CelestialBody Body => body;
    public RectTransform RootRect => rootRT;

    public void Open(CelestialBody b)
    {
        body = b;
        if (titleText != null) titleText.text = $"Detailed Surface — {b.name}";

        var tex = SurfaceTextureRenderer.Build(b);
        map.texture = tex;

        BuildMarkers();
        root.SetActive(true);
        rootRT.SetAsLastSibling();
    }

    public void Close() { if (root != null) root.SetActive(false); }
    public bool IsOpen => root != null && root.activeSelf;

    void BuildMarkers()
    {
        for (int i = markerLayer.childCount - 1; i >= 0; i--)
            Destroy(markerLayer.GetChild(i).gameObject);

        foreach (var poi in body.pointsOfInterest)
            CreateMarker(poi);
    }

    void CreateMarker(PointOfInterest poi)
    {
        var go = UIFactory.NewUI(markerLayer, "POI");
        var img = go.AddComponent<Image>();
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(Mathf.Clamp01(poi.u), Mathf.Clamp01(poi.v));
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(20, 20);

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.8f);

        var label = UIFactory.Text(go.transform, "", 13, Color.black, TextAlignmentOptions.Center);
        UIFactory.Stretch(label.rectTransform);

        var marker = go.AddComponent<POIMarker>();
        marker.Init(this, poi, img, label);
    }
}

// Shows the terrain under the cursor while hovering the detailed map.
public class MapHoverProbe : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    DetailedSurfaceWindow window;
    RectTransform mapRT;
    bool hovering;

    public void Init(DetailedSurfaceWindow w, RectTransform rt) { window = w; mapRT = rt; }
    public void OnPointerEnter(PointerEventData e) { hovering = true; }
    public void OnPointerExit(PointerEventData e) { hovering = false; if (!POIMarker.HoveringAny) TooltipManager.Instance.Hide(); }

    void Update()
    {
        if (!hovering || window == null || window.Body == null || POIMarker.HoveringAny) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRT, Input.mousePosition, null, out Vector2 local))
            return;
        Rect r = mapRT.rect;
        float u = Mathf.Clamp01((local.x - r.xMin) / r.width);
        float v = Mathf.Clamp01((local.y - r.yMin) / r.height);

        var body = window.Body;
        var s = PlanetTerrainGenerator.SampleNormalized(body, u, v, PlanetTerrainGenerator.NoiseParams.Default, 6);

        var sb = new StringBuilder();
        sb.Append($"<b>{s.terrain}</b>\n{TerrainColorMap.Describe(s.terrain)}");
        if (body.surface != null)
        {
            int lx = Mathf.Clamp((int)(u * body.surface.width), 0, body.surface.width - 1);
            int ly = Mathf.Clamp((int)(v * body.surface.height), 0, body.surface.height - 1);
            var tile = body.surface.tiles[lx, ly];
            if (tile != null && tile.HasOre)
            {
                var oi = OreDatabase.Get(tile.ore);
                bool known = ResearchManager.IsDiscovered(tile.ore);
                sb.Append(known ? $"\n<color=#8FD0FF>Ore: {oi.displayName}</color>" : "\n<color=#8FD0FF>Unidentified ore</color>");
            }
        }
        TooltipManager.Instance.ShowAboveRect(window.RootRect, sb.ToString());
    }
}

// A point-of-interest pin. Hover shows details; clicking a mystery reveals it.
public class POIMarker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public static bool HoveringAny;

    DetailedSurfaceWindow window;
    PointOfInterest poi;
    Image img;
    TMP_Text label;

    public void Init(DetailedSurfaceWindow w, PointOfInterest p, Image i, TMP_Text l)
    {
        window = w; poi = p; img = i; label = l;
        Refresh();
    }

    void Refresh()
    {
        switch (poi.type)
        {
            case POIType.Settlement:      img.color = new Color(0.30f, 1f, 0.45f); label.text = "C"; break;
            case POIType.AncientRuins:    img.color = new Color(0.72f, 0.55f, 1f);  label.text = "R"; break;
            case POIType.SpecialResource: img.color = new Color(0.56f, 0.82f, 1f);  label.text = "M"; break;
            case POIType.Mystery:
                img.color = poi.explored ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 0.82f, 0.30f);
                label.text = poi.explored ? "!" : "?";
                break;
        }
    }

    public void OnPointerEnter(PointerEventData e)
    {
        HoveringAny = true;
        TooltipManager.Instance.ShowAboveRect(window.RootRect, poi.HoverText());
    }

    public void OnPointerExit(PointerEventData e)
    {
        HoveringAny = false;
        TooltipManager.Instance.Hide();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (poi.type == POIType.Mystery && !poi.explored)
        {
            poi.explored = true;
            ResearchManager.AwardExploration();
            if (poi.relatedOre != OreType.None) ResearchManager.Discover(poi.relatedOre);
            Refresh();
            TooltipManager.Instance.ShowAboveRect(window.RootRect, poi.HoverText());
        }
        else if (poi.type == POIType.SpecialResource && poi.relatedOre != OreType.None)
        {
            ResearchManager.Discover(poi.relatedOre);
            TooltipManager.Instance.ShowAboveRect(window.RootRect, poi.HoverText());
        }
    }
}
