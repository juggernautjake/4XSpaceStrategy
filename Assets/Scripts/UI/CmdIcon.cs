using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// AN ICON BUTTON, AND THE THREE THINGS IT HAS TO SAY AT A GLANCE
//
// The fleet controls were twenty text buttons reading "Line\nabreast" and "Withdraw\nif hurt" at
// eight point. That is legible exactly once — the first time, when the player reads all twenty — and
// never again, because a wall of small words is something you parse rather than something you
// recognise. Nobody picking a formation mid-engagement is reading.
//
// Every control has to answer three questions instantly:
//
//   WHAT IS IT?        the icon (see tools/make-command-icons.mjs — the formation icons are diagrams
//                      of the formation, so there is nothing to learn)
//   IS IT ON?          the fill and the border
//   CAN I PRESS IT?    the tint, and whether the tooltip explains why not
//
// ---- WHY THE ICONS ARE TINTED RATHER THAN PRE-COLOURED ---------------------------------------
//
// The art is flat white on transparent. A control has at least four states here — idle, hovered,
// active, unavailable — and the same icons also appear in the roster, in tooltips and on the ship
// panel, each wanting a different weight. Pre-rendering twenty-five icons in four states is a hundred
// files that fall out of step; tinting is twenty-five files and one multiply.
//
// It also means the whole set follows the theme: change UITheme.Accent and every active control
// changes with it, rather than a hundred PNGs quietly staying the old blue.
//
// ---- AND WHY A DISABLED BUTTON IS STILL HOVERABLE --------------------------------------------
//
// A greyed-out control that will not say why it is greyed out is the single most frustrating thing a
// UI can do. Unity's Button stops running its own pointer callbacks when `interactable` is false, so
// the tooltip is attached by UIFactory.Tooltip — a separate handler on the same object, which still
// hears the raycast. The disabled tooltip carries the REASON.
// ============================================================================================
public class CmdIcon : MonoBehaviour
{
    /// The look of a control that is available but not currently chosen.
    static readonly Color IdleFill = new Color(0.11f, 0.16f, 0.22f, 0.95f);
    static readonly Color IdleIcon = new Color(0.72f, 0.82f, 0.92f, 1f);

    /// ...currently chosen. The fill lifts and the icon goes bright, so an active control reads as
    /// active from the fill alone at the edge of vision and from the icon when looked at.
    static readonly Color OnFill = new Color(0.15f, 0.42f, 0.62f, 1f);
    static readonly Color OnIcon = new Color(1f, 1f, 1f, 1f);
    static readonly Color OnEdge = new Color(0.45f, 0.82f, 1f, 1f);

    /// ...and not available. Deliberately still VISIBLE rather than nearly invisible: a control the
    /// player cannot use yet is information about the game, and hiding it means they never learn it
    /// exists. Dim enough to read as off, solid enough to read as a thing.
    static readonly Color OffFill = new Color(0.07f, 0.09f, 0.12f, 0.9f);
    static readonly Color OffIcon = new Color(0.34f, 0.40f, 0.47f, 1f);

    Image fill;
    Image edge;
    RawImage icon;
    Button button;

    bool on, enabledState = true;

    /// Build one. `iconName` is the file under SpaceAssets/CommandIcons without the `Cmd_` prefix;
    /// pass null for a label-only button.
    public static CmdIcon Make(Transform parent, string iconName, string label, float width,
                               System.Action onClick, string tip)
    {
        var go = UIFactory.NewUI(parent, $"Cmd_{iconName ?? label}");
        var rt = go.GetComponent<RectTransform>();

        var c = go.AddComponent<CmdIcon>();

        c.fill = go.AddComponent<Image>();
        c.fill.color = IdleFill;

        // The active border, as four edges at a fixed thickness so it stays a hairline whatever the
        // button's size — the same trick the index icon bar uses.
        var frame = UIFactory.NewUI(go.transform, "Edge").GetComponent<RectTransform>();
        frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one;
        frame.offsetMin = Vector2.zero; frame.offsetMax = Vector2.zero;
        c.edge = frame.gameObject.AddComponent<Image>();
        c.edge.color = new Color(0, 0, 0, 0);
        c.edge.raycastTarget = false;
        Edge(frame, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2f), Vector2.zero);
        Edge(frame, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 2f));
        Edge(frame, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(2f, 0));
        Edge(frame, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2f, 0), Vector2.zero);

        bool hasLabel = !string.IsNullOrEmpty(label);

        if (!string.IsNullOrEmpty(iconName))
        {
            var ig = UIFactory.NewUI(go.transform, "Icon");
            c.icon = ig.AddComponent<RawImage>();
            c.icon.raycastTarget = false;
            c.icon.texture = Resources.Load<Texture2D>($"SpaceAssets/CommandIcons/Cmd_{iconName}");
            c.icon.color = IdleIcon;
            var irt = c.icon.rectTransform;
            irt.anchorMin = new Vector2(0.5f, 1f); irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.sizeDelta = new Vector2(24, 24);
            irt.anchoredPosition = new Vector2(0, hasLabel ? -3f : -(40f - 24f) * 0.5f);
        }

        if (hasLabel)
        {
            var t = UIFactory.Text(go.transform, label, 8, IdleIcon, TextAlignmentOptions.Center);
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 0);
            trt.sizeDelta = new Vector2(-2, 12);
            trt.anchoredPosition = new Vector2(0, 1);
            t.raycastTarget = false;
        }

        c.button = go.AddComponent<Button>();
        c.button.targetGraphic = c.fill;
        if (onClick != null) c.button.onClick.AddListener(() => onClick());

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.flexibleWidth = 0;

        if (!string.IsNullOrEmpty(tip)) UIFactory.Tooltip(go, tip);

        c.Apply();
        return c;
    }

    static void Edge(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var e = UIFactory.NewUI(parent, "E").GetComponent<RectTransform>();
        e.anchorMin = aMin; e.anchorMax = aMax;
        e.offsetMin = oMin; e.offsetMax = oMax;
        var img = e.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        e.gameObject.AddComponent<FrameEdgeTint>();
    }

    /// Chosen / not chosen.
    public CmdIcon SetOn(bool value) { on = value; Apply(); return this; }

    /// Available / not. A disabled control keeps its tooltip, which is where the reason lives.
    public CmdIcon SetEnabled(bool value) { enabledState = value; Apply(); return this; }

    /// Replace the tooltip — for controls whose explanation depends on the current state, like a
    /// button that reads "Hold position" one moment and "Release" the next.
    public CmdIcon SetTip(string tip)
    {
        if (!string.IsNullOrEmpty(tip)) UIFactory.Tooltip(gameObject, tip);
        return this;
    }

    void Apply()
    {
        if (button != null) button.interactable = enabledState;

        if (!enabledState)
        {
            if (fill != null) fill.color = OffFill;
            if (icon != null) icon.color = OffIcon;
            if (edge != null) edge.color = new Color(0, 0, 0, 0);
            return;
        }

        if (fill != null) fill.color = on ? OnFill : IdleFill;
        if (icon != null) icon.color = on ? OnIcon : IdleIcon;
        if (edge != null) edge.color = on ? OnEdge : new Color(0, 0, 0, 0);
    }
}
