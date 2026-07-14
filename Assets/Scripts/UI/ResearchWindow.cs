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
        BuildTechTree();

        UIFactory.WrapText(list, "<b>ORE CODEX</b>", UITheme.SmallSize, UITheme.Accent);
        foreach (var info in OreDatabase.All())
            BuildCard(info);
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

        bool can = TechManager.CanResearch(t, out string reason);
        var id = t.id;
        var btn = UIFactory.Button(card.transform, can ? $"Research ({t.cost} RP)" : $"Locked — {reason}",
            () => TechManager.Research(id), 26);
        btn.interactable = can;
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
