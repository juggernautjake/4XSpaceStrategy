using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A small, draggable 3D globe embedded inside another panel — currently the Inspector's Overview tab,
// above the body's readout.
//
// It owns no rendering of its own. PlanetGlobeWindow keeps one camera, sphere, light and RenderTexture,
// and this simply displays that texture and asks for a frame each time it is drawn. Both viewers always
// want the same subject (the selected body), so a second stage would be a second copy of the same picture
// at twice the cost — and two stages would then have to be kept in sync with each other.
//
// The request is per-frame and self-expiring (PlanetGlobeWindow.RequestFrame). That is what makes this
// safe to drop into a rebuilt panel: when the Inspector switches tabs or closes, this object stops asking
// and the shared stage switches itself off on the very next frame. Nothing has to remember to tell it.
public class EmbeddedGlobe : MonoBehaviour,
    IDragHandler, IScrollHandler
{
    CelestialBody body;
    RawImage view;

    const float DegPerPixel = 0.45f;

    /// Add a globe panel of `height` pixels to `parent`, showing `b`.
    public static EmbeddedGlobe Build(Transform parent, CelestialBody b, float height)
    {
        if (b == null) return null;

        var holder = UIFactory.NewUI(parent, "Globe");
        UIFactory.AddLayout(holder, height);

        // The render target is SQUARE (512x512) while this holder is as wide as the Inspector — roughly
        // 490x210 — so painting the texture straight onto the holder would stretch the planet into a
        // 2.3:1 ellipse. An inner child with an AspectRatioFitter keeps it round and letterboxes the
        // sides, which is why the holder gets a dark backing: the letterbox has to be something.
        var backing = UIFactory.Panel(holder.transform, "GlobeBacking", new Color(0.02f, 0.03f, 0.05f, 1f));
        var brt = backing.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
        backing.raycastTarget = false;

        var frame = UIFactory.NewUI(holder.transform, "GlobeFrame").GetComponent<RectTransform>();
        frame.anchorMin = new Vector2(0.5f, 0.5f); frame.anchorMax = new Vector2(0.5f, 0.5f);
        frame.pivot = new Vector2(0.5f, 0.5f);
        frame.anchoredPosition = Vector2.zero;
        frame.sizeDelta = new Vector2(height, height);
        var fit = frame.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 1f;

        var img = frame.gameObject.AddComponent<RawImage>();
        img.color = Color.white;

        var eg = holder.AddComponent<EmbeddedGlobe>();
        eg.body = b;
        eg.view = img;

        var hint = UIFactory.Text(holder.transform, "drag to rotate", 10, UITheme.SubText,
                                  TMPro.TextAlignmentOptions.BottomRight);
        var hrt = hint.rectTransform;
        hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
        hrt.offsetMin = new Vector2(6, 4); hrt.offsetMax = new Vector2(-6, -4);
        hint.raycastTarget = false;   // never steal the drag from the globe underneath

        return eg;
    }

    void LateUpdate()
    {
        var globe = PlanetGlobeWindow.Instance;
        if (globe == null || body == null)
        {
            // Drop the reference rather than leaving it pointing at a RenderTexture the window has
            // already Released and Destroyed on its way out.
            if (view != null) view.texture = null;
            return;
        }

        // Ask every frame we are visible. The stage stops rendering on the first frame nobody asks.
        globe.RequestFrame(body);

        // Bound late rather than at Build: the shared RenderTexture may not exist yet on the frame this
        // panel is created, and rebinding a texture that has not changed is free.
        if (view != null && view.texture != globe.Texture) view.texture = globe.Texture;
    }

    public void OnDrag(PointerEventData e)
    {
        // Vertical drag inverted, so dragging down tips the north pole toward you — what grabbing a
        // physical globe does. Matches the standalone window exactly; two different feels for the same
        // object would be worse than either.
        var g = PlanetGlobeWindow.Instance;
        if (g != null) g.Orbit(-e.delta.x * DegPerPixel, e.delta.y * DegPerPixel);
    }

    public void OnScroll(PointerEventData e)
    {
        var g = PlanetGlobeWindow.Instance;
        if (g != null) g.Zoom(e.scrollDelta.y);
    }
}
