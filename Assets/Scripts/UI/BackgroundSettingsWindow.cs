using UnityEngine;
using UnityEngine.UI;

// Controls for the procedural space backdrop: show/hide, regenerate a fresh sky, or switch to a
// flat solid colour chosen with RGB sliders.
public class BackgroundSettingsWindow : MonoBehaviour
{
    public static BackgroundSettingsWindow Instance;

    GameObject root;
    Toggle showT, solidT;
    Slider rS, gS, bS;
    Image swatch;
    bool suppress;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("BackgroundSettingsWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<BackgroundSettingsWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Background", new Vector2(320, 380), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = new Vector2(-40, -20);
        UIFactory.VerticalLayout(content, 8);

        showT = UIFactory.Toggle(content, "Show space background", true, on => { if (!suppress) SpaceBackground.Instance?.SetEnabled(on); });
        solidT = UIFactory.Toggle(content, "Use solid colour instead", false, on => { if (!suppress) SpaceBackground.Instance?.SetSolidMode(on); });

        UIFactory.Button(content, "🔄 Regenerate space", () => SpaceBackground.Instance?.Regenerate(), 32);

        UIFactory.Label(content, "Solid colour", UITheme.HeaderSize, UITheme.Accent, 22);
        rS = UIFactory.LabeledSlider(content, "Red", 0f, 1f, 0.02f, _ => ApplyColor(), "F2");
        gS = UIFactory.LabeledSlider(content, "Green", 0f, 1f, 0.03f, _ => ApplyColor(), "F2");
        bS = UIFactory.LabeledSlider(content, "Blue", 0f, 1f, 0.06f, _ => ApplyColor(), "F2");

        swatch = UIFactory.Panel(content, "Swatch", new Color(0.02f, 0.03f, 0.06f));
        UIFactory.AddLayout(swatch.gameObject, 28);

        root.SetActive(false);
    }

    void ApplyColor()
    {
        if (suppress) return;
        var c = new Color(rS.value, gS.value, bS.value);
        swatch.color = c;
        SpaceBackground.Instance?.SetSolidColor(c);
    }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show)
        {
            Sync();
            root.GetComponent<RectTransform>().SetAsLastSibling();
        }
    }

    void Sync()
    {
        var bg = SpaceBackground.Instance;
        if (bg == null) return;
        suppress = true;
        showT.isOn = bg.Enabled;
        solidT.isOn = bg.SolidMode;
        rS.value = bg.SolidColor.r; gS.value = bg.SolidColor.g; bS.value = bg.SolidColor.b;
        swatch.color = bg.SolidColor;
        suppress = false;
    }
}
