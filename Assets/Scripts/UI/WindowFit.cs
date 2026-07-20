using UnityEngine;

// Guarantees a window fits the current screen: whenever it's shown (or the screen size changes) it
// clamps the window's size to the canvas (minus a margin) and nudges it fully on-screen. Combined with
// scroll views and pinned action buttons, this keeps every control reachable at any resolution.
public class WindowFit : MonoBehaviour
{
    RectTransform rt;
    Vector2 lastCanvas;

    void Awake() { rt = GetComponent<RectTransform>(); }

    void OnEnable() { Fit(); }

    void Update()
    {
        // Re-fit if the canvas (screen) size changed.
        var cr = CanvasRect();
        if (cr != null && cr.rect.size != lastCanvas) Fit();
    }

    RectTransform CanvasRect()
    {
        var canvas = GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform as RectTransform : null;
    }

    /// Clearance kept between a window's edge and the canvas edge, per side.
    ///
    /// PUBLIC because callers that size a window themselves have to use the same number. PlanetViewWindow
    /// sizes itself to fill the screen, and it used its own 8px margin while this clamped at 14 — two
    /// margins for one relationship, which is how a window ends up sized to one rule and positioned by
    /// another. There is now one number and it lives here, next to the code that enforces it.
    public const float Margin = 14f;

    public void Fit()
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        var cr = CanvasRect();
        if (cr == null) return;

        // Bail if the canvas has not been measured yet, and DO NOT record lastCanvas — so Update sees a
        // change as soon as it is real and fits then.
        //
        // Without this the whole method runs against a zero-size rect: `max` becomes (-28, -28), the
        // Min() assigns a NEGATIVE sizeDelta, and the window is left inside-out until something else
        // resizes it. A canvas legitimately reads zero for the first frame or two after bootstrap, which
        // is exactly when windows are being built.
        Vector2 canvasSize = cr.rect.size;
        if (canvasSize.x < 1f || canvasSize.y < 1f) return;

        lastCanvas = canvasSize;

        // Only ever SHRINK to fit the screen (never fight a user's manual resize by growing).
        Vector2 max = canvasSize - new Vector2(Margin * 2f, Margin * 2f);
        Vector2 s = rt.sizeDelta;
        s.x = Mathf.Min(s.x, Mathf.Max(64f, max.x));
        s.y = Mathf.Min(s.y, Mathf.Max(64f, max.y));
        rt.sizeDelta = s;

        ClampOnScreen(cr);
    }

    void ClampOnScreen(RectTransform cr)
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var wc = new Vector3[4]; rt.GetWorldCorners(wc);
        var cc = new Vector3[4]; cr.GetWorldCorners(cc);

        // If the window is STILL wider or taller than the canvas after the shrink above — a layout group
        // can force a minimum size that beats sizeDelta — clamping one edge only pushes the opposite edge
        // further out, and the nudge fights itself frame after frame. Centre it instead: both edges
        // overflow equally, which is the least-bad framing and, unlike the clamp, converges.
        float wW = wc[2].x - wc[0].x, wH = wc[1].y - wc[0].y;
        float cW = cc[2].x - cc[0].x, cH = cc[1].y - cc[0].y;

        float dx = 0f, dy = 0f;
        if (wW > cW) dx = ((cc[0].x + cc[2].x) - (wc[0].x + wc[2].x)) * 0.5f;
        else if (wc[0].x < cc[0].x) dx = cc[0].x - wc[0].x;
        else if (wc[2].x > cc[2].x) dx = cc[2].x - wc[2].x;

        if (wH > cH) dy = ((cc[0].y + cc[1].y) - (wc[0].y + wc[1].y)) * 0.5f;
        else if (wc[0].y < cc[0].y) dy = cc[0].y - wc[0].y;
        else if (wc[1].y > cc[1].y) dy = cc[1].y - wc[1].y;
        if (dx != 0f || dy != 0f)
        {
            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            rt.anchoredPosition += new Vector2(dx, dy) / scale;
        }
    }
}
