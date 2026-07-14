using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Live bindings for runtime-built UI.
//
// The rule every window here follows: build widgets ONLY when the structure changes, then refresh
// their values in place every frame. The economy ticks several times a second, and rebuilding a card
// on every tick destroys and recreates its button — which restarts the button's colour fade and reads
// as a strobe. A LiveSet is the "refresh in place" half of that rule, so each window doesn't have to
// reinvent it.
//
// Usage: clear the set when you rebuild, register bindings as you build each widget, then call Tick()
// once per frame.
public class LiveSet
{
    readonly List<Action> bindings = new List<Action>();

    public void Clear() => bindings.Clear();
    public int Count => bindings.Count;

    public void Tick() { for (int i = 0; i < bindings.Count; i++) bindings[i](); }

    // EVERY binding below writes ONLY when the value actually changed.
    //
    // This is not a micro-optimisation, it is the whole point. Assigning TMP_Text.text or
    // CanvasGroup.alpha marks the canvas and its layout dirty — and these widgets live inside nested
    // VerticalLayoutGroup + ContentSizeFitter cards inside a scrolling layout group. Writing them
    // unconditionally every frame forces a full layout rebuild every frame, which reads on screen as
    // flashing, jittering rows and drags the whole game's frame rate down with it.

    /// A text element whose content is recomputed each frame, but only WRITTEN when it changes.
    public void Text(TMP_Text label, Func<string> text)
    {
        if (label == null || text == null) return;
        string last = null;
        bindings.Add(() =>
        {
            if (label == null) return;
            string v = text();
            if (v == last) return;
            last = v;
            label.text = v;
        });
    }

    /// A button whose caption and enabled state follow live values. When it can't be used it dims and
    /// says why, which is how every actionable control in this UI explains itself.
    public void Button(UnityEngine.UI.Button button, Func<(bool can, string text)> eval, CanvasGroup dim = null)
    {
        if (button == null || eval == null) return;
        // Resolved ONCE. GetComponentInChildren allocates and walks the hierarchy; doing it per frame
        // per button was pure waste.
        var label = button.GetComponentInChildren<TMP_Text>();
        string lastText = null;
        bool? lastCan = null;
        bindings.Add(() =>
        {
            if (button == null) return;
            var (can, text) = eval();
            if (lastCan != can)
            {
                lastCan = can;
                button.interactable = can;
                if (dim != null) dim.alpha = can ? 1f : 0.45f;
            }
            if (label != null && text != lastText) { lastText = text; label.text = text; }
        });
    }

    /// A progress bar: fill fraction plus an optional caption and colour.
    public void Bar(Image fill, Func<(float t, string text, Color color)> eval, TMP_Text label = null)
    {
        if (fill == null || eval == null) return;
        float lastT = float.NaN;
        Color lastC = default;
        string lastText = null;
        bindings.Add(() =>
        {
            if (fill == null) return;
            var (t, text, color) = eval();
            t = Mathf.Clamp01(t);
            // A bar's anchor only needs to move when it visibly would. Sub-pixel churn is invisible and
            // dirties the layout for nothing.
            if (float.IsNaN(lastT) || Mathf.Abs(t - lastT) > 0.0015f)
            {
                lastT = t;
                fill.rectTransform.anchorMax = new Vector2(t, 1f);
            }
            if (color != lastC) { lastC = color; fill.color = color; }
            if (label != null && text != lastText) { lastText = text; label.text = text; }
        });
    }

    /// Anything else that needs per-frame work.
    public void Custom(Action a) { if (a != null) bindings.Add(a); }
}
