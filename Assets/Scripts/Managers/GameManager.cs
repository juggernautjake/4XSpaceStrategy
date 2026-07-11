using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Generation")]
    public SolarSystemGenerator solarSystemGenerator;

    [Header("Visualization")]
    public SystemVisualizer systemVisualizer;

    [Header("Scene Setup")]
    public Transform systemParent;

    private void Start()
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

        Debug.Log("=== Generating New Solar System ===");

        var bodies = solarSystemGenerator.GenerateSystem();  // Use solarSystemGenerator

        Debug.Log($"Successfully generated {bodies.Count} celestial bodies!");

        // Log details...
        for (int i = 0; i < bodies.Count; i++)
        {
            var body = bodies[i];
            Debug.Log($"Body {i + 1}: {body.type} | Size: {body.surfaceSize} | Moons: {body.moons.Count}");
        }

        if (systemVisualizer != null)
        {
            systemVisualizer.solarSystemGenerator = solarSystemGenerator;
            systemVisualizer.VisualizeSystem(bodies, solarSystemGenerator.currentStarType);
        }
        else
        {
            Debug.LogWarning("SystemVisualizer not assigned!");
        }
    }
}