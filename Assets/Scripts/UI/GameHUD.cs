using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Top command bar. Opens every tool window, drives simulation speed, toggles the habitable zone,
// and shows the current species / research points. Built entirely at runtime.
public class GameHUD : MonoBehaviour
{
    TMP_Text statusText;
    Button orbitBtn, detailedBtn;

    public void Build(Transform canvas)
    {
        var bar = UIFactory.Panel(canvas, "HUDBar", UITheme.HeaderBg);
        var brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1);
        brt.pivot = new Vector2(0.5f, 1); brt.sizeDelta = new Vector2(0, 40); brt.anchoredPosition = Vector2.zero;

        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 5, 5); h.spacing = 6;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        BarButton(bar.transform, "New System", 96, () => GameManager.Instance?.GenerateStartingSystem());
        BarButton(bar.transform, "Save / Load", 96, () => SaveLoadMenu.Instance?.Toggle());
        BarButton(bar.transform, "Species", 74, () => SpeciesWindow.Instance?.Toggle());
        BarButton(bar.transform, "Ore Codex", 86, () => ResearchWindow.Instance?.Toggle());
        BarButton(bar.transform, "Background", 92, () => BackgroundSettingsWindow.Instance?.Toggle());
        BarButton(bar.transform, "Zone", 60, () => SystemContext.Zone?.Toggle());
        orbitBtn = BarButton(bar.transform, "Orbit", 62, () => OrbitControlPanel.Instance?.Toggle());
        detailedBtn = BarButton(bar.transform, "Detailed Map", 106, OpenDetailed);

        Spacer(bar.transform, 14);
        BarButton(bar.transform, "❚❚", 40, () => SetSpeed(0f));
        BarButton(bar.transform, "1x", 40, () => SetSpeed(1f));
        BarButton(bar.transform, "2x", 40, () => SetSpeed(2f));
        BarButton(bar.transform, "5x", 40, () => SetSpeed(5f));

        // Status readout on the right.
        statusText = UIFactory.Text(bar.transform, "", UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.Right);
        var le = statusText.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1; le.minWidth = 200;

        PlanetUI.OnBodySelected += HandleSelected;
        PlanetUI.OnClosed += UpdateContext;
        SpeciesManager.OnSpeciesChanged += UpdateStatus;
        ResearchManager.OnChanged += UpdateStatus;

        UpdateContext();
        UpdateStatus();
    }

    void HandleSelected(CelestialBody b) => UpdateContext();

    Button BarButton(Transform parent, string label, float width, System.Action onClick)
    {
        var b = UIFactory.Button(parent, label, onClick, 30);
        var le = b.GetComponent<LayoutElement>();
        le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 30;
        return b;
    }

    void Spacer(Transform parent, float width)
    {
        var go = UIFactory.NewUI(parent, "Spacer");
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.minWidth = width;
    }

    void OpenDetailed()
    {
        if (PlanetUI.Selected != null) DetailedSurfaceWindow.Instance?.Open(PlanetUI.Selected);
    }

    void SetSpeed(float v)
    {
        Time.timeScale = v;
        TimeController.timeScale = v; // keep the scene TimeController in sync (it writes every frame)
        UpdateStatus();
    }

    void UpdateContext()
    {
        bool has = PlanetUI.Selected != null;
        if (orbitBtn != null) orbitBtn.interactable = has;
        if (detailedBtn != null) detailedBtn.interactable = has;
        UpdateStatus();
    }

    void UpdateStatus()
    {
        if (statusText == null) return;
        statusText.text = $"Species: <b>{SpeciesManager.Current.name}</b>   ·   Research: <b>{ResearchManager.ResearchPoints}</b>   ·   Speed: {Time.timeScale:0.#}x";
    }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= HandleSelected;
        PlanetUI.OnClosed -= UpdateContext;
        SpeciesManager.OnSpeciesChanged -= UpdateStatus;
        ResearchManager.OnChanged -= UpdateStatus;
    }
}
