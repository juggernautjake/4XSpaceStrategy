using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The terraforming console for the selected world: what is WRONG with it through the current species'
// eyes, how high its habitability can be pushed today, how high it could go with more research, and
// the specific engineering projects that will get it there.
//
// Follows the same rules as the other rebuilt windows: structural rebuilds only on a signature change,
// live values refreshed in place each frame. Never rebuilds on an economy tick.
public class TerraformWindow : MonoBehaviour
{
    public static TerraformWindow Instance;

    GameObject root;
    TMP_Text titleText, summary;
    RectTransform list;
    CelestialBody body;
    string lastSig = null;

    class DynRow
    {
        public UnityEngine.UI.Button button;
        public TMP_Text label;
        public CanvasGroup group;
        public System.Func<(bool can, string text)> evaluate;
    }

    class JobRow
    {
        public TerraformJob job;
        public Image fill;
        public TMP_Text label;
        public TMP_Text pauseLabel;
    }

    readonly List<DynRow> dynamics = new List<DynRow>();
    readonly List<JobRow> jobRows = new List<JobRow>();

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("TerraformWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<TerraformWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Terraforming", new Vector2(560, 660), out root, out titleText);
        root.GetComponent<RectTransform>().anchoredPosition = new Vector2(120, 0);

        summary = UIFactory.Text(content, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.TopLeft);
        var srt = summary.rectTransform;
        srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
        srt.pivot = new Vector2(0.5f, 1); srt.sizeDelta = new Vector2(0, 54);

        var holder = UIFactory.NewUI(content, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder, 0, 0, 58, 0);
        UIFactory.ScrollView(holder, out list);

        PlanetUI.OnBodySelected += OnBodySelected;
        root.SetActive(false);
    }

    void OnBodySelected(CelestialBody b)
    {
        body = b;
        lastSig = null;
    }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        if (show && body == null) body = PlanetUI.Selected;
        root.SetActive(show);
        if (show) { lastSig = null; root.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    public void ShowFor(CelestialBody b)
    {
        body = b; lastSig = null;
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    // What the window's SHAPE depends on. Habitability is excluded — it climbs continuously while a
    // terraformer works, and rebuilding on it would strobe the buttons.
    string Signature()
    {
        if (body == null) return "none";
        var sb = new System.Text.StringBuilder();
        sb.Append(body.id).Append('|').Append(SpeciesManager.CurrentIndex).Append('|');
        sb.Append((int)body.type).Append('|').Append(EmpireTech.Level).Append('|');
        if (body.terraformProjects != null) foreach (int p in body.terraformProjects) sb.Append(p).Append(',');
        sb.Append('|');
        var jobs = TerraformManager.Instance != null ? TerraformManager.Instance.JobsFor(body) : null;
        if (jobs != null) foreach (var j in jobs) sb.Append((int)j.type).Append(',');
        sb.Append('|');
        // Researched terraforming tech changes which projects exist at all.
        foreach (var t in TechDatabase.InBranch(TechBranch.Expansion)) if (TechManager.IsResearched(t.id)) sb.Append(t.id);
        return sb.ToString();
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;

        string sig = Signature();
        if (sig != lastSig) { lastSig = sig; Rebuild(); }

        RefreshSummary();
        foreach (var d in dynamics)
        {
            var (can, text) = d.evaluate();
            if (d.button != null) d.button.interactable = can;
            if (d.label != null) d.label.text = text;
            if (d.group != null) d.group.alpha = can ? 1f : 0.45f;
        }
        UpdateJobRows();
    }

    void RefreshSummary()
    {
        var s = SpeciesManager.Current;
        if (body == null || s == null) { summary.text = "Select a world to survey it for terraforming."; return; }

        float now = body.habitability;
        float ceiling = Colony.TerraformCeiling(body);
        float reachable = TerraformProjects.ReachableCeiling(body, s);
        float potential = TerraformProjects.PotentialCeiling(body, s);

        titleText.text = $"Terraforming — {body.name}";
        summary.text =
            $"<b>{body.name}</b> · {TerraformDiagnosis.Pretty(body.type)} · viewed as <b>{s.name}</b>\n" +
            $"Habitable now <color={Habitability.ScoreColorHex(now)}><b>{now:F0}%</b></color>   " +
            $"-> ceiling today <color={Habitability.ScoreColorHex(ceiling)}><b>{ceiling:F0}%</b></color>   " +
            $"-> with every project you've researched <color={Habitability.ScoreColorHex(reachable)}><b>{reachable:F0}%</b></color>   " +
            $"-> with all known science <color={Habitability.ScoreColorHex(potential)}><b>{potential:F0}%</b></color>\n" +
            $"<size=11><color=#9FB4C8>Colonizable at {Colony.FoundThreshold:F0}%. Projects raise the ceiling; terraformers then raise habitability toward it.</color></size>";
    }

    void Rebuild()
    {
        dynamics.Clear(); jobRows.Clear();
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        var s = SpeciesManager.Current;
        if (body == null || s == null)
        {
            UIFactory.WrapText(list, "Select a world to survey it.", UITheme.SmallSize, UITheme.SubText);
            return;
        }

        BuildDiagnosis(s);
        BuildActiveJobs();
        BuildProjectList(s);
        BuildCompleted();
    }

    // ---- What's wrong with this world, for THIS species ----
    void BuildDiagnosis(Species s)
    {
        UIFactory.WrapText(list, "<b>SURVEY — WHAT IS WRONG WITH THIS WORLD</b>", UITheme.SmallSize, UITheme.Accent);

        var issues = TerraformDiagnosis.Analyze(body, s);
        if (issues.Count == 0)
        {
            UIFactory.WrapText(list, $"<color=#4DFF6E>Nothing. This world already suits {s.name} as it is.</color>", UITheme.SmallSize, UITheme.Good);
            return;
        }

        foreach (var i in issues)
        {
            var card = Card(new Color(0.14f, 0.10f, 0.10f, 0.85f));
            string sevHex = ColorUtility.ToHtmlStringRGB(Color.Lerp(UITheme.Warn, UITheme.Bad, i.severity));
            UIFactory.WrapText(card, $"<b><color=#{sevHex}>{TerraformDiagnosis.Describe(i.problem)}</color></b>  " +
                                     $"<size=10><color=#9FB4C8>severity {i.severity * 100f:F0}%</color></size>", UITheme.SmallSize, UITheme.Text);
            UIFactory.WrapText(card, i.detail, UITheme.SmallSize, UITheme.SubText);
        }
    }

    // ---- Projects under way here ----
    void BuildActiveJobs()
    {
        var mgr = TerraformManager.Instance;
        if (mgr == null) return;
        var jobs = mgr.JobsFor(body);
        if (jobs.Count == 0) return;

        UIFactory.WrapText(list, "<b>UNDER WAY</b>", UITheme.SmallSize, UITheme.Accent);
        foreach (var j in jobs)
        {
            var card = Card(UITheme.RowBg);
            var label = UIFactory.Text(card, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
            UIFactory.AddLayout(label.gameObject, 16);

            var barHolder = UIFactory.NewUI(card, "Bar"); UIFactory.AddLayout(barHolder, 12);
            var track = UIFactory.Panel(barHolder.transform, "Track", UITheme.TrackBg);
            UIFactory.Stretch(track.rectTransform);
            var fill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
            var frt = fill.rectTransform;
            frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

            var row = UIFactory.NewUI(card, "Tools"); UIFactory.AddLayout(row, 24);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            var cap = j;
            var pause = UIFactory.Button(row.transform, "Pause", () => mgr.SetPaused(cap, !cap.paused), 22);
            UIFactory.Button(row.transform, "Abandon (full refund)", () => { mgr.Cancel(cap); lastSig = null; }, 22);

            jobRows.Add(new JobRow { job = j, fill = fill, label = label, pauseLabel = pause.GetComponentInChildren<TMP_Text>() });
        }
    }

    void UpdateJobRows()
    {
        foreach (var r in jobRows)
        {
            var j = r.job;
            var info = j.Info;
            r.fill.rectTransform.anchorMax = new Vector2(j.Progress, 1f);
            r.fill.color = j.paused ? UITheme.Warn : UITheme.Good;
            r.label.text = j.paused
                ? $"<b>{info.name}</b> — <color=#FFBF4D>paused</color> at {j.Progress * 100f:F0}%"
                : $"<b>{info.name}</b> — {j.Progress * 100f:F0}% ({j.Remaining:F0}s left) · +{info.ceilingGain:F0}% ceiling on completion";
            if (r.pauseLabel != null) r.pauseLabel.text = j.paused ? "Resume" : "Pause";
        }
    }

    // ---- The projects that would fix this world ----
    void BuildProjectList(Species s)
    {
        var mgr = TerraformManager.Instance;
        var available = TerraformProjectDatabase.For(body, s, false);   // everything relevant, tech or no tech
        if (available.Count == 0) return;

        UIFactory.WrapText(list, "<b>PLANETARY ENGINEERING PROJECTS</b>", UITheme.SmallSize, UITheme.Accent);

        foreach (var p in available)
        {
            if (mgr != null && mgr.IsRunning(body, p.type)) continue;   // already shown under UNDER WAY

            var card = Card(UITheme.RowBg);
            var group = card.gameObject.GetComponent<CanvasGroup>() ?? card.gameObject.AddComponent<CanvasGroup>();

            UIFactory.WrapText(card, $"<b>{p.name}</b>  <size=10><color=#8FD0FF>fixes: {TerraformDiagnosis.Describe(p.solves)}</color></size>",
                UITheme.BodySize, new Color(0.5f, 0.95f, 0.75f));
            UIFactory.WrapText(card, p.description, UITheme.SmallSize, UITheme.Text);

            var costLine = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.SubText);

            var t = p.type;
            var btn = UIFactory.Button(card, "", () => { TerraformManager.Instance?.Begin(body, t); lastSig = null; }, 26);

            // Costs and gating are all live values, so they update in place rather than forcing a rebuild.
            dynamics.Add(new DynRow
            {
                button = btn, label = btn.GetComponentInChildren<TMP_Text>(), group = group,
                evaluate = () =>
                {
                    int m = TerraformProjects.MetalCost(p, body), e = TerraformProjects.EnergyCost(p, body), w = TerraformProjects.WaterCost(p, body);
                    float dur = TerraformProjects.Duration(p, body);
                    costLine.text = $"<color=#9FB4C8>{m} metal · {e} energy" + (w > 0 ? $" · {w} water" : "") +
                                    $" · {dur:F0}s · <color=#4DFF6E>+{p.ceilingGain:F0}% ceiling</color></color>";

                    var tm = TerraformManager.Instance;
                    if (tm == null) return (false, "unavailable");
                    bool can = tm.CanStart(body, t, out string why);
                    return can ? (true, $"Begin {p.name}") : (false, why ?? "unavailable");
                }
            });
        }
    }

    void BuildCompleted()
    {
        if (body.terraformProjects == null || body.terraformProjects.Count == 0) return;
        UIFactory.WrapText(list, "<b>COMPLETED HERE</b>", UITheme.SmallSize, UITheme.Accent);
        foreach (int id in body.terraformProjects)
        {
            if (id < 0 || id >= TerraformProjectDatabase.All.Length) continue;
            var info = TerraformProjectDatabase.All[id];
            if (info == null) continue;
            UIFactory.WrapText(list, $"<color=#4DFF6E>+ {info.name}</color>  <size=10><color=#9FB4C8>+{info.ceilingGain:F0}% ceiling</color></size>",
                UITheme.SmallSize, UITheme.Good);
        }
    }

    Transform Card(Color bg)
    {
        var card = UIFactory.Panel(list, "Card", bg);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 3;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return card.transform;
    }

    void OnDestroy() { PlanetUI.OnBodySelected -= OnBodySelected; }
}
