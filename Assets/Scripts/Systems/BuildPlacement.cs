using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// PLACEMENT MODE — the session that exists between picking a building and confirming it
//
// Drawing used to be a gesture with no state behind it: press, paint, release, and the release WAS the
// build. Everything wrong with that follows from the missing middle. You could paint across the whole
// map because nothing was counting; you could paint a checkerboard because nothing was checking what
// touched what; you found out it was unaffordable at the moment it silently refused; and you could not
// change your mind, because letting go of the button was the commitment.
//
// So there is now a SESSION. Picking a structure opens one, painting edits it, and it ends only when
// the player confirms or cancels. This file is that session and every rule it enforces; PlanetViewWindow
// draws it and feeds it the mouse. Keeping the two apart is what makes the rules testable at all — none
// of what follows needs a canvas, a camera or a frame.
//
// ---- THE FIVE RULES, and why each is here rather than in the UI ----
//
//   AFFORDABILITY   You cannot paint a tile you cannot pay for. Not "paint it red and refuse later" —
//                   the tile does not go down, and the session says what you are short. A cap that only
//                   appears at the end is a cap you discover by wasting a gesture.
//
//   CONNECTIVITY    After the first tile, only the four orthogonal neighbours of what is already painted
//                   may be painted. Corner-to-corner is not adjacency (BuildShapeRules has held this
//                   line for the shape validator; now the BRUSH holds it too, so an illegal shape is
//                   unpaintable rather than merely rejected).
//
//   GROUND          Water, another building, a queued site: refused per tile, at the tile.
//
//   MINIMUM         A class has a smallest size at which it is recognisably itself. The session tracks
//                   progress toward it and Confirm refuses below it.
//
//   CONFIRMATION    Nothing is spent until Confirm. Cancel costs nothing and leaves nothing behind.
//
// ---- WHY THE REFUSAL IS STATE AND NOT A RETURN VALUE ----
// A refusal has to survive the frame it happened on, because the player is told about it by a label
// that fades over a second and a half at the tile they tried. Returning an enum from Paint would put the
// job of remembering it on the caller, and the caller is a 6000-line window. It lives here, with an
// expiry, and the UI just draws whatever is currently unexpired.
// ============================================================================================
public static class BuildPlacement
{
    // ---- What the session is ----
    public static bool Active { get; private set; }
    public static CelestialBody Body { get; private set; }
    public static SurfaceBuildingType Type { get; private set; }

    public static SurfaceBuildingInfo Info =>
        Active ? SurfaceBuildingDatabase.Get(Type) : null;

    /// The building being EXTENDED, when this session is an expansion of one that already stands rather
    /// than a new structure. Null for a fresh build. See SurfaceBuildManager's merge rules: a farm drawn
    /// onto the edge of a farm is one farm, and this is how the session knows which one before a tile is
    /// even painted.
    public static PlacedBuilding Expanding { get; private set; }

    // Ordered, because for a free paint the FIRST cell is the origin the building remembers
    // (PlacedBuilding.SetDrawnShape). The set is membership, so re-crossing a tile mid-drag is free.
    static readonly List<Vector2Int> cells = new List<Vector2Int>();
    static readonly HashSet<Vector2Int> set = new HashSet<Vector2Int>();

    public static IReadOnlyList<Vector2Int> Cells => cells;
    public static int Tiles => cells.Count;

    /// Is this cell part of what is currently painted? Exposed rather than left to the caller to scan
    /// the list for — the drawing code asks it per cell, per frame, and the set is right here.
    public static bool HasCell(Vector2Int c) => set.Contains(c);

    public static int MinTiles => Active ? Mathf.Max(1, Info.minTiles) : 1;

    /// How big the BUILDING will be, which is not the same as how many tiles have been painted.
    ///
    /// An extension of a twenty-tile farm is one tile of drawing and a twenty-one-tile farm. Every rule
    /// about size — the minimum, the counter, whether Confirm is allowed — is about the building, so
    /// they all read this rather than Tiles. Measuring the minimum against the painted tiles instead
    /// would demand four NEW tiles to add anything at all to a farm that is already twenty, which is a
    /// rule with no reason behind it.
    public static int MergedTiles => Tiles + (Expanding != null ? Expanding.TileCount : 0);

    /// Tiles against tiles needed, as the floating counter reads it. CLAMPED at the minimum on purpose —
    /// the spec asks for 3/3 to stay 3/3 once it is met, not to become 7/3. Past the minimum the number
    /// has stopped being a target and would only read as an error.
    public static int CounterShown => Mathf.Min(MergedTiles, MinTiles);
    public static bool MeetsMinimum => MergedTiles >= MinTiles;

    // ---- The refusal label ----
    // What the player last tried that could not be done, where they tried it, and how long it stays up.
    public static string RefusalText { get; private set; }
    public static Vector2Int RefusalCell { get; private set; }
    static float refusalUntil;

    /// How long a refusal stays on screen. Long enough to read a short sentence, short enough that a
    /// player dragging along a coastline is not trailed by a queue of them.
    public const float RefusalSeconds = 1.6f;

    public static bool RefusalShowing =>
        !string.IsNullOrEmpty(RefusalText) && Time.unscaledTime < refusalUntil;

    /// 1 when the refusal has just appeared, falling to 0 as it expires — the label's alpha.
    public static float RefusalFade =>
        !RefusalShowing ? 0f : Mathf.Clamp01((refusalUntil - Time.unscaledTime) / (RefusalSeconds * 0.5f));

    static void Refuse(Vector2Int at, string text)
    {
        // Deliberately NOT suppressed when the same refusal repeats at the same tile. Re-stamping the
        // expiry is what keeps the label up while the player keeps trying, which is exactly when they
        // most want to be reading it.
        RefusalCell = at;
        RefusalText = text;
        refusalUntil = Time.unscaledTime + RefusalSeconds;
    }

    public static void ClearRefusal() { RefusalText = null; }

    // ---- Guidance ----
    // The translucent highlight over every cell that could legally be painted next. Recomputed only when
    // the footprint changes: it is read every frame by the drawing code, and a flood of set operations
    // per frame over a 400-wide world is a real cost for an answer that only moves when a tile does.
    static readonly HashSet<Vector2Int> guidance = new HashSet<Vector2Int>();
    static bool guidanceStale = true;

    // ============================================================================================
    // OPENING AND CLOSING
    // ============================================================================================

    /// Enter Placement Mode for a class. `expand` makes it an extension of a standing building.
    public static void Begin(CelestialBody b, SurfaceBuildingType t, PlacedBuilding expand = null)
    {
        Active = true;
        Body = b;
        Type = t;
        Expanding = expand;
        cells.Clear();
        set.Clear();
        guidanceStale = true;
        expansionStale = true;
        ClearRefusal();
    }

    /// Leave without building. Nothing was ever spent, so there is nothing to give back.
    public static void Cancel()
    {
        Active = false;
        Body = null;
        Expanding = null;
        cells.Clear();
        set.Clear();
        guidance.Clear();
        guidanceStale = true;
        ClearRefusal();
    }

    /// Throw away what is painted but stay in the mode, ready to draw again somewhere else.
    public static void ClearShape()
    {
        cells.Clear();
        set.Clear();
        // The extension target went with the tiles. Keeping it would leave the session insisting the
        // next tile must touch a building the player has just walked away from.
        Expanding = null;
        guidanceStale = true;
        expansionStale = true;
        ClearRefusal();
    }

    /// The session is only meaningful while the window is on the world it was opened for.
    public static bool IsFor(CelestialBody b) => Active && Body == b;

    // ============================================================================================
    // PAINTING
    // ============================================================================================

    /// Try to add one cell. Returns true if the footprint actually grew.
    ///
    /// Every refusal path sets the floating label, so a caller that ignores the return value still shows
    /// the player why nothing happened. The ORDER of the checks is the order the reasons matter in: a
    /// tile under the sea is not "unaffordable", it is wet, and saying the wrong one sends the player to
    /// fix the wrong thing.
    public static bool Paint(Vector2Int cell)
    {
        if (!Active || Body?.surface == null) return false;

        // Already ours. Silent — re-crossing a painted tile during a drag is the normal case, not a
        // mistake, and a label for it would strobe the whole time the button is held.
        if (set.Contains(cell)) return false;

        var info = Info;

        if (!InBounds(cell)) return false;      // off the map entirely; nothing to point a label at

        // ---- Connectivity, before ground ----
        // A tile that is both disconnected AND underwater should read as disconnected: the player is
        // being told the SHAPE rule, which is the one they are currently learning, and the water is
        // obvious from the map anyway.
        if (!IsConnectedCandidate(cell))
        {
            Refuse(cell, Tiles == 0 && Expanding != null
                ? "Must touch the existing building!"
                : "Must connect edge-to-edge!");
            return false;
        }

        // ---- Ground ----
        if (!SurfaceBuildManager.CellBuildable(Body, info, cell.x, cell.y, out string why))
        {
            Refuse(cell, Capitalise(why) + "!");
            return false;
        }

        if (SurfaceBuildManager.At(Body, cell.x, cell.y) != null)
        {
            Refuse(cell, "Something is already built here!");
            return false;
        }

        if (SurfaceBuildQueue.PendingCells(Body).Contains(cell))
        {
            Refuse(cell, "Another project is going up here!");
            return false;
        }

        // ---- Affordability, last, because it is the only one that is about the BUILDING rather than
        // about this tile. Quoted as the shortfall for the tile being attempted, which is the number the
        // player needs: "Need 34 metal!" tells them how far off they are, "not enough resources" does not.
        if (!CanAffordTiles(Tiles + 1, out int shortMetal, out int shortEnergy))
        {
            Refuse(cell, Shortfall(shortMetal, shortEnergy));
            return false;
        }

        // ---- THE FIRST TILE DECIDES WHETHER THIS IS AN EXTENSION ----
        //
        // Painting onto the edge of a standing farm makes this session an extension OF that farm, which
        // is what lets the guidance grids reach along the existing building from the very next tile and
        // what decides the survivor when the merge happens.
        //
        // Picked up automatically rather than requiring the player to choose a mode first. They already
        // said what they meant by putting the tile there, and the merge at completion would happen
        // either way (SurfaceBuildQueue.Complete) — so a session that did not notice would only differ
        // by showing worse guidance on the way to the same building.
        if (Tiles == 0 && Expanding == null)
            Expanding = SurfaceBuildManager.ExpansionTargetAt(Body, Type, cell);

        cells.Add(cell);
        set.Add(cell);
        guidanceStale = true;
        return true;
    }

    /// Is this cell a legal place for the NEXT tile, ignoring ground and cost?
    ///
    /// The first tile of a fresh building may go anywhere. The first tile of an EXPANSION must touch the
    /// building being expanded — that is what makes it an expansion rather than a second building that
    /// happens to be nearby. Everything after the first must touch what is already painted.
    static bool IsConnectedCandidate(Vector2Int cell)
    {
        if (Tiles == 0)
        {
            if (Expanding == null) return true;
            foreach (var c in SurfaceBuildingDatabase.Footprint(Expanding))
                if (IsOrthogonal(c, cell)) return true;
            return false;
        }

        foreach (var c in cells)
            if (IsOrthogonal(c, cell)) return true;

        // An expansion stays connected to its parent too, so painting can grow back along the existing
        // building rather than only outward from the first new tile.
        if (Expanding != null)
            foreach (var c in SurfaceBuildingDatabase.Footprint(Expanding))
                if (IsOrthogonal(c, cell)) return true;

        return false;
    }

    /// Edge-to-edge only. The one line that makes corner-to-corner illegal everywhere in this file.
    static bool IsOrthogonal(Vector2Int a, Vector2Int b)
        => (a.x == b.x && Mathf.Abs(a.y - b.y) == 1) || (a.y == b.y && Mathf.Abs(a.x - b.x) == 1);

    // ============================================================================================
    // THE BOX DRAGS
    //
    // Square and Rectangle classes are not painted tile by tile — the anchor is a corner and the box
    // grows toward the cursor. They go through the same session as everything else so that the confirm
    // step, the cost readout and the minimum counter are identical whatever you are placing; only the
    // brush differs.
    //
    // THE BOX IS SHRUNK UNTIL IT IS AFFORDABLE rather than refused outright. A player dragging a reactor
    // out across a continent should watch it stop growing at the size their stockpile allows, which is
    // the same "you cannot draw what you cannot pay for" rule the paint brush enforces, expressed the
    // way a drag can express it. Refusing the whole box would mean the shape vanishes mid-drag.
    // ============================================================================================
    public static void SetBox(Vector2Int anchor, Vector2Int cursor, bool square)
    {
        if (!Active) return;

        int minSide = square ? MinSide(Info) : 2;
        int minW = square ? minSide : 2, minH = square ? minSide : 2;

        int dx = cursor.x - anchor.x, dy = cursor.y - anchor.y;
        int wantW = Mathf.Max(minW, Mathf.Abs(dx) + 1);
        int wantH = Mathf.Max(minH, Mathf.Abs(dy) + 1);
        if (square) wantW = wantH = Mathf.Max(minSide, Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) + 1);

        // Shrink toward the minimum until the box is payable. Steps down the LONGER side first so a
        // rectangle keeps roughly the proportions that were dragged instead of collapsing along one axis.
        while (true)
        {
            if (CanAffordTiles(wantW * wantH, out _, out _)) break;
            if (wantW <= minW && wantH <= minH) break;      // already at the smallest legal box
            if (wantW - minW >= wantH - minH && wantW > minW) wantW--;
            else if (wantH > minH) wantH--;
            else wantW--;
            if (square) wantW = wantH = Mathf.Min(wantW, wantH);
        }

        int sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;

        cells.Clear(); set.Clear();
        for (int i = 0; i < wantW; i++)
            for (int j = 0; j < wantH; j++)
            {
                var c = new Vector2Int(anchor.x + sx * i, anchor.y + sy * j);
                if (set.Add(c)) cells.Add(c);
            }
        guidanceStale = true;

        // The box is a shape, not a claim about the ground: cells over water or over another building
        // are left in and reported by CanConfirm, exactly as they were before this session existed. A
        // box that silently omitted its illegal cells would no longer be a box.
    }

    /// The shortest side a square class may have, from its tile minimum.
    public static int MinSide(SurfaceBuildingInfo info)
        => Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, info.minTiles))));

    // ============================================================================================
    // WHAT IT COSTS
    // ============================================================================================

    /// Metal and energy for a footprint of `tiles`, at this world's discounts. The same arithmetic
    /// SurfaceBuildQueue.Enqueue will do when the job is actually created — quoted from one place so the
    /// figure under the cursor and the figure charged can never disagree.
    public static void CostOf(int tiles, out int metal, out int energy)
    {
        metal = energy = 0;
        var info = Info;
        if (info == null || tiles <= 0) return;
        float mult = BuildScaling.CostMultiplier(tiles);
        metal = Mathf.RoundToInt(ColonyManager.DiscCost(info.costMetal) * mult);
        energy = Mathf.RoundToInt(ColonyManager.DiscCost(info.costEnergy) * mult);
    }

    /// What is currently painted would cost this.
    public static void Cost(out int metal, out int energy) => CostOf(Tiles, out metal, out energy);

    /// Can the empire pay for a footprint of this size? Reports the SHORTFALL, not the price.
    public static bool CanAffordTiles(int tiles, out int shortMetal, out int shortEnergy)
    {
        shortMetal = shortEnergy = 0;
        if (GameMode.DevMode) return true;

        CostOf(tiles, out int m, out int e);
        shortMetal = Mathf.Max(0, m - Mathf.FloorToInt(PlayerEconomy.Get(ResourceType.Metal)));
        shortEnergy = Mathf.Max(0, e - Mathf.FloorToInt(PlayerEconomy.Get(ResourceType.Energy)));
        return shortMetal == 0 && shortEnergy == 0;
    }

    /// True when one more tile is beyond what the player can pay for — the brush has hit its ceiling.
    /// The UI reads this to turn the cursor and the resource figures red BEFORE the player tries.
    public static bool AtResourceCeiling => Active && !CanAffordTiles(Tiles + 1, out _, out _);

    /// "Need 34 metal!" / "Need 34 metal, 12 energy!"
    static string Shortfall(int metal, int energy)
    {
        if (metal > 0 && energy > 0) return $"Need {metal} metal, {energy} energy!";
        if (metal > 0) return $"Need {metal} metal!";
        if (energy > 0) return $"Need {energy} energy!";
        return "Not enough resources!";       // unreachable; a shortfall with no shortfall is a bug
    }

    // ============================================================================================
    // THE GUIDANCE GRIDS
    //
    // Every cell that could legally take the next tile, highlighted so the legal move is visible rather
    // than discovered. Ground is checked here as well as in Paint, because a highlight over a cell that
    // then refuses the click is worse than no highlight at all — it is an instruction the game does not
    // honour.
    //
    // COST IS DELIBERATELY NOT CHECKED HERE. At the resource ceiling every cell would go unlit at once
    // and the player would be looking at a map that had simply stopped responding. The ceiling is
    // reported by its own channel (AtResourceCeiling, and the label on the attempted tile), which says
    // WHY; an empty map says nothing.
    // ============================================================================================
    // ============================================================================================
    // WHERE YOU COULD EXTEND SOMETHING YOU ALREADY HAVE
    //
    // Shown the moment a class is picked, BEFORE a single tile is drawn: every empty buildable cell
    // touching a structure of that same class. It is the offer the merge rule implies — "there is a farm
    // over there and you could make it bigger instead of starting a new one" — and without it the offer
    // is invisible and the player only discovers merging by accident.
    //
    // Distinct from Guidance() on purpose, and drawn in a different colour: guidance is "the next tile
    // of the thing you are drawing may go here", this is "the thing you are ABOUT to draw could join
    // that". They are answers to different questions and collapsing them would make a world with one
    // farm on it light up around that farm as though the player had already started drawing there.
    // ============================================================================================
    static readonly HashSet<Vector2Int> expansionSites = new HashSet<Vector2Int>();
    static bool expansionStale = true;
    static int expansionBuildingCount = -1;

    public static HashSet<Vector2Int> ExpansionSites()
    {
        if (!Active || Body == null) { expansionSites.Clear(); return expansionSites; }

        // Only offered before drawing starts. Once there are tiles down, Guidance() is the live answer
        // and this would be a second, contradictory highlight over the same map.
        if (Tiles > 0) { expansionSites.Clear(); return expansionSites; }

        // Recomputed when the world's building list changes under it — a queued extension completing is
        // exactly the case that adds new sites while this session is still open. Counting buildings is a
        // weak test (a demolish plus a build in one frame is invisible to it) but it is O(1) and the
        // consequence of missing one is a stale highlight for a frame, not a wrong build.
        int n = SurfaceBuildManager.On(Body).Count;
        if (!expansionStale && n == expansionBuildingCount) return expansionSites;
        expansionStale = false;
        expansionBuildingCount = n;

        expansionSites.Clear();
        foreach (var c in SurfaceBuildManager.ExpansionSites(Body, Type)) expansionSites.Add(c);
        return expansionSites;
    }

    public static HashSet<Vector2Int> Guidance()
    {
        if (!guidanceStale) return guidance;
        guidanceStale = false;
        guidance.Clear();

        if (!Active || Body?.surface == null) return guidance;

        var info = Info;
        var pending = SurfaceBuildQueue.PendingCells(Body);
        var occupied = SurfaceBuildManager.Occupied(Body);

        // Nothing painted and nothing being expanded: the first tile may go anywhere, and lighting the
        // whole world would be a wash rather than guidance. The UI shows the plain hover ghost instead.
        if (Tiles == 0 && Expanding == null) return guidance;

        // The frontier is everything currently part of this footprint — the painted cells, plus the
        // building being expanded if there is one.
        var frontier = new List<Vector2Int>(cells);
        if (Expanding != null) frontier.AddRange(SurfaceBuildingDatabase.Footprint(Expanding));

        foreach (var c in frontier)
        {
            TryGuide(c + Vector2Int.up, info, occupied, pending);
            TryGuide(c + Vector2Int.down, info, occupied, pending);
            TryGuide(c + Vector2Int.left, info, occupied, pending);
            TryGuide(c + Vector2Int.right, info, occupied, pending);
        }

        return guidance;
    }

    static void TryGuide(Vector2Int c, SurfaceBuildingInfo info,
                         HashSet<Vector2Int> occupied, HashSet<Vector2Int> pending)
    {
        if (set.Contains(c)) return;                 // already painted
        if (!InBounds(c)) return;
        if (occupied.Contains(c)) return;            // "if there is an existing building on one of the
                                                     // valid sides, the guidance grid should not appear"
        if (pending.Contains(c)) return;
        if (!SurfaceBuildManager.CellBuildable(Body, info, c.x, c.y, out _)) return;
        guidance.Add(c);
    }

    static bool InBounds(Vector2Int c)
        => Body?.surface != null && c.x >= 0 && c.y >= 0
        && c.x < Body.surface.width && c.y < Body.surface.height;

    // ============================================================================================
    // CONFIRMING
    // ============================================================================================

    /// Is the painted footprint something that can actually be built right now?
    ///
    /// Re-asks EVERYTHING rather than trusting the checks the brush already made. Between the first tile
    /// going down and the player pressing Confirm the economy has ticked, another world may have spent
    /// the metal, a settlement may have grown onto the site, and a research project may have finished.
    /// The brush's checks are there to stop illegal tiles being painted; this is the one that decides.
    public static bool CanConfirm(out string why)
    {
        why = null;
        if (!Active) { why = "not placing anything"; return false; }
        if (Tiles == 0) { why = "nothing drawn"; return false; }

        var info = Info;

        if (!MeetsMinimum)
        {
            why = $"{info.name} needs at least {MinTiles} tiles — {MergedTiles} drawn";
            return false;
        }

        if (!SurfaceBuildManager.CanPlaceType(Body, Type, out why)) return false;

        // ---- THE SHAPE RULES ARE ASKED OF THE FINISHED BUILDING, NOT OF THE NEW TILES ----
        //
        // For a fresh structure those are the same set. For an EXTENSION they are not, in two ways that
        // both matter:
        //
        //   The new tiles need not be connected to EACH OTHER. Paint two tiles onto opposite sides of a
        //   farm and the brush is perfectly happy — each touches the farm, which is the rule — but the
        //   two of them as a set are two pieces. Validating the new cells alone would refuse a shape
        //   the brush had just spent the whole session telling the player was legal.
        //
        //   The minimum is about the building, as above.
        //
        // Merging is restricted to Free classes (SurfaceBuildManager.CanMerge), whose only shape rule is
        // "one connected piece" — and a union of two touching connected pieces always is one. So this
        // check passes by construction for a legal extension, and is here to catch the cases where it
        // would not: a stale Expanding pointing at a building that has moved or gone.
        var shape = new List<Vector2Int>(cells);
        if (Expanding != null)
            foreach (var c in SurfaceBuildingDatabase.Footprint(Expanding))
                if (!set.Contains(c)) shape.Add(c);

        if (!BuildShapeRules.Validate(info, shape, out why)) return false;

        var occupied = SurfaceBuildManager.Occupied(Body);
        var pending = SurfaceBuildQueue.PendingCells(Body);
        foreach (var c in cells)
        {
            if (!SurfaceBuildManager.CellBuildable(Body, info, c.x, c.y, out why)) return false;
            if (occupied.Contains(c)) { why = "something is already standing there"; return false; }
            if (pending.Contains(c)) { why = "another project is already going up there"; return false; }
        }

        if (!CanAffordTiles(Tiles, out int sm, out int se)) { why = Shortfall(sm, se); return false; }

        return true;
    }

    /// Commit: the footprint becomes a queued build job and the session ends.
    ///
    /// The job is what charges for it — this deliberately does not touch the economy, so there is
    /// exactly one place a building is paid for and it is the same one whether the player drew it or a
    /// script queued it.
    public static bool Confirm(out string why)
    {
        if (!CanConfirm(out why)) return false;

        var b = Body;
        var t = Type;
        var expand = Expanding;
        var painted = new List<Vector2Int>(cells);

        // The merge target goes in with the job rather than being attached afterwards: Enqueue validates
        // the SHAPE, and for an extension the shape is the new cells plus the building being extended.
        // Setting it after the fact would mean the validation had already run against the wrong set.
        var job = SurfaceBuildQueue.Enqueue(b, t, painted, expand, out why);
        if (job == null) return false;

        Cancel();
        return true;
    }

    /// A world was replaced, or the window closed on the session. Same as Cancel, named for the caller.
    public static void Abandon() => Cancel();

    static string Capitalise(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}
