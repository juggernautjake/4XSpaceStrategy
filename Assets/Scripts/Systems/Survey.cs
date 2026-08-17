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
        // A level-2 survey is six indexes of three passes, so its "whole" is that many sweeps of the map.
        float total = SweepSeconds(b, u, deep) * (deep ? TotalPasses : 1);
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

        float stage = Mathf.Clamp01(b.deepProgress) * TotalPasses;   // 0 .. 18
        float mine = stage - slot * Bands;                            // this index's own share

        if (mine <= 0f) return r;                                     // not reached yet
        if (mine >= Bands) return new Reveal { started = true, pass = Bands - 1, frac = 1f, complete = true };

        int pass = Mathf.Clamp(Mathf.FloorToInt(mine), 0, Bands - 1);
        return new Reveal { started = true, pass = pass, frac = mine - pass, complete = false };
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
        int slot = Mathf.Clamp(Mathf.FloorToInt(b.deepProgress * TotalPasses / Bands), 0, SurfaceIndex.All.Length - 1);
        return SurfaceIndex.All[slot];
    }

    // ---- Which cells have been reached ----------------------------------------------------------
    //
    // A survey reveals CELLS, and which cells it has got to has to be answerable at any moment without
    // storing a bitmask per world per index. So it is a pure function: every cell is given a fixed
    // position in the running order, and a cell is uncovered once the survey has passed it.
    //
    // The order is a diagonal sweep with hash jitter mixed in, not a plain hash. A plain hash dissolves
    // the map as television static — every part of the world uncovering at once, which reads as a
    // rendering effect. A pure sweep is a clean line crossing the planet, which reads as a wipe. The
    // mix gives a ragged front that advances across the world: a machine working its way over ground.
    //
    // Seeded off the world, so the same planet always uncovers the same way and a reload mid-survey
    // does not reshuffle what is already known.

    /// 0..1 position of this cell in the reveal order. Stable for a given world.
    public static float CellOrder(CelestialBody b, int x, int y)
    {
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        int h = Mathf.Max(1, MapMetrics.SurfH(b));

        // The sweep. Normalised so it is the same shape on every world, and tilted so it does not run
        // exactly along a row — a front parallel to the grid reads as a scanline.
        float sweep = (x / (float)w) * 0.72f + (y / (float)h) * 0.28f;

        return Mathf.Clamp01(sweep * (1f - JitterWeight) + Hash01(b, x, y) * JitterWeight);
    }

    /// How much of the reveal order is noise rather than sweep. Enough to break the front into a ragged
    /// edge, little enough that it still visibly travels in one direction.
    const float JitterWeight = 0.34f;

    /// Has the survey reached this cell yet, at `progress` through the current sweep?
    public static bool Reached(CelestialBody b, int x, int y, float progress)
    {
        if (progress >= 1f) return true;
        if (progress <= 0f) return false;
        return CellOrder(b, x, y) < progress;
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
}
