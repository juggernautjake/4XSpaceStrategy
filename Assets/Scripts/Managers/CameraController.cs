using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    public float panSpeed = 30f;           // WASD panning speed
    public float heightSpeed = 80f;        // Mouse wheel height change speed

    [Header("Limits")]
    public float minHeight = 4f;           // Closest to the system
    public float maxHeight = 900f;         // Farthest view (room to see the whole galaxy)

    private float targetHeight;            // For smooth movement

    public static CameraController Instance;
    private void Awake() { Instance = this; }

    private void Start()
    {
        targetHeight = transform.position.y;
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
        targetHeight = h;
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
        bool canPan = !following && !menuOpen && !planetSelected;
        if (canPan) HandlePanning();

        // Mouse-wheel zoom is suppressed only when the cursor is over UI (so scrolling a window
        // doesn't also zoom the world) and while the menu is open.
        if (!overUI && !menuOpen) HandleHeightChange();

        SmoothHeightMovement();   // identical zoom smoothing whether following or not
        KeepCameraAngle();
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

    public void SetFollow(bool on) { following = on; }
    public bool IsFollowing => following;
    public void ClearFocus() { following = false; followTarget = null; }

    private void HandlePanning()
    {
        // Raw (no built-in smoothing) so the speed depends only on our unscaled multiplier.
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        // Unscaled time so panning speed is constant regardless of the simulation speed (and works
        // while paused). Scale with height so panning stays usable when zoomed far out.
        float heightFactor = Mathf.Clamp(transform.position.y / 20f, 1f, 20f);
        Vector3 move = new Vector3(h, 0, v) * panSpeed * heightFactor * Time.unscaledDeltaTime;
        transform.Translate(move, Space.World);
    }

    private void HandleHeightChange()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            targetHeight += scroll * heightSpeed * -25f; // Negative for natural feel
            targetHeight = Mathf.Clamp(targetHeight, minHeight, maxHeight);
        }
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