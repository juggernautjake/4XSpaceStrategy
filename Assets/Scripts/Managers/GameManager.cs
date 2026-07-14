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

    public void GenerateGalaxy(int systemCount, int avgPlanets)
    {
        if (solarSystemGenerator == null)
        {
            Debug.LogError("Assign SolarSystemGenerator in Inspector!");
            return;
        }

        ResearchManager.NewGame();
        EmpireTech.Reset();
        Galaxy = GalaxyGenerator.Generate(solarSystemGenerator, systemCount, avgPlanets, SpeciesManager.Current);
        FocusedSystem = Galaxy.Home;

        Debug.Log($"Generated galaxy: {Galaxy.systems.Count} systems; home = {(Galaxy.Home != null ? Galaxy.Home.name : "?")}.");
        Visualize();

        // Player economy + starting fleet (home planet is rendered by Visualize above).
        var homePlanet = FindHomePlanet();
        PlayerEconomy.NewGame(homePlanet, SpeciesManager.Current);
        UnitManager.Instance?.NewGame(homePlanet);
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
