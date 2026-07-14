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
    public List<StarData> Stars { get; private set; } = new List<StarData>();
    public bool IsBlackHole { get; private set; }

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
        Stars = solarSystemGenerator.stars;
        IsBlackHole = solarSystemGenerator.isBlackHole;

        Debug.Log($"Generated {CurrentBodies.Count} bodies; centre = {(IsBlackHole ? "BLACK HOLE" : Stars.Count + "-star " + CurrentStar.type)} " +
                  $"(HZ: {(CurrentStar.hasHabitableZone ? $"{CurrentStar.hzInner:F1}-{CurrentStar.hzOuter:F1}" : "none")}).");

        Visualize();
    }

    // Used by the save system to display a loaded system.
    public void LoadSystem(List<CelestialBody> bodies, StarData star, List<StarData> stars, bool isBlackHole)
    {
        CurrentBodies = bodies;
        CurrentStar = star;
        Stars = (stars != null && stars.Count > 0) ? stars : new List<StarData> { star };
        IsBlackHole = isBlackHole;
        if (solarSystemGenerator != null)
        {
            solarSystemGenerator.currentStar = star;
            solarSystemGenerator.currentStarType = star.type;
            solarSystemGenerator.stars = Stars;
            solarSystemGenerator.isBlackHole = isBlackHole;
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
        systemVisualizer.VisualizeSystem(CurrentBodies, CurrentStar, Stars, IsBlackHole);
    }
}
