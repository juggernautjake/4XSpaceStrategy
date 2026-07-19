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
            // A bound cluster on a real barycenter model (StarCluster), not a plain shared ring:
            //   Binary  — two suns orbit their mass-split barycenter; the heavier sun rides the closer circle.
            //   Trinary — suns [0]/[1] are a close inner pair orbiting their own barycenter, and that pair
            //             (as one combined mass) plus the third sun orbit the SYSTEM barycenter.
            var layout = StarCluster.Layout(sys.stars);

            // The inner pair (if any) orbits this moving point rather than the fixed system centre.
            Transform pairCenter = pivot.transform;
            if (layout.hasPair)
            {
                var pb = new GameObject("PairBarycenter");
                pb.transform.SetParent(pivot.transform, false);
                var pbc = pb.AddComponent<OrbitController>();
                pbc.ringVisible = false;
                pbc.Setup(pivot.transform, layout.pairRadius, layout.pairSpeed);
                pbc.SetPhase(layout.pairPhase);
                pbc.SetRingVisible(false);
                pairCenter = pb.transform;
            }

            int count = Mathf.Min(sys.stars.Count, layout.orbits.Length);
            for (int i = 0; i < count; i++)
            {
                var go = CreateStarVisual(sys.stars[i], pivot.transform, sys.combinedStar);
                SetStarSystem(go, sys);
                var o = layout.orbits[i];
                Transform center = o.aboutPair ? pairCenter : pivot.transform;
                var oc = go.GetComponent<OrbitController>() ?? go.AddComponent<OrbitController>();
                oc.ringVisible = false;
                oc.Setup(center, o.radius, o.speed);
                oc.SetPhase(o.phase);
                oc.SetRingVisible(false);
            }
        }

        // --- Planets ---
        foreach (var body in sys.bodies)
        {
            GameObject visual = Instantiate(planetPrefab, systemParent);
            visual.name = body.name;
            body.visualObject = visual;
            // Size comes from OrbitSafety, which is also what reserves orbital room for it. Hardcoding
            // it here (as this used to) let the rendered size drift away from the spacing maths.
            visual.transform.localScale = Vector3.one * OrbitSafety.Scale(body);
            EnsureClickCollider(visual, 1.5f);   // generous, easy-to-hit selection target

            var click = visual.GetComponent<PlanetClick>();
            if (click != null) click.data = body;

            // Reuse the OrbitController the prefab already carries rather than ADDING a second one. The
            // planet prefab embeds an (unconfigured, parentBody-null) OrbitController; adding another left
            // TWO on the body, and GetComponent<OrbitController>() — used by the orbit-radius slider
            // (OrbitControlPanel) and by terraforming's orbit migration (TerraformManager.RescoreOrbit) —
            // returns the FIRST, i.e. the inert prefab copy. Their SetRadius calls then moved nothing (its
            // UpdatePosition early-returns on the null parent, its RedrawRing no-ops on a null ring) while
            // this appended controller silently drove the planet. One controller, configured AND fetched.
            var oc = visual.GetComponent<OrbitController>() ?? visual.AddComponent<OrbitController>();
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
                moonVisual.transform.localScale = Vector3.one * OrbitSafety.Scale(moon);
                EnsureClickCollider(moonVisual, 1.1f);

                var moonClick = moonVisual.GetComponent<PlanetClick>();
                if (moonClick != null) moonClick.data = moon;

                // Same as the planet above: reuse the prefab's controller so the one that's configured is
                // the one GetComponent later returns.
                var moc = moonVisual.GetComponent<OrbitController>() ?? moonVisual.AddComponent<OrbitController>();
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
            // Glow strength tracks the star's LIGHT INTENSITY (shared formula) so a dim red dwarf and a
            // blazing blue giant read differently AND the Dev intensity slider visibly changes the sun.
            float emK = StarDatabase.EmissionStrength(s);
            rend.material.color = s.color;
            rend.material.EnableKeyword("_EMISSION");
            rend.material.SetColor("_EmissionColor", s.color * emK);
        }
        EnsureClickCollider(star, 1.8f);

        // A star is NOT a planet. If starPrefab happens to carry a PlanetClick — which it does whenever
        // it's the planet prefab, or a variant of it — that component has no CelestialBody to point at,
        // so every click on a star logged "Planet has no data!" and then did nothing. Stars are handled
        // by StarInteraction below; drop the planet handler rather than leave a dead one to warn.
        var stray = star.GetComponent<PlanetClick>();
        if (stray != null) Destroy(stray);

        var si = star.GetComponent<StarInteraction>() ?? star.AddComponent<StarInteraction>();
        si.star = combined;   // combined cluster data (shared light/heat/HZ)
        si.member = s;         // this sun's OWN data, so the editor can target it individually

        var lightGo = new GameObject("StarLight");
        lightGo.transform.SetParent(star.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = s.color;
        light.intensity = s.lightIntensity / Mathf.Max(1, combined.starCount);
        light.range = 160f;   // local to its own system so the whole galaxy isn't over-lit

        return star;
    }

    // The visuals themselves now live in BlackHoleVisual, shared with the galaxy overview and the deep
    // view — this only adds what is specific to a black hole standing in a rendered system: something to
    // click on, and the StarInteraction that opens its Overview.
    void CreateBlackHole(Transform parent, StarData combined, StarSystemData sys)
    {
        float scale = Mathf.Max(1f, combined.visualScale);
        var root = BlackHoleVisual.Build(parent, scale, withLight: true,
                                         lightIntensity: combined.lightIntensity,
                                         clickable: true);

        var horizon = root.transform.Find("EventHorizon");
        if (horizon == null) return;

        EnsureClickCollider(horizon.gameObject, Mathf.Max(1.8f, scale * 0.6f));
        var si = horizon.gameObject.AddComponent<StarInteraction>();
        si.star = combined;
        si.system = sys;
    }

    // Ensures a body has exactly one sphere collider sized for an easy click. The collider's WORLD
    // radius is at least minWorldRadius, so even tiny moons are comfortable to select.
    static void EnsureClickCollider(GameObject go, float minWorldRadius)
    {
        var mesh = go.GetComponent<MeshCollider>();
        if (mesh != null) Destroy(mesh);
        var sc = go.GetComponent<SphereCollider>();
        if (sc == null) sc = go.AddComponent<SphereCollider>();
        float sl = Mathf.Max(0.0001f, go.transform.lossyScale.x);
        sc.center = Vector3.zero;
        sc.radius = Mathf.Max(0.5f, minWorldRadius / sl);

        // Keep it clickable when zoomed out (grows the pick radius with camera height).
        var scaler = go.GetComponent<ClickColliderScaler>() ?? go.AddComponent<ClickColliderScaler>();
        scaler.baseRadius = sc.radius;
    }
}

// Rotates its transform about local Y — used to slowly spin a black hole's accretion disc.
//
// `unscaled` opts out of Time.timeScale. Default OFF, because the things that spin in the SIMULATION —
// a planet's cloud shell, a derelict's slow tumble — are part of the world and should stop when the
// player pauses. The deep view is not: it is a map, drawn at a zoom where no simulation is visible, and
// a galaxy that freezes mid-turn while the rest of the UI keeps animating just looks broken.
public class SelfSpin : MonoBehaviour
{
    public float speed = 30f;
    public bool unscaled;

    void Update()
    {
        float dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
        transform.Rotate(0f, speed * dt, 0f, Space.Self);
    }
}
