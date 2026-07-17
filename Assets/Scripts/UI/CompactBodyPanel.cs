using UnityEngine;
using UnityEngine.UI;
using TMPro;

// A small, NON-floating info panel pinned to the right edge that appears when you select a world.
//
// It's the first step in de-cluttering the map: a single click on a planet used to throw the entire
// full-screen Planetary Viewer open over the map. Now a click just selects (camera focuses, this compact
// readout appears), and the full viewer opens deliberately — a double-click on the world (PlanetClick)
// or this panel's "Open Planetary View" button. While the full viewer IS up, this panel hides so the two
// don't stack.
public class CompactBodyPanel : MonoBehaviour
{
    public static CompactBodyPanel Instance;

    GameObject root;
    TMP_Text title;
    TMP_Text info;
    CelestialBody body;
    readonly LiveSet live = new LiveSet();

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("CompactBodyPanel");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<CompactBodyPanel>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        // A plain right-anchored Panel, not a UIFactory.Window — no title bar, no drag, no resize grip.
        // It's chrome that follows the selection, not a window you manage.
        var panel = UIFactory.Panel(parent, "CompactBodyPanel", UITheme.PanelBg);
        root = panel.gameObject;
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(300f, 300f);
        rt.anchoredPosition = new Vector2(-16f, 0f);

        UIFactory.VerticalLayout(rt, 8f);

        title = UIFactory.Label(rt, "", UITheme.HeaderSize, UITheme.Accent, 26f);
        info = UIFactory.WrapText(rt, "", UITheme.BodySize, UITheme.Text);
        UIFactory.Button(rt, "Open Planetary View »", () =>
        {
            if (PlanetUI.Selected != null) PlanetViewWindow.Instance?.ShowFor(PlanetUI.Selected);
        }, 32f);

        // The readout refreshes in place every frame (habitability moves during terraforming, ownership
        // and resources change) — the standard LiveSet discipline, no structural rebuild.
        live.Text(info, () => body != null ? Summary(body) : "");

        PlanetUI.OnBodySelected += ShowFor;
        PlanetUI.OnClosed += Hide;
        root.SetActive(false);
    }

    void ShowFor(CelestialBody b)
    {
        body = b;
        if (b != null && title != null) title.text = b.name;
        // Visibility itself is resolved in Update so it can defer to the full viewer.
    }

    void Hide() => body = null;

    void Update()
    {
        if (root == null) return;
        // Show only when a world is selected AND the full-screen viewer isn't up — while the big view is
        // open it owns the screen, and two right-side panels stacked would be exactly the clutter we're
        // removing.
        bool viewerOpen = PlanetViewWindow.Instance != null && PlanetViewWindow.Instance.IsOpen;
        bool shouldShow = body != null && !viewerOpen;
        if (root.activeSelf != shouldShow) root.SetActive(shouldShow);
        if (root.activeSelf) live.Tick();
    }

    static string Summary(CelestialBody b)
    {
        string sub = ColorUtility.ToHtmlStringRGB(UITheme.SubText);
        string owner = b.owner == null ? "Unclaimed"
                     : (b.owner == FactionManager.Player ? "Yours" : "Rival-held");
        float metal  = b.resources != null ? b.resources.Get(ResourceType.Metal) : 0f;
        float energy = b.resources != null ? b.resources.Get(ResourceType.Energy) : 0f;
        float water  = b.resources != null ? b.resources.Get(ResourceType.Water) : 0f;

        return
            $"<color=#{sub}>Type</color>  {TerraformDiagnosis.Pretty(b.type)}\n" +
            $"<color=#{sub}>Owner</color>  {owner}\n" +
            $"<color=#{sub}>Habitability</color>  {b.habitability:F0}%\n" +
            $"<color=#{sub}>Resources</color>  {metal:F0} metal · {energy:F0} energy · {water:F0} water\n\n" +
            $"<color=#{sub}>Double-click the world, or use the button below, to open the full Planetary View.</color>";
    }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= ShowFor;
        PlanetUI.OnClosed -= Hide;
    }
}
