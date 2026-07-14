using System.Text;
using UnityEngine;
using TMPro;

// Opens when you click a system marker in the galaxy view. Lists the system's star and planets, and
// offers a "Go to system" button that flies the camera into that system and restores the detailed view.
public class SystemSummaryWindow : MonoBehaviour
{
    public static SystemSummaryWindow Instance;

    GameObject root;
    TMP_Text titleText, bodyText;
    StarSystemData system;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("SystemSummaryWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<SystemSummaryWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "System", new Vector2(420, 460), out root, out titleText);
        root.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        UIFactory.VerticalLayout(content, 8);

        var holder = UIFactory.NewUI(content, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder, 0, 0, 0, 42);
        UIFactory.ScrollView(holder, out RectTransform col);
        bodyText = UIFactory.Label(col, "", UITheme.SmallSize, UITheme.Text, 40);
        bodyText.alignment = TextAlignmentOptions.TopLeft;

        var go = UIFactory.Button(content, "Go to system", GoTo, 34);
        var grt = go.GetComponent<RectTransform>();
        grt.anchorMin = new Vector2(0, 0); grt.anchorMax = new Vector2(1, 0); grt.pivot = new Vector2(0.5f, 0);
        grt.sizeDelta = new Vector2(-8, 34); grt.anchoredPosition = new Vector2(0, 4);
        go.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;

        root.SetActive(false);
    }

    public void Show(StarSystemData sys)
    {
        if (sys == null) return;
        system = sys;
        titleText.text = sys.name;

        var sb = new StringBuilder();
        var star = sys.combinedStar;
        string starDesc = sys.isBlackHole ? "Black hole"
            : (star != null ? (star.starCount > 1 ? $"{star.starCount} stars" : $"{star.type}-type star") : "star");
        sb.AppendLine($"<b>{starDesc}</b>");
        sb.AppendLine($"Planets: {sys.bodies.Count}\n");

        for (int i = 0; i < sys.bodies.Count; i++)
        {
            var b = sys.bodies[i];
            string owner = b.owner == FactionManager.Player ? "  <color=#4DFF6E>(yours)</color>"
                : (b.owner != null ? $"  <color=#FF9A5C>({FactionManager.OwnerName(b.owner)})</color>" : "");
            string known = b.Surveyed || b.Visited ? $"{b.type}" : "<color=#9FB4C8>unexplored</color>";
            sb.AppendLine($"• <b>{b.name}</b> — {known}{owner}" + (b.moons.Count > 0 ? $"  <size=11><color=#8FA4BE>({b.moons.Count} moon{(b.moons.Count > 1 ? "s" : "")})</color></size>" : ""));
        }

        bodyText.text = sb.ToString();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    void GoTo()
    {
        if (system == null) return;
        CameraController.Instance?.JumpTo(system.galaxyPosition, 140f);
        root.SetActive(false);
    }
}
