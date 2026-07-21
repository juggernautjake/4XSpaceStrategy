using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The shipyard: a CATALOGUE of hulls you can lay down, and below it the STOCKS — every ship currently
// being built or queued, with its own progress bar.
//
// Clicking a hull in the catalogue queues one of it (click three times, get three). Your shipyards pool
// their build power, and the stocks spend it in queue order, so several ships genuinely build at once.
// Queue widgets can be dragged to reorder, paused (which hands their power to the next ship), or
// cancelled for a full refund.
//
// The window rebuilds its widgets ONLY when something structural changes (a signature check), and
// refreshes affordability, power and progress in place every frame. Rebuilding on every economy tick —
// which fires several times a second — is what used to make the buttons strobe.
public class ShipyardWindow : MonoBehaviour
{
    public static ShipyardWindow Instance;

    GameObject root;
    TMP_Text resourceText, powerText, yardText;
    RectTransform catalogue, stocks;

    // One hull in the catalogue. Held so affordability can be re-evaluated without a rebuild.
    // The label is resolved once at build time, and the last written values are remembered: writing
    // TMP text or CanvasGroup alpha dirties the layout, and these cards are nested layout groups inside
    // a scrolling layout group, so writing them every frame rebuilds the whole catalogue every frame.
    class ShipCard
    {
        public UnitType type;
        public Button button;
        public CanvasGroup group;
        public TMP_Text costLine;
        public TMP_Text buttonLabel;
        public string lastButtonText, lastCostText;
        public bool? lastCan;
    }

    // One ship on the stocks, bound to its BuildOrder (not to an index — the order moves when dragged).
    class QueueRow
    {
        public BuildOrder order;
        public RectTransform rt;
        public Image fill;
        public TMP_Text label;
        public TMP_Text pauseLabel;
        public string lastLabel, lastPause;
        public float lastFill = float.NaN;
        public Color lastColor;
    }

    readonly List<ShipCard> cards = new List<ShipCard>();
    readonly List<QueueRow> rows = new List<QueueRow>();
    // null = "never built yet". An empty queue has an empty signature, so "" can't mean unbuilt.
    string lastCatalogueSig = null, lastQueueSig = null;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("ShipyardWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<ShipyardWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Shipyard", new Vector2(560, 720), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        // --- Header: stockpile, pooled build power, and the selected yard's own contribution ---
        resourceText = UIFactory.Text(content, "", UITheme.SmallSize, UITheme.Accent, TextAlignmentOptions.Left);
        var rrt = resourceText.rectTransform;
        rrt.anchorMin = new Vector2(0, 1); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(0.5f, 1);
        rrt.sizeDelta = new Vector2(0, 18); rrt.anchoredPosition = Vector2.zero;

        powerText = UIFactory.Text(content, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        var prt = powerText.rectTransform;
        prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(1, 1); prt.pivot = new Vector2(0.5f, 1);
        prt.sizeDelta = new Vector2(0, 18); prt.anchoredPosition = new Vector2(0, -18);

        yardText = UIFactory.Text(content, "", UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.Left);
        var yrt = yardText.rectTransform;
        yrt.anchorMin = new Vector2(0, 1); yrt.anchorMax = new Vector2(1, 1); yrt.pivot = new Vector2(0.5f, 1);
        yrt.sizeDelta = new Vector2(0, 18); yrt.anchoredPosition = new Vector2(0, -36);

        // --- Catalogue (top half) ---
        var catHolder = UIFactory.NewUI(content, "CatalogueHolder").GetComponent<RectTransform>();
        catHolder.anchorMin = new Vector2(0, 0.42f); catHolder.anchorMax = new Vector2(1, 1);
        catHolder.offsetMin = new Vector2(0, 4); catHolder.offsetMax = new Vector2(0, -58);
        UIFactory.ScrollView(catHolder, out catalogue);

        // --- Stocks (bottom) ---
        var stocksTitle = UIFactory.Text(content, "<b>ON THE STOCKS</b>", UITheme.SmallSize, UITheme.Accent, TextAlignmentOptions.Left);
        var strt = stocksTitle.rectTransform;
        strt.anchorMin = new Vector2(0, 0.42f); strt.anchorMax = new Vector2(1, 0.42f);
        strt.pivot = new Vector2(0.5f, 0); strt.sizeDelta = new Vector2(0, 18); strt.anchoredPosition = Vector2.zero;

        var stocksHolder = UIFactory.NewUI(content, "StocksHolder").GetComponent<RectTransform>();
        stocksHolder.anchorMin = new Vector2(0, 0); stocksHolder.anchorMax = new Vector2(1, 0.42f);
        stocksHolder.offsetMin = Vector2.zero; stocksHolder.offsetMax = new Vector2(0, -20);
        UIFactory.ScrollView(stocksHolder, out stocks);

        root.SetActive(false);
    }

    // Nothing subscribes to the economy or build events: the window polls in Update while it is open,
    // and only touches widgets when a signature actually changes. That is the whole fix for the strobe —
    // an event-driven Refresh() fires several times a second and rebuilt every button each time.
    void MarkDirty() { lastQueueSig = null; }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show)
        {
            lastCatalogueSig = null; lastQueueSig = null;   // force one rebuild on open
            root.GetComponent<RectTransform>().SetAsLastSibling();
        }
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        var um = UnitManager.Instance;
        if (um == null) return;

        RefreshHeader(um);

        // Never tear widgets down while a row is being dragged — the dragged object would be destroyed
        // under the cursor and the drag would die with it.
        if (!QueueDragHandle.Dragging)
        {
            RebuildCatalogueIfChanged(um);
            RebuildStocksIfChanged(um);
        }

        UpdateCardStates(um);
        UpdateQueueRows(um);
    }

    string lastRes, lastPower, lastYard;

    void RefreshHeader(UnitManager um)
    {
        int total = um.TotalBuildPower, used = um.UsedBuildPower;

        string res = PlayerEconomy.Summary();
        if (res != lastRes) { lastRes = res; resourceText.text = res; }

        Color powerColor = used >= total ? UITheme.Warn : UITheme.Good;
        string hex = ColorUtility.ToHtmlStringRGB(powerColor);
        string power = $"Build Power: <color=#{hex}><b>{total - used}</b> free</color> of <b>{total}</b>" +
                       $"   <color=#9FB4C8>({used} in use · {BuildPower.PlayerYardCount()} shipyard(s) pooling)</color>";
        if (power != lastPower) { lastPower = power; powerText.text = power; }

        // The individually selected yard, so you can see what each world contributes to the pool.
        var sel = PlanetUI.Selected;
        string yard;
        if (sel != null && sel.owner == FactionManager.Player && sel.shipyardLevel >= 1)
            yard = $"Selected: <b>{sel.name}</b> — shipyard Lv{sel.shipyardLevel}/{Colony.MaxShipyardLevel}, " +
                   $"contributing <b>{BuildPower.ForBody(sel)}</b> build power" +
                   (TechEffects.ShipyardPowerBonus > 0 ? $" <color=#9FB4C8>(incl. +{TechEffects.ShipyardPowerBonus} from research)</color>" : "");
        else
            yard = "<color=#9FB4C8>Select one of your shipyard worlds to see what it contributes.</color>";
        if (yard != lastYard) { lastYard = yard; yardText.text = yard; }
    }

    // ---- Catalogue ----
    // Grouped, ordered sections so the (large) catalogue stays navigable. Predicates are mutually
    // exclusive and together cover every UnitType.
    static readonly (string name, System.Func<UnitInfo, bool> pred)[] Categories =
    {
        ("Starter Hulls", i => i.type == UnitType.Scout || i.type == UnitType.ResearchShip || i.type == UnitType.Fighter || i.type == UnitType.ColonyShip),
        ("Upgraded Hulls (Mk II / III)", i => i.type == UnitType.ScoutII || i.type == UnitType.ResearchShipII || i.type == UnitType.FighterII || i.type == UnitType.ScoutIII || i.type == UnitType.FighterIII || i.type == UnitType.ResearchShipIII),
        ("Civilian & Logistics", i => i.isWorker),
        ("Warships", i => i.type == UnitType.Frigate || i.type == UnitType.Cruiser || i.type == UnitType.Carrier || i.type == UnitType.Dreadnought),
        ("Science, Exploration & Terraforming", i => i.type == UnitType.ScienceVessel || i.type == UnitType.Explorer || i.type == UnitType.Probe || i.type == UnitType.Terraformer),
        ("Space Stations", i => i.isStation),
    };

    // The catalogue's SHAPE only changes when what you're allowed to build changes. Affordability is
    // handled per-frame in UpdateCardStates instead, so a ticking economy never rebuilds anything.
    void RebuildCatalogueIfChanged(UnitManager um)
    {
        string sig = $"{Colony.PlayerMaxShipyardLevel()}|{EmpireTech.Level}|{um.TotalBuildPower}|{GameMode.DevMode}";
        if (sig == lastCatalogueSig && catalogue.childCount > 0) return;
        lastCatalogueSig = sig;

        cards.Clear();
        for (int i = catalogue.childCount - 1; i >= 0; i--) Destroy(catalogue.GetChild(i).gameObject);

        foreach (var cat in Categories)
        {
            bool headerAdded = false;
            foreach (var info in UnitDatabase.All)
            {
                if (info == null || !cat.pred(info)) continue;
                if (!headerAdded)
                {
                    var h = UIFactory.WrapText(catalogue, $"<b>{cat.name}</b>", UITheme.HeaderSize, UITheme.Accent);
                    var hle = UIFactory.Ensure<LayoutElement>(h.gameObject);
                    hle.minHeight = 24;
                    headerAdded = true;
                }
                BuildCard(info);
            }
        }
    }

    void BuildCard(UnitInfo info)
    {
        var card = UIFactory.Panel(catalogue, "Card", UITheme.RowBg);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 2;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var group = card.gameObject.AddComponent<CanvasGroup>();

        // Title row: the ship's icon next to its name and gates.
        var titleRow = UIFactory.NewUI(card.transform, "Title");
        UIFactory.AddLayout(titleRow, 22);
        var hl = titleRow.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6; hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleLeft;

        AddIcon(titleRow.transform, info.type, 20);

        string gates = "";
        if (info.minShipyardLevel > 1) gates += $"  <size=11><color=#C9A94D>[Lv{info.minShipyardLevel} shipyard]</color></size>";
        if (info.minEmpireLevel > 1) gates += $"  <size=11><color=#8FC1FF>[Empire Lv{info.minEmpireLevel}]</color></size>";
        var nameText = UIFactory.Text(titleRow.transform, $"<b>{info.name}</b>{gates}", UITheme.BodySize,
            new Color(info.iconColor.r, info.iconColor.g, info.iconColor.b), TextAlignmentOptions.Left);
        var nle = nameText.gameObject.AddComponent<LayoutElement>(); nle.flexibleWidth = 1;

        // Cost line: resources, build power and time — everything the click will actually spend.
        var cost = UIFactory.WrapText(card.transform, "", UITheme.SmallSize, UITheme.SubText);

        UIFactory.WrapText(card.transform, $"HP {info.health}  Armor {info.armor}  Speed {info.speed}  Research {info.research}  Attack {info.attack}", UITheme.SmallSize, UITheme.SubText);
        string fx = EffectSummary(info);
        if (fx != "") UIFactory.WrapText(card.transform, fx, UITheme.SmallSize, UITheme.Good);
        UIFactory.WrapText(card.transform, info.description, UITheme.SmallSize, UITheme.Text);

        var t = info.type;
        var btn = UIFactory.Button(card.transform, "Build", () => { UnitManager.Instance?.QueueBuild(t); MarkDirty(); }, 26);

        cards.Add(new ShipCard
        {
            type = t, button = btn, group = group, costLine = cost,
            buttonLabel = btn.GetComponentInChildren<TMP_Text>()
        });
    }

    // Bright and clickable when you can afford it; dimmed and inert when you can't, with the reason on
    // the button so it's obvious what's missing.
    // Every write below is guarded by a change check. Without the guards this dirtied the layout of
    // every card every frame, which is what made the buttons flash and the whole game stutter.
    void UpdateCardStates(UnitManager um)
    {
        foreach (var c in cards)
        {
            var info = UnitDatabase.Get(c.type);
            bool can = um.CanBuildShip(c.type, out string why);

            if (c.lastCan != can)
            {
                c.lastCan = can;
                c.group.alpha = can ? 1f : 0.45f;
                c.button.interactable = can;
            }

            string btnText = can ? (info.isStation ? "Construct" : "Build") : why;
            if (c.buttonLabel != null && btnText != c.lastButtonText)
            {
                c.lastButtonText = btnText;
                c.buttonLabel.text = btnText;
            }

            int cm = ColonyManager.DiscCost(info.costMetal), ce = ColonyManager.DiscCost(info.costEnergy);
            bool affordable = GameMode.DevMode || PlayerEconomy.CanAfford(cm, ce);
            string costHex = ColorUtility.ToHtmlStringRGB(affordable ? UITheme.SubText : UITheme.Bad);
            string costText = $"<color=#{costHex}>{cm} metal · {ce} energy</color>   " +
                              $"<color=#8FD0FF>{info.buildPower} build power</color>   " +
                              $"<color=#9FB4C8>{info.buildTime * TechEffects.BuildTimeMult:F0}s</color>";
            if (costText != c.lastCostText)
            {
                c.lastCostText = costText;
                c.costLine.text = costText;
            }
        }
    }

    // ---- Stocks (the live queue) ----
    void RebuildStocksIfChanged(UnitManager um)
    {
        var q = um.BuildQueue;
        string sig = BuildSig(um);
        if (sig == lastQueueSig) return;
        lastQueueSig = sig;

        rows.Clear();
        for (int i = stocks.childCount - 1; i >= 0; i--) Destroy(stocks.GetChild(i).gameObject);

        if (q.Count == 0)
        {
            UIFactory.WrapText(stocks, "Nothing under construction. Click a hull above to lay one down — click it again to queue another.", UITheme.SmallSize, UITheme.SubText);
            return;
        }

        // A clear-everything control, since cancelling refunds in full anyway.
        var top = UIFactory.NewUI(stocks, "StocksTools"); UIFactory.AddLayout(top, 26);
        var th = top.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 6; th.childControlWidth = true; th.childControlHeight = true; th.childForceExpandWidth = true;
        UIFactory.Button(top.transform, "Cancel All (full refund)", () =>
        {
            var um2 = UnitManager.Instance; if (um2 == null) return;
            for (int i = um2.BuildQueue.Count - 1; i >= 0; i--) um2.CancelOrder(um2.BuildQueue[i]);
            MarkDirty();
        }, 24);

        foreach (var o in q) BuildQueueRow(o);
    }

    void BuildQueueRow(BuildOrder order)
    {
        var card = UIFactory.Panel(stocks, "QueueRow", UITheme.RowBg);
        var rt = card.rectTransform;
        UIFactory.AddLayout(card.gameObject, 46);
        var hl = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(4, 4, 4, 4); hl.spacing = 6;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleLeft;

        // Grip: drag this to reorder. Bound to the ORDER, so it keeps working as the list shuffles.
        QueueDragHandle.Attach(card.transform, rt,
            () => UnitManager.Instance != null ? UnitManager.Instance.IndexOfOrder(order) : -1,
            (from, to) => { UnitManager.Instance?.MoveOrder(from, to); ResyncRowOrder(); },
            () => { });

        AddIcon(card.transform, order.type, 24);

        // Name + state + a progress bar that keeps its fill while paused.
        var mid = UIFactory.NewUI(card.transform, "Mid");
        var mle = mid.AddComponent<LayoutElement>(); mle.flexibleWidth = 1;
        var mv = mid.AddComponent<VerticalLayoutGroup>();
        mv.spacing = 2; mv.childControlWidth = true; mv.childControlHeight = true; mv.childForceExpandWidth = true;

        var label = UIFactory.Text(mid.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        UIFactory.AddLayout(label.gameObject, 16);

        var barHolder = UIFactory.NewUI(mid.transform, "Bar"); UIFactory.AddLayout(barHolder, 12);
        var track = UIFactory.Panel(barHolder.transform, "Track", UITheme.TrackBg);
        UIFactory.Stretch(track.rectTransform);
        var fill = UIFactory.Panel(track.transform, "Fill", UITheme.Accent);
        var frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

        var pause = UIFactory.Button(card.transform, "||", () =>
        {
            UnitManager.Instance?.SetOrderPaused(order, !order.paused);
            MarkDirty();
        }, 24);
        var ple = pause.GetComponent<LayoutElement>(); ple.preferredWidth = 34; ple.minWidth = 34; ple.flexibleWidth = 0;

        var cancel = UIFactory.Button(card.transform, "X", () =>
        {
            UnitManager.Instance?.CancelOrder(order);
            MarkDirty();
        }, 24);
        var cle = cancel.GetComponent<LayoutElement>(); cle.preferredWidth = 28; cle.minWidth = 28; cle.flexibleWidth = 0;

        rows.Add(new QueueRow
        {
            order = order, rt = rt, fill = fill, label = label,
            pauseLabel = pause.GetComponentInChildren<TMP_Text>()
        });
    }

    // After a drag moves the model, re-sort the EXISTING row objects to match. Never rebuilds — the row
    // under the cursor has to survive for the drag to continue.
    void ResyncRowOrder()
    {
        var um = UnitManager.Instance; if (um == null) return;
        foreach (var r in rows)
        {
            int idx = um.IndexOfOrder(r.order);
            if (idx >= 0) r.rt.SetSiblingIndex(idx + 1);   // +1: the tools row sits at the top
        }
        lastQueueSig = BuildSig(um);
    }

    static string BuildSig(UnitManager um)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var o in um.BuildQueue) sb.Append(o.id).Append(',');
        return sb.ToString();
    }

    void UpdateQueueRows(UnitManager um)
    {
        foreach (var r in rows)
        {
            var o = r.order;
            var info = UnitDatabase.Get(o.type);
            int pos = um.IndexOfOrder(o);
            if (pos < 0) continue;

            // Only nudge the bar when it would visibly move — sub-pixel churn dirties the layout for
            // nothing, and there is one of these per queued ship.
            float prog = o.Progress;
            if (float.IsNaN(r.lastFill) || Mathf.Abs(prog - r.lastFill) > 0.0015f)
            {
                r.lastFill = prog;
                r.fill.rectTransform.anchorMax = new Vector2(prog, 1);
            }

            string state;
            Color barColor;
            switch (o.state)
            {
                case BuildState.Building:
                    state = $"<color=#4DFF6E>Building</color> — {o.Progress * 100f:F0}% ({o.Remaining:F0}s left)";
                    barColor = UITheme.Accent; break;
                case BuildState.Paused:
                    state = $"<color=#FFBF4D>Paused</color> at {o.Progress * 100f:F0}% — its {o.Power} power went to the next ship";
                    barColor = UITheme.Warn; break;
                case BuildState.Impossible:
                    state = $"<color=#FF6659>Needs {o.Power} build power</color> — more than your yards can supply";
                    barColor = UITheme.Bad; break;
                default:
                    state = $"<color=#9FB4C8>Queued</color> — waiting for {o.Power} build power";
                    barColor = UITheme.SubText; break;
            }
            if (barColor != r.lastColor) { r.lastColor = barColor; r.fill.color = barColor; }

            string text = $"<b>{info.name}</b>  <size=10><color=#8FD0FF>{o.Power}p</color></size>  <size=10>{state}</size>";
            if (text != r.lastLabel) { r.lastLabel = text; r.label.text = text; }

            string pause = o.paused ? "»" : "||";
            if (r.pauseLabel != null && pause != r.lastPause) { r.lastPause = pause; r.pauseLabel.text = pause; }
        }
    }

    // The ship's placeholder token, reused from the fleet map so a hull looks the same everywhere.
    static void AddIcon(Transform parent, UnitType type, float size)
    {
        var go = UIFactory.NewUI(parent, "Icon");
        var img = go.AddComponent<Image>();
        img.sprite = UnitIconRenderer.Sprite(type);
        img.preserveAspect = true;
        img.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = size; le.minWidth = size; le.preferredHeight = size; le.flexibleWidth = 0;
    }

    // One-line summary of a hull's passive station/worker effects (blank for plain ships).
    static string EffectSummary(UnitInfo i)
    {
        var parts = new List<string>();
        if (i.researchAura > 0f) parts.Add($"+{i.researchAura:0.#} research/s");
        if (i.supplyBonus > 0f) parts.Add($"+{i.supplyBonus:0.#} metal & energy/s");
        if (i.mineBonus > 0f) parts.Add($"+{i.mineBonus:0.#} metal/s mining");
        if (i.terraformAura > 0f) parts.Add($"+{i.terraformAura:0.#}× terraform speed");
        if (i.relayBoost > 0f) parts.Add($"+{i.relayBoost * 100f:0}% fleet range & speed");
        if (i.deepSpace) parts.Add("runs on starlight — deploy anywhere");
        return string.Join("   ", parts);
    }

}
