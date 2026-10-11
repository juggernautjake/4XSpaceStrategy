using System;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================================
// GRAVITY WELLS — ONE SWITCH FOR THE MAP'S GRIDS (2026-10-11)
//
// The setting: every planet's gravity-well grid (ZoomFieldRenderer) and the faint solar-system grid
// that lies under them (SystemGridVisual). ONE switch for both, on purpose: they are one picture — the
// system grid is the floor the wells sit on, and showing the wells without it (or it without them)
// is not a view anybody asked for. Two switches would be two chances to get a half-drawn map.
//
// ON by default and remembered in PlayerPrefs. While it is OFF a planet's own Orbit-tab toggle and the
// eight-second "here is where your station goes" highlight still show that one field, because those
// are explicit asks about one planet rather than a preference about the map.
// ============================================================================================
public static class MapGrids
{
    const string PrefKey = "MapGrids.GravityWells";

    static bool loaded;
    static bool show = true;

    /// Are the gravity wells (and the system grid under them) shown?
    public static bool Show
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                try { show = PlayerPrefs.GetInt(PrefKey, 1) != 0; } catch { show = true; }
            }
            return show;
        }
    }

    public static event Action OnChanged;

    public static void Set(bool on)
    {
        if (on == Show) return;
        show = on;
        try { PlayerPrefs.SetInt(PrefKey, on ? 1 : 0); PlayerPrefs.Save(); } catch { }
        OnChanged?.Invoke();
    }

    public static void Toggle() => Set(!Show);
}

/// The screen-space button for it: bottom-right corner of the main game screen, a pixel-art grid on a
/// black plate with the same bright square "on" frame as the Surface View's shortcut column, so the two
/// families of map switches read as one.
///
/// LAYOUT. Nothing else is anchored to the bottom-right corner of the main canvas: the Inspector, Ship
/// and Orbit windows anchor to the right EDGE's middle (their bottoms sit ~190px up at 1080p), the fleet
/// command bar and the onboarding banner are bottom-CENTRE (the bar's right edge is at x=1550 of 1920),
/// and the notification stack is top-centre. The button sits in the 16px gutter every one of those
/// uses, and is built before the windows so any window dragged over it draws on top.
public class MapGridToggle : MonoBehaviour
{
    public static MapGridToggle Instance;

    // 16px art drawn at exactly 2x (cell 38 minus a 3px inset each side = 32), so every art pixel is a
    // clean 2x2 block. A non-integer scale smears pixel art.
    const float CellPx = 38f, InsetPx = 3f, MarginPx = 16f, FramePx = 3f;
    static readonly Color Plate = new Color(0.02f, 0.02f, 0.03f, 0.88f);
    static readonly Color OnColor = new Color(0.55f, 0.85f, 1.00f);
    static readonly Color IconOn = Color.white;
    static readonly Color IconOff = new Color(0.55f, 0.58f, 0.63f);

    RectTransform cell;
    RawImage icon;
    Image frame;
    bool shownState = true;
    bool builtState;
    bool hasState;

    public static void Create(Transform canvas)
    {
        if (Instance != null || canvas == null) return;
        var go = new GameObject("MapGridToggle");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<MapGridToggle>();
        Instance.Build(canvas);
    }

    void Build(Transform canvas)
    {
        cell = UIFactory.NewUI(canvas, "GravityWellsButton").GetComponent<RectTransform>();
        cell.anchorMin = cell.anchorMax = new Vector2(1f, 0f);
        cell.pivot = new Vector2(1f, 0f);
        cell.sizeDelta = new Vector2(CellPx, CellPx);
        cell.anchoredPosition = new Vector2(-MarginPx, MarginPx);

        var plate = cell.gameObject.AddComponent<Image>();
        plate.color = Plate;
        var btn = cell.gameObject.AddComponent<Button>();
        btn.targetGraphic = plate;
        btn.onClick.AddListener(() =>
        {
            SimpleAudio.Instance?.PlayClick();
            MapGrids.Toggle();
        });

        icon = UIFactory.NewUI(cell, "Icon").AddComponent<RawImage>();
        icon.texture = PixelIcons.Grid;
        icon.raycastTarget = false;
        var irt = icon.rectTransform;
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(InsetPx, InsetPx); irt.offsetMax = new Vector2(-InsetPx, -InsetPx);

        // The "on" frame: four edges under a transparent holder, recoloured together — the same build as
        // the Surface View's shortcut column and the index bar.
        frame = UIFactory.Panel(cell, "Frame", new Color(0, 0, 0, 0));
        frame.raycastTarget = false;
        UIFactory.Stretch(frame.rectTransform);
        Edge(frame.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -FramePx), Vector2.zero);
        Edge(frame.rectTransform, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, FramePx));
        Edge(frame.rectTransform, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(FramePx, 0));
        Edge(frame.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-FramePx, 0), Vector2.zero);

        Refresh();
    }

    static void Edge(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var e = UIFactory.Panel(parent, "Edge", new Color(0, 0, 0, 0));
        e.raycastTarget = false;
        var rt = e.rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = oMin; rt.offsetMax = oMax;
    }

    void OnEnable() { MapGrids.OnChanged += Refresh; }
    void OnDisable() { MapGrids.OnChanged -= Refresh; }

    void Refresh()
    {
        if (cell == null) return;
        bool on = MapGrids.Show;
        if (hasState && builtState == on) return;
        hasState = true;
        builtState = on;

        if (icon != null) icon.color = on ? IconOn : IconOff;
        if (frame != null)
        {
            var col = on ? OnColor : new Color(0, 0, 0, 0);
            for (int i = 0; i < frame.transform.childCount; i++)
            {
                var img = frame.transform.GetChild(i).GetComponent<Image>();
                if (img != null) img.color = col;
            }
        }
        UIFactory.Tooltip(cell.gameObject, on
            ? "<b>Gravity wells: on</b>\nEach planet's gravity-well grid, over the faint grid of its solar system. Click to hide them."
            : "<b>Gravity wells: off</b>\nClick to show each planet's gravity-well grid and the solar-system grid beneath them.");
    }

    void Update()
    {
        if (cell == null) return;
        // Map furniture: nothing to switch before there is a map, or while the intro owns the camera.
        bool want = SystemContext.Galaxy != null && !GenesisCamera.Active;
        if (want != shownState)
        {
            shownState = want;
            cell.gameObject.SetActive(want);
        }
    }
}
