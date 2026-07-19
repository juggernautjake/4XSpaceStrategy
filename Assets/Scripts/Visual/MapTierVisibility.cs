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

    // Keyed off the MODE, not off any alpha. Ships belong to the detailed systems and must vanish on the
    // exact frame those do — an alpha-based test hands over a few frames early or late and leaves the
    // fleet hanging in an otherwise collapsed map. SystemMode is the single switch both follow.
    static bool ShouldShow => GalaxyLOD.SystemMode;

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
