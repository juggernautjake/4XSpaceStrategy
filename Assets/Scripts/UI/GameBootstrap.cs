using UnityEngine;

// Spawns all runtime-built systems and UI automatically when the scene starts — so none of it needs
// to be wired up in the Unity Editor. Also gives each generated system its own procedural sky and
// keeps habitability in sync with the current species.
public static class GameBootstrap
{
    static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (initialized) return;
        initialized = true;

        // Core managers (no canvas needed).
        Safe("GameClock", () => GameClock.Create());                 // the calendar: one second of game time is one in-game day
        Safe("SimpleAudio", () => SimpleAudio.Create());
        Safe("DevCheats", () => DevCheats.Create());                 // Dev Mode: keeps a million of everything topped up
        Safe("ResearchTaskManager", () => ResearchTaskManager.Create());
        Safe("UnitManager", () => UnitManager.Create());               // before the token renderer subscribes to it
        Safe("ColonyManager", () => ColonyManager.Create());             // colony economy, growth, terraforming, construction
        Safe("TerraformManager", () => TerraformManager.Create());          // planetary-engineering projects that raise world ceilings
        Safe("CityGrowth", () => CityGrowth.Create());                // colonies grow their own settlements (GameConfig toggle)
        Safe("FactionAI", () => FactionAI.Create());                 // rival civilisations: race + personality, slow natural growth & expansion
        Safe("DerelictRenderer", () => DerelictRenderer.Create());          // ancient derelict stations at odd orbits (some hold Vael fragments)
        Safe("CometManager", () => CometManager.Create());              // comets that sweep through systems; study/catch them for salvage or lore
        Safe("EarthquakeManager", () => EarthquakeManager.Create());         // fault-line quakes damage infrastructure on tectonic worlds
        Safe("ControlGroupInput", () => ControlGroupInput.Create());         // Ctrl+1..9 to bind fleets, 1..9 to recall them
        Safe("SquadronAI", () => SquadronAI.Create());                // standing orders: intercept, evade-and-report, escort, patrol
        Safe("PatrolTool", () => PatrolTool.Create());                // click out a patrol route
        Safe("RallyTool", () => RallyTool.Create());                 // click a squadron's fall-back point
        Safe("FleetMovementController", () => FleetMovementController.Create());
        Safe("TargetIndicator", () => TargetIndicator.Create());           // pulsing lock-on ring for right-click sends
        // Combat. Order matters: the renderers must exist before CombatManager's first tick, because
        // it hands them shots rather than drawing anything itself.
        Safe("ProjectileRenderer", () => ProjectileRenderer.Create());        // pooled bolts and beams, with rate-limited homing
        Safe("ExplosionRenderer", () => ExplosionRenderer.Create());         // impacts, intercepts and ships coming apart
        Safe("CombatManager", () => CombatManager.Create());             // ships fight whatever hostile comes into weapons range
        Safe("UnitTokenRenderer", () => UnitTokenRenderer.Create());
        Safe("UnitModelRenderer", () => UnitModelRenderer.Create());         // 3D meshes for stations + colony ships (falls back to tokens)
        Safe("GenesisCamera", () => GenesisCamera.Create());             // the intro films the real world with the real camera
        Safe("GenesisSequence", () => GenesisSequence.Create());           // ...and this is the story it films
        Safe("SpaceBackground", () => SpaceBackground.Create());
        Safe("PostFxController", () => PostFxController.Create());

        var _ = TooltipManager.Instance; // ensure tooltip exists

        // Context-aware custom cursor (stylized pointer + select / send / loading graphics).
        Safe("CursorManager", () => CursorManager.Create());

        // Main window canvas (above the scene's own UI).
        var canvas = UIFactory.CreateCanvas("RuntimeUICanvas", 100);

        Safe("ObjectLabelManager", () => ObjectLabelManager.Create(canvas.transform));
        Safe("ContextMenu", () => ContextMenu.Create(canvas.transform));
        Safe("NotificationManager", () => NotificationManager.Create(canvas.transform));

        Safe("OrbitControlPanel", () => OrbitControlPanel.Create(canvas.transform));
        // StarInfoPanel retired: clicking a star now shows only the tabbed InspectorWindow (star tabs);
        // the simpler StarInfoPanel was a duplicate that popped up alongside it. Not instantiated. (Its
        // habitable-zone toggle also lives on the Inspector's star Overview tab, so nothing is lost.)
        //   StarInfoPanel.Create(canvas.transform);
        Safe("ResearchWindow", () => ResearchWindow.Create(canvas.transform));
        Safe("SaveLoadMenu", () => SaveLoadMenu.Create(canvas.transform));
        Safe("SpeciesWindow", () => SpeciesWindow.Create(canvas.transform));
        Safe("UnitInfoPanel", () => UnitInfoPanel.Create(canvas.transform));
        Safe("SendToWindow", () => SendToWindow.Create(canvas.transform));      // pick a destination from a list of known places
        Safe("FleetWindow", () => FleetWindow.Create(canvas.transform));
        Safe("ShipyardWindow", () => ShipyardWindow.Create(canvas.transform));
        Safe("TerraformWindow", () => TerraformWindow.Create(canvas.transform));
        Safe("InspectorWindow", () => InspectorWindow.Create(canvas.transform));   // the tabbed panel for whatever you click on
        Safe("PlanetViewWindow", () => PlanetViewWindow.Create(canvas.transform));  // surface grid: info / build / survey overlays
        // CompactBodyPanel retired: single-clicking a body now opens the fleshed-out tabbed InspectorWindow
        // on it (InspectorWindow.OnBodySelected) instead of a compact readout, per the user's request to
        // keep the panel with more info and tabs. Not instantiated, so it never appears.
        //   CompactBodyPanel.Create(canvas.transform);
        Safe("BodyUnitsPanel", () => BodyUnitsPanel.Create(canvas.transform));
        Safe("FleetRosterPanel", () => FleetRosterPanel.Create(canvas.transform));   // fleet > squadron > ship, with condition bars
        Safe("FleetCommandBar", () => FleetCommandBar.Create(canvas.transform));    // formation, protocol, patrol, rally, roster
        // "Around Homeworld" (AssociatedObjectsWindow) retired at Raptok's request: its moon-hopping list
        // is superseded by the Planet View's moon tabs. Not instantiated, so it never subscribes to
        // selection and never appears. The class is left in the tree as dead code for now.
        //
        // "Colony — Homeworld" (ColonyWindow) likewise retired: its shipyard controls moved to the Planet
        // View's Orbit tab, the research-centre and society/objectives readouts to Overview, and the
        // Farm/Mine building to surface Build Mode. Not instantiated, so it never subscribes to selection
        // and never pops up. Left as dead code for now.
        //   ColonyWindow.Create(canvas.transform);
        Safe("SystemSummaryWindow", () => SystemSummaryWindow.Create(canvas.transform));
        Safe("ViewEditorWindow", () => ViewEditorWindow.Create(canvas.transform));
        Safe("ObjectVisibilityWindow", () => ObjectVisibilityWindow.Create(canvas.transform));   // Dev Mode: hide / delete anything in the galaxy
        Safe("PlanetGlobeWindow", () => PlanetGlobeWindow.Create(canvas.transform));
        // Sibling order here does not matter — the menus are created after this. Open() calls
        // SetAsLastSibling(), which is what actually puts it in front of everything.
        Safe("LoadingScreen", () => LoadingScreen.Create(canvas.transform));
        Safe("GalaxyLOD", () => GalaxyLOD.Create(canvas.transform));
        Safe("BoxSelectController", () => BoxSelectController.Create(canvas.transform));
        Safe("SettingsWindow", () => SettingsWindow.Create(canvas.transform));
        Safe("TileCatalogWindow", () => TileCatalogWindow.Create(canvas.transform));   // reference viewer for every terrain tile type
        Safe("AncientClueWindow", () => AncientClueWindow.Create(canvas.transform));   // the Vael Codex — ancient-civilisation message fragments
        Safe("AnomalyWindow", () => AnomalyWindow.Create(canvas.transform));       // study window for derelict stations and comets
        Safe("GenerationMenu", () => GenerationMenu.Create(canvas.transform));
        Safe("EscapeMenu", () => EscapeMenu.Create(canvas.transform));
        Safe("StartMenu", () => StartMenu.Create(canvas.transform));

        // Built by hand rather than through a Create, and the single most important thing in the list,
        // so it gets the same isolation as everything else.
        Safe("GameHUD", () =>
        {
            var hud = new GameObject("GameHUD").AddComponent<GameHUD>();
            hud.Build(canvas.transform);
        });

        // Per-map sky + keep habitability aligned with the current species on (re)generation.
        SystemContext.OnSystemChanged += OnSystemChanged;

        // Toggling Dev Mode re-reveals or re-fogs every world.
        GameMode.OnChanged += () => SystemContext.Visualizer?.RefreshFog();
    }

    // ============================================================================================
    // ONE BROKEN PANEL MUST NOT COST THE PLAYER THE GAME
    //
    // Init builds about forty independent things in a fixed order, and it used to build them in one
    // unguarded run. So an exception in any single one of them aborted the whole method and
    // EVERYTHING BELOW IT SILENTLY FAILED TO EXIST.
    //
    // That is not hypothetical. A duplicate VerticalLayoutGroup in FleetRosterPanel — a panel nobody
    // would call load-bearing — threw a NullReferenceException four fifths of the way down the list,
    // and took StartMenu, EscapeMenu, GenerationMenu and the entire GameHUD with it, because those
    // are created afterwards. The game booted to a starfield with a black hole in it and no way to do
    // anything, and the console blamed a layout group.
    //
    // These forty things are genuinely independent: the shipyard window does not need the research
    // window to have been built. So a failure in one is isolated, logged with the name of the culprit,
    // and the rest of the interface still comes up. The player gets a game missing one panel instead
    // of a game missing everything, and the log says exactly which panel to go and look at.
    //
    // Deliberately NOT silent. LogException keeps the full stack trace, so this hides nothing — it
    // only stops one fault from cascading into forty.
    // ============================================================================================
    static void Safe(string what, System.Action create)
    {
        try { create(); }
        catch (System.Exception e)
        {
            Debug.LogError($"GameBootstrap: {what} failed to initialise — the rest of the UI will " +
                           $"still be built, but this one is missing.");
            Debug.LogException(e);
        }
    }

    static void OnSystemChanged()
    {
        if (SpaceBackground.Instance != null)
        {
            SpaceBackground.Instance.SetSeed(DeriveSeed());
            SpaceBackground.Instance.Rebuild();
        }
        SpeciesManager.RecomputeWorld();
    }

    static int DeriveSeed()
    {
        var g = GameManager.Instance;
        int s = (g != null && g.CurrentStar != null) ? (int)g.CurrentStar.type * 131 + 7 : 7;
        if (g != null && g.CurrentBodies != null)
        {
            s += g.CurrentBodies.Count * 17;
            foreach (var b in g.CurrentBodies) s = s * 31 + Mathf.RoundToInt(b.terrainSeed);
        }
        return s & 0x7fffffff;
    }
}
