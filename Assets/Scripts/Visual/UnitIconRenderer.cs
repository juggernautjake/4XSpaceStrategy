using System.Collections.Generic;
using UnityEngine;

// Generates simple, recognizable placeholder icons for each ship class:
//  Scout        = up-pointing triangle (fast arrowhead)
//  Research Ship = diamond
//  Fighter      = right-pointing triangle (aggressive arrow)
//  Colony Ship  = filled circle (settlement)
public static class UnitIconRenderer
{
    static readonly Dictionary<UnitType, Texture2D> cache = new Dictionary<UnitType, Texture2D>();
    static readonly Dictionary<UnitType, Sprite> spriteCache = new Dictionary<UnitType, Sprite>();

    public static Texture2D Get(UnitType t)
    {
        if (cache.TryGetValue(t, out var tex)) return tex;
        var info = UnitDatabase.Get(t);
        tex = Build(info.iconShape, info.iconColor);
        cache[t] = tex;
        return tex;
    }

    // The same token as a UI sprite, cached so the build menus can show an icon per row without
    // allocating a fresh Sprite every time a list is rebuilt.
    public static Sprite Sprite(UnitType t)
    {
        if (spriteCache.TryGetValue(t, out var s)) return s;
        var tex = Get(t);
        s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        spriteCache[t] = s;
        return s;
    }

    static Texture2D Build(int shape, Color c)
    {
        int s = 48;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        Color outline = new Color(0, 0, 0, 0.9f);

        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s * 2f - 1f;
                float v = (y + 0.5f) / s * 2f - 1f;
                float d = ShapeDistance(shape, u, v);   // <0 inside, ~0 edge
                Color col;
                if (d < -0.06f) col = c;
                else if (d < 0.02f) col = outline;      // edge
                else col = new Color(0, 0, 0, 0);
                px[y * s + x] = col;
            }

        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    // Signed-ish distance: negative inside the shape.
    static float ShapeDistance(int shape, float u, float v)
    {
        switch (shape)
        {
            case 0: return -InTri(u, v, 0f, 0.85f, -0.8f, -0.7f, 0.8f, -0.7f);      // up triangle
            case 1: return (Mathf.Abs(u) + Mathf.Abs(v)) - 0.9f;                     // diamond
            case 2: return -InTri(u, v, 0.85f, 0f, -0.7f, -0.8f, -0.7f, 0.8f);       // right triangle
            default: return Mathf.Sqrt(u * u + v * v) - 0.85f;                       // circle
        }
    }

    // Returns positive-ish "insideness" (used negated as distance). ~0 near edges.
    static float InTri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float d1 = Sign(px, py, ax, ay, bx, by);
        float d2 = Sign(px, py, bx, by, cx, cy);
        float d3 = Sign(px, py, cx, cy, ax, ay);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        bool inside = !(hasNeg && hasPos);
        return inside ? 0.1f : -0.1f;   // crude but fine for a placeholder token
    }

    static float Sign(float px, float py, float ax, float ay, float bx, float by)
        => (px - bx) * (ay - by) - (ax - bx) * (py - by);
}
