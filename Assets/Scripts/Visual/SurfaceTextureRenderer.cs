using UnityEngine;

// Renders a planet's surface to a point-filtered Texture2D for the detailed map view.
// It samples the SAME deterministic noise field as the low-res grid (just far more densely, with
// extra octaves), so the continents and oceans line up exactly between the two views — the detailed
// version simply resolves finer coastlines and features. Ore-bearing areas are tinted so mineral
// regions are visible.
public static class SurfaceTextureRenderer
{
    public static Texture2D Build(CelestialBody body)
    {
        int w = Mathf.Clamp(body.surfaceSize * 2 * 6, 96, 384);
        int h = Mathf.Max(48, w / 2);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,   // crisp "pixel map" look
            wrapMode = TextureWrapMode.Clamp
        };

        var p = PlanetTerrainGenerator.NoiseParams.Default;
        var pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float v = (y + 0.5f) / h;

                var s = PlanetTerrainGenerator.SampleNormalized(body, u, v, p, 6);
                Color c = TerrainColorMap.Get(s.terrain);
                float b = Mathf.Lerp(0.80f, 1.18f, s.shade);
                c = new Color(c.r * b, c.g * b, c.b * b, 1f);

                // Emphasise coastlines: darken the waterline a touch for clearer continents.
                if (s.water && s.elevation > 0.30f)
                    c *= 0.85f;

                // Tint mineral-rich regions from the low-res ore map.
                if (body.surface != null)
                {
                    int lx = Mathf.Clamp((int)(u * body.surface.width), 0, body.surface.width - 1);
                    int ly = Mathf.Clamp((int)(v * body.surface.height), 0, body.surface.height - 1);
                    var tile = body.surface.tiles[lx, ly];
                    if (tile != null && tile.HasOre)
                    {
                        Color oc = OreDatabase.Get(tile.ore).color;
                        c = Color.Lerp(c, oc, 0.4f);
                    }
                }

                pixels[y * w + x] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
