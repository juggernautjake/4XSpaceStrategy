using UnityEngine;

// Click a star / cluster / black hole to focus its system, open its info panel, zoom onto it, move
// the habitable-zone rings to it, and float its labels.
[RequireComponent(typeof(Collider))]
public class StarInteraction : MonoBehaviour
{
    public StarData star;
    public StarSystemData system;

    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        SimpleAudio.Instance?.PlaySelect();

        if (system != null)
        {
            GameManager.Instance?.SetFocus(system);
            if (SystemContext.Zone != null)
                SystemContext.Zone.Retarget(system.combinedStar, system.pivot, system.bodies);
        }

        if (StarInfoPanel.Instance != null) StarInfoPanel.Instance.Show(star);

        if (CameraController.Instance != null)
            CameraController.Instance.FocusAndZoom(transform, transform.lossyScale.x, CameraController.Instance.IsFollowing);
        ObjectLabelManager.Instance?.ShowForStar(transform, transform.lossyScale.x * 0.5f, Name(), Category());
    }

    string Name()
    {
        if (star == null) return "Star";
        if (star.isBlackHole) return "Black Hole";
        return $"{star.type}-type Star";
    }

    string Category()
    {
        string sys = system != null ? $"{system.name} · {FactionManager.OwnerLabel(system.owner)}" : "";
        string kind = star == null ? "Star"
            : star.isBlackHole ? "Black Hole"
            : star.starCount >= 3 ? "Ternary system"
            : star.starCount == 2 ? "Binary system"
            : $"{star.type}-type star";
        return string.IsNullOrEmpty(sys) ? kind : $"{kind}  ({sys})";
    }
}
