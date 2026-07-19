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
    private bool suppress;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (panel != null) panel.SetActive(false);
        if (applyButton != null)
            applyButton.onClick.AddListener(ApplyChanges);

        // Real-time editing: these sliders now apply live as you drag (not only on Apply).
        if (sizeSlider != null) sizeSlider.onValueChanged.AddListener(LiveSize);
        if (radiusSlider != null) radiusSlider.onValueChanged.AddListener(LiveRadius);
        if (speedSlider != null) speedSlider.onValueChanged.AddListener(LiveSpeed);
    }

    void LiveSize(float v)
    {
        if (suppress || currentBody == null) return;
        currentBody.surfaceSize = Mathf.RoundToInt(v);
        if (currentBody.visualObject != null)
        {
            bool moon = currentBody.parentBody != null;
            currentBody.visualObject.transform.localScale = Vector3.one * Mathf.Max(0.35f, v * (moon ? 0.05f : 0.08f));
        }
    }

    void LiveRadius(float v)
    {
        if (suppress || currentOrbit == null) return;
        currentOrbit.SetRadius(v);
        currentBody.orbitRadius = v;
    }

    void LiveSpeed(float v)
    {
        if (suppress || currentOrbit == null) return;
        currentOrbit.SetSpeed(v);
        currentBody.orbitSpeed = v;
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
        suppress = true; // don't let programmatic value changes fire live edits

        // Size. A moon is detected by parentBody, not by type: a moon can now roll an Ocean/Rocky/etc.
        // surface type (see RollMoonType) yet is still a small, planet-orbiting body — using type here
        // would hand a retyped moon full planet-size slider bounds. Matches LiveSize's own parentBody test.
        bool small = currentBody.parentBody != null || currentBody.type == CelestialBodyType.Asteroid;
        if (sizeSlider != null)
        {
            sizeSlider.minValue = small ? 4f : 6f;
            sizeSlider.maxValue = small ? 8f : 24f;
            sizeSlider.value = currentBody.surfaceSize;
        }

        // Radius & Speed — Read from OrbitController if available
        if (currentOrbit != null)
        {
            if (radiusSlider != null)
            {
                radiusSlider.minValue = 2.5f;
                radiusSlider.maxValue = (currentBody.parentBody != null) ? 12f : 60f;
                radiusSlider.value = currentOrbit.orbitRadius;
            }
            if (speedSlider != null)
            {
                speedSlider.minValue = 0f;
                speedSlider.maxValue = 30f;   // angular speed in deg/sec
                speedSlider.value = currentOrbit.orbitSpeed;
            }
        }
        else if (currentBody != null)
        {
            // Fallback to data on CelestialBody
            if (radiusSlider != null) radiusSlider.value = currentBody.orbitRadius;
            if (speedSlider != null) speedSlider.value = currentBody.orbitSpeed;
        }

        suppress = false;
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
        currentBody.orbitRadius = newRadius;   // <- Sync to data

        // Speed
        float newSpeed = speedSlider.value;
        currentOrbit.SetSpeed(newSpeed);
        currentBody.orbitSpeed = newSpeed;     // <- Sync to data

        currentOrbit.ForceRingRedraw();
        currentOrbit.UpdatePosition();

        RefreshAllSliders();  // Refresh after changes

        if (PlanetUI.Instance != null)
            PlanetUI.Instance.Show(currentBody);

        Debug.Log($"Applied -> {currentBody.type}: Size={newSize}, Radius={newRadius:F1}, Speed={newSpeed:F1}");
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        currentBody = null;
        currentOrbit = null;
    }
}