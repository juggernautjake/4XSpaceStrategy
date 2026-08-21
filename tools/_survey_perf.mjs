import fs from 'node:fs';
const p = 'Assets/Scripts/Systems/Survey.cs';
let s = fs.readFileSync(p, 'utf8');
const before = s;

// 1. BandForShip uses a shared buffer rather than asking BandOrder to allocate.
s = s.replace(`        var rows = Rows(b);
        var order = BandOrder(b, bands, null);
        int seen = 0, last = -1;`,
`        var rows = Rows(b);
        var order = BandOrder(b, bands, BandOrderBuffer(bands));
        int seen = 0, last = -1;`);

// 2. The shared buffer itself.
s = s.replace(`    /// How far through a band the survey has got — the LEAST-done row in it, so a band only counts as
    /// finished once every row of it is.`,
`    /// A scratch buffer for band orders, grown on demand and never handed out twice at once.
    ///
    /// Safe because every use is a single synchronous read-and-discard inside one method — nothing
    /// holds a band order across a call to anything else that could ask for one. If that ever stops
    /// being true this wants to become a per-call buffer again, not a lock.
    static int[] bandOrderBuf = new int[64];

    static int[] BandOrderBuffer(int bands)
    {
        if (bandOrderBuf.Length < bands) bandOrderBuf = new int[Mathf.NextPowerOfTwo(bands)];
        return bandOrderBuf;
    }

    /// How far through a band the survey has got — the LEAST-done row in it, so a band only counts as
    /// finished once every row of it is.`);

// 3. A fast path for the fog renderer: resolve every row's block size ONCE, then answer per pixel
//    from the array.
s = s.replace(`    /// The block size in force on a given row — that of whichever ship is working it, or the default
    /// for this world when nobody is.`,
`    // ============================================================================================
    // THE PER-PIXEL FAST PATH
    //
    // BuildFogTexture asks "is this cell uncovered" once for every cell on the world, and the honest
    // answer needs to know how wide a block is on that ROW — which depends on which ship is working
    // that band, which means walking the unit list. Per pixel, that is the unit list walked two
    // hundred thousand times to produce three hundred and twenty distinct answers.
    //
    // So the renderer resolves the rows once into a buffer it owns and then answers from the array.
    // The buffer is the caller's rather than a cache here on purpose: the fog is built for the host
    // planet AND for every open moon pane, and a single static cache would thrash between them and be
    // wrong in exactly the case nobody tests — a moon open beside its planet.
    // ============================================================================================

    /// Resolve every row's block size in one pass. Grows `into` if it is the wrong size.
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

    /// ReachedGround, answered from a pre-resolved row table. Same result, no unit walk.
    public static bool ReachedGround(CelestialBody b, int x, int y, int[] rowBlocks)
    {
        if (b == null) return false;
        if (GameMode.DevMode || b.Surveyed) return true;

        var rows = Rows(b);
        if (y < 0 || y >= rows.Length) return false;
        if (rows[y] >= 1f) return true;

        int bc = (rowBlocks != null && y < rowBlocks.Length) ? rowBlocks[y] : DefaultBlockCells(b);
        bc = Mathf.Max(1, bc);

        int across = BlocksAcross(b, bc);
        int done = Mathf.FloorToInt(rows[y] * across + 1e-4f);
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        return ColRank(w, x) / bc < done;
    }

    /// The block size in force on a given row — that of whichever ship is working it, or the default
    /// for this world when nobody is.
    ///
    /// The SLOW form, for one-off questions like a tooltip. Anything asking per cell wants RowBlocks
    /// and the overload above.`);

// 4. Route the single-cell ReachedGround through the same code so the two cannot diverge.
s = s.replace(`    public static bool ReachedGround(CelestialBody b, int x, int y)
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
    }`,
`    public static bool ReachedGround(CelestialBody b, int x, int y)
    {
        if (b == null) return false;
        if (GameMode.DevMode || b.Surveyed) return true;

        // One row's worth of work, and it shares its body with the fast path so the two can never
        // disagree about where a block boundary falls.
        oneRow[0] = RowBlockCells(b, y);
        var rows = Rows(b);
        if (y < 0 || y >= rows.Length) return false;
        if (rows[y] >= 1f) return true;

        int bc = Mathf.Max(1, oneRow[0]);
        int across = BlocksAcross(b, bc);
        int done = Mathf.FloorToInt(rows[y] * across + 1e-4f);
        int w = Mathf.Max(1, MapMetrics.SurfW(b));
        return ColRank(w, x) / bc < done;
    }

    static readonly int[] oneRow = new int[1];`);

fs.writeFileSync(p, s);
console.log('changed:', s !== before);
console.log('buffer:', s.includes('BandOrderBuffer(bands)'));
console.log('fastpath:', s.includes('public static int[] RowBlocks(CelestialBody b, int[] into)'));
console.log('no null BandOrder:', !s.includes('BandOrder(b, bands, null)'));
