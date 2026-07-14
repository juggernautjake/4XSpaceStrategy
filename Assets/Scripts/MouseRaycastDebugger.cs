using UnityEngine;

// Logs what the mouse ray hits. Kept as a tracing aid, but gated behind Dev Mode: it fired on EVERY
// left click, so in normal play it buried the console in "Raycast hit NOTHING" and made real warnings
// impossible to spot. Turn Dev Mode on (HUD) when you actually want it.
public class MouseRaycastDebugger : MonoBehaviour
{
    void Update()
    {
        if (!GameMode.DevMode) return;
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