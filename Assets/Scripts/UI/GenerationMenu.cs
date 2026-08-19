using UnityEngine;
using UnityEngine.UI;
using TMPro;

// New-game wizard: name your faction, pick your species and difficulty, choose how many star systems
// the galaxy holds, then generate. How full each of those systems is is not a setting — it comes from
// the mass of the system's own star (see SolarSystemGenerator). The home system always gets a
// difficulty-appropriate habitable home world your faction owns (with its moons).
public class GenerationMenu : MonoBehaviour
{
    public static GenerationMenu Instance;

    GameObject root;
    TMP_InputField nameInput;
    Slider systemsS;
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
        var content = UIFactory.Window(parent, "New Game", new Vector2(520, 640), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        // Scrollable wizard fills the window EXCEPT the bottom strip, where Start Game is pinned so it
        // is always visible without needing to scroll or resize.
        var holder = UIFactory.NewUI(content, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder, 0, 0, 0, 46);
        UIFactory.ScrollView(holder, out RectTransform col);

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

        // "Average planets per system" USED TO BE HERE, and it is gone rather than hidden.
        //
        // A system's size is decided by its Solar System Mass allowance now — 100 per solar mass of its
        // star or stars, spent outward on planets, their moons and any belts until it runs out (see
        // SolarSystemGenerator). That makes a binary genuinely richer than a red dwarf, which is the
        // point. A slider asking for four planets is a second, contradictory answer to the same
        // question: a dim star that can only afford two either ignores the slider or breaks the ceiling,
        // and there is no version of that where both rules are true.
        //
        // What replaced it as information is the note below — the player still gets told what decides
        // how full their galaxy will be, which is what the slider was really communicating.
        UIFactory.Label(col,
            "<size=10><color=#9FB4C8>Each system's planets, moons and asteroid belts are funded by the " +
            "mass of its own star: brighter suns and multiple-star systems build larger systems.</color></size>",
            UITheme.SmallSize, UITheme.Text, 34);

        summary = UIFactory.Label(col, "", UITheme.SmallSize, UITheme.Text, 40);

        // Start Game pinned to the bottom of the window (outside the scroll), always visible.
        var start = UIFactory.Button(content, "Start Game", StartGame, 40);
        var srt = start.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(0.5f, 0);
        srt.sizeDelta = new Vector2(-8, 40); srt.anchoredPosition = new Vector2(0, 4);
        start.GetComponent<LayoutElement>().ignoreLayout = true;

        root.SetActive(false);
    }

    void UpdateSummary()
    {
        var s = SpeciesDatabase.Get(selectedSpecies);
        summary.text = $"<b>{s.name}</b> · <b>{selectedDifficulty}</b> · " +
                       $"{Mathf.RoundToInt(systemsS.value)} systems";
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

        // Menus close FIRST, so the loading screen covers an empty backdrop rather than the wizard.
        root.SetActive(false);
        StartMenu.Instance?.Close();
        EscapeMenu.Instance?.Close();

        // Time stays paused until the galaxy exists. Resuming here would run the simulation against a
        // half-built world for the frames generation spans — colony ticks and faction AI on a galaxy
        // whose systems are still being added.
        GameManager.Instance?.GenerateGalaxyAsync(Mathf.RoundToInt(systemsS.value), TimeControl.Resume);
    }
}
