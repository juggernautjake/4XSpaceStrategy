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
        SimpleAudio.Create();
        ResearchTaskManager.Create();
        UnitManager.Create();               // before the token renderer subscribes to it
        ColonyManager.Create();             // colony economy, growth, terraforming, construction
        FleetMovementController.Create();
        UnitTokenRenderer.Create();
        SpaceBackground.Create();
        PostFxController.Create();

        var _ = TooltipManager.Instance; // ensure tooltip exists

        // Main window canvas (above the scene's own UI).
        var canvas = UIFactory.CreateCanvas("RuntimeUICanvas", 100);

        ObjectLabelManager.Create(canvas.transform);
        ContextMenu.Create(canvas.transform);
        NotificationManager.Create(canvas.transform);

        OrbitControlPanel.Create(canvas.transform);
        TerrainControlPanel.Create(canvas.transform);
        StarInfoPanel.Create(canvas.transform);
        ResearchWindow.Create(canvas.transform);
        SaveLoadMenu.Create(canvas.transform);
        SpeciesWindow.Create(canvas.transform);
        DetailedSurfaceWindow.Create(canvas.transform);
        UnitInfoPanel.Create(canvas.transform);
        FleetWindow.Create(canvas.transform);
        ShipyardWindow.Create(canvas.transform);
        BodyUnitsPanel.Create(canvas.transform);
        ColonyWindow.Create(canvas.transform);
        SettingsWindow.Create(canvas.transform);
        GenerationMenu.Create(canvas.transform);
        EscapeMenu.Create(canvas.transform);
        StartMenu.Create(canvas.transform);

        var hud = new GameObject("GameHUD").AddComponent<GameHUD>();
        hud.Build(canvas.transform);

        // Per-map sky + keep habitability aligned with the current species on (re)generation.
        SystemContext.OnSystemChanged += OnSystemChanged;

        // Toggling Dev Mode re-reveals or re-fogs every world.
        GameMode.OnChanged += () => SystemContext.Visualizer?.RefreshFog();
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
