using UnityEngine;

// Spawns all runtime-built UI, the HUD, the tooltip and the space background automatically when the
// scene starts — so none of it needs to be wired up in the Unity Editor. Also gives each generated
// system its own procedural sky and keeps habitability in sync with the current species.
public static class GameBootstrap
{
    static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (initialized) return;
        initialized = true;

        // Ensure a tooltip exists.
        var _ = TooltipManager.Instance;

        // Main window canvas (above the scene's own UI).
        var canvas = UIFactory.CreateCanvas("RuntimeUICanvas", 100);

        OrbitControlPanel.Create(canvas.transform);
        StarInfoPanel.Create(canvas.transform);
        ResearchWindow.Create(canvas.transform);
        SaveLoadMenu.Create(canvas.transform);
        SpeciesWindow.Create(canvas.transform);
        BackgroundSettingsWindow.Create(canvas.transform);
        DetailedSurfaceWindow.Create(canvas.transform);

        SpaceBackground.Create();
        PostFxController.Create();

        var hud = new GameObject("GameHUD").AddComponent<GameHUD>();
        hud.Build(canvas.transform);

        // Per-map sky + keep habitability aligned with the current species on (re)generation.
        SystemContext.OnSystemChanged += OnSystemChanged;
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

    // A stable seed derived from the current system, so its sky stays constant until regenerated.
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
