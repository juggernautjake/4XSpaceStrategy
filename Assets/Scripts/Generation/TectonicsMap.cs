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
// One site per plate gives one Voronoi cell per plate, and every boundary is then a SINGLE bisector
// arc between two sites — one smooth curve from junction to junction. The map reads as a sheet of
// soap bubbles rather than as continents, and warp and edge roughness only wobble those arcs; they
// cannot turn one arc into several.
//
// So the sites are no longer plates. There are now SIX OR SO CELLS PER PLATE, and a plate is a
// CLUSTER of them, fused at layout time. A plate boundary then runs along whichever cell edges happen
// to lie between two clusters — a chain of several bisector arcs meeting at angles, which is what a
// plate margin looks like on a real map.
//
// ---- AND WHY THAT CLUSTER IS CONVEX ----------------------------------------------------------
//
// A plate is a seed cell plus a WEIGHT, and each cell joins whichever plate maximises
// dot(cell, seed) + weight. That is a spherical POWER DIAGRAM, and its regions are convex. A plate
// built this way cannot have a tendril, a neck or a bay, because a convex region has nowhere to put
// one — while still being several cells across, so its rim is still a chain of arcs. Convex removes
// the RE-ENTRANT corner, not the corner.
//
// This replaced a region-growing pass that annexed the frontier cell a plate touched most, plus
// jitter. That was deliberately non-convex, and it went too far: rendered at 240x120 across two dozen
// worlds it gave, in a quarter of them, a plate shaped like a C or one carrying two continent-sized
// masses joined by a two-tile isthmus. Measured by taking pairs of tiles inside a plate and walking
// the great circle between them, region growing kept 86% of those walks inside the plate and the
// power diagram keeps 90% — against a ceiling of 91% for ANY partition at that raster size, since a
// rasterised spherical polygon is never exactly convex. Plates carrying two masses joined by a neck:
// six across those worlds before, none after.
//
// THE WEIGHTS ARE WHAT MAKE PLATES DIFFERENT SIZES, and that detail matters. A plain nearest-seed
// partition gives every plate roughly the same area. Each plate is drawn a TARGET SIZE up front from
// a curve that skews small, and a plate under its target has its weight raised so it reaches further
// next round. Measured, the biggest plate holds 19% of the cells and a world carries about four
// one- or two-cell plates — both within a point of what the growth version gave, so a world still
// gets a couple of continents and a scatter of small plates rather than a dozen identical ones.
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

        /// How hard the two plates SLIDE PAST each other across this fault, 0..1 — the component of
        /// their relative motion ALONG the boundary rather than across it. A transform margin (the San
        /// Andreas) has almost no convergence and enormous shear, and the Geothermal index has to read
        /// it as an active fault: the request calls out "two neighboring continental plates pushing
        /// past each other (shearing)" alongside head-on collision as a high-activity margin.
        ///
        /// Kept separate from `convergence` rather than folded into a single "activity" number because
        /// they do different things to the GROUND: convergence lifts and rifting drops, while shear
        /// does neither — it only shakes and vents. Terrain reads convergence; the Geothermal index and
        /// the earthquakes read both.
        public float shear;

        /// Distance from the drawn fault line, IN TILES of this world's grid. `boundary` and `belt` are
        /// both normalized falloffs whose width is decided here; the Geothermal index needs the raw
        /// distance instead, because the request specifies its radiation in tiles ("up to 3 in each
        /// direction") and a normalized falloff cannot answer that question at two different map sizes.
        public float distanceTiles;
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
    ///
    /// RAISED FROM 0.004, and the old value is worth understanding because it looked harmless. A world
    /// carries four to thirteen plates, so an average plate holds 8-25% of the map; 0.4% is therefore not
    /// "a small plate", it is a plate one fiftieth the size of its neighbours. Rendered, a region that
    /// small is not a landmass with a border — it is entirely border, because every one of its tiles
    /// touches another plate. It draws as a solid clump of red, and a clump of red in a picture whose
    /// whole language is "red means the CRACK between two plates" says something that isn't true.
    ///
    /// Two percent still lets a world have genuinely small plates (a Juan de Fuca beside a Pacific), and
    /// MinPlatesOnMap below still stops the absorption from flattening a world into two continents.
    const float MinPlateFraction = 0.02f;

    /// ...and never fewer than this many tiles, so a small world's plates aren't all specks by fraction.
    ///
    /// This is the floor that was actually binding on the worlds where the artefact showed. A 60x30 moon
    /// is 1,800 cells, so the fraction above asked for 7 tiles and this asked for 12 — and a 12-tile
    /// region on a 60-wide map is a five-by-three blob with no interior at all. Thirty tiles is about the
    /// smallest region that can have a couple of cells nothing can see the edge of, which is the real
    /// requirement: a plate has to have an INSIDE, or it is a spot rather than a plate.
    const int MinPlateTiles = 30;

    /// How many tiles a region must have that are COMPLETELY surrounded by their own plate. A region with
    /// none is one tile wide somewhere along its whole length — a sliver — and a sliver wedged between two
    /// plates is precisely what draws as a double line. Three rather than one so a two-tile-wide tail
    /// still goes: the request is that extremely thin cells join a neighbour.
    const int MinPlateCore = 3;

    /// Absorption stops here however many slivers are left. A world reduced to two plates has one fault
    /// and reads as a cracked egg rather than as a plate map.
    const int MinPlatesOnMap = 3;

    /// A backstop, not a budget: each pass absorbs every offender it can find, and the pass loop exits
    /// the moment one moves nothing. Measured across map sizes and seeds, the third pass is already
    /// the empty one.
    const int AbsorbPasses = 12;

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
    //   TOO FEW and a plate is one or two cells, so its whole margin is a single arc and we are back
    //           to bubbles. (The plate is convex either way now — what more cells buy is the number
    //           of ARCS its rim is made of, not whether the rim can turn inward.)
    //   TOO MANY and the boundary becomes a fine dither of tiny steps that reads as noise at map scale
    //           rather than as a coastline — and Sample's cost is linear in the total, since it scans
    //           every cell to find the nearest.
    //
    // Six was chosen against rendered maps. It roughly doubles Sample's per-call cost against the old
    // one-site-per-plate version, which is acceptable because the warp arithmetic either side of the
    // scan is comparable work and was always the bulk of it.
    const int CellsPerPlate = 6;

    /// Rounds of weight fitting in the cells-to-plates partition, and how hard a round pushes a plate
    /// that is off its target size. The step decays to zero across the rounds, so the last few settle
    /// rather than oscillating either side of the target. Sixty lands every plate within a cell or two
    /// of the size it was drawn; at ten the small plates all come out the same size as each other,
    /// which is the variety the target curve exists to create.
    const int PowerIterations = 60;
    const float PowerRate = 3f;

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

    // ============================================================================================
    // SAVE / LOAD
    //
    // A layout is DERIVED, and everything else in this file is written on the assumption that
    // deriving it again gives the same answer. That holds for a given build and stops holding the day
    // the algorithm changes — at which point every existing save's plates move, and the mountains
    // already baked into those worlds stay where the OLD plates put them. The overlay would then draw
    // fault lines that no longer run along the ranges they raised.
    //
    // So a save carries the layout. Export flattens it, Import puts it straight into the cache where
    // Get would have put a freshly built one, and generation downstream cannot tell the difference.
    // Nothing else in the file changes: a world with no stored layout still builds one on demand.
    // ============================================================================================

    /// The body's layout, flattened for the save file. Null for a world without tectonics.
    public static TectonicsDTO Export(CelestialBody b)
    {
        if (!Active(b)) return null;
        var l = Get(b);
        if (l?.plates == null || l.cellSites == null) return null;

        var dto = new TectonicsDTO
        {
            plateCount = l.plates.Length,
            heightTiles = l.heightTiles,
            faultTiles = l.faultTiles,
            beltTiles = l.beltTiles,
            minCos = l.minCos
        };

        foreach (var s in l.cellSites) { dto.cellSites.Add(s.x); dto.cellSites.Add(s.y); dto.cellSites.Add(s.z); }
        foreach (int p in l.cellPlate) dto.cellPlate.Add(p);
        foreach (var p in l.plates)
        {
            dto.plates.Add(p.site.x); dto.plates.Add(p.site.y); dto.plates.Add(p.site.z);
            dto.plates.Add(p.motion.x); dto.plates.Add(p.motion.y); dto.plates.Add(p.motion.z);
            dto.plates.Add(p.strength);
        }
        foreach (var t in l.warp)
        {
            dto.warp.Add(t.freq.x); dto.warp.Add(t.freq.y); dto.warp.Add(t.freq.z);
            dto.warp.Add(t.dir.x); dto.warp.Add(t.dir.y); dto.warp.Add(t.dir.z);
            dto.warp.Add(t.amp); dto.warp.Add(t.phase);
        }
        if (l.edge != null)
            foreach (var t in l.edge)
            {
                dto.edge.Add(t.freq.x); dto.edge.Add(t.freq.y); dto.edge.Add(t.freq.z);
                dto.edge.Add(t.amp); dto.edge.Add(t.phase);
            }
        return dto;
    }

    /// Installs a stored layout as this body's layout. Returns false — leaving the body to build its
    /// own — if there is nothing stored, if the world is not tectonic, or if the stored geometry does
    /// not describe the world the body is now.
    ///
    /// MUST run before the terrain is baked. The generator samples this while it lays down mountains,
    /// so a layout imported afterwards would draw an overlay over ranges raised by a different one.
    public static bool Import(CelestialBody b, TectonicsDTO dto)
    {
        if (b == null || dto == null || !dto.HasLayout || !Active(b)) return false;

        // Stride checks before anything is read. A malformed or hand-edited save must fall back to
        // generating a layout, not index off the end of a list mid-way through building one.
        int cells = dto.cellSites.Count / 3;
        if (dto.cellSites.Count % 3 != 0 || cells == 0 || dto.cellPlate.Count != cells) return false;
        if (dto.plates.Count != dto.plateCount * 7 || dto.plateCount <= 0) return false;
        if (dto.warp.Count % 8 != 0 || dto.edge.Count % 5 != 0) return false;

        // The warp terms are read back by INDEX RANGE — Warp uses the first WarpCount of them and
        // Grain the rest — so a save carrying a different number of them cannot be interpreted.
        if (dto.warp.Count / 8 != CoarseTerms + FineTerms + GrainTerms) return false;

        // The band widths below are in TILES against a particular grid height. If the world is no
        // longer that size — a sandbox resize, or a change to how mass maps to a grid — this layout
        // is not this world's, and Get would throw it out on the next call anyway. Refusing here says
        // so honestly instead of returning true and quietly building a different one a moment later.
        if (dto.heightTiles != MapMetrics.SurfH(b)) return false;

        // Every cell must name a plate that exists, or Sample indexes the plate array off the end.
        for (int i = 0; i < cells; i++)
            if (dto.cellPlate[i] < 0 || dto.cellPlate[i] >= dto.plateCount) return false;

        var l = new Layout
        {
            plates = new Plate[dto.plateCount],
            cellSites = new Vector3[cells],
            cellPlate = new int[cells],
            warp = new WarpTerm[dto.warp.Count / 8],
            edge = new EdgeTerm[dto.edge.Count / 5],
            builtForSeed = b.terrainSeed,
            builtForSize = b.surfaceSize,
            heightTiles = dto.heightTiles,
            faultTiles = dto.faultTiles,
            beltTiles = dto.beltTiles,
            minCos = dto.minCos
        };

        for (int i = 0; i < cells; i++)
        {
            l.cellSites[i] = new Vector3(dto.cellSites[i * 3], dto.cellSites[i * 3 + 1], dto.cellSites[i * 3 + 2]);
            l.cellPlate[i] = dto.cellPlate[i];
        }
        for (int i = 0; i < dto.plateCount; i++)
            l.plates[i] = new Plate
            {
                id = i,
                site = new Vector3(dto.plates[i * 7], dto.plates[i * 7 + 1], dto.plates[i * 7 + 2]),
                motion = new Vector3(dto.plates[i * 7 + 3], dto.plates[i * 7 + 4], dto.plates[i * 7 + 5]),
                strength = dto.plates[i * 7 + 6]
            };
        for (int i = 0; i < l.warp.Length; i++)
            l.warp[i] = new WarpTerm
            {
                freq = new Vector3(dto.warp[i * 8], dto.warp[i * 8 + 1], dto.warp[i * 8 + 2]),
                dir = new Vector3(dto.warp[i * 8 + 3], dto.warp[i * 8 + 4], dto.warp[i * 8 + 5]),
                amp = dto.warp[i * 8 + 6],
                phase = dto.warp[i * 8 + 7]
            };
        for (int i = 0; i < l.edge.Length; i++)
            l.edge[i] = new EdgeTerm
            {
                freq = new Vector3(dto.edge[i * 5], dto.edge[i * 5 + 1], dto.edge[i * 5 + 2]),
                amp = dto.edge[i * 5 + 3],
                phase = dto.edge[i * 5 + 4]
            };

        // cellCount per plate is only ever read for display, but leaving it zero would report every
        // continent as made of nothing.
        for (int i = 0; i < cells; i++) l.plates[l.cellPlate[i]].cellCount++;

        // Straight into the cache, which is where Get would have put a built one. The tile raster is
        // dropped rather than kept: it is cheap, and it must be rebuilt from THIS layout.
        cache[b.id] = l;
        tileCache.Remove(b);
        return true;
    }

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
        var cellPlate = Agglomerate(cellSites, n, R);

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
    // A weighted partition: every cell joins whichever plate maximises dot(cell, seed) + weight, and
    // the weights are fitted so each plate lands on the size it was drawn. See the header for why
    // that shape — a spherical power diagram, whose regions are convex — is the one wanted here, and
    // for the numbers it was chosen against.
    //
    // Returns cellPlate[i] for every cell.
    // ============================================================================================
    static int[] Agglomerate(Vector3[] sites, int plateCount, System.Func<float> R)
    {
        int n = sites.Length;

        // ---- Seeds, spread as far apart among the cells as they can be ----
        // Taking them at random would sometimes put two plate seeds in adjacent cells, and the two
        // would then split one cell's worth of sphere between them however the weights were fitted.
        var seeds = new int[plateCount];
        var taken = new bool[n];
        for (int p = 0; p < plateCount; p++)
        {
            int best = -1;
            float bestSep = -1f;
            for (int j = 0; j < n; j++)
            {
                if (taken[j]) continue;
                float nearest = float.MaxValue;
                for (int k = 0; k < p; k++) nearest = Mathf.Min(nearest, 1f - Vector3.Dot(sites[seeds[k]], sites[j]));
                if (p == 0) nearest = R();               // the first seed can go anywhere
                if (nearest > bestSep) { bestSep = nearest; best = j; }
            }
            if (best < 0) break;                          // fewer cells than plates: shouldn't happen
            taken[best] = true;
            seeds[p] = best;
        }

        // ---- Target sizes ----
        // Drawn per plate from a curve that skews small, so a world gets a couple of continents and a
        // scatter of small plates rather than a dozen identical ones. Normalised to sum to the cell
        // count, which is what puts them on the same scale as the sizes the fit below measures.
        var target = new float[plateCount];
        float tsum = 0f;
        for (int p = 0; p < plateCount; p++)
        {
            float t = 0.25f + Mathf.Pow(R(), 2.2f) * 3f;
            target[p] = t;
            tsum += t;
        }
        for (int p = 0; p < plateCount; p++) target[p] = target[p] / Mathf.Max(0.0001f, tsum) * n;

        // ---- Fit the weights ----
        // Partition, measure, nudge, repeat. A plate below its target gets a bigger weight and so
        // reaches further next round; one above it shrinks back. The step decays to zero across the
        // rounds so the tail settles instead of hunting either side of the target.
        var weight = new float[plateCount];
        var owner = new int[n];
        var size = new int[plateCount];
        for (int iter = 0; iter < PowerIterations; iter++)
        {
            System.Array.Clear(size, 0, size.Length);
            for (int j = 0; j < n; j++)
            {
                int bp = 0;
                float bs = float.MinValue;
                for (int p = 0; p < plateCount; p++)
                {
                    float s = Vector3.Dot(sites[j], sites[seeds[p]]) + weight[p];
                    if (s > bs) { bs = s; bp = p; }
                }
                owner[j] = bp;
                size[bp]++;
            }

            float rate = PowerRate * (1f - (float)iter / PowerIterations);
            for (int p = 0; p < plateCount; p++) weight[p] += rate * (target[p] - size[p]) / n;
        }

        // A plate the weights squeezed out entirely keeps its own seed cell. Every plate id has to own
        // ground somewhere, or Sample can name a plateB that is nowhere on the map and the overlay
        // draws a motion arrow for a continent that does not exist.
        System.Array.Clear(size, 0, size.Length);
        for (int j = 0; j < n; j++) size[owner[j]]++;
        for (int p = 0; p < plateCount; p++)
        {
            if (size[p] > 0) continue;
            size[owner[seeds[p]]]--;
            owner[seeds[p]] = p;
            size[p] = 1;
        }

        return owner;
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
    // ============================================================================================
    // CONTINENTAL vs OCEANIC CRUST — where the continents come from on a plate world
    //
    // The request's terrain rework asks for the continents of a tectonic world to be drawn from the
    // Voronoi/cell system rather than from a noise field. That is exactly what a plate map already is:
    // the plates ARE the cells, and all that was missing was the one property that makes some of them
    // land and the rest sea floor. A plate of continental crust rides high; a plate of oceanic crust
    // sits low, and the water collects in it. That is not an analogy — it is the actual reason Earth
    // has an Atlantic and an Africa rather than an even scatter of both.
    //
    // DERIVED, NOT STORED, and that is deliberate rather than lazy. `Export`/`Import` flatten a layout
    // at a fixed stride of seven floats per plate, and a save written by an older build must keep
    // loading; adding an eighth field would have made every existing world fall back to rebuilding its
    // plates, which is the one thing that serialization exists to prevent. A pure function of the
    // layout's own seed and the plate's index answers identically for a built layout and an imported
    // one, so there is nothing to keep in step.
    //
    // Roughly a third of plates come out continental, which is about Earth's land fraction once the
    // continental shelves are counted — and, more to the point, it is the ratio that reliably gives a
    // world a couple of real landmasses in a real ocean rather than either a waterworld or a pangaea.
    public static float PlateCrust(Layout l, int plateId)
    {
        if (l == null || plateId < 0) return 0f;
        float h = Hash01(l.builtForSeed * 0.7331f + plateId * 37.19f + 11.3f);

        // Skewed, not centred. Below the cut the plate is ocean floor and how far below decides how
        // DEEP; above it the plate is land and how far above decides how HIGH. The cut sits at 0.62 so
        // ~38% of plates are continental.
        const float Cut = 0.62f;
        return h < Cut
            ? -Mathf.InverseLerp(Cut, 0f, h)          //  0 .. -1  ocean basin
            :  Mathf.InverseLerp(Cut, 1f, h);         //  0 ..  1  continent
    }

    /// The crust height at a point, from whichever plate actually owns it. The one call the terrain
    /// generator makes; everything about which plate that is lives in `Sample`.
    public static float CrustAt(CelestialBody b, in Hit hit)
    {
        if (hit.owner < 0) return 0f;
        var l = Get(b);
        return l == null ? 0f : PlateCrust(l, hit.owner);
    }

    /// A stable 0..1 from a float. Same sin-based construction AtmosphereRules uses for its
    /// seed-derived values, so "deterministic variation from a seed" means one thing in this codebase.
    static float Hash01(float seed)
    {
        float v = Mathf.Sin(seed * 12.9898f + 78.233f) * 43758.5453f;
        return Mathf.Clamp01(v - Mathf.Floor(v));
    }

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

        Hit hit = new Hit
        {
            plateA = plateA, plateB = -1, owner = plateA,
            boundary = 0f, belt = 0f, convergence = 0f, shear = 0f,
            // No fault in reach is not "on a fault" — it has to read as FAR, or the Geothermal index
            // would light the whole of a one-plate world up at the fault-line value.
            distanceTiles = float.MaxValue
        };
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
        hit.distanceTiles = distTiles;

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
            float across = Vector3.Dot(vrel, nrm);
            hit.convergence = Mathf.Clamp(across * 0.5f, -1f, 1f);

            // SHEAR is what is left of the relative motion once the across-the-fault part is removed:
            // the two plates grinding along each other. Measured in the tangent plane at A, so the
            // radial component (which is not motion on the surface at all) never leaks into it. Same
            // 0.5 scaling as convergence so the two are on one scale and can be compared directly.
            Vector3 along = vrel - nrm * across;
            along -= A * Vector3.Dot(along, A);
            hit.shear = Mathf.Clamp01(along.magnitude * 0.5f);
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
        // A plate that narrows to a needle draws a red line down each flank of it — see TrimSlivers.
        // Absorbed again afterwards because trimming can leave a one- or two-tile orphan behind, and
        // that is exactly what the absorption pass exists to sweep up.
        TrimSlivers(map);
        Absorb(map, plateCount);

        // A region can pass every test above and still be RINGED: too small to hold anything the drawn
        // line does not touch, so the line closes around it and the map shows a red loop with a couple
        // of tiles trapped inside. Caught here rather than in Absorb because the test is about the
        // BORDER, which does not exist until it is marked. See AbsorbRinged.
        AbsorbRinged(map, plateCount);

        MarkBorders(map);
        // ...and the last word on the picture itself: red is a LINE. Whatever the plates came out as,
        // the raster must not contain a filled patch of red or a line that stops in mid-air.
        ThinBorders(map);

        for (int i = 0; i < n; i++) map.plateDrawn[map.plate[i]] = true;
        return map;
    }

    static int Wrap(int x, int w) => ((x % w) + w) % w;

    // ---- Absorption: a sliver is not a plate --------------------------------------------------
    //
    // Finds every offending REGION — a 4-connected run of one plate's tiles that is either too small to
    // be a continent or too thin to have an inside — and hands each to whichever neighbouring plate it
    // shares the most edge with. Regions rather than plates, because a big plate can still send a
    // two-tile tail into its neighbour, and that tail draws with a red line down each of its flanks:
    // the double border the request is about.
    //
    // EVERY OFFENDER EACH PASS, not one. This used to take only the single worst region per pass and
    // cap itself at sixteen passes, which quietly meant it absorbed at most sixteen regions on any
    // world. Measured over 24 worlds from 80x40 to 400x200, the raster carries 2188 offending regions
    // in total — a wandering margin sheds specks the length of every boundary — so ~99% of them
    // survived, and every surviving speck drew its own closed red outline a tile or two from the real
    // boundary. That is the "too many lines" in the request. Absorbing all of them leaves 7 regions
    // beyond one per plate across the same 24 worlds, and no world with more than one extra.
    //
    // Smallest first, so a speck joins the continent beside it before that continent is itself
    // measured. Still iterative, because absorbing one sliver can leave the plate that ate it a
    // sliver in turn, and it stops as soon as a pass moves nothing.
    static void Absorb(TileMap map, int plateCount)
    {
        int w = map.width, h = map.height, n = w * h;
        int minTiles = Mathf.Max(MinPlateTiles, Mathf.RoundToInt(n * MinPlateFraction));

        var label = new int[n];
        var stack = new Stack<int>();
        var sizes = new List<int>();
        var offenders = new List<int>();
        var share = new Dictionary<int, int>();
        var tilesPerPlate = new int[plateCount];

        for (int pass = 0; pass < AbsorbPasses; pass++)
        {
            LabelRegions(map, label, sizes, stack);

            offenders.Clear();
            for (int id = 0; id < sizes.Count; id++)
                if (sizes[id] < minTiles || !HasCore(map, label, id)) offenders.Add(id);
            if (offenders.Count == 0) return;
            offenders.Sort((a, b) => sizes[a].CompareTo(sizes[b]));

            // Which tiles each region holds, gathered once for the whole pass. Rescanning the map per
            // offender would make this quadratic in the region count, and the region count is exactly
            // the thing that turned out to be in the hundreds.
            var tilesOf = new List<int>[sizes.Count];
            for (int id = 0; id < sizes.Count; id++) tilesOf[id] = new List<int>(sizes[id]);
            for (int i = 0; i < n; i++) tilesOf[label[i]].Add(i);

            // How many DISTINCT plates are still on the map — the floor the absorption must not cross.
            // Kept as live tile counts rather than recomputed per offender: a plate is present iff it
            // still holds a tile, and absorbing a region moves a known number of tiles between two of
            // them. NOT an early return — a plate that still has a body elsewhere may lose a tail
            // however few plates are left, and only the absorption that would delete a plate outright
            // is refused.
            System.Array.Clear(tilesPerPlate, 0, tilesPerPlate.Length);
            for (int i = 0; i < n; i++) tilesPerPlate[map.plate[i]]++;
            int present = 0;
            for (int p = 0; p < plateCount; p++) if (tilesPerPlate[p] > 0) present++;

            bool moved = false;
            foreach (int id in offenders)
            {
                var tiles = tilesOf[id];
                if (tiles.Count == 0) continue;
                int mine = map.plate[tiles[0]];

                // This region is everything its plate has left, and the map cannot spare the plate.
                if (present - 1 < MinPlatesOnMap && tilesPerPlate[mine] == tiles.Count) continue;

                // The longest shared edge wins: a sliver joins the continent it is mostly pressed
                // against. Read off the LIVE plate map, so a speck that was itself beside a speck
                // absorbed earlier this pass joins where that one went rather than where it had been.
                share.Clear();
                foreach (int i in tiles)
                {
                    int x = i % w, y = i / w;
                    Share(map, label, share, id, x + 1, y);
                    Share(map, label, share, id, x - 1, y);
                    Share(map, label, share, id, x, y + 1);
                    Share(map, label, share, id, x, y - 1);
                }

                int into = -1, best = -1;
                foreach (var kv in share) if (kv.Key != mine && kv.Value > best) { best = kv.Value; into = kv.Key; }
                if (into < 0) continue;   // a region with no neighbours: it is the whole world

                foreach (int i in tiles) map.plate[i] = into;
                tilesPerPlate[mine] -= tiles.Count;
                tilesPerPlate[into] += tiles.Count;
                if (tilesPerPlate[mine] == 0) present--;
                moved = true;
            }

            if (!moved) return;
        }
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


    // ---- Sliver trimming: a plate must not taper to a point --------------------------------------
    //
    // Absorb removes whole REGIONS that are too small or too thin. It cannot help with a large plate
    // that narrows to a one- or two-tile tip, or sends a thin tongue between two neighbours: the region
    // passes every test Absorb applies — it is big, and it has plenty of tiles with all four neighbours
    // its own — while the tip draws with a red line down each flank. That reads as a spike or a comb
    // hanging off the boundary rather than as a boundary, and it is the artefact in the report.
    //
    // The test is a morphological OPENING. Erode every region by one tile, dilate what survives back by
    // one, and anything the dilation cannot reach is a part of the region thinner than the erosion: a
    // tip, a tongue, a two-wide finger. Those tiles go to whichever plate holds most of their four
    // neighbours.
    //
    // A tile in the body of a plate is never touched — the erosion keeps it and the dilation returns it
    // — so this rounds off needles without moving a single boundary that was already a boundary.
    // Measured over twelve worlds at 200x100: narrow appendages one tile wide go from 35 to 3, two
    // tiles wide from 80 to 20, and plate convexity comes out very slightly HIGHER than before (91.5%
    // against 90.5%) because a needle is the least convex thing a plate can have.
    //
    // ONE ROUND IS ENOUGH. A second pass of trim-then-absorb takes the count from 3 to 2, which is not
    // worth doubling the cost of the raster for.
    static void TrimSlivers(TileMap map)
    {
        int w = map.width, h = map.height, n = w * h;

        var label = new int[n];
        var sizes = new List<int>();
        var stack = new Stack<int>();
        LabelRegions(map, label, sizes, stack);

        // Distance from every tile to the nearest tile of a DIFFERENT region. 1 on a region's rim.
        var dist = new int[n];
        for (int i = 0; i < n; i++) dist[i] = -1;
        var queue = new List<int>(n / 4);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                // A pole row is the end of the map, so a tile on it is on a rim by definition.
                bool rim = y == 0 || y == h - 1
                        || label[y * w + Wrap(x + 1, w)] != label[i]
                        || label[y * w + Wrap(x - 1, w)] != label[i]
                        || label[(y + 1) * w + x] != label[i]
                        || label[(y - 1) * w + x] != label[i];
                if (rim) { dist[i] = 1; queue.Add(i); }
            }

        for (int qi = 0; qi < queue.Count; qi++)
        {
            int cur = queue[qi], cx = cur % w, cy = cur / w;
            Spread(map, label, dist, queue, cur, cx + 1, cy);
            Spread(map, label, dist, queue, cur, cx - 1, cy);
            Spread(map, label, dist, queue, cur, cx, cy + 1);
            Spread(map, label, dist, queue, cur, cx, cy - 1);
        }

        // The core — what survives an erosion by one — dilated back by one inside its own region.
        var open = new bool[n];
        var front = new List<int>();
        for (int i = 0; i < n; i++) if (dist[i] > 1) { open[i] = true; front.Add(i); }
        foreach (int cur in front)
        {
            int cx = cur % w, cy = cur / w;
            Dilate(label, open, cur, cx + 1, cy, w, h);
            Dilate(label, open, cur, cx - 1, cy, w, h);
            Dilate(label, open, cur, cx, cy + 1, w, h);
            Dilate(label, open, cur, cx, cy - 1, w, h);
        }

        // Collected first, applied after: moving tiles while still reading neighbours would let one
        // trimmed tile decide the fate of the next one in the same pass, and the result would depend on
        // the scan order rather than on the shape.
        var moveAt = new List<int>();
        var moveTo = new List<int>();
        var share = new Dictionary<int, int>();

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (open[i]) continue;                       // part of a region's body
                if (sizes[label[i]] < MinSliverRegion) continue;   // Absorb owns the specks

                int me = map.plate[i];
                share.Clear();
                Neighbour(map, share, me, x + 1, y);
                Neighbour(map, share, me, x - 1, y);
                Neighbour(map, share, me, x, y + 1);
                Neighbour(map, share, me, x, y - 1);

                int best = -1, bestN = 0;
                foreach (var kv in share) if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
                if (best < 0) continue;

                moveAt.Add(i);
                moveTo.Add(best);
            }

        for (int i = 0; i < moveAt.Count; i++) map.plate[moveAt[i]] = moveTo[i];
    }

    /// A region smaller than this is Absorb's problem, not this pass's — trimming a speck would just
    /// nibble it a tile at a time when the other pass removes it whole.
    const int MinSliverRegion = 4;

    static void Spread(TileMap map, int[] label, int[] dist, List<int> queue, int from, int x, int y)
    {
        if (y < 0 || y >= map.height) return;
        int i = y * map.width + Wrap(x, map.width);
        if (label[i] != label[from] || dist[i] >= 0) return;
        dist[i] = dist[from] + 1;
        queue.Add(i);
    }

    static void Dilate(int[] label, bool[] open, int from, int x, int y, int w, int h)
    {
        if (y < 0 || y >= h) return;
        int i = y * w + Wrap(x, w);
        if (label[i] != label[from] || open[i]) return;
        open[i] = true;
    }

    static void Neighbour(TileMap map, Dictionary<int, int> share, int me, int x, int y)
    {
        if (y < 0 || y >= map.height) return;      // a pole is the end of the map, not a neighbour
        int p = map.plate[y * map.width + Wrap(x, map.width)];
        if (p == me) return;
        share.TryGetValue(p, out int c);
        share[p] = c + 1;
    }
    // ============================================================================================
    // RINGED REGIONS — the red loop with nothing inside it
    //
    // Absorb measures a region by tile count and by whether it has an interior. Both tests can pass on a
    // region that still comes out as a solid blob of red, because neither of them knows how WIDE the
    // drawn line is. The line is one tile, plus whatever the corner-squaring adds, and it is drawn on
    // ONE side of each boundary — so a region roughly four or five tiles across has every one of its own
    // tiles either red or touching red, and what the player sees is a closed red loop with two or three
    // stranded cells in the middle of it. Both circled artefacts in the report are this: one where the
    // region carried the red itself (the clump), one where its neighbours did (the ring with a hole).
    //
    // The test is therefore about the PICTURE, not about the partition: how many tiles of this region
    // would a player see as ordinary ground — neither drawn as a boundary nor immediately beside one? A
    // region with almost none of those is not a landmass with a margin around it, it is a margin. It
    // joins the neighbour it shares the most edge with, and its boundary becomes part of theirs, which
    // is what "absorbed into the nearby faultlines" means.
    //
    // Which is why this runs AFTER the border has been marked, and re-marks it every pass: absorbing one
    // ringed region changes which tiles are boundary, and can leave the region that ate it ringed in
    // turn.
    // ============================================================================================

    /// How many tiles a region must have that are neither drawn as a boundary nor adjacent to one. Three
    /// is the same "does it have an inside" bar MinPlateCore sets, asked of the rendered map instead of
    /// the partition.
    const int MinUnringedTiles = 3;

    /// A backstop, not a budget — the loop exits the moment a pass moves nothing.
    const int RingPasses = 5;

    static void AbsorbRinged(TileMap map, int plateCount)
    {
        int w = map.width, h = map.height, n = w * h;

        var label = new int[n];
        var sizes = new List<int>();
        var stack = new Stack<int>();
        var share = new Dictionary<int, int>();
        var tilesPerPlate = new int[plateCount];

        for (int pass = 0; pass < RingPasses; pass++)
        {
            // A provisional line, so the test can be asked of the drawing. Cleared first because
            // MarkBorders only ever adds.
            System.Array.Clear(map.border, 0, n);
            MarkBorders(map);
            LabelRegions(map, label, sizes, stack);

            var free = new int[sizes.Count];
            var tilesOf = new List<int>[sizes.Count];
            for (int id = 0; id < sizes.Count; id++) tilesOf[id] = new List<int>(sizes[id]);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    tilesOf[label[i]].Add(i);
                    if (map.border[i] || TouchesBorder(map, x, y)) continue;
                    free[label[i]]++;
                }

            System.Array.Clear(tilesPerPlate, 0, tilesPerPlate.Length);
            for (int i = 0; i < n; i++) tilesPerPlate[map.plate[i]]++;
            int present = 0;
            for (int p = 0; p < plateCount; p++) if (tilesPerPlate[p] > 0) present++;

            // Smallest first, for the same reason Absorb does it: a speck joins the continent beside it
            // before that continent is itself measured.
            var offenders = new List<int>();
            for (int id = 0; id < sizes.Count; id++) if (free[id] < MinUnringedTiles) offenders.Add(id);
            if (offenders.Count == 0) break;
            offenders.Sort((a, b) => sizes[a].CompareTo(sizes[b]));

            bool moved = false;
            foreach (int id in offenders)
            {
                var tiles = tilesOf[id];
                if (tiles.Count == 0) continue;
                int mine = map.plate[tiles[0]];

                // Never delete the last plate the map can spare, and never take a plate's only region
                // when doing so would drop the count below the floor — the same guard Absorb applies.
                if (present - 1 < MinPlatesOnMap && tilesPerPlate[mine] == tiles.Count) continue;

                share.Clear();
                foreach (int i in tiles)
                {
                    int x = i % w, y = i / w;
                    Share(map, label, share, id, x + 1, y);
                    Share(map, label, share, id, x - 1, y);
                    Share(map, label, share, id, x, y + 1);
                    Share(map, label, share, id, x, y - 1);
                }

                int into = -1, best = -1;
                foreach (var kv in share) if (kv.Key != mine && kv.Value > best) { best = kv.Value; into = kv.Key; }
                if (into < 0) continue;

                foreach (int i in tiles) map.plate[i] = into;
                tilesPerPlate[mine] -= tiles.Count;
                tilesPerPlate[into] += tiles.Count;
                if (tilesPerPlate[mine] == 0) present--;
                moved = true;
            }

            if (!moved) break;
        }

        // The line is re-marked by the caller from the FINAL plate map; anything left here describes an
        // arrangement of plates that no longer exists.
        System.Array.Clear(map.border, 0, n);
    }

    static bool TouchesBorder(TileMap map, int x, int y)
    {
        int w = map.width, h = map.height;
        if (map.border[y * w + Wrap(x + 1, w)]) return true;
        if (map.border[y * w + Wrap(x - 1, w)]) return true;
        if (y + 1 < h && map.border[(y + 1) * w + x]) return true;
        if (y - 1 >= 0 && map.border[(y - 1) * w + x]) return true;
        return false;
    }

    // ============================================================================================
    // THINNING — red is a LINE, and the raster has to prove it
    //
    // Everything above works on the PARTITION and hopes the drawing follows. This works on the drawing
    // directly, and it exists because the two failure modes the report names are both statements about
    // the picture rather than about the plates:
    //
    //   A FILLED PATCH OF RED. A boundary is a crack between two landmasses. A solid block of it is not
    //   a crack that got wider, it is a shape — and a shape drawn in the colour reserved for "edge"
    //   reads as a landmass made of edge, which is not a thing.
    //   A LINE THAT STOPS. Boundaries close: they run into another boundary at a junction, or they run
    //   around and meet themselves. A red line trailing off into open ground announces a plate margin
    //   that goes nowhere, which no partition of a sphere can actually contain.
    //
    // THE ONE INVARIANT that must survive both passes: every pair of adjacent tiles belonging to
    // DIFFERENT plates has at least one red tile in it. `Separates` asks exactly that question of a
    // single tile, live against the current raster, and no tile that answers yes is ever removed — so
    // the thinning can never open a gap between two plates however far it erodes.
    // ============================================================================================

    /// Backstops. Each loop exits the moment a pass changes nothing; twelve is enough to erode a solid
    /// five-by-five patch down to a line from its corners inward.
    const int ThinPasses = 12;
    const int StubPasses = 12;

    // MEASURED (Node port of these passes, 24 worlds: 60x30 through 400x200, four to six seeds each,
    // against the same rasters run through the old thresholds and no cleanup at all):
    //
    //   2x2 blocks of red   13.0 per world  ->  1.3      and 94% of what survives is a genuine
    //                                           three-plate junction, which really is thicker than a
    //                                           line and is left alone on purpose.
    //   dead-end tiles       2.4 per world  ->  0.75     of which a sixth are on a pole row, where the
    //                                           map's edge genuinely IS where the boundary stops.
    //   unseparated pairs    0              ->  0        the invariant, never broken by either pass.
    //   plates on the map    unchanged in 20 of 24; one fewer in 4, which is the ringed absorption
    //                        doing exactly what it is for.
    //
    // The stubs that survive are single tiles that ARE separators — a one-cell flip of ownership, where
    // removing the red would put two plates in contact with nothing between them. Under one per world,
    // one tile each, and not removable without breaking the thing the line is for.

    static void ThinBorders(TileMap map)
    {
        int w = map.width, h = map.height;

        // ---- 1) No filled patches ----
        //
        // Break every 2x2 block of red by dropping ONE of its corners. Whichever three remain form an L,
        // which is still 4-connected, so this can never sever the line through the block itself — and
        // the corner chosen is one with no red neighbour OUTSIDE the block, so it cannot sever anything
        // attached to the block either. If no corner qualifies, the block is a genuine triple junction
        // where three margins meet, and it is left alone: that really is thicker than a line, and
        // drawing it thinner would be the lie.
        for (int pass = 0; pass < ThinPasses; pass++)
        {
            bool changed = false;
            for (int y = 0; y + 1 < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int xr = Wrap(x + 1, w);
                    int tl = y * w + x, tr = y * w + xr, bl = (y + 1) * w + x, br = (y + 1) * w + xr;
                    if (!map.border[tl] || !map.border[tr] || !map.border[bl] || !map.border[br]) continue;

                    int drop = -1;
                    if (Droppable(map, tl, x, y, -1, -1)) drop = tl;
                    else if (Droppable(map, tr, xr, y, 1, -1)) drop = tr;
                    else if (Droppable(map, bl, x, y + 1, -1, 1)) drop = bl;
                    else if (Droppable(map, br, xr, y + 1, 1, 1)) drop = br;
                    if (drop < 0) continue;

                    map.border[drop] = false;
                    changed = true;
                }
            if (!changed) break;
        }

        // ---- 2) No dead ends ----
        //
        // A red tile with at most one red neighbour is a leaf: nothing routes THROUGH it, so removing it
        // cannot disconnect the network. Repeated, this retracts a whole trailing stub one tile at a
        // time back to the junction it grew out of. Separator tiles are exempt, so a one-tile plate
        // margin that genuinely has to be drawn stays drawn.
        for (int pass = 0; pass < StubPasses; pass++)
        {
            bool changed = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!map.border[i]) continue;
                    if (Separates(map, i)) continue;
                    if (BorderDegree(map, x, y) > 1) continue;
                    map.border[i] = false;
                    changed = true;
                }
            if (!changed) break;
        }
    }

    /// May this corner of a 2x2 red block be dropped? Only if removing it opens no gap between two
    /// plates (`Separates`) AND it has no red neighbour outside the block — the two directions it does
    /// not share with the block are given as (dx, dy).
    static bool Droppable(TileMap map, int i, int x, int y, int dx, int dy)
    {
        if (Separates(map, i)) return false;
        int w = map.width, h = map.height;
        if (map.border[y * w + Wrap(x + dx, w)]) return false;
        int ny = y + dy;
        if (ny >= 0 && ny < h && map.border[ny * w + x]) return false;
        return true;
    }

    /// Would removing this red tile leave two neighbouring plates touching with nothing drawn between
    /// them? Asked LIVE against the current raster, which is what lets two adjacent red tiles on
    /// opposite sides of one margin protect each other: drop the first and the second immediately
    /// becomes the separator, so the second can no longer be dropped.
    static bool Separates(TileMap map, int i)
    {
        int w = map.width;
        int x = i % w, y = i / w;
        int me = map.plate[i];
        return NeedsMe(map, me, x + 1, y) || NeedsMe(map, me, x - 1, y)
            || NeedsMe(map, me, x, y + 1) || NeedsMe(map, me, x, y - 1);
    }

    static bool NeedsMe(TileMap map, int me, int x, int y)
    {
        if (y < 0 || y >= map.height) return false;
        int i = y * map.width + Wrap(x, map.width);
        return map.plate[i] != me && !map.border[i];
    }

    static int BorderDegree(TileMap map, int x, int y)
    {
        int w = map.width, h = map.height, d = 0;
        if (map.border[y * w + Wrap(x + 1, w)]) d++;
        if (map.border[y * w + Wrap(x - 1, w)]) d++;
        if (y + 1 < h && map.border[(y + 1) * w + x]) d++;
        if (y - 1 >= 0 && map.border[(y - 1) * w + x]) d++;
        return d;
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
