using UnityEngine;

// Logs what the mouse ray hits — a tracing aid, silent by default. It used to fire on every click in Dev
// Mode, which now doubles as the sandbox mode, so it buried the console in "Raycast hit: X" while you were
// dragging the terrain/orbit sliders. Flip `Verbose` on in the debugger (or from code) when you actually
// want to trace what the ray hits; otherwise it stays quiet.
public class MouseRaycastDebugger : MonoBehaviour
{
    public static bool Verbose = false;

    void Update()
    {
        if (!Verbose || !GameMode.DevMode) return;
        if (!Input.GetMouseButtonDown(0)) return;
        var cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // Only HITS are worth a line. Logging every miss too meant that clicking empty space — which is
        // most clicks, since the map is mostly empty space — printed "Raycast hit NOTHING" over and
        // over, drowning the console in a non-event. A miss is the absence of information; if nothing
        // is logged for a click, nothing was under it.
        if (Physics.Raycast(ray, out RaycastHit hit))
            Debug.Log("Raycast hit: " + hit.collider.gameObject.name);
    }
}