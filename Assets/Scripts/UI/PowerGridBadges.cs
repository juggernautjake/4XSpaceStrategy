using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================================
// WHICH GRID IS EVERYTHING ON — tags on the map and a grid list (2026-10-11)
//
// "We need to be able to see what all utilities and stuff are on which grid."
//
// Shown whenever the power overlay is up (the map's bolt shortcut, the Survey toggle, or a power piece
// in hand on Build). Two halves:
//
//   ON THE MAP   every building on a grid wears a small tag in that grid's own colour (PowerGrid.GridColor):
//                  "2"     on Grid 2, lit directly by a plant or pylon (plants and pylons get a brighter tag)
//                  "2+1"   on Grid 2, fed through 1 adjacent building (the hop rule; at most +3)
//                  "x4"    too far: it touches Grid 2's chain but sits 4 buildings out (the limit is 3) —
//                          its tiles are also washed red, so "powered vs too far" reads at a glance
//                  "!"     wants power and no grid reaches it at all
//                Below a few pixels per tile only the plants and the problems keep their tags.
//
//   THE GRID LIST   a small panel in the map's bottom-left corner: one row per grid with its colour,
//                health, supply/demand, and how many plants, pylons, consumers and capacitors it has.
//                CLICK A ROW to highlight that grid's buildings on the map (washed in its colour, every
//                other tag dimmed); click it again, or another, to change. Ends with the legend.
//
// Read from PowerGrid's cached topology, refreshed twice a second; positions are re-laid every frame so
// the tags pan and zoom with the map. Pooled — no per-frame allocation.
// ============================================================================================
public class PowerGridBadges : MonoBehaviour
{
    public System.Func<CelestialBody> Body;
    public System.Func<bool> Visible;

    RectTransform root, chipLayer, viewport, panel, rows;
    RawImage tint;
    Texture2D tintTex;
    Color32[] tintPx;
    TMP_Text legend;

    class Chip
    {
        public RectTransform rt;
        public Image bg;
        public TMP_Text text;
        public Vector2 at;          // grid units (cell centre)
        public bool important;      // kept when zoomed far out
        public int grid;            // 0 = none
    }
    readonly List<Chip> chips = new List<Chip>();
    int chipsUsed;

    class Row { public RectTransform rt; public TMP_Text text; public int grid; }
    readonly List<Row> rowPool = new List<Row>();

    /// The grid highlighted from the list, or 0. Static so it survives the window rebuilding around it.
    public static int Highlighted { get; private set; }
    static CelestialBody highlightOn;

    CelestialBody cur;
    float nextRefresh;
    const float MinTagTilePx = 9f;

    public static PowerGridBadges Create(RectTransform mapRT)
    {
        var go = UIFactory.NewUI(mapRT, "PowerGridBadges");
        var pg = go.AddComponent<PowerGridBadges>();
        pg.root = go.GetComponent<RectTransform>();
        UIFactory.Stretch(pg.root);

        var tintGO = UIFactory.NewUI(pg.root, "GridTint");
        pg.tint = tintGO.AddComponent<RawImage>();
        pg.tint.raycastTarget = false;
        UIFactory.Stretch(pg.tint.rectTransform);

        pg.chipLayer = UIFactory.NewUI(pg.root, "Tags").GetComponent<RectTransform>();
        UIFactory.Stretch(pg.chipLayer);

        pg.viewport = mapRT.parent as RectTransform;
        pg.BuildPanel();
        return pg;
    }

    // ---- The list panel --------------------------------------------------------------------------

    void BuildPanel()
    {
        if (viewport == null) return;
        var bg = UIFactory.Panel(viewport, "GridList", new Color(UITheme.PanelBg.r, UITheme.PanelBg.g, UITheme.PanelBg.b, 0.93f));
        bg.raycastTarget = true;
        panel = bg.rectTransform;
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 0f);
        panel.pivot = new Vector2(0f, 0f);
        panel.anchoredPosition = new Vector2(8f, 8f);
        panel.sizeDelta = new Vector2(250f, 60f);
        var ol = bg.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(UITheme.AccentDim.r, UITheme.AccentDim.g, UITheme.AccentDim.b, 0.9f);
        ol.effectDistance = new Vector2(1f, -1f);

        var vl = bg.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(8, 8, 6, 6);
        vl.spacing = 3f;
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        var fit = bg.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var head = UIFactory.Text(panel, "<b>POWER GRIDS</b>", 12, UITheme.Accent);
        head.gameObject.AddComponent<LayoutElement>().minHeight = 16f;

        rows = UIFactory.NewUI(panel, "Rows").GetComponent<RectTransform>();
        var rl = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        rl.spacing = 2f;
        rl.childControlWidth = true; rl.childControlHeight = true;
        rl.childForceExpandWidth = true; rl.childForceExpandHeight = false;

        legend = UIFactory.Text(panel, "", 10, UITheme.SubText);
        legend.textWrappingMode = TextWrappingModes.Normal;
        legend.gameObject.AddComponent<LayoutElement>().minHeight = 14f;
        legend.text = $"Tags: <b>2</b> on Grid 2 · <b>2+1</b> fed through 1 building (max {PowerGrid.MaxContactHops}) · " +
                      "<color=#FF6659><b>x4</b></color> too far · <color=#FF6659><b>!</b></color> no power. " +
                      "Click a grid to highlight it.";

        panel.gameObject.SetActive(false);
    }

    Row RowAt(int i)
    {
        while (rowPool.Count <= i)
        {
            var go = UIFactory.NewUI(rows, "GridRow");
            var img = go.AddComponent<Image>();
            img.color = UITheme.RowBg;
            var btn = go.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = btn.colors;
            colors.normalColor = Color.white; colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white; colors.pressedColor = new Color(0.8f, 0.9f, 1f);
            btn.colors = colors;
            var row = new Row { rt = go.GetComponent<RectTransform>() };
            btn.onClick.AddListener(() => Toggle(row.grid));
            var le = go.AddComponent<LayoutElement>(); le.minHeight = 30f;
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(5, 5, 2, 2);
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
            row.text = UIFactory.Text(go.transform, "", 11, UITheme.Text);
            row.text.textWrappingMode = TextWrappingModes.Normal;
            rowPool.Add(row);
        }
        return rowPool[i];
    }

    void Toggle(int grid)
    {
        Highlighted = Highlighted == grid ? 0 : grid;
        highlightOn = cur;
        nextRefresh = 0f;
        SimpleAudio.Instance?.PlayClick();
    }

    /// Is this screen point on the grid list? The Planet View asks before it pans or paints.
    public bool Covers(Vector2 screen)
        => panel != null && panel.gameObject.activeInHierarchy
        && RectTransformUtility.RectangleContainsScreenPoint(panel, screen, EventCam());

    Camera EventCam()
    {
        var c = GetComponentInParent<Canvas>();
        c = c != null ? c.rootCanvas : null;
        return c == null || c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
    }

    // ---- Per frame -------------------------------------------------------------------------------

    void LateUpdate()
    {
        var b = Body != null ? Body() : null;
        bool show = b?.surface != null && (Visible == null || Visible());
        if (chipLayer.gameObject.activeSelf != show) chipLayer.gameObject.SetActive(show);
        if (tint.gameObject.activeSelf != show) tint.gameObject.SetActive(show);
        if (panel != null && panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
        if (!show) return;

        if (b != cur) { cur = b; nextRefresh = 0f; }
        if (highlightOn != cur) Highlighted = 0;

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            Refresh(b);
        }
        Layout(b);
    }

    void Layout(CelestialBody b)
    {
        int w = b.surface.width, h = b.surface.height;
        var r = root.rect;
        float tilePx = r.width / Mathf.Max(1, w);
        bool near = tilePx >= MinTagTilePx;
        for (int i = 0; i < chipsUsed; i++)
        {
            var c = chips[i];
            bool on = near || c.important;
            if (c.rt.gameObject.activeSelf != on) c.rt.gameObject.SetActive(on);
            if (!on) continue;
            // Hung from the top edge of its cell so it sits on the building's roofline, not over its art.
            c.rt.anchoredPosition = new Vector2((c.at.x / w - 0.5f) * r.width, ((c.at.y + 0.95f) / h - 0.5f) * r.height);
        }
    }

    void Refresh(CelestialBody b)
    {
        var nets = PowerGrid.Nets(b);
        chipsUsed = 0;

        int w = b.surface.width, h = b.surface.height;
        if (tintPx == null || tintPx.Length != w * h) tintPx = new Color32[w * h];
        System.Array.Clear(tintPx, 0, tintPx.Length);
        bool anyTint = false;

        foreach (var p in SurfaceBuildManager.On(b))
        {
            var info = p.Info;
            if (info == null) continue;
            var net = PowerGrid.NetOf(b, p);
            var cells = SurfaceBuildingDatabase.Footprint(p);
            if (cells.Count == 0) continue;

            string label = null; Color col = Color.white; bool important = false; int grid = 0; bool bright = false;
            if (net != null)
            {
                int hop = PowerGrid.HopOf(b, p);
                grid = net.index;
                col = PowerGrid.GridColor(net.index);
                label = hop > 0 ? $"{net.index}+{hop}" : net.index.ToString();
                bright = PowerGrid.Projects(info);
                important = info.energyPerSec > 0f;
                if (Highlighted == net.index) { Paint(cells, w, h, col, 120); anyTint = true; }
            }
            else if (PowerGrid.TooFar(b, p, out var nearNet, out int hops))
            {
                label = $"x{hops}";
                col = UITheme.Bad;
                important = true;
                grid = nearNet != null ? nearNet.index : 0;
                Paint(cells, w, h, new Color(1f, 0.25f, 0.2f), 105);
                anyTint = true;
            }
            else if (info.powerDraw > 0f || info.powerStorage > 0f)
            {
                label = "!";
                col = UITheme.Bad;
                important = true;
            }
            if (label == null) continue;

            // The topmost-leftmost cell: a consistent place on every building's roof.
            var top = cells[0];
            foreach (var c in cells) if (c.y > top.y || (c.y == top.y && c.x < top.x)) top = c;

            var chip = ChipAt(chipsUsed++);
            chip.at = new Vector2(top.x + 0.06f, top.y);
            chip.important = important;
            chip.grid = grid;
            bool dim = Highlighted != 0 && grid != Highlighted;
            float a = dim ? 0.35f : 1f;
            Color fill = bright ? col : Color.Lerp(col, Color.black, 0.45f);
            chip.bg.color = new Color(fill.r, fill.g, fill.b, 0.92f * a);
            chip.text.color = new Color(bright ? 0.05f : 1f, bright ? 0.07f : 1f, bright ? 0.1f : 1f, a);
            chip.text.text = label;
            float wpx = 8f + label.Length * 6.5f;
            chip.rt.sizeDelta = new Vector2(wpx, 13f);
        }
        for (int i = chipsUsed; i < chips.Count; i++)
            if (chips[i].rt.gameObject.activeSelf) chips[i].rt.gameObject.SetActive(false);

        // The wash: the highlighted grid in its colour, too-far buildings in red.
        if (tintTex == null || tintTex.width != w || tintTex.height != h)
        {
            if (tintTex != null) Destroy(tintTex);
            tintTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }
        tintTex.SetPixels32(tintPx);
        tintTex.Apply(false);
        tint.texture = tintTex;
        tint.enabled = anyTint;

        RefreshRows(nets);
    }

    void Paint(List<Vector2Int> cells, int w, int h, Color c, byte alpha)
    {
        var c32 = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), alpha);
        foreach (var cell in cells)
            if (cell.x >= 0 && cell.y >= 0 && cell.x < w && cell.y < h) tintPx[cell.y * w + cell.x] = c32;
    }

    Chip ChipAt(int i)
    {
        while (chips.Count <= i)
        {
            var go = UIFactory.NewUI(chipLayer, "tag");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            var t = UIFactory.Text(rt, "", 10, Color.white, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            UIFactory.Stretch(t.rectTransform);
            chips.Add(new Chip { rt = rt, bg = img, text = t });
        }
        var chip = chips[i];
        if (!chip.rt.gameObject.activeSelf) chip.rt.gameObject.SetActive(true);
        return chip;
    }

    void RefreshRows(List<PowerNet> nets)
    {
        if (rows == null) return;
        int i = 0;
        var sb = new StringBuilder();
        foreach (var n in nets)
        {
            var row = RowAt(i++);
            row.grid = n.index;
            if (!row.rt.gameObject.activeSelf) row.rt.gameObject.SetActive(true);

            int pylons = 0;
            foreach (var p in n.projectors) if (p.Info != null && p.Info.energyPerSec <= 0f) pylons++;
            string ghex = ColorUtility.ToHtmlStringRGB(PowerGrid.GridColor(n.index));
            string shex = ColorUtility.ToHtmlStringRGB(PowerGrid.SupplyColor(n));
            bool hl = Highlighted == n.index;

            sb.Clear();
            sb.Append($"<color=#{ghex}>•</color> <b>Grid {n.index}</b>{(hl ? " <color=#FFF266>(shown)</color>" : "")}  ");
            sb.Append($"<color=#{shex}>{PowerGrid.SupplyLabel(n)}</color>");
            sb.Append($"\n<size=10><color=#4DFF6E>+{n.generation:0.0}</color> / <color=#FF6659>-{n.draw:0.0}</color>");
            if (n.storage > 0f) sb.Append($" · bank {n.Stored:0}/{n.storage:0}");
            sb.Append($"<color=#9FB4C8> · {n.generators.Count} plant{(n.generators.Count == 1 ? "" : "s")}, {pylons} pylon{(pylons == 1 ? "" : "s")}, " +
                      $"{n.consumers.Count} user{(n.consumers.Count == 1 ? "" : "s")}, {n.capacitors.Count} cap</color></size>");
            row.text.text = sb.ToString();
            row.rt.GetComponent<Image>().color = hl ? new Color(0.16f, 0.24f, 0.34f, 0.95f) : UITheme.RowBg;
        }
        if (nets.Count == 0)
        {
            var row = RowAt(i++);
            row.grid = 0;
            if (!row.rt.gameObject.activeSelf) row.rt.gameObject.SetActive(true);
            row.text.text = "<color=#FFBF4D>No grid on this world.</color> <size=10><color=#9FB4C8>Build a plant from the Electrical tab.</color></size>";
            row.rt.GetComponent<Image>().color = UITheme.RowBg;
        }
        for (; i < rowPool.Count; i++)
            if (rowPool[i].rt.gameObject.activeSelf) rowPool[i].rt.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (tintTex != null) Destroy(tintTex);
    }
}
