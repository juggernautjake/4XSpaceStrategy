using UnityEngine;
using UnityEngine.UI;

public class TerrainEditorPanel : MonoBehaviour
{
    public static TerrainEditorPanel Instance;

    void Awake()
    {
        Instance = this;
    }

    [Header("UI References")]
    public GameObject panel;                    // The whole panel GameObject
    public Slider scaleSlider;
    public Slider elevationSlider;
    public Slider moistureSlider;
    public Slider heatSlider;
    public Slider ridgeSlider;
    public Button regenerateButton;

    private CelestialBody currentBody;          // The planet we're editing

    private void Start()
    {
        // Hide the panel at the start
        if (panel != null)
            panel.SetActive(false);

        // Connect the regenerate button
        if (regenerateButton != null)
            regenerateButton.onClick.AddListener(RegenerateTerrain);
    }

    // Call this from PlanetUI when a planet is selected
    public void ShowForPlanet(CelestialBody body)
    {
        currentBody = body;

        if (panel != null)
            panel.SetActive(true);

        Debug.Log("Terrain Editor opened for: " + body.type);
    }

    public void Hide()
    {
        if (panel != null)
            panel.SetActive(false);

        currentBody = null;
    }

    // This runs when you press the Regenerate button
    private void RegenerateTerrain()
    {
        if (currentBody == null || currentBody.visualObject == null)
        {
            Debug.LogWarning("No planet selected to regenerate!");
            return;
        }

        // Get current slider values
        float noiseScale = scaleSlider != null ? scaleSlider.value : 0.08f;
        float elevationStr = elevationSlider != null ? elevationSlider.value : 1f;
        float moistureStr = moistureSlider != null ? moistureSlider.value : 1f;
        float heatStr = heatSlider != null ? heatSlider.value : 1f;
        float ridgeStr = ridgeSlider != null ? ridgeSlider.value : 1f;

        Debug.Log("Regenerating terrain with new values...");

        // Generate new surface using the slider values
        currentBody.surface = PlanetTerrainGenerator.GenerateSurfaceWithParams(
            currentBody,
            noiseScale,
            elevationStr,
            moistureStr,
            heatStr,
            ridgeStr
        );

        // Re-seed ore deposits so the mineral markers persist after a regenerate.
        OreGenerator.Populate(currentBody);

        // Refresh the grid in the UI
        if (PlanetUI.Instance != null && PlanetUI.Instance.gridVisualizer != null)
        {
            PlanetUI.Instance.gridVisualizer.ShowSurface(currentBody.surface);
        }

        Debug.Log("Terrain regenerated!");
    }
}