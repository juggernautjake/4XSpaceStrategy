using UnityEngine;
using UnityEngine.UI;

// Unified settings: sound (volume/mute) and the space background / view. This is the one place for
// the "game vibe" controls, opened from the pause menu or the HUD.
public class SettingsWindow : MonoBehaviour
{
    public static SettingsWindow Instance;

    GameObject root;
    Toggle muteT, spaceT;
    Slider volS, effS, ambS, rS, gS, bS;
    Image swatch;
    bool suppress;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("SettingsWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<SettingsWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Settings", new Vector2(440, 490), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

        var scroll = UIFactory.ScrollView(content, out RectTransform col);

        UIFactory.Label(col, "SOUND", UITheme.SmallSize, UITheme.Accent, 18);
        muteT = UIFactory.Toggle(col, "Mute all sound", false, on => { if (!suppress) SimpleAudio.Instance?.SetMuted(on); });
        volS = UIFactory.LabeledSlider(col, "Master Volume", 0f, 1f, 0.8f, v => { if (!suppress) SimpleAudio.Instance?.SetVolume(v); }, "F2");
        effS = UIFactory.LabeledSlider(col, "Effects Volume (clicks, selection, alerts)", 0f, 1f, 0.9f, v => { if (!suppress) SimpleAudio.Instance?.SetEffectsVolume(v); }, "F2");
        ambS = UIFactory.LabeledSlider(col, "Ambient Volume (hum, space chatter)", 0f, 1f, 0.8f, v => { if (!suppress) SimpleAudio.Instance?.SetAmbientVolume(v); }, "F2");

        UIFactory.Label(col, "BACKGROUND / VIEW", UITheme.SmallSize, UITheme.Accent, 18);
        spaceT = UIFactory.Toggle(col, "Show space background", true, on => { if (!suppress) SpaceBackground.Instance?.SetEnabled(on); });
        UIFactory.Label(col, "When OFF, the solid colour below is used.", UITheme.SmallSize, UITheme.SubText, 30);
        UIFactory.Button(col, "Regenerate Space", () => SpaceBackground.Instance?.Regenerate(), 30);

        UIFactory.Label(col, "Background Colour", UITheme.SmallSize, UITheme.SubText, 18);
        rS = UIFactory.LabeledSlider(col, "Red", 0f, 1f, 0.02f, _ => ApplyColor(), "F2");
        gS = UIFactory.LabeledSlider(col, "Green", 0f, 1f, 0.03f, _ => ApplyColor(), "F2");
        bS = UIFactory.LabeledSlider(col, "Blue", 0f, 1f, 0.06f, _ => ApplyColor(), "F2");
        swatch = UIFactory.Panel(col, "Swatch", new Color(0.02f, 0.03f, 0.06f));
        UIFactory.AddLayout(swatch.gameObject, 26);

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
        if (show) { Sync(); root.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    public void Open()
    {
        Sync();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    void Sync()
    {
        suppress = true;
        var a = SimpleAudio.Instance;
        if (a != null) { muteT.isOn = a.Muted; volS.value = a.MasterVolume; effS.value = a.EffectsVolume; ambS.value = a.AmbientVolume; }
        var bg = SpaceBackground.Instance;
        if (bg != null)
        {
            spaceT.isOn = bg.SpaceEnabled;
            rS.value = bg.SolidColor.r; gS.value = bg.SolidColor.g; bS.value = bg.SolidColor.b;
            swatch.color = bg.SolidColor;
        }
        suppress = false;
    }
}
