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

    GameObject starObject;

    // New preferred entry point: full star data drives light/heat and the habitable zone.
    public void VisualizeSystem(List<CelestialBody> bodies, StarData star)
    {
        if (planetPrefab == null || systemParent == null)
        {
            Debug.LogError("Missing references in SystemVisualizer!");
            return;
        }

        foreach (Transform child in systemParent)
            Destroy(child.gameObject);

        // === Star ===
        starObject = starPrefab != null
            ? Instantiate(starPrefab, systemParent)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        if (starObject.transform.parent != systemParent) starObject.transform.SetParent(systemParent);
        starObject.name = "Star";
        starObject.transform.localPosition = Vector3.zero;
        starObject.transform.localScale = Vector3.one * star.visualScale;

        var starRend = starObject.GetComponent<Renderer>();
        if (starRend != null)
        {
            starRend.material.color = star.color;
            starRend.material.EnableKeyword("_EMISSION");
            starRend.material.SetColor("_EmissionColor", star.color * 1.5f);
        }
        if (starObject.GetComponent<Collider>() == null)
            starObject.AddComponent<SphereCollider>();

        // Starlight (heat/light made visible).
        var lightGo = new GameObject("StarLight");
        lightGo.transform.SetParent(starObject.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = star.color;
        light.intensity = star.lightIntensity;
        light.range = 600f;

        var starInfo = starObject.GetComponent<StarInteraction>() ?? starObject.AddComponent<StarInteraction>();
        starInfo.star = star;

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
            oc.SetupFromData(starObject.transform, body);

            // Texture the globe with its own surface map + atmosphere.
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
        zone.Build(star, starObject.transform, bodies);

        // Set Zone first so OnSystemChanged handlers (species recompute) see the new zone.
        SystemContext.Zone = zone;
        SystemContext.Set(bodies, star, starObject.transform, systemParent, this);
    }

    // Back-compat overload (StarType only).
    public void VisualizeSystem(List<CelestialBody> bodies, StarType starType)
    {
        VisualizeSystem(bodies, StarDatabase.Get(starType));
    }
}
