using UnityEngine;
using System.Collections.Generic;

public class SystemVisualizer : MonoBehaviour
{
    public SolarSystemGenerator solarSystemGenerator;

    [Header("Prefabs")]
    public GameObject planetPrefab;
    public GameObject starPrefab;

    [Header("References")]
    public Transform systemParent;

    // Preferred entry point: renders the star cluster (1-3 suns or a black hole) at the barycenter,
    // then planets/moons orbiting it.
    public void VisualizeSystem(List<CelestialBody> bodies, StarData star, List<StarData> stars, bool isBlackHole)
    {
        if (planetPrefab == null || systemParent == null)
        {
            Debug.LogError("Missing references in SystemVisualizer!");
            return;
        }

        foreach (Transform child in systemParent)
            Destroy(child.gameObject);

        // Central pivot at the barycenter — planets orbit THIS (works for single & multi-star).
        var pivot = new GameObject("StarSystem");
        pivot.transform.SetParent(systemParent, false);
        pivot.transform.localPosition = Vector3.zero;

        if (isBlackHole)
        {
            CreateBlackHole(pivot.transform, star);
        }
        else
        {
            if (stars == null || stars.Count == 0) stars = new List<StarData> { star };
            if (stars.Count == 1)
            {
                CreateStarVisual(stars[0], pivot.transform, star);
            }
            else
            {
                float radius = 2.6f + stars.Count * 0.4f;
                for (int i = 0; i < stars.Count; i++)
                {
                    var sv = CreateStarVisual(stars[i], pivot.transform, star);
                    var oc = sv.AddComponent<OrbitController>();
                    oc.ringVisible = false;
                    oc.Setup(pivot.transform, radius, 14f);
                    oc.SetPhase(i * 360f / stars.Count);   // spread the suns around the barycenter
                    oc.SetRingVisible(false);
                }
            }
        }

        // === Planets ===
        for (int i = 0; i < bodies.Count; i++)
        {
            var body = bodies[i];
            GameObject visual = Instantiate(planetPrefab, systemParent);
            visual.name = body.name;
            body.visualObject = visual;
            visual.transform.localScale = Vector3.one * Mathf.Max(0.6f, body.surfaceSize * 0.08f);

            var click = visual.GetComponent<PlanetClick>();
            if (click != null) click.data = body;

            var oc = visual.AddComponent<OrbitController>();
            oc.SetupFromData(pivot.transform, body);

            PlanetAppearance.Apply(body, visual);
        }

        // === Moons ===
        foreach (var body in bodies)
        {
            if (body.visualObject == null) continue;
            foreach (var moon in body.moons)
            {
                moon.parentBody = body;
                GameObject moonVisual = Instantiate(planetPrefab, systemParent);
                moonVisual.name = moon.name;
                moon.visualObject = moonVisual;
                moonVisual.transform.localScale = Vector3.one * Mathf.Max(0.35f, moon.surfaceSize * 0.05f);

                var moonClick = moonVisual.GetComponent<PlanetClick>();
                if (moonClick != null) moonClick.data = moon;

                var moc = moonVisual.AddComponent<OrbitController>();
                moc.SetupFromData(body.visualObject.transform, moon);

                PlanetAppearance.Apply(moon, moonVisual);
            }
        }

        // === Habitable zone ===
        var zoneGo = new GameObject("HabitableZone");
        zoneGo.transform.SetParent(systemParent, false);
        var zone = zoneGo.AddComponent<HabitableZoneVisualizer>();
        zone.Build(star, pivot.transform, bodies);

        SystemContext.Zone = zone;
        SystemContext.Set(bodies, star, pivot.transform, systemParent, this);
    }

    // Back-compat overloads.
    public void VisualizeSystem(List<CelestialBody> bodies, StarData star)
        => VisualizeSystem(bodies, star, new List<StarData> { star }, star != null && star.isBlackHole);

    public void VisualizeSystem(List<CelestialBody> bodies, StarType starType)
        => VisualizeSystem(bodies, StarDatabase.Get(starType));

    GameObject CreateStarVisual(StarData s, Transform parent, StarData combined)
    {
        GameObject star = starPrefab != null ? Instantiate(starPrefab, parent) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        if (star.transform.parent != parent) star.transform.SetParent(parent, false);
        star.name = "Star";
        star.transform.localPosition = Vector3.zero;
        star.transform.localScale = Vector3.one * s.visualScale;

        var rend = star.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = s.color;
            rend.material.EnableKeyword("_EMISSION");
            rend.material.SetColor("_EmissionColor", s.color * 1.5f);
        }
        if (star.GetComponent<Collider>() == null) star.AddComponent<SphereCollider>();

        var si = star.GetComponent<StarInteraction>() ?? star.AddComponent<StarInteraction>();
        si.star = combined;

        var lightGo = new GameObject("StarLight");
        lightGo.transform.SetParent(star.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = s.color;
        light.intensity = s.lightIntensity / Mathf.Max(1, combined.starCount);
        light.range = 700f;

        return star;
    }

    void CreateBlackHole(Transform parent, StarData combined)
    {
        var bh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bh.name = "BlackHole";
        bh.transform.SetParent(parent, false);
        bh.transform.localPosition = Vector3.zero;
        bh.transform.localScale = Vector3.one * combined.visualScale;
        var rend = bh.GetComponent<Renderer>();
        if (rend != null) rend.material.color = new Color(0.02f, 0.02f, 0.03f);
        if (bh.GetComponent<Collider>() == null) bh.AddComponent<SphereCollider>();

        var si = bh.GetComponent<StarInteraction>() ?? bh.AddComponent<StarInteraction>();
        si.star = combined;

        // Bright accretion ring.
        var ringGo = new GameObject("Accretion");
        ringGo.transform.SetParent(bh.transform, false);
        var lr = ringGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = true; lr.positionCount = 96;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(1f, 0.5f, 0.15f, 0.9f);
        lr.startWidth = lr.endWidth = 0.25f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 96; i++)
        {
            float a = i * Mathf.PI * 2f / 96;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * 1.7f, 0f, Mathf.Sin(a) * 1.7f));
        }

        var lightGo = new GameObject("BHLight");
        lightGo.transform.SetParent(bh.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.3f);
        light.intensity = combined.lightIntensity;
        light.range = 500f;
    }
}
