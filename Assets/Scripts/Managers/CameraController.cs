using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    public float panSpeed = 30f;           // WASD panning speed
    public float heightSpeed = 130f;       // Mouse wheel height change speed

    [Header("Limits")]
    public float minHeight = 4f;           // Closest to the system
    public float maxHeight = 9000f;        // Farthest view — pull right back to a galaxy-wide symbol map

    private float targetHeight;            // For smooth movement

    public static CameraController Instance;
    private Camera cam;

    // The camera's far clip plane is the thing that actually decides how far out you can see. The scene
    // ships with Unity's default of 1000, so pulling back past that height quietly clipped the entire
    // galaxy away and made zooming out look broken. We now drive it from the current height every frame,
    // which is what lets you pull all the way back to the whole map.
    const float MinFarClip = 1200f;

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

    // The height needed to frame a disc of this radius, given the camera's pitch and vertical FOV.
    // Includes generous margin so the outermost systems aren't sitting on the screen edge.
    public float HeightToFrame(float radius)
    {
        if (cam == null) cam = GetComponent<Camera>();
        float fov = cam != null ? cam.fieldOfView : 60f;
        float halfFovRad = fov * 0.5f * Mathf.Deg2Rad;
        // Rough but reliable: the ground half-extent visible scales with height / tan(halfFov), adjusted
        // for the fixed 55° pitch which stretches the view along the forward axis.
        float needed = (radius * 1.35f) / Mathf.Max(0.05f, Mathf.Tan(halfFovRad));
        return Mathf.Clamp(needed * Mathf.Sin(55f * Mathf.Deg2Rad), 50f, maxHeight);
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
        float slant = h / Mathf.Sin(55f * Mathf.Deg2Rad);
        cam.farClipPlane = Mathf.Max(MinFarClip, slant + GalaxyRadius() * 2.5f + 1000f);
        // Near plane has to grow with distance or z-fighting sets in at galaxy scale.
        cam.nearClipPlane = Mathf.Clamp(h * 0.002f, 0.03f, 5f);
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

    // Snap onto and zoom close to an object; smaller objects get a closer view.
    public void FocusAndZoom(Transform target, float objectSize, bool follow)
    {
        followTarget = target;
        following = follow;
        targetHeight = Mathf.Clamp(3.5f + objectSize * 0.45f, 3.5f, 22f);
        if (target != null) FocusOn(target.position);
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
        targetHeight = Mathf.Clamp(targetHeight * factor, minHeight, ZoomCeiling());
    }

    // The furthest you can pull back: far enough to frame the whole generated galaxy with margin, and
    // no further (staring at empty space isn't useful). Falls back to the inspector limit pre-galaxy.
    private float ZoomCeiling()
    {
        float radius = GalaxyRadius();
        if (radius <= 0f) return Mathf.Min(maxHeight, 1600f);
        return Mathf.Min(maxHeight, HeightToFrame(radius) * 1.15f);
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
        transform.rotation = Quaternion.Euler(55f, transform.eulerAngles.y, 0);
    }
}