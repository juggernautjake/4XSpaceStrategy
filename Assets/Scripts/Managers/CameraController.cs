using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
    // ============================================================================================
    // TUNING — const, and that is LOAD-BEARING. Do not make these public fields again.
    //
    // These were `public float`, which means Unity SERIALIZED them into the scene. Once a value is baked
    // into SampleScene.unity, the scene's copy is deserialized OVER the script's default every time the
    // component loads — so editing the default here does exactly nothing to the instance in the scene.
    //
    // That is not a hypothetical. The scene held:
    //     minHeight: 10        maxHeight: 80        panSpeed: 20        heightSpeed: 5
    // while this file claimed 0.04 / 120000 / 30 / 130. The camera was hard-capped at 80 units the whole
    // time. The ceiling was raised here three separate times — 9000, then 60000, then 120000 — and every
    // one of those edits was silently discarded on load. "Zooming out doesn't go far enough", three
    // times over, was this: the number being read was never the number being written.
    //
    // A const cannot be serialized, so the script is now the only source of truth and the two cannot
    // drift apart again. The old keys still sitting in the scene YAML are simply ignored — an unknown
    // key deserializes to nothing.
    //
    // The rule this encodes: a value that is TUNING (one right answer, same for every instance) does not
    // belong in the Inspector. Only genuinely per-instance data should be a serialized field.
    // ============================================================================================

    const float panSpeed = 30f;            // WASD panning speed
    // heightSpeed is gone: it was declared, serialized (as 5), and never read by anything. Zoom is
    // proportional (HandleHeightChange), so a linear "units per notch" speed has nothing to mean.

    // The absolute floor. Low, because a moon is only ~0.35 world units across and filling the screen
    // with one genuinely needs the camera this close.
    const float minHeight = 0.04f;

    // The floor when nothing is selected — and the one that actually governs "how far can I zoom in",
    // since with a body selected the limit is that body's own surface (see ZoomFloor). Low enough to get
    // right down among the planets. The scene's serialized minHeight of 10 was overriding this into
    // uselessness: 10 units up is further away than a whole planet is wide.
    const float freeLookMinHeight = 0.35f;

    // Farthest view. Framing the whole galaxy needs only ~1.12 * its radius (see HeightToFrame), so for
    // any galaxy this generates that's a few thousand units — this ceiling is orders of magnitude beyond
    // anything reachable, on purpose. Headroom costs nothing but a bigger far clip, which
    // UpdateClipPlanes already tracks, and a ceiling that binds is a ceiling that fights you.
    const float maxHeight = 120000f;

    private float targetHeight;            // For smooth movement

    public static CameraController Instance;
    private Camera cam;

    // The camera's far clip plane is the thing that actually decides how far out you can see. The scene
    // ships with Unity's default of 1000, so pulling back past that height quietly clipped the entire
    // galaxy away and made zooming out look broken. We now drive it from the current height every frame,
    // which is what lets you pull all the way back to the whole map.
    const float MinFarClip = 1200f;

    // The fixed top-down angle the view is locked to, in degrees below horizontal. Was hardcoded as a
    // bare 55f in four places, including the framing maths that decides how far out you can see.
    public const float Pitch = 55f;

    // Extra room around the galaxy when framing it, so the outermost systems aren't on the screen edge.
    const float FrameMargin = 1.15f;

    private void Awake() { Instance = this; cam = GetComponent<Camera>(); }

    private void Start()
    {
        targetHeight = transform.position.y;
        if (cam == null) cam = GetComponent<Camera>();
        UpdateClipPlanes();
    }

    // How far out the galaxy actually extends, in world units (0 when there's no galaxy yet).
    public static float GalaxyRadius()
    {
        var g = SystemContext.Galaxy;
        if (g == null || g.systems == null || g.systems.Count == 0) return 0f;
        float r = 0f;
        foreach (var sys in g.systems)
        {
            var p = sys.galaxyPosition;
            r = Mathf.Max(r, new Vector2(p.x, p.z).magnitude);
        }
        return r;
    }

    // The height needed to frame a disc of this radius on screen.
    //
    // Worked from the actual geometry rather than a fudge factor. The camera looks down at Pitch, and
    // the vertical FOV spreads +/- fov/2 around that, so on the ground the view runs from
    //   near edge = h / tan(Pitch + fov/2)   to   far edge = h / tan(Pitch - fov/2)
    // and the usable DEPTH is the difference. Depth is what binds here — the view is far wider than it
    // is deep at this pitch — so solve depth >= 2*radius and add a margin.
    public float HeightToFrame(float radius)
    {
        if (cam == null) cam = GetComponent<Camera>();
        float fov = cam != null ? cam.fieldOfView : 60f;
        float half = fov * 0.5f;

        float topAng = Mathf.Max(2f, Pitch - half) * Mathf.Deg2Rad;      // shallower ray -> reaches furthest
        float botAng = Mathf.Min(88f, Pitch + half) * Mathf.Deg2Rad;     // steeper ray -> lands closest
        float depthPerHeight = (1f / Mathf.Tan(topAng)) - (1f / Mathf.Tan(botAng));
        if (depthPerHeight <= 0.01f) return Mathf.Clamp(radius * 1.5f, 50f, maxHeight);

        float needed = (radius * 2f * FrameMargin) / depthPerHeight;
        return Mathf.Clamp(needed, 50f, maxHeight);
    }

    /// How far out the wheel may actually go, derived from the galaxy rather than the absolute ceiling.
    ///
    /// `maxHeight` is a 120,000-unit backstop, which is orders of magnitude past anything worth looking
    /// at — a galaxy frames at a few thousand. Left as the wheel's limit, most of the scroll range was
    /// empty black with the whole galaxy a dot in the middle, and the representative stars shrank to
    /// nothing long before you reached it. This puts the ceiling just past the point where the deep view
    /// has fully faded in, so "fully zoomed out" is a composed shot — the spiral, its core, and the system
    /// stars still readable inside it — rather than an unbounded void.
    ///
    /// 2.4x the framing height: the deep view finishes fading in at ~2.08x (see GalaxyLOD), so this
    /// clears it with a little room and nothing more.
    /// Public because GalaxyLOD has to keep the deep view's fade INSIDE this ceiling — a fade that
    /// completes above the height the wheel stops at can never be seen finished.
    public const float DeepZoomFactor = 2.4f;

    public float ZoomCeiling()
    {
        float r = GalaxyRadius();
        if (r <= 0f) return Mathf.Min(maxHeight, 4000f);
        return Mathf.Clamp(HeightToFrame(r) * DeepZoomFactor, 200f, maxHeight);
    }

    // ---- Named zoom levels -----------------------------------------------------------------------
    //
    // The three rungs of the ladder, as things the player can ask for by name rather than by scrolling
    // until it looks right. Each targets the height GalaxyLOD actually switches at, so "System" lands
    // safely inside system mode and "Galaxy" safely inside galaxy mode rather than on a boundary.

    /// Frame the selected body (or the focused system's first world) close up.
    public void ViewPlanet()
    {
        var body = PlanetUI.Selected;
        if (body == null)
        {
            var sys = GameManager.Instance != null ? GameManager.Instance.FocusedSystem : null;
            if (sys != null && sys.bodies != null && sys.bodies.Count > 0) body = sys.bodies[0];
        }
        if (body == null || body.visualObject == null) { ViewSystem(); return; }

        FocusAndZoom(body.visualObject.transform, body.surfaceSize, false);
    }

    /// Frame the focused system: its star(s) and every planet in it.
    public void ViewSystem()
    {
        var sys = GameManager.Instance != null ? GameManager.Instance.FocusedSystem : null;
        if (sys == null) { ViewWholeGalaxy(); return; }

        // Reach of the outermost body, so the whole system fits rather than just the inner planets.
        float reach = 12f;
        if (sys.bodies != null)
            foreach (var b in sys.bodies)
            {
                if (b == null) continue;
                reach = Mathf.Max(reach, b.orbitRadius + OrbitSafety.SystemReach(b));
            }

        ClearFocus();
        float want = HeightToFrame(reach);

        // Hold it below the system/galaxy boundary, or asking for "System" would hand you the galaxy
        // overview — the systems are laid out far enough apart that a large one can frame above it.
        want = Mathf.Min(want, SystemModeCeiling());

        JumpTo(sys.galaxyPosition, want);
    }

    /// The highest height that is still system mode, with a margin off the boundary.
    float SystemModeCeiling()
    {
        float frame = HeightToFrame(GalaxyRadius());
        // Mirrors GalaxyLOD's GalaxyEnterFrac (0.40). Kept as a fraction of the same framing height so
        // the two move together if the galaxy is resized.
        return Mathf.Max(50f, frame * 0.40f * 0.85f);
    }

    /// Pull all the way back so the entire generated galaxy is on screen at once.
    public void ViewWholeGalaxy()
    {
        ClearFocus();
        float radius = GalaxyRadius();
        if (radius <= 0f) { targetHeight = Mathf.Min(maxHeight, 1600f); return; }

        // Centre on the galaxy's middle rather than wherever we happened to be.
        Vector3 centre = Vector3.zero;
        var g = SystemContext.Galaxy;
        if (g != null && g.systems != null && g.systems.Count > 0)
        {
            foreach (var sys in g.systems) centre += sys.galaxyPosition;
            centre /= g.systems.Count;
        }
        targetHeight = HeightToFrame(radius);
        var p = transform.position; p.y = targetHeight; transform.position = p;
        FocusOn(centre);
        UpdateClipPlanes();
    }

    // Keep the view volume big enough for whatever height we're at. Without this the far plane silently
    // eats the galaxy; with it, zooming out simply works.
    private void UpdateClipPlanes()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;
        float h = Mathf.Max(transform.position.y, targetHeight);
        // The ground is reached at h/sin(pitch); everything beyond that still has to be drawn, so give
        // the far plane the slant range plus the galaxy's own extent plus headroom.
        float slant = h / Mathf.Sin(Pitch * Mathf.Deg2Rad);

        // Near plane has to grow with distance or z-fighting sets in at galaxy scale. What matters for
        // depth precision is the far:near RATIO, not either number, so the near plane has to keep pace
        // as the ceiling rises — pinning it at 5 while the far plane runs past 100,000 is a 20,000:1
        // ratio and the depth buffer starts tearing. Nothing is within 240 units of the camera when it's
        // 120,000 up, so a near plane that far out clips nothing you could see anyway. The 0.01 floor is
        // what lets the other end work: at minHeight the camera is centimetres off a moon's surface.
        //
        // QUANTIZED, and that's the anti-jitter part.
        //
        // The near plane is what decides how depth-buffer precision is distributed across the scene.
        // Computed straight from h, it slid a little EVERY FRAME while zooming — so every surface's depth
        // quantisation shifted every frame, and the z-fighting boundaries between near-coplanar surfaces
        // (a planet and its atmosphere shell, a ring and the plane it lies in) crawled. That reads as a
        // faint shimmer on the assets, and only while zooming, because h is only moving then.
        //
        // Snapping to powers of two means the planes hold still through a whole octave of zoom and change
        // once, briefly, when you cross a boundary — one discrete step instead of a continuous crawl. The
        // cost is up to 2x more near-plane than strictly needed, which is far cheaper than the shimmer.
        cam.nearClipPlane = QuantizeUp(h * 0.002f, 0.01f, 240f);
        cam.farClipPlane = QuantizeUp(slant + GalaxyRadius() * 2.5f + 1000f, MinFarClip, 1e7f);
    }

    /// Round `v` UP to the next power-of-two multiple of `min`, clamped to [min, max].
    ///
    /// Up, never down, and that matters for the far plane: rounding down would clip away scenery that
    /// was meant to be visible, which is the bug this whole method exists to avoid. Rounding up only ever
    /// draws slightly more than needed.
    static float QuantizeUp(float v, float min, float max)
    {
        if (v <= min) return min;
        if (v >= max) return max;
        float steps = Mathf.Ceil(Mathf.Log(v / min, 2f));
        return Mathf.Clamp(min * Mathf.Pow(2f, steps), min, max);
    }

    // Recenters the view on a world position (used by notifications to jump to a discovery).
    public void FocusOn(Vector3 worldPos)
    {
        // Any deliberate move invalidates the zoom anchor — it's a stale answer to "where should the
        // camera be so the thing under the cursor stays put", and the cursor was somewhere else when it
        // was computed. Left set, it would drag the view back mid-flight.
        //
        // Cleared HERE because FocusOn is the choke point: ViewWholeGalaxy, JumpTo, FocusAndZoom and the
        // notification jumps all route through it. Clearing at each of those instead would be four places
        // to remember and one to forget.
        ClearAnchor();

        Vector3 f = transform.forward;
        if (Mathf.Abs(f.y) < 0.001f)
        {
            transform.position = new Vector3(worldPos.x, transform.position.y, worldPos.z);
            return;
        }
        float h = transform.position.y;
        float d = (worldPos.y - h) / f.y;   // distance along forward until it hits the body's height
        Vector3 pos = worldPos - f * d;
        pos.y = h;
        transform.position = pos;
        // NOTE: do NOT touch targetHeight here. While following, this runs every frame; resetting the
        // zoom target would fight the scroll wheel and make zooming feel janky/stuck when a body is
        // selected. Zoom is owned solely by HandleHeightChange/SmoothHeightMovement.
    }

    // Convenience: focus whatever camera controller exists (or the main camera as a fallback).
    public static void Focus(Vector3 worldPos)
    {
        if (Instance != null) { Instance.FocusOn(worldPos); return; }
        var cam = Camera.main;
        if (cam != null) cam.transform.position = new Vector3(worldPos.x, cam.transform.position.y, worldPos.z - 10f);
    }

    [Header("Follow / Focus")]
    public Transform followTarget;
    public bool following;

    // Default preference: clicking a planet auto-follows it. Turned off if the user unfollows.
    public static bool AutoFollow = true;

    private void Update()
    {
        bool menuOpen = (EscapeMenu.Instance != null && EscapeMenu.Instance.IsOpen)
                     || (StartMenu.Instance != null && StartMenu.Instance.IsOpen);
        bool planetViewOpen = PlanetViewWindow.Instance != null && PlanetViewWindow.Instance.IsOpen;

        // WASD pans the world camera whenever the keys aren't wanted elsewhere: not while the pause menu is
        // open, not while actively FOLLOWING a body (the camera is locked to it — press F again or click
        // away to release), and not while the full-screen Planet View is up (it has its own map). Selecting
        // a planet no longer blocks panning — the old mini-map tile cursor that used to claim WASD is gone,
        // so a selected planet pans just like a selected star.
        bool canPan = !following && !menuOpen && !planetViewOpen;
        if (canPan) HandlePanning();

        // Rotation is allowed even while following a body — spinning around what you are watching is the
        // main reason to want it. Blocked only by a modal menu and the full-screen Planet View.
        if (!menuOpen && !planetViewOpen) HandleRotationInput();

        // Wheel-zoom the WORLD only when the wheel isn't wanted elsewhere: not while a modal menu is open,
        // not while the full-screen Planet View is up (its own map owns the wheel there), and — the fix for
        // the weird double action — not while the cursor is over a SCROLLABLE menu, which consumes the wheel
        // to scroll itself. Over a NON-scrolling panel the wheel still zooms the world behind it.
        bool overScroller = UIScroll.PointerOverScroller();
        if (!menuOpen && !planetViewOpen && !overScroller) HandleHeightChange();
        else if (ZoomLog && Input.GetAxis("Mouse ScrollWheel") != 0f)
            Debug.Log($"[Zoom] scroll IGNORED — menuOpen={menuOpen} planetView={planetViewOpen} overScroller={overScroller}.");

        // F9 = dump the whole zoom chain. See LogZoomReport.
        if (Input.GetKeyDown(KeyCode.F9))
        {
            ZoomLog = !ZoomLog;
            LogZoomReport(ZoomLog ? "logging ON" : "logging OFF (final report)");
        }

        // Home = pull back to the whole galaxy. A one-key way to see the entire map.
        if (!menuOpen && Input.GetKeyDown(KeyCode.Home)) ViewWholeGalaxy();

        // F / Q = focus the current selection (fly to it and lock on). Selection no longer auto-focuses the
        // camera, so this and the Inspector's "Focus" button are how you deliberately fly to a body/star.
        // A selected planet goes through PlanetUI.Selected; a selected star/facility/etc. through the
        // Inspector's current subject. Suppressed while typing into a text field so renaming a world (or a
        // save name) doesn't yank the camera.
        bool typingField = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                           EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null;
        if (!menuOpen && !typingField && (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Q)))
        {
            var sel = PlanetUI.Selected;
            if (sel != null && sel.visualObject != null)
                FocusAndZoom(sel.visualObject.transform, sel.surfaceSize, true);
            else
                InspectorWindow.Instance?.FocusCurrent();   // a selected star / facility / etc.
        }

        SmoothHeightMovement();   // identical zoom smoothing whether following or not
        KeepCameraAngle();
        UpdateClipPlanes();       // the view volume has to keep up with the zoom, or the galaxy vanishes
    }

    private void LateUpdate()
    {
        // Recenter on the followed body after orbits move it (zoom is already smoothed in Update).
        if (following && followTarget != null)
        {
            FocusOn(followTarget.position);
            KeepCameraAngle();
        }
    }

    // Snap onto an object and zoom until it FILLS the view — a small moon gets a close look, a giant
    // gets a wider one, automatically. See FillHeight for how close, and why.
    //
    // `objectSizeHint` is now only a fallback. It used to be the whole calculation
    //     targetHeight = 3.5 + objectSize * 0.45   (clamped 3.5 .. 22)
    // which was wrong twice over. First, callers disagreed about what it MEANT: bodies passed
    // surfaceSize (a gameplay number, 3-28), the star passed lossyScale.x (a world diameter), tokens
    // passed a literal 3 — all fed to one formula as if they were the same unit. Second, a planet is
    // only ~0.6-2.2 world units across, so parking the camera 9.8 units up left it a speck.
    //
    // The object's real size is a property of the object, so we measure it: renderer bounds first,
    // falling back to scale, and only then to the hint.
    public void FocusAndZoom(Transform target, float objectSizeHint, bool follow)
    {
        followTarget = target;
        following = follow;

        float radius = WorldRadius(target, objectSizeHint);
        // For a PLANET, frame its whole neighbourhood — the planet AND its moons / orbiting stations —
        // rather than filling the screen with just the globe (which read as "too zoomed in"). Use the
        // system's orbital reach (its outermost moon); for a moonless world still pull back a couple of
        // radii so there's room to see whatever is in orbit around it. The zoom FLOOR is unchanged (it's
        // still the planet's own surface), so you can hand-zoom right down onto it afterwards — even during
        // an orbit migration: focus follows the planet and zoom is bounded only by the planet's surface and
        // the galaxy ceiling, never by keeping the star in view, so you can zoom right in as it moves.
        var pc = target != null ? target.GetComponent<PlanetClick>() : null;
        if (pc != null && pc.data != null)
            radius = Mathf.Max(OrbitSafety.SystemReach(pc.data), radius * 2.2f);

        targetHeight = Mathf.Clamp(FillHeight(radius), minHeight, maxHeight);
        if (target == null) return;

        FocusOn(target.position);

        // Aim the EASE at the target as well, not just the snap.
        //
        // FocusOn centres the target for the height the camera is at RIGHT NOW. The height then eases to
        // targetHeight while x/z sit still — and the point the camera looks at is a function of its
        // height, so the target slides off centre as the zoom comes in. It ends up framed at the size you
        // asked for and no longer under the middle of the screen.
        //
        // Following hid this: LateUpdate re-centres a followed body every frame, so it's corrected
        // continuously. Stars pass follow=false, nothing re-centred them, and clicking a star zoomed
        // toward where it USED to be — which is the "doesn't zoom in on the star correctly" bug.
        //
        // Same anchor the cursor zoom uses (see HandleHeightChange), aimed at the screen centre instead
        // of the pointer: solve where the camera must be at targetHeight for the target to be centred,
        // and let the one easing carry x, y and z there together.
        if (!follow) AnchorOnCentre(target.position, targetHeight);
    }

    /// Point the ease at `worldPos` being centred once the camera reaches `atHeight`.
    void AnchorOnCentre(Vector3 worldPos, float atHeight)
    {
        Vector3 f = transform.forward;
        if (f.y > -0.0001f) return;              // not looking down: nothing to solve
        float kx = f.x / f.y, kz = f.z / f.y;    // same xz/y slope the cursor anchor uses
        anchorXZ = new Vector2(worldPos.x + atHeight * kx, worldPos.z + atHeight * kz);
        haveAnchor = true;
    }

    // ============================================================================================
    // HOW CLOSE "FOCUS ON THIS" GETS — expressed as a multiple of the object's own radius.
    //
    // The whole geometry, once, so these numbers can be read rather than guessed at:
    //   camera distance    d = h / sin(Pitch)          (Pitch = 55, so d = 1.221 * h)
    //   angular size       a = 2 * asin(R / d)
    //   the SURFACE is at  d = R,  i.e.  h = R * sin(55) = 0.819 * R   <- a hard wall
    //
    //   h = 2.01R -> 48 deg -> fills  81% of the 60 deg FOV   (what this used to do)
    //   h = 1.30R -> 78 deg -> fills 130%                     (overflows the screen)
    //   h = 1.00R -> 110 deg -> fills 183%
    //   h = 0.82R -> the camera is touching the surface
    //
    // 2.01R was the old "fill 80% of the FOV" target, and 80% of the screen is exactly the framing you
    // want to start zooming in FROM, not to land on. Worse, it made clicking a body you were already
    // close to PULL THE CAMERA BACK to it — you'd zoom in by hand, click the planet to look at it, and
    // get yanked away. Landing above 1.0R means every focus overflows the view: the body is the screen.
    //
    // Small things get proportionally closer still. A moon at 1.0R and a gas giant at 1.3R both overfill
    // the view, but the moon lets you right down onto it — which is what "even more zoomed in for
    // smaller planets and moons and ships" means, and it can't come from a single multiplier, because a
    // single multiplier IS proportional and would treat them identically.
    // ============================================================================================

    const float FocusKSmall = 1.00f;   // R <= FocusRSmall: nearly on the surface
    const float FocusKLarge = 1.30f;   // R >= FocusRLarge: still overflows the screen
    const float FocusRSmall = 0.2f;    // a ship token / small moon
    const float FocusRLarge = 1.3f;    // a gas giant

    /// The height at which an object of this radius FILLS the view (and then some).
    public float FillHeight(float radius)
    {
        float k = Mathf.Lerp(FocusKSmall, FocusKLarge,
                             Mathf.InverseLerp(FocusRSmall, FocusRLarge, radius));
        return radius * k;
    }

    /// An object's actual radius in world units. Bounds are authoritative (they account for children
    /// and any scaling); lossyScale is the fallback; the caller's hint is the last resort.
    public static float WorldRadius(Transform t, float hint = 1f)
    {
        if (t != null)
        {
            // Measure the BODY, not its decorations. GetComponentsInChildren would pull in the atmosphere
            // shell, floating labels, fog, AND the orbit-ring LineRenderer — which spans the whole orbit,
            // tens of units across. Encapsulating those set the zoom floor way out at the orbit's scale, so
            // you could never zoom in on the planet at all — the "zoom is broken when a planet is focused"
            // bug. The body's OWN renderer (or, failing that, its transform scale, which is set to the body
            // size) is the true radius, and it excludes every child decoration.
            // The first NON-LINE renderer on the body itself — its sphere mesh. Skipping LineRenderer /
            // TrailRenderer guards against a stray line component (e.g. a prefab's leftover world-space
            // ring) being measured instead of the body, which would blow the radius up to orbit scale and
            // stop you zooming in at all.
            Renderer own = null;
            foreach (var rr in t.GetComponents<Renderer>())
                if (rr != null && !(rr is LineRenderer) && !(rr is TrailRenderer)) { own = rr; break; }
            if (own != null)
            {
                var e = own.bounds.extents;
                float r = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
                if (r > 0.0001f) return r;
            }
            float s = t.lossyScale.x * 0.5f;
            if (s > 0.0001f) return s;
        }
        return Mathf.Max(0.25f, hint * 0.5f);
    }

    // Jump to a world position at a chosen height (used by the galaxy view's "Go to system").
    public void JumpTo(Vector3 worldPos, float height)
    {
        ClearFocus();
        targetHeight = Mathf.Clamp(height, minHeight, maxHeight);
        var p = transform.position; p.y = targetHeight; transform.position = p;
        FocusOn(worldPos);
    }

    public float Height => transform.position.y;

    public void SetFollow(bool on) { following = on; }
    public bool IsFollowing => following;
    public void ClearFocus() { following = false; followTarget = null; }

    private void HandlePanning()
    {
        // Raw (no built-in smoothing) so the speed depends only on our unscaled multiplier.
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        // Unscaled time so panning speed is constant regardless of the simulation speed (and works
        // while paused). Scale with height so panning stays usable when zoomed far out — the cap is
        // generous enough to cross the whole galaxy at galaxy-scale zoom without feeling glacial.
        if (h == 0f && v == 0f) return;

        // Panning is the player saying where to look, which overrides where the zoom was heading. Without
        // this the anchor keeps easing X/Z back and WASD fights it.
        ClearAnchor();

        float heightFactor = Mathf.Clamp(transform.position.y / 20f, 1f, 200f);

        // Rotated into the camera's own bearing, so W is always "away from the viewer" whichever way the
        // view has been spun. Panning in raw world axes was correct only while yaw was locked at 0; the
        // moment the player rotates, unrotated WASD sends the camera sideways relative to what they see,
        // which reads as broken controls rather than as a different heading.
        Vector3 move = Quaternion.Euler(0f, yaw, 0f) * new Vector3(h, 0f, v);
        move *= panSpeed * heightFactor * Time.unscaledDeltaTime;
        transform.Translate(move, Space.World);
    }

    // Zoom is PROPORTIONAL: each notch changes the height by a percentage rather than a fixed amount.
    // A fixed step can't serve both a 4-unit close-up of a moon and a 2,500-unit galaxy view — it's
    // either unusably coarse up close or takes a hundred scrolls to pull back. This makes one scroll
    // feel the same at every scale, which is what actually lets you reach the whole map by hand.
    private void HandleHeightChange()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll == 0) return;

        float factor = Mathf.Exp(-scroll * 6f);      // scroll up -> factor < 1 -> closer
        float floor = ZoomFloor();
        float want = targetHeight * factor;
        float ceiling = ZoomCeiling();
        float got = Mathf.Clamp(want, floor, ceiling);

        if (ZoomLog)
        {
            // The line that matters: what the wheel ASKED for, what it GOT, and which limit ate the
            // difference. A dump of every value is a haystack; naming the binding constraint is the
            // answer. This one line, printed once, is what would have found the eight months the scene
            // was overriding maxHeight to 80 while this file said 120000.
            string why = got < want - 0.001f ? $"CLAMPED BY CEILING (ZoomCeiling={ceiling:F0}, maxHeight={maxHeight:F0})"
                       : got > want + 0.001f ? $"CLAMPED BY FLOOR (ZoomFloor={floor:F2})"
                       : "free";
            Debug.Log($"[Zoom] scroll={scroll:F3} x{factor:F2}  {targetHeight:F1} -> want {want:F1} -> got {got:F1}  [{why}]");
        }

        // Solve the cursor anchor ONCE, here, for the height we're heading to — rather than nudging the
        // camera sideways a little every frame on the way there. See SmoothHeightMovement.
        if (!following && ZoomToCursor && CursorGround(out Vector3 g, out float kx, out float kz))
        {
            // Ground under the cursor is  hit = pos - h * k  (k = dir.xz / dir.y), so the camera has to
            // be at  pos = hit + h * k  for that same ground to still be under the cursor at height h.
            anchorXZ = new Vector2(g.x + got * kx, g.z + got * kz);
            haveAnchor = true;
        }

        targetHeight = got;
    }

    // Where the zoom is heading in X/Z, so the point under the cursor lands under the cursor.
    Vector2 anchorXZ;
    bool haveAnchor;

    /// Ground point under the cursor, plus the ray's xz/y slope — everything needed to solve for where
    /// the camera must be at any other height.
    bool CursorGround(out Vector3 ground, out float kx, out float kz)
    {
        ground = default; kx = kz = 0f;
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return false;

        Vector3 dir = cam.ScreenPointToRay(Input.mousePosition).direction;
        if (dir.y > -0.0001f) return false;      // level with or above the horizon: never meets the plane

        kx = dir.x / dir.y;
        kz = dir.z / dir.y;

        Vector3 pos = transform.position;
        ground = new Vector3(pos.x - pos.y * kx, 0f, pos.z - pos.y * kz);
        return true;
    }

    /// Drop the anchor — the camera is being sent somewhere else and shouldn't drift back.
    void ClearAnchor() { haveAnchor = false; }

    // ============================================================================================
    // ZOOM DIAGNOSTICS — F9 toggles.
    //
    // Prints the whole chain in one place, because the zoom's behaviour is the product of about eight
    // numbers that live in five different files, and reading any one of them in isolation is how you end
    // up "fixing" the wrong one. Repeatedly.
    // ============================================================================================
    public static bool ZoomLog;

    public void LogZoomReport(string reason)
    {
        if (cam == null) cam = GetComponent<Camera>();

        float floor = ZoomFloor();
        float h = transform.position.y;
        float gr = GalaxyRadius();
        float frame = HeightToFrame(gr);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"===== ZOOM REPORT ({reason}) =====");
        sb.AppendLine($"  height        {h:F2}        target {targetHeight:F2}");
        // The EFFECTIVE ceiling, not maxHeight. The wheel is clamped by ZoomCeiling (a few thousand);
        // maxHeight is a 120,000 backstop that never binds. Reporting the backstop here would recreate
        // this file's original sin in diagnostic form — printing a number that is not the one in force,
        // and computing a "12x further out available" that the wheel will refuse to deliver.
        float ceiling = ZoomCeiling();
        sb.AppendLine($"  LIMITS        floor {floor:F2}  ..  ceiling {ceiling:F0}     range x{(floor > 0.001f ? ceiling / floor : 0f):F0}");
        sb.AppendLine($"    minHeight         {minHeight}       (const - the scene cannot override it)");
        sb.AppendLine($"    freeLookMinHeight {freeLookMinHeight}");
        sb.AppendLine($"    ZoomCeiling       {ceiling:F0}   (galaxy-derived; what the wheel actually obeys)");
        sb.AppendLine($"    maxHeight         {maxHeight}   (absolute backstop, does not bind)");

        // Which end are we sitting on? "At the ceiling" is the single most useful fact here, and it's
        // what an isolated height reading never tells you.
        if (h >= ceiling * 0.999f) sb.AppendLine("  >> AT THE CEILING. Cannot zoom out further.");
        else if (h <= floor * 1.001f) sb.AppendLine("  >> AT THE FLOOR. Cannot zoom in further.");
        else sb.AppendLine($"  >> free: {ceiling / Mathf.Max(0.001f, h):F1}x further out available, " +
                           $"{h / Mathf.Max(0.001f, floor):F1}x further in");

        sb.AppendLine($"  FOCUS         following={following} target={(followTarget != null ? followTarget.name : "<none>")}");
        if (followTarget != null)
        {
            float r = WorldRadius(followTarget);
            sb.AppendLine($"                radius {r:F3}   focus lands at {FillHeight(r):F3} ({FillHeight(r) / Mathf.Max(0.0001f, r):F2}x radius)");
            sb.AppendLine($"                surface at {r * 0.819f:F3}   floor {r * SurfaceK:F3} ({SurfaceK}x radius)");
        }

        sb.AppendLine($"  GALAXY        radius {gr:F1}   frames entirely at height {frame:F1}");
        sb.AppendLine($"                (so a ceiling below {frame:F0} makes the whole map unreachable)");
        sb.AppendLine($"  CLIP          near {cam?.nearClipPlane:F3}  far {cam?.farClipPlane:F0}   fov {cam?.fieldOfView:F0}  pitch {Pitch}");
        sb.AppendLine($"  INPUT         overUI={(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())}  " +
                      $"scrollAxis={Input.GetAxis("Mouse ScrollWheel"):F3}");
        Debug.Log(sb.ToString());
    }

    // How close the wheel may get.
    //
    // With something focused, the limit comes from THAT OBJECT: you may zoom until it fills the view
    // and then a little closer, so a moon lets you in much further than a gas giant. With nothing
    // focused there's no subject to frame, so it stops at a sensible free-look height.
    private float ZoomFloor()
    {
        if (followTarget == null) return Mathf.Max(minHeight, freeLookMinHeight);

        // Stop just off the SURFACE of the thing you're looking at. Closer than the surface is INSIDE
        // it, where there is nothing to see but backfaces and the near plane — this is the hard limit on
        // zooming in on a body, and it comes from geometry, not taste.
        //
        // Derived from the RADIUS, not from FillHeight. It used to be `fill * 0.44`, which only worked
        // because fill happened to be 2.01R at the time (0.44 * 2.01 = 0.88). The moment FillHeight's
        // framing changed, that fraction silently meant something else — at fill = 1.3R it would put the
        // floor at 0.57R, well inside the planet. A limit defined as a fraction of a framing choice
        // isn't a limit, it's a coincidence waiting to be broken.
        float r = WorldRadius(followTarget);
        return Mathf.Max(minHeight, r * SurfaceK);
    }

    /// Height, as a multiple of the target's radius, at which the closest hand-zoom stops.
    /// The camera grazes the surface at exactly sin(Pitch) = 0.819·R; stopping at 0.90·R (a hair above
    /// that) let you zoom until the globe overflowed the screen and read as "inside the surface". Pulled
    /// back to 1.8·R so the tightest zoom instead frames the WHOLE planet filling most of the centre of
    /// the view with a thin rim around it — close enough to read its terrain, far enough to see the whole
    /// world rather than its crust (at 1.6·R the disc just overflowed a 60° FOV; 1.8·R leaves the margin).
    const float SurfaceK = 1.8f;

    // ============================================================================================
    // ZOOM TOWARD THE CURSOR
    //
    // Whatever is under the pointer stays under the pointer as the height changes. Without it, zooming
    // always converges on the screen centre, so reaching a system in the corner is zoom-pan-zoom-pan
    // instead of just pointing at it and scrolling.
    //
    // Done HERE rather than in HandleHeightChange, and that's the crux: the height is SMOOTHED, so the
    // frame the wheel is read is not the frame the camera actually moves. Correcting at wheel-time would
    // apply the whole shift instantly while the height eased in behind it, and the point under the cursor
    // would visibly slide. Correcting per-frame against the height that ACTUALLY changed keeps them
    // locked together however the smoothing behaves.
    //
    // The maths: the camera looks along `forward` at Pitch, so the ground point under a screen ray is
    // found by walking that ray to y=0. Do it before the height changes and again after; the difference
    // is how far the world slid, so translating back by it pins the point.
    // ============================================================================================
    // ONE EASING, ALL THREE AXES — and this is what killed the zoom jitter.
    //
    // The first cut anchored the cursor by DIFFERENCING per frame: find the ground point under the
    // cursor, move the height a step, find it again, translate by the difference. It's exactly correct
    // on paper and it jittered visibly, because of what it does to frame-time noise.
    //
    // The height step is Lerp(y, target, rate * dt), so it's proportional to dt, and dt is never
    // steady — 8ms, 13ms, 9ms. Jitter in the height step is invisible: it just means the zoom advances
    // slightly unevenly, and nobody can see a 2% variation in zoom speed. But the differencing CONVERTS
    // that jitter into horizontal camera movement, and sideways judder against a starfield is one of the
    // most visible things there is. The bug wasn't the anchoring maths, it was feeding frame-time noise
    // into an axis the eye is good at.
    //
    // So: solve the destination ONCE when the wheel turns (HandleHeightChange), then ease X, Y and Z
    // toward it with a single factor. Now dt noise only affects how fast we approach — which is the
    // invisible kind again — and the anchor is exact at the destination rather than accumulated.
    //
    // The easing is also genuinely frame-rate independent now: 1 - exp(-rate*dt) is the real form. The
    // old `rate * dt` was linear in dt and only approximates it for small dt, so the effective smoothing
    // changed with framerate — the comment claimed "time-independent" while the code wasn't.
    private void SmoothHeightMovement()
    {
        float k = 1f - Mathf.Exp(-ZoomEaseRate * Time.unscaledDeltaTime);
        Vector3 pos = transform.position;

        pos.y = Mathf.Lerp(pos.y, targetHeight, k);

        // Following pins X/Z to the body (LateUpdate re-centres every frame), so an anchor would only
        // fight it.
        if (haveAnchor && !following)
        {
            pos.x = Mathf.Lerp(pos.x, anchorXZ.x, k);
            pos.z = Mathf.Lerp(pos.z, anchorXZ.y, k);
        }

        transform.position = pos;
    }

    /// How fast the camera converges on its zoom target, in e-folds per second.
    const float ZoomEaseRate = 10f;

    /// Toggle for cursor-anchored zoom. On by default; here so the behaviour has a name.
    public static bool ZoomToCursor = true;

    private void KeepCameraAngle()
    {
        // Fixed top-down pitch, free yaw. See the Yaw region below.
        transform.rotation = Quaternion.Euler(Pitch, yaw, 0);
    }

    // ============================================================================================
    // YAW — spinning the view around.
    //
    // The camera was locked to a single compass bearing: KeepCameraAngle rebuilt the rotation every frame
    // from `transform.eulerAngles.y`, which nothing ever wrote, so it was always 0. Yaw is now real state.
    //
    // PITCH STAYS FIXED at 55 degrees, deliberately. It is a `const` that HeightToFrame and UpdateClipPlanes
    // both solve geometry against, and every render-tier boundary in GalaxyLOD is derived from
    // HeightToFrame — so making pitch adjustable would move the framing height, and with it the zoom
    // thresholds, every time the player tilted the view. Yaw has no such coupling: spinning about the
    // world's up axis leaves every one of those distances unchanged.
    // ============================================================================================

    float yaw;

    /// Current bearing in degrees. Setting it takes effect on the next frame's KeepCameraAngle.
    public float Yaw
    {
        get => yaw;
        set { yaw = Mathf.Repeat(value, 360f); KeepCameraAngle(); }
    }

    /// Spin the view, ORBITING the point currently at screen centre rather than the camera's own position.
    ///
    /// This distinction is the whole feature. Rotating in place pivots about the camera, and because the
    /// camera looks down at 55 degrees it sits `h / tan(55)` ≈ 0.7*h to one side of what it is actually
    /// looking at — so at galaxy zoom a 90-degree turn swings the thing you were studying thousands of
    /// units across the screen, and a half-turn throws the whole galaxy out of frame. Orbiting the
    /// look-at point keeps the subject nailed to the middle, which is what "spin the view around" means.
    public void RotateBy(float degrees)
    {
        if (Mathf.Abs(degrees) < 0.0001f) return;

        Vector3 pivot = GroundAtScreenCentre();
        Vector3 offset = transform.position - pivot;
        // Rotate the camera's offset from the pivot about world up, then set the bearing to match.
        offset = Quaternion.AngleAxis(degrees, Vector3.up) * offset;
        transform.position = pivot + offset;

        // A yaw change invalidates a zoom anchor solved for the previous bearing — otherwise the eased
        // X/Z keeps pulling toward a point that no longer means what it did.
        ClearAnchor();
        Yaw = yaw + degrees;
    }

    public void ResetRotation() => RotateBy(-yaw);

    /// The point on the y=0 plane under the middle of the screen — what the camera is looking at.
    Vector3 GroundAtScreenCentre()
    {
        Vector3 p = transform.position;
        Vector3 f = transform.forward;

        // Guard a level or upward-facing camera; with the pitch fixed at 55 this cannot happen, but the
        // division is the kind that turns into a NaN position if it ever does.
        if (f.y > -0.01f) return new Vector3(p.x, 0f, p.z);

        float t = p.y / -f.y;
        return p + f * t;
    }

    /// One notch of zoom, for UI buttons. Positive pulls out, negative pushes in — the same proportional
    /// step the wheel uses, and clamped by the same floor and ceiling, so a button press and a wheel click
    /// are the same action.
    public void NudgeZoom(int notches)
    {
        if (notches == 0) return;
        // Same as panning and rotating: a deliberate camera move overrides wherever the last cursor-
        // anchored zoom was still easing toward, or X/Z keeps drifting to a stale anchor point.
        ClearAnchor();
        float factor = Mathf.Exp(notches * 0.35f);
        targetHeight = Mathf.Clamp(targetHeight * factor, ZoomFloor(), ZoomCeiling());
        UpdateClipPlanes();
    }

    /// Degrees per second while a rotate button is held, and per 100px of middle-drag.
    public const float RotateSpeed = 90f;
    const float RotateDragPerPixel = 0.35f;

    bool dragRotating;
    Vector3 lastDragPos;

    // Middle-mouse drag spins the view. Middle button because left is selection and right is already
    // taken by structure rotation in the Planet View — this is the one button with nothing on it.
    void HandleRotationInput()
    {
        if (Input.GetMouseButtonDown(2))
        {
            dragRotating = true;
            lastDragPos = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(2))
        {
            dragRotating = false;
        }

        if (dragRotating)
        {
            Vector3 d = Input.mousePosition - lastDragPos;
            lastDragPos = Input.mousePosition;
            if (Mathf.Abs(d.x) > 0.01f) RotateBy(d.x * RotateDragPerPixel);
        }
    }
}