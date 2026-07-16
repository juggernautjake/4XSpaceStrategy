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

    public enum Tab { Info, Sites, Build, Infrastructure, Power, Survey, Terrain }

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
            case Tab.Info:
                return true;                       // always: name, type, orbit and host star are free

            case Tab.Sites:
                if (!body.Surveyed) { why = "survey this world to see what's on it"; return false; }
                return true;

            case Tab.Survey:
                if (!body.Surveyed) { why = "survey this world first"; return false; }
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

            case Tab.Infrastructure:
                if (body.owner != FactionManager.Player) { why = "this world isn't yours"; return false; }
                if (!body.settled) { why = "nothing is built here — settle the world first"; return false; }
                return true;

            case Tab.Power:
                // Same bar as Infrastructure: it reports on what's standing here, and on a world with
                // nothing standing on it there is no grid to report.
                if (body.owner != FactionManager.Player) { why = "this world isn't yours"; return false; }
                if (!body.settled) { why = "nothing is built here — settle the world first"; return false; }
                return true;
        }
        return true;
    }

    GameObject root;
    TMP_Text titleText;
    RectTransform tabStrip, sidePanel, gridHolder;
    RawImage mapImage, overlayImage;
    RectTransform mapRT, pieceLayer, ghostLayer;
    TMP_Text statusText;

    CelestialBody body;
    Tab tab = Tab.Info;

    // Build-mode state.
    SurfaceBuildingType? selected;      // null = nothing picked up
    int rotation;
    Vector2Int hoverCell = new Vector2Int(-1, -1);
    bool hoverValid;

    // Survey-mode state.
    SurfaceIndexKind activeIndex = SurfaceIndexKind.None;

    readonly LiveSet live = new LiveSet();
    string lastSig = null;
    Texture2D overlayTex;
    float powerRepaintIn;   // see Update: the power overlay repaints on a timer, not every frame
    Color[] powerPx;        // reused scratch for that repaint — see RefreshPowerOverlay

    // Selection marker (see DrawSelectionMarker / AnimateMarker).
    RectTransform markerLayer;
    Image markerRing, markerArrow;
    float markerRingBase, markerArrowBaseY;
    PlacedBuilding lastMarkedSelection;

    // A FULL-SCREEN window (Raptok's request: selecting a planet fills the screen with the Planet
    // View). Measured from the live canvas rather than the 1920x1080 reference so it fills the ACTUAL
    // screen, with a small margin so the frame isn't flush to the edge. Re-measured on every open
    // (ShowFor) since the canvas rect isn't known at bootstrap. The map zooms inside its viewport; the
    // resize grip and draggable title bar still work, so it can be shrunk by hand afterwards.
    const float ScreenMargin = 8f;
    static Vector2 WindowSize(Transform parent)
    {
        var canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
        var crt = canvas != null ? canvas.GetComponent<RectTransform>() : null;
        Vector2 screen = crt != null && crt.rect.width > 1f
            ? crt.rect.size
            : new Vector2(1920f, 1080f);   // fallback: the canvas reference resolution
        return new Vector2(
            Mathf.Max(640f, screen.x - ScreenMargin * 2f),
            Mathf.Max(400f, screen.y - ScreenMargin * 2f));
    }

    const float SidePanelW = 300f;

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

        // Tabs.
        tabStrip = UIFactory.NewUI(content, "Tabs").GetComponent<RectTransform>();
        tabStrip.anchorMin = new Vector2(0, 1); tabStrip.anchorMax = new Vector2(1, 1);
        tabStrip.pivot = new Vector2(0.5f, 1); tabStrip.sizeDelta = new Vector2(0, 26);
        var th = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 4; th.childControlWidth = true; th.childControlHeight = true; th.childForceExpandWidth = false;

        // The VIEWPORT: a fixed window onto the surface. It never changes size — zooming scales the map
        // INSIDE it, which is what a map window should do. It used to resize the window itself, so
        // zooming in on a big world grew the panel off the edge of the screen.
        gridHolder = UIFactory.NewUI(content, "Viewport").GetComponent<RectTransform>();
        gridHolder.anchorMin = new Vector2(0, 0); gridHolder.anchorMax = new Vector2(1, 1);
        gridHolder.offsetMin = new Vector2(0, 34);                 // clear the status line
        gridHolder.offsetMax = new Vector2(-(SidePanelW + 8f), -32); // clear the tabs and the side panel
        var vpImg = gridHolder.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0.02f, 0.03f, 0.05f, 1f);          // letterbox behind a small map
        gridHolder.gameObject.AddComponent<RectMask2D>();          // the map is clipped to the viewport

        var mapGO = UIFactory.NewUI(gridHolder, "Map");
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

        pieceLayer = UIFactory.NewUI(mapRT, "Pieces").GetComponent<RectTransform>();
        UIFactory.Stretch(pieceLayer);
        var plImg = pieceLayer.gameObject.AddComponent<Image>();
        plImg.color = new Color(0, 0, 0, 0); plImg.raycastTarget = false;

        // Above the pieces so the ring/arrow are never hidden behind a structure's own tiles.
        markerLayer = UIFactory.NewUI(mapRT, "Markers").GetComponent<RectTransform>();
        UIFactory.Stretch(markerLayer);
        var mlImg = markerLayer.gameObject.AddComponent<Image>();
        mlImg.color = new Color(0, 0, 0, 0); mlImg.raycastTarget = false;

        ghostLayer = UIFactory.NewUI(mapRT, "Ghost").GetComponent<RectTransform>();
        UIFactory.Stretch(ghostLayer);
        var glImg = ghostLayer.gameObject.AddComponent<Image>();
        glImg.color = new Color(0, 0, 0, 0); glImg.raycastTarget = false;

        // The map itself is the click/hover target for placement.
        var probe = mapGO.AddComponent<SurfaceGridProbe>();
        probe.Init(this, mapRT);

        BuildZoomBar();

        // Side panel: the tab's controls.
        // Pinned to the RIGHT edge at a fixed width, rather than inset from the left by the map's size.
        // The map now grows with the world (ApplyMapSize), so a left-inset panel would be shoved off
        // the window by any world bigger than the old fixed 720px map.
        var sideHolder = UIFactory.NewUI(content, "SideHolder").GetComponent<RectTransform>();
        sideHolder.anchorMin = new Vector2(1, 0); sideHolder.anchorMax = new Vector2(1, 1);
        sideHolder.pivot = new Vector2(1, 0.5f);
        sideHolder.sizeDelta = new Vector2(SidePanelW, -66f);   // 32 top chrome + 34 bottom status bar
        sideHolder.anchoredPosition = new Vector2(0, -1f);
        UIFactory.ScrollView(sideHolder, out sidePanel);

        statusText = UIFactory.Text(content, "", UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.TopLeft);
        var srt = statusText.rectTransform;
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0);
        srt.pivot = new Vector2(0.5f, 0); srt.sizeDelta = new Vector2(0, 30); srt.anchoredPosition = Vector2.zero;

        PlanetUI.OnBodySelected += OnBodySelected;
        PlanetUI.OnClosed += HideOnDeselect;

        // The title-bar 'X' bakes in a bare root.SetActive(false). Now that this window IS the planet
        // selection, closing it should also clear the selection — otherwise the camera stays locked on a
        // world whose window is gone and the labels linger until the next empty-space click. Route the X
        // through CloseAll so it's symmetric with click-away; the factory's own hide still fires too,
        // which is harmless.
        var closeBtn = root.transform.Find("TitleBar")?.GetComponentInChildren<Button>();
        if (closeBtn != null)
            closeBtn.onClick.AddListener(() => { if (PlanetUI.Selected != null) PlanetUI.Instance?.CloseAll(); });

        root.SetActive(false);
    }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= OnBodySelected;
        PlanetUI.OnClosed -= HideOnDeselect;
    }

    // Selecting a body now OPENS the Planet View full-screen (Raptok's request), rather than only
    // updating the stored body when the window already happened to be open.
    void OnBodySelected(CelestialBody b)
    {
        if (b != null) ShowFor(b);
        else body = b;
    }

    // Clearing the selection (click-away, Esc-driven CloseAll) closes the window with it, so the
    // full-screen view doesn't stay up over a deselected world.
    void HideOnDeselect() { if (root != null) root.SetActive(false); }

    public void ShowFor(CelestialBody b) => ShowFor(b, null);

    /// Open on a world, optionally landing on a specific tab. `openOn` is honoured only if that tab is
    /// actually available for this world — asking for Build on an unsettled rock lands on Info, which is
    /// the tab that can explain why.
    public void ShowFor(CelestialBody b, Tab? openOn)
    {
        body = b;
        selected = null; rotation = 0;
        CancelPlace();          // a confirm from the last world means nothing on this one
        lastSig = null;

        // The tab you were on may not exist for THIS world — Build on your capital, then click a
        // barren rock, and Build has to give way to something readable rather than showing an empty
        // build list on a world nobody lives on.
        if (openOn.HasValue && TabAvailable(openOn.Value, out _)) tab = openOn.Value;
        else if (!TabAvailable(tab, out _)) tab = Tab.Info;
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
        rrt.SetAsLastSibling();
        RefreshMapTexture();
    }

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
        bar.sizeDelta = new Vector2(188, 26);

        var bg = bar.gameObject.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.07f, 0.11f, 0.85f);

        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4; h.padding = new RectOffset(4, 4, 3, 3);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;

        ZoomButton(bar, "–", () => ZoomBy(1f / ZoomStep));
        ZoomButton(bar, "+", () => ZoomBy(ZoomStep));

        var fit = UIFactory.Button(bar.transform, "Fit", () => { tilePx = 0f; mapPan = Vector2.zero; ApplyMapSize(); DrawSelectionMarker(); }, 20f);
        var fle = fit.gameObject.AddComponent<LayoutElement>();
        fle.preferredWidth = 40f; fle.flexibleWidth = 0f;

        zoomLabel = UIFactory.Text(bar, "100%", UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.Center);
        var zle = zoomLabel.gameObject.AddComponent<LayoutElement>();
        zle.flexibleWidth = 1f;
    }

    void ZoomButton(RectTransform bar, string label, System.Action onClick)
    {
        var b = UIFactory.Button(bar.transform, label, onClick, 20f);
        var le = b.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 26f; le.flexibleWidth = 0f;
    }

    /// How much one button press (or one wheel notch) changes the zoom.
    const float ZoomStep = 1.5f;

    /// Zoom about the CENTRE of the viewport — the buttons have no cursor to zoom toward, and pulling
    /// the view sideways when someone presses "+" would be its own bug.
    public void ZoomBy(float factor)
    {
        if (body?.surface == null) return;
        float fit = FitTilePx();
        float max = Mathf.Max(fit, MaxTilePx());
        float next = Mathf.Clamp(tilePx * factor, fit, max);
        if (Mathf.Approximately(next, tilePx)) return;

        // The centre of the viewport stays put: scale the pan by the same ratio the map scaled by.
        mapPan *= next / tilePx;
        tilePx = next;
        ApplyMapSize();
        DrawSelectionMarker();
    }

    /// Current zoom as a percentage of "the whole world fits", for the readout.
    float ZoomPercent()
    {
        float fit = FitTilePx();
        return fit > 0.001f ? tilePx / fit * 100f : 100f;
    }

    /// Pixels per cell at which the ENTIRE surface is visible. This is the zoomed-all-the-way-out end.
    ///
    /// No ceiling on it any more. There used to be one — capped at the detailed map's own tile size —
    /// because a coarse 12x6 moon fitted to the viewport gave 67px tiles. With the grid at detail
    /// resolution that can't happen: the smallest world is 96x48 cells, so fitting it is ~7px a cell.
    /// The cap was also what crushed the zoom range down to about 2.4x, since it was clamping both ends
    /// to nearly the same number.
    float FitTilePx()
    {
        if (body?.surface == null) return 4f;
        var vp = gridHolder.rect;
        if (vp.width < 1f || vp.height < 1f) return 4f;
        return Mathf.Min(vp.width / body.surface.width, vp.height / body.surface.height);
    }

    /// Pixels per cell at the zoomed-all-the-way-IN end: roughly MaxVisibleTiles cells fill the
    /// viewport, so the closest view shows the same amount of ground on every world rather than
    /// depending on how big the planet happens to be.
    ///
    /// visibleTiles = (w/px) * (h/px) = area / px^2  ->  px = sqrt(area / visibleTiles)
    float MaxTilePx()
    {
        var vp = gridHolder.rect;
        float area = Mathf.Max(1f, vp.width * vp.height);
        return Mathf.Sqrt(area / MaxVisibleTiles);
    }

    void ApplyMapSize()
    {
        if (body?.surface == null) return;

        float fit = FitTilePx();
        float max = Mathf.Max(fit, MaxTilePx());     // a tiny world may already show <200 tiles when fitted
        tilePx = Mathf.Clamp(tilePx <= 0f ? fit : tilePx, fit, max);

        mapRT.sizeDelta = new Vector2(body.surface.width * tilePx, body.surface.height * tilePx);
        ClampPan();
    }

    // Keep the viewport covered: you can never drag the map so far that you're looking at the letterbox
    // instead of the world. When the map is smaller than the viewport on an axis, it centres on it.
    void ClampPan()
    {
        var vp = gridHolder.rect;
        Vector2 size = mapRT.sizeDelta;
        float slackX = Mathf.Max(0f, (size.x - vp.width) * 0.5f);
        float slackY = Mathf.Max(0f, (size.y - vp.height) * 0.5f);
        mapPan.x = Mathf.Clamp(mapPan.x, -slackX, slackX);
        mapPan.y = Mathf.Clamp(mapPan.y, -slackY, slackY);
        mapRT.anchoredPosition = mapPan;
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
        sb.Append((int)activeIndex).Append('|').Append(body.Surveyed ? 1 : 0).Append('|').Append(body.deepSurveyed ? 1 : 0).Append('|');

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

        string sig = Signature();
        if (sig != lastSig) { lastSig = sig; Rebuild(); }

        // The zoom limits are derived from the VIEWPORT'S size, which isn't known until Unity has laid
        // the window out — so the first ApplyMapSize (from ShowFor) can run against a zero rect. Re-fit
        // once it's real, and again whenever the window is resized by its grip.
        if (gridHolder != null && gridHolder.rect.size != lastViewportSize)
        {
            lastViewportSize = gridHolder.rect.size;
            ApplyMapSize();
            DrawSelectionMarker();
        }

        live.Tick();
        PollHover();
        PollMapZoom();
        PollMapPan();
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
        if (tab == Tab.Power && body.surface != null)
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
                statusText.text = activeIndex == SurfaceIndexKind.None
                    ? "<color=#9FB4C8>Pick an index on the right to overlay it on the map.</color>"
                    : $"<b>{SurfaceIndex.Name(activeIndex)}</b> — {SurfaceIndex.Describe(activeIndex)}";
                break;
            case Tab.Power:
                {
                    // The legend belongs here rather than in the panel: it explains the MAP, and this is
                    // the line that sits under the map.
                    var nets = PowerGrid.Nets(body);
                    if (nets.Count == 0)
                    {
                        statusText.text = "<color=#FFBF4D>No power on this world.</color> <size=10><color=#9FB4C8>" +
                                          "The map is dark because there is no grid on it — build a plant from the Build tab.</color></size>";
                        break;
                    }
                    float gen = PowerGrid.TotalGeneration(body), draw = PowerGrid.TotalDraw(body);
                    string hex = ColorUtility.ToHtmlStringRGB(gen >= draw ? UITheme.Good : UITheme.Bad);
                    var sb = new System.Text.StringBuilder();
                    sb.Append("<color=#F5F58C>■</color> grid   <color=#4DC8FF>■</color> plants & relays");
                    sb.Append($"   ·   <b>{gen:0.0}</b> made, <b>{draw:0.0}</b> drawn, ");
                    sb.Append($"<color=#{hex}><b>{(gen - draw >= 0f ? "+" : "")}{gen - draw:0.0}/s</b></color>");
                    if (hoverCell.x >= 0)
                    {
                        var n = PowerGrid.NetAt(body, hoverCell.x, hoverCell.y);
                        sb.Append($"\n<color=#9FB4C8>({hoverCell.x},{hoverCell.y})</color> ");
                        sb.Append(n == null
                            ? "<color=#FF6659>dark — no grid reaches this tile</color>"
                            : $"<color=#F5F58C>on Grid {n.index}</color> <size=10><color=#9FB4C8>· {PowerGrid.SupplyLabel(n)}</color></size>");
                    }
                    statusText.text = sb.ToString();
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
            case Tab.Info: BuildInfoPanel(); break;
            case Tab.Sites: BuildSitesPanel(); break;
            case Tab.Build: BuildBuildPanel(); break;
            case Tab.Infrastructure: BuildInfrastructurePanel(); break;
            case Tab.Power: BuildPowerPanel(); break;
            case Tab.Survey: BuildSurveyPanel(); break;
            case Tab.Terrain: BuildTerrainPanel(); break;
        }

        RefreshOverlay();
        DrawPieces();
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

    // ---------------- INFO ----------------
    void BuildInfoPanel()
    {
        Header("THIS WORLD");
        var card = Card();
        Stat(card, "Name", () => body.name);
        Stat(card, "Type", () => TerraformDiagnosis.Pretty(body.type));
        // The conditionals MUST be parenthesised: inside an interpolation hole a bare ':' is parsed as
        // the start of a format specifier, not as part of a ternary.
        Stat(card, "Surface", () => $"{(body.surface != null ? body.surface.width : 0)} × {(body.surface != null ? body.surface.height : 0)} tiles");
        Stat(card, "Size class", () => SizeWord(body.surfaceSize));
        Stat(card, "Owner", () =>
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(body.owner));
            return $"<color={hex}>{FactionManager.OwnerLabel(body.owner)}</color>";
        });
        Stat(card, "Habitability", () => $"<color={Habitability.ScoreColorHex(body.habitability)}>{body.habitability:F0}%</color> for {SpeciesManager.Current.name}");

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
    }

    static string SizeWord(int size)
    {
        if (size <= 4) return $"Tiny ({size})";
        if (size <= 7) return $"Small ({size})";
        if (size <= 11) return $"Medium ({size})";
        if (size <= 15) return $"Large ({size})";
        return $"Huge ({size})";
    }

    static string WeatherProse(CelestialBody b)
    {
        var sb = new System.Text.StringBuilder();
        float spin = Mathf.Abs(b.spinSpeed);
        sb.Append(spin < 3f ? "Barely turns — one face bakes, the other freezes. "
                : spin > 45f ? "Spins violently; the storms never stop. "
                : "A steady day/night cycle. ");
        sb.Append(Mathf.Abs(b.inclination) > 28f ? "Its severe axial tilt gives it savage seasons. " : "Mild seasons. ");
        float water = b.resources != null ? b.resources.Get(ResourceType.Water) : 0f;
        sb.Append(water < 60f ? "Bone dry — no weather to speak of. "
                : water > 300f ? "Wet, with heavy cloud and frequent storms. "
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

        BuildPlacedList();
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

            // Centre the map on it. A list entry you can't find on the map is a list entry.
            UIFactory.Button(card, "Show on map", () => CentreOn(cap.u, cap.v), 22);

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
    void BuildTerrainPanel()
    {
        Header("TERRAIN SANDBOX");
        Note("<color=#FFBF4D>Dev Mode.</color> Regenerates this world's surface live. Every map reads the same terrainParams, so what you see here is what the world becomes.");

        var p = body.terrainParams;
        SliderRow("Feature scale", "continent size", 0.4f, 3f, p.scale, v => SetTerrain(0, v));
        SliderRow("Elevation", "land vs water", 0.3f, 2f, p.elevation, v => SetTerrain(1, v));
        SliderRow("Moisture", "dry vs lush", 0.3f, 2f, p.moisture, v => SetTerrain(2, v));
        SliderRow("Temperature", "cold vs hot", 0.3f, 2f, p.heat, v => SetTerrain(3, v));
        SliderRow("Mountains", "ridged terrain", 0.3f, 2f, p.ridge, v => SetTerrain(4, v));

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

    // 0=scale 1=elevation 2=moisture 3=heat 4=ridge
    void SetTerrain(int which, float v)
    {
        if (body == null) return;
        var p = body.terrainParams;
        switch (which)
        {
            case 0: p.scale = v; break;
            case 1: p.elevation = v; break;
            case 2: p.moisture = v; break;
            case 3: p.heat = v; break;
            case 4: p.ridge = v; break;
        }
        body.terrainParams = p;
        RegenerateTerrain();
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
        Header("INDEX OVERLAYS");
        Note("Each overlay paints the grid with where a kind of building actually belongs. Survey a world to read its minerals; a deep survey by a research ship unlocks the rest.");

        AddIndexToggle(SurfaceIndexKind.None, "None (plain terrain)");
        foreach (var k in SurfaceIndex.All) AddIndexToggle(k, null);

        // Read the exact numbers under the cursor — the overlay shows you the region, this confirms it.
        Header("UNDER THE CURSOR");
        var card = Card();
        var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
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

        var btn = UIFactory.Button(card, "", () => { activeIndex = k; lastSig = null; }, 24);
        live.Button(btn, () =>
        {
            string nm = labelOverride ?? SurfaceIndex.Name(k);
            if (k == SurfaceIndexKind.None) return (true, activeIndex == k ? $"• {nm}" : nm);
            if (!SurfaceIndex.Unlocked(body, k)) return (false, $"{nm} — {SurfaceIndex.LockReason(body, k)}");
            return (true, activeIndex == k ? $"• {nm} (showing)" : $"Show {nm}");
        }, group);
    }

    // ---- Overlay texture ----
    // One point-filtered texture the size of the grid, stretched over the map. A cell per texel means
    // the overlay lines up with the build grid exactly and costs nothing to redraw.
    // The best fraction of a world highlighted when you pick a building up. Relative to THIS world, so
    // a frozen planet still shows you its ten hottest tiles — you just also get told they're poor.
    const float BestSitesFraction = 0.10f;

    void RefreshOverlay()
    {
        // Two different overlays share one texture:
        //  BUILD  — holding a structure highlights the best 10% of sites for it on this world.
        //  SURVEY — the raw index ramp.
        // The power grid is its own overlay entirely: it isn't a ramp over an index, it's a map of what
        // the electricity reaches, so it gets its own pass rather than being forced through Ramp().
        if (tab == Tab.Power && body.surface != null)
        {
            overlayImage.gameObject.SetActive(true);
            RefreshPowerOverlay();
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
        else if (tab == Tab.Survey && SurfaceIndex.Unlocked(body, activeIndex))
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
        EnsureOverlayTex(w, h);

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

        overlayTex.SetPixels(px);
        overlayTex.Apply();
        overlayImage.texture = overlayTex;
    }

    void EnsureOverlayTex(int w, int h)
    {
        if (overlayTex != null && overlayTex.width == w && overlayTex.height == h) return;
        if (overlayTex != null) Destroy(overlayTex);
        overlayTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
    }

    // ---- Structures on the map ----
    void DrawPieces()
    {
        for (int i = pieceLayer.childCount - 1; i >= 0; i--) Destroy(pieceLayer.GetChild(i).gameObject);
        if (body?.surface == null) return;

        foreach (var p in SurfaceBuildManager.On(body))
        {
            var info = p.Info;
            // Fully opaque and pushed past its own saturation. What separates a structure from the
            // ground is now the black OUTLINE below, not the terrain being dulled to get out of its way
            // — so this only has to be a strong, readable colour, not the only strong colour on screen.
            var c = Vivid(info.color);
            var cells = SurfaceBuildingDatabase.Footprint(p);
            foreach (var cell in cells) AddCellQuad(pieceLayer, cell.x, cell.y, c);
            OutlineFootprint(pieceLayer, cells);
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

        confirmPanel = UIFactory.NewUI(gridHolder, "ConfirmPlace").GetComponent<RectTransform>();
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
        var vp = gridHolder.rect;
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
    {
        x = y = -1;
        if (body?.surface == null || mapRT == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRT, screenPos, cam, out Vector2 lp)) return false;

        // The local point is relative to the rect's pivot; normalize against the rect's own bounds.
        var r = mapRT.rect;
        float u = (lp.x - r.xMin) / r.width;
        float v = (lp.y - r.yMin) / r.height;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

        x = Mathf.Clamp(Mathf.FloorToInt(u * body.surface.width), 0, body.surface.width - 1);
        y = Mathf.Clamp(Mathf.FloorToInt(v * body.surface.height), 0, body.surface.height - 1);
        return true;
    }

    // Scroll over the viewport to zoom the MAP inside it — the window itself never moves or resizes.
    // Proportional, like the world camera, so one notch feels the same at every scale. Only when the
    // cursor is over the viewport, so scrolling the side panel still scrolls the side panel.
    void PollMapZoom()
    {
        if (body?.surface == null || mapRT == null) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(gridHolder, Input.mousePosition, null)) return;

        float fit = FitTilePx();
        float max = Mathf.Max(fit, MaxTilePx());

        // One wheel notch = one press of + or -, so the two controls agree. Unity's ScrollWheel axis is
        // ~0.1 per notch, hence the 10x before raising ZoomStep to it: pow(1.5, 0.1*10) = 1.5.
        float next = Mathf.Clamp(tilePx * Mathf.Pow(ZoomStep, scroll * 10f), fit, max);
        if (Mathf.Approximately(next, tilePx)) return;

        // Zoom TOWARD THE CURSOR: keep whatever is under the pointer pinned there, rather than
        // zooming to the middle and making the player chase what they were looking at.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                gridHolder, Input.mousePosition, null, out Vector2 vpPoint))
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
        if (body?.surface == null) return;

        // Cleared on ANY press, not just one that starts a pan. It has to be: a piece-holding press
        // never starts a pan (see leftPans below), so if the flag only reset when a pan began, one drag
        // would latch it true and silently swallow every click from then on.
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2)) panDragged = false;

        // Left-drag is off while a piece is held — there, a left press is placing a building, and
        // dragging the ground out from under it is the last thing anyone wants.
        bool leftPans = !selected.HasValue;

        if (!panning &&
            ((leftPans && Input.GetMouseButtonDown(0)) || Input.GetMouseButtonDown(2)) &&
            RectTransformUtility.RectangleContainsScreenPoint(gridHolder, Input.mousePosition, null) &&
            // The zoom bar floats INSIDE the viewport, so its rect is inside the pan region too. Without
            // this, pressing + and twitching a few pixels would drag the map out from under you.
            !RectTransformUtility.RectangleContainsScreenPoint(zoomBar, Input.mousePosition, null))
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
        if (ScreenToCell(Input.mousePosition, null, out int x, out int y))
        {
            if (x != hoverCell.x || y != hoverCell.y)
            {
                hoverCell = new Vector2Int(x, y);
                RecomputeHoverValidity();
            }
        }
        else if (hoverCell.x >= 0)
        {
            hoverCell = new Vector2Int(-1, -1);
            hoverValid = false;
        }
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
