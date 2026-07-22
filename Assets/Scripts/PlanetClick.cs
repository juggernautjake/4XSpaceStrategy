using UnityEngine;

public class PlanetClick : MonoBehaviour
{
    public CelestialBody data;           // This should be set when spawning the visual

    // Double-click detection. A single click selects a world (camera focus + compact info panel); a
    // double-click on the SAME world opens the full-screen Planetary Viewer. unscaledTime so it still
    // works while the sim is paused.
    static PlanetClick lastClicked;
    static float lastClickTime;
    const float DoubleClickWindow = 0.35f;

    private void OnMouseDown()
    {
        if (data == null)
        {
            Debug.LogWarning("Planet has no data!");
            return;
        }

        // Unity fires OnMouseDown even when the cursor is over UI, so without this a click on an open
        // window ALSO punched through to whatever world happened to be orbiting behind it — selecting a
        // planet while you were trying to place a building on the surface map. Every other click handler
        // already guards this way; this one didn't.
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        // If a fleet is currently being aimed, this click is the destination confirmation — let the
        // FleetMovementController handle it instead of opening the info panel.
        if (FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting) return;

        // A DOCKED SHIP WINS.
        //
        // This world's pick sphere is deliberately oversized (EnsureClickCollider floors it at 1.5 world
        // units, ClickColliderScaler adds up to twelve more as you zoom out), and a docked ship parks
        // only a couple of units off the surface — so the ship ends up INSIDE the sphere and the sphere's
        // near face wins Unity's nearest-hit test. The ship's own OnMouseDown never fires, which is why
        // clicking a docked ship selected the planet instead.
        //
        // Handing the click over rather than just declining it: declining would select nothing, because
        // nothing else is going to fire for that ship.
        if (ClickPriority.TryClickUnitUnderCursor()) return;

        // Left-clicking a world INSPECTS it. Worlds are never "commandable", so this also drops any
        // ship selection — to send ships you select the ships, then right-click a destination.
        UnitSelection.Clear();

        PlanetUI ui = FindFirstObjectByType<PlanetUI>(FindObjectsInactive.Include); // include inactive
        if (ui == null)
        {
            Debug.LogError("PlanetUI script not found in scene! Create a GameObject with PlanetUI component.");
            return;
        }
        ui.Show(data);

        // A second click on the same world within the window OPENS the full Planetary Viewer; a lone
        // click leaves it selected with the compact panel showing, so clicking a world no longer throws
        // the whole full-screen view over the map.
        if (lastClicked == this && Time.unscaledTime - lastClickTime <= DoubleClickWindow)
        {
            PlanetViewWindow.Instance?.ShowFor(data);
            lastClicked = null;   // consume, so a third click doesn't read as another double-click
        }
        else
        {
            lastClicked = this;
            lastClickTime = Time.unscaledTime;
        }
    }
}