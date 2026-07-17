using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// UI SANITY — automatic checks that catch the "this element thinks it is bigger than it is" class of
// bug: text, buttons, sliders or panels that render CLIPPED, OFF-SCREEN, or too small to click.
//
// Every window here is built in code (UIFactory), so a mistake in one builder silently clips content
// everywhere it's used — and there is no compiler feedback on layout, so these bugs are normally only
// found by squinting at the running game. This turns them into located, logged warnings with the exact
// element path, so a dev can go straight to the offender.
//
// READ-ONLY BY DESIGN. It reports; it never mutates the layout (auto-"fixing" a live layout tends to
// fight the layout groups and cause worse, flickery bugs). The fix is made in the builder that produced
// the element — usually: put overflowing content in a ScrollView, give a Label a content-driven height
// (WrapText), or widen a box.
//
// Two ways in:
//   • UISanityGuard (added to every UIFactory.Window) scans its own subtree shortly after it opens and
//     whenever it resizes, logging issues in Dev Mode.
//   • UISanity.ScanAll() sweeps every canvas at once and prints one report — wire it to a dev hotkey.
// ============================================================================================
public static class UISanity
{
    public static bool LogInDevMode = true;

    public const float MinInteractable = 14f;   // px — a button/slider/toggle smaller than this is a trap
    const float Eps = 1.5f;

    public struct Issue
    {
        public string kind, detail, path;
        public override string ToString() => $"[{kind}] {path} — {detail}";
    }

    // Sweep every active canvas and log a single report. Safe to call any time (e.g. from a dev key).
    public static void ScanAll()
    {
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        int total = 0;
        var sb = new StringBuilder();
        foreach (var c in canvases)
        {
            if (c == null || !c.isActiveAndEnabled) continue;
            var issues = new List<Issue>();
            Scan((RectTransform)c.transform, c.GetComponent<RectTransform>(), issues);
            if (issues.Count == 0) continue;
            sb.AppendLine($"— Canvas '{c.name}': {issues.Count} issue(s)");
            foreach (var i in issues) sb.Append("   ").AppendLine(i.ToString());
            total += issues.Count;
        }
        if (total == 0) Debug.Log("[UISanity] ScanAll: no layout issues found.");
        else Debug.LogWarning($"[UISanity] ScanAll found {total} issue(s):\n{sb}");
    }

    // Walk a subtree, collecting issues (does not log — the caller decides).
    public static void Scan(RectTransform root, RectTransform canvasRect, List<Issue> issues)
    {
        if (root == null || issues == null) return;
        ScanRecursive(root, canvasRect, issues);
    }

    static void ScanRecursive(RectTransform rt, RectTransform canvasRect, List<Issue> issues)
    {
        if (rt == null) return;
        var go = rt.gameObject;
        if (!go.activeInHierarchy) return;

        Vector2 size = rt.rect.size;
        var graphic = go.GetComponent<Graphic>();
        var selectable = go.GetComponent<Selectable>();
        var tmp = go.GetComponent<TMP_Text>();

        // 1) Zero / negative size on something meant to be seen or clicked — it renders nothing.
        if ((graphic != null || selectable != null) && (size.x < 1f || size.y < 1f))
            Add(issues, rt, "zero-size", $"{size.x:F0}x{size.y:F0} — will not render / cannot be clicked");

        // 2) Text taller than its box, with no ScrollView/ContentSizeFitter to reveal it -> clipped.
        //    This is the exact "the element thinks it's bigger than it is" case: preferredHeight is what
        //    TMP needs to show the text at the current width; the box is what the player actually sees.
        if (tmp != null && !string.IsNullOrEmpty(tmp.text))
        {
            float need = tmp.preferredHeight;
            if (need > size.y + Eps && !ScrolledOrFitted(rt))
                Add(issues, rt, "text-clipped", $"needs {need:F0}px tall, box is {size.y:F0}px — bottom lines are cut off");
        }

        // 3) An interactable too small to hit reliably.
        if (selectable != null && (size.x < MinInteractable || size.y < MinInteractable))
            Add(issues, rt, "control-too-small", $"{size.x:F0}x{size.y:F0} (min {MinInteractable:F0}px) — hard to click");

        // 4) A drawn element that extends past the canvas edge — partly off-screen. Skipped when it sits
        //    inside a mask (a scroll viewport), where content below the fold is hidden on purpose and
        //    scrolls into view — that's not a visibility bug.
        if (graphic != null && canvasRect != null && !MaskedByAncestor(rt) && OutsideCanvas(rt, canvasRect))
            Add(issues, rt, "off-canvas", "extends past the canvas edge — partly off-screen");

        // 5) A ContentSizeFitter that can't measure anything: a CONTAINER with a PreferredSize/MinSize
        //    fitter but no LayoutGroup (and no explicit LayoutElement size) computes height ~0 and clips
        //    its children. This is the classic broken-vertical-scroll cause — the scroll's content ends up
        //    too short to ever reveal everything inside such a card.
        var fitter = go.GetComponent<ContentSizeFitter>();
        if (fitter != null && tmp == null && rt.childCount > 0 &&
            (fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize || fitter.verticalFit == ContentSizeFitter.FitMode.MinSize) &&
            go.GetComponent<LayoutGroup>() == null)
        {
            var le = go.GetComponent<LayoutElement>();
            bool sized = le != null && (le.preferredHeight > 0f || le.minHeight > 0f);
            if (!sized)
                Add(issues, rt, "fitter-collapses", "ContentSizeFitter with no LayoutGroup/LayoutElement — sizes to ~0 and clips its children (breaks vertical scrolling)");
        }

        for (int i = 0; i < rt.childCount; i++)
            ScanRecursive(rt.GetChild(i) as RectTransform, canvasRect, issues);
    }

    // Is this element under a ScrollRect, or does it/an ancestor carry a ContentSizeFitter? If so, being
    // taller than its own rect is fine — the scroll view or the fitter will reveal/resize it.
    static bool ScrolledOrFitted(RectTransform rt)
    {
        var t = rt;
        int guard = 0;
        while (t != null && guard++ < 64)
        {
            if (t.GetComponent<ScrollRect>() != null) return true;
            if (t.GetComponent<ContentSizeFitter>() != null) return true;
            t = t.parent as RectTransform;
        }
        return false;
    }

    // Does an ancestor clip this element (a scroll viewport's RectMask2D, or a Mask)? Then sitting past
    // the canvas edge is intentional masking (it scrolls into view), not a visibility bug.
    static bool MaskedByAncestor(RectTransform rt)
    {
        var t = rt;
        int guard = 0;
        while (t != null && guard++ < 64)
        {
            if (t.GetComponent<RectMask2D>() != null || t.GetComponent<Mask>() != null) return true;
            t = t.parent as RectTransform;
        }
        return false;
    }

    static readonly Vector3[] Corners = new Vector3[4];
    static bool OutsideCanvas(RectTransform rt, RectTransform canvasRect)
    {
        rt.GetWorldCorners(Corners);
        Rect cr = canvasRect.rect;
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = canvasRect.InverseTransformPoint(Corners[i]);
            if (local.x < cr.xMin - 2f || local.x > cr.xMax + 2f ||
                local.y < cr.yMin - 2f || local.y > cr.yMax + 2f)
                return true;
        }
        return false;
    }

    static void Add(List<Issue> issues, RectTransform rt, string kind, string detail)
        => issues.Add(new Issue { kind = kind, detail = detail, path = Path(rt) });

    static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        var p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 12) { sb.Insert(0, p.name + "/"); p = p.parent; }
        return sb.ToString();
    }
}
