using System.Collections.Generic;
using UnityEngine;

// 16x16 pixel-art icons drawn from character grids at runtime (2026-10-09) — for the Surface View's
// shortcut column. Built in code rather than imported as PNGs because a new image needs import settings
// (point filtering, no compression) that only a machine with Unity can write into its .meta; a texture
// made here is point-filtered by construction and needs nothing from the asset pipeline.
//
// Grids are 16 strings of 16 characters, top row first: '.' transparent, '#' white, 'g' grey,
// 'd' dark grey. Icons that change colour with state (the bulldozer) are drawn white and tinted by the
// RawImage, so white pixels take the tint and grey ones stay readable.
public static class PixelIcons
{
    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    /// A bulldozer from the side, blade to the left.
    public static Texture2D Bulldozer => Get("bulldozer", new[]
    {
        "................",
        "................",
        "........#####...",
        "........#...#...",
        "........#...#...",
        "..##....#####...",
        "..#.##########..",
        ".##.##########..",
        ".##.##########..",
        "##..##########..",
        "##..............",
        "##..##########..",
        "...#..........#.",
        "...#.##.##.##.#.",
        "...#..........#.",
        "....##########..",
    });

    /// A lightning bolt, the Z of the power grid.
    public static Texture2D Bolt => Get("bolt", new[]
    {
        "................",
        ".........#####..",
        "........#####...",
        ".......#####....",
        "......#####.....",
        ".....#####......",
        "....##########..",
        "...##########...",
        "........####....",
        ".......####.....",
        "......####......",
        ".....###........",
        "....###.........",
        "...##...........",
        "..#.............",
        "................",
    });

    /// A white line that runs flat, rises into an arch and returns to its starting level, over a darker
    /// flat line marking where the ground would have been, with a grey arrow under the arch pointing up:
    /// "this is how the ground rises" — the elevation contours.
    public static Texture2D Heightmap => Get("heightmap", new[]
    {
        "................",
        "................",
        "................",
        "......####......",
        ".....#....#.....",
        "....#..g...#....",
        "....#.ggg..#....",
        "...#.g.g.g..#...",
        "...#...g....#...",
        "..#....g.....#..",
        "..#....g.....#..",
        ".#.....g......#.",
        "###dddddddddd###",
        "................",
        "................",
        "................",
    });

    static Texture2D Get(string key, string[] rows)
    {
        if (cache.TryGetValue(key, out var t) && t != null) return t;
        t = new Texture2D(16, 16, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "PixelIcon_" + key
        };
        var px = new Color32[256];
        for (int y = 0; y < 16; y++)
        {
            string row = y < rows.Length ? rows[y] : "";
            for (int x = 0; x < 16; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                // Texture rows run bottom-up; the grids are written top-down.
                px[(15 - y) * 16 + x] = c == '#' ? new Color32(255, 255, 255, 255)
                                      : c == 'g' ? new Color32(150, 156, 166, 255)
                                      : c == 'd' ? new Color32(88, 94, 104, 255)
                                      : new Color32(0, 0, 0, 0);
            }
        }
        t.SetPixels32(px);
        t.Apply(false, true);
        cache[key] = t;
        return t;
    }
}
