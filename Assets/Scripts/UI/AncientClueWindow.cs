using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The Vael Codex — a specially-styled viewer for the ancient civilisation's message fragments. Deliberately
// NOT a normal window: a dark obsidian slab framed in gold and turquoise, an Aztec-by-way-of-alien look, with
// ten numbered relic slots down the left and the chosen fragment's cryptic text carved on the right. Locked
// slots read as worn, empty stone; recovered ones glow. Opens itself when a new fragment is found, and from
// the HUD's "Codex" button any time.
public class AncientClueWindow : MonoBehaviour
{
    public static AncientClueWindow Instance;

    // The codex palette.
    static readonly Color Stone   = new Color(0.09f, 0.075f, 0.06f, 0.99f);
    static readonly Color Gold    = new Color(0.85f, 0.70f, 0.32f);
    static readonly Color Teal    = new Color(0.42f, 0.90f, 0.82f);
    static readonly Color Dim     = new Color(0.34f, 0.30f, 0.24f);
    static readonly Color SlotOff = new Color(0.15f, 0.13f, 0.10f, 0.95f);
    static readonly Color SlotOn  = new Color(0.20f, 0.17f, 0.09f, 0.98f);

    GameObject root;
    RectTransform listCol;
    TMP_Text progressText, glyphText, titleText, bodyText, footText;
    int selected;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("AncientClueWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<AncientClueWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "The Vael Codex", new Vector2(700, 560), out root, out TMP_Text title);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        // Re-skin the window frame: obsidian slab, gold rim.
        var bg = root.GetComponent<Image>();
        if (bg != null) bg.color = Stone;
        var outline = root.GetComponent<Outline>();
        if (outline != null) { outline.effectColor = Gold; outline.effectDistance = new Vector2(2f, -2f); }
        if (title != null) { title.color = Gold; title.text = "THE  VAEL  CODEX"; }

        // Header: a turquoise rule, the eyebrow line, the progress count.
        Rule(content, 1f, Teal, -2f);
        var eyebrow = UIFactory.Text(content, "RELICS OF A VANISHED STAR-FARING PEOPLE", 11, Gold, TextAlignmentOptions.Center);
        Place(eyebrow.rectTransform, 0, 1, 1, 1, new Vector2(0, -6), new Vector2(0, 18));
        progressText = UIFactory.Text(content, "", UITheme.SmallSize, Teal, TextAlignmentOptions.Center);
        Place(progressText.rectTransform, 0, 1, 1, 1, new Vector2(0, -24), new Vector2(0, 18));

        // Left: the ten relic slots.
        var listHolder = UIFactory.NewUI(content, "Slots").GetComponent<RectTransform>();
        Place(listHolder, 0, 0, 0.4f, 1, Vector2.zero, Vector2.zero, top: 46, bottom: 2, right: 6);
        UIFactory.ScrollView(listHolder, out listCol);

        // Right: the chosen fragment, carved on the slab.
        var detail = UIFactory.NewUI(content, "Fragment").GetComponent<RectTransform>();
        Place(detail, 0.4f, 0, 1, 1, Vector2.zero, Vector2.zero, top: 46, bottom: 2, left: 6);
        var dv = detail.gameObject.AddComponent<VerticalLayoutGroup>();
        dv.padding = new RectOffset(14, 14, 12, 12); dv.spacing = 8;
        dv.childControlWidth = true; dv.childControlHeight = true;
        dv.childForceExpandWidth = true; dv.childForceExpandHeight = false;
        dv.childAlignment = TextAnchor.UpperLeft;
        var detailBg = detail.gameObject.AddComponent<Image>();
        detailBg.color = new Color(0.06f, 0.05f, 0.04f, 0.85f);

        glyphText = UIFactory.Text(detail, "", 30, Gold, TextAlignmentOptions.Center);
        AddH(glyphText.gameObject, 40);
        titleText = UIFactory.Text(detail, "", 18, Teal, TextAlignmentOptions.Center);
        titleText.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        AddH(titleText.gameObject, 26);
        RuleIn(detail, Gold);
        bodyText = UIFactory.WrapText(detail, "", 14, new Color(0.92f, 0.88f, 0.78f));
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        var ble = bodyText.gameObject.AddComponent<LayoutElement>(); ble.flexibleHeight = 1;
        footText = UIFactory.WrapText(detail, "", 11, Gold);
        footText.alignment = TextAlignmentOptions.Center;

        AncientClues.OnChanged += RefreshIfOpen;
        RebuildSlots();
        Select(0);
        root.SetActive(false);
    }

    void OnDestroy() { AncientClues.OnChanged -= RefreshIfOpen; }

    void RebuildSlots()
    {
        if (listCol == null) return;
        for (int i = listCol.childCount - 1; i >= 0; i--) Destroy(listCol.GetChild(i).gameObject);

        for (int i = 0; i < AncientClues.Total; i++)
        {
            int idx = i;
            bool found = AncientClues.IsFound(i);

            var row = UIFactory.Panel(listCol, "Slot" + i, found ? SlotOn : SlotOff);
            UIFactory.AddLayout(row.gameObject, 40);
            var ol = row.gameObject.AddComponent<Outline>();
            ol.effectColor = found ? Gold : Dim; ol.effectDistance = new Vector2(1f, -1f);

            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.padding = new RectOffset(8, 8, 2, 2);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            var num = UIFactory.Text(row.transform, Roman(i), 15, found ? Gold : Dim, TextAlignmentOptions.Center);
            num.fontStyle = FontStyles.Bold;
            var nle = num.gameObject.AddComponent<LayoutElement>(); nle.preferredWidth = 34; nle.minWidth = 34; nle.flexibleWidth = 0;
            num.raycastTarget = false;

            string label = found ? AncientClues.Get(i).title : "— sealed —";
            var lbl = UIFactory.Text(row.transform, label, UITheme.SmallSize, found ? Teal : Dim, TextAlignmentOptions.Left);
            lbl.fontStyle = found ? FontStyles.Bold : FontStyles.Italic;
            lbl.raycastTarget = false;
            var lle = lbl.gameObject.AddComponent<LayoutElement>(); lle.flexibleWidth = 1;

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row;
            btn.onClick.AddListener(() => { SimpleAudio.Instance?.PlayClick(); Select(idx); });
        }
    }

    public void Select(int index)
    {
        selected = Mathf.Clamp(index, 0, AncientClues.Total - 1);
        bool found = AncientClues.IsFound(selected);
        var clue = AncientClues.Get(selected);

        if (progressText != null)
            progressText.text = AncientClues.AllFound
                ? $"<b>ALL {AncientClues.Total} VOICES RECOVERED</b>"
                : $"{AncientClues.FoundCount} / {AncientClues.Total} recovered";

        if (glyphText != null) glyphText.text = found ? $"·  {Roman(selected)}  ·" : $"·  {Roman(selected)}  ·";
        if (titleText != null)
        {
            titleText.text = found ? clue.title : "SEALED FRAGMENT";
            titleText.color = found ? Teal : Dim;
        }
        if (bodyText != null)
        {
            if (found)
            {
                bodyText.text = $"“{clue.body}”";
                bodyText.color = new Color(0.92f, 0.88f, 0.78f);
            }
            else
            {
                bodyText.text = "This voice is still sealed in stone.\n\nSomewhere among the stars a world remembers it. " +
                                "Find that world, survey it, and study it on the ground — and the Vael will speak.";
                bodyText.color = new Color(0.62f, 0.58f, 0.5f);
            }
        }
        if (footText != null)
            footText.text = AncientClues.AllFound
                ? "— the road continues —"
                : (found ? "— a voice of the Vael —" : "");
    }

    void RefreshIfOpen()
    {
        RebuildSlots();
        if (root != null && root.activeSelf) Select(selected);
    }

    // Open the codex on a specific fragment (a fresh find opens it here).
    public void Show(int index)
    {
        if (root == null) return;
        RebuildSlots();
        Select(index);
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    public void Toggle()
    {
        if (root == null) return;
        bool show = !root.activeSelf;
        if (show) { RebuildSlots(); Select(selected); root.GetComponent<RectTransform>().SetAsLastSibling(); }
        root.SetActive(show);
    }

    // ---- small styling helpers ----
    static string Roman(int n)
    {
        string[] r = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return (n >= 0 && n < r.Length) ? r[n] : (n + 1).ToString();
    }

    // A thin coloured rule pinned near the top of a container.
    static void Rule(Transform parent, float _unused, Color c, float y)
    {
        var bar = UIFactory.Panel(parent, "Rule", c);
        Place(bar.rectTransform, 0, 1, 1, 1, new Vector2(0, y), new Vector2(0, 2));
    }

    // A rule inside a vertical layout (takes a layout slot).
    static void RuleIn(Transform parent, Color c)
    {
        var bar = UIFactory.Panel(parent, "Rule", c);
        AddH(bar.gameObject, 2);
    }

    static void AddH(GameObject go, float h)
    {
        var le = UIFactory.Ensure<LayoutElement>(go);
        le.preferredHeight = h; le.minHeight = h;
    }

    // Anchor+size a RectTransform with optional inset margins.
    static void Place(RectTransform rt, float aMinX, float aMinY, float aMaxX, float aMaxY,
                      Vector2 anchoredPos, Vector2 sizeDelta,
                      float top = 0, float bottom = 0, float left = 0, float right = 0)
    {
        rt.anchorMin = new Vector2(aMinX, aMinY);
        rt.anchorMax = new Vector2(aMaxX, aMaxY);
        if (aMinX == aMaxX || aMinY == aMaxY)   // a pinned strip, not a stretched region
        {
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
        }
        else
        {
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }
    }
}
