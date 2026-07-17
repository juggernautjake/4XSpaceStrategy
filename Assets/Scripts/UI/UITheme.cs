using UnityEngine;

// Shared palette + sizing for all runtime-built UI so windows match one another (dark translucent
// panels with a cyan accent, echoing the blue orbit rings).
public static class UITheme
{
    // Space/tech restyle v1 (palette only — safe, reversible). Deeper, higher-contrast panels so text
    // and the map read more cleanly; a brighter cyan accent for a more "holographic" feel. The textural
    // pass (panel gradients, glow borders, 9-slice) belongs in UIFactory and is best tuned against real
    // screenshots — see the UI-consolidation planning doc.
    public static readonly Color PanelBg     = new Color(0.045f, 0.065f, 0.11f, 0.95f);
    public static readonly Color HeaderBg    = new Color(0.09f, 0.16f, 0.25f, 1.00f);
    public static readonly Color RowBg       = new Color(0.09f, 0.13f, 0.19f, 0.88f);
    public static readonly Color Accent      = new Color(0.34f, 0.78f, 1.00f, 1.00f);
    public static readonly Color AccentDim   = new Color(0.20f, 0.44f, 0.64f, 1.00f);
    public static readonly Color Good        = new Color(0.32f, 1.00f, 0.50f, 1.00f);
    public static readonly Color Warn        = new Color(1.00f, 0.76f, 0.32f, 1.00f);
    public static readonly Color Bad         = new Color(1.00f, 0.42f, 0.38f, 1.00f);

    public static readonly Color Text        = new Color(0.88f, 0.94f, 1.00f, 1.00f);
    public static readonly Color SubText     = new Color(0.58f, 0.70f, 0.84f, 1.00f);

    public static readonly Color ButtonBg    = new Color(0.12f, 0.20f, 0.30f, 1.00f);
    public static readonly Color ButtonHover = new Color(0.19f, 0.33f, 0.47f, 1.00f);
    public static readonly Color ButtonActive= new Color(0.26f, 0.50f, 0.70f, 1.00f);
    public static readonly Color TrackBg     = new Color(0.06f, 0.09f, 0.14f, 1.00f);

    public const int TitleSize = 20;
    public const int HeaderSize = 16;
    public const int BodySize = 14;
    public const int SmallSize = 12;
}
