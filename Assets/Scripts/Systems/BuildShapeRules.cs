using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WHAT SHAPE A BUILDING IS ALLOWED TO BE
//
// Drawing replaced fixed tetrominoes (see BuildScaling), and the first version of it allowed ANY set of
// tiles for ANY building. That is too free in both directions at once: a one-tile farm is not a farm, and
// a spaceport drawn as a diagonal smear is not a spaceport. So a building now declares TWO things — the
// fewest tiles it can work with, and what family of shape it has to be — and this file is the one place
// that decides whether what the player drew satisfies them.
//
// THE FOUR FAMILIES, and why there are four rather than one flexible rule:
//
//   Free        Draw whatever you like, at or above `minTiles`, as one orthogonally-connected piece.
//               This is the resource-generator case and the whole point of the mechanic: your farm is
//               short of food, so you paint four more tiles onto the edge of the farm you already have
//               rather than siting a second farm somewhere else.
//
//   Fixed       The authored footprint, exactly. A spaceport is 3x3 because a landing field is a landing
//               field; there is no such thing as a bigger or smaller one. Drawing is simply not offered.
//
//   Square      Drag a corner and the other corner follows as a SQUARE — 2x2, 3x3, 4x4 — however far out
//               you pull. The reactor case: it may be any size, but it may not be a ribbon.
//
//   Rectangle   A filled rectangle at least 2 tiles wide in BOTH directions. The storage depot case:
//               2x8 and 10x2 are both fine, 1x9 is not, because a one-tile-wide warehouse stretching
//               across a continent looks like a mistake rather than a building.
//
// ORTHOGONAL ADJACENCY IS THE RULE EVERYWHERE. Corner-to-corner does not connect. Two blocks touching
// only at a diagonal are two buildings that happen to be near each other, and treating them as one would
// let a player draw a checkerboard across a whole hemisphere and call it a single farm.
//
// WHAT THIS FILE DOES NOT DO: it never looks at the ground. Whether a tile is water, occupied, in bounds
// or rich enough is SurfaceBuildManager's business (CellBuildable / CanPlaceType). This asks only
// "is that a legal SHAPE for this class of building", so the two concerns can be reported separately —
// "a farm needs at least 4 tiles" and "that tile is under water" are different refusals and a player
// should never have to guess which one they hit.
// ============================================================================================
public enum BuildDrawMode
{
    Free,
    Fixed,
    Square,
    Rectangle,

    /// The power node. One tile per pylon, but a DRAG lays a whole chain of them at once, auto-spaced —
    /// see BuildShapeRules.NodeChain. The shape rules below barely apply; the drag generator is the rule.
    NodeChain,
}

public static class BuildShapeRules
{
    /// The four orthogonal neighbours. Corner-to-corner is deliberately absent — see the header.
    static readonly Vector2Int[] Orthogonal =
    {
        new Vector2Int( 1,  0),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 0, -1),
    };

    /// Is `cells` a legal footprint for this building class? `why` is player-facing on failure.
    public static bool Validate(SurfaceBuildingInfo info, List<Vector2Int> cells, out string why)
    {
        why = null;
        if (info == null) { why = "unknown building"; return false; }
        if (cells == null || cells.Count == 0) { why = "nothing drawn"; return false; }

        // Duplicates first. Every rule below counts tiles, and a drag that visits a cell twice would
        // otherwise satisfy a minimum it has not actually met.
        var unique = new HashSet<Vector2Int>(cells);
        if (unique.Count != cells.Count) { why = "the same tile was drawn twice"; return false; }

        int min = Mathf.Max(1, info.minTiles);
        if (unique.Count < min)
        {
            why = $"{info.name} needs at least {min} tiles — {unique.Count} drawn";
            return false;
        }

        if (!IsConnected(unique))
        {
            why = $"{info.name} must be one connected block — tiles that meet only at a corner are not joined";
            return false;
        }

        switch (info.drawMode)
        {
            case BuildDrawMode.Fixed:
                // The authored footprint is the only legal one. Compared by COUNT rather than by exact
                // cells because the placement path offers no way to draw a Fixed building in the first
                // place — this is the backstop for a caller that built the list some other way.
                int want = info.shape != null ? info.shape.Length : min;
                if (unique.Count != want)
                {
                    why = $"{info.name} is a fixed {want}-tile structure and cannot be resized";
                    return false;
                }
                return true;

            case BuildDrawMode.Square:
                if (!IsFilledBox(unique, out int sw, out int sh))
                {
                    why = $"{info.name} must be drawn as a solid square — drag a corner out";
                    return false;
                }
                if (sw != sh)
                {
                    why = $"{info.name} must be square — {sw}x{sh} drawn";
                    return false;
                }
                return true;

            case BuildDrawMode.Rectangle:
                if (!IsFilledBox(unique, out int rw, out int rh))
                {
                    why = $"{info.name} must be drawn as a solid rectangle";
                    return false;
                }
                // The reason this mode exists at all: no 1-wide ribbons.
                if (rw < 2 || rh < 2)
                {
                    why = $"{info.name} must be at least 2 tiles wide in both directions — {rw}x{rh} drawn";
                    return false;
                }
                return true;

            // Free and NodeChain: the minimum and the connectivity check above are the whole rule.
            default:
                return true;
        }
    }

    /// Are all these cells reachable from one another moving only up/down/left/right?
    ///
    /// A flood fill from an arbitrary starting cell: if it reaches every cell, the set is one piece.
    public static bool IsConnected(HashSet<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0) return false;
        if (cells.Count == 1) return true;

        Vector2Int start = default;
        foreach (var c in cells) { start = c; break; }

        var seen = new HashSet<Vector2Int> { start };
        var stack = new Stack<Vector2Int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            for (int i = 0; i < Orthogonal.Length; i++)
            {
                var n = cur + Orthogonal[i];
                if (cells.Contains(n) && seen.Add(n)) stack.Push(n);
            }
        }

        return seen.Count == cells.Count;
    }

    /// Is this set exactly a solid, gap-free rectangle? Reports its dimensions either way.
    ///
    /// Checked by AREA rather than by walking the box: a set whose bounding box is w*h and which holds
    /// w*h distinct cells cannot have a hole, because a hole would need a cell outside the box to make up
    /// the count, and there are none by construction.
    public static bool IsFilledBox(HashSet<Vector2Int> cells, out int w, out int h)
    {
        w = h = 0;
        if (cells == null || cells.Count == 0) return false;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in cells)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.y > maxY) maxY = c.y;
        }

        w = maxX - minX + 1;
        h = maxY - minY + 1;
        return (long)w * h == cells.Count;
    }

    // ============================================================================================
    // THE SQUARE DRAG
    //
    // Anchor at the first cell clicked, and the square grows toward the cursor however far it is dragged.
    // The side is the LARGER of the two spans, so pulling mostly sideways still gives a square rather
    // than snapping back to whichever axis happens to be shorter — dragging out and getting less than you
    // reached for is the frustrating version of this control.
    // ============================================================================================
    public static List<Vector2Int> SquareFrom(Vector2Int anchor, Vector2Int cursor, int minSide)
    {
        int dx = cursor.x - anchor.x, dy = cursor.y - anchor.y;
        int side = Mathf.Max(minSide, Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) + 1);

        // Grow AWAY from the anchor, in whichever direction the drag went, so the anchor stays the corner
        // the player put down rather than sliding out from under the cursor.
        int sx = dx < 0 ? -1 : 1;
        int sy = dy < 0 ? -1 : 1;

        var outCells = new List<Vector2Int>(side * side);
        for (int i = 0; i < side; i++)
            for (int j = 0; j < side; j++)
                outCells.Add(new Vector2Int(anchor.x + sx * i, anchor.y + sy * j));
        return outCells;
    }

    /// The rectangle drag: a filled box between two corners, floored to `minW` x `minH` in each axis.
    public static List<Vector2Int> RectFrom(Vector2Int anchor, Vector2Int cursor, int minW, int minH)
    {
        int dx = cursor.x - anchor.x, dy = cursor.y - anchor.y;
        int w = Mathf.Max(minW, Mathf.Abs(dx) + 1);
        int h = Mathf.Max(minH, Mathf.Abs(dy) + 1);

        int sx = dx < 0 ? -1 : 1;
        int sy = dy < 0 ? -1 : 1;

        var outCells = new List<Vector2Int>(w * h);
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
                outCells.Add(new Vector2Int(anchor.x + sx * i, anchor.y + sy * j));
        return outCells;
    }

    // ============================================================================================
    // THE POWER NODE CHAIN
    //
    // Drag from a node and it lays a LINE of pylons toward the cursor, spaced as far apart as they can be
    // while still lighting overlapping ground — the factory-game power-pole control. This is what makes
    // reaching across a continent one gesture instead of forty clicks at a spacing the player has to
    // work out by eye.
    //
    // THE SPACING IS THE INTERESTING NUMBER. Two projectors are on the same grid when their lit ground
    // OVERLAPS (PowerGrid), and a node lights `powerRange` tiles in every direction, so two nodes stay
    // joined while they are at most 2 * powerRange apart. We step at slightly under that — see
    // ChainSafety — because the span rarely divides evenly and a chain that is one tile too long
    // somewhere in the middle is two grids, silently, which is exactly the failure the player cannot see.
    //
    // The step is measured along the DOMINANT axis and the line is walked with the same integer
    // interpolation a Bresenham run would use, so a diagonal drag lays a diagonal chain rather than an
    // L-shaped one.
    // ============================================================================================

    /// How much of the theoretical maximum reach a chain actually uses. Under 1 on purpose.
    public const float ChainSafety = 0.9f;

    /// Lay a chain of node positions from `anchor` toward `cursor`, spaced to stay on one grid.
    ///
    /// Always includes the anchor, so a click with no drag is simply a single pylon.
    public static List<Vector2Int> NodeChain(Vector2Int anchor, Vector2Int cursor, float powerRange)
    {
        var outCells = new List<Vector2Int> { anchor };

        int dx = cursor.x - anchor.x, dy = cursor.y - anchor.y;
        int span = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (span <= 0) return outCells;

        // Two nodes hold together while their lit discs overlap: at most 2*range apart, backed off by
        // ChainSafety. Floored at 1 so a zero-range node still advances and this cannot spin.
        int step = Mathf.Max(1, Mathf.FloorToInt(2f * Mathf.Max(0.5f, powerRange) * ChainSafety));

        for (int d = step; d <= span; d += step)
        {
            float t = d / (float)span;
            outCells.Add(new Vector2Int(
                anchor.x + Mathf.RoundToInt(dx * t),
                anchor.y + Mathf.RoundToInt(dy * t)));
        }

        // The far end always gets a pylon, even when the span does not divide evenly by the step —
        // otherwise a drag ending just past the last node stops short of where the player pointed, and
        // the one thing this control promises is reach to where you dragged.
        var last = new Vector2Int(cursor.x, cursor.y);
        if (outCells[outCells.Count - 1] != last) outCells.Add(last);

        return outCells;
    }
}
