using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SandboxEditorPanel : MonoBehaviour
{
    public static SandboxEditorPanel Instance;

    [Header("UI References")]
    public GameObject panel;

    [Header("Sliders")]
    public Slider sizeSlider;
    public Slider radiusSlider;
    public Slider speedSlider;

    [Header("Value Labels")]
    public TMP_Text sizeLabel;
    public TMP_Text radiusLabel;
    public TMP_Text speedLabel;

    public Button applyButton;

    private CelestialBody currentBody;
    private OrbitController currentOrbit;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (panel != null) panel.SetActive(false);
        if (applyButton != null)
            applyButton.onClick.AddListener(ApplyChanges);
    }

    public void ShowForBody(CelestialBody body)
    {
        currentBody = body;
        currentOrbit = null;

        if (body.visualObject != null)
        {
            currentOrbit = body.visualObject.GetComponent<OrbitController>();

            if (currentOrbit == null)
            {
                Debug.LogWarning("No OrbitController found on visualObject!");
                currentOrbit = body.visualObject.GetComponentInChildren<OrbitController>(true);
            }

            // Force sync from CelestialBody data to controller
            if (currentOrbit != null)
            {
                if (body.orbitRadius > 0) currentOrbit.orbitRadius = body.orbitRadius;
                if (body.orbitSpeed > 0) currentOrbit.orbitSpeed = body.orbitSpeed;

                // Restore parent
                if (body.parentBody != null && body.parentBody.visualObject != null)
                {
                    currentOrbit.RestoreParent(body.parentBody.visualObject.transform);
                }
            }
        }

        if (panel != null) panel.SetActive(true);
        RefreshAllSliders();
    }

    private void RefreshAllSliders()
    {
        if (currentBody == null) return;

        // Size
        if (sizeSlider != null)
        {
            sizeSlider.minValue = (currentBody.type == CelestialBodyType.Moon || currentBody.type == CelestialBodyType.Asteroid) ? 4f : 6f;
            sizeSlider.maxValue = (currentBody.type == CelestialBodyType.Moon || currentBody.type == CelestialBodyType.Asteroid) ? 8f : 24f;
            sizeSlider.value = currentBody.surfaceSize;
        }

        // Radius & Speed — Read from OrbitController if available
        if (currentOrbit != null)
        {
            if (radiusSlider != null)
            {
                radiusSlider.minValue = 2.5f;
                radiusSlider.maxValue = (currentBody.type == CelestialBodyType.Moon) ? 12f : 60f;
                radiusSlider.value = currentOrbit.orbitRadius;
            }
            if (speedSlider != null)
            {
                speedSlider.minValue = 10f;
                speedSlider.maxValue = 120f;
                speedSlider.value = currentOrbit.orbitSpeed;
            }
        }
        else if (currentBody != null)
        {
            // Fallback to data on CelestialBody
            if (radiusSlider != null) radiusSlider.value = currentBody.orbitRadius;
            if (speedSlider != null) speedSlider.value = currentBody.orbitSpeed;
        }
    }

    public void ApplyChanges()
    {
        if (currentBody == null || currentOrbit == null)
        {
            Debug.LogWarning("No valid body or OrbitController!");
            return;
        }

        // Size
        float newSize = sizeSlider.value;
        currentBody.surfaceSize = Mathf.RoundToInt(newSize);
        if (currentBody.visualObject != null)
            currentBody.visualObject.transform.localScale = Vector3.one * Mathf.Max(0.4f, newSize * 0.08f);

        // Radius
        float newRadius = radiusSlider.value;
        currentOrbit.SetRadius(newRadius);
        currentBody.orbitRadius = newRadius;   // ← Sync to data

        // Speed
        float newSpeed = speedSlider.value;
        currentOrbit.SetSpeed(newSpeed);
        currentBody.orbitSpeed = newSpeed;     // ← Sync to data

        currentOrbit.ForceRingRedraw();
        currentOrbit.UpdatePosition();

        RefreshAllSliders();  // Refresh after changes

        if (PlanetUI.Instance != null)
            PlanetUI.Instance.Show(currentBody);

        Debug.Log($"Applied → {currentBody.type}: Size={newSize}, Radius={newRadius:F1}, Speed={newSpeed:F1}");
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        currentBody = null;
        currentOrbit = null;
    }
}