using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// ============================================================================================
// THE INDEXES BECOME MAP FURNITURE
//
// An index used to be something you went and switched on in the Survey tab, one at a time, from a
// list of cards. Three things were wrong with that and they compound:
//
//   IT WAS SOMEWHERE ELSE. The question "where is the good ground" is asked WHILE looking at the
//   ground. Making the answer live in a different tab means leaving the thing you are asking about.
//
//   IT WAS ONE AT A TIME. Every real siting decision is a conjunction — a farm wants fertile soil AND
//   a grid connection, a geothermal plant wants heat AND somewhere to put it. One index at a time
//   turns that into memorising one map and then looking at another.
//
//   IT DID NOT SAY WHAT WAS AVAILABLE. A world with no water still showed a Water card.
//
// So the indexes are now a row of icons in the top right corner of the map itself. The icons ARE the
// buttons. Several can be up at once, and their highlights are allowed to overlap — which is why every
// highlight dropped to 40% opacity (see SurfaceIndex.HighlightAlphaMax): at the old 94% the top index
// simply painted over everything under it and a second overlay was worse than useless.
//
// ---- WHAT IS SHOWN, AND WHEN -----------------------------------------------------------------
//
// An icon appears only if the world HAS that index — no plates means no Geothermal button, not a
// Geothermal button that reports nothing — and only once a level-2 survey has reached it, or is
// reaching it right now. That second half matters: the icons appear one at a time as the science ship
// works through them, so the bar filling up IS the progress readout for a deep survey.
//
// An active icon is framed in a square of that index's own brightest colour, so which overlays are up
// is readable from the same glance that reads the map. Colour rather than a checkbox because the frame
// then also says WHICH of the overlapping washes belongs to which button.
//
// ---- WHY THE STATE IS PER WORLD AND NOT PER WINDOW ---------------------------------------------
//
// Because a moon pane and its host planet are two maps of two different worlds open at the same time,
// and "show me the minerals" is a question about a world rather than about a window. Keyed on the body
// means switching to a moon and back does not lose what was up, and the moon's own bar starts from its
// own state rather than inheriting the planet's.
// ============================================================================================

/// Which index overlays are up, per world.
public static class IndexToggles
{
    static readonly Dictionary<CelestialBody, HashSet<SurfaceIndexKind>> on =
        new Dictionary<CelestialBody, HashSet<SurfaceIndexKind>>();

    /// Fired whenever anything is toggled, so every open map can redraw without polling.
    public static System.Action OnChanged;

    public static bool IsOn(CelestialBody b, SurfaceIndexKind k)
        => b != null && on.TryGetValue(b, out var set) && set.Contains(k);

    public static void Toggle(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null || k == SurfaceIndexKind.None) return;
        if (!on.TryGetValue(b, out var set)) on[b] = set = new HashSet<SurfaceIndexKind>();
        if (!set.Remove(k)) set.Add(k);
        OnChanged?.Invoke();
    }

    public static void Set(CelestialBody b, SurfaceIndexKind k, bool state)
    {
        if (b == null || k == SurfaceIndexKind.None) return;
        if (!on.TryGetValue(b, out var set)) on[b] = set = new HashSet<SurfaceIndexKind>();
        bool changed = state ? set.Add(k) : set.Remove(k);
        if (changed) OnChanged?.Invoke();
    }

    /// Every index currently up on this world that is still legitimately available. Filtered on the
    /// way out rather than pruned on the way in, because availability CHANGES — an index becomes
    /// available part-way through a deep survey, and a world can be regenerated under a set that was
    /// switched on for the world it used to be.
    public static void Active(CelestialBody b, List<SurfaceIndexKind> into)
    {
        into.Clear();
        if (b == null || !on.TryGetValue(b, out var set)) return;
        // Canonical order, not hash order, so the compositing is deterministic: two overlapping washes
        // must not swap which one is on top between frames.
        foreach (var k in SurfaceIndex.All)
            if (set.Contains(k) && Available(b, k)) into.Add(k);
    }

    /// Should this world offer this index at all?
    ///
    /// Two gates, and they answer different questions. `Present` is about the WORLD — a dry rock has no
    /// hydrology and never will. The reveal is about the SURVEY — the ground is there, nobody has read
    /// it yet. Both have to pass, and the second is what makes the bar fill in as a science ship works.
    public static bool Available(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null || k == SurfaceIndexKind.None) return false;
        if (!SurfaceIndex.Present(b, k)) return false;
        return Survey.RevealOf(b, k).started;
    }

    /// Forget everything. Called when a galaxy is replaced — the keys are bodies about to stop
    /// existing, and this dictionary would otherwise hold them alive.
    public static void ResetAll()
    {
        on.Clear();
        OnChanged?.Invoke();
    }
}

/// A row of index toggle buttons pinned to the top right of one map.
///
/// Rebuilt rather than updated when the AVAILABLE set changes, and merely re-tinted when only the
/// active set does — a rebuild is cheap at six buttons and the alternative is a diffing pass nobody
/// will maintain, but re-tinting every frame is cheaper still and that is the common case.
public class IndexIconBar : MonoBehaviour
{
    CelestialBody body;
    RectTransform bar;
    readonly List<SurfaceIndexKind> built = new List<SurfaceIndexKind>();
    readonly List<Image> frames = new List<Image>();
    readonly List<Image> plates = new List<Image>();
    readonly List<SurfaceIndexKind> scratch = new List<SurfaceIndexKind>();

    const float IconPx = 26f;      // the art is 16x16; drawn larger so it is a comfortable click target
    const float GapPx = 4f;
    const float MarginPx = 6f;
    // THREE, not two. "Buttons should have a border outline when selected." There WAS one at 2px, and
    // at 26px across a two-pixel edge reads as an anti-aliasing artefact rather than as a selection.
    const float FramePx = 3f;      // the active square's thickness

    /// The dark plate behind an icon, inactive then ACTIVE.
    ///
    /// "The background fill is fine but lower the opacity a LOT because the icon can barely be seen
    /// when the button is toggled on." Both plates were 0.82, and on top of the frame and the wash the
    /// lit button was the one you could read LEAST — which is backwards. The inactive plate keeps most
    /// of its opacity because an unlit icon still has to be legible over terrain (that is what the
    /// plate is for); the active one drops to a quarter, because the frame is already saying it is on.
    static readonly Color PlateIdle = new Color(0.04f, 0.06f, 0.09f, 0.78f);
    static readonly Color PlateOn   = new Color(0.04f, 0.06f, 0.09f, 0.22f);

    /// Attach a bar to a map frame. `parent` should be the frame the map is clipped to rather than the
    /// map image itself — the bar is furniture pinned to the window, and a bar that panned and zoomed
    /// away with the terrain would be a bar you have to go and find.
    public static IndexIconBar Attach(RectTransform parent, CelestialBody b)
    {
        var go = UIFactory.NewUI(parent, "IndexBar");
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-MarginPx, -MarginPx);
        rt.sizeDelta = new Vector2(IconPx, 10f);

        var bar = go.AddComponent<IndexIconBar>();
        bar.bar = rt;
        bar.body = b;
        bar.Rebuild();
        return bar;
    }

    public void SetBody(CelestialBody b)
    {
        if (body == b) return;
        body = b;
        Rebuild();
    }

    void OnEnable() { IndexToggles.OnChanged += Retint; }
    void OnDisable() { IndexToggles.OnChanged -= Retint; }

    float nextCheck;

    void Update()
    {
        // Availability changes as a deep survey works through the indexes, and it changes on a clock
        // nobody raises an event for. Twice a second is far more often than an index can appear and far
        // less often than a frame.
        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + 0.5f;
            if (AvailabilityChanged()) Rebuild();
        }
        Retint();
    }

    bool AvailabilityChanged()
    {
        int n = 0;
        foreach (var k in SurfaceIndex.All)
        {
            if (!IndexToggles.Available(body, k)) continue;
            if (n >= built.Count || built[n] != k) return true;
            n++;
        }
        return n != built.Count;
    }

    void Rebuild()
    {
        for (int i = bar.childCount - 1; i >= 0; i--) Destroy(bar.GetChild(i).gameObject);
        built.Clear();
        frames.Clear();
        plates.Clear();

        if (body == null) { bar.gameObject.SetActive(false); return; }

        foreach (var k in SurfaceIndex.All)
            if (IndexToggles.Available(body, k)) built.Add(k);

        bar.gameObject.SetActive(built.Count > 0);
        if (built.Count == 0) return;

        // A COLUMN, not a row. "The index toggle buttons should stack vertically, not horizontally."
        // Six icons across the top of the map is a strip of furniture over the ground you are reading;
        // down the right edge they are out of the way of everything except the map's own margin.
        float tall = built.Count * IconPx + (built.Count - 1) * GapPx;
        bar.sizeDelta = new Vector2(IconPx, tall);

        for (int i = 0; i < built.Count; i++)
        {
            var kind = built[i];

            var cell = UIFactory.NewUI(bar, $"Index_{kind}").GetComponent<RectTransform>();
            cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 1f);
            cell.pivot = new Vector2(0.5f, 1f);
            cell.sizeDelta = new Vector2(IconPx, IconPx);
            cell.anchoredPosition = new Vector2(0f, -i * (IconPx + GapPx));

            // A dark plate behind the art. The icons are 16x16 pixel art with transparent margins and
            // they sit over a planet's terrain — without something to sit ON, a brown mineral icon over
            // brown desert is invisible, which is the one thing a button must never be.
            var plate = cell.gameObject.AddComponent<Image>();
            plate.color = PlateIdle;
            plates.Add(plate);

            // The active frame, drawn as four edges rather than a sprite so its thickness stays constant
            // and it never depends on an asset that might not import.
            var frame = MakeFrame(cell);
            frames.Add(frame);

            var iconGO = UIFactory.NewUI(cell, "Icon");
            var icon = iconGO.AddComponent<RawImage>();
            icon.raycastTarget = false;
            icon.texture = IconFor(kind);
            // Point-filtered: the art is 16x16 and drawn at 26, and bilinear on pixel art at a
            // non-integer scale is exactly the mush the artist was avoiding by working at 16x16.
            if (icon.texture != null) icon.texture.filterMode = FilterMode.Point;
            var irt = icon.rectTransform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(3, 3); irt.offsetMax = new Vector2(-3, -3);
            // A missing texture would otherwise draw a white square over the icon's own plate; the
            // index's colour at least says which button it is.
            if (icon.texture == null) icon.color = SurfaceIndex.Outline(kind);

            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            var captured = kind;
            btn.onClick.AddListener(() => IndexToggles.Toggle(body, captured));

            UIFactory.Tooltip(cell.gameObject, TipFor(kind));
        }

        Retint();
    }

    Image MakeFrame(RectTransform cell)
    {
        // The parent of the four edges. Its own Image is what carries the colour; the edges reference
        // it, so re-tinting is one assignment rather than four.
        var holder = UIFactory.NewUI(cell, "Frame").GetComponent<RectTransform>();
        holder.anchorMin = Vector2.zero; holder.anchorMax = Vector2.one;
        holder.offsetMin = Vector2.zero; holder.offsetMax = Vector2.zero;
        var tint = holder.gameObject.AddComponent<Image>();
        tint.color = new Color(0, 0, 0, 0);      // the holder itself never draws
        tint.raycastTarget = false;

        Edge(holder, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -FramePx), Vector2.zero);       // top
        Edge(holder, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, FramePx));        // bottom
        Edge(holder, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(FramePx, 0));        // left
        Edge(holder, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-FramePx, 0), Vector2.zero);       // right
        return tint;
    }

    static void Edge(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var e = UIFactory.NewUI(parent, "Edge").GetComponent<RectTransform>();
        e.anchorMin = aMin; e.anchorMax = aMax;
        e.offsetMin = oMin; e.offsetMax = oMax;
        var img = e.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        e.gameObject.AddComponent<FrameEdgeTint>();
    }

    void Retint()
    {
        if (body == null) return;
        for (int i = 0; i < built.Count && i < frames.Count; i++)
        {
            bool active = IndexToggles.IsOn(body, built[i]);
            // The index's own BRIGHTEST colour, which is the top band's outline — so the square round
            // the button is literally the colour of the strongest ground that button will show.
            frames[i].color = active ? SurfaceIndex.Outline(built[i], 1f) : new Color(0, 0, 0, 0);
            // ...and the plate gets out of the icon's way once the frame is carrying the state.
            if (i < plates.Count && plates[i] != null) plates[i].color = active ? PlateOn : PlateIdle;
        }
    }

    static Texture2D IconFor(SurfaceIndexKind k)
        => Resources.Load<Texture2D>($"SpaceAssets/IndexIcons/Index_{k}");

    string TipFor(SurfaceIndexKind k)
    {
        string state = IndexToggles.IsOn(body, k) ? "Showing" : "Hidden";
        var r = Survey.RevealOf(body, k);
        string prog = r.complete ? "fully surveyed"
                    : $"survey in progress — pass {r.pass + 1} of {Survey.Bands}";
        return $"<b>{SurfaceIndex.Name(k)}</b> — {state}\n{SurfaceIndex.Describe(k)}\n" +
               $"<color=#8FA3B5>{prog}</color>\n\nClick to toggle. Several can be up at once.";
    }
}

/// Copies its parent frame's colour onto itself.
///
/// The four edges of an active square have to change colour together, and the alternative to this is
/// the bar keeping a list of four Images per button and setting all of them — twenty-four assignments
/// a frame instead of six, and a list that has to stay in step with the hierarchy.
public class FrameEdgeTint : MonoBehaviour
{
    Image self, parent;

    void Awake()
    {
        self = GetComponent<Image>();
        parent = transform.parent != null ? transform.parent.GetComponent<Image>() : null;
    }

    void LateUpdate()
    {
        if (self == null || parent == null) return;
        if (self.color != parent.color) self.color = parent.color;
    }
}
