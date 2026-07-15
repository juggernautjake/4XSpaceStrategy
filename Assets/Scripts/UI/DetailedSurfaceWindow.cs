using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// The expanded, high-detail surface map. Shows the same continents as the low-res grid (both sample
// the same field/params) but far more finely, plus points of interest. Hovering the map shows terrain
// under the cursor; hovering a POI shows its details; RIGHT-clicking a mystery/resource opens a menu
// to Research it, which runs a timed job shown by the progress bar at the bottom.
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

    GameObject progressRoot;
    Image progressFill;
    TMP_Text progressLabel;

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
        var content = UIFactory.Window(parent, "Detailed Surface", new Vector2(MapW + 40, MapH + 156), out root, out titleText);
        rootRT = root.GetComponent<RectTransform>();
        rootRT.anchoredPosition = new Vector2(60, 0);

        var mapGO = UIFactory.NewUI(content, "Map");
        map = mapGO.AddComponent<RawImage>();
        mapRT = map.rectTransform;
        mapRT.anchorMin = new Vector2(0.5f, 1f); mapRT.anchorMax = new Vector2(0.5f, 1f);
        mapRT.pivot = new Vector2(0.5f, 1f);
        mapRT.sizeDelta = new Vector2(MapW, MapH);
        mapRT.anchoredPosition = new Vector2(0, -4);
        mapGO.AddComponent<Outline>().effectColor = UITheme.AccentDim;

        markerLayer = UIFactory.NewUI(mapGO.transform, "Markers").GetComponent<RectTransform>();
        UIFactory.Stretch(markerLayer);

        var probe = mapGO.AddComponent<MapHoverProbe>();
        probe.Init(this, mapRT);

        var legend = UIFactory.Text(content, LegendText(), UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.TopLeft);
        var lrt = legend.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 0);
        lrt.pivot = new Vector2(0.5f, 0); lrt.sizeDelta = new Vector2(0, 96); lrt.anchoredPosition = new Vector2(0, 30);

        BuildProgressBar(content);

        root.SetActive(false);
    }

    void BuildProgressBar(RectTransform content)
    {
        progressRoot = UIFactory.NewUI(content, "Progress");
        var prt = progressRoot.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0, 0); prt.anchorMax = new Vector2(1, 0);
        prt.pivot = new Vector2(0.5f, 0); prt.sizeDelta = new Vector2(-8, 22); prt.anchoredPosition = new Vector2(0, 4);

        var track = UIFactory.Panel(prt, "Track", UITheme.TrackBg);
        UIFactory.Stretch(track.rectTransform);
        progressFill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
        var frt = progressFill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

        progressLabel = UIFactory.Text(prt, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Center);
        UIFactory.Stretch(progressLabel.rectTransform);

        progressRoot.SetActive(false);
    }

    static string LegendText()
    {
        return "<color=#4DFF6E>C</color> Settlement    <color=#B98CFF>R</color> Ancient Ruins    " +
               "<color=#8FD0FF>M</color> Special Resource    <color=#FFD24D>?</color> Mystery\n" +
               "Left-click a marker for info. <b>Right-click a mystery or resource to Research it.</b>\n" +
               "Hover the map for terrain. Shapes match the small viewer — this view just shows more detail.";
    }

    public CelestialBody Body => body;
    public RectTransform RootRect => rootRT;

    public void Open(CelestialBody b)
    {
        body = b;
        if (titleText != null) titleText.text = $"Detailed Surface — {b.name}";
        ApplySize(b);
        map.texture = SurfaceTextureRenderer.Build(b);   // same colours as the Planet View build grid
        BuildMarkers();
        root.SetActive(true);
        rootRT.SetAsLastSibling();
    }

    // The detailed map is exactly MapMetrics.DetailFactor times the mini map (same pixels-per-tile
    // ratio for every body), so a small moon shows a small map and a giant world a large one — and the
    // two views stay proportionate. Markers use normalized anchors, so they scale automatically.
    void ApplySize(CelestialBody b)
    {
        int size = b != null ? b.surfaceSize : 12;
        float tile = MapMetrics.DetailTile(size);
        float w = MapMetrics.SurfW(size) * tile, h = MapMetrics.SurfH(size) * tile;
        mapRT.sizeDelta = new Vector2(w, h);
        rootRT.sizeDelta = new Vector2(w + 40f, h + 156f);
    }

    public void RefreshIfShowing(CelestialBody b)
    {
        if (root != null && root.activeSelf && body == b)
        {
            map.texture = SurfaceTextureRenderer.Build(b);   // same colours as the Planet View build grid
            BuildMarkers();
        }
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
        go.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);

        var label = UIFactory.Text(go.transform, "", 13, Color.black, TextAlignmentOptions.Center);
        UIFactory.Stretch(label.rectTransform);

        go.AddComponent<POIMarker>().Init(this, poi, img, label);
    }

    void Update()
    {
        if (!IsOpen || body == null) return;
        var task = ResearchTaskManager.Instance != null ? ResearchTaskManager.Instance.GetActiveFor(body) : null;
        if (task == null) { progressRoot.SetActive(false); return; }

        progressRoot.SetActive(true);
        progressFill.rectTransform.anchorMax = new Vector2(task.Progress, 1f);
        string name = task.poi.type == POIType.Mystery
            ? (string.IsNullOrEmpty(task.poi.revealTitle) ? task.poi.kind : task.poi.revealTitle)
            : task.poi.title;
        progressLabel.text = $"Researching {name}:  {task.Progress * 100f:F0}%   ({task.Remaining:F0}s)";
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
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRT, Input.mousePosition, null, out Vector2 local)) return;

        Rect r = mapRT.rect;
        float u = Mathf.Clamp01((local.x - r.xMin) / r.width);
        float v = Mathf.Clamp01((local.y - r.yMin) / r.height);

        var b = window.Body;
        var s = PlanetTerrainGenerator.SampleNormalized(b, u, v, b.terrainParams, 6);

        var sb = new StringBuilder();
        sb.Append($"<b>{s.terrain}</b>\n{TerrainColorMap.Describe(s.terrain)}");
        if (b.surface != null)
        {
            int lx = Mathf.Clamp((int)(u * b.surface.width), 0, b.surface.width - 1);
            int ly = Mathf.Clamp((int)(v * b.surface.height), 0, b.surface.height - 1);
            var tile = b.surface.tiles[lx, ly];
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

// A point-of-interest pin. Hover shows details; right-click opens the research menu.
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
        if (e.button == PointerEventData.InputButton.Right)
        {
            var opts = new List<ContextMenu.Option>();
            var rtm = ResearchTaskManager.Instance;
            bool inProg = rtm != null && rtm.IsResearching(poi);
            if (poi.IsResearchable)
            {
                // Say what the dig costs and what it pays, and why it's unavailable when it is —
                // an anomaly you can't afford should say so rather than ignore the click.
                bool can = false;
                string why = "unavailable";
                if (rtm != null) can = rtm.CanStart(window.Body, poi, out why);
                string label = inProg
                    ? "Researching…"
                    : can
                        ? $"Research — {poi.researchPointCost} pts, ~{poi.researchDuration:F0}s (+{poi.researchReward} pts" +
                          (poi.yieldsSchematic ? ", may recover a schematic)" : ")")
                        : $"Research — {why}";
                opts.Add(new ContextMenu.Option(label, () => rtm?.StartResearch(window.Body, poi), can && !inProg));
            }
            else
                opts.Add(new ContextMenu.Option("Already studied", null, false));

            opts.Add(new ContextMenu.Option("View info", () =>
                TooltipManager.Instance.ShowAboveRect(window.RootRect, poi.HoverText())));

            ContextMenu.Instance?.Show(e.position, opts);
        }
        else
        {
            TooltipManager.Instance.ShowAboveRect(window.RootRect, poi.HoverText());
        }
    }
}
