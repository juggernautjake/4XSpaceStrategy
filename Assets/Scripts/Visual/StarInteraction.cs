using UnityEngine;

// Click the star / cluster / black hole to open its info panel, zoom onto it, and float its labels.
[RequireComponent(typeof(Collider))]
public class StarInteraction : MonoBehaviour
{
    public StarData star;

    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        SimpleAudio.Instance?.PlaySelect();
        if (StarInfoPanel.Instance != null) StarInfoPanel.Instance.Show(star);

        string name = Name();
        string category = Category();
        if (CameraController.Instance != null)
            CameraController.Instance.FocusAndZoom(transform, transform.lossyScale.x, CameraController.Instance.IsFollowing);
        ObjectLabelManager.Instance?.ShowForStar(transform, transform.lossyScale.x * 0.5f, name, category);
    }

    string Name()
    {
        if (star == null) return "Star";
        if (star.isBlackHole) return "Black Hole";
        return $"{star.type}-type Star";
    }

    string Category()
    {
        if (star == null) return "Star";
        if (star.isBlackHole) return "Black Hole";
        if (star.starCount >= 3) return "Ternary star system";
        if (star.starCount == 2) return "Binary star system";
        return $"{star.type}-type star";
    }
}
