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
        var content = UIFactory.Window(parent, "Ore Codex & Research", new Vector2(430, 620), out root, out _);
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

        foreach (var info in OreDatabase.All())
            BuildCard(info);
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

    void OnDestroy() { ResearchManager.OnChanged -= Refresh; }
}
