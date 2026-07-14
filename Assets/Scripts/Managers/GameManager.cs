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

    public bool isEditMode = false;   // Toggle this to enter/exit sandbox mode

    public List<CelestialBody> CurrentBodies { get; private set; } = new List<CelestialBody>();
    public StarData CurrentStar { get; private set; }

    void Awake() { Instance = this; }

    void Start()
    {
        GenerateStartingSystem();
    }

    public void GenerateStartingSystem()
    {
        if (solarSystemGenerator == null)
        {
            Debug.LogError("Assign SolarSystemGenerator in Inspector!");
            return;
        }

        ResearchManager.NewGame();

        CurrentBodies = solarSystemGenerator.GenerateSystem();
        CurrentStar = solarSystemGenerator.currentStar;

        Debug.Log($"Generated {CurrentBodies.Count} bodies around a {CurrentStar.type}-type star " +
                  $"(HZ: {(CurrentStar.hasHabitableZone ? $"{CurrentStar.hzInner:F1}-{CurrentStar.hzOuter:F1}" : "none")}).");

        Visualize();
    }

    // Used by the save system to display a loaded system.
    public void LoadSystem(List<CelestialBody> bodies, StarData star)
    {
        CurrentBodies = bodies;
        CurrentStar = star;
        if (solarSystemGenerator != null)
        {
            solarSystemGenerator.currentStar = star;
            solarSystemGenerator.currentStarType = star.type;
        }
        Visualize();
    }

    void Visualize()
    {
        if (systemVisualizer == null)
        {
            Debug.LogWarning("SystemVisualizer not assigned!");
            return;
        }
        systemVisualizer.solarSystemGenerator = solarSystemGenerator;
        systemVisualizer.VisualizeSystem(CurrentBodies, CurrentStar);
    }
}
