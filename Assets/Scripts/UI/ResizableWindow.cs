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

        // Clamp to the same margin WindowFit enforces, not to the raw screen.
        //
        // Dragging the grip to the full screen width put the window 14px past the canvas on each side —
        // and WindowFit only re-fits when the CANVAS size changes, which a grip drag is not, so nothing
        // ever pulled it back. That is a UISanity off-canvas report you can produce by hand at any time.
        float m = WindowFit.Margin * 2f;
        float maxW = Mathf.Max(minSize.x, Screen.width / sf - m);
        float maxH = Mathf.Max(minSize.y, Screen.height / sf - m);

        Vector2 size = target.sizeDelta;
        size.x = Mathf.Clamp(size.x + e.delta.x / sf, minSize.x, maxW);
        size.y = Mathf.Clamp(size.y - e.delta.y / sf, minSize.y, maxH);   // drag down = taller
        target.sizeDelta = size;

        // Growing from a corner can push the opposite edge off-screen even at a legal size, so re-clamp
        // the position as well. Cheap, and it is the component that already knows how.
        target.GetComponent<WindowFit>()?.Fit();
    }
}
