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

    // One project on the queue, bound to its ResearchOrder (not to an index — orders move when dragged).
    class QueueRow
    {
        public ResearchOrder order;
        public RectTransform rt;
        public Image fill;
        public TMP_Text label;
        public TMP_Text pauseLabel;
    }

    // A button whose enabled state and caption depend on live values (points, capacity, affordability).
    // Re-evaluated in place each frame so the list never has to be rebuilt just because RP ticked up.
    class DynamicButton
    {
        public UnityEngine.UI.Button button;
        public TMP_Text label;
        public System.Func<(bool can, string text)> evaluate;
    }

    readonly System.Collections.Generic.List<QueueRow> rows = new System.Collections.Generic.List<QueueRow>();
    readonly System.Collections.Generic.List<DynamicButton> dynamics = new System.Collections.Generic.List<DynamicButton>();
    RectTransform queueHolder;
    string lastSig = null, lastQueueSig = null;

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

        root.SetActive(false);
    }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show) { lastSig = null; lastQueueSig = null; root.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    // What the window's SHAPE depends on. Research points are deliberately absent: they tick constantly,
    // and rebuilding the whole list on every tick is what made the buttons strobe. Point-dependent
    // captions are refreshed in place instead (see dynamics).
    string Signature()
    {
        var sb = new System.Text.StringBuilder();
        // Dev state is part of the SHAPE: the dev card appears and disappears with the mode, and granting
        // the tree changes what its own toggle should read.
        sb.Append(GameMode.DevMode ? 'D' : '-').Append(DevCheats.AllTechGranted ? 'A' : '-').Append('|');
        sb.Append(EmpireTech.Level).Append('|').Append(AncientLore.SchematicsFound).Append('|');
        sb.Append(TechManager.TotalCapacity).Append('|');
        foreach (var t in TechDatabase.All) if (TechManager.IsResearched(t.id)) sb.Append(t.id).Append(',');
        sb.Append('|');
        foreach (var info in OreDatabase.All())
            sb.Append(ResearchManager.IsDiscovered(info.type) ? '1' : '0')
              .Append(ResearchManager.IsResearched(info.type) ? '1' : '0');
        return sb.ToString();
    }

    static string QueueSig()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var o in TechManager.Queue) sb.Append(o.id).Append(',');
        return sb.ToString();
    }

    void Rebuild()
    {
        rows.Clear();
        dynamics.Clear();
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        BuildDevPanel();
        BuildEmpireCard();
        BuildResearchQueuePanel();
        BuildTechTree();

        UIFactory.WrapText(list, "<b>ORE CODEX</b>", UITheme.SmallSize, UITheme.Accent);
        foreach (var info in OreDatabase.All())
            BuildCard(info);

        lastQueueSig = QueueSig();
    }

    // ---- Dev Mode: the whole tree, on a switch ----
    //
    // Only exists in Dev Mode, and deliberately sits at the very top of this window rather than on the
    // HUD: it is a research cheat, and the place you find out what it did is the research window.
    void BuildDevPanel()
    {
        if (!GameMode.DevMode) return;

        var card = UIFactory.Panel(list, "DevTech", new Color(0.20f, 0.11f, 0.05f, 0.98f));
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(9, 9, 7, 7); vlg.spacing = 4;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIFactory.WrapText(card.transform, "<b>DEV MODE</b>", UITheme.SmallSize, UITheme.Warn);

        UIFactory.Toggle(card.transform, "Grant every technology", DevCheats.AllTechGranted,
                         on => { DevCheats.SetAllTech(on); lastSig = null; lastQueueSig = null; });

        UIFactory.WrapText(card.transform,
            "<size=10><color=#9FB4C8>A real grant — every building, hull and terraforming option in the " +
            "game unlocks for as long as it is on. Switching it off puts back exactly the technologies " +
            "and research queue you had before, and leaving Dev Mode switches it off for you.</color></size>",
            UITheme.SmallSize, UITheme.SubText);
    }

    // ---- Research queue: the laboratory twin of the shipyard stocks ----
    // Your research centres pool their capacity; each project occupies its own share while it's being
    // studied. So several small technologies can run side by side, or one huge project can take the lot.
    void BuildResearchQueuePanel()
    {
        var card = UIFactory.Panel(list, "ResQueue", new Color(0.09f, 0.15f, 0.22f, 0.98f));
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(9, 9, 7, 7); vlg.spacing = 4;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UIFactory.WrapText(card.transform, "<b>RESEARCH QUEUE</b>", UITheme.SmallSize, UITheme.Accent);

        // Capacity readout — the research-side twin of the shipyard's build power line.
        var cap = UIFactory.WrapText(card.transform, "", UITheme.SmallSize, UITheme.Text);
        dynamics.Add(new DynamicButton
        {
            button = null, label = cap,
            evaluate = () =>
            {
                int total = TechManager.TotalCapacity, used = TechManager.UsedCapacity;
                string hex = ColorUtility.ToHtmlStringRGB(used >= total ? UITheme.Warn : UITheme.Good);
                int labs = ResearchCapacity.PlayerLabCount();
                return (true, $"Research Capacity: <color=#{hex}><b>{total - used}</b> free</color> of <b>{total}</b>" +
                              $"   <color=#9FB4C8>({used} in use · {labs} research centre(s) pooling)</color>");
            }
        });

        // Global pause / clear.
        var row = UIFactory.NewUI(card.transform, "Row"); UIFactory.AddLayout(row, 28);
        var h = row.AddComponent<HorizontalLayoutGroup>(); h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
        var pauseAll = UIFactory.Button(row.transform, "Pause All", () => { TechManager.SetPaused(!TechManager.Paused); }, 26);
        dynamics.Add(new DynamicButton
        {
            button = pauseAll, label = pauseAll.GetComponentInChildren<TMP_Text>(),
            evaluate = () => (true, TechManager.Paused ? "Resume All" : "Pause All")
        });
        UIFactory.Button(row.transform, "Clear Queue (refunds RP)", () => { TechManager.ClearQueue(); lastQueueSig = null; }, 26);

        queueHolder = UIFactory.NewUI(card.transform, "QueueRows").GetComponent<RectTransform>();
        var qv = queueHolder.gameObject.AddComponent<VerticalLayoutGroup>();
        qv.spacing = 4; qv.childControlWidth = true; qv.childControlHeight = true; qv.childForceExpandWidth = true;
        var qfit = queueHolder.gameObject.AddComponent<ContentSizeFitter>(); qfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildQueueRows();
    }

    void BuildQueueRows()
    {
        if (queueHolder == null) return;
        rows.Clear();
        for (int i = queueHolder.childCount - 1; i >= 0; i--) Destroy(queueHolder.GetChild(i).gameObject);

        if (TechManager.Queue.Count == 0)
        {
            UIFactory.WrapText(queueHolder, "Nothing queued. Research is funded by your Research Points over time — queue a technology below.", UITheme.SmallSize, UITheme.SubText);
            return;
        }

        foreach (var o in TechManager.Queue) BuildQueueRow(o);
        lastQueueSig = QueueSig();
    }

    void BuildQueueRow(ResearchOrder order)
    {
        var t = order.Def; if (t == null) return;

        var card = UIFactory.Panel(queueHolder, "QRow", UITheme.RowBg);
        var rt = card.rectTransform;
        UIFactory.AddLayout(card.gameObject, 44);
        var hl = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(4, 4, 4, 4); hl.spacing = 6;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleLeft;

        QueueDragHandle.Attach(card.transform, rt,
            () => TechManager.QueuePosition(order.id),
            (from, to) => { TechManager.MoveOrder(from, to); ResyncRowOrder(); },
            () => { });

        var mid = UIFactory.NewUI(card.transform, "Mid");
        var mle = mid.AddComponent<LayoutElement>(); mle.flexibleWidth = 1;
        var mv = mid.AddComponent<VerticalLayoutGroup>();
        mv.spacing = 2; mv.childControlWidth = true; mv.childControlHeight = true; mv.childForceExpandWidth = true;

        var label = UIFactory.Text(mid.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        UIFactory.AddLayout(label.gameObject, 16);

        var barHolder = UIFactory.NewUI(mid.transform, "Bar"); UIFactory.AddLayout(barHolder, 12);
        var track = UIFactory.Panel(barHolder.transform, "Track", UITheme.TrackBg);
        UIFactory.Stretch(track.rectTransform);
        var fill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
        var frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

        var pause = UIFactory.Button(card.transform, "||", () => { TechManager.SetOrderPaused(order, !order.paused); }, 24);
        var ple = pause.GetComponent<LayoutElement>(); ple.preferredWidth = 34; ple.minWidth = 34; ple.flexibleWidth = 0;

        var cancel = UIFactory.Button(card.transform, "X", () => { TechManager.RemoveOrder(order); lastQueueSig = null; }, 24);
        var cle = cancel.GetComponent<LayoutElement>(); cle.preferredWidth = 28; cle.minWidth = 28; cle.flexibleWidth = 0;

        rows.Add(new QueueRow
        {
            order = order, rt = rt, fill = fill, label = label,
            pauseLabel = pause.GetComponentInChildren<TMP_Text>()
        });
    }

    // After a drag reorders the model, re-sort the existing row objects instead of rebuilding them —
    // destroying the row under the cursor would kill the drag.
    void ResyncRowOrder()
    {
        foreach (var r in rows)
        {
            int idx = TechManager.QueuePosition(r.order.id);
            if (idx >= 0) r.rt.SetSiblingIndex(idx);
        }
        lastQueueSig = QueueSig();
    }

    void UpdateQueueRows()
    {
        foreach (var r in rows)
        {
            var o = r.order;
            var t = o.Def; if (t == null) continue;

            r.fill.rectTransform.anchorMax = new Vector2(o.Progress01, 1f);

            string state;
            Color barColor;
            switch (o.state)
            {
                case ResearchState.Researching:
                    state = $"<color=#4DFF6E>Researching</color> — {o.progress:F0}/{t.cost} RP";
                    barColor = UITheme.Good; break;
                case ResearchState.Paused:
                    state = $"<color=#FFBF4D>Paused</color> at {o.Progress01 * 100f:F0}% — its {o.Cost} capacity went to the next project";
                    barColor = UITheme.Warn; break;
                case ResearchState.WaitingForPrereq:
                    state = "<color=#9FB4C8>Waiting</color> — its prerequisite is still being researched";
                    barColor = UITheme.SubText; break;
                case ResearchState.Impossible:
                    state = $"<color=#FF6659>Needs {o.Cost} capacity</color> — more than your laboratories can supply";
                    barColor = UITheme.Bad; break;
                default:
                    state = $"<color=#9FB4C8>Queued</color> — waiting for {o.Cost} research capacity";
                    barColor = UITheme.SubText; break;
            }
            r.fill.color = barColor;
            r.label.text = $"<b>{t.name}</b>  <size=10><color=#8FD0FF>{o.Cost}c</color></size>  <size=10>{state}</size>";
            if (r.pauseLabel != null) r.pauseLabel.text = o.paused ? "»" : "||";
        }
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;

        header.text = $"Research Points: <b>{ResearchManager.ResearchPoints}</b>";

        if (!QueueDragHandle.Dragging)
        {
            string sig = Signature();
            if (sig != lastSig) { lastSig = sig; Rebuild(); }
            else if (QueueSig() != lastQueueSig) BuildQueueRows();
        }

        // Live captions and enabled states, updated in place — never a rebuild.
        foreach (var d in dynamics)
        {
            var (can, text) = d.evaluate();
            if (d.button != null) d.button.interactable = can;
            if (d.label != null) d.label.text = text;
        }

        UpdateQueueRows();
    }

    // ---- Tech tree ----
    static string BranchName(TechBranch b)
    {
        switch (b)
        {
            case TechBranch.Expansion: return "Expansion & Terraforming";
            case TechBranch.Doctrine:  return "Doctrines";
            case TechBranch.Ancients:  return $"Ancients — Precursor Secrets  <size=11><color=#9FB4C8>({AncientLore.SchematicsFound} schematic(s) recovered)</color></size>";
            default: return b.ToString();
        }
    }

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
            case TechBranch.Doctrine:    return new Color(0.80f, 0.62f, 1f);
            case TechBranch.Ancients:    return new Color(0.45f, 0.95f, 0.88f);
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
        if (t.shipyardPower != 0) p.Add($"+{t.shipyardPower} build power per shipyard");
        if (t.researchCap != 0) p.Add($"+{t.researchCap} research capacity per centre");
        return string.Join(" · ", p);
    }

    void BuildTechTree()
    {
        UIFactory.WrapText(list, "<b>TECHNOLOGY</b>", UITheme.SmallSize, UITheme.Accent);
        // The Ancients branch stays hidden until you've opened the path — by studying precursor ruins
        // (Xenoarchaeology) or recovering your first schematic in the field.
        bool ancientsUnlocked = AncientLore.SchematicsFound > 0 || TechManager.IsResearched("S3");
        foreach (TechBranch br in System.Enum.GetValues(typeof(TechBranch)))
        {
            if (br == TechBranch.Ancients && !ancientsUnlocked) continue;
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

        // Capacity cost sits alongside the point cost: it's what decides whether this can run beside
        // your other projects or needs the laboratories to itself.
        string head = $"<b>{t.name}</b>  <size=11><color=#9FB4C8>T{t.tier} · {t.cost} RP</color>" +
                      $" · <color=#8FD0FF>{t.CapacityCost} capacity</color></size>";
        UIFactory.WrapText(card.transform, head, UITheme.SmallSize, done ? UITheme.Good : branchColor);

        string eff = EffectSummary(t);
        if (!string.IsNullOrEmpty(eff)) UIFactory.WrapText(card.transform, eff, UITheme.SmallSize, UITheme.Text);
        if (!string.IsNullOrEmpty(t.unlockNote)) UIFactory.WrapText(card.transform, $"<color=#C9A94D>{t.unlockNote}</color>", UITheme.SmallSize, UITheme.SubText);

        if (done) { UIFactory.WrapText(card.transform, "<color=#4DFF6E>Researched</color>", UITheme.SmallSize, UITheme.Good); return; }

        // One button per tech whose caption follows the live state: queue it, or (once queued) drop it.
        // Evaluated every frame in place, so a ticking research bank never rebuilds the tree.
        var id = t.id;
        var group = card.gameObject.AddComponent<CanvasGroup>();
        var qbtn = UIFactory.Button(card.transform, "", () =>
        {
            if (TechManager.IsQueued(id)) TechManager.RemoveFromQueue(TechManager.QueuePosition(id));
            else TechManager.Enqueue(id);
            lastQueueSig = null;
        }, 26);

        dynamics.Add(new DynamicButton
        {
            button = qbtn, label = qbtn.GetComponentInChildren<TMP_Text>(),
            evaluate = () =>
            {
                var order = TechManager.Find(id);
                if (order != null)
                {
                    group.alpha = 1f;
                    int pos = TechManager.QueuePosition(id) + 1;
                    return (true, order.Active ? $"Researching now (#{pos}) — abandon" : $"Queued (#{pos}) — remove");
                }
                bool can = TechManager.CanQueue(t, out string reason);
                group.alpha = can ? 1f : 0.45f;
                return (can, can ? $"Queue ({t.cost} RP · {t.CapacityCost} capacity)" : $"Locked — {reason}");
            }
        });
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

        var btn = UIFactory.Button(card.transform, "", () => { EmpireTech.Advance(); }, 30);
        dynamics.Add(new DynamicButton
        {
            button = btn, label = btn.GetComponentInChildren<TMP_Text>(),
            evaluate = () =>
            {
                bool can = EmpireTech.CanAdvance;
                return (can, can
                    ? $"Advance to Level {EmpireTech.Level + 1}  ({EmpireTech.NextCost} RP)"
                    : $"Advance to Level {EmpireTech.Level + 1} — need {EmpireTech.NextCost} RP (have {ResearchManager.ResearchPoints})");
            }
        });
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
            var ore = info.type;
            var btn = UIFactory.Button(card.transform, "", () => ResearchManager.Research(ore), 28);
            dynamics.Add(new DynamicButton
            {
                button = btn, label = btn.GetComponentInChildren<TMP_Text>(),
                evaluate = () =>
                {
                    bool can = ResearchManager.CanResearch(ore);
                    return (can, can ? $"Research ({info.researchCost} pts)" : $"Need {info.researchCost} pts");
                }
            });
        }
    }
}
