using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    public float panSpeed = 30f;           // WASD panning speed
    public float heightSpeed = 130f;       // Mouse wheel height change speed

    [Header("Limits")]
    // The absolute floor. Low, because a moon is only ~0.35 world units across and filling the screen
    // with one genuinely needs the camera this close. It was 4, which is further away than some whole
    // planets are wide — you could never get near anything.
    public float minHeight = 0.08f;
    // The floor when nothing is selected. Kept off the orbital plane so free-look never ends up inside
    // it, but low enough to get right down among the bodies — it was 3, which is further away than a
    // whole planet is wide, so free-look could never get close to anything.
    public float freeLookMinHeight = 0.7f;
    // Farthest view. Generous on purpose: this is a hard ceiling on how far back you can ever pull, and
    // the only cost of headroom is a bigger far clip, which UpdateClipPlanes already tracks. The galaxy
    // is framed by HeightToFrame, not by this — so this only ever needs to be comfortably beyond it.
    public float maxHeight = 60000f;

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
        cam.farClipPlane = Mathf.Max(MinFarClip, slant + GalaxyRadius() * 2.5f + 1000f);
        // Near plane has to grow with distance or z-fighting sets in at galaxy scale. What matters for
        // depth precision is the far:near RATIO, not either number, so the near plane has to keep pace
        // as the ceiling rises — pinning it at 5 while the far plane runs to ~74,000 is a 15,000:1 ratio
        // and the depth buffer starts tearing. Nothing is within 100 units of the camera when it's
        // 60,000 up, so a near plane that far out clips nothing you can see.
        cam.nearClipPlane = Mathf.Clamp(h * 0.002f, 0.02f, 120f);
    }

    // Recenters the view on a world position (used by notifications to jump to a discovery).
    public void FocusOn(Vector3 worldPos)
    {
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
    // gets a wider one, automatically.
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
        if (target != null) FocusOn(target.position);
    }

    /// The height at which an object of this radius fills most of the view.
    ///
    /// Solve it rather than guess: at distance d an object of radius R subtends asin(R/d), so to fill
    /// ~80% of the vertical FOV we need d = R / sin(0.8 * fov/2). The camera sits at Pitch above the
    /// plane, so h = d * sin(Pitch).
    public float FillHeight(float radius)
    {
        if (cam == null) cam = GetComponent<Camera>();
        float fov = cam != null ? cam.fieldOfView : 60f;
        float halfAngle = fov * 0.5f * 0.8f * Mathf.Deg2Rad;
        float d = radius / Mathf.Max(0.05f, Mathf.Sin(halfAngle));
        return d * Mathf.Sin(Pitch * Mathf.Deg2Rad);
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
        targetHeight = Mathf.Clamp(targetHeight * factor, ZoomFloor(), maxHeight);
    }

    // How close the wheel may get.
    //
    // With something focused, the limit comes from THAT OBJECT: you may zoom until it fills the view
    // and then a little closer, so a moon lets you in much further than a gas giant. With nothing
    // focused there's no subject to frame, so it stops at a sensible free-look height.
    private float ZoomFloor()
    {
        if (followTarget == null) return Mathf.Max(minHeight, freeLookMinHeight);
        float fill = FillHeight(WorldRadius(followTarget));
        // A fifth of the fill height: close enough that the body overflows the screen and you're looking
        // at its surface rather than at it. This was half, which stopped you at roughly "it fills the
        // view" — the point you'd want to start zooming in FROM.
        return Mathf.Max(minHeight, fill * 0.2f);
    }

    private void SmoothHeightMovement()
    {
        Vector3 pos = transform.position;
        pos.y = Mathf.Lerp(pos.y, targetHeight, 10f * Time.unscaledDeltaTime); // Smoothness (time-independent)
        transform.position = pos;
    }

    private void KeepCameraAngle()
    {
        // Keep a nice 55 degree top-down angle
        transform.rotation = Quaternion.Euler(Pitch, transform.eulerAngles.y, 0);
    }
}