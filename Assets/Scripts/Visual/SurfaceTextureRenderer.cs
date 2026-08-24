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

                // Per-type colour, so every terrain type stays clearly distinguishable...
                Color c = TerrainColorMap.Get(tile.type);

                // ...and then, on a gas giant only, moved round the wheel by that world's own tint. The
                // table is keyed on terrain type and so cannot tell one giant from another; this is the
                // only place the BODY gets a say. Banding, storms and the per-tile jitter are all
                // structure and survive untouched — see GasGiantPalette.
                c = GasGiantPalette.Apply(body, c, tile.type);

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

    // ============================================================================================
    // GRID render WITH BIOME TEXTURE: N x N texels per grid cell, still exactly one CELL per cell.
    //
    // Same tiles, same colours, same shade jitter as BuildGrid — the only difference is that each cell
    // is filled with its biome's grain (TerrainTextureMap) instead of one flat texel. The ONE-TO-ONE
    // RULE in MapMetrics is untouched: the texture is a picture of the grid at N times the resolution,
    // and nothing measures it. It is assigned to a RawImage whose UVs run 0..1 either way, so the map's
    // on-screen size, its zoom, its overlays and every footprint test are unaffected — verified by the
    // fact that `mapTex` is written to `mapImage.texture` and never read, measured or sampled back.
    //
    // WHY THIS IS A SEPARATE ENTRY POINT. BuildGrid also feeds the moon thumbnails, the moon panes and
    // the 3D globes, and those are drawn a couple of hundred pixels across. Handing them a texture
    // sixty-four times the size buys nothing a player can see and costs the memory on every body in the
    // system at once. Only the Planet View map — the one you actually build on, and the only one ever
    // zoomed in far enough to resolve a cell — asks for this.
    //
    // N is a power of two so the pattern downsample stays an exact box filter and the per-tile offset
    // below can be a mask. It is the largest such size that keeps the whole texture inside the budget,
    // so a 100x50 moon gets the art at its native 16 and a 640x320 super-earth gets 4.
    // ============================================================================================

    /// Texels the textured map may spend. 8M is 32 MB at RGBA32 — one texture, for one body, freed when
    /// the window closes. At this budget a 400x200 world renders at 8 texels per cell (3200x1600).
    public const int MapTexelBudget = 8 * 1024 * 1024;

    public static Texture2D BuildGridTextured(CelestialBody body) => BuildGridTextured(body, MapTexelBudget);

    public static Texture2D BuildGridTextured(CelestialBody body, int texelBudget)
    {
        if (body?.surface == null) return null;
        int w = body.surface.width, h = body.surface.height;

        int scale = ScaleFor(w, h, texelBudget);
        // At one texel per cell there is no room for a pattern — a 1x1 patch is just the mean, which is
        // the flat colour. Hand it straight to the plain path rather than doing the same work slower.
        if (scale <= 1) return BuildGrid(body);

        int tw = w * scale, th = h * scale;
        var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,   // still hard edges: this is pixel art, not a photograph
            wrapMode = TextureWrapMode.Repeat
        };

        // One pattern lookup per TYPE rather than per tile — a 400x200 world has 80,000 tiles and 41
        // types, and Pattern() is a dictionary hit plus a string concat.
        int typeCount = System.Enum.GetValues(typeof(TerrainType)).Length;
        var pattern = new float[typeCount][];
        var colour = new Color[typeCount];
        for (int t = 0; t < typeCount; t++)
        {
            var type = (TerrainType)t;
            pattern[t] = TerrainTextureMap.Pattern(type, scale);
            // PER BODY, not per type. This array used to be the raw table, which meant the detailed map
            // ignored GasGiantPalette entirely — a methane giant drew blue on the grid and tan here.
            // Folding the palette in as the array is built keeps the one-lookup-per-type optimisation
            // and costs one extra call per TYPE rather than per tile.
            colour[t] = GasGiantPalette.Apply(body, TerrainColorMap.Get(type), type);
        }

        var px = new Color32[tw * th];
        int mask = scale - 1;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var tile = body.surface.tiles[x, y];
                int ox = x * scale, oy = y * scale;

                if (tile == null)
                {
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                            px[(oy + sy) * tw + ox + sx] = new Color32(0, 0, 0, 255);
                    continue;
                }

                int ti = (int)tile.type;
                Color c = (uint)ti < (uint)typeCount ? colour[ti]
                                                    : GasGiantPalette.Apply(body, TerrainColorMap.Get(tile.type), tile.type);
                float[] pat = (uint)ti < (uint)typeCount ? pattern[ti] : null;

                // The same per-tile shade jitter BuildGrid uses, so the two views still look like the
                // same world — and so a field of one biome is not a field of one repeated stamp.
                float b = Mathf.Lerp(0.86f, 1.12f, tile.shade);
                float cr = c.r * b * 255f, cg = c.g * b * 255f, cb = c.b * b * 255f;

                // Slide the pattern by a per-tile amount. The art tiles seamlessly, so any offset is a
                // valid window onto it, and without one every cell of a continent is the same stamp and
                // the grid reads as wallpaper. Hashed off the coordinates so it is stable across rebuilds
                // — the map must not shimmer when a terraform redraws it.
                uint hash = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                int px0 = (int)(hash & (uint)mask), py0 = (int)((hash >> 8) & (uint)mask);

                for (int sy = 0; sy < scale; sy++)
                {
                    int row = (oy + sy) * tw + ox;
                    int prow = ((sy + py0) & mask) * scale;
                    for (int sx = 0; sx < scale; sx++)
                    {
                        float m = pat != null ? pat[prow + ((sx + px0) & mask)] : 1f;
                        px[row + sx] = new Color32(
                            (byte)Mathf.Clamp(cr * m, 0f, 255f),
                            (byte)Mathf.Clamp(cg * m, 0f, 255f),
                            (byte)Mathf.Clamp(cb * m, 0f, 255f),
                            255);
                    }
                }
            }

        PaintContours(body, px, w, h, tw, scale);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ============================================================================================
    // CONTOUR LINES — 500 m apart, on the cell edges, both above and below the water
    //
    // "A thin black border separating each elevation band ... giving the planet map a topographic read.
    // This is what replaces biomes-as-elevation."
    //
    // A SEPARATE PASS over the finished texture rather than a test inside the fill loop, for one
    // reason: a contour is a property of the BOUNDARY between two cells, so drawing it needs both of
    // them, and the fill loop only ever has one. Trying to do it inline means either sampling the
    // neighbour's tile again per texel or carrying a row of state, and both are more work than walking
    // the grid a second time over an array that is already hot.
    //
    // ONE TEXEL, on the inside edge of the LOWER cell — not both cells, or every line is two texels
    // wide and the map reads as a mesh laid over the ground rather than as contours on it. Which side
    // owns the line is arbitrary but must be consistent, or a slope drawn east-to-west and the same
    // slope drawn west-to-east would sit half a cell apart.
    //
    // LONGITUDE WRAPS and latitude does not. The map is a cylinder: the cell at x = w-1 borders the one
    // at x = 0, and a contour that stops at the seam is a contour with a visible join in it. The poles
    // are the top and bottom of the texture and border nothing, so the y comparison simply stops.
    //
    // ONLY IN THE TEXTURED PATH. BuildGrid draws one texel per cell and feeds the moon thumbnails, the
    // moon panes and the 3D globes; there is no edge to put a line on at that size, and a black texel
    // per cell boundary would be a black grid over a hundred-pixel picture. The Planet View map is the
    // one the request is about and the only one ever zoomed far enough to read a contour.
    // ============================================================================================

    /// Contour lines are drawn this far toward black. Not pure black: at scale 4 a line is a quarter of
    /// the cell edge, and full black there reads as a heavy grid rather than as a hairline over terrain.
    const float ContourDarken = 0.22f;

    static void PaintContours(CelestialBody body, Color32[] px, int w, int h, int tw, int scale)
    {
        // One band lookup per CELL rather than per comparison — every interior cell is otherwise read
        // twice (once as itself, once as its west/south neighbour), and the band is a divide and a floor.
        var band = new int[w * h];
        float water = body.terrainParams.SeaLevelOrNeutral;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = body.surface.tiles[x, y];
                band[y * w + x] = t == null ? 0
                    : PlanetTerrainGenerator.ContourBand(t.elevation, water);
            }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int here = band[y * w + x];
                int ox = x * scale, oy = y * scale;

                // ---- the eastern edge, wrapping at the seam ----
                int east = band[y * w + (x + 1 == w ? 0 : x + 1)];
                if (east != here)
                {
                    // The lower of the two owns the line, so it lands on one side of the step only.
                    int col = here < east ? ox + scale - 1 : (x + 1 == w ? 0 : ox + scale);
                    if (col < tw)
                        for (int sy = 0; sy < scale; sy++) Darken(px, (oy + sy) * tw + col);
                }

                // ---- the northern edge; the poles have no neighbour ----
                if (y + 1 < h)
                {
                    int north = band[(y + 1) * w + x];
                    if (north != here)
                    {
                        int row = here < north ? oy + scale - 1 : oy + scale;
                        for (int sx = 0; sx < scale; sx++) Darken(px, row * tw + ox + sx);
                    }
                }
            }
    }

    static void Darken(Color32[] px, int i)
    {
        var c = px[i];
        px[i] = new Color32((byte)(c.r * ContourDarken), (byte)(c.g * ContourDarken),
                            (byte)(c.b * ContourDarken), 255);
    }

    /// Texels per cell edge: the largest power of two, capped at the art's own size, whose square times
    /// the cell count still fits the budget.
    static int ScaleFor(int w, int h, int texelBudget)
    {
        long cells = (long)w * h;
        if (cells <= 0) return 1;
        int s = TerrainTextureMap.ArtSize;
        while (s > 1 && cells * s * s > texelBudget) s >>= 1;
        return s;
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
                // Through the palette, like the other two views. Without this the globe was the only
                // place a giant kept the raw tan table — so a blue giant turned tan as the camera pulled
                // back, which is the sort of inconsistency nobody reports because nobody believes it.
                Color c = GasGiantPalette.Apply(body, TerrainColorMap.Get(s.terrain), s.terrain);
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
