using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Opens when the star is clicked. Shows the star's physical attributes (heat/light/mass) and a
// toggle that reveals the green Goldilocks zone (and green rings on habitable worlds).
public class StarInfoPanel : MonoBehaviour
{
    public static StarInfoPanel Instance;

    GameObject root;
    TMP_Text titleText;
    TMP_Text body;
    Toggle zoneToggle;
    bool suppress;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("StarInfoPanel");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<StarInfoPanel>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Star", new Vector2(400, 320), out root, out titleText);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-260, 60);

        UIFactory.VerticalLayout(content, 8);
        body = UIFactory.Label(content, "", UITheme.BodySize, UITheme.Text, 180);
        body.alignment = TextAlignmentOptions.TopLeft;

        zoneToggle = UIFactory.Toggle(content, "Show Habitable Zone", false, on =>
        {
            if (suppress) return;
            if (SystemContext.Zone != null) SystemContext.Zone.SetVisible(on);
        });

        root.SetActive(false);
    }

    public void Show(StarData star)
    {
        if (star == null) return;

        string kind = star.isBlackHole ? "Black Hole"
            : star.starCount >= 3 ? "Ternary System"
            : star.starCount == 2 ? "Binary System"
            : $"{star.type}-type Star";
        titleText.text = string.IsNullOrEmpty(star.name) ? kind : $"{star.name} — {kind}";

        string hz = star.hasHabitableZone
            ? $"{star.hzInner:F1} - {star.hzOuter:F1} units"
            : "<color=#FF6659>none</color>";

        if (star.isBlackHole)
        {
            body.text =
                "A collapsed star of immense gravity.\n" +
                $"Mass: {star.mass:0.#} x Sun\n" +
                "It emits no light of its own — only the glow of its accretion disc.\n\n" +
                $"<b>Habitable Zone:</b> {hz}";
        }
        else
        {
            body.text =
                (star.starCount > 1 ? $"Stars: {star.starCount}\n" : $"Class: {star.type}\n") +
                $"Temperature: {star.temperatureK:N0} K\n" +
                $"Luminosity: {star.luminosity:0.##} x Sun\n" +
                $"Mass: {star.mass:0.##} x Sun\n" +
                $"Light intensity: {star.lightIntensity:0.0}\n\n" +
                $"<b>Habitable Zone:</b> {hz}";
        }

        suppress = true;
        zoneToggle.isOn = SystemContext.Zone != null && SystemContext.Zone.IsVisible;
        zoneToggle.interactable = star.hasHabitableZone;
        suppress = false;

        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    public void Hide() { if (root != null) root.SetActive(false); }
}
