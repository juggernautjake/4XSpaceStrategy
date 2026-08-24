using UnityEngine;

// ============================================================================================
// WHAT A WORLD HAS GIVEN UP, AND HOW FAST
//
// Surveying used to be one number: explorationProgress, 0 to 1, and at 1 the world was "surveyed" and
// everything about it appeared at once. Deep Research then bolted a second, unrelated ladder on top —
// three tiers, each an empire-tech unlock, each handing over two more index overlays in a lump.
//
// Both had the same problem: a survey was a TIMER, not a picture. Nothing on screen changed while it
// ran, and then everything changed at the instant it finished.
//
// So there are two levels now, and both of them are things you WATCH:
//
//   LEVEL 1 — THE GROUND. The surface map starts blacked out cell by cell and uncovers as the ship
//             works, so the coastlines arrive as they are found. Finishing it settles the world's
//             habitability. Any ship that can survey at all can do this.
//
//   LEVEL 2 — WHAT THE GROUND IS WORTH. The six index overlays, one after another in the order they
//             have always been listed, and each one in three passes: the 70s band first, then the
//             80s, then the 90s. A pass paints its whole range in the coarse colour it has resolved so
//             far and then refines it, which is what a survey actually does — it finds the promising
//             region before it finds the best cell in it. SCIENCE SHIPS ONLY.
//
// ---- WHY THE DEEP RESEARCH TIERS ARE GONE ----------------------------------------------------
// They gated the same six overlays behind Empire Tech levels, which meant a world could be studied to
// exhaustion and still refuse to show you its water map because the empire had not researched
// something unrelated somewhere else. That is a fine gate for a CAPABILITY and a bad one for a fact
// about a rock you are standing on. The order the tiers implied is kept — it was a good order, and it
// is the order in SurfaceIndex.All — but reaching the end of it is now a matter of finishing the job.
// The Vael fragment that used to appear at Tier III appears on the last index instead, so the Ancients
// hunt keeps its late-game trigger.
// ============================================================================================
public static class Survey
{
    /// Bands in the shown range: 70s, 80s, 90s. Each is one PASS of a level-2 index survey.
    public static int Bands => Mathf.Max(1, Mathf.RoundToInt((1f - SurfaceIndex.ShowFloor) / SurfaceIndex.BandStep));

    /// Total level-2 passes on a world that uses every index. The per-world figure is
    /// `PresentCount(b) * Bands`; this is the ceiling, kept for readouts that describe the system
    /// rather than a particular planet.
    public static int TotalPasses => SurfaceIndex.All.Length * Bands;

    /// Total level-2 passes THIS world will actually run.
    public static int TotalPassesOn(CelestialBody b) => PresentCount(b) * Bands;

    // ---- How long a cell takes ------------------------------------------------------------------
    //
    // The unit of survey time is ONE CELL, because that is the thing the player watches uncover — and
    // it is the number a technology should be able to buy down.
    //
    // BUT NOT LINEARLY IN THE CELL COUNT, and this is the part worth writing down. Grids in this game
    // run from 10x5 to 640x320 — a factor of FOUR THOUSAND in area — while the survey durations the
    // game is balanced around run from about ten seconds to about a minute and a half. A flat seconds-
    // per-cell across that range cannot produce both: pick a number that makes a moon feel worth doing
    // and a gas giant becomes a half-hour of watching a bar, and pick one that makes the gas giant
    // bearable and a moon is finished before the ship has stopped moving.
    //
    // So the cell count is compressed by SizeExponent before it is charged for. A world with forty
    // times the ground takes about six times as long, which is the shape the old size factor had and
    // is the shape that plays. Measured against the rate this replaced (0.05 per second over
    // max(0.5, surfaceSize/8)), across every grid size in the game:
    //
    //     grid        old      new
    //     100x50      10s      12s
    //     200x100     15s      26s
    //     400x200     30s      56s
    //     640x320     98s      93s
    //
    // CellSeconds below then reports the ACTUAL per-cell time on a given world, which is what the
    // reveal front advances at and what the tech multiplier reduces. It is a real number about a real
    // world rather than a constant that happens to be in the file.

    /// The world the pacing is quoted against: 200x100, the size most worlds you settle come out at.
    const int ReferenceCells = 20_000;

    /// Seconds per cell ON THE REFERENCE WORLD, before ship rate, tech and hostility. 26 seconds for
    /// the whole of it.
    public const float SecondsPerCell = 0.0013f;

    /// How area is charged for. 1 would be linear (see above for why it is not); 0 would make every
    /// world take the same time however big it is.
    const float SizeExponent = 0.55f;

    /// The cell count this world is actually billed for.
    static float ChargedCells(CelestialBody b)
    {
        float cells = Mathf.Max(1, MapMetrics.SurfW(b) * MapMetrics.SurfH(b));
        return ReferenceCells * Mathf.Pow(cells / ReferenceCells, SizeExponent);
    }

    /// How a level-2 pass is priced against a level-1 one, per cell.
    ///
    /// It was 0.55 — cheaper, on the reasoning that the ship is already in orbit with the terrain in
    /// hand and is reading one field rather than charting a coastline. True per pass, and the wrong
    /// number overall once the research hulls became the FAST ones at level 1 too: their higher rate and
    /// the crew-quality multiplier compounded, and the whole six-index deep survey collapsed to between
    /// twelve seconds and two minutes. That is not the long second look it is described as anywhere else
    /// in the game.
    ///
    /// At 1.6 the deep survey runs about 35 seconds for the best hull on a small moon and six minutes
    /// for a basic research ship on a 400x200 world, with level 1 unchanged at 4-56 seconds. The per-PASS
    /// reasoning survives in the pass weights, where each band costs half the one before it.
    public const float DeepSecondsPerCellScale = 1.6f;

    /// The technology hook. Survey speed is meant to improve as the empire does, and this is the one
    /// place that decides by how much: every tier of Empire Tech takes a slice off the per-cell time,
    /// floored so it can never reach zero and make a survey instant.
    ///
    /// A multiplier on SPEED rather than on time, so a caller divides the cell time by it and higher is
    /// always better — the direction of the number matches the direction of the improvement.
    public static float TechSpeedMultiplier => 1f + 0.20f * Mathf.Max(0, EmpireTech.Level - 1);

    /// Seconds this ship takes over one cell of this world — the real number, for this world, after
    /// everything. What the reveal front advances at, and what a technology buys down.
    ///
    /// Hostility survives from the old formula and is the one size-independent thing worth keeping: a
    /// world that is trying to kill the survey team is slower to map whatever its area.
    public static float CellSeconds(CelestialBody b, Unit u, bool deep)
    {
        float cells = Mathf.Max(1, MapMetrics.SurfW(b) * MapMetrics.SurfH(b));
        return SweepSeconds(b, u, deep) / cells;
    }

    /// Seconds for one ship to make ONE full sweep of this world — the whole of level 1, or one band of
    /// one index at level 2.
    public static float SweepSeconds(CelestialBody b, Unit u, bool deep)
    {
        if (b == null) return 1f;
        float hostility = Mathf.Lerp(1f, 2.2f, Mathf.Clamp01((100f - b.habitability) / 100f));
        float rate = Mathf.Max(0.05f, u?.Info.surveyRate ?? 1f) * TechSpeedMultiplier;

        // A better research suite and a more experienced crew read the indexes faster — but on a curve
        // that keeps a level-2 survey a real commitment at every tier. Base research ship ~1.3x, Mk III
        // ~1.8x, a legendary Science Vessel ~2.5x, never the order of magnitude the raw stat gives. This
        // is the old DeepSurveyQuality factor, kept because it was tuned and the tuning still applies;
        // only the thing it scales has changed from a progress rate to a time.
        if (deep && u != null) rate *= 1f + u.EffectiveResearch / 24f;

        float per = SecondsPerCell * (deep ? DeepSecondsPerCellScale : 1f);
        return Mathf.Max(0.01f, ChargedCells(b) * per * hostility / rate);
    }

    /// The fraction of a whole level a ship covers in `dt` seconds.
    public static float Fraction(CelestialBody b, Unit u, float dt, bool deep)
    {
        if (b == null) return 0f;

        // A level-2 survey is six indexes, and each index is a full sweep for its 70s pass plus a half
        // and a quarter for the two that refine it — 1.75 sweeps, not 3, because the later passes are
        // quicker (see PassShare). Getting this wrong would not break anything visibly; it would just
        // silently make a deep survey take twice as long as the pass weights say it should.
        float perIndex = 0f;
        for (int p = 0; p < Bands; p++) perIndex += Mathf.Pow(0.5f, p);

        // PresentCount, not All.Length: a deep survey only sweeps the indexes this world has a use for,
        // so a world with four of them finishes in two thirds the time. This is the line that turns
        // "stop showing me empty indexes" into "stop charging me for them".
        float total = SweepSeconds(b, u, deep) * (deep ? perIndex * PresentCount(b) : 1f);
        return total <= 0f ? 1f : dt / total;
    }

    // ---- The level ------------------------------------------------------------------------------

    /// 0 nothing, 1 the ground is mapped, 2 every index is read.
    public static int LevelOf(CelestialBody b)
    {
        if (b == null) return 0;
        if (GameMode.DevMode) return 2;
        if (b.deepProgress >= 1f) return 2;
        return b.Surveyed ? 1 : 0;
    }

    public static string LevelLabel(CelestialBody b)
    {
        if (b == null) return "Unknown";
        switch (LevelOf(b))
        {
            case 2: return "Level 2 — fully studied";
            case 1: return b.deepProgress > 0f
                ? $"Level 1 — indexes {b.deepProgress * 100f:F0}%"
                : "Level 1 — surface mapped";
            default: return b.explorationProgress > 0f
                ? $"Not surveyed — {b.explorationProgress * 100f:F0}%"
                : "Not surveyed";
        }
    }

    // ---- Which index is being read, and how far in -----------------------------------------------

    /// Where a level-2 survey has got to on one index.
    ///
    ///   started  — is there anything to draw at all
    ///   pass     — how many bands have been resolved: 0 while the 70s are being laid down, 1 while the
    ///              80s are being refined out of them, 2 for the 90s
    ///   frac     — progress through that pass, 0..1
    ///   complete — every band resolved; the overlay is the real thing and its numbers can be read
    public struct Reveal
    {
        public bool started;
        public int pass;
        public float frac;
        public bool complete;
    }

    public static Reveal RevealOf(CelestialBody b, SurfaceIndexKind k)
    {
        var r = new Reveal();
        if (b == null || k == SurfaceIndexKind.None) return r;

        // Dev mode and a world the player owns show everything: the first is a tool and the second is
        // ground the empire lives on, which it does not need a ship to re-explain to it.
        if (GameMode.DevMode || b.deepProgress >= 1f || b.owner == FactionManager.Player)
            return new Reveal { started = true, pass = Bands - 1, frac = 1f, complete = true };

        // -1 means this world has no use for this index at all — no plates and no plumes, no water, no
        // biosphere. It is never reached because it is never visited, which is the point.
        int slot = IndexSlot(b, k);
        if (slot < 0) return r;

        // Each index this world HAS gets an equal share of the whole; within it, the passes are weighted.
        int n = PresentCount(b);
        float mine = Mathf.Clamp01(b.deepProgress) * n - slot;        // 0..1 through THIS index

        if (mine <= 0f) return r;                                     // not reached yet
        if (mine >= 1f) return new Reveal { started = true, pass = Bands - 1, frac = 1f, complete = true };

        // ---- EACH PASS TAKES HALF AS LONG AS THE ONE BEFORE ----
        //
        // The 70s pass is charting unknown ground; the 80s pass is refining a region already found; the
        // 90s pass is picking the best cells out of a region already narrowed twice. Each is a smaller
        // question than the last, so each is quicker — 1 : 1/2 : 1/4, which is 4/7, 2/7 and 1/7 of the
        // index's time.
        float acc = 0f;
        for (int p = 0; p < Bands; p++)
        {
            float share = PassShare(p);
            if (mine < acc + share || p == Bands - 1)
                return new Reveal
                {
                    started = true,
                    pass = p,
                    frac = share <= 0f ? 1f : Mathf.Clamp01((mine - acc) / share),
                    complete = false
                };
            acc += share;
        }
        return new Reveal { started = true, pass = Bands - 1, frac = 1f, complete = true };
    }

    /// What fraction of one index's time pass `p` takes. Halving weights, normalised.
    public static float PassShare(int p)
    {
        float total = 0f;
        for (int i = 0; i < Bands; i++) total += Mathf.Pow(0.5f, i);
        return total <= 0f ? 1f : Mathf.Pow(0.5f, p) / total;
    }

    // ============================================================================================
    // THE RUNNING ORDER IS PER WORLD, NOT GLOBAL
    //
    // SurfaceIndex.All is still the ORDER — Mineral, Geothermal, Fertile, Wind, Solar, Water — but a
    // sweep only visits the indexes this particular world has any use for (SurfaceIndex.Present). A dry,
    // dead rock has no hydrology and no farmland, and it used to spend a third of a level-2 survey
    // mapping both of them before reporting, twice, that there was nothing there. The player paid real
    // minutes for an answer that was knowable before the ship arrived.
    //
    // Slots therefore CLOSE UP. On a world with four usable indexes each takes a quarter of the sweep
    // rather than a sixth, so removing the dead ones makes the survey shorter AND makes every remaining
    // index finish sooner — which is the right shape: less time, same information.
    //
    // No allocation on any of these. They are asked while drawing and, through RevealOf, potentially per
    // tile; `Present` is O(1) behind two caches, and iterating a static array is not an allocation.
    // ============================================================================================

    /// How many indexes this world's survey will actually visit. Never zero — Mineral, Wind and Solar
    /// are present on everything solid, so the floor is defensive rather than reachable.
    public static int PresentCount(CelestialBody b)
    {
        int n = 0;
        foreach (var k in SurfaceIndex.All) if (SurfaceIndex.Present(b, k)) n++;
        return Mathf.Max(1, n);
    }

    /// Position of an index in THIS WORLD's running order, or -1 if this world has no use for it.
    public static int IndexSlot(CelestialBody b, SurfaceIndexKind k)
    {
        int i = 0;
        foreach (var kk in SurfaceIndex.All)
        {
            if (!SurfaceIndex.Present(b, kk)) continue;
            if (kk == k) return i;
            i++;
        }
        return -1;
    }

    /// Position in the canonical order, with no world in hand. Only for callers that genuinely mean
    /// "where does this index sit in the list" rather than "where does this survey reach it".
    public static int IndexSlot(SurfaceIndexKind k)
    {
        for (int i = 0; i < SurfaceIndex.All.Length; i++) if (SurfaceIndex.All[i] == k) return i;
        return -1;
    }

    /// The index a level-2 survey is working on right now, or None when it is finished or not running.
    public static SurfaceIndexKind CurrentIndex(CelestialBody b)
    {
        if (b == null || b.deepProgress <= 0f || b.deepProgress >= 1f) return SurfaceIndexKind.None;
        int n = PresentCount(b);
        int want = Mathf.Clamp(Mathf.FloorToInt(b.deepProgress * n), 0, n - 1);

        int i = 0;
        foreach (var k in SurfaceIndex.All)
        {
            if (!SurfaceIndex.Present(b, k)) continue;
            if (i == want) return k;
            i++;
        }
        return SurfaceIndexKind.None;
    }

    // ---- Which cells have been reached ----------------------------------------------------------
    //
    // A survey reveals CELLS, and which cells it has got to has to be answerable at any moment without
    // storing a bitmask per world per index. So it is a pure function: every cell has a fixed place in
    // the running order, and a cell is uncovered once the survey has passed it. One float per world per
    // level says everything, it costs nothing to save, and a reload mid-survey cannot reshuffle what is
    // already known.
    //
    // THE ORDER IS A SURVEY PATTERN, not a dissolve. It starts at the middle of the map, runs RIGHT
    // along that row and wraps, and then works outward alternating above and below — so the equator is
    // charted first and the poles last, which is the order a ship in an inclined orbit would actually
    // build a map in. Cell 0 is the centre; the last cell is a corner of a pole row.
    //
    //     row rank:  centre, +1, -1, +2, -2, ...
    //     within a row: start at the centre column, run right, wrap around
    //
    // This replaced a hash-jittered diagonal. That looked like weather; this looks like work.

    /// Where a row sits in the running order: 0 is the middle, then alternately one above, one below.
    public static int RowRank(int h, int y)
    {
        int d = y - h / 2;
        return d == 0 ? 0 : (d > 0 ? 2 * d - 1 : -2 * d);
    }

    /// How far along a row a column sits: 0 at the middle column, running right and wrapping.
    public static int ColRank(int w, int x) => ((x - w / 2) % w + w) % w;

    /// Every row of a world, in the order a survey works them. Rows that would fall off the top or
    /// bottom of an asymmetric grid are simply absent — the rank sequence overshoots on one side first.
    public static int[] RowOrder(int h)
    {
        if (rowOrderCache != null && rowOrderCache.Length == h) return rowOrderCache;

        var order = new int[h];
        int n = 0;
        for (int rank = 0; n < h && rank < h * 2 + 2; rank++)
        {
            int y = RowForRank(h, rank);
            if (y >= 0 && y < h) order[n++] = y;
        }
        return rowOrderCache = order;
    }

    static int[] rowOrderCache;

    static int RowForRank(int h, int rank)
    {
        int cy = h / 2;
        if (rank <= 0) return cy;
        int d = (rank + 1) / 2;
        return (rank % 2 == 1) ? cy + d : cy - d;
    }

    /// This cell's place in the running order, 0 .. cells-1. Stable for a given world.
    ///
    /// Used by the LEVEL-2 passes, which are one front rather than several: a deep survey is one science
    /// ship reading one field, and splitting it across rows would be describing a division of labour
    /// that is not happening. Level 1 uses the per-row fills instead — see Rows.
    public static int CellRank(CelestialBody b, int x, int y)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        return RowRank(h, y) * w + ColRank(w, x);
    }

    /// 0..1 position of this cell in the running order.
    public static float CellOrder(CelestialBody b, int x, int y)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        return CellRank(b, x, y) / (float)Mathf.Max(1, w * h);
    }

    // ============================================================================================
    // LEVEL 2 WORKS IN BLOCKS TOO
    //
    // "Index Surveying still looks like it is following the 'row by row' approach to surveying, instead
    // of adopting the new [gridsize]x[gridsize] method. Please update this to reduce the jittery-ness
    // and lag during surveys."
    //
    // It was literally row-major: CellOrder is RowRank * width + ColRank, so a level-2 pass advanced one
    // CELL at a time along one row and then moved to the next. That is the per-cell crawl the level-1
    // rework replaced, still running underneath the index overlays.
    //
    // Both complaints have the same cause and the same fix. The JITTER is that the front moves a
    // fraction of a cell per frame, so the boundary shimmers between two cells. The LAG is that
    // PlanetViewWindow rebuilds and re-uploads the whole overlay texture whenever anything in it
    // changes, and a per-cell front changes something several times a second — on a 640x320 world that
    // is a two-hundred-thousand-pixel upload to move one pixel.
    //
    // Ordering by BLOCK instead makes the front advance in 7x7 steps: the texture is rebuilt on block
    // boundaries rather than continuously, and what the player sees is the same patch-at-a-time sweep
    // level 1 does. The ORDER is unchanged in shape — centre band first, then outward, and within a
    // band from the middle column running right — so a survey still reads the same way.
    // ============================================================================================

    /// The block grid a LEVEL-2 sweep uses. Not a ship's block: a deep survey is one science ship
    /// reading one field across the whole world, and its front is a single front regardless of who is
    /// flying it. The scout block size is the unit, so the level-2 sweep steps at the same visible
    /// grain as a level-1 one.
    public static int DeepBlockCells(CelestialBody b) => DefaultBlockCells(b);

    /// This cell's place in the level-2 running order, as a BLOCK index rather than a cell index.
    public static int BlockRank(CelestialBody b, int x, int y)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int bc = DeepBlockCells(b);
        int ny = BandCount(b, bc);
        int across = BlocksAcross(b, bc);

        // Which band this row falls in. Bands can overlap by a row or two where the world does not
        // divide evenly (see BandY), so the FIRST one that contains the row wins — which is the one
        // nearer the top, and therefore stable.
        int band = ny - 1;
        for (int i = 0; i < ny; i++)
        {
            int y0 = BandY(b, bc, i);
            if (y >= y0 && y < y0 + bc) { band = i; break; }
        }

        return BandRank(b, ny, band) * across + ColBlock(w, bc, x);
    }

    /// Has the survey reached this cell yet, at `progress` through the current sweep?
    ///
    /// The single-front form, for the LEVEL-2 passes. Level 1 asks ReachedGround instead.
    public static bool Reached(CelestialBody b, int x, int y, float progress)
    {
        if (progress >= 1f) return true;
        if (progress <= 0f) return false;

        int bc = DeepBlockCells(b);
        int total = Mathf.Max(1, BlocksAcross(b, bc) * BandCount(b, bc));
        return BlockRank(b, x, y) < Mathf.FloorToInt(progress * total + 1e-4f);
    }

    // ============================================================================================
    // LEVEL 1, A BLOCK AT A TIME
    //
    // A survey used to advance one CELL at a time along one row. That is the finest possible grain and
    // it was the wrong one for two separate reasons:
    //
    //   IT LAGGED. Every cell that changed state forced the whole fog texture to be rebuilt and
    //   re-uploaded, and on a 640x320 world the front crosses a cell several times a second. The map
    //   spent its frame budget re-uploading a two-hundred-thousand-pixel texture to change one pixel.
    //
    //   IT DID NOT LOOK LIKE ANYTHING. A single cell winking out is beneath notice at map zoom. What a
    //   survey should look like is a machine working a patch of ground and then moving to the next one.
    //
    // So a ship now clears a whole BLOCK at a time, sits on it for a few seconds while the cloud over
    // it thins, and moves on. Four seconds for a scout, three and a half for a science ship, and the
    // block a science ship clears is half again as wide — which is what "science ships always have the
    // advantage" comes out as when you put both numbers together: about 2.2 times the ground per second.
    //
    // ---- HOW BIG A BLOCK IS, AND THE ARITHMETIC THAT FORCED IT --------------------------------
    //
    // A block is quoted in SURVEY UNITS — 5 for a scout, 7 for a science hull, more with technology —
    // and a unit is worth some number of cells ON THIS WORLD. It is not always one.
    //
    // It cannot always be one. Grids here run from 10x5 to 640x320, a factor of four thousand in area.
    // At a literal 5x5 cells and four seconds a block, a 200x100 world is eight hundred blocks, which
    // is FIFTY-THREE MINUTES of watching a survey — against the ten to ninety seconds every other part
    // of the game is balanced around. Meanwhile a small moon would be nine blocks and over in half a
    // minute, which is about right. One constant cannot serve both ends of that range, and the same
    // problem is already documented above for seconds-per-cell.
    //
    // So the block time is exactly what was asked for — 4.0s and 3.5s, and technology buys it down —
    // and the block GROWS with the world instead, so the number of blocks stays in a range a player
    // will sit through. A small moon really does get a 5x5 patch; a 400x200 world gets the same five
    // units drawn as a much larger square. The ratio between the two hulls is preserved everywhere, so
    // a science ship's block is always visibly bigger than a scout's, which is the thing the player
    // actually reads off the screen.
    //
    // ---- WHY THE STATE DID NOT CHANGE ------------------------------------------------------------
    //
    // Still one float per pixel row, still `surveyRows`, still the same save format and the same
    // migration path. A block reveal is a contiguous run along each of the rows it covers, which is
    // exactly what a row fill already describes — so `rows[y] * blocksPerRow` reads as "blocks finished
    // on this row", and the FRACTIONAL part is how far into the current block the ship has got. That
    // fraction is what the cloud over the working block thins by, so the veil needs no state of its own
    // either.
    //
    // The consequence worth knowing: two ships with different block sizes on the same world cannot
    // desynchronise, because fills only ever increase and a block boundary is only ever a place where
    // one stops. The ship count still only decides who works on what NEXT.
    // ============================================================================================

    /// The block a scout takes in one bite, and the block a research hull takes. Named because
    /// WorldSurveySeconds quotes the world's difficulty against the SCOUT's figure.
    public const int ScoutUnits = 5, ScienceUnits = 7;

    /// How many cells across a ship's block is. THE BLOCK IS THIS, LITERALLY — see the header below.
    ///
    /// The technology hook: every tier of Empire Tech widens the patch a survey can take in one bite.
    public static int SurveyUnits(Unit u)
    {
        // A science hull is the only thing that can run a level-2 survey at all, and it leads at level
        // 1 too — see the Rate() block in UnitType. Seven against five is a 96% bigger patch.
        int baseUnits = (u?.Info != null && u.Info.canResearch) ? ScienceUnits : ScoutUnits;
        return baseUnits + Mathf.Clamp(EmpireTech.Level - 1, 0, 6);
    }

    // ============================================================================================
    // A BLOCK IS 7x7. IT IS NOT 7x7 TIMES SOMETHING.
    //
    // "I have seen the scout ship have varying grid surveying sizes all the way up to around 8x8, and
    // Science ships with a grid survey size of 14x14... on a 10x5 asteroid, a science ship is only
    // doing a survey grid size of 2x2. This should take no time at all with a fixed 7x7 grid survey
    // size, yet for some reason the grid survey size seems to scale based on the planet, this should
    // not be the case."
    //
    // Both observations were exactly what the code did. `CellsPerUnit` multiplied the ship's units by a
    // per-world factor, so the same science hull drew a 2x2 patch on a 10x5 moon and a 14x14 one on a
    // 400x200 world — and the 14x14 did not even fit inside the band it was working, which is the other
    // half of the report.
    //
    // ---- WHY THE SCALING WAS THERE, AND WHAT REPLACES IT -----------------------------------------
    //
    // It was there because of arithmetic that has not gone away. Grids run 10x5 to 640x320. At a literal
    // 7x7 and the specified 3.5 seconds a block:
    //
    //     10x5        2 blocks       7s
    //     50x25      32 blocks     112s
    //     200x100   435 blocks      25 MINUTES
    //     640x320  4232 blocks       4.1 HOURS
    //
    // So one of the three numbers — block size, block time, total duration — has to give on large
    // worlds, and the request is explicit that it must not be the block size.
    //
    // It is the DWELL that floats now, and only downward, and only when it has to. A world small enough
    // to finish inside MaxSurveySeconds at the full 3.5s a block gets exactly that; a bigger one has its
    // dwell shortened until either the world fits or the dwell hits a floor where the marker would start
    // to strobe. Past that floor the ship widens its SWEEP HEAD instead, working several adjacent blocks
    // at once — which reads as a bigger job needing more coverage, and keeps every square on screen 7x7.
    //
    //     world      blocks   dwell   heads   total
    //     10x5            2   3.5s        1      7s
    //     50x25          32   3.5s        1    112s
    //     200x100       435   0.55s       1      4m
    //     560x280      3200   0.35s       4    4.7m
    //     640x320      4232   0.35s       4    6.2m
    // ============================================================================================

    /// The dwell a block gets when the world is small enough to afford it — the figure the original
    /// request named. Scout, then science hull.
    const float ScoutBlockSeconds = 4.0f, ScienceBlockSeconds = 3.5f;

    /// The shortest a block may be dwelt on. Below this the marker stops reading as a machine working a
    /// patch of ground and starts reading as a flicker, and the fog texture is rebuilt on every block
    /// boundary — which is the cost the whole block system exists to avoid.
    const float MinBlockSeconds = 0.35f;

    /// Where a survey's duration stops growing in proportion to the world and starts being compressed.
    const float SoftCapSeconds = 120f;

    /// How hard it is compressed past that. 1 would be no compression at all (and a four-hour gas
    /// giant); 0 would make every world past the cap take exactly the same time however big it is.
    /// A quarter-power puts a 256x range of area into a 4x range of time.
    const float SurveyCompression = 0.25f;

    /// How much faster a research hull surveys than a scout. THE ONLY PLACE THE CLASS DIFFERENCE LIVES.
    ///
    /// It used to be an emergent property of two other numbers — the science hull's bigger block and its
    /// shorter dwell — and the survey checker caught what that produced once the block stopped scaling
    /// with the world: on a 200x100 world the SCOUT came out faster (140s against 240s), because its
    /// smaller block gave it more of them, which pushed it over the threshold where a ship widens its
    /// sweep head, and the widening overshot. Two numbers multiplying into a third that nobody states is
    /// exactly how a ladder inverts without anyone noticing.
    ///
    /// Stating it directly makes the ordering true by construction rather than by arithmetic accident.
    const float ScienceAdvantage = 2.2f;

    /// The most blocks a single ship's sweep head is drawn as. A visual cap only — the ship's actual
    /// rate is a float (see HeadSpeed), so hitting this ceiling does not slow the survey down, it just
    /// stops the marker becoming a bar across the whole map.
    public const int MaxHeads = 8;

    /// The side of this ship's block on this world, in cells. LITERALLY the survey units, clamped only
    /// so it cannot exceed the world it is drawn on — a 7-cell block on a 5-tall asteroid is 5 tall.
    public static int BlockCells(CelestialBody b, Unit u)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        return Mathf.Clamp(SurveyUnits(u), 2, Mathf.Max(2, Mathf.Min(w, h)));
    }

    /// How many blocks a survey of this world is, for a given block size.
    public static int BlockCount(CelestialBody b, int bc)
        => Mathf.Max(1, BlocksAcross(b, bc) * BandCount(b, bc));

    /// How long a level-1 survey of THIS WORLD should take a baseline scout, before its own rate,
    /// its class or any technology is applied.
    ///
    /// Quoted against the SCOUT's block deliberately, so the world's difficulty is a property of the
    /// world. Deriving it from whichever ship is asking would mean a world got easier because a better
    /// ship turned up, and then the class advantage below would be applied on top of that — counting
    /// the same thing twice, which is the bug this shape exists to avoid.
    public static float WorldSurveySeconds(CelestialBody b)
    {
        int bc = Mathf.Clamp(ScoutUnits, 2, Mathf.Max(2, Mathf.Min(MapMetrics.SurfW(b), MapMetrics.SurfH(b))));
        float ideal = BlockCount(b, bc) * ScoutBlockSeconds;
        return ideal <= SoftCapSeconds
             ? ideal
             : SoftCapSeconds * Mathf.Pow(ideal / SoftCapSeconds, SurveyCompression);
    }

    /// How long a level-1 survey of this world takes THIS ship. The one number the dwell and the sweep
    /// head are both derived from, so they cannot disagree about how long the job is.
    public static float SurveySeconds(CelestialBody b, Unit u)
    {
        float rate = Mathf.Max(0.05f, u?.Info.surveyRate ?? 1f);
        float cls = (u?.Info != null && u.Info.canResearch) ? ScienceAdvantage : 1f;

        // The hull's own survey rate, on a deliberately shallow curve — a Mk III beats a Mk I without
        // swamping the class difference above.
        float speed = cls * (0.6f + 0.4f * rate) * TechSpeedMultiplier;

        // Hostility survives from the pre-block formula and is the one size-independent thing worth
        // keeping: a world trying to kill the survey team is slower to map whatever its area.
        float hostility = b != null ? Mathf.Lerp(1f, 1.6f, Mathf.Clamp01((100f - b.habitability) / 100f)) : 1f;

        return Mathf.Max(1f, WorldSurveySeconds(b) * hostility / Mathf.Max(0.05f, speed));
    }

    /// Seconds a ship spends on ONE block of THIS world.
    ///
    /// The full dwell where the world can afford it, shortened where it cannot, and floored where
    /// shortening it further would make the marker strobe. Past that floor the ship widens its sweep
    /// head instead — see HeadSpeed.
    public static float BlockSeconds(CelestialBody b, Unit u)
    {
        float baseSeconds = (u?.Info != null && u.Info.canResearch) ? ScienceBlockSeconds : ScoutBlockSeconds;
        int bc = BlockCells(b, u);
        float want = SurveySeconds(b, u) / BlockCount(b, bc);
        return Mathf.Clamp(want, MinBlockSeconds, baseSeconds);
    }

    /// How many blocks' worth of ground the ship covers per dwell. A FLOAT, and that matters.
    ///
    /// An integer here is what inverted the class ladder: rounding 1.17 up to 2 made a scout finish a
    /// 200x100 world in 140 seconds against a science ship's 240. As a float the arithmetic closes
    /// exactly — blocks * dwell / speed IS SurveySeconds — so the duration is whatever SurveySeconds
    /// says and nothing else can perturb it. Only the MARKER COUNT is rounded, and only for drawing.
    public static float HeadSpeed(CelestialBody b, Unit u)
    {
        int bc = BlockCells(b, u);
        float total = BlockCount(b, bc) * BlockSeconds(b, u);
        return Mathf.Max(1f, total / Mathf.Max(0.01f, SurveySeconds(b, u)));
    }

    /// How many block markers this ship's sweep head is drawn as. Visual only.
    public static int Heads(CelestialBody b, Unit u)
        => Mathf.Clamp(Mathf.CeilToInt(HeadSpeed(b, u) - 0.001f), 1, MaxHeads);

    // ============================================================================================
    // THE BLOCK GRID — CENTRED, AND NEVER OFF THE MAP
    //
    // "First of all the Survey area should attempt to start at the center of the Grid map... you can
    // see the white square is the size of the science ships survey grid size and the unveiled grids at
    // the top of the map is only 6 grids tall. The entire 14x14 survey area was not even within the
    // surveyable area to begin with."
    //
    // Both faults come from the grid having been ANCHORED AT THE ORIGIN. Bands were `band * blockCells`
    // from y = 0, so:
    //
    //   THE LAST BAND WAS A STUB. On a 20-tall world with a 14-cell block there are two bands: rows
    //   0-13 and rows 14-19. The second is six rows tall and the marker drawn over it was fourteen.
    //
    //   THE CENTRE BAND WAS NOT THE CENTRE. The running order starts at band `count / 2`, which on that
    //   same world is band 1 — the six-row stub at the BOTTOM. So a survey documented as starting in the
    //   middle started at an edge.
    //
    // The grid is now defined by where the bands GO rather than by tiling from a corner: `ny` bands,
    // every one of them exactly `bc` tall, spread evenly across [0, h - bc] so the first and last sit
    // flush with the edges and the middle one is centred. They overlap slightly where the world does not
    // divide evenly, which costs nothing — a row can be revealed twice — and buys the guarantee that no
    // block is ever partly off the map.
    //
    // Longitude needs no such care because it WRAPS, but it does need centring: column 0 of the grid is
    // the block straddling the middle column, so a survey starts in the middle and runs right.
    // ============================================================================================

    /// How many bands of `bc` rows this world is divided into.
    public static int BandCount(CelestialBody b, int bc)
        => Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, MapMetrics.SurfH(b)) / (float)Mathf.Max(1, bc)));

    /// The top row of band `i`. Always in [0, h - bc], so the band is always wholly on the map.
    public static int BandY(CelestialBody b, int bc, int i)
    {
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        bc = Mathf.Clamp(bc, 1, h);
        int ny = BandCount(b, bc);
        if (ny <= 1) return 0;
        return Mathf.Clamp(Mathf.RoundToInt(i * (h - bc) / (float)(ny - 1)), 0, h - bc);
    }

    /// Blocks across one row of this world, for a ship with this block size.
    public static int BlocksAcross(CelestialBody b, int blockCells)
        => Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, MapMetrics.SurfW(b)) / (float)Mathf.Max(1, blockCells)));

    /// The leftmost column of block 0 — the block straddling the middle of the map.
    public static int ColOrigin(int w, int bc) => ((w / 2 - bc / 2) % w + w) % w;

    /// Which block along the row this column falls in: 0 at the centre, running right and wrapping.
    public static int ColBlock(int w, int bc, int x)
    {
        bc = Mathf.Max(1, bc);
        int d = ((x - ColOrigin(w, bc)) % w + w) % w;
        return d / bc;
    }

    // ============================================================================================
    // WHICH WAY IT WORKS OUTWARD, AND WHY IT IS A COIN FLIP
    //
    // A survey starts on the middle band, works right, wraps, and then takes the band above or below —
    // "50/50 as to which", and alternating from there. The coin is the WORLD's, not the moment's: it
    // comes out of the terrain seed, so a given planet always surveys the same way round however many
    // times it is reloaded, and two planets in the same system do not move in lockstep.
    //
    // Deriving it from the seed rather than storing it also means it costs nothing to save and cannot
    // come back different, which for anything that decides what is already revealed is the difference
    // between a quirk and a bug.
    // ============================================================================================
    public static bool UpFirst(CelestialBody b)
        => b != null && ((Mathf.RoundToInt(b.terrainSeed * 131f) ^ b.id) & 1) == 0;

    /// This world's per-row fills, sized to its grid and seeded from `explorationProgress` if it has
    /// none — which is what carries an older save, and what survives a sandbox resize.
    public static float[] Rows(CelestialBody b)
    {
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        if (b.surveyRows != null && b.surveyRows.Length == h) return b.surveyRows;

        var rows = new float[h];
        SeedRows(rows, h, b.explorationProgress);
        b.surveyRows = rows;
        return rows;
    }

    /// Lay `progress` out over the rows in running order: the first rows full, one part-done, the rest
    /// empty. The same shape the survey itself would have produced, so a converted save looks like a
    /// survey caught mid-sweep rather than like a wash over the whole map.
    static void SeedRows(float[] rows, int h, float progress)
    {
        var order = RowOrder(h);
        float done = Mathf.Clamp01(progress) * h;
        for (int i = 0; i < order.Length; i++)
            rows[order[i]] = Mathf.Clamp01(done - i);
    }

    /// Keep `explorationProgress` — which everything else in the game reads — equal to the average of
    /// the rows, so there is still exactly one number that decides when a world is surveyed.
    public static void SyncAggregate(CelestialBody b)
    {
        var rows = Rows(b);
        float sum = 0f;
        for (int i = 0; i < rows.Length; i++) sum += rows[i];
        b.explorationProgress = Mathf.Clamp01(sum / Mathf.Max(1, rows.Length));
    }

    /// Where in this world's band order a given band sits: 0 is the middle, then alternately one to
    /// the side the world's coin picked and one to the other.
    public static int BandRank(CelestialBody b, int bands, int band)
    {
        int d = band - bands / 2;
        if (d == 0) return 0;
        bool up = UpFirst(b);
        // The favoured side takes the odd ranks, the other the even ones.
        return (d > 0) == up ? 2 * d - 1 : 2 * Mathf.Abs(d);
    }

    /// Every band of this world, in the order a survey works them.
    ///
    /// Takes a buffer. It USED to allocate one when handed null, and that was a genuine disaster
    /// rather than an untidiness: the call chain from the fog renderer ran
    /// ReachedGround -> RowBlockCells -> BandForShip -> BandOrder, and ReachedGround is asked once per
    /// PIXEL. On a 640x320 gas giant, at eight repaints a second, that is three and a half MILLION
    /// array allocations per second to answer a question whose answer is the same for every pixel on
    /// the row. The block rework existed to stop the survey lagging; this would have made it lag
    /// considerably worse, and only on the largest worlds — which is to say, exactly where it was
    /// already worst and hardest to notice a regression.
    ///
    /// The buffer is now required. See BandOrderBuffer for the one every caller uses.
    static int[] BandOrder(CelestialBody b, int bands, int[] into)
    {
        var order = (into != null && into.Length >= bands) ? into : new int[bands];
        int n = 0;
        int cy = bands / 2;
        bool up = UpFirst(b);
        if (n < bands) order[n++] = cy;
        for (int d = 1; n < bands && d <= bands; d++)
        {
            int first = up ? cy + d : cy - d;
            int second = up ? cy - d : cy + d;
            if (first >= 0 && first < bands && n < bands) order[n++] = first;
            if (second >= 0 && second < bands && n < bands) order[n++] = second;
        }
        return order;
    }

    /// The band this ship is working: the Nth unfinished band in running order, where N is the ship's
    /// place among the ships surveying this world. Sorted by id so the assignment is stable frame to
    /// frame — two ships must not swap bands every tick and leave two half-swept latitudes.
    ///
    /// Returns -1 when every band is done, or when this ship is not surveying here.
    public static int BandForShip(CelestialBody b, Unit u, int blockCells)
    {
        if (b?.units == null || u == null) return -1;

        int bands = BandCount(b, blockCells);

        int slot = 0;
        foreach (var other in b.units)
        {
            if (other == null || other.status != UnitStatus.Exploring) continue;
            if (other == u) break;
            if (other.id < u.id) slot++;
        }

        var rows = Rows(b);
        var order = BandOrder(b, bands, BandOrderBuffer(bands));
        int seen = 0, last = -1;
        for (int i = 0; i < order.Length; i++)
        {
            if (BandFill(b, rows, order[i], blockCells) >= 1f) continue;
            last = order[i];
            if (seen == slot) return order[i];
            seen++;
        }

        // More ships than unfinished bands: everyone piles onto the last one rather than idling.
        return last;
    }

    /// A scratch buffer for band orders, grown on demand.
    ///
    /// Safe because every use is a single synchronous read-and-discard inside one method — nothing
    /// holds a band order across a call to anything that could ask for another one.
    static int[] bandOrderBuf = new int[64];

    static int[] BandOrderBuffer(int bands)
    {
        if (bandOrderBuf.Length < bands) bandOrderBuf = new int[Mathf.NextPowerOfTwo(bands)];
        return bandOrderBuf;
    }

    /// How far through a band the survey has got — the LEAST-done row in it, so a band only counts as
    /// finished once every row of it is.
    /// Every band is exactly blockCells tall and wholly on the map now, so this walks BandY..+bc with
    /// no partial-band case to handle — that stub band is what BandY exists to remove.
    static float BandFill(CelestialBody b, float[] rows, int band, int blockCells)
    {
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        int bc = Mathf.Clamp(blockCells, 1, h);
        int y0 = BandY(b, bc, band);
        float f = 1f;
        for (int y = y0; y < y0 + bc && y < h; y++)
            if (y >= 0 && y < rows.Length) f = Mathf.Min(f, rows[y]);
        return f;
    }

    /// Advance this ship's block. One block is 1/blocksAcross of a row, so a ship sitting on a block
    /// for `BlockSeconds` moves exactly that much.
    ///
    /// The rows of the band move TOGETHER and are only ever raised, never lowered. That is what keeps
    /// two ships with different block sizes from fighting: the slower one simply finds ground already
    /// uncovered and moves past it.
    public static void AdvanceGround(CelestialBody b, Unit u, float dt)
    {
        if (b == null || u == null || dt <= 0f) return;

        int blockCells = BlockCells(b, u);
        int band = BandForShip(b, u, blockCells);
        if (band < 0) return;

        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        int bc = Mathf.Clamp(blockCells, 1, h);
        int across = BlocksAcross(b, bc);
        var rows = Rows(b);

        // HeadSpeed, not Heads. The FLOAT, so blocks * dwell / speed closes exactly on SurveySeconds —
        // rounding here is what let a scout finish a world faster than a science ship. Heads() is the
        // integer, and it is only ever used to decide how many markers to draw.
        float step = dt * HeadSpeed(b, u) / (BlockSeconds(b, u) * across);
        float f = Mathf.Clamp01(BandFill(b, rows, band, bc) + step);

        int y0 = BandY(b, bc, band);
        for (int y = y0; y < y0 + bc && y < h; y++)
            if (y >= 0 && y < rows.Length) rows[y] = Mathf.Max(rows[y], f);

        SyncAggregate(b);
    }

    /// Has the ground survey uncovered this cell?
    ///
    /// WHOLE BLOCKS ONLY. The fractional part of a row fill is the ship's progress through the block it
    /// is standing on, and that block is still under cloud — it is the thing being uncovered, not
    /// something already uncovered. Reading the fill directly, as this used to, would reveal the
    /// working block the instant the ship arrived on it and leave the fading cloud with nothing under
    /// it to hide.
    public static bool ReachedGround(CelestialBody b, int x, int y)
        => ReachedAt(b, x, y, RowBlockCells(b, y));

    // ============================================================================================
    // THE PER-PIXEL FAST PATH
    //
    // BuildFogTexture asks "is this cell uncovered" once for every cell on the world, and the honest
    // answer needs to know how wide a block is on that ROW — which depends on which ship is working
    // that band, which means walking the unit list. Asked per pixel, that is the unit list walked two
    // hundred thousand times to produce three hundred and twenty distinct answers.
    //
    // Worse, it used to ALLOCATE on the way: BandForShip asked BandOrder for a fresh array every
    // time. On a 640x320 gas giant at eight repaints a second that is three and a half million array
    // allocations per second — and the whole point of the block rework was to stop the survey
    // lagging. It would have lagged considerably worse, and only on the biggest worlds, which is
    // exactly where it was already worst and where a regression is hardest to spot.
    //
    // So the renderer resolves every row's block size ONCE into a buffer it owns, and then answers
    // per pixel out of the array. The buffer belongs to the caller rather than being a cache here, on
    // purpose: the fog is built for the host planet AND for every open moon pane, and a single static
    // cache would thrash between them and be wrong in precisely the case nobody tests — a moon pane
    // open beside its planet.
    // ============================================================================================

    /// Resolve every row's block size in one pass. Reuses `into` when it is already the right size.
    public static int[] RowBlocks(CelestialBody b, int[] into)
    {
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        if (into == null || into.Length != h) into = new int[h];

        int fallback = DefaultBlockCells(b);
        for (int y = 0; y < h; y++) into[y] = fallback;

        if (b?.units != null)
        {
            foreach (var u in b.units)
            {
                if (u == null || u.status != UnitStatus.Exploring) continue;
                int bc = BlockCells(b, u);
                int band = BandForShip(b, u, bc);
                if (band < 0) continue;
                int y0 = band * bc;
                for (int y = y0; y < y0 + bc && y < h; y++) if (y >= 0) into[y] = bc;
            }
        }
        return into;
    }

    /// ReachedGround answered from a pre-resolved row table. Same result, no unit walk, no allocation.
    public static bool ReachedGround(CelestialBody b, int x, int y, int[] rowBlocks)
        => ReachedAt(b, x, y, (rowBlocks != null && y >= 0 && y < rowBlocks.Length)
                              ? rowBlocks[y] : DefaultBlockCells(b));

    /// The one implementation both forms use, so a block boundary cannot fall in one place for the
    /// renderer and somewhere else for a tooltip.
    static bool ReachedAt(CelestialBody b, int x, int y, int blockCells)
    {
        if (b == null) return false;
        if (GameMode.DevMode || b.Surveyed) return true;

        var rows = Rows(b);
        if (y < 0 || y >= rows.Length) return false;
        if (rows[y] >= 1f) return true;

        int bc = Mathf.Max(1, blockCells);
        int across = BlocksAcross(b, bc);
        int done = Mathf.FloorToInt(rows[y] * across + 1e-4f);
        int w = Mathf.Max(1, MapMetrics.SurfW(b));

        // ColBlock, not ColRank / bc. The two differ by half a block: the grid is anchored so that
        // block 0 STRADDLES the middle column rather than starting at it, which is what makes the first
        // square a survey draws sit centred on the map instead of hanging off the middle to the right.
        return ColBlock(w, bc, x) < done;
    }

    /// The block size in force on a given row — that of whichever ship is working it, or the default
    /// for this world when nobody is.
    ///
    /// Rows have to agree with themselves about how wide a block is or `ReachedGround` and the veil
    /// would disagree about where the boundary falls, and the seam would show as a stripe of cloud on
    /// ground that is already mapped.
    public static int RowBlockCells(CelestialBody b, int y)
    {
        if (b?.units != null)
        {
            foreach (var u in b.units)
            {
                if (u == null || u.status != UnitStatus.Exploring) continue;
                int bc = BlockCells(b, u);
                int band = BandForShip(b, u, bc);
                if (band < 0) continue;
                int y0 = BandY(b, bc, band);
                if (y >= y0 && y < y0 + bc) return bc;
            }
        }
        return DefaultBlockCells(b);
    }

    /// The block size for a world nobody is currently surveying — a plain scout's, which is what
    /// already-mapped ground was most likely uncovered at and is the least surprising fallback.
    public static int DefaultBlockCells(CelestialBody b)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        // A plain scout, at the literal five units it surveys with. No per-world scaling any more — see
        // the header on BlockCells.
        return Mathf.Clamp(5, 2, Mathf.Max(2, Mathf.Min(w, h)));
    }

    // ============================================================================================
    // THE BLOCK A SHIP IS STANDING ON
    //
    // What the white marker frames, what the thinning cloud covers, and the only thing on the map that
    // changes between one block and the next. Handed out as a small struct rather than answered per
    // pixel, because the marker is drawn as one rectangle over the map — a pulsating border baked into
    // the fog texture would mean rebuilding and re-uploading that texture every frame, which is the
    // cost this whole rework exists to remove.
    // ============================================================================================

    public struct Block
    {
        public int x0, y0, w, h;   // cell rectangle
        public float frac;         // 0 just arrived, 1 about to move on
    }

    /// Every block being worked on this world right now, one per surveying ship. Returns how many were
    /// written into `into`.
    public static int ActiveBlocks(CelestialBody b, Block[] into)
    {
        if (b == null || into == null || b.units == null || b.Surveyed || GameMode.DevMode) return 0;

        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        var rows = Rows(b);
        int n = 0;

        foreach (var u in b.units)
        {
            if (n >= into.Length) break;
            if (u == null || u.status != UnitStatus.Exploring) continue;

            int bc = BlockCells(b, u);
            int band = BandForShip(b, u, bc);
            if (band < 0) continue;

            int across = BlocksAcross(b, bc);
            float fill = BandFill(b, rows, band, bc);
            if (fill >= 1f) continue;

            float pos = fill * across;
            int done = Mathf.FloorToInt(pos + 1e-4f);
            if (done >= across) continue;

            int y0 = BandY(b, bc, band);
            int origin = ColOrigin(w, bc);
            int heads = Heads(b, u);

            // ONE RECT PER HEAD. A ship on a large world works several adjacent blocks at once, and the
            // marker has to show all of them or the ground would uncover in places nothing is drawn.
            // They run RIGHT from the block currently being finished, in the same direction the fill
            // advances, so the leading edge of the marker is the leading edge of the survey.
            for (int k = 0; k < heads && n < into.Length && done + k < across; k++)
            {
                into[n++] = new Block
                {
                    // The grid is anchored on the middle column, so a block index maps back to a real
                    // column by walking right from that origin — not from x = 0, which is what made the
                    // marker cross the map from the left while the ground uncovered from the middle.
                    x0 = ((origin + (done + k) * bc) % w + w) % w,
                    y0 = y0,
                    // Every band is wholly on the map now (see BandY), so a marker can no longer be
                    // taller than the ground under it — which is the "14x14 survey area was not even
                    // within the surveyable area" case.
                    w = Mathf.Min(bc, w),
                    h = Mathf.Min(bc, h - y0),
                    // Only the block actually being worked is part-done; the ones behind the head are
                    // full and the ones ahead have not started.
                    frac = k == 0 ? Mathf.Clamp01(pos - done) : 0f,
                };
            }
        }
        return n;
    }

    /// Is this cell under a ship's sweep head right now?
    public static bool BeingSurveyedGround(CelestialBody b, int x, int y)
    {
        var blocks = blockScratch;
        int n = ActiveBlocks(b, blocks);
        for (int i = 0; i < n; i++)
            if (InBlock(blocks[i], x, y, Mathf.Max(1, MapMetrics.SurfW(b)))) return true;
        return false;
    }

    /// How much cloud is still over this cell: 0 clear, 1 untouched. The working block thins across
    /// its survey time, which is what makes a block visibly resolve rather than pop.
    public static float VeilAt(CelestialBody b, int x, int y)
    {
        if (b == null) return 0f;
        if (ReachedGround(b, x, y)) return 0f;

        var blocks = blockScratch;
        int n = ActiveBlocks(b, blocks);
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        for (int i = 0; i < n; i++)
            if (InBlock(blocks[i], x, y, w)) return 1f - blocks[i].frac;
        return 1f;
    }

    /// Blocks wrap in rank space, so a block near the right edge is two runs of columns rather than one.
    static bool InBlock(Block blk, int x, int y, int w)
    {
        if (y < blk.y0 || y >= blk.y0 + blk.h) return false;
        int dx = ((x - blk.x0) % w + w) % w;
        return dx < blk.w;
    }

    // One per world; a world with more than eight ships surveying it at once is not a case worth
    // allocating for, and the ninth simply does not draw a marker.
    /// Eight ships times MaxHeads rects each. It was a flat 8 back when one ship meant one rectangle;
    /// a ship on a large world now draws up to MaxHeads of them, and undersizing this would silently
    /// drop the trailing markers off the last ship rather than the ninth ship's marker — a much harder
    /// thing to notice than a missing ship.
    static readonly Block[] blockScratch = new Block[8 * MaxHeads];

    // ---- Packing, for the save ------------------------------------------------------------------

    /// The row fills as a compact string. Quantised to a byte each and run-length encoded, which is
    /// near-free: a survey in progress is a run of finished rows, one partial, and a run of empty ones.
    public static string PackRows(CelestialBody b)
    {
        if (b?.surveyRows == null || b.surveyRows.Length == 0) return "";
        var bytes = new byte[b.surveyRows.Length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(b.surveyRows[i] * 255f), 0, 255);
        return GridCodec.Encode(bytes);
    }

    /// Restore packed rows. Refuses a string that is not this world's height — the grid is derived from
    /// mass, and stretching one world's rows over another's map would be worse than re-seeding.
    public static void UnpackRows(CelestialBody b, string packed)
    {
        if (b == null) return;
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        var bytes = GridCodec.Decode(packed, h);
        if (bytes == null) { b.surveyRows = null; return; }   // Rows() will re-seed from the aggregate

        var rows = new float[h];
        for (int i = 0; i < h; i++) rows[i] = bytes[i] / 255f;
        b.surveyRows = rows;
    }

    /// Is this cell in the band the survey is working on RIGHT NOW — the tiles the white marker sits on?
    ///
    /// A band rather than a single cell, and it widens with the number of ships on station. One ship on
    /// a big world advances a cell every few tenths of a second, and a one-cell marker at that rate
    /// reads as a flicker rather than as a machine working; a short run of tiles reads as a sweep head.
    /// It is also the honest picture of two ships being twice as fast: the front is simply wider.
    ///
    /// NOT a real division of labour. The spec asks for several ships to work separate rows at once, and
    /// that would need per-ship row assignments and a per-row progress array in the save. This is one
    /// front, moving proportionally faster, drawn proportionally wider — the same information, at a
    /// fraction of the state.
    public static bool BeingSurveyed(CelestialBody b, int x, int y, float progress, int ships)
    {
        if (b == null || ships <= 0 || progress <= 0f || progress >= 1f) return false;

        // IN BLOCKS, like the reveal it marks. This used to measure the head in CELLS against a cell
        // rank, which put a three-cell smear somewhere in the middle of the 7x7 block actually being
        // resolved — the marker and the thing it marks were different shapes and different sizes.
        int bc = DeepBlockCells(b);
        int total = Mathf.Max(1, BlocksAcross(b, bc) * BandCount(b, bc));

        int front = Mathf.Clamp(Mathf.FloorToInt(progress * total), 0, total - 1);
        int width = Mathf.Clamp(ships, 1, MaxHeads);      // one block per ship reading the field
        int rank = BlockRank(b, x, y);

        return rank >= front && rank < front + width;
    }

    /// A stable 0..1 per cell, mixed with the world's seed so two worlds do not uncover identically.
    public static float Hash01(CelestialBody b, int x, int y)
    {
        unchecked
        {
            int seed = b != null ? (b.id * 73856093) ^ Mathf.RoundToInt(b.terrainSeed * 131f) : 0;
            uint n = (uint)(seed ^ (x * 374761393) ^ (y * 668265263));
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // ---- The read-only questions the UI asks ----------------------------------------------------

    /// Is a ship on station and actively mapping this world's ground right now?
    public static bool InProgress(CelestialBody b)
    {
        if (b?.units == null) return false;
        foreach (var u in b.units)
            if (u != null && u.status == UnitStatus.Exploring) return true;
        return false;
    }

    /// Is a research ship reading this world's indexes right now?
    public static bool ResearchInProgress(CelestialBody b)
    {
        if (b?.units == null) return false;
        foreach (var u in b.units)
            if (u != null && u.status == UnitStatus.Researching) return true;
        return false;
    }

    /// How many ships are working this world right now, at the given level. Drives how wide the active
    /// marker is drawn — more ships, more front.
    public static int ShipsOn(CelestialBody b, bool deep)
    {
        if (b?.units == null) return 0;
        var want = deep ? UnitStatus.Researching : UnitStatus.Exploring;
        int n = 0;
        foreach (var u in b.units) if (u != null && u.status == want) n++;
        return n;
    }

    /// Which surface sites a world will admit to at the level it has been read to.
    ///
    /// A level-1 survey is a MAP: it charts the things you can see by looking — ruins standing on the
    /// surface, a settlement, the shape of the ground. What it cannot do is tell you that a patch of
    /// rock is an anomaly, or that a seam is an exceptional one rather than an ordinary one; both of
    /// those are a reading rather than a sighting, and readings are what the level-2 pass buys.
    ///
    /// That also gives the deep survey something to find. Before this, one orbital pass handed over
    /// every site on the world and the six index overlays were the only thing left to earn.
    public static bool SiteRevealed(CelestialBody b, POIType type)
    {
        if (b == null) return false;
        if (GameMode.DevMode || b.owner == FactionManager.Player) return true;
        if (!b.Surveyed) return false;

        if (type == POIType.Mystery || type == POIType.SpecialResource) return LevelOf(b) >= 2;
        return true;
    }

    /// May the player open this world's Surface View at all?
    ///
    /// The map unlocks when a ship STARTS work, not when it finishes: the blacked-out grid filling in is
    /// the thing worth watching, and locking the window until the survey is done would hide the entire
    /// mechanic behind its own completion. Before that there is genuinely nothing to show — not even the
    /// grid's shape, which is a fact about a world nobody has been to.
    public static bool MapUnlocked(CelestialBody b)
    {
        if (b == null) return false;
        if (GameMode.DevMode) return true;
        if (b.owner == FactionManager.Player || b.settled || b.Surveyed) return true;
        return b.explorationProgress > 0f || InProgress(b);
    }
}
