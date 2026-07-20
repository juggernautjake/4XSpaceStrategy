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
// THE RULE NOW: there are TWO MODES, and they are mutually exclusive.
//
//   height ->      [ Body ]   [ System ]  |     [ Galaxy ]           [ Deep ]
//   detailed systems  ##########################|
//   star proxies                                |###############################
//   deep spiral                                 |            #############+#####
//
// SYSTEM MODE draws the real systems: suns, planets, moons, orbits, ships. GALAXY MODE draws one enlarged
// star per system, the galactic core, and — further out — the spiral those systems sit in. Crossing the
// boundary switches modes outright. The small and large representations of a system NEVER exist at the
// same time; a system is either drawn as itself or drawn as a marker, never as both.
//
// That exclusivity is a deliberate reversal. The previous version held the detailed systems on until the
// proxies had finished fading in OVER them, which crossfaded smoothly but meant a band of ~190 units of
// height where each system was drawn twice — once at true scale and once as a huge sphere on top of it.
// It read as clutter, not as a transition.
//
// Smoothness now comes from the proxies fading UP from the boundary rather than across it: at the instant
// detail switches off, proxy alpha is 0 and rising, so nothing pops in fully-formed and nothing overlaps.
// The switch itself has hysteresis — a binary test on a jittering height flickers, and there is no longer
// a fade spanning the threshold to hide it.
//
// The deep spiral is ADDITIVE to galaxy mode, not a third replacement for it: the system stars stay lit
// and at full alpha inside it, which is the point of the widest view — you should be able to see where
// your empire is while looking at the whole galaxy.
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

    /// True while the real systems are being drawn. This is THE authority on which mode is up — anything
    /// that has to appear and disappear together with the planets (ships, most obviously) must key off
    /// this and not off an alpha, or it will hand over on a slightly different frame and be left floating
    /// alone in an otherwise collapsed map.
    public static bool SystemMode { get; private set; } = true;

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
    // The base size is small (1% of the galaxy radius) precisely SO there is headroom to grow. At the
    // original 4.5% a proxy started at ~63 units against ~95-unit system spacing, so it could barely grow
    // before neighbours merged — the growth had to be capped almost immediately, and capped growth is
    // what let the stars shrink away at wide zoom. Starting small buys a long runway.
    const float ProxySizeFrac = 0.010f;
    const float ProxySizeMin = 2f;

    // The ceiling on that growth is COMPUTED, not guessed — from the real closest approach between any
    // two systems in this galaxy (see RecomputeProxyLimits). A hardcoded number cannot be right for both
    // a 1-system and a 12-system map, and getting it wrong in either direction is visible: too low and
    // the stars shrink away as you pull back, too high and the inner map merges into one blob.
    float proxyMaxScale = 6f;

    // Fraction of the closest system-to-system distance a proxy's DIAMETER may occupy. At 0.8 neighbours
    // are clearly distinct but the field still reads as populated.
    const float ProxySpacingUse = 0.8f;

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

    // How long the enlarged stars take to dissolve in after the mode switch. Long enough to read as a
    // transition rather than a pop, short enough that it is over before you have finished scrolling.
    const float ProxyFadeSeconds = 0.3f;
    float proxyFade;
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
        // mid-transition. A small galaxy can frame below MinGalaxyEnter, which would leave Home sitting
        // inside the switch band with a half-faded proxy on top of the still-rendered star — and its
        // collider in front of it, stealing clicks from the star underneath. (With the current layout the
        // inner ring alone is 1400 units out, so the clamp no longer binds in practice; it stays because
        // nothing else guarantees the relationship if the layout is retuned.)
        // The 0.999 keeps the clamp strictly below cFrame rather than landing a float ULP either side of
        // it, so "frame the whole galaxy" is unambiguously in galaxy mode at every galaxy size.
        cB1 = Mathf.Min(b1, Mathf.Max(1f, cFrame / (1f + BlendFrac) * 0.999f));

        // Kept clear of the galaxy boundary so the two crossfades never overlap — if they did, all three
        // representations would be partly visible at once and the screen would fog.
        //
        // `DeepEnterFrac` (1.6) is what actually keeps `cFrame` below the deep band: the band opens at
        // 1.6 * 0.7 = 1.12 * cFrame. The third term is a belt-and-braces floor that would take over if
        // DeepEnterFrac were ever tuned below 1/(1-BlendFrac) = 1.43; at 1.6 it never binds.
        cB2 = Mathf.Max(Mathf.Max(cB1 * 3f, cFrame * DeepEnterFrac),
                        cFrame / (1f - BlendFrac) * 1.08f);

        // ...but never so high that the deep view cannot finish arriving before the wheel stops.
        //
        // The fade completes at cB2 * (1 + BlendFrac), and the wheel is clamped at
        // CameraController.ZoomCeiling (DeepZoomFactor * cFrame). On a small galaxy the `cB1 * 3` term
        // wins and pushes the completion point above that ceiling — so the spiral would be stuck
        // permanently part-faded, at a height the player cannot zoom past. Capping here means the widest
        // view is always the fully-formed one.
        float deepCeiling = cFrame * CameraController.DeepZoomFactor / (1f + BlendFrac);
        if (deepCeiling > 1f) cB2 = Mathf.Min(cB2, deepCeiling);
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

        // Mode first — the proxy fade is driven by which mode we are in, not by height.
        ApplyDetail(h, b1);

        // The proxy fade is over TIME, not over height, and that is the whole reason this works.
        //
        // Height-based was the obvious thing and it was wrong. Exclusive modes mean detail vanishes at a
        // threshold and proxies must start from zero there, so a height-based ramp leaves a band where
        // detail is already off and proxies are still near-invisible. With the switch carrying hysteresis
        // that band is ~220 units wide, and a single scroll notch (height x0.55) can PARK you inside it —
        // an empty starfield with no planets, no stars, no spiral, and no way to tell it is a bug rather
        // than empty space. Descending only, so it would have read as intermittent.
        //
        // On a timer there is no such band: whatever height you stop at, the fade finishes in 0.3s.
        // Branch on the MODE, not on a comparison between the fade and its target. Writing this as
        // `target > proxyFade ? MoveTowards(...) : 0f` looks equivalent and is not: MoveTowards returns
        // the target EXACTLY once it arrives, so the very next frame `1 > 1` is false and the fade was
        // slammed back to zero — a permanent 0.3-second sawtooth that blinked every star in the galaxy
        // out for one frame, restarted the dissolve, and pulsed the backdrop along with it.
        proxyFade = detailOn
            ? 0f   // returning to system mode: cut instantly, detail is already back on top
            : Mathf.MoveTowards(proxyFade, 1f, Time.unscaledDeltaTime / ProxyFadeSeconds);

        float toDeep = Blend(h, b2);

        // NOT multiplied by (1 - toDeep). The system stars stay fully lit inside the deep view: the
        // widest zoom is meant to show you the galaxy AND where you are in it, and fading the stars out
        // left it a pretty but uninformative picture.
        ProxyAlpha = proxyFade;
        DeepAlpha = toDeep;

        Tier = h < BodyMaxHeight ? ViewTier.Body
             : h < b1 ? ViewTier.System
             : h < b2 ? ViewTier.Galaxy
             : ViewTier.Deep;

        ApplyProxies(ProxyAlpha, h, b1);
        ApplyDeep(DeepAlpha);
    }

    // The mode switch. Detail off above the boundary, on below it — with a hysteresis gap, because a bare
    // `h < b1` on a height that eases toward its target will flip back and forth for several frames as it
    // settles, and each flip is a full activate/deactivate of every planet, moon and orbit in the galaxy.
    //
    // The gap is asymmetric on purpose: you must climb past b1 to leave system mode, but drop to 0.88*b1
    // to come back. Zooming out is the deliberate gesture, so it gets the tighter edge.
    void ApplyDetail(float h, float b1)
    {
        bool want = detailOn ? h < b1 : h < b1 * 0.88f;
        if (want == detailOn) return;
        detailOn = want;
        SystemMode = want;
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

    // Takes only the alpha now: the deep view is fixed-scale, so it no longer depends on camera height,
    // the boundary, or the galaxy.
    void ApplyDeep(float alpha)
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

        // FIXED at true galaxy scale — deliberately not grown with height like the proxies are.
        //
        // The spiral is the galaxy the systems actually sit in, so its arms have to stay registered with
        // where those systems are. Scaling it with camera height slid it out from under them: the spiral
        // swelled while the star positions stayed put, until the whole empire was a knot in the middle of
        // a vastly oversized disc. Leaving it fixed means pulling back genuinely makes it recede, which is
        // what sells the distance — and the zoom ceiling (CameraController.ZoomCeiling) stops before it
        // ever becomes too small to read.
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
        // Must move together with detailOn. ApplyDetail early-returns when `want == detailOn`, so if the
        // galaxy changed while in galaxy mode and the camera lands below the boundary, that early return
        // fires and SystemMode is never written — leaving it false while the planets render. Every ship
        // then stays hidden indefinitely (and newly built ones get hidden too, since MapTierVisibility
        // re-applies while hidden), until the player manually zooms out past the boundary and back.
        SystemMode = true;
        proxyFade = 0f;

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
        RecomputeProxyLimits(g, size);
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

    // How large a proxy may grow before it starts merging with its neighbour.
    //
    // Measured, not assumed: the golden-angle layout means the closest pair is not necessarily adjacent
    // in index order, so this walks every pair. Twelve systems is 66 comparisons, once per galaxy.
    void RecomputeProxyLimits(Galaxy g, float size)
    {
        proxyMaxScale = 6f;
        if (g.systems == null || g.systems.Count < 2 || size <= 0.0001f) return;

        float minSep = float.MaxValue;
        for (int i = 0; i < g.systems.Count; i++)
            for (int j = i + 1; j < g.systems.Count; j++)
                minSep = Mathf.Min(minSep,
                    (g.systems[i].galaxyPosition - g.systems[j].galaxyPosition).magnitude);

        if (minSep <= 0.0001f || minSep == float.MaxValue) return;
        // size is the proxy's DIAMETER at scale 1, so the budget compares directly against separation.
        proxyMaxScale = Mathf.Clamp(minSep * ProxySpacingUse / size, 1f, 40f);
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
        // the FULL article, same builder the system view uses: event horizon, one tilted accretion disc
        // with relativistic beaming, a camera-facing photon ring, and a halo.
        coreProxyBaseSize = size * 3.2f;
        BlackHoleVisual.Build(art, coreProxyBaseSize, withLight: false);

        // ...but its growth is capped so the event horizon never reaches the nearest system.
        //
        // The core scales with the same zoom ramp as the system stars, while the systems sit at FIXED
        // galaxy positions. So the horizon grows toward a stationary target, and on a tightly-packed map
        // it used to overtake it: the innermost system ended up inside the black hole and fully occluded,
        // because at that height the proxies are opaque and the horizon has its depth write back on.
        //
        // The budget is measured against the HORIZON specifically, because the horizon is the only part
        // that can occlude anything — it is opaque and depth-writing. The halo and the outer disc extend
        // roughly 4.8x further and will overlap the inner systems at high zoom, which is fine and in fact
        // wanted: they are additive and low-alpha, so they read as the core's glow washing over the inner
        // map rather than as anything hiding behind it.
        float nearest = float.MaxValue;
        foreach (var sys in g.systems)
            nearest = Mathf.Min(nearest, (sys.galaxyPosition - g.centerPosition).magnitude);
        float horizonRadius = Mathf.Max(0.01f, coreProxyBaseSize * 0.5f);
        coreMaxScale = (nearest < float.MaxValue)
            ? Mathf.Clamp(nearest * 0.5f / horizonRadius, 1f, proxyMaxScale)
            : proxyMaxScale;

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
        // 1.3x the outermost system, so the arms extend a little past the systems rather than being
        // clipped by them. Fixed for the life of the galaxy — see the note in ApplyDeep.
        float radius = Mathf.Max(200f, CameraController.GalaxyRadius() * 1.3f);
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

        // Anchored at the mode boundary, which is exactly where the proxies come into existence. World
        // size then tracks camera height, so SCREEN size is constant: a system's star is the same size to
        // the eye the moment galaxy mode opens and at the zoom ceiling, and never shrinks away.
        float refH = Mathf.Max(1f, b1);
        float f = Mathf.Clamp(Mathf.Max(1f, h) / refH, 1f, proxyMaxScale);
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
        SystemMode = true;   // or the next scene's ships start hidden
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
                float dia = size * (n > 1 ? 0.8f : 1f);
                sphere.transform.localPosition = off;
                sphere.transform.localScale = Vector3.one * dia;

                // THE SAME BRIGHTNESS CURVE THE REAL STAR USES.
                //
                // These were plain LDR unlit spheres tinted with sun.color, and that is why they looked
                // dull and washed-out next to the same star in system view. A real star gets
                // `_EmissionColor = color * EmissionStrength(s)` — up to 3.5x, i.e. HDR — so bloom and the
                // ACES tonemapper pick it up and it reads as a light source. A flat colour under 1.0
                // reads as a painted ball, and no amount of picking a nicer colour fixes that, because
                // the difference is luminance range rather than hue.
                //
                // An unlit material writes its base colour straight into the HDR buffer, so pushing the
                // colour above 1 is all it takes to get the same treatment without needing the URP Unlit
                // shader to expose an emission slot.
                // GalaxyBoost on top of the star's own emission curve.
                //
                // Matching the system view's emission exactly still read as dimmer out here, and the
                // reason is context rather than colour: in system view a star is the brightest thing on
                // screen and fills a good fraction of it, while in the overview it is a small disc
                // competing with a full starfield backdrop. Bloom is thresholded, so a small bright disc
                // spills far less glow than a large one at the same value. Overshooting the curve is what
                // makes the two read as equally hot to the eye, which is the actual requirement.
                const float GalaxyBoost = 2.2f;
                float emK = StarDatabase.EmissionStrength(sun) * GalaxyBoost;
                Color hot = new Color(c.r * emK, c.g * emK, c.b * emK, 1f);

                var rend = sphere.GetComponent<Renderer>();
                // Fadeable: the tier crossfade dissolves these in and out, and an opaque material would
                // ignore the alpha entirely and pop instead. FadeGroup scales only alpha, so the HDR
                // colour survives a fade intact.
                if (rend != null) rend.material = SpaceMaterials.Unlit(hot, fadeable: true);

                // A corona, so the star still glows when it is small on screen and whatever bloom is
                // configured has stopped helping. Additive, so it brightens the sky rather than sitting
                // on it as a disc.
                // Two coronae, not one: a tight bright halo that reads as the star's own light, and a
                // wide faint one that gives it presence against the backdrop. A single quad has to
                // choose between looking hot and looking big, and ends up doing neither.
                var glow = SpaceMaterials.Glow(art, "Glow" + i, dia * 3.0f,
                                               new Color(c.r, c.g, c.b, 0.85f));
                glow.transform.localPosition = off;

                var halo = SpaceMaterials.Glow(art, "Halo" + i, dia * 4.6f,
                                               new Color(c.r, c.g, c.b, 0.30f));
                halo.transform.localPosition = off;
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
            var ring = SpaceMaterials.MakeRing(art, "OwnerRing", size * 1.3f, rc, size * 0.12f, 72);

            // Hold a minimum on-screen thickness. Which empire holds a system is INFORMATION, and at the
            // widest zoom these rings were thinning to a shimmering hairline — technically drawn, and
            // unreadable. The floor is angular, so nothing changes until distance would make it too thin.
            var minW = ring.gameObject.AddComponent<MinScreenWidthLine>();
            minW.baseWidth = size * 0.12f;
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
        // every system's pick sphere well clear of its neighbours' and of the core's, while restoring most
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
