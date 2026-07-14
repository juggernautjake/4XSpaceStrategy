using UnityEngine;

public class PlanetClick : MonoBehaviour
{
    public CelestialBody data;           // This should be set when spawning the visual

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

        // Left-clicking a world INSPECTS it. Worlds are never "commandable", so this also drops any
        // ship selection — to send ships you select the ships, then right-click a destination.
        UnitSelection.Clear();

        PlanetUI ui = FindFirstObjectByType<PlanetUI>(FindObjectsInactive.Include); // include inactive
        if (ui != null)
        {
            ui.Show(data);
        }
        else
        {
            Debug.LogError("PlanetUI script not found in scene! Create a GameObject with PlanetUI component.");
        }
    }
}