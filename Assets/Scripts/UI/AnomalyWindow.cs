using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The study window for space anomalies — ancient derelict stations now, comets next. Same obsidian-and-gold
// Vael styling as the Codex. Shows what the anomaly is and a context-aware action: salvage/study it (gated on
// empire tech), which grants a Vael fragment or material/technology salvage.
public class AnomalyWindow : MonoBehaviour
{
    public static AnomalyWindow Instance;

    // The empire tech level needed to study each kind of anomaly.
    public const int DerelictStudyLevel = 2;
    public const int CometStudyLevel = 3;

    static readonly Color Stone = new Color(0.09f, 0.075f, 0.06f, 0.99f);
    static readonly Color Gold  = new Color(0.85f, 0.70f, 0.32f);
    static readonly Color Teal  = new Color(0.42f, 0.90f, 0.82f);
    static readonly Color Parch = new Color(0.92f, 0.88f, 0.78f);

    GameObject root;
    TMP_Text titleText, kindText, bodyText, statusText;
    Button actionBtn;
    TMP_Text actionLabel;

    Derelict _derelict;
    Comet _comet;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("AnomalyWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<AnomalyWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Anomaly", new Vector2(470, 340), out root, out titleText);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        var bg = root.GetComponent<Image>(); if (bg != null) bg.color = Stone;
        var ol = root.GetComponent<Outline>(); if (ol != null) ol.effectColor = Gold;
        if (titleText != null) titleText.color = Gold;

        var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(14, 14, 12, 12); v.spacing = 8;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;

        kindText = UIFactory.Text(content, "", 12, Teal, TextAlignmentOptions.Left);
        bodyText = UIFactory.WrapText(content, "", 14, Parch);
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        var ble = bodyText.gameObject.AddComponent<LayoutElement>(); ble.flexibleHeight = 1;
        statusText = UIFactory.WrapText(content, "", 12, Gold);

        actionBtn = UIFactory.Button(content, "", OnAction, 32);
        actionLabel = actionBtn.GetComponentInChildren<TMP_Text>();

        root.SetActive(false);
    }

    // ---- Derelicts ----
    public void ShowDerelict(Derelict d)
    {
        if (d == null || root == null) return;
        _derelict = d; _comet = null;

        titleText.text = "ANCIENT DERELICT";
        kindText.text = $"A broken Vael station — {d.Kind}.";
        bodyText.text = "A hull older than any living memory, cold and silent, its purpose long since ended. " +
                        "Something of the Vael endures inside — whether a shard of their lost technology, salvage " +
                        "worth stripping, or one of the ten voices they scattered across the dark.";
        RefreshDerelict();

        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    void RefreshDerelict()
    {
        var d = _derelict;
        if (d == null) return;

        if (d.studied)
        {
            statusText.text = d.clueIndex >= 0
                ? "<color=#4DFF6E>Its fragment has been recovered.</color>"
                : "<color=#9FB4C8>This hull has already been stripped.</color>";
            SetAction(false, "Nothing more to recover");
        }
        else if (EmpireTech.Level < DerelictStudyLevel)
        {
            statusText.text = $"<color=#FFBF4D>Your science is not yet equal to this.</color> Reach empire tech " +
                              $"level {DerelictStudyLevel} to send a crew aboard.";
            SetAction(false, $"Requires empire tech level {DerelictStudyLevel}");
        }
        else
        {
            statusText.text = "Send a crew aboard to strip the hull of whatever it still holds.";
            SetAction(true, "Board and study the derelict");
        }
    }

    // ---- Comets ----
    public void ShowComet(Comet c)
    {
        if (c == null || root == null) return;
        _comet = c; _derelict = null;

        titleText.text = "COMET";
        kindText.text = $"{c.SizeWord} comet, burning bright as it crosses the dark.";
        bodyText.text = c.studied
            ? c.RevealText
            : "A great comet streaks through the void, its coma blazing in the starlight. Most are only ice and " +
              "dust — but a scan says this one may be worth a closer look. With the right instruments you could " +
              "study it, even run it down and catch a piece.";
        RefreshComet();

        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    void RefreshComet()
    {
        var c = _comet;
        if (c == null) return;

        if (c.studied)
        {
            statusText.text = "<color=#4DFF6E>You have already run this comet down.</color>";
            SetAction(false, "Already studied");
        }
        else if (EmpireTech.Level < CometStudyLevel)
        {
            statusText.text = $"<color=#FFBF4D>You lack the instruments to catch it.</color> Reach empire tech " +
                              $"level {CometStudyLevel} to study a comet in flight.";
            SetAction(false, $"Requires empire tech level {CometStudyLevel}");
        }
        else
        {
            statusText.text = "Chase it down and study what it carries.";
            SetAction(true, "Study and catch the comet");
        }
    }

    void SetAction(bool enabled, string label)
    {
        if (actionBtn != null) actionBtn.interactable = enabled;
        if (actionLabel != null) actionLabel.text = label;
    }

    void OnAction()
    {
        if (_derelict != null) StudyDerelict();
        else if (_comet != null) StudyComet();
    }

    void StudyDerelict()
    {
        var d = _derelict;
        if (d == null || d.studied || EmpireTech.Level < DerelictStudyLevel) return;
        d.studied = true;

        if (d.clueIndex >= 0)
        {
            AncientClues.RevealIndex(d.clueIndex, "an ancient Vael derelict");
        }
        else
        {
            PlayerEconomy.Add(ResourceType.Metal, d.rewardMetal);
            PlayerEconomy.Add(ResourceType.Energy, d.rewardEnergy);
            ResearchManager.AddPoints(d.rewardResearch);
            NotificationManager.Instance?.Push("Derelict salvaged",
                $"Your crews strip the ancient hull — +{d.rewardMetal} metal, +{d.rewardEnergy} energy, " +
                $"+{d.rewardResearch} research.", null, NotifKind.Discovery);
        }

        DerelictRenderer.Instance?.MarkStudied(d);
        RefreshDerelict();
    }

    void StudyComet()
    {
        var c = _comet;
        if (c == null || c.studied || EmpireTech.Level < CometStudyLevel) return;
        CometManager.Instance?.Study(c);
        RefreshComet();
    }
}
