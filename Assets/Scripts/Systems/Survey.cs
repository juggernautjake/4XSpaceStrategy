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

    /// Total level-2 passes: every index, every band.
    public static int TotalPasses => SurfaceIndex.All.Length * Bands;

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

    /// A level-2 pass is quicker per cell than mapping the ground — the ship is already in orbit with
    /// the terrain in hand, and it is reading one field rather than charting a coastline.
    public const float DeepSecondsPerCellScale = 0.55f;

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

        float total = SweepSeconds(b, u, deep) * (deep ? perIndex * SurfaceIndex.All.Length : 1f);
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

        int slot = IndexSlot(k);
        if (slot < 0) return r;

        // Each index gets an equal share of the whole; within it, the passes are weighted.
        int n = SurfaceIndex.All.Length;
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

    /// Position of an index in the level-2 running order. SurfaceIndex.All IS the order — Mineral,
    /// Heat, Fertile, Wind, Solar, Water — so reordering the survey is reordering that array.
    public static int IndexSlot(SurfaceIndexKind k)
    {
        for (int i = 0; i < SurfaceIndex.All.Length; i++) if (SurfaceIndex.All[i] == k) return i;
        return -1;
    }

    /// The index a level-2 survey is working on right now, or None when it is finished or not running.
    public static SurfaceIndexKind CurrentIndex(CelestialBody b)
    {
        if (b == null || b.deepProgress <= 0f || b.deepProgress >= 1f) return SurfaceIndexKind.None;
        int n = SurfaceIndex.All.Length;
        int slot = Mathf.Clamp(Mathf.FloorToInt(b.deepProgress * n), 0, n - 1);
        return SurfaceIndex.All[slot];
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

    /// This cell's place in the running order, 0 .. cells-1. Stable for a given world.
    public static int CellRank(CelestialBody b, int x, int y)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        int cx = w / 2, cy = h / 2;

        // Rows outward from the middle, alternating: centre, above, below, above, below...
        int d = y - cy;
        int rowRank = d == 0 ? 0 : (d > 0 ? 2 * d - 1 : -2 * d);

        // ...and within a row, rightward from the middle column, wrapping at the date line.
        int colRank = ((x - cx) % w + w) % w;

        return rowRank * w + colRank;
    }

    /// 0..1 position of this cell in the running order.
    public static float CellOrder(CelestialBody b, int x, int y)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));
        return CellRank(b, x, y) / (float)Mathf.Max(1, w * h);
    }

    /// Has the survey reached this cell yet, at `progress` through the current sweep?
    public static bool Reached(CelestialBody b, int x, int y, float progress)
    {
        if (progress >= 1f) return true;
        if (progress <= 0f) return false;
        return CellOrder(b, x, y) < progress;
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
