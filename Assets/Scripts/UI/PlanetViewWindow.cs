using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// ============================================================================================
// PLANET VIEW — the surface map you actually develop a world on.
//
// Three tabs over one shared grid:
//
//   INFO    — what this world is: name, type, size, climate, weather.
//   BUILD   — pick a structure, then place it. The selected building follows the cursor as a GHOST:
//             snapped to the mouse while it's over open UI, snapped to the GRID once it's over the
//             map. Right-click rotates it at any point, before or after it snaps. Left-click commits.
//             Footprints are tetromino-like, so packing a dense city is a real puzzle.
//   SURVEY  — the index overlays. Each paints the grid with its own colour ramp so you can see, at a
//             glance, where a mine or a geothermal plant or a farm actually wants to go.
//
// The grid is drawn as ONE point-filtered texture per layer (terrain tint + overlay), not as hundreds
// of UI objects — a 40x20 world is 800 cells and building a GameObject per cell would be miserable.
// Structures and the ghost are a handful of quads on top of that.
// ============================================================================================
public class PlanetViewWindow : MonoBehaviour
{
    public static PlanetViewWindow Instance;

    // Exactly the four tabs Raptok asked for, plus the Dev-Mode-only Terrain sandbox (hidden in normal
    // play). Sites and the Power grid folded into Survey; the Infrastructure list folded into Build.
    public enum Tab { Overview, Build, Survey, Orbit, Terrain }

    // ============================================================================================
    // WHAT EACH TAB NEEDS, AND WHY
    //
    // This is the one place that answers "why can't I click that tab?", and it answers it in words. A
    // greyed tab that doesn't say what's missing is a dead end — the player can see the feature exists
    // and has no idea what to go and do.
    //
    // The milestones, in the order they actually happen:
    //   VISITED   — a ship has been here. You get the map at all.
    //   SURVEYED  — mapped from orbit. Terrain, ores, and the sites that are visible from up there.
    //   DEEP      — a research ship studied it on the ground. The Heat/Fertile/Wind indexes, and the
    //               anomalies you can only find by walking on them.
    //   CLAIMED   — the world is legally yours (Claim.cs).
    //   SETTLED   — people live here. Only now can you build.
    // ============================================================================================
    bool TabAvailable(Tab t, out string why)
    {
        why = null;
        if (body == null) { why = "no world"; return false; }

        // The sandbox terrain editor. The ONLY tab that Dev Mode gates rather than opens, so it's tested
        // before the blanket Dev Mode pass below — otherwise Dev Mode would unlock it and then this
        // would never be reached to hide it in normal play.
        if (t == Tab.Terrain)
        {
            if (!GameMode.DevMode) { why = "Dev Mode only"; return false; }
            return true;
        }

        if (GameMode.DevMode) return true;

        switch (t)
        {
            case Tab.Overview:
                return true;                       // always: name, type, orbit and host star are free

            case Tab.Orbit:
                return true;                       // always: what's in orbit is visible from up here, and
                                                   // the shipyard section explains itself when there's none

            case Tab.Survey:
                // Always open: it carries Climate and Terraform (known from orbit — name, type, orbit and
                // host star are free on any world), plus the folded Sites and Power sections. The index
                // overlays, ore list and power section inside gate themselves on survey/ownership state,
                // so an unsurveyed world lands here on a readable Climate/Terraform page, not a locked tab.
                return true;

            case Tab.Build:
                if (!body.Surveyed) { why = "survey this world first"; return false; }
                if (body.owner != FactionManager.Player) { why = "claim this world first"; return false; }
                if (!body.settled)
                {
                    why = body.habitability >= Colony.FoundThreshold
                        ? "settle this world — nobody lives here to build anything"
                        : $"terraform to {Colony.FoundThreshold:F0}% (now {body.habitability:F0}%), then settle it";
                    return false;
                }
                return true;
        }
        return true;
    }

    GameObject root;
    TMP_Text titleText;
    RectTransform tabStrip, sidePanel, gridHolder;
    RawImage mapImage, overlayImage;
    RectTransform mapRT, pieceLayer, ghostLayer;

    // ---- Horizontal wrap ----------------------------------------------------------------------
    //
    // A planet map is a CYLINDER: its left and right edges are the same meridian, and the terrain is
    // generated to join there (PlanetTerrainGenerator.WrapU). So scrolling east past the right edge should
    // arrive back at the left, endlessly, rather than stopping at a wall.
    //
    // Making the pan wrap is the easy half. The hard half is that at the seam you must see BOTH edges at
    // once, which means the map has to be drawn more than once. Rather than duplicate the whole map node,
    // each mirror carries only what is actually visible — terrain, overlay, structures — and is parented
    // INSIDE mapRT, stretched to it and offset by exactly one map width. That way every mirror inherits
    // the zoom and pan for free: there is one source of truth for where the map is, and the copies are
    // rigidly attached to it rather than being kept in sync by hand.
    //
    // Two mirrors, not one. Which side the gap opens on depends on which way you scrolled, and a single
    // mirror would have to be moved across at the moment the gap flips — one more thing to get wrong at
    // exactly the moment the player is looking at it.
    class WrapMirror
    {
        public RectTransform root;
        public RawImage terrain;
        public RawImage overlay;
        public RectTransform pieces;

        /// The power grid, mirrored ABOVE the pieces exactly as it sits on the real map. Its own field
        /// rather than a second use of `overlay`, because the two layers straddle the pieces: the ground
        /// index below, the grid above. One image cannot be on both sides of them.
        public RawImage power;
    }

    readonly List<WrapMirror> mirrors = new List<WrapMirror>();

    // How far one arrow press scrolls, as a fraction of the viewport width.
    const float ScrollStepFrac = 0.22f;
    // Degrees... rather, pixels per second while an arrow is held down.
    const float ScrollHoldSpeed = 900f;
    float scrollHoldDir;
    TMP_Text statusText;

    // Host map + moon maps. The host map lives in hostViewport (which shrinks to the bottom when any moon
    // is open); moon maps are drawn in moonLayer's top band; moonTabStrip is the row of moon tabs under
    // the map. See the MOON MAPS section near the bottom of the file.
    RectTransform hostViewport, moonLayer, moonTabStrip;
    TMP_Text emptyHint;              // shown in the map area when no map tab is open

    CelestialBody body;
    Tab tab = Tab.Overview;

    // ---- Map panes (the planet AND its moons) ----
    // Every map is a toggleable pane now: the planet has its own (bigger) tab alongside the moon tabs, and
    // any mix of up to five can be open at once. The open panes TILE the whole map area with no gaps, sized
    // purely by how many are open, and each pane zooms its CONTENTS inside a fixed frame — cover-fit at the
    // fullest-out end, so a map always fills its frame instead of floating in letterbox. The host planet
    // keeps its own tilePx / mapPan / mapRT and all its placement machinery; each open moon carries its own
    // frame, content image, texture, zoom and pan in the dictionaries below (keyed by the moon, so opening
    // or closing one never disturbs another's view).
    readonly List<CelestialBody> openMaps = new List<CelestialBody>();   // in open order; host is `body`
    const int MaxOpenMaps = 5;

    readonly Dictionary<CelestialBody, RectTransform> moonFrame = new Dictionary<CelestialBody, RectTransform>();
    readonly Dictionary<CelestialBody, RawImage> moonImg = new Dictionary<CelestialBody, RawImage>();
    readonly Dictionary<CelestialBody, Texture2D> moonTex = new Dictionary<CelestialBody, Texture2D>();
    readonly Dictionary<CelestialBody, float> moonTilePx = new Dictionary<CelestialBody, float>();   // px/cell, like the host's tilePx
    readonly Dictionary<CelestialBody, Vector2> moonPan = new Dictionary<CelestialBody, Vector2>();

    // Downscaled planet/moon thumbnails on the tab strip itself, rebuilt whenever the strip is and freed
    // the same way.
    readonly List<Texture2D> moonTabThumbTextures = new List<Texture2D>();

    // The pane the wheel / zoom bar currently acts on — whichever open frame the cursor is over, latched so
    // the bar's own buttons (which the cursor sits on) keep acting on the last map hovered.
    CelestialBody activePane;
    CelestialBody moonPanDrag;       // the moon whose map is being dragged, if any
    Vector2 moonPanGrabScreen;
    Vector2 moonPanGrabOffset;

    // How the open panes are arranged in the map area, cycled by the "Change Map View" button. Four formats,
    // each working with the planet plus any number (0–4) of moons, in any open/closed combination:
    //   MoonsAbove — planet large on the bottom, moons in a row across the top
    //   MoonsBelow — planet large on the top, moons in a row across the bottom
    //   MoonsSplit — planet large in the middle, moons split into a row above AND a row below
    //   MoonsSide  — planet large on the left, moons stacked in a column down the right
    enum MapLayout { MoonsAbove, MoonsBelow, MoonsSplit, MoonsSide }
    MapLayout mapLayout = MapLayout.MoonsAbove;
    RectTransform viewFormatBtn;
    TMP_Text viewFormatLabel;

    // Build-mode state.
    SurfaceBuildingType? selected;      // null = nothing picked up

    /// True while the player is carrying an Electrical Engineering piece and has not placed it yet.
    ///
    /// Siting a plant, node or capacitor is entirely a question of what the existing grid already reaches
    /// — an unpowered mine two tiles out of range looks identical to a powered one on the plain map. So
    /// the power overlay comes up on its own for as long as the piece is in hand. The player never has to
    /// know the overlay exists, which is the point: the information appears when it is what you need.
    ///
    /// "In hand" outlasts a single placement, deliberately. DoPlace does not clear `selected` — you keep
    /// the piece so you can lay a run of nodes without re-picking it each time — so the overlay stays up
    /// across the whole run and clears when you actually put the piece down (Esc, or leaving the Build
    /// tab). Clearing it on the first placement would flash the overlay off and on for every node in a
    /// chain, which is the case where it is most wanted.
    /// Carrying something the GRID is relevant to — which is not only the things that make power.
    ///
    /// Anything that DRAWS power needs to know where the grid reaches, or it will be sited somewhere
    /// pretty and brown out (PowerGrid.UnpoweredFactor). A farm, a mine, a factory, a lab: all of them
    /// want to see the grid while they are being placed, alongside whatever index decides how well they
    /// will actually perform there.
    bool CarryingPowerPiece
    {
        get
        {
            if (!selected.HasValue) return false;
            var info = SurfaceBuildingDatabase.Get(selected.Value);
            return info != null
                && (info.category == SurfaceBuildingCategory.Electrical
                    || info.powerDraw > 0f
                    || info.powerRange > 0f);
        }
    }

    /// The power overlay is up — either switched on from the Survey tab, or automatically because a
    /// power piece is in hand on the Build tab.
    bool PowerOverlayActive =>
        (tab == Tab.Survey && showPowerOverlay) || (tab == Tab.Build && CarryingPowerPiece);

    /// True while the player is carrying a piece whose siting is a question of ORE — a mine, a refinery,
    /// a combustion plant. Exactly the same bargain the power overlay makes: the one map that answers
    /// "where does this go?" comes up on its own while the piece is in hand.
    ///
    /// Tested by index rather than by category, because the buildings that care about ore are spread
    /// across three categories (Harvesting, Industry, Electrical) and the index is the actual statement
    /// of what a building is sited against.
    bool CarryingMiningPiece =>
        selected.HasValue &&
        SurfaceBuildingDatabase.Get(selected.Value).index == SurfaceIndexKind.Mineral;

    /// The Mineral Index is up — chosen in the Survey tab, or automatically with a mining piece in hand.
    ///
    /// This is now the ONLY circumstance under which named ore deposits are drawn anywhere. They used to
    /// be baked into the terrain texture and so were visible in every view, at every zoom, forever; the
    /// deposits still generate exactly as they did, but reading them is now something you do on purpose.
    bool MineralOverlayActive =>
        body != null && body.surface != null && SurfaceIndex.Unlocked(body, SurfaceIndexKind.Mineral) &&
        // No `!showPowerOverlay` here any more. Leaving it in meant switching the grid on quietly
        // downgraded the Mineral view: it still looked mineral-coloured, but fell through to the plain
        // ramp and lost the NAMED ORE DEPOSITS, which this is the only place that draws. A map that
        // looks the same and silently stops answering the question is the worst kind of regression.
        ((tab == Tab.Survey && !showTectonicsOverlay && activeIndex == SurfaceIndexKind.Mineral) ||
         (tab == Tab.Build && CarryingMiningPiece));
    int rotation;
    Vector2Int hoverCell = new Vector2Int(-1, -1);
    bool hoverValid;

    // Survey-mode state.
    SurfaceIndexKind activeIndex = SurfaceIndexKind.None;
    // The Power grid is now a Survey overlay rather than its own tab: this flag is the "showing the power
    // grid" option. NOT exclusive with the index ramps any more — the grid has its own layer above the
    // buildings while an index ramp sits below them, so both can be read at once.
    bool showPowerOverlay;
    // The plate-tectonics overlay — another bespoke Survey overlay (not an index ramp): it washes the map
    // white, paints the fault lines red, and draws a push-direction arrow per plate. Mutually exclusive
    // with the index ramps AND the power overlay, and only offered on a world that has active tectonics.
    bool showTectonicsOverlay;

    readonly LiveSet live = new LiveSet();
    string lastSig = null;
    Texture2D overlayTex;
    // The power grid's own layer and texture — see the note where it is built. Separate from
    // overlayTex because the two are drawn at once now, at different depths.
    RawImage powerOverlayImage;
    Texture2D powerTex;
    float powerRepaintIn;   // see Update: the power overlay repaints on a timer, not every frame
    Color[] powerPx;        // reused scratch for that repaint — see RefreshPowerOverlay

    // Selection marker (see DrawSelectionMarker / AnimateMarker).
    RectTransform markerLayer;
    Image markerRing, markerArrow;

    // Per-plate push arrows for the tectonics overlay (see DrawPlateArrows). Its own layer so they clear
    // and redraw independently of the selection marker.
    RectTransform plateArrowLayer;
    float markerRingBase, markerArrowBaseY;
    PlacedBuilding lastMarkedSelection;

    // A FULL-SCREEN window (Raptok's request: selecting a planet fills the screen with the Planet
    // View). Measured from the live canvas rather than the 1920x1080 reference so it fills the ACTUAL
    // screen, with a small margin so the frame isn't flush to the edge. Re-measured on every open
    // (ShowFor) since the canvas rect isn't known at bootstrap. The map zooms inside its viewport; the
    // resize grip and draggable title bar still work, so it can be shrunk by hand afterwards.
    // The SAME margin WindowFit clamps to. It used to be its own 8, while WindowFit enforced 14 — so this
    // sized the window 12px wider and taller than the clamp would allow, and every open began with the
    // window over the edge until something re-fitted it. Two numbers describing one relationship is how
    // that happens; there is now one, and it lives with the code that enforces it.
    static Vector2 WindowSize(Transform parent)
    {
        var canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
        var crt = canvas != null ? canvas.GetComponent<RectTransform>() : null;
        bool measured = crt != null && crt.rect.width > 1f && crt.rect.height > 1f;
        Vector2 screen = measured ? crt.rect.size : new Vector2(1920f, 1080f);   // fallback: reference res

        // Fill the canvas, less the margin, and nothing else.
        //
        // There were 640x400 minimums here. They are deliberately gone rather than reinstated: a minimum
        // can only ever be honoured by exceeding the canvas, which is precisely the off-canvas state this
        // is meant to prevent — and WindowFit would immediately shrink it back anyway, so the floor was
        // never real. (An earlier attempt wrote it as Min(Max(640, X), X), which is algebraically just X:
        // a floor that reads as a guarantee and provides none. Better to not claim it.)
        float m = WindowFit.Margin * 2f;
        return new Vector2(screen.x - m, screen.y - m);
    }

    // Raptok's layout: the surface map anchors to the LEFT and never takes more than 3/4 of the window
    // width; the far-right 1/4 is the selected tab's panel. Expressed as an anchor fraction rather than a
    // pixel width so it scales with the full-screen window and can never creep past three-quarters.
    const float MapFraction = 0.75f;

    // The status line's height. FIXED, and the map's bottom edge is a matching fixed literal.
    //
    // It was 30px, which fitted the one or two lines it carried at the time. Then the Survey readout
    // learned to describe an index AND the power grid AND the world-wide weather or solar ceiling at
    // once, three lines need ~41px, and UISanityGuard caught the bottom line being clipped.
    //
    // THE OBVIOUS FIX — grow the box and push the map's bottom edge up with it — IS A TRAP, and it was
    // tried first. `gridHolder` is the map viewport, and every one of the map's own bounds is derived
    // from its rect: `FitTilePx`, `CoverTilePx`, `MaxTilePx`, and `ClampPan`'s slack. `tilePx` and
    // `mapPan` are persistent fields that those bounds CLAMP but never restore, so each resize was a
    // one-way ratchet — hovering on and off the map stripped about 1% of zoom every time, permanently.
    // Worse, at contain zoom the map's bottom edge coincides with the viewport's, so a cursor in the
    // last few pixels flipped hover-valid → 3-line status → viewport shrinks → hover invalid → 1-line
    // status → viewport grows, a two-frame flicker that rebuilt the moon panes every frame it ran.
    //
    // So the geometry stays still and the TEXT adapts instead: three lines are reserved, and TMP's auto
    // sizing shrinks the font a couple of points on the rare occasion the content needs a fourth.
    // Nothing downstream of the layout can tell the difference, which is the entire point.
    const float StatusHeight = 44f;         // three lines at UITheme.SmallSize
    const float StatusMapGap = 4f;          // breathing room between the status line and the map above it
    const float StatusMapBottom = StatusHeight + StatusMapGap;
    const float PanelGap = 8f;      // gap between the map's right edge and the panel

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("PlanetViewWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<PlanetViewWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Planet View", WindowSize(parent), out root, out titleText);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        // Tabs — sit ABOVE THE MAP (the left 3/4), not the whole window, per Raptok's layout.
        tabStrip = UIFactory.NewUI(content, "Tabs").GetComponent<RectTransform>();
        tabStrip.anchorMin = new Vector2(0, 1); tabStrip.anchorMax = new Vector2(MapFraction, 1);
        tabStrip.pivot = new Vector2(0.5f, 1); tabStrip.sizeDelta = new Vector2(0, 26);
        var th = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 4; th.childControlWidth = true; th.childControlHeight = true; th.childForceExpandWidth = false;

        // The VIEWPORT: a fixed window onto the surface, anchored to the LEFT and capped at 3/4 of the
        // window width (MapFraction). It never changes size — zooming scales the map INSIDE it, which is
        // what a map window should do. It used to resize the window itself, so zooming in on a big world
        // grew the panel off the edge of the screen.
        gridHolder = UIFactory.NewUI(content, "Viewport").GetComponent<RectTransform>();
        gridHolder.anchorMin = new Vector2(0, 0); gridHolder.anchorMax = new Vector2(MapFraction, 1);
        gridHolder.offsetMin = new Vector2(0, StatusMapBottom);     // clear the status line below
        gridHolder.offsetMax = new Vector2(-PanelGap, -32);        // clear the tabs; gap before the panel
        var vpImg = gridHolder.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0.06f, 0.08f, 0.11f, 1f);          // themed grout that shows between tiled panes
        gridHolder.gameObject.AddComponent<RectMask2D>();          // panes are clipped to the map area

        // Shown when no map tab is open — the map area is otherwise blank, so it says what to do.
        emptyHint = UIFactory.Text(gridHolder, "Click a planet or moon tab to view its map.",
                                   UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.Center);
        UIFactory.Stretch(emptyHint.rectTransform);
        emptyHint.gameObject.SetActive(false);

        // The HOST planet map lives in its own sub-viewport — one pane among the tiled panes. LayoutPanes
        // positions it into its grid cell (the whole area when it's the only one open) and hides it when the
        // planet tab is closed. Every fit/pan/zoom/confirm calculation measures THIS rect, so the host map
        // re-fits into whatever cell it's given and its clicks keep mapping correctly.
        hostViewport = UIFactory.NewUI(gridHolder, "HostViewport").GetComponent<RectTransform>();
        UIFactory.Stretch(hostViewport);
        hostViewport.gameObject.AddComponent<RectMask2D>();

        var mapGO = UIFactory.NewUI(hostViewport, "Map");
        mapImage = mapGO.AddComponent<RawImage>();
        mapRT = mapImage.rectTransform;
        // Centre-anchored and free-floating inside the viewport: its size is the zoom, its position is
        // the pan. Everything on the map (pieces, ghost, markers) is anchored in ITS normalised space,
        // so they all follow for free.
        mapRT.anchorMin = mapRT.anchorMax = new Vector2(0.5f, 0.5f);
        mapRT.pivot = new Vector2(0.5f, 0.5f);
        mapRT.anchoredPosition = Vector2.zero;

        var ovGO = UIFactory.NewUI(mapRT, "Overlay");
        overlayImage = ovGO.AddComponent<RawImage>();
        UIFactory.Stretch(overlayImage.rectTransform);
        overlayImage.raycastTarget = false;
        ovGO.SetActive(false);

        // Points of interest sit ABOVE the terrain but BELOW the pieces: a site is GROUND, so a building
        // put on top of it should cover it. Built before the piece layer so hierarchy order says so.
        siteLayer = UIFactory.NewUI(mapRT, "Sites").GetComponent<RectTransform>();
        UIFactory.Stretch(siteLayer);

        pieceLayer = UIFactory.NewUI(mapRT, "Pieces").GetComponent<RectTransform>();
        UIFactory.Stretch(pieceLayer);
        var plImg = pieceLayer.gameObject.AddComponent<Image>();
        plImg.color = new Color(0, 0, 0, 0); plImg.raycastTarget = false;

        // The wrap mirrors. Built here, immediately after the things they mirror, so the two cannot
        // drift apart structurally. They sit BELOW the markers/ghost in sibling order because those are
        // deliberately not mirrored — a selection ring or the piece riding the cursor belongs to where
        // you are actually pointing, not to a copy of it one world away.
        BuildWrapMirror(-1);
        BuildWrapMirror(+1);

        // ============================================================================================
        // THE POWER GRID GETS ITS OWN LAYER, ABOVE THE PIECES
        //
        // Overlays used to share one image, which forced them to be mutually exclusive — and the loser
        // was always the one the player most needed. A Combustion Plant is Electrical (so it wants the
        // power map) but is sited on ORE (so it wants the Mineral map), and one of those had to be
        // thrown away. Same for a Farm, which needs fertile ground AND a grid connection, and for wind
        // and solar arrays, which need their own index AND somewhere to plug in.
        //
        // They cannot simply be composited into one texture either, because they belong at DIFFERENT
        // DEPTHS. A ground index describes the ground, so a building standing on it should cover it. The
        // power overlay describes which structures the grid reaches, so hiding it behind those same
        // structures answers the question by obscuring it — a mine in a dead zone looked identical to a
        // powered one.
        //
        // So: two layers. The ground index stays below the pieces; the power grid sits above them, right
        // under the markers. Both can be on at once, each at the depth that makes it readable.
        // ============================================================================================
        var pwGO = UIFactory.NewUI(mapRT, "PowerOverlay");
        powerOverlayImage = pwGO.AddComponent<RawImage>();
        UIFactory.Stretch(powerOverlayImage.rectTransform);
        powerOverlayImage.raycastTarget = false;
        pwGO.SetActive(false);

        // Above the pieces so the ring/arrow are never hidden behind a structure's own tiles.
        markerLayer = UIFactory.NewUI(mapRT, "Markers").GetComponent<RectTransform>();
        UIFactory.Stretch(markerLayer);
        var mlImg = markerLayer.gameObject.AddComponent<Image>();
        mlImg.color = new Color(0, 0, 0, 0); mlImg.raycastTarget = false;

        ghostLayer = UIFactory.NewUI(mapRT, "Ghost").GetComponent<RectTransform>();
        UIFactory.Stretch(ghostLayer);
        var glImg = ghostLayer.gameObject.AddComponent<Image>();
        glImg.color = new Color(0, 0, 0, 0); glImg.raycastTarget = false;

        // Topmost overlay layer: the tectonics push arrows, so they sit above the fault-line wash.
        plateArrowLayer = UIFactory.NewUI(mapRT, "PlateArrows").GetComponent<RectTransform>();
        UIFactory.Stretch(plateArrowLayer);
        var palImg = plateArrowLayer.gameObject.AddComponent<Image>();
        palImg.color = new Color(0, 0, 0, 0); palImg.raycastTarget = false;

        // The map itself is the click/hover target for placement.
        var probe = mapGO.AddComponent<SurfaceGridProbe>();
        probe.Init(this, mapRT);

        // The moon panes live in this full-area container, a sibling of the host viewport that stretches the
        // whole map area. It carries no mask of its own — each moon FRAME has its own RectMask2D — and each
        // frame is positioned into its own grid cell (disjoint from the host's), so the panes never overlap.
        moonLayer = UIFactory.NewUI(gridHolder, "MoonLayer").GetComponent<RectTransform>();
        UIFactory.Stretch(moonLayer);

        BuildZoomBar();
        BuildViewFormatButton();

        // The map tab strip is anchored inside the area's TOP-LEFT corner and stacks vertically: the planet
        // tab (bigger) first, then a tab per moon closest-first. Rebuilt per world in SetupMapTabs.
        moonTabStrip = UIFactory.NewUI(gridHolder, "MoonTabs").GetComponent<RectTransform>();
        moonTabStrip.anchorMin = new Vector2(0, 1); moonTabStrip.anchorMax = new Vector2(0, 1);
        moonTabStrip.pivot = new Vector2(0, 1);
        moonTabStrip.anchoredPosition = new Vector2(6f, -6f);
        moonTabStrip.sizeDelta = new Vector2(PlanetTabSize, 0);   // wide enough for the bigger planet tab
        var mth = moonTabStrip.gameObject.AddComponent<VerticalLayoutGroup>();
        mth.spacing = 7; mth.childControlWidth = true; mth.childControlHeight = true;
        mth.childForceExpandWidth = false; mth.childForceExpandHeight = false; mth.childAlignment = TextAnchor.UpperLeft;
        var mtf = moonTabStrip.gameObject.AddComponent<ContentSizeFitter>();
        mtf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Side panel: the selected tab's controls — the far-right 1/4 of the window (Raptok's layout).
        // Anchored across [MapFraction .. 1] so it's exactly the quarter the map doesn't use and scales
        // with the window, rather than a fixed pixel width.
        var sideHolder = UIFactory.NewUI(content, "SideHolder").GetComponent<RectTransform>();
        sideHolder.anchorMin = new Vector2(MapFraction, 0); sideHolder.anchorMax = new Vector2(1, 1);
        sideHolder.pivot = new Vector2(0.5f, 0.5f);
        sideHolder.offsetMin = new Vector2(0f, 8f);     // full-height control column (the docked hover panel sits under the MAP — a separate column)
        sideHolder.offsetMax = new Vector2(0f, -32f);   // clear the tab strip / title chrome
        UIFactory.ScrollView(sideHolder, out sidePanel);

        // A thin status line at the very bottom of the map column — build hints, the power balance, the
        // survey readout. Tile hover info itself no longer lives here: it's a small floating tooltip that
        // follows the cursor over the map (see PollHover), so the map gets nearly all of this space back.
        statusText = UIFactory.Text(content, "", UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.TopLeft);
        var srt = statusText.rectTransform;
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(MapFraction, 0);
        srt.pivot = new Vector2(0.5f, 0); srt.sizeDelta = new Vector2(-PanelGap, StatusHeight);
        srt.anchoredPosition = new Vector2(-PanelGap * 0.5f, 2f);

        // SHRINK THE FONT RATHER THAN CLIP THE TEXT. The three reserved lines cover every ordinary
        // state; the Survey tab at its very fullest — a long index description, the grid legend, the
        // balance and a hovered tile — can still want a fourth, and on a window narrowed by its resize
        // grip, a fifth. Auto sizing gives those states a slightly smaller line instead of a missing
        // one. The floor is 9pt: below that it stops being readable, and a state that needs less than
        // 9pt is a state whose text should be shortened rather than squeezed.
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 9f;
        statusText.fontSizeMax = UITheme.SmallSize;

        PlanetUI.OnBodySelected += OnBodySelected;
        PlanetUI.OnClosed += HideOnDeselect;

        // The title-bar 'X' bakes in a bare root.SetActive(false). Now that this window IS the planet
        // selection, closing it should also clear the selection — otherwise the camera stays locked on a
        // world whose window is gone and the labels linger until the next empty-space click. Route the X
        // through CloseAll so it's symmetric with click-away; the factory's own hide still fires too,
        // which is harmless.
        var closeBtn = root.transform.Find("TitleBar")?.GetComponentInChildren<Button>();
        if (closeBtn != null)
        {
            closeBtn.onClick.AddListener(() => { if (PlanetUI.Selected != null) PlanetUI.Instance?.CloseAll(); });
            closeBtn.onClick.AddListener(() => MapHoverPanel.Instance.Hide());
        }

        root.SetActive(false);
    }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= OnBodySelected;
        PlanetUI.OnClosed -= HideOnDeselect;
        ClearMoonPanes();
        foreach (var tx in moonTabThumbTextures) if (tx != null) Destroy(tx);
        moonTabThumbTextures.Clear();
        // Both overlay textures are ours alone — nothing else references them, so nothing else frees them.
        if (overlayTex != null) Destroy(overlayTex);
        if (powerTex != null) Destroy(powerTex);
        if (MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();
    }

    // A single selection no longer throws the full-screen viewer open — that clutters the map. It just
    // remembers the world, and repaints only if the viewer already happens to be open. The viewer is now
    // opened deliberately: a double-click on the world (PlanetClick) or the compact panel's "Open
    // Planetary View" button.
    void OnBodySelected(CelestialBody b)
    {
        body = b;
        if (b != null && root != null && root.activeSelf) ShowFor(b);
    }

    // Clearing the selection (click-away, Esc-driven CloseAll) closes the window with it, so the
    // full-screen view doesn't stay up over a deselected world.
    void HideOnDeselect()
    {
        if (root != null) root.SetActive(false);
        MapHoverPanel.Instance.Hide();
    }

    public void ShowFor(CelestialBody b) => ShowFor(b, null);

    /// Open on a world, optionally landing on a specific tab. `openOn` is honoured only if that tab is
    /// actually available for this world — asking for Build on an unsettled rock lands on Info, which is
    /// the tab that can explain why.
    public void ShowFor(CelestialBody b, Tab? openOn)
    {
        body = b;
        selected = null; rotation = 0;
        showPowerOverlay = false;   // a fresh world opens on the plain map, not the last world's power view
        showTectonicsOverlay = false;   // ...nor the last world's tectonics view (also a survey-gated overlay)
        CancelPlace();          // a confirm from the last world means nothing on this one
        lastSig = null;

        // The tab you were on may not exist for THIS world — Build on your capital, then click a
        // barren rock, and Build has to give way to something readable rather than showing an empty
        // build list on a world nobody lives on.
        if (openOn.HasValue && TabAvailable(openOn.Value, out _)) tab = openOn.Value;
        else if (!TabAvailable(tab, out _)) tab = Tab.Overview;
        // Open showing the WHOLE world, centred — the zoom of the last planet you looked at means
        // nothing on this one.
        tilePx = 0f;            // ApplyMapSize resolves this to the fit-everything zoom
        mapPan = Vector2.zero;
        root.SetActive(true);

        // Always open centred AND re-sized to fill the current screen. The canvas rect isn't known at
        // bootstrap, so the size is re-measured here where it's real. If you dragged it into a corner
        // last time, that was for last time — a window that opens off where you left it is one you have
        // to go find.
        var rrt = root.GetComponent<RectTransform>();
        rrt.sizeDelta = WindowSize(root.transform);
        rrt.anchoredPosition = Vector2.zero;
        // Guarantee it actually fits the canvas. WindowSize can fall back to the 1920x1080 reference before
        // the canvas rect is measurable (or exceed a smaller canvas), and WindowFit only re-clamps on a
        // canvas SIZE CHANGE — which setting sizeDelta here isn't — so it would otherwise leave the window
        // hanging off the edge (the UISanity "off-canvas" warning). Fit() shrinks it to the canvas and
        // nudges it fully on-screen right now.
        root.GetComponent<WindowFit>()?.Fit();
        rrt.SetAsLastSibling();
        RefreshMapTexture();

        // Rebuild the tab strip for THIS world (planet + moons), close any panes left open from the last
        // one, and open the planet's own map by default.
        SetupMapTabs();
    }

    /// Is the full-screen viewer currently open? Used by the compact selection panel to get out of the
    /// way while the full view is up.
    public bool IsOpen => root != null && root.activeSelf;

    public void Toggle()
    {
        bool show = !root.activeSelf;
        if (show)
        {
            if (body == null) body = PlanetUI.Selected;
            if (body == null) return;
            ShowFor(body);
            return;
        }
        root.SetActive(false);
        MapHoverPanel.Instance.Hide();
    }

    /// Re-draw this window if it happens to be showing `b`, after something ELSE changed that world.
    ///
    /// The three callers all own their change and have already made it: terraforming and the terrain
    /// editor have regenerated the surface and dropped the derived caches themselves, and a finished
    /// research task has rewritten a site in place. So this only REPAINTS. It deliberately regenerates
    /// nothing — a window that re-derived a world every time someone asked it to redraw would be doing
    /// the caller's job with the caller's data, and would sometimes reach a different answer.
    ///
    /// The `body != b` test is what makes this safe to call from a tick: terraforming fires it every
    /// time a world gains 1.5 habitability, and the window is nearly always closed or looking at
    /// somewhere else, so the common case costs one reference compare.
    ///
    /// (This is the counterpart of DetailedSurfaceWindow.RefreshIfShowing, which these callers used to
    /// reach. When that window was retired into this one's Sites and Terrain tabs, the calls were
    /// repointed here but the method never came with them.)
    public void RefreshIfShowing(CelestialBody b)
    {
        if (root == null || !root.activeSelf) return;
        if (b == null || body != b) return;

        RefreshMapTexture();   // terraforming can remodel a world outright, not just retint it

        // Force the side panel, the structures and the overlay to re-read on the next Update. Going
        // through the signature rather than calling Rebuild() straight away keeps ONE rebuild path, so
        // this can't drift from the one everything else uses — and it collapses naturally if several
        // things ask on the same frame.
        lastSig = null;
    }

    void RefreshMapTexture()
    {
        if (body == null) return;

        // One texel per build cell, read straight off the grid — so a 1x1 structure covers exactly one
        // terrain pixel. The grid is now as fine as the detail render (see MapMetrics.Subdiv), so this
        // is the detailed map AND the build grid at once, rather than two maps six times apart.
        //
        // Rebuilt on every open rather than cached by body id, because terraforming can remodel a
        // world's terrain outright and a cache keyed only on identity would show the planet it used
        // to be.
        if (mapTex != null) Destroy(mapTex);
        mapTex = SurfaceTextureRenderer.BuildGrid(body);
        mapImage.texture = mapTex;
        titleText.text = $"Planet View — {body.name}";
        ApplyMapSize();
    }

    // The map texture this window owns. Terrain comes out of the renderer at full vibrance — no tone
    // pass, here or there.
    Texture2D mapTex;

    // ---- Zoom ----
    // Expressed as PIXELS PER TILE, because that's what both limits are naturally about.
    //
    //   Zoomed OUT  = the whole surface fits the viewport, whatever size the world is.
    //   Zoomed IN   = about MaxVisibleTiles cells fill it, so the closest view is the same "how much
    //                 can I see" on every world rather than depending on how big the planet happens
    //                 to be.
    //
    // The window never changes size. Only the map inside it does.
    const int MaxVisibleTiles = 200;

    float tilePx;                  // current zoom
    Vector2 mapPan;                // map offset within the viewport
    Vector2 lastViewportSize;      // re-fit when the window is laid out or resized

    // ---- Zoom bar ----
    // Floats over the map's bottom-left corner: minus, plus, Fit, and a live zoom readout.
    //
    // The scroll wheel is the fast way to do this and the buttons are the discoverable one. Both exist
    // because a wheel is invisible — nothing on screen says the map zooms — and because trackpads and
    // some mice make a precise notch genuinely hard.
    TMP_Text zoomLabel;
    RectTransform zoomBar;

    void BuildZoomBar()
    {
        var bar = zoomBar = UIFactory.NewUI(gridHolder, "ZoomBar").GetComponent<RectTransform>();
        bar.anchorMin = bar.anchorMax = new Vector2(0, 0);
        bar.pivot = new Vector2(0, 0);
        bar.anchoredPosition = new Vector2(6, 6);
        bar.sizeDelta = new Vector2(268, 26);

        var bg = bar.gameObject.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.07f, 0.11f, 0.85f);

        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4; h.padding = new RectOffset(4, 4, 3, 3);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;

        ZoomButton(bar, "–", () => ZoomBy(1f / ZoomStep));
        ZoomButton(bar, "+", () => ZoomBy(ZoomStep));

        var fit = UIFactory.Button(bar.transform, "Fit", FitActive, 20f);
        var fle = fit.gameObject.AddComponent<LayoutElement>();
        fle.preferredWidth = 40f; fle.flexibleWidth = 0f;

        zoomLabel = UIFactory.Text(bar, "100%", UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.Center);
        var zle = zoomLabel.gameObject.AddComponent<LayoutElement>();
        zle.flexibleWidth = 1f;

        // Scroll arrows, on the same strip as the zoom controls and acting on the same map — whichever
        // pane is active (see ActiveScrollTarget). They live here rather than floating over the map so
        // there is one place the map's controls are, instead of two.
        ScrollButton(bar, "<", -1f);
        ScrollButton(bar, ">", +1f);
    }

    /// A press-and-hold scroll arrow. Click nudges; holding scrolls continuously, which is what you want
    /// for crossing a whole world rather than clicking twenty times.
    void ScrollButton(RectTransform bar, string label, float dir)
    {
        var btn = UIFactory.Button(bar.transform, label, () => NudgeScroll(dir), 20f);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 30f; le.flexibleWidth = 0f;

        var hold = btn.gameObject.AddComponent<ViewHoldButton>();
        hold.onDown = () => scrollHoldDir = dir;
        hold.onUp = () => { if (Mathf.Approximately(scrollHoldDir, dir)) scrollHoldDir = 0f; };
    }

    void NudgeScroll(float dir)
    {
        var vp = ActiveScrollViewport();
        if (vp == null) return;
        ScrollActive(dir * vp.rect.width * ScrollStepFrac);
    }

    /// The viewport the scroll arrows act on.
    ///
    /// Whichever pane was last clicked — host or moon. `activePane` is already this window's notion of
    /// "the map you are working in" (the tab strip, the zoom buttons and Fit all follow it), so the
    /// arrows follow it too rather than inventing a second idea of which map has focus.
    RectTransform ActiveScrollViewport()
    {
        var m = activePane;
        if (m != null && m != body && moonFrame.TryGetValue(m, out var fr) && fr != null) return fr;
        return hostViewport;
    }

    /// Note the NEGATION. `dx` is which way the player asked the VIEW to travel; the pan moves the MAP,
    /// and those are opposites — pressing ">" to look further east has to slide the map west.
    void ScrollActive(float dx)
    {
        var m = activePane;
        if (m != null && m != body && moonFrame.TryGetValue(m, out var fr) && fr != null
            && moonImg.TryGetValue(m, out var img) && img != null)
        {
            Vector2 pan = moonPan.TryGetValue(m, out Vector2 pv) ? pv : Vector2.zero;
            pan.x -= dx;
            ClampPanePan(fr.rect, img.rectTransform, ref pan);
            moonPan[m] = pan;
            SyncMoonMirrors(img, fr.rect);
            return;
        }
        ScrollMap(-dx);
    }

    /// Continuous scroll while an arrow is held. Frame-rate independent, and unscaled so it works while
    /// the game is paused — the Planet View is a place you use while paused.
    void TickScrollHold()
    {
        if (Mathf.Approximately(scrollHoldDir, 0f)) return;
        ScrollActive(scrollHoldDir * ScrollHoldSpeed * Time.unscaledDeltaTime);
    }

    void ZoomButton(RectTransform bar, string label, System.Action onClick)
    {
        var b = UIFactory.Button(bar.transform, label, onClick, 20f);
        var le = b.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 26f; le.flexibleWidth = 0f;
    }

    /// How much one button press (or one wheel notch) changes the zoom.
    const float ZoomStep = 1.5f;

    /// Which pane the zoom bar acts on: the last one the cursor was over (if still open), else the host if
    /// it's open, else the first open pane. The bar's own buttons sit under the cursor, so without the latch
    /// the target would flip to "nothing" the moment you moved off a map to press a button.
    CelestialBody ZoomTarget =>
        (activePane != null && openMaps.Contains(activePane)) ? activePane
        : HostOpen ? body
        : (openMaps.Count > 0 ? openMaps[0] : null);

    /// Zoom the active pane about the CENTRE of its frame — the buttons have no cursor to zoom toward, and
    /// pulling the view sideways when someone presses "+" would be its own bug. Dispatches to the host or
    /// the relevant moon.
    public void ZoomBy(float factor)
    {
        var t = ZoomTarget;
        if (t == null) return;
        if (t == body) ZoomHostBy(factor); else ZoomMoonBy(t, factor);
    }

    void ZoomHostBy(float factor)
    {
        if (body?.surface == null) return;
        float fit = FitTilePx();
        float max = Mathf.Max(CoverTilePx(), MaxTilePx());
        float next = Mathf.Clamp(tilePx * factor, fit, max);
        if (Mathf.Approximately(next, tilePx)) return;
        mapPan *= next / tilePx;   // centre stays put: scale the pan by the same ratio the map scaled by
        tilePx = next;
        ApplyMapSize();
        DrawSelectionMarker();
    }

    void ZoomMoonBy(CelestialBody m, float factor)
    {
        if (!moonFrame.TryGetValue(m, out var frame) || frame == null) return;
        Rect fr = frame.rect;
        float floor = ContainFit(fr, m);
        float max = Mathf.Max(CoverFit(fr, m), CeilTilePx(fr));
        float cur = moonTilePx.TryGetValue(m, out float z) ? z : CoverFit(fr, m);
        float next = Mathf.Clamp(cur * factor, floor, max);
        if (Mathf.Approximately(next, cur)) return;
        Vector2 pan = moonPan.TryGetValue(m, out Vector2 pv) ? pv : Vector2.zero;
        moonPan[m] = pan * (next / cur);   // centre stays put, same as the host
        moonTilePx[m] = next;
        ApplyMoonSize(m);
    }

    /// Reset the active pane to its framed default (cover fit, centred). Zoom out from here to see the whole
    /// map within the fixed frame.
    void FitActive()
    {
        var t = ZoomTarget;
        if (t == null) return;
        if (t == body) { tilePx = 0f; mapPan = Vector2.zero; ApplyMapSize(); DrawSelectionMarker(); }
        else { moonTilePx[t] = 0f; moonPan[t] = Vector2.zero; ApplyMoonSize(t); }
    }

    /// Current zoom of the active pane as a percentage of its CONTAIN fit (100% = the whole map fits the
    /// frame). The cover default reads above 100%; zooming out toward the whole map approaches 100%.
    float ZoomPercent()
    {
        var t = ZoomTarget;
        if (t == null) return 100f;
        if (t == body) { float fit = FitTilePx(); return fit > 0.001f ? tilePx / fit * 100f : 100f; }
        if (moonFrame.TryGetValue(t, out var frame) && frame != null)
        {
            float fit = ContainFit(frame.rect, t);
            float cur = moonTilePx.TryGetValue(t, out float z) ? z : CoverFit(frame.rect, t);
            return fit > 0.001f ? cur / fit * 100f : 100f;
        }
        return 100f;
    }

    /// Pixels per cell at the zoomed-all-the-way-OUT end: a CONTAIN fit, where the WHOLE map fits inside the
    /// frame (letterboxed on whichever axis is proportionally shorter). This is the floor you can zoom out
    /// to so you can always see the entire map within its fixed window, and the 100% reference for the zoom
    /// readout. The DEFAULT view on open is CoverTilePx (fills the frame, no dead space); you zoom out from
    /// there to this to see the whole map, or in past it to see fewer, larger cells.
    float FitTilePx()
    {
        if (body?.surface == null) return 4f;
        var vp = hostViewport.rect;
        if (vp.width < 1f || vp.height < 1f) return 4f;
        return Mathf.Min(vp.width / body.surface.width, vp.height / body.surface.height);
    }

    /// Pixels per cell at which the map exactly COVERS the frame (fills it, cropping the longer axis) — the
    /// framed default view, so there's no dead space around a map until you deliberately zoom out to fit.
    float CoverTilePx()
    {
        if (body?.surface == null) return 4f;
        var vp = hostViewport.rect;
        if (vp.width < 1f || vp.height < 1f) return 4f;
        return Mathf.Max(vp.width / body.surface.width, vp.height / body.surface.height);
    }

    /// Pixels per cell at the zoomed-all-the-way-IN end: roughly MaxVisibleTiles cells fill the
    /// viewport, so the closest view shows the same amount of ground on every world rather than
    /// depending on how big the planet happens to be.
    ///
    /// visibleTiles = (w/px) * (h/px) = area / px^2  ->  px = sqrt(area / visibleTiles)
    float MaxTilePx()
    {
        var vp = hostViewport.rect;
        float area = Mathf.Max(1f, vp.width * vp.height);
        return Mathf.Sqrt(area / MaxVisibleTiles);
    }

    void ApplyMapSize()
    {
        if (body?.surface == null) return;

        float floor = FitTilePx();                          // fully zoomed out = whole map fits (contain)
        float max = Mathf.Max(CoverTilePx(), MaxTilePx());  // in past cover to ~200 cells
        tilePx = Mathf.Clamp(tilePx <= 0f ? CoverTilePx() : tilePx, floor, max);   // default view = cover

        mapRT.sizeDelta = new Vector2(body.surface.width * tilePx, body.surface.height * tilePx);
        ClampPan();
    }

    // ---- Wrap mirrors --------------------------------------------------------------------------

    /// One copy of the map's visible content, offset by `side` map-widths.
    void BuildWrapMirror(int side)
    {
        var m = new WrapMirror();

        m.root = UIFactory.NewUI(mapRT, side < 0 ? "WrapLeft" : "WrapRight").GetComponent<RectTransform>();
        UIFactory.Stretch(m.root);
        m.root.name = side < 0 ? "WrapLeft" : "WrapRight";

        var t = UIFactory.NewUI(m.root, "Terrain");
        m.terrain = t.AddComponent<RawImage>();
        UIFactory.Stretch(m.terrain.rectTransform);
        // A raycast target, deliberately. The mirrors are children of mapRT, so a click on one bubbles up
        // to the probe on the real map — which then wraps the longitude back onto the cell this is a copy
        // of (see ScreenToCellIn). Left non-interactive, the mirrored half of the screen would look like
        // the world and behave like a hole.
        m.terrain.raycastTarget = true;

        var o = UIFactory.NewUI(m.root, "Overlay");
        m.overlay = o.AddComponent<RawImage>();
        UIFactory.Stretch(m.overlay.rectTransform);
        m.overlay.raycastTarget = false;
        o.SetActive(false);

        m.pieces = UIFactory.NewUI(m.root, "Pieces").GetComponent<RectTransform>();
        UIFactory.Stretch(m.pieces);

        // Built AFTER pieces, so it draws over them — matching the real map's stacking. A grid whose job
        // is showing which structures it reaches cannot be hidden behind those structures.
        var p = UIFactory.NewUI(m.root, "PowerOverlay");
        m.power = p.AddComponent<RawImage>();
        UIFactory.Stretch(m.power.rectTransform);
        m.power.raycastTarget = false;
        p.SetActive(false);

        m.root.gameObject.SetActive(false);
        mirrors.Add(m);
    }

    /// Put the mirrors one map-width to either side, and copy across what they display.
    ///
    /// Offsetting a STRETCHED rect is done by shifting offsetMin and offsetMax together — that translates
    /// it while it keeps matching the parent's size, so the mirrors track every zoom change for free
    /// rather than needing their own size kept in step.
    void SyncWrapMirrors()
    {
        if (mirrors.Count == 0 || mapRT == null) return;

        float w = mapRT.rect.width;
        bool on = WrapEnabled;

        for (int i = 0; i < mirrors.Count; i++)
        {
            var m = mirrors[i];
            if (m.root == null) continue;

            if (m.root.gameObject.activeSelf != on) m.root.gameObject.SetActive(on);
            if (!on) continue;

            float dx = (i == 0 ? -w : w);
            m.root.offsetMin = new Vector2(dx, 0f);
            m.root.offsetMax = new Vector2(dx, 0f);

            if (m.terrain != null && mapImage != null)
            {
                m.terrain.texture = mapImage.texture;
                m.terrain.color = mapImage.color;
            }
            if (m.overlay != null && overlayImage != null)
            {
                bool ov = overlayImage.gameObject.activeSelf && overlayImage.texture != null;
                if (m.overlay.gameObject.activeSelf != ov) m.overlay.gameObject.SetActive(ov);
                m.overlay.texture = overlayImage.texture;
                m.overlay.color = overlayImage.color;
            }

            // Mirrored on its own now that it has its own layer. Miss this and a node chain crossing
            // longitude 0 reads as BROKEN: the mirrored half shows the terrain, the index and the
            // buildings, and the grid simply stops at the seam.
            if (m.power != null && powerOverlayImage != null)
            {
                bool pv = powerOverlayImage.gameObject.activeSelf && powerOverlayImage.texture != null;
                if (m.power.gameObject.activeSelf != pv) m.power.gameObject.SetActive(pv);
                m.power.texture = powerOverlayImage.texture;
                m.power.color = powerOverlayImage.color;
            }
        }
    }

    /// Wrapping only makes sense once the map is at least as wide as the viewport. Below that the whole
    /// world already fits on screen, there is no edge to run off, and a mirror would just draw a second
    /// copy of the planet beside the first.
    bool WrapEnabled =>
        body != null && body.surface != null && hostViewport != null && mapRT != null &&
        mapRT.rect.width >= hostViewport.rect.width - 0.5f;

    /// Scroll the map horizontally by `dx` pixels, wrapping around the seam.
    public void ScrollMap(float dx)
    {
        if (body == null || body.surface == null) return;
        mapPan.x += dx;
        ClampPan();
    }

    // Keep the viewport covered: you can never drag the map so far that you're looking at the letterbox
    // instead of the world. When the map is smaller than the viewport on an axis, it centres on it.
    //
    // X WRAPS rather than clamping, once the map is wide enough for there to be an edge to run off. The
    // pan is folded back into one map-width, and because a mirror is sitting exactly one map-width to
    // either side, the fold lands on identical content — so the position jumps and the picture does not.
    // Y still clamps: latitude has real ends. The poles are edges, not a seam.
    void ClampPan()
    {
        var vp = hostViewport.rect;
        Vector2 size = mapRT.sizeDelta;

        if (WrapEnabled && size.x > 0.5f)
            mapPan.x = Mathf.Repeat(mapPan.x + size.x * 0.5f, size.x) - size.x * 0.5f;
        else
        {
            float slackX = Mathf.Max(0f, (size.x - vp.width) * 0.5f);
            mapPan.x = Mathf.Clamp(mapPan.x, -slackX, slackX);
        }

        float slackY = Mathf.Max(0f, (size.y - vp.height) * 0.5f);
        mapPan.y = Mathf.Clamp(mapPan.y, -slackY, slackY);
        mapRT.anchoredPosition = mapPan;

        SyncWrapMirrors();
    }

    // Signature covers only the SHAPE of the window: which world, which tab, what's selected, what's
    // built. Live values (costs, efficiency under the cursor) refresh in place, so nothing rebuilds
    // while the economy ticks.
    string Signature()
    {
        if (body == null) return "none";
        var sb = new System.Text.StringBuilder();
        sb.Append(body.id).Append('|').Append((int)tab).Append('|');
        sb.Append(selected.HasValue ? (int)selected.Value : -1).Append('|');
        sb.Append((int)activeIndex).Append('|').Append(showPowerOverlay ? 1 : 0).Append('|').Append(showTectonicsOverlay ? 1 : 0).Append('|').Append(body.Surveyed ? 1 : 0).Append('|').Append(body.deepSurveyed ? 1 : 0).Append('|');

        // The Overview and Orbit tabs fold in the colony/shipyard structure, so their SHAPE changes when
        // a shipyard or research centre is built or upgraded, when a city appears, when ownership flips,
        // or when a ship arrives or leaves orbit. A count/level alone here is enough — the per-value text
        // (costs, progress, ship status) refreshes in place through the LiveSet.
        sb.Append(body.shipyardLevel).Append('|').Append(body.researchCenterLevel).Append('|').Append(body.cities).Append('|');
        sb.Append(body.owner != null ? body.owner.id : -1).Append('|');
        sb.Append(body.units != null ? body.units.Count : 0).Append('|');

        // Species and terraform projects reshape the Survey tab's Climate/Terraform sections (habitability
        // re-scores, the fault list changes) and the Overview's claim/settle road — all structural, and
        // both change rarely, so they belong in the signature rather than in per-frame live text.
        sb.Append(SpeciesManager.CurrentIndex).Append('|');
        if (body.terraformProjects != null) foreach (int pr in body.terraformProjects) sb.Append(pr).Append(',');
        sb.Append('|');

        // The Orbit tab's INBOUND list is drawn from ships in transit toward this world — a set that
        // isn't in body.units, so a ship dispatched here (or diverted away) wouldn't otherwise rebuild
        // the panel. This count only moves on depart/arrive/retarget, never per-frame, so it can't strobe.
        if (tab == Tab.Orbit && UnitManager.Instance != null)
        {
            int inbound = 0;
            foreach (var u in UnitManager.Instance.Units)
                if (u.status == UnitStatus.Traveling && u.travelTarget == body) inbound++;
            sb.Append(inbound).Append('|');
        }

        // The buildings, by TYPE and LEVEL rather than just how many there are. A count alone misses the
        // two mutations that change what's standing here without changing how much of it there is: a
        // structure upgrading a tier, and a settlement growing into a town. Both matter to the Power
        // tab, which lists one card per GRID — and a node's reach scales with its tier, so upgrading one
        // can join two grids into one and leave a card behind pointing at a grid that no longer exists.
        if (body.placedBuildings != null)
        {
            sb.Append(body.placedBuildings.Count).Append('|');
            foreach (var p in body.placedBuildings) sb.Append(p.type).Append(':').Append(p.level).Append(',');
        }
        else sb.Append(0);
        return sb.ToString();
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        if (body == null) { root.SetActive(false); return; }

        TickScrollHold();

        // Once per frame rather than at each of the half-dozen places the map's texture, overlay or size
        // can change. Those are scattered (RefreshMapTexture, the overlay refreshes, ApplyMapSize, the
        // piece rebuild), and a mirror that misses one shows a stale copy of the world beside the real
        // one — a failure that only appears at the seam, which is where nobody is looking during a test.
        // The call is a handful of property writes and early-outs entirely when wrapping is off.
        SyncWrapMirrors();

        string sig = Signature();
        if (sig != lastSig) { lastSig = sig; Rebuild(); }

        // The pane sizes are derived from the map area's size, which isn't known until Unity has laid the
        // window out — so the first layout (from ShowFor) can run against a zero rect. Re-tile once it's
        // real, and again whenever the window is resized by its grip. LayoutPanes re-fits every open pane.
        if (gridHolder != null && gridHolder.rect.size != lastViewportSize)
        {
            lastViewportSize = gridHolder.rect.size;
            LayoutPanes();
        }

        live.Tick();
        PollHover();
        PollMapZoom();
        PollMapPan();
        PollMoonZoomPan();
        PollClickAway();

        // The confirm panel is anchored to a map cell, so it has to be re-placed whenever the map moves
        // under it — which is every frame you're zooming or panning.
        if (pendingType.HasValue) RefreshConfirmPanel();

        // Written straight rather than through LiveSet: it's one short string on one label with no
        // layout group above it, and it only changes while you're actively zooming.
        if (zoomLabel != null)
        {
            string z = $"{ZoomPercent():F0}%";
            if (zoomLabel.text != z) zoomLabel.text = z;
        }

        // The selection marker is rebuilt only when the SELECTION changes — not on the signature, since
        // clicking a building must move the ring instantly without tearing down the whole side panel.
        SurfaceSelection.Validate();
        if (SurfaceSelection.Selected != lastMarkedSelection)
        {
            lastMarkedSelection = SurfaceSelection.Selected;
            DrawSelectionMarker();
        }
        AnimateMarker();

        // Rotate the held piece 90° — before it snaps to the grid and after. Handled here rather than on
        // the map so it works the moment you pick a building up, wherever the cursor happens to be.
        // R is offered alongside right-click because right-click is also the world's "send fleet" verb,
        // so a keyboard rotate is never ambiguous.
        // Not while a confirm is up: the panel is asking about a specific footprint at a specific
        // rotation, and rotating underneath the question would make the answer mean something else.
        if (tab == Tab.Build && selected.HasValue && !pendingType.HasValue &&
            (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.R)))
        {
            rotation = (rotation + 1) % 4;
            RecomputeHoverValidity();   // a rotated piece may now fit (or stop fitting) where it is
            SimpleAudio.Instance?.PlayTick();
        }

        // Escape backs out of the confirm first, and only then drops the held piece — one step at a
        // time, so cancelling a misclick doesn't also make you re-pick the building.
        if (pendingType.HasValue && Input.GetKeyDown(KeyCode.Escape)) { CancelPlace(); return; }

        // Escape drops the held piece.
        if (tab == Tab.Build && selected.HasValue && Input.GetKeyDown(KeyCode.Escape))
        {
            selected = null; CancelPlace(); lastSig = null; ClearGhost();
        }

        if (tab == Tab.Build) DrawGhost();

        // The power overlay's colour tracks each grid's LIVE supply, so it has to be repainted as the
        // economy moves rather than only when the window rebuilds. A few times a second is plenty: it's
        // following a number that drifts, and repainting up to 70,000 texels every frame to do it would
        // be a real cost for something the eye can't see happening anyway.
        if (PowerOverlayActive && body.surface != null)
        {
            powerRepaintIn -= Time.unscaledDeltaTime;
            if (powerRepaintIn <= 0f) { powerRepaintIn = 0.25f; RefreshPowerOverlay(); }
        }

        UpdateStatus();
    }

    void UpdateStatus()
    {
        switch (tab)
        {
            case Tab.Build:
                if (!selected.HasValue)
                    statusText.text = "<color=#9FB4C8>Pick a structure on the right, then click the map to site it — you'll be asked to confirm. " +
                                      "Right-click rotates. Esc cancels.  ·  Scroll to zoom · drag the map to pan.</color>";
                else
                {
                    var info = SurfaceBuildingDatabase.Get(selected.Value);
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"<b>{info.name}</b> · rot {rotation * 90}° <size=10><color=#9FB4C8>(R / right-click rotates · Esc cancels · middle-drag pans)</color></size>");

                    if (hoverCell.x >= 0)
                    {
                        // PREDICTED YIELD at whatever the cursor is over — the honest number, so hovering
                        // a hot spot visibly out-earns hovering cold rock, on every world.
                        sb.Append($"\n<color=#9FB4C8>({hoverCell.x},{hoverCell.y})</color> ");
                        sb.Append($"<b>{SurfaceBuildManager.PredictedYield(body, selected.Value, hoverCell.x, hoverCell.y, rotation)}</b>");

                        if (info.index != SurfaceIndexKind.None)
                        {
                            float e = SurfaceBuildManager.EfficiencyAt(body, selected.Value, hoverCell.x, hoverCell.y, rotation);
                            string hex = ColorUtility.ToHtmlStringRGB(SurfaceBuildManager.EfficiencyColor(e));
                            // Absolute AND relative: what it yields, and how that ranks on THIS world —
                            // because on a poor world the best available site is still worth knowing.
                            float pct = SurfaceIndex.Percentile(body, info.index, hoverCell.x, hoverCell.y);
                            sb.Append($"\n{SurfaceIndex.Name(info.index)} <color=#{hex}><b>{e * 100f:F0}% ({SurfaceBuildManager.EfficiencyLabel(e)})</b></color>");
                            sb.Append($" <size=10><color=#9FB4C8>· better than {pct * 100f:F0}% of this world</color></size>");
                        }

                        sb.Append(hoverValid
                            ? "   <color=#4DFF6E>Left-click to build</color>"
                            : $"   <color=#FF6659>{HoverWhy()}</color>");
                    }
                    else if (!string.IsNullOrEmpty(info.siteRequirement))
                        sb.Append($"\n<color=#C9A94D>{info.siteRequirement}</color>");

                    statusText.text = sb.ToString();
                }
                break;
            case Tab.Survey:
                // BOTH overlays can be up at once now, so the status line ACCUMULATES rather than
                // branching. The old `if power / else index` meant switching the grid on left the index
                // ramp on screen with no legend and no description — a coloured map and nothing saying
                // what the colours were.
                {
                    var sur = new System.Text.StringBuilder();

                    if (activeIndex != SurfaceIndexKind.None)
                    {
                        sur.Append($"<b>{SurfaceIndex.Name(activeIndex)}</b> — {SurfaceIndex.Describe(activeIndex)}");

                        // The Weather index is the one whose ceiling is a fact about the WHOLE WORLD
                        // rather than about any tile — an airless world's map is uniformly black, and
                        // without this the player is left to guess whether that means "nowhere is windy"
                        // or "this world has no air". Say which.
                        if (activeIndex == SurfaceIndexKind.Wind)
                            sur.Append($"  <color=#9FB4C8>·</color> <b>{SurfaceIndex.WeatherLabel(body)}</b> " +
                                       $"<size=10><color=#9FB4C8>at {body.atmospheres:0.#} atmospheres</color></size>");

                        // Same for Solar, where pressure sets a hard ceiling on the best tile.
                        if (activeIndex == SurfaceIndexKind.Solar)
                        {
                            float f = SurfaceIndex.SolarPressureFactor(body.atmospheres);
                            string hex = ColorUtility.ToHtmlStringRGB(f >= 1f ? UITheme.Good : f > 0f ? UITheme.Accent : UITheme.Bad);
                            sur.Append($"  <color=#9FB4C8>·</color> <color=#{hex}><b>{f * 100f:F0}% panel output</b></color> " +
                                       $"<size=10><color=#9FB4C8>at {body.atmospheres:0.#} atmospheres</color></size>");
                        }
                    }

                    if (showPowerOverlay)
                    {
                        if (sur.Length > 0) sur.Append('\n');

                        var nets = PowerGrid.Nets(body);
                        if (nets.Count == 0)
                        {
                            sur.Append("<color=#FFBF4D>No power on this world.</color> <size=10><color=#9FB4C8>" +
                                       "The map is dark because there is no grid on it — build a plant from the Build tab.</color></size>");
                        }
                        else
                        {
                            float gen = PowerGrid.TotalGeneration(body), draw = PowerGrid.TotalDraw(body);
                            string hex = ColorUtility.ToHtmlStringRGB(gen >= draw ? UITheme.Good : UITheme.Bad);
                            sur.Append("<color=#F5F58C>■</color> grid   <color=#4DC8FF>■</color> plants & relays");
                            sur.Append($"   ·   <b>{gen:0.0}</b> made, <b>{draw:0.0}</b> drawn, ");
                            sur.Append($"<color=#{hex}><b>{(gen - draw >= 0f ? "+" : "")}{gen - draw:0.0}/s</b></color>");
                            if (hoverCell.x >= 0)
                            {
                                var n = PowerGrid.NetAt(body, hoverCell.x, hoverCell.y);
                                sur.Append($"\n<color=#9FB4C8>({hoverCell.x},{hoverCell.y})</color> ");
                                sur.Append(n == null
                                    ? "<color=#FF6659>dark — no grid reaches this tile</color>"
                                    : $"<color=#F5F58C>on Grid {n.index}</color> <size=10><color=#9FB4C8>· {PowerGrid.SupplyLabel(n)}</color></size>");
                            }
                        }
                    }

                    if (sur.Length == 0)
                        sur.Append("<color=#9FB4C8>Pick an index and/or the power grid on the right to overlay them on the map.</color>");

                    statusText.text = sur.ToString();
                }
                break;
            default:
                statusText.text = body.Surveyed
                    ? "<color=#9FB4C8>Surveyed. Use the Build tab to develop the surface, or Survey to see where things belong.</color>"
                    : "<color=#FFBF4D>This world is unsurveyed — send a ship to map it before building on it.</color>";
                break;
        }
    }

    string HoverWhy()
    {
        if (!selected.HasValue || hoverCell.x < 0) return "";
        SurfaceBuildManager.CanPlace(body, selected.Value, hoverCell.x, hoverCell.y, rotation, out string why);
        return why ?? "";
    }

    // ---- Rebuild ----
    void Rebuild()
    {
        live.Clear();
        for (int i = sidePanel.childCount - 1; i >= 0; i--) Destroy(sidePanel.GetChild(i).gameObject);
        for (int i = tabStrip.childCount - 1; i >= 0; i--) Destroy(tabStrip.GetChild(i).gameObject);

        BuildTabStrip();
        switch (tab)
        {
            case Tab.Overview: BuildOverviewPanel(); break;
            case Tab.Build: BuildBuildPanel(); break;
            case Tab.Survey: BuildSurveyPanel(); break;
            case Tab.Orbit: BuildOrbitPanel(); break;
            case Tab.Terrain: BuildTerrainPanel(); break;
        }

        RefreshOverlay();
        DrawPieces();
        RefreshSiteMarkers();
        if (tab != Tab.Build) ClearGhost();
    }

    void BuildTabStrip()
    {
        foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
        {
            // The terrain editor doesn't exist outside Dev Mode — greying it would advertise a sandbox
            // tool to a player who can never use it. Every other tab is a real feature they can unlock,
            // so those stay visible and explain themselves.
            if (t == Tab.Terrain && !GameMode.DevMode) continue;

            var captured = t;
            bool active = t == tab;
            bool open = TabAvailable(t, out string why);

            var btn = UIFactory.Button(tabStrip, t.ToString(), () =>
            {
                if (!TabAvailable(captured, out _)) return;
                tab = captured;
                if (captured != Tab.Build) { selected = null; CancelPlace(); }
                lastSig = null;
            }, 22);
            btn.interactable = open;

            var le = btn.GetComponent<LayoutElement>();
            le.preferredWidth = 90; le.minWidth = 70; le.flexibleWidth = 0;

            // The active tab is state, not hover, so a persistent tint is correct here.
            var colors = btn.colors;
            colors.normalColor = active ? UITheme.ButtonActive : UITheme.ButtonBg;
            colors.highlightedColor = colors.normalColor;
            colors.selectedColor = colors.normalColor;
            btn.colors = colors;
            var lbl = btn.GetComponentInChildren<TMP_Text>();
            if (lbl != null)
            {
                lbl.fontSize = UITheme.SmallSize;
                lbl.color = !open ? new Color(0.45f, 0.52f, 0.62f) : active ? Color.white : UITheme.SubText;
            }

            // The reason lives on the tab itself, so it's there when you go looking for it rather than
            // only in a status line you have to already be reading.
            if (!open && why != null) UIFactory.Tooltip(btn.gameObject, $"{t} — {why}");
        }
    }

    // ---------------- OVERVIEW ----------------
    // What this world IS, and — for a world you own — how its colony is doing. The Society/Production
    // summary (population, cities, development, objectives) and the research-centre ladder were folded in
    // here from the retired "Colony" and Inspector windows. The shipyard, being a space construct, went
    // to the Orbit tab instead (Raptok's mapping); the research centre is ground infrastructure, so its
    // upgrade stays on this colony-side tab.
    void BuildOverviewPanel()
    {
        Header("THIS WORLD");
        var card = Card();
        Stat(card, "Name", () => body.name);
        Stat(card, "Type", () => TerraformDiagnosis.Pretty(body));
        // The conditionals MUST be parenthesised: inside an interpolation hole a bare ':' is parsed as
        // the start of a format specifier, not as part of a ternary.
        Stat(card, "Surface", () => $"{(body.surface != null ? body.surface.width : 0)} × {(body.surface != null ? body.surface.height : 0)} tiles");
        Stat(card, "Mass", () => MassWord(body.mass));
        // Beside Mass, because Mass is what SETS it — one atmosphere per unit of mass, halved without a
        // magnetic field. Putting them next to each other is what makes that relationship legible
        // without a tooltip explaining it.
        Stat(card, "Atmospheres", () =>
        {
            string suit = "";
            var sp = SpeciesManager.Current;
            if (sp != null && body.atmospheres > 0.01f)
            {
                float fit = sp.AtmosphereSuitability(body.atmospheres);
                if (fit < 0.999f)
                    suit = body.atmospheres < sp.minAtmospheres
                        ? $" <color=#FFBF4D>· too thin for {sp.name}</color>"
                        : $" <color=#FFBF4D>· too dense for {sp.name}</color>";
            }
            return $"{body.atmospheres:0.#} <size=10><color=#9FB4C8>({AtmosphereRules.Describe(body)})</color></size>{suit}";
        });
        Stat(card, "Magnetic field", () => body.hasMagneticField
            ? "<color=#4DFF6E>Yes</color>"
            : $"<color=#FF8F5C>No</color> <size=10><color=#9FB4C8>— ceiling halved to {AtmosphereRules.Ceiling(body):0.#}</color></size>");
        Stat(card, "Owner", () =>
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(body.owner));
            return $"<color={hex}>{FactionManager.OwnerLabel(body.owner)}</color>";
        });
        Stat(card, "Habitability", () => $"<color={Habitability.ScoreColorHex(body.habitability)}>{body.habitability:F0}%</color> for {SpeciesManager.Current.name}");

        // The ownership road — claim, then settle — folded from the retired Inspector body window, plus
        // the direct "establish city" path the Colony/Production windows offered for an owned world.
        BuildOwnershipSection();

        Header("CLIMATE & WEATHER");
        var w = Card();
        var t = UIFactory.WrapText(w, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () => WeatherProse(body));

        Header("DEVELOPMENT");
        var d = Card();
        var dt = UIFactory.WrapText(d, "", UITheme.SmallSize, UITheme.Text);
        live.Text(dt, () =>
        {
            int n = body.placedBuildings != null ? body.placedBuildings.Count : 0;
            float dens = SurfaceBuildManager.Density(body);
            return $"<b>{n}</b> structure(s) on the surface\nLand developed: <b>{dens * 100f:F0}%</b> of buildable ground";
        });

        Header("URBANISATION");
        Bar(d, () =>
        {
            float f = CityGrowth.UrbanFraction(body);
            return (f, $"{CityGrowth.UrbanLabel(body)} — {f * 100f:F0}% of the land is settled", UITheme.Accent);
        });
        var ut = UIFactory.WrapText(d, "", UITheme.SmallSize, UITheme.SubText);
        live.Text(ut, () =>
        {
            if (!GameConfig.OrganicCityGrowth)
                return "<color=#9FB4C8>Organic city growth is off — this world holds only what you place on it.</color>";
            if (body.owner != FactionManager.Player) return "";

            float live01 = CityGrowth.Liveability(body);
            if (live01 <= 0.01f)
                return $"<color=#FFBF4D>At {body.habitability:F0}% habitability nobody will settle here on their own.</color> " +
                       $"Terraform it past {Colony.FoundThreshold:F0}% and the population will start spreading.";

            int have = CityGrowth.CountSettlements(body);
            int cap = CityGrowth.MaxSettlements(body);
            string ceiling = CityGrowth.MaxTier(body) == 3 ? "full cities"
                           : CityGrowth.MaxTier(body) == 2 ? "towns" : "small settlements";
            return $"{have}/{cap} settlements · this world can grow <b>{ceiling}</b>\n" +
                   $"<size=10><color=#9FB4C8>At {body.habitability:F0}% habitability, a new one roughly every " +
                   $"{CityGrowth.SpawnInterval(body):F0}s once there are people to fill it.</color></size>";
        });

        // ---- Society & Production (folded from the retired Colony window) ----
        // Only for a world you own: population, cities and the objectives that establish a colony are
        // meaningless on somebody else's planet or a dead rock.
        if (body.owner == FactionManager.Player)
        {
            Header("SOCIETY");
            var soc = Card();
            Stat(soc, "Population", () => $"{Population.Format(body.population)} <color=#9FB4C8>of {Population.Format(Colony.PopTarget(body))} capacity</color>");
            Stat(soc, "Cities", () => body.cities.ToString());
            Stat(soc, "Development", () => $"<b>{Colony.ClaimProgress(body) * 100f:F0}%</b>" +
                (Colony.IsFullyEstablished(body) ? "  <color=#4DFF6E>fully established</color>" : ""));
            Bar(soc, () =>
            {
                int popCap = Colony.PopTarget(body);
                float f = popCap > 0 ? body.population / (float)popCap : 0f;
                var c = f >= 1f ? UITheme.Bad : f > 0.9f ? UITheme.Warn : UITheme.Accent;
                return (f, $"Population {body.population}/{popCap}", c);
            });
            // The three ceilings are shown apart: "capacity" is a min() of three different problems, and
            // the number alone doesn't say which one you have — land wants terraforming, housing wants
            // building, food wants farms.
            Stat(soc, "Land supports", () => Population.Format(Carrying.LandCap(body)));
            Stat(soc, "Housing for", () => Population.Format(Carrying.HousingCap(body)));
            Stat(soc, "Food", () => Carrying.FoodLine(body));

            // Satisfaction, with the full reasoning — an unhappy colony should say exactly what it's
            // unhappy about, and whether that's stalling its growth. Folded from the Inspector's Society tab.
            Header("SATISFACTION");
            Bar(sidePanel, () =>
            {
                float sat = Satisfaction.For(body);
                return (sat / 100f, $"{Satisfaction.Label(sat)} — {sat:F0}%", Satisfaction.Color(sat));
            });
            var breakdown = Card();
            var bt = UIFactory.WrapText(breakdown, "", UITheme.SmallSize, UITheme.Text);
            live.Text(bt, () =>
            {
                var sb = new System.Text.StringBuilder();
                foreach (var f in Satisfaction.Breakdown(body))
                {
                    string hex = ColorUtility.ToHtmlStringRGB(f.delta >= 0f ? UITheme.Good : UITheme.Bad);
                    sb.AppendLine($"<color=#{hex}>{(f.delta >= 0f ? "+" : "")}{f.delta:F0}</color>  <b>{f.label}</b>  <color=#9FB4C8>{f.detail}</color>");
                }
                float mult = Satisfaction.GrowthMultiplier(body);
                string stall = Population.StallReason(body, InfrastructureGrowth(body));
                sb.AppendLine(stall != null
                    ? $"\n<color=#FF6659>Not growing — {stall}.</color>"
                    : $"\n<color=#9FB4C8>Birth rate ×{mult:0.00} from satisfaction · " +
                      $"{Population.Format(Mathf.RoundToInt(Population.BirthRate(body, InfrastructureGrowth(body)) * 60f))} per minute</color>");
                return sb.ToString();
            });

            // What this colony can actually DO — food/power/research/industry/housing counted across BOTH
            // colony facilities and surface structures. The Production tab's rollup, folded here.
            Header("CAPABILITY");
            var capCard = Card();
            var ct = UIFactory.WrapText(capCard, "", UITheme.SmallSize, UITheme.Text);
            live.Text(ct, () =>
            {
                int food = ColonyFacilities.FoodSources(body);
                int power = ColonyFacilities.PowerSources(body);
                int res = ColonyFacilities.ResearchSources(body);
                int ind = ColonyFacilities.IndustrySources(body);
                int hou = ColonyFacilities.HousingSources(body);
                string F(int n, string good, string bad) =>
                    n > 0 ? $"<color=#4DFF6E>{good}</color>" : $"<color=#FF7A6E>{bad}</color>";
                return $"{F(food, $"{food} food source(s)", "no food")}  ·  {F(power, $"{power} generator(s)", "no power")}\n" +
                       $"{F(res, $"{res} research tier(s)", "no research")}  ·  {F(ind, $"{ind} industry tier(s)", "no industry")}  ·  " +
                       $"{F(hou, $"{hou} housing", "no housing")}";
            });

            Header("OBJECTIVES TO FULLY ESTABLISH");
            var obj = Card();
            var ot = UIFactory.WrapText(obj, "", UITheme.SmallSize, UITheme.Text);
            live.Text(ot, () =>
            {
                var sb = new System.Text.StringBuilder();
                foreach (var o in Colony.Objectives(body))
                    sb.AppendLine($"{(o.done ? "<color=#4DFF6E>[x]</color>" : "<color=#FF7A6E>[ ]</color>")} {o.label}  <color=#9FB4C8>({o.detail})</color>");
                return sb.ToString().TrimEnd();
            });

            BuildResearchCentreSection();
        }

        Header("SURVEY STATE");
        var s = Card();
        var st = UIFactory.WrapText(s, "", UITheme.SmallSize, UITheme.Text);
        live.Text(st, () =>
        {
            if (!body.Surveyed) return $"<color=#FFBF4D>Unsurveyed — {body.explorationProgress * 100f:F0}% mapped.</color> Send a ship to map it.";
            if (!body.deepSurveyed) return "<color=#4DFF6E>Surveyed.</color> The Mineral Index is available.\n" +
                                          "<color=#FFBF4D>No deep survey yet</color> — send a research ship to study this world and unlock the Heat, Fertile and Weather indexes.";
            return "<color=#4DFF6E>Fully surveyed.</color> Every index overlay is available.";
        });

        // Restore the world's ORIGINAL look — the terrain seed and natural climate it generated with —
        // undoing however terraforming (or the Dev terrain sandbox) has remodelled its surface. The world's
        // structures, ownership, population and terraform PROJECT list are untouched; only the terrain
        // appearance snaps back to the planet you first found.
        Header("APPEARANCE");
        var ap = Card();
        Note(ap, "Make this world look the way it did when it was first generated. Its colony and terraforming " +
                 "progress stay; only the surface's appearance resets.");
        UIFactory.Button(ap, "Reset appearance to original", () =>
        {
            body.terrainSeed = body.naturalSeed;
            body.terrainParams = body.naturalParams;
            RegenerateTerrain();
            lastSig = null;   // rebuild so any dependent readouts refresh
        }, 26);
    }

    // The two-stage road to owning a world — CLAIM it, then SETTLE it once it's liveable — plus the
    // direct "establish city" founding an owned world can use. Folded from the Inspector body window's
    // claim section and the Colony/Production windows. Nothing shows on a world that's already settled
    // or belongs to someone else.
    void BuildOwnershipSection()
    {
        var b = body;
        var mgr = ColonyManager.Instance;

        if (!Claim.IsSettled(b) && (b.owner == null || b.owner == FactionManager.Player))
        {
            if (!Claim.IsMine(b))
            {
                Header("CLAIM THIS WORLD");
                var card = Card();
                Note(card, "A claim is a flag, not a colony. Habitability doesn't matter — it's what keeps the world yours while you terraform it.");
                ConditionList(card, () => Claim.ClaimConditions(b));

                var btn = UIFactory.Button(sidePanel, "", () => { if (Claim.DoClaim(b)) lastSig = null; }, 26);
                live.Button(btn, () => Claim.CanClaim(b, out string why)
                    ? (true, $"Claim {b.name}  ({Claim.BeaconMetal(b)}m {Claim.BeaconEnergy(b)}e)")
                    : (false, $"Claim — {why}"));
            }
            else
            {
                Header("SETTLE THIS WORLD");
                var card = Card();
                Note(card, "Claimed. Nobody lives here yet — a world has to be liveable before anyone can, and until it's settled you can't build on its surface.");
                ConditionList(card, () => Claim.SettleConditions(b));

                var btn = UIFactory.Button(sidePanel, "", () =>
                {
                    var ship = FirstColonyShip(b);
                    if (ship != null)
                        UnitManager.Instance?.IssueAction(new List<Unit> { ship }, OrderKind.Colonize, b, false);
                }, 26);
                live.Button(btn, () => Claim.CanSettle(b, out string why)
                    ? (true, $"Settle {b.name} — land the colony ship")
                    : (false, $"Settle — {why}"));
            }
        }

        // An owned world without a city can found one directly (a home moon, say) — the Colony/Production
        // windows' "Establish City" path, kept alive now those windows are retired.
        if (mgr != null && b.owner == FactionManager.Player && !b.buildings.Contains((int)BuildingType.City))
        {
            var cityBtn = UIFactory.Button(sidePanel, "", () => { if (mgr.StartEstablishCity(b)) lastSig = null; }, 26);
            live.Button(cityBtn, () =>
            {
                bool can = mgr.CanEstablishCity(b, out string why);
                return (can, can ? $"Establish City ({ColonyManager.CityMetal}m {ColonyManager.CityEnergy}e, {ColonyManager.CityBuildTime:F0}s)"
                                 : $"Establish City — {why}");
            });
        }
    }

    // A live tick-list of conditions — re-read every refresh, so it updates as a ship arrives or
    // terraforming lands. Ported from the Inspector.
    void ConditionList(Transform parent, System.Func<List<ColonyObjective>> src)
    {
        var t = UIFactory.WrapText(parent, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () =>
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in src())
            {
                string hex = ColorUtility.ToHtmlStringRGB(c.done ? UITheme.Good : UITheme.Bad);
                sb.AppendLine($"<color=#{hex}>{(c.done ? "+" : "×")}</color> {c.label}  <color=#9FB4C8>{c.detail}</color>");
            }
            return sb.ToString().TrimEnd();
        });
    }

    static Unit FirstColonyShip(CelestialBody b)
    {
        if (b?.units == null) return null;
        foreach (var u in b.units)
            if (u != null && u.owner == FactionManager.Player && u.Info.canColonize) return u;
        return null;
    }

    // The colony's total capacity to raise people — every building's popGrowthPerSec scaled by siting,
    // plus surface structures. Mirrors what ColonyManager feeds Population.BirthRate, so the readout and
    // the simulation can't disagree. Ported from the Inspector's Society tab.
    static float InfrastructureGrowth(CelestialBody b)
    {
        float g = 0f;
        foreach (int id in b.buildings) g += BuildingDatabase.Get((BuildingType)id).popGrowthPerSec;
        g += SurfaceBuildManager.PopGrowthPerSec(b);
        return g;
    }

    // The research-centre ladder, folded from the Colony window. A tier of research centre adds a point
    // of research CAPACITY (how many technologies can study at once). It's ground infrastructure, so it
    // lives on Overview rather than Orbit. If the world hasn't got one yet, offer to build one.
    void BuildResearchCentreSection()
    {
        var mgr = ColonyManager.Instance;
        Header("RESEARCH CENTRE");

        if (body.researchCenterLevel < 1)
        {
            var card = Card();
            Note(card, "No research centre here. Build one to add research capacity to your empire.");
            if (mgr != null)
            {
                var group = card.gameObject.AddComponent<CanvasGroup>();
                var btn = UIFactory.Button(card, "", () => { if (mgr.StartBuilding(body, BuildingType.ResearchCenter)) lastSig = null; }, 24);
                live.Button(btn, () =>
                {
                    bool can = mgr.CanBuild(body, BuildingType.ResearchCenter, out string why);
                    var info = BuildingDatabase.Get(BuildingType.ResearchCenter);
                    return (can, can ? $"Build Research Centre ({ColonyManager.DiscCost(info.costMetal)}m {ColonyManager.DiscCost(info.costEnergy)}e)"
                                     : $"Build Research Centre — {why}");
                }, group);
            }
            return;
        }

        var rc = Card();
        Stat(rc, "Tier", () => $"Level <b>{body.researchCenterLevel}</b> / {Colony.MaxResearchCenterLevel}");
        Stat(rc, "Research capacity", () => $"<color=#8FD0FF><b>{ResearchCapacity.ForBody(body)}</b></color>");

        if (mgr != null && body.researchCenterLevel < Colony.MaxResearchCenterLevel)
        {
            int next = body.researchCenterLevel + 1;
            var btn = UIFactory.Button(rc, "", () => { if (mgr.StartLabUpgrade(body)) lastSig = null; }, 24);
            live.Button(btn, () =>
            {
                bool can = mgr.CanUpgradeLab(body, out string why, out _);
                return (can, can
                    ? $"Upgrade -> Lv{next} ({ColonyManager.LabUpgradeMetal(next)}m {ColonyManager.LabUpgradeEnergy(next)}e, {ColonyManager.LabUpgradeTime(next):F0}s) -> {ResearchCapacity.ForLevel(next)} capacity"
                    : $"Upgrade -> Lv{next} — {why}");
            });
        }
        else if (body.researchCenterLevel >= Colony.MaxResearchCenterLevel)
            UIFactory.WrapText(rc, "<color=#4DFF6E>At maximum tier.</color>", UITheme.SmallSize, UITheme.Good);
    }

    // ---------------- ORBIT ----------------
    // Space constructs and everything in dock or orbit around this world, folded from the retired Colony
    // window's shipyard controls and the Inspector's "Objects" tab (Raptok's mapping). The shipyard is
    // the headline: a tier of it is PARALLELISM — how many hulls it can hold on the stocks at once,
    // pooled with every other yard you own. Moons are NOT listed here; they get their own tabs under the
    // map (see the moon-tab work), so this is ships, stations and inbound traffic.
    void BuildOrbitPanel()
    {
        var mgr = ColonyManager.Instance;

        // Per-world orbit-ring visibility (the request's "click a planet, turn its orbit and its moons'
        // orbits off"). For a planet the toggle also hides every one of its moons' rings; for a moon it's
        // just that moon's own ring.
        Header("ORBIT DISPLAY");
        {
            bool isMoon = body.parentBody != null;
            var card = Card();
            UIFactory.Toggle(card,
                isMoon ? "Show this moon's orbit ring" : "Show this world's + its moons' orbit rings",
                body.showRing, on => SetPlanetOrbitRings(body, on));
            Note(card, isMoon
                ? "Hide the blue ring this moon traces around its planet."
                : "Hide the blue orbit ring for this world and all of its moons at once.");
        }

        Header("SHIPYARD");
        if (body.shipyardLevel < 1)
        {
            var card = Card();
            Note(card, "No shipyard in orbit of this world. A shipyard is where hulls are laid down; every one you own pools its build power.");
            if (mgr != null && body.owner == FactionManager.Player)
            {
                var group = card.gameObject.AddComponent<CanvasGroup>();
                var btn = UIFactory.Button(card, "", () => { if (mgr.StartBuilding(body, BuildingType.Shipyard)) lastSig = null; }, 24);
                live.Button(btn, () =>
                {
                    bool can = mgr.CanBuild(body, BuildingType.Shipyard, out string why);
                    var info = BuildingDatabase.Get(BuildingType.Shipyard);
                    return (can, can ? $"Build Shipyard ({ColonyManager.DiscCost(info.costMetal)}m {ColonyManager.DiscCost(info.costEnergy)}e)"
                                     : $"Build Shipyard — {why}");
                }, group);
            }
        }
        else
        {
            var card = Card();
            Stat(card, "Tier", () => $"Level <b>{body.shipyardLevel}</b> / {Colony.MaxShipyardLevel}  <color=#9FB4C8>({Colony.ShipyardPerk(body.shipyardLevel)})</color>");
            Stat(card, "Build power", () => $"<color=#8FD0FF><b>{BuildPower.ForBody(body)}</b></color>" +
                (TechEffects.ShipyardPowerBonus > 0 ? $"  <color=#9FB4C8>(+{TechEffects.ShipyardPowerBonus} from research)</color>" : ""));

            // The tier/build-power readout above is informational and shows for any world (an enemy
            // yard's tier is intel). The interactive controls — upgrade and the build-ships link — are
            // gated to worlds you own, matching the "Build Shipyard" path and the retired Colony window.
            if (body.owner == FactionManager.Player)
            {
                if (mgr != null && body.shipyardLevel < Colony.MaxShipyardLevel)
                {
                    int next = body.shipyardLevel + 1;
                    var up = UIFactory.Button(card, "", () => { if (mgr.StartShipyardUpgrade(body)) lastSig = null; }, 24);
                    live.Button(up, () =>
                    {
                        bool can = mgr.CanUpgradeShipyard(body, out string why, out _);
                        return (can, can
                            ? $"Upgrade -> Lv{next} ({ColonyManager.ShipyardUpgradeMetal(next)}m {ColonyManager.ShipyardUpgradeEnergy(next)}e, {ColonyManager.ShipyardUpgradeTime(next):F0}s) -> {BuildPower.ForLevel(next)} build power"
                            : $"Upgrade -> Lv{next} — {why}");
                    });
                }
                else UIFactory.WrapText(card, "<color=#4DFF6E>This yard is at its maximum tier.</color>", UITheme.SmallSize, UITheme.Good);

                // Laying down hulls is the empire-wide Shipyard window's job (yards pool their power, so
                // the catalogue isn't per-world). Link straight to it rather than re-implementing stocks.
                UIFactory.Button(sidePanel, "Open Shipyard (build ships) »", () => ShipyardWindow.Instance?.Toggle(), 26);
            }
        }

        // ---- What's in orbit ----
        // Stations are infrastructure; ships are a fleet. Listed apart, like the Objects tab did, plus
        // what's inbound so you can see traffic before you decide anything.
        var ships = new List<Unit>();
        var stations = new List<Unit>();
        if (body.units != null)
            foreach (var u in body.units) (u.Info.isStation ? stations : ships).Add(u);

        Header("STATIONS & CONSTRUCTS");
        if (stations.Count == 0) Note("No stations deployed here.");
        else foreach (var u in stations) OrbitUnitRow(u);

        Header("SHIPS IN ORBIT");
        if (ships.Count == 0) Note("No ships here.");
        else foreach (var u in ships) OrbitUnitRow(u);

        var inbound = new List<Unit>();
        if (UnitManager.Instance != null)
            foreach (var u in UnitManager.Instance.Units)
                if (u.status == UnitStatus.Traveling && u.travelTarget == body) inbound.Add(u);
        if (inbound.Count > 0)
        {
            Header("INBOUND");
            foreach (var u in inbound)
            {
                var cap = u;
                var card = Card();
                var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.SubText);
                live.Text(t, () => $"<b>{cap.name}</b> — arriving in {Mathf.Max(0f, cap.travelDuration - cap.travelElapsed):F0}s");
                UIFactory.Button(card, "Select »", () => UnitSelection.SelectOnly(cap), 22);
            }
        }
    }

    // Turn a world's orbit ring on/off, and — for a planet — all of its moons' rings with it. Writes the
    // body data (so it saves and survives re-visualization) and drives the live OrbitController.
    void SetPlanetOrbitRings(CelestialBody b, bool show)
    {
        if (b == null) return;
        ApplyOrbitRing(b, show);
        if (b.parentBody == null && b.moons != null)
            foreach (var m in b.moons) ApplyOrbitRing(m, show);
    }

    static void ApplyOrbitRing(CelestialBody b, bool show)
    {
        if (b == null) return;
        b.showRing = show;
        if (b.visualObject != null)
        {
            var oc = b.visualObject.GetComponent<OrbitController>();
            if (oc != null) oc.SetRingVisible(show);
        }
    }

    // A selectable ship/station row for the Orbit tab. Selecting it hands off to the unit selection
    // system, exactly as the Inspector's Objects tab did.
    void OrbitUnitRow(Unit u)
    {
        var cap = u;
        var card = Card();
        var row = UIFactory.NewUI(card, "Row"); UIFactory.AddLayout(row, 22);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childAlignment = TextAnchor.MiddleLeft;

        var icon = UIFactory.NewUI(row.transform, "Icon");
        var img = icon.AddComponent<Image>();
        img.sprite = UnitIconRenderer.Sprite(u.type);
        img.preserveAspect = true; img.raycastTarget = false;
        var ile = icon.AddComponent<LayoutElement>();
        ile.preferredWidth = 18; ile.minWidth = 18; ile.preferredHeight = 18; ile.flexibleWidth = 0;

        var t = UIFactory.Text(row.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        var tle = t.gameObject.AddComponent<LayoutElement>(); tle.flexibleWidth = 1;
        live.Text(t, () =>
        {
            int g = ControlGroups.GroupOf(cap);
            string badge = g > 0 ? $"<color=#5AB4F0>[{g}]</color> " : "";
            return $"{badge}<b>{cap.name}</b>  <size=10><color=#9FB4C8>{cap.Info.name} · {cap.RankName} · {cap.status}</color></size>";
        });

        UIFactory.Button(card, "Select »", () => UnitSelection.SelectOnly(cap), 22);
    }

    static string SizeWord(int size)
    {
        if (size <= 4) return $"Tiny ({size})";
        if (size <= 7) return $"Small ({size})";
        if (size <= 11) return $"Medium ({size})";
        if (size <= 15) return $"Large ({size})";
        return $"Huge ({size})";
    }

    // The world's MASS VALUE as the player sees it — a descriptor plus the number (Earth-like ~2, gas
    // giants 7-13, moons/asteroids below 1). This replaces the old surfaceSize "size class" readout.
    static string MassWord(float mass)
    {
        string w = mass < 0.5f ? "Tiny" : mass < 1.5f ? "Small" : mass < 4f ? "Medium" : mass < 7f ? "Large" : "Giant";
        return $"{w} ({MassRules.Format(mass)})";
    }

    static string WeatherProse(CelestialBody b)
    {
        var sb = new System.Text.StringBuilder();
        float spin = Mathf.Abs(b.spinSpeed);
        sb.Append(spin < 3f ? "Barely turns — one face bakes, the other freezes. "
                : spin > 45f ? "Spins violently; the storms never stop. "
                : "A steady day/night cycle. ");
        sb.Append(Mathf.Abs(b.inclination) > 28f ? "Its severe axial tilt gives it savage seasons. " : "Mild seasons. ");
        // Read the water actually on the surface (its Water Level), not the disconnected Water resource
        // number — so a world covered in ocean tiles never reads "bone dry".
        float waterLevel = PlanetTerrainGenerator.WaterLevelFromSeaLevel(b.terrainParams.SeaLevelOrNeutral);
        sb.Append(waterLevel < 0.15f ? "Bone dry — no weather to speak of. "
                : waterLevel > 0.6f ? "Wet, with heavy cloud and frequent storms. "
                : "Moderate moisture and weather. ");
        if (b.surfaceSize >= 14) sb.Append("Massive enough to hold a deep, heavy atmosphere.");
        else if (b.surfaceSize <= 4) sb.Append("Too small to hold much of an atmosphere at all.");
        return sb.ToString();
    }

    // ---------------- BUILD ----------------
    void BuildBuildPanel()
    {
        Header("STRUCTURES");
        if (body.owner != FactionManager.Player)
        { Note("You can only build on worlds you own. Colonize this world first."); return; }
        if (!body.Surveyed)
        { Note("Survey this world before developing it."); return; }

        Note("Click a structure to pick it up, then click the map to place it. <b>Right-click rotates.</b> Esc cancels. Footprints interlock — pack them tightly.");

        // Grouped by what a structure is FOR, so a growing catalogue stays navigable.
        foreach (SurfaceBuildingCategory cat in System.Enum.GetValues(typeof(SurfaceBuildingCategory)))
        {
            bool headerAdded = false;
            foreach (var info in SurfaceBuildingDatabase.All)
            {
                if (info == null || info.category != cat) continue;
                // The capitol and the grounded ship aren't placed from the tray — the ship arrives with
                // the colony, and the capitol is what it becomes. Listing them here would only confuse.
                if (info.type == SurfaceBuildingType.PlanetCapitol) continue;
                if (info.type == SurfaceBuildingType.ColonyShipBase && !GameMode.DevMode) continue;
                // Settlements/towns/cities are grown by the population, never placed.
                if (CityGrowth.IsSettlement(info.type) && !GameMode.DevMode) continue;
                if (!headerAdded) { Header(CategoryName(cat)); headerAdded = true; }
                BuildStructureCard(info);
            }
        }

        // The built-here list is the (richer) Infrastructure panel, folded in now that Infrastructure is
        // no longer its own tab: per-structure health, siting, power draw, select-on-map, upgrade and
        // demolish. This replaces the simpler BuildPlacedList (kept below, unused, as reference).
        BuildInfrastructurePanel();
    }

    static string CategoryName(SurfaceBuildingCategory c)
    {
        switch (c)
        {
            case SurfaceBuildingCategory.Government: return "GOVERNMENT";
            case SurfaceBuildingCategory.Harvesting: return "HARVESTING";
            case SurfaceBuildingCategory.Industry: return "INDUSTRY";
            case SurfaceBuildingCategory.Military: return "MILITARY";
            case SurfaceBuildingCategory.Electrical: return "ELECTRICAL ENGINEERING";
            default: return c.ToString().ToUpper();
        }
    }

    void BuildStructureCard(SurfaceBuildingInfo info)
    {
        {
            var t = info.type;
            var card = Card();
            var group = card.gameObject.AddComponent<CanvasGroup>();
            bool isSel = selected.HasValue && selected.Value == t;

            // Title + a little shape preview so you can see the footprint before you pick it up.
            var titleRow = UIFactory.NewUI(card, "T"); UIFactory.AddLayout(titleRow, 34);
            var h = titleRow.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childAlignment = TextAnchor.MiddleLeft;
            BuildShapePreview(titleRow.transform, t, info.color);
            var nm = UIFactory.Text(titleRow.transform, $"<b>{info.name}</b>" + (isSel ? "  <color=#4DFF6E>(held)</color>" : ""),
                UITheme.SmallSize, info.color, TextAlignmentOptions.Left);
            var nle = nm.gameObject.AddComponent<LayoutElement>(); nle.flexibleWidth = 1;

            Note(card, info.description);

            var meta = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.SubText);
            live.Text(meta, () =>
            {
                int m = ColonyManager.DiscCost(info.costMetal), e = ColonyManager.DiscCost(info.costEnergy);
                bool afford = GameMode.DevMode || PlayerEconomy.CanAfford(m, e);
                string hex = ColorUtility.ToHtmlStringRGB(afford ? UITheme.SubText : UITheme.Bad);
                string idx = info.index == SurfaceIndexKind.None
                    ? "<color=#9FB4C8>terrain doesn't matter</color>"
                    : $"<color=#8FD0FF>{SurfaceIndex.Name(info.index)}</color>";

                // What this thing does to the GRID, said on the card rather than only discovered after
                // it's standing there: what it feeds in, what it takes out, how far it carries power.
                var pw = new System.Text.StringBuilder();
                if (info.energyPerSec > 0f) pw.Append($" · <color=#F5F58C>+{info.energyPerSec:0.0} power</color>");
                if (info.powerDraw > 0f) pw.Append($" · <color=#FFBF4D>-{info.powerDraw:0.0} power</color>");
                if (info.powerRange > 0f) pw.Append($" · <color=#4DC8FF>lights {info.powerRange:0.#}</color>");
                if (info.powerStorage > 0f) pw.Append($" · <color=#4DC8FF>banks {info.powerStorage:0}</color>");

                return $"<color=#{hex}>{m} metal · {e} energy</color> · {info.Cells} tiles · {idx}{pw}";
            });

            var btn = UIFactory.Button(card, "", () =>
            {
                selected = (selected.HasValue && selected.Value == t) ? (SurfaceBuildingType?)null : t;
                rotation = 0;
                CancelPlace();   // picking a different structure abandons the pending question
                lastSig = null;
            }, 24);
            live.Button(btn, () =>
            {
                bool held = selected.HasValue && selected.Value == t;
                if (held) return (true, "Put down");

                // Tech before money: "Needs Fusion Power" is the real answer, and quoting a price for
                // something you couldn't build at any price would send the player off to bank metal for
                // a building that will still refuse them when they get back.
                if (!string.IsNullOrEmpty(info.requiredTech) && !GameMode.DevMode
                    && !TechManager.IsResearched(info.requiredTech))
                {
                    var tech = TechDatabase.Get(info.requiredTech);
                    return (false, $"Needs {(tech != null ? tech.name : info.requiredTech)}");
                }

                int m = ColonyManager.DiscCost(info.costMetal), e = ColonyManager.DiscCost(info.costEnergy);
                bool afford = GameMode.DevMode || PlayerEconomy.CanAfford(m, e);
                return (afford, afford ? "Select" : $"Need {m}m {e}e");
            }, group);
        }
    }

    // What's already down, with upgrade and demolish.
    void BuildPlacedList()
    {
        Header("BUILT HERE");
        var placed = SurfaceBuildManager.On(body);
        if (placed.Count == 0) Note("Nothing built on the surface yet.");
        foreach (var p in new List<PlacedBuilding>(placed))
        {
            var cap = p;
            var card = Card();
            var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
            live.Text(t, () =>
            {
                var info = cap.Info;
                string hex = ColorUtility.ToHtmlStringRGB(SurfaceBuildManager.EfficiencyColor(cap.efficiency));
                string eff = info.index == SurfaceIndexKind.None
                    ? "<color=#9FB4C8>full output</color>"
                    : $"<color=#{hex}>{cap.efficiency * 100f:F0}% — {SurfaceBuildManager.EfficiencyLabel(cap.efficiency)}</color>";
                string adj = SurfaceBuildManager.AdjacencyBonus(body, cap) > 0f
                    ? $"  <color=#F5F58C>+{SurfaceBuildManager.AdjacencyBonus(body, cap) * 100f:F0}% grid</color>" : "";
                return $"<b>{info.name}</b> at ({cap.x},{cap.y})  <size=10>{eff}{adj}</size>";
            });

            // The Colony Ship Base's one job: become a real capitol.
            if (cap.Info.upgradesTo.HasValue)
            {
                var up = UIFactory.Button(card, "", () => { SurfaceBuildManager.Upgrade(body, cap); lastSig = null; }, 22);
                live.Button(up, () =>
                {
                    var info = cap.Info;
                    if (!info.upgradesTo.HasValue) return (false, "—");
                    var target = SurfaceBuildingDatabase.Get(info.upgradesTo.Value);
                    bool can = SurfaceBuildManager.CanUpgrade(body, cap, out string why);
                    int m = ColonyManager.DiscCost(info.upgradeMetal), e = ColonyManager.DiscCost(info.upgradeEnergy);
                    return (can, can ? $"Upgrade to {target.name} ({m}m {e}e)" : $"Upgrade to {target.name} — {why}");
                });
            }

            UIFactory.Button(card, "Demolish (60% back)", () =>
            {
                SurfaceBuildManager.Demolish(body, cap);
                lastSig = null;
            }, 22);
        }
    }

    // A tiny grid drawing of a footprint, so the list reads like a tetris piece tray.
    void BuildShapePreview(Transform parent, SurfaceBuildingType t, Color color)
    {
        var holder = UIFactory.NewUI(parent, "Shape");
        var le = holder.AddComponent<LayoutElement>();
        le.preferredWidth = 34; le.minWidth = 34; le.preferredHeight = 30; le.flexibleWidth = 0;
        var rt = holder.GetComponent<RectTransform>();

        var cells = SurfaceBuildingDatabase.CellsOf(t, 0);
        int maxX = 1, maxY = 1;
        foreach (var c in cells) { maxX = Mathf.Max(maxX, c.x + 1); maxY = Mathf.Max(maxY, c.y + 1); }
        float cell = Mathf.Min(30f / maxX, 26f / maxY);

        foreach (var c in cells)
        {
            var q = UIFactory.Panel(rt, "c", color);
            q.raycastTarget = false;
            var qrt = q.rectTransform;
            qrt.anchorMin = qrt.anchorMax = new Vector2(0, 0);
            qrt.pivot = new Vector2(0, 0);
            qrt.sizeDelta = new Vector2(cell - 1f, cell - 1f);
            qrt.anchoredPosition = new Vector2(c.x * cell, c.y * cell);
        }
    }

    // ---------------- INFRASTRUCTURE ----------------
    // Everything standing on this world: what it is, its tech level, its condition, and how well it was
    // sited. Clicking a row selects that structure and moves the map's ring/arrow onto it — the list
    // half of "select by clicking the map, or the list".
    void BuildInfrastructurePanel()
    {
        Header("BUILT ON THIS WORLD");

        var placed = SurfaceBuildManager.On(body);
        if (placed.Count == 0)
        {
            Note("Nothing built here yet. Use the Build tab to develop the surface.");
            return;
        }

        var summary = UIFactory.WrapText(sidePanel, "", UITheme.SmallSize, UITheme.SubText);
        live.Text(summary, () =>
        {
            int n = SurfaceBuildManager.On(body).Count;
            return $"{n} structure(s) · {SurfaceBuildManager.Density(body) * 100f:F0}% of buildable land developed";
        });

        // Grouped by category so a long list stays navigable, matching the build tray.
        foreach (SurfaceBuildingCategory cat in System.Enum.GetValues(typeof(SurfaceBuildingCategory)))
        {
            bool headerAdded = false;
            foreach (var p in new List<PlacedBuilding>(placed))
            {
                if (p.Info.category != cat) continue;
                if (!headerAdded) { Header(CategoryName(cat)); headerAdded = true; }
                BuildInfraRow(p);
            }
        }
    }

    void BuildInfraRow(PlacedBuilding p)
    {
        var cap = p;
        var card = Card();

        // The card itself is the click target, so the whole row selects — not just a button on it.
        var bg = card.GetComponent<Image>();
        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        var colors = btn.colors;
        colors.normalColor = UITheme.RowBg;
        colors.highlightedColor = UITheme.RowBg;
        colors.pressedColor = UITheme.ButtonActive;
        colors.selectedColor = UITheme.RowBg;
        btn.colors = colors;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(() =>
        {
            SurfaceSelection.Select(body, cap);
            SimpleAudio.Instance?.PlaySelect();
        });

        // Title: colour chip, name, level. Marked when selected so the list agrees with the map.
        var title = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        live.Text(title, () =>
        {
            var info = cap.Info;
            string hex = ColorUtility.ToHtmlStringRGB(info.color);
            string mark = SurfaceSelection.IsSelected(cap) ? "<color=#FFF266>» </color>" : "";
            string lvl = cap.CanUpgrade
                ? $"<color=#9FB4C8>Tech Lv {cap.level}/{PlacedBuilding.MaxLevel}</color>"
                : $"<color=#4DFF6E>Tech Lv {cap.level} (max)</color>";
            return $"{mark}<color=#{hex}>•</color> <b>{info.name}</b>  <size=10>{lvl}</size>";
        });

        // Health bar — real data, not a label.
        Bar(card, () =>
        {
            float f = Mathf.Clamp01(cap.health);
            Color c = f > 0.66f ? UITheme.Good : f > 0.33f ? UITheme.Warn : UITheme.Bad;
            return (f, $"{cap.CurrentHealth}/{cap.MaxHealth} HP", c);
        });

        var stats = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.SubText);
        live.Text(stats, () =>
        {
            var info = cap.Info;
            string site = info.index == SurfaceIndexKind.None
                ? "<color=#9FB4C8>terrain-independent</color>"
                : $"{SurfaceIndex.Name(info.index)} <color=#{ColorUtility.ToHtmlStringRGB(SurfaceBuildManager.EfficiencyColor(cap.efficiency))}>" +
                  $"{cap.efficiency * 100f:F0}% ({SurfaceBuildManager.EfficiencyLabel(cap.efficiency)})</color>";
            string adj = SurfaceBuildManager.AdjacencyBonus(body, cap) > 0f
                ? $" · <color=#F5F58C>+{SurfaceBuildManager.AdjacencyBonus(body, cap) * 100f:F0}% switchyard</color>" : "";

            // Power is now a THIRD multiplier on output, so it has to be visible next to the other two —
            // an Output ×0.35 with no explanation next to it is exactly the kind of unattributed number
            // that sends people reading source code.
            float pf = PowerGrid.PowerFactor(body, cap);
            string power = "";
            if (info.powerDraw > 0f)
            {
                var net = PowerGrid.NetOf(body, cap);
                power = net == null
                    ? " · <color=#FF6659>no grid reaches it</color>"
                    : net.Failed
                        ? $" · <color=#FF6659>Grid {net.index} has no plant</color>"
                        : net.Dead
                            ? $" · <color=#FFBF4D>Grid {net.index} — no plant, on the bank</color>"
                            : net.served >= 0.999f
                                ? $" · <color=#4DFF6E>Grid {net.index}</color>"
                                : $" · <color=#FFBF4D>Grid {net.index} at {net.served * 100f:F0}%</color>";
            }
            return $"({cap.x},{cap.y}) · {site}{adj}{power}\n" +
                   $"<color=#9FB4C8>Output ×{cap.OutputMult * pf:0.00}</color> (siting × tech level" +
                   $"{(cap.health < 0.999f ? " × condition" : "")}" +
                   $"{(info.powerDraw > 0f ? " × power" : "")})";
        });

        // Upgrade + demolish.
        var row = UIFactory.NewUI(card, "Row"); UIFactory.AddLayout(row, 22);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;

        var up = UIFactory.Button(row.transform, "", () => { SurfaceBuildManager.UpgradeLevel(body, cap); lastSig = null; }, 20);
        live.Button(up, () =>
        {
            if (!cap.CanUpgrade) return (false, "Max tech level");
            bool can = SurfaceBuildManager.CanUpgradeLevel(body, cap, out string why);
            SurfaceBuildManager.LevelUpCost(cap, out int m, out int e);
            return (can, can ? $"Upgrade -> Lv{cap.level + 1} ({m}m {e}e)" : $"Upgrade — {why}");
        });

        UIFactory.Button(row.transform, "Demolish", () =>
        {
            SurfaceBuildManager.Demolish(body, cap);
            lastSig = null;
        }, 20);
    }

    // ---------------- POWER ----------------
    // What the electricity on this world actually reaches, and what it's failing to reach.
    //
    // The map answers "where", so this panel answers "how much" and "is it enough" — a balance per grid,
    // the bank each one is carrying, and a list of everything sitting in the dark. It is deliberately a
    // DIAGNOSTIC view and not a second build tray: the plants and relays are placed from the Build tab
    // like everything else, under ELECTRICAL ENGINEERING. Two places to put a building down would mean
    // two copies of the ghost, the confirm and the rotation handling, all of which live on Build.
    //
    // NOTE on the live closures below: PowerGrid derives fresh PowerNet objects every frame, so a
    // captured `net` would be a snapshot that stops updating the moment the frame ends. Every closure
    // captures the grid's INDEX and looks the grid up again — see NetByIndex.
    void BuildPowerPanel()
    {
        Header("POWER GRID");

        var nets = PowerGrid.Nets(body);

        // Gated on GENERATION, not on whether any grid exists. A world with a node on it has a grid —
        // it just has no power in it, and telling someone with a relay standing right there that there
        // is "no grid at all" would send them to build a second relay.
        //
        // Also gated on the BANK: a world generating nothing but still coasting on charged capacitors
        // has a live grid, a draining reserve and a deadline, and the full panel is the only thing that
        // shows any of that. Bailing out to a "go build a plant" note would hide the countdown.
        if (PowerGrid.TotalGeneration(body) <= 0f && PowerGrid.TotalStored(body) <= 0f)
        {
            Note(nets.Count == 0
                ? "<color=#FFBF4D>Nothing on this world makes power.</color> The map is dark because there is no grid on it at all.\n\n" +
                  "Build a generator from the Build tab under <color=#F5F58C>ELECTRICAL ENGINEERING</color> — a Combustion Plant is cheap and " +
                  "will run on almost anything. A plant lights only the ground immediately around itself; <color=#4DC8FF>Power Nodes</color> " +
                  "are what carry that power anywhere else."
                : "<color=#FF6659>This world has a grid but nothing generating on it.</color> Relays carry power; they don't make it, " +
                  "so a chain of nodes with no plant at the end of it is just wire.\n\n" +
                  "Build a generator from the Build tab under <color=#F5F58C>ELECTRICAL ENGINEERING</color> and anything already " +
                  "standing on this grid will pick it up.");
            return;
        }

        // ---- The world's books ----
        var sum = Card();
        var st = UIFactory.WrapText(sum, "", UITheme.SmallSize, UITheme.Text);
        live.Text(st, () =>
        {
            float gen = PowerGrid.TotalGeneration(body), draw = PowerGrid.TotalDraw(body);
            float net = gen - draw;
            string hex = ColorUtility.ToHtmlStringRGB(net >= 0f ? UITheme.Good : UITheme.Bad);
            int grids = PowerGrid.Nets(body).Count;
            return $"<b>{gen:0.0}</b> generated  ·  <b>{draw:0.0}</b> drawn\n" +
                   $"<color=#{hex}><b>{(net >= 0f ? "+" : "")}{net:0.0} per second</b></color>" +
                   $"  <size=10><color=#9FB4C8>across {grids} grid{(grids == 1 ? "" : "s")}</color></size>";
        });

        // Stored vs capacity — the spec's fill bar. Reads "no capacitors" rather than an empty bar when
        // there's nothing to store with, because an empty bar and a missing building look identical.
        Bar(sum, () =>
        {
            float stored = PowerGrid.TotalStored(body), cap = PowerGrid.TotalStorage(body);
            if (cap <= 0f) return (0f, "no capacitors — nothing banked", UITheme.SubText);
            float f = Mathf.Clamp01(stored / cap);
            Color c = f > 0.5f ? UITheme.Good : f > 0.15f ? UITheme.Warn : UITheme.Bad;
            return (f, $"{stored:0} / {cap:0} banked", c);
        });

        var hint = UIFactory.WrapText(sum, "", UITheme.SmallSize, UITheme.SubText);
        live.Text(hint, () =>
        {
            float net = PowerGrid.TotalGeneration(body) - PowerGrid.TotalDraw(body);
            if (net >= 0f) return "<size=10>Surplus tops up the capacitors first, then goes to the empire's stockpile.</size>";
            return "<size=10><color=#FFBF4D>This world is running at a deficit — it is living off its capacitors, " +
                   "and when they empty everything on the short grids browns out.</color></size>";
        });

        // ---- Per grid ----
        // Separate grids are the whole point of the system, so they're listed separately even when
        // there's only one. Merging them into a single planetary total would hide the exact thing the
        // player is here to see: that grid 2 is failing while grid 1 has power to spare.
        Header(nets.Count == 1 ? "THE GRID" : $"{nets.Count} SEPARATE GRIDS");
        if (nets.Count > 1)
            Note("<size=10>These grids are not connected, so a surplus on one cannot help a shortfall on another. " +
                 "Chain <color=#4DC8FF>Power Nodes</color> between them and they become one grid.</size>");

        foreach (var n in nets) BuildGridCard(n.index);

        // ---- What's in the dark ----
        var dark = PowerGrid.Unpowered(body);
        if (dark.Count > 0)
        {
            Header("IN THE DARK");
            Note($"<color=#FF6659>{dark.Count} structure(s) have no working grid.</color> Anything that needs power falls back " +
                 $"on its own back-up plant and runs at <b>{PowerGrid.UnpoweredFactor * 100f:F0}%</b>. Put a generator nearby, or run " +
                 $"<color=#4DC8FF>Power Nodes</color> out to them from a grid that has power to spare.");
            foreach (var p in dark)
            {
                var cap2 = p;
                var card = Card();
                var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
                live.Text(t, () =>
                {
                    var info = cap2.Info;
                    string hex = ColorUtility.ToHtmlStringRGB(info.color);
                    var net = PowerGrid.NetOf(body, cap2);

                    // Three genuinely different faults, and they want three different fixes — so they
                    // are not collapsed into one "unpowered" label. A capacitor draws nothing and can't
                    // be throttled; saying it "wants 0.0" would be nonsense.
                    string fault;
                    if (net == null && info.powerStorage > 0f && info.powerDraw <= 0f)
                        fault = "<color=#FF6659>off the grid — banking nothing</color>";
                    else if (net == null)
                        fault = $"<color=#FF6659>no grid reaches it — wants {info.powerDraw * cap2.LevelMult:0.0}</color>";
                    else
                        fault = $"<color=#FF6659>on Grid {net.index}, which has no plant on it</color>";

                    return $"<color=#{hex}>•</color> <b>{info.name}</b> at ({cap2.x},{cap2.y})  <size=10>{fault}</size>";
                });
            }
        }
    }

    /// Nets are re-derived every frame, so anything that outlives a frame holds the INDEX and re-finds
    /// the grid rather than holding the object.
    PowerNet NetByIndex(int index)
    {
        foreach (var n in PowerGrid.Nets(body)) if (n.index == index) return n;
        return null;
    }

    void BuildGridCard(int index)
    {
        var card = Card();

        var title = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        live.Text(title, () =>
        {
            var n = NetByIndex(index);
            if (n == null) return "<color=#9FB4C8>grid gone</color>";
            string hex = ColorUtility.ToHtmlStringRGB(PowerGrid.SupplyColor(n));
            return $"<b>Grid {n.index}</b>  <size=10><color=#{hex}>{PowerGrid.SupplyLabel(n)}</color></size>";
        });

        // Supply, as a bar. A grid meeting its load sits full and green; one that doesn't shows exactly
        // how far short it is, which is the number that decides whether you need another plant or
        // another capacitor.
        Bar(card, () =>
        {
            var n = NetByIndex(index);
            if (n == null) return (0f, "", UITheme.SubText);
            if (n.draw <= 0.0001f) return (1f, $"{n.generation:0.0} spare · nothing drawing", UITheme.SubText);

            // The bar tracks what the grid can SUSTAIN, not what it happens to be delivering off the
            // bank this second. A grid generating nothing while its capacitors carry the load is at
            // served = 1, and a full green bar reading "0.0 / 8.5 demanded (100%)" is a sentence that
            // reads as a bug — the bank is a countdown, and the bar is where you'd look to see it.
            float f = Mathf.Clamp01(n.Sustainable);
            string bank = n.served > n.Sustainable + 0.001f ? "  <color=#FFBF4D>· on the bank</color>" : "";
            return (f, $"{n.generation:0.0} / {n.draw:0.0} demanded  ({f * 100f:F0}%){bank}", PowerGrid.SupplyColor(n));
        });

        var detail = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.SubText);
        live.Text(detail, () =>
        {
            var n = NetByIndex(index);
            if (n == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append($"{n.generators.Count} plant(s) · {n.projectors.Count} projector(s) · {n.consumers.Count} drawing · ");
            sb.Append($"<color=#F5F58C>{n.coverage.Count} tiles lit</color>");
            if (n.storage > 0f)
                sb.Append($"\n<color=#4DC8FF>{n.Stored:0} / {n.storage:0} banked</color> across {n.capacitors.Count} capacitor(s)");
            else if (n.draw > 0f)
                sb.Append("\n<color=#9FB4C8>No capacitors on this grid — it has no reserve to ride a shortfall out on.</color>");

            // The honest diagnosis, in words. A bar tells you the grid is at 60%; this tells you what to
            // go and do about it, which is the only reason anyone opened this tab.
            if (n.Dead)
            {
                sb.Append("\n<color=#FF6659>Nothing on this grid generates.</color> Relays carry power; they don't make it. " +
                          "Build a plant anywhere this grid reaches and everything on it picks it up at once.");
                if (!n.Failed)
                    sb.Append($" <color=#FFBF4D>It is running on {n.Stored:0} banked — full output until that runs out.</color>");
            }
            else if (n.draw > 0.0001f && n.Sustainable < 0.999f)
            {
                float shortfall = n.draw - n.generation;
                sb.Append($"\n<color=#FF6659>Short by {shortfall:0.0}/s.</color> ");
                sb.Append(n.Stored > 0f
                    ? "<color=#FFBF4D>Running on the bank</color> — it holds until the capacitors empty, and then everything here throttles to match."
                    : "Everything on this grid is throttled to match.");
            }
            return sb.ToString();
        });
    }

    // ---------------- SURVEY ----------------
    // ---------------- SITES (points of interest) ----------------
    //
    // What's actually ON this world: ruins, settlements, rich seams, and the anomalies that are just a
    // "?" until somebody goes and looks. This replaces the separate detailed-map window, which drew a
    // second copy of the same terrain purely to hang these markers on it.
    //
    // What you can KNOW about a site is staged like everything else:
    //   surveyed      — the site exists, and its type. Orbit can see that much.
    //   deep survey   — a research ship on the ground. Some sites are only visible from down there.
    //   researched    — what it actually IS. A Mystery reads "?" until studied; that's the whole point
    //                   of it, and it's the only route into the Ancients tech tree.
    void BuildSitesPanel()
    {
        Header("POINTS OF INTEREST");

        var pois = body.pointsOfInterest;
        if (pois == null || pois.Count == 0)
        {
            Note("Nothing of note found here. A deep survey by a research ship sometimes turns up what an orbital pass missed.");
            return;
        }

        if (!body.deepSurveyed)
            Note("<color=#FFBF4D>Orbital survey only.</color> Send a research ship to study this world on the ground — some sites can't be seen from orbit at all, and anomalies can't be identified from up there.");

        foreach (var poi in pois)
        {
            var cap = poi;
            var card = Card();

            var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
            live.Text(t, () =>
            {
                string hex = ColorUtility.ToHtmlStringRGB(SiteColor(cap));
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"<color=#{hex}><b>{SiteMark(cap)}</b></color>  <b>{SiteTitle(cap)}</b>");
                sb.AppendLine($"<size=10><color=#9FB4C8>{SiteBlurb(cap)}</color></size>");
                if (cap.IsResearchable)
                    sb.AppendLine($"<size=10><color=#8FD0FF>Study: {cap.researchPointCost} pts · ~{cap.researchDuration:F0}s · " +
                                  $"pays {cap.researchReward} pts{(cap.yieldsSchematic ? " · may yield a schematic" : "")}</color></size>");
                return sb.ToString().TrimEnd();
            });

            // Centre the map on it AND light it up. A list entry you cannot find on the map is a list
            // entry; one that takes you there and then flashes the actual ground is a place.
            UIFactory.Button(card, "Show on map", () => FocusSite(cap), 22);

            if (poi.IsResearchable)
            {
                var btn = UIFactory.Button(card, "", () => ResearchTaskManager.Instance?.StartResearch(body, cap), 22);
                live.Button(btn, () =>
                {
                    var rtm = ResearchTaskManager.Instance;
                    if (rtm == null) return (false, "Study — unavailable");
                    if (rtm.IsResearching(cap)) return (false, "Studying…");
                    return rtm.CanStart(body, cap, out string why)
                        ? (true, $"Study this site ({cap.researchPointCost} pts)")
                        : (false, $"Study — {why}");
                });
            }
        }
    }

    // ============================================================================================
    // SITES ON THE GROUND
    //
    // A point of interest has always had a real position (u,v) on the surface, but nothing drew it —
    // the list said a world had ancient ruins and the map showed undifferentiated terrain. These put
    // the site where it actually is: a marked patch of tiles you can see, hover for its report, and
    // jump to from the list.
    //
    // The patch is DERIVED from (u,v) and the site's type rather than stored, so it costs nothing in
    // the save and cannot disagree with the list. Radius by type: a settlement sprawls, an anomaly is
    // a point.
    // ============================================================================================
    RectTransform siteLayer;
    PointOfInterest focusedSite;     // the one currently pulsing, from "Show on map"
    float sitePulseUntil;            // unscaled time the emphasis pulse ends

    /// How many tiles out from its centre a site covers, by kind.
    static int SiteRadius(PointOfInterest p)
    {
        switch (p.type)
        {
            case POIType.Settlement: return 2;        // a town has a footprint
            case POIType.AncientRuins: return 2;      // so does a ruin field
            case POIType.SpecialResource: return 1;   // a seam is tight
            default: return 1;                        // an anomaly is a point
        }
    }

    /// The cells a site covers, clamped to the grid. Longitude wraps; latitude does not.
    List<Vector2Int> SiteCells(PointOfInterest p)
    {
        var cells = new List<Vector2Int>();
        if (body?.surface == null) return cells;

        int w = body.surface.width, h = body.surface.height;
        int cx = Mathf.Clamp(Mathf.FloorToInt(p.u * w), 0, w - 1);
        int cy = Mathf.Clamp(Mathf.FloorToInt(p.v * h), 0, h - 1);
        int r = SiteRadius(p);

        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                // Round patch, not square — a square reads as a building, which is the one thing a
                // natural feature must not look like. `> r*r` and not `> r*r + r`: the looser test
                // admits the diagonals at r=1 (1+1=2), which renders a single-tile anomaly as a full
                // 3x3 block — exactly the shape this is avoiding.
                if (dx * dx + dy * dy > r * r) continue;
                int y = cy + dy;
                if (y < 0 || y >= h) continue;
                cells.Add(new Vector2Int(((cx + dx) % w + w) % w, y));
            }
        return cells;
    }

    /// Jump the map to a site and start it pulsing so the eye lands on it.
    void FocusSite(PointOfInterest p)
    {
        if (p == null) return;
        CentreOn(p.u, p.v);
        focusedSite = p;
        sitePulseUntil = Time.unscaledTime + SitePulseSeconds;
        // No rebuild: the markers already exist and SitePulse reads the focus live every frame. Forcing
        // one here would tear down and rebuild the whole side panel on every "Show on map" click.
    }

    const float SitePulseSeconds = 4f;

    /// Draw every visible site's patch onto the marker layer.
    ///
    /// Only for a world that has been surveyed — before that the player has not been there, and a map
    /// dotted with things they have not found would give away the survey's whole payoff.
    void RefreshSiteMarkers()
    {
        if (siteLayer == null || body?.surface == null) return;

        for (int i = siteLayer.childCount - 1; i >= 0; i--) Destroy(siteLayer.GetChild(i).gameObject);
        if (!body.Surveyed && !GameMode.DevMode) return;
        if (body.pointsOfInterest == null) return;

        int w = body.surface.width, h = body.surface.height;

        foreach (var poi in body.pointsOfInterest)
        {
            // An UNIDENTIFIED anomaly is drawn faintly — you can see something is there, not what.
            // Keyed off `explored`, the same flag SiteTitle and SiteMark use, so the patch and the words
            // next to it can never disagree about how much is known.
            bool known = poi.explored || poi.type != POIType.Mystery;
            var col = SiteColor(poi);

            // ONE holder per site, carrying a CanvasGroup.
            //
            // The pulse is then a single alpha write per site per frame instead of one per TILE. A
            // colour write dirties a Graphic and forces a canvas re-batch, and a couple of ruin fields
            // is already ~40 tiles — re-tinting all of them every frame, forever, on the canvas that
            // also carries the whole side panel, is the kind of cost that does not show up until the
            // map is busy.
            var holderGO = UIFactory.NewUI(siteLayer, $"Site{poi.type}");
            var holder = holderGO.GetComponent<RectTransform>();
            UIFactory.Stretch(holder);
            var group = UIFactory.Ensure<CanvasGroup>(holderGO);

            foreach (var c in SiteCells(poi))
            {
                var go = UIFactory.NewUI(holder, "c");
                var rt = go.GetComponent<RectTransform>();
                // NORMALISED anchors, exactly like AddCellQuad. mapRT's sizeDelta IS the zoom — it is
                // rewritten on every scroll notch — and Signature carries no zoom term, so nothing
                // rebuilds these on a zoom. Pixel offsets captured at build time would keep their old
                // size and collapse toward the map's corner the moment the player scrolled.
                rt.anchorMin = new Vector2(c.x / (float)w, c.y / (float)h);
                rt.anchorMax = new Vector2((c.x + 1) / (float)w, (c.y + 1) / (float)h);
                rt.offsetMin = new Vector2(0.5f, 0.5f);
                rt.offsetMax = new Vector2(-0.5f, -0.5f);

                var img = go.AddComponent<Image>();
                img.color = new Color(col.r, col.g, col.b, known ? 0.55f : 0.35f);
                // Raycast target ON: hovering a site's ground is how you read it, which is the whole
                // point of putting it on the map. Clicks still reach the map's own probe — uGUI walks
                // UP the parent chain to find a handler, which is the same mechanism the wrap mirrors
                // already rely on.
                img.raycastTarget = true;

                go.AddComponent<SiteHover>().Init(this, poi);
            }

            holderGO.AddComponent<SitePulse>().Init(this, poi, group);
        }
    }

    /// Is this site the one the player just asked to be shown, and still within its pulse window?
    public bool IsSitePulsing(PointOfInterest p) =>
        p != null && p == focusedSite && Time.unscaledTime < sitePulseUntil;

    /// The hover text for a site's ground — the same information the list card carries, so the map and
    /// the list can never say different things.
    public string SiteTooltip(PointOfInterest p)
    {
        if (p == null) return "";
        var sb = new System.Text.StringBuilder();
        string hex = ColorUtility.ToHtmlStringRGB(SiteColor(p));
        sb.AppendLine($"<color=#{hex}><b>{SiteTitle(p)}</b></color>");
        sb.AppendLine(SiteBlurb(p));
        if (!p.surveyed && p.type == POIType.Mystery)
            sb.AppendLine("<size=10><color=#FFBF4D>Not yet identified — a research ship must deep-survey this world.</color></size>");
        else if (p.IsResearchable)
            sb.AppendLine($"<size=10><color=#8FD0FF>Study: {p.researchPointCost} pts · ~{p.researchDuration:F0}s</color></size>");
        else if (p.explored)
            sb.AppendLine("<size=10><color=#9FB4C8>Already studied.</color></size>");
        return sb.ToString().TrimEnd();
    }

    /// Scroll the map so a normalized surface position sits in the middle of the viewport.
    void CentreOn(float u, float v)
    {
        if (body?.surface == null || mapRT == null) return;
        // mapPan moves the MAP, so it's the negative of where the point sits relative to the centre.
        mapPan = new Vector2(-(u - 0.5f) * mapRT.rect.width, -(v - 0.5f) * mapRT.rect.height);
        ClampPan();
    }

    static Color SiteColor(PointOfInterest p)
    {
        switch (p.type)
        {
            case POIType.Settlement: return new Color(0.30f, 1f, 0.45f);
            case POIType.AncientRuins: return new Color(0.72f, 0.55f, 1f);
            case POIType.SpecialResource: return new Color(0.56f, 0.82f, 1f);
            default: return p.explored ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 0.82f, 0.30f);
        }
    }

    static string SiteMark(PointOfInterest p)
    {
        switch (p.type)
        {
            case POIType.Settlement: return "C";
            case POIType.AncientRuins: return "R";
            case POIType.SpecialResource: return "M";
            default: return p.explored ? "!" : "?";
        }
    }

    /// The site's name — or the absence of one. An unstudied Mystery is deliberately anonymous.
    static string SiteTitle(PointOfInterest p)
        => p.type == POIType.Mystery && !p.explored ? "Unknown anomaly"
         : p.type == POIType.Mystery ? p.revealTitle
         : p.title;

    static string SiteBlurb(PointOfInterest p)
    {
        if (p.type == POIType.Mystery)
            return p.explored ? p.revealText : "Something is down there. Nothing more is known until it's studied.";
        if (p.type == POIType.SpecialResource)
        {
            string ore = p.relatedOre != OreType.None ? OreDatabase.Get(p.relatedOre).displayName : "an unidentified material";
            bool known = p.relatedOre == OreType.None || ResearchManager.IsDiscovered(p.relatedOre);
            return known ? $"A rich {ore} deposit. {p.description}" : $"A rich seam of something unidentified. {p.description}";
        }
        return p.description;
    }

    // ---------------- TERRAIN (Dev Mode sandbox) ----------------
    //
    // The old free-floating Terrain Controls window, moved in here. It edited the body's shared
    // terrainParams and then had to remember to refresh three different viewers by name — the low-res
    // grid, the detailed map, and the 3D globe. Two of those are gone now, and living inside the map it
    // edits means the result is right there as you drag rather than in another window you have to find.
    // Widened mostly toward the cold end so an actually-frozen world is reachable (at 0.05 the equator
    // itself can't clear ~0.06, so every biome classifier's coldest branch fires everywhere). The hot end
    // was already saturating (SampleNormalized clamps temperature to 0..1) well below the old max in the
    // tropics, so 2.2 barely changes anything there; it does still shrink the polar cold cap a bit
    // further than 2.0 did, so it's not purely inert, just mostly cold-end headroom.
    //
    // NOTE: on a body with active terraforming (body.terraforming == true), TerraformVisuals.Compose
    // clamps heat back to [0.30, 2.20] every tick, so a sandbox value below 0.30 won't stick on a world
    // that's actively terraforming — only on one that isn't.
    const float TempMin = 0.05f, TempMax = 2.2f;

    // The BioSphere slider drives terrainParams.moisture, whose barren floor is 0.3 and whose lush
    // maximum is 2.0. Named because the 0..1 BioSphere ceiling has to be mapped onto that range in three
    // separate places, and three copies of `Lerp(0.3f, 2f, …)` is three chances to disagree.
    const float BioFloor = 0.3f, BioMax = 2f;

    void BuildTerrainPanel()
    {
        Header("TERRAIN SANDBOX");
        Note("<color=#FFBF4D>Dev Mode.</color> Regenerates this world's surface live. Every map reads the same terrainParams, so what you see here is what the world becomes.");

        var p = body.terrainParams;
        SliderRow("Feature scale", "continent size", 0.4f, 3f, p.scale, v => SetTerrain(0, v));
        // Water Level and Relief are now SEPARATE axes. Water drives seaLevel (slot 5) and only floods;
        // Relief drives elevation amplitude (slot 1) and only changes how tall the land is. They used to
        // be the same number, so dragging water up flattened the mountains rather than drowning them.
        SliderRow("Water Level", "dry world <-> even the peaks drowned", 0f, 1f,
            PlanetTerrainGenerator.WaterLevelFromSeaLevel(p.SeaLevelOrNeutral),
            v => SetTerrain(5, PlanetTerrainGenerator.SeaLevelFromWaterLevel(v)));
        SliderRow("Elevation range", "flat world <-> deep basins and high peaks",
            PlanetTerrainGenerator.ElevationMin, PlanetTerrainGenerator.ElevationMax, p.elevation,
            v => SetTerrain(1, v));

        // Dev Mode lets you paint plant life on ANY world (the whole Terrain tab is a Dev sandbox), so the
        // slider is freed there — otherwise it stays gated on a genuinely habitable, biosphere-active world.
        // Without this the range collapses to 0.3..0.3 on a barren world and the handle can't move at all.
        // THE BIOSPHERE CEILING IS REAL EVEN IN DEV MODE.
        //
        // The sandbox used to hand Dev Mode a free 0.3..2.0 range on any world, so you could paint a
        // jungle onto an airless rock. That is now the one slider that is NOT free, because the whole
        // point of the ceiling is that plant life is downstream of water, temperature and air — and a
        // sandbox that ignores its own rule cannot be used to explore the rule.
        //
        // Nothing is actually lost: the Water Level, Temperature and Atmosphere sliders are all right
        // here, and raising them raises this. The ceiling moves when you fix the world, which is the
        // behaviour the spec is describing.
        float bioCeiling01 = BiosphereRules.Ceiling(body);
        float bioCeiling = Mathf.Lerp(BioFloor, BioMax, bioCeiling01);
        bool canGrow = bioCeiling > BioFloor + 0.01f;

        // Open at the world's real state: its moisture if it has a living biosphere, otherwise the barren
        // floor — so the handle position matches what the map is actually showing.
        float bioValue = body.biosphereActive ? Mathf.Min(p.moisture, bioCeiling) : BioFloor;
        SliderRow("BioSphere",
            canGrow ? $"sparse <-> lush plant life  ·  capped at {bioCeiling01 * 100f:F0}%" : "capped — see note below",
            BioFloor, Mathf.Max(bioCeiling, BioFloor + 0.001f), bioValue, v => SetTerrain(2, v));

        if (bioCeiling01 < 0.999f)
            Note($"<color=#FF8F5C>BioSphere capped at {bioCeiling01 * 100f:F0}%:</color> {BiosphereRules.LimitingFactor(body)}. " +
                 $"<size=10><color=#9FB4C8>The ceiling is the average of Water Level ({PlanetTerrainGenerator.WaterLevelFromSeaLevel(p.SeaLevelOrNeutral) * 100f:F0}%) " +
                 $"and how close Temperature is to 1.0 ({BiosphereRules.TemperatureTerm(p.heat) * 100f:F0}%).</color></size>");

        SliderRow("Temperature", "extreme cold <-> extreme heat", TempMin, TempMax, p.heat, v => SetTerrain(3, v));

        // ---- Atmosphere ----
        Header("ATMOSPHERE");
        var air = Card();
        Stat(air, "Atmospheres", () => $"{body.atmospheres:0.#} ({AtmosphereRules.Describe(body)})");
        Stat(air, "Ceiling", () => $"{AtmosphereRules.Ceiling(body):0.#} — mass {body.mass:0.#}" +
                                   (body.hasMagneticField ? "" : ", halved with no magnetic field") +
                                   (body.hasTectonics ? $", +{AtmosphereRules.TectonicBonus(body):0.#} from tectonics" : ""));

        SliderRow("Atmosphere", "vacuum <-> gas-giant deep", 0f, 12f, body.atmospheres, v =>
        {
            body.atmospheres = AtmosphereRules.Quantize(v);
            SetAtmosphere();
        });

        UIFactory.Button(air, body.hasMagneticField ? "Magnetic field: ON — remove it" : "Magnetic field: OFF — give it one", () =>
        {
            body.hasMagneticField = !body.hasMagneticField;
            // Losing the field halves the ceiling, so anything above the new ceiling has to go with it —
            // otherwise the sandbox would show a world holding more air than its own ceiling allows.
            body.atmospheres = Mathf.Min(body.atmospheres, AtmosphereRules.Ceiling(body));
            SetAtmosphere();
        }, 24);

        // Relief. SetTerrain already had a ridge case (index 4) and nothing had ever been wired to it, so
        // the one axis that decides how mountainous a world is was the one axis the sandbox could not
        // touch. Ridge is the mountain-building field: the classifiers test it directly (`ridge > 0.8` is
        // Mountains on most world types), so raising it converts more of the map to peaks and lowering it
        // flattens the world toward plains.
        SliderRow("Ruggedness", "smooth ground <-> broken, jagged mountain country", 0.3f, 2.5f, p.ridge, v => SetTerrain(4, v));

        Header("SEED");
        var seed = Card();
        Stat(seed, "Terrain seed", () => $"{body.terrainSeed:F0}");
        UIFactory.Button(seed, "Randomize (new world, same settings)", () =>
        {
            body.terrainSeed = Random.Range(0f, 10000f);
            RegenerateTerrain();
        }, 24);

        UIFactory.Button(sidePanel, "Reset to default", () =>
        {
            // Restore the world as it was GENERATED — its original terrain seed and the per-world
            // natural params — not the flat NoiseParams.Default. This is the way back to the planet you
            // started with after rerolling the seed above or dragging the sliders.
            body.terrainSeed = body.naturalSeed;
            body.terrainParams = body.naturalParams;
            RegenerateTerrain();
            lastSig = null;    // rebuild so the sliders and seed readout snap back to the restored values
        }, 26);
    }

    void SliderRow(string label, string hint, float min, float max, float value, System.Action<float> onChanged)
    {
        var card = Card();
        UIFactory.WrapText(card, $"<b>{label}</b>  <size=10><color=#9FB4C8>{hint}</color></size>", UITheme.SmallSize, UITheme.Text);
        UIFactory.LabeledSlider(card, "", min, max, value, onChanged, "F2", 34f);
    }

    // 0=scale 1=elevation(Water Level) 2=moisture(BioSphere) 3=heat 4=ridge
    void SetTerrain(int which, float v)
    {
        if (body == null) return;
        var p = body.terrainParams;
        switch (which)
        {
            case 0: p.scale = v; break;
            case 1: p.elevation = v; break;
            // In the Dev sandbox the BioSphere slider paints plant life directly: dragging it above the
            // floor switches the world's biosphere ON (which lifts the generator's no-biosphere moisture
            // floor in SampleNormalized so plants actually appear), and dragging back to the floor switches
            // it OFF (barren). Outside Dev Mode the biosphere gates stand and moisture stays capped at the
            // floor. Re-checked here (not just at the slider's max bound) so a value set before conditions
            // changed can't linger above the ceiling once something else moves it.
            case 2:
            {
                // Clamped to the LIVE ceiling, recomputed here rather than trusting the slider's bound —
                // a value set while the world was wet and temperate must not survive dragging the
                // temperature to 2.0 afterwards. This is the one place that guarantee can be made, since
                // every other slider routes through this same method.
                float ceiling = Mathf.Lerp(BioFloor, BioMax, BiosphereRules.Ceiling(body));
                p.moisture = Mathf.Clamp(v, BioFloor, Mathf.Max(BioFloor, ceiling));
                if (GameMode.DevMode) body.biosphereActive = p.moisture > BioFloor + 0.01f;
                break;
            }
            case 3: p.heat = v; break;
            case 4: p.ridge = v; break;
            // Sea level — the height the water sits at, independent of how tall the land is.
            case 5: p.seaLevel = Mathf.Clamp01(v); break;
        }
        body.terrainParams = p;

        // ONLY THE SLIDERS THAT FEED THE CEILING RE-CLAMP IT.
        //
        // The ceiling is a function of water level and temperature, so dragging the world hotter or drier
        // has to take the plant life down with it. Ruggedness and Feature scale are NOT inputs to it, and
        // running the clamp for them was actively harmful: no world's generated moisture had ever been
        // checked against a ceiling that did not exist until now, so nudging Ruggedness by one pixel
        // could silently strip a world's vegetation on an axis the player never touched — irreversibly,
        // since dragging it back does not restore the old moisture.
        //
        // Case 2 does its own clamp against the same value, so it is not listed here.
        if (which == 3 || which == 5) ClampBiosphereToCeiling();

        RegenerateTerrain();
    }

    /// Everything that has to happen after the sandbox changes a world's AIR.
    ///
    /// The atmosphere controls sit outside SetTerrain, so they were bypassing all of its consequences.
    /// Air is not a cosmetic number: it multiplies the biosphere ceiling, and below 0.6 atmospheres it
    /// takes the surface water with it. Dragging Atmosphere to zero used to leave a jungle standing on a
    /// drowned world in a hard vacuum, with the BioSphere slider's own "capped at N%" note still
    /// reporting the old figure, because nothing rebuilt the panel either.
    void SetAtmosphere()
    {
        AtmosphereRules.ApplyWaterLoss(body);   // thin air boils the oceans off first
        ClampBiosphereToCeiling();              // then whatever was living in them
        RegenerateTerrain();
        Rebuild();                              // the cap note and the slider bounds both moved
    }

    /// Pull plant life back down to whatever the world can currently support.
    ///
    /// Shared because three different things move the ceiling — temperature, water level, and atmosphere
    /// — and the atmosphere controls live in a different method entirely. When this only existed inline
    /// in SetTerrain, dragging Atmosphere from 3 to 0 left a jungle standing in a hard vacuum: the exact
    /// thing the ceiling was added to prevent, reachable from the slider directly above it.
    void ClampBiosphereToCeiling()
    {
        if (body == null) return;

        float cap = Mathf.Lerp(BioFloor, BioMax, BiosphereRules.Ceiling(body));
        var p = body.terrainParams;
        if (p.moisture <= cap) return;

        p.moisture = Mathf.Max(BioFloor, cap);
        body.terrainParams = p;

        // Cleared regardless of Dev Mode. The old guard only touched the flag inside the Dev arm, so a
        // world could end up sitting at the barren moisture floor with biosphereActive still true —
        // reported as living, rendered as dead.
        if (p.moisture <= BioFloor + 0.01f) body.biosphereActive = false;
    }

    void RegenerateTerrain()
    {
        if (body == null) return;
        body.surface = PlanetTerrainGenerator.GenerateSurface(body);
        OreGenerator.Populate(body);

        // Every derived read of the surface has to be dropped, or the map shows a new world scored
        // against the old one's statistics.
        SurfaceIndex.InvalidateStats(body);

        RefreshMapTexture();                                   // this window's map
        PlanetAppearance.RefreshTexture(body, body.visualObject);   // and the globe in space
    }

    void BuildSurveyPanel()
    {
        // Folded from the retired Inspector body window (Raptok's mapping: Climate, Ores, Terraform all
        // land on the Survey tab). Climate first (what the world IS), then its ores, then how to fix it,
        // then Sites (points of interest), then the index overlays and the power grid.
        BuildSurveyClimate();
        BuildSurveyOres();
        BuildSurveyTerraform();
        // Points of interest — folded from the retired Sites tab, which required a survey to reveal what's
        // on the world. Keep that gate now that Survey itself is always open.
        if (body.Surveyed || GameMode.DevMode) BuildSitesPanel();

        Header("INDEX OVERLAYS");
        Note("Each overlay paints the grid with where a kind of building actually belongs. Survey a world to read its minerals; a deep survey by a research ship unlocks the rest.");

        AddIndexToggle(SurfaceIndexKind.None, "None (plain terrain)");
        foreach (var k in SurfaceIndex.All) AddIndexToggle(k, null);

        // The power grid — folded from the retired Power tab. Its map overlay is a Survey overlay now,
        // reachable from this toggle (mutually exclusive with the index ramps); the diagnostic panel is
        // shown below for a world of yours that's settled, since a grid only exists once something's built.
        AddPowerToggle();
        AddTectonicsToggle();
        if (body.owner == FactionManager.Player && body.settled)
            BuildPowerPanel();

        // Read the exact numbers under the cursor — the overlay shows you the region, this confirms it.
        Header("UNDER THE CURSOR");
        var card = Card();
        var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        // Reserve enough height for the FULLY-populated readout (tile position/type, ore, and one line per
        // index) up front, so this section always occupies its space in the scroll list. Otherwise it's an
        // empty one-liner until you hover a tile, at which point it suddenly grows and pushes the real
        // content down BELOW the visible area — where you can't tell there's anything to scroll to. Reserving
        // it makes the scroll range include it at all times, so it's reachable the moment it fills in.
        int reservedLines = System.Enum.GetValues(typeof(SurfaceIndexKind)).Length + 2;  // indexes + pos/type + ore
        t.gameObject.AddComponent<LayoutElement>().minHeight = reservedLines * 16f;
        live.Text(t, () =>
        {
            if (hoverCell.x < 0 || body.surface == null) return "<color=#9FB4C8>Hover the map.</color>";
            var tile = body.surface.tiles[hoverCell.x, hoverCell.y];
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>({hoverCell.x}, {hoverCell.y})</b> — {tile.type}");
            if (tile.HasOre && ResearchManager.IsDiscovered(tile.ore))
                sb.AppendLine($"<color=#8FD0FF>Ore: {OreDatabase.Get(tile.ore).displayName}</color> ({tile.oreRichness * 100f:F0}% rich)");
            foreach (SurfaceIndexKind k in System.Enum.GetValues(typeof(SurfaceIndexKind)))
            {
                if (k == SurfaceIndexKind.None) continue;
                if (!SurfaceIndex.Unlocked(body, k)) { sb.AppendLine($"<color=#5A6A7A>{SurfaceIndex.Name(k)}: locked</color>"); continue; }
                float v = SurfaceIndex.Get(body, k, hoverCell.x, hoverCell.y);
                string hex = ColorUtility.ToHtmlStringRGB(SurfaceIndex.Ramp(k, v));
                sb.AppendLine($"{SurfaceIndex.Name(k)}: <color=#{hex}><b>{v * 100f:F0}%</b></color>");
            }
            return sb.ToString();
        });
    }

    void AddIndexToggle(SurfaceIndexKind k, string labelOverride)
    {
        var card = Card();
        var group = card.gameObject.AddComponent<CanvasGroup>();

        if (k != SurfaceIndexKind.None)
        {
            // A colour strip showing the ramp, so the legend IS the ramp.
            var strip = UIFactory.NewUI(card, "Ramp"); UIFactory.AddLayout(strip, 10);
            var srt = strip.GetComponent<RectTransform>();
            for (int i = 0; i < 10; i++)
            {
                var q = UIFactory.Panel(srt, "s", SurfaceIndex.Ramp(k, i / 9f));
                q.raycastTarget = false;
                var qrt = q.rectTransform;
                qrt.anchorMin = new Vector2(i / 10f, 0); qrt.anchorMax = new Vector2((i + 1) / 10f, 1);
                qrt.offsetMin = Vector2.zero; qrt.offsetMax = Vector2.zero;
            }
            Note(card, SurfaceIndex.Describe(k));
        }

        // Picking an index no longer switches the grid off — they draw on separate layers now, and the
        // exclusivity was asymmetric anyway: index-then-power worked, power-then-index killed the grid
        // with nothing on screen to explain why.
        var btn = UIFactory.Button(card, "", () => { activeIndex = k; showTectonicsOverlay = false; lastSig = null; }, 24);
        live.Button(btn, () =>
        {
            bool on = activeIndex == k && !showTectonicsOverlay;   // an index and the grid can both be up
            string nm = labelOverride ?? SurfaceIndex.Name(k);
            if (k == SurfaceIndexKind.None) return (true, on ? $"• {nm}" : nm);
            if (!SurfaceIndex.Unlocked(body, k)) return (false, $"{nm} — {SurfaceIndex.LockReason(body, k)}");
            return (true, on ? $"• {nm} (showing)" : $"Show {nm}");
        }, group);
    }

    // The power-grid overlay toggle — the Power tab's map view, now one of the Survey overlays. Picking
    // it clears any index ramp; picking an index clears it. Only meaningful on a settled world of yours,
    // so it disables itself elsewhere with the reason on the button.
    void AddPowerToggle()
    {
        var card = Card();
        var group = card.gameObject.AddComponent<CanvasGroup>();
        Note(card, "<color=#F5F58C>■</color> grid   <color=#4DC8FF>■</color> plants & relays — where the electricity reaches.");
        var btn = UIFactory.Button(card, "", () =>
        {
            showPowerOverlay = !showPowerOverlay;
            // The power grid no longer clears the chosen index — it has its own layer above the pieces
            // and the two are legible together. Tectonics still takes the whole map, so it still yields.
            if (showPowerOverlay) showTectonicsOverlay = false;
            lastSig = null;
        }, 24);
        live.Button(btn, () =>
        {
            if (body.owner != FactionManager.Player || !body.settled)
                return (false, "Power grid — settle this world first");
            return (true, showPowerOverlay ? "• Power grid (showing)" : "Show power grid");
        }, group);
    }

    // The plate-tectonics overlay toggle — a Survey overlay like the power grid, not an index ramp. Only
    // meaningful on a world that actually HAS active plates (hasTectonics is known from a survey), so it
    // disables itself with the reason on the button everywhere else. Picking it clears any index ramp and
    // the power overlay; picking either of those clears it.
    void AddTectonicsToggle()
    {
        var card = Card();
        var group = card.gameObject.AddComponent<CanvasGroup>();
        // No ➤ (U+27A4) here: the runtime font (LiberationSans SDF) has no glyph for it, so it rendered as
        // a tofu box and spammed a TMP warning every time this card drew. Describe the arrows in words.
        Note(card, "<color=#DCE2EA>■</color> plates   <color=#F22B2B>■</color> fault lines   <color=#F22B2B>red arrows</color> = push direction & strength — where quakes, mountains and volcanoes cluster.");
        var btn = UIFactory.Button(card, "", () =>
        {
            showTectonicsOverlay = !showTectonicsOverlay;
            if (showTectonicsOverlay) { activeIndex = SurfaceIndexKind.None; showPowerOverlay = false; }
            lastSig = null;
        }, 24);
        live.Button(btn, () =>
        {
            if (!TectonicsMap.Active(body)) return (false, "Tectonics — this world has no active plate tectonics");
            if (!body.Surveyed) return (false, "Tectonics — survey this world first");
            return (true, showTectonicsOverlay ? "• Tectonics (showing)" : "Show tectonics");
        }, group);
    }

    // ---- Folded from the Inspector body window (Climate / Ores / Terraform) ----

    // The world as a PLACE: what the sky looks like, how long a day is, and how well it fits your species.
    void BuildSurveyClimate()
    {
        var b = body;
        var s = SpeciesManager.Current;

        Header("THE WORLD");
        var card = Card();
        UIFactory.WrapText(card, ClimateProse(b, s), UITheme.SmallSize, UITheme.Text);

        Header("STARLIGHT & ORBIT");
        var orbit = Card();
        Stat(orbit, "Distance from star", () => $"{b.distanceFromStar:F1} units");
        Stat(orbit, "Orbital radius", () => $"{b.orbitRadius:F1}");
        Stat(orbit, "Year", () => $"{OrbitalMechanics.PeriodSeconds(b.orbitSpeed):F1}s");
        Stat(orbit, "Eccentricity", () => $"{b.eccentricity:F2}");
        Stat(orbit, "Average Temperature", () =>
        {
            float c = PlanetTemperature.BodyAverageCelsius(b);
            string hex = ColorUtility.ToHtmlStringRGB(PlanetTemperature.GradientColor(c));
            return $"<color=#{hex}>{PlanetTemperature.Label(c)}</color>";
        });
        Stat(orbit, "Axial tilt", () => $"{b.inclination:F0}°" + (Mathf.Abs(b.inclination) > 28f ? "  <color=#FFBF4D>(severe seasons)</color>" : ""));
        Stat(orbit, "Day length", () =>
        {
            float spin = Mathf.Abs(b.spinSpeed);
            if (spin < 3f) return $"{spin:F1}°/s  <color=#FFBF4D>(near tidally locked)</color>";
            if (spin > 45f) return $"{spin:F1}°/s  <color=#FFBF4D>(violently fast)</color>";
            return $"{spin:F1}°/s";
        });

        if (b.hostStar != null)
        {
            Header("HOST STAR");
            var star = Card();
            Stat(star, "Temperature", () => $"{b.hostStar.temperatureK:F0} K");
            Stat(star, "Luminosity", () => $"{b.hostStar.luminosity:F2}×");
            Stat(star, "Habitable zone", () =>
                Habitability.GetZone(b.hostStar, s, out float inner, out float outer)
                    ? $"{inner:F1} – {outer:F1} for {s.name}" + (b.distanceFromStar >= inner && b.distanceFromStar <= outer
                        ? "  <color=#4DFF6E>(this world is inside it)</color>"
                        : "  <color=#FFBF4D>(this world is outside it)</color>")
                    : "none — this star has no habitable band");
        }

        Header("HOW YOUR SPECIES SEES IT");
        var spec = Card();
        Note(spec, $"{s.name}: {s.habitat}");
        Stat(spec, "Affinity for this world type", () =>
        {
            float a = s.Affinity(b.type);
            string hex = Habitability.ScoreColorHex(a * 100f);
            return $"<color={hex}>{a * 100f:F0}%</color>" +
                   (a < Habitability.HabitableAffinity ? "  <color=#FFBF4D>— the wrong kind of world for them</color>" : "");
        });
        Note(spec, $"They would rather be on a {TerraformDiagnosis.Pretty(s.BestType())}. " +
                   (s.PrefersDry ? "They need it dry." : "They need liquid water."));
    }

    // A readable paragraph — what it would actually be like to stand here. Ported from the Inspector.
    static string ClimateProse(CelestialBody b, Species s)
    {
        var parts = new List<string>();

        switch (b.type)
        {
            case CelestialBodyType.OceanPlanet: parts.Add("A world of open ocean, broken only by island chains and storm fronts."); break;
            case CelestialBodyType.IcePlanet: parts.Add("A frozen world. Its water is all here — locked in glaciers kilometres deep."); break;
            case CelestialBodyType.VolcanicPlanet: parts.Add("A furnace world of magma fields and ash skies, lit from below as much as above."); break;
            case CelestialBodyType.BarrenPlanet: parts.Add("A dead rock. No air, no water, no magnetic field — just dust and hard radiation."); break;
            case CelestialBodyType.RockyPlanet: parts.Add("A rocky world with real ground underfoot and weather worth the name."); break;
            case CelestialBodyType.GasGiant: parts.Add("A gas giant: banded storms the size of continents, and no surface to stand on at all."); break;
            case CelestialBodyType.Moon: parts.Add("A moon, locked to its primary."); break;
            default: parts.Add("A small body."); break;
        }

        if (b.hostStar != null && Habitability.GetZone(b.hostStar, s, out float inner, out float outer))
        {
            if (b.distanceFromStar < inner) parts.Add($"It sits closer to its star than {s.name} can comfortably bear — too much light, too much heat.");
            else if (b.distanceFromStar > outer) parts.Add($"It orbits out beyond the light {s.name} needs; the sun here is a bright star and little more.");
            else parts.Add($"It sits squarely in the band {s.name} can live in.");
        }

        // Describe the water actually on the surface (its Water Level), not the disconnected Water resource
        // number, and call out frozen water as frozen rather than absent.
        float surfaceWater = PlanetTerrainGenerator.WaterLevelFromSeaLevel(b.terrainParams.SeaLevelOrNeutral);
        if (surfaceWater < 0.15f) parts.Add("There is essentially no water on the surface.");
        else if (!BiosphereRules.HasLiquidWaterClimate(b)) parts.Add("Its water is all here — but frozen solid.");
        else if (surfaceWater > 0.6f) parts.Add("Water is abundant — arguably too abundant.");
        else parts.Add("There is some liquid water.");

        if (b.surfaceSize <= 4) parts.Add("It is small enough that gravity is a suggestion and any atmosphere drifts away.");
        else if (b.surfaceSize >= 14) parts.Add("It is massive, and holds a deep, heavy atmosphere.");

        return string.Join(" ", parts);
    }

    // What is in the ground, and whether you have researched it. Discovering happens on survey; the Codex
    // is where an ore's uses are unlocked. Gated on the survey state, since orbit can't read the seams.
    void BuildSurveyOres()
    {
        var b = body;

        Header("MINERAL SURVEY");
        if (!b.Surveyed) { Note("Survey this world to reveal the ore deposits in its crust."); return; }

        var ores = OreGenerator.OresOnBody(b);
        if (ores.Count == 0) { Note("No ore deposits were found on this world."); return; }

        Note($"{ores.Count} ore type(s) present. Surveying DISCOVERS an ore; researching it in the Codex unlocks its uses.");

        foreach (var ore in ores)
        {
            var captured = ore;
            var info = OreDatabase.Get(ore);
            var card = Card();
            var title = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
            live.Text(title, () =>
            {
                bool k = ResearchManager.IsDiscovered(captured);
                bool r = ResearchManager.IsResearched(captured);
                string state = r ? "<color=#4DFF6E>researched</color>"
                             : k ? "<color=#FFBF4D>discovered — not yet researched</color>"
                                 : "<color=#9FB4C8>undiscovered</color>";
                return $"<b>{(k ? info.displayName : "??? — unidentified")}</b>  <size=10>Tier {info.tier} · {info.baseValue}cr · {state}</size>";
            });

            if (!ResearchManager.IsDiscovered(ore)) { Note(card, "Click its deposits on the surface map, or survey this world, to identify it."); continue; }
            Note(card, info.description);
            if (ResearchManager.IsResearched(ore))
            {
                UIFactory.WrapText(card, $"<color=#8FD0FF>Uses:</color> {info.uses}", UITheme.SmallSize, UITheme.Text);
                UIFactory.WrapText(card, $"<color=#FFBF4D>Refining:</color> {info.refining}", UITheme.SmallSize, UITheme.Text);
            }
            else
            {
                var btn = UIFactory.Button(card, "", () => ResearchManager.Research(captured), 24);
                live.Button(btn, () =>
                {
                    bool can = ResearchManager.CanResearch(captured);
                    return (can, can ? $"Research {info.displayName} ({info.researchCost} pts)"
                                     : $"Research — need {info.researchCost} pts (have {ResearchManager.ResearchPoints})");
                });
            }
        }
    }

    // What is wrong with this world for your species, its habitability ceiling, and the road to fixing it:
    // a live terraform toggle, the fault list, and a link to the full projects console.
    void BuildSurveyTerraform()
    {
        var b = body;
        var s = SpeciesManager.Current;

        // The Inspector hid its Terraform tab for gas giants — there is no surface to terraform.
        if (b.type == CelestialBodyType.GasGiant) return;

        Header("HABITABILITY CEILING");
        var card = Card();
        var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () =>
        {
            float now = b.habitability, ceiling = Colony.TerraformCeiling(b);
            float reach = TerraformProjects.ReachableCeiling(b, s), pot = TerraformProjects.PotentialCeiling(b, s);
            return $"Now <color={Habitability.ScoreColorHex(now)}><b>{now:F0}%</b></color>  ->  " +
                   $"ceiling today <color={Habitability.ScoreColorHex(ceiling)}><b>{ceiling:F0}%</b></color>  ->  " +
                   $"with researched projects <color={Habitability.ScoreColorHex(reach)}><b>{reach:F0}%</b></color>  ->  " +
                   $"with all known science <color={Habitability.ScoreColorHex(pot)}><b>{pot:F0}%</b></color>\n" +
                   $"<color=#9FB4C8>Colonizable at {Colony.FoundThreshold:F0}%.</color>";
        });

        Bar(sidePanel, () => (b.habitability / 100f, $"{b.habitability:F0}% habitable", Habitability.ScoreColor(b.habitability)));

        var mgr = ColonyManager.Instance;
        if (mgr != null)
        {
            var tf = UIFactory.Button(sidePanel, "", () => { mgr.ToggleTerraform(b); lastSig = null; }, 26);
            live.Button(tf, () =>
            {
                if (b.habitability >= Colony.FoundThreshold && !b.terraforming) return (false, "Already habitable");
                if (!Colony.CanReachLivable(b) && !b.terraforming) return (false, $"Can't be made livable for {s.name} — run projects below first");
                return (true, b.terraforming ? "Stop terraforming" : "Start terraforming (consumes water, energy, metal)");
            });
        }

        Header("WHAT IS WRONG WITH THIS WORLD");
        var issues = TerraformDiagnosis.Analyze(b, s);
        if (issues.Count == 0) UIFactory.WrapText(sidePanel, $"<color=#4DFF6E>Nothing — this world already suits {s.name}.</color>", UITheme.SmallSize, UITheme.Good);
        foreach (var i in issues)
        {
            var ic = Card();
            string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(UITheme.Warn, UITheme.Bad, i.severity));
            UIFactory.WrapText(ic, $"<b><color=#{hex}>{TerraformDiagnosis.Describe(i.problem)}</color></b>  <size=10><color=#9FB4C8>severity {i.severity * 100f:F0}%</color></size>",
                UITheme.SmallSize, UITheme.Text);
            Note(ic, i.detail);
        }

        Header("PROJECTS");
        Note("Projects raise this world's ceiling permanently. The full console has costs, durations and progress.");
        UIFactory.Button(sidePanel, "Open Terraforming Console »", () => TerraformWindow.Instance?.ShowFor(b), 26);
    }

    // ---- Overlay texture ----
    // One point-filtered texture the size of the grid, stretched over the map. A cell per texel means
    // the overlay lines up with the build grid exactly and costs nothing to redraw.
    // The best fraction of a world highlighted when you pick a building up. Relative to THIS world, so
    // a frozen planet still shows you its ten hottest tiles — you just also get told they're poor.
    const float BestSitesFraction = 0.10f;

    void RefreshOverlay()
    {
        // The plate arrows belong to the tectonics overlay only — clear them up front so every other path
        // (index ramp, power, build, nothing) leaves none behind; the tectonics branch redraws them.
        ClearPlateArrows();

        // The power layer is resolved FIRST, before any branch can early-return, because it is now
        // independent of whatever the ground layer is doing. Left to the branches, a path that returned
        // early — tectonics, or an unsurveyed world — would strand the grid on screen from the last
        // repaint.
        bool wantPower = PowerOverlayActive && body != null && body.surface != null;
        if (powerOverlayImage != null)
        {
            powerOverlayImage.gameObject.SetActive(wantPower);
            if (wantPower) RefreshPowerOverlay();
        }

        // Two different overlays share one texture:
        //  BUILD  — holding a structure highlights the best 10% of sites for it on this world.
        //  SURVEY — the raw index ramp.
        // The power grid and the tectonics map are each their own overlay entirely: not a ramp over an
        // index but a purpose-drawn map (what the electricity reaches; where the plates and faults are), so
        // each gets its own pass rather than being forced through Ramp(). Both are Survey overlays chosen
        // by their toggle rather than a dedicated tab.
        if (tab == Tab.Survey && showTectonicsOverlay && body.surface != null && body.Surveyed && TectonicsMap.Active(body))
        {
            overlayImage.gameObject.SetActive(true);
            SetOverlayBelowPieces();   // ground, not grid — buildings stand on top of it
            RefreshTectonicsOverlay();
            DrawPlateArrows();
            return;
        }

        // ============================================================================================
        // POWER IS NO LONGER EXCLUSIVE WITH THE GROUND MAPS
        //
        // These used to be an if/else chain, and the comment here explained which building lost: the
        // Combustion Plant is Electrical (so it raised the power overlay) but is sited on a SEAM (so its
        // index is Mineral), and only one could win. The same bind caught every building that needs both
        // a good site and a grid connection — a farm on fertile ground, a wind or solar array where the
        // weather is right AND within reach of something to plug into.
        //
        // Now the power grid draws on its own layer, so it is answered INDEPENDENTLY of whatever ground
        // map is up. Both questions are live at once, which is what siting one of these actually is.
        // ============================================================================================
        // (The power layer was already resolved at the top of this method — see the note there.)

        // The ground overlay always sits below the pieces now — the power layer is the only thing that
        // ever needed to be above them, and it has its own.
        SetOverlayBelowPieces();

        if (MineralOverlayActive && body.surface != null)
        {
            overlayImage.gameObject.SetActive(true);
            RefreshMineralOverlay();
            return;
        }

        var kind = SurfaceIndexKind.None;
        bool bestSites = false;

        if (tab == Tab.Build && selected.HasValue)
        {
            var info = SurfaceBuildingDatabase.Get(selected.Value);
            if (info.index != SurfaceIndexKind.None && SurfaceIndex.Unlocked(body, info.index))
            { kind = info.index; bestSites = true; }
        }
        else if (tab == Tab.Survey && SurfaceIndex.Unlocked(body, activeIndex))   // power no longer excludes it — separate layer
        {
            kind = activeIndex;
        }

        bool show = kind != SurfaceIndexKind.None && body.surface != null;
        overlayImage.gameObject.SetActive(show);
        if (!show) return;

        if (bestSites) { RefreshBestSitesOverlay(kind); return; }

        int w = body.surface.width, h = body.surface.height;
        EnsureOverlayTex(w, h);

        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = SurfaceIndex.Ramp(kind, SurfaceIndex.Get(body, kind, x, y));
        overlayTex.SetPixels(px);
        overlayTex.Apply();
        overlayImage.texture = overlayTex;
    }

    /// Park the ground overlay at the very bottom of the map stack.
    ///
    /// This used to take an `above` flag, back when ONE RawImage served every overlay and the grid had to
    /// climb over the buildings while the ground indexes stayed under them. The grid has its own layer
    /// now (`powerOverlayImage`), so this one is always ground and always belongs at the bottom — and the
    /// flag had become a landmine: with the power layer inserted, the old `above` arithmetic would have
    /// landed the ground map ON TOP of the grid, the exact inversion the second layer exists to prevent.
    ///
    /// Index 0 rather than "just below the pieces": the site markers live between the two, and aiming at
    /// the piece layer used to leave the overlay tinting over them.
    void SetOverlayBelowPieces()
    {
        if (overlayImage == null) return;
        var rt = overlayImage.rectTransform;
        if (rt.GetSiblingIndex() != 0) rt.SetSiblingIndex(0);
    }

    // THE MINERAL INDEX — the prospecting map, and the only place named ore deposits are ever drawn.
    //
    // Two things stacked on one texture, because they answer two halves of the same question:
    //   * the index RAMP underneath — how mineral-bearing the ground is generally (ridges, elevation,
    //     metallic crust), which is what tells you where it is worth looking at all;
    //   * the named DEPOSITS on top — Ferralite, Cuprion, Aurelium and the rest, drawn in each ore's own
    //     colour at full strength so a seam reads as a distinct find rather than a slightly warmer patch.
    //
    // Richness drives the deposit's alpha, so a thin showing looks like one and a mother lode is
    // unmistakable. That is the informed decision the overlay exists to support: not just "is there ore
    // here" but "is there enough of it, and which one is it".
    void RefreshMineralOverlay()
    {
        int w = body.surface.width, h = body.surface.height;
        EnsureOverlayTex(w, h);

        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = SurfaceIndex.Ramp(SurfaceIndexKind.Mineral,
                                            SurfaceIndex.Get(body, SurfaceIndexKind.Mineral, x, y));

                var tile = body.surface.tiles[x, y];
                if (tile != null && tile.HasOre)
                {
                    var oc = OreDatabase.Get(tile.ore).color;
                    // Floor of 0.55 so even a poor seam is legible; a rich one reaches near-opaque.
                    c = new Color(oc.r, oc.g, oc.b, Mathf.Lerp(0.55f, 0.95f, tile.oreRichness));
                }

                px[y * w + x] = c;
            }

        overlayTex.SetPixels(px);
        overlayTex.Apply();
        overlayImage.texture = overlayTex;
    }

    // "Where should this go?" — paints THIS WORLD'S best sites for the held structure.
    //
    // Deliberately relative (a percentile of this planet's own distribution) rather than an absolute
    // threshold: on a cold world every geothermal site is poor, and an absolute cutoff would highlight
    // nothing and tell you nothing. You always get to see where the best ground IS. Whether it's any
    // good is a separate question the hover readout answers honestly, and tiles that can't meet the
    // building's hard requirement are marked as refusals rather than sites.
    void RefreshBestSitesOverlay(SurfaceIndexKind kind)
    {
        int w = body.surface.width, h = body.surface.height;
        EnsureOverlayTex(w, h);

        var info = SurfaceBuildingDatabase.Get(selected.Value);
        float cut = SurfaceIndex.TopFractionThreshold(body, kind, BestSitesFraction);

        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = SurfaceIndex.Get(body, kind, x, y);
                bool buildable = info.minIndex <= 0f || v >= info.minIndex;
                Color c;

                if (v >= cut && buildable)
                {
                    // The prize: this world's best ground for this thing. Hot pink-red, unmissable.
                    c = new Color(1f, 0.15f, 0.35f, 0.85f);
                }
                else if (v >= cut)
                {
                    // Best available, but still below the hard floor — this world can't support one.
                    c = new Color(0.55f, 0.1f, 0.15f, 0.5f);
                }
                else
                {
                    // Everything else, dimly ramped so you can still read the gradient toward the good bits.
                    c = SurfaceIndex.Ramp(kind, v);
                    c.a *= 0.35f;
                }
                px[y * w + x] = c;
            }

        overlayTex.SetPixels(px);
        overlayTex.Apply();
        overlayImage.texture = overlayTex;
    }

    // ---- Power overlay ----
    // Reading the grid is a question about REACH, and reach is invisible against full-vibrance terrain.
    // So this overlay does something none of the index ramps do: it DULLS the whole map first, and then
    // paints the electricity back on top in the only two colours on screen.
    //
    //   YELLOW — the grid. Every tile the electricity reaches.
    //   BLUE   — the infrastructure. What's doing the reaching: the plants and the relays.
    //
    // Yellow is the grid, blue is the source. Once you know that you can read a world's power at a
    // glance: blue dots joined by yellow puddles, and the gaps between the puddles are the problem.
    //
    // On a world with nothing generating, every tile stays dull — which is itself the answer, and a
    // more honest one than an empty overlay that looks like it failed to load.
    void RefreshPowerOverlay()
    {
        int w = body.surface.width, h = body.surface.height;
        // Deliberately NOT EnsureOverlayTex: that texture belongs to the ground layer, which is very
        // likely showing an index at the same time now. This writes its own (see the tail of this
        // method), or the two would fight over one image every repaint.

        // Reused across repaints. This runs several times a second for as long as the tab is open, and a
        // big world is 384x192 — a fresh Color[73728] each time is ~1.2 MB of garbage per repaint.
        if (powerPx == null || powerPx.Length != w * h) powerPx = new Color[w * h];
        var px = powerPx;

        var dull = new Color(0.02f, 0.03f, 0.06f, 0.62f);
        for (int i = 0; i < px.Length; i++) px[i] = dull;

        foreach (var net in PowerGrid.Nets(body))
        {
            // A failing grid is drawn in a sicklier, dimmer yellow than a healthy one, so "which of
            // these grids is in trouble" is answerable from the map rather than only from the list.
            //
            // A FAILED grid — relays with no plant behind them and no charge left — is drawn in dead
            // grey rather than any shade of yellow. It has no load it can serve either, so a
            // supply-based tint would paint it the same confident yellow as a working one, and the
            // player would be looking at an apparently healthy grid wondering why the mine under it is
            // at a third output. A dead grid still coasting on its capacitors gets amber: it IS
            // delivering, it just has nothing behind it and a deadline.
            float s = Mathf.Clamp01(net.served);
            var lit = net.Failed
                ? new Color(0.38f, 0.40f, 0.45f, 0.34f)
                : net.Dead
                    ? new Color(0.90f, 0.55f, 0.15f, 0.40f)
                    : Color.Lerp(new Color(0.85f, 0.45f, 0.10f, 0.34f), new Color(1.00f, 0.95f, 0.20f, 0.42f), s);
            foreach (var c in net.coverage)
            {
                if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h) continue;
                px[c.y * w + c.x] = lit;
            }
        }

        // Sources and relays go on last so they always read on top of their own light.
        var electricBlue = new Color(0.25f, 0.72f, 1.00f, 0.85f);
        foreach (var p in SurfaceBuildManager.On(body))
        {
            if (p.Info.powerRange <= 0f && p.Info.powerStorage <= 0f) continue;
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            {
                if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h) continue;
                px[c.y * w + c.x] = electricBlue;
            }
        }

        // Its OWN texture and its OWN image — the ground index is very likely using the other one at the
        // same time now.
        if (powerTex == null || powerTex.width != w || powerTex.height != h)
        {
            if (powerTex != null) Destroy(powerTex);
            powerTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }

        powerTex.SetPixels(px);
        powerTex.Apply();
        if (powerOverlayImage != null) powerOverlayImage.texture = powerTex;
    }

    void EnsureOverlayTex(int w, int h)
    {
        if (overlayTex != null && overlayTex.width == w && overlayTex.height == h) return;
        if (overlayTex != null) Destroy(overlayTex);
        overlayTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
    }

    // ---- Tectonics overlay ----
    // Like the power overlay, this doesn't ramp an index — it draws the plate geometry directly: a
    // translucent WHITE wash over the whole map (so it reads at a glance as the tectonic view), with the
    // FAULT LINES between plates painted red. Built from the same TectonicsMap the terrain generator folds
    // its mountains from, so the red lines land exactly where the ranges and volcanoes cluster. Static
    // (plates don't move frame to frame), so it's painted once per rebuild rather than on a timer.
    void RefreshTectonicsOverlay()
    {
        int w = body.surface.width, h = body.surface.height;
        EnsureOverlayTex(w, h);

        var wash  = new Color(0.86f, 0.89f, 0.93f, 0.30f);   // translucent white plate wash
        var fault = new Color(0.95f, 0.16f, 0.16f, 0.92f);   // red fault line

        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float boundary = TectonicsMap.Sample(body, u, v).boundary;
                // A fault cell where the tile sits close to a plate boundary; the band comes out ~1-3 cells
                // wide, widening where three plates meet at a corner, exactly as the request describes.
                px[y * w + x] = boundary > 0.55f ? fault : wash;
            }
        overlayTex.SetPixels(px);
        overlayTex.Apply();
        overlayImage.texture = overlayTex;
    }

    // A red arrow per plate, at the plate's centre, pointing the way the plate pushes and sized by how
    // hard — the request's "each continent should have a red arrow ... the size should indicate how
    // strongly it is pushing". Same TectonicsMap geometry as the fault wash and the terrain.
    void DrawPlateArrows()
    {
        ClearPlateArrows();
        if (plateArrowLayer == null || body?.surface == null || !TectonicsMap.Active(body)) return;
        var layout = TectonicsMap.Get(body);
        if (layout?.plates == null) return;

        foreach (var plate in layout.plates)
        {
            TectonicsMap.ArrowOnMap(plate, out float u, out float v, out Vector2 dir, out float strength);
            if (dir.sqrMagnitude < 1e-6f) continue;

            var go = UIFactory.NewUI(plateArrowLayer, "PlateArrow");
            var img = go.AddComponent<Image>();
            img.sprite = SurfaceMarkerArt.Arrow();
            img.raycastTarget = false;
            img.color = new Color(0.96f, 0.18f, 0.18f, 0.95f);   // same red as the fault lines
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(u, v);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float sz = Mathf.Lerp(16f, 34f, Mathf.Clamp01(strength));
            rt.sizeDelta = new Vector2(sz * 0.7f, sz);
            rt.anchoredPosition = Vector2.zero;
            // SurfaceMarkerArt.Arrow points straight DOWN (-Y) by default. Rotating a down-vector by z=θ
            // (CCW) sends it to (sinθ, -cosθ); solving that == dir gives θ = atan2(dir.x, -dir.y).
            rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg);
        }
    }

    void ClearPlateArrows()
    {
        if (plateArrowLayer == null) return;
        for (int i = plateArrowLayer.childCount - 1; i >= 0; i--) Destroy(plateArrowLayer.GetChild(i).gameObject);
    }

    // ---- Structures on the map ----
    void DrawPieces()
    {
        for (int i = pieceLayer.childCount - 1; i >= 0; i--) Destroy(pieceLayer.GetChild(i).gameObject);
        foreach (var m in mirrors)
            if (m.pieces != null)
                for (int i = m.pieces.childCount - 1; i >= 0; i--) Destroy(m.pieces.GetChild(i).gameObject);

        if (body?.surface == null) return;

        foreach (var p in SurfaceBuildManager.On(body))
        {
            var info = p.Info;
            // Fully opaque and pushed past its own saturation. What separates a structure from the
            // ground is now the black OUTLINE below, not the terrain being dulled to get out of its way
            // — so this only has to be a strong, readable colour, not the only strong colour on screen.
            var c = Vivid(info.color);
            var cells = SurfaceBuildingDatabase.Footprint(p);

            // Drawn into the real layer AND both wrap mirrors. Structures have to survive the seam: a map
            // that loops but whose cities vanish as they cross the join is worse than one that does not
            // loop at all, because it looks like the buildings were destroyed.
            foreach (var cell in cells) AddCellQuad(pieceLayer, cell.x, cell.y, c);
            OutlineFootprint(pieceLayer, cells);

            foreach (var m in mirrors)
            {
                if (m.pieces == null) continue;
                foreach (var cell in cells) AddCellQuad(m.pieces, cell.x, cell.y, c);
                OutlineFootprint(m.pieces, cells);
            }
        }

        SyncWrapMirrors();
    }

    // ---- Confirm before building ----
    //
    // The panel is a child of the VIEWPORT and anchored to the map cell, so it sits next to the thing it
    // is asking about. A dialog in the middle of the screen would ask "place the Mine?" while covering
    // the ground you were looking at to decide.
    RectTransform confirmPanel;
    TMP_Text confirmText;
    Vector2Int pendingCell = new Vector2Int(-1, -1);
    int pendingRotation;
    SurfaceBuildingType? pendingType;

    void AskPlace(int x, int y)
    {
        pendingCell = new Vector2Int(x, y);
        pendingRotation = rotation;
        pendingType = selected;
        BuildConfirmPanel();
        RefreshConfirmPanel();
    }

    void CancelPlace()
    {
        pendingType = null;
        pendingCell = new Vector2Int(-1, -1);
        if (confirmPanel != null) confirmPanel.gameObject.SetActive(false);
    }

    void DoPlace()
    {
        if (!pendingType.HasValue) { CancelPlace(); return; }

        // Re-check rather than trust the check from when the panel opened. Between then and now the
        // economy has ticked, another world may have spent the metal, and organic growth may have put a
        // settlement on the very cell being asked about.
        if (SurfaceBuildManager.CanPlace(body, pendingType.Value, pendingCell.x, pendingCell.y, pendingRotation, out _) &&
            SurfaceBuildManager.Place(body, pendingType.Value, pendingCell.x, pendingCell.y, pendingRotation))
        {
            lastSig = null;   // the built list and the map both changed
            SimpleAudio.Instance?.PlayComplete();
        }
        else SimpleAudio.Instance?.PlayTick();

        CancelPlace();
    }

    void BuildConfirmPanel()
    {
        if (confirmPanel != null) { confirmPanel.gameObject.SetActive(true); confirmPanel.SetAsLastSibling(); return; }

        confirmPanel = UIFactory.NewUI(hostViewport, "ConfirmPlace").GetComponent<RectTransform>();
        confirmPanel.sizeDelta = new Vector2(212, 62);
        var bg = confirmPanel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.09f, 0.14f, 0.97f);
        var outline = confirmPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = UITheme.Accent;
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        var v = confirmPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(6, 6, 5, 5); v.spacing = 4;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        confirmText = UIFactory.Text(confirmPanel, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        var tle = confirmText.gameObject.AddComponent<LayoutElement>();
        tle.preferredHeight = 30f;

        var row = UIFactory.NewUI(confirmPanel, "Row");
        UIFactory.AddLayout(row, 22);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;

        UIFactory.Button(row.transform, "Build", DoPlace, 20);
        UIFactory.Button(row.transform, "Cancel", CancelPlace, 20);

        confirmPanel.SetAsLastSibling();   // above the zoom bar, which is also a child of the viewport
    }

    void RefreshConfirmPanel()
    {
        if (confirmPanel == null || !pendingType.HasValue || body?.surface == null) return;

        var info = SurfaceBuildingDatabase.Get(pendingType.Value);
        int m = ColonyManager.DiscCost(info.costMetal), e = ColonyManager.DiscCost(info.costEnergy);
        string hex = ColorUtility.ToHtmlStringRGB(Vivid(info.color));
        // "•" and not "■": the Geometric Shapes block isn't in the LiberationSans atlas, so a square
        // renders as a tofu box. This is the same swatch glyph the rest of the UI settled on.
        confirmText.text = $"<color=#{hex}>•</color> Build <b>{info.name}</b> here?\n" +
                           $"<size=10><color=#9FB4C8>{m} metal · {e} energy · ({pendingCell.x},{pendingCell.y})</color></size>";

        // Follow the cell. Anchored in the MAP's normalised space so it tracks through zoom and pan
        // rather than being pinned to a screen position the map has since slid out from under.
        int w = body.surface.width, hgt = body.surface.height;
        Vector2 cellCentre = new Vector2((pendingCell.x + 0.5f) / w, (pendingCell.y + 0.5f) / hgt);
        Vector2 inMap = new Vector2((cellCentre.x - 0.5f) * mapRT.rect.width,
                                    (cellCentre.y - 0.5f) * mapRT.rect.height);
        Vector2 pos = inMap + mapPan + new Vector2(0f, 46f);   // just above the footprint

        // Keep it inside the viewport: a confirm you have to pan to find is worse than no confirm.
        var vp = hostViewport.rect;
        float hw = confirmPanel.sizeDelta.x * 0.5f, hh = confirmPanel.sizeDelta.y * 0.5f;
        pos.x = Mathf.Clamp(pos.x, vp.xMin + hw, vp.xMax - hw);
        pos.y = Mathf.Clamp(pos.y, vp.yMin + hh, vp.yMax - hh);

        confirmPanel.anchorMin = confirmPanel.anchorMax = new Vector2(0.5f, 0.5f);
        confirmPanel.pivot = new Vector2(0.5f, 0.5f);
        confirmPanel.anchoredPosition = pos;
    }

    // A thin black outline around a placed structure's PERIMETER.
    //
    // The perimeter, not each cell. Outlining every cell would draw a black grid THROUGH a multi-cell
    // building and read as several small structures rather than one large one — the opposite of what an
    // outline is for. So an edge is drawn only where the neighbouring cell isn't part of this same
    // building.
    //
    // Thickness is in PIXELS, not in cells, so it stays a hairline at every zoom. In cells it would
    // vanish when zoomed out and become a fat black border when zoomed in.
    void OutlineFootprint(RectTransform layer, List<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0) return;

        var set = new HashSet<Vector2Int>(cells);
        foreach (var cell in cells)
        {
            if (!set.Contains(cell + Vector2Int.up))    AddEdgeQuad(layer, cell.x, cell.y, 0);
            if (!set.Contains(cell + Vector2Int.right)) AddEdgeQuad(layer, cell.x, cell.y, 1);
            if (!set.Contains(cell + Vector2Int.down))  AddEdgeQuad(layer, cell.x, cell.y, 2);
            if (!set.Contains(cell + Vector2Int.left))  AddEdgeQuad(layer, cell.x, cell.y, 3);
        }
    }

    /// Outline thickness, in screen pixels. Thin, but never sub-pixel — below 1 it starts dropping out
    /// entirely on some edges as the rasteriser rounds.
    const float OutlinePx = 1.5f;

    /// One edge of one cell. dir: 0=top, 1=right, 2=bottom, 3=left. Drawn INSIDE the cell, so the
    /// outline never encroaches on the neighbouring tile's ground.
    void AddEdgeQuad(RectTransform layer, int x, int y, int dir)
    {
        if (body?.surface == null) return;
        int w = body.surface.width, h = body.surface.height;

        float l = x / (float)w, r = (x + 1) / (float)w;
        float b = y / (float)h, t = (y + 1) / (float)h;

        var q = UIFactory.Panel(layer, "o", Color.black);
        q.raycastTarget = false;
        var rt = q.rectTransform;

        // The ANCHORS collapse onto the edge; the OFFSETS then give it thickness. Both halves matter:
        // collapsing the anchors is what makes it an edge rather than the whole cell, and expressing the
        // thickness as a pixel offset is what keeps it a hairline at every zoom (an anchor-space
        // thickness would scale with the map and become a fat black border zoomed in).
        switch (dir)
        {
            case 0:  // top
                rt.anchorMin = new Vector2(l, t); rt.anchorMax = new Vector2(r, t);
                rt.offsetMin = new Vector2(0f, -OutlinePx); rt.offsetMax = Vector2.zero;
                break;
            case 1:  // right
                rt.anchorMin = new Vector2(r, b); rt.anchorMax = new Vector2(r, t);
                rt.offsetMin = new Vector2(-OutlinePx, 0f); rt.offsetMax = Vector2.zero;
                break;
            case 2:  // bottom
                rt.anchorMin = new Vector2(l, b); rt.anchorMax = new Vector2(r, b);
                rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(0f, OutlinePx);
                break;
            default: // left
                rt.anchorMin = new Vector2(l, b); rt.anchorMax = new Vector2(l, t);
                rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(OutlinePx, 0f);
                break;
        }
    }

    // Push a colour away from grey, so a structure's own hue is as strong as it can be.
    static Color Vivid(Color c)
    {
        float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        var v = Color.LerpUnclamped(new Color(grey, grey, grey), c, 1.35f);
        return new Color(Mathf.Clamp01(v.r), Mathf.Clamp01(v.g), Mathf.Clamp01(v.b), 1f);
    }

    // ---- Selection marker ----
    // A pulsing ring around the selected structure plus a downward arrow hovering over it. Rebuilt only
    // when the SELECTION changes; the pulse/spin is animated in place each frame (AnimateMarker).
    void DrawSelectionMarker()
    {
        for (int i = markerLayer.childCount - 1; i >= 0; i--) Destroy(markerLayer.GetChild(i).gameObject);
        markerRing = null; markerArrow = null;

        var sel = SurfaceSelection.Selected;
        if (sel == null || SurfaceSelection.Body != body || body?.surface == null) return;

        // Centre the marker on the footprint's middle, so it sits on the building rather than its origin.
        var cells = SurfaceBuildingDatabase.Footprint(sel);
        if (cells.Count == 0) return;
        float sx = 0f, sy = 0f;
        foreach (var c in cells) { sx += c.x + 0.5f; sy += c.y + 0.5f; }
        Vector2 centre = new Vector2(sx / cells.Count, sy / cells.Count);

        int w = body.surface.width, h = body.surface.height;
        float tileW = mapRT.rect.width / w, tileH = mapRT.rect.height / h;

        // Ring big enough to enclose the whole footprint.
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in cells)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
        }
        float ringPx = Mathf.Max((maxX - minX + 1) * tileW, (maxY - minY + 1) * tileH) * 1.55f;

        var ringGO = UIFactory.NewUI(markerLayer, "SelRing");
        markerRing = ringGO.AddComponent<Image>();
        markerRing.sprite = SurfaceMarkerArt.Ring();
        markerRing.raycastTarget = false;
        var rrt = markerRing.rectTransform;
        rrt.anchorMin = rrt.anchorMax = new Vector2(centre.x / w, centre.y / h);
        rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.sizeDelta = new Vector2(ringPx, ringPx);
        rrt.anchoredPosition = Vector2.zero;
        markerRingBase = ringPx;

        // Arrow hovering above, pivoted at its point so it aims AT the building.
        var arrowGO = UIFactory.NewUI(markerLayer, "SelArrow");
        markerArrow = arrowGO.AddComponent<Image>();
        markerArrow.sprite = SurfaceMarkerArt.Arrow();
        markerArrow.raycastTarget = false;
        markerArrow.color = new Color(1f, 0.95f, 0.4f);
        var art = markerArrow.rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(centre.x / w, (maxY + 1) / (float)h);
        art.pivot = new Vector2(0.5f, 0f);
        art.sizeDelta = new Vector2(22f, 28f);
        art.anchoredPosition = new Vector2(0, 6f);
        markerArrowBaseY = 6f;
    }

    // The pulse and the spin. Runs every frame, touching only transform/colour — never the layout.
    void AnimateMarker()
    {
        if (markerRing == null && markerArrow == null) return;
        float t = Time.unscaledTime;

        if (markerRing != null)
        {
            float pulse = 1f + Mathf.Sin(t * 3.2f) * 0.07f;
            markerRing.rectTransform.sizeDelta = new Vector2(markerRingBase * pulse, markerRingBase * pulse);
            float a = 0.55f + Mathf.Sin(t * 3.2f) * 0.25f;
            markerRing.color = new Color(1f, 0.95f, 0.4f, a);
        }

        if (markerArrow != null)
        {
            // Squashing X to cos(t) reads as an arrow spinning about its own vertical axis. Actually
            // ROTATING a downward arrow in 2D would just make it point sideways, which is not what a
            // "spinning downward arrow" means.
            float spin = Mathf.Cos(t * 2.4f);
            var rt = markerArrow.rectTransform;
            rt.localScale = new Vector3(Mathf.Max(0.12f, Mathf.Abs(spin)), 1f, 1f);
            rt.anchoredPosition = new Vector2(0, markerArrowBaseY + 3f + Mathf.Sin(t * 2f) * 3f);
        }
    }

    // ---- The ghost ----
    // Snapped to the GRID whenever the cursor is over the map; when it isn't, the piece rides the mouse
    // as a loose ghost so you can see what you're carrying.
    void DrawGhost()
    {
        ClearGhost();
        if (!selected.HasValue || body?.surface == null) return;

        var info = SurfaceBuildingDatabase.Get(selected.Value);

        // A confirm is up: freeze the ghost on the footprint being asked about. Letting it keep chasing
        // the cursor would show the piece in one place while the panel asks about another.
        if (pendingType.HasValue)
        {
            var pc = Vivid(SurfaceBuildingDatabase.Get(pendingType.Value).color);
            foreach (var cell in SurfaceBuildingDatabase.Footprint(pendingType.Value, pendingCell.x, pendingCell.y, pendingRotation))
                AddCellQuad(ghostLayer, cell.x, cell.y, pc);
            return;
        }

        // The ghost carries the structure's FULL colour — Vivid, exactly as the placed building will be
        // drawn. It used to be a 62%-alpha wash of it, which made every structure look like a different,
        // paler thing while you were choosing where to put it than it did once it landed. You should be
        // deciding with the real colour in front of you.
        //
        // It's still distinguishable from a placed structure, just not by hue: a ghost has no black
        // outline, and a placed one does.
        if (hoverCell.x >= 0)
        {
            // Vivid when it fits, red when it doesn't, so validity is obvious before you click.
            Color c = hoverValid ? Vivid(info.color) : new Color(1f, 0.25f, 0.2f, 0.85f);
            foreach (var cell in SurfaceBuildingDatabase.Footprint(selected.Value, hoverCell.x, hoverCell.y, rotation))
                AddCellQuad(ghostLayer, cell.x, cell.y, c);
        }
        else
        {
            // Loose: follow the mouse in screen space.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    ghostLayer, Input.mousePosition, null, out Vector2 lp)) return;
            float cw = mapRT.rect.width / body.surface.width;
            float ch = mapRT.rect.height / body.surface.height;
            var c = Vivid(info.color);
            foreach (var cell in SurfaceBuildingDatabase.CellsOf(selected.Value, rotation))
            {
                var q = UIFactory.Panel(ghostLayer, "g", c);
                q.raycastTarget = false;
                var qrt = q.rectTransform;
                qrt.anchorMin = qrt.anchorMax = new Vector2(0.5f, 0.5f);
                qrt.pivot = new Vector2(0.5f, 0.5f);
                qrt.sizeDelta = new Vector2(cw - 1f, ch - 1f);
                qrt.anchoredPosition = lp + new Vector2(cell.x * cw, cell.y * ch);
            }
        }
    }

    void ClearGhost()
    {
        for (int i = ghostLayer.childCount - 1; i >= 0; i--) Destroy(ghostLayer.GetChild(i).gameObject);
    }

    // Grid cell -> a quad anchored in the map's normalized space, so it scales with the window.
    void AddCellQuad(RectTransform layer, int x, int y, Color c)
    {
        if (body?.surface == null) return;
        int w = body.surface.width, h = body.surface.height;
        var q = UIFactory.Panel(layer, "c", c);
        q.raycastTarget = false;
        var rt = q.rectTransform;
        rt.anchorMin = new Vector2(x / (float)w, y / (float)h);
        rt.anchorMax = new Vector2((x + 1) / (float)w, (y + 1) / (float)h);
        rt.offsetMin = new Vector2(0.5f, 0.5f);
        rt.offsetMax = new Vector2(-0.5f, -0.5f);
    }

    // ---- Hover ----
    /// Screen point -> grid cell. Public so the click probe shares exactly this mapping.
    public bool ScreenToCell(Vector2 screenPos, Camera cam, out int x, out int y)
        => ScreenToCellIn(mapRT, body, screenPos, cam, out x, out y);

    // Map a screen point over ANY surface RawImage (the host map OR a moon map) to a cell in that body's
    // surface. The texture always fills its rect (uv 0..1), so normalising the local point against the
    // rect's own bounds yields the cell directly, at any zoom/pan.
    bool ScreenToCellIn(RectTransform mapRect, CelestialBody b, Vector2 screenPos, Camera cam, out int x, out int y)
    {
        x = y = -1;
        if (b?.surface == null || mapRect == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, screenPos, cam, out Vector2 lp)) return false;

        // The local point is relative to the rect's pivot; normalize against the rect's own bounds.
        var r = mapRect.rect;
        float u = (lp.x - r.xMin) / r.width;
        float v = (lp.y - r.yMin) / r.height;

        // Longitude WRAPS for the host map once the wrap mirrors are up.
        //
        // Without this, clicking on a mirror resolves to a u outside 0..1 and gets rejected — so exactly
        // the half of the screen showing mirrored world would be dead to clicks, hover and building,
        // while still scrolling and looking perfectly normal. At a half-width pan that is half the
        // viewport. Wrapping u maps a point on a mirror back onto the cell it is a copy of, which is the
        // whole reason the mirror looks like that cell.
        // Moon panes wrap too. Their map is centre-anchored inside its frame, which is its parent, so the
        // same "is the map at least as wide as what shows it" test applies — and without this a moon
        // would grow the identical dead zone the host map just had fixed.
        bool wrapU;
        if (mapRect == mapRT) wrapU = WrapEnabled;
        else
        {
            var frameRT = mapRect.parent as RectTransform;
            wrapU = frameRT != null && PaneWrapEnabled(frameRT.rect, mapRect.sizeDelta);
        }

        if (wrapU) u = Mathf.Repeat(u, 1f);
        else if (u < 0f || u > 1f) return false;

        if (v < 0f || v > 1f) return false;

        x = Mathf.Clamp(Mathf.FloorToInt(u * b.surface.width), 0, b.surface.width - 1);
        y = Mathf.Clamp(Mathf.FloorToInt(v * b.surface.height), 0, b.surface.height - 1);
        return true;
    }

    // Scroll over the viewport to zoom the MAP inside it — the window itself never moves or resizes.
    // Proportional, like the world camera, so one notch feels the same at every scale. Only when the
    // cursor is over the viewport, so scrolling the side panel still scrolls the side panel.
    void PollMapZoom()
    {
        if (!HostOpen || body?.surface == null || mapRT == null) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;
        // A scrollable menu dragged OVER the map owns the wheel — let it scroll, don't also zoom the map.
        // A non-scrolling panel over the map does NOT block the zoom (the wheel passes through to the map).
        if (UIScroll.PointerOverScroller()) return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(hostViewport, Input.mousePosition, null)) return;
        activePane = body;

        float fit = FitTilePx();                            // zoom-out floor = whole map fits
        float max = Mathf.Max(CoverTilePx(), MaxTilePx());

        // One wheel notch = one press of + or -, so the two controls agree. Unity's ScrollWheel axis is
        // ~0.1 per notch, hence the 10x before raising ZoomStep to it: pow(1.5, 0.1*10) = 1.5.
        float next = Mathf.Clamp(tilePx * Mathf.Pow(ZoomStep, scroll * 10f), fit, max);
        if (Mathf.Approximately(next, tilePx)) return;

        // Zoom TOWARD THE CURSOR: keep whatever is under the pointer pinned there, rather than
        // zooming to the middle and making the player chase what they were looking at.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                hostViewport, Input.mousePosition, null, out Vector2 vpPoint))
        {
            Vector2 mapPoint = vpPoint - mapPan;          // cursor, in map space, before the zoom
            float ratio = next / tilePx;
            mapPan = vpPoint - mapPoint * ratio;          // ...still under the cursor after it
        }

        tilePx = next;
        ApplyMapSize();
        // Pieces and the ghost are anchored in the map's normalised space and follow for free, but the
        // selection ring's size is in pixels, so it has to be rebuilt at the new scale.
        DrawSelectionMarker();
    }

    // Drag the map with the middle or right mouse button to pan. (Right-click also rotates a held
    // piece, so panning with it only starts once you've actually moved — a click stays a rotate.)
    Vector2 panGrabScreen;
    Vector2 panGrabOffset;
    bool panning;

    // ---- Grab the map and drag it ----
    //
    // LEFT drag is the pan, because that's what "grab the map" means everywhere else. It only conflicts
    // with left-click-to-select for as long as it takes to move DragThreshold pixels: under that it's
    // still a click and the map never moves; over it, `panDragged` latches and OnGridClick ignores the
    // release, so a drag can't accidentally select whatever you happened to start the drag on top of.
    //
    // Middle drag pans too, and works even while a building is held.
    //
    // RIGHT drag used to pan, which was a straight conflict: right-click is also "rotate the held
    // piece", so rotating nudged the map and panning rotated the piece.
    void PollMapPan()
    {
        if (!HostOpen || body?.surface == null) return;

        // Cleared on ANY press, not just one that starts a pan. It has to be: a piece-holding press
        // never starts a pan (see leftPans below), so if the flag only reset when a pan began, one drag
        // would latch it true and silently swallow every click from then on.
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2)) panDragged = false;

        // Left-drag is off while a piece is held — there, a left press is placing a building, and
        // dragging the ground out from under it is the last thing anyone wants.
        bool leftPans = !selected.HasValue;

        if (!panning &&
            ((leftPans && Input.GetMouseButtonDown(0)) || Input.GetMouseButtonDown(2)) &&
            RectTransformUtility.RectangleContainsScreenPoint(hostViewport, Input.mousePosition, null) &&
            // The zoom bar and moon-tab strip float INSIDE the viewport, so their rects are inside the pan
            // region too. Without this, pressing + or a moon tab and twitching would drag the map.
            !RectTransformUtility.RectangleContainsScreenPoint(zoomBar, Input.mousePosition, null) &&
            !RectTransformUtility.RectangleContainsScreenPoint(moonTabStrip, Input.mousePosition, null) &&
            // The host frame and the moon frames tile disjoint cells, but guard the seam anyway so a press
            // right on the boundary can't start a host pan while the cursor is over a moon frame.
            !OverAnyMoonFrame(Input.mousePosition))
        {
            panning = true;
            panGrabScreen = Input.mousePosition;
            panGrabOffset = mapPan;
        }

        if (panning)
        {
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(2)) { panning = false; return; }

            Vector2 delta = (Vector2)Input.mousePosition - panGrabScreen;
            if (!panDragged && delta.sqrMagnitude < DragThreshold * DragThreshold) return;

            panDragged = true;
            mapPan = panGrabOffset + delta;
            ClampPan();
        }
    }

    /// Pixels of movement before a press stops being a click and becomes a drag.
    const float DragThreshold = 5f;

    bool panDragged;

    // Click outside the window to dismiss it.
    //
    // Only a click that lands on NO UI at all counts: clicking another window is working with that
    // window, not dismissing this one, and closing it out from under the player would be obnoxious.
    // A piece held for placement swallows the click too — Esc drops it first.
    void PollClickAway()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (selected.HasValue) return;
        if (panning) return;

        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && es.IsPointerOverGameObject()) return;   // over some UI — not a click-away

        root.SetActive(false);
    }

    // Polled every frame. Cheap, and independent of which input module is installed.
    void PollHover()
    {
        // The moon-tab strip owns MapHoverPanel while the cursor is over IT — MoonTabHover shows its own
        // content on OnPointerEnter/Exit, event-driven rather than polled. If this method touched the panel
        // at all over that rect (even just to Hide() it), it would stomp MoonTabHover's own state every
        // single frame, since this runs unconditionally and that only runs once on the enter/exit edge.
        if (moonTabStrip != null && RectTransformUtility.RectangleContainsScreenPoint(moonTabStrip, Input.mousePosition, null))
            return;

        // Over an open MOON map's frame? Show that moon's tile info in the floating tooltip — the same
        // biome / ore / temperature readout the main map gives, so a moon's surface is as inspectable as
        // the planet's. Each moon has its own framed pane now, so this just tests each frame in turn.
        foreach (var m in openMaps)
        {
            if (m == body) continue;
            if (!moonFrame.TryGetValue(m, out var frame) || frame == null) continue;
            if (!moonImg.TryGetValue(m, out var img) || img == null || m.surface == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(frame, Input.mousePosition, null)) continue;
            activePane = m;
            if (ScreenToCellIn(img.rectTransform, m, Input.mousePosition, null, out int mx, out int my))
                MapHoverPanel.Instance.ShowAtCursor(
                    $"<size=11><color=#8FD0FF>{m.name}</color></size>\n" + TileHoverText(m, mx, my));
            else
                MapHoverPanel.Instance.Hide();
            return;
        }

        // Otherwise the host planet's map, if it's open.
        if (HostOpen && ScreenToCell(Input.mousePosition, null, out int x, out int y))
        {
            activePane = body;
            if (x != hoverCell.x || y != hoverCell.y)
            {
                hoverCell = new Vector2Int(x, y);
                RecomputeHoverValidity();
            }
            // Suppressed over the zoom bar / confirm dialog: they float over the same part of the map the
            // tooltip targets, and covering their own buttons and text would be worse than no tooltip
            // there. Re-shown every frame (not just on cell change) so it tracks the mouse WITHIN a cell.
            if (!OverFloatingMapControl()) MapHoverPanel.Instance.ShowAtCursor(TileHoverText(x, y));
            else MapHoverPanel.Instance.Hide();
        }
        else
        {
            if (hoverCell.x >= 0)
            {
                hoverCell = new Vector2Int(-1, -1);
                hoverValid = false;
            }
            MapHoverPanel.Instance.Hide();
        }
    }

    // Is the cursor over one of the small floating controls that sit ON TOP of the map (the zoom bar, the
    // Build confirm dialog)? Neither owns a tooltip of its own, unlike the moon-tab strip (handled
    // separately, see PollHover), so simply hiding the tile panel over them is correct.
    bool OverFloatingMapControl()
    {
        var p = Input.mousePosition;
        if (zoomBar != null && RectTransformUtility.RectangleContainsScreenPoint(zoomBar, p, null)) return true;
        if (confirmPanel != null && confirmPanel.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(confirmPanel, p, null)) return true;
        return false;
    }

    // Tile type (coloured like the tile), its ore if one has been discovered here, and this spot's
    // temperature (coloured on PlanetTemperature's global gradient) — every field fetched from data the
    // game already tracks, nothing new stored per tile.
    string TileHoverText(int x, int y) => TileHoverText(body, x, y);

    // Tile readout for ANY body's surface (the main planet or an open moon), so a moon map's tiles show
    // the same biome / ore / temperature info the main map does.
    string TileHoverText(CelestialBody b, int x, int y)
    {
        var tile = b.surface.tiles[x, y];
        string typeHex = ColorUtility.ToHtmlStringRGB(TerrainColorMap.Get(tile.type));
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b><color=#{typeHex}>{tile.type}</color></b>");

        if (tile.HasOre && ResearchManager.IsDiscovered(tile.ore))
            sb.Append($"\n<color=#8FD0FF>{OreDatabase.Get(tile.ore).displayName}</color> ({tile.oreRichness * 100f:F0}% rich)");

        float celsius = PlanetTemperature.CelsiusAt(b, y);
        string tempHex = ColorUtility.ToHtmlStringRGB(PlanetTemperature.GradientColor(celsius));
        sb.Append($"\n<color=#{tempHex}>{PlanetTemperature.Label(celsius)}</color>");

        return sb.ToString();
    }

    void RecomputeHoverValidity()
    {
        hoverValid = selected.HasValue && hoverCell.x >= 0 &&
                     SurfaceBuildManager.CanPlace(body, selected.Value, hoverCell.x, hoverCell.y, rotation, out _);
    }

    public void OnGridClick(int x, int y)
    {
        // The release that ends a pan is not a click. uGUI fires OnPointerClick on release whenever the
        // press and release landed on the same object, however far the pointer travelled in between — so
        // without this, every drag of the map would also select (or deselect) whatever was under where
        // you grabbed it.
        if (panDragged) return;

        // Holding a piece? The click ASKS to place it — it doesn't place it. Building costs resources
        // and permanently occupies ground on a grid where siting decides yield, so it's not something to
        // do on a stray click.
        if (tab == Tab.Build && selected.HasValue)
        {
            if (!SurfaceBuildManager.CanPlace(body, selected.Value, x, y, rotation, out _)) return;
            AskPlace(x, y);
            return;
        }

        // Otherwise the click SELECTS whatever is standing on that cell — the map half of "select a
        // building by clicking it on the map, or in the list". Clicking bare ground clears.
        var hit = SurfaceBuildManager.At(body, x, y);
        if (hit != null)
        {
            SurfaceSelection.Select(body, hit);
            SimpleAudio.Instance?.PlaySelect();
        }
        else SurfaceSelection.Clear();
    }

    public CelestialBody Body => body;
    public bool BuildMode => tab == Tab.Build && selected.HasValue;

    // ============================================================================================
    // MAP PANES — the planet and each of its moons are toggleable tabs (the planet's a touch bigger). Any
    // mix of up to five open panes TILES the whole map area with no gaps, sized only by how many are open;
    // closing a tab reflows the rest to fill the freed space. Each pane zooms its CONTENTS inside a fixed
    // frame — cover-fit at the fullest-out end, so a map fills its frame rather than floating in letterbox.
    // With nothing open the area shows a hint. The host planet keeps its own tilePx/mapPan/mapRT and all its
    // placement machinery; each moon carries its own frame, zoom and pan.
    // ============================================================================================
    const float MoonTabSize = 34f;            // square moon tab — big enough to read its terrain thumbnail
    const float PlanetTabSize = 46f;          // the planet's own tab, a touch bigger so it reads as the host
    const float PaneGap = 3f;                 // hairline grout between tiled panes (the themed bg shows through)

    // Moons closest to the host first (topmost tab), ordered by orbit radius.
    List<CelestialBody> MoonsClosestFirst()
    {
        var list = new List<CelestialBody>();
        if (body?.moons != null) list.AddRange(body.moons);
        list.Sort((a, b) => a.orbitRadius.CompareTo(b.orbitRadius));
        return list;
    }

    // Rebuild the tab strip for the current world, close any panes left open from the previous one, and open
    // the planet's own map by default so the window still lands on the world you clicked.
    void SetupMapTabs()
    {
        openMaps.Clear();
        ClearMoonPanes();
        moonTilePx.Clear(); moonPan.Clear(); moonPanDrag = null; activePane = null;
        if (body != null) openMaps.Add(body);   // the planet map is open by default
        BuildMapTabStrip();
        LayoutPanes();
    }

    void BuildMapTabStrip()
    {
        if (moonTabStrip == null) return;
        foreach (var tx in moonTabThumbTextures) if (tx != null) Destroy(tx);
        moonTabThumbTextures.Clear();
        for (int i = moonTabStrip.childCount - 1; i >= 0; i--) Destroy(moonTabStrip.GetChild(i).gameObject);

        moonTabStrip.gameObject.SetActive(true);
        if (body != null) BuildMapTab(body, PlanetTabSize);           // the planet's own (bigger) tab first
        foreach (var m in MoonsClosestFirst()) BuildMapTab(m, MoonTabSize);
    }

    // One tab: a square terrain thumbnail of the body it opens, tinted when that map is open, with a hover
    // survey card. Used for both the planet (bigger) and each moon.
    void BuildMapTab(CelestialBody target, float size)
    {
        var captured = target;
        bool open = openMaps.Contains(target);
        var btn = UIFactory.Button(moonTabStrip, "", () => ToggleMap(captured), size);
        var le = UIFactory.Ensure<LayoutElement>(btn.gameObject);
        le.preferredWidth = size; le.minWidth = size;
        le.preferredHeight = size; le.minHeight = size;
        le.flexibleWidth = 0; le.flexibleHeight = 0;

        // Active-tab tint like the main strip: an open map reads as the selected one.
        var colors = btn.colors;
        colors.normalColor = open ? UITheme.ButtonActive : UITheme.ButtonBg;
        colors.highlightedColor = colors.normalColor;
        colors.selectedColor = colors.normalColor;
        btn.colors = colors;

        // A downscaled image of the body itself, from the same renderer the open maps use.
        var thumbGO = UIFactory.NewUI(btn.transform, "Thumb");
        var thumbImg = thumbGO.AddComponent<RawImage>();
        thumbImg.raycastTarget = false;
        UIFactory.Stretch(thumbImg.rectTransform, 2f, 2f, 2f, 2f);
        Texture2D tex = target.surface != null ? SurfaceTextureRenderer.BuildGrid(target) : null;
        thumbImg.texture = tex;
        if (tex != null) moonTabThumbTextures.Add(tex);

        var hover = btn.gameObject.AddComponent<MoonTabHover>();
        hover.Configure(target);
    }

    bool HostOpen => body != null && openMaps.Contains(body);

    // Open or close a body's map (planet or moon). Opening a sixth pushes out the oldest, so at most five
    // show at once. Closing a tab reflows the rest to fill the freed space (LayoutPanes).
    void ToggleMap(CelestialBody m)
    {
        if (openMaps.Contains(m))
        {
            openMaps.Remove(m);
            moonTilePx.Remove(m); moonPan.Remove(m);
            if (activePane == m) activePane = null;
        }
        else
        {
            if (openMaps.Count >= MaxOpenMaps)
            {
                var dropped = openMaps[0];
                openMaps.RemoveAt(0);   // drop the oldest to make room
                moonTilePx.Remove(dropped); moonPan.Remove(dropped);
            }
            openMaps.Add(m);
        }
        moonPanDrag = null;    // the open set just changed — any in-flight drag no longer means anything
        SimpleAudio.Instance?.PlayTick();
        BuildMapTabStrip();    // refresh the highlights
        LayoutPanes();
    }

    // Fractions used by the map-view formats.
    const float MoonRowFrac = 0.42f;    // a single moon row's share of the height (planet takes the rest)
    const float SplitRowFrac = 0.30f;   // each row's share when moons are split ABOVE and BELOW the planet
    const float SideColFrac = 0.34f;    // the moon column's share of the width in the "beside" format

    // A small "Change Map View" button floating in the map area's top-right corner. Clicking it cycles the
    // arrangement of the open panes (moons above / below / split / beside the planet).
    void BuildViewFormatButton()
    {
        var b = UIFactory.Button(gridHolder, "", CycleMapLayout, 24f);
        viewFormatBtn = b.GetComponent<RectTransform>();
        viewFormatBtn.anchorMin = viewFormatBtn.anchorMax = new Vector2(1f, 1f);
        viewFormatBtn.pivot = new Vector2(1f, 1f);
        viewFormatBtn.anchoredPosition = new Vector2(-6f, -6f);
        viewFormatBtn.sizeDelta = new Vector2(150f, 26f);
        viewFormatLabel = b.GetComponentInChildren<TMP_Text>();
        if (viewFormatLabel != null) viewFormatLabel.fontSize = UITheme.SmallSize;
        UpdateViewFormatLabel();
    }

    void CycleMapLayout()
    {
        mapLayout = (MapLayout)(((int)mapLayout + 1) % 4);
        SimpleAudio.Instance?.PlayTick();
        UpdateViewFormatLabel();
        LayoutPanes();
    }

    void UpdateViewFormatLabel()
    {
        if (viewFormatLabel == null) return;
        string name = mapLayout switch
        {
            MapLayout.MoonsAbove => "Moons Above",
            MapLayout.MoonsBelow => "Moons Below",
            MapLayout.MoonsSplit => "Moons Split",
            MapLayout.MoonsSide  => "Moons Beside",
            _ => "Map View",
        };
        viewFormatLabel.text = $"View: {name}";
    }

    // Arrange the open panes according to the current map-view FORMAT (cycled by the Change Map View
    // button). Every format keeps the planet as the large map and lays the moons out around it; each pane
    // cover-fits its own fixed frame, so any mix of planet/moon sizes and counts fits — some maps just
    // shrink more than others. The frames only ever CHANGE SIZE; zoom happens inside each fixed frame.
    void LayoutPanes()
    {
        if (gridHolder == null) return;
        RebuildMoonPanes();

        int k = openMaps.Count;
        bool hostOpen = HostOpen;
        if (hostViewport != null) hostViewport.gameObject.SetActive(hostOpen);
        if (emptyHint != null) emptyHint.gameObject.SetActive(k == 0);
        if (zoomBar != null) zoomBar.gameObject.SetActive(k > 0);
        if (viewFormatBtn != null) viewFormatBtn.gameObject.SetActive(k > 0);
        if (k == 0) { KeepControlsOnTop(); return; }

        var area = gridHolder.rect;
        float W = area.width, H = area.height;
        if (W < 1f || H < 1f) { KeepControlsOnTop(); return; }

        // The open moons, in the order their tabs were opened.
        var moons = new List<CelestialBody>();
        foreach (var b in openMaps) if (b != body) moons.Add(b);
        int n = moons.Count;

        if (!hostOpen) LayoutMoonsNoPlanet(moons, W, H);
        else if (n == 0) PlaceHost(new Rect(0f, 0f, W, H));   // planet only — fills the area
        else switch (mapLayout)
        {
            case MapLayout.MoonsBelow:
            {
                float rowH = H * MoonRowFrac;
                LayoutMoonRow(moons, new Rect(0f, 0f, W, rowH));
                PlaceHost(new Rect(0f, rowH, W, H - rowH));
                break;
            }
            case MapLayout.MoonsSide:
            {
                float colW = W * SideColFrac;
                PlaceHost(new Rect(0f, 0f, W - colW, H));
                LayoutMoonColumn(moons, new Rect(W - colW, 0f, colW, H));
                break;
            }
            case MapLayout.MoonsSplit:
            {
                int topN = (n + 1) / 2;                          // ceil: the top row gets the extra one
                var top = moons.GetRange(0, topN);
                var bot = moons.GetRange(topN, n - topN);
                float rowH = H * SplitRowFrac;
                bool hasBot = bot.Count > 0;
                float planetY = hasBot ? rowH : 0f;
                float planetH = H - rowH - (hasBot ? rowH : 0f);   // top row is always present (topN >= 1)
                PlaceHost(new Rect(0f, planetY, W, planetH));
                LayoutMoonRow(top, new Rect(0f, H - rowH, W, rowH));
                if (hasBot) LayoutMoonRow(bot, new Rect(0f, 0f, W, rowH));
                break;
            }
            default: // MoonsAbove
            {
                float rowH = H * MoonRowFrac;
                PlaceHost(new Rect(0f, 0f, W, H - rowH));
                LayoutMoonRow(moons, new Rect(0f, H - rowH, W, rowH));
                break;
            }
        }

        DrawSelectionMarker();
        KeepControlsOnTop();
    }

    // With the planet closed, the moons fill the whole area themselves, matching the format's spirit: a
    // single row (Above/Below), two rows top+bottom (Split), or a single column (Beside).
    void LayoutMoonsNoPlanet(List<CelestialBody> moons, float W, float H)
    {
        int n = moons.Count;
        if (n == 0) return;
        switch (mapLayout)
        {
            case MapLayout.MoonsSplit:
            {
                int topN = (n + 1) / 2;
                var top = moons.GetRange(0, topN);
                var bot = moons.GetRange(topN, n - topN);
                if (bot.Count == 0) LayoutMoonRow(top, new Rect(0f, 0f, W, H));
                else
                {
                    LayoutMoonRow(top, new Rect(0f, H * 0.5f, W, H * 0.5f));
                    LayoutMoonRow(bot, new Rect(0f, 0f, W, H * 0.5f));
                }
                break;
            }
            case MapLayout.MoonsSide:
                LayoutMoonColumn(moons, new Rect(0f, 0f, W, H));
                break;
            default:
                LayoutMoonRow(moons, new Rect(0f, 0f, W, H));
                break;
        }
    }

    void PlaceHost(Rect r) { PlaceFrame(hostViewport, r); ApplyMapSize(); }

    // Moons side by side across a region, column widths proportional to each moon's aspect (w/h) so a wide
    // moon gets a wider column; the row fills the region exactly (last column absorbs rounding).
    void LayoutMoonRow(List<CelestialBody> moons, Rect region)
    {
        int n = moons.Count;
        if (n == 0) return;
        float total = 0f;
        for (int i = 0; i < n; i++) total += MoonAspect(moons[i]);
        if (total < 0.001f) total = n;

        float x = region.x;
        for (int i = 0; i < n; i++)
        {
            var m = moons[i];
            float cw = (i == n - 1) ? region.x + region.width - x : region.width * (MoonAspect(m) / total);
            if (moonFrame.TryGetValue(m, out var f) && f != null)
            {
                PlaceFrame(f, new Rect(x, region.y, cw, region.height));
                ApplyMoonSize(m);
            }
            x += cw;
        }
    }

    // Moons stacked top-to-bottom in a region, row heights proportional to each moon's INVERSE aspect (h/w)
    // so a tall moon gets a taller row; the column fills the region exactly (last row absorbs rounding).
    void LayoutMoonColumn(List<CelestialBody> moons, Rect region)
    {
        int n = moons.Count;
        if (n == 0) return;
        float total = 0f;
        for (int i = 0; i < n; i++) total += 1f / MoonAspect(moons[i]);
        if (total < 0.001f) total = n;

        float used = 0f;
        for (int i = 0; i < n; i++)
        {
            var m = moons[i];
            float ch = (i == n - 1) ? region.height - used : region.height * ((1f / MoonAspect(m)) / total);
            float y = region.y + region.height - used - ch;   // first moon at the top
            if (moonFrame.TryGetValue(m, out var f) && f != null)
            {
                PlaceFrame(f, new Rect(region.x, y, region.width, ch));
                ApplyMoonSize(m);
            }
            used += ch;
        }
    }

    // A body's surface aspect ratio (width / height), clamped to a sane band so one freakishly long map
    // can't starve the others of column width (or height, when stacked).
    static float MoonAspect(CelestialBody b)
    {
        if (b?.surface == null || b.surface.height < 1) return 1.5f;
        return Mathf.Clamp(b.surface.width / (float)b.surface.height, 0.5f, 3f);
    }

    // The floating controls (zoom bar, view-format button, tab strip) have to stay above the panes, which
    // are (re)ordered as tabs open and close.
    void KeepControlsOnTop()
    {
        if (zoomBar != null) zoomBar.SetAsLastSibling();
        if (viewFormatBtn != null) viewFormatBtn.SetAsLastSibling();
        if (moonTabStrip != null) moonTabStrip.SetAsLastSibling();
    }

    // Is the cursor over one of the floating map controls (zoom bar, view-format button, tab strip, Build
    // confirm)? The moon zoom/pan uses geometry, not raycasts, so it must skip these the way the host does —
    // otherwise scrolling the zoom bar or pressing a tab that overlays a moon frame would also move that map.
    bool OverMapChrome(Vector2 p)
    {
        if (zoomBar != null && zoomBar.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(zoomBar, p, null)) return true;
        if (viewFormatBtn != null && viewFormatBtn.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(viewFormatBtn, p, null)) return true;
        if (moonTabStrip != null && RectTransformUtility.RectangleContainsScreenPoint(moonTabStrip, p, null)) return true;
        if (confirmPanel != null && confirmPanel.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(confirmPanel, p, null)) return true;
        return false;
    }

    // Place a frame into a grid cell, inset by the hairline gap so the themed background reads as thin grout
    // rather than dead space. Cells are gridHolder-local, origin bottom-left; the frame is centre-pivoted so
    // its content's centre-relative pan lines up with a cursor point measured against the frame (the same
    // relationship the host map has with hostViewport).
    void PlaceFrame(RectTransform f, Rect cell)
    {
        f.anchorMin = f.anchorMax = new Vector2(0f, 0f);
        f.pivot = new Vector2(0.5f, 0.5f);
        f.sizeDelta = new Vector2(Mathf.Max(1f, cell.width - PaneGap), Mathf.Max(1f, cell.height - PaneGap));
        f.anchoredPosition = new Vector2(cell.x + cell.width * 0.5f, cell.y + cell.height * 0.5f);
        f.gameObject.SetActive(true);
    }

    // Bring the moon frames in line with the open set: build one for each open moon that lacks one, destroy
    // any whose tab was closed. The planet is not a moon frame — it uses hostViewport / mapRT.
    void RebuildMoonPanes()
    {
        List<CelestialBody> stale = null;
        foreach (var kv in moonFrame)
            if (!openMaps.Contains(kv.Key)) (stale ??= new List<CelestialBody>()).Add(kv.Key);
        if (stale != null)
            foreach (var m in stale)
            {
                if (moonFrame.TryGetValue(m, out var f) && f != null) Destroy(f.gameObject);
                if (moonTex.TryGetValue(m, out var t) && t != null) Destroy(t);
                moonFrame.Remove(m); moonImg.Remove(m); moonTex.Remove(m);
            }

        foreach (var m in openMaps)
        {
            if (m == body || moonFrame.ContainsKey(m)) continue;

            var frame = UIFactory.NewUI(moonLayer, "MoonFrame").GetComponent<RectTransform>();
            frame.gameObject.AddComponent<RectMask2D>();

            var contentGO = UIFactory.NewUI(frame, "MoonMap");
            var img = contentGO.AddComponent<RawImage>();
            img.raycastTarget = true;   // a click on a moon map is UI, not a click-away that closes the window
            Texture2D tex = m.surface != null ? SurfaceTextureRenderer.BuildGrid(m) : null;
            img.texture = tex;
            if (tex == null) img.color = new Color(0.10f, 0.12f, 0.16f, 1f);
            var crt = img.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = Vector2.zero;

            moonFrame[m] = frame; moonImg[m] = img; moonTex[m] = tex;
        }
    }

    void ClearMoonPanes()
    {
        foreach (var kv in moonFrame) if (kv.Value != null) Destroy(kv.Value.gameObject);
        foreach (var kv in moonTex) if (kv.Value != null) Destroy(kv.Value);
        moonFrame.Clear(); moonImg.Clear(); moonTex.Clear();
    }

    // Fit a moon map inside its fixed frame and apply its own zoom (px per cell), clipped by the frame's
    // mask. The DEFAULT view is cover (fills the frame, no dead space); you can zoom OUT to contain (the
    // whole moon map visible inside the frame, letterboxed) or IN past cover to fewer, larger cells.
    // Mirrors the host's ApplyMapSize / ClampPan.
    void ApplyMoonSize(CelestialBody m)
    {
        if (m?.surface == null) return;
        if (!moonFrame.TryGetValue(m, out var frame) || frame == null) return;
        if (!moonImg.TryGetValue(m, out var img) || img == null) return;

        Rect fr = frame.rect;
        float floor = ContainFit(fr, m);                        // fully out = whole map fits
        float cover = CoverFit(fr, m);                          // default view = fills the frame
        float max = Mathf.Max(cover, CeilTilePx(fr));
        float tpx = moonTilePx.TryGetValue(m, out float z) ? z : 0f;
        tpx = Mathf.Clamp(tpx <= 0f ? cover : tpx, floor, max);
        moonTilePx[m] = tpx;

        img.rectTransform.sizeDelta = new Vector2(m.surface.width * tpx, m.surface.height * tpx);
        Vector2 pan = moonPan.TryGetValue(m, out Vector2 pv) ? pv : Vector2.zero;
        ClampPanePan(fr, img.rectTransform, ref pan);
        moonPan[m] = pan;
        SyncMoonMirrors(img, fr);
    }

    // Pixels per cell at which a map exactly COVERS a frame (fills it, cropping the proportionally longer
    // axis) — the framed DEFAULT view, so there's no dead space around a map until you zoom out to fit.
    float CoverFit(Rect frame, CelestialBody b)
    {
        if (b?.surface == null || frame.width < 1f || frame.height < 1f) return 4f;
        return Mathf.Max(frame.width / b.surface.width, frame.height / b.surface.height);
    }

    // Pixels per cell at which the WHOLE map fits inside a frame (letterboxed on the shorter axis) — the
    // zoom-out floor, so you can always pull back to see the entire map within its fixed window.
    float ContainFit(Rect frame, CelestialBody b)
    {
        if (b?.surface == null || frame.width < 1f || frame.height < 1f) return 4f;
        return Mathf.Min(frame.width / b.surface.width, frame.height / b.surface.height);
    }

    // Pixels per cell at the zoomed-all-the-way-IN end (~MaxVisibleTiles cells fill the frame).
    float CeilTilePx(Rect frame) => Mathf.Sqrt(Mathf.Max(1f, frame.width * frame.height) / MaxVisibleTiles);

    // Keep content covering its frame: clamp the pan so you can never drag past the map's own edge into
    // letterbox. When the map is exactly frame-sized on an axis there's no slack, so it stays centred.
    void ClampPanePan(Rect frame, RectTransform content, ref Vector2 pan)
    {
        Vector2 size = content.sizeDelta;

        // Longitude wraps here exactly as it does on the host map: a moon is a cylinder too, its terrain
        // is generated to join at the seam by the same sampler, and a moon map that stops dead at its edge
        // while the planet's loops would just look like one of them is broken.
        if (PaneWrapEnabled(frame, size) )
            pan.x = Mathf.Repeat(pan.x + size.x * 0.5f, size.x) - size.x * 0.5f;
        else
        {
            float sx = Mathf.Max(0f, (size.x - frame.width) * 0.5f);
            pan.x = Mathf.Clamp(pan.x, -sx, sx);
        }

        float sy = Mathf.Max(0f, (size.y - frame.height) * 0.5f);
        pan.y = Mathf.Clamp(pan.y, -sy, sy);
        content.anchoredPosition = pan;
    }

    /// Same rule as the host map: wrapping only means something once the map is at least as wide as the
    /// frame showing it. Below that the whole moon already fits and there is no edge to run off.
    static bool PaneWrapEnabled(Rect frame, Vector2 size)
        => size.x > 0.5f && size.x >= frame.width - 0.5f;

    /// Give a moon's map the same two wrap mirrors the host map has, and keep them in step.
    ///
    /// Terrain only — a moon pane draws no structures, ghost or markers, so unlike the host there is
    /// nothing else to mirror. Idempotent: it creates the mirrors on first call and thereafter just
    /// re-syncs them, so ApplyMoonSize can call it unconditionally.
    void SyncMoonMirrors(RawImage img, Rect frame)
    {
        if (img == null) return;
        var rt = img.rectTransform;
        bool on = PaneWrapEnabled(frame, rt.sizeDelta);

        for (int i = 0; i < 2; i++)
        {
            string nm = i == 0 ? "WrapL" : "WrapR";
            var child = rt.Find(nm) as RectTransform;
            if (child == null)
            {
                if (!on) continue;                       // don't build mirrors a map will never need
                child = UIFactory.NewUI(rt, nm).GetComponent<RectTransform>();
                UIFactory.Stretch(child);
                var ri = child.gameObject.AddComponent<RawImage>();
                ri.raycastTarget = false;                // moon panes are viewers, not build surfaces
            }

            var mi = child.GetComponent<RawImage>();
            if (child.gameObject.activeSelf != on) child.gameObject.SetActive(on);
            if (!on) continue;

            float dx = i == 0 ? -rt.rect.width : rt.rect.width;
            child.offsetMin = new Vector2(dx, 0f);
            child.offsetMax = new Vector2(dx, 0f);
            if (mi != null) { mi.texture = img.texture; mi.color = img.color; }
        }
    }

    // Is the cursor over an open moon frame? Keeps PollMapPan from starting a host pan at a shared edge.
    bool OverAnyMoonFrame(Vector2 screenPos)
    {
        foreach (var kv in moonFrame)
            if (kv.Value != null && RectTransformUtility.RectangleContainsScreenPoint(kv.Value, screenPos, null))
                return true;
        return false;
    }

    // Scroll to zoom the cells inside a moon's fixed frame, drag to pan — independently per open moon, the
    // same gestures and cursor-anchored zoom the host map answers to. The content is centre-anchored in its
    // own frame, so the cursor pin is measured against the frame, exactly like the host's hostViewport.
    void PollMoonZoomPan()
    {
        if (moonFrame.Count == 0) { moonPanDrag = null; return; }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (!Mathf.Approximately(scroll, 0f) && !UIScroll.PointerOverScroller() && !OverMapChrome(Input.mousePosition))
        {
            foreach (var m in openMaps)
            {
                if (m == body) continue;
                if (!moonFrame.TryGetValue(m, out var frame) || frame == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(frame, Input.mousePosition, null)) continue;

                Rect fr = frame.rect;
                float floor = ContainFit(fr, m);                    // zoom-out floor = whole map fits
                float cover = CoverFit(fr, m);                      // default view = fills the frame
                float max = Mathf.Max(cover, CeilTilePx(fr));
                float cur = moonTilePx.TryGetValue(m, out float z) ? z : cover;
                float next = Mathf.Clamp(cur * Mathf.Pow(ZoomStep, scroll * 10f), floor, max);
                if (!Mathf.Approximately(next, cur))
                {
                    Vector2 pan0 = moonPan.TryGetValue(m, out Vector2 pv0) ? pv0 : Vector2.zero;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            frame, Input.mousePosition, null, out Vector2 framePoint))
                    {
                        Vector2 mapPoint = framePoint - pan0;                    // cursor in map space, pre-zoom
                        moonPan[m] = framePoint - mapPoint * (next / cur);       // ...still under the cursor after
                    }
                    moonTilePx[m] = next;
                    ApplyMoonSize(m);
                }
                activePane = m;
                break;   // only the map under the cursor zooms
            }
        }

        if (Input.GetMouseButtonDown(0) && !OverMapChrome(Input.mousePosition))
        {
            foreach (var m in openMaps)
            {
                if (m == body) continue;
                if (!moonFrame.TryGetValue(m, out var frame) || frame == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(frame, Input.mousePosition, null)) continue;
                moonPanDrag = m;
                moonPanGrabScreen = Input.mousePosition;
                moonPanGrabOffset = moonPan.TryGetValue(m, out Vector2 pv) ? pv : Vector2.zero;
                break;
            }
        }

        if (moonPanDrag != null)
        {
            if (!Input.GetMouseButton(0)) moonPanDrag = null;
            else if (moonFrame.ContainsKey(moonPanDrag))
            {
                Vector2 delta = (Vector2)Input.mousePosition - moonPanGrabScreen;
                moonPan[moonPanDrag] = moonPanGrabOffset + delta;
                ApplyMoonSize(moonPanDrag);
            }
        }
    }

    // ---- Small helpers, matching the Inspector's vocabulary ----
    void Header(string t) => UIFactory.WrapText(sidePanel, $"<b>{t}</b>", UITheme.SmallSize, UITheme.Accent);
    void Note(string t) => UIFactory.WrapText(sidePanel, t, UITheme.SmallSize, UITheme.SubText);
    void Note(Transform p, string t) => UIFactory.WrapText(p, t, UITheme.SmallSize, UITheme.SubText);

    Transform Card()
    {
        var card = UIFactory.Panel(sidePanel, "Card", UITheme.RowBg);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 5, 5); vlg.spacing = 2;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return card.transform;
    }

    void Stat(Transform parent, string label, System.Func<string> value)
    {
        var t = UIFactory.WrapText(parent, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () => $"<color=#9FB4C8>{label}:</color> {value()}");
    }

    // A progress bar bound to live values. Same shape as InspectorWindow's, but this window is a
    // separate class — it can't borrow that one, and the health bars in the infrastructure list need it.
    Image Bar(Transform parent, System.Func<(float t, string text, Color color)> eval)
    {
        var holder = UIFactory.NewUI(parent, "Bar");
        UIFactory.AddLayout(holder, 14);
        var track = UIFactory.Panel(holder.transform, "Track", UITheme.TrackBg);
        UIFactory.Stretch(track.rectTransform);
        var fill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
        var frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        var label = UIFactory.Text(holder.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Center);
        UIFactory.Stretch(label.rectTransform);
        live.Bar(fill, eval, label);
        return fill;
    }
}

// Turns clicks on the map into grid placements. Hover is POLLED by the window each frame rather than
// handled here: IPointerMoveHandler dispatch depends on which input module the project uses, and the
// ghost following the cursor is far too central to this screen to hang on that.
public class SurfaceGridProbe : MonoBehaviour, IPointerClickHandler
{
    PlanetViewWindow window;
    RectTransform mapRT;

    public void Init(PlanetViewWindow w, RectTransform rt) { window = w; mapRT = rt; }

    public void OnPointerClick(PointerEventData e)
    {
        // Right-click is rotation, handled in the window's Update so it works off the map too.
        if (e.button != PointerEventData.InputButton.Left) return;
        if (window != null && window.ScreenToCell(e.position, e.pressEventCamera, out int x, out int y))
            window.OnGridClick(x, y);
    }
}

// Hovering a moon tab shows its name and a description of the kind of moon it is, anchored the same way
// as the tile hover-info window (Raptok's follow-up request). `CelestialBodyType` has no moon sub-types
// (a moon is just `Moon`), so the "kind" is read off the moon's own generated terrain — its most common
// biome — rather than inventing a new taxonomy or asset.
public class MoonTabHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public CelestialBody moon;

    RectTransform rt, borderLight;
    Outline outline;
    bool hover;
    float t;

    static readonly Color Spark = new Color(0.88f, 0.97f, 1.00f, 1.00f);    // the travelling light
    const float LapsPerSec = 0.55f;   // how fast the light circles the border
    const float HoverScale = 1.14f;

    void Awake()
    {
        rt = GetComponent<RectTransform>();

        // A standout border around the little terrain thumbnail, so a moon tab reads as its own thing.
        // Its COLOUR is set per-moon in Configure() (each tab a different, stable colour).
        outline = UIFactory.Ensure<Outline>(gameObject);
        outline.effectDistance = new Vector2(1.6f, -1.6f);
        outline.effectColor = new Color(0.55f, 0.85f, 1.00f, 0.95f);   // placeholder until Configure

        // A small bright light that travels the border clockwise — built once, moved every frame.
        var go = UIFactory.NewUI(rt, "BorderLight");
        borderLight = go.GetComponent<RectTransform>();
        borderLight.anchorMin = borderLight.anchorMax = new Vector2(0.5f, 0.5f);
        borderLight.pivot = new Vector2(0.5f, 0.5f);
        borderLight.sizeDelta = new Vector2(5f, 5f);
        var img = go.AddComponent<Image>();
        img.color = Spark;
        img.raycastTarget = false;
    }

    // Per-moon styling, applied once the moon is known (called right after the component is added, since
    // Awake runs before `moon` is set): a stable, well-spread border COLOUR and a different STARTING point
    // for the border light — so no two moon tabs share a colour or run their light in sync.
    public void Configure(CelestialBody m)
    {
        moon = m;
        float seed = m != null ? Mathf.Abs(m.terrainSeed) : 0f;
        if (seed <= 0.0001f && m != null && m.name != null) seed = Mathf.Abs(m.name.GetHashCode() % 9973);
        float hue = Frac(seed * 0.6180339887f);       // golden-ratio scatter -> well-separated hues
        t = Frac(seed * 0.37f + 0.13f);               // this tab's light starts at a different point
        if (outline != null)
        {
            var c = Color.HSVToRGB(hue, 0.6f, 1f);
            c.a = 0.95f;
            outline.effectColor = c;
        }
    }

    static float Frac(float v) => v - Mathf.Floor(v);

    void Update()
    {
        if (rt == null) return;

        // Clockwise around the border.
        t += Time.unscaledDeltaTime * LapsPerSec;
        if (borderLight != null) borderLight.anchoredPosition = Perimeter(Mathf.Repeat(t, 1f));

        // Hover: a subtle grow so the tab reads as clickable. localScale (not layout size) so it never
        // reflows the neighbours — the other tabs keep sitting neatly together.
        float target = hover ? HoverScale : 1f;
        float k = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
        rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one * target, k);
    }

    // A point on the tab's border rectangle, clockwise from the top-left corner, `f` in [0,1).
    Vector2 Perimeter(float f)
    {
        var r = rt.rect;
        float hw = r.width * 0.5f - 2f, hh = r.height * 0.5f - 2f;
        if (hw < 1f || hh < 1f) return Vector2.zero;
        float eh = 2f * hw, ev = 2f * hh, per = 2f * (eh + ev);
        float d = f * per;
        if (d < eh) return new Vector2(-hw + d, hh);      // top    L -> R
        d -= eh;
        if (d < ev) return new Vector2(hw, hh - d);       // right  T -> B
        d -= ev;
        if (d < eh) return new Vector2(hw - d, -hh);      // bottom R -> L
        d -= eh;
        return new Vector2(-hw, -hh + d);                 // left   B -> T
    }

    public void OnPointerEnter(PointerEventData e)
    {
        hover = true;
        if (moon != null) MapHoverPanel.Instance.ShowAtCursor(Tooltip(moon));
    }

    // Everything worth knowing about a world at a glance, so hovering its tab reads like a survey card:
    // what it IS, whether it could be lived on, how warm it runs, how big it is, what it's made of, and
    // whether anyone holds it yet. Detail is gated behind a survey (Dev Mode reveals all) — an unmapped
    // world says so honestly rather than inventing numbers. Used for the planet tab and the moon tabs.
    static string Tooltip(CelestialBody m)
    {
        if (m == null) return "An uncharted world.";
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b>{m.name}</b>  <color=#7E93A8>{TerraformDiagnosis.Pretty(m)}</color>");

        if (!m.Surveyed)
        {
            sb.Append("\n<color=#9FB4C8>Unsurveyed</color> — survey to reveal details.");
            return sb.ToString();
        }

        sb.Append('\n').Append(Describe(m));

        // Habitability + whether it physically sits inside the star's habitable zone.
        string zone = m.isHabitable ? "in the habitable zone" : "outside the habitable zone";
        sb.Append($"\n<color=#9FB4C8>Habitability</color> {m.habitability:F0}%  <color=#7E93A8>({zone})</color>");

        // Temperature, tinted by the same gradient the map uses.
        float c = PlanetTemperature.BodyAverageCelsius(m);
        string tempHex = ColorUtility.ToHtmlStringRGB(PlanetTemperature.GradientColor(c));
        sb.Append($"\n<color=#9FB4C8>Temperature</color> <color=#{tempHex}>{PlanetTemperature.Label(c)}</color>");

        // How far it could be pushed toward livable for the CURRENT species.
        sb.Append($"\n<color=#9FB4C8>Terraformability</color> {m.terraformability:F0}%");

        // Surface extent and what it's made of.
        sb.Append($"\n<color=#9FB4C8>Mass</color> {MassWord(m.mass)}");
        string res = ResourceSummary(m.resources);
        if (res != null) sb.Append($"\n<color=#9FB4C8>Resources</color> {res}");

        // Who, if anyone, holds or lives here.
        if (m.settled && m.population > 0)
            sb.Append($"\n<color=#9FB4C8>Colony</color> {Population.Format(m.population)}");
        else if (m.owner != null)
            sb.Append($"\n<color=#9FB4C8>Claimed by</color> {FactionManager.OwnerName(m.owner)}");

        return sb.ToString();
    }

    static string SizeWord(int cells)
    {
        if (cells <= 0) return "unknown";
        if (cells < 24) return "small";
        if (cells < 40) return "medium";
        if (cells < 56) return "large";
        return "vast";
    }

    // MoonTabHover is its own class, so it needs its own copy of the Mass descriptor (mirrors
    // PlanetViewWindow.MassWord). Kept identical so a moon reads the same in the hover card as in the panel.
    static string MassWord(float mass)
    {
        string w = mass < 0.5f ? "Tiny" : mass < 1.5f ? "Small" : mass < 4f ? "Medium" : mass < 7f ? "Large" : "Giant";
        return $"{w} ({MassRules.Format(mass)})";
    }

    static string ResourceSummary(ResourceDeposit d)
    {
        if (d == null) return null;
        var parts = new List<string>();
        AddRes(parts, "Metal",  d.Get(ResourceType.Metal));
        AddRes(parts, "Energy", d.Get(ResourceType.Energy));
        AddRes(parts, "Water",  d.Get(ResourceType.Water));
        return parts.Count > 0 ? string.Join(", ", parts) : "none of note";
    }

    static void AddRes(List<string> parts, string name, float v)
    {
        if (v <= 0f) return;
        string grade = v >= 70f ? "rich" : v >= 35f ? "moderate" : "trace";
        parts.Add($"{name} <color=#7E93A8>({grade})</color>");
    }

    public void OnPointerExit(PointerEventData e)
    {
        hover = false;
        MapHoverPanel.Instance.Hide();
    }

    static string Describe(CelestialBody m)
    {
        if (m?.surface?.tiles == null) return "Uncharted — no surface survey yet.";

        var counts = new Dictionary<TerrainType, int>();
        int w = m.surface.width, h = m.surface.height;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = m.surface.tiles[x, y].type;
                counts.TryGetValue(t, out int c);
                counts[t] = c + 1;
            }

        TerrainType dominant = TerrainType.Barren;
        int best = -1;
        foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; dominant = kv.Key; }

        return $"{dominant} terrain — {TerrainColorMap.Describe(dominant)}";
    }
}


// The glow on a point of interest's ground: a slow breath at rest, and a hard fast pulse for a few
// seconds after the player clicks "Show on map" so the eye finds it immediately.
//
// Drives ONE CanvasGroup covering the whole site rather than tinting each tile. A colour write dirties
// a Graphic and forces a canvas re-batch; a couple of ruin fields is already ~40 tiles, and re-tinting
// all of them every frame on the canvas that also carries the side panel is a cost that only shows up
// once the map is busy. One alpha write per site does the same job.
public class SitePulse : MonoBehaviour
{
    PlanetViewWindow owner;
    PointOfInterest poi;
    CanvasGroup group;

    public void Init(PlanetViewWindow window, PointOfInterest site, CanvasGroup g)
    {
        owner = window; poi = site; group = g;
    }

    void Update()
    {
        if (group == null) return;

        // Unscaled, so a site keeps breathing while the game is paused — which is exactly when someone
        // is studying a map.
        bool emphasised = owner != null && owner.IsSitePulsing(poi);
        float speed = emphasised ? 7f : 1.6f;
        float depth = emphasised ? 0.38f : 0.10f;
        float mid = emphasised ? 0.78f : 0.62f;
        group.alpha = Mathf.Clamp01(mid + (Mathf.Sin(Time.unscaledTime * speed) - 0.5f) * depth);
    }
}

// Hovering a site's ground reports it, using the same text the list card carries so the map and the
// list can never say different things about the same place.
public class SiteHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler,
                                        UnityEngine.EventSystems.IPointerExitHandler
{
    PlanetViewWindow owner;
    PointOfInterest poi;

    public void Init(PlanetViewWindow window, PointOfInterest site) { owner = window; poi = site; }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
    {
        if (owner == null || poi == null) return;
        string text = owner.SiteTooltip(poi);
        if (!string.IsNullOrEmpty(text)) TooltipManager.Instance?.ShowAtCursor(text);
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => TooltipManager.Instance?.Hide();

    // A destroyed object never gets OnPointerExit, and these are destroyed wholesale on every rebuild —
    // so without this the tooltip sticks on screen after a site is refreshed out from under the cursor.
    void OnDisable() { TooltipManager.Instance?.Hide(); }
}
