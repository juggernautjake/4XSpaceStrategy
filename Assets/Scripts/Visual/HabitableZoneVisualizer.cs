using System.Collections.Generic;
using UnityEngine;

// Draws the star's Goldilocks (habitable) zone as a green band and, while shown, puts a green ring
// around every body that sits inside it. Toggled from the star info panel / "Show Habitable Zone".
public class HabitableZoneVisualizer : MonoBehaviour
{
    StarData star;
    Transform starTransform;
    List<CelestialBody> bodies;
    readonly List<LineRenderer> rings = new List<LineRenderer>();
    bool visible;

    const int Segments = 128;
    const int BandRings = 7; // faint concentric rings that read as a filled green band

    public bool IsVisible => visible;
    public bool HasZone => star != null && star.hasHabitableZone;

    /// Draw the CURRENT SPECIES' band instead of the star's liquid-water band (2026-10-09). On only while a
    /// new game's starting world is being chosen — "bring the player to the starting solar system with
    /// the species specific habitable zone turned on" — because that is the one moment the question is
    /// "where could MY people live". Everywhere else the band stays a fact about the star.
    bool speciesMode;
    public bool SpeciesMode => speciesMode;

    public void SetSpeciesMode(bool on)
    {
        if (speciesMode == on) return;
        speciesMode = on;
        Rebuild(visible);
    }

    bool InBand(CelestialBody b)
    {
        if (speciesMode && SpeciesManager.Current != null)
            return Habitability.InZone(star, SpeciesManager.Current, b.distanceFromStar);
        return StarDatabase.InZone(star, b.distanceFromStar);
    }

    public void Build(StarData starData, Transform starT, List<CelestialBody> systemBodies)
    {
        star = starData;
        starTransform = starT;
        bodies = systemBodies;
        Rebuild(false);
    }

    // Rebuild the band, preserving visibility.
    public void Refresh() => Rebuild(visible);

    // Point the zone at a different system (e.g. when the player clicks another star).
    public void Retarget(StarData starData, Transform starT, List<CelestialBody> systemBodies)
    {
        // The SAME star again (clicking the sun you are already looking at) keeps the band as it is —
        // it used to switch it off, which hid the species zone mid-way through choosing a homeworld.
        bool same = starData == star;
        bool keep = same && visible;
        // speciesMode is left alone: the opening switches it off itself once a world is chosen.
        star = starData;
        starTransform = starT;
        bodies = systemBodies;
        Rebuild(keep);
    }

    void Rebuild(bool keepVisible)
    {
        ClearRings();
        if (star == null || !star.hasHabitableZone) return;
        // THE STAR'S OWN LIQUID-WATER BAND, not the current species' preference. It used to draw
        // Habitability.GetZone — shifted and scaled per species — so switching species moved the green
        // band, and no species' band was the place water is actually liquid. A species' preference is
        // still reported in the readouts; the ring on the map is a fact about the star.
        float inner = star.hzInner, outer = star.hzOuter;
        if (speciesMode && SpeciesManager.Current != null &&
            Habitability.GetZone(star, SpeciesManager.Current, out float sIn, out float sOut))
        { inner = sIn; outer = sOut; }

        for (int i = 0; i < BandRings; i++)
        {
            float t = BandRings == 1 ? 0.5f : i / (float)(BandRings - 1);
            float radius = Mathf.Lerp(inner, outer, t);
            bool edge = (i == 0 || i == BandRings - 1);
            var lr = MakeRing(radius, edge ? 0.9f : 0.28f, edge ? 0.14f : 0.07f);
            rings.Add(lr);
        }
        SetVisible(keepVisible);
    }

    LineRenderer MakeRing(float radius, float alpha, float width)
    {
        var go = new GameObject("HZ_Ring");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = Segments;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        Color green = new Color(0.2f, 1f, 0.35f, alpha);
        lr.startColor = lr.endColor = green;
        lr.startWidth = lr.endWidth = width;
        // Held at a minimum on-screen width, like the orbit rings, so the band doesn't break up into a
        // gappy hairline when zoomed out.
        OrbitController.HoldScreenWidth(lr, width, width >= 0.1f ? 0.0018f : 0.0012f);
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        return lr;
    }

    public void Toggle() => SetVisible(!visible);

    public void SetVisible(bool state)
    {
        visible = state && HasZone;
        foreach (var r in rings) if (r != null) r.enabled = visible;

        if (bodies != null)
        {
            foreach (var b in Flatten(bodies))
            {
                if (b.visualObject == null) continue;
                var oc = b.visualObject.GetComponent<OrbitController>();
                // Ringed when it orbits inside the band drawn — the same physical band, so a ring is never
                // drawn round a world sitting outside the green.
                if (oc != null) oc.SetHabitableHighlight(visible && InBand(b));
            }
        }
    }

    static IEnumerable<CelestialBody> Flatten(List<CelestialBody> list)
    {
        foreach (var b in list)
        {
            yield return b;
            foreach (var m in b.moons) yield return m;
        }
    }

    void LateUpdate()
    {
        // Keep the band centred on the star (it sits at the system origin, but stay robust).
        if (starTransform != null) transform.position = starTransform.position;
    }

    void ClearRings()
    {
        foreach (var r in rings) if (r != null) Destroy(r.gameObject);
        rings.Clear();
    }
}
