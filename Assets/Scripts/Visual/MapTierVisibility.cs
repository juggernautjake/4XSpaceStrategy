using UnityEngine;

// Hides everything under this transform once the camera pulls back past the system view.
//
// WHY THIS EXISTS. The render ladder de-renders the detailed systems by switching off SystemContext's
// SystemParent, and that covers planets, moons, orbit rings and stars — everything parented under it. It
// does NOT cover ships, because the unit renderers each build their models under their OWN root object
// (UnitModelRenderer and UnitTokenRenderer both parent to `transform`), which is not a
// child of SystemParent. So at galaxy zoom the systems collapsed to single stars while the fleets kept
// rendering at full size, scattered across the map with nothing around them.
//
// (UnitIconRenderer needs none of this — it is a static texture/sprite factory for the build menus, not
// something that puts objects in the scene.)
//
// VISUALS ONLY — this is the important part. Orders, movement, construction and every other unit task
// live in UnitManager and the systems around it, none of which consult a Renderer. Deactivating the model
// GameObjects stops them being DRAWN and nothing else: a ship mid-journey keeps travelling, a terraformer
// keeps terraforming, and a build queue keeps building. Transforms can still be written while inactive,
// so the renderers' per-frame positioning keeps working too and a ship reappears exactly where it should
// be when you zoom back in, not where it was when it vanished.
//
// It toggles the direct CHILDREN rather than itself, because switching off the host GameObject would stop
// the renderer component's own Update and with it the bookkeeping that keeps models in step with units.
[DisallowMultipleComponent]
public class MapTierVisibility : MonoBehaviour
{
    bool lastVisible = true;

    // Keyed off the CONTINUOUS proxy alpha, not the discrete tier — and that is the difference between
    // ships that vanish with their systems and ships that vanish before them.
    //
    // Tier flips to Galaxy at the boundary, but the detailed systems are deliberately held on until the
    // proxies have finished fading in OVER them (see GalaxyLOD.ApplyDetail). Keying off Tier put a band
    // of ~190 units of camera height where planets, moons and orbit rings were still fully drawn while
    // every ship had already popped out — and popping back in 190 units late on the way down.
    // ProxyAlpha reaching 1 is exactly the moment the detailed systems switch off.
    //
    // The DeepAlpha term is not redundant. ProxyAlpha is `toGalaxy * (1 - toDeep)`, so it falls back
    // toward zero as the deep view fades in — meaning a bare `ProxyAlpha < 0.999f` becomes true AGAIN at
    // the top of the range and every ship reappears in the deep view, which is the exact thing this class
    // exists to prevent, reinstated at the far end. The test has to be "the systems are drawn", not
    // "the proxies are not fully opaque".
    static bool ShouldShow => GalaxyLOD.ProxyAlpha < 0.999f && GalaxyLOD.DeepAlpha < 0.001f;

    void LateUpdate()
    {
        bool visible = ShouldShow;

        // Fast path while shown and already shown: nothing to do. Note the asymmetry — while HIDDEN this
        // re-applies every frame, deliberately, so a ship built or spawned during galaxy view is hidden
        // too rather than appearing on its own in an otherwise collapsed map.
        if (visible && lastVisible) return;
        lastVisible = visible;

        int n = transform.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = transform.GetChild(i);

            // Never hide a self-destructing effect. A DestroyFlash removes itself from its own LateUpdate;
            // deactivate it and that never runs, so the quad becomes immortal — a kill resolved at galaxy
            // zoom would leak one forever, and they would all flash at once when the player zoomed in.
            if (c.GetComponent<DestroyFlash>() != null) continue;

            if (c.gameObject.activeSelf != visible) c.gameObject.SetActive(visible);
        }
    }
}
