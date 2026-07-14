using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Top command bar. Opens tool windows and the pause menu, controls simulation speed via a slider +
// pause/play (which remembers the last speed), toggles camera-follow on the selection, and shows the
// current species / research points / speed.
public class GameHUD : MonoBehaviour
{
    TMP_Text statusText;
    TMP_Text speedReadout;
    Button orbitBtn, terrainBtn, detailedBtn, followBtn, devBtn;
    Slider timeSlider;
    bool suppress;

    public void Build(Transform canvas)
    {
        var bar = UIFactory.Panel(canvas, "HUDBar", UITheme.HeaderBg);
        var brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1);
        brt.pivot = new Vector2(0.5f, 1); brt.sizeDelta = new Vector2(0, 40); brt.anchoredPosition = Vector2.zero;

        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 5, 5); h.spacing = 5;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        BarButton(bar.transform, "Menu", 66, () => EscapeMenu.Instance?.Toggle());
        BarButton(bar.transform, "Species", 72, () => SpeciesWindow.Instance?.Toggle());
        BarButton(bar.transform, "Ore Codex", 84, () => ResearchWindow.Instance?.Toggle());
        BarButton(bar.transform, "Notices", 70, () => NotificationManager.Instance?.ToggleHistory());
        BarButton(bar.transform, "Zone", 56, () => SystemContext.Zone?.Toggle());
        orbitBtn = BarButton(bar.transform, "Orbit", 58, () => OrbitControlPanel.Instance?.Toggle());
        terrainBtn = BarButton(bar.transform, "Terrain", 68, () => TerrainControlPanel.Instance?.Toggle());
        detailedBtn = BarButton(bar.transform, "Map", 52, OpenDetailed);
        followBtn = BarButton(bar.transform, "Follow", 66, ToggleFollow);
        BarButton(bar.transform, "Fleet", 56, () => FleetWindow.Instance?.Toggle());
        BarButton(bar.transform, "Build", 56, () => ShipyardWindow.Instance?.Toggle());
        BarButton(bar.transform, "Terraform", 84, () => TerraformWindow.Instance?.Toggle());
        BarButton(bar.transform, "Inspect", 68, () => InspectorWindow.Instance?.Toggle());
        BarButton(bar.transform, "Surface", 72, () => PlanetViewWindow.Instance?.Toggle());
        // Pull all the way back to the whole generated map in one click (Home also does it).
        BarButton(bar.transform, "Galaxy", 64, () => CameraController.Instance?.ViewWholeGalaxy());
        devBtn = BarButton(bar.transform, "Dev: OFF", 78, () => GameMode.Toggle());

        Spacer(bar.transform, 10);
        BarButton(bar.transform, "Pause", 56, () => TimeControl.TogglePause());
        // Inline time slider.
        var slHolder = UIFactory.NewUI(bar.transform, "TimeSlider");
        var le = slHolder.AddComponent<LayoutElement>(); le.preferredWidth = 130; le.minWidth = 130;
        timeSlider = UIFactory.Slider(slHolder.transform, 0f, TimeControl.Max, 1f, v => { if (!suppress) TimeControl.Set(v); });
        UIFactory.Stretch(timeSlider.GetComponent<RectTransform>(), 0, 0, 6, 6);
        speedReadout = UIFactory.Text(bar.transform, "1.0x", UITheme.SmallSize, UITheme.Accent, TextAlignmentOptions.Center);
        var sle = speedReadout.gameObject.AddComponent<LayoutElement>(); sle.preferredWidth = 40; sle.minWidth = 40;

        statusText = UIFactory.Text(bar.transform, "", UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.Right);
        var stle = statusText.gameObject.AddComponent<LayoutElement>();
        stle.flexibleWidth = 1; stle.minWidth = 150;

        PlanetUI.OnBodySelected += HandleSelected;
        PlanetUI.OnClosed += UpdateContext;
        SpeciesManager.OnSpeciesChanged += UpdateStatus;
        ResearchManager.OnChanged += UpdateStatus;
        PlayerEconomy.OnChanged += UpdateStatus;
        TimeControl.OnChanged += UpdateSpeed;
        GameMode.OnChanged += UpdateContext;

        UpdateContext();
        UpdateStatus();
        UpdateSpeed();
    }

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
        var b = PlanetUI.Selected;
        if (b == null) return;
        if (!b.Surveyed)
        {
            SimpleAudio.Instance?.PlayNotify(NotifKind.Info);
            NotificationManager.Instance?.Push($"{b.name} not surveyed",
                "Send a ship to survey this world to unlock its detailed map.", null, NotifKind.Info);
            return;
        }
        DetailedSurfaceWindow.Instance?.Open(b);
    }

    void ToggleFollow()
    {
        var cam = CameraController.Instance;
        bool newFollow = !(cam != null && cam.IsFollowing);
        CameraController.AutoFollow = newFollow;   // persists as the default for future selections
        var sel = PlanetUI.Selected;
        if (cam != null && sel != null && sel.visualObject != null)
        {
            if (newFollow) cam.FocusAndZoom(sel.visualObject.transform, sel.surfaceSize, true);
            else cam.ClearFocus();
        }
        UpdateContext();
    }

    void HandleSelected(CelestialBody b) => UpdateContext();

    void UpdateContext()
    {
        bool has = PlanetUI.Selected != null;
        bool dev = GameMode.DevMode;
        // Orbit / terrain editors are Dev-Mode sandbox tools.
        if (orbitBtn != null) orbitBtn.interactable = has && dev;
        if (terrainBtn != null) terrainBtn.interactable = has && dev;
        if (devBtn != null)
        {
            var lbl = devBtn.GetComponentInChildren<TMP_Text>();
            if (lbl != null) lbl.text = dev ? "DEV MODE" : "Dev: OFF";
            var c = devBtn.colors;
            c.normalColor = dev ? new Color(0.5f, 0.25f, 0.1f) : UITheme.ButtonBg;
            devBtn.colors = c;
        }
        if (detailedBtn != null) detailedBtn.interactable = has;
        if (followBtn != null)
        {
            followBtn.interactable = has;
            var lbl = followBtn.GetComponentInChildren<TMP_Text>();
            if (lbl != null) lbl.text = (CameraController.Instance != null && CameraController.Instance.IsFollowing) ? "Unfollow" : "Follow";
        }
        UpdateStatus();
    }

    void UpdateStatus()
    {
        if (statusText == null) return;
        statusText.text = $"{PlayerEconomy.Summary()}   ·   <b>{SpeciesManager.Current.name}</b>   ·   RP <b>{ResearchManager.ResearchPoints}</b>";
    }

    void UpdateSpeed()
    {
        if (speedReadout != null) speedReadout.text = TimeControl.IsPaused ? "Paused" : $"{Time.timeScale:0.#}x";
        if (timeSlider != null) { suppress = true; timeSlider.value = Time.timeScale; suppress = false; }
    }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= HandleSelected;
        PlanetUI.OnClosed -= UpdateContext;
        SpeciesManager.OnSpeciesChanged -= UpdateStatus;
        ResearchManager.OnChanged -= UpdateStatus;
        PlayerEconomy.OnChanged -= UpdateStatus;
        TimeControl.OnChanged -= UpdateSpeed;
        GameMode.OnChanged -= UpdateContext;
    }
}
