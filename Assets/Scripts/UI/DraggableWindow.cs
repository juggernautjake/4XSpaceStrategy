using UnityEngine;
using UnityEngine.EventSystems;

// Drag a window by its title bar; clicking it brings the window to the front.
public class DraggableWindow : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public RectTransform target;   // the window root to move

    public void OnPointerDown(PointerEventData e)
    {
        if (target != null) target.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData e)
    {
        if (target == null) return;
        target.anchoredPosition += e.delta;
    }
}
