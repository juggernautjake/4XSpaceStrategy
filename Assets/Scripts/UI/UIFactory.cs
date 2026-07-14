using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Builds uGUI elements in code, styled with UITheme. Everything the runtime windows need:
// canvases, panels, text, buttons, sliders, toggles, input fields and scroll views.
public static class UIFactory
{
    public static GameObject NewUI(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    public static void Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
    }

    public static Canvas CreateCanvas(string name, int sortOrder)
    {
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        return canvas;
    }

    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    public static Image Panel(Transform parent, string name, Color color)
    {
        var go = NewUI(parent, name);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    // A draggable window shell with a title bar and a vertical content area. Returns the content
    // RectTransform to add controls to.
    public static RectTransform Window(Transform parent, string title, Vector2 size, out GameObject root, out TMP_Text titleText, bool closeButton = true)
    {
        var win = Panel(parent, title + "Window", UITheme.PanelBg);
        var rootGO = win.gameObject; // local: 'out' params can't be captured in a lambda below
        root = rootGO;
        var rt = win.rectTransform;
        rt.sizeDelta = size;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var outline = win.gameObject.AddComponent<Outline>();
        outline.effectColor = UITheme.AccentDim;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        // Title bar
        var bar = Panel(rt, "TitleBar", UITheme.HeaderBg);
        var barRT = bar.rectTransform;
        barRT.anchorMin = new Vector2(0, 1); barRT.anchorMax = new Vector2(1, 1);
        barRT.pivot = new Vector2(0.5f, 1); barRT.sizeDelta = new Vector2(0, 34);
        barRT.anchoredPosition = Vector2.zero;
        bar.gameObject.AddComponent<DraggableWindow>().target = rt;

        titleText = Text(barRT, title, UITheme.HeaderSize, UITheme.Accent, TextAlignmentOptions.Left);
        Stretch(titleText.rectTransform, 12, 40, 0, 0);

        if (closeButton)
        {
            var close = Button(barRT, "X", null);
            var crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1, 0.5f); crt.anchorMax = new Vector2(1, 0.5f);
            crt.pivot = new Vector2(1, 0.5f); crt.sizeDelta = new Vector2(30, 26);
            crt.anchoredPosition = new Vector2(-4, 0);
            close.onClick.AddListener(() => rootGO.SetActive(false));
        }

        // Content area — generous horizontal inset so nothing hugs (or hides under) the frame.
        var content = NewUI(rt, "Content").GetComponent<RectTransform>();
        Stretch(content, 18, 18, 42, 16);

        // Resize grip (bottom-right corner) — drag to resize the window.
        var grip = Panel(rt, "ResizeGrip", new Color(UITheme.Accent.r, UITheme.Accent.g, UITheme.Accent.b, 0.55f));
        var grt = grip.rectTransform;
        grt.anchorMin = grt.anchorMax = new Vector2(1, 0);
        grt.pivot = new Vector2(1, 0);
        grt.sizeDelta = new Vector2(18, 18);
        grt.anchoredPosition = Vector2.zero;
        var resize = grip.gameObject.AddComponent<ResizableWindow>();
        resize.target = rt;
        resize.minSize = new Vector2(Mathf.Min(size.x, 260f), Mathf.Min(size.y, 150f));

        // Keep the window within the screen at any resolution.
        rootGO.AddComponent<WindowFit>();

        return content;
    }

    // Adds a VerticalLayoutGroup to a container for stacked controls.
    public static VerticalLayoutGroup VerticalLayout(RectTransform container, float spacing = 6f, RectOffset padding = null)
    {
        var v = container.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = padding ?? new RectOffset(14, 14, 10, 10);
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        return v;
    }

    public static TMP_Text Text(Transform parent, string content, int size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var go = NewUI(parent, "Text");
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    public static TMP_Text Label(Transform parent, string content, int size, Color color, float height = 22f)
    {
        var t = Text(parent, content, size, color);
        StretchHorizontally(t);
        t.margin = new Vector4(4, 1, 4, 1);   // small inner text inset so it never touches the edge
        // Content-driven height (with a floor) so multi-line labels never clip inside layout groups.
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.flexibleWidth = 1;                 // encourage the layout group to give it the full width
        return t;
    }

    // Multi-line text whose HEIGHT is driven by its content (no fixed height -> never clipped).
    // Use inside a VerticalLayoutGroup with childControlHeight = true.
    public static TMP_Text WrapText(Transform parent, string content, int size, Color color)
    {
        var t = Text(parent, content, size, color);
        StretchHorizontally(t);
        t.margin = new Vector4(4, 1, 4, 1);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.minHeight = size + 4;             // at least one line
        le.flexibleWidth = 1;
        // No preferredHeight: the VerticalLayoutGroup will query TMP's own preferred height.
        return t;
    }

    // Anchor a text element to fill its parent's width so it can't end up center-anchored and clipped.
    static void StretchHorizontally(TMP_Text t)
    {
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0, rt.anchorMin.y);
        rt.anchorMax = new Vector2(1, rt.anchorMax.y);
        rt.offsetMin = new Vector2(0, rt.offsetMin.y);
        rt.offsetMax = new Vector2(0, rt.offsetMax.y);
    }

    public static LayoutElement AddLayout(GameObject go, float preferredHeight, float minHeight = -1)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredHeight = preferredHeight;
        if (minHeight >= 0) le.minHeight = minHeight;
        return le;
    }

    public static UnityEngine.UI.Button Button(Transform parent, string label, Action onClick, float height = 30f)
    {
        var go = NewUI(parent, "Button");
        var img = go.AddComponent<Image>();
        img.color = UITheme.ButtonBg;
        var btn = go.AddComponent<UnityEngine.UI.Button>();
        var colors = btn.colors;
        colors.normalColor = UITheme.ButtonBg;
        colors.highlightedColor = UITheme.ButtonHover;
        colors.pressedColor = UITheme.ButtonActive;
        colors.selectedColor = UITheme.ButtonBg;
        colors.disabledColor = new Color(0.1f, 0.12f, 0.15f, 0.6f);
        btn.colors = colors;

        var t = Text(go.transform, label, UITheme.BodySize, UITheme.Text, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
        btn.onClick.AddListener(() => SimpleAudio.Instance?.PlayClick());
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        go.AddComponent<ButtonHoverSound>();   // soft hover cue
        AddLayout(go, height);
        return btn;
    }

    public static UnityEngine.UI.Toggle Toggle(Transform parent, string label, bool isOn, Action<bool> onChanged, float height = 24f)
    {
        var go = NewUI(parent, "Toggle");
        var toggle = go.AddComponent<UnityEngine.UI.Toggle>();
        AddLayout(go, height);

        var box = Panel(go.transform, "Box", UITheme.TrackBg);
        var boxRT = box.rectTransform;
        boxRT.anchorMin = new Vector2(0, 0.5f); boxRT.anchorMax = new Vector2(0, 0.5f);
        boxRT.pivot = new Vector2(0, 0.5f); boxRT.sizeDelta = new Vector2(18, 18);
        boxRT.anchoredPosition = new Vector2(2, 0);
        var check = Panel(box.transform, "Check", UITheme.Good);
        Stretch(check.rectTransform, 3, 3, 3, 3);

        var t = Text(go.transform, label, UITheme.BodySize, UITheme.Text, TextAlignmentOptions.Left);
        var trt = t.rectTransform;
        Stretch(trt, 26, 2, 0, 0);

        toggle.targetGraphic = box;
        toggle.graphic = check;
        toggle.isOn = isOn;
        toggle.onValueChanged.AddListener(_ => SimpleAudio.Instance?.PlayClick());
        if (onChanged != null) toggle.onValueChanged.AddListener(v => onChanged(v));
        return toggle;
    }

    // A labelled slider with a live value readout. Returns the Slider; updates the readout itself.
    public static UnityEngine.UI.Slider LabeledSlider(Transform parent, string label, float min, float max, float value,
                                       Action<float> onChanged, string valueFormat = "F1", float height = 44f)
    {
        var group = NewUI(parent, label + "Group");
        var grt = group.GetComponent<RectTransform>();
        AddLayout(group, height);

        var caption = Text(group.transform, label, UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.TopLeft);
        caption.margin = new Vector4(2, 0, 4, 0);
        var caprt = caption.rectTransform;
        caprt.anchorMin = new Vector2(0, 1); caprt.anchorMax = new Vector2(0.82f, 1);
        caprt.pivot = new Vector2(0, 1); caprt.sizeDelta = new Vector2(0, 16); caprt.anchoredPosition = Vector2.zero;

        var readout = Text(group.transform, value.ToString(valueFormat), UITheme.SmallSize, UITheme.Accent, TextAlignmentOptions.TopRight);
        readout.margin = new Vector4(0, 0, 2, 0);
        var readrt = readout.rectTransform;
        readrt.anchorMin = new Vector2(0.82f, 1); readrt.anchorMax = new Vector2(1, 1);
        readrt.pivot = new Vector2(1, 1); readrt.sizeDelta = new Vector2(0, 16); readrt.anchoredPosition = Vector2.zero;

        var slider = Slider(group.transform, min, max, value, v =>
        {
            readout.text = v.ToString(valueFormat);
            SimpleAudio.Instance?.PlayTick();
            onChanged?.Invoke(v);
        });
        var srt = slider.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0);
        srt.pivot = new Vector2(0.5f, 0); srt.sizeDelta = new Vector2(0, 18); srt.anchoredPosition = new Vector2(0, 2);
        return slider;
    }

    public static UnityEngine.UI.Slider Slider(Transform parent, float min, float max, float value, Action<float> onChanged)
    {
        var go = NewUI(parent, "Slider");
        // Transparent root graphic so the whole slider area reliably receives drags.
        var rootImg = go.AddComponent<Image>();
        rootImg.color = new Color(0, 0, 0, 0);
        var slider = go.AddComponent<UnityEngine.UI.Slider>();

        var bg = Panel(go.transform, "Background", UITheme.TrackBg);
        var bgRT = bg.rectTransform;
        bgRT.anchorMin = new Vector2(0, 0.35f); bgRT.anchorMax = new Vector2(1, 0.65f);
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

        var fillArea = NewUI(go.transform, "Fill Area").GetComponent<RectTransform>();
        fillArea.anchorMin = new Vector2(0, 0.35f); fillArea.anchorMax = new Vector2(1, 0.65f);
        fillArea.offsetMin = new Vector2(5, 0); fillArea.offsetMax = new Vector2(-15, 0);
        var fill = Panel(fillArea, "Fill", UITheme.Accent);
        var fillRT = fill.rectTransform;
        fillRT.anchorMin = new Vector2(0, 0); fillRT.anchorMax = new Vector2(1, 1);
        fillRT.sizeDelta = new Vector2(10, 0);

        var handleArea = NewUI(go.transform, "Handle Slide Area").GetComponent<RectTransform>();
        handleArea.anchorMin = new Vector2(0, 0); handleArea.anchorMax = new Vector2(1, 1);
        handleArea.offsetMin = new Vector2(10, 0); handleArea.offsetMax = new Vector2(-10, 0);
        var handle = Panel(handleArea, "Handle", new Color(0.6f, 0.85f, 1f));
        var handleRT = handle.rectTransform;
        handleRT.sizeDelta = new Vector2(16, 0);
        handleRT.anchorMin = new Vector2(0, 0); handleRT.anchorMax = new Vector2(0, 1);

        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handle;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        // No keyboard navigation, so WASD/arrow panning never changes a slider (e.g. the time slider).
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        slider.minValue = min; slider.maxValue = max; slider.value = value;
        if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
        return slider;
    }

    public static TMP_InputField InputField(Transform parent, string placeholder, string initial = "", float height = 30f)
    {
        var go = NewUI(parent, "InputField");
        var bg = go.AddComponent<Image>();
        bg.color = UITheme.TrackBg;
        var input = go.AddComponent<TMP_InputField>();
        AddLayout(go, height);

        var area = NewUI(go.transform, "Text Area").GetComponent<RectTransform>();
        Stretch(area, 8, 8, 4, 4);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = Text(area, placeholder, UITheme.BodySize, UITheme.SubText, TextAlignmentOptions.Left);
        Stretch(ph.rectTransform);
        ph.fontStyle = FontStyles.Italic;

        var txt = Text(area, "", UITheme.BodySize, UITheme.Text, TextAlignmentOptions.Left);
        Stretch(txt.rectTransform);

        input.textViewport = area;
        input.textComponent = txt;
        input.placeholder = ph;
        input.text = initial;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    // Vertical scroll view. Returns the ScrollRect; out param is the content to fill.
    public static ScrollRect ScrollView(Transform parent, out RectTransform content)
    {
        var go = NewUI(parent, "ScrollView");
        Stretch(go.GetComponent<RectTransform>()); // fill parent (else it collapses to zero size)
        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.15f);
        var scroll = go.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;

        var viewport = NewUI(go.transform, "Viewport").GetComponent<RectTransform>();
        Stretch(viewport);
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(1, 1, 1, 0.02f);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = NewUI(viewport, "Content").GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1); content.anchoredPosition = Vector2.zero;
        // Force the content to be EXACTLY the viewport width (left/right offsets = 0). Without this a
        // nonzero default horizontal offset makes the content wider than the viewport and centred, so
        // left-aligned rows spill off the left edge and get clipped — the root cause of the text-cutoff
        // seen across scrolling menus (Fleet, New Game, Settings, Species, Save/Load, etc.).
        content.offsetMin = new Vector2(0f, content.offsetMin.y);
        content.offsetMax = new Vector2(0f, content.offsetMax.y);
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6; vlg.padding = new RectOffset(14, 14, 10, 10);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        scroll.viewport = viewport;
        scroll.content = content;
        return scroll;
    }
}
