using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// The pause / main menu. Opening it pauses the simulation; closing resumes the previous speed.
public class EscapeMenu : MonoBehaviour
{
    public static EscapeMenu Instance;

    GameObject root;
    Button firstButton;

    public bool IsOpen => root != null && root.activeSelf;

    public static void Create(Transform canvas)
    {
        if (Instance != null) return;
        var go = new GameObject("EscapeMenu");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<EscapeMenu>();
        Instance.Build(canvas);
    }

    void Build(Transform canvas)
    {
        // Full-screen dim overlay that blocks the game while paused.
        var dim = UIFactory.Panel(canvas, "EscapeMenu", new Color(0.01f, 0.02f, 0.04f, 0.72f));
        root = dim.gameObject;
        UIFactory.Stretch(dim.rectTransform);

        // Centred box of options.
        var box = UIFactory.Panel(dim.transform, "Box", UITheme.PanelBg);
        var brt = box.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(300, 380);
        box.gameObject.AddComponent<Outline>().effectColor = UITheme.AccentDim;
        var vlg = box.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 18, 18); vlg.spacing = 10;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        UIFactory.Label(box.transform, "<b>PAUSED</b>", UITheme.TitleSize, UITheme.Accent, 30).alignment = TMPro.TextAlignmentOptions.Center;

        BuildLights(box.transform);

        firstButton = UIFactory.Button(box.transform, "Resume", Close, 40);
        UIFactory.Button(box.transform, "New Game", NewGame, 40);
        UIFactory.Button(box.transform, "Save", () => { SaveLoadMenu.Instance?.Toggle(); }, 40);
        UIFactory.Button(box.transform, "Load", () => { SaveLoadMenu.Instance?.Toggle(); }, 40);
        UIFactory.Button(box.transform, "Settings", () => { SettingsWindow.Instance?.Open(); }, 40);
        UIFactory.Button(box.transform, "Exit", Exit, 40);

        root.SetActive(false);
    }

    void BuildLights(Transform parent)
    {
        var row = UIFactory.NewUI(parent, "Lights");
        UIFactory.AddLayout(row, 16);
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8; hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false;

        Color[] palette = { UITheme.Good, UITheme.Accent, UITheme.Warn, UITheme.Good, UITheme.Accent, UITheme.Bad, UITheme.Good };
        var dots = new Image[palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            var d = UIFactory.Panel(row.transform, "Dot", palette[i]);
            var le = d.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 10; le.preferredHeight = 10; le.minWidth = 10; le.minHeight = 10;
            dots[i] = d;
        }
        var bl = row.AddComponent<BlinkingLights>();
        bl.lights = dots;
    }

    // NOT DURING GENERATION.
    //
    // `Galaxy` is assigned and the start menu is closed roughly HALFWAY through the load, so without the
    // third clause Escape opens the pause menu over the loading screen — on top of it, since Open() calls
    // SetAsLastSibling — with Save, Load and New Game all live. Saving there captured the galaxy mid-
    // cinematic, with every system flagged as concealed by the genesis sequence; loading or starting
    // another game there ran a second generation into the middle of the first one. The loading screen is
    // not a state the player is meant to be able to act from.
    static bool GameRunning => GameManager.Instance != null && GameManager.Instance.Galaxy != null
                               && !GameManager.Instance.IsGenerating
                               && !(StartMenu.Instance != null && StartMenu.Instance.IsOpen);

    void Update()
    {
        // Esc opens the pause menu only once a game is actually running (the start menu handles launch).
        if (Input.GetKeyDown(KeyCode.Escape) && (IsOpen || GameRunning)) Toggle();
    }

    public void Toggle() { if (root.activeSelf) Close(); else Open(); }

    public void Open()
    {
        if (!GameRunning) return;   // no game yet -> the start menu is showing
        TimeControl.Pause();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
        // Give the menu keyboard focus so WASD / arrows navigate the options.
        if (firstButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    public void Close()
    {
        root.SetActive(false);
        TimeControl.Resume();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    void NewGame()
    {
        // Open the galaxy generation menu (systems + avg planets).
        GenerationMenu.Instance?.Open();
    }

    void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
