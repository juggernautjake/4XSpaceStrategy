using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Generation")]
    public SolarSystemGenerator solarSystemGenerator;

    [Header("Visualization")]
    public SystemVisualizer systemVisualizer;

    [Header("Scene Setup")]
    public Transform systemParent;

    public bool isEditMode = false;

    public Galaxy Galaxy { get; private set; }
    public StarSystemData FocusedSystem { get; private set; }

    static readonly List<CelestialBody> _empty = new List<CelestialBody>();

    // Back-compat views onto the currently-focused system.
    public List<CelestialBody> CurrentBodies => FocusedSystem != null ? FocusedSystem.bodies : _empty;
    public StarData CurrentStar => FocusedSystem != null ? FocusedSystem.combinedStar : null;
    public List<StarData> Stars => FocusedSystem != null ? FocusedSystem.stars : new List<StarData>();
    public bool IsBlackHole => FocusedSystem != null && FocusedSystem.isBlackHole;

    void Awake() { Instance = this; }

    void Start()
    {
        // Launch into the main menu instead of auto-generating a galaxy.
        if (StartMenu.Instance != null) StartMenu.Instance.Open();
        else GenerateStartingSystem();
    }

    // Default new game — a small galaxy (fallback / used by the R debug key).
    public void GenerateStartingSystem() => GenerateGalaxy(5, 4);

    /// Generate a galaxy with a loading screen, a system at a time.
    ///
    /// Same work as GenerateGalaxy, spread across frames so the bar can actually repaint. Every value it
    /// reports is a step that really completed — the generator is phased (Begin / AddSystem / Finish) for
    /// exactly this reason, rather than the screen guessing against a timer.
    ///
    /// Systems are ~70% of the budget because they are ~70% of the work: each one rolls a star, lays out
    /// its worlds, and generates a full terrain grid for every one of them.
    public void GenerateGalaxyAsync(int systemCount, int avgPlanets, System.Action onDone = null)
        => StartCoroutine(GenerateGalaxyRoutine(systemCount, avgPlanets, onDone));

    System.Collections.IEnumerator GenerateGalaxyRoutine(int systemCount, int avgPlanets, System.Action onDone)
    {
        if (solarSystemGenerator == null)
        {
            Debug.LogError("Assign SolarSystemGenerator in Inspector!");
            onDone?.Invoke();
            yield break;
        }

        var screen = LoadingScreen.Instance;
        screen?.Open("Generating the universe");
        // One frame before any work, so the screen is actually on-screen before the main thread is busy.
        yield return null;

        ResearchManager.NewGame();
        EmpireTech.Reset();
        TechManager.Reset();
        AncientLore.Reset();
        AncientClues.Reset();

        int count = GalaxyGenerator.ClampSystems(systemCount);
        var galaxy = GalaxyGenerator.Begin(solarSystemGenerator, avgPlanets);
        screen?.Report(0.03f, "Seeding " + galaxy.name);
        yield return null;

        const float SystemsShare = 0.70f;
        for (int i = 0; i < count; i++)
        {
            GalaxyGenerator.AddSystem(galaxy, solarSystemGenerator, i, count);
            screen?.Report(0.03f + SystemsShare * ((i + 1) / (float)count),
                           $"Forming star systems  {i + 1} / {count}");
            // Give the screen a couple of frames to actually animate in.
            //
            // One `yield return null` per system means one rendered frame per system, and a bar cannot
            // look smooth when it is only drawn eight times across the whole load — the fade on the dots
            // and the easing on the fill both need frames to happen in. A handful of idle frames costs a
            // few milliseconds against work measured in hundreds.
            yield return null;
            yield return null;
        }

        screen?.Report(0.78f, "Settling the home world");
        yield return null;
        GalaxyGenerator.Finish(galaxy, SpeciesManager.Current, count);

        Galaxy = galaxy;
        FocusedSystem = Galaxy.Home;

        screen?.Report(0.86f, "Lighting the stars");
        yield return null;
        Visualize();

        screen?.Report(0.94f, "Founding your empire");
        yield return null;
        var homePlanet = FindHomePlanet();
        PlayerEconomy.NewGame(homePlanet, SpeciesManager.Current);
        UnitManager.Instance?.NewGame(homePlanet);
        FactionAI.NewGame(Galaxy);

        screen?.Report(1f, "Ready");
        // Hold the full bar for a beat. Reaching 100% and vanishing in the same frame reads as a glitch
        // rather than as completion.
        yield return new WaitForSecondsRealtime(0.35f);
        screen?.Close();
        onDone?.Invoke();
    }

    public void GenerateGalaxy(int systemCount, int avgPlanets)
    {
        if (solarSystemGenerator == null)
        {
            Debug.LogError("Assign SolarSystemGenerator in Inspector!");
            return;
        }

        ResearchManager.NewGame();
        EmpireTech.Reset();
        TechManager.Reset();
        AncientLore.Reset();
        AncientClues.Reset();   // a fresh galaxy re-scatters the ten Vael fragments (SeedGalaxy, in Generate)
        Galaxy = GalaxyGenerator.Generate(solarSystemGenerator, systemCount, avgPlanets, SpeciesManager.Current);
        FocusedSystem = Galaxy.Home;

        // (Silenced to keep the console clean — this was a one-time generation confirmation.)
        // Debug.Log($"Generated galaxy: {Galaxy.systems.Count} systems; home = {(Galaxy.Home != null ? Galaxy.Home.name : "?")}.");
        Visualize();

        // Player economy + starting fleet (home planet is rendered by Visualize above).
        var homePlanet = FindHomePlanet();
        PlayerEconomy.NewGame(homePlanet, SpeciesManager.Current);
        UnitManager.Instance?.NewGame(homePlanet);

        // Seed the rival civilisations: give each non-player faction a race + personality and a homeworld,
        // from which it grows and expands on its own. After Visualize() so the seeded worlds' visuals exist.
        FactionAI.NewGame(Galaxy);
    }

    CelestialBody FindHomePlanet()
    {
        var home = Galaxy != null ? Galaxy.Home : null;
        if (home == null) return null;
        foreach (var b in home.bodies) if (b.owner == FactionManager.Player) return b;
        return home.bodies.Count > 0 ? home.bodies[0] : null;
    }

    public void LoadGalaxy(Galaxy g)
    {
        Galaxy = g;
        FocusedSystem = g.Home;
        Visualize();
    }

    public void SetFocus(StarSystemData sys) { if (sys != null) FocusedSystem = sys; }

    void Visualize()
    {
        if (systemVisualizer == null) { Debug.LogWarning("SystemVisualizer not assigned!"); return; }
        systemVisualizer.solarSystemGenerator = solarSystemGenerator;
        systemVisualizer.VisualizeGalaxy(Galaxy);
    }
}
