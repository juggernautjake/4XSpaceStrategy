using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

// Which of the four zoom levels the camera is currently in. Rendering is driven by continuous alpha
// (see GalaxyLOD), not by this enum — it exists so other code can ask "what is the player looking at".
public enum ViewTier
{
    Body,     // close on a single planet, moon or star
    System,   // one whole system: its suns, planets, moons and orbits
    Galaxy,   // every system collapsed to its star(s), with the black hole at the centre
    Deep      // the galaxy itself: one turning spiral around its core
}

// The render ladder. Four zoom levels, three boundaries, and nothing pops.
//
// WHAT CHANGED AND WHY. This used to be a single binary: above one height the entire SystemParent subtree
// went inactive on one frame and full-size star proxies appeared on the same frame. The data stayed live,
// so nothing broke, but the transition was a hard cut — and there was no level beyond it, so the widest
// zoom was just the same proxies drifting apart, with the galactic core rendering as a near-black sphere
// on a black background, i.e. invisible.
//
// THE RULE NOW: every representation has a continuous alpha derived from camera height, and a thing stays
// rendered until its alpha reaches zero. Two representations overlap through each boundary, one dissolving
// in as the other dissolves out, so there is never a frame where the screen jumps.
//
//   height ->      [ Body ]   [ System ]        [ Galaxy ]           [ Deep ]
//   detailed systems  ############################
//   star proxies                        ####################################
//   deep spiral                                            ##################
//
// A continuous crossfade needs no hysteresis. The old code needed an enter/exit gap because a binary test
// on a jittering height flickers; a fade has no discrete decision to flicker on. Activation still happens
// at a threshold, but only where alpha is ~0, so a flicker there is invisible by construction.
//
// THE ONE EXCEPTION is the detailed systems. Everything else here is built from runtime materials this
// file controls, so alpha is safe to drive. The detailed systems are textured, lit planet materials, and
// forcing several hundred of them into transparent blending to fade them out would be both expensive and
// a real risk of wrecking how they render. So detail is still a hard toggle — but it is held on until the
// proxies have finished fading IN over the top of it, which reads as a crossfade without touching a single
// planet material. See DetailShouldRender.
public class GalaxyLOD : MonoBehaviour
{
    public static GalaxyLOD Instance;

    /// The zoom level the camera is in right now.
    public static ViewTier Tier { get; private set; } = ViewTier.System;

    /// How opaque each wide representation is, 0..1. Exposed as CONTINUOUS values because anything that
    /// wants to react to the zoom-out (the backdrop dims behind the deep view) has to track the fade, not
    /// the tier — Tier flips at the hard boundary while the visuals are still halfway through blending, so
    /// keying off it makes the backdrop lurch a beat after the thing it is reacting to.
    public static float ProxyAlpha { get; private set; }
    public static float DeepAlpha { get; private set; }

    // ---- Boundaries ------------------------------------------------------------------------------
    // Derived from how big the galaxy actually is, so they hold at any galaxy size rather than being
    // tuned for one. `frame` is the height at which the whole galaxy exactly fills the screen.

    const float BodyMaxHeight = 8f;      // below this you are looking at one body, not a system

    const float GalaxyEnterFrac = 0.40f; // detailed systems -> star proxies
    const float MinGalaxyEnter = 200f;

    // The deep view opens PAST the height that frames the whole galaxy. That ordering is deliberate: the
    // "Galaxy" HUD button and the Home key both jump to exactly `frame`, and they should land you in the
    // galaxy overview looking at all your systems — not in the deep view, which shows none of them.
    //
    // 1.6, not 1.35, and the difference is load-bearing. The crossfade band reaches DOWN from the
    // boundary by BlendFrac, so the deep view starts appearing at `boundary * (1 - BlendFrac)`. At 1.35
    // that is `frame * 0.945` — BELOW frame — so pressing Home switched the deep view on at ~1% alpha and
    // hung a translucent spiral over the galaxy overview. Any value under `1 / (1 - BlendFrac)` = 1.43
    // has that bug; RecomputeBoundaries carries a floor term so tuning it back down cannot reintroduce it.
    const float DeepEnterFrac = 1.6f;

    // Crossfade width, as a fraction of the boundary it straddles. Wide enough that a normal scroll notch
    // (which scales height by ~exp(0.06) per notch) takes several notches to cross, so the blend is
    // actually seen rather than skipped through in one frame.
    const float BlendFrac = 0.30f;

    // ---- Proxy sizing ----------------------------------------------------------------------------
    //
    // A system's star must never shrink out of sight while the galaxy overview is up — that is the whole
    // point of the overview. The rule is: the proxy's WORLD size grows in proportion to camera height, so
    // its SCREEN size (roughly worldSize / height) stays put. Pull back twice as far, the star is twice as
    // big in world units and exactly as big on screen.
    //
    // The base size is small (2.4% of the galaxy radius, was 4.5%) precisely SO there is headroom to grow.
    // At 4.5% a proxy already started at ~63 units against ~95-unit system spacing, so it could barely
    // grow at all before neighbouring systems merged — the growth had to be capped almost immediately,
    // and capped growth is what let the stars shrink away at wide zoom. Starting smaller buys a much
    // longer runway before overlap becomes a problem.
    const float ProxySizeFrac = 0.024f;
    const float ProxySizeMin = 2f;

    // How far a proxy may grow. Generous, because the alternative to a big star is an invisible one.
    // In practice the crossfade to the deep view retires the proxies before this binds.
    const float ProxyMaxScale = 14f;

    Camera cam;
    Transform proxyRoot;
    readonly List<GalaxyStarProxy> proxies = new List<GalaxyStarProxy>();
    FadeGroup coreProxyFade;
    Transform coreProxy;       // the ART child — this is what the zoom ramp scales
    Transform coreProxyRoot;   // the object that owns the collider; this is what Rebuild destroys

    Transform deepRoot;
    GalaxySpiralVisual deepVisual;
    FadeGroup deepFade;

    TMP_Text galaxyTitle;
    Transform uiParent;

    Galaxy builtFor;
    bool detailOn = true;
    float proxyBaseSize = 1f;
    float coreProxyBaseSize = 1f;
    float coreMaxScale = 1f;   // capped so the horizon never reaches the nearest system

    public static void Create(Transform canvas)
    {
        if (Instance != null) return;
        var go = new GameObject("GalaxyLOD");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<GalaxyLOD>();
        Instance.uiParent = canvas;
        Instance.Init();
    }

    void Awake() { Instance = this; }

    void Init()
    {
        cam = Camera.main;

        // World-space, NOT under the canvas: these are real 3D objects at the systems' positions. Separate
        // roots from SystemParent, which is switched off underneath them.
        proxyRoot = new GameObject("GalaxyStarProxies").transform;
        proxyRoot.gameObject.SetActive(false);

        deepRoot = new GameObject("DeepGalaxyView").transform;
        deepRoot.gameObject.SetActive(false);

        BuildGalaxyTitle();
    }

    // The galaxy's name, shown only in the deep view — the one zoom level where the galaxy is the subject
    // rather than the container. Fades with the spiral it labels.
    void BuildGalaxyTitle()
    {
        if (uiParent == null) return;
        galaxyTitle = UIFactory.Text(uiParent, "", 30, UITheme.Text, TextAlignmentOptions.Center);
        if (galaxyTitle == null) return;
        galaxyTitle.fontStyle = FontStyles.Bold;
        var rt = galaxyTitle.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -70f);
        rt.sizeDelta = new Vector2(760f, 46f);
        var sh = galaxyTitle.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
        sh.effectDistance = new Vector2(1.5f, -1.5f);
        galaxyTitle.gameObject.SetActive(false);
    }

    // ---- Heights ---------------------------------------------------------------------------------

    // Cached at Rebuild. These derive from GalaxyRadius(), which loops every system, and they only change
    // when the galaxy does — recomputing them was costing three full passes over the system list per
    // frame in this class alone (Update called two accessors, one of which called the other plus Frame()
    // again). CameraController.UpdateClipPlanes still makes its own call every frame; that one is not
    // ours to cache from here.
    float cFrame, cB1, cB2;

    void RecomputeBoundaries()
    {
        var cc = CameraController.Instance;
        cFrame = cc != null ? cc.HeightToFrame(CameraController.GalaxyRadius()) : 0f;

        float b1 = Mathf.Max(MinGalaxyEnter, cFrame * GalaxyEnterFrac);
        // The floor must never push the boundary so high that framing the whole galaxy (h == frame) lands
        // mid-crossfade. In a one-system galaxy `frame` is only ~190 while MinGalaxyEnter is 200, so Home
        // would leave a 38%-opaque proxy sphere sitting on top of the still-rendered star — with its own
        // collider in front of it, stealing clicks from the star underneath.
        // The 0.999 is not decoration. `cB1 * (1 + BlendFrac)` is the upper edge of the crossfade band,
        // and ApplyDetail compares against it with a strict <. Setting cB1 to exactly cFrame/(1+BlendFrac)
        // makes that round-trip land a float ULP or two ABOVE cFrame for some radii (170 and 180 both do),
        // so at h == cFrame the comparison flips and the detailed system renders underneath an opaque
        // proxy. Shaving a thousandth off puts the edge unambiguously below cFrame at every size.
        cB1 = Mathf.Min(b1, Mathf.Max(1f, cFrame / (1f + BlendFrac) * 0.999f));

        // Kept clear of the galaxy boundary so the two crossfades never overlap — if they did, all three
        // representations would be partly visible at once and the screen would fog.
        //
        // `DeepEnterFrac` (1.6) is what actually keeps `cFrame` below the deep band: the band opens at
        // 1.6 * 0.7 = 1.12 * cFrame. The third term is a belt-and-braces floor that would take over if
        // DeepEnterFrac were ever tuned below 1/(1-BlendFrac) = 1.43; at 1.6 it never binds.
        cB2 = Mathf.Max(Mathf.Max(cB1 * 3f, cFrame * DeepEnterFrac),
                        cFrame / (1f - BlendFrac) * 1.08f);
    }

    /// 0 below the boundary, 1 above it, smoothly across the blend band.
    static float Blend(float h, float boundary)
    {
        float band = Mathf.Max(1f, boundary * BlendFrac);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(boundary - band, boundary + band, h));
    }

    void Update()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        var g = SystemContext.Galaxy;
        if (g != builtFor) Rebuild(g);
        if (g == null) return;

        float h = cam.transform.position.y;
        float b1 = cB1;
        float b2 = cB2;

        float toGalaxy = Blend(h, b1);      // 0 = detailed systems, 1 = proxies
        float toDeep = Blend(h, b2);        // 0 = proxies, 1 = deep spiral

        ProxyAlpha = toGalaxy * (1f - toDeep);
        DeepAlpha = toDeep;

        Tier = h < BodyMaxHeight ? ViewTier.Body
             : h < b1 ? ViewTier.System
             : h < b2 ? ViewTier.Galaxy
             : ViewTier.Deep;

        ApplyDetail(h, b1);
        ApplyProxies(ProxyAlpha, h, b1);
        ApplyDeep(DeepAlpha, h, b2, g);
    }

    // The detailed systems: a hard toggle, held on until the proxies have fully faded in over them.
    //
    // The upper edge of the blend band is exactly where proxyAlpha reaches 1, so detail switches off on the
    // frame the proxies stop being translucent — the swap happens underneath something already opaque.
    void ApplyDetail(float h, float b1)
    {
        bool want = h < b1 + Mathf.Max(1f, b1 * BlendFrac);
        if (want == detailOn) return;
        detailOn = want;
        if (SystemContext.SystemParent != null)
            SystemContext.SystemParent.gameObject.SetActive(want);
        if (!want && MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();
    }

    void ApplyProxies(float alpha, float h, float b1)
    {
        bool on = alpha > 0.004f;
        if (proxyRoot.gameObject.activeSelf != on)
        {
            proxyRoot.gameObject.SetActive(on);
            if (!on && MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();
        }
        if (!on) return;

        UpdateProxyScales(h, b1);
        foreach (var p in proxies) if (p != null) p.SetAlpha(alpha);
        if (coreProxyFade != null) coreProxyFade.SetAlpha(alpha);
    }

    void ApplyDeep(float alpha, float h, float b2, Galaxy g)
    {
        bool on = alpha > 0.004f;
        if (deepRoot.gameObject.activeSelf != on) deepRoot.gameObject.SetActive(on);

        if (galaxyTitle != null)
        {
            bool titleOn = on && alpha > 0.15f;
            if (galaxyTitle.gameObject.activeSelf != titleOn) galaxyTitle.gameObject.SetActive(titleOn);
            if (titleOn)
            {
                var c = UITheme.Text;
                // Ramps in over the back half of the fade, so the name arrives once the spiral is
                // legible rather than floating over an empty screen.
                c.a = Mathf.InverseLerp(0.15f, 0.6f, alpha);
                galaxyTitle.color = c;
            }
        }

        if (!on || deepVisual == null) return;

        // Grow with height so the spiral holds a roughly constant on-screen size however far you pull
        // back — the same trick the proxies use. Without it the galaxy would shrink to a dot and the
        // widest zoom would show nothing at all.
        float f = Mathf.Clamp(h / Mathf.Max(1f, b2), 1f, 40f);
        deepVisual.SetScale(f);
        if (deepFade != null) deepFade.SetAlpha(alpha);
    }

    // ---- Building --------------------------------------------------------------------------------

    void Rebuild(Galaxy g)
    {
        foreach (var m in proxies) if (m != null) Destroy(m.gameObject);
        proxies.Clear();
        // The ROOT, not `coreProxy` — which points at the Art child. Destroying the child left the root
        // alive under the never-destroyed proxyRoot, still holding its collider and a CoreProxyHover
        // pointing at the PREVIOUS galaxy, so hovering the centre after a reload reported the old
        // galaxy's name and system count. One orphan accumulated per rebuild.
        if (coreProxyRoot != null) Destroy(coreProxyRoot.gameObject);
        coreProxyRoot = null; coreProxy = null; coreProxyFade = null;
        if (deepVisual != null) { Destroy(deepVisual.gameObject); deepVisual = null; deepFade = null; }

        builtFor = g;

        // A new galaxy brings a NEW SystemParent, freshly active. `detailOn` still describes the OLD one,
        // so if we were in galaxy view when the galaxy changed, ApplyDetail would compare want:false to
        // detailOn:false, conclude nothing had changed, and leave the new detailed systems rendering
        // underneath the proxies. Resetting to true forces the next ApplyDetail to actually re-evaluate.
        detailOn = true;

        if (g == null)
        {
            // Update() returns on a null galaxy before it reaches ApplyDeep, which is the only code that
            // hides these — so quitting to the menu from the deep view would leave the old galaxy's name
            // painted across the screen over an active, empty deep root.
            if (galaxyTitle != null) galaxyTitle.gameObject.SetActive(false);
            if (deepRoot != null) deepRoot.gameObject.SetActive(false);
            if (proxyRoot != null) proxyRoot.gameObject.SetActive(false);
            return;
        }

        RecomputeBoundaries();

        float size = Mathf.Max(ProxySizeMin, CameraController.GalaxyRadius() * ProxySizeFrac);
        proxyBaseSize = size;
        foreach (var sys in g.systems)
            proxies.Add(GalaxyStarProxy.Build(proxyRoot, sys, size));

        BuildCoreProxy(g, size);

        // Built EAGERLY, here, rather than lazily on the first frame the deep view is wanted. Generating
        // the spiral texture is ~100k pixels of Perlin and transcendentals on the main thread; doing it
        // lazily put that freeze in the middle of a scroll gesture, which is the worst possible moment.
        // Here it lands during galaxy creation, where the game is already loading.
        BuildDeep(g);

        if (galaxyTitle != null) galaxyTitle.text = g.name;
    }

    // The galactic core, in the galaxy overview.
    //
    // It had no proxy at all before: RebuildProxies walked g.systems, and the core is not a system, so the
    // centrepiece of the galaxy was simply absent from the one view that shows the galaxy. Built at ~1.6x
    // a system's proxy so it dominates without swallowing the systems nearest it.
    void BuildCoreProxy(Galaxy g, float size)
    {
        if (g.center == null) return;

        // Two levels on purpose: the collider lives on the UNSCALED root, the visuals on a child that the
        // zoom ramp scales. Putting both on one object meant the pick sphere grew with the art — at a
        // fourfold zoom ramp the core's collider reached 600+ units and sat in front of the four or five
        // innermost systems, so hovering your home system said "Supermassive black hole" and clicking it
        // did nothing at all (CoreProxyHover has no click handler).
        var root = new GameObject("Proxy_GalacticCore");
        root.transform.SetParent(proxyRoot, false);
        root.transform.position = g.centerPosition;

        var art = new GameObject("Art").transform;
        art.SetParent(root.transform, false);

        // Deliberately much larger than a system's star — this is the supermassive object the whole
        // galaxy turns around, and at the widest playable zoom it is the anchor the eye goes to. It gets
        // the FULL article, same builder the system view uses: event horizon, photon ring, both
        // counter-rotating accretion discs with relativistic beaming, halo and polar jets.
        coreProxyBaseSize = size * 3.2f;
        BlackHoleVisual.Build(art, coreProxyBaseSize, withLight: false);

        // ...but its growth is capped so the event horizon never reaches the nearest system.
        //
        // The core scales with the same zoom ramp as the system stars, while the systems sit at FIXED
        // galaxy positions — the home system is hardcoded 170 units out (GalaxyGenerator.SpiralPosition).
        // So the horizon grows toward a stationary target. On an 11- or 12-system map it overtook it: at
        // the exact height the Home key jumps to, the horizon radius passed 190 and the home system was
        // inside the black hole, fully occluded because at that height the proxies are opaque and the
        // horizon has its depth write back on. The player pressed Home and their home system was gone.
        //
        // The cap is applied to the Art transform, so it holds the WHOLE black hole — horizon, discs,
        // halo, jets — not just the horizon. That is a little more conservative than strictly needed
        // (only the opaque horizon can actually occlude anything), but capping the parent keeps the
        // object internally consistent, and a core that stops growing early still reads correctly.
        float nearest = float.MaxValue;
        foreach (var sys in g.systems)
            nearest = Mathf.Min(nearest, (sys.galaxyPosition - g.centerPosition).magnitude);
        float horizonRadius = Mathf.Max(0.01f, coreProxyBaseSize * 0.5f);
        coreMaxScale = (nearest < float.MaxValue)
            ? Mathf.Clamp(nearest * 0.5f / horizonRadius, 1f, ProxyMaxScale)
            : ProxyMaxScale;

        var hover = root.AddComponent<CoreProxyHover>();
        hover.galaxy = g;
        var sc = root.AddComponent<SphereCollider>();
        sc.center = Vector3.zero;
        // Snug to the horizon rather than the halo. The core is scenery you read, not a target you pick,
        // so it has no business claiming pick priority over the systems around it.
        sc.radius = coreProxyBaseSize * 0.6f;

        coreProxyRoot = root.transform;
        coreProxy = art;
        coreProxyFade = root.AddComponent<FadeGroup>();
        coreProxyFade.Capture();
    }

    void BuildDeep(Galaxy g)
    {
        float radius = Mathf.Max(200f, CameraController.GalaxyRadius() * 1.35f);
        deepVisual = GalaxySpiralVisual.Build(deepRoot, g, radius);
        deepRoot.position = g.centerPosition;
        deepFade = deepVisual.GetComponent<FadeGroup>();
        if (deepFade != null) deepFade.Capture();
    }

    // Grow every proxy with camera height so its ON-SCREEN size stays roughly constant as you pull back
    // (screen size ~ worldSize / height, so worldSize ~ height holds it fixed) — a system's star never
    // shrinks to a sub-pixel dot. Capped, or extreme zoom would turn the map into one overlapping blob.
    void UpdateProxyScales(float h, float b1)
    {
        // Guard on the base size only. Returning on an empty proxy list would also skip the core below,
        // leaving it frozen at scale 1 in a galaxy that has a centre but no systems.
        if (proxyBaseSize <= 0.0001f) return;

        // Grow from the moment the overview becomes reachable, not from the boundary. Anchoring the ramp
        // at b1 meant f stayed pinned at 1 for the whole lower half of the crossfade — so through the
        // entire fade-in the stars were at their smallest AND their most transparent, which is exactly
        // when players reported them disappearing.
        float refH = Mathf.Max(1f, b1 * (1f - BlendFrac));
        float f = Mathf.Clamp(Mathf.Max(1f, h) / refH, 1f, ProxyMaxScale);
        foreach (var m in proxies) if (m != null) m.SetScale(f);
        if (coreProxy != null) coreProxy.localScale = Vector3.one * Mathf.Min(f, coreMaxScale);
    }

    void OnDestroy()
    {
        if (proxyRoot != null) Destroy(proxyRoot.gameObject);
        if (deepRoot != null) Destroy(deepRoot.gameObject);
        if (galaxyTitle != null) Destroy(galaxyTitle.gameObject);

        // Statics survive scene teardown. Left at Deep, the next galaxy's backdrop would start dimmed to
        // its deep-view level because SpaceBackground reads these before this component's first Update.
        Tier = ViewTier.System;
        ProxyAlpha = 0f;
        DeepAlpha = 0f;
    }
}

// Hover text for the galactic core in the galaxy overview.
public class CoreProxyHover : MonoBehaviour
{
    public Galaxy galaxy;

    void OnMouseOver()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (galaxy == null) return;
        string core = galaxy.center != null ? galaxy.center.name : "Galactic Core";
        MapHoverPanel.Instance?.ShowAtCursor(
            $"<b>{core}</b>\n<color=#9FB4C8>Supermassive black hole</color>" +
            $"\n<color=#9FB4C8>Galaxy:</color> {galaxy.name}" +
            $"\n<color=#9FB4C8>Systems:</color> {(galaxy.systems != null ? galaxy.systems.Count : 0)}");
    }

    void OnMouseExit()
    {
        if (MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();
    }
}

// One enlarged 3D star (or cluster, or black hole) standing in for a whole system in the galaxy overview.
// Carries the empire ring, the cursor hover panel, and the click behaviour.
public class GalaxyStarProxy : MonoBehaviour
{
    public StarSystemData system;
    public Transform art;      // scaled by the zoom ramp; the collider lives on the unscaled root

    SphereCollider pick;
    float baseColliderRadius = 1f;
    const float ColliderMaxScale = 2.5f;

    FadeGroup fade;
    float lastClickTime = -1f;
    bool pendingClick;
    const float DoubleClickWindow = 0.35f;   // matches PlanetClick, so the whole map feels consistent

    public static GalaxyStarProxy Build(Transform parent, StarSystemData sys, float size)
    {
        var root = new GameObject("Proxy_" + sys.name);
        root.transform.SetParent(parent, false);
        root.transform.position = sys.galaxyPosition;
        var proxy = root.AddComponent<GalaxyStarProxy>();
        proxy.system = sys;

        // Art on a scaled child, collider on the unscaled root — the same split BuildCoreProxy uses, and
        // for the same reason. With the collider on the scaled root, a system's pick sphere grew with the
        // zoom ramp until it reached the galaxy centre, so at wide zoom hovering the galactic core
        // reported the home system instead of the core.
        var art = new GameObject("Art").transform;
        art.SetParent(root.transform, false);
        proxy.art = art;

        int n;
        if (sys.isBlackHole)
        {
            // A black-hole system used to render here as a near-black sphere on a black background —
            // technically present, visually absent. It gets the real article now, at proxy scale.
            BlackHoleVisual.Build(art, size * 1.1f, withLight: false);
            n = 1;
        }
        else
        {
            var suns = (sys.stars != null && sys.stars.Count > 0)
                ? sys.stars
                : new List<StarData> { sys.combinedStar };
            n = Mathf.Max(1, suns.Count);

            for (int i = 0; i < n; i++)
            {
                var sun = i < suns.Count ? suns[i] : null;
                if (sun == null) sun = sys.combinedStar;
                Color c = sun != null ? sun.color : Color.white;

                var sphere = SpaceMaterials.Primitive(PrimitiveType.Sphere, art, "Sun" + i);
                // Spread the members of a cluster so a binary/ternary reads as more than one dot.
                Vector3 off = Vector3.zero;
                if (n > 1)
                {
                    float a = i * Mathf.PI * 2f / n;
                    off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size * 0.9f;
                }
                sphere.transform.localPosition = off;
                sphere.transform.localScale = Vector3.one * size * (n > 1 ? 0.8f : 1f);

                var rend = sphere.GetComponent<Renderer>();
                // Fadeable: the tier crossfade dissolves these in and out, and an opaque material would
                // ignore the alpha entirely and pop instead.
                if (rend != null) rend.material = SpaceMaterials.Unlit(c, fadeable: true);
            }
        }

        // One generous click/hover target covering the whole cluster, on the unscaled root. SetScale grows
        // it with the zoom, capped, so it tracks the drawn star without reaching its neighbours.
        var sc = root.AddComponent<SphereCollider>();
        sc.center = Vector3.zero;
        sc.radius = size * (n > 1 ? 1.9f : 1.15f);
        proxy.pick = sc;
        proxy.baseColliderRadius = sc.radius;

        // The green empire ring — the same idea planets and moons use — for any system an empire holds;
        // the home system always shows it.
        if (sys.isHome || sys.owner != null)
        {
            Color rc = sys.owner != null ? FactionManager.OwnerColor(sys.owner) : new Color(0.35f, 1f, 0.45f);
            // 1.3x the proxy size, not 2.1x. The ring scales with the art, so at wide zoom the old radius
            // swept out past several neighbouring systems and encircled the galactic core — it read as a
            // boundary around a region rather than a marker on one system.
            SpaceMaterials.MakeRing(art, "OwnerRing", size * 1.3f, rc, size * 0.12f, 72);
        }

        proxy.fade = root.AddComponent<FadeGroup>();
        proxy.fade.Capture();
        return proxy;
    }

    public void SetAlpha(float a)
    {
        if (fade != null) fade.SetAlpha(a);
    }

    public void SetScale(float f)
    {
        if (art != null) art.localScale = Vector3.one * f;

        // The pick sphere grows too, just more slowly and with a hard cap.
        //
        // Moving the collider to the unscaled root stopped it over-reaching into the galactic core, but it
        // also froze it — the drawn star grew to 7.4x while its clickable radius stayed put, so at wide
        // zoom the outer third of a visibly large star was dead to the cursor, which is the "stars are
        // hard to hit when zoomed out" problem the growth existed to solve. Capped at 2.5x, which keeps
        // the home system's pick sphere clear of the core's (162 < 170 units apart) while restoring most
        // of the screen-space pick size.
        if (pick != null) pick.radius = baseColliderRadius * Mathf.Min(f, ColliderMaxScale);
    }

    void OnMouseOver()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        MapHoverPanel.Instance?.ShowAtCursor(HoverText());
    }

    void OnMouseExit()
    {
        if (MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();
    }

    // Single click opens the light system summary; DOUBLE click opens the full Overview with every sun
    // listed together — the same gesture that opens a planet's viewer, so the map reads consistently.
    //
    // The single click is DEFERRED by one double-click window, and that is not fussiness. Acting on the
    // first click immediately opened the summary window, which anchors dead centre of the screen — and the
    // second click of the double-click then landed on that window, so the guard below saw
    // IsPointerOverGameObject() and returned before it ever reached the double-click test. Any system near
    // screen centre, which is exactly where the system you just zoomed toward sits, could never reach the
    // Overview at all. Waiting costs 0.35s on the single click and makes the double click actually work.
    void OnMouseDown()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        SimpleAudio.Instance?.PlaySelect();
        if (MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();

        if (pendingClick && Time.unscaledTime - lastClickTime < DoubleClickWindow)
        {
            pendingClick = false;
            lastClickTime = -1f;
            // The summary may already be up from an earlier single click — close it, or the same system
            // ends up shown twice in two different windows.
            SystemSummaryWindow.Instance?.Hide();
            StarOverview.Open(system);
            return;
        }

        pendingClick = true;
        lastClickTime = Time.unscaledTime;
    }

    void Update()
    {
        if (!pendingClick) return;
        if (Time.unscaledTime - lastClickTime < DoubleClickWindow) return;
        pendingClick = false;
        // Re-check the pointer: between the click and now it may have moved onto the HUD, and a window
        // that opens under the cursor while you are already using another one is a jump-scare.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        SystemSummaryWindow.Instance?.Show(system);
    }

    // A pending click must not survive the proxy being switched off.
    //
    // ApplyProxies deactivates the whole proxy root as the tier fades out, which stops Update — so a
    // click made just before zooming in would sit unfired, then pop its window minutes later when the
    // player zoomed back out to the galaxy view. Dropping it on deactivate loses the click instead,
    // which is the correct outcome: the view it belonged to is gone.
    void OnDisable() { pendingClick = false; }

    // Cursor-anchored summary: system name, class, each sun's mass (and the total for a cluster), owner.
    string HoverText()
    {
        var sb = new StringBuilder();
        sb.Append($"<b>{system.name}</b>");
        var star = system.combinedStar;
        sb.Append($"\n<color=#9FB4C8>{StarDatabase.SystemClass(star)}</color>");

        var suns = system.stars;
        if (!system.isBlackHole && suns != null && suns.Count > 1)
        {
            var ordered = new List<StarData>(suns);
            ordered.Sort((a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));
            float total = 0f;
            foreach (var sun in ordered)
            {
                if (sun == null) continue;
                total += sun.mass;
                sb.Append($"\n<color=#9FB4C8>{sun.name}:</color> {sun.mass:F2} solar");
            }
            sb.Append($"\n<color=#9FB4C8>Total mass:</color> <b>{total:F2}</b> solar");
        }
        else if (star != null)
            sb.Append($"\n<color=#9FB4C8>Mass:</color> {star.mass:F2} solar");

        string own = system.owner == FactionManager.Player ? "<color=#4DFF6E>Your empire</color>"
                   : system.owner != null ? FactionManager.OwnerName(system.owner)
                   : "Unclaimed";
        sb.Append($"\n<color=#9FB4C8>Owner:</color> {own}");
        string civ = FactionAI.Describe(system.owner);
        if (civ != null) sb.Append($"\n<size=10><color=#9FB4C8>{civ}</color></size>");
        sb.Append("\n<size=10><color=#7F8C9B>Double-click for full overview</color></size>");
        return sb.ToString();
    }
}
