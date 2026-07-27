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
// ---- WHY THE MARGINS ARE RAGGED --------------------------------------------------------------
// Great-circle arcs plus a domain warp gave plates that were CURVED but smooth — soap bubbles, not
// continents. The roughness that fixes it is not more warp (see the note on EdgeTerms for why that
// approach is a dead end); it is a small scalar field added to the fault's own signed distance, in
// TILES, which slides the boundary sideways without touching the space it is drawn in. Sample corrects
// the band width for that field's gradient, so the line wanders without pinching or fattening.
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
        public Vector3 site;     // plate centre as a UNIT VECTOR on the sphere
        public Vector3 motion;   // push, tangent to the sphere at `site`; magnitude == strength
        public float strength;   // |motion|, 0..1; kept apart so the overlay can size its arrow by it
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
        public int plateA;        // nearest plate — the plate this point belongs to
        public int plateB;        // second-nearest plate — the plate across the closest fault
        public float boundary;    // 0 (off the fault) .. 1 (right on the fault line) — the DRAWN hairline
        public float belt;        // 0 .. 1 mountain-building influence — far wider than `boundary`, and
                                  // ragged, so ranges are not confined to the red line on the overlay
        public float convergence; // relative motion across that fault: >0 plates driven TOGETHER
                                  // (compression -> mountains/volcanoes), <0 pulled apart (a rift)
    }

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

    // ---- Domain warp -----------------------------------------------------------------------------
    // Two bands of plane waves. The coarse band sweeps whole boundaries off the great circles they would
    // otherwise follow; the fine band gives them their wander. Amplitude per term is Gain/|freq|, which
    // fixes each term's GRADIENT at Gain — that is the number that matters, because the gradient is both
    // how much a boundary bends and how close the warp comes to folding space over on itself. Six terms
    // at these gains sit comfortably under the fold.
    const int CoarseTerms = 3, FineTerms = 3;
    const float CoarseFreqMin = 2.0f, CoarseFreqMax = 4.0f, CoarseGain = 0.40f;
    const float FineFreqMin = 6.0f, FineFreqMax = 11.0f, FineGain = 0.45f;

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

    // A world has plate geometry iff it rolled tectonics. NOTE: deliberately does NOT require b.surface —
    // the terrain generator queries this WHILE it is baking that very surface (body.surface is still null
    // then), and the geometry only needs the seed, not the grid.
    public static bool Active(CelestialBody b) => b != null && b.hasTectonics;

    public static void Invalidate(CelestialBody b) { if (b != null) cache.Remove(b.id); }
    public static void InvalidateAll() => cache.Clear();

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

        // Sites, spread by best-candidate sampling. Uniform random points on a sphere clump, and a clump is
        // exactly what produces two near-coincident plates with a boundary that runs half way round the
        // world. Drawing a few candidates and keeping the one furthest from everything already placed
        // (Mitchell's best-candidate) gives a roughly Poisson-disc spread: plates of comparable size,
        // boundaries that meet in short segments and honest triple junctions. Five candidates rather than
        // a dozen — enough to break up the clumps, few enough that the layout doesn't come out regimented.
        var sites = new List<Vector3>(n);
        while (sites.Count < n)
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

        var plates = new Plate[sites.Count];
        for (int i = 0; i < sites.Count; i++)
        {
            // A push TANGENT to the sphere at the site: a random direction in the local tangent plane.
            Vector3 s = sites[i];
            Vector3 up = Mathf.Abs(s.y) > 0.95f ? Vector3.right : Vector3.up;
            Vector3 e = Vector3.Cross(up, s).normalized;
            Vector3 nth = Vector3.Cross(s, e);
            float a = R() * Mathf.PI * 2f;
            float strength = Mathf.Lerp(0.35f, 1f, R());   // no plate is truly motionless
            plates[i] = new Plate
            {
                id = i,
                site = s,
                motion = (e * Mathf.Cos(a) + nth * Mathf.Sin(a)) * strength,
                strength = strength
            };
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

        int iA = -1, iB = -1;
        float c1 = -2f, c2 = -2f;
        for (int i = 0; i < l.plates.Length; i++)
        {
            float c = Vector3.Dot(l.plates[i].site, q);   // on a unit sphere, nearest == largest dot
            if (c > c1) { c2 = c1; iB = iA; c1 = c; iA = i; }
            else if (c > c2) { c2 = c; iB = i; }
        }

        Hit hit = new Hit { plateA = iA, plateB = iB, boundary = 0f, belt = 0f, convergence = 0f };
        if (iB < 0) return hit;   // only one plate: no faults anywhere

        Vector3 A = l.plates[iA].site, B = l.plates[iB].site;

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

        float angReal = Mathf.Abs(signed / stretch + offset) / Mathf.Max(0.45f, Mathf.Abs(1f + slope));

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
        // is the tangent at A pointing toward B; project the plates' relative motion onto it. >0 compresses
        // the boundary (mountains, volcanoes), <0 opens a rift.
        Vector3 nrm = B - A * Vector3.Dot(A, B);
        if (nrm.sqrMagnitude > 1e-6f)
        {
            nrm.Normalize();
            Vector3 vrel = l.plates[iA].motion - l.plates[iB].motion;
            hit.convergence = Mathf.Clamp(Vector3.Dot(vrel, nrm) * 0.5f, -1f, 1f);
        }
        return hit;
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
