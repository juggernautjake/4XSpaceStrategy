using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Real-time, clearly-labelled terrain editor for the selected body. Dragging any slider regenerates
// the surface live and refreshes BOTH the low-res grid viewer and the detailed map (and the 3D
// globe), because all three sample the body's shared terrainParams. Reset and Randomize actually
// drive the sliders and regenerate.
public class TerrainControlPanel : MonoBehaviour
{
    public static TerrainControlPanel Instance;

    GameObject root;
    RectTransform rootRT;
    TMP_Text titleText;

    CelestialBody current;
    bool suppress;

    Slider scaleS, elevS, moistS, heatS, ridgeS;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("TerrainControlPanel");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<TerrainControlPanel>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Terrain Controls", new Vector2(320, 430), out root, out titleText);
        rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = rootRT.anchorMax = new Vector2(0f, 0.5f);
        rootRT.pivot = new Vector2(0f, 0.5f);
        rootRT.anchoredPosition = new Vector2(16, -40);

        UIFactory.VerticalLayout(content, 6);

        UIFactory.Label(content, "Adjust and watch both maps update live.", UITheme.SmallSize, UITheme.SubText, 18);

        scaleS = UIFactory.LabeledSlider(content, "Feature Scale (continent size)", 0.4f, 3f, 1f, v => SetParam(0, v), "F2");
        elevS  = UIFactory.LabeledSlider(content, "Elevation (land vs water)", 0.3f, 2f, 1f, v => SetParam(1, v), "F2");
        moistS = UIFactory.LabeledSlider(content, "Moisture (dry vs lush)", 0.3f, 2f, 1f, v => SetParam(2, v), "F2");
        heatS  = UIFactory.LabeledSlider(content, "Temperature (cold vs hot)", 0.3f, 2f, 1f, v => SetParam(3, v), "F2");
        ridgeS = UIFactory.LabeledSlider(content, "Mountains (ridged terrain)", 0.3f, 2f, 1f, v => SetParam(4, v), "F2");

        UIFactory.Button(content, "Reset to Default", ResetParams, 30);
        UIFactory.Button(content, "Randomize Terrain", Randomize, 30);

        PlanetUI.OnBodySelected += ShowFor;
        PlanetUI.OnClosed += Hide;
        root.SetActive(false);
    }

    // 0=scale 1=elevation 2=moisture 3=heat 4=ridge
    void SetParam(int which, float v)
    {
        if (suppress || current == null) return;
        var p = current.terrainParams;
        switch (which)
        {
            case 0: p.scale = v; break;
            case 1: p.elevation = v; break;
            case 2: p.moisture = v; break;
            case 3: p.heat = v; break;
            case 4: p.ridge = v; break;
        }
        current.terrainParams = p;
        Regenerate();
    }

    void Regenerate()
    {
        if (current == null) return;
        current.surface = PlanetTerrainGenerator.GenerateSurface(current);
        OreGenerator.Populate(current);

        if (PlanetUI.Instance != null && PlanetUI.Instance.gridVisualizer != null && PlanetUI.Selected == current)
            PlanetUI.Instance.gridVisualizer.ShowSurface(current.surface);

        PlanetAppearance.RefreshTexture(current, current.visualObject);
        DetailedSurfaceWindow.Instance?.RefreshIfShowing(current);
    }

    void ResetParams()
    {
        if (current == null) return;
        current.terrainParams = PlanetTerrainGenerator.NoiseParams.Default;
        SyncSliders();       // drives the sliders back to 1.0 (updates their readouts)
        Regenerate();
    }

    void Randomize()
    {
        if (current == null) return;
        current.terrainSeed = Random.Range(0f, 10000f);
        Regenerate();
    }

    public void ShowFor(CelestialBody body)
    {
        if (!GameMode.DevMode) { Hide(); return; }   // sandbox tool: Dev Mode only
        current = body;
        titleText.text = $"Terrain — {body.name}";
        SyncSliders();
        root.SetActive(true);
    }

    void SyncSliders()
    {
        if (current == null) return;
        suppress = true;
        var p = current.terrainParams;
        scaleS.value = p.scale;
        elevS.value = p.elevation;
        moistS.value = p.moisture;
        heatS.value = p.heat;
        ridgeS.value = p.ridge;
        suppress = false;
    }

    public void Toggle()
    {
        if (root.activeSelf) root.SetActive(false);
        else if (current != null) root.SetActive(true);
    }

    public void Hide() { if (root != null) root.SetActive(false); }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= ShowFor;
        PlanetUI.OnClosed -= Hide;
    }
}
