using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The Inspector's tabs for a STAR (or a binary/ternary cluster, or a black hole).
//
//   Overview — what kind of star it is and what that means to live near
//   Zone     — its habitable band, and which of its worlds sit inside it FOR YOUR SPECIES
//   Worlds   — every body orbiting it, drillable
public partial class InspectorWindow
{
    // Surface-grid thumbnails minted for the Worlds/Zone planet lists. Tracked so we can Destroy them
    // on the next rebuild (a rebuild throws away the RawImages that referenced them) — otherwise every
    // tab switch would leak a handful of little textures.
    readonly List<Texture2D> starThumbs = new List<Texture2D>();

    // Dev star-editor sliders. Size/mass/density are interconnected (density = mass / (size/RefScale)^3),
    // so moving one programmatically moves the dependent one — starSuppress stops that write from
    // recursing back into its own callback.
    Slider starSizeS, starMassS, starDensityS, starIntS, starRS, starGS, starBS;
    bool starSuppress;
    // Which sun of a bound cluster the editor's sliders currently act on (a tab per sun). 0 for a single
    // star. Clamped to the sun count each build, so it survives switching between systems.
    int editSunIndex;

    void CollectStarTabs()
    {
        tabs.Add(new InspectorTab("Overview", BuildStarOverview));
        tabs.Add(new InspectorTab("Zone", BuildStarZone, () => target.star != null && !target.star.isBlackHole));
        tabs.Add(new InspectorTab("Worlds", BuildStarWorlds));
    }

    // A small square surface-grid image for a planet's list row — the same idea as the moon tabs in the
    // Planet View: point-filtered terrain colours, an "unexplored" placeholder until the world's been
    // visited. Square regardless of the world's grid aspect, so the list stays tidy.
    void AddStarThumb(Transform parent, CelestialBody b, float size = 42f)
    {
        var go = UIFactory.NewUI(parent, "Thumb");
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = size; le.preferredHeight = size;
        le.minWidth = size; le.minHeight = size;
        le.flexibleWidth = 0; le.flexibleHeight = 0;
        var raw = go.AddComponent<RawImage>();
        raw.raycastTarget = false;
        var tex = b != null && b.Visited ? StarSurfaceThumb(b) : null;
        if (tex != null) { raw.texture = tex; starThumbs.Add(tex); }
        else raw.color = new Color(0.12f, 0.16f, 0.22f, 1f);   // unexplored placeholder
    }

    // Render a body's surface grid to a tiny point-filtered texture using the shared terrain colours —
    // matches AssociatedObjectsWindow.SurfaceThumb so the star list and the Planet View agree.
    static Texture2D StarSurfaceThumb(CelestialBody b)
    {
        var s = b.surface;
        if (s == null || s.tiles == null || s.width <= 0 || s.height <= 0) return null;
        var tex = new Texture2D(s.width, s.height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s.width * s.height];
        for (int y = 0; y < s.height; y++)
            for (int x = 0; x < s.width; x++)
            {
                var t = s.tiles[x, y];
                Color c = t != null ? TerrainColorMap.Get(t.type) : new Color(0.10f, 0.10f, 0.12f);
                px[y * s.width + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }

    // A planet list row: [surface thumbnail] [live text] with an Inspect button. Shared by the Zone and
    // Worlds tabs so both show the little square image the request asks for.
    void StarWorldRow(Transform p, CelestialBody cap, System.Func<string> text)
    {
        var card = Card(p);

        var rowGo = UIFactory.NewUI(card, "Row");
        var h = rowGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8; h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;
        UIFactory.AddLayout(rowGo, 46f);

        AddStarThumb(rowGo.transform, cap);
        var t = UIFactory.WrapText(rowGo.transform, "", UITheme.SmallSize, UITheme.Text);
        t.raycastTarget = false;
        live.Text(t, text);

        UIFactory.Button(card, "Inspect »", () => PlanetUI.Instance?.Show(cap), 22);
    }

    void ClearStarThumbs()
    {
        foreach (var t in starThumbs) if (t != null) Destroy(t);
        starThumbs.Clear();
    }

    void BuildStarOverview(Transform p)
    {
        var s = target.star;

        Header(p, "THE STAR");
        var card = Card(p);
        UIFactory.WrapText(card, StarProse(s), UITheme.SmallSize, UITheme.Text);

        var stats = Card(p);
        Stat(stats, "Class", () => s.isBlackHole ? "Black hole" : $"{s.type}-type");
        Stat(stats, "Stars in system", () => s.starCount == 1 ? "1 (single)" : s.starCount == 2 ? "2 (binary)" : $"{s.starCount} (ternary)");
        Stat(stats, "Surface temperature", () => $"{s.temperatureK:F0} K");
        Stat(stats, "Luminosity", () => $"{s.luminosity:F2}× our sun");
        Stat(stats, "Mass", () => $"{s.mass:F2} solar masses");
        Stat(stats, "Habitable zone", () => s.hasHabitableZone ? $"{s.hzInner:F1} – {s.hzOuter:F1}" : "<color=#FF7A6E>none</color>");

        // For a bound cluster, break the combined star back into its individual suns — each one's name,
        // spectral class, mass and a colour swatch — with the system's combined mass. This is the "naming
        // and classifications" the single combined readout above otherwise hides.
        var suns = target.system != null ? target.system.stars : null;
        if (suns != null && suns.Count > 1)
        {
            Header(p, "SUNS OF THIS SYSTEM");
            var sunCard = Card(p);
            Note(sunCard, $"A {StarDatabase.SystemClass(s).ToLower()} — {suns.Count} suns bound together; their worlds feel the combined light.");
            float total = 0f;
            for (int i = 0; i < suns.Count; i++)
            {
                var sun = suns[i];
                if (sun == null) continue;
                total += sun.mass;
                string tag = i < 3 ? ((char)('A' + i)).ToString() : (i + 1).ToString();
                string hex = ColorUtility.ToHtmlStringRGB(sun.color);
                string nm = string.IsNullOrEmpty(sun.name) ? $"Star {tag}" : sun.name;
                UIFactory.WrapText(sunCard,
                    $"<color=#{hex}>•</color> <b>{nm}</b> <size=10><color=#9FB4C8>· {sun.type}-type · {sun.mass:F2} solar masses</color></size>",
                    UITheme.SmallSize, UITheme.Text);
            }
            UIFactory.WrapText(sunCard, $"<color=#9FB4C8>Combined mass:</color> <b>{total:F2}</b> solar masses",
                UITheme.SmallSize, UITheme.Text);
        }

        if (target.system != null)
        {
            Header(p, "SYSTEM");
            var sys = Card(p);
            Stat(sys, "Name", () => target.system.name);
            Stat(sys, "Owner", () =>
            {
                string hex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(target.system.owner));
                return $"<color={hex}>{FactionManager.OwnerLabel(target.system.owner)}</color>";
            });
            Stat(sys, "Bodies", () => target.system.bodies != null ? target.system.bodies.Count.ToString() : "0");
        }

        Header(p, "ACTIONS");
        // Focus is offered wherever a thing is selected, not only on right-click: having selected it,
        // "take me to it" shouldn't require finding it on the map again to right-click it.
        var focusBtn = UIFactory.Button(p, "Focus Camera", () =>
        {
            var t = StarInteraction.TransformOf(s);
            if (t != null) CameraController.Instance?.FocusAndZoom(t, t.lossyScale.x, true);
        }, 26);
        live.Button(focusBtn, () => StarInteraction.TransformOf(s) != null
            ? (true, $"Focus on {s.name}")
            : (false, "Focus — this star isn't rendered"));

        UIFactory.Button(p, "Toggle Habitable Zone Rings", () => SystemContext.Zone?.Toggle(), 26);

        // Hide/show every orbit line in this system at once — the request's "select a system's star and
        // turn all its orbits off". Not Dev-gated: it's a view preference, like the habitable-zone rings.
        var orbitBtn = UIFactory.Button(p, "Toggle all orbit rings", ToggleSystemOrbits, 26);
        live.Button(orbitBtn, () => (true, AnySystemRingOn() ? "Hide all orbit rings" : "Show all orbit rings"));

        // Dev-Mode only: put every planet and moon in this system back on the orbit it generated with,
        // undoing whatever the orbit editor moved. Matches "Dev mode bypasses the parameters, dev-off does not".
        if (GameMode.DevMode && target.system != null)
        {
            UIFactory.Button(p, "Reset system orbits (Dev)",
                () => DevReset.ResetSystem(target.system.bodies, target.star), 26);
            // Keeps dev-set radii, only pushing in the ones that would now clip the (edited) star, and
            // re-times every planet from the star's current mass. The counterpart to editing star size/mass:
            // Reset restores the GENERATED orbits, this ADAPTS the current ones to the new star.
            UIFactory.Button(p, "Fit orbits to star size/mass (Dev)",
                () => DevReset.FitOrbitsToStar(target.system.bodies, target.star), 26);
        }

        // Dev-Mode star editor: size / mass / density (interconnected) and the star's light.
        if (GameMode.DevMode && s != null && !s.isBlackHole)
            BuildStarEditor(p, s);
    }

    // ===== System-wide orbit ring visibility =====

    bool AnySystemRingOn()
    {
        foreach (var b in SystemBodies())
            if (b != null && b.parentBody == null && b.showRing) return true;
        return false;
    }

    void ToggleSystemOrbits()
    {
        bool show = !AnySystemRingOn();
        foreach (var b in SystemBodies())
        {
            if (b == null) continue;
            SetBodyRing(b, show);
            if (b.moons != null) foreach (var m in b.moons) SetBodyRing(m, show);
        }
    }

    static void SetBodyRing(CelestialBody b, bool show)
    {
        if (b == null) return;
        b.showRing = show;
        if (b.visualObject != null)
        {
            var oc = b.visualObject.GetComponent<OrbitController>();
            if (oc != null) oc.SetRingVisible(show);
        }
    }

    // ===== Dev star editor (per-sun, tabbed for bound clusters) =====

    // The suns the editor can act on: a bound cluster's individual members, or the lone star.
    List<StarData> EditableSuns()
    {
        if (target.system != null && target.system.stars != null && target.system.stars.Count > 0)
            return target.system.stars;
        var one = new List<StarData>();
        if (target.star != null) one.Add(target.star);
        return one;
    }

    // The sun the sliders currently drive.
    StarData EditSun()
    {
        var suns = EditableSuns();
        if (suns.Count == 0) return target.star;
        editSunIndex = Mathf.Clamp(editSunIndex, 0, suns.Count - 1);
        return suns[editSunIndex];
    }

    void BuildStarEditor(Transform p, StarData combined)
    {
        var suns = EditableSuns();
        editSunIndex = Mathf.Clamp(editSunIndex, 0, Mathf.Max(0, suns.Count - 1));

        Header(p, "STAR EDITOR (Dev)");

        // A tab per sun for a bound cluster — edit each one individually, live. A single star has no tabs.
        if (suns.Count > 1)
        {
            Note(p, "Bound cluster — edit each sun on its own tab. Growing one won't clip its partner; the " +
                    "cluster re-spaces live.");
            var tabsGo = UIFactory.NewUI(p, "SunTabs");
            UIFactory.AddLayout(tabsGo, 26);
            var htabs = tabsGo.AddComponent<HorizontalLayoutGroup>();
            htabs.spacing = 4; htabs.childControlWidth = true; htabs.childControlHeight = true;
            htabs.childForceExpandWidth = true; htabs.childForceExpandHeight = true;
            for (int i = 0; i < suns.Count; i++)
            {
                int idx = i;
                string tag = i < 3 ? ((char)('A' + i)).ToString() : (i + 1).ToString();
                var tb = UIFactory.Button(tabsGo.transform, $"Sun {tag}", () => { editSunIndex = idx; lastSig = null; }, 22);
                var colors = tb.colors;
                colors.normalColor = idx == editSunIndex ? UITheme.ButtonActive : UITheme.ButtonBg;
                colors.highlightedColor = colors.normalColor;
                colors.selectedColor = colors.normalColor;
                tb.colors = colors;
            }
        }

        var s = suns.Count > 0 ? suns[editSunIndex] : combined;

        Note(p, "Size, mass and density are linked: move size or mass and density follows; move density and " +
                "mass follows. Changing mass re-times this system's planets.");
        var card = Card(p);
        starSizeS    = UIFactory.LabeledSlider(card, "Size (render scale)", 1f, 20f, Mathf.Clamp(s.visualScale, 1f, 20f), ApplyStarSize, "F1");
        starMassS    = UIFactory.LabeledSlider(card, "Mass (solar)", 0.1f, 20f, Mathf.Clamp(s.mass, 0.1f, 20f), ApplyStarMass, "F2");
        starDensityS = UIFactory.LabeledSlider(card, "Density", 0.1f, 6f, Mathf.Clamp(s.density, 0.1f, 6f), ApplyStarDensity, "F2");

        Header(p, "STAR LIGHT (Dev)");
        var lcard = Card(p);
        starIntS = UIFactory.LabeledSlider(lcard, "Light intensity", 0f, 4f, Mathf.Clamp(s.lightIntensity, 0f, 4f), ApplyStarIntensity, "F2");
        starRS   = UIFactory.LabeledSlider(lcard, "Light Red",   0f, 1f, Mathf.Clamp01(s.color.r), _ => ApplyStarColor(), "F2");
        starGS   = UIFactory.LabeledSlider(lcard, "Light Green", 0f, 1f, Mathf.Clamp01(s.color.g), _ => ApplyStarColor(), "F2");
        starBS   = UIFactory.LabeledSlider(lcard, "Light Blue",  0f, 1f, Mathf.Clamp01(s.color.b), _ => ApplyStarColor(), "F2");
    }

    void ApplyStarSize(float v)
    {
        if (starSuppress) return;
        var s = EditSun(); if (s == null) return;
        s.visualScale = v;
        s.density = StarDatabase.DensityOf(s.mass, v);
        foreach (var t in StarInteraction.MembersOf(s)) if (t != null) t.localScale = Vector3.one * v;
        SetSliderQuiet(starDensityS, s.density);
        RelayoutCluster();       // a grown sun re-spaces so it can't clip its partner
        RecombineAndRetime();    // combined size/HZ/cluster reach may have shifted
    }

    void ApplyStarMass(float v)
    {
        if (starSuppress) return;
        var s = EditSun(); if (s == null) return;
        s.mass = v;
        s.density = StarDatabase.DensityOf(v, s.visualScale);
        SetSliderQuiet(starDensityS, s.density);
        RelayoutCluster();       // barycenter shifts with mass — the heavier sun rides the closer circle
        RecombineAndRetime();
    }

    void ApplyStarDensity(float v)
    {
        if (starSuppress) return;
        var s = EditSun(); if (s == null) return;
        s.density = v;
        s.mass = StarDatabase.MassFrom(v, s.visualScale);
        SetSliderQuiet(starMassS, s.mass);
        RelayoutCluster();
        RecombineAndRetime();
    }

    void ApplyStarIntensity(float v)
    {
        if (starSuppress) return;
        var s = EditSun(); if (s == null) return;
        s.lightIntensity = v;
        ApplyStarLight(s);
    }

    void ApplyStarColor()
    {
        if (starSuppress) return;
        var s = EditSun(); if (s == null) return;
        if (starRS == null || starGS == null || starBS == null) return;
        s.color = new Color(starRS.value, starGS.value, starBS.value);
        ApplyStarLight(s);
    }

    // Push ONE sun's colour and intensity onto its own material (glow) and point light — its sisters are
    // left alone. Divides the light by the system's sun count so a cluster isn't over-lit, matching
    // SystemVisualizer.CreateStarVisual.
    void ApplyStarLight(StarData s)
    {
        if (s == null) return;
        int n = (target.system != null && target.system.stars != null && target.system.stars.Count > 0)
            ? target.system.stars.Count : 1;
        float emK = StarDatabase.EmissionStrength(s);   // glow tracks intensity, so the slider is visible
        foreach (var t in StarInteraction.MembersOf(s))
        {
            if (t == null) continue;
            var rend = t.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material.color = s.color;
                rend.material.EnableKeyword("_EMISSION");
                rend.material.SetColor("_EmissionColor", s.color * emK);
            }
            var light = t.GetComponentInChildren<Light>();
            if (light != null) { light.color = s.color; light.intensity = s.lightIntensity / Mathf.Max(1, n); }
        }
    }

    // Recompute the COMBINED cluster data in place from the current per-sun stats (mass/light/HZ/cluster
    // reach), then re-time the planets, which orbit that combined mass. In place so target.star and every
    // body.hostStar reference stays valid. No-op for a single star (combined already IS the star).
    void RecombineAndRetime()
    {
        RecombineInPlace(target.star, EditableSuns());
        RecomputePlanetSpeeds();
    }

    static void RecombineInPlace(StarData combined, List<StarData> stars)
    {
        if (combined == null || stars == null || stars.Count <= 1) return;
        float lum = 0f, mass = 0f, scale = 0f;
        Color col = Color.black;
        StarData bright = stars[0];
        foreach (var s in stars)
        {
            if (s == null) continue;
            lum += s.luminosity; mass += s.mass;
            col += s.color * Mathf.Max(0.1f, s.luminosity);
            scale = Mathf.Max(scale, s.visualScale);
            if (s.luminosity > bright.luminosity) bright = s;
        }
        combined.luminosity = lum;
        combined.mass = mass;
        combined.temperatureK = bright.temperatureK;
        combined.type = bright.type;
        combined.visualScale = scale;
        combined.color = col / Mathf.Max(0.1f, lum);
        combined.lightIntensity = Mathf.Clamp(0.6f + Mathf.Sqrt(lum) * 0.25f, 0.6f, 3.5f);
        combined.density = StarDatabase.DensityOf(combined.mass, combined.visualScale);
        combined.clusterRadius = StarCluster.Layout(stars).reach;
        float sqrtL = Mathf.Sqrt(lum);
        combined.hzInner = 0.95f * sqrtL * StarDatabase.AU;
        combined.hzOuter = 1.37f * sqrtL * StarDatabase.AU;
        combined.hasHabitableZone = true;
    }

    // Re-space the suns of a bound cluster after a size/mass edit so nothing clips and the heavier sun rides
    // the closer circle — the same StarCluster layout the renderer used, pushed onto the live orbits.
    void RelayoutCluster()
    {
        var sys = target.system;
        if (sys == null || sys.stars == null || sys.stars.Count <= 1) return;
        var layout = StarCluster.Layout(sys.stars);

        if (layout.hasPair && sys.pivot != null)
        {
            var pb = sys.pivot.Find("PairBarycenter");
            var pbc = pb != null ? pb.GetComponent<OrbitController>() : null;
            if (pbc != null) { pbc.SetRadius(layout.pairRadius); pbc.SetSpeed(layout.pairSpeed); }
        }

        int count = Mathf.Min(sys.stars.Count, layout.orbits.Length);
        for (int i = 0; i < count; i++)
        {
            var o = layout.orbits[i];
            foreach (var t in StarInteraction.MembersOf(sys.stars[i]))
            {
                var oc = t != null ? t.GetComponent<OrbitController>() : null;
                if (oc != null) { oc.SetRadius(o.radius); oc.SetSpeed(o.speed); }
            }
        }
    }

    // Re-time every planet after the star's mass changed — orbital speed follows sqrt(mass)/radius.
    // Moons orbit their planet's mass, not the star's, so they're left alone.
    void RecomputePlanetSpeeds()
    {
        var s = target.star;
        if (s == null || target.system == null || target.system.bodies == null) return;
        foreach (var b in target.system.bodies)
        {
            if (b == null || b.parentBody != null) continue;
            float sp = OrbitalMechanics.PlanetAngularSpeed(s, b.orbitRadius);
            b.orbitSpeed = sp;
            if (b.visualObject != null)
            {
                var oc = b.visualObject.GetComponent<OrbitController>();
                if (oc != null) oc.SetSpeed(sp);
            }
        }
    }

    // Move a linked slider to `value` without letting its onChanged callback fire back into another
    // Apply — the value is already derived, so re-deriving from it would be circular.
    void SetSliderQuiet(Slider s, float value)
    {
        if (s == null) return;
        starSuppress = true;
        s.value = Mathf.Clamp(value, s.minValue, s.maxValue);
        starSuppress = false;
    }

    // Why this star matters, in plain language rather than a table.
    static string StarProse(StarData s)
    {
        if (s.isBlackHole)
            return "A black hole. No light, no habitable band, no world here will ever be settled by anything that needs a sun — but the physics around it is worth studying, and the gravity well is a landmark.";

        var parts = new List<string>();
        switch (s.type)
        {
            case StarType.O: parts.Add("A blue supergiant: monstrously hot, blindingly bright, and short-lived. It burns too fiercely and dies too young for life to get started around it."); break;
            case StarType.B: parts.Add("A blue-white giant — enormously luminous and violent, with a lifespan too short for a biosphere to form."); break;
            case StarType.A: parts.Add("A hot white star. Bright and fast-burning, with a habitable band pushed far out."); break;
            case StarType.F: parts.Add("A yellow-white star, hotter and brighter than our sun, with a comfortably wide habitable band."); break;
            case StarType.G: parts.Add("A yellow main-sequence star — the familiar, forgiving kind. Stable, long-lived, and kind to worlds in its band."); break;
            case StarType.K: parts.Add("An orange dwarf: cooler and dimmer than our sun but astonishingly long-lived. Its habitable band sits close in, and it will stay put for billions of years."); break;
            case StarType.M: parts.Add("A red dwarf — dim, cool, and effectively immortal. Its habitable band hugs the star, so worlds there are usually tidally locked."); break;
        }
        if (!s.hasHabitableZone) parts.Add("It has no stable habitable zone at all — anything here must be terraformed or built from scratch.");
        else if (s.hzInner < 6f) parts.Add("Its habitable band sits very close in, so worlds in it tend to be tidally locked and need their rotation fixed.");
        if (s.starCount > 1) parts.Add($"It is not alone: {s.starCount} stars orbit each other here, and their combined light is what its worlds actually feel.");
        return string.Join(" ", parts);
    }

    // The habitable band, read through the CURRENT SPECIES — the whole point being that it moves.
    void BuildStarZone(Transform p)
    {
        ClearStarThumbs();
        var s = target.star;
        var sp = SpeciesManager.Current;

        Header(p, "THE HABITABLE BAND");
        if (!s.hasHabitableZone)
        {
            UIFactory.WrapText(p, "<color=#FF7A6E>This star has no stable habitable zone.</color>", UITheme.SmallSize, UITheme.Bad);
            Note(p, "Its worlds can still be mined, studied and — with enough engineering — built on. They just won't ever be naturally livable.");
            return;
        }

        var card = Card(p);
        Stat(card, "Star's base band", () => $"{s.hzInner:F1} – {s.hzOuter:F1}");
        Stat(card, $"Band for {sp.name}", () =>
            Habitability.GetZone(s, sp, out float inner, out float outer)
                ? $"<color=#4DFF6E>{inner:F1} – {outer:F1}</color>"
                : "none");
        Note(card, $"{sp.name} shift the band themselves: they prefer {TempWord(sp.idealTemp)} worlds, so their comfortable band sits " +
                   $"{(sp.idealTemp > 0.55f ? "closer in" : sp.idealTemp < 0.45f ? "further out" : "near the middle")} than the star's raw figures suggest. " +
                   $"Their tolerance ({sp.tolerance:0.00}×) sets how wide it is.");

        Header(p, "WHICH WORLDS SIT IN IT");
        var bodies = SystemBodies();
        if (bodies.Count == 0) { Note(p, "No known bodies orbit this star."); return; }

        foreach (var b in bodies)
        {
            if (b.parentBody != null) continue;   // moons follow their planet
            var cap = b;
            StarWorldRow(p, cap, () =>
            {
                bool inZone = Habitability.InZone(s, SpeciesManager.Current, cap.distanceFromStar);
                string mark = inZone ? "<color=#4DFF6E>• in band</color>" : "<color=#9FB4C8>· outside</color>";
                return $"{mark}  <b>{cap.name}</b>  <size=10><color=#9FB4C8>{TerraformDiagnosis.Pretty(cap.type)} · " +
                       $"distance {cap.distanceFromStar:F1} · hab <color={Habitability.ScoreColorHex(cap.habitability)}>{cap.habitability:F0}%</color></color></size>";
            });
        }
    }

    static string TempWord(float idealTemp)
        => idealTemp > 0.7f ? "scorching" : idealTemp > 0.55f ? "warm" : idealTemp < 0.3f ? "frigid" : idealTemp < 0.45f ? "cool" : "temperate";

    void BuildStarWorlds(Transform p)
    {
        ClearStarThumbs();
        Header(p, "WORLDS OF THIS SYSTEM");
        var bodies = SystemBodies();
        if (bodies.Count == 0) { Note(p, "No known bodies orbit this star."); return; }

        foreach (var b in bodies)
        {
            if (b.parentBody != null) continue;
            var cap = b;
            StarWorldRow(p, cap, () =>
            {
                string owner = cap.owner != null
                    ? $"<color=#{ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(cap.owner))}>{FactionManager.OwnerLabel(cap.owner)}</color>"
                    : "<color=#9FB4C8>unclaimed</color>";
                string surveyed = cap.Surveyed ? "" : $"  <color=#FFBF4D>(unsurveyed — {cap.explorationProgress * 100f:F0}%)</color>";
                int moons = cap.moons != null ? cap.moons.Count : 0;
                int ships = cap.units != null ? cap.units.Count : 0;
                return $"<b>{cap.name}</b>{surveyed}\n" +
                       $"<size=10><color=#9FB4C8>{TerraformDiagnosis.Pretty(cap.type)} · {owner} · " +
                       $"hab <color={Habitability.ScoreColorHex(cap.habitability)}>{cap.habitability:F0}%</color> · " +
                       $"{moons} moon(s) · {ships} ship(s)</color></size>";
            });
        }
    }

    // The bodies belonging to this star: the focused system's own list when we have it, otherwise the
    // current system context.
    List<CelestialBody> SystemBodies()
    {
        if (target.system != null && target.system.bodies != null) return target.system.bodies;
        return SystemContext.Bodies ?? new List<CelestialBody>();
    }
}
