using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The ore Codex. Undiscovered ores read as "???". Discovered ores can be researched (spending
// research points). Researched ores reveal their full dossier: description, uses and refining notes.
public class ResearchWindow : MonoBehaviour
{
    public static ResearchWindow Instance;

    GameObject root;
    TMP_Text header;
    RectTransform list;
    Image activeFill;        // live progress bar for the active research
    TMP_Text activeLabel;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("ResearchWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<ResearchWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Ore Codex & Research", new Vector2(520, 640), out root, out _);
        var rt = root.GetComponent<RectTransform>();
        rt.anchoredPosition = Vector2.zero;

        header = UIFactory.Text(content, "", UITheme.HeaderSize, UITheme.Good, TextAlignmentOptions.Left);
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1); hrt.sizeDelta = new Vector2(0, 24);

        var listHolder = UIFactory.NewUI(content, "ListHolder").GetComponent<RectTransform>();
        UIFactory.Stretch(listHolder, 0, 0, 30, 0);
        UIFactory.ScrollView(listHolder, out list);

        ResearchManager.OnChanged += Refresh;
        EmpireTech.OnChanged += Refresh;
        TechManager.OnChanged += Refresh;
        root.SetActive(false);
    }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show) { Refresh(); root.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    void Refresh()
    {
        if (root == null || !root.activeSelf) return;
        header.text = $"Research Points: <b>{ResearchManager.ResearchPoints}</b>";

        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        BuildEmpireCard();
        BuildResearchQueuePanel();
        BuildTechTree();

        UIFactory.WrapText(list, "<b>ORE CODEX</b>", UITheme.SmallSize, UITheme.Accent);
        foreach (var info in OreDatabase.All())
            BuildCard(info);
    }

    // ---- Research queue (timed, pausable, editable — like the shipyard queue) ----
    void BuildResearchQueuePanel()
    {
        activeFill = null; activeLabel = null;

        var card = UIFactory.Panel(list, "ResQueue", new Color(0.09f, 0.15f, 0.22f, 0.98f));
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(9, 9, 7, 7); vlg.spacing = 4;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIFactory.WrapText(card.transform, "<b>RESEARCH QUEUE</b>", UITheme.SmallSize, UITheme.Accent);

        if (TechManager.Active == null)
        {
            UIFactory.WrapText(card.transform, "Nothing queued. Research is funded by your Research Points over time — queue a technology below.", UITheme.SmallSize, UITheme.SubText);
            return;
        }

        // Live progress bar for the active research.
        var barHolder = UIFactory.NewUI(card.transform, "Bar"); UIFactory.AddLayout(barHolder, 20);
        var track = UIFactory.Panel(barHolder.transform, "Track", UITheme.TrackBg); UIFactory.Stretch(track.rectTransform);
        activeFill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
        var frt = activeFill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(TechManager.ActiveProgress01, 1); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        activeLabel = UIFactory.Text(barHolder.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Center);
        UIFactory.Stretch(activeLabel.rectTransform);

        // Pause / clear controls.
        var row = UIFactory.NewUI(card.transform, "Row"); UIFactory.AddLayout(row, 28);
        var h = row.AddComponent<HorizontalLayoutGroup>(); h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
        UIFactory.Button(row.transform, TechManager.Paused ? "Resume" : "Pause", () => TechManager.SetPaused(!TechManager.Paused), 26);
        UIFactory.Button(row.transform, "Clear Queue", () => TechManager.ClearQueue(), 26);

        // The ordered queue with remove buttons.
        var q = TechManager.Queue;
        for (int i = 0; i < q.Count; i++)
        {
            var tq = TechDatabase.Get(q[i]); if (tq == null) continue;
            int idx = i;
            var rowGo = UIFactory.NewUI(card.transform, "QRow"); UIFactory.AddLayout(rowGo, 24);
            var rh = rowGo.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 6; rh.childControlWidth = true; rh.childControlHeight = true; rh.childForceExpandWidth = true;
            string pfx = i == 0 ? "> " : $"{i + 1}. ";
            UIFactory.Text(rowGo.transform, $"{pfx}{tq.name}", UITheme.SmallSize, i == 0 ? UITheme.Accent : UITheme.Text, TextAlignmentOptions.Left);
            var rm = UIFactory.Button(rowGo.transform, "X", () => TechManager.RemoveFromQueue(idx), 22);
            var le = rm.GetComponent<LayoutElement>(); if (le != null) le.preferredWidth = 30;
        }

        UpdateActiveBar();
    }

    void UpdateActiveBar()
    {
        if (activeFill == null) return;
        var at = TechDatabase.Get(TechManager.Active);
        activeFill.rectTransform.anchorMax = new Vector2(TechManager.ActiveProgress01, 1f);
        if (activeLabel != null)
            activeLabel.text = at == null ? "" :
                $"{at.name}: {TechManager.ActiveProgress01 * 100f:F0}%  ({TechManager.ActiveProgressRP:F0}/{at.cost} RP)" + (TechManager.Paused ? "  <color=#FFBF4D>(paused)</color>" : "");
    }

    void Update()
    {
        if (root != null && root.activeSelf) UpdateActiveBar();
    }

    // ---- Tech tree ----
    static string BranchName(TechBranch b) => b == TechBranch.Expansion ? "Expansion & Terraforming" : b.ToString();

    static Color BranchColor(TechBranch b)
    {
        switch (b)
        {
            case TechBranch.Foundations: return new Color(0.62f, 0.69f, 0.77f);
            case TechBranch.Warfare:     return new Color(1f, 0.42f, 0.34f);
            case TechBranch.Science:     return new Color(0.35f, 0.69f, 1f);
            case TechBranch.Expansion:   return new Color(0.30f, 0.82f, 0.54f);
            case TechBranch.Exploration: return new Color(0.88f, 0.66f, 0.30f);
            case TechBranch.Industry:    return new Color(0.82f, 0.52f, 0.30f);
            default: return UITheme.Text;
        }
    }

    static string EffectSummary(Tech t)
    {
        var p = new System.Collections.Generic.List<string>();
        if (t.researchRate != 0f) p.Add($"+{t.researchRate * 100f:F0}% research");
        if (t.buildCostCut != 0f) p.Add($"-{t.buildCostCut * 100f:F0}% build cost");
        if (t.buildTimeCut != 0f) p.Add($"-{t.buildTimeCut * 100f:F0}% build time");
        if (t.terraCeiling != 0f) p.Add($"+{t.terraCeiling:F0} terraform ceiling");
        if (t.terraSpeed != 0f) p.Add($"+{t.terraSpeed * 100f:F0}% terraform speed");
        if (t.rangeMult != 0f) p.Add($"+{t.rangeMult * 100f:F0}% ship range");
        if (t.oreYield != 0f) p.Add($"+{t.oreYield * 100f:F0}% ore yield");
        return string.Join(" · ", p);
    }

    void BuildTechTree()
    {
        UIFactory.WrapText(list, "<b>TECHNOLOGY</b>", UITheme.SmallSize, UITheme.Accent);
        foreach (TechBranch br in System.Enum.GetValues(typeof(TechBranch)))
        {
            UIFactory.WrapText(list, $"<b>{BranchName(br)}</b>", UITheme.SmallSize, BranchColor(br));
            foreach (var t in TechDatabase.InBranch(br)) BuildTechCard(t, BranchColor(br));
        }
    }

    void BuildTechCard(Tech t, Color branchColor)
    {
        bool done = TechManager.IsResearched(t.id);

        var card = UIFactory.Panel(list, "Tech", UITheme.RowBg);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 5, 5); vlg.spacing = 2;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        string head = $"<b>{t.name}</b>  <size=11><color=#9FB4C8>T{t.tier} · {t.cost} RP</color></size>";
        UIFactory.WrapText(card.transform, head, UITheme.SmallSize, done ? UITheme.Good : branchColor);

        string eff = EffectSummary(t);
        if (!string.IsNullOrEmpty(eff)) UIFactory.WrapText(card.transform, eff, UITheme.SmallSize, UITheme.Text);
        if (!string.IsNullOrEmpty(t.unlockNote)) UIFactory.WrapText(card.transform, $"<color=#C9A94D>{t.unlockNote}</color>", UITheme.SmallSize, UITheme.SubText);

        if (done) { UIFactory.WrapText(card.transform, "<color=#4DFF6E>Researched</color>", UITheme.SmallSize, UITheme.Good); return; }

        var id = t.id;
        if (TechManager.Active == id)
        {
            UIFactory.WrapText(card.transform, "<color=#8FD0FF>Researching now…</color>", UITheme.SmallSize, UITheme.Accent);
        }
        else if (TechManager.IsQueued(id))
        {
            UIFactory.Button(card.transform, $"Queued (#{TechManager.QueuePosition(id) + 1}) — remove",
                () => TechManager.RemoveFromQueue(TechManager.QueuePosition(id)), 26);
        }
        else
        {
            bool can = TechManager.CanQueue(t, out string reason);
            var btn = UIFactory.Button(card.transform, can ? $"Queue ({t.cost} RP)" : $"Locked — {reason}",
                () => TechManager.Enqueue(id), 26);
            btn.interactable = can;
        }
    }

    // The empire-wide Tech Level: the hybrid progression track that gates the big milestones
    // (probes, stations, hyper-relays, terraforming stations, hyperdrives, mega-stations).
    void BuildEmpireCard()
    {
        var card = UIFactory.Panel(list, "EmpireTech", new Color(0.10f, 0.16f, 0.24f, 0.98f));
        var outline = card.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(1f, 0.85f, 0.35f, 0.9f);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8); vlg.spacing = 4;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIFactory.WrapText(card.transform,
            $"<b>Empire Tech Level {EmpireTech.Level}<color=#9FB4C8>/{EmpireTech.MaxLevel}</color></b>", UITheme.HeaderSize, new Color(1f, 0.85f, 0.35f));

        if (EmpireTech.AtMax)
        {
            UIFactory.WrapText(card.transform, EmpireTech.CanReachEleven
                ? "Your species has reached the transcendent peak. All milestones are unlocked."
                : "Peak level for your species. A more intelligent race could push to level 11.",
                UITheme.SmallSize, UITheme.Good);
            return;
        }

        UIFactory.WrapText(card.transform, $"<color=#8FD0FF>Next (Lv {EmpireTech.Level + 1}):</color> {EmpireTech.MilestoneFor(EmpireTech.Level + 1)}", UITheme.SmallSize, UITheme.Text);

        bool can = EmpireTech.CanAdvance;
        var btn = UIFactory.Button(card.transform,
            can ? $"Advance to Level {EmpireTech.Level + 1}  ({EmpireTech.NextCost} RP)"
                : $"Advance to Level {EmpireTech.Level + 1} — need {EmpireTech.NextCost} RP (have {ResearchManager.ResearchPoints})",
            () => { EmpireTech.Advance(); }, 30);
        btn.interactable = can;
    }

    void BuildCard(OreInfo info)
    {
        bool discovered = ResearchManager.IsDiscovered(info.type);
        bool researched = ResearchManager.IsResearched(info.type);

        var card = UIFactory.Panel(list, "Card", UITheme.RowBg);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 3;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Colour chip + title
        string title = discovered ? info.displayName : "??? — Undiscovered";
        Color titleColor = discovered ? new Color(info.color.r, info.color.g, info.color.b) : UITheme.SubText;
        var t = UIFactory.Text(card.transform, $"<b>{title}</b>", UITheme.BodySize, titleColor, TextAlignmentOptions.Left);
        UIFactory.AddLayout(t.gameObject, 20);

        if (!discovered)
        {
            UIFactory.WrapText(card.transform, "Find this ore on a planet surface to discover it.", UITheme.SmallSize, UITheme.SubText);
            return;
        }

        UIFactory.WrapText(card.transform, $"Tier {info.tier}  ·  Value {info.baseValue}cr  ·  Research cost {info.researchCost}", UITheme.SmallSize, UITheme.SubText);
        UIFactory.WrapText(card.transform, info.description, UITheme.SmallSize, UITheme.Text);

        if (researched)
        {
            UIFactory.WrapText(card.transform, $"<color=#8FD0FF>Uses:</color> {info.uses}", UITheme.SmallSize, UITheme.Text);
            UIFactory.WrapText(card.transform, $"<color=#FFBF4D>Refining:</color> {info.refining}", UITheme.SmallSize, UITheme.Text);
        }
        else
        {
            bool can = ResearchManager.CanResearch(info.type);
            var btn = UIFactory.Button(card.transform, can ? $"Research ({info.researchCost} pts)" : $"Need {info.researchCost} pts",
                () => ResearchManager.Research(info.type), 28);
            btn.interactable = can;
        }
    }

    void OnDestroy() { ResearchManager.OnChanged -= Refresh; EmpireTech.OnChanged -= Refresh; TechManager.OnChanged -= Refresh; }
}
