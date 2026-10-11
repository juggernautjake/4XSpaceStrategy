using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// SURVEY PROGRESS, WHERE THE PLAYER IS LOOKING (2026-10-11)
//
// "Make sure for surveying planets that research ships show a loading bar in the planet view while the
// research is happening."
//
// Two readouts, both reading the same Survey helpers the ship panels read, so every bar in the game
// agrees on the percentage, the ETA and the ship:
//
//   SurveyProgressUI.BuildSection — the SURVEY card at the top of the Planet View's Survey tab: one row
//       per world (the planet, then any moon with a survey running or part-done), its level, a live bar
//       while a ship works it, the ETA, who is doing it, and what the next level would give.
//   SurveyPaneStrip — a compact bar pinned to the top-left of each MAP PANE (the planet's and every open
//       moon's), so a running survey is visible from any tab. It appears when a ship starts work, follows
//       the progress live, says "complete" for a few seconds when the level finishes, then goes away.
//
// Both are refresh-in-place: built once, values written only when they change (LiveSet / cached ints),
// so a survey ticking every frame never rebuilds a layout.
// ============================================================================================
public static class SurveyProgressUI
{
    /// One status line for a world: "Survey 45% · ~18 days · Scout 3" while running, or where it stands.
    public static string StatusLine(CelestialBody b)
    {
        if (b == null) return "";
        if (Survey.Active(b, out bool deep))
        {
            float p = Survey.Progress(b, deep);
            var lead = Survey.LeadShip(b, deep);
            int ships = Survey.ShipsOn(b, deep);
            string who = lead != null ? lead.name : "?";
            if (ships > 1) who += $" +{ships - 1}";
            return $"<b>{Survey.JobName(b, deep)}</b> {p * 100f:F0}%  ·  {EtaText(b, deep)}  ·  {who}";
        }
        if (b.deepProgress >= 1f) return "<color=#4DFF6E>Fully studied</color> — every index read, sites charted";
        if (b.explorationProgress >= 1f)
            return b.deepProgress > 0f
                ? $"Surveyed · Deep Survey paused at {b.deepProgress * 100f:F0}% — send a research ship to finish it"
                : "Surveyed · send a research ship for a Deep Survey (index overlays, sites, ores)";
        if (b.owner == FactionManager.Player)
            return "Yours — mapped by ownership. A research ship can still Deep Survey it";
        return b.explorationProgress > 0f
            ? $"<color=#FFBF4D>Survey paused at {b.explorationProgress * 100f:F0}%</color> — send any survey-capable ship to resume"
            : "<color=#FFBF4D>Not surveyed</color> — send a scout or research ship";
    }

    /// "~18 days left" from the live rate, or "starting" before the first frame of work.
    public static string EtaText(CelestialBody b, bool deep)
    {
        float eta = Survey.EtaSeconds(b, deep);
        if (eta < 0f) return "paused";
        if (eta < 1f) return "finishing";
        return $"~{GameCalendar.Duration(eta)} left";
    }

    /// The SURVEY card for the Planet View's Survey tab. Rows for the world and for each moon that has
    /// a survey running or part-done; every value is a live binding, so nothing rebuilds while it runs.
    public static void BuildSection(Transform panel, CelestialBody b, LiveSet live)
    {
        if (panel == null || b == null || live == null) return;
        UIFactory.WrapText(panel, "<b>SURVEY</b>", UITheme.SmallSize, UITheme.Accent);

        var card = UIFactory.Panel(panel, "SurveyCard", UITheme.RowBg);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 5, 5); vlg.spacing = 3;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        WorldRow(card.transform, b, live, alwaysShow: true);
        if (b.moons != null)
            foreach (var m in b.moons)
                if (m != null) WorldRow(card.transform, m, live, alwaysShow: false);

        // ---- DEV: survey this world fully, now ----
        // Through UnitManager.DevCompleteSurvey, i.e. the same two completions a ship runs, so nothing
        // is left half-unlocked. Hidden (not just disabled) outside Dev Mode, and inert there anyway.
        var devBtn = UIFactory.Button(card.transform, "Dev: survey fully now  (Ctrl+Shift+U)", () =>
        {
            if (!GameMode.DevMode) return;
            UnitManager.Instance?.DevCompleteSurvey(b);
            if (b.moons != null) foreach (var m in b.moons) UnitManager.Instance?.DevCompleteSurvey(m);
        }, 22);
        UIFactory.Tooltip(devBtn.gameObject, "Dev Mode only. Completes Survey and Deep Survey on this world and its moons " +
                                             "through the real completion path: indexes, sites, ores, lore, clues, notifications.");
        var devGo = devBtn.gameObject;
        live.Custom(() =>
        {
            if (devGo == null) return;
            bool want = GameMode.DevMode;
            if (devGo.activeSelf != want) devGo.SetActive(want);
        });
    }

    static void WorldRow(Transform card, CelestialBody w, LiveSet live, bool alwaysShow)
    {
        var row = UIFactory.NewUI(card, "SurveyRow");
        var rv = row.AddComponent<VerticalLayoutGroup>();
        rv.spacing = 1; rv.childControlWidth = true; rv.childControlHeight = true; rv.childForceExpandWidth = true;

        var title = UIFactory.WrapText(row.transform, "", UITheme.SmallSize, UITheme.Text);
        var line = UIFactory.WrapText(row.transform, "", UITheme.SmallSize, UITheme.SubText);

        // The bar: a track and a hard-edged fill, the same build as the window's other bars.
        var holder = UIFactory.NewUI(row.transform, "Bar");
        UIFactory.AddLayout(holder, 10);
        var track = UIFactory.Panel(holder.transform, "Track", UITheme.TrackBg);
        UIFactory.Stretch(track.rectTransform);
        var fill = UIFactory.Panel(track.transform, "Fill", UITheme.Accent);
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0f, 1f);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

        live.Text(title, () =>
        {
            string kind = w.parentBody != null ? "moon" : "world";
            return $"{w.name} <color=#7E93A8>({kind} · {Survey.LevelLabel(w)})</color>";
        });
        live.Text(line, () => StatusLine(w));
        live.Bar(fill, () =>
        {
            bool on = Survey.Active(w, out bool deep);
            if (!on) deep = w.explorationProgress >= 1f;
            return (Survey.Progress(w, deep), "", deep ? UITheme.Good : UITheme.Accent);
        });

        // Visibility. The bar only while a ship works the world; a moon's whole row only while it has a
        // survey running or part-done, so a planet with four untouched moons is not a wall of "not surveyed".
        var rowGo = row; var barGo = holder;
        live.Custom(() =>
        {
            if (rowGo == null) return;
            bool active = Survey.Active(w, out _);
            bool rowWant = alwaysShow || active
                           || (w.explorationProgress > 0f && w.explorationProgress < 1f)
                           || (w.deepProgress > 0f && w.deepProgress < 1f);
            if (rowGo.activeSelf != rowWant) rowGo.SetActive(rowWant);
            if (barGo != null && barGo.activeSelf != active) barGo.SetActive(active);
        });
    }
}

/// The compact survey bar pinned inside the top-left of one Planet View map pane. See the header above.
public class SurveyPaneStrip : MonoBehaviour
{
    const float Height = 30f, Inset = 6f, MaxWidth = 300f;
    const float CompleteNoteSeconds = 5f;
    static readonly Color Fill = new Color(0.04f, 0.06f, 0.09f, 0.86f);

    Func<CelestialBody> who;
    RectTransform rt, frame, fillRT;
    Image border, fillImg;
    TMP_Text label;

    CelestialBody shownFor;
    bool wasActive, wasDeep;
    float doneUntil, nextText, lastFill = -1f, lastWidth = -1f;
    int lastPct = -1;
    bool lastShown = true;

    public static SurveyPaneStrip Attach(RectTransform paneFrame, Func<CelestialBody> who)
    {
        if (paneFrame == null) return null;
        var border = UIFactory.Panel(paneFrame, "SurveyStrip", UITheme.Accent);
        border.raycastTarget = false;
        var s = border.gameObject.AddComponent<SurveyPaneStrip>();
        s.who = who;
        s.frame = paneFrame;
        s.border = border;
        s.rt = border.rectTransform;
        s.rt.anchorMin = s.rt.anchorMax = new Vector2(0f, 1f);
        s.rt.pivot = new Vector2(0f, 1f);
        s.rt.anchoredPosition = new Vector2(Inset, -Inset);
        s.rt.sizeDelta = new Vector2(220f, Height);

        // One-pixel border, dark fill: the nameplates' frame, so the map's furniture is one family.
        var inner = UIFactory.Panel(s.rt, "Fill", Fill);
        inner.raycastTarget = false;
        UIFactory.Stretch(inner.rectTransform, 1, 1, 1, 1);

        s.label = UIFactory.Text(inner.transform, "", 11, UITheme.Text, TextAlignmentOptions.TopLeft);
        s.label.raycastTarget = false;
        s.label.textWrappingMode = TextWrappingModes.NoWrap;
        s.label.overflowMode = TextOverflowModes.Ellipsis;
        UIFactory.Stretch(s.label.rectTransform, 5, 5, 2, 12);

        var track = UIFactory.Panel(inner.transform, "Track", UITheme.TrackBg);
        track.raycastTarget = false;
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(1f, 0f);
        trt.pivot = new Vector2(0.5f, 0f);
        trt.offsetMin = new Vector2(5f, 4f); trt.offsetMax = new Vector2(-5f, 10f);

        s.fillImg = UIFactory.Panel(track.transform, "Bar", UITheme.Accent);
        s.fillImg.raycastTarget = false;
        s.fillRT = s.fillImg.rectTransform;
        s.fillRT.anchorMin = Vector2.zero; s.fillRT.anchorMax = new Vector2(0f, 1f);
        s.fillRT.offsetMin = Vector2.zero; s.fillRT.offsetMax = Vector2.zero;

        s.SetShown(false);
        return s;
    }

    void SetShown(bool on)
    {
        if (lastShown == on) return;
        lastShown = on;
        // The strip's own image and its children, without deactivating this GameObject (Update must run).
        border.enabled = on;
        for (int i = 0; i < rt.childCount; i++)
        {
            var c = rt.GetChild(i).gameObject;
            if (c.activeSelf != on) c.SetActive(on);
        }
    }

    void Update()
    {
        var b = who != null ? who() : null;
        if (b == null) { SetShown(false); return; }
        if (b != shownFor) { shownFor = b; wasActive = false; doneUntil = 0f; lastPct = -1; }

        float now = Time.unscaledTime;
        bool active = Survey.Active(b, out bool deep);

        // The level a ship was on has just finished: say so for a few seconds rather than vanishing.
        if (wasActive && !active && Survey.Progress(b, wasDeep) >= 1f) doneUntil = now + CompleteNoteSeconds;
        if (active && (!wasActive || deep != wasDeep)) lastPct = -1;
        wasActive = active;
        if (active) wasDeep = deep;

        bool show = active || now < doneUntil;
        SetShown(show);
        if (!show) return;

        // Width follows the pane, capped, so a narrow moon pane still fits it.
        float want = Mathf.Min(MaxWidth, Mathf.Max(120f, frame.rect.width * 0.6f));
        if (!Mathf.Approximately(want, lastWidth)) { lastWidth = want; rt.sizeDelta = new Vector2(want, Height); }

        bool lvlDeep = active ? deep : wasDeep;
        float p = active ? Survey.Progress(b, deep) : 1f;
        if (Mathf.Abs(p - lastFill) > 0.001f) { lastFill = p; fillRT.anchorMax = new Vector2(p, 1f); }
        Color c = lvlDeep ? UITheme.Good : UITheme.Accent;
        if (fillImg.color != c) { fillImg.color = c; border.color = c; }

        // Text four times a second at most, and only when the whole-number percent moves or the ETA
        // ticks — a string per frame per pane is exactly the allocation this UI exists to avoid.
        if (now < nextText) return;
        nextText = now + 0.25f;
        int pct = Mathf.FloorToInt(p * 100f);
        if (active)
        {
            var lead = Survey.LeadShip(b, deep);
            label.text = $"{Survey.JobName(b, deep)}  {pct}%  ·  {SurveyProgressUI.EtaText(b, deep)}" +
                         (lead != null ? $"  ·  {lead.name}" : "");
        }
        else if (pct != lastPct)
        {
            label.text = lvlDeep ? $"Deep Survey complete — {b.name} fully studied" : $"Survey complete — {b.name} mapped";
        }
        lastPct = pct;
    }
}
