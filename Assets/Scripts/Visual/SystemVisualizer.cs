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
    /// Drain the stepped version. ONE implementation, two entry points — the same pattern the galaxy and
    /// terrain generators use. A second copy of this would be a second place for the galaxy's visuals to
    /// drift from the galaxy's data.
    public void VisualizeGalaxy(Galaxy galaxy)
    {
        var it = VisualizeGalaxyStepped(galaxy, null);
        while (it.MoveNext()) { }
    }

    // ============================================================================================
    // BUILDING THE GALAXY'S VISUALS, A FRAME AT A TIME
    //
    // This is the biggest single block of work in a load: a prefab per body and per moon, an ellipse
    // per orbit, and a Texture2D built per world that is drawn as itself. On a twelve-system galaxy that
    // is a few hundred instantiations and a few hundred thousand texel writes, and it all used to happen
    // between two yields — one frame, seconds long, with the loading bar frozen on whatever it last
    // reported. That freeze is the one the bar was blamed for.
    //
    // Stepped, it is the same work spread over a few dozen frames, and `report` moves the bar through it.
    //
    // THE APPEARANCE PASS IS SEPARATE, and that is the point rather than tidiness. Instantiating a body
    // is cheap; giving it its surface is not, and only some bodies get one. Doing both in RenderSystem
    // meant the expensive half was buried inside the cheap half and could not be sliced independently —
    // one system with a dozen revealed worlds was one enormous frame however finely the systems were
    // divided. So every body is built wearing its silhouette, and the worlds that have earned a face get
    // one afterwards, a few bodies per frame.
    // ============================================================================================
    public System.Collections.IEnumerator VisualizeGalaxyStepped(Galaxy galaxy, System.Action<float, string> report)
    {
        if (planetPrefab == null || systemParent == null)
        {
            Debug.LogError("Missing references in SystemVisualizer!");
            yield break;
        }
        if (galaxy == null || galaxy.systems.Count == 0) yield break;

        foreach (Transform child in systemParent)
            Destroy(child.gameObject);

        // ---- BEFORE ANY BODY IS RENDERED ----
        //
        // RenderSystem asks SystemPresence whether each world is revealed, and SystemPresence answers by
        // looking the body up in SystemContext.Galaxy. This assignment used to sit at the BOTTOM of this
        // method, so every one of those questions was asked against the PREVIOUS galaxy — or against
        // null on a fresh game — and the lookup failed for every body in the new one. The visible
        // symptom was the whole home system coming up as black spheres except the homeworld itself,
        // which is revealed by a direct owner check that needs no lookup.
        SystemContext.Galaxy = galaxy;
        SystemPresence.Invalidate();

        // Central supermassive object.
        if (galaxy.center != null)
        {
            var centerPivot = new GameObject("GalacticCenter");
            centerPivot.transform.SetParent(systemParent, false);
            centerPivot.transform.localPosition = galaxy.centerPosition;
            CreateBlackHole(centerPivot.transform, galaxy.center, null);
        }

        for (int i = 0; i < galaxy.systems.Count; i++)
        {
            RenderSystem(galaxy.systems[i]);
            // The Dev-only detection ring. Built here so it exists for every system whether or not Dev
            // Mode is on right now — it hides itself, and building it lazily on the first toggle would
            // mean the toggle had to know how to walk the galaxy.
            DetectionRingVisual.Ensure(galaxy.systems[i]);
            report?.Invoke(0.55f * (i + 1) / galaxy.systems.Count, $"Placing {galaxy.systems[i].name}");
            yield return null;
        }

        // Habitable zone for the focused (home) system.
        var focus = GameManager.Instance != null && GameManager.Instance.FocusedSystem != null
            ? GameManager.Instance.FocusedSystem : galaxy.Home;

        var zoneGo = new GameObject("HabitableZone");
        zoneGo.transform.SetParent(systemParent, false);
        var zone = zoneGo.AddComponent<HabitableZoneVisualizer>();
        zone.Build(focus.combinedStar, focus.pivot, focus.bodies);

        SystemContext.Zone = zone;
        SystemContext.Set(focus.bodies, focus.combinedStar, focus.pivot, systemParent, this);
        yield return null;

        // ---- The faces of the worlds that have one ----
        var all = new List<CelestialBody>();
        foreach (var sys in galaxy.systems)
            foreach (var b in sys.AllBodies())
                if (b != null) all.Add(b);

        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            if (b.visualObject != null && SystemPresence.Revealed(b))
            {
                var fog = b.visualObject.GetComponent<BodyFog>();
                if (fog != null) Destroy(fog);
                PlanetAppearance.Apply(b, b.visualObject);
            }

            // A handful per frame. One per frame would take three hundred frames on a big galaxy and
            // make this step longer than the generation it follows.
            if ((i & 7) == 7)
            {
                report?.Invoke(0.55f + 0.45f * (i + 1) / all.Count, "Lighting the worlds");
                yield return null;
            }
        }

        // Every visual above is brand new and knows nothing about what was concealed. Concealment lives
        // on the DATA (CelestialBody.hideReason and friends), so it survives a rebuild — but it has to be
        // pushed back at the freshly built objects, or a rare undiscovered world would be drawn in plain
        // sight the moment the galaxy is generated, and a cloaked one would reappear on every reload.
        VisibilityService.ApplyAll();
        report?.Invoke(1f, "Lighting the worlds");
    }

    // Re-applies fog / reveal to every body based on whether the empire is in its system (called when
    // Dev Mode toggles, so the reveal is correct in both directions).
    //
    // The test is SystemPresence.Revealed, not `Surveyed` — see BodyFog for why those came apart. Note
    // it has to run in BOTH directions: leaving Dev Mode has to put the silhouettes back on systems the
    // player has never been to, which the old one-way "reveal if surveyed" branch already handled and
    // which is easy to lose when rewriting this.
    public void RefreshFog()
    {
        if (SystemContext.Galaxy == null) return;
        foreach (var sys in SystemContext.Galaxy.systems)
            foreach (var b in sys.AllBodies())
            {
                if (b.visualObject == null) continue;
                var fog = b.visualObject.GetComponent<BodyFog>();
                if (SystemPresence.Revealed(b))
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
                var oc = UIFactory.Ensure<OrbitController>(go);
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
            var oc = UIFactory.Ensure<OrbitController>(visual);
            oc.SetupFromData(pivot.transform, body);
            // ALWAYS the silhouette here, even for a world that has earned its face. Giving it that face
            // means building a texture, which is the expensive half of this method — and buried here it
            // could not be sliced apart from the cheap half. VisualizeGalaxyStepped's appearance pass
            // replaces this a few bodies per frame. See the note there.
            visual.AddComponent<BodyFog>().Init(body);
            if (body.owner != null) oc.SetOwnerHighlight(FactionManager.OwnerColor(body.owner), true, Claim.IsMine(body));

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
                var moc = UIFactory.Ensure<OrbitController>(moonVisual);
                moc.SetupFromData(body.visualObject.transform, moon);
                moonVisual.AddComponent<BodyFog>().Init(moon);   // same as the planet above
                if (moon.owner != null) moc.SetOwnerHighlight(FactionManager.OwnerColor(moon.owner), true, Claim.IsMine(moon));
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

            // A STAR IS A LIGHT SOURCE, NOT A LIT SURFACE.
            //
            // Left at the renderer defaults, the star's own sphere both CAST and RECEIVED shadows. Both
            // are wrong for the same reason: the sphere is the surface of the thing emitting the light.
            // Receiving meant the star could be shaded — by a planet passing in front of it, or by its
            // own companion in a binary — so a sun would visibly go dark down one side. Casting meant a
            // star threw a shadow of itself across its own system.
            //
            // The point light below sits at the sphere's centre, so every outward-facing normal on it
            // points AWAY from that light. The star reads as bright from every angle because of its
            // emission, which is view-independent — not because anything is lighting it.
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        // ---- THE CORONA ----
        //
        // A soft additive halo around the sun, sized and faded by how luminous it is. This is the cue
        // that tells a player who has never heard of a spectral class that THIS star is a monster and
        // that one is an ember.
        //
        // Emission alone could not carry that. A star's glow is at the mercy of bloom — which has a
        // threshold, can be switched off, and does not widen as the star shrinks on screen — so zoomed
        // out to the whole system, a blue giant and a red dwarf collapsed into two dots that differed
        // only in tint. A real quad in the scene is bright at every distance and every setting.
        //
        // Parented to the star and scaled in ITS local space, so it tracks the sun's size for free; the
        // FaceCamera inside Glow keeps it turned toward the viewer at any angle.
        // Non-interactive: SpaceMaterials.Primitive strips the quad's collider, which matters here —
        // the halo is much wider than the star, and a clickable one would grab the sun whenever you
        // aimed at a planet passing near it.
        SpaceMaterials.Glow(star.transform, "Corona", StarDatabase.CoronaScale(s),
            new Color(s.color.r, s.color.g, s.color.b, StarDatabase.CoronaAlpha(s)));

        EnsureClickCollider(star, 1.8f);

        // A star is NOT a planet. If starPrefab happens to carry a PlanetClick — which it does whenever
        // it's the planet prefab, or a variant of it — that component has no CelestialBody to point at,
        // so every click on a star logged "Planet has no data!" and then did nothing. Stars are handled
        // by StarInteraction below; drop the planet handler rather than leave a dead one to warn.
        var stray = star.GetComponent<PlanetClick>();
        if (stray != null) Destroy(stray);

        var si = UIFactory.Ensure<StarInteraction>(star);
        si.star = combined;   // combined cluster data (shared light/heat/HZ)
        si.member = s;         // this sun's OWN data, so the editor can target it individually

        // The handle concealment needs. A star has no back-reference to its GameObject anywhere else —
        // bodies have CelestialBody.visualObject and this is its equivalent, set at the one place suns
        // are built.
        s.visualObject = star;

        var lightGo = new GameObject("StarLight");
        lightGo.transform.SetParent(star.transform, false);
        var light = lightGo.AddComponent<Light>();
        // POINT, so it throws light equally in every direction — a star has no facing. (A Spot would
        // light one cone of the system and leave the worlds behind it in permanent night.)
        light.type = LightType.Point;
        light.color = s.color;
        light.intensity = s.lightIntensity / Mathf.Max(1, combined.starCount);
        light.range = 160f;   // local to its own system so the whole galaxy isn't over-lit

        // NO SHADOWS FROM A SUN. Set explicitly rather than trusting the component default: a star
        // shadow-casting would mean every world in the system carving a hard black cone across its
        // neighbours, and in a binary the two suns would shadow each other. Worlds still get their own
        // day/night terminator, which is the shading that matters — that comes from the surface facing
        // toward or away from this light, not from shadow maps.
        light.shadows = LightShadows.None;

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

        // A black hole renders from combinedStar rather than from the stars list, so this is where its
        // concealment handle comes from — the ROOT, not the horizon, so hiding it takes the disc, the
        // photon ring and the halo with it rather than leaving a glowing hole where it used to be.
        combined.visualObject = root;

        var horizon = root.transform.Find("EventHorizon");
        if (horizon == null) return;

        EnsureClickCollider(horizon.gameObject, Mathf.Max(1.8f, scale * 0.6f));
        var si = horizon.gameObject.AddComponent<StarInteraction>();
        si.star = combined;
        si.system = sys;
        // sys == null is only ever the GALACTIC CORE — VisualizeGalaxy is the one caller that passes it,
        // and every black-hole SYSTEM passes its own. Recorded as a flag so the click handler never has
        // to re-derive it from a missing field. See StarInteraction.isGalacticCore.
        si.isGalacticCore = sys == null;
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
        var scaler = UIFactory.Ensure<ClickColliderScaler>(go);
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
