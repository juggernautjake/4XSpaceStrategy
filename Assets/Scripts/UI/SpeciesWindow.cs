using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The species codex. Lists all playable species with their biology, habitat, strengths, weaknesses
// and attribute bars, and lets the player switch whose perspective worlds are judged from (which
// re-scores every planet and reshapes the habitable zone).
public class SpeciesWindow : MonoBehaviour
{
    public static SpeciesWindow Instance;

    GameObject root;
    TMP_Text header;
    RectTransform list;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("SpeciesWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<SpeciesWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Species", new Vector2(460, 640), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        header = UIFactory.Text(content, "", UITheme.HeaderSize, UITheme.Accent, TextAlignmentOptions.Left);
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1); hrt.sizeDelta = new Vector2(0, 24);

        var holder = UIFactory.NewUI(content, "ListHolder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder, 0, 0, 30, 0);
        UIFactory.ScrollView(holder, out list);

        SpeciesManager.OnSpeciesChanged += Refresh;
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
        header.text = $"Viewing worlds as: <b>{SpeciesManager.Current.name}</b>";

        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        var all = SpeciesDatabase.All;
        for (int i = 0; i < all.Count; i++)
            BuildCard(all[i], i);
    }

    void BuildCard(Species s, int index)
    {
        bool current = index == SpeciesManager.CurrentIndex;

        var card = UIFactory.Panel(list, "Card", current ? new Color(0.12f, 0.20f, 0.28f, 0.95f) : UITheme.RowBg);
        if (current)
        {
            var ol = card.gameObject.AddComponent<Outline>();
            ol.effectColor = UITheme.Good; ol.effectDistance = new Vector2(1.5f, -1.5f);
        }
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8); vlg.spacing = 3;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = UIFactory.Text(card.transform, $"<b>{s.name}</b>  <size=11><color=#9FB4C8>Known for {s.signature}</color></size>",
            18, new Color(s.color.r, s.color.g, s.color.b), TextAlignmentOptions.Left);
        UIFactory.AddLayout(title.gameObject, 24);

        AddText(card, s.description, UITheme.Text, 34);
        AddText(card, $"<color=#8FD0FF>Biology:</color> {s.biology}", UITheme.SubText, 34);
        AddText(card, $"<color=#8FD0FF>Natural worlds:</color> {s.habitat}", UITheme.SubText, 34);
        AddText(card, $"<color=#4DFF6E>Strengths:</color> {s.strengths}", UITheme.SubText, 34);
        AddText(card, $"<color=#FF7A6E>Weaknesses:</color> {s.weaknesses}", UITheme.SubText, 34);

        // Attribute bars
        AttrBar(card, "Intelligence", s.iq);
        AttrBar(card, "Longevity", s.longevity);
        AttrBar(card, "Fertility", s.fertility);
        AttrBar(card, "Durability", s.durability);
        AttrBar(card, "Adaptability", s.adaptability);

        string climate = s.idealTemp > 0.66f ? "hot worlds" : s.idealTemp < 0.34f ? "cold worlds" : "temperate worlds";
        AddText(card, $"<color=#FFD24D>Prefers {climate}; tolerance x{s.tolerance:0.0}</color>", UITheme.SubText, 20);

        if (!current)
            UIFactory.Button(card.transform, $"View worlds as {s.name}", () => SpeciesManager.Select(index), 28);
        else
            AddText(card, "<b><color=#4DFF6E>&gt; Current perspective</color></b>", UITheme.Good, 20);
    }

    void AddText(Image card, string text, Color color, float h)
    {
        // Height driven by content so nothing clips regardless of length.
        UIFactory.WrapText(card.transform, text, UITheme.SmallSize, color);
    }

    void AttrBar(Image card, string label, int value)
    {
        var row = UIFactory.NewUI(card.transform, "Attr");
        UIFactory.AddLayout(row, 16);

        var lbl = UIFactory.Text(row.transform, label, 11, UITheme.SubText, TextAlignmentOptions.Left);
        var lrt = lbl.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0.38f, 1);
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

        var track = UIFactory.Panel(row.transform, "Track", UITheme.TrackBg);
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(0.4f, 0.2f); trt.anchorMax = new Vector2(0.9f, 0.8f);
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var fill = UIFactory.Panel(track.transform, "Fill", value >= 8 ? UITheme.Good : value >= 5 ? UITheme.Accent : UITheme.Warn);
        var frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(Mathf.Clamp01(value / 10f), 1);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

        var num = UIFactory.Text(row.transform, value + "/10", 11, UITheme.Text, TextAlignmentOptions.Right);
        var nrt = num.rectTransform;
        nrt.anchorMin = new Vector2(0.9f, 0); nrt.anchorMax = new Vector2(1, 1);
        nrt.offsetMin = Vector2.zero; nrt.offsetMax = Vector2.zero;
    }

    void OnDestroy() { SpeciesManager.OnSpeciesChanged -= Refresh; }
}
