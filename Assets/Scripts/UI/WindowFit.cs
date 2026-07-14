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

    public void Fit()
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        var cr = CanvasRect();
        if (cr == null) return;
        lastCanvas = cr.rect.size;

        // Only ever SHRINK to fit the screen (never fight a user's manual resize by growing).
        Vector2 max = cr.rect.size - new Vector2(28f, 28f);
        Vector2 s = rt.sizeDelta;
        s.x = Mathf.Min(s.x, max.x);
        s.y = Mathf.Min(s.y, max.y);
        rt.sizeDelta = s;

        ClampOnScreen(cr);
    }

    void ClampOnScreen(RectTransform cr)
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var wc = new Vector3[4]; rt.GetWorldCorners(wc);
        var cc = new Vector3[4]; cr.GetWorldCorners(cc);
        float dx = 0f, dy = 0f;
        if (wc[0].x < cc[0].x) dx = cc[0].x - wc[0].x; else if (wc[2].x > cc[2].x) dx = cc[2].x - wc[2].x;
        if (wc[0].y < cc[0].y) dy = cc[0].y - wc[0].y; else if (wc[1].y > cc[1].y) dy = cc[1].y - wc[1].y;
        if (dx != 0f || dy != 0f)
        {
            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            rt.anchoredPosition += new Vector2(dx, dy) / scale;
        }
    }
}
