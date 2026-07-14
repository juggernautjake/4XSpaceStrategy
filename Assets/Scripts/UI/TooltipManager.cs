using UnityEngine;
using UnityEngine.UI;
using TMPro;

// A single floating tooltip used by the terrain viewers and POI markers. It can follow the cursor
// or dock just above a given viewer window. Self-creates on first use so nothing needs wiring.
public class TooltipManager : MonoBehaviour
{
    static TooltipManager _instance;
    public static TooltipManager Instance => _instance != null ? _instance : Create();

    RectTransform panel;
    TMP_Text label;
    Canvas canvas;

    static TooltipManager Create()
    {
        var go = new GameObject("TooltipManager");
        _instance = go.AddComponent<TooltipManager>();
        _instance.Build();
        return _instance;
    }

    void Awake() { if (_instance == null) { _instance = this; Build(); } }

    void Build()
    {
        if (panel != null) return;
        canvas = UIFactory.CreateCanvas("TooltipCanvas", 5000); // above all windows
        canvas.transform.SetParent(transform, false);

        var bg = UIFactory.Panel(canvas.transform, "Tooltip", new Color(0.04f, 0.07f, 0.11f, 0.96f));
        panel = bg.rectTransform;
        panel.pivot = new Vector2(0.5f, 0f);
        var outline = bg.gameObject.AddComponent<Outline>();
        outline.effectColor = UITheme.Accent;
        outline.effectDistance = new Vector2(1f, -1f);

        var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.Fit.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.Fit.PreferredSize;
        var vlg = bg.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.childControlWidth = true; vlg.childControlHeight = true;

        label = UIFactory.Text(panel, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.TopLeft);
        var le = label.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 240;
        bg.raycastTarget = false;
        label.raycastTarget = false;

        panel.gameObject.SetActive(false);
    }

    // Docks the tooltip just above the top edge of a viewer window.
    public void ShowAboveRect(RectTransform target, string text)
    {
        if (target == null) { ShowAtCursor(text); return; }
        label.text = text;
        panel.gameObject.SetActive(true);
        panel.SetAsLastSibling();

        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners); // overlay -> screen pixels
        Vector3 topCenter = (corners[1] + corners[2]) * 0.5f;
        panel.pivot = new Vector2(0.5f, 0f);
        panel.position = topCenter + new Vector3(0, 10f, 0);
    }

    public void ShowAtCursor(string text)
    {
        label.text = text;
        panel.gameObject.SetActive(true);
        panel.SetAsLastSibling();
        panel.pivot = new Vector2(0f, 1f);
        panel.position = Input.mousePosition + new Vector3(16f, -8f, 0f);
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }
}
