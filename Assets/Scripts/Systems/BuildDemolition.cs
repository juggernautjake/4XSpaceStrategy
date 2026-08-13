using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// DEMOLITION MODE — Placement Mode's opposite number
//
// Deliberately the same shape as BuildPlacement, because it is the same interaction run backwards: you
// enter a mode, you paint tiles, a panel appears under what you painted, and nothing happens until you
// confirm. A player who has learned one has learned the other, and the alternative — a Demolish button
// on a list somewhere that tears down a whole twenty-tile farm — is not the same verb at all.
//
// ---- WHAT IS DIFFERENT, and why ----
//
//   NO ADJACENCY RULE. Painting a building needs one because the result has to be a legal shape.
//   Un-painting does not: any set of built tiles is a legal thing to want gone, including four corners
//   of four different buildings at once. Forcing connectivity here would be a rule with nothing behind
//   it.
//
//   NO RESOURCE CAP, obviously — this pays out rather than in.
//
//   ONLY TILES WITH SOMETHING ON THEM. Painting bare ground in demolition mode is not an error worth a
//   red label; it is a miss, and it is silently ignored the way dragging across empty space in any
//   paint tool is.
//
//   A SECOND CONFIRMATION when the removal would SPLIT a building into disconnected pieces. That is the
//   one outcome the player cannot read off the tiles they clicked: take the waist out of an hourglass
//   farm and you have two farms, with two efficiency figures, two entries in every list, and a merge
//   rule that will not put them back together because they no longer touch. See
//   SurfaceBuildManager.WouldSplitInto — the count in the warning is the count you get, because both
//   come from the same flood fill.
// ============================================================================================
public static class BuildDemolition
{
    public static bool Active { get; private set; }
    public static CelestialBody Body { get; private set; }

    static readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();

    public static IReadOnlyCollection<Vector2Int> Cells => cells;
    public static int Tiles => cells.Count;
    public static bool HasCell(Vector2Int c) => cells.Contains(c);

    /// The player has confirmed once and is being asked about a split. Nothing is torn down until this
    /// is confirmed in turn, and cancelling here returns to the painted selection rather than clearing
    /// it — the whole point of the second question is that "no, not like that" is a likely answer, and
    /// it should not cost the work of re-selecting.
    public static bool AwaitingSplitConfirm { get; private set; }

    public static bool IsFor(CelestialBody b) => Active && Body == b;

    public static void Begin(CelestialBody b)
    {
        Active = true;
        Body = b;
        AwaitingSplitConfirm = false;
        cells.Clear();
    }

    public static void Cancel()
    {
        Active = false;
        Body = null;
        AwaitingSplitConfirm = false;
        cells.Clear();
    }

    /// Drop the selection but stay in the mode.
    public static void ClearShape()
    {
        cells.Clear();
        AwaitingSplitConfirm = false;
    }

    // ---- Painting ----

    /// Add one built cell to the selection. Silently ignores bare ground — see the header.
    public static bool Paint(Vector2Int cell)
    {
        if (!Active || Body?.surface == null) return false;
        if (cells.Contains(cell)) return false;
        if (SurfaceBuildManager.At(Body, cell.x, cell.y) == null) return false;

        // A change to what is selected invalidates a split question that was asked about the old
        // selection. Without this, painting one more tile while the second confirm was up would leave
        // the panel asking about a split that the new selection might not even cause.
        AwaitingSplitConfirm = false;

        cells.Add(cell);
        return true;
    }

    /// Take one cell back out of the selection — the eraser, for a drag that went one tile too far.
    public static bool Unpaint(Vector2Int cell)
    {
        if (!Active || !cells.Remove(cell)) return false;
        AwaitingSplitConfirm = false;
        return true;
    }

    /// Select an entire building at once. The common case by far: most demolition is "remove that".
    public static void PaintWhole(PlacedBuilding p)
    {
        if (!Active || p == null) return;
        AwaitingSplitConfirm = false;
        foreach (var c in SurfaceBuildingDatabase.Footprint(p)) cells.Add(c);
    }

    // ---- What would happen ----

    /// Every building the current selection touches.
    public static List<PlacedBuilding> Affected()
    {
        var list = new List<PlacedBuilding>();
        if (!Active || Body == null) return list;

        foreach (var p in SurfaceBuildManager.On(Body))
            foreach (var c in SurfaceBuildingDatabase.Footprint(p))
                if (cells.Contains(c)) { list.Add(p); break; }
        return list;
    }

    /// How many buildings would be left in pieces, and how many pieces in total.
    ///
    /// Counted across every affected building, because one selection can split two of them at once — a
    /// line drawn across a field of farmland is exactly that gesture.
    public static void SplitSummary(out int buildingsSplit, out int extraPieces)
    {
        buildingsSplit = 0;
        extraPieces = 0;
        if (!Active) return;

        foreach (var p in Affected())
        {
            int pieces = SurfaceBuildManager.WouldSplitInto(p, cells);
            if (pieces <= 1) continue;      // 0 = destroyed outright, 1 = still one building
            buildingsSplit++;
            extraPieces += pieces - 1;
        }
    }

    public static bool WouldSplit
    {
        get { SplitSummary(out int n, out _); return n > 0; }
    }

    /// How many buildings the selection would destroy completely.
    public static int WouldDestroy()
    {
        int n = 0;
        foreach (var p in Affected())
            if (SurfaceBuildManager.WouldSplitInto(p, cells) == 0) n++;
        return n;
    }

    public static void Refund(out int metal, out int energy)
        => SurfaceBuildManager.DemolishRefundFor(Body, cells, out metal, out energy);

    public static bool CanConfirm(out string why)
    {
        why = null;
        if (!Active) { why = "not demolishing anything"; return false; }
        if (Body == null || Body.owner != FactionManager.Player) { why = "this world isn't yours"; return false; }
        if (cells.Count == 0) { why = "nothing selected"; return false; }
        return true;
    }

    /// First Confirm. Either does the work, or raises the split question and waits.
    ///
    /// Returns true only when something was actually torn down, so a caller can tell "done" from "asked
    /// you something" without inspecting the state itself.
    public static bool Confirm(out string why)
    {
        if (!CanConfirm(out why)) return false;

        if (!AwaitingSplitConfirm && WouldSplit)
        {
            AwaitingSplitConfirm = true;
            return false;
        }

        return ConfirmSplit(out why);
    }

    /// Second Confirm — or the only one, when there is no split to warn about.
    public static bool ConfirmSplit(out string why)
    {
        if (!CanConfirm(out why)) return false;

        var b = Body;
        int removed = SurfaceBuildManager.DemolishCells(b, new HashSet<Vector2Int>(cells));

        AwaitingSplitConfirm = false;
        cells.Clear();

        if (removed <= 0) { why = "nothing came down"; return false; }
        return true;
    }

    /// Back out of the split question, keeping the selection so it can be adjusted.
    public static void CancelSplitConfirm() => AwaitingSplitConfirm = false;
}
