using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Named save management: type a name and Save, or Load/Delete any existing save. Multiple saves
// per game are supported — each is its own file.
public class SaveLoadMenu : MonoBehaviour
{
    public static SaveLoadMenu Instance;

    GameObject root;
    TMP_InputField nameInput;
    RectTransform list;
    TMP_Text status;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("SaveLoadMenu");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<SaveLoadMenu>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Save / Load", new Vector2(440, 560), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        UIFactory.VerticalLayout(content, 8);

        UIFactory.Label(content, "Save current game", UITheme.HeaderSize, UITheme.Accent, 22);
        nameInput = UIFactory.InputField(content, "Enter a save name…", DefaultName());

        UIFactory.Button(content, "Save", DoSave, 32);

        status = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.Good, 18);

        UIFactory.Label(content, "Existing saves", UITheme.HeaderSize, UITheme.Accent, 22);

        var holder = UIFactory.NewUI(content, "ListHolder").GetComponent<RectTransform>();
        UIFactory.AddLayout(holder.gameObject, 340);
        UIFactory.ScrollView(holder, out list);

        root.SetActive(false);
    }

    string DefaultName()
    {
        var star = GameManager.Instance != null && GameManager.Instance.CurrentStar != null
            ? GameManager.Instance.CurrentStar.type.ToString() : "System";
        return $"{star} save";
    }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show) { RefreshList(); root.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    void DoSave()
    {
        string name = string.IsNullOrWhiteSpace(nameInput.text) ? DefaultName() : nameInput.text.Trim();
        var game = GameStateSerializer.Capture(name);
        SaveSystem.Save(game);
        SimpleAudio.Instance?.PlaySave();
        status.text = $"Saved '{name}'.";
        RefreshList();
    }

    void RefreshList()
    {
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        var saves = SaveSystem.ListSaves();
        if (saves.Count == 0)
        {
            UIFactory.Label(list, "No saves yet.", UITheme.SmallSize, UITheme.SubText, 20);
            return;
        }

        foreach (var g in saves)
            BuildRow(g);
    }

    void BuildRow(SaveGame g)
    {
        var row = UIFactory.Panel(list, "Row", UITheme.RowBg);
        UIFactory.AddLayout(row.gameObject, 58);

        var info = UIFactory.Text(row.transform, $"<b>{g.saveName}</b>\n<size=11><color=#8FA4BE>{g.summary}  ·  {ShortDate(g.savedAtIso)}</color></size>",
            UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        var irt = info.rectTransform;
        irt.anchorMin = new Vector2(0, 0); irt.anchorMax = new Vector2(1, 1);
        irt.offsetMin = new Vector2(8, 0); irt.offsetMax = new Vector2(-150, 0);

        var load = UIFactory.Button(row.transform, "Load", () => DoLoad(g.saveName), 26);
        var lrt = load.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(1, 0.5f); lrt.anchorMax = new Vector2(1, 0.5f);
        lrt.pivot = new Vector2(1, 0.5f); lrt.sizeDelta = new Vector2(66, 30); lrt.anchoredPosition = new Vector2(-78, 0);
        load.GetComponent<LayoutElement>().ignoreLayout = true;

        var del = UIFactory.Button(row.transform, "Delete", () => DoDelete(g.saveName), 26);
        var drt = del.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(1, 0.5f); drt.anchorMax = new Vector2(1, 0.5f);
        drt.pivot = new Vector2(1, 0.5f); drt.sizeDelta = new Vector2(66, 30); drt.anchoredPosition = new Vector2(-6, 0);
        del.GetComponent<LayoutElement>().ignoreLayout = true;
        var dc = del.colors; dc.normalColor = new Color(0.4f, 0.15f, 0.15f); del.colors = dc;
    }

    void DoLoad(string name)
    {
        var g = SaveSystem.Load(name);
        if (g == null) { status.text = $"Could not load '{name}'."; return; }
        GameStateSerializer.Apply(g);
        SimpleAudio.Instance?.PlayLoad();
        status.text = $"Loaded '{name}'.";
        root.SetActive(false);
        StartMenu.Instance?.Close();     // in case we loaded from the main menu
        EscapeMenu.Instance?.Close();
        TimeControl.Resume();
    }

    void DoDelete(string name)
    {
        SaveSystem.Delete(name);
        status.text = $"Deleted '{name}'.";
        RefreshList();
    }

    static string ShortDate(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        return iso.Length >= 16 ? iso.Substring(0, 16).Replace('T', ' ') : iso;
    }
}
