using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// BUILDING ART — preliminary pixel art for every surface structure (2026-10-09)
//
// One 16x16 tile per grid cell, drawn in code so no image import settings are needed (see PixelIcons).
// How a footprint is dressed depends on how the class is drawn:
//
//   FREE / node / grown  — every cell is its own small building, picked at random from TWO VARIANTS by
//                          a hash of the cell (stable across redraws and reloads, so a placed farm does
//                          not reshuffle every time the map repaints).
//   FIXED                — one big SITE image the size of the footprint's bounding box (32x32 for a
//                          2x2, 48x48 for a 3x3), each cell showing its slice of it.
//   SQUARE               — a 32x32 site tiled in 2x2 blocks, so a 4x4 fusion plant is four of them.
//   RECTANGLE            — a 48x48 NINE-SLICE: corners, edges and a middle, chosen per cell from which
//                          neighbours are part of the building, so a storage depot of any length reads as
//                          one long building with walls round the outside.
//
// "We can work out the details of buildings later": every drawing here is a first pass, kept in one
// place so it can be redrawn without touching anything that uses it.
// ============================================================================================
public static class BuildingArt
{
    // ---- Public API ----------------------------------------------------------------------------

    /// The art shown on a build-menu card: the structure at the smallest size it can be placed at.
    public static Texture2D CardArt(SurfaceBuildingType t)
    {
        var info = SurfaceBuildingDatabase.Get(t);
        var mode = info != null ? info.drawMode : BuildDrawMode.Free;
        switch (mode)
        {
            case BuildDrawMode.Fixed:
            case BuildDrawMode.Square: return Site(t);
            case BuildDrawMode.Rectangle: return Get(t, "card", () => SliceCorners(t));
            default: return Tile(t, 0);
        }
    }

    /// The texture and its sub-rectangle for one cell of a footprint.
    public static Texture2D CellArt(SurfaceBuildingType t, ICollection<Vector2Int> cells, Vector2Int cell, out Rect uv)
    {
        var info = SurfaceBuildingDatabase.Get(t);
        var mode = info != null ? info.drawMode : BuildDrawMode.Free;
        uv = new Rect(0, 0, 1, 1);
        if (cells == null || cells.Count == 0) return Tile(t, Variant(cell));
        // A free-drawn cell needs nothing from the rest of the footprint — answered before the bounding
        // box is measured, so a 300-tile farm is not 300 x 300 work per redraw.
        if (mode != BuildDrawMode.Fixed && mode != BuildDrawMode.Square && mode != BuildDrawMode.Rectangle)
            return Tile(t, Variant(cell));

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        if (mode != BuildDrawMode.Rectangle)
            foreach (var c in cells)
            {
                if (c.x < minX) minX = c.x; if (c.y < minY) minY = c.y;
                if (c.x > maxX) maxX = c.x; if (c.y > maxY) maxY = c.y;
            }

        switch (mode)
        {
            case BuildDrawMode.Fixed:
            {
                // Wrapped footprints (across the date line) would give a huge bounding box; fall back to tiles.
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (bw > 4 || bh > 4) return Tile(t, Variant(cell));
                var tex = Site(t);
                int sw = Mathf.Max(1, tex.width / 16), sh = Mathf.Max(1, tex.height / 16);
                int cx = Mathf.Clamp(cell.x - minX, 0, sw - 1), cy = Mathf.Clamp(cell.y - minY, 0, sh - 1);
                uv = new Rect(cx / (float)sw, cy / (float)sh, 1f / sw, 1f / sh);
                return tex;
            }
            case BuildDrawMode.Square:
            {
                var tex = Site(t);
                int qx = Mod(cell.x - minX, 2), qy = Mod(cell.y - minY, 2);
                uv = new Rect(qx * 0.5f, qy * 0.5f, 0.5f, 0.5f);
                return tex;
            }
            case BuildDrawMode.Rectangle:
            {
                // Callers pass a HashSet for rectangles (PlanetViewWindow.AddBuildingArt) so this is O(1).
                var set = cells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(cells);
                bool l = set.Contains(new Vector2Int(cell.x - 1, cell.y)), r = set.Contains(new Vector2Int(cell.x + 1, cell.y));
                bool d = set.Contains(new Vector2Int(cell.x, cell.y - 1)), u = set.Contains(new Vector2Int(cell.x, cell.y + 1));
                int col = !l ? 0 : !r ? 2 : 1;
                int row = !d ? 0 : !u ? 2 : 1;       // texture rows run bottom-up, like the map's y
                uv = new Rect(col / 3f, row / 3f, 1f / 3f, 1f / 3f);
                return Slice(t);
            }
            default:
                return Tile(t, Variant(cell));
        }
    }

    /// A 16x16 single-cell tile, variant 0 or 1.
    public static Texture2D Tile(SurfaceBuildingType t, int variant)
    {
        variant &= 1;
        return Get(t, "tile" + variant, () =>
        {
            var c = new Px(16, 16);
            DrawTile(t, c, variant);
            return c;
        });
    }

    /// The big site image for a fixed or square class.
    public static Texture2D Site(SurfaceBuildingType t)
        => Get(t, "site", () =>
        {
            int n = t == SurfaceBuildingType.SurfaceShipyard ? 48 : 32;
            var c = new Px(n, n);
            DrawSite(t, c);
            return c;
        });

    /// The 48x48 nine-slice for a rectangle class.
    public static Texture2D Slice(SurfaceBuildingType t)
        => Get(t, "slice", () => { var c = new Px(48, 48); DrawSlice(t, c); return c; });

    /// Which of the two variants a cell gets: a stable hash of where it is.
    public static int Variant(Vector2Int cell)
    {
        unchecked
        {
            uint h = (uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (int)(h & 1);
        }
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    // ---- Cache -----------------------------------------------------------------------------------

    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    static Texture2D Get(SurfaceBuildingType t, string kind, System.Func<Px> draw)
    {
        string key = (int)t + ":" + kind;
        if (cache.TryGetValue(key, out var tex) && tex != null) return tex;
        tex = draw().Tex("BuildingArt_" + t + "_" + kind);
        cache[key] = tex;
        return tex;
    }

    // ---- Palette ---------------------------------------------------------------------------------

    static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);
    static readonly Color32 Ground = C(58, 62, 68), GroundDark = C(42, 45, 50), Dark = C(22, 24, 28);
    static readonly Color32 Grey = C(124, 130, 138), LightGrey = C(178, 184, 192), White = C(232, 236, 240);
    static readonly Color32 Concrete = C(150, 148, 138), Steel = C(92, 120, 156);
    static readonly Color32 Brown = C(110, 78, 48), DarkBrown = C(74, 52, 32), Dirt = C(98, 74, 50);
    static readonly Color32 Green = C(72, 146, 58), DarkGreen = C(44, 104, 40), Wheat = C(196, 170, 72), DarkWheat = C(150, 124, 50);
    static readonly Color32 Panel = C(40, 78, 158), PanelLight = C(112, 168, 228), Water = C(42, 112, 198), WaterLight = C(110, 170, 230);
    static readonly Color32 Red = C(196, 62, 50), Orange = C(230, 132, 42), Yellow = C(240, 218, 90), Gold = C(220, 186, 88);
    static readonly Color32 Brick = C(140, 82, 62), Glass = C(118, 182, 204), Cream = C(204, 194, 170);

    // ---- Single-cell tiles -------------------------------------------------------------------------

    static void DrawTile(SurfaceBuildingType t, Px c, int v)
    {
        switch (t)
        {
            case SurfaceBuildingType.Mine:
                c.Fill(Dirt);
                int o = v == 0 ? 0 : -2;
                c.Line(4 + o, 13, 8 + o, 2, LightGrey); c.Line(12 + o, 13, 8 + o, 2, LightGrey);
                c.Line(5 + o, 10, 11 + o, 10, Grey); c.Line(6 + o, 7, 10 + o, 7, Grey);
                c.VLine(8 + o, 2, 15, Dark);
                c.Rect(2 + o, 13, 12, 2, Steel);
                if (v == 0) { c.Set(2, 5, Orange); c.Set(3, 5, Orange); c.Set(13, 9, Orange); }
                else { c.Rect(11, 10, 4, 2, Grey); c.Set(11, 12, Dark); c.Set(14, 12, Dark); c.Set(12, 9, Orange); c.Set(13, 9, Orange); }
                break;

            case SurfaceBuildingType.Farm:
                if (v == 0) { c.Fill(Green); for (int y = 1; y < 16; y += 3) c.HLine(0, 15, y, DarkGreen); }
                else { c.Fill(Wheat); for (int x = 1; x < 16; x += 3) c.VLine(x, 0, 15, DarkWheat); }
                break;

            case SurfaceBuildingType.Habitat:
            case SurfaceBuildingType.Settlement:
            case SurfaceBuildingType.Town:
                c.Fill(Ground);
                if (v == 0)
                {
                    c.Rect(3, 3, 10, 11, Cream); c.Rect(3, 3, 10, 2, Red);
                    Windows(c, 5, 6, 2, 2, 4, 4);
                }
                else
                {
                    House(c, 1, 7, 6, 7); House(c, 9, 5, 6, 9);
                }
                break;

            case SurfaceBuildingType.City:
                c.Fill(GroundDark);
                if (v == 0) { c.Rect(2, 2, 5, 13, LightGrey); c.Rect(9, 5, 5, 10, Grey); Windows(c, 3, 4, 2, 6, 2, 2); Windows(c, 10, 7, 2, 4, 2, 2); }
                else { c.Rect(3, 4, 10, 11, Grey); c.Rect(6, 1, 4, 3, LightGrey); Windows(c, 4, 6, 4, 4, 2, 2); }
                break;

            case SurfaceBuildingType.SolarArray:
                c.Fill(GroundDark);
                if (v == 0)
                {
                    foreach (var p in new[] { (1, 1), (9, 1), (1, 9), (9, 9) })
                    { c.Rect(p.Item1, p.Item2, 6, 6, Panel); c.HLine(p.Item1, p.Item1 + 5, p.Item2 + 3, PanelLight); c.VLine(p.Item1 + 3, p.Item2, p.Item2 + 5, PanelLight); }
                }
                else
                {
                    for (int y = 1; y < 16; y += 5) { c.Rect(1, y, 14, 4, Panel); for (int x = 4; x < 15; x += 4) c.VLine(x, y, y + 3, PanelLight); }
                }
                break;

            case SurfaceBuildingType.WindFarm:
                c.Fill(C(78, 108, 70));
                c.VLine(8, 7, 15, LightGrey);
                if (v == 0) { c.Line(8, 6, 8, 0, White); c.Line(8, 6, 2, 10, White); c.Line(8, 6, 14, 10, White); }
                else { c.Line(8, 6, 14, 3, White); c.Line(8, 6, 3, 1, White); c.Line(8, 6, 6, 12, White); }
                c.Disc(8, 6, 1, LightGrey);
                break;

            case SurfaceBuildingType.Factory:
                c.Fill(Ground);
                c.Rect(1, 6, 14, 9, Grey);
                for (int x = 1; x < 15; x++) c.VLine(x, 6 - (x % 4), 6, LightGrey);
                int sx = v == 0 ? 11 : 3;
                c.Rect(sx, 1, 2, 6, Brick); c.Disc(sx + 1, 0, 1, LightGrey);
                c.Rect(4, 11, 3, 4, Dark); c.Rect(9, 11, 3, 4, Dark);
                break;

            case SurfaceBuildingType.ResearchOutpost:
                c.Fill(Ground);
                if (v == 0) { Dome(c, 7, 11, 5, White); c.VLine(13, 2, 8, LightGrey); c.HLine(11, 15, 2, LightGrey); }
                else { Dome(c, 4, 12, 3, White); Dome(c, 11, 11, 4, White); c.Set(11, 6, Red); }
                c.HLine(1, 14, 13, Grey);
                break;

            case SurfaceBuildingType.ResearchCenter:
                c.Fill(Ground);
                if (v == 0) { c.Rect(2, 3, 12, 11, Glass); for (int x = 5; x < 14; x += 3) c.VLine(x, 3, 13, Steel); c.HLine(2, 13, 8, Steel); }
                else { c.Rect(2, 2, 6, 12, Glass); c.Rect(8, 8, 6, 6, Glass); c.HLine(2, 7, 7, Steel); c.VLine(11, 8, 13, Steel); }
                break;

            case SurfaceBuildingType.Refinery:
                c.Fill(Ground);
                if (v == 0) { Tank(c, 5, 6, 3); Tank(c, 11, 10, 3); c.HLine(5, 11, 8, Orange); }
                else { c.Rect(3, 2, 3, 12, LightGrey); c.HLine(3, 5, 5, Grey); c.HLine(3, 5, 9, Grey); Tank(c, 11, 9, 4); c.HLine(5, 8, 11, Orange); }
                break;

            case SurfaceBuildingType.PowerDistribution:
                c.Fill(Ground);
                c.HLine(0, 15, 2, Yellow);
                if (v == 0) { c.Rect(2, 5, 4, 6, Grey); c.Rect(10, 5, 4, 6, Grey); c.VLine(4, 2, 5, LightGrey); c.VLine(12, 2, 5, LightGrey); }
                else { c.Rect(3, 5, 10, 8, Grey); Bolt(c, 6, 6); }
                c.HLine(0, 15, 14, Grey);
                break;

            case SurfaceBuildingType.HydroPlant:
                if (v == 0) { c.Rect(0, 0, 16, 8, Water); c.HLine(0, 15, 3, WaterLight); c.Rect(0, 8, 16, 3, Concrete); c.Rect(4, 11, 8, 5, Grey); }
                else { c.Rect(0, 0, 8, 16, Water); c.VLine(3, 0, 15, WaterLight); c.Rect(8, 0, 3, 16, Concrete); c.Rect(11, 4, 5, 8, Grey); }
                break;

            case SurfaceBuildingType.Capacitor:
                c.Fill(Ground);
                if (v == 0) { Cell(c, 2, 3, 4, 10); Cell(c, 10, 3, 4, 10); }
                else { Cell(c, 1, 4, 3, 9); Cell(c, 6, 4, 3, 9); Cell(c, 11, 4, 3, 9); }
                break;

            case SurfaceBuildingType.CombustionPlant:
                c.Fill(Ground);
                int stack = v == 0 ? 11 : 2;
                c.Rect(v == 0 ? 2 : 6, 8, 9, 7, Brick);
                c.Rect(stack, 2, 3, 13, Grey); c.HLine(stack, stack + 2, 4, Red);
                c.Disc(stack + 1, 1, 2, LightGrey);
                break;

            case SurfaceBuildingType.SteamTurbine:
                c.Fill(Ground);
                c.Rect(1, 7, 14, 8, LightGrey); c.HLine(1, 14, 7, Grey);
                if (v == 0) { c.Disc(5, 4, 2, White); c.Disc(9, 3, 2, White); }
                else { c.Rect(3, 10, 10, 3, Steel); c.Disc(12, 4, 2, White); }
                break;

            case SurfaceBuildingType.FissionReactor:
                c.Fill(Ground);
                if (v == 0)
                {
                    for (int y = 4; y < 16; y++) { int w = 6 + Mathf.Abs(y - 10) / 2; c.HLine(8 - w / 2, 8 + w / 2, y, LightGrey); }
                    c.Disc(8, 2, 2, White);
                }
                else { Dome(c, 6, 13, 5, LightGrey); c.Rect(12, 5, 3, 10, Grey); c.Disc(13, 3, 2, White); }
                break;

            case SurfaceBuildingType.PowerNode:
                c.Fill(Ground);
                int px = v == 0 ? 8 : 7;
                c.Line(px - 4, 15, px, 2, LightGrey); c.Line(px + 4, 15, px, 2, LightGrey);
                c.HLine(px - 5, px + 5, 4, LightGrey); c.HLine(px - 3, px + 3, 9, Grey);
                c.Set(px - 5, 5, Yellow); c.Set(px + 5, 5, Yellow);
                break;

            default:
                // A class with no drawing yet: a plain block in its own colour, so it is still a building.
                var info = SurfaceBuildingDatabase.Get(t);
                Color32 col = info != null ? (Color32)info.color : (Color32)Grey;
                c.Fill(Ground); c.Rect(2, 2, 12, 12, col); c.Frame(2, 2, 12, 12, Dark);
                if (v == 1) c.Rect(6, 6, 4, 4, Dark);
                break;
        }
    }

    // ---- Multi-cell sites --------------------------------------------------------------------------

    static void DrawSite(SurfaceBuildingType t, Px c)
    {
        switch (t)
        {
            case SurfaceBuildingType.PlanetCapitol:
                c.Fill(Concrete);
                c.Rect(4, 15, 24, 14, Cream);
                for (int x = 6; x < 27; x += 3) c.VLine(x, 18, 28, Grey);
                c.HLine(4, 27, 15, Gold);
                Dome(c, 16, 15, 7, Gold);
                c.VLine(16, 2, 8, Dark); c.Rect(17, 2, 5, 3, Red);
                c.Rect(13, 24, 6, 5, DarkBrown);
                break;

            case SurfaceBuildingType.Spaceport:
                c.Fill(GroundDark);
                c.Disc(14, 17, 12, Grey); c.Ring(14, 17, 12, LightGrey); c.Ring(14, 17, 9, Yellow);
                c.VLine(10, 12, 22, White); c.VLine(18, 12, 22, White); c.HLine(10, 18, 17, White);
                c.Rect(27, 2, 4, 12, LightGrey); c.Rect(26, 2, 6, 2, Red);
                break;

            case SurfaceBuildingType.ColonyShipBase:
                c.Fill(Dirt);
                c.Rect(10, 8, 12, 18, LightGrey);
                for (int y = 2; y < 8; y++) c.HLine(16 - (y - 2), 15 + (y - 2), y, LightGrey);
                c.Rect(13, 12, 6, 4, Glass);
                c.Line(10, 26, 6, 30, Grey); c.Line(21, 26, 25, 30, Grey);
                c.Rect(12, 26, 8, 3, Orange);
                break;

            case SurfaceBuildingType.GeothermalPlant:
                c.Fill(C(70, 52, 46));
                c.Disc(7, 23, 4, Orange); c.Disc(7, 23, 2, Yellow);
                c.Disc(23, 26, 3, Orange); c.Disc(23, 26, 1, Yellow);
                c.Rect(14, 9, 14, 12, Grey); c.HLine(14, 27, 9, LightGrey);
                c.Line(7, 19, 14, 15, Steel); c.Line(23, 23, 22, 21, Steel);
                c.Disc(6, 7, 3, White); c.Disc(10, 4, 3, White); c.Disc(22, 4, 2, White);
                break;

            case SurfaceBuildingType.FusionReactor:
                c.Fill(GroundDark);
                c.Disc(16, 16, 12, Steel); c.Disc(16, 16, 7, GroundDark);
                c.Ring(16, 16, 12, LightGrey); c.Ring(16, 16, 7, LightGrey);
                c.Disc(16, 16, 4, C(120, 220, 255)); c.Disc(16, 16, 2, White);
                c.Rect(1, 1, 5, 5, Grey); c.Rect(26, 26, 5, 5, Grey);
                break;

            case SurfaceBuildingType.SurfaceShipyard:
                c.Fill(Ground);
                c.Rect(4, 6, 40, 36, Concrete);
                c.Rect(12, 14, 24, 20, LightGrey);
                for (int y = 34; y < 40; y++) c.HLine(18 + (y - 34), 29 - (y - 34), y, LightGrey);
                c.VLine(6, 2, 44, Yellow); c.VLine(41, 2, 44, Yellow); c.HLine(6, 41, 4, Yellow);
                c.Rect(20, 4, 6, 3, Orange);
                break;

            default:
            {
                var info = SurfaceBuildingDatabase.Get(t);
                Color32 col = info != null ? (Color32)info.color : (Color32)Grey;
                c.Fill(Ground); c.Rect(3, 3, c.W - 6, c.H - 6, col); c.Frame(3, 3, c.W - 6, c.H - 6, Dark);
                break;
            }
        }
    }

    // ---- Nine-slice ----------------------------------------------------------------------------------

    static void DrawSlice(SurfaceBuildingType t, Px c)
    {
        // A long warehouse: a ridged roof with a wall round the outside and a loading door on the
        // south side. Drawn as one 48x48 building so the slices line up wherever they are cut.
        Color32 roof = C(150, 140, 110), ridge = C(120, 110, 84), wall = C(96, 88, 66);
        c.Fill(roof);
        for (int y = 5; y < 44; y += 4) c.HLine(3, 44, y, ridge);
        c.Rect(0, 0, 48, 3, wall); c.Rect(0, 45, 48, 3, wall);
        c.Rect(0, 0, 3, 48, wall); c.Rect(45, 0, 3, 48, wall);
        c.Rect(20, 41, 8, 7, Dark); c.HLine(20, 27, 43, Grey);
    }

    /// The 2x2 at the smallest a rectangle class can be: the slice's four corners put together.
    static Px SliceCorners(SurfaceBuildingType t)
    {
        var src = new Px(48, 48);
        DrawSlice(t, src);
        var dst = new Px(32, 32);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                dst.Set(x, y, src.Get(x, y));               // top-left
                dst.Set(x + 16, y, src.Get(x + 32, y));     // top-right
                dst.Set(x, y + 16, src.Get(x, y + 32));     // bottom-left
                dst.Set(x + 16, y + 16, src.Get(x + 32, y + 32));
            }
        return dst;
    }

    // ---- Little helpers ------------------------------------------------------------------------------

    static void Windows(Px c, int x0, int y0, int w, int h, int stepX, int stepY)
    {
        for (int y = y0; y < y0 + h * stepY; y += stepY)
            for (int x = x0; x < x0 + w * stepX; x += stepX)
                c.Rect(x, y, 2, 2, C(40, 60, 96));
    }

    static void House(Px c, int x, int y, int w, int h)
    {
        c.Rect(x, y + 2, w, h - 2, Cream);
        for (int i = 0; i < 3; i++) c.HLine(x + i, x + w - 1 - i, y + 2 - i, Red);
        c.Rect(x + w / 2 - 1, y + h - 3, 2, 3, DarkBrown);
    }

    static void Dome(Px c, int cx, int baseY, int r, Color32 col)
    {
        for (int y = baseY - r; y <= baseY; y++)
            for (int x = cx - r; x <= cx + r; x++)
                if ((x - cx) * (x - cx) + (y - baseY) * (y - baseY) <= r * r + r) c.Set(x, y, col);
    }

    static void Tank(Px c, int cx, int cy, int r)
    {
        c.Disc(cx, cy, r, LightGrey);
        c.Ring(cx, cy, r, Dark);
    }

    static void Cell(Px c, int x, int y, int w, int h)
    {
        c.Rect(x, y, w, h, Steel); c.Rect(x, y, w, 2, PanelLight); c.Frame(x, y, w, h, Dark);
    }

    static void Bolt(Px c, int x, int y)
    {
        c.Line(x + 3, y, x, y + 3, Yellow); c.HLine(x, x + 3, y + 3, Yellow); c.Line(x + 3, y + 3, x, y + 6, Yellow);
    }

    // ---- The canvas ----------------------------------------------------------------------------------

    /// A tiny pixel canvas with y running DOWN from the top, as the drawings are written; flipped to
    /// Unity's bottom-up rows on the way into a texture.
    sealed class Px
    {
        public readonly int W, H;
        readonly Color32[] p;
        public Px(int w, int h) { W = w; H = h; p = new Color32[w * h]; }

        public void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < W && y < H) p[(H - 1 - y) * W + x] = c; }
        public Color32 Get(int x, int y) => (x >= 0 && y >= 0 && x < W && y < H) ? p[(H - 1 - y) * W + x] : default;
        public void Fill(Color32 c) { for (int i = 0; i < p.Length; i++) p[i] = c; }
        public void Rect(int x, int y, int w, int h, Color32 c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Set(i, j, c); }
        public void Frame(int x, int y, int w, int h, Color32 c) { HLine(x, x + w - 1, y, c); HLine(x, x + w - 1, y + h - 1, c); VLine(x, y, y + h - 1, c); VLine(x + w - 1, y, y + h - 1, c); }
        public void HLine(int x0, int x1, int y, Color32 c) { for (int x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x++) Set(x, y, c); }
        public void VLine(int x, int y0, int y1, Color32 c) { for (int y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y++) Set(x, y, c); }

        public void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 512; guard++)
            {
                Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public void Disc(int cx, int cy, int r, Color32 c)
        {
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r + r) Set(x, y, c);
        }

        public void Ring(int cx, int cy, int r, Color32 c)
        {
            int outer = r * r + r, inner = (r - 1) * (r - 1) + (r - 1);
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    int d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d <= outer && d > inner) Set(x, y, c);
                }
        }

        public Texture2D Tex(string name)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            t.SetPixels32(p);
            t.Apply(false, true);
            return t;
        }
    }
}
