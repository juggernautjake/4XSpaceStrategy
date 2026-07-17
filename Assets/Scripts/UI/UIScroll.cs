using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Does the mouse currently sit over a UI element that will CONSUME the scroll wheel to scroll itself?
//
// The world camera and the Planet View map both read Input.GetAxis("Mouse ScrollWheel") DIRECTLY,
// bypassing the event system — so on their own they can't tell that a menu under the cursor is about to
// scroll, and you get the weird double action: the menu scrolls AND the map/space zooms at the same time.
//
// This answers the one question that resolves it: is a SCROLLABLE ScrollRect under the cursor?
//   • Yes  -> the wheel belongs to that scroller; the world/map must NOT zoom.
//   • No   -> the wheel zooms whatever is behind the cursor, even THROUGH a plain (non-scrolling) panel
//             like a small info readout — which is what a player expects.
//
// "Scrollable" means the content actually overflows its viewport. A ScrollRect whose content fits has
// nothing to scroll, so the wheel should pass through to the zoom behind it.
public static class UIScroll
{
    static readonly List<RaycastResult> _hits = new List<RaycastResult>();

    public static bool PointerOverScroller()
    {
        var es = EventSystem.current;
        if (es == null) return false;

        var ped = new PointerEventData(es) { position = Input.mousePosition };
        _hits.Clear();
        es.RaycastAll(ped, _hits);

        for (int i = 0; i < _hits.Count; i++)
        {
            var go = _hits[i].gameObject;
            if (go == null) continue;
            var sr = go.GetComponentInParent<ScrollRect>();
            if (sr != null && CanScroll(sr)) return true;
        }
        return false;
    }

    // Only a scroll view that can ACTUALLY move counts. A ScrollRect whose content fits entirely inside
    // its viewport has nothing to scroll, so the wheel should still zoom what's behind it rather than be
    // swallowed by a scroller that wouldn't move anyway.
    static bool CanScroll(ScrollRect sr)
    {
        if (!sr.vertical && !sr.horizontal) return false;
        var content = sr.content;
        var vp = sr.viewport != null ? sr.viewport : sr.GetComponent<RectTransform>();
        if (content == null || vp == null) return true;   // unknown extents -> assume it scrolls (suppress zoom)
        return (sr.vertical   && content.rect.height > vp.rect.height + 1f) ||
               (sr.horizontal && content.rect.width  > vp.rect.width  + 1f);
    }
}
