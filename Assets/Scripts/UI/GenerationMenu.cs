using UnityEngine;
using UnityEngine.UI;
using TMPro;

// New-game wizard: name your faction, pick your species and difficulty, choose galaxy size (number
// of systems + average planets per system), then generate. The home system always gets a
// difficulty-appropriate habitable home world your faction owns (with its moons).
public class GenerationMenu : MonoBehaviour
{
    public static GenerationMenu Instance;

    GameObject root;
    TMP_InputField nameInput;
    Slider systemsS, planetsS;
    TMP_Text summary;

    int selectedSpecies = 0;
    Difficulty selectedDifficulty = Difficulty.Medium;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("GenerationMenu");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<GenerationMenu>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "New Game", new Vector2(480, 580), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        var scroll = UIFactory.ScrollView(content, out RectTransform col);

        UIFactory.Label(col, "FACTION NAME", UITheme.SmallSize, UITheme.Accent, 16);
        nameInput = UIFactory.InputField(col, "Name your empire…", "Your Empire");

        UIFactory.Label(col, "SPECIES", UITheme.SmallSize, UITheme.Accent, 16);
        var all = SpeciesDatabase.All;
        for (int i = 0; i < all.Count; i++)
        {
            int idx = i;
            var s = all[i];
            UIFactory.Button(col, $"{s.name} — {s.signature}", () => { selectedSpecies = idx; UpdateSummary(); }, 28);
        }

        UIFactory.Label(col, "DIFFICULTY", UITheme.SmallSize, UITheme.Accent, 16);
        UIFactory.Button(col, "Easy  (home 100%, more resources, fast research)", () => { selectedDifficulty = Difficulty.Easy; UpdateSummary(); }, 28);
        UIFactory.Button(col, "Medium  (home 90-99%)", () => { selectedDifficulty = Difficulty.Medium; UpdateSummary(); }, 28);
        UIFactory.Button(col, "Hard  (home 80-89%, scarce, slow research)", () => { selectedDifficulty = Difficulty.Hard; UpdateSummary(); }, 28);

        UIFactory.Label(col, "GALAXY", UITheme.SmallSize, UITheme.Accent, 16);
        systemsS = UIFactory.LabeledSlider(col, "Number of systems", 1f, 12f, 5f, _ => UpdateSummary(), "F0");
        planetsS = UIFactory.LabeledSlider(col, "Average planets per system", 1f, 8f, 4f, _ => UpdateSummary(), "F0");

        summary = UIFactory.Label(col, "", UITheme.SmallSize, UITheme.Text, 40);

        UIFactory.Button(col, "Start Game", StartGame, 38);

        root.SetActive(false);
    }

    void UpdateSummary()
    {
        var s = SpeciesDatabase.Get(selectedSpecies);
        summary.text = $"<b>{s.name}</b> · <b>{selectedDifficulty}</b> · " +
                       $"{Mathf.RoundToInt(systemsS.value)} systems, ~{Mathf.RoundToInt(planetsS.value)} planets each";
    }

    public void Open()
    {
        UpdateSummary();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    public void Toggle() { if (root.activeSelf) root.SetActive(false); else Open(); }

    void StartGame()
    {
        SpeciesManager.Select(selectedSpecies);
        GameConfig.CurrentDifficulty = selectedDifficulty;
        string fn = string.IsNullOrWhiteSpace(nameInput.text) ? "Your Empire" : nameInput.text.Trim();
        GameConfig.FactionName = fn;
        if (FactionManager.Player != null) FactionManager.Player.name = fn;

        GameManager.Instance?.GenerateGalaxy(Mathf.RoundToInt(systemsS.value), Mathf.RoundToInt(planetsS.value));

        root.SetActive(false);
        StartMenu.Instance?.Close();
        EscapeMenu.Instance?.Close();
        TimeControl.Resume();
    }
}
