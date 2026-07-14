using UnityEngine;

// Click the star to open the star info panel (light/heat readout + habitable-zone toggle).
[RequireComponent(typeof(Collider))]
public class StarInteraction : MonoBehaviour
{
    public StarData star;

    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        if (StarInfoPanel.Instance != null)
            StarInfoPanel.Instance.Show(star);
    }
}
