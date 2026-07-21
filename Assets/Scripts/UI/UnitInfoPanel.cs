using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Shows a selected ship's stats, rank, owner and current task with a loading bar + time remaining.
// Lets you rename it, send it, and (once at a location) survey/research or begin colonizing.
public class UnitInfoPanel : MonoBehaviour
{
    public static UnitInfoPanel Instance;

    GameObject root;
    TMP_Text titleText, body, progressLabel;
    TMP_InputField nameInput;
    Image progressFill;
    Button surveyBtn, researchBtn, colonizeBtn, terraformBtn, returnBtn, scrapBtn, pauseBtn;
    TMP_Text researchLabel;
    TMP_Text pauseLabel, colonizeLabel, terraformLabel;
    Toggle queueModeToggle;
    RectTransform queueList;
    TMP_Text queueHeader;
    string lastQueueSig = "";
    Unit current;

    bool QueueMode => queueModeToggle != null && queueModeToggle.isOn;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("UnitInfoPanel");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<UnitInfoPanel>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var win = UIFactory.Window(parent, "Ship", new Vector2(400, 580), out root, out titleText);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-16, -60);

        // SCROLLED, not a plain VerticalLayoutGroup on the window.
        //
        // This panel stacks roughly 730px of controls — name field, seven buttons, two rows, a toggle, a
        // progress bar and the order queue — into a 580px window that the player can then resize smaller.
        // A layout group answers that overflow by shrinking every child toward zero, and it shrinks the
        // things with no minHeight hardest: that is how the queue-mode toggle ended up 14px tall, too
        // short to click and too short for its own 16px label. A ScrollView's ContentSizeFitter gives
        // every control its full preferred height instead and puts the overflow below the fold, which is
        // also what makes the panel survive being resized.
        var holder = UIFactory.NewUI(win, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder);
        UIFactory.ScrollView(holder, out RectTransform content);

        // Name is read-only until you press Edit Name (so a stray click can't rename a ship).
        nameInput = UIFactory.InputField(content, "Ship name…", "", 44f);
        nameInput.interactable = false;
        if (nameInput.textComponent != null) nameInput.textComponent.fontSize = UITheme.HeaderSize;
        var phc = nameInput.placeholder as TMP_Text; if (phc != null) phc.fontSize = UITheme.HeaderSize;
        nameInput.onEndEdit.AddListener(v =>
        {
            if (current != null && !string.IsNullOrWhiteSpace(v)) { current.name = v.Trim(); Refresh(); }
            nameInput.interactable = false;
        });
        UIFactory.Button(content, "Edit Name", () =>
        {
            if (current == null) return;
            nameInput.interactable = true;
            nameInput.ActivateInputField();
        }, 34);

        body = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.Text, 168);
        body.alignment = TextAlignmentOptions.TopLeft;

        // Task loading bar.
        var barHolder = UIFactory.NewUI(content, "Bar");
        UIFactory.AddLayout(barHolder, 20);
        var track = UIFactory.Panel(barHolder.transform, "Track", UITheme.TrackBg);
        UIFactory.Stretch(track.rectTransform);
        progressFill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
        var frt = progressFill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        progressLabel = UIFactory.Text(barHolder.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Center);
        UIFactory.Stretch(progressLabel.rectTransform);

        // When Queue mode is ON, the action buttons ADD to the queue instead of replacing; when OFF
        // they do it now. (Right-clicking the map with Ctrl also queues.)
        queueModeToggle = UIFactory.Toggle(content, "Queue mode (add to end instead of doing now)", false, null);

        surveyBtn = UIFactory.Button(content, "Survey / Collect Samples", DoSurvey, 28);
        // "Deep Survey", not "Research Here": it is the second half of the survey ladder — the surface
        // pass any explorer can fly, then the long landing a research ship makes on top of it.
        researchBtn = UIFactory.Button(content, "Deep Survey", DoResearch, 28);
        researchLabel = researchBtn.GetComponentInChildren<TMP_Text>();
        colonizeBtn = UIFactory.Button(content, "Found Colony", DoColonize, 28);
        colonizeLabel = colonizeBtn.GetComponentInChildren<TMP_Text>();
        terraformBtn = UIFactory.Button(content, "Terraform", DoTerraform, 28);
        terraformLabel = terraformBtn.GetComponentInChildren<TMP_Text>();

        var row = UIFactory.NewUI(content, "Row"); UIFactory.AddLayout(row, 30);
        var h = row.AddComponent<HorizontalLayoutGroup>(); h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
        UIFactory.Button(row.transform, "Send…", DoSend, 28);
        returnBtn = UIFactory.Button(row.transform, "Return Home", DoReturn, 28);

        // Queue controls + a live list of queued orders (each removable).
        var qrow = UIFactory.NewUI(content, "QueueRow"); UIFactory.AddLayout(qrow, 30);
        var qh = qrow.AddComponent<HorizontalLayoutGroup>(); qh.spacing = 6; qh.childControlWidth = true; qh.childControlHeight = true; qh.childForceExpandWidth = true;
        pauseBtn = UIFactory.Button(qrow.transform, "Pause Queue", TogglePause, 28);
        pauseLabel = pauseBtn.GetComponentInChildren<TMP_Text>();
        UIFactory.Button(qrow.transform, "Stop All", DoStop, 28);

        queueHeader = UIFactory.Label(content, "Orders", UITheme.SmallSize, UITheme.Accent, 16);
        var qholder = UIFactory.NewUI(content, "QueueHolder").GetComponent<RectTransform>();
        UIFactory.AddLayout(qholder.gameObject, 110);
        UIFactory.ScrollView(qholder, out queueList);

        var row2 = UIFactory.NewUI(content, "Row2"); UIFactory.AddLayout(row2, 30);
        var h2 = row2.AddComponent<HorizontalLayoutGroup>(); h2.spacing = 6; h2.childControlWidth = true; h2.childControlHeight = true; h2.childForceExpandWidth = true;
        var sd = UIFactory.Button(row2.transform, "Self-Destruct", () => { if (current != null) { UnitManager.Instance?.DestroyUnit(current, false); root.SetActive(false); } }, 28);
        var sdc = sd.colors; sdc.normalColor = new Color(0.4f, 0.15f, 0.15f); sd.colors = sdc;
        scrapBtn = UIFactory.Button(row2.transform, "Scrap (20-30%)", () => { if (current != null) { UnitManager.Instance?.DestroyUnit(current, true); root.SetActive(false); } }, 28);

        root.SetActive(false);
    }

    public void Show(Unit u)
    {
        current = u;
        if (u == null) { root.SetActive(false); return; }
        nameInput.text = u.name;
        titleText.text = u.name;
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
        Refresh();
    }

    // These issue ORDERS (so they travel-then-act if needed, and can be queued/interrupted).
    // Actions target the ship's current location, or — in Queue mode — the destination of its last
    // queued order (so you can chain "go to X, then survey X").
    CelestialBody ActionTarget()
    {
        if (current == null) return null;
        if (QueueMode && current.orders != null && current.orders.Count > 0)
        {
            for (int i = current.orders.Count - 1; i >= 0; i--)
                if (current.orders[i].target != null) return current.orders[i].target;
        }
        return current.location;
    }

    void DoSurvey()
    {
        var t = ActionTarget();
        if (current == null || t == null || !current.Info.canExplore) return;
        UnitManager.Instance?.IssueAction(new List<Unit> { current }, OrderKind.Survey, t, QueueMode);
    }

    void DoResearch()
    {
        var t = ActionTarget();
        if (current == null || t == null || !current.Info.canResearch) return;
        UnitManager.Instance?.IssueAction(new List<Unit> { current }, OrderKind.Research, t, QueueMode);
    }

    void DoColonize()
    {
        var t = ActionTarget();
        if (current == null || t == null || !current.Info.canColonize) return;
        if (t.owner == FactionManager.Player) return;
        UnitManager.Instance?.IssueAction(new List<Unit> { current }, OrderKind.Colonize, t, QueueMode);
    }

    void DoTerraform()
    {
        var t = ActionTarget();
        if (current == null || t == null || !current.Info.canTerraform) return;
        UnitManager.Instance?.IssueAction(new List<Unit> { current }, OrderKind.Terraform, t, QueueMode);
    }

    // One-line summary of a station/worker's passive effects (matches the shipyard card).
    static string StationEffectText(UnitInfo i)
    {
        var parts = new List<string>();
        if (i.researchAura > 0f) parts.Add($"+{i.researchAura:0.#} research/s");
        if (i.supplyBonus > 0f) parts.Add($"+{i.supplyBonus:0.#} metal & energy/s");
        if (i.mineBonus > 0f) parts.Add($"+{i.mineBonus:0.#} metal/s mining");
        if (i.terraformAura > 0f) parts.Add($"+{i.terraformAura:0.#}× terraform speed at its world");
        if (i.relayBoost > 0f) parts.Add($"+{i.relayBoost * 100f:0}% fleet range & speed");
        return string.Join("   ", parts);
    }

    void DoStop() { if (current != null) UnitManager.Instance?.StopAll(current); }
    void TogglePause() { if (current != null) UnitManager.Instance?.SetPaused(current, !current.queuePaused); }

    void DoSend()
    {
        // Include ships/stations parked in open space (deep-space stations), not just those at a body.
        var fleet = new List<Unit>();
        foreach (var u in UnitSelection.Selected) if (u.location != null || u.inSpace) fleet.Add(u);
        if (fleet.Count == 0 && current != null && (current.location != null || current.inSpace)) fleet.Add(current);
        FleetMovementController.Instance?.Arm(fleet);
    }

    void DoReturn()
    {
        if (current == null || current.location == null) return;
        UnitManager.Instance?.SendUnitsHome(new List<Unit> { current });
    }

    void Update()
    {
        if (root.activeSelf && current != null) Refresh();
    }

    void Refresh()
    {
        if (current == null) return;
        var u = current;
        string ownerHex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(u.owner));

        float prog = 0f; string task;
        switch (u.status)
        {
            case UnitStatus.Traveling:
                prog = u.TravelProgress;
                task = $"Traveling to {(u.travelTarget != null ? u.travelTarget.name : "?")} ({Mathf.Max(0f, u.travelDuration - u.travelElapsed):F0}s)";
                break;
            case UnitStatus.Exploring:
                prog = u.location != null ? u.location.explorationProgress : 0f;
                task = $"Surveying {(u.location != null ? u.location.name : "?")}";
                break;
            case UnitStatus.Colonizing:
                prog = u.location != null ? u.location.claimProgress : 0f;
                task = $"Colonizing {(u.location != null ? u.location.name : "?")}";
                break;
            case UnitStatus.Researching:
                prog = u.location != null ? u.location.researchProgress : 0f;
                task = $"Deep surveying {(u.location != null ? u.location.name : "?")}";
                break;
            case UnitStatus.Returning: task = "Returning home"; break;
            default: task = u.location != null ? $"Idle at {u.location.name}" : "Idle"; break;
        }
        if (u.queuePaused) task += "  <color=#FFBF4D>(paused)</color>";

        progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(prog), 1f);
        progressLabel.text = prog > 0f ? $"{prog * 100f:F0}%" : "";

        string queueLine = "";
        if (u.orders != null && u.orders.Count > 1)
            queueLine = $"\n<color=#9FB4C8>Queue:</color> {u.orders.Count - 1} more order(s)";
        string sampleLine = u.samples != null && u.samples.Count > 0
            ? $"\n<color=#8FD0FF>Carrying {u.samples.Count} ore sample(s)</color> (needs a world with a research centre)"
            : "";

        float er = UnitManager.Instance != null ? UnitManager.Instance.EffectiveRange(u) : u.Info.range;
        string rangeStr = (u.Info.range <= 0 || er >= float.MaxValue) ? "unlimited" : $"{er:F0}";

        // Station / worker readout: role, whether it's deployed and working, and its live effects.
        string structLine = "";
        if (u.Info.isStation || u.Info.isWorker)
        {
            bool deployed = u.status != UnitStatus.Traveling && (u.location != null || u.inSpace);
            string where = u.location != null ? $"at {u.location.name}" : (u.inSpace ? "in deep space" : "in transit");
            string kind = u.Info.isStation ? $"Station · {u.Info.stationRole}" : "Civilian";
            string state = deployed ? $"<color=#4DFF6E>deployed & working</color> ({where})" : "<color=#FFBF4D>not yet deployed — send it to position</color>";
            structLine = $"\n<color=#B39DFF>{kind}:</color> {state}";
            string fx = StationEffectText(u.Info);
            if (fx != "") structLine += $"\n<color=#8FE9C0>{fx}</color>";
        }

        body.text =
            $"<b>{u.Info.name}</b>  ·  <color=#FFD24D>{u.RankName}</color>\n" +
            $"Owner: <color={ownerHex}>{FactionManager.OwnerName(u.owner)}</color>\n" +
            $"Health {u.EffectiveHealth}  Armor {u.Armor}  Speed {u.Speed}  Range {rangeStr}\n" +
            $"Research {u.EffectiveResearch}  Attack {u.EffectiveAttack}\n" +
            $"XP {u.experience:F0}  Worlds {u.worldsExplored}{structLine}\n\n" +
            $"<color=#8FD0FF>Task:</color> {task}{queueLine}{sampleLine}";

        // Button availability. In Queue mode the target is the ship's last queued destination (so you
        // can chain "go to X, then survey/research/colonize X" even before it arrives).
        var t = ActionTarget();
        bool q = QueueMode;
        surveyBtn.interactable = t != null && u.Info.canExplore && (q || !t.Surveyed);
        researchBtn.interactable = t != null && u.Info.canResearch && (q || t.Surveyed);
        // A world can be studied again — leftover ores that could not be afforded last time, and any
        // sites excavated since — so the label says which run this is rather than greying out.
        if (researchLabel != null)
            researchLabel.text = (t != null && t.deepSurveyed) ? "Deep Survey (again)" : "Deep Survey";
        returnBtn.interactable = u.location != null && u.location != UnitManager.Instance?.HomePlanet;
        scrapBtn.interactable = UnitManager.Instance != null && UnitManager.Instance.CanScrap(u);
        if (pauseLabel != null) pauseLabel.text = u.queuePaused ? "Resume Queue" : "Pause Queue";

        UpdateColonizeButton(u, t);

        // Terraform button only exists for terraformer-capable ships.
        terraformBtn.gameObject.SetActive(u.Info.canTerraform);
        if (u.Info.canTerraform)
        {
            string tr = null;
            if (t == null) tr = QueueMode ? "queue a destination first" : "travel to a world first";
            else if (t.habitability >= Colony.FoundThreshold) tr = "already habitable";
            else if (!Colony.CanReachLivable(t)) tr = "can't be made livable";
            terraformBtn.interactable = tr == null;
            if (terraformLabel != null) terraformLabel.text = tr == null ? $"Terraform {t.name}" : $"Terraform — {tr}";
        }

        RefreshQueue(u);
    }

    // The Found Colony button is locked with a plain-language reason when it can't be used, so it's
    // always clear WHY colonization isn't available.
    void UpdateColonizeButton(Unit u, CelestialBody t)
    {
        string reason = null;
        if (!u.Info.canColonize) reason = "colony ship only";
        else if (t == null) reason = QueueMode ? "queue a destination first" : "travel to a world first";
        else if (t.owner == FactionManager.Player) reason = "already yours";
        else if (t.habitability < UnitManager.ColonizeMinHabitability)
            reason = $"needs {UnitManager.ColonizeMinHabitability:F0}% hab (this: {t.habitability:F0}%)";

        colonizeBtn.interactable = reason == null;
        if (colonizeLabel != null)
            colonizeLabel.text = reason == null ? "Found Colony (consumes ship)" : $"Found Colony — {reason}";
    }

    // Rebuilds the visible order list only when it actually changes (so it's not recreated per frame).
    void RefreshQueue(Unit u)
    {
        string sig = u.queuePaused ? "P" : "";
        if (u.orders != null) foreach (var o in u.orders) sig += $"|{(int)o.kind}:{(o.target != null ? o.target.name : (o.isPoint ? "space" : "?"))}";
        if (sig == lastQueueSig) return;
        lastQueueSig = sig;

        for (int i = queueList.childCount - 1; i >= 0; i--) Destroy(queueList.GetChild(i).gameObject);

        int count = u.orders != null ? u.orders.Count : 0;
        queueHeader.text = count == 0 ? "Orders — none" : $"Orders ({count})" + (u.queuePaused ? "  <color=#FFBF4D>PAUSED</color>" : "");
        if (count == 0)
        {
            UIFactory.Label(queueList, "<color=#7C8CA0>Idle. Use Queue mode or Ctrl+right-click to line up orders.</color>", UITheme.SmallSize, UITheme.SubText, 30);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            int idx = i;
            var o = u.orders[i];
            var rowGo = UIFactory.NewUI(queueList, "Order");
            UIFactory.AddLayout(rowGo, 26);
            var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 4; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false;

            string tag = i == 0 ? "<color=#4DFF6E>»</color>" : $"{i}.";
            var lbl = UIFactory.Text(rowGo.transform, $"{tag} {o.Describe()}", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
            var le = lbl.gameObject.AddComponent<LayoutElement>(); le.flexibleWidth = 1;

            var rm = UIFactory.Button(rowGo.transform, "X", () => UnitManager.Instance?.RemoveOrder(current, idx), 22);
            var rmle = rm.GetComponent<LayoutElement>(); if (rmle != null) { rmle.minWidth = 26; rmle.preferredWidth = 26; }
        }
    }
}
