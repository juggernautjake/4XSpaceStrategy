using UnityEngine;
using UnityEngine.EventSystems;

// A corner grip that lets the user resize a window by dragging. Clamped to a minimum size (so it
// can't collapse) and to the screen. Accounts for the canvas scale factor so it tracks the cursor.
public class ResizableWindow : MonoBehaviour, IDragHandler, IPointerDownHandler
{
    public RectTransform target;
    public Vector2 minSize = new Vector2(220f, 140f);

    public void OnPointerDown(PointerEventData e)
    {
        if (target != null) target.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData e)
    {
        if (target == null) return;

        float sf = 1f;
        var canvas = target.GetComponentInParent<Canvas>();
        if (canvas != null) sf = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

        Vector2 size = target.sizeDelta;
        size.x = Mathf.Clamp(size.x + e.delta.x / sf, minSize.x, Screen.width / sf);
        size.y = Mathf.Clamp(size.y - e.delta.y / sf, minSize.y, Screen.height / sf);   // drag down = taller
        target.sizeDelta = size;
    }
}
