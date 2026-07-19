using UnityEngine;
using UnityEngine.UI;
using TMPro;

// A small, semi-transparent floating window used by the Planet View for the tile hover-info window
// and the moon-tab hover window — both asked for the exact same anchor behaviour, so one shared,
// self-creating panel (like TooltipManager) serves both rather than building two one-off windows.
//
// Anchored by its BOTTOM-LEFT corner to the mouse with a slight rightward offset, so the window grows
// up and to the right of the cursor and the cursor never sits over its own tooltip. That's the opposite
// corner from TooltipManager.ShowAtCursor (top-left), which is why this is a separate class rather than
// a new mode on that one — changing TooltipManager's anchor would move every existing tooltip that uses it.
public class MapHoverPanel : MonoBehaviour
{
    static MapHoverPanel _instance;
    static bool _quitting;

    // Lazily self-creating — but NEVER during teardown. On scene close / stop-play this panel is destroyed,
    // yet other objects' OnDestroy (e.g. PlanetViewWindow) still call Instance.Hide(); without a guard that
    // call spawns a FRESH MapHoverPanel (and, via its canvas, an EventSystem) from inside OnDestroy, which
    // Unity reports as "some objects were not cleaned up (did you spawn from OnDestroy?)". Application.isPlaying
    // alone flips too late to catch this — the teardown OnDestroy calls run while it's still true — so we also
    // watch Application.quitting, which fires FIRST, at the very start of shutdown.
    public static MapHoverPanel Instance =>
        _instance != null ? _instance : (Application.isPlaying && !_quitting ? Create() : null);

    [RuntimeInitializeOnLoadMethod]
    static void HookShutdown()
    {
        _quitting = false;                       // fresh play session (also covers domain-reload-off in the Editor)
        Application.quitting -= MarkQuitting;     // de-dupe so the handler doesn't stack across sessions
        Application.quitting += MarkQuitting;
    }
    static void MarkQuitting() => _quitting = true;

    RectTransform panel;
    TMP_Text label;

    static readonly Vector2 CursorOffset = new Vector2(14f, 6f);

    static MapHoverPanel Create()
    {
        var go = new GameObject("MapHoverPanel");
        _instance = go.AddComponent<MapHoverPanel>();
        _instance.Build();
        return _instance;
    }

    void Awake() { if (_instance == null) { _instance = this; Build(); } }

    void Build()
    {
        if (panel != null) return;
        var canvas = UIFactory.CreateCanvas("MapHoverCanvas", 5001); // above windows and the plain tooltip
        canvas.transform.SetParent(transform, false);

        // "Somewhat transparent" — noticeably lower alpha than a normal window (UITheme.PanelBg is 0.94).
        var bg = UIFactory.Panel(canvas.transform, "MapHover", new Color(0.04f, 0.06f, 0.09f, 0.55f));
        panel = bg.rectTransform;
        panel.pivot = new Vector2(0f, 0f);   // bottom-left corner is the anchor point

        var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = bg.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 6);
        vlg.childControlWidth = true; vlg.childControlHeight = true;

        label = UIFactory.Text(panel, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.TopLeft);
        var le = label.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 220;
        bg.raycastTarget = false;
        label.raycastTarget = false;

        // A dark shadow behind the text is what keeps it legible over whatever the transparent panel is
        // sitting on — the map underneath can be anything from snow-white to lava-red.
        var shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1f, -1f);

        panel.gameObject.SetActive(false);
    }

    /// Rich-text content, positioned with its bottom-left corner at the mouse plus a slight rightward
    /// offset. Call every frame the hover target is still active; call Hide() the moment it isn't.
    public void ShowAtCursor(string richText)
    {
        label.text = richText;
        panel.gameObject.SetActive(true);
        panel.SetAsLastSibling();
        panel.position = Input.mousePosition + (Vector3)CursorOffset;
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }
}
