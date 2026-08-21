using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// ============================================================================================
// PLANET VIEW — the surface map you actually develop a world on.
//
// Four tabs over one shared grid (plus a Dev-Mode-only terrain sandbox), in a strip UNDER the map:
//
//   OVERVIEW — what this world is: name, type, size, climate, weather, and how its colony is doing.
//   BUILD    — pick a structure, then place it. The tray is a row of COLOURED CATEGORY TABS (see
//              SurfaceBuildingCategory) rather than one long list. The selected building follows the
//              cursor as a GHOST: snapped to the mouse while it's over open UI, snapped to the GRID once
//              it's over the map. Right-click rotates it at any point, before or after it snaps.
//              Left-click commits. Footprints are tetromino-like, so packing a dense city is a puzzle.
//   SURVEY   — the index overlays. Each paints the grid with its own colour ramp so you can see, at a
//              glance, where a mine or a geothermal plant or a farm actually wants to go. The Sites list
//              and the power grid are folded in here.
//   ORBIT    — what is in orbit, and the shipyard.
//
// Which tabs you may open depends on how far you have got with the world — see TabAvailable, which is
// also the one place that answers "why can't I click that?" in words.
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

                // THE ONE CASE WHERE BUILD OPENS ON AN UNSETTLED WORLD, and the reason the rule below it
                // has an exception at all. A colony ship in orbit waiting for a landing site needs this
                // tab to place itself, and it cannot make the world settled first — placing the hull is
                // what settles it (UnitManager.FinishColonyLanding). Without this the tab that the
                // landing REQUIRES is greyed out with "nobody lives here to build anything", which is
                // true and completely unhelpful.
                if (ColonyLanding.AwaitingOn(body)) return true;

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

    // Placement Mode's two layers: the translucent guidance grids under the ghost, and the numbers over
    // it (the per-tile yields, the size counter, the refusal label). See DrawPlacement.
    RectTransform placementLayer, placementHud;

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

        /// The blacked-out grid of an unsurveyed world. EVERY layer that covers the map has to be
        /// mirrored or the seam becomes a hole in it: without this one the wrapped strip showed the
        /// terrain of an unsurveyed world in full daylight beside the fogged copy of itself, which
        /// reads as half the map being lit differently rather than as a missing overlay.
        public RawImage fog;
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

    // Which category the Build tab's structure tray is showing. Persists across rebuilds AND across
    // worlds: laying out a city means placing six power pieces in a row, and being dropped back on Civil
    // after each one would be maddening. Civil is the opening category because it is where a new colony
    // starts — habitats and storage before reactors.
    SurfaceBuildingCategory buildCategory = SurfaceBuildingCategory.Civil;

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

    // A moon is surveyed exactly as a planet is, so its pane needs the same blackout. Its own layer and
    // its own texture per moon, because each moon has its own grid and its own survey progress.
    readonly Dictionary<CelestialBody, RawImage> moonFog = new Dictionary<CelestialBody, RawImage>();
    readonly Dictionary<CelestialBody, Texture2D> moonFogTex = new Dictionary<CelestialBody, Texture2D>();
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
        ((tab == Tab.Survey && activeIndex == SurfaceIndexKind.Mineral) ||
         (tab == Tab.Build && CarryingMiningPiece));
    int rotation;
    Vector2Int hoverCell = new Vector2Int(-1, -1);
    bool hoverValid;

    /// Is `hoverCell` a cell that exists on the world this window is showing RIGHT NOW?
    ///
    /// Every read of hoverCell has to go through this rather than the old `hoverCell.x >= 0`, because
    /// "the cursor was over a cell" and "that cell is still on the map" are different questions. PollHover
    /// clamps the cell it stores, so it is always valid for the surface it was read from — and then the
    /// surface underneath it changes without the cursor moving:
    ///
    ///   the window is pointed at another world (a moon tab, a different planet), whose grid is smaller;
    ///   the surface is regenerated at a different resolution from the Survey tab.
    ///
    /// Either leaves a cell from a 128-wide map indexing a 64-wide one, and `tiles[x, y]` throws — once
    /// per frame, from a live text callback, which is how a stale hover turns into a console full of
    /// IndexOutOfRangeException rather than a single visible glitch.
    bool HasHoverCell => body != null && body.surface != null && body.surface.tiles != null
                      && hoverCell.x >= 0 && hoverCell.y >= 0
                      && hoverCell.x < body.surface.width && hoverCell.y < body.surface.height;

    // Survey-mode state.
    // Kept only so the Survey tab's cards still have something to reflect; the OVERLAY is driven by
    // IndexToggles now. Clicking a card sets both, so the tab and the icon bar agree.
    SurfaceIndexKind activeIndex = SurfaceIndexKind.None;
    readonly List<SurfaceIndexKind> scratchKinds = new List<SurfaceIndexKind>();
    // The Power grid is now a Survey overlay rather than its own tab: this flag is the "showing the power
    // grid" option. NOT exclusive with the index ramps any more — the grid has its own layer above the
    // buildings while an index ramp sits below them, so both can be read at once.
    /// How strongly the power grid washes over everything under it.
    ///
    /// The overlay paints its own per-texel alphas — a dull ground at 0.62, lit tiles at 0.34-0.42, the
    /// rim brighter still — and this scales all of them at once on the RawImage, so their RELATIVE
    /// strengths, which are what make the map readable, are untouched. Halving them is what lets a
    /// ground index stay legible underneath: the two are shown together far more often than not, and at
    /// full strength the grid simply erased whichever index the player had also asked for.
    const float PowerOverlayAlpha = 0.5f;

    /// Texels a single open MOON pane may spend on its map. A quarter of the host's, because several
    /// moons can be open at once and each is a fraction of the window — a moon-sized grid still gets the
    /// tile art at its native resolution, which is the point.
    const int MoonPaneTexelBudget = 2 * 1024 * 1024;

    bool showPowerOverlay;
    readonly LiveSet live = new LiveSet();
    string lastSig = null;
    Texture2D overlayTex;
    // The power grid's own layer and texture — see the note where it is built. Separate from
    // overlayTex because the two are drawn at once now, at different depths.
    RawImage powerOverlayImage;
    Texture2D powerTex;
    float powerRepaintIn;   // see Update: the power overlay repaints on a timer, not every frame
    Color[] powerPx;        // reused scratch for that repaint — see RefreshPowerOverlay
    bool[] powerLit;        // ...and which tiles any grid reaches, for the edge pass
    Color[] powerOut;       // ...and the supersampled texels the rim is drawn into

    RawImage surveyFogImage;    // the veil over an unsurveyed world
    RectTransform surveyMarkerLayer;
    readonly List<RectTransform> surveyMarkers = new List<RectTransform>();
    readonly List<Image> surveyMarkerFills = new List<Image>();
    readonly List<Image> surveyMarkerEdges = new List<Image>();
    IndexIconBar hostIndexBar;
    float nextFogRepaint;
    readonly Dictionary<CelestialBody, IndexIconBar> moonIndexBar = new Dictionary<CelestialBody, IndexIconBar>();
    Texture2D surveyFogTex;
    Color32[] surveyFogPx;

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

    // The tab strip sits UNDER the map, between it and the status line — Raptok's layout, and the reason
    // the map column now stacks bottom-up as: status line, tabs, map.
    //
    // Tabs under the map rather than over it puts the control you click NEXT to the thing it changes:
    // the side panel's contents are what a tab switches, and the eye travels tab → panel across the
    // bottom-right corner instead of all the way back up over the map. It also gives the map the full
    // height of the column, since the content area already clears the title bar on its own.
    const float TabStripHeight = 26f;
    const float TabStripGap = 6f;                                    // between the tabs and the map above
    const float TabStripBottom = StatusMapBottom;                    // tabs sit directly on the status line
    const float MapBottom = TabStripBottom + TabStripHeight + TabStripGap;

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

        // Tabs — UNDER THE MAP (the left 3/4), sitting directly on top of the status line, per Raptok's
        // layout. Inset by PanelGap on the right exactly as the status line is, so the map, the tabs and
        // the status readout share one right edge and the side panel starts cleanly past all three.
        tabStrip = UIFactory.NewUI(content, "Tabs").GetComponent<RectTransform>();
        tabStrip.anchorMin = new Vector2(0, 0); tabStrip.anchorMax = new Vector2(MapFraction, 0);
        tabStrip.pivot = new Vector2(0.5f, 0);
        tabStrip.sizeDelta = new Vector2(-PanelGap, TabStripHeight);
        tabStrip.anchoredPosition = new Vector2(-PanelGap * 0.5f, TabStripBottom);
        var th = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 4; th.childControlWidth = true; th.childControlHeight = true; th.childForceExpandWidth = false;
        th.childAlignment = TextAnchor.MiddleLeft;   // tabs read left-to-right from the map's left edge

        // The VIEWPORT: a fixed window onto the surface, anchored to the LEFT and capped at 3/4 of the
        // window width (MapFraction). It never changes size — zooming scales the map INSIDE it, which is
        // what a map window should do. It used to resize the window itself, so zooming in on a big world
        // grew the panel off the edge of the screen.
        gridHolder = UIFactory.NewUI(content, "Viewport").GetComponent<RectTransform>();
        gridHolder.anchorMin = new Vector2(0, 0); gridHolder.anchorMax = new Vector2(MapFraction, 1);
        gridHolder.offsetMin = new Vector2(0, MapBottom);           // clear the tabs AND the status line below
        // Flush to the top of the content area: UIFactory.Window already insets `content` by 42 to clear
        // the title bar, so the old -32 here was reserving room for a tab strip that has moved to the
        // bottom. The map gets that height back.
        gridHolder.offsetMax = new Vector2(-PanelGap, 0);           // gap before the panel
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

        // ============================================================================================
        // THE UNSURVEYED GROUND, BLACKED OUT
        //
        // A world nobody has mapped shows its grid and nothing in it: one opaque cell per grid cell,
        // lifting cell by cell as the survey front crosses the world. Above the ground overlay so it
        // hides the indexes and the tectonics with the terrain — an unsurveyed world should not be
        // leaking its fault lines — and below everything else, because there is nothing else on a world
        // nobody has been to.
        // ============================================================================================
        var fogGO = UIFactory.NewUI(mapRT, "SurveyFog");
        surveyFogImage = fogGO.AddComponent<RawImage>();
        UIFactory.Stretch(surveyFogImage.rectTransform);
        surveyFogImage.raycastTarget = false;
        fogGO.SetActive(false);

        // ============================================================================================
        // THE BLOCK A SHIP IS WORKING, FRAMED AND PULSING
        //
        // A child of the MAP, not of the viewport, so it pans and zooms with the ground it is framing —
        // a marker that stayed put while the map moved under it would be pointing at the wrong place
        // the moment anybody scrolled.
        //
        // And a set of RectTransforms rather than pixels baked into the fog texture, which is the whole
        // reason this is affordable: the marker pulses every frame, and a pulsing texture would mean
        // rebuilding and re-uploading a two-hundred-thousand-texel image sixty times a second. Moving a
        // rectangle and changing two colours costs nothing.
        // ============================================================================================
        surveyMarkerLayer = UIFactory.NewUI(mapRT, "SurveyMarkers").GetComponent<RectTransform>();
        UIFactory.Stretch(surveyMarkerLayer);
        surveyMarkerLayer.gameObject.SetActive(false);

        // Points of interest sit ABOVE the terrain but BELOW the pieces: a site is GROUND, so a building
        // put on top of it should cover it. Built before the piece layer so hierarchy order says so.
        siteLayer = UIFactory.NewUI(mapRT, "Sites").GetComponent<RectTransform>();
        UIFactory.Stretch(siteLayer);

        // The index buttons, pinned to the top right of the VIEWPORT rather than of the map — furniture
        // belongs to the window, and a bar that panned off with the terrain would be a bar you have to
        // go and find. Parented to gridHolder rather than hostViewport so the viewport's RectMask2D
        // cannot clip it at the edges.
        hostIndexBar = IndexIconBar.Attach(gridHolder, body);

        // ============================================================================================
        // THE POWER GRID GETS ITS OWN LAYER, BETWEEN THE GROUND AND THE BUILDINGS
        //
        // Overlays used to share one image, which forced them to be mutually exclusive — and the loser
        // was always the one the player most needed. A Combustion Plant is Electrical (so it wants the
        // power map) but is sited on ORE (so it wants the Mineral map), and one of those had to be
        // thrown away. Same for a Farm, which needs fertile ground AND a grid connection, and for wind
        // and solar arrays, which need their own index AND somewhere to plug in.
        //
        // They cannot simply be composited into one texture either: a ground index and the grid are
        // different KINDS of fact and the player reads them together, so each needs its own depth.
        //
        // IT USED TO SIT ABOVE THE PIECES. The argument was that the grid describes which structures it
        // reaches, so hiding it behind those structures answers the question by obscuring it. True, but
        // it bought that read by paying a worse price: a full-strength wash over every building on the
        // map meant you could not see what was ALREADY BUILT, and the first thing a player does with the
        // power map open is look for somewhere to put the next thing. Losing the standing structures is
        // worse than losing the tint on top of them, so the pieces now draw over it — and the grid is
        // half-transparent (see PowerOverlayAlpha), which is what lets the ground index below it and the
        // buildings above it both stay readable through the wash.
        // ============================================================================================
        var pwGO = UIFactory.NewUI(mapRT, "PowerOverlay");
        powerOverlayImage = pwGO.AddComponent<RawImage>();
        UIFactory.Stretch(powerOverlayImage.rectTransform);
        powerOverlayImage.raycastTarget = false;
        powerOverlayImage.color = new Color(1f, 1f, 1f, PowerOverlayAlpha);
        pwGO.SetActive(false);

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

        // Above the pieces so the ring/arrow are never hidden behind a structure's own tiles.
        markerLayer = UIFactory.NewUI(mapRT, "Markers").GetComponent<RectTransform>();
        UIFactory.Stretch(markerLayer);
        var mlImg = markerLayer.gameObject.AddComponent<Image>();
        mlImg.color = new Color(0, 0, 0, 0); mlImg.raycastTarget = false;

        // PLACEMENT MODE'S GUIDANCE GRIDS, under the ghost so the footprint being drawn always reads on
        // top of the hints about where it may go next. Its own layer rather than sharing the ghost's,
        // because the two are rebuilt on different triggers: the ghost follows the cursor every frame,
        // while the guidance only moves when a tile is actually painted.
        placementLayer = UIFactory.NewUI(mapRT, "Placement").GetComponent<RectTransform>();
        UIFactory.Stretch(placementLayer);
        var pcImg = placementLayer.gameObject.AddComponent<Image>();
        pcImg.color = new Color(0, 0, 0, 0); pcImg.raycastTarget = false;

        ghostLayer = UIFactory.NewUI(mapRT, "Ghost").GetComponent<RectTransform>();
        UIFactory.Stretch(ghostLayer);
        var glImg = ghostLayer.gameObject.AddComponent<Image>();
        glImg.color = new Color(0, 0, 0, 0); glImg.raycastTarget = false;

        // The per-tile yield icons and the size counter. ABOVE the ghost: they are text over the very
        // footprint the ghost is filling, and a number the building draws over is not a readout.
        placementHud = UIFactory.NewUI(mapRT, "PlacementHUD").GetComponent<RectTransform>();
        UIFactory.Stretch(placementHud);
        var phImg = placementHud.gameObject.AddComponent<Image>();
        phImg.color = new Color(0, 0, 0, 0); phImg.raycastTarget = false;

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
        // Top-aligned with the map now that the tab strip has moved to the bottom of the map column. The
        // old -32 kept this level with a map that started 32px down; both start at the content top today,
        // so the panel and the map read as one row rather than one sitting proud of the other.
        sideHolder.offsetMax = new Vector2(0f, 0f);
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
        BuildPlacement.Cancel();   // static state must not outlive the window that drives it
        BuildDemolition.Cancel();
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
        // The window is the only way out of Placement Mode, so closing it has to end the session — a
        // session left open would keep answering IsFor() for a window nobody can see. Same for the
        // demolition selection, which would otherwise still be armed the next time the window opened.
        BuildPlacement.Cancel();
        BuildDemolition.Cancel();
    }

    public void ShowFor(CelestialBody b) => ShowFor(b, null);

    /// Open on a world, optionally landing on a specific tab. `openOn` is honoured only if that tab is
    /// actually available for this world — asking for Build on an unsettled rock lands on Info, which is
    /// the tab that can explain why.
    public void ShowFor(CelestialBody b, Tab? openOn)
    {
        body = b;
        selected = null; rotation = 0;
        // The cursor has not moved, but the map under it has been replaced. A cell remembered from the
        // last world is either out of bounds on this one — the readouts index tiles[x, y] directly — or,
        // worse, in bounds and quietly describing the wrong ground until the mouse next moves.
        hoverCell = new Vector2Int(-1, -1);
        hoverValid = false;
        showPowerOverlay = false;   // a fresh world opens on the plain map, not the last world's power view
        CancelPlace();          // a confirm from the last world means nothing on this one
        // ...and neither does a half-drawn footprint. The session holds cells in the OLD world's grid
        // coordinates, so carrying it over would draw a shape on this world at cells that mean nothing
        // here — and Confirm would then try to build it.
        BuildPlacement.Cancel();
        BuildDemolition.Cancel();   // ...and neither does a selection of the last world's tiles
        lastSig = null;

        // The tab you were on may not exist for THIS world — Build on your capital, then click a
        // barren rock, and Build has to give way to something readable rather than showing an empty
        // build list on a world nobody lives on.
        if (openOn.HasValue && TabAvailable(openOn.Value, out _)) tab = openOn.Value;
        else if (!TabAvailable(tab, out _)) tab = Tab.Overview;

        // A COLONY SHIP WAITING FOR A LANDING SITE OPENS WITH ITSELF ALREADY IN HAND.
        //
        // The whole point of the flow is "here is your world, put the ship somewhere" — making the player
        // find the Build tab and hunt for a building they cannot normally select would turn the one
        // decision into a scavenger hunt. Set AFTER the tab resolution above so it cannot be undone by it.
        if (ColonyLanding.AwaitingOn(b))
        {
            tab = Tab.Build;
            selected = SurfaceBuildingType.ColonyShipBase;
            rotation = 0;
        }
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

        // One CELL per build cell, read straight off the grid — so a 1x1 structure covers exactly one
        // terrain cell. The grid is now as fine as the detail render (see MapMetrics.Subdiv), so this
        // is the detailed map AND the build grid at once, rather than two maps six times apart.
        //
        // Textured rather than flat: each cell is filled with its biome's grain instead of one flat
        // texel. That changes the texture's RESOLUTION, not the grid — the one-to-one rule is intact,
        // this window's zoom and hit-testing work in cells and never touch texel counts, and the
        // renderer falls back to the flat build on any world too big to afford the extra texels.
        //
        // Rebuilt on every open rather than cached by body id, because terraforming can remodel a
        // world's terrain outright and a cache keyed only on identity would show the planet it used
        // to be.
        if (mapTex != null) Destroy(mapTex);
        mapTex = SurfaceTextureRenderer.BuildGridTextured(body);
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

        // Directly above the ground index, exactly where it sits on the real map — it hides the indexes
        // and the tectonics along with the terrain, and a mirror that let either show through would be
        // leaking an unsurveyed world's geology at the seam.
        var fg = UIFactory.NewUI(m.root, "SurveyFog");
        m.fog = fg.AddComponent<RawImage>();
        UIFactory.Stretch(m.fog.rectTransform);
        m.fog.raycastTarget = false;
        fg.SetActive(false);

        // Built BEFORE pieces, so they draw over it — matching the real map's stacking. Miss this and
        // the seam becomes a place where buildings vanish under the grid on one side of it and not the
        // other, which reads as a rendering fault rather than as a wrapped map.
        var p = UIFactory.NewUI(m.root, "PowerOverlay");
        m.power = p.AddComponent<RawImage>();
        UIFactory.Stretch(m.power.rectTransform);
        m.power.raycastTarget = false;
        p.SetActive(false);

        m.pieces = UIFactory.NewUI(m.root, "Pieces").GetComponent<RectTransform>();
        UIFactory.Stretch(m.pieces);

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

            if (m.fog != null && surveyFogImage != null)
            {
                bool fv = surveyFogImage.gameObject.activeSelf && surveyFogImage.texture != null;
                if (m.fog.gameObject.activeSelf != fv) m.fog.gameObject.SetActive(fv);
                m.fog.texture = surveyFogImage.texture;
                m.fog.color = surveyFogImage.color;
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
        // Demolition Mode reshapes the Build panel — the mode button's caption, the instruction under
        // the heading — so entering or leaving it has to rebuild the side panel. Without this the tray
        // would still be telling you to click a structure to pick it up while the map was in demolition.
        sb.Append(BuildDemolition.IsFor(body) ? 1 : 0).Append('|');
        scratchKinds.Clear();
        IndexToggles.Active(body, scratchKinds);
        for (int i = 0; i < scratchKinds.Count; i++) sb.Append((int)scratchKinds[i]).Append(',');
        sb.Append('|').Append((int)activeIndex).Append('|').Append(showPowerOverlay ? 1 : 0).Append('|').Append(body.Surveyed ? 1 : 0).Append('|').Append(body.deepSurveyed ? 1 : 0).Append('|');

        // A SURVEY IN PROGRESS IS A CHANGING PICTURE, and the whole point of it is that you can watch.
        // Quantised rather than raw so this is not a rebuild every frame: 200 steps across a level is
        // about a cell's worth of change on a mid-sized world, which is the granularity the map is drawn
        // at anyway. Without it the fog and the index passes would only redraw when something else in
        // the window happened to change shape.
        sb.Append(Mathf.FloorToInt(body.explorationProgress * 200f)).Append('|');
        sb.Append(Mathf.FloorToInt(body.deepProgress * 200f)).Append('|');

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

        // The build queue's SHAPE — how many jobs, of what, in what order. Confirming, cancelling,
        // reordering or completing a job all change the rows in the queue panel AND the ghosts on the
        // map, and both are structure rather than text, so both have to be rebuilt when this moves.
        //
        // Deliberately WITHOUT progress or pause state: elapsed changes every frame, and putting it here
        // would rebuild the entire side panel several times a second — the exact strobe the LiveSet
        // exists to prevent. Progress is live text and a live bar; pause is a live button caption.
        sb.Append('|');
        var queue = SurfaceBuildQueue.Peek(body);
        if (queue != null)
        {
            sb.Append(queue.Count).Append(':');
            foreach (var job in queue)
                if (job != null) sb.Append((int)job.type).Append('.').Append(job.Tiles).Append(',');
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

        // Every frame, deliberately, and outside the signature check. The marker PULSES and the block
        // under a working ship advances continuously — both are animations, and an animation gated on
        // "has anything changed enough to rebuild the map" would step at whatever rate the signature
        // happens to change at. Costs a rectangle move and two colour writes per surveying ship.
        RefreshSurveyMarkers();
        if (hostIndexBar != null) hostIndexBar.SetBody(body);

        // ---- the veil thins continuously, so it has to be repainted continuously ----------------
        //
        // But NOT every frame. The block under a ship clears over three and a half seconds, and eight
        // repaints a second is far smoother than the eye needs for a fade that slow — while sixty would
        // be re-uploading the whole fog texture sixty times a second, which is precisely the cost the
        // block rework was done to remove. The signature check cannot cover this because the fade is a
        // continuous quantity and a signature is a discrete one.
        if (Survey.InProgress(body) && Time.unscaledTime >= nextFogRepaint)
        {
            nextFogRepaint = Time.unscaledTime + 0.125f;
            RefreshSurveyFog();
        }

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
        // AFTER PollHover, which is what resolves `hoverCell` from the cursor — the drag reads that cell
        // every frame, so running first would draw one frame behind the mouse for the whole gesture.
        PollBuildDraw();
        PollDemolish();
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

        // The construction ghosts pulse and fill in as their jobs progress. Animated in place, like the
        // marker above and for the same reason: rebuilding the piece layer to move a build's opacity
        // would tear down and recreate every quad on the map several times a second.
        AnimateConstruction();

        // Rotate the held piece 90° — before it snaps to the grid and after. Handled here rather than on
        // the map so it works the moment you pick a building up, wherever the cursor happens to be.
        //
        // R ONLY. Rotate used to also be on right-click, and right-click is now the way OUT of placement
        // mode (below), which is the more valuable binding: rotating has a key sitting right under the
        // hand already, while leaving the mode meant travelling back to the tray to put the building
        // down. Not while a confirm is up: the panel is asking about a specific footprint at a specific
        // rotation, and rotating underneath the question would make the answer mean something else.
        if (tab == Tab.Build && selected.HasValue && !pendingType.HasValue && Input.GetKeyDown(KeyCode.R))
        {
            rotation = (rotation + 1) % 4;
            RecomputeHoverValidity();   // a rotated piece may now fit (or stop fitting) where it is
            SimpleAudio.Instance?.PlayTick();
        }

        // ---- RIGHT-CLICK LEAVES PLACEMENT MODE ----
        //
        // One press, all the way out: the piece is dropped and any drawn shape goes with it. Escape
        // keeps its one-step-at-a-time ladder (confirm, then shape, then piece) for the player who wants
        // to undo just the last thing; this is the other half of that pair — the quick way out for the
        // far more common "actually, not here, and not this".
        //
        // A confirm still gets backed out of first rather than dismissed along with everything else. It
        // is a question already on screen, and answering "no" to it is not the same gesture as
        // abandoning the build, so collapsing the two would make a stray click lose more than it looks
        // like it should.
        if (tab == Tab.Build && Input.GetMouseButtonDown(1))
        {
            if (pendingType.HasValue) { CancelPlace(); return; }
            if (selected.HasValue || (BuildPlacement.IsFor(body) && BuildPlacement.Tiles > 0))
            {
                DoCancelPlacement();
                return;
            }
        }

        // Escape backs out of the confirm first, and only then drops the held piece — one step at a
        // time, so cancelling a misclick doesn't also make you re-pick the building.
        if (pendingType.HasValue && Input.GetKeyDown(KeyCode.Escape)) { CancelPlace(); return; }

        // The same one-step-at-a-time rule for a drawn footprint: Escape throws away what is painted and
        // leaves you still holding the structure, ready to draw it somewhere else. A second Escape puts
        // the structure down. Collapsing the two would make a single slip of the shape cost the trip
        // back to the build tray as well.
        if (tab == Tab.Build && Input.GetKeyDown(KeyCode.Escape)
            && BuildPlacement.IsFor(body) && BuildPlacement.Tiles > 0)
        {
            BuildPlacement.ClearShape();
            SimpleAudio.Instance?.PlayTick();
            return;
        }

        // Escape drops the held piece.
        if (tab == Tab.Build && selected.HasValue && Input.GetKeyDown(KeyCode.Escape))
        {
            selected = null; CancelPlace(); BuildPlacement.Cancel(); lastSig = null; ClearGhost();
        }

        // Escape backs out of demolition the same way Cancel does — question first, then the mode.
        if (tab == Tab.Build && BuildDemolition.IsFor(body) && Input.GetKeyDown(KeyCode.Escape))
        {
            if (BuildDemolition.AwaitingSplitConfirm) BuildDemolition.CancelSplitConfirm();
            else if (BuildDemolition.Tiles > 0) BuildDemolition.ClearShape();
            else ExitDemolition();
            return;
        }

        if (tab == Tab.Build) { DrawGhost(); DrawPlacement(); }
        else
        {
            // Leaving the Build tab has to take the build layers with it. It didn't: DrawGhost was only
            // called on the Build tab, so whatever was on the ghost layer when you switched away stayed
            // painted on the map underneath the Survey overlay until you came back.
            ClearGhost();
            ClearLayer(placementLayer);
            ClearLayer(placementHud);
        }

        // THE NUMBERS FOLLOW THE CURSOR, on Survey as much as on Build. They used to be a Placement Mode
        // fixture, so surveying a world — the one activity that is entirely about reading these figures —
        // showed colours and no numbers at all, and you had to pick up a building you did not want in
        // order to read the ground. Called out here rather than inside DrawPlacement for exactly that
        // reason: it is not a placement decoration any more.
        RefreshYieldIcons();
        RefreshPlacePanel();
        RefreshDemolishPanel();

        // The power overlay's colour tracks each grid's LIVE supply, so it has to be repainted as the
        // economy moves rather than only when the window rebuilds. A few times a second is plenty: it's
        // following a number that drifts, and repainting up to 70,000 texels every frame to do it would
        // be a real cost for something the eye can't see happening anyway.
        if (PowerOverlayActive && body.surface != null)
        {
            powerRepaintIn -= Time.unscaledDeltaTime;
            if (powerRepaintIn <= 0f) { powerRepaintIn = 0.25f; RefreshPowerOverlay(); }
        }

        // The pylon-to-pylon lines, every frame while the grid is up. Unlike the overlay texture — which
        // is anchored in the map's normalised space and rescales itself — these are quads whose LENGTH
        // is in map pixels, so they have to be re-measured whenever the map is zoomed or panned. There
        // are a handful of them, and the ghost layer already rebuilds far more than this per frame.
        DrawNodeLinks(PowerOverlayActive && body.surface != null);

        UpdateStatus();
    }

    void UpdateStatus()
    {
        switch (tab)
        {
            case Tab.Build:
                if (BuildDemolition.IsFor(body))
                    statusText.text = DemolitionModeBanner();

                else if (!selected.HasValue)
                    statusText.text = "<color=#9FB4C8>Pick a structure on the right, then click the map to site it — you'll be asked to confirm. " +
                                      "Right-click rotates. Esc cancels.  ·  Scroll to zoom · drag the map to pan.</color>";

                // ============================================================================
                // PLACEMENT MODE: THE READOUT MOVES TO THE CURSOR
                //
                // Everything the branch below prints — the yield here, the index percentage, the
                // efficiency, what it will cost — used to live in this line under the map. Which meant
                // deciding where to put a mine was a matter of looking at the tile, then looking at the
                // bottom of the window, then back at the tile, for every candidate site. The two things
                // you are comparing were at opposite ends of the screen.
                //
                // So while a structure is held, all of it goes into the window locked to the mouse
                // (PlacementHoverText, shown by PollHover) and this line becomes what it should have
                // been: the MODE indicator, saying what you are doing and how to get out of it.
                // ============================================================================
                else if (UsesPlacementSession(SurfaceBuildingDatabase.Get(selected.Value)))
                    statusText.text = PlacementModeBanner();

                else
                {
                    var info = SurfaceBuildingDatabase.Get(selected.Value);
                    var sb = new System.Text.StringBuilder();

                    // ---- MID-DRAG: the size and the bill, live ----
                    //
                    // What it will cost is the one thing that cannot be worked out by looking at the map,
                    // and it is the number that decides how far to keep dragging. Shown while the button
                    // is still down, because afterwards it is too late to want a smaller farm.
                    if (drawing && drawCells.Count > 0)
                    {
                        int tiles = drawCells.Count;
                        if (info.drawMode == BuildDrawMode.NodeChain)
                        {
                            sb.Append($"<b>{info.name}</b> — <b>{tiles}</b> pylon{(tiles == 1 ? "" : "s")}");
                            sb.Append($" <size=10><color=#9FB4C8>· spaced to stay on one grid · release to build</color></size>");
                        }
                        else
                        {
                            float mult = BuildScaling.CostMultiplier(tiles);
                            int cm = Mathf.RoundToInt(ColonyManager.DiscCost(info.costMetal) * mult);
                            int ce = Mathf.RoundToInt(ColonyManager.DiscCost(info.costEnergy) * mult);
                            sb.Append($"<b>{info.name}</b> — <b>{tiles}</b> tile{(tiles == 1 ? "" : "s")}");
                            sb.Append($"  <color=#9FB4C8>{cm}m {ce}e · {GameCalendar.Duration(info.buildTime * mult * TechEffects.BuildTimeMult)}</color>");
                            sb.Append($"  <color=#8FD0FF>x{BuildScaling.OutputMultiplier(tiles):0.0} output</color>");
                        }

                        sb.Append(string.IsNullOrEmpty(drawWhy)
                            ? "\n<color=#4DFF6E>Release to build</color>"
                            : $"\n<color=#FF6659>{drawWhy}</color>");
                        statusText.text = sb.ToString();
                        break;
                    }

                    sb.Append($"<b>{info.name}</b>");

                    // The verb differs by class, so the hint has to. Telling a farm's player to
                    // right-click-rotate is telling them about a control that does nothing here.
                    if (IsDrawn(info))
                    {
                        string how =
                            info.drawMode == BuildDrawMode.NodeChain ? "press and drag to lay a run of pylons"
                          : info.drawMode == BuildDrawMode.Square ? $"press and drag out a square (min {MinSideFor(info)}x{MinSideFor(info)})"
                          : info.drawMode == BuildDrawMode.Rectangle ? "press and drag out a rectangle (min 2 wide both ways)"
                          : $"press and drag to draw it (min {info.minTiles} tiles)";
                        sb.Append($" <size=10><color=#9FB4C8>· {how} · Esc cancels</color></size>");
                    }
                    else
                        sb.Append($" · rot {rotation * 90}° <size=10><color=#9FB4C8>(R / right-click rotates · Esc cancels · middle-drag pans)</color></size>");

                    // WHAT THE MAP IS SHOWING YOU WHILE YOU HOLD THIS. The overlay switched to this
                    // building's own index the moment it was picked up, so name it — an unexplained
                    // coloured map is a decoration, and a named one is an instruction.
                    if (info.index != SurfaceIndexKind.None)
                    {
                        if (SurfaceIndex.Unlocked(body, info.index))
                        {
                            string ihex = ColorUtility.ToHtmlStringRGB(SurfaceIndex.Outline(info.index));
                            // "•", not "■": the Geometric Shapes block is missing from the LiberationSans
                            // atlas this project ships, so a square draws as a tofu box (see
                            // RefreshConfirmPanel, which settled on this same swatch glyph).
                            sb.Append($"  <size=10><color=#{ihex}>•</color><color=#9FB4C8> the map is showing this world's best " +
                                      $"{SurfaceIndex.Name(info.index)} ground</color></size>");
                        }
                        else
                            sb.Append($"  <size=10><color=#C9A94D>{SurfaceIndex.Name(info.index)} not surveyed — " +
                                      $"{SurfaceIndex.LockReason(body, info.index)}</color></size>");
                    }

                    if (HasHoverCell)
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

                        // A drawn class has nothing to say about a single hovered cell yet — the shape
                        // does not exist until the drag does, and "Left-click to build" would be a
                        // straight lie about a control that starts a drag rather than building anything.
                        if (IsDrawn(info))
                            sb.Append("   <color=#4DFF6E>Press and drag</color>");
                        else
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

                        // ...and for Geothermal, whose map is decided by two facts about the whole world:
                        // whether it has plates at all, and how strong its volcanic hotspots are. A world
                        // with neither shows an empty map, and "there is nothing here" and "there will
                        // never be anything here" are answers a player has to be able to tell apart
                        // before deciding whether to bring a science ship back.
                        if (activeIndex == SurfaceIndexKind.Geothermal)
                            sur.Append($"  <color=#9FB4C8>·</color> <b>{GeothermalMap.Label(body)}</b>");

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
                            if (HasHoverCell)
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

    // ============================================================================================
    // THE PLACEMENT MODE INDICATOR
    //
    // A short, loud line saying which mode the window is in, what is being placed, and the two ways out.
    // It replaces the detailed readout that used to live here, all of which has moved to the cursor.
    //
    // It says the SIZE as well, because the floating counter on the map only shows progress toward the
    // minimum and clamps there — past the minimum the only place the real tile count appears is here and
    // on the Confirm panel.
    // ============================================================================================
    string PlacementModeBanner()
    {
        var info = SurfaceBuildingDatabase.Get(selected.Value);
        string hex = ColorUtility.ToHtmlStringRGB(Vivid(info.color));
        var sb = new System.Text.StringBuilder();

        sb.Append($"<color=#4DFF6E><b>PLACEMENT MODE</b></color>  <color=#{hex}>•</color> <b>{info.name}</b>");

        int tiles = BuildPlacement.IsFor(body) ? BuildPlacement.Tiles : 0;
        if (tiles == 0)
        {
            string how =
                info.drawMode == BuildDrawMode.Square ? $"press and drag out a square (min {MinSideFor(info)}x{MinSideFor(info)})"
              : info.drawMode == BuildDrawMode.Rectangle ? "press and drag out a rectangle (min 2 wide both ways)"
              : $"press and drag to draw it — at least {info.minTiles} tiles, edge to edge";
            sb.Append($"  <size=10><color=#9FB4C8>{how}</color></size>");

            // The offer the merge rule makes, said out loud. The coloured cells around a standing farm
            // are meaningless if nobody has told the player what they are.
            int sites = BuildPlacement.IsFor(body) ? BuildPlacement.ExpansionSites().Count : 0;
            if (sites > 0)
                sb.Append($"  <size=10><color=#{hex}>•</color><color=#9FB4C8> the tinted ground is where you " +
                          $"could extend a {info.name.ToLower()} you already have, instead of starting another</color></size>");
        }
        else
        {
            BuildPlacement.Cost(out int m, out int e);
            bool met = BuildPlacement.MeetsMinimum;
            string mh = ColorUtility.ToHtmlStringRGB(met ? UITheme.Good : UITheme.Bad);
            sb.Append($"  <color=#{mh}><b>{tiles} tile{(tiles == 1 ? "" : "s")}</b></color>");
            sb.Append($" <color=#9FB4C8>· {m}m {e}e</color>");
            sb.Append(met
                ? "  <size=10><color=#9FB4C8>Confirm below the shape when you're happy with it.</color></size>"
                : $"  <size=10><color=#FF6659>needs {BuildPlacement.MinTiles - tiles} more</color></size>");
        }

        // The one thing the player must always be able to find: the way out.
        sb.Append(tiles > 0
            ? "\n<size=10><color=#9FB4C8>Esc clears the shape · Esc again puts the structure down · Cancel does both</color></size>"
            : "\n<size=10><color=#9FB4C8>Esc puts the structure down</color></size>");

        // AT THE CEILING. Said here as well as at the tile, because the tile label fades and this does
        // not: a player who stopped painting a while ago and is wondering why the shape will not grow
        // should not have to try again to be told.
        if (BuildPlacement.IsFor(body) && BuildPlacement.AtResourceCeiling)
        {
            BuildPlacement.CanAffordTiles(tiles + 1, out int sm, out int se);
            string need = sm > 0 && se > 0 ? $"{sm} metal and {se} energy"
                        : sm > 0 ? $"{sm} metal" : $"{se} energy";
            sb.Append($"   <color=#FF6659><b>At your limit</b> — another tile needs {need}</color>");
        }

        return sb.ToString();
    }

    /// The demolition equivalent of PlacementModeBanner.
    string DemolitionModeBanner()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<color=#FF6659><b>DEMOLITION MODE</b></color>");

        int tiles = BuildDemolition.Tiles;
        if (tiles == 0)
        {
            sb.Append("  <size=10><color=#9FB4C8>left-drag over built tiles to select them · " +
                      "right-drag to un-select · nothing comes down until you confirm</color></size>");
            sb.Append("\n<size=10><color=#9FB4C8>Esc leaves the mode</color></size>");
            return sb.ToString();
        }

        BuildDemolition.Refund(out int m, out int e);
        BuildDemolition.SplitSummary(out int split, out int extra);
        int destroyed = BuildDemolition.WouldDestroy();

        sb.Append($"  <b>{tiles} tile{(tiles == 1 ? "" : "s")}</b> selected");
        sb.Append($" <color=#9FB4C8>· {m}m {e}e back</color>");
        if (destroyed > 0)
            sb.Append($"  <color=#FFBF4D>{destroyed} structure{(destroyed == 1 ? "" : "s")} removed outright</color>");
        if (split > 0)
            sb.Append($"  <color=#FFBF4D>{split} will split into {split + extra}</color>");

        sb.Append("\n<size=10><color=#9FB4C8>Esc clears the selection · Esc again leaves the mode</color></size>");
        return sb.ToString();
    }

    // ============================================================================================
    // WHAT THE CURSOR WINDOW SAYS WHILE PLACING
    //
    // The whole siting decision, at the mouse: what this tile would yield, how good the ground is both
    // absolutely and relative to the rest of the world, whether the grid reaches, and what the building
    // as drawn so far costs. This is the readout that used to be under the map.
    //
    // It keeps the plain tile readout at the top — biome, ore, temperature — because that is context for
    // the numbers below it and losing it would make Placement Mode strictly less informative than idly
    // hovering.
    // ============================================================================================
    string PlacementHoverText(int x, int y)
    {
        var info = SurfaceBuildingDatabase.Get(selected.Value);
        var sb = new System.Text.StringBuilder();

        sb.Append(TileHoverText(body, x, y, info.index));

        // ---- This tile, for this building ----
        if (info.index != SurfaceIndexKind.None)
        {
            if (SurfaceIndex.Unlocked(body, info.index))
            {
                float v = SurfaceIndex.Get(body, info.index, x, y);
                float pct = SurfaceIndex.Percentile(body, info.index, x, y);
                string hex = ColorUtility.ToHtmlStringRGB(SurfaceBuildManager.EfficiencyColor(v));
                sb.Append($"\n<color=#8FD0FF>{SurfaceIndex.Name(info.index)}</color> " +
                          $"<color=#{hex}><b>{v * 100f:F0}% ({SurfaceBuildManager.EfficiencyLabel(v)})</b></color>");
                sb.Append($"\n<size=10><color=#9FB4C8>better than {pct * 100f:F0}% of this world</color></size>");
            }
            else
                sb.Append($"\n<color=#C9A94D>{SurfaceIndex.Name(info.index)} not surveyed — " +
                          $"{SurfaceIndex.LockReason(body, info.index)}</color>");
        }

        // ---- What it would produce, sited here ----
        // Quoted for a SINGLE tile of this class, because that is the honest answer to "what does this
        // tile give me": the drawn building's total is the sum over its cells and is on the Confirm
        // panel. PredictedYield takes an origin and a rotation, which for a one-cell question is this
        // cell and no rotation.
        string yield = SurfaceBuildManager.PredictedYield(body, selected.Value, x, y, 0);
        if (!string.IsNullOrEmpty(yield) && yield != "no direct output")
            sb.Append($"\n<size=10><color=#9FB4C8>per tile here:</color></size> <b>{yield}</b>");

        // ---- Would this join something already standing? ----
        //
        // Said BEFORE the first tile as well as after, because "this will become part of that farm
        // rather than a new one" changes the decision: the merged building's efficiency is area-weighted
        // across both, so extending a good farm onto poor ground drags it down and the player deserves
        // to know that at the moment they are choosing where to start.
        var joining = BuildPlacement.Expanding
                   ?? SurfaceBuildManager.ExpansionTargetAt(body, selected.Value, new Vector2Int(x, y));
        if (joining != null)
        {
            string jhex = ColorUtility.ToHtmlStringRGB(SurfaceBuildManager.EfficiencyColor(joining.efficiency));
            sb.Append($"\n<color=#4DFF6E>Joins the {joining.Info.name.ToLower()} here</color> " +
                      $"<size=10><color=#9FB4C8>({joining.TileCount} tiles, " +
                      $"<color=#{jhex}>{joining.efficiency * 100f:F0}% sited</color>)</color></size>");
        }

        // ---- The building as drawn so far ----
        if (BuildPlacement.IsFor(body) && BuildPlacement.Tiles > 0)
        {
            int tiles = BuildPlacement.Tiles;
            BuildPlacement.Cost(out int m, out int e);
            bool met = BuildPlacement.MeetsMinimum;
            string mh = ColorUtility.ToHtmlStringRGB(met ? UITheme.Good : UITheme.Bad);

            sb.Append($"\n<color=#{mh}>{tiles}/{BuildPlacement.MinTiles} tiles</color>");

            // THE RUNNING TOTAL, which is the number the player is actually spending. Red the moment the
            // next tile is out of reach, so the limit is visible before it is hit rather than only when
            // the brush stops responding.
            string ch = ColorUtility.ToHtmlStringRGB(
                BuildPlacement.AtResourceCeiling ? UITheme.Bad : UITheme.SubText);
            sb.Append($"  <color=#{ch}><b>{m} metal · {e} energy</b></color>");

            if (BuildPlacement.AtResourceCeiling)
                sb.Append("\n<color=#FF6659><b>Cannot afford another tile</b></color>");
        }
        else
            sb.Append("\n<size=10><color=#4DFF6E>Press and drag to draw</color></size>");

        return sb.ToString();
    }

    /// The cursor window in Demolition Mode: what is under the mouse, and what taking it would do.
    string DemolitionHoverText(int x, int y)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(TileHoverText(body, x, y));

        var hit = SurfaceBuildManager.At(body, x, y);
        if (hit == null)
            sb.Append("\n<size=10><color=#9FB4C8>nothing built here</color></size>");
        else
        {
            var info = hit.Info;
            string hex = ColorUtility.ToHtmlStringRGB(Vivid(info.color));
            sb.Append($"\n<color=#{hex}>•</color> <b>{info.name}</b> " +
                      $"<size=10><color=#9FB4C8>Lv{hit.level} · {hit.TileCount} tiles</color></size>");

            // WHAT REMOVING THIS TILE ALONE WOULD DO — the live version of the split warning, so the
            // player can see a cut coming while they are still choosing where to make it rather than
            // being told about it by a dialog after the fact.
            var probe = new HashSet<Vector2Int>(BuildDemolition.Cells) { new Vector2Int(x, y) };
            int pieces = SurfaceBuildManager.WouldSplitInto(hit, probe);
            if (pieces == 0)
                sb.Append("\n<color=#FFBF4D>Taking this removes the whole structure</color>");
            else if (pieces > 1)
                sb.Append($"\n<color=#FFBF4D>Taking this splits it into {pieces}</color>");
        }

        int tiles = BuildDemolition.Tiles;
        if (tiles > 0)
        {
            BuildDemolition.Refund(out int m, out int e);
            sb.Append($"\n<color=#FF6659><b>{tiles} tile{(tiles == 1 ? "" : "s")} selected</b></color>" +
                      $" <color=#9FB4C8>· {m}m {e}e back</color>");
        }
        else
            sb.Append("\n<size=10><color=#9FB4C8>Left-drag to select · right-drag to un-select</color></size>");

        return sb.ToString();
    }

    string HoverWhy()
    {
        if (!selected.HasValue || !HasHoverCell) return "";
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
                // Leaving the Build tab ends Placement Mode. The spec asks for other UI to be locked out
                // while placing; disabling the tabs outright would be the literal reading and a trap —
                // a player who wants to go and check the Survey map should not have to hunt for the exit
                // first. Leaving CANCELS instead, which costs nothing (nothing is spent until Confirm)
                // and cannot strand anyone in a mode.
                if (captured != Tab.Build)
                { selected = null; CancelPlace(); BuildPlacement.Cancel(); BuildDemolition.Cancel(); }
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
                   $"{GameCalendar.Duration(CityGrowth.SpawnInterval(body))} once there are people to fill it.</color></size>";
        });

        // ---- Society & Production (folded from the retired Colony window) ----
        // Only for a world you own: population, cities and the objectives that establish a colony are
        // meaningless on somebody else's planet or a dead rock.
        if (body.owner == FactionManager.Player)
        {
            // ---- SOCIETY AND SATISFACTION NEED SOMEBODY TO BE SATISFIED ----
            //
            // Claimed is not settled (Claim.StageOf draws exactly that line), and everything under these
            // two headings is about people: how many, how they feel, and what that does to the birth
            // rate. On a claimed rock with nobody on it the panel reported a population of nobody and
            // then explained, factor by factor, how content that nobody was — a satisfaction percentage
            // for an empty world, on the screen the player uses to check whether the world is empty.
            //
            // CAPABILITY and OBJECTIVES below stay: "no food, no power, no research" is a true statement
            // about a bare claim, and the objectives list is the road out of being one.
            if (!Claim.IsSettled(body))
            {
                Header("SOCIETY");
                Note(Card(), "This world is <b>claimed, not settled</b> — it is legally yours and nobody lives " +
                             "on it. Land a colony ship to found a settlement; population and satisfaction " +
                             "start once there is someone here to count.");
            }
            else
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
            }

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
                return (can, can ? $"Establish City ({ColonyManager.CityMetal}m {ColonyManager.CityEnergy}e, {GameCalendar.Duration(ColonyManager.CityBuildTime)})"
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

    /// Jump to the Build tab with a category already selected.
    ///
    /// The panels that used to carry a "Build X" button for an abstract facility now carry one of these
    /// instead. The distinction matters: the old button BUILT something invisible, this one takes you to
    /// where you draw a real one. Refused on a world the Build tab is not open on (unowned, unsurveyed) —
    /// TabAvailable is the same gate the tab strip uses, so this can never strand the window on a tab
    /// that will not render.
    void GoToBuild(SurfaceBuildingCategory cat, string label)
    {
        if (!TabAvailable(Tab.Build, out string why))
        {
            Note($"<color=#C9A94D>{why}</color>");
            return;
        }

        UIFactory.Button(sidePanel, label, () =>
        {
            tab = Tab.Build;
            buildCategory = cat;
            lastSig = null;
        }, 26);
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
            // NO "BUILD RESEARCH CENTRE" BUTTON HERE ANY MORE.
            //
            // This used to start an ABSTRACT facility: a timer, and then a word in a list. Nothing stood
            // anywhere on the world, and the entire laboratory was the line of text above this one. A
            // Research Centre is now a structure you draw on the surface like any other, so the button
            // that conjured one out of nothing is gone and this points at the tab that actually builds it.
            Note(card, "No research centre here. Draw one on the surface from the <b>Build</b> tab " +
                       "(Science) — a campus scales with every tile you give it, and its tier is this " +
                       "world's research capacity.");
            GoToBuild(SurfaceBuildingCategory.Science, "Build a Research Centre »");
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
                    ? $"Upgrade -> Lv{next} ({ColonyManager.LabUpgradeMetal(next)}m {ColonyManager.LabUpgradeEnergy(next)}e, {GameCalendar.Duration(ColonyManager.LabUpgradeTime(next))}) -> {ResearchCapacity.ForLevel(next)} capacity"
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
            // Same retirement as the research centre on Overview: the abstract "Build Shipyard" button
            // produced a number and no building. A yard is ground infrastructure with an orbital tether —
            // it is placed on the surface grid, in the Military category — so this points at the tab that
            // builds one rather than pretending to build it here.
            Note(card, "No shipyard on this world. A shipyard is where hulls are laid down; every one you " +
                       "own pools its build power. Place one on the surface from the <b>Build</b> tab (Military).");
            if (body.owner == FactionManager.Player)
                GoToBuild(SurfaceBuildingCategory.Military, "Build a Shipyard »");
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
                            ? $"Upgrade -> Lv{next} ({ColonyManager.ShipyardUpgradeMetal(next)}m {ColonyManager.ShipyardUpgradeEnergy(next)}e, {GameCalendar.Duration(ColonyManager.ShipyardUpgradeTime(next))}) -> {BuildPower.ForLevel(next)} build power"
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
                live.Text(t, () => $"<b>{cap.name}</b> — arriving in {GameCalendar.Duration(Mathf.Max(0f, cap.travelDuration - cap.travelElapsed))}");
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
    // giants 10-40, moons/asteroids at or under 0.5). This replaces the old surfaceSize "size class" readout.
    static string MassWord(float mass)
    {
        // Against the Earth-relative scale: 1 IS Earth, so "Small" has to straddle it rather than sit
        // below it, and the old cuts (which assumed Earth was 2) called an Earth-mass world Small and a
        // 4-mass super-Earth Medium.
        string w = mass <= MassRules.AsteroidMax ? "Tiny"
                 : mass < 0.9f ? "Small"
                 : mass < 1.6f ? "Earth-sized"
                 : mass < 3f ? "Large"
                 : mass < WorldClassifier.GasGiantMassFloor ? "Super-Earth"
                 : "Giant";
        return $"{w} ({MassRules.Format(mass)})";
    }

    static string WeatherProse(CelestialBody b)
    {
        var sb = new System.Text.StringBuilder();
        // Against the rotation the generator actually produces (RotationRules: 0.4..40, dynamo line at
        // 12). The old cuts were 3 and 45, and neither could fire: nothing ever exceeded 45, and 3 only
        // caught the very deepest tidal locks — so a braked world with no magnetosphere and no air was
        // described as having "a steady day/night cycle".
        float spin = Mathf.Abs(b.spinSpeed);
        float dayLength = RotationRules.RotationPeriodDays(spin);
        sb.Append(spin < RotationRules.MagneticFieldSpin
                    ? $"Turns once every {dayLength:0.#} days — one face bakes while the other freezes, and it is too slow to run a dynamo. "
                : spin > RotationRules.MaxSpin * 0.85f
                    ? $"A {dayLength:0.#}-day rotation; it spins violently and the storms never stop. "
                    : $"A steady {dayLength:0.#}-day cycle of day and night. ");
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
        if (body.owner != FactionManager.Player)
        { Header("STRUCTURES"); Note("You can only build on worlds you own. Colonize this world first."); return; }
        if (!body.Surveyed)
        { Header("STRUCTURES"); Note("Survey this world before developing it."); return; }

        // THE QUEUE COMES FIRST, above the catalogue. What this world is already building is both more
        // urgent than what it could build — it is spending the Labor and holding the ground — and the
        // thing the player has come back to the tab to check on. Below the tray it would be under a
        // scroll, on a panel whose length depends on which category tab happens to be selected.
        BuildQueuePanel();

        // ---- The two modes, side by side ----
        //
        // Demolition is a MODE, like placement, rather than a button on each building's row — because
        // what it operates on is tiles rather than buildings, and "take those four tiles back" has
        // nowhere to live in a per-building list. The per-building Demolish buttons are still there and
        // still work; they now open this mode with that building already selected.
        var modeRow = UIFactory.NewUI(sidePanel, "ModeRow");
        UIFactory.AddLayout(modeRow, 24);
        var mh = modeRow.AddComponent<HorizontalLayoutGroup>();
        mh.spacing = 4;
        mh.childControlWidth = true; mh.childControlHeight = true;
        mh.childForceExpandWidth = true; mh.childForceExpandHeight = true;

        bool demo = BuildDemolition.IsFor(body);
        var demoBtn = UIFactory.Button(modeRow.transform, demo ? "Stop demolishing" : "Demolish...",
            () => { if (BuildDemolition.IsFor(body)) ExitDemolition(); else EnterDemolition(); }, 22);
        UIFactory.Tooltip(demoBtn.gameObject,
            "Paint over built tiles to take them back. Left-drag selects, right-drag un-selects, and " +
            "nothing comes down until you confirm. Removing the middle of a building splits it into " +
            "separate ones — you'll be asked again before that happens.");

        Header("STRUCTURES");
        Note(demo
            ? "<color=#FF6659><b>Demolition mode.</b></color> Left-drag over built tiles to select them, right-drag to un-select. Confirm below the selection."
            : "Click a structure to pick it up, then draw it on the map. Esc cancels. Footprints interlock — pack them tightly.");

        // A ROW OF COLOURED TABS, not one long list with headings.
        //
        // The catalogue outgrew the list: twenty-five structures under six headings in a quarter-width
        // scrolling panel meant hunting for a reactor past every farm and habitat. Tabs cost one click and
        // give back the whole panel, and the colour is what makes the click a reflex rather than a read —
        // you go to "the yellow one" for power without processing the word ELECTRICAL first.
        BuildCategoryTabs();
        Note(SurfaceBuildingCategoryStyle.Blurb(buildCategory));

        int shown = 0;
        foreach (var info in SurfaceBuildingDatabase.All)
        {
            if (info == null || info.category != buildCategory) continue;
            if (!PlaceableFromTray(info)) continue;
            BuildStructureCard(info);
            shown++;
        }

        // Defensive: no category is empty today (the thinnest, Agriculture and Science, hold one each, and
        // tech-locked structures still show as locked cards rather than being filtered out). It exists so
        // that adding a category before its buildings — or filtering them all out — reads as an empty
        // category rather than as a tab that silently does nothing.
        if (shown == 0)
            Note("<i>Nothing to build here yet.</i>");

        // The built-here list is the (richer) Infrastructure panel, folded in now that Infrastructure is
        // no longer its own tab: per-structure health, siting, power draw, select-on-map, upgrade and
        // demolish. Deliberately NOT filtered by the selected category: it is the inventory of what
        // stands on this world, and hiding four-fifths of it behind whichever tab you happen to be on
        // would make "what have I actually built here?" unanswerable without six clicks.
        BuildInfrastructurePanel();
    }

    // ============================================================================================
    // THE BUILD QUEUE
    //
    // Everything this world is currently putting up: how far along it is, what is holding it back, and
    // the two controls that matter — pause and cancel.
    //
    // It exists because a build now takes TIME. Confirming one used to be the end of the interaction and
    // is now the start of one, and without this panel the player has no way to answer any of the
    // questions that follow: how long, why so slow, and can I have my metal back. The map's ghosts
    // (DrawConstructionGhosts) say WHERE; this says WHEN.
    //
    // Shaped like the shipyard's stocks on purpose — a list of rows, each a bar and a cancel — because
    // that is the queue the player has already learned.
    // ============================================================================================
    void BuildQueuePanel()
    {
        var jobs = SurfaceBuildQueue.Peek(body);
        if (jobs == null || jobs.Count == 0) return;   // no heading over an empty list; the tray moves up

        Header($"UNDER CONSTRUCTION ({jobs.Count})");

        // THE POOL EVERY ROW BELOW IS COMPETING FOR. Without it, "3 of 5 Labor" on a row is a fraction
        // with no denominator anywhere on screen, and a queue where everything crawls looks broken
        // rather than over-committed.
        var pool = UIFactory.WrapText(sidePanel, "", UITheme.SmallSize, UITheme.SubText);
        live.Text(pool, () =>
        {
            float max = SurfaceLabor.Max(body), used = SurfaceLabor.Used(body);
            string hex = ColorUtility.ToHtmlStringRGB(used > max + 0.01f ? UITheme.Warn : UITheme.SubText);
            string over = used > max + 0.01f
                ? " <size=10>· over-committed, so everything here is stretched</size>"
                : "";
            return $"<color=#{hex}><b>{used:0.#} / {max:0.#} Labor</b> in use</color>{over}   " +
                   $"<size=10>Labor is handed out from the top — the first job gets the workforce it wants.</size>";
        });

        // Copied before iterating: a cancel from one of these rows removes from the live list, and the
        // rows are built once and then live-updated, so the loop must not be walking the same list a
        // click could mutate.
        foreach (var job in new List<SurfaceBuildJob>(jobs)) BuildQueueCard(job);
    }

    void BuildQueueCard(SurfaceBuildJob job)
    {
        var info = SurfaceBuildingDatabase.Get(job.type);
        var card = Card();

        var title = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        live.Text(title, () =>
        {
            // The POSITION is read live rather than captured from the build loop, because the reorder
            // buttons below can change it without rebuilding the panel — a "#3" frozen at draw time
            // would still say #3 on a job the player had just promoted to the top.
            var list = SurfaceBuildQueue.Peek(body);
            int at = list != null ? list.IndexOf(job) + 1 : 0;
            string hex = ColorUtility.ToHtmlStringRGB(info.color);
            string state = job.paused ? "  <color=#FFBF4D>PAUSED</color>" : "";
            return $"<color=#9FB4C8>#{at}</color> <b><color=#{hex}>{info.name}</color></b>" +
                   $"  <size=10><color=#9FB4C8>{job.Tiles} tiles</color></size>{state}";
        });

        Bar(card, () =>
        {
            float granted = SurfaceBuildQueue.LaborGranted(body, job);
            bool starved = !job.paused && granted < job.labor - 0.01f;
            Color c = job.paused ? UITheme.SubText : starved ? UITheme.Warn : UITheme.Good;
            return (job.Progress, $"{job.Progress * 100f:F0}%  ·  {EtaText(job)}", c);
        });

        var detail = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.SubText);
        live.Text(detail, () =>
        {
            if (job.paused)
                return "<color=#FFBF4D>Held.</color> <size=10>Its workforce is free for the jobs below it, " +
                       "and the ground it is standing on is still reserved.</size>";

            float granted = SurfaceBuildQueue.LaborGranted(body, job);
            if (granted < job.labor - 0.01f)
            {
                float factor = BuildScaling.TimeFactorFor(job.labor, granted);
                return $"<color=#FFBF4D>{granted:0.#} of {job.labor:0.#} Labor</color> — " +
                       $"<size=10>running at {100f / factor:F0}% speed. Free some up by pausing or " +
                       $"cancelling a job above it, or build housing and depots.</size>";
            }
            return $"{job.labor:0.#} Labor  <size=10>· fully staffed</size>";
        });

        // ---- Controls ----
        // Two rows, not one. The panel is a quarter of the window wide and the cancel button carries a
        // refund figure ("Cancel — 240m 150e back"), which cannot share a line with three other controls
        // without every label being clipped to two characters.
        //
        // Priority sits ABOVE cancel deliberately: when a job is starving something more important,
        // promoting the important one is the cheap answer and abandoning this one is the expensive one.
        // Burying the cheap answer under the destructive one would be the wrong way round.
        //
        // WORDS, NOT ARROW GLYPHS. The Geometric Shapes block isn't in the LiberationSans atlas this
        // project ships (see RefreshConfirmPanel), so an arrow renders as a tofu box.
        var row = UIFactory.NewUI(card, "QueueRow");
        UIFactory.AddLayout(row, 22);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 3;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = false;

        var up = UIFactory.Button(row.transform, "Up", () => { SurfaceBuildQueue.Reorder(body, job, -1); lastSig = null; }, 20f);
        UIFactory.Tooltip(up.gameObject, "Move up the queue. Labor is handed out from the top, so the " +
                                         "higher a job sits the more of the workforce it gets.");
        live.Button(up, () =>
        {
            var list = SurfaceBuildQueue.Peek(body);
            return (list != null && list.IndexOf(job) > 0, "Up");
        });

        var down = UIFactory.Button(row.transform, "Down", () => { SurfaceBuildQueue.Reorder(body, job, 1); lastSig = null; }, 20f);
        UIFactory.Tooltip(down.gameObject, "Move down the queue — everything above it takes its Labor first.");
        live.Button(down, () =>
        {
            var list = SurfaceBuildQueue.Peek(body);
            int at = list != null ? list.IndexOf(job) : -1;
            return (at >= 0 && at < list.Count - 1, "Down");
        });

        // Hold keeps the progress and the ground and gives the workforce back. The caption is live rather
        // than rebuilt, so the button doesn't vanish and reappear under the cursor as it toggles.
        var hold = UIFactory.Button(row.transform, "", () => SurfaceBuildQueue.SetPaused(body, job, !job.paused), 20f);
        UIFactory.Tooltip(hold.gameObject,
            "Hold this project. It keeps everything built so far and its ground stays reserved, but it " +
            "hands its workforce back so something else can use it.");
        live.Button(hold, () => (true, job.paused ? "Resume" : "Hold"));

        // CANCEL REFUNDS IN FULL, and says the figure on the button, because a queue you are afraid to
        // cancel out of is a queue that punishes experimenting with a layout. It gives back exactly what
        // was PAID (SurfaceBuildQueue.Cancel), never a re-derived price — a refund at today's cost would
        // turn the queue into somewhere to park metal across an Industry research.
        var cancel = UIFactory.Button(card, "", () =>
        {
            SurfaceBuildQueue.Cancel(body, job);
            lastSig = null;   // the row goes, and so do its ghosts on the map
        }, 22f);
        UIFactory.Tooltip(cancel.gameObject,
            "Abandon this project. Everything paid for it comes straight back and the ground it was " +
            "holding is free to build on again. The work already done is lost.");
        live.Button(cancel, () => (true,
            job.metalPaid > 0 || job.energyPaid > 0
                ? $"Cancel — {job.metalPaid}m {job.energyPaid}e back"
                : "Cancel"));
    }

    /// "~2m 10s left", on the game clock the build actually runs on.
    ///
    /// Quoted at the CURRENT rate, which is why it can jump when a job above this one finishes: the
    /// alternative — a figure computed as if the queue were empty — would be a promise the queue has no
    /// intention of keeping, and would read as the timer being broken.
    string EtaText(SurfaceBuildJob job)
    {
        if (job.paused) return "held";
        if (job.Progress >= 1f) return "finishing";

        float s = SurfaceBuildQueue.Eta(body, job);
        if (float.IsInfinity(s) || float.IsNaN(s)) return "held";
        if (s < 1f) return "finishing";
        // "~3m 20s left" was the last place in the game still speaking in minutes and seconds, and it was
        // the exact one the request names: "placing a building on a planet surface would, instead of
        // taking minutes or seconds, take months and days".
        return $"~{GameCalendar.Duration(s)} left";
    }

    /// Whether a structure is offered in the build tray at all. The capitol and the grounded colony ship
    /// aren't placed — the ship arrives with the colony and the capitol is what it becomes — and
    /// settlements/towns/cities are grown by the population. Dev Mode reveals the placeable ones for
    /// testing. Factored out of the tray loop so the tab COUNTS and the list can never disagree about
    /// what is on offer.
    static bool PlaceableFromTray(SurfaceBuildingInfo info)
    {
        if (info == null) return false;
        if (info.type == SurfaceBuildingType.PlanetCapitol) return false;
        if (info.type == SurfaceBuildingType.ColonyShipBase && !GameMode.DevMode) return false;
        if (CityGrowth.IsSettlement(info.type) && !GameMode.DevMode) return false;
        return true;
    }

    static int TrayCount(SurfaceBuildingCategory cat)
    {
        int n = 0;
        foreach (var info in SurfaceBuildingDatabase.All)
            if (info != null && info.category == cat && PlaceableFromTray(info)) n++;
        return n;
    }

    // Two rows of three. A quarter-width panel cannot hold six tabs across, and a horizontally scrolling
    // tab strip is worse than a second row — you cannot see what you are choosing between.
    void BuildCategoryTabs()
    {
        const int PerRow = 3;
        var cats = (SurfaceBuildingCategory[])System.Enum.GetValues(typeof(SurfaceBuildingCategory));

        Transform row = null;
        for (int i = 0; i < cats.Length; i++)
        {
            if (i % PerRow == 0) row = CategoryTabRow();
            AddCategoryTab(row, cats[i]);
        }
    }

    Transform CategoryTabRow()
    {
        var go = UIFactory.NewUI(sidePanel, "CategoryRow");
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 3;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 22f; le.preferredHeight = 22f; le.flexibleHeight = 0f;
        return go.transform;
    }

    void AddCategoryTab(Transform row, SurfaceBuildingCategory cat)
    {
        var captured = cat;
        bool active = cat == buildCategory;
        Color tint = SurfaceBuildingCategoryStyle.Of(cat);

        var btn = UIFactory.Button(row, SurfaceBuildingCategoryStyle.Name(cat), () =>
        {
            buildCategory = captured;
            // Picking up a structure and then switching category would leave a ghost from a tab you can
            // no longer see, so the selection is dropped with the tab — same rule the main tab strip uses.
            selected = null; CancelPlace();
            lastSig = null;                 // force one rebuild; the tray is redrawn for the new category
        }, 22f);

        // Equal thirds of the row, whatever the panel's width. Button's own LayoutElement sets a
        // preferred width that would otherwise pin the tabs to their label lengths and leave MILITARY
        // three times the size of CIVIL.
        var le = btn.GetComponent<LayoutElement>();
        if (le != null) { le.minWidth = 0f; le.preferredWidth = 0f; le.flexibleWidth = 1f; }

        // THE COLOUR IS ALWAYS ON, selected or not — a dim wash of the category's hue when it isn't
        // chosen, the full hue when it is. Colour that only appears on the active tab would be useless
        // for finding the tab you want, which is the entire reason for colouring them.
        var colors = btn.colors;
        colors.normalColor = active ? tint : Dim(tint, 0.30f);
        colors.highlightedColor = active ? tint : Dim(tint, 0.45f);
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = tint;
        btn.colors = colors;

        var lbl = btn.GetComponentInChildren<TMP_Text>();
        if (lbl != null)
        {
            lbl.fontSize = 9;
            lbl.color = active ? LabelOn(tint) : tint;
        }

        int n = TrayCount(cat);
        UIFactory.Tooltip(btn.gameObject,
            $"{SurfaceBuildingCategoryStyle.Name(cat)} — {SurfaceBuildingCategoryStyle.Blurb(cat)}\n" +
            (n == 1 ? "1 structure" : $"{n} structures"));
    }

    /// Readable text over a filled category tab. The bright categories (yellow, green, steel) need dark
    /// text and the darker ones (blue, red) need light, so this is decided by PERCEIVED luminance rather
    /// than by a hand-kept list that would go stale the moment a colour is retuned.
    static Color LabelOn(Color bg)
        => (bg.r * 0.299f + bg.g * 0.587f + bg.b * 0.114f) > 0.6f
            ? new Color(0.06f, 0.07f, 0.09f)
            : Color.white;

    /// Darken a colour while keeping it OPAQUE. Unity's `Color * float` scales alpha along with the
    /// channels, so the obvious `tint * 0.3f` would make an unselected tab 30% transparent rather than
    /// 30% bright — the panel behind it showing through, and the dimming barely visible.
    static Color Dim(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

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
                if (PowerGrid.Projects(info)) pw.Append($" · <color=#4DC8FF>lights {info.powerRange:0.#}</color>");
                if (info.powerStorage > 0f) pw.Append($" · <color=#4DC8FF>banks {info.powerStorage:0}</color>");

                return $"<color=#{hex}>{m} metal · {e} energy</color> · {info.Cells} tiles · {idx}{pw}";
            });

            var btn = UIFactory.Button(card, "", () =>
            {
                bool wasHeld = selected.HasValue && selected.Value == t;
                CancelPlace();            // picking a different structure abandons the pending question
                BuildPlacement.Cancel();  // ...and any half-drawn footprint of the last one
                BuildDemolition.Cancel(); // picking something to BUILD is leaving demolition mode

                if (wasHeld) selected = null;
                else
                {
                    selected = t;
                    rotation = 0;
                    // PICKING A STRUCTURE *IS* ENTERING PLACEMENT MODE. It used to only arm a brush: the
                    // map behaved exactly as before and the first press started a gesture that ended in
                    // a building. Now the session opens here, which is what lets the map light up its
                    // guidance grids, the overlay switch to this class's index, and the counter and the
                    // Confirm panel exist at all before a single tile is painted.
                    //
                    // Fixed classes have nothing to draw, so they stay on the click-and-confirm path
                    // (OnGridClick -> AskPlace) and open no session.
                    if (UsesPlacementSession(info)) BuildPlacement.Begin(body, t);
                }
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

            // Opens Demolition Mode with this building selected — see the note on the other Demolish
            // button, in BuildInfraRow.
            UIFactory.Button(card, $"Demolish ({SurfaceBuildManager.DemolishRefund * 100f:F0}% back)", () =>
            {
                EnterDemolition();
                BuildDemolition.PaintWhole(cap);
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

        // ONE BUTTON FOR THE WHOLE COLONY, because that is the shape of the problem a quake creates: it
        // damages a patch, not a structure, and putting the colony back together one row at a time
        // through a list of forty entries is busywork rather than a decision. The per-structure Repair
        // is still there for choosing WHICH one when materials are short.
        var fixAll = UIFactory.Button(sidePanel, "", () => { SurfaceBuildManager.RepairAll(body); lastSig = null; }, 24);
        live.Button(fixAll, () =>
        {
            int hurt = 0, metal = 0, energy = 0;
            foreach (var p in SurfaceBuildManager.On(body))
            {
                if (p == null || p.health >= 0.999f) continue;
                hurt++;
                SurfaceBuildManager.RepairCost(p, out int m, out int e);
                metal += m; energy += e;
            }
            if (hurt == 0) return (false, "Nothing damaged");
            bool afford = GameMode.DevMode || PlayerEconomy.CanAfford(metal, energy);
            // Offered even when the full bill is unaffordable: RepairAll works cheapest-first and stops
            // when the treasury runs out, so a partial repair is a real and useful outcome.
            return (body.owner == FactionManager.Player,
                    afford ? $"Repair all {hurt} damaged ({metal}m {energy}e)"
                           : $"Repair what you can afford — {hurt} damaged, {metal}m {energy}e in full");
        });

        // Grouped by category so a long list stays navigable. Headed in each category's own COLOUR, so
        // this list and the tray's tabs read as the same scheme — the yellow block is your power plant
        // whether you are choosing one or reviewing one.
        foreach (SurfaceBuildingCategory cat in System.Enum.GetValues(typeof(SurfaceBuildingCategory)))
        {
            bool headerAdded = false;
            foreach (var p in new List<PlacedBuilding>(placed))
            {
                if (p.Info.category != cat) continue;
                if (!headerAdded) { CategoryHeader(cat); headerAdded = true; }
                BuildInfraRow(p);
            }
        }
    }

    /// A section heading in its category's colour. Replaces the old plain-accent Header for anything
    /// grouped by category, so the colour scheme is one scheme rather than a tray convention.
    void CategoryHeader(SurfaceBuildingCategory cat)
        => UIFactory.WrapText(sidePanel, $"<b>{SurfaceBuildingCategoryStyle.Name(cat)}</b>",
                              UITheme.SmallSize, SurfaceBuildingCategoryStyle.Of(cat));

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

        // REPAIR sits beside Upgrade because it is the same kind of decision: spend materials on this
        // site rather than somewhere else. It reads "Undamaged" on a healthy structure rather than
        // vanishing, so the row does not change shape every time a quake lands and the player learns
        // the button is there before they need it.
        var fix = UIFactory.Button(row.transform, "", () => { SurfaceBuildManager.Repair(body, cap); lastSig = null; }, 20);
        live.Button(fix, () =>
        {
            if (cap.health >= 0.999f) return (false, "Undamaged");
            bool can = SurfaceBuildManager.CanRepair(body, cap, out string why);
            SurfaceBuildManager.RepairCost(cap, out int m, out int e);
            return (can, can ? $"Repair ({m}m {e}e)" : $"Repair — {why}");
        });

        // DEMOLISH OPENS THE MODE with this building already selected, rather than tearing it down on
        // the spot. Two reasons, and the second is the one that matters:
        //
        //   A twenty-tile farm is not an all-or-nothing proposition any more. The useful verb is "take
        //   those four tiles back", and a button that can only mean "lose the farm" cannot express it.
        //   And the removal is shown on the MAP before it happens, which is where the consequences are —
        //   what it will split, what it will strand off the power grid, what it frees up.
        UIFactory.Button(row.transform, "Demolish", () =>
        {
            EnterDemolition();
            BuildDemolition.PaintWhole(cap);
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

        // Filtered to what this world has actually given up. Anomalies and rare seams are a level-2
        // finding — see Survey.SiteRevealed, which the map markers use too, so the list and the map
        // always name the same set.
        var pois = new List<PointOfInterest>();
        if (body.pointsOfInterest != null)
            foreach (var poi in body.pointsOfInterest)
                if (Survey.SiteRevealed(body, poi.type)) pois.Add(poi);

        if (pois.Count == 0)
        {
            Note("Nothing of note found here. A deep survey by a research ship sometimes turns up what an orbital pass missed.");
            return;
        }

        if (Survey.LevelOf(body) < 2)
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
                    sb.AppendLine($"<size=10><color=#8FD0FF>Study: {cap.researchPointCost} pts · ~{GameCalendar.Duration(cap.researchDuration)} · " +
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
            // An anomaly or a rare seam is a READING, not a sighting — the level-2 pass finds those.
            // See Survey.SiteRevealed, which is also what the Survey tab's list filters on, so the map
            // and the list cannot disagree about what has been found.
            if (!Survey.SiteRevealed(body, poi.type)) continue;

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
            sb.AppendLine($"<size=10><color=#8FD0FF>Study: {p.researchPointCost} pts · ~{GameCalendar.Duration(p.researchDuration)}</color></size>");
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
    // WIDENED, to reach both ends of the new −270 °C .. 1000 °C range (PlanetTemperature).
    //
    // The old 0.05 .. 2.2 could not: the temperature law is 288.15·√heat, so 2.2 tops out around 155 °C
    // before greenhouse and type, and a developer could not build a molten world to look at even though
    // the terrain generator now lays magma fields down above 650 °C. Eight reaches ~542 °C on its own,
    // which with a thick atmosphere and a volcanic world's internal heat covers the whole molten band;
    // 0.02 bottoms out near −232 °C, which with an ice world's own modifier reaches the floor.
    //
    // NOTE: on a body with active terraforming (body.terraforming == true), TerraformVisuals.Compose
    // clamps heat back to its own range every tick, so an extreme sandbox value won't stick on a world
    // that's actively terraforming — only on one that isn't.
    const float TempMin = 0.02f, TempMax = 8f;

    // The BioSphere slider drives terrainParams.moisture, whose barren floor is 0.3 and whose lush
    // maximum is 2.0. Named because the 0..1 BioSphere ceiling has to be mapped onto that range in three
    // separate places, and three copies of `Lerp(0.3f, 2f, …)` is three chances to disagree.
    const float BioFloor = 0.3f, BioMax = 2f;

    void BuildTerrainPanel()
    {
        Header("TERRAIN SANDBOX");
        Note("<color=#FFBF4D>Dev Mode.</color> Regenerates this world's surface live. Every map reads the same terrainParams, so what you see here is what the world becomes.");

        // The CLASSIFICATION these sliders produce, live. Generation sets the attributes and the type is
        // derived from them (WorldClassifier); this is the same derivation, so a developer can dial in
        // Mass, Atmosphere, Water and Temperature and watch the world-class change — the spec's
        // "prerequisite settings represented with sliders" made visible.
        var cls = Card();
        Stat(cls, "Classifies as", () => $"<color=#DCE6F0>{WorldClassifier.DescribeLive(body)}</color>");

        var p = body.terrainParams;
        SliderRow("Feature scale", "continent size", 0.4f, 3f, p.scale, v => SetTerrain(0, v));
        // Water Level and Relief are now SEPARATE axes. Water drives seaLevel (slot 5) and only floods;
        // Relief drives elevation amplitude (slot 1) and only changes how tall the land is. They used to
        // be the same number, so dragging water up flattened the mountains rather than drowning them.
        SliderRow("Water Level", "dry world <-> even the peaks drowned", 0f, 1f,
            PlanetTerrainGenerator.WaterLevelFromSeaLevel(p.SeaLevelOrNeutral),
            v => SetTerrain(5, PlanetTerrainGenerator.SeaLevelFromWaterLevel(v)));
        // ---- THE ONE SURFACE-RELIEF SLIDER ----
        //
        // There used to be two, "Elevation range" and "Ruggedness", and between them they could put a
        // mountain anywhere: Ruggedness drove an independent mountain-building noise field that peaked
        // wherever it liked, including in the middle of a plain and in the middle of an ocean. Both are
        // gone, replaced by this — and the replacement is not a rename, it is a different KIND of control.
        //
        // ELEVATION ACCENTUATES WHAT IS ALREADY THERE. A world's shape comes from its geology now: its
        // plates draw its continents, its convergent margins fold up its mountains, its volcanic hotspots
        // pile up their cones (see PlanetTerrainGenerator's elevation pipeline). This slider scales that
        // shape's deviation from sea level, so high ground goes higher and low ground goes lower and
        // ground at the waterline does not move at all. Turn it up and the mountains this world HAS
        // become dramatic; it cannot make one appear where the ground was flat, because there is nothing
        // there to accentuate. That is the request in one sentence.
        //
        // Relief is meaningless on a gas giant — TerraformDiagnosis says so in as many words on the same
        // world ("A gas giant: there is no ground to stand on") — so the control is HIDDEN there rather
        // than greyed. A greyed control asserts the axis exists and is merely unavailable; this axis does
        // not apply to that class of body at all, which is a different statement.
        //
        // Deliberately NOT extended to the other sliders. Atmosphere, Temperature and Feature scale all
        // remain meaningful on a gas giant — it is very much made of atmosphere at a temperature, and the
        // cloud bands the surface renderer draws are laid out by `scale`. Only relief is meaningless.
        bool hasSolidSurface = body.type != CelestialBodyType.GasGiant;

        if (hasSolidSurface)
            SliderRow("Elevation", "flatten toward sea level <-> accentuate this world's own peaks and basins",
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
                                   (body.hasTectonics ? $", +{AtmosphereRules.TectonicBonus(body):0.#} from tectonics" : "") +
                                   (AtmosphereRules.InnerOrbitRetention(WorldClassifier.RelOf(body)) < 0.999f ? ", cut close to the star" : ""));

        // What TERRAFORMING can reach, which is the ceiling above cut again by the heat the world is
        // actually at — the limit air projects clamp to. Shown separately rather than folded into the
        // line above because the two differ for a reason worth seeing: the ceiling is structural (mass,
        // core, orbit), this one is reversible, and watching it climb as the Temperature slider comes
        // down is the clearest demonstration of why shades come before processors.
        Stat(air, "Terraformable to", () =>
        {
            float sustainable = AtmosphereRules.SustainableCeiling(body);
            float heatKeep = AtmosphereRules.HeatRetention(Mathf.Max(0f, body.mass), body.terrainParams.heat);
            return $"{sustainable:0.#}" + (heatKeep < 0.999f ? $" — {(1f - heatKeep) * 100f:F0}% boils off at this temperature" : "")
                   + $" (headroom {AtmosphereRules.Headroom(body):0.#})";
        });

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

        // THE RUGGEDNESS SLIDER USED TO BE HERE, and it is gone rather than moved.
        //
        // It drove `ridge`, an independent mountain-building noise field that the classifiers thresholded
        // directly (`ridge > 0.82` is Mountains on most world types). That field is what put mountains in
        // the middle of plains and on the floors of oceans: it peaked wherever the noise peaked, with no
        // relationship to how the ground got there. Ridge is DERIVED now — from the height the geology
        // produced, plus the collision or the vent that produced it (PlanetTerrainGenerator.
        // RidgeFromRelief) — so there is no longer an independent axis for a slider to drive, and adding
        // one back would mean adding the artefact back with it.
        //
        // What the slider was really for is served by Elevation above: a world with dramatic mountains is
        // a world whose existing peaks are accentuated, not one with extra roughness sprinkled over it.

        Header("GEOLOGY");
        var geo = Card();
        Stat(geo, "Plate tectonics", () => body.hasTectonics
            ? "<color=#FF8F5C>active</color> — its plates draw its continents and fold its mountains"
            : "<color=#9FB4C8>none</color> — no plates, so no continental margins");
        Stat(geo, "Geothermal", () => GeothermalMap.Label(body));
        Stat(geo, "Rotation", () => RotationRules.Describe(body) +
            (body.hasMagneticField ? " — fast enough for a magnetic field" : " — too slow for a magnetic field"));

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
            // Ridge no longer has a slider — it is derived from the ground the geology raised (see the
            // note where the Ruggedness slider used to be). The case survives because `ridge` is still a
            // real field in the save format and in every world's captured natural params, and a future
            // terraforming project that genuinely flattens or roughens a whole world would come through
            // here. Nothing in the UI reaches it today.
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

        // The grid the cursor was over no longer exists — and a regenerated surface can come back at a
        // different resolution, so the remembered cell may not even be a cell any more. Dropped here
        // rather than left for the next mouse move, because the readouts refresh every frame and the
        // mouse may never move again.
        hoverCell = new Vector2Int(-1, -1);
        hoverValid = false;

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
        // The blanket claim used to be "nothing under 70% is drawn". That is no longer true of the
        // Geothermal Index, which paints its plate boundaries from 40 — so the sentence is split into
        // the two statements it was always making, and only the YIELD one is universal.
        Note($"<color=#9FB4C8>Nothing under <b>{SurfaceIndex.ShowFloor * 100f:F0}%</b> yields anything or can be " +
             $"built on — a world's resources sit in a few patches rather than spread thinly over all of it. " +
             $"Every <b>{SurfaceIndex.BandStep * 100f:F0}%</b> above that is a brighter step with its own outline, " +
             $"so the best ground is the innermost, brightest ring. The Geothermal Index also draws its plate " +
             $"boundaries down at <b>{SurfaceIndex.PlateLineFloor * 100f:F0}%</b>, which mark where the crust is " +
             $"moving rather than where it is hot. Zoom in near the cursor for the exact numbers.</color>");

        AddIndexToggle(SurfaceIndexKind.None, "None (plain terrain)");
        // PRESENT, not All. An index this world has nothing for — hydrology on a dry rock, farmland on a
        // sterile one, geothermal on a world with no plates and no plumes — is not offered at all. It is
        // not locked and it is not greyed: greying says "not yet", and there is no yet. See
        // SurfaceIndex.Present, which is re-asked every rebuild, so terraforming water onto a world makes
        // its Hydro row appear.
        foreach (var k in SurfaceIndex.All)
            if (SurfaceIndex.Present(body, k)) AddIndexToggle(k, null);

        // The power grid — folded from the retired Power tab. Its map overlay is a Survey overlay now,
        // reachable from this toggle (mutually exclusive with the index ramps); the diagnostic panel is
        // shown below for a world of yours that's settled, since a grid only exists once something's built.
        AddPowerToggle();
        // "Show tectonics" USED TO BE A TOGGLE HERE. The plate lines and push arrows come up with the
        // Geothermal index now — see the note where AddTectonicsToggle was.
        if (body.owner == FactionManager.Player && body.settled)
            BuildPowerPanel();

        // THE PER-TILE READOUT IS NOT HERE ANY MORE. It moved to the window anchored to the cursor,
        // under the tile's name and temperature — see AppendIndexReadout. It was always a readout about
        // wherever the pointer is, and putting it in a side panel meant reading it cost a look away from
        // the tile and a look back, by which time the number was no longer about anywhere in particular.
        Note("<color=#9FB4C8>Point at the map and the tile's own figures appear beside the cursor, " +
             "under its name and temperature. Zoom in and the numbers appear on the tiles around it too.</color>");
    }

    void AddIndexToggle(SurfaceIndexKind k, string labelOverride)
    {
        var card = Card();
        var group = card.gameObject.AddComponent<CanvasGroup>();

        if (k != SurfaceIndexKind.None)
        {
            // THE LEGEND IS THE MAP, one cell per band. It draws Highlight and Outline at exactly the
            // band values the overlay uses, so the strip is a sample of the real thing rather than an
            // approximation of it — and it stops where the overlay stops, because a legend showing
            // colours the map no longer paints is worse than no legend.
            //
            // Each cell carries its band's own outline down its right-hand edge, which is what the map
            // does at the boundary between two bands. Read left to right it is 70s, 80s, 90s, 100.
            int steps = Mathf.Max(1, Mathf.RoundToInt((1f - SurfaceIndex.ShowFloor) / SurfaceIndex.BandStep));
            var strip = UIFactory.NewUI(card, "Ramp"); UIFactory.AddLayout(strip, 14);
            var srt = strip.GetComponent<RectTransform>();
            for (int i = 0; i < steps; i++)
            {
                float t = steps > 1 ? i / (float)(steps - 1) : 1f;
                var cell = UIFactory.Panel(srt, "s", SurfaceIndex.Highlight(k, t));
                cell.raycastTarget = false;
                var qrt = cell.rectTransform;
                qrt.anchorMin = new Vector2(i / (float)steps, 0); qrt.anchorMax = new Vector2((i + 1) / (float)steps, 1);
                qrt.offsetMin = Vector2.zero; qrt.offsetMax = Vector2.zero;

                var line = UIFactory.Panel(qrt, "edge", SurfaceIndex.Outline(k, t));
                line.raycastTarget = false;
                var lrt = line.rectTransform;
                lrt.anchorMin = new Vector2(0.86f, 0f); lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

                // The FIRST swatch of the Geothermal ramp is labelled 40, not 70, and that is not a
                // fudge — it is what the map draws. Geothermal paints from 40 (a plate margin) and
                // `Band` returns 0 for everything under 70, so this one cell genuinely covers 40 through
                // 79. Labelling it 70 would point at a colour and name a value that colour does not
                // start at, which is the one thing a legend must never do.
                float lo = i == 0 ? SurfaceIndex.DrawFloor(k) : SurfaceIndex.ShowFloor + i * SurfaceIndex.BandStep;
                var lab = UIFactory.Text(qrt, $"{lo * 100f:F0}",
                                         9, new Color(0f, 0f, 0f, 0.75f), TextAlignmentOptions.Center);
                lab.raycastTarget = false;
                UIFactory.Stretch(lab.rectTransform);
            }
            Note(card, SurfaceIndex.Describe(k));

            // ---- PLATES GET SAID OUT LOUD ----
            //
            // Whether a world has continental plates is the single most consequential fact the
            // Geothermal Index carries: it decides where the mountains came from, where the rifts are,
            // where quakes can reach, and whether those faint 40% lines on the map are boundaries or
            // just quiet ground. It was legible only by looking at the map and knowing what to look
            // for. In the index's own red, so it reads as part of the overlay rather than as a caption.
            if (k == SurfaceIndexKind.Geothermal && body != null)
            {
                string hex = ColorUtility.ToHtmlStringRGB(SurfaceIndex.Outline(SurfaceIndexKind.Geothermal));
                Note(card, TectonicsMap.Active(body)
                    ? $"<color=#{hex}><b>Continental plates.</b> The faint lines are plate boundaries; " +
                      $"where two drive together the ground is hottest and the quakes are worst.</color>"
                    : $"<color=#{hex}><b>No plates.</b> Whatever heat this world has comes from hotspots " +
                      $"venting straight up through the crust.</color>");
            }
        }

        // Picking an index no longer switches the grid off — they draw on separate layers now, and the
        // exclusivity was asymmetric anyway: index-then-power worked, power-then-index killed the grid
        // with nothing on screen to explain why.
        var btn = UIFactory.Button(card, "", () =>
        {
            // The card and the icon on the map are two views of one switch. Toggling through
            // IndexToggles means pressing either one moves both, rather than the tab quietly holding a
            // different opinion from the map it is describing.
            IndexToggles.Toggle(body, k);
            activeIndex = IndexToggles.IsOn(body, k) ? k : SurfaceIndexKind.None;
            lastSig = null;
        }, 24);
        live.Button(btn, () =>
        {
            bool on = IndexToggles.IsOn(body, k);   // an index and the power grid can both be up
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
            // and the two are legible together.
            lastSig = null;
        }, 24);
        live.Button(btn, () =>
        {
            if (body.owner != FactionManager.Player || !body.settled)
                return (false, "Power grid — settle this world first");
            return (true, showPowerOverlay ? "• Power grid (showing)" : "Show power grid");
        }, group);
    }

    // ============================================================================================
    // THE PLATE VIEW — no longer a toggle of its own
    //
    // There used to be a "Show tectonics" button here, mutually exclusive with the index ramps: picking
    // it cleared the chosen index and picking an index cleared it. That WAS the two-systems problem, in
    // UI form. A player wanting to know where the crust was hot had one map, a player wanting to know
    // where the plates were had a different one, and the two could not be looked at together — which is
    // exactly how a red fault line ended up running across ground the Heat Index called cold.
    //
    // Selecting the GEOTHERMAL index now draws the plate lines and the per-plate push arrows over its own
    // ramp (see RefreshIndexOverlay / PaintPlateLines / DrawPlateArrows). One map, and it answers both
    // questions at once, because they were always one question.
    //
    // The one thing genuinely lost is the plate-OWNERSHIP wash — a flat tint per plate. It is not missed:
    // the plates are still bounded by their own drawn margins and still carry their arrows, so which
    // continent is which reads off the lines. Putting a per-plate tint under an index ramp would mean two
    // colour scales competing for the same pixels, and the index is the one carrying the numbers.
    //
    // What remains of that overlay — RefreshTectonicsOverlay — is kept and unreferenced rather than
    // deleted, because "show me the raw plate partition" is a fair thing for a future Dev-Mode tool to
    // want and it is thirty lines.
    // ============================================================================================

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
        Stat(orbit, "Year", () => GameCalendar.Duration(OrbitalMechanics.PeriodSeconds(b.orbitSpeed)));
        Stat(orbit, "Eccentricity", () => $"{b.eccentricity:F2}");
        Stat(orbit, "Average Temperature", () =>
        {
            float c = PlanetTemperature.BodyAverageCelsius(b);
            string hex = ColorUtility.ToHtmlStringRGB(PlanetTemperature.GradientColor(c));
            return $"<color=#{hex}>{PlanetTemperature.Label(c)}</color>";
        });
        Stat(orbit, "Axial tilt", () => $"{b.inclination:F0}°" + (Mathf.Abs(b.inclination) > 28f ? "  <color=#FFBF4D>(severe seasons)</color>" : ""));
        // The DAY, in days, plus which way round it turns and whether that is enough to run a dynamo.
        // Degrees per second is the stored unit and was never a figure a player could reason about; a
        // rotation period in the same days the rest of the game now counts in is (see GameCalendar).
        Stat(orbit, "Day length", () =>
        {
            string s = RotationRules.Describe(b);
            if (Mathf.Abs(b.spinSpeed) < RotationRules.MagneticFieldSpin)
                return s + "  <color=#FFBF4D>(too slow for a magnetic field)</color>";
            return s;
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

        // A GAS GIANT KEEPS THE PROJECTS AND LOSES THE CLIMATE.
        //
        // There is no surface, so the climate half of this tab is meaningless on one: nothing to warm, wet
        // or thicken, a habitability ceiling Habitability short-circuits to nothing anyway, and a terraform
        // toggle offering to change the weather on a body with no ground under it. The Inspector hides its
        // Terraform tab outright for exactly that reason.
        //
        // But it must not be a DEAD END, which is what returning early here made it. Shellworld
        // Construction is gated on this body type and nothing else (see Terraforming's project table) and
        // it is the one project that turns a gas giant into somewhere you can stand — so the single useful
        // thing you can do about a gas giant had no way to be reached from the gas giant's own panel. Skip
        // the ceiling, the bar and the toggle; keep the diagnosis, which names NoSurface as the problem,
        // and keep the console that can actually fix it.
        bool noSurface = b.type == CelestialBodyType.GasGiant;

        if (!noSurface)
        {
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
        Note(noSurface
            ? "A gas giant has no surface to raise a ceiling on — Shellworld Construction builds one from scratch. The full console has costs, durations and progress."
            : "Projects raise this world's ceiling permanently. The full console has costs, durations and progress.");
        UIFactory.Button(sidePanel, "Open Terraforming Console »", () => TerraformWindow.Instance?.ShowFor(b), 26);
    }

    // ---- Overlay texture ----
    // One point-filtered texture the size of the grid, stretched over the map. A cell per texel means
    // the overlay lines up with the build grid exactly and costs nothing to redraw.
    void RefreshOverlay()
    {
        // The plate arrows belong to the tectonics overlay only — clear them up front so every other path
        // (index ramp, power, build, nothing) leaves none behind; the tectonics branch redraws them.
        ClearPlateArrows();

        // Same reasoning as the power layer below: resolved before any branch can return early, so an
        // unsurveyed world cannot strand a stale fog on screen — or, worse, fail to draw one.
        RefreshSurveyFog();

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
        // The transmission lines are NOT drawn here. They are quads measured in map pixels, so they have
        // to be re-laid whenever the map is zoomed or panned — which does not rebuild the overlay. See
        // the call in Update.

        // Two different overlays share one texture:
        //  BUILD  — holding a structure raises THAT STRUCTURE'S OWN INDEX, so a farm shows the Fertile
        //           map, an array the Solar map. It used to be a bespoke hot-pink "best sites" wash,
        //           which threw away the one thing the player had already learned to read (the index
        //           colours) and replaced it with a colour that meant nothing anywhere else.
        //  SURVEY — the same index map, chosen by hand.
        // Both are the SAME drawing now (RefreshIndexOverlay), so picking up a farm shows exactly what
        // the Fertile overlay shows, and the survey you did is the survey you build from.
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
            RefreshIndexOverlay(SurfaceIndexKind.Mineral);
            return;
        }

        // ---- WHICH INDEXES ARE UP ----------------------------------------------------------------
        //
        // The icon bar owns this now, on every tab rather than only on Survey. That is the point of
        // moving the buttons onto the map: "where is the good ground" is a question you ask while
        // siting a building, while reading the terrain, and while watching a survey run — not only
        // while sitting in the tab that used to hold the switches.
        //
        // Build mode still forces its own index up on top of whatever the player chose. A footprint
        // being dragged around needs the index it will be scored against visible whether or not anyone
        // remembered to switch it on, and now that overlays composite rather than replace, showing it
        // costs the player's own selection nothing.
        overlayKinds.Clear();
        IndexToggles.Active(body, overlayKinds);

        if (tab == Tab.Build && selected.HasValue)
        {
            var info = SurfaceBuildingDatabase.Get(selected.Value);
            if (info.index != SurfaceIndexKind.None && SurfaceIndex.Unlocked(body, info.index)
                && !overlayKinds.Contains(info.index))
                overlayKinds.Add(info.index);
        }

        bool show = overlayKinds.Count > 0 && body.surface != null;
        overlayImage.gameObject.SetActive(show);
        if (!show) return;

        var kind = overlayKinds[overlayKinds.Count - 1];   // the plate arrows below follow the topmost
        RefreshIndexOverlay(overlayKinds);

        // The plate PUSH ARROWS come up with the Geothermal index too, not only with the dedicated
        // tectonics view. Which way each plate is driving is the fact that explains the map: it is why
        // one margin reads 100% and shakes and the margin on the other side of the same continent reads
        // 45% and does not. Without the arrows the index is a picture of consequences with the cause left
        // out, and the player has to switch views to find it.
        if (kind == SurfaceIndexKind.Geothermal && TectonicsMap.Active(body)) DrawPlateArrows();
    }

    // ============================================================================================
    // AN INDEX MAP: PATCHES, NOT A WASH
    //
    // Draws only the ground SurfaceIndex.Shown accepts — everything at or above the 70% floor, and
    // nothing else — banded every 10%, with each band outlined in a brightened version of the index's own
    // colour. So a good area has a boundary you can aim a footprint at instead of a gradient you have to
    // squint at, and a BETTER area inside it has one of its own.
    //
    // THE TEXTURE IS SUPERSAMPLED, several texels per tile, purely so the outline can be a LINE. At one
    // texel per tile the only way to mark a boundary is to recolour the whole edge tile, which eats the
    // very ground the outline is pointing at. `sub` is chosen from the map's size so a big world doesn't
    // pay for a texture nobody asked for, and at sub == 1 the outline is simply skipped — the fill still
    // carries the information, and a 640-wide gas giant is not where anyone is siting a farm.
    //
    // SUB WENT 3 -> 6, WHICH IS THE OUTLINE GETTING THINNER. The line is one texel wide, so at sub 3 it
    // was a THIRD of a tile on every open side — and a tile open on two sides lost two thirds of itself
    // to its own border. That is the complaint: the outline was eating the ground and the per-tile
    // numbers it was supposed to be pointing at. At sub 6 the same line is a sixth of a tile.
    //
    // The cost is the texture: a 280x140 world goes from 840x420 to 1680x840, about 5 MB, rebuilt only
    // when the overlay actually changes rather than per frame. The tiers below keep the big worlds off
    // that bill, and they are not the worlds anyone sites a building on.
    // ============================================================================================
    // A CELL IS SIXTEEN TEXELS AND THE OUTLINE IS ONE OF THEM. That is the spec, and it is the right
    // one: the outline exists to say where a band ends, and anything thicker starts eating the ground
    // and the per-tile numbers it is pointing at. One texel in sixteen is a hairline that still lands on
    // a pixel at every sane zoom.
    //
    // The tiers below are a MEMORY ceiling, not a taste one. 16 texels a side is 256 texels per cell:
    //
    //     100x50    ->  1600x800    5 MB     sixteenths
    //     200x100   ->  3200x1600  20 MB     sixteenths
    //     400x200   ->  3200x1600  20 MB     eighths
    //     640x320   ->  2560x1280  13 MB     quarters
    //
    // So every world anyone actually sites a building on gets the sixteenth the spec asks for, and the
    // two sizes above that trade it for not spending eighty megabytes on one overlay. The outline stays
    // ONE texel throughout — the fraction changes because the cell does, never because the line does.
    static int OverlaySub(int w, int h)
    {
        long cells = (long)w * h;
        if (cells <= 20_000) return 16;    // up to 200x100 — the spec, exactly
        if (cells <= 90_000) return 8;     // up to 400x200
        return 4;                          // enormous
    }

    /// How much of a tile the band outline may eat, as a fraction of the tile. The texel count follows
    /// from `sub` so the line is the same visual weight whichever tier a world falls into, rather than
    /// being "one texel" and therefore three times fatter on a world that happens to be smaller.
    /// ONE TEXEL. Not a fraction of the cell that gets rounded — the line is a line, and how much of a
    /// cell it covers is whatever one texel happens to be at that world's supersampling.
    static int OutlineTexels(int sub) => 1;

    // ============================================================================================
    // SEVERAL INDEXES AT ONCE
    //
    // The overlay used to be built for exactly one index, which is why it could write straight into the
    // texture. Now that the icon bar lets any number of them be up together (see IndexIconBar), the
    // texture is cleared once and each index is COMPOSITED into it in the canonical order — so two
    // overlapping patches produce a blend of the two rather than whichever happened to be painted last.
    //
    // Order matters and is deliberately SurfaceIndex.All's, not the order the player clicked them in.
    // Compositing is not commutative, and an overlay that changed appearance depending on which button
    // was pressed first would be the sort of thing nobody could ever describe a bug in.
    // ============================================================================================
    readonly List<SurfaceIndexKind> overlayKinds = new List<SurfaceIndexKind>();

    void RefreshIndexOverlay(SurfaceIndexKind kind)
    {
        overlayKinds.Clear();
        if (kind != SurfaceIndexKind.None) overlayKinds.Add(kind);
        RefreshIndexOverlay(overlayKinds);
    }

    void RefreshIndexOverlay(List<SurfaceIndexKind> kinds)
    {
        if (kinds == null || kinds.Count == 0 || body?.surface == null) return;

        int w0 = body.surface.width, h0 = body.surface.height;
        int sub0 = OverlaySub(w0, h0);
        int tw0 = w0 * sub0, th0 = h0 * sub0;
        EnsureOverlayTex(tw0, th0);

        if (overlayPx == null || overlayPx.Length != tw0 * th0) overlayPx = new Color32[tw0 * th0];
        // Zeroed is fully transparent, which is what "no index reaches this ground" means.
        System.Array.Clear(overlayPx, 0, overlayPx.Length);

        for (int i = 0; i < kinds.Count; i++) PaintIndexInto(kinds[i], overlayPx, tw0, th0, sub0);

        overlayTex.SetPixels32(overlayPx);
        overlayTex.Apply();
        overlayImage.texture = overlayTex;
    }

    Color32[] overlayPx;

    /// Composite one index into an overlay buffer that may already hold others.
    void PaintIndexInto(SurfaceIndexKind kind, Color32[] px, int tw, int th, int sub)
    {
        int w = body.surface.width, h = body.surface.height;

        // Resolved for every tile FIRST, because the outline pass has to ask about neighbours — and
        // asking Shown again per neighbour would re-sample the terrain noise four more times per tile.
        // `step` is the tile's 10% band, -1 for ground the index does not reach at all.
        var fill = new Color32[w * h];
        var edgeOf = new Color32[w * h];
        var step = new int[w * h];
        int steps = Mathf.Max(1, Mathf.RoundToInt((1f - SurfaceIndex.ShowFloor) / SurfaceIndex.BandStep));
        for (int i = 0; i < step.Length; i++) step[i] = -1;

        // ============================================================================================
        // THE SURVEY IS DRAWN WHILE IT HAPPENS
        //
        // An index is not a thing you have or have not got any more — it arrives in three passes, and
        // this is what those passes look like. `pass` is how many 10% bands the ship has resolved, so
        // a tile is drawn at min(its own band, pass): during the first pass the whole 70-and-up region
        // is one flat 70s colour, and the 80s and 90s separate out of it as the later passes land.
        //
        // Within the pass in progress, a tile is only upgraded once the survey has REACHED it — see
        // Survey.Reached, which is a ragged front travelling across the world rather than a dissolve.
        // A tile the front has not got to yet stays at the previous pass's fidelity, and during the
        // very first pass that means it is not drawn at all.
        var reveal = Survey.RevealOf(body, kind);
        int maxBand = reveal.complete ? steps - 1 : Mathf.Min(reveal.pass, steps - 1);

        // The sweep head, same as the level-1 blackout has. Without it a level-2 pass is a picture that
        // quietly fills in, and the player cannot tell whether anything is happening or where — which is
        // the whole reason the survey is drawn while it runs rather than reported when it ends.
        var activeMark = new Color32(240, 246, 255, 150);
        int deepShips = Mathf.Max(1, Survey.ShipsOn(body, true));
        bool sweeping = !reveal.complete && reveal.started;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float v = SurfaceIndex.Get(body, kind, x, y);
                if (!SurfaceIndex.ShownFor(body, kind, v, out float t)) continue;

                int band = Mathf.Clamp(Mathf.RoundToInt(t * (steps - 1)), 0, steps - 1);

                if (!reveal.complete)
                {
                    // Has the front reached this cell during the pass currently being painted?
                    bool reached = Survey.Reached(body, x, y, reveal.frac);
                    int resolved = reached ? maxBand : maxBand - 1;
                    if (resolved < 0) continue;                 // first pass, not yet reached: nothing here
                    band = Mathf.Min(band, resolved);
                    t = steps > 1 ? band / (float)(steps - 1) : 1f;
                }

                step[i] = band;
                fill[i] = SurfaceIndex.Highlight(kind, t);
                edgeOf[i] = SurfaceIndex.Outline(kind, t);

                // Under the sweep head, the cell is drawn white instead of its band colour — it is being
                // read right now, and what it is worth is not settled until the head has passed.
                if (sweeping && Survey.BeingSurveyed(body, x, y, reveal.frac, deepShips))
                {
                    fill[i] = activeMark;
                    edgeOf[i] = activeMark;
                }

                // NAMED ORE DEPOSITS, on the mineral map only, and the one thing here that is a find
                // rather than a field: drawn in the ore's own colour at near-full strength so a seam
                // reads as Ferralite or Aurelium rather than as slightly warmer ground. Always inside
                // the shown band by construction — a deposit floors the Mineral index at 0.6.
                if (kind == SurfaceIndexKind.Mineral)
                {
                    var tile = body.surface.tiles[x, y];
                    if (tile != null && tile.HasOre)
                    {
                        var oc = OreDatabase.Get(tile.ore).color;
                        fill[i] = new Color(oc.r, oc.g, oc.b, Mathf.Lerp(0.62f, 0.95f, tile.oreRichness));
                    }
                }
            }

        // ============================================================================================
        // EVERY BAND GETS ITS OWN EDGE, so the contours NEST
        //
        // The outline test used to be "is my neighbour unlit", which drew one line round the whole
        // highlighted region and said nothing about its inside: a 95% core and a 72% fringe came out as
        // one shape with one border, and the only way to tell them apart was to zoom in far enough for
        // the numbers to appear. Now a tile draws an edge against any neighbour in a LOWER band — unlit
        // ground being the lowest of all — so the 90s patch is ringed inside the 80s patch, which is
        // ringed inside the 70s. The quality distribution reads from across the map: aim for the
        // innermost ring, and zoom in only to confirm the exact number.
        //
        // The HIGHER band draws the line, never the lower one, so a bright ring sits inside its own
        // bright ground rather than eating a tile of the weaker ground beside it.
        // ============================================================================================
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int me = step[i];
                if (me < 0) continue;
                Color32 c = fill[i], edge = edgeOf[i];

                // LONGITUDE WRAPS and latitude does not, exactly as the map itself does: a patch running
                // over the date line is one patch and must not be outlined down the seam, while a patch
                // that reaches the pole row genuinely ends there.
                bool openL = step[y * w + ((x - 1 + w) % w)] < me;
                bool openR = step[y * w + ((x + 1) % w)] < me;
                bool openD = y == 0 || step[(y - 1) * w + x] < me;
                bool openU = y == h - 1 || step[(y + 1) * w + x] < me;

                int et = OutlineTexels(sub);
                for (int sy = 0; sy < sub; sy++)
                    for (int sx = 0; sx < sub; sx++)
                    {
                        bool onEdge = sub > 1 &&
                            ((openL && sx < et) || (openR && sx >= sub - et) ||
                             (openD && sy < et) || (openU && sy >= sub - et));
                        int t = (y * sub + sy) * tw + (x * sub + sx);
                        px[t] = Blend(px[t], onEdge ? edge : c);
                    }
            }

        // ============================================================================================
        // THE PLATE LINES BELONG TO THE GEOTHERMAL INDEX
        //
        // "If a planet has Tectonic Activity (continental plates), then when viewing the Heat Index —
        // either in the survey tab, or while a science ship is doing a level 2 survey — the Tectonic
        // Plate Lines should appear."
        //
        // They are not a separate view any more, because they were never describing separate ground. The
        // index's own gradient already reaches its brightest exactly along a convergent margin (that is
        // what puts a head-on collision at 100%), so the line drawn here is a GUIDELINE over the top of
        // it: it says precisely where the boundary runs, which the smooth field can only imply.
        //
        // Drawn after the bands and their outlines, so it sits over them, and only on tiles the survey
        // has already resolved — a plate map handed over before the survey reached that ground would be
        // giving away the very thing the survey is for.
        if (kind == SurfaceIndexKind.Geothermal && TectonicsMap.Active(body))
            PaintPlateLines(px, tw, th, sub, step);
    }

    /// Source-over. The straightforward compositing rule, written out because the alternative — letting
    /// the later index simply overwrite — is what made a second overlay useless before highlights
    /// dropped to 40%.
    static Color32 Blend(Color32 under, Color32 over)
    {
        float sa = over.a / 255f;
        if (sa <= 0.001f) return under;
        if (sa >= 0.999f && under.a == 0) return over;
        float ua = under.a / 255f;
        float outA = sa + ua * (1f - sa);
        if (outA <= 0.0001f) return new Color32(0, 0, 0, 0);
        float inv = 1f / outA;
        return new Color32(
            (byte)Mathf.Clamp((over.r * sa + under.r * ua * (1f - sa)) * inv, 0f, 255f),
            (byte)Mathf.Clamp((over.g * sa + under.g * ua * (1f - sa)) * inv, 0f, 255f),
            (byte)Mathf.Clamp((over.b * sa + under.b * ua * (1f - sa)) * inv, 0f, 255f),
            (byte)Mathf.Clamp(outA * 255f, 0f, 255f));
    }

    /// The fault lines, painted over a finished Geothermal overlay.
    ///
    /// Reads the SAME TectonicsMap.Tiles raster the standalone tectonics view does — one plate map, so
    /// the line cannot be in one place on one overlay and somewhere else on the other. `step` carries
    /// which tiles the survey has resolved; a tile at -1 has not been read yet and gets no line.
    void PaintPlateLines(Color32[] px, int tw, int th, int sub, int[] step)
    {
        var map = TectonicsMap.Tiles(body);
        if (map == null) return;
        int w = body.surface.width, h = body.surface.height;
        if (map.width != w || map.height != h) return;

        // Slightly deeper and more opaque than anything the Geothermal ramp itself can reach, so the line
        // reads as a line over the field rather than as the field's own hottest contour.
        var fault = new Color32(255, 40, 34, 250);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!map.border[i]) continue;
                if (step[i] < 0) continue;      // not surveyed yet — nothing to annotate

                for (int sy = 0; sy < sub; sy++)
                    for (int sx = 0; sx < sub; sx++)
                        px[(y * sub + sy) * tw + (x * sub + sx)] = fault;
            }
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

        // Which tiles any grid lights, kept so the edge pass below can find the outside of the lit
        // region. Reused across repaints for the same reason `powerPx` is — this runs several times a
        // second for as long as the tab is open.
        if (powerLit == null || powerLit.Length != w * h) powerLit = new bool[w * h];
        System.Array.Clear(powerLit, 0, powerLit.Length);

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
                powerLit[c.y * w + c.x] = true;
            }
        }

        // Sources and relays go on before the rim so the rim can still draw over their own edge — they
        // are lit tiles like any other and a grid that ends on a relay should still show where it ends.
        var electricBlue = new Color(0.25f, 0.72f, 1.00f, 0.85f);
        foreach (var p in SurfaceBuildManager.On(body))
        {
            // The blue "this is infrastructure" mark goes on what MAKES or MOVES or BANKS power. Read
            // through Projects rather than off powerRange, so a switchyard that no longer lights ground
            // stops being drawn as though it did.
            if (!PowerGrid.Projects(p.Info) && p.Info.powerStorage <= 0f) continue;
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
            {
                if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h) continue;
                px[c.y * w + c.x] = electricBlue;
            }
        }

        // ============================================================================================
        // THE GRID'S EDGE, IN A LIGHTER YELLOW — AND AS A LINE, NOT A TILE
        //
        // The yellow coverage is a translucent wash, and a translucent wash over terrain has no boundary
        // you can point at: you can see roughly where the power is and not where it STOPS, which is the
        // only question the overlay is really asked. Worse, the wash is a reach radius, so its edge is
        // exactly the thing the player is trying to plan a pylon against.
        //
        // So every lit tile with an unlit side neighbour gets a rim in a paler, more opaque version of
        // its own grid's colour — paler rather than a fixed white, so a failing grid's rim stays grey and
        // a healthy one's stays yellow and the two are still told apart at the edge as well as in the
        // middle. The lit set is shared across grids on purpose: where two grids' coverage touches they
        // are ONE grid by definition (PowerGrid's whole rule), and drawing a rim down the middle of a
        // single grid would say the opposite.
        //
        // IT USED TO RECOLOUR THE WHOLE TILE, because the texture was one texel per tile and a whole
        // tile was the thinnest line available. That made the border of the grid a full cell wide —
        // wider than any index outline, and it swallowed the ground and the per-tile numbers at exactly
        // the edge where a player is deciding where the next pylon goes. Supersampling like the index
        // overlay makes the rim a third of a tile instead.
        //
        // A COARSER SUB THAN THE INDEXES, deliberately. This repaints four times a second for as long as
        // the tab is open, where an index overlay is rebuilt only when it changes; the indexes can
        // afford 36 texels per tile and this cannot.
        // ============================================================================================
        int sub = PowerSub(w, h);
        int tw = w * sub, th = h * sub;
        int et = OutlineTexels(sub);

        if (powerOut == null || powerOut.Length != tw * th) powerOut = new Color[tw * th];
        var outp = powerOut;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                Color c = px[i];

                // Longitude wraps, latitude does not — the pole rows genuinely are the end of the map,
                // and a grid reaching one has its edge there.
                bool lit = powerLit[i];
                bool openL = lit && !powerLit[y * w + ((x - 1 + w) % w)];
                bool openR = lit && !powerLit[y * w + ((x + 1) % w)];
                bool openD = lit && (y == 0 || !powerLit[(y - 1) * w + x]);
                bool openU = lit && (y == h - 1 || !powerLit[(y + 1) * w + x]);

                Color rim = (openL || openR || openD || openU)
                    ? new Color(Mathf.Lerp(c.r, 1f, 0.55f), Mathf.Lerp(c.g, 1f, 0.6f),
                                Mathf.Lerp(c.b, 0.55f, 0.5f), Mathf.Min(1f, c.a + 0.34f))
                    : c;

                for (int sy = 0; sy < sub; sy++)
                    for (int sx = 0; sx < sub; sx++)
                    {
                        bool onEdge = sub > 1 &&
                            ((openL && sx < et) || (openR && sx >= sub - et) ||
                             (openD && sy < et) || (openU && sy >= sub - et));
                        outp[(y * sub + sy) * tw + (x * sub + sx)] = onEdge ? rim : c;
                    }
            }

        // Its OWN texture and its OWN image — the ground index is very likely using the other one at the
        // same time now.
        if (powerTex == null || powerTex.width != tw || powerTex.height != th)
        {
            if (powerTex != null) Destroy(powerTex);
            powerTex = new Texture2D(tw, th, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }

        powerTex.SetPixels(outp);
        powerTex.Apply();
        if (powerOverlayImage != null) powerOverlayImage.texture = powerTex;
    }

    // ============================================================================================
    // THE SURVEY FOG — one black cell per grid cell, lifting as the ship works
    //
    // Deliberately ONE TEXEL PER TILE and point-filtered, because the thing being drawn is the GRID:
    // the player is meant to see the shape of the map they are about to be given and watch it fill in
    // square by square. Supersampling it would soften exactly the edges that make it read as cells.
    //
    // ============================================================================================

    // ---- THE VEIL IS TRANSLUCENT AGAIN, AND IT IS THE COLOUR OF THE SKY -------------------------
    //
    // It was alpha 255 — solid black — on the reasoning that a partly transparent blackout reads as a
    // DIMMED map rather than a covered one, and a map that is merely dim reads as a rendering fault.
    //
    // That was right about the failure and wrong about the fix. What made the old translucent version
    // look broken was that it was a flat grey wash at a low alpha, and a flat grey wash IS what a
    // rendering fault looks like. The answer is not opacity, it is making the veil look like something:
    // cloud, over a world that has air, in the colour that world's air actually is, and denser where
    // the air is denser. See SurveyVeil, which owns all of that.
    //
    // The per-cell alpha now comes from Survey.VeilAt, which is 1 over untouched ground, 0 over ground
    // already mapped, and somewhere between over the block a ship is standing on right now — so the
    // working block visibly thins across its three and a half seconds and is clear at the moment the
    // survey moves on.

    /// Does this world's map need blacking out at all?
    static bool WantsFog(CelestialBody b) =>
        b?.surface != null && !GameMode.DevMode && !b.Surveyed;

    /// Paint one world's blackout into `tex`, creating or resizing it as needed. Returns the texture.
    ///
    /// Shared by the host map and every moon pane, because a moon is surveyed exactly as a planet is and
    /// two implementations of "which cells are still dark" is two places for them to disagree.
    Texture2D BuildFogTexture(CelestialBody b, Texture2D tex)
    {
        int w = b.surface.width, h = b.surface.height;
        if (surveyFogPx == null || surveyFogPx.Length != w * h) surveyFogPx = new Color32[w * h];

        // One colour for the whole world, sampled once rather than per cell — it depends only on the
        // body, and asking sixty thousand times for the same answer is the sort of thing that makes a
        // texture rebuild expensive.
        Color veil = SurveyVeil.ColorFor(b);
        byte vr = (byte)Mathf.Clamp(veil.r * 255f, 0f, 255f);
        byte vg = (byte)Mathf.Clamp(veil.g * 255f, 0f, 255f);
        byte vb = (byte)Mathf.Clamp(veil.b * 255f, 0f, 255f);
        float peak = veil.a;

        // The blocks under a ship right now, fetched ONCE. Asking Survey per pixel would re-derive
        // every ship's band assignment sixty thousand times a rebuild, which is the cost this whole
        // block rework exists to remove.
        int nb = Survey.ActiveBlocks(b, fogBlocks);

        // And every row's block size, once. Asking per pixel walks the unit list two hundred thousand
        // times to produce as many answers as there are rows — see Survey's fast-path header.
        fogRowBlocks = Survey.RowBlocks(b, fogRowBlocks);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cover;
                if (Survey.ReachedGround(b, x, y, fogRowBlocks)) cover = 0f;
                else
                {
                    cover = 1f;
                    for (int i = 0; i < nb; i++)
                        if (InFogBlock(fogBlocks[i], x, y, w)) { cover = 1f - fogBlocks[i].frac; break; }
                }

                byte a = (byte)Mathf.Clamp(cover * peak * 255f, 0f, 255f);
                surveyFogPx[y * w + x] = new Color32(vr, vg, vb, a);
            }

        if (tex == null || tex.width != w || tex.height != h)
        {
            if (tex != null) Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }

        tex.SetPixels32(surveyFogPx);
        tex.Apply();
        return tex;
    }

    // Reused between rebuilds; matches Survey's own scratch size.
    static readonly Survey.Block[] fogBlocks = new Survey.Block[8];

    // Per-row block sizes for the world currently being painted. Grown by Survey.RowBlocks when the
    // world changes size, which for a moon pane means every time a different moon is opened.
    int[] fogRowBlocks;

    /// Blocks wrap in rank space, so one near the right edge is two runs of columns rather than one.
    static bool InFogBlock(Survey.Block blk, int x, int y, int w)
    {
        if (y < blk.y0 || y >= blk.y0 + blk.h) return false;
        int dx = ((x - blk.x0) % w + w) % w;
        return dx < blk.w;
    }

    // ============================================================================================
    // THE MARKERS
    //
    // One rectangle per ship on station, framing the block it is working, pulsing on the fleet beat.
    // Pooled: a world is worked by at most a handful of ships and the rectangles are reused rather
    // than rebuilt, so this runs every frame without allocating.
    // ============================================================================================
    void RefreshSurveyMarkers()
    {
        if (surveyMarkerLayer == null) return;

        int n = (body?.surface != null && !GameMode.DevMode && !body.Surveyed)
            ? Survey.ActiveBlocks(body, fogBlocks) : 0;

        if (n == 0)
        {
            if (surveyMarkerLayer.gameObject.activeSelf) surveyMarkerLayer.gameObject.SetActive(false);
            return;
        }
        if (!surveyMarkerLayer.gameObject.activeSelf) surveyMarkerLayer.gameObject.SetActive(true);

        while (surveyMarkers.Count < n) MakeSurveyMarker();

        int w = body.surface.width, h = body.surface.height;
        Color fill = SurveyVeil.MarkerColor();
        Color edge = SurveyVeil.MarkerEdgeColor();

        for (int i = 0; i < surveyMarkers.Count; i++)
        {
            var rt = surveyMarkers[i];
            bool live = i < n;
            if (rt.gameObject.activeSelf != live) rt.gameObject.SetActive(live);
            if (!live) continue;

            var blk = fogBlocks[i];

            // Blocks wrap in rank space. A block straddling the seam is drawn at its left run only —
            // the map's own mirror layers redraw the wrapped remainder, so a second rectangle here
            // would double up on exactly the worlds where the mirrors are already doing the job.
            float x0 = blk.x0 / (float)w;
            float x1 = Mathf.Min(1f, (blk.x0 + blk.w) / (float)w);
            float y0 = blk.y0 / (float)h;
            float y1 = Mathf.Min(1f, (blk.y0 + blk.h) / (float)h);

            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            surveyMarkerFills[i].color = fill;
            surveyMarkerEdges[i].color = edge;
        }
    }

    void MakeSurveyMarker()
    {
        var rt = UIFactory.NewUI(surveyMarkerLayer, "Block").GetComponent<RectTransform>();
        rt.pivot = new Vector2(0.5f, 0.5f);

        var f = rt.gameObject.AddComponent<Image>();
        f.raycastTarget = false;

        // The border is four child edges at a fixed pixel thickness, so the frame stays a hairline at
        // every zoom instead of growing into the block it is supposed to be outlining.
        var holder = UIFactory.NewUI(rt, "Edges").GetComponent<RectTransform>();
        holder.anchorMin = Vector2.zero; holder.anchorMax = Vector2.one;
        holder.offsetMin = Vector2.zero; holder.offsetMax = Vector2.zero;
        var tint = holder.gameObject.AddComponent<Image>();
        tint.color = new Color(0, 0, 0, 0);
        tint.raycastTarget = false;

        const float T = 2f;
        MarkerEdge(holder, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -T), Vector2.zero);
        MarkerEdge(holder, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, T));
        MarkerEdge(holder, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(T, 0));
        MarkerEdge(holder, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-T, 0), Vector2.zero);

        surveyMarkers.Add(rt);
        surveyMarkerFills.Add(f);
        surveyMarkerEdges.Add(tint);
    }

    static void MarkerEdge(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var e = UIFactory.NewUI(parent, "Edge").GetComponent<RectTransform>();
        e.anchorMin = aMin; e.anchorMax = aMax;
        e.offsetMin = oMin; e.offsetMax = oMax;
        var img = e.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        e.gameObject.AddComponent<FrameEdgeTint>();
    }

    void RefreshSurveyFog()
    {
        // ---- The host map ----
        if (surveyFogImage != null)
        {
            bool want = WantsFog(body);
            if (!want)
            {
                if (surveyFogImage.gameObject.activeSelf) surveyFogImage.gameObject.SetActive(false);
            }
            else
            {
                surveyFogTex = BuildFogTexture(body, surveyFogTex);
                surveyFogImage.texture = surveyFogTex;
                if (!surveyFogImage.gameObject.activeSelf) surveyFogImage.gameObject.SetActive(true);
            }
        }

        // ---- And every open moon pane, on its own progress ----
        foreach (var kv in moonFog)
        {
            var m = kv.Key;
            var img = kv.Value;
            if (img == null) continue;

            bool want = WantsFog(m);
            if (!want)
            {
                if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                continue;
            }

            moonFogTex.TryGetValue(m, out var mtex);
            mtex = BuildFogTexture(m, mtex);
            moonFogTex[m] = mtex;
            img.texture = mtex;
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        }
    }

    /// Texels per tile for the power overlay. See the rim note in RefreshPowerOverlay for why this is
    /// coarser than the index overlays' — this one repaints on a timer, they repaint on a change.
    static int PowerSub(int w, int h)
    {
        long cells = (long)w * h;
        if (cells <= 20_000) return 8;
        if (cells <= 90_000) return 4;
        return 2;
    }

    // ============================================================================================
    // THE TRANSMISSION LINES
    //
    // Electric blue, pylon to pylon, in the Power overlay — the same blue the plants and relays are
    // drawn in, because it is the same thing: the infrastructure, as opposed to the yellow ground it
    // lights. A chain of relays used to read as a row of unrelated dots, and whether any two of them
    // were actually carrying power to each other was invisible.
    //
    // Drawn as ROTATED QUADS on their own layer rather than into the overlay texture. The texture is one
    // texel per tile, so a diagonal line in it would be a staircase of whole cells — unreadable at the
    // zoom levels where the chain matters, and it would also stamp over the yellow it is supposed to sit
    // on top of. A quad is a straight line at any angle and any zoom, and its thickness stays in pixels.
    // ============================================================================================
    RectTransform nodeLinkLayer;

    // WHICH pylons are joined, cached; WHERE those joins are on screen, per frame.
    //
    // The two change on completely different clocks and separating them matters. The link list is an
    // O(relays^2) scan with a dictionary lookup per pair, and it only moves when a building does — but
    // the QUADS are measured in map pixels and have to be re-laid every time the map is zoomed or
    // panned. Rebuilding the list every frame to satisfy the quads would put a quadratic scan on the
    // frame budget for a set of wires that had not changed.
    List<PowerGrid.NodeLink> nodeLinks = new List<PowerGrid.NodeLink>();
    float nodeLinkRefreshIn;

    void DrawNodeLinks(bool show)
    {
        if (nodeLinkLayer == null)
        {
            if (!show) return;
            nodeLinkLayer = UIFactory.NewUI(mapRT, "NodeLinks").GetComponent<RectTransform>();
            UIFactory.Stretch(nodeLinkLayer);
            var img = nodeLinkLayer.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0); img.raycastTarget = false;
        }

        for (int i = nodeLinkLayer.childCount - 1; i >= 0; i--) Destroy(nodeLinkLayer.GetChild(i).gameObject);
        nodeLinkLayer.gameObject.SetActive(show);
        if (!show || body?.surface == null) { nodeLinks.Clear(); return; }

        // On the same cadence as the power overlay's repaint, and for the same reason: it follows a
        // number that drifts, and a quarter-second of lag on a wire appearing is invisible.
        nodeLinkRefreshIn -= Time.unscaledDeltaTime;
        if (nodeLinkRefreshIn <= 0f || nodeLinks == null)
        {
            nodeLinkRefreshIn = 0.25f;
            nodeLinks = PowerGrid.NodeLinks(body);
        }

        int w = body.surface.width, h = body.surface.height;
        float tileW = mapRT.rect.width / w, tileH = mapRT.rect.height / h;

        var nets = PowerGrid.Nets(body);

        foreach (var link in nodeLinks)
        {
            // The same electric blue RefreshPowerOverlay paints the relays themselves in — dulled to
            // the grey it paints a FAILED grid in when this span is carrying nothing. A chain that is
            // intact but dead should not look identical to one delivering power; the yellow underneath
            // it already says so, and the wire agreeing costs one lookup.
            var net = link.net >= 1 && link.net <= nets.Count ? nets[link.net - 1] : null;
            var wire = net != null && net.Failed
                ? new Color(0.42f, 0.45f, 0.50f, 0.85f)
                : new Color(0.25f, 0.72f, 1.00f, 0.95f);

            // Cell centres, in the map's local space (origin at its middle, which is what anchoring at
            // 0.5,0.5 and offsetting by an anchoredPosition means).
            Vector2 a = new Vector2((link.a.x + 0.5f - w * 0.5f) * tileW, (link.a.y + 0.5f - h * 0.5f) * tileH);
            Vector2 bb = new Vector2((link.b.x + 0.5f - w * 0.5f) * tileW, (link.b.y + 0.5f - h * 0.5f) * tileH);

            Vector2 d = bb - a;
            float len = d.magnitude;
            if (len < 0.01f) continue;

            var q = UIFactory.Panel(nodeLinkLayer, "wire", wire);
            q.raycastTarget = false;
            var rt = q.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(len, NodeLinkPx);
            rt.anchoredPosition = (a + bb) * 0.5f;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }
    }

    /// Wire thickness, in screen pixels — like the building outlines, so it stays a line at every zoom.
    const float NodeLinkPx = 2.5f;

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
    // its mountains from, so the red lines mark exactly the margins the ranges and volcanoes gather along
    // — the line is a GUIDELINE, not a boundary the mountains stay inside: the belt the terrain reads is
    // several times wider than the line drawn here, and deliberately so. Static (plates don't move frame
    // to frame), so it's painted once per rebuild rather than on a timer.
    void RefreshTectonicsOverlay()
    {
        int w = body.surface.width, h = body.surface.height;
        EnsureOverlayTex(w, h);

        var wash  = new Color(0.86f, 0.89f, 0.93f, 0.30f);   // translucent white plate wash
        var fault = new Color(0.95f, 0.16f, 0.16f, 0.98f);   // solid red plate border

        // READ OFF THE PLATE MAP, not off a sampled distance field. TectonicsMap.Tiles has already decided
        // which plate owns every tile and which tiles carry the line, and it guarantees the three things a
        // thresholded band could not: exactly one tile of red between any two plates, never two; a line
        // that is never dashed or missing; and a line that is 4-CONNECTED, stepping sideways one tile at a
        // time as it climbs, rather than a staircase of corner-touching dots. See the header there.
        var map = TectonicsMap.Tiles(body);
        var px = new Color[w * h];

        if (map == null || map.width != w || map.height != h)
        {
            for (int i = 0; i < px.Length; i++) px[i] = wash;
        }
        else
        {
            for (int i = 0; i < px.Length; i++) px[i] = map.border[i] ? fault : wash;
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

        // A plate the absorption pass took off the map has no arrow. It has no ground either — an arrow
        // over somebody else's continent, pushing a plate that is no longer drawn anywhere, is the map
        // annotating a thing that is not on it.
        var tiles = TectonicsMap.Tiles(body);

        foreach (var plate in layout.plates)
        {
            if (tiles?.plateDrawn != null && plate.id < tiles.plateDrawn.Length && !tiles.plateDrawn[plate.id])
                continue;

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

        // The animated ghosts live in the layer that was just emptied, so their list has to be dropped
        // with them or AnimateConstruction spends every frame walking references to destroyed quads.
        constructionQuads.Clear();

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

        DrawConstructionGhosts();

        SyncWrapMirrors();
    }

    // ============================================================================================
    // CONSTRUCTION GHOSTS — the ground a queued build has already claimed
    //
    // A confirmed build takes real time, and until this the map showed absolutely nothing for it: you
    // drew a six-tile farm, paid for it, and the world looked exactly as it had a second earlier. The
    // ground was reserved (SurfaceBuildQueue.PendingCells refuses a second job on it) but the only
    // evidence was a refusal when you tried to build there again. So the tiles a job holds are drawn as
    // a translucent shell of the building that is coming.
    //
    // READS AS UNFINISHED, THREE WAYS, because "faint version of a building" alone would just look like
    // a building drawn wrong:
    //
    //   IT BREATHES        a slow pulse in alpha — nothing else on this map moves, so movement means work
    //   IT FILLS IN        opacity rises with progress, so a site at 90% is nearly solid and one just
    //                      confirmed is a whisper. The map answers "how far along?" without the panel.
    //   ITS OUTLINE IS ITS OWN COLOUR, not the black every standing structure carries. That black line
    //                      is what makes a placed building look planted; withholding it is what makes
    //                      this one look pencilled in.
    //
    // A PAUSED job holds still and dims. It is not slow, it is stopped, and a paused site that kept
    // breathing at the same rate as a working one would say the opposite of what the pause button did.
    // ============================================================================================

    /// One ghost quad and the job it belongs to, so the animation can read that job's live progress
    /// rather than baking a brightness in at draw time and freezing it there until the next rebuild.
    struct ConstructionQuad
    {
        public Image img;
        public SurfaceBuildJob job;
    }
    readonly List<ConstructionQuad> constructionQuads = new List<ConstructionQuad>();

    void DrawConstructionGhosts()
    {
        var jobs = SurfaceBuildQueue.Peek(body);
        if (jobs == null || jobs.Count == 0) return;

        foreach (var job in jobs)
        {
            if (job?.cells == null || job.cells.Count == 0) continue;
            var info = SurfaceBuildingDatabase.Get(job.type);
            if (info == null) continue;

            // The colour the finished building will be, so the ghost is recognisably THAT structure —
            // you can tell the queued reactor from the queued farm without reading a word. Alpha is set
            // per frame by AnimateConstruction; this is only the starting value for the first frame.
            var fill = Vivid(info.color);
            fill.a = 0.3f;

            // The outline sits well above the fill's alpha: a shell that fades toward nothing still has
            // a crisp edge, so the FOOTPRINT — which is the thing the player needs to plan around — is
            // legible even when the site has barely started.
            var edge = Vivid(info.color);
            edge.a = 0.85f;

            foreach (var cell in job.cells)
                constructionQuads.Add(new ConstructionQuad { img = AddCellQuad(pieceLayer, cell.x, cell.y, fill), job = job });
            OutlineFootprint(pieceLayer, job.cells, edge);

            // Onto the wrap mirrors as well, for the same reason the buildings go there: a site that
            // vanishes as it crosses the seam looks like a cancelled project.
            foreach (var m in mirrors)
            {
                if (m.pieces == null) continue;
                foreach (var cell in job.cells)
                    constructionQuads.Add(new ConstructionQuad { img = AddCellQuad(m.pieces, cell.x, cell.y, fill), job = job });
                OutlineFootprint(m.pieces, job.cells, edge);
            }
        }
    }

    /// The breath and the fill-in, per frame. Cheap: an alpha write per ghost cell, and only when it
    /// actually changed — writing a Graphic's colour dirties its mesh, and these sit on the same canvas
    /// as everything else in the window.
    void AnimateConstruction()
    {
        if (constructionQuads.Count == 0) return;

        float breath = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);

        for (int i = 0; i < constructionQuads.Count; i++)
        {
            var q = constructionQuads[i];
            if (q.img == null || q.job == null) continue;

            // A held job sits at a flat, dim alpha with no breath in it at all: still, and visibly
            // stalled, however far along it happens to be.
            float a = q.job.paused
                ? 0.16f
                : Mathf.Lerp(0.20f, 0.60f, Mathf.Clamp01(q.job.Progress)) + 0.08f * breath;

            var c = q.img.color;
            if (Mathf.Abs(c.a - a) < 0.004f) continue;
            c.a = a;
            q.img.color = c;
        }
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
        // Captured BEFORE the placement, because placing it is what ends the landing — asking
        // afterwards would always say no and the ship would never be consumed.
        bool wasLanding = ColonyLanding.AwaitingOn(body)
                       && pendingType.Value == SurfaceBuildingType.ColonyShipBase;

        if (SurfaceBuildManager.CanPlace(body, pendingType.Value, pendingCell.x, pendingCell.y, pendingRotation, out _) &&
            SurfaceBuildManager.Place(body, pendingType.Value, pendingCell.x, pendingCell.y, pendingRotation))
        {
            lastSig = null;   // the built list and the map both changed
            SimpleAudio.Instance?.PlayComplete();

            // THE HULL IS DOWN — the world becomes a colony and the ship in orbit is consumed.
            // Only after a placement that actually SUCCEEDED, so a refused landing leaves the ship
            // exactly where it was rather than deleting it for a building that never went up.
            if (wasLanding)
            {
                ColonyLanding.Complete();
                selected = null;          // stop holding the piece; the landing is over
            }
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
    ///
    /// `edge` overrides the colour. A standing building outlines in black — that black line is what
    /// separates it from the ground — while a construction site outlines in its own hue, so the two
    /// never read as the same kind of thing even at a glance.
    void OutlineFootprint(RectTransform layer, List<Vector2Int> cells, Color? edge = null)
    {
        if (cells == null || cells.Count == 0) return;

        var set = new HashSet<Vector2Int>(cells);
        foreach (var cell in cells)
        {
            if (!set.Contains(cell + Vector2Int.up))    AddEdgeQuad(layer, cell.x, cell.y, 0, edge);
            if (!set.Contains(cell + Vector2Int.right)) AddEdgeQuad(layer, cell.x, cell.y, 1, edge);
            if (!set.Contains(cell + Vector2Int.down))  AddEdgeQuad(layer, cell.x, cell.y, 2, edge);
            if (!set.Contains(cell + Vector2Int.left))  AddEdgeQuad(layer, cell.x, cell.y, 3, edge);
        }
    }

    /// Outline thickness, in screen pixels. Thin, but never sub-pixel — below 1 it starts dropping out
    /// entirely on some edges as the rasteriser rounds.
    const float OutlinePx = 1.5f;

    /// One edge of one cell. dir: 0=top, 1=right, 2=bottom, 3=left. Drawn INSIDE the cell, so the
    /// outline never encroaches on the neighbouring tile's ground.
    void AddEdgeQuad(RectTransform layer, int x, int y, int dir, Color? edge = null)
    {
        if (body?.surface == null) return;
        int w = body.surface.width, h = body.surface.height;

        float l = x / (float)w, r = (x + 1) / (float)w;
        float b = y / (float)h, t = (y + 1) / (float)h;

        var q = UIFactory.Panel(layer, "o", edge ?? Color.black);
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

        // ---- PLACEMENT MODE: the footprint being drawn ----
        //
        // Drawn in the structure's own colour rather than the old red/green pass-fail wash. The wash was
        // the only feedback the release-commits gesture could give, and it had to carry every possible
        // refusal in one bit. Now an illegal tile is simply UNPAINTABLE — the brush refuses it and says
        // why, at the tile (see the refusal label) — so what is on the map is by construction a legal
        // footprint, and colouring it as though it might not be would be a lie.
        if (BuildPlacement.IsFor(body) && BuildPlacement.Tiles > 0)
        {
            var pc = Vivid(info.color);
            foreach (var cell in BuildPlacement.Cells) AddCellQuad(ghostLayer, cell.x, cell.y, pc);

            // The brush still rides the cursor, so it is clear the shape is still being drawn — but only
            // over ground the next tile could actually go on.
            if (HasHoverCell && !BuildPlacement.HasCell(hoverCell)
                && BuildPlacement.Guidance().Contains(hoverCell))
                AddCellQuad(ghostLayer, hoverCell.x, hoverCell.y,
                            new Color(pc.r, pc.g, pc.b, 0.55f));
            return;
        }

        // ---- The node chain, which is still a release-commits drag ----
        if (drawing && drawCells.Count > 0)
        {
            Color dc = string.IsNullOrEmpty(drawWhy) ? Vivid(info.color) : new Color(1f, 0.25f, 0.2f, 0.85f);
            foreach (var cell in drawCells) AddCellQuad(ghostLayer, cell.x, cell.y, dc);
            return;
        }

        if (HasHoverCell)
        {
            // Vivid when it fits, red when it doesn't, so validity is obvious before you click.
            Color c = hoverValid ? Vivid(info.color) : new Color(1f, 0.25f, 0.2f, 0.85f);

            // A DRAWN class has no authored footprint worth previewing — a farm's stored 2x3 is not what
            // you are about to build, it is only the fallback for saves older than drawing. Showing it
            // would promise a shape the drag does not produce, so the idle ghost is a single-cell brush:
            // "press here and drag". The drag itself takes over the instant the button goes down.
            if (IsDrawn(info)) AddCellQuad(ghostLayer, hoverCell.x, hoverCell.y, c);
            else
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

    // ============================================================================================
    // WHAT PLACEMENT MODE PUTS ON THE MAP
    //
    // Three things, and they answer three different questions the player is holding at once:
    //
    //   GUIDANCE GRIDS   "where may the next tile go?" — a translucent wash over every legal neighbour,
    //                    updated as the shape grows. Cells with a building already on them are simply
    //                    absent, which is how the map says "not there" without drawing a refusal.
    //   THE COUNTER      "am I big enough yet?" — n/min over the middle of the shape, red then green.
    //   THE REFUSAL      "why did nothing happen?" — at the tile the player just tried, fading out.
    //
    // The yield numbers used to be a fourth. They belong to the CURSOR rather than to this mode — they
    // are up on the Survey tab too now — so Update owns them; see RefreshYieldIcons.
    //
    // ALL THREE ARE REBUILT EVERY FRAME, which is affordable because all three are bounded by the drawn
    // shape rather than by the size of the world: the guidance is at most four cells per painted tile.
    // ============================================================================================
    void DrawPlacement()
    {
        ClearLayer(placementLayer);
        ClearLayer(placementHud);

        if (body?.surface == null) return;

        // Demolition draws on the same layer and is mutually exclusive with placement — you cannot be
        // putting something down and taking something up at the same time.
        if (BuildDemolition.IsFor(body)) { DrawDemolition(); return; }

        if (!BuildPlacement.IsFor(body)) return;

        DrawGuidanceGrids();
        DrawSizeCounter();
        DrawRefusal();

        // The yield numbers are NOT drawn from here any more. They belong to the cursor rather than to
        // Placement Mode — they are up on the Survey tab too — so Update owns them, and clearing them on
        // every frame this method took an early return would have reset their signature and rebuilt every
        // label every frame, which is the exact cost the signature exists to avoid.
    }

    void ClearLayer(RectTransform layer)
    {
        if (layer == null) return;
        for (int i = layer.childCount - 1; i >= 0; i--) Destroy(layer.GetChild(i).gameObject);
    }

    /// The translucent "you may build here" wash. Deliberately faint and white rather than the
    /// structure's own colour: it is not a preview of the building, it is a statement about the ground,
    /// and painting it in the building's hue made a half-drawn farm look twice the size it was.
    void DrawGuidanceGrids()
    {
        // ---- Before a tile is drawn: where you could EXTEND something you already have ----
        //
        // In the structure's own colour, not the neutral white the guidance uses, because it is a
        // statement about a specific existing building rather than about the ground: "that farm down
        // there could be bigger". The two must not read as the same highlight — see
        // BuildPlacement.ExpansionSites.
        if (BuildPlacement.Tiles == 0)
        {
            var sites = BuildPlacement.ExpansionSites();
            if (sites.Count > 0)
            {
                var info = BuildPlacement.Info;
                var ec = Vivid(info.color);
                ec.a = 0.30f;
                foreach (var cell in sites) AddCellQuad(placementLayer, cell.x, cell.y, ec);
            }
            return;
        }

        var guide = BuildPlacement.Guidance();
        if (guide.Count == 0) return;

        // Brighter when the shape is still short of its minimum, because that is when the player most
        // needs to be told where the next tile can go; once it is big enough the hint recedes and the
        // building itself is what the eye should be on.
        float a = BuildPlacement.MeetsMinimum ? 0.16f : 0.26f;
        var c = new Color(0.85f, 0.95f, 1.00f, a);

        foreach (var cell in guide) AddCellQuad(placementLayer, cell.x, cell.y, c);
    }

    /// The floating "3/4" over the middle of what is drawn.
    ///
    /// Red below the minimum and green at or above it, and the number it shows is CLAMPED at the minimum
    /// (BuildPlacement.CounterShown) so a nine-tile farm with a four-tile minimum reads 4/4 rather than
    /// 9/4. Past the minimum the fraction has stopped being a target, and a numerator that keeps
    /// climbing past its denominator reads as an error rather than as success.
    void DrawSizeCounter()
    {
        if (BuildPlacement.Tiles == 0) return;

        // Centre of the painted area, in cells. Follows the shape as it grows, which is what makes it
        // feel attached to the building rather than parked somewhere on the map.
        float sx = 0f, sy = 0f;
        foreach (var c in BuildPlacement.Cells) { sx += c.x + 0.5f; sy += c.y + 0.5f; }
        int n = BuildPlacement.Tiles;
        var centre = new Vector2(sx / n, sy / n);

        bool met = BuildPlacement.MeetsMinimum;
        var col = met ? new Color(0.35f, 1f, 0.45f) : new Color(1f, 0.36f, 0.30f);
        string text = $"{BuildPlacement.CounterShown}/{BuildPlacement.MinTiles}";

        var go = UIFactory.NewUI(placementHud, "SizeCounter");
        var label = UIFactory.Text(go.transform, $"<b>{text}</b>", 17, col, TextAlignmentOptions.Center);
        label.raycastTarget = false;

        // A hard shadow, because this sits over terrain that can be any colour from snow to lava and the
        // one thing it must always be is readable.
        var shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        var rt = go.GetComponent<RectTransform>();
        int w = body.surface.width, h = body.surface.height;
        rt.anchorMin = rt.anchorMax = new Vector2(centre.x / w, centre.y / h);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(64f, 24f);
        rt.anchoredPosition = Vector2.zero;
        UIFactory.Stretch(label.rectTransform);
    }

    /// "Need 34 metal!" in red at the tile the player just tried to paint, fading out.
    void DrawRefusal()
    {
        if (!BuildPlacement.RefusalShowing) return;

        var cell = BuildPlacement.RefusalCell;
        float fade = BuildPlacement.RefusalFade;

        var go = UIFactory.NewUI(placementHud, "Refusal");
        var label = UIFactory.Text(go.transform, $"<b>{BuildPlacement.RefusalText}</b>", 13,
            new Color(1f, 0.28f, 0.24f, fade), TextAlignmentOptions.Center);
        label.raycastTarget = false;

        var shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f * fade);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        int w = body.surface.width, h = body.surface.height;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2((cell.x + 0.5f) / w, (cell.y + 0.5f) / h);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(190f, 20f);
        // Drifts upward as it fades — the standard "this happened just now" motion, and it also lifts the
        // text off the tile so the ground underneath is visible again by the time it matters.
        rt.anchoredPosition = new Vector2(0f, 14f + (1f - fade) * 12f);
        UIFactory.Stretch(label.rectTransform);
    }

    // ============================================================================================
    // THE TILE YIELDS — a block around the cursor, not a wall over the map
    //
    // Every tile near the pointer shows what the index actually reads there. This is what turns the
    // overlay from a picture into a decision: the bands say "this patch is better than that one", the
    // numbers say by how much.
    //
    // IT USED TO COVER THE WHOLE VIEWPORT, and that was the wrong shape for the job in two ways. It cost
    // hundreds of TMP objects, which is a stall you can watch on a big world at high zoom — the reported
    // lag. And it was unreadable anyway: a screen of two-digit numbers is not a survey, it is noise, and
    // you cannot pick a site out of it. You read numbers about the tile you are pointing at and its
    // immediate neighbours, so that is what is drawn — a block four tiles out in every direction, which
    // is enough to cover any footprint in the game and its surroundings, and about eighty labels at the
    // very most instead of several hundred.
    //
    // THREE THINGS STILL GATE IT:
    //
    //   ONLY WHEN THE TILES ARE BIG ENOUGH. Below about 22 pixels a percentage does not fit in a cell,
    //   so it is simply not drawn — zoom in and the numbers appear.
    //   ONLY GROUND THE OVERLAY LIT. SurfaceIndex.Shown is the same test the index map uses, so a number
    //   appears exactly where there is colour under it and the two can never disagree.
    //   ONLY WHEN SOMETHING MOVED. Keyed on the index, the hovered cell and the zoom, and skipped on the
    //   frames where none of those changed — which, while the mouse is still, is all of them.
    //   SurfaceIndex.Get re-samples the terrain noise field per call, so the loop is not free either.
    //
    // ON SURVEY AS WELL AS BUILD. Surveying a world is the activity that is entirely about reading these
    // figures, and it was the one place they never appeared: you had to pick up a building you did not
    // want in order to read the ground under it.
    // ============================================================================================

    /// Below this many pixels per tile the yield numbers are suppressed — they would not fit.
    const float YieldIconMinTilePx = 22f;

    /// How far out from the hovered tile the numbers reach, in tiles. Four gives a 9x9 block: big enough
    /// to hold any footprint in the game with room around it, small enough to read at a glance.
    const int YieldIconRadius = 4;

    RectTransform yieldLayer;
    string yieldSig;

    void ClearYieldIcons()
    {
        if (yieldLayer != null) ClearLayer(yieldLayer);
        yieldSig = null;
    }

    /// Which index the numbers should be about: the held structure's on Build, the chosen overlay's on
    /// Survey. One place, so the numbers can never be about a different map than the colours under them.
    SurfaceIndexKind YieldIndex()
    {
        if (tab == Tab.Build)
        {
            var info = BuildPlacement.IsFor(body) ? BuildPlacement.Info
                     : selected.HasValue ? SurfaceBuildingDatabase.Get(selected.Value) : null;
            return info?.index ?? SurfaceIndexKind.None;
        }
        // The numbers under the cursor follow the LAST index switched on, on any tab. With several up
        // at once something has to be chosen, and the most recently added is the one the player was
        // most recently thinking about.
        scratchKinds.Clear();
        IndexToggles.Active(body, scratchKinds);
        return scratchKinds.Count > 0 ? scratchKinds[scratchKinds.Count - 1] : SurfaceIndexKind.None;
    }

    void RefreshYieldIcons()
    {
        var kind = YieldIndex();

        // The numbers wait for the index to be FINISHED, not merely started.
        //
        // A level-2 survey resolves an index in three passes, and until the last one lands the colours
        // on the map are deliberately coarse — the whole 70-and-up region painted as one band, then
        // splitting. An exact per-tile figure printed over that would be a precise answer sitting on
        // top of an approximate picture, and the player would site a building off a number the map
        // underneath does not yet support.
        if (body?.surface == null || kind == SurfaceIndexKind.None
            || !SurfaceIndex.Unlocked(body, kind)      // not surveyed: nothing honest to show
            || !Survey.RevealOf(body, kind).complete   // still being read: the map is coarse, so is the truth
            || !HasHoverCell)                          // nothing to centre on
        { ClearYieldIcons(); return; }

        int w = body.surface.width, h = body.surface.height;
        float tileW = mapRT.rect.width / w, tileH = mapRT.rect.height / h;
        if (Mathf.Min(tileW, tileH) < YieldIconMinTilePx) { ClearYieldIcons(); return; }

        // The block, clipped to the map. Latitude does not wrap, so the block simply runs out at the
        // poles rather than folding over — there is no tile on the other side of the top row.
        int x0 = hoverCell.x - YieldIconRadius, x1 = hoverCell.x + YieldIconRadius;
        int y0 = Mathf.Max(0, hoverCell.y - YieldIconRadius);
        int y1 = Mathf.Min(h - 1, hoverCell.y + YieldIconRadius);
        if (y1 < y0) { ClearYieldIcons(); return; }

        // Everything the drawn output depends on. The tile size is bucketed to whole pixels because it
        // moves continuously while zooming and the labels only need to be re-laid out when a cell
        // actually changes size on screen — anchored positions rescale themselves.
        string sig = $"{kind}|{hoverCell.x},{hoverCell.y}|{Mathf.RoundToInt(tileW)}";
        if (sig == yieldSig && yieldLayer != null) return;
        yieldSig = sig;

        if (yieldLayer == null)
        {
            yieldLayer = UIFactory.NewUI(mapRT, "YieldIcons").GetComponent<RectTransform>();
            UIFactory.Stretch(yieldLayer);
            var img = yieldLayer.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0); img.raycastTarget = false;
        }
        ClearLayer(yieldLayer);

        for (int y = y0; y <= y1; y++)
            for (int gx = x0; gx <= x1; gx++)
            {
                // LONGITUDE WRAPS, so a block straddling the date line carries on round rather than
                // stopping dead — the same rule the map itself draws by.
                int x = ((gx % w) + w) % w;

                float v = SurfaceIndex.Get(body, kind, x, y);
                if (!SurfaceIndex.ShownFor(body, kind, v, out float t)) continue;

                // Coloured by the tile's own band, in that band's outline colour — so a number is the
                // same brightness as the ring drawn around the ground it is standing on, and the two
                // readings reinforce each other instead of competing.
                var col = SurfaceIndex.Outline(kind, t);
                bool top = t >= 0.999f;

                var go = UIFactory.NewUI(yieldLayer, "Y");
                var label = UIFactory.Text(go.transform, top ? $"<b>{v * 100f:F0}</b>" : $"{v * 100f:F0}",
                    top ? 12 : 11, col, TextAlignmentOptions.Center);
                label.raycastTarget = false;

                var sh = label.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
                sh.effectDistance = new Vector2(1f, -1f);

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(x / (float)w, y / (float)h);
                rt.anchorMax = new Vector2((x + 1) / (float)w, (y + 1) / (float)h);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                UIFactory.Stretch(label.rectTransform);
            }
    }

    // ============================================================================================
    // THE CONFIRM PANEL
    //
    // Anchored under the BOTTOM of the drawn shape, so it belongs to the thing it is asking about. It
    // follows the footprint as the footprint grows, and through zoom and pan, because it is positioned
    // in the map's own normalised space rather than pinned to a screen point the map slides out from
    // under.
    //
    // Confirm goes live only when the shape is genuinely buildable; Cancel is always available, which is
    // the property that makes drawing safe to experiment with.
    // ============================================================================================
    RectTransform placePanel;
    TMP_Text placeText;
    // Only Confirm is held: it is the one whose interactability tracks the shape. Cancel is always
    // enabled, always says the same word, and never needs touching again after it is made.
    Button placeConfirmBtn;

    // ---- The validity check, throttled ----
    //
    // CanConfirm is not a cheap question: it walks the world's buildings, builds two hash sets and
    // re-runs the shape rules. Asking it every frame for a panel whose answer changes only when a tile
    // is painted or the economy ticks is pure waste — and unlike most waste in this file it ALLOCATES,
    // so it would be garbage every frame for as long as a shape sits on the map waiting to be confirmed.
    //
    // Re-asked immediately when the footprint changes (the tile count is the trigger) and otherwise a
    // few times a second, which is fast enough to catch the case that actually matters: another world
    // spending the metal out from under a shape the player is still looking at.
    bool placeOk;
    string placeWhy;
    int placeCheckedTiles = -1;
    float placeCheckIn;

    void RefreshPlacePanel()
    {
        bool want = BuildPlacement.IsFor(body) && BuildPlacement.Tiles > 0 && tab == Tab.Build;

        if (!want)
        {
            if (placePanel != null) placePanel.gameObject.SetActive(false);
            placeCheckedTiles = -1;
            return;
        }

        BuildPlacePanel();
        placePanel.gameObject.SetActive(true);

        var info = BuildPlacement.Info;
        BuildPlacement.Cost(out int m, out int e);
        int tiles = BuildPlacement.Tiles;

        placeCheckIn -= Time.unscaledDeltaTime;
        if (tiles != placeCheckedTiles || placeCheckIn <= 0f)
        {
            placeCheckedTiles = tiles;
            placeCheckIn = 0.25f;
            placeOk = BuildPlacement.CanConfirm(out placeWhy);
        }
        bool ok = placeOk;
        string why = placeWhy;

        string hex = ColorUtility.ToHtmlStringRGB(Vivid(info.color));
        float mult = BuildScaling.CostMultiplier(tiles);
        var sb = new System.Text.StringBuilder();

        // An extension names what it is joining and what the merged building will be, because the size
        // and cost above describe only the NEW tiles — the thing that will be standing afterwards is
        // bigger than the shape on screen, and confirming without knowing that is confirming blind.
        var join = BuildPlacement.Expanding;
        if (join != null)
            sb.Append($"<color=#{hex}>•</color> Extend <b>{info.name}</b> — " +
                      $"{join.TileCount} + {tiles} = <b>{join.TileCount + tiles}</b> tiles\n");
        else
            sb.Append($"<color=#{hex}>•</color> <b>{info.name}</b> — {tiles} tile{(tiles == 1 ? "" : "s")}\n");
        sb.Append($"<size=10><color=#9FB4C8>{m} metal · {e} energy · " +
                  $"{GameCalendar.Duration(info.buildTime * mult * TechEffects.BuildTimeMult)} · " +
                  $"x{BuildScaling.OutputMultiplier(tiles):0.0} output</color></size>");
        if (!ok) sb.Append($"\n<size=10><color=#FF6659>{why}</color></size>");
        placeText.text = sb.ToString();

        // Written directly rather than registered with `live`. This runs every frame while a shape is
        // drawn, and LiveSet entries are registered once and ticked — feeding it a new closure per frame
        // would grow the set without bound until the next panel rebuild cleared it.
        placeConfirmBtn.interactable = ok;

        PositionPlacePanel();
    }

    /// Under the bottom edge of the footprint's bounding box, clamped inside the viewport.
    void PositionPlacePanel()
    {
        if (placePanel == null || body?.surface == null || BuildPlacement.Tiles == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue;
        foreach (var c in BuildPlacement.Cells)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y;
        }

        int w = body.surface.width, h = body.surface.height;
        float u = (minX + maxX + 1) * 0.5f / w;      // horizontal centre of the shape
        float v = minY / (float)h;                    // its bottom edge

        Vector2 inMap = new Vector2((u - 0.5f) * mapRT.rect.width, (v - 0.5f) * mapRT.rect.height);
        float halfH = placePanel.sizeDelta.y * 0.5f;
        Vector2 pos = inMap + mapPan + new Vector2(0f, -halfH - 10f);   // just below the shape

        var vp = hostViewport.rect;
        float hw = placePanel.sizeDelta.x * 0.5f;
        pos.x = Mathf.Clamp(pos.x, vp.xMin + hw, vp.xMax - hw);
        pos.y = Mathf.Clamp(pos.y, vp.yMin + halfH, vp.yMax - halfH);

        placePanel.anchorMin = placePanel.anchorMax = new Vector2(0.5f, 0.5f);
        placePanel.pivot = new Vector2(0.5f, 0.5f);
        placePanel.anchoredPosition = pos;
    }

    void BuildPlacePanel()
    {
        if (placePanel != null) { placePanel.SetAsLastSibling(); return; }

        placePanel = UIFactory.NewUI(hostViewport, "ConfirmPlacement").GetComponent<RectTransform>();
        placePanel.sizeDelta = new Vector2(228, 78);
        var bg = placePanel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.09f, 0.14f, 0.97f);
        var outline = placePanel.gameObject.AddComponent<Outline>();
        outline.effectColor = UITheme.Accent;
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        var v = placePanel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(6, 6, 5, 5); v.spacing = 4;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        placeText = UIFactory.Text(placePanel, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        var tle = placeText.gameObject.AddComponent<LayoutElement>();
        tle.preferredHeight = 44f;

        var row = UIFactory.NewUI(placePanel, "Row");
        UIFactory.AddLayout(row, 22);
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 4;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;

        placeConfirmBtn = UIFactory.Button(row.transform, "Confirm", DoConfirmPlacement, 20);
        UIFactory.Button(row.transform, "Cancel", DoCancelPlacement, 20);

        placePanel.SetAsLastSibling();   // above the zoom bar, which is also a child of the viewport
    }

    // ============================================================================================
    // DEMOLITION MODE
    //
    // The same interaction as Placement Mode, run backwards: enter the mode, paint tiles, a panel
    // appears under what you painted, nothing happens until you confirm. Everything below is the drawing
    // and the panel; every rule lives in BuildDemolition and SurfaceBuildManager.DemolishCells.
    //
    // The one thing this adds that placement does not have is the SECOND question, asked when the
    // removal would split a building into pieces that no longer touch. That is the only outcome the
    // player cannot read off the tiles they clicked, so it is the only one worth interrupting for.
    // ============================================================================================

    void EnterDemolition()
    {
        tab = Tab.Build;
        selected = null;              // demolishing and placing are different modes; you cannot be in both
        CancelPlace();
        BuildPlacement.Cancel();
        BuildDemolition.Begin(body);
        lastSig = null;
    }

    void ExitDemolition()
    {
        BuildDemolition.Cancel();
        lastSig = null;
    }

    /// The drag, mirroring PollBuildDraw. Left paints, right erases.
    void PollDemolish()
    {
        if (!BuildDemolition.IsFor(body) || tab != Tab.Build) { demolishing = false; return; }

        // The split question is a modal moment: the panel is asking about exactly the tiles that are
        // selected, and letting the brush keep running underneath it would change the question while it
        // was on screen.
        if (BuildDemolition.AwaitingSplitConfirm) { demolishing = false; return; }

        bool paint = Input.GetMouseButton(0);
        bool erase = Input.GetMouseButton(1);
        if (!paint && !erase) { demolishing = false; return; }

        if (!demolishing)
        {
            if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;
            if (!HasHoverCell) return;
            if (OverFloatingMapControl()) return;
            demolishing = true;
        }

        if (!HasHoverCell) return;
        if (erase) BuildDemolition.Unpaint(hoverCell);
        else BuildDemolition.Paint(hoverCell);
    }

    bool demolishing;

    /// The selection, in demolition red, plus a wash over every OTHER cell of a building it touches — so
    /// "these four tiles" and "the farm those four tiles are part of" are both visible at once. Without
    /// the wash, the extent of what you are cutting into is invisible and the split warning arrives from
    /// nowhere.
    void DrawDemolition()
    {
        if (!BuildDemolition.IsFor(body) || body?.surface == null) return;

        var red = new Color(1.00f, 0.24f, 0.20f, 0.85f);
        var context = new Color(1.00f, 0.55f, 0.35f, 0.20f);

        foreach (var p in BuildDemolition.Affected())
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
                if (!BuildDemolition.HasCell(c)) AddCellQuad(placementLayer, c.x, c.y, context);

        foreach (var c in BuildDemolition.Cells) AddCellQuad(placementLayer, c.x, c.y, red);

        // The brush, so it is clear the mode is live even over ground with nothing on it.
        if (HasHoverCell && !BuildDemolition.HasCell(hoverCell)
            && SurfaceBuildManager.At(body, hoverCell.x, hoverCell.y) != null)
            AddCellQuad(placementLayer, hoverCell.x, hoverCell.y, new Color(1f, 0.4f, 0.3f, 0.45f));
    }

    RectTransform demolishPanel;
    TMP_Text demolishText;
    Button demolishConfirmBtn;

    void RefreshDemolishPanel()
    {
        bool want = BuildDemolition.IsFor(body) && BuildDemolition.Tiles > 0 && tab == Tab.Build;
        if (!want)
        {
            if (demolishPanel != null) demolishPanel.gameObject.SetActive(false);
            return;
        }

        BuildDemolishPanel();
        demolishPanel.gameObject.SetActive(true);

        BuildDemolition.Refund(out int m, out int e);
        BuildDemolition.SplitSummary(out int split, out int extra);
        int destroyed = BuildDemolition.WouldDestroy();
        int tiles = BuildDemolition.Tiles;
        bool asking = BuildDemolition.AwaitingSplitConfirm;

        var sb = new System.Text.StringBuilder();

        if (asking)
        {
            // THE SECOND QUESTION. Phrased as the consequence rather than as a warning label: the player
            // does not need to be told this is dangerous, they need to be told what they will have
            // afterwards, because "you will have 3 farms instead of 1" is a thing they can decide about.
            sb.Append("<color=#FFBF4D><b>This will split a building.</b></color>\n");
            sb.Append($"<size=10><color=#9FB4C8>{split} structure{(split == 1 ? "" : "s")} will come apart into " +
                      $"{split + extra} separate one{(split + extra == 1 ? "" : "s")}, each with its own " +
                      $"efficiency and its own entry in the list. They will not re-join — buildings only " +
                      $"merge when they are built touching.</color></size>");
        }
        else
        {
            sb.Append($"<color=#FF6659>•</color> Demolish <b>{tiles} tile{(tiles == 1 ? "" : "s")}</b>\n");
            sb.Append($"<size=10><color=#9FB4C8>{m} metal · {e} energy back</color></size>");
            if (destroyed > 0)
                sb.Append($"\n<size=10><color=#FFBF4D>{destroyed} structure{(destroyed == 1 ? "" : "s")} " +
                          $"removed completely</color></size>");
            if (split > 0)
                sb.Append($"\n<size=10><color=#FFBF4D>{split} will be split apart — you'll be asked again" +
                          $"</color></size>");
        }

        demolishText.text = sb.ToString();

        var lbl = demolishConfirmBtn.GetComponentInChildren<TMP_Text>();
        if (lbl != null) lbl.text = asking ? "Split it" : "Confirm";

        PositionDemolishPanel();
    }

    void PositionDemolishPanel()
    {
        if (demolishPanel == null || body?.surface == null || BuildDemolition.Tiles == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue;
        foreach (var c in BuildDemolition.Cells)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y;
        }

        int w = body.surface.width, h = body.surface.height;
        float u = (minX + maxX + 1) * 0.5f / w;
        float v = minY / (float)h;

        Vector2 inMap = new Vector2((u - 0.5f) * mapRT.rect.width, (v - 0.5f) * mapRT.rect.height);
        float halfH = demolishPanel.sizeDelta.y * 0.5f;
        Vector2 pos = inMap + mapPan + new Vector2(0f, -halfH - 10f);

        var vp = hostViewport.rect;
        float hw = demolishPanel.sizeDelta.x * 0.5f;
        pos.x = Mathf.Clamp(pos.x, vp.xMin + hw, vp.xMax - hw);
        pos.y = Mathf.Clamp(pos.y, vp.yMin + halfH, vp.yMax - halfH);

        demolishPanel.anchorMin = demolishPanel.anchorMax = new Vector2(0.5f, 0.5f);
        demolishPanel.pivot = new Vector2(0.5f, 0.5f);
        demolishPanel.anchoredPosition = pos;
    }

    void BuildDemolishPanel()
    {
        if (demolishPanel != null) { demolishPanel.SetAsLastSibling(); return; }

        demolishPanel = UIFactory.NewUI(hostViewport, "ConfirmDemolish").GetComponent<RectTransform>();
        demolishPanel.sizeDelta = new Vector2(240, 92);
        var bg = demolishPanel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.05f, 0.05f, 0.97f);
        var outline = demolishPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = UITheme.Bad;
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        var v = demolishPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(6, 6, 5, 5); v.spacing = 4;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        demolishText = UIFactory.Text(demolishPanel, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        var tle = demolishText.gameObject.AddComponent<LayoutElement>();
        tle.preferredHeight = 58f;

        var row = UIFactory.NewUI(demolishPanel, "Row");
        UIFactory.AddLayout(row, 22);
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 4;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;

        demolishConfirmBtn = UIFactory.Button(row.transform, "Confirm", DoConfirmDemolition, 20);
        UIFactory.Button(row.transform, "Cancel", DoCancelDemolition, 20);

        demolishPanel.SetAsLastSibling();
    }

    void DoConfirmDemolition()
    {
        // Confirm returns false BOTH when it merely raised the split question and when it genuinely
        // failed, which is why the state is asked rather than the return value: the two need different
        // sounds and only one of them is a refusal.
        bool asking = BuildDemolition.AwaitingSplitConfirm;
        bool done = BuildDemolition.Confirm(out string why);

        if (done)
        {
            lastSig = null;
            SimpleAudio.Instance?.PlayComplete();
            return;
        }

        if (BuildDemolition.AwaitingSplitConfirm && !asking)
        {
            SimpleAudio.Instance?.PlayTick();   // the question, not a refusal
            return;
        }

        SimpleAudio.Instance?.PlayTick();
        if (!string.IsNullOrEmpty(why))
            NotificationManager.Instance?.Push("Can't demolish that", why, null, NotifKind.Danger);
    }

    /// Cancel backs out ONE step: out of the split question first, and only then out of the mode.
    ///
    /// The same one-step-at-a-time rule Escape follows during placement, and for the same reason —
    /// answering "no, not like that" to the split question should not also throw away the selection the
    /// player spent a drag making.
    void DoCancelDemolition()
    {
        if (BuildDemolition.AwaitingSplitConfirm)
        {
            BuildDemolition.CancelSplitConfirm();
            SimpleAudio.Instance?.PlayTick();
            return;
        }
        ExitDemolition();
        SimpleAudio.Instance?.PlayTick();
    }

    void DoConfirmPlacement()
    {
        if (BuildPlacement.Confirm(out string why))
        {
            lastSig = null;                 // the queue gained a row and the map gained a ghost
            SimpleAudio.Instance?.PlayComplete();
            // The structure stays held, so laying down a row of habitats is one pick and several draws
            // rather than a trip back to the tray between each. Esc or Cancel puts it down.
            if (selected.HasValue) BuildPlacement.Begin(body, selected.Value);
        }
        else
        {
            SimpleAudio.Instance?.PlayTick();
            if (!string.IsNullOrEmpty(why))
                NotificationManager.Instance?.Push("Can't build that yet", why, null, NotifKind.Danger);
        }
    }

    /// Cancel clears the drawing and leaves Placement Mode entirely — the structure is put down too.
    ///
    /// Clearing the shape but staying armed would leave the player in a mode with no visible state and
    /// no obvious way out, which is the thing a Cancel button is supposed to prevent.
    void DoCancelPlacement()
    {
        BuildPlacement.Cancel();
        selected = null;
        lastSig = null;
        ClearGhost();
        SimpleAudio.Instance?.PlayTick();
    }

    void ClearGhost()
    {
        for (int i = ghostLayer.childCount - 1; i >= 0; i--) Destroy(ghostLayer.GetChild(i).gameObject);
    }

    // Grid cell -> a quad anchored in the map's normalized space, so it scales with the window.
    /// Returns the quad it made, so a caller that wants to keep animating it (the construction ghosts,
    /// which breathe) can hold onto it instead of hunting it back out of the layer's children.
    Image AddCellQuad(RectTransform layer, int x, int y, Color c)
    {
        if (body?.surface == null) return null;
        int w = body.surface.width, h = body.surface.height;
        var q = UIFactory.Panel(layer, "c", c);
        q.raycastTarget = false;
        var rt = q.rectTransform;
        rt.anchorMin = new Vector2(x / (float)w, y / (float)h);
        rt.anchorMax = new Vector2((x + 1) / (float)w, (y + 1) / (float)h);
        rt.offsetMin = new Vector2(0.5f, 0.5f);
        rt.offsetMax = new Vector2(-0.5f, -0.5f);
        return q;
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
        //
        // ...and off in Demolition Mode for exactly the same reason. That mode holds no piece, so the
        // `selected` test alone let left-drag pan the map instead of selecting tiles: the one gesture
        // the mode is built around would have scrolled the world instead. Middle-drag still pans in
        // both modes, which is how you reposition without leaving what you are doing.
        bool leftPans = !selected.HasValue && !BuildDemolition.IsFor(body);

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

    // ============================================================================================
    // DRAWING A BUILDING
    //
    // Most structures are no longer a fixed tetromino you stamp down — you press on the grid and DRAW the
    // footprint, and everything about the building scales with how much you drew (BuildScaling). What a
    // drag means depends on the class, and BuildShapeRules owns all four answers:
    //
    //   Free        paint. Every cell the cursor crosses joins the footprint. This is the farm you extend
    //               by four tiles because the colony is hungry.
    //   Square      the anchor is one corner and the square grows toward the cursor: 2x2, 3x3, 4x4.
    //   Rectangle   same, but any filled box at least 2 wide in both directions.
    //   NodeChain   lay a RUN of power pylons toward the cursor, auto-spaced to stay on one grid.
    //   Fixed       no drag at all. Falls through to the old click-and-confirm path below.
    //
    // WHY A DRAG AND NOT A SEQUENCE OF CLICKS. The whole appeal of the mechanic is gestural — you sweep
    // out a farm the size you want and let go. Clicking tile by tile would make a ten-tile building ten
    // decisions instead of one, and would make the power-pole chain (the control this is modelled on)
    // pointless, since its entire purpose is covering distance in a single motion.
    //
    // Left-drag is free to mean this because PollMapPan turns left-panning OFF while a piece is held
    // (`leftPans`), so there is no gesture conflict to resolve.
    // ============================================================================================

    bool drawing;
    Vector2Int drawAnchor = new Vector2Int(-1, -1);

    /// The footprint currently proposed by the drag. Ordered, because for a Free paint the FIRST cell is
    /// the origin the building will remember (PlacedBuilding.SetDrawnShape).
    readonly List<Vector2Int> drawCells = new List<Vector2Int>();

    /// Set membership for the Free paint, so re-crossing a tile doesn't add it twice.
    readonly HashSet<Vector2Int> drawSet = new HashSet<Vector2Int>();

    /// Why the current drag would be refused, or null. Shown live so the player knows before releasing.
    string drawWhy;

    /// Does this class use the drag at all?
    static bool IsDrawn(SurfaceBuildingInfo info)
        => info != null && info.drawMode != BuildDrawMode.Fixed;

    /// Does this class go through Placement Mode (BuildPlacement)?
    ///
    /// Everything drawn except the node chain. A chain lays a RUN of independent one-tile pylons rather
    /// than one footprint, so a session that tracks a single connected shape has nothing to hold for it;
    /// it keeps the immediate drag below. Fixed classes have no shape to draw at all.
    static bool UsesPlacementSession(SurfaceBuildingInfo info)
        => info != null && info.drawMode != BuildDrawMode.Fixed && info.drawMode != BuildDrawMode.NodeChain;

    /// The shortest side a square building may have, from its tile minimum.
    static int MinSideFor(SurfaceBuildingInfo info) => BuildPlacement.MinSide(info);

    void PollBuildDraw()
    {
        // A Fixed class's confirm dialog is up, or nothing is held: no drag. Any in-flight one is
        // abandoned rather than left latched, or the next press would resume a gesture the player has
        // forgotten about.
        if (tab != Tab.Build || !selected.HasValue || pendingType.HasValue) { EndDraw(); return; }

        var info = SurfaceBuildingDatabase.Get(selected.Value);
        if (!IsDrawn(info)) { EndDraw(); return; }

        if (info.drawMode == BuildDrawMode.NodeChain) { PollNodeChainDrag(info); return; }

        // The session should already be open — picking the card opens it — but a tab change or a world
        // change can leave the two out of step, and a brush with no session behind it would paint into
        // nothing.
        if (!BuildPlacement.IsFor(body)) BuildPlacement.Begin(body, selected.Value);

        bool box = info.drawMode == BuildDrawMode.Square || info.drawMode == BuildDrawMode.Rectangle;

        // ---- Begin ----
        if (!drawing)
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (!HasHoverCell) return;                   // not over the host map
            if (OverFloatingMapControl()) return;        // the zoom bar / Confirm panel float over the map

            drawing = true;
            drawAnchor = hoverCell;
            Brush(info, box);
            return;
        }

        // ---- Continue ----
        if (Input.GetMouseButton(0)) { Brush(info, box); return; }

        // ---- Release ----
        //
        // WHICH NO LONGER BUILDS ANYTHING. This used to be the commit: letting go of the button queued
        // the job. Now the footprint simply stays on the map and the Confirm panel appears under it, so
        // the player can lift the button, look at what they have drawn, keep adding to it, and decide.
        // That is the whole of the "bring back placement confirmation" change, and it is one line.
        drawing = false;
    }

    /// One brush stroke, for whichever kind of brush this class uses.
    ///
    /// A BOX REPLACES and a PAINT ADDS, which is the difference that makes both feel right: dragging a
    /// reactor's corner should resize the one square, while painting a farm should let you release the
    /// button, move, and carry on adding tiles to the same farm.
    void Brush(SurfaceBuildingInfo info, bool box)
    {
        if (!HasHoverCell) return;
        if (box) BuildPlacement.SetBox(drawAnchor, hoverCell, info.drawMode == BuildDrawMode.Square);
        else BuildPlacement.Paint(hoverCell);
    }

    // The node chain keeps its old immediate behaviour: press, drag out a run of pylons, release and
    // they go up. Left alone here deliberately — the power rules that decide where a node may be placed
    // at all are a separate change, and rebuilding this control before those land would mean building
    // it twice.
    void PollNodeChainDrag(SurfaceBuildingInfo info)
    {
        if (!drawing)
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (!HasHoverCell) return;
            if (OverFloatingMapControl()) return;

            drawing = true;
            drawAnchor = hoverCell;
            drawSet.Clear(); drawCells.Clear();
            RecomputeDraw(info);
            return;
        }

        if (Input.GetMouseButton(0)) { RecomputeDraw(info); return; }
        CommitDraw(info);
    }

    // ---- The node chain's own drag ----
    //
    // Everything that used to live here for the OTHER four draw modes has moved into BuildPlacement,
    // which is the session behind Placement Mode. What is left is the chain, which is not a footprint at
    // all: it is a run of independent one-tile pylons, so it has no shape to validate, no minimum, and
    // nothing for a Confirm panel to be anchored to.

    /// Rebuild the proposed pylon run from the anchor and wherever the cursor is now.
    void RecomputeDraw(SurfaceBuildingInfo info)
    {
        var cursor = HasHoverCell ? hoverCell : drawAnchor;

        drawCells.Clear(); drawSet.Clear();
        foreach (var c in BuildShapeRules.NodeChain(drawAnchor, cursor, info.powerRange))
            if (drawSet.Add(c)) drawCells.Add(c);

        // THE CHAIN IS VALID IF ANY PYLON CAN GO UP, not if all of them can. CommitDraw deliberately
        // skips the ones on bad ground and raises the rest — you dragged across a lake and expect poles
        // on both shores — so reddening the whole run because one pylon landed in water would contradict
        // what releasing actually does.
        drawWhy = null;
        string last = null;
        foreach (var c in drawCells)
            if (SurfaceBuildManager.CanPlace(body, selected.Value, c.x, c.y, 0, out last)) return;
        drawWhy = last ?? "nowhere along that line will take a pylon";
    }

    void CommitDraw(SurfaceBuildingInfo info)
    {
        var cells = new List<Vector2Int>(drawCells);
        var type = selected.Value;
        EndDraw();

        if (cells.Count == 0) return;

        // A CHAIN IS N BUILDINGS, NOT ONE. Each pylon is its own relay that can be destroyed on its own
        // and break the chain in half, so they are queued one at a time, and one that lands on bad
        // ground is skipped rather than failing the whole run.
        //
        // QUEUED, NOT PLACED. These used to go up the instant the button was released — the only
        // structure in the game that appeared out of nothing. A pylon is cheap, not free of effort, and
        // an instant one made the relay the answer to every power problem because it was the only
        // building with no delay attached. Now it takes its eight seconds like everything else, which
        // also means a long chain across a continent is a real commitment of time and Labor.
        //
        // ---- THE CHAIN VALIDATES AGAINST ITSELF ----
        //
        // A pylon may only be planted where there is already power. Applied naively to a queued chain
        // that rule refuses everything past the first one, because the pylons ahead do not exist yet and
        // the grid they will make does not either — so a drag across a continent would lay exactly one
        // mast and the control would be pointless.
        //
        // So the run is walked IN ORDER and each pylon is accepted if the grid reaches it OR a pylon
        // already accepted in this same run does. That is the same rule, applied to the chain as the
        // player is committing to it: the first mast has to start from real power, and every one after
        // it hangs off the one before. A drag that begins in empty desert still lays nothing.
        //
        // The reach test uses the pylon's own powerRange at tier 1, which is what a newly built one will
        // have. Being conservative here is correct — quoting an upgraded reach for a mast that has not
        // been upgraded would let a chain be committed that then fails to connect.
        int queued = 0;
        string firstWhy = null;
        var one = new List<Vector2Int>(1) { Vector2Int.zero };
        var accepted = new List<Vector2Int>();
        float reach = info.powerRange;
        float reach2 = reach * reach;

        foreach (var c in cells)
        {
            // The ordinary ground checks — in bounds, dry, clear, affordable, and the node rule.
            bool ok = SurfaceBuildManager.CanPlace(body, type, c.x, c.y, 0, out string why);

            // ...but a refusal that is ONLY about power is forgiven when the pylon behind it will
            // supply it. Anything else (water, occupied ground, no metal) still refuses.
            if (!ok && WithinReach(accepted, c, reach2)
                && SurfaceBuildManager.CanPlace(body, type, c.x, c.y, 0, out _, ignoreNodePower: true))
                ok = true;

            if (!ok) { firstWhy = firstWhy ?? why; continue; }

            one[0] = c;
            if (SurfaceBuildQueue.Enqueue(body, type, one, out why) != null) { queued++; accepted.Add(c); }
            else firstWhy = firstWhy ?? why;
        }

        if (queued > 0) { lastSig = null; SimpleAudio.Instance?.PlayComplete(); }
        else
        {
            SimpleAudio.Instance?.PlayTick();
            if (!string.IsNullOrEmpty(firstWhy))
                NotificationManager.Instance?.Push("Can't run pylons there", firstWhy, null, NotifKind.Danger);
        }
    }

    /// Is `c` inside the relay reach of any pylon already accepted in this run?
    static bool WithinReach(List<Vector2Int> accepted, Vector2Int c, float reach2)
    {
        foreach (var a in accepted)
        {
            float dx = a.x - c.x, dy = a.y - c.y;
            if (dx * dx + dy * dy <= reach2) return true;
        }
        return false;
    }

    void EndDraw()
    {
        drawing = false;
        drawAnchor = new Vector2Int(-1, -1);
        drawCells.Clear(); drawSet.Clear();
        drawWhy = null;
    }

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
    // ============================================================================================
    // THE MAP ENDS WHERE THE VIEWPORT ENDS
    //
    // `mapRT` is far bigger than the window shows. Zoomed in it runs several screens wide, and the wrap
    // mirrors extend it a whole world further in each direction; the RectMask2D on the viewport clips
    // what is DRAWN, and nothing else. So the map is still lying there, full size, underneath the side
    // panel, the tab strip and the status line.
    //
    // That was invisible for as long as everything went through uGUI, whose raycast the mask does filter.
    // The build DRAG does not: PollBuildDraw reads Input.GetMouseButtonDown directly and starts from
    // whatever cell PollHover last resolved — and PollHover resolved it against mapRT's own bounds. So
    // clicking a card in the side panel with a structure in hand pressed and released over a cell of
    // hidden map, and quietly built there. Hovering the panel showed that hidden tile's readout too.
    //
    // Hence one gate, asked before any cell is resolved: is the cursor inside the pane that is actually
    // showing this world?
    bool PointerOverHostMap()
    {
        if (!HostOpen || hostViewport == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(hostViewport, Input.mousePosition, null);
    }

    /// Forget the host map's hover. Called from every path that is NOT over the host map, so a cell can
    /// never survive the cursor leaving the map and become the anchor of a drag somewhere else.
    void ClearHostHover()
    {
        if (hoverCell.x < 0 && !hoverValid) return;
        hoverCell = new Vector2Int(-1, -1);
        hoverValid = false;
    }

    void PollHover()
    {
        // The moon-tab strip owns MapHoverPanel while the cursor is over IT — MoonTabHover shows its own
        // content on OnPointerEnter/Exit, event-driven rather than polled. If this method touched the panel
        // at all over that rect (even just to Hide() it), it would stomp MoonTabHover's own state every
        // single frame, since this runs unconditionally and that only runs once on the enter/exit edge.
        if (moonTabStrip != null && RectTransformUtility.RectangleContainsScreenPoint(moonTabStrip, Input.mousePosition, null))
        {
            // The tab strip floats OVER the host map, so the cell underneath it is still resolvable — and
            // pressing a moon tab while holding a structure would otherwise start a drag on the map behind
            // the button. Dropping the hover is the one thing this branch must still do.
            ClearHostHover();
            return;
        }

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
            ClearHostHover();   // over a MOON's pane: the host map is not under this cursor
            if (ScreenToCellIn(img.rectTransform, m, Input.mousePosition, null, out int mx, out int my))
                MapHoverPanel.Instance.ShowAtCursor(
                    $"<size=11><color=#8FD0FF>{m.name}</color></size>\n" + TileHoverText(m, mx, my));
            else
                MapHoverPanel.Instance.Hide();
            return;
        }

        // Otherwise the host planet's map — but only where it is actually VISIBLE. PointerOverHostMap is
        // the mask the RectMask2D only applies to drawing; without it the map answers for the whole
        // screen, including everything the side panel is sitting on top of.
        if (PointerOverHostMap() && ScreenToCell(Input.mousePosition, null, out int x, out int y))
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
            //
            // WHILE PLACING, this window carries the siting decision rather than the plain tile readout —
            // see PlacementHoverText. That is the whole of "put the information under the mouse instead
            // of at the bottom of the window": one panel, already anchored to the cursor, told to say
            // something more useful while a structure is in hand.
            if (OverFloatingMapControl()) MapHoverPanel.Instance.Hide();
            else if (tab == Tab.Build && BuildDemolition.IsFor(body))
                MapHoverPanel.Instance.ShowAtCursor(DemolitionHoverText(x, y));
            else if (tab == Tab.Build && selected.HasValue
                     && UsesPlacementSession(SurfaceBuildingDatabase.Get(selected.Value)))
                MapHoverPanel.Instance.ShowAtCursor(PlacementHoverText(x, y));
            else MapHoverPanel.Instance.ShowAtCursor(TileHoverText(x, y));
        }
        else
        {
            ClearHostHover();
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
        // Placement Mode's Confirm/Cancel panel sits ON the map, directly under the shape it is asking
        // about — so without this, pressing Confirm would also paint a tile on the cell behind the
        // button, and the building would grow by one on the way to being confirmed.
        if (placePanel != null && placePanel.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(placePanel, p, null)) return true;
        if (demolishPanel != null && demolishPanel.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(demolishPanel, p, null)) return true;
        return false;
    }

    // Tile type (coloured like the tile), its ore if one has been discovered here, and this spot's
    // temperature (coloured on PlanetTemperature's global gradient) — every field fetched from data the
    // game already tracks, nothing new stored per tile.
    string TileHoverText(int x, int y) => TileHoverText(body, x, y);

    // Tile readout for ANY body's surface (the main planet or an open moon), so a moon map's tiles show
    // the same biome / ore / temperature info the main map does.
    /// `except` drops one index from the readout — the one the caller is about to report in more detail
    /// itself. While placing, the held structure's index gets its own line with an efficiency label and a
    /// percentile on it, and printing the plain figure directly above that is the same fact twice.
    string TileHoverText(CelestialBody b, int x, int y, SurfaceIndexKind except = SurfaceIndexKind.None)
    {
        var tile = b.surface.tiles[x, y];
        var sb = new System.Text.StringBuilder();

        // A CELL STILL UNDER THE BLACKOUT SAYS NOTHING, and that has to be enforced here as well as in
        // the drawing. The mask hides the colour; the readout would otherwise hand over the biome, the
        // temperature and the ore of a cell the player is looking at a black square of — which is the
        // whole survey given away by hovering.
        if (!GameMode.DevMode && !b.Surveyed && !Survey.ReachedGround(b, x, y))
        {
            sb.Append("<b><color=#7E8B9C>Unknown</color></b>");
            sb.Append("\n<size=10><color=#7E8B9C>Not yet surveyed</color></size>");
            return sb.ToString();
        }

        string typeHex = ColorUtility.ToHtmlStringRGB(TerrainColorMap.Get(tile.type));
        sb.Append($"<b><color=#{typeHex}>{tile.type}</color></b>");

        // WHAT IS UNDERNEATH, when this tile is water, sea ice or snow. Those are covers, not biomes —
        // "if I wanted to remove the water, it will still be there" — and since terraforming cannot move
        // a tile's elevation, draining or thawing this one really would uncover exactly this. Saying so
        // here is what lets a player judge a terraforming project before paying for it, instead of
        // finding out what was under the ice by melting it.
        if (tile.HasCover)
        {
            string underHex = ColorUtility.ToHtmlStringRGB(TerrainColorMap.Get(tile.ground));
            sb.Append($" <size=10><color=#9FB4C8>over</color> <color=#{underHex}>{tile.ground}</color></size>");
        }

        if (tile.HasOre && ResearchManager.IsDiscovered(tile.ore))
            sb.Append($"\n<color=#8FD0FF>{OreDatabase.Get(tile.ore).displayName}</color> ({tile.oreRichness * 100f:F0}% rich)");

        // ---- ELEVATION, as a band and as a number ----
        //
        // "You could see information such as 'Mountain, [elevation], [Temperature]' inside the mouse
        // window." The band is the word the terrain used to be NAMED after (Highlands, Hills) before
        // those stopped being biomes and became what they always were: a statement about height. The
        // metres are measured against this world's own waterline, so the number means what a player
        // expects it to mean whether the world is drowned or bone dry.
        float metres = PlanetTerrainGenerator.ElevationMetres(b, tile.elevation);
        string band = PlanetTerrainGenerator.ElevationBand(b, tile.elevation);
        sb.Append($"\n<color=#B8C6D4>{band}</color> <size=10><color=#9FB4C8>{metres:N0} m</color></size>");

        // Elevation included — a peak and the plain beside it are not the same temperature, and the map
        // plainly shows they are not.
        float celsius = PlanetTemperature.CelsiusAt(b, x, y);
        string tempHex = ColorUtility.ToHtmlStringRGB(PlanetTemperature.GradientColor(celsius));
        sb.Append($"\n<color=#{tempHex}>{PlanetTemperature.Label(celsius)}</color>");

        AppendIndexReadout(sb, b, x, y, except);

        // A construction site names itself. The ghost on the map says a structure is coming and roughly
        // how far along it is; this is where you find out WHICH structure without going to the panel and
        // matching colours by eye.
        var job = SurfaceBuildQueue.JobAt(b, x, y);
        if (job != null)
        {
            var jinfo = SurfaceBuildingDatabase.Get(job.type);
            string jhex = ColorUtility.ToHtmlStringRGB(Vivid(jinfo.color));
            sb.Append(job.paused
                ? $"\n<color=#{jhex}>{jinfo.name}</color> <color=#FFBF4D>— held at {job.Progress * 100f:F0}%</color>"
                : $"\n<color=#{jhex}>{jinfo.name}</color> — under construction, {job.Progress * 100f:F0}%");
        }

        return sb.ToString();
    }

    // ============================================================================================
    // THE INDEX READOUT, UNDER THE TILE TITLE
    //
    // This used to live at the bottom of the Survey side panel, under a heading called UNDER THE CURSOR,
    // and it was in the wrong place for the one thing it is for. Reading it meant looking away from the
    // tile you were pointing at, across to a panel on the other side of the window, and then back — and
    // in that round trip the number you had just read stopped being about anywhere in particular. A
    // readout about the cursor belongs AT the cursor, under the tile's name and its temperature, where
    // the eye already is.
    //
    // It follows the same rule as everything else: a tile that no index reaches says so in one line,
    // rather than listing six single-digit percentages and inviting the player to weigh a 9% against an
    // 11% as though either were a reason to build somewhere.
    //
    // Locked indexes are still named. That is a fact about your RESEARCH rather than about this tile,
    // and silence would read as "nothing here" when it means "you cannot see this yet".
    // ============================================================================================
    void AppendIndexReadout(System.Text.StringBuilder sb, CelestialBody b, int x, int y, SurfaceIndexKind except)
    {
        if (b?.surface == null) return;

        int listed = 0, locked = 0;
        foreach (var k in SurfaceIndex.All)
        {
            if (k == except) continue;
            // An index this world has no use for is not "locked" — there is nothing behind it. Skipping
            // it silently keeps it out of the cursor readout's "N more locked" tally, which would
            // otherwise promise readings that will never exist.
            if (!SurfaceIndex.Present(b, k)) continue;
            if (!SurfaceIndex.Unlocked(b, k)) { locked++; continue; }

            float v = SurfaceIndex.Get(b, k, x, y);
            if (!SurfaceIndex.ShownFor(b, k, v, out float t)) continue;

            listed++;
            string hex = ColorUtility.ToHtmlStringRGB(SurfaceIndex.Outline(k, t));
            sb.Append($"\n<size=11><color=#{hex}>{SurfaceIndex.ShortName(k)} <b>{v * 100f:F0}%</b></color></size>");
        }

        if (listed == 0)
            sb.Append("\n<size=10><color=#5A6A7A><i>no resource here</i></color></size>");

        if (locked > 0)
            sb.Append($"\n<size=10><color=#5A6A7A>{locked} more index{(locked == 1 ? "" : "es")} not yet surveyed</color></size>");
    }

    void RecomputeHoverValidity()
    {
        hoverValid = selected.HasValue && HasHoverCell &&
                     SurfaceBuildManager.CanPlace(body, selected.Value, hoverCell.x, hoverCell.y, rotation, out _);
    }

    public void OnGridClick(int x, int y)
    {
        // The release that ends a pan is not a click. uGUI fires OnPointerClick on release whenever the
        // press and release landed on the same object, however far the pointer travelled in between — so
        // without this, every drag of the map would also select (or deselect) whatever was under where
        // you grabbed it.
        if (panDragged) return;

        // In Demolition Mode the click has already been handled by the brush (PollDemolish). Selecting
        // the building underneath as well would move the marker to something the player is in the middle
        // of taking down, and then Validate would clear it the moment they confirmed.
        if (tab == Tab.Build && BuildDemolition.IsFor(body)) return;

        // Holding a piece? The click ASKS to place it — it doesn't place it. Building costs resources
        // and permanently occupies ground on a grid where siting decides yield, so it's not something to
        // do on a stray click.
        if (tab == Tab.Build && selected.HasValue)
        {
            // DRAWN CLASSES ARE THE DRAG'S BUSINESS, NOT THIS METHOD'S. A press-and-release over one cell
            // reaches both PollBuildDraw and here, and without this the drag would commit the footprint
            // while this simultaneously opened a confirm dialog for the type's AUTHORED shape — two
            // different buildings from one click. Fixed classes still use the click-and-confirm path,
            // which is the whole of their placement.
            if (IsDrawn(SurfaceBuildingDatabase.Get(selected.Value))) return;

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
                // Same reasoning as ClearMoonPanes: the fog image dies with the frame, its texture does
                // not belong to a GameObject and has to go by hand or it leaks per closed tab.
                if (moonFogTex.TryGetValue(m, out var ft) && ft != null) Destroy(ft);
                moonFrame.Remove(m); moonImg.Remove(m); moonTex.Remove(m);
                moonFog.Remove(m); moonFogTex.Remove(m);
                moonIndexBar.Remove(m);
            }

        foreach (var m in openMaps)
        {
            if (m == body || moonFrame.ContainsKey(m)) continue;

            var frame = UIFactory.NewUI(moonLayer, "MoonFrame").GetComponent<RectTransform>();
            frame.gameObject.AddComponent<RectMask2D>();

            var contentGO = UIFactory.NewUI(frame, "MoonMap");
            var img = contentGO.AddComponent<RawImage>();
            img.raycastTarget = true;   // a click on a moon map is UI, not a click-away that closes the window
            // TEXTURED, like the host planet's map — this is a real scrollable map of a real world that
            // the player pans, zooms and builds on, not a thumbnail. It used to take the flat build,
            // which meant a moon's ground was untextured beside a planet's textured ground in the same
            // window, and the difference read as the moon having failed to load rather than as two
            // renderers.
            //
            // A smaller texel budget than the host's, because a moon pane is a fraction of the window:
            // it still buys the art at full resolution on the moon-sized grids that matter, and it stops
            // three open moons costing more memory than the planet they orbit.
            Texture2D tex = m.surface != null
                ? SurfaceTextureRenderer.BuildGridTextured(m, MoonPaneTexelBudget)
                : null;
            img.texture = tex;
            if (tex == null) img.color = new Color(0.10f, 0.12f, 0.16f, 1f);
            var crt = img.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = Vector2.zero;

            // ---- THE SAME BLACKOUT THE HOST GETS ----
            //
            // A moon is surveyed exactly as a planet is, so an unsurveyed moon has to be exactly as
            // blank. Without its own mask layer a moon pane sat there showing every coastline of a world
            // nobody had been to, beside a host planet correctly blacked out — and the pane is the same
            // scrollable map, so it gave away everything the host's mask was protecting.
            //
            // A child of the map image, stretched over it, so it pans and zooms with the terrain for free.
            var fogGO = UIFactory.NewUI(crt, "MoonFog");
            var mfog = fogGO.AddComponent<RawImage>();
            UIFactory.Stretch(mfog.rectTransform);
            mfog.raycastTarget = false;
            fogGO.SetActive(false);

            // ---- ITS OWN INDEX BUTTONS, ON ITS OWN SURVEY ----
            //
            // A moon is surveyed exactly as a planet is and has its own indexes on its own ground, so
            // the bar is attached to the moon's FRAME and given the moon. Sharing the host planet's bar
            // would offer the planet's indexes over the moon's terrain, which is worse than offering
            // none — it would be confidently wrong about what is under the cursor.
            moonIndexBar[m] = IndexIconBar.Attach(frame, m);

            moonFrame[m] = frame; moonImg[m] = img; moonTex[m] = tex; moonFog[m] = mfog;
        }
    }

    void ClearMoonPanes()
    {
        foreach (var kv in moonFrame) if (kv.Value != null) Destroy(kv.Value.gameObject);
        foreach (var kv in moonTex) if (kv.Value != null) Destroy(kv.Value);
        // The fog IMAGE is a child of the frame and dies with it; its TEXTURE is not owned by any
        // GameObject and leaks one per moon per world change if it is not destroyed here by hand.
        foreach (var kv in moonFogTex) if (kv.Value != null) Destroy(kv.Value);
        moonFrame.Clear(); moonImg.Clear(); moonTex.Clear();
        moonFog.Clear(); moonFogTex.Clear();
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
        // Against the Earth-relative scale: 1 IS Earth, so "Small" has to straddle it rather than sit
        // below it, and the old cuts (which assumed Earth was 2) called an Earth-mass world Small and a
        // 4-mass super-Earth Medium.
        string w = mass <= MassRules.AsteroidMax ? "Tiny"
                 : mass < 0.9f ? "Small"
                 : mass < 1.6f ? "Earth-sized"
                 : mass < 3f ? "Large"
                 : mass < WorldClassifier.GasGiantMassFloor ? "Super-Earth"
                 : "Giant";
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
