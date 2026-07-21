using UnityEngine;

// Renders a planet's surface to a point-filtered Texture2D for the detailed map view.
// It samples the SAME deterministic noise field as the low-res grid (just far more densely, with
// extra octaves), so the continents and oceans line up exactly between the two views — the detailed
// version simply resolves finer coastlines and features. Ore-bearing areas are tinted so mineral
// regions are visible.
public static class SurfaceTextureRenderer
{
    // ---- On terrain colour ----
    // Terrain renders at FULL vibrance in every view — the map, the build grid and the 3D globes all get
    // the same colours TerrainColorMap defines, untouched.
    //
    // There used to be a MapTone() here that desaturated each map pixel 30% toward its own grey and
    // lifted it 28% toward white. Its job was to make the terrain recede so that placed structures — the
    // only saturated things left — read as the foreground. That worked, but it paid for the figure by
    // damaging the ground: every biome on every map view was permanently washed out, and a map's whole
    // job is to show you the ground.
    //
    // It's gone because structures no longer need it. They're drawn Vivid AND carry a thin black outline
    // (PlanetViewWindow.OutlineFootprint), and an outline separates figure from ground without touching
    // the ground at all — which is what an outline is for. Layering a global desaturation on top of that
    // would now be paying the cost twice for a separation already achieved.
    //
    // If structures ever stop reading clearly, the fix belongs on the structure: a heavier outline, a
    // drop shadow, anything local. Not another pass over every terrain pixel on the planet.

    // ============================================================================================
    // GRID-RESOLUTION render: EXACTLY ONE TEXEL PER GRID CELL.
    //
    // Reads body.surface.tiles DIRECTLY rather than re-sampling the noise field at a resolution that
    // ought to match. That's a stronger guarantee than matching numbers: the terrain you see IS the grid
    // the placement code tests against, so a tile and a footprint cell cannot drift apart even if
    // somebody changes the dimensions later.
    //
    // Build() below now samples at the same resolution (both take it from MapMetrics), so the two agree
    // — but only this one agrees BY CONSTRUCTION. Prefer it for anything you can build on.
    // ============================================================================================
    public static Texture2D BuildGrid(CelestialBody body)
    {
        if (body?.surface == null) return null;
        int w = body.surface.width, h = body.surface.height;

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,   // hard cell edges: each texel reads as one buildable tile
            // Repeat, not Clamp: the map is a cylinder, so u=1 must blend into u=0. Under Clamp the edge
            // texel blends with itself and the join shows as a faint line on the 3D globe.
            wrapMode = TextureWrapMode.Repeat
        };

        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var tile = body.surface.tiles[x, y];
                if (tile == null) { pixels[y * w + x] = Color.black; continue; }

                // Per-type colour, so every terrain type stays clearly distinguishable.
                Color c = TerrainColorMap.Get(tile.type);

                // The same per-tile shade jitter the detailed map uses, so the two views still look
                // like the same world — just at different fidelity.
                float b = Mathf.Lerp(0.86f, 1.12f, tile.shade);
                c = new Color(c.r * b, c.g * b, c.b * b, 1f);

                // NO ore tint here — deliberately.
                //
                // This texture is the TERRAIN, and it is used by the planet map, the moon panes, the moon
                // thumbnails, the 3D globe and the loading screen alike. Tinting ore into it meant named
                // deposits speckled across every one of those views at all times, which both muddied the
                // terrain read and gave away a world's mineral wealth at a glance from anywhere.
                // Deposits are drawn on the OVERLAY layer instead, under the Mineral Index — see
                // PlanetViewWindow.RefreshOverlay. One place, one rule, and the globe gets it for free.
                pixels[y * w + x] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    public static Texture2D Build(CelestialBody body)
    {
        // From MapMetrics, which is also what the grid is built at — so this renders exactly one texel
        // per cell, same as BuildGrid. The bare `* 6` that used to live here was the whole bug: it made
        // this render six times finer than the grid it was supposed to be depicting.
        int w = MapMetrics.SurfW(body);
        int h = MapMetrics.SurfH(body);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,   // crisp "pixel map" look
            // Repeat, not Clamp: the map is a cylinder, so u=1 must blend into u=0. Under Clamp the edge
            // texel blends with itself and the join shows as a faint line on the 3D globe.
            wrapMode = TextureWrapMode.Repeat
        };

        var p = body.terrainParams; // same params as the grid -> both views always match
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

                // No ore tint — same rule as BuildGrid above. Deposits belong to the Mineral Index
                // overlay, not to the terrain.
                pixels[y * w + x] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
