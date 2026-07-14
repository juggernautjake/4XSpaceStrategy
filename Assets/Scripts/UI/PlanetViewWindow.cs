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

    public enum Tab { Info, Build, Infrastructure, Survey }

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

    // Selection marker (see DrawSelectionMarker / AnimateMarker).
    RectTransform markerLayer;
    Image markerRing, markerArrow;
    float markerRingBase, markerArrowBaseY;
    PlacedBuilding lastMarkedSelection;

    // The map is sized per body from MapMetrics (see ApplyMapSize) so one grid cell is always exactly a
    // detailed-map tile. These are only the initial/fallback dimensions before a world is selected.
    const float MapW = 720f, MapH = 380f;
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
        var content = UIFactory.Window(parent, "Planet View", new Vector2(MapW + SidePanelW + 60f, MapH + 190), out root, out titleText);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        // Tabs.
        tabStrip = UIFactory.NewUI(content, "Tabs").GetComponent<RectTransform>();
        tabStrip.anchorMin = new Vector2(0, 1); tabStrip.anchorMax = new Vector2(1, 1);
        tabStrip.pivot = new Vector2(0.5f, 1); tabStrip.sizeDelta = new Vector2(0, 26);
        var th = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 4; th.childControlWidth = true; th.childControlHeight = true; th.childForceExpandWidth = false;

        // The map: terrain texture, an overlay texture on top, then the structure and ghost layers.
        gridHolder = UIFactory.NewUI(content, "GridHolder").GetComponent<RectTransform>();
        gridHolder.anchorMin = new Vector2(0, 1); gridHolder.anchorMax = new Vector2(0, 1);
        gridHolder.pivot = new Vector2(0, 1);
        gridHolder.sizeDelta = new Vector2(MapW, MapH);
        gridHolder.anchoredPosition = new Vector2(0, -32);

        var mapGO = UIFactory.NewUI(gridHolder, "Map");
        mapImage = mapGO.AddComponent<RawImage>();
        mapRT = mapImage.rectTransform;
        UIFactory.Stretch(mapRT);
        mapGO.AddComponent<Outline>().effectColor = UITheme.AccentDim;

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
        root.SetActive(false);
    }

    void OnDestroy() { PlanetUI.OnBodySelected -= OnBodySelected; }

    void OnBodySelected(CelestialBody b)
    {
        if (root != null && root.activeSelf) ShowFor(b);
        else body = b;
    }

    public void ShowFor(CelestialBody b)
    {
        body = b;
        selected = null; rotation = 0;
        lastSig = null;
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
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

    void RefreshMapTexture()
    {
        if (body == null) return;
        mapImage.texture = Pastel(SurfaceTextureRenderer.Build(body));
        titleText.text = $"Planet View — {body.name}";
        ApplyMapSize();
    }

    // Terrain is rendered PASTEL: every biome keeps its own hue, so tundra still reads as tundra and
    // desert as desert — but the saturation is pulled down and the lightness up, so nothing on the map
    // competes with the fully-saturated, fully-opaque structures sitting on top of it.
    //
    // Done by desaturating the PIXELS rather than by fading the image with alpha. Alpha would drop the
    // whole map toward the dark panel behind it, which crushes the differences BETWEEN terrain types —
    // exactly the information the map exists to convey. Pastel keeps the hue relationships intact and
    // only gives up intensity.
    Texture2D pastelTex; int pastelFor = -1;

    Texture2D Pastel(Texture2D src)
    {
        if (src == null) return null;
        if (pastelTex != null && pastelFor == body.id &&
            pastelTex.width == src.width && pastelTex.height == src.height) return pastelTex;

        if (pastelTex != null) Destroy(pastelTex);
        pastelTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
        { filterMode = src.filterMode, wrapMode = TextureWrapMode.Clamp };

        var px = src.GetPixels();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            // Toward its own grey (keeps hue, drops saturation), then toward white (raises lightness).
            float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            c = Color.Lerp(c, new Color(grey, grey, grey), 0.30f);
            c = Color.Lerp(c, Color.white, 0.28f);
            c.a = px[i].a;
            px[i] = c;
        }
        pastelTex.SetPixels(px);
        pastelTex.Apply();
        pastelFor = body.id;
        return pastelTex;
    }

    // Size the map from MapMetrics, exactly like the detailed viewer does, instead of stretching the
    // surface across a fixed 720x380 rectangle.
    //
    // That fixed size was the bug behind "the tiles are too big": a 12x6 moon and a 48x24 world were
    // both stretched to the same rectangle, so a tile was a different number of pixels on every body
    // and matched the detailed viewer on none of them. Deriving width/height from tile count x
    // DetailTile means one grid cell is ALWAYS 42px — the same cell, at the same size, as the detailed
    // map — so a building footprint lands exactly on the tiles you can see.
    // Scroll-to-zoom over the map. 1 = one grid cell is exactly a detailed-map tile (42px).
    float mapZoom = 1f;
    const float MapZoomMin = 0.35f, MapZoomMax = 3f;

    void ApplyMapSize()
    {
        if (body?.surface == null) return;
        float tile = MapMetrics.DetailTile(body.surfaceSize) * mapZoom;
        float w = body.surface.width * tile;
        float h = body.surface.height * tile;

        gridHolder.sizeDelta = new Vector2(w, h);

        // Grow the window to fit the map plus the side panel and chrome, so a big world simply shows a
        // bigger map rather than squashing it.
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w + SidePanelW + 60f, Mathf.Max(360f, h + 100f));
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
        sb.Append(body.placedBuildings != null ? body.placedBuildings.Count : 0);
        return sb.ToString();
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        if (body == null) { root.SetActive(false); return; }

        string sig = Signature();
        if (sig != lastSig) { lastSig = sig; Rebuild(); }

        live.Tick();
        PollHover();
        PollMapZoom();

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
        if (tab == Tab.Build && selected.HasValue &&
            (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.R)))
        {
            rotation = (rotation + 1) % 4;
            RecomputeHoverValidity();   // a rotated piece may now fit (or stop fitting) where it is
            SimpleAudio.Instance?.PlayTick();
        }

        // Escape drops the held piece.
        if (tab == Tab.Build && selected.HasValue && Input.GetKeyDown(KeyCode.Escape))
        {
            selected = null; lastSig = null; ClearGhost();
        }

        if (tab == Tab.Build) DrawGhost();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        switch (tab)
        {
            case Tab.Build:
                if (!selected.HasValue)
                    statusText.text = "<color=#9FB4C8>Pick a structure on the right, then hover the map to place it. Right-click rotates. Esc cancels.</color>";
                else
                {
                    var info = SurfaceBuildingDatabase.Get(selected.Value);
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"<b>{info.name}</b> · rot {rotation * 90}° <size=10><color=#9FB4C8>(R / right-click rotates · Esc cancels)</color></size>");

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
            case Tab.Build: BuildBuildPanel(); break;
            case Tab.Infrastructure: BuildInfrastructurePanel(); break;
            case Tab.Survey: BuildSurveyPanel(); break;
        }

        RefreshOverlay();
        DrawPieces();
        if (tab != Tab.Build) ClearGhost();
    }

    void BuildTabStrip()
    {
        foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
        {
            var captured = t;
            bool active = t == tab;
            var btn = UIFactory.Button(tabStrip, t.ToString(), () =>
            {
                tab = captured;
                if (captured != Tab.Build) selected = null;
                lastSig = null;
            }, 22);
            var le = btn.GetComponent<LayoutElement>();
            le.preferredWidth = 90; le.minWidth = 70; le.flexibleWidth = 0;

            // The active tab is state, not hover, so a persistent tint is correct here.
            var colors = btn.colors;
            colors.normalColor = active ? UITheme.ButtonActive : UITheme.ButtonBg;
            colors.highlightedColor = colors.normalColor;
            colors.selectedColor = colors.normalColor;
            btn.colors = colors;
            var lbl = btn.GetComponentInChildren<TMP_Text>();
            if (lbl != null) { lbl.fontSize = UITheme.SmallSize; lbl.color = active ? Color.white : UITheme.SubText; }
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
                return $"<color=#{hex}>{m} metal · {e} energy</color> · {info.Cells} tiles · {idx}";
            });

            var btn = UIFactory.Button(card, "", () =>
            {
                selected = (selected.HasValue && selected.Value == t) ? (SurfaceBuildingType?)null : t;
                rotation = 0;
                lastSig = null;
            }, 24);
            live.Button(btn, () =>
            {
                bool held = selected.HasValue && selected.Value == t;
                if (held) return (true, "Put down");
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
            string mark = SurfaceSelection.IsSelected(cap) ? "<color=#FFF266>▸ </color>" : "";
            string lvl = cap.CanUpgrade
                ? $"<color=#9FB4C8>Tech Lv {cap.level}/{PlacedBuilding.MaxLevel}</color>"
                : $"<color=#4DFF6E>Tech Lv {cap.level} (max)</color>";
            return $"{mark}<color=#{hex}>■</color> <b>{info.name}</b>  <size=10>{lvl}</size>";
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
                ? $" · <color=#F5F58C>+{SurfaceBuildManager.AdjacencyBonus(body, cap) * 100f:F0}% grid</color>" : "";
            return $"({cap.x},{cap.y}) · {site}{adj}\n" +
                   $"<color=#9FB4C8>Output ×{cap.OutputMult:0.00}</color> (siting × tech level)";
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
            return (can, can ? $"Upgrade → Lv{cap.level + 1} ({m}m {e}e)" : $"Upgrade — {why}");
        });

        UIFactory.Button(row.transform, "Demolish", () =>
        {
            SurfaceBuildManager.Demolish(body, cap);
            lastSig = null;
        }, 20);
    }

    // ---------------- SURVEY ----------------
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
            if (k == SurfaceIndexKind.None) return (true, activeIndex == k ? $"● {nm}" : nm);
            if (!SurfaceIndex.Unlocked(body, k)) return (false, $"{nm} — {SurfaceIndex.LockReason(body, k)}");
            return (true, activeIndex == k ? $"● {nm} (showing)" : $"Show {nm}");
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
            // FULLY OPAQUE and pushed to full saturation. The terrain under it is deliberately pastel
            // (see Pastel), so a structure is the one vivid thing on the map — what you built should
            // never be something you have to hunt for.
            var c = Vivid(info.color);
            foreach (var cell in SurfaceBuildingDatabase.Footprint(p)) AddCellQuad(pieceLayer, cell.x, cell.y, c);
        }
    }

    // Push a colour away from grey — the opposite of what Pastel does to the terrain, so the two can
    // never be confused for one another.
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

        if (hoverCell.x >= 0)
        {
            // Snapped: green when it fits, red when it doesn't, so validity is obvious before you click.
            Color c = hoverValid ? new Color(info.color.r, info.color.g, info.color.b, 0.62f)
                                 : new Color(1f, 0.25f, 0.2f, 0.5f);
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
            var c = new Color(info.color.r, info.color.g, info.color.b, 0.45f);
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

    // Scroll over the map to zoom it. Proportional, like the world camera, so one notch feels the same
    // at every scale. Only when the cursor is actually over the map, so scrolling the side panel still
    // scrolls the side panel.
    void PollMapZoom()
    {
        if (body?.surface == null || mapRT == null) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(gridHolder, Input.mousePosition, null)) return;

        float next = Mathf.Clamp(mapZoom * Mathf.Exp(scroll * 4f), MapZoomMin, MapZoomMax);
        if (Mathf.Approximately(next, mapZoom)) return;
        mapZoom = next;

        ApplyMapSize();
        // The markers and pieces are anchored in the map's normalised space, so they follow the resize
        // for free — but the ring's size is in pixels, so it has to be rebuilt at the new scale.
        DrawSelectionMarker();
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
        // Holding a piece? The click places it.
        if (tab == Tab.Build && selected.HasValue)
        {
            if (!SurfaceBuildManager.CanPlace(body, selected.Value, x, y, rotation, out _)) return;
            if (SurfaceBuildManager.Place(body, selected.Value, x, y, rotation))
                lastSig = null;   // the built list and the map both changed
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
