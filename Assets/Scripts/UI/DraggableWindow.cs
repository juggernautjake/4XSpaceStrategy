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
        // Convert the screen-space drag into the canvas's own units so dragging tracks the cursor at
        // any UI scale, then keep the window fully on-screen.
        var canvas = target.GetComponentInParent<Canvas>();
        float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
        target.anchoredPosition += e.delta / scale;
        ClampOnScreen();
    }

    // Keep the whole window inside the canvas, regardless of how it's anchored. Works in the overlay
    // canvas's screen-pixel world space, then converts any correction back to canvas units.
    void ClampOnScreen()
    {
        var canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var canvasRT = canvas.transform as RectTransform;
        if (canvasRT == null) return;

        var wc = new Vector3[4]; target.GetWorldCorners(wc);   // 0=BL 1=TL 2=TR 3=BR
        var cc = new Vector3[4]; canvasRT.GetWorldCorners(cc);

        float dx = 0f, dy = 0f;
        if (wc[0].x < cc[0].x) dx = cc[0].x - wc[0].x;
        else if (wc[2].x > cc[2].x) dx = cc[2].x - wc[2].x;
        if (wc[0].y < cc[0].y) dy = cc[0].y - wc[0].y;
        else if (wc[1].y > cc[1].y) dy = cc[1].y - wc[1].y;

        if (dx != 0f || dy != 0f)
        {
            float scale = (canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
            target.anchoredPosition += new Vector2(dx, dy) / scale;
        }
    }
}
