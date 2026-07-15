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
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool menuOpen = (EscapeMenu.Instance != null && EscapeMenu.Instance.IsOpen)
                     || (StartMenu.Instance != null && StartMenu.Instance.IsOpen);
        bool planetSelected = PlanetUI.Selected != null;

        // WASD pans the camera ONLY when nothing else wants the keys:
        //  - a planet is selected  -> WASD drives the mini-map tile cursor instead
        //  - the pause menu is open -> WASD navigates the menu instead
        //  - following an object    -> camera is locked
        // ...except once you're pulled back to a system/galaxy-wide view, where the mini-map cursor is
        // irrelevant and being unable to pan across the map is simply broken.
        bool wideView = transform.position.y > 120f;
        bool canPan = !following && !menuOpen && (!planetSelected || wideView);
        if (canPan) HandlePanning();

        // Mouse-wheel zoom is suppressed only when the cursor is over UI (so scrolling a window
        // doesn't also zoom the world) and while the menu is open.
        if (!overUI && !menuOpen) HandleHeightChange();
        else if (ZoomLog && Input.GetAxis("Mouse ScrollWheel") != 0f)
            Debug.Log($"[Zoom] scroll IGNORED — overUI={overUI} menuOpen={menuOpen}. " +
                      "The wheel never reached the camera.");

        // F9 = dump the whole zoom chain. See LogZoomReport.
        if (Input.GetKeyDown(KeyCode.F9))
        {
            ZoomLog = !ZoomLog;
            LogZoomReport(ZoomLog ? "logging ON" : "logging OFF (final report)");
        }

        // Home = pull back to the whole galaxy. A one-key way to see the entire map.
        if (!menuOpen && Input.GetKeyDown(KeyCode.Home)) ViewWholeGalaxy();

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
        targetHeight = Mathf.Clamp(FillHeight(WorldRadius(target, objectSizeHint)), minHeight, maxHeight);
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
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float r = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
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
        Vector3 move = new Vector3(h, 0, v) * panSpeed * heightFactor * Time.unscaledDeltaTime;
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
        float got = Mathf.Clamp(want, floor, maxHeight);

        if (ZoomLog)
        {
            // The line that matters: what the wheel ASKED for, what it GOT, and which limit ate the
            // difference. A dump of every value is a haystack; naming the binding constraint is the
            // answer. This one line, printed once, is what would have found the eight months the scene
            // was overriding maxHeight to 80 while this file said 120000.
            string why = got < want - 0.001f ? $"CLAMPED BY CEILING (maxHeight={maxHeight:F0})"
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
        sb.AppendLine($"  LIMITS        floor {floor:F2}  ..  ceiling {maxHeight:F0}     range x{(floor > 0.001f ? maxHeight / floor : 0f):F0}");
        sb.AppendLine($"    minHeight         {minHeight}       (const - the scene cannot override it)");
        sb.AppendLine($"    freeLookMinHeight {freeLookMinHeight}");
        sb.AppendLine($"    maxHeight         {maxHeight}");

        // Which end are we sitting on? "At the ceiling" is the single most useful fact here, and it's
        // what an isolated height reading never tells you.
        if (h >= maxHeight * 0.999f) sb.AppendLine("  >> AT THE CEILING. Cannot zoom out further.");
        else if (h <= floor * 1.001f) sb.AppendLine("  >> AT THE FLOOR. Cannot zoom in further.");
        else sb.AppendLine($"  >> free: {maxHeight / Mathf.Max(0.001f, h):F1}x further out available, " +
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

    /// Height, as a multiple of the target's radius, at which the camera grazes its surface.
    /// The surface is exactly sin(Pitch) = 0.819; the extra is clearance so it's a near miss rather than
    /// a plane through the crust.
    const float SurfaceK = 0.90f;

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
        // Keep a nice 55 degree top-down angle
        transform.rotation = Quaternion.Euler(Pitch, transform.eulerAngles.y, 0);
    }
}