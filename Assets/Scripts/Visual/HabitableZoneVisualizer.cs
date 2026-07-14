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

    public void Build(StarData starData, Transform starT, List<CelestialBody> systemBodies)
    {
        star = starData;
        starTransform = starT;
        bodies = systemBodies;
        Rebuild(false);
    }

    // Rebuild the band for the CURRENT species' shifted zone, preserving visibility.
    public void Refresh() => Rebuild(visible);

    // Point the zone at a different system (e.g. when the player clicks another star).
    public void Retarget(StarData starData, Transform starT, List<CelestialBody> systemBodies)
    {
        Build(starData, starT, systemBodies);
    }

    void Rebuild(bool keepVisible)
    {
        ClearRings();
        if (star == null || !star.hasHabitableZone) return;
        if (!Habitability.GetZone(star, SpeciesManager.Current, out float inner, out float outer)) return;

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
                if (oc != null) oc.SetHabitableHighlight(visible && b.isHabitable);
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
