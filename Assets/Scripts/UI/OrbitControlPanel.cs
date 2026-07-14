using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Real-time orbit editor for the selected body. Every control applies live to the OrbitController AND
// writes back to the CelestialBody data (so it saves and survives re-selection). "Realistic Speed"
// restores the physically-derived Kepler default. Follows the current PlanetUI selection.
public class OrbitControlPanel : MonoBehaviour
{
    public static OrbitControlPanel Instance;

    GameObject root;
    RectTransform rootRT;
    TMP_Text titleText;

    CelestialBody current;
    OrbitController oc;
    bool suppress;

    Slider sizeS, radiusS, speedS, spinS, phaseS, incS, eccS, vertS;
    Toggle dirT, ringT;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("OrbitControlPanel");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<OrbitControlPanel>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Orbit Controls", new Vector2(320, 560), out root, out titleText);
        rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = rootRT.anchorMax = new Vector2(1f, 0.5f);
        rootRT.pivot = new Vector2(1f, 0.5f);
        rootRT.anchoredPosition = new Vector2(-16, 0);

        var scroll = UIFactory.ScrollView(content, out RectTransform col);

        UIFactory.Label(col, "BODY", UITheme.SmallSize, UITheme.Accent, 18);
        sizeS   = UIFactory.LabeledSlider(col, "Size", 4, 24, 10, v => ApplySize(v), "F0");

        UIFactory.Label(col, "ORBIT", UITheme.SmallSize, UITheme.Accent, 18);
        radiusS = UIFactory.LabeledSlider(col, "Radius (distance)", 2f, 80f, 10f, v => ApplyRadius(v), "F1");
        speedS  = UIFactory.LabeledSlider(col, "Speed (°/sec)", 0f, 30f, 8f, v => Apply(() => { oc.SetSpeed(v); current.orbitSpeed = v; }), "F1");
        phaseS  = UIFactory.LabeledSlider(col, "Start Angle (°)", 0f, 360f, 0f, v => Apply(() => { oc.SetPhase(v); current.orbitPhase = v; }), "F0");
        incS    = UIFactory.LabeledSlider(col, "Inclination (°)", -45f, 45f, 0f, v => Apply(() => { oc.SetInclination(v); current.inclination = v; }), "F0");
        eccS    = UIFactory.LabeledSlider(col, "Eccentricity", 0f, 0.6f, 0f, v => Apply(() => { oc.SetEccentricity(v); current.eccentricity = v; }), "F2");
        vertS   = UIFactory.LabeledSlider(col, "Vertical Offset", -10f, 10f, 0f, v => Apply(() => { oc.SetVerticalOffset(v); current.verticalOffset = v; }), "F1");

        UIFactory.Label(col, "ROTATION", UITheme.SmallSize, UITheme.Accent, 18);
        spinS   = UIFactory.LabeledSlider(col, "Spin (°/sec)", 0f, 40f, 10f, v => Apply(() => { oc.SetSpin(v); current.spinSpeed = v; }), "F1");

        UIFactory.Label(col, "OPTIONS", UITheme.SmallSize, UITheme.Accent, 18);
        dirT  = UIFactory.Toggle(col, "Prograde (counter-clockwise)", true, on => Apply(() => { oc.SetDirection(on ? 1 : -1); current.orbitDirection = on ? 1 : -1; }));
        ringT = UIFactory.Toggle(col, "Show orbit ring", true, on => Apply(() => { oc.SetRingVisible(on); current.showRing = on; }));

        UIFactory.Button(col, "Realistic Speed (recompute)", RecomputeRealistic);

        PlanetUI.OnBodySelected += ShowFor;
        PlanetUI.OnClosed += Hide;
        root.SetActive(false);
    }

    void Apply(System.Action action)
    {
        if (suppress || current == null || oc == null) return;
        action();
    }

    void ApplySize(float v)
    {
        if (suppress || current == null) return;
        current.surfaceSize = Mathf.RoundToInt(v);
        if (current.visualObject != null)
        {
            bool moon = current.parentBody != null;
            current.visualObject.transform.localScale = Vector3.one * Mathf.Max(0.35f, v * (moon ? 0.05f : 0.08f));
        }
    }

    void ApplyRadius(float v)
    {
        if (suppress || current == null || oc == null) return;
        oc.SetRadius(v);
        current.orbitRadius = v;

        // For planets, radius IS distance from the star, so habitability updates live.
        if (current.parentBody == null)
        {
            var star = GameManager.Instance != null ? GameManager.Instance.CurrentStar : null;
            var species = SpeciesManager.Current;
            current.distanceFromStar = v;
            current.isHabitable = Habitability.InZone(star, species, v);
            current.habitability = Habitability.Rate(star, species, current.type, v);
            if (SystemContext.Zone != null && SystemContext.Zone.IsVisible)
                SystemContext.Zone.SetVisible(true); // refresh green highlights
        }
    }

    void RecomputeRealistic()
    {
        if (current == null) return;
        var star = GameManager.Instance != null ? GameManager.Instance.CurrentStar : null;
        float def = current.parentBody != null
            ? OrbitalMechanics.MoonAngularSpeed(current.parentBody, current.orbitRadius)
            : (star != null ? OrbitalMechanics.PlanetAngularSpeed(star, current.orbitRadius) : 8f);
        speedS.value = def; // triggers Apply
    }

    public void ShowFor(CelestialBody body)
    {
        if (!GameMode.DevMode) { Hide(); return; }   // sandbox tool: Dev Mode only
        current = body;
        oc = body.visualObject != null ? body.visualObject.GetComponent<OrbitController>() : null;
        if (oc == null) { Hide(); return; }

        titleText.text = $"Orbit — {body.name}";

        suppress = true;
        sizeS.value = body.surfaceSize;
        radiusS.value = body.orbitRadius;
        speedS.value = body.orbitSpeed;
        phaseS.value = body.orbitPhase;
        incS.value = body.inclination;
        eccS.value = body.eccentricity;
        vertS.value = body.verticalOffset;
        spinS.value = body.spinSpeed;
        dirT.isOn = body.orbitDirection >= 0;
        ringT.isOn = body.showRing;
        suppress = false;

        root.SetActive(true);
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
