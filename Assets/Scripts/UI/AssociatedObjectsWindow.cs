using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Lists the objects associated with the selected body — its parent planet and sibling moons (and, in
// future, comets and stations orbiting them) — each as a clickable row with a small live mini-map
// thumbnail. Selecting a row selects that body, so you can hop between a planet and its moons and see
// all their maps stacked together above the main planet map.
public class AssociatedObjectsWindow : MonoBehaviour
{
    public static AssociatedObjectsWindow Instance;

    // Constant pixels-per-tile for every thumbnail — so, exactly like the mini/detailed maps, the tile
    // size never changes and only the thumbnail's overall size scales with the world's grid. A bigger
    // world simply shows a bigger thumbnail, never bigger tiles.
    const float ThumbTilePx = 2.4f;

    // Hard ceiling on a thumbnail's width in pixels. Below it a bigger world really does show a bigger
    // thumbnail; past it the tiles shrink instead, so a mass-6 world cannot hand a list row a 1500px box.
    const float ThumbMaxW = 320f;

    GameObject root;
    TMP_Text title;
    RectTransform listRoot;
    readonly List<Texture2D> thumbs = new List<Texture2D>();

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("AssociatedObjectsWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<AssociatedObjectsWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Associated Objects", new Vector2(300, 300), out root, out title);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -64);   // sits near the top, above the planet map

        var holder = UIFactory.NewUI(content, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder);
        UIFactory.ScrollView(holder, out listRoot);

        PlanetUI.OnBodySelected += ShowFor;
        PlanetUI.OnClosed += Hide;
        root.SetActive(false);
    }

    void ShowFor(CelestialBody b)
    {
        if (b == null) { Hide(); return; }

        // The "family" is the parent planet plus its moons (from a moon you also see its siblings).
        var anchor = b.parentBody != null ? b.parentBody : b;
        var family = new List<CelestialBody> { anchor };
        foreach (var m in anchor.moons) family.Add(m);

        // Only worth showing when there's more than a lone planet.
        if (family.Count <= 1) { Hide(); return; }

        title.text = $"Around {anchor.name}";
        Rebuild(family, b);
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    void Rebuild(List<CelestialBody> family, CelestialBody selected)
    {
        ClearThumbs();
        for (int i = listRoot.childCount - 1; i >= 0; i--) Destroy(listRoot.GetChild(i).gameObject);

        foreach (var body in family)
        {
            bool isSel = body == selected;
            bool isMoon = body.parentBody != null;

            // Thumbnail dimensions. These used to be `cells * ThumbTilePx` with no ceiling, which was
            // already producing 920px-wide rows at the old 384-cell maximum and would produce 1500px rows
            // now that grid width tracks mass. A list row is a fixed piece of UI — the thumbnail has to
            // fit the row, not the other way round — so the tile size is derived from a target box and
            // the grid's aspect, rather than the box being derived from the tile size.
            int sw = MapMetrics.SurfW(body), sh = MapMetrics.SurfH(body);
            float scale = Mathf.Min(ThumbTilePx, ThumbMaxW / Mathf.Max(1, sw));
            float thumbW = sw * scale, thumbH = sh * scale;

            var row = UIFactory.Panel(listRoot, "Row", isSel ? new Color(0.16f, 0.26f, 0.36f, 0.98f) : UITheme.RowBg);
            UIFactory.AddLayout(row.gameObject, Mathf.Max(40f, thumbH + 10f));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(6, 6, 4, 4); h.spacing = 8;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;

            // Live mini-map thumbnail (only once the body has been visited). Same tile size for every
            // body — the box just grows with the world's grid.
            var thumbGo = UIFactory.NewUI(row.transform, "Thumb");
            var le = thumbGo.AddComponent<LayoutElement>();
            le.preferredWidth = thumbW; le.preferredHeight = thumbH; le.flexibleWidth = 0; le.flexibleHeight = 0;
            var raw = thumbGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var tex = body.Visited ? SurfaceThumb(body) : null;
            if (tex != null) { raw.texture = tex; thumbs.Add(tex); }
            else { raw.color = new Color(0.12f, 0.16f, 0.22f, 1f); }

            string tag = isMoon ? "moon" : body.type.ToString();
            string sub = body.Visited ? "" : "  <color=#FFBF4D>(unexplored)</color>";
            var lbl = UIFactory.Text(row.transform, $"<b>{body.name}</b>  <size=11><color=#9FB4C8>{tag}</color></size>{sub}",
                UITheme.SmallSize, isSel ? UITheme.Accent : UITheme.Text, TextAlignmentOptions.Left);
            lbl.raycastTarget = false;

            var btn = row.gameObject.AddComponent<Button>();
            var cap = body;
            btn.onClick.AddListener(() => { SimpleAudio.Instance?.PlayClick(); PlanetUI.Instance?.Show(cap); });
        }
    }

    // Render a body's surface grid to a tiny point-filtered texture using the same terrain colours as
    // the main map, so the thumbnail matches what you'd see zoomed in.
    Texture2D SurfaceThumb(CelestialBody b)
    {
        var s = b.surface;
        if (s == null || s.tiles == null || s.width <= 0 || s.height <= 0) return null;
        var tex = new Texture2D(s.width, s.height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s.width * s.height];
        for (int y = 0; y < s.height; y++)
            for (int x = 0; x < s.width; x++)
            {
                var t = s.tiles[x, y];
                Color c = t != null ? TerrainColorMap.Get(t.type) : new Color(0.10f, 0.10f, 0.12f);
                px[y * s.width + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }

    void ClearThumbs()
    {
        foreach (var t in thumbs) if (t != null) Destroy(t);
        thumbs.Clear();
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        ClearThumbs();
    }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= ShowFor;
        PlanetUI.OnClosed -= Hide;
        ClearThumbs();
    }
}
