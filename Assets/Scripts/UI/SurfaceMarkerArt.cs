using UnityEngine;

// Procedural art for the surface-map selection marker: a ring and a downward arrow.
//
// Generated in code rather than shipped as sprites, for the same reason the ship tokens are
// (UnitIconRenderer): no import step, no meta files, no asset that can go missing on a fresh checkout.
public static class SurfaceMarkerArt
{
    static Sprite ring, arrow;

    /// A hollow circle. Used as the pulsing selection ring around the chosen structure.
    public static Sprite Ring()
    {
        if (ring != null) return ring;

        const int s = 128;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];

        const float outer = 0.98f, inner = 0.80f;   // ring thickness, in normalised radius
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s * 2f - 1f;
                float v = (y + 0.5f) / s * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);

                // Soft edges on both sides of the band so the ring doesn't read as jagged when it pulses.
                float a = Mathf.Clamp01((outer - d) / 0.05f) * Mathf.Clamp01((d - inner) / 0.05f);
                px[y * s + x] = new Color(1f, 1f, 1f, a);
            }

        tex.SetPixels(px); tex.Apply();
        ring = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        return ring;
    }

    /// A solid downward-pointing triangle with a short stem — the "it's this one" pointer that hovers
    /// above the selected structure.
    public static Sprite Arrow()
    {
        if (arrow != null) return arrow;

        const int w = 64, h = 80;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;          // 0..1 across
                float v = (y + 0.5f) / h;          // 0 at the point (bottom), 1 at the top

                bool inside;
                if (v < 0.55f)
                {
                    // Head: a triangle that narrows to a point at the bottom.
                    float halfWidth = Mathf.Lerp(0f, 0.5f, v / 0.55f);
                    inside = Mathf.Abs(u - 0.5f) <= halfWidth;
                }
                else
                {
                    // Stem.
                    inside = Mathf.Abs(u - 0.5f) <= 0.16f;
                }
                px[y * w + x] = inside ? Color.white : new Color(1, 1, 1, 0);
            }

        tex.SetPixels(px); tex.Apply();
        arrow = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f));   // pivot at the POINT
        return arrow;
    }
}
