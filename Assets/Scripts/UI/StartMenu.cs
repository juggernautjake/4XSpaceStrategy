using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// The main menu shown when the game launches (over the nebula backdrop, time paused):
// New Game / Load Game / Options / Exit. Starting or loading a game closes it and resumes time.
public class StartMenu : MonoBehaviour
{
    public static StartMenu Instance;

    GameObject root;
    Button firstButton;

    public bool IsOpen => root != null && root.activeSelf;

    public static void Create(Transform canvas)
    {
        if (Instance != null) return;
        var go = new GameObject("StartMenu");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<StartMenu>();
        Instance.Build(canvas);
    }

    void Build(Transform canvas)
    {
        var dim = UIFactory.Panel(canvas, "StartMenu", new Color(0.01f, 0.02f, 0.05f, 0.88f));
        root = dim.gameObject;
        UIFactory.Stretch(dim.rectTransform);

        var box = UIFactory.Panel(dim.transform, "Box", UITheme.PanelBg);
        var brt = box.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(340, 360);
        box.gameObject.AddComponent<Outline>().effectColor = UITheme.AccentDim;
        var vlg = box.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20); vlg.spacing = 12;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        UIFactory.Label(box.transform, "<b>4X SPACE STRATEGY</b>", UITheme.TitleSize, UITheme.Accent, 34).alignment = TMPro.TextAlignmentOptions.Center;
        BuildLights(box.transform);

        firstButton = UIFactory.Button(box.transform, "New Game", () => GenerationMenu.Instance?.Open(), 44);
        UIFactory.Button(box.transform, "Load Game", () => SaveLoadMenu.Instance?.Toggle(), 44);
        UIFactory.Button(box.transform, "Options", () => SettingsWindow.Instance?.Open(), 44);
        UIFactory.Button(box.transform, "Exit", Exit, 44);

        root.SetActive(false);
    }

    void BuildLights(Transform parent)
    {
        var row = UIFactory.NewUI(parent, "Lights");
        UIFactory.AddLayout(row, 16);
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8; hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false;

        Color[] palette = { UITheme.Accent, UITheme.Good, UITheme.Accent, UITheme.Warn, UITheme.Accent, UITheme.Good, UITheme.Accent };
        var dots = new Image[palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            var d = UIFactory.Panel(row.transform, "Dot", palette[i]);
            var le = d.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 10; le.preferredHeight = 10; le.minWidth = 10; le.minHeight = 10;
            dots[i] = d;
        }
        row.AddComponent<BlinkingLights>().lights = dots;
    }

    public void Open()
    {
        TimeControl.Pause();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
        if (firstButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    public void Close()
    {
        root.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
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
