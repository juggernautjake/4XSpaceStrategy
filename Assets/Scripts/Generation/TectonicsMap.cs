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
// ============================================================================================
public static class TectonicsMap
{
    public class Plate
    {
        public int id;
        public Vector2 site;     // plate centre in FEATURE SPACE (u scaled to the map's 2:1 width, v as-is)
        public Vector2 motion;   // push direction (unit) * strength — feature-space vector
        public float strength;   // |motion|, 0..1; kept apart so the overlay can size its arrow by it
    }

    public class Layout
    {
        public Plate[] plates;
        public float builtForSeed;   // the terrainSeed this was derived from; rebuild if the world reseeds
        public int builtForSize;     // and the surfaceSize — plate COUNT depends on it (see Build), and a
                                     // Dev-sandbox size edit changes size without touching the seed
    }

    public struct Hit
    {
        public int plateA;        // nearest plate — the plate this point belongs to
        public int plateB;        // second-nearest plate — the plate across the closest fault
        public float boundary;    // 0 (deep in a plate interior) .. 1 (right on a fault line)
        public float convergence; // relative motion across that fault: >0 plates driven TOGETHER
                                  // (compression -> mountains/volcanoes), <0 pulled apart (a rift)
    }

    // The surface map is 2:1 — u spans twice the physical width of v (PlanetTerrainGenerator samples with
    // fx = u * freq * 2). Scaling u by 2 into "feature space" keeps plates roughly square on the map and
    // makes the distance metric isotropic, so faults don't come out stretched along the longitude axis.
    const float UStretch = 2f;

    // How wide the fault band reads, in feature-space distance-difference units. Calibrated by eye (there
    // is no Editor here to playtest against) so a fault is ~1-3 grid cells wide at the sizes this game
    // uses, widening naturally where three plates meet because two boundaries overlap there.
    const float FaultWidth = 0.05f;

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
        // Keyed by id, but re-derived if the world's seed OR size changed under it (a reseed or a size drag
        // in the Dev sandbox, a remodel) — so the cache can never describe the plates of a world this one
        // used to be. Guarding both is also what makes id reuse across a New Game / Load safe: a fresh body
        // reusing an old id but carrying a different seed/size misses the cache and rebuilds.
        if (cache.TryGetValue(b.id, out var l) &&
            Mathf.Approximately(l.builtForSeed, b.terrainSeed) && l.builtForSize == b.surfaceSize)
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

        // Plate count: a handful, more on bigger worlds. 4..9.
        int n = 4 + rng.Next(0, 4) + Mathf.Clamp(b.surfaceSize / 9, 0, 2);

        var plates = new Plate[n];
        for (int i = 0; i < n; i++)
        {
            float angle = R() * Mathf.PI * 2f;
            float strength = Mathf.Lerp(0.35f, 1f, R());   // no plate is truly motionless
            plates[i] = new Plate
            {
                id = i,
                site = new Vector2(R() * UStretch, R()),   // u in [0,UStretch) (wraps), v in [0,1)
                motion = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * strength,
                strength = strength
            };
        }

        return new Layout { plates = plates, builtForSeed = b.terrainSeed, builtForSize = b.surfaceSize };
    }

    // The shortest delta from a->b along x, accounting for the longitude wrap at u=1 (feature x wraps at
    // UStretch). y (latitude) does not wrap.
    static Vector2 WrappedDelta(Vector2 from, Vector2 to)
    {
        float dx = to.x - from.x;
        dx -= Mathf.Round(dx / UStretch) * UStretch;   // fold into [-UStretch/2, UStretch/2]
        return new Vector2(dx, to.y - from.y);
    }

    // The plate geometry at a normalized (u,v). Cheap: an O(plates) scan (4..9) over the cached layout.
    public static Hit Sample(CelestialBody b, float u, float v)
    {
        var l = Get(b);
        if (l == null || l.plates == null || l.plates.Length == 0) return default;

        Vector2 p = new Vector2(u * UStretch, v);

        int iA = -1, iB = -1;
        float d1 = float.MaxValue, d2 = float.MaxValue;
        for (int i = 0; i < l.plates.Length; i++)
        {
            Vector2 d = WrappedDelta(l.plates[i].site, p);
            float dist = d.sqrMagnitude;
            if (dist < d1) { d2 = d1; iB = iA; d1 = dist; iA = i; }
            else if (dist < d2) { d2 = dist; iB = i; }
        }

        Hit hit = new Hit { plateA = iA, plateB = iB, boundary = 0f, convergence = 0f };
        if (iB < 0) return hit;   // only one plate: no faults anywhere

        // Proximity to the fault = how close the two nearest plates are to equidistant. The difference of
        // distances is ~0 on the bisector and grows as you move into a plate's interior; map that gap
        // through FaultWidth so boundary is 1 on the line and 0 a band's-width away. This is a heuristic
        // proximity (not an exact geodesic width), which is all the overlay and the ridge bias need.
        float gap = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
        hit.boundary = 1f - Mathf.Clamp01(gap / FaultWidth);

        // Convergence across that fault: does plate A drive INTO plate B faster than B pulls away?
        // n points from A's centre toward B's; project the plates' relative motion onto it. >0 compresses
        // the boundary (mountains, volcanoes), <0 opens a rift.
        Vector2 nrm = WrappedDelta(l.plates[iA].site, l.plates[iB].site);
        if (nrm.sqrMagnitude > 1e-6f)
        {
            nrm.Normalize();
            Vector2 vrel = l.plates[iA].motion - l.plates[iB].motion;
            hit.convergence = Mathf.Clamp(Vector2.Dot(vrel, nrm) * 0.5f, -1f, 1f);
        }
        return hit;
    }

    // Where a plate's direction arrow sits on the MAP and which way it points. The site lives in feature
    // space (u scaled by UStretch); convert back to normalized (u,v) for placement, and un-stretch the
    // motion's x so the drawn arrow points the right way on the 2:1 map rather than skewed toward the pole.
    public static void ArrowOnMap(Plate p, out float u, out float v, out Vector2 dir, out float strength)
    {
        u = Mathf.Repeat(p.site.x / UStretch, 1f);
        v = Mathf.Clamp01(p.site.y);
        dir = new Vector2(p.motion.x / UStretch, p.motion.y);
        strength = p.strength;
    }
}
