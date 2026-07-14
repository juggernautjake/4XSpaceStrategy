using UnityEngine;

// Shared palette + sizing for all runtime-built UI so windows match one another (dark translucent
// panels with a cyan accent, echoing the blue orbit rings).
public static class UITheme
{
    public static readonly Color PanelBg     = new Color(0.06f, 0.09f, 0.14f, 0.94f);
    public static readonly Color HeaderBg    = new Color(0.10f, 0.17f, 0.26f, 1.00f);
    public static readonly Color RowBg       = new Color(0.10f, 0.14f, 0.20f, 0.85f);
    public static readonly Color Accent      = new Color(0.35f, 0.72f, 1.00f, 1.00f);
    public static readonly Color AccentDim   = new Color(0.22f, 0.42f, 0.60f, 1.00f);
    public static readonly Color Good        = new Color(0.30f, 1.00f, 0.45f, 1.00f);
    public static readonly Color Warn        = new Color(1.00f, 0.75f, 0.30f, 1.00f);
    public static readonly Color Bad         = new Color(1.00f, 0.40f, 0.35f, 1.00f);

    public static readonly Color Text        = new Color(0.86f, 0.92f, 1.00f, 1.00f);
    public static readonly Color SubText     = new Color(0.60f, 0.70f, 0.82f, 1.00f);

    public static readonly Color ButtonBg    = new Color(0.14f, 0.22f, 0.32f, 1.00f);
    public static readonly Color ButtonHover = new Color(0.20f, 0.34f, 0.48f, 1.00f);
    public static readonly Color ButtonActive= new Color(0.26f, 0.46f, 0.64f, 1.00f);
    public static readonly Color TrackBg     = new Color(0.08f, 0.11f, 0.16f, 1.00f);

    public const int TitleSize = 20;
    public const int HeaderSize = 16;
    public const int BodySize = 14;
    public const int SmallSize = 12;
}
