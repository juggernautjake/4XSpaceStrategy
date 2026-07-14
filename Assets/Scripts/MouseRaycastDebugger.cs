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
        Debug.Log(Physics.Raycast(ray, out RaycastHit hit)
            ? "Raycast hit: " + hit.collider.gameObject.name
            : "Raycast hit NOTHING");
    }
}