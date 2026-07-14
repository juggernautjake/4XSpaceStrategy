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