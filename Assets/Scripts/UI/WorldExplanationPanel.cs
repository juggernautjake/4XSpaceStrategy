using System;
using TMPro;
using UnityEngine;

// ============================================================================================
// "WHY IS IT LIKE THIS?" — the world explanation, as UI (2026-10-11)
//
// The text is built by WorldExplanation (Systems/); this only puts it on screen, the same way in both
// places it appears — the Planet View's Overview and the Inspector's planet Overview — so the two cannot
// drift. One wrapped label bound to a LiveSet: WorldExplanation caches the text against a signature of
// everything it reads, so binding it per frame costs a hash, and the label re-lays out only when a survey
// level is reached or the world itself changes.
//
// Fog of war is WorldExplanation's job (it returns "" for a world the player cannot see); the callers
// also only build this for a world they are already showing.
// ============================================================================================
public static class WorldExplanationPanel
{
    public const string Title = "WHY IS IT LIKE THIS?";

    /// The explanation as one live label inside `card`. `body` is read each frame, so a window that
    /// re-targets without rebuilding still shows the right world.
    public static TMP_Text Build(Transform card, Func<CelestialBody> body, LiveSet live)
    {
        if (card == null || body == null || live == null) return null;
        var t = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () =>
        {
            var b = body();
            string s = WorldExplanation.ForDisplay(b);
            return s.Length > 0 ? s : "<color=#7E8B9C>Nothing is known about this world yet.</color>";
        });
        return t;
    }

    /// Hang the BASIC description on the last row added to `card` as a hover tooltip — used on the
    /// "Type" row, so hovering a world's kind says why it is that kind. Text is taken when the row is
    /// built; both windows rebuild when their subject changes.
    public static void TooltipOnLastRow(Transform card, CelestialBody body)
    {
        if (card == null || body == null || card.childCount == 0) return;
        string tip = WorldExplanation.Tooltip(body);
        if (string.IsNullOrEmpty(tip)) return;
        var row = card.GetChild(card.childCount - 1);
        var text = row.GetComponent<TMP_Text>();
        if (text != null) text.raycastTarget = true;   // UIFactory.Text turns raycasts off; a tooltip needs them
        UIFactory.Tooltip(row.gameObject, tip);
    }
}
