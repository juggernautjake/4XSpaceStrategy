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

    /// Has the survey reached this cell yet, at `progress` through the current sweep?
    ///
    /// The single-front form, for the LEVEL-2 passes. Level 1 asks ReachedGround instead.
    public static bool Reached(CelestialBody b, int x, int y, float progress)
    {
        if (progress >= 1f) return true;
        if (progress <= 0f) return false;
        return CellOrder(b, x, y) < progress;
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

    /// How many cells across a ship's block is, before the world's scale is applied. The technology
    /// hook: every tier of Empire Tech widens the patch a survey can take in one bite.
    public static int SurveyUnits(Unit u)
    {
        // A science hull is the only thing that can run a level-2 survey at all, and it leads at level
        // 1 too — see the Rate() block in UnitType. Seven against five is a 96% bigger patch.
        int baseUnits = (u?.Info != null && u.Info.canResearch) ? 7 : 5;
        return baseUnits + Mathf.Clamp(EmpireTech.Level - 1, 0, 6);
    }

    /// Seconds a ship spends on one block. Lower is better, so technology divides it.
    public static float BlockSeconds(Unit u)
    {
        float baseSeconds = (u?.Info != null && u.Info.canResearch) ? 3.5f : 4.0f;

        // The hull's own survey rate, on a deliberately shallow curve. Applying `surveyRate` directly
        // would make a Science Vessel more than three times as fast as a scout ON TOP of its bigger
        // block, and the block is already where the class difference is meant to show. This keeps the
        // ladder — a Mk III beats a Mk I — without letting it swamp the headline numbers.
        float rate = Mathf.Max(0.05f, u?.Info.surveyRate ?? 1f);
        return Mathf.Max(0.25f, baseSeconds / ((0.6f + 0.4f * rate) * TechSpeedMultiplier));
    }

    /// How many blocks a scout should need to cover this whole world.
    ///
    /// Grows with area but slowly — the same sub-linear shape `ChargedCells` uses and for the same
    /// reason. Four blocks is the floor so the smallest moon is still a few visible steps rather than
    /// one flash, forty the ceiling so the largest gas giant is minutes rather than an afternoon.
    static float TargetBlocks(CelestialBody b)
    {
        float cells = Mathf.Max(1, MapMetrics.SurfW(b) * MapMetrics.SurfH(b));
        return Mathf.Clamp(7f * Mathf.Pow(cells / 800f, 0.32f), 8f, 40f);
    }

    /// How many cells one survey unit is worth on this world. 1 on a small moon, and larger the bigger
    /// the world gets — see the header for why this cannot simply be 1 everywhere.
    public static float CellsPerUnit(CelestialBody b)
    {
        float cells = Mathf.Max(1, MapMetrics.SurfW(b) * MapMetrics.SurfH(b));
        float scoutBlock = Mathf.Sqrt(cells / TargetBlocks(b));      // cells across, for a 5-unit hull
        return Mathf.Max(0.2f, scoutBlock / 5f);
    }

    /// The side of this ship's block on this world, in cells. Never smaller than two — a one-cell
    /// "block" is the per-cell crawl this replaced.
    public static int BlockCells(CelestialBody b, Unit u)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        int s = Mathf.RoundToInt(SurveyUnits(u) * CellsPerUnit(b));
        return Mathf.Clamp(s, 2, Mathf.Max(2, Mathf.Min(w, h)));
    }

    /// Blocks across one row of this world, for a ship with this block size.
    public static int BlocksAcross(CelestialBody b, int blockCells)
        => Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, MapMetrics.SurfW(b)) / (float)Mathf.Max(1, blockCells)));

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
    static int[] BandOrder(CelestialBody b, int bands, int[] into)
    {
        var order = (into != null && into.Length == bands) ? into : new int[bands];
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

        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        int bands = Mathf.Max(1, Mathf.CeilToInt(h / (float)Mathf.Max(1, blockCells)));

        int slot = 0;
        foreach (var other in b.units)
        {
            if (other == null || other.status != UnitStatus.Exploring) continue;
            if (other == u) break;
            if (other.id < u.id) slot++;
        }

        var rows = Rows(b);
        var order = BandOrder(b, bands, null);
        int seen = 0, last = -1;
        for (int i = 0; i < order.Length; i++)
        {
            if (BandFill(rows, order[i], blockCells, h) >= 1f) continue;
            last = order[i];
            if (seen == slot) return order[i];
            seen++;
        }

        // More ships than unfinished bands: everyone piles onto the last one rather than idling.
        return last;
    }

    /// How far through a band the survey has got — the LEAST-done row in it, so a band only counts as
    /// finished once every row of it is.
    static float BandFill(float[] rows, int band, int blockCells, int h)
    {
        int y0 = band * blockCells;
        float f = 1f;
        for (int y = y0; y < y0 + blockCells && y < h; y++)
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
        int across = BlocksAcross(b, blockCells);
        var rows = Rows(b);

        float step = dt / (BlockSeconds(u) * across);
        float f = Mathf.Clamp01(BandFill(rows, band, blockCells, h) + step);

        int y0 = band * blockCells;
        for (int y = y0; y < y0 + blockCells && y < h; y++)
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
    {
        if (b == null) return false;
        if (GameMode.DevMode || b.Surveyed) return true;

        var rows = Rows(b);
        if (y < 0 || y >= rows.Length) return false;
        if (rows[y] >= 1f) return true;

        int across = BlocksAcross(b, RowBlockCells(b, y));
        int done = Mathf.FloorToInt(rows[y] * across + 1e-4f);
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        return ColRank(w, x) / Mathf.Max(1, RowBlockCells(b, y)) < done;
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
                if (y >= band * bc && y < (band + 1) * bc) return bc;
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
        int s = Mathf.RoundToInt(5f * CellsPerUnit(b));
        return Mathf.Clamp(s, 2, Mathf.Max(2, Mathf.Min(w, h)));
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
            float fill = BandFill(rows, band, bc, h);
            if (fill >= 1f) continue;

            float pos = fill * across;
            int done = Mathf.FloorToInt(pos + 1e-4f);
            if (done >= across) continue;

            // ColRank runs right from the middle column and wraps, so the block's left edge has to be
            // mapped back out of rank space — otherwise the marker walks the map from the left while
            // the ground uncovers from the middle.
            int rank0 = done * bc;
            int x0 = ((rank0 + w / 2) % w + w) % w;

            into[n++] = new Block
            {
                x0 = x0,
                y0 = band * bc,
                w = Mathf.Min(bc, w),
                h = Mathf.Min(bc, h - band * bc),
                frac = Mathf.Clamp01(pos - done),
            };
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
    static readonly Block[] blockScratch = new Block[8];

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

        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        int cells = Mathf.Max(1, w * h);

        int front = Mathf.Clamp(Mathf.FloorToInt(progress * cells), 0, cells - 1);
        int width = Mathf.Clamp(ActiveBandCells * ships, ActiveBandCells, w);
        int rank = CellRank(b, x, y);

        return rank >= front && rank < front + width;
    }

    /// How many cells one ship's marker covers. Chosen to read as a sweep head at map zoom rather than
    /// as a single blinking tile.
    const int ActiveBandCells = 3;

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
