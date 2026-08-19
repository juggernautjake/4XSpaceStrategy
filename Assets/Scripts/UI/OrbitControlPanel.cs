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
    Toggle dirT, ringT, rotationT;
    TMP_Text rotationNote;

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
        var content = UIFactory.Window(parent, "Orbit Controls", new Vector2(400, 560), out root, out titleText);
        rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = rootRT.anchorMax = new Vector2(1f, 0.5f);
        rootRT.pivot = new Vector2(1f, 0.5f);
        rootRT.anchoredPosition = new Vector2(-16, 0);

        var scroll = UIFactory.ScrollView(content, out RectTransform col);

        UIFactory.Label(col, "BODY", UITheme.SmallSize, UITheme.Accent, 18);
        // Spans the whole mass scheme: an asteroid at 0.1, an Earth at 1, the terrestrial ceiling at 4,
        // and gas giants from 10 to 40. Nothing is generated in the 4-10 gap, but the slider passes
        // through it because a dev tool that cannot express a value the classifier has an answer for is
        // a tool with a hole in it.
        sizeS   = UIFactory.LabeledSlider(col, "Mass (Earths)", 0.1f, MassRules.GasGiantMax, 1f, v => ApplyMass(v), "F1");

        UIFactory.Label(col, "ORBIT", UITheme.SmallSize, UITheme.Accent, 18);
        radiusS = UIFactory.LabeledSlider(col, "Radius (distance)", 2f, 80f, 10f, v => ApplyRadius(v), "F1");
        speedS  = UIFactory.LabeledSlider(col, "Speed (°/sec)", 0f, 30f, 8f, v => Apply(() => { oc.SetSpeed(v); current.orbitSpeed = v; }), "F1");
        phaseS  = UIFactory.LabeledSlider(col, "Start Angle (°)", 0f, 360f, 0f, v => Apply(() => { oc.SetPhase(v); current.orbitPhase = v; }), "F0");
        incS    = UIFactory.LabeledSlider(col, "Inclination (°)", -45f, 45f, 0f, v => Apply(() => { oc.SetInclination(v); current.inclination = v; }), "F0");
        eccS    = UIFactory.LabeledSlider(col, "Eccentricity", 0f, 0.6f, 0f, v => Apply(() => { oc.SetEccentricity(v); current.eccentricity = v; }), "F2");
        vertS   = UIFactory.LabeledSlider(col, "Vertical Offset", -10f, 10f, 0f, v => Apply(() => { oc.SetVerticalOffset(v); current.verticalOffset = v; }), "F1");

        // ---- ROTATION ----
        //
        // Not a cosmetic axis any more. A world's MAGNETIC FIELD is driven by how fast it turns
        // (RotationRules): stir the core fast enough and the dynamo runs, brake it and the field dies —
        // and without a field the atmosphere ceiling halves and the world starts looking like Mars. So
        // both controls here have real consequences, and the readout under them says what they are.
        UIFactory.Label(col, "ROTATION", UITheme.SmallSize, UITheme.Accent, 18);
        spinS   = UIFactory.LabeledSlider(col, "Rotation speed (°/sec)", 0f, RotationRules.MaxSpin, 10f,
            v => ApplySpin(v), "F1");
        rotationT = UIFactory.Toggle(col, "Rotation: prograde (turns the usual way)", true,
            on => Apply(() => { oc.SetSpinDirection(on ? 1 : -1); current.rotationDirection = on ? 1 : -1; RefreshRotationNote(); }));
        rotationNote = UIFactory.Label(col, "", UITheme.SmallSize, UITheme.SubText, 34);

        UIFactory.Label(col, "OPTIONS", UITheme.SmallSize, UITheme.Accent, 18);
        dirT  = UIFactory.Toggle(col, "Orbit: prograde (counter-clockwise)", true, on => Apply(() => { oc.SetDirection(on ? 1 : -1); current.orbitDirection = on ? 1 : -1; }));
        ringT = UIFactory.Toggle(col, "Show orbit ring", true, on => Apply(() => { oc.SetRingVisible(on); current.showRing = on; }));

        UIFactory.Button(col, "Realistic Speed (recompute)", RecomputeRealistic);
        UIFactory.Button(col, "Reset orbit (to original)", ResetToNaturalOrbit);

        PlanetUI.OnBodySelected += ShowFor;
        PlanetUI.OnClosed += Hide;
        root.SetActive(false);
    }

    // Re-fetch the OrbitController from the LIVE selection before every edit. Caching it once in ShowFor
    // left it pointing at a destroyed controller after any system re-visualization (Unity "fake-null"),
    // which silently no-op'd every slider while the panel stayed open — the "sliders do nothing" bug.
    // Fetching fresh here means the sliders always drive the body you're actually looking at.
    bool Ready()
    {
        if (suppress || current == null) return false;
        oc = current.visualObject != null ? current.visualObject.GetComponent<OrbitController>() : null;
        return oc != null;
    }

    void Apply(System.Action action)
    {
        if (!Ready()) return;
        action();
    }

    // Mass is now the size control: it sets the body's Mass Value, derives its grid/visual surfaceSize
    // (MassRules), and rescales the 3D mesh live. The surface grid itself rebuilds from the new size the
    // next time the Planet View opens on this body. Quantized to the Mass scheme (whole at 1+, first
    // decimal below).
    void ApplyMass(float v)
    {
        if (suppress || current == null) return;

        // Quantized to the scheme the class it lands in actually uses: gas giants come in multiples of
        // five, everything else to one decimal. Doing it here rather than rounding uniformly means the
        // slider cannot produce a mass generation never would — a 27-mass gas giant does not exist.
        float mass = v >= WorldClassifier.GasGiantMassFloor
            ? MassRules.QuantizeGiant(v)
            : Mathf.Max(0.1f, Mathf.Round(v * 10f) / 10f);

        current.mass = mass;
        current.surfaceSize = MassRules.SurfaceSize(mass);

        // Through OrbitSafety.Scale, which reads MassRules.VisualDiameter — the ONE place a body's
        // rendered size is defined, and the same one the orbit spacing reserves room from. The inline
        // `surfaceSize * 0.08` this used to be was a stale copy of a formula that has since changed
        // twice, so a mass edit here rescaled the mesh to a size nothing else in the game agreed with.
        if (current.visualObject != null)
            current.visualObject.transform.localScale = Vector3.one * OrbitSafety.Scale(current);

        // Mass drives the magnetic field's floor as well as the rotation does (a body under terrestrial
        // mass froze through and cannot run a dynamo however fast it turns), so the field has to be
        // re-derived — and its note re-read — whenever mass moves.
        current.hasMagneticField = RotationRules.GeneratesField(current);
        RefreshRotationNote();
    }

    // ============================================================================================
    // ROTATION HAS CONSEQUENCES, AND THEY ARE APPLIED HERE
    //
    // Spinning a world down past the dynamo threshold really does cost it its magnetic field, and losing
    // the field really does halve its atmosphere ceiling (AtmosphereRules) — so the air above that new
    // ceiling has to go with it, exactly as the sandbox's own magnetic-field button already does. Without
    // that the panel would let a developer create a world holding more atmosphere than its own physics
    // allows, and every readout downstream would then be describing an impossible object.
    //
    // The reverse is free and immediate: spin it back up and the field returns, the ceiling doubles, and
    // an atmosphere project can fill the headroom.
    void ApplySpin(float v)
    {
        if (!Ready()) return;
        oc.SetSpin(v);
        current.spinSpeed = v;

        bool field = RotationRules.GeneratesField(current);
        if (field != current.hasMagneticField)
        {
            current.hasMagneticField = field;
            // Losing the field halves the ceiling, so anything above the new ceiling goes with it.
            if (!field) current.atmospheres = Mathf.Min(current.atmospheres, AtmosphereRules.Ceiling(current));
        }
        RefreshRotationNote();
    }

    void RefreshRotationNote()
    {
        if (rotationNote == null) return;
        if (current == null) { rotationNote.text = ""; return; }

        bool field = current.hasMagneticField;
        string colour = field ? "#7CE38B" : "#FF8F5C";
        string verdict = field
            ? "fast enough to run a dynamo — magnetic field present"
            : $"too slow for a dynamo (needs {RotationRules.MagneticFieldSpin:0.#}°/s) — no magnetic field, atmosphere ceiling halved";
        rotationNote.text = $"<size=10>{RotationRules.Describe(current)} · <color={colour}>{verdict}</color></size>";
    }

    void ApplyRadius(float v)
    {
        if (!Ready()) return;
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

    // Dev-Mode "put this planet back where it started". Restores the orbit it generated with and snaps the
    // sliders (radius/speed) to match.
    void ResetToNaturalOrbit()
    {
        if (current == null) return;
        DevReset.ResetOrbit(current, current.hostStar);
        ShowFor(current);
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
        // Capture the generated orbit the first time we open on a body generation didn't record one for (an
        // old save, or a body made outside GalaxyGenerator), so "Reset orbit" always has something to
        // restore. Only ever set when unset, so it can't be overwritten by a dev-moved radius.
        if (body.naturalOrbitRadius <= 0f) body.naturalOrbitRadius = body.orbitRadius;
        oc = body.visualObject != null ? body.visualObject.GetComponent<OrbitController>() : null;
        if (oc == null) { Hide(); return; }

        titleText.text = $"Orbit — {body.name}";

        suppress = true;
        sizeS.value = body.mass;
        // Per-body radius range: a planet's comes from its STAR (OrbitSafety.OrbitLimits — a bigger, brighter
        // sun holds planets much further out); a moon's is a small band around its planet. The max is widened
        // to the body's current orbit so an already-far world isn't clamped inward, giving the slider the full
        // dynamic sweep from just clear of the star out to the edge of the system and back.
        if (body.parentBody == null)
        {
            OrbitSafety.OrbitLimits(body.hostStar, out float rlo, out float _);
            radiusS.minValue = rlo;
            // Max reaches only a LITTLE past the outermost planet in this system, not a big luminosity-
            // scaled distance — the request's "the max orbit should not go nearly so far out". Find the
            // system's outermost planet orbit and add a modest margin.
            float outer = body.orbitRadius;
            if (body.system != null && body.system.bodies != null)
                foreach (var b in body.system.bodies)
                    if (b != null && b.parentBody == null) outer = Mathf.Max(outer, b.orbitRadius);
            radiusS.maxValue = outer * 1.25f + 4f;
        }
        else
        {
            radiusS.minValue = OrbitSafety.DiscRadius(body.parentBody) + 0.5f;
            radiusS.maxValue = Mathf.Max(radiusS.minValue + 8f, body.orbitRadius * 1.6f);
        }
        radiusS.value = body.orbitRadius;
        speedS.value = body.orbitSpeed;
        phaseS.value = body.orbitPhase;
        incS.value = body.inclination;
        eccS.value = body.eccentricity;
        vertS.value = body.verticalOffset;
        spinS.value = body.spinSpeed;
        rotationT.isOn = body.rotationDirection >= 0;
        dirT.isOn = body.orbitDirection >= 0;
        ringT.isOn = body.showRing;
        suppress = false;

        RefreshRotationNote();
        root.SetActive(true);
    }

    public void Toggle()
    {
        if (root.activeSelf) { root.SetActive(false); return; }
        // Open from the LIVE selection, not a stale cache: ShowFor re-fetches current + oc, so the panel
        // works even when the body was selected BEFORE Dev Mode was on (ShowFor's Dev gate had returned
        // early then, leaving current null and Toggle a no-op), and it never opens onto a dead controller.
        if (PlanetUI.Selected != null) ShowFor(PlanetUI.Selected);
        else if (current != null) root.SetActive(true);
    }

    public void Hide() { if (root != null) root.SetActive(false); }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= ShowFor;
        PlanetUI.OnClosed -= Hide;
    }
}
