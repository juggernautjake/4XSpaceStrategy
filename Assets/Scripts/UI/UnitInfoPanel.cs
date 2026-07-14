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
    Button surveyBtn, colonizeBtn, returnBtn, scrapBtn;
    Unit current;

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
        var content = UIFactory.Window(parent, "Ship", new Vector2(330, 400), out root, out titleText);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-16, -60);
        UIFactory.VerticalLayout(content, 7);

        nameInput = UIFactory.InputField(content, "Ship name…");
        nameInput.onEndEdit.AddListener(v => { if (current != null && !string.IsNullOrWhiteSpace(v)) { current.name = v.Trim(); Refresh(); } });

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

        surveyBtn = UIFactory.Button(content, "Survey / Collect Samples", DoSurvey, 28);
        colonizeBtn = UIFactory.Button(content, "Begin Colonization", DoColonize, 28);
        var row = UIFactory.NewUI(content, "Row"); UIFactory.AddLayout(row, 30);
        var h = row.AddComponent<HorizontalLayoutGroup>(); h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
        UIFactory.Button(row.transform, "Send…", DoSend, 28);
        returnBtn = UIFactory.Button(row.transform, "Return Home", DoReturn, 28);

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

    void DoSurvey()
    {
        if (current == null || current.location == null || !current.Info.canExplore) return;
        current.status = UnitStatus.Exploring;
        ResearchManager.AddPoints(10);
        current.AddExperience(20f);
        SimpleAudio.Instance?.PlayNotify(NotifKind.Research);
        NotificationManager.Instance?.Push($"{current.name} collecting samples", $"Surveying {current.location.name}.", null, NotifKind.Research);
    }

    void DoColonize()
    {
        if (current == null || current.location == null || !current.Info.canColonize) return;
        if (current.location.owner == FactionManager.Player) return;
        current.status = UnitStatus.Colonizing;
        current.location.claimingFaction = FactionManager.Player;
    }

    void DoSend()
    {
        var fleet = new List<Unit>();
        foreach (var u in UnitSelection.Selected) if (u.location != null) fleet.Add(u);
        if (fleet.Count == 0 && current != null && current.location != null) fleet.Add(current);
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
            case UnitStatus.Returning: task = "Returning home"; break;
            default: task = u.location != null ? $"Idle at {u.location.name}" : "Idle"; break;
        }

        progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(prog), 1f);
        progressLabel.text = prog > 0f ? $"{prog * 100f:F0}%" : "";

        body.text =
            $"<b>{u.Info.name}</b>  ·  <color=#FFD24D>{u.RankName}</color>\n" +
            $"Owner: <color={ownerHex}>{FactionManager.OwnerName(u.owner)}</color>\n" +
            $"Health {u.EffectiveHealth}  Armor {u.Armor}  Speed {u.Speed}\n" +
            $"Research {u.EffectiveResearch}  Attack {u.EffectiveAttack}\n" +
            $"XP {u.experience:F0}  Worlds {u.worldsExplored}\n\n" +
            $"<color=#8FD0FF>Task:</color> {task}";

        bool atBody = u.location != null;
        surveyBtn.interactable = atBody && u.Info.canExplore;
        colonizeBtn.interactable = atBody && u.Info.canColonize && u.location.owner != FactionManager.Player;
        returnBtn.interactable = atBody && u.location != UnitManager.Instance?.HomePlanet;
        scrapBtn.interactable = UnitManager.Instance != null && UnitManager.Instance.CanScrap(u);
    }
}
