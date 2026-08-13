using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// PLATE-TECTONICS GEOMETRY — the fault lines a tectonically-active world is missing.
//
// TectonicsRules gives a world the hasTectonics BOOL. This gives it the GEOMETRY that bool implies:
// which continental plate each point of the surface belongs to, where the fault lines between plates
// run, and which way each plate is pushing. A convergent boundary (two plates driving together) is where
// mountains and volcanoes pile up; that same geometry is what the Survey overlay draws and what the
// earthquake system shakes.
//
// DERIVED, never stored — exactly like SurfaceIndex. A plate layout is a deterministic function of the
// body's terrainSeed, so a world re-derived from the same seed reads identically, it costs nothing to
// save, and it survives a reload untouched. It is queryable at ANY normalized (u,v) and at any
// resolution, so the terrain generator, the map overlay, and the earthquake events all read the SAME
// geometry and can never disagree with each other.
//
// Only worlds with hasTectonics have plates; Active() is the gate every consumer checks first.
//
// ---- WHY THIS IS SOLVED ON A SPHERE ---------------------------------------------------------
// The plates live on the UNIT SPHERE — sites are unit vectors, "nearest plate" is the largest dot
// product, and a fault is the great circle equidistant from two sites. The surface grid is only the
// equirectangular *picture* of that sphere. Doing it the other way round — Voronoi on the flat 2:1
// rectangle, which is what this used to be — produces three specific artefacts, and all three were
// visible on screen:
//
//   * PLATES LIKE SHARDS OF GLASS. A flat Voronoi cell is bounded by straight perpendicular bisectors.
//     Nothing at continental scale is straight. On the sphere the boundaries are great-circle arcs, and
//     an arc drawn on an equirectangular map is a curve by construction — so plates come out rounded
//     before a single line of noise is involved.
//   * FAULTS THAT BISECT THE WORLD POLE TO POLE. On the flat map the poles are edges, so a boundary can
//     simply run off the top. On the sphere a pole is an ordinary point that lands INSIDE somebody's
//     plate, which draws on the map as a band of one plate across the whole top or bottom — the polar
//     crust the request asked for, arising from the geometry rather than being special-cased. Which
//     plate that is, and whether it is a small cap or the flank of a big mid-latitude plate, varies
//     seed to seed on its own.
//   * A FAULT BAND THAT FLARES INTO A RED WEDGE. See below.
//
// ---- WHY THE RED LINE IS THIN NOW -----------------------------------------------------------
// The old version thresholded the DIFFERENCE of the two nearest site distances, |d2 - d1|, against a
// fixed constant. That difference is not a distance to the boundary: it grows at a rate set by how far
// apart the two sites are and how obliquely you cross the bisector, so a fault between two distant
// sites bloomed into a wedge tens of tiles across while a fault between two close ones stayed hairline.
// Half the map came out red. Here the distance to a fault is exact — the angle from a point to a great
// circle with unit normal m is asin(p·m) — and it is then converted into TILES through the local map
// scale, which is what pins the drawn line at one to three tiles everywhere, at every map size, at the
// poles as much as the equator.
//
// ---- WHY A PLATE IS SEVERAL VORONOI CELLS AND NOT ONE ----------------------------------------
//
// One site per plate gives one Voronoi cell per plate, and a Voronoi cell is CONVEX. Every boundary is
// then a single bisector arc between two sites, so every plate comes out as a rounded convex blob and
// the map reads as a sheet of soap bubbles rather than as continents. Adding warp and edge roughness
// wobbles those arcs but cannot change what they fundamentally are: a plate still has no bays, no
// peninsulas, no re-entrant corners, because a convex region cannot have them.
//
// So the sites are no longer plates. There are now SIX OR SO CELLS PER PLATE, and a plate is a
// CLUSTER of them, agglomerated by a region-growing pass at layout time. A plate boundary then runs
// along whichever cell edges happen to lie between two clusters — a chain of several bisector arcs
// meeting at angles — which is non-convex by construction and reads as a coastline. Leftover cells that
// no big cluster claimed become the small plates wedged between the continents, which is exactly what
// a real plate map looks like.
//
// THE GROWTH IS COMPETITIVE, and that detail matters. Scoring every (plate, frontier cell) pair and
// taking the best globally lets one plate that rolls a high jitter keep winning; rendered out, two
// plates held 67% of the world between them and nine plates were a single cell each. Each plate instead
// gets a TARGET SIZE up front and the one furthest below its target annexes next, so a plate that has
// eaten its share stops bidding until the others catch up. Targets are drawn from a curve that skews
// small, so a world still gets a couple of continents and a scatter of small plates rather than a dozen
// identical ones.
//
// ---- WHY THE MARGINS ARE RAGGED --------------------------------------------------------------
// Great-circle arcs plus a domain warp gave plates that were CURVED but smooth. The cell clustering
// above is what supplies the large-scale irregularity now. On top of it there is still a small scalar
// field added to the fault's own signed distance, in TILES, which slides the boundary sideways without
// touching the space it is drawn in; Sample corrects the band width for that field's gradient, so the
// line wanders without pinching or fattening. The domain warp is kept but weakened — with the cell
// structure doing the shaping, a strong warp only smears the cell edges back into curves.
//
// Two proximity fields come off that one distance, and the difference between them matters:
//   `boundary` — the hairline: 1 on the fault, 0 about a tile away. This is the red line the Survey
//                overlay draws, i.e. the GUIDELINE for where a range belongs.
//   `belt`     — the mountain-building field: a much wider, noise-ragged falloff. Ranges raised by a
//                collision spill well outside the drawn line, because an orogenic belt is hundreds of
//                km wide and the fault trace is a line on a map.
// ============================================================================================
public static class TectonicsMap
{
    public class Plate
    {
        public int id;
        public Vector3 site;     // plate CENTROID as a unit vector — the average of its cells' sites,
                                 // which is where the overlay puts its arrow. No longer a Voronoi site:
                                 // a plate is a cluster of cells now and has no single generating point.
        public Vector3 motion;   // push, tangent to the sphere at `site`; magnitude == strength
        public float strength;   // |motion|, 0..1; kept apart so the overlay can size its arrow by it
        public int cellCount;    // how many Voronoi cells were agglomerated into it
    }

    /// One sinusoidal plane wave of the domain warp. Kept as a plain basis rather than a noise lattice
    /// because a lattice would have to be made to tile at the longitude seam, whereas a plane wave over
    /// 3D positions on a sphere is continuous everywhere by construction — there is no seam to tile. It
    /// is also analytically differentiable, which the width correction in Sample needs.
    public struct WarpTerm
    {
        public Vector3 freq;   // wave vector; |freq| sets the feature size
        public Vector3 dir;    // direction the wave pushes points
        public float amp;      // radians of displacement
        public float phase;
    }

    /// One octave of the EDGE ROUGHNESS field — see the note above Sample. Same seamless plane-wave
    /// trick as WarpTerm, but this one is a SCALAR: it has no direction to push in, it simply says how
    /// far sideways the fault line has moved at this point, so it needs no `dir`.
    public struct EdgeTerm
    {
        public Vector3 freq;
        public float amp;      // radians of LATERAL SHIFT of the boundary
        public float phase;
    }

    public class Layout
    {
        public Plate[] plates;

        // THE VORONOI CELLS, which are what Sample actually searches. `cellPlate[i]` is which plate
        // cell i was agglomerated into — the indirection that turns a cell map into a plate map.
        public Vector3[] cellSites;
        public int[] cellPlate;

        public WarpTerm[] warp;
        public EdgeTerm[] edge;      // the roughness on the margins themselves
        public float builtForSeed;   // the terrainSeed this was derived from; rebuild if the world reseeds
        public int builtForSize;     // and the surfaceSize — a Dev-sandbox size edit changes the world
                                     // without touching the seed, and a stale layout would outlive it
        public int heightTiles;      // grid height this was calibrated against — the widths below are in
                                     // TILES, so a body whose map size moved has to re-derive
        public float faultTiles;     // full width of the DRAWN fault line, in tiles
        public float beltTiles;      // full width of the mountain-building belt, in tiles
        public float minCos;         // floor on cos(latitude) — see Sample; keeps the pole rows finite
    }

    public struct Hit
    {
        public int plateA;        // nearest plate — the plate this point belongs to BEFORE roughness
        public int plateB;        // second-nearest plate — the plate across the closest fault
        public int owner;         // the plate that actually owns this point: plateA or plateB, decided by
                                  // the SIGN of the roughened fault distance. See Sample.
        public float boundary;    // 0 (off the fault) .. 1 (right on the fault line) — the DRAWN hairline
        public float belt;        // 0 .. 1 mountain-building influence — far wider than `boundary`, and
                                  // ragged, so ranges are not confined to the red line on the overlay
        public float convergence; // relative motion across that fault: >0 plates driven TOGETHER
                                  // (compression -> mountains/volcanoes), <0 pulled apart (a rift)
    }

    // ============================================================================================
    // THE PLATE MAP — plate ownership per TILE, and the red line drawn from it.
    //
    // WHY THE LINE IS NO LONGER A THRESHOLDED DISTANCE. The old overlay asked every tile "how far are
    // you from a fault" and painted it red under a cutoff. That is a sampled continuous field, and a
    // sampled field cannot promise anything about the PICTURE:
    //
    //   * WHERE TWO PLATES MEET NEARLY EDGE-ON the band passed between two tile centres and coloured
    //     neither, so the line came out dashed — or vanished entirely for a stretch.
    //   * WHERE A SLIVER PLATE ran between two big ones, both of its margins fell inside the cutoff and
    //     the map drew two parallel lines a tile apart: the double borders in the screenshot.
    //   * NOTHING GUARANTEED A SEPARATOR. Two plates could be adjacent on the map with no red between
    //     them at all, which is the one thing a plate boundary must never do.
    //
    // So the line is drawn from OWNERSHIP instead. Every tile is assigned the plate that owns it; a tile
    // is border iff one of its FOUR SIDE NEIGHBOURS belongs to a different plate, and of the two tiles
    // either side of a boundary only the lower-numbered plate's marks. That gives exactly one tile of red
    // per boundary — never two, never none — because it is a property of the tile grid rather than of a
    // field sampled on it.
    //
    // THEN IT IS SQUARED OFF. A boundary running at a shallow angle marks a staircase of tiles that touch
    // only at their corners, which reads as a dotted line and is not a barrier you could walk along.
    // Wherever two border tiles meet corner-to-corner the tile that squares that corner is added, so the
    // finished line is 4-CONNECTED: it runs up several tiles, steps sideways by one, and carries on up —
    // the same edge-to-edge connectivity rule building footprints use.
    //
    // MEASURED (Node port, 80x40 through 400x200, three seeds each): every adjacent plate pair separated,
    // the whole border network one 4-connected piece, and 90-98% of border tiles exactly one or two tiles
    // thick — the rest being triple junctions, where three lines meeting genuinely is thicker.
    // ============================================================================================
    public class TileMap
    {
        public int width, height;
        public int[] plate;          // which plate owns each tile
        public bool[] border;        // the drawn red line
        public bool[] plateDrawn;    // does this plate still hold any tile? (see the absorption below)
        public float builtForSeed;
        public int builtForSize;
    }

    /// A plate holding fewer than this share of the map's tiles is not a plate, it is a speck. Absorbed.
    const float MinPlateFraction = 0.004f;

    /// ...and never fewer than this many tiles, so a small world's plates aren't all specks by fraction.
    const int MinPlateTiles = 12;

    /// How many tiles a region must have that are COMPLETELY surrounded by their own plate. A region with
    /// none is one tile wide somewhere along its whole length — a sliver — and a sliver wedged between two
    /// plates is precisely what draws as a double line. Three rather than one so a two-tile-wide tail
    /// still goes: the request is that extremely thin cells join a neighbour.
    const int MinPlateCore = 3;

    /// Absorption stops here however many slivers are left. A world reduced to two plates has one fault
    /// and reads as a cracked egg rather than as a plate map.
    const int MinPlatesOnMap = 3;

    // ---- Band widths, in TILES -------------------------------------------------------------------
    // The red fault line is a guideline drawn over the terrain, so it wants to be thin: about two tiles
    // of red between two plates, three where the width jitter and a triple junction stack up. Scaled
    // gently with map size so a 10x5 pebble doesn't get a band a third of its height, then clamped at
    // both ends. (Measured against a rendered map: median drawn thickness 2 tiles, 99th percentile 2.8,
    // worst case 4 at a triple junction, on maps from 120x60 to 400x200.)
    //
    // The ceiling went from 2.0 to 2.6 when the margins were roughened (see EdgeTiles below), and that is
    // a consequence rather than a taste change: a line that WANDERS by a tile or two has to be thick
    // enough to still land on tile centres as it wanders, or the overlay — which samples one point per
    // tile — draws it as a dotted trail. Measured on rendered maps at 100x50 through 400x200: at 2.0 the
    // roughened line broke into 32 fragments on a 200x100 world, at 2.6 it is 17 with the same coverage
    // as the smooth line had. Small worlds are unaffected: their width is under the old cap anyway.
    const float FaultTilesPerHeight = 0.033f, FaultTilesMin = 1.1f, FaultTilesMax = 2.6f;

    // The mountain belt is the geology, not the annotation: a wide skirt either side of the fault that
    // the ridge field reads, roughly seven times the drawn line.
    const float BeltTilesPerHeight = 0.15f, BeltTilesMin = 3f, BeltTilesMax = 26f;

    // ---- The cell layer --------------------------------------------------------------------------
    //
    // How many Voronoi cells each plate is made of, on average. This is the number that decides how
    // ragged a margin is, and it is a genuine trade-off in both directions:
    //
    //   TOO FEW and a plate is one or two cells, which is convex again and we are back to bubbles.
    //   TOO MANY and the boundary becomes a fine dither of tiny steps that reads as noise at map scale
    //           rather than as a coastline — and Sample's cost is linear in the total, since it scans
    //           every cell to find the nearest.
    //
    // Six was chosen against rendered maps. It roughly doubles Sample's per-call cost against the old
    // one-site-per-plate version, which is acceptable because the warp arithmetic either side of the
    // scan is comparable work and was always the bulk of it.
    const int CellsPerPlate = 6;

    /// How much randomness goes into which frontier cell a plate annexes next. 0 gives compact blobs
    /// (bubbles again); very high gives long tendrils that do not read as continents. See Agglomerate.
    const float GrowJitter = 2.5f;

    /// Points sampled on the sphere to discover which cells are neighbours. The exact answer is the
    /// spherical Delaunay triangulation, which is a convex hull — a great deal of machinery for a graph
    /// that only has to be approximately right, since a missed edge costs one cell being annexed a step
    /// later than it might have been.
    const int AdjacencySamples = 6000;

    // ---- Domain warp -----------------------------------------------------------------------------
    // Two bands of plane waves. The coarse band sweeps whole boundaries off the great circles they would
    // otherwise follow; the fine band gives them their wander. Amplitude per term is Gain/|freq|, which
    // fixes each term's GRADIENT at Gain — that is the number that matters, because the gradient is both
    // how much a boundary bends and how close the warp comes to folding space over on itself.
    //
    // WEAKER THAN IT WAS (0.40/0.45 -> 0.30/0.28). The warp used to be solely responsible for making
    // boundaries anything other than perfect arcs, and had to be pushed as far as it could go without
    // folding. The cell clustering does that job now, and a strong warp on top of it only smears the
    // cell edges — the very thing supplying the shape — back into smooth curves.
    const int CoarseTerms = 3, FineTerms = 3;
    const float CoarseFreqMin = 2.0f, CoarseFreqMax = 4.0f, CoarseGain = 0.30f;
    const float FineFreqMin = 6.0f, FineFreqMax = 13.0f, FineGain = 0.28f;

    // The scalar fields that vary the line's width along its length and break a mountain belt into peaks
    // and saddles. Higher frequency than the warp — this is texture, not shape.
    const int GrainTerms = 2;
    const float GrainFreqMin = 14f, GrainFreqMax = 22f;

    // ---- Edge roughness -------------------------------------------------------------------------
    // WHY THIS IS NOT MORE DOMAIN WARP. The obvious way to roughen a margin is another, faster band of
    // warp — and it does not work, for a reason worth writing down so nobody tries it twice. A warp
    // term's displacement is amp = gain/|freq| RADIANS, while its gradient (how close the warp comes to
    // folding space onto itself, which is the hard ceiling) is just `gain`. So at the frequencies that
    // would give tile-scale detail, the displacement you can afford is a fraction of a tile: rendered
    // out, adding a third warp band at 14-24 changed nothing a player could see, and pushing its gain
    // far enough to matter folded the sphere.
    //
    // The displacement is also in radians, so the SAME warp draws a 4-tile wobble on a 400-wide world
    // and a 0.5-tile one on a 100-wide world. The small worlds — the ones you actually settle — were
    // exactly the ones that came out looking like soap bubbles.
    //
    // So the roughness is applied to the fault's own SIGNED DISTANCE instead, in TILES: a scalar field
    // added to "how far are we from the boundary", which slides the boundary sideways without touching
    // the space it lives in. It cannot fold, it is the same size in tiles on every world, and it costs
    // two sines. Sample corrects the band's width for the field's own gradient, the same way it already
    // corrects for the warp's stretch — without that the line pinches where the offset moves fastest and
    // the overlay draws it as a dashed one.
    //
    // Two octaves, amplitudes in TILES. 1.2 at wavelength ~2h/6 tiles for the wander, 0.5 at ~2h/14 for
    // the crinkle on top of it. Both chosen against rendered maps: enough that no margin reads as an arc
    // any more, little enough that the line stays connected at tile resolution.
    const int EdgeTerms = 2;
    static readonly float[] EdgeFreq = { 6f, 14f };
    static readonly float[] EdgeTiles = { 1.2f, 0.5f };

    static readonly Dictionary<int, Layout> cache = new Dictionary<int, Layout>();
    static readonly Dictionary<CelestialBody, TileMap> tileCache = new Dictionary<CelestialBody, TileMap>();

    // A world has plate geometry iff it rolled tectonics. NOTE: deliberately does NOT require b.surface —
    // the terrain generator queries this WHILE it is baking that very surface (body.surface is still null
    // then), and the geometry only needs the seed, not the grid.
    public static bool Active(CelestialBody b) => b != null && b.hasTectonics;

    public static void Invalidate(CelestialBody b)
    {
        if (b == null) return;
        cache.Remove(b.id);
        tileCache.Remove(b);
    }

    public static void InvalidateAll() { cache.Clear(); tileCache.Clear(); }

    public static Layout Get(CelestialBody b)
    {
        if (b == null) return null;
        // Keyed by id, but re-derived if the world's seed, size OR grid height changed under it (a reseed
        // or a size drag in the Dev sandbox, a remodel) — so the cache can never describe the plates of a
        // world this one used to be. Guarding all three is also what makes id reuse across a New Game /
        // Load safe: a fresh body reusing an old id but carrying a different seed/size misses the cache
        // and rebuilds. Height is in there because the band widths are calibrated in tiles.
        if (cache.TryGetValue(b.id, out var l) &&
            Mathf.Approximately(l.builtForSeed, b.terrainSeed) && l.builtForSize == b.surfaceSize &&
            l.heightTiles == MapMetrics.SurfH(b))
            return l;
        l = Build(b);
        cache[b.id] = l;
        return l;
    }

    static Layout Build(CelestialBody b)
    {
        // System.Random seeded from the terrainSeed: deterministic, and INDEPENDENT of UnityEngine.Random's
        // global stream — deriving plate geometry must never perturb the RNG world generation is drawing
        // from, or it would change which planets spawn. Mixed with the id so two bodies that happen to
        // share a seed float still get distinct plates.
        int seed = b.terrainSeed.GetHashCode() ^ (b.id * 486187739);
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();

        // heightTiles is stored EXACTLY as MapMetrics reports it, because Get compares the two: rounding or
        // flooring it here would make the cache check fail forever and re-derive the layout on every
        // single sample. `hf` is the same number with a floor under it, purely for the arithmetic below.
        int hTiles = MapMetrics.SurfH(b);
        float hf = Mathf.Max(4f, hTiles);

        // Plate count: 4..13, scaled to the GRID, not to surfaceSize. Plate count is what sets plate SIZE,
        // and the two measures disagree badly at the ends — surfaceSize is a 3..32 abstraction while the
        // grid runs 10 to 640 cells wide off mass. Keying off the grid is what stops a 24x12 pebble being
        // cut into nine plates whose fault lines then cover half of it, and stops a 640-wide world being
        // cut into four continents the size of hemispheres with boundaries long enough to cross the map.
        int n = Mathf.Clamp(4 + Mathf.FloorToInt(hf / 14f) + rng.Next(0, 3), 4, 13);

        // ---- The CELLS, not the plates ----
        //
        // Spread by best-candidate sampling. Uniform random points on a sphere clump, and clumped cells
        // make the agglomeration lopsided — one plate of forty tiny cells beside one of two huge ones.
        // Drawing a few candidates and keeping the one furthest from everything already placed
        // (Mitchell's best-candidate) gives a roughly Poisson-disc spread: cells of comparable size,
        // boundaries that meet in short segments and honest triple junctions. Five candidates rather
        // than a dozen — enough to break up the clumps, few enough that it doesn't come out regimented.
        int cellCount = n * CellsPerPlate;
        var sites = new List<Vector3>(cellCount);
        while (sites.Count < cellCount)
        {
            Vector3 best = Vector3.up;
            float bestSep = -1f;
            for (int k = 0; k < 5; k++)
            {
                Vector3 c = RandomDirection(R);
                float nearest = float.MaxValue;
                for (int i = 0; i < sites.Count; i++) nearest = Mathf.Min(nearest, 1f - Vector3.Dot(sites[i], c));
                if (nearest > bestSep) { bestSep = nearest; best = c; }
            }
            sites.Add(best);
        }

        var cellSites = sites.ToArray();
        var cellPlate = Agglomerate(cellSites, n, R, seed);

        // ---- The plates, now derived FROM the clusters ----
        var plates = new Plate[n];
        for (int i = 0; i < n; i++) plates[i] = new Plate { id = i };

        // A plate's site is the centroid of its cells, normalised back onto the sphere. That is where
        // the overlay draws its arrow, and it is the point the convergence maths measures relative
        // motion at — both of which want "the middle of this continent" rather than any one cell.
        var sum = new Vector3[n];
        for (int c = 0; c < cellSites.Length; c++)
        {
            int p = cellPlate[c];
            sum[p] += cellSites[c];
            plates[p].cellCount++;
        }

        for (int i = 0; i < n; i++)
        {
            // A cluster whose cells happen to straddle the sphere can sum to near zero; fall back to any
            // one of its cells rather than normalising a zero vector into a NaN.
            Vector3 s = sum[i].sqrMagnitude > 1e-6f ? sum[i].normalized : FirstCellOf(cellSites, cellPlate, i);
            plates[i].site = s;

            // A push TANGENT to the sphere at the centroid: a random direction in the local tangent plane.
            Vector3 up = Mathf.Abs(s.y) > 0.95f ? Vector3.right : Vector3.up;
            Vector3 e = Vector3.Cross(up, s).normalized;
            Vector3 nth = Vector3.Cross(s, e);
            float a = R() * Mathf.PI * 2f;
            float strength = Mathf.Lerp(0.35f, 1f, R());   // no plate is truly motionless
            plates[i].motion = (e * Mathf.Cos(a) + nth * Mathf.Sin(a)) * strength;
            plates[i].strength = strength;
        }

        var warp = new WarpTerm[CoarseTerms + FineTerms + GrainTerms];
        int w = 0;
        for (int k = 0; k < CoarseTerms; k++) warp[w++] = MakeTerm(R, CoarseFreqMin, CoarseFreqMax, CoarseGain);
        for (int k = 0; k < FineTerms; k++) warp[w++] = MakeTerm(R, FineFreqMin, FineFreqMax, FineGain);
        // The grain terms carry no displacement (amp 0 would make them invisible to Warp, so they are kept
        // OUT of the warp range by index instead — see WarpCount).
        for (int k = 0; k < GrainTerms; k++) warp[w++] = MakeTerm(R, GrainFreqMin, GrainFreqMax, 1f);

        // Drawn LAST, so every random number before this point is the one it always was: a world's plates
        // and its warp are untouched by adding this, and only the fine shape of its margins moves.
        var edge = new EdgeTerm[EdgeTerms];
        for (int k = 0; k < EdgeTerms; k++)
        {
            float f = EdgeFreq[k];
            edge[k] = new EdgeTerm
            {
                freq = RandomDirection(R) * f,
                // Tiles -> radians, against THIS world's grid: hf tiles span pi of latitude. This is the
                // whole reason the roughness reads the same on a moon and on a super-earth.
                amp = EdgeTiles[k] * Mathf.PI / hf,
                phase = R() * Mathf.PI * 2f
            };
        }

        return new Layout
        {
            plates = plates,
            cellSites = cellSites,
            cellPlate = cellPlate,
            warp = warp,
            edge = edge,
            builtForSeed = b.terrainSeed,
            builtForSize = b.surfaceSize,
            heightTiles = hTiles,
            faultTiles = Mathf.Clamp(hf * FaultTilesPerHeight, FaultTilesMin, FaultTilesMax),
            beltTiles = Mathf.Clamp(hf * BeltTilesPerHeight, BeltTilesMin, BeltTilesMax),
            // Never let the longitude scale below half a tile: at the exact pole cos(lat) is 0 and the map
            // is infinitely stretched, which would divide by zero. Half a tile is the finest thing the
            // grid can express, so clamping there is exact rather than arbitrary.
            minCos = 0.5f * Mathf.PI / hf
        };
    }

    // ============================================================================================
    // CELLS -> PLATES
    //
    // Seeded region growing over the cells' adjacency graph. Every cell ends up owned by exactly one
    // plate, and the plate's outline is whatever the union of its cells happens to look like — which is
    // the entire point, because that union is not convex and a single Voronoi cell always is.
    //
    // Returns cellPlate[i] for every cell.
    // ============================================================================================
    static int[] Agglomerate(Vector3[] sites, int plateCount, System.Func<float> R, int seed)
    {
        int n = sites.Length;
        var owner = new int[n];
        for (int i = 0; i < n; i++) owner[i] = -1;

        var adj = BuildAdjacency(sites, seed);

        // ---- Seeds, spread as far apart among the cells as they can be ----
        // Taking them at random would sometimes put two plate seeds in adjacent cells, and one of the
        // two would then be squeezed to a single cell by its neighbour before it could grow at all.
        var seeds = new int[plateCount];
        for (int p = 0; p < plateCount; p++)
        {
            int best = -1;
            float bestSep = -1f;
            for (int j = 0; j < n; j++)
            {
                if (owner[j] >= 0) continue;
                float nearest = float.MaxValue;
                for (int k = 0; k < p; k++) nearest = Mathf.Min(nearest, 1f - Vector3.Dot(sites[seeds[k]], sites[j]));
                if (p == 0) nearest = R();               // the first seed can go anywhere
                if (nearest > bestSep) { bestSep = nearest; best = j; }
            }
            if (best < 0) break;                          // fewer cells than plates: shouldn't happen
            owner[best] = p;
            seeds[p] = best;
        }

        // ---- Target sizes ----
        // Drawn per plate from a curve that skews small, so a world gets a couple of continents and a
        // scatter of small plates rather than a dozen identical ones. Normalised so they sum to the cell
        // count, which is what makes "furthest below target" a meaningful comparison between plates.
        var target = new float[plateCount];
        float tsum = 0f;
        for (int p = 0; p < plateCount; p++)
        {
            float t = 0.25f + Mathf.Pow(R(), 2.2f) * 3f;
            target[p] = t;
            tsum += t;
        }
        for (int p = 0; p < plateCount; p++) target[p] = target[p] / Mathf.Max(0.0001f, tsum) * n;

        var size = new int[plateCount];
        for (int p = 0; p < plateCount; p++) size[p] = 1;

        int remaining = n - plateCount;

        // ---- Grow ----
        while (remaining > 0)
        {
            // The plate furthest below its target annexes next. See the header on why this is not a
            // global best-pair search.
            int hungriest = -1;
            float worst = float.MinValue;
            for (int p = 0; p < plateCount; p++)
            {
                float deficit = target[p] - size[p];
                if (deficit <= worst) continue;
                if (!HasFrontier(adj, owner, p)) continue;
                worst = deficit; hungriest = p;
            }

            // Nobody can reach anything else — a sampled adjacency graph can miss an edge and strand a
            // cell. Hand every orphan to whichever plate centre is nearest and stop.
            if (hungriest < 0)
            {
                for (int j = 0; j < n; j++)
                {
                    if (owner[j] >= 0) continue;
                    int bp = 0; float bd = -2f;
                    for (int p = 0; p < plateCount; p++)
                    {
                        float d = Vector3.Dot(sites[seeds[p]], sites[j]);
                        if (d > bd) { bd = d; bp = p; }
                    }
                    owner[j] = bp;
                }
                break;
            }

            // It takes the frontier cell it is most attached to, plus jitter. Always taking the
            // most-connected one gives compact blobs; taking one at random gives tendrils.
            int bestCell = -1;
            float bestScore = float.MinValue;
            for (int j = 0; j < n; j++)
            {
                if (owner[j] >= 0) continue;
                int touching = 0;
                foreach (int k in adj[j]) if (owner[k] == hungriest) touching++;
                if (touching == 0) continue;
                float score = touching + R() * GrowJitter;
                if (score > bestScore) { bestScore = score; bestCell = j; }
            }

            if (bestCell < 0) break;   // HasFrontier said otherwise; defensive
            owner[bestCell] = hungriest;
            size[hungriest]++;
            remaining--;
        }

        // Anything still unclaimed (a break above) goes to plate 0 rather than staying at -1, which
        // would index out of the plate array on the first Sample.
        for (int i = 0; i < n; i++) if (owner[i] < 0) owner[i] = 0;
        return owner;
    }

    static bool HasFrontier(List<int>[] adj, int[] owner, int plate)
    {
        for (int j = 0; j < owner.Length; j++)
        {
            if (owner[j] >= 0) continue;
            foreach (int k in adj[j]) if (owner[k] == plate) return true;
        }
        return false;
    }

    /// Which cells border which, discovered by sampling the sphere and recording each point's two
    /// nearest sites. Approximate, and that is fine — see AdjacencySamples.
    static List<int>[] BuildAdjacency(Vector3[] sites, int seed)
    {
        int n = sites.Length;
        var adj = new List<int>[n];
        var seen = new HashSet<long>();
        for (int i = 0; i < n; i++) adj[i] = new List<int>();

        // Its OWN random stream, so changing the sample count cannot shift the plate motions or the
        // warp — those are drawn from `R` in Build, and a shared stream would make this an invisible
        // dependency between an implementation detail and every world's geology.
        var rng = new System.Random(seed ^ 0x5f356495);
        System.Func<float> S = () => (float)rng.NextDouble();

        for (int s = 0; s < AdjacencySamples; s++)
        {
            Vector3 p = RandomDirection(S);
            int a = -1, b = -1;
            float c1 = -2f, c2 = -2f;
            for (int j = 0; j < n; j++)
            {
                float c = Vector3.Dot(sites[j], p);
                if (c > c1) { c2 = c1; b = a; c1 = c; a = j; }
                else if (c > c2) { c2 = c; b = j; }
            }
            if (a < 0 || b < 0) continue;

            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!seen.Add(key)) continue;
            adj[a].Add(b);
            adj[b].Add(a);
        }
        return adj;
    }

    static Vector3 FirstCellOf(Vector3[] sites, int[] cellPlate, int plate)
    {
        for (int i = 0; i < cellPlate.Length; i++) if (cellPlate[i] == plate) return sites[i];
        return Vector3.up;
    }

    static WarpTerm MakeTerm(System.Func<float> R, float fMin, float fMax, float gain)
    {
        float f = Mathf.Lerp(fMin, fMax, R());
        return new WarpTerm
        {
            freq = RandomDirection(R) * f,
            dir = RandomDirection(R),
            amp = gain / f,
            phase = R() * Mathf.PI * 2f
        };
    }

    /// A uniformly-distributed direction on the sphere. Uniform in z (NOT in latitude) — sampling latitude
    /// uniformly would crowd the poles, which is precisely the bias this whole file exists to avoid.
    static Vector3 RandomDirection(System.Func<float> R)
    {
        float z = R() * 2f - 1f;
        float lon = R() * Mathf.PI * 2f;
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(r * Mathf.Cos(lon), z, r * Mathf.Sin(lon));
    }

    // How many of a layout's terms actually displace the surface; the rest are the grain field.
    const int WarpCount = CoarseTerms + FineTerms;

    static Vector3 Warp(Layout l, Vector3 p)
    {
        Vector3 d = Vector3.zero;
        for (int i = 0; i < WarpCount; i++)
        {
            var t = l.warp[i];
            d += t.dir * (t.amp * Mathf.Sin(Vector3.Dot(t.freq, p) + t.phase));
        }
        return p + d;
    }

    /// The warp's Jacobian applied to a direction: d(Warp)/dp · t. Exact, because the basis is analytic.
    static Vector3 WarpJacobian(Layout l, Vector3 p, Vector3 t)
    {
        Vector3 r = Vector3.zero;
        for (int i = 0; i < WarpCount; i++)
        {
            var w = l.warp[i];
            r += w.dir * (w.amp * Mathf.Cos(Vector3.Dot(w.freq, p) + w.phase) * Vector3.Dot(w.freq, t));
        }
        return r;
    }

    /// A seamless scalar field in -1..1 from the grain terms. `salt` re-phases it so two callers asking
    /// for a field off the same terms don't get the same field.
    static float Grain(Layout l, Vector3 p, float salt)
    {
        float s = 0f;
        for (int i = WarpCount; i < l.warp.Length; i++)
        {
            var w = l.warp[i];
            s += Mathf.Sin(Vector3.Dot(w.freq, p) + w.phase + salt);
        }
        return s / Mathf.Max(1, l.warp.Length - WarpCount);
    }

    // The plate geometry at a normalized (u,v). Cheap: a dozen sines plus an O(plates) scan (4..13) over
    // the cached layout.
    public static Hit Sample(CelestialBody b, float u, float v)
    {
        var l = Get(b);
        if (l == null || l.plates == null || l.plates.Length == 0) return default;
        if (l.cellSites == null || l.cellSites.Length == 0) return default;

        // (u,v) is an equirectangular position: u wraps once around, v runs pole to pole. Put it on the
        // sphere, then warp it — everything downstream (which plate owns it, how far it is from the fault)
        // is asked of the warped point, which is what turns the underlying great-circle boundaries into
        // wandering, organic margins.
        float lon = Mathf.Repeat(u, 1f) * Mathf.PI * 2f;
        float lat = (Mathf.Clamp01(v) - 0.5f) * Mathf.PI;
        float cosLat = Mathf.Cos(lat), sinLat = Mathf.Sin(lat);
        float cosLon = Mathf.Cos(lon), sinLon = Mathf.Sin(lon);
        Vector3 p = new Vector3(cosLat * cosLon, sinLat, cosLat * sinLon);

        Vector3 raw = Warp(l, p);
        float mag = Mathf.Max(1e-5f, raw.magnitude);
        Vector3 q = raw / mag;

        // ---- THE NEAREST CELL, AND THE NEAREST CELL OF A DIFFERENT PLATE ----
        //
        // This is the whole change. It used to be "nearest plate site, second-nearest plate site", so
        // the boundary was the bisector between two PLATE centres and was therefore a single arc. Now
        // the fault is the bisector between two CELLS on opposite sides of it — a different pair of
        // cells as you walk along it, so the margin is a chain of arcs meeting at angles rather than one
        // smooth curve.
        //
        // The second-nearest cell OVERALL is not what is wanted: deep inside a large plate that is
        // another cell of the SAME plate, and its bisector is an internal cell edge with no geological
        // meaning — drawing it would put a red line down the middle of every continent.
        //
        // ONE PASS, NOT TWO. The obvious implementation scans for the nearest cell, reads its plate, and
        // scans again for the nearest cell of any other plate. That doubles the cost of the hottest
        // function in world generation: Sample is called once per tile per survey index (six of them)
        // and once per tile while baking terrain, so on a 400x200 world it runs the better part of a
        // million times before the player has looked at anything.
        //
        // So both are tracked together. The case that makes this subtle is a new cell that beats the
        // current best AND belongs to a different plate: the old best then becomes the best
        // different-plate candidate, and it is correct to overwrite the previous one because the old
        // best outranks everything else already seen.
        int cellA = -1, cellB = -1;
        int plateA = -1;
        float c1 = -2f, c2 = -2f;

        for (int i = 0; i < l.cellSites.Length; i++)
        {
            float c = Vector3.Dot(l.cellSites[i], q);     // on a unit sphere, nearest == largest dot
            int pi = l.cellPlate[i];

            if (c > c1)
            {
                if (cellA >= 0 && pi != plateA) { c2 = c1; cellB = cellA; }
                c1 = c; cellA = i; plateA = pi;
            }
            else if (pi != plateA && c > c2) { c2 = c; cellB = i; }
        }

        Hit hit = new Hit { plateA = plateA, plateB = -1, owner = plateA, boundary = 0f, belt = 0f, convergence = 0f };
        if (cellB < 0) return hit;   // only one plate on this world: no faults anywhere

        hit.plateB = l.cellPlate[cellB];

        Vector3 A = l.cellSites[cellA], B = l.cellSites[cellB];

        // The fault between A and B is the great circle equidistant from both: the plane through the
        // origin with normal m. The angular distance from a point to that circle is asin(q·m) — exact,
        // closed form, no approximation, and (unlike the difference-of-distances this used to use) an
        // actual distance, so a band of constant width really is a band of constant width.
        Vector3 m = (A - B).normalized;

        // SIGNED, and it stays signed until the roughness has been added. `abs` here would fold the two
        // sides of the fault together, and an offset added after that fold would DILATE the band (making
        // the red line fatter in places) rather than MOVE it, which is the opposite of a rough edge.
        float signed = Mathf.Asin(Mathf.Clamp(Vector3.Dot(q, m), -1f, 1f));

        // That distance was measured in WARPED space. Where the warp stretches space apart the same band
        // would cover more ground and the red line would fatten; dividing by the warp's local stretch
        // along the fault's own normal undoes exactly that. This correction is what allows the warp to be
        // strong enough to matter without the line's width wandering with it.
        Vector3 t = m - q * Vector3.Dot(m, q);   // the fault normal, brought into the tangent plane at q
        float tl = t.magnitude;
        float stretch = 1f;
        bool haveNormal = tl > 1e-4f;
        if (haveNormal)
        {
            t /= tl;
            Vector3 e = t + WarpJacobian(l, p, t);
            e -= q * Vector3.Dot(e, q);          // only the part that moves ALONG the surface counts
            stretch = Mathf.Max(0.3f, e.magnitude / mag);
        }

        // ---- THE MARGIN'S OWN ROUGHNESS ----
        //
        // `offset` slides the boundary sideways by a couple of tiles, in a pattern that is continuous
        // over the whole sphere and seamless at the date line. `slope` is that field's exact derivative
        // ALONG the fault's normal, and dividing by |1 + slope| is what keeps the drawn band the width it
        // is supposed to be: adding a varying offset to a distance field stops it being a distance field,
        // and the correction restores it. Skipped where the normal is degenerate (q parallel to m, i.e.
        // the two poles of the fault's great circle) — there is no "sideways" to move in there.
        float offset = 0f, slope = 0f;
        if (l.edge != null)
        {
            for (int i = 0; i < l.edge.Length; i++)
            {
                var w2 = l.edge[i];
                float a2 = Vector3.Dot(w2.freq, q) + w2.phase;
                offset += w2.amp * Mathf.Sin(a2);
                if (haveNormal) slope += w2.amp * Mathf.Cos(a2) * Vector3.Dot(w2.freq, t);
            }
        }

        // SIGNED, roughness and all. Its magnitude is how far the point is from the fault; its SIGN is
        // which side of the fault the point is on, and therefore which plate owns it. Taking ownership
        // from here rather than from the raw nearest-cell scan is what makes the drawn boundary and the
        // ownership boundary the SAME LINE: the margin's wander moves both together, so the plate map can
        // never disagree with the red line drawn on it.
        float adjusted = (signed / stretch + offset) / Mathf.Max(0.45f, Mathf.Abs(1f + slope));
        hit.owner = adjusted >= 0f ? plateA : hit.plateB;

        float angReal = Mathf.Abs(adjusted);

        // Angle -> TILES on the 2:1 equirectangular map. h tiles span pi of latitude and w = 2h tiles span
        // 2pi of longitude, so a step north costs h/pi tiles while a step east costs h/(pi*cos lat): near
        // a pole the very same angle is many more tiles wide. Converting through the fault's OWN normal
        // direction is what keeps the drawn band a constant handful of tiles everywhere on the map,
        // instead of flaring into a red smear across the polar rows.
        Vector3 east = new Vector3(-sinLon, 0f, cosLon);
        Vector3 north = Vector3.Cross(east, p);
        float tE = Vector3.Dot(t, east), tN = Vector3.Dot(t, north);
        float cosl = Mathf.Max(l.minCos, Mathf.Abs(cosLat));
        float tilesPerRad = Mathf.Sqrt((tE / cosl) * (tE / cosl) + tN * tN) * l.heightTiles / Mathf.PI;
        if (tilesPerRad < 1e-4f) tilesPerRad = l.heightTiles / Mathf.PI;
        float distTiles = angReal * tilesPerRad;

        // The drawn line. Its width breathes a little along its length — real margins are not drafted with
        // a ruler — but the jitter is bounded, so the band stays inside its one-to-three tile budget. The
        // floor on the half-width is what keeps the line CONTINUOUS: a band narrower than about 0.7 tiles
        // can pass between two cell centres without colouring either, and the overlay then draws the fault
        // as a dashed line, which reads as a rendering fault rather than as geology.
        float jitter = 0.80f + 0.30f * (Grain(l, q, 1.7f) * 0.5f + 0.5f);
        float faultHalf = Mathf.Max(0.75f, l.faultTiles * 0.5f * jitter);
        hit.boundary = 1f - Mathf.Clamp01(distTiles / faultHalf);

        // The mountain belt: much wider, smoothstepped so ranges rise INTO the fault rather than stopping
        // at a hard edge, and modulated by grain so a range has peaks, saddles and gaps along its length
        // instead of reading as an extruded wall. Deliberately not clipped to the drawn line — the red
        // overlay marks where a range belongs, it is not a fence the range has to stay inside.
        float bt = 1f - Mathf.Clamp01(distTiles / (l.beltTiles * 0.5f));
        bt = bt * bt * (3f - 2f * bt);
        hit.belt = bt * (0.55f + 0.45f * (Grain(l, q, 4.3f) * 0.5f + 0.5f));

        // Convergence across that fault: does plate A drive INTO plate B faster than B pulls away? `nrm`
        // is the tangent at A pointing toward B; project the PLATES' relative motion onto it. >0
        // compresses the boundary (mountains, volcanoes), <0 opens a rift.
        //
        // The direction comes from the two CELLS either side of the fault (that is where the boundary
        // actually is), but the motion comes from the two PLATES those cells belong to — a cell has no
        // motion of its own, and giving it one would make a single continent's interior shear against
        // itself along every internal cell edge.
        Vector3 nrm = B - A * Vector3.Dot(A, B);
        if (nrm.sqrMagnitude > 1e-6f)
        {
            nrm.Normalize();
            Vector3 vrel = l.plates[hit.plateA].motion - l.plates[hit.plateB].motion;
            hit.convergence = Mathf.Clamp(Vector3.Dot(vrel, nrm) * 0.5f, -1f, 1f);
        }
        return hit;
    }

    // ============================================================================================
    // THE PLATE MAP, BUILT
    //
    // Derived on demand and cached per body, like everything else here. It costs one Sample per tile —
    // the same work the terrain generator already does once — and only the overlay asks for it, so a
    // world nobody surveys never pays for one at all.
    //
    // DELIBERATELY NOT FED BACK INTO THE LAYOUT. The absorption below edits this raster, not `cellPlate`,
    // so `Sample` still reports the geometry it always did and the mountain belts the terrain was baked
    // from stay exactly where they are. The two can differ only inside an absorbed sliver — a strip a
    // tile or two across — and there the belt is unchanged, which is the right answer: simplifying the
    // MAP is a drawing decision, and the rock does not move because we stopped drawing a line through it.
    // ============================================================================================
    public static TileMap Tiles(CelestialBody b)
    {
        if (b?.surface == null || !Active(b)) return null;
        if (tileCache.TryGetValue(b, out var tm) &&
            tm.width == b.surface.width && tm.height == b.surface.height &&
            Mathf.Approximately(tm.builtForSeed, b.terrainSeed) && tm.builtForSize == b.surfaceSize)
            return tm;

        tm = BuildTiles(b);
        tileCache[b] = tm;
        return tm;
    }

    static TileMap BuildTiles(CelestialBody b)
    {
        int w = b.surface.width, h = b.surface.height, n = w * h;
        var map = new TileMap
        {
            width = w, height = h,
            plate = new int[n],
            border = new bool[n],
            builtForSeed = b.terrainSeed,
            builtForSize = b.surfaceSize
        };

        var layout = Get(b);
        int plateCount = layout?.plates?.Length ?? 1;
        map.plateDrawn = new bool[plateCount];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var hit = Sample(b, (x + 0.5f) / w, (y + 0.5f) / h);
                map.plate[y * w + x] = Mathf.Clamp(hit.owner, 0, plateCount - 1);
            }

        Absorb(map, plateCount);
        MarkBorders(map);

        for (int i = 0; i < n; i++) map.plateDrawn[map.plate[i]] = true;
        return map;
    }

    static int Wrap(int x, int w) => ((x % w) + w) % w;

    // ---- Absorption: a sliver is not a plate --------------------------------------------------
    //
    // Repeatedly finds the worst offending REGION — a 4-connected run of one plate's tiles that is either
    // too small to be a continent or too thin to have an inside — and hands it to whichever neighbouring
    // plate it shares the most edge with. Regions rather than plates, because a big plate can still send a
    // two-tile tail into its neighbour, and that tail draws with a red line down each of its flanks: the
    // double border the request is about.
    //
    // Iterative because absorbing one sliver can leave the plate that ate it a sliver in turn.
    static void Absorb(TileMap map, int plateCount)
    {
        int w = map.width, h = map.height, n = w * h;
        int minTiles = Mathf.Max(MinPlateTiles, Mathf.RoundToInt(n * MinPlateFraction));

        var label = new int[n];
        var stack = new Stack<int>();
        var sizes = new List<int>();

        for (int pass = 0; pass < 16; pass++)
        {
            LabelRegions(map, label, sizes, stack);

            // How many DISTINCT plates are still on the map — the floor the absorption must not cross.
            // NOT an early return: a plate that still has a body elsewhere may lose a tail however few
            // plates are left, and only the absorption that would delete a plate outright is refused.
            var present = new HashSet<int>();
            for (int i = 0; i < n; i++) present.Add(map.plate[i]);

            // The smallest offender that may legally go. Absorbing a region that is its plate's ONLY
            // presence deletes that plate, which is refused once the map is down to the floor — so the
            // scan skips those rather than stopping at one, or a single unabsorbable polar sliver would
            // leave every other sliver on the map standing.
            int worst = -1, worstSize = int.MaxValue;
            for (int id = 0; id < sizes.Count; id++)
            {
                if (sizes[id] >= minTiles && HasCore(map, label, id)) continue;
                if (sizes[id] >= worstSize) continue;
                if (present.Count - 1 < MinPlatesOnMap && IsSoleRegionOfItsPlate(map, label, id)) continue;
                worstSize = sizes[id]; worst = id;
            }
            if (worst < 0) return;

            // The longest shared edge wins: a sliver joins the continent it is mostly pressed against.
            var share = new Dictionary<int, int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (label[y * w + x] != worst) continue;
                    Share(map, label, share, worst, x + 1, y);
                    Share(map, label, share, worst, x - 1, y);
                    Share(map, label, share, worst, x, y + 1);
                    Share(map, label, share, worst, x, y - 1);
                }

            int into = -1, best = -1;
            foreach (var kv in share) if (kv.Value > best) { best = kv.Value; into = kv.Key; }
            if (into < 0) return;    // a region with no neighbours: it is the whole world

            for (int i = 0; i < n; i++) if (label[i] == worst) map.plate[i] = into;
        }
    }

    /// Is this region everything its plate has left on the map? Absorbing one that is deletes the plate.
    static bool IsSoleRegionOfItsPlate(TileMap map, int[] label, int region)
    {
        int n = map.plate.Length, plate = -1;
        for (int i = 0; i < n; i++) if (label[i] == region) { plate = map.plate[i]; break; }
        if (plate < 0) return false;
        for (int i = 0; i < n; i++) if (map.plate[i] == plate && label[i] != region) return false;
        return true;
    }

    static void Share(TileMap map, int[] label, Dictionary<int, int> share, int region, int x, int y)
    {
        if (y < 0 || y >= map.height) return;
        int i = y * map.width + Wrap(x, map.width);
        if (label[i] == region) return;
        share.TryGetValue(map.plate[i], out int c);
        share[map.plate[i]] = c + 1;
    }

    /// 4-connected runs of one plate. Longitude wraps and latitude does not, the same connectivity every
    /// other rule in the project uses.
    static void LabelRegions(TileMap map, int[] label, List<int> sizes, Stack<int> stack)
    {
        int w = map.width, h = map.height, n = w * h;
        sizes.Clear();
        for (int i = 0; i < n; i++) label[i] = -1;

        for (int start = 0; start < n; start++)
        {
            if (label[start] >= 0) continue;
            int id = sizes.Count, p = map.plate[start], count = 0;
            label[start] = id;
            stack.Push(start);

            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                count++;
                int cx = cur % w, cy = cur / w;
                PushSame(map, label, stack, id, p, cx + 1, cy);
                PushSame(map, label, stack, id, p, cx - 1, cy);
                PushSame(map, label, stack, id, p, cx, cy + 1);
                PushSame(map, label, stack, id, p, cx, cy - 1);
            }
            sizes.Add(count);
        }
    }

    static void PushSame(TileMap map, int[] label, Stack<int> stack, int id, int plate, int x, int y)
    {
        if (y < 0 || y >= map.height) return;
        int i = y * map.width + Wrap(x, map.width);
        if (label[i] >= 0 || map.plate[i] != plate) return;
        label[i] = id;
        stack.Push(i);
    }

    /// Does this region have an INSIDE — at least a few tiles all four of whose neighbours are also its
    /// own? A region with none is one tile wide along its entire length. Pole rows are never core: a tile
    /// on the top row has no neighbour above it, and a plate that only reaches the map's top edge is a
    /// polar fringe rather than a continent.
    static bool HasCore(TileMap map, int[] label, int region)
    {
        int w = map.width, h = map.height, core = 0;
        for (int y = 1; y < h - 1; y++)
            for (int x = 0; x < w; x++)
            {
                if (label[y * w + x] != region) continue;
                if (label[y * w + Wrap(x + 1, w)] != region) continue;
                if (label[y * w + Wrap(x - 1, w)] != region) continue;
                if (label[(y + 1) * w + x] != region) continue;
                if (label[(y - 1) * w + x] != region) continue;
                if (++core >= MinPlateCore) return true;
            }
        return false;
    }

    // ---- The line ------------------------------------------------------------------------------
    static void MarkBorders(TileMap map)
    {
        int w = map.width, h = map.height, n = w * h;

        // One side only — the lower-numbered plate's — so a boundary is one tile of red rather than two.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int me = map.plate[y * w + x];
                if (LowerThanNeighbour(map, me, x + 1, y) || LowerThanNeighbour(map, me, x - 1, y) ||
                    LowerThanNeighbour(map, me, x, y + 1) || LowerThanNeighbour(map, me, x, y - 1))
                    map.border[y * w + x] = true;
            }

        // SQUARE OFF THE STAIRCASE. Two passes: the first fills the corners the marking pass left, the
        // second catches any corner the first one created. A third pass has never found anything in the
        // Node port across every map size and seed tried, so two is the fixed point rather than a budget.
        var add = new List<int>();
        for (int pass = 0; pass < 2; pass++)
        {
            add.Clear();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!map.border[y * w + x]) continue;
                    Square(map, add, x, y, 1, 1);
                    Square(map, add, x, y, 1, -1);
                    Square(map, add, x, y, -1, 1);
                    Square(map, add, x, y, -1, -1);
                }
            if (add.Count == 0) break;
            foreach (int i in add) map.border[i] = true;
        }
    }

    static bool LowerThanNeighbour(TileMap map, int me, int x, int y)
    {
        if (y < 0 || y >= map.height) return false;
        int other = map.plate[y * map.width + Wrap(x, map.width)];
        return other != me && me < other;
    }

    /// (x,y) and (x+dx,y+dy) are both border but touch only at a corner. Add the tile that squares that
    /// corner off, preferring the one that is itself against another plate — that is the tile a player
    /// would call part of the boundary, rather than one pulled arbitrarily out of a plate's interior.
    static void Square(TileMap map, List<int> add, int x, int y, int dx, int dy)
    {
        int w = map.width, h = map.height;
        int ny = y + dy;
        if (ny < 0 || ny >= h) return;
        if (!map.border[ny * w + Wrap(x + dx, w)]) return;

        int sideA = y * w + Wrap(x + dx, w);      // step sideways first
        int sideB = ny * w + Wrap(x, w);          // step up/down first
        if (map.border[sideA] || map.border[sideB]) return;

        add.Add(OnAMargin(map, x + dx, y) ? sideA : sideB);
    }

    static bool OnAMargin(TileMap map, int x, int y)
    {
        int w = map.width, h = map.height;
        int me = map.plate[y * w + Wrap(x, w)];
        if (map.plate[y * w + Wrap(x + 1, w)] != me) return true;
        if (map.plate[y * w + Wrap(x - 1, w)] != me) return true;
        if (y + 1 < h && map.plate[(y + 1) * w + Wrap(x, w)] != me) return true;
        if (y - 1 >= 0 && map.plate[(y - 1) * w + Wrap(x, w)] != me) return true;
        return false;
    }

    // Where a plate's direction arrow sits on the MAP and which way it points. The site is a point on the
    // sphere, so this is the inverse of Sample's projection; the direction is the plate's tangential push
    // resolved into east/north and then rescaled for the 2:1 map, where a degree of longitude is half the
    // pixels of a degree of latitude and shrinks further with cos(latitude). Without that rescale a plate
    // near a pole would draw an arrow pointing somewhere it is not going.
    public static void ArrowOnMap(Plate p, out float u, out float v, out Vector2 dir, out float strength)
    {
        Vector3 s = p.site;
        float lat = Mathf.Asin(Mathf.Clamp(s.y, -1f, 1f));
        float lon = Mathf.Atan2(s.z, s.x);
        u = Mathf.Repeat(lon / (Mathf.PI * 2f), 1f);
        v = Mathf.Clamp01(lat / Mathf.PI + 0.5f);

        Vector3 east = new Vector3(-Mathf.Sin(lon), 0f, Mathf.Cos(lon));
        Vector3 north = Vector3.Cross(east, s);
        float ve = Vector3.Dot(p.motion, east), vn = Vector3.Dot(p.motion, north);
        // The cos(lat) floor is generous on purpose: at the pole the true scaling is unbounded, and an
        // arrow that snaps to horizontal there says less than one that merely leans.
        dir = new Vector2(ve / (2f * Mathf.Max(0.25f, Mathf.Cos(lat))), vn);
        strength = p.strength;
    }
}
