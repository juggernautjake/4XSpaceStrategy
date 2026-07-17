using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Added automatically to every UIFactory.Window. In Dev Mode it scans the window's own subtree shortly
// after the window opens and whenever it resizes, and logs any UISanity issue (clipped text, off-screen
// bits, controls too small to click) with the exact element path. Cheap: it does NOT run every frame —
// only after a show or a size change (layout needs a frame or two to settle first), and it dedupes so it
// won't spam the same count every rebuild. Does nothing outside Dev Mode.
public class UISanityGuard : MonoBehaviour
{
    RectTransform rt;
    Canvas canvas;
    Vector2 lastSize;
    float recheckAt = -1f;
    int lastCount = -1;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    // Re-arm on every show — the window may have been rebuilt with different content while hidden.
    void OnEnable() { lastSize = Vector2.zero; recheckAt = -1f; lastCount = -1; }

    void Update()
    {
        if (rt == null || !UISanity.LogInDevMode || !GameMode.DevMode) return;

        // Debounce: (re)schedule a scan whenever the window's size changes, then run once it settles.
        Vector2 size = rt.rect.size;
        if ((size - lastSize).sqrMagnitude > 1f)
        {
            lastSize = size;
            recheckAt = Time.unscaledTime + 0.25f;
        }
        if (recheckAt < 0f || Time.unscaledTime < recheckAt) return;
        recheckAt = -1f;

        var issues = new List<UISanity.Issue>();
        UISanity.Scan(rt, canvas != null ? canvas.GetComponent<RectTransform>() : null, issues);
        if (issues.Count > 0 && issues.Count != lastCount)
        {
            lastCount = issues.Count;
            var sb = new StringBuilder($"[UISanity] '{name}': {issues.Count} layout issue(s) — nothing should render clipped or off-screen:\n");
            foreach (var i in issues) sb.Append("   ").AppendLine(i.ToString());
            Debug.LogWarning(sb.ToString());
        }
    }
}
