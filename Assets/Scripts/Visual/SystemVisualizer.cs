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

    // Renders the whole galaxy: a central object plus every star system at its (static) galaxy
    // position, each with its own orbiting bodies.
    public void VisualizeGalaxy(Galaxy galaxy)
    {
        if (planetPrefab == null || systemParent == null)
        {
            Debug.LogError("Missing references in SystemVisualizer!");
            return;
        }
        if (galaxy == null || galaxy.systems.Count == 0) return;

        foreach (Transform child in systemParent)
            Destroy(child.gameObject);

        // Central supermassive object.
        if (galaxy.center != null)
        {
            var centerPivot = new GameObject("GalacticCenter");
            centerPivot.transform.SetParent(systemParent, false);
            centerPivot.transform.localPosition = galaxy.centerPosition;
            CreateBlackHole(centerPivot.transform, galaxy.center, null);
        }

        foreach (var sys in galaxy.systems)
            RenderSystem(sys);

        // Habitable zone for the focused (home) system.
        var focus = GameManager.Instance != null && GameManager.Instance.FocusedSystem != null
            ? GameManager.Instance.FocusedSystem : galaxy.Home;

        var zoneGo = new GameObject("HabitableZone");
        zoneGo.transform.SetParent(systemParent, false);
        var zone = zoneGo.AddComponent<HabitableZoneVisualizer>();
        zone.Build(focus.combinedStar, focus.pivot, focus.bodies);

        SystemContext.Galaxy = galaxy;
        SystemContext.Zone = zone;
        SystemContext.Set(focus.bodies, focus.combinedStar, focus.pivot, systemParent, this);
    }

    // Re-applies fog / reveal to every body based on current Surveyed state (called when Dev Mode
    // toggles, so the reveal is correct in both directions).
    public void RefreshFog()
    {
        if (SystemContext.Galaxy == null) return;
        foreach (var sys in SystemContext.Galaxy.systems)
            foreach (var b in sys.AllBodies())
            {
                if (b.visualObject == null) continue;
                var fog = b.visualObject.GetComponent<BodyFog>();
                if (b.Surveyed)
                {
                    if (fog != null) Destroy(fog);
                    PlanetAppearance.Apply(b, b.visualObject);
                }
                else if (fog == null)
                {
                    b.visualObject.AddComponent<BodyFog>().Init(b);
                }
            }
    }

    void RenderSystem(StarSystemData sys)
    {
        var pivot = new GameObject("System_" + sys.name);
        pivot.transform.SetParent(systemParent, false);
        pivot.transform.localPosition = sys.galaxyPosition;
        sys.pivot = pivot.transform;

        // --- Star cluster ---
        if (sys.isBlackHole)
        {
            CreateBlackHole(pivot.transform, sys.combinedStar, sys);
        }
        else if (sys.stars.Count <= 1)
        {
            var s = sys.stars.Count > 0 ? sys.stars[0] : sys.combinedStar;
            var go = CreateStarVisual(s, pivot.transform, sys.combinedStar);
            SetStarSystem(go, sys);
        }
        else
        {
            float radius = 2.6f + sys.stars.Count * 0.4f;
            for (int i = 0; i < sys.stars.Count; i++)
            {
                var go = CreateStarVisual(sys.stars[i], pivot.transform, sys.combinedStar);
                SetStarSystem(go, sys);
                var oc = go.AddComponent<OrbitController>();
                oc.ringVisible = false;
                oc.Setup(pivot.transform, radius, 14f);
                oc.SetPhase(i * 360f / sys.stars.Count);
                oc.SetRingVisible(false);
            }
        }

        // --- Planets ---
        foreach (var body in sys.bodies)
        {
            GameObject visual = Instantiate(planetPrefab, systemParent);
            visual.name = body.name;
            body.visualObject = visual;
            visual.transform.localScale = Vector3.one * Mathf.Max(0.6f, body.surfaceSize * 0.08f);

            var click = visual.GetComponent<PlanetClick>();
            if (click != null) click.data = body;

            var oc = visual.AddComponent<OrbitController>();
            oc.SetupFromData(pivot.transform, body);
            if (body.Surveyed) PlanetAppearance.Apply(body, visual);
            else visual.AddComponent<BodyFog>().Init(body);   // fog-of-war silhouette
            if (body.owner != null) oc.SetOwnerHighlight(FactionManager.OwnerColor(body.owner), true);

            // --- Moons ---
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
                if (moon.Surveyed) PlanetAppearance.Apply(moon, moonVisual);
                else moonVisual.AddComponent<BodyFog>().Init(moon);
                if (moon.owner != null) moc.SetOwnerHighlight(FactionManager.OwnerColor(moon.owner), true);
            }
        }
    }

    static void SetStarSystem(GameObject starGo, StarSystemData sys)
    {
        var si = starGo.GetComponent<StarInteraction>();
        if (si != null) si.system = sys;
    }

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
        light.range = 160f;   // local to its own system so the whole galaxy isn't over-lit

        return star;
    }

    void CreateBlackHole(Transform parent, StarData combined, StarSystemData sys)
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
        si.system = sys;

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
        light.range = 160f;
    }
}
