using System.Collections.Generic;
using UnityEngine;

// The single authority on how much room an orbiting body needs, and the guarantee that no two orbits
// in a system ever intersect.
//
// WHY THIS CAN BE GUARANTEED AT ALL
// Two bodies orbiting one centre are each at some distance from it. By the triangle inequality the gap
// between them is at least the difference of those distances — no tilt, phase or eccentricity can beat
// that. So "orbits never collide" reduces to a one-dimensional problem: reserve a non-overlapping BAND
// of radius for each body, and nothing can ever touch. Everything below is bookkeeping on those bands.
//
// A planet's band is not its orbit line. It is its own disc PLUS the entire reach of its moon system,
// because the moons sweep inward and outward as the planet goes round. Two planets whose bands overlap
// will eventually collide even if their orbit radii differ.
//
// The numbers here are the ONLY place body sizes are defined. They used to be copied into
// SystemVisualizer and both generators, so a change in one silently broke spacing in the others.
public static class OrbitSafety
{
    // ---- Visual size ----
    // Now derived from MASS (MassRules.VisualDiameter), not from surfaceSize. surfaceSize is a rounded,
    // clamped integer, and running the render size through it flattened every body under ~mass 2.3 to one
    // identical dot — a 0.1 moon and a 2.0 moon drew the same. See the note in MassRules.
    //
    // These constants are kept only to size a body whose mass is missing (pre-Mass saves, before the
    // backfill runs). Nothing in normal generation reaches them.
    public const float PlanetScalePerSize = 0.08f;
    public const float PlanetScaleMin = 0.6f;
    public const float MoonScalePerSize = 0.05f;
    public const float MoonScaleMin = 0.35f;

    // ---- Clearances ----
    public const float MoonSurfaceGap = 0.9f;   // air between a planet's surface and its innermost moon
    public const float MoonGap = 1.4f;          // air between one moon's disc and the next
    public const float LaneGap = 6f;            // air between one planet's band and the next
    public const float StarClearance = 8f;      // air between the star and the innermost planet's band

    /// Rendered diameter of a body, exactly as SystemVisualizer scales it.
    public static float Scale(CelestialBody b)
    {
        if (b == null) return PlanetScaleMin;
        bool isMoon = b.parentBody != null || b.type == CelestialBodyType.Moon;

        // A pre-Mass save can arrive with mass 0 before the backfill runs. Fall back to the old
        // surfaceSize formula in that case rather than rendering it at the floor.
        if (b.mass <= 0.0001f)
            return isMoon
                ? Mathf.Max(MoonScaleMin, b.surfaceSize * MoonScalePerSize)
                : Mathf.Max(PlanetScaleMin, b.surfaceSize * PlanetScalePerSize);

        return MassRules.VisualDiameter(b.mass, isMoon);
    }

    /// Rendered RADIUS of a body (scale is a diameter — forgetting the halving is an easy way to
    /// reserve twice the room you need, or half).
    public static float DiscRadius(CelestialBody b) => Scale(b) * 0.5f;

    /// How far the STAR (or bound star cluster) reaches from the system barycenter — the amount the
    /// innermost planet has to clear. For a single sun it's just its render radius; for a binary/ternary
    /// it's the whole cluster's reach (StarData.clusterRadius, set from the same layout the renderer uses),
    /// so planets can't be placed inside a pair of suns.
    public static float StarRadius(StarData star)
    {
        if (star == null) return 1f;
        return star.clusterRadius > 0.001f ? star.clusterRadius : star.visualScale * 0.5f;
    }

    /// How far a planet's whole system reaches from its orbit line: its own disc, or its outermost
    /// moon's orbit plus that moon's disc — whichever is further.
    public static float SystemReach(CelestialBody b)
    {
        if (b == null) return 0f;
        float reach = DiscRadius(b);
        if (b.moons != null)
            foreach (var m in b.moons)
                if (m != null) reach = Mathf.Max(reach, m.orbitRadius + DiscRadius(m));
        return reach;
    }

    /// The band [inner, outer] of radius a planet's system occupies.
    public static void Band(CelestialBody b, out float inner, out float outer)
    {
        float reach = SystemReach(b);
        inner = b.orbitRadius - reach;
        outer = b.orbitRadius + reach;
    }

    // ---- Moons ----
    /// Re-space a planet's moons so the innermost clears the planet's surface and each clears the one
    /// before it. Idempotent, and safe to call after anything changes a planet's size or moon list.
    public static void SpaceMoons(CelestialBody planet)
    {
        if (planet?.moons == null || planet.moons.Count == 0) return;

        float r = DiscRadius(planet) + MoonSurfaceGap;
        foreach (var m in planet.moons)
        {
            if (m == null) continue;
            float md = DiscRadius(m);
            r += md;                       // this moon's near edge must clear what came before
            if (m.orbitRadius < r) m.orbitRadius = r;
            else r = m.orbitRadius;        // already further out: keep the generator's spacing
            r += md + MoonGap;             // reserve its far edge plus air for the next
            m.orbitSpeed = OrbitalMechanics.MoonAngularSpeed(planet, m.orbitRadius);
        }
    }

    // ---- Systems ----
    /// THE GUARANTEE. Walks a system outward and pushes any planet whose band overlaps its inner
    /// neighbour (or the star) far enough out to clear it. Also re-spaces every moon system first, so a
    /// planet's band is correct before it's used.
    ///
    /// Idempotent: running it on an already-safe system changes nothing. Call it after generation and
    /// after ANYTHING moves or resizes a body.
    public static void EnforceSystem(List<CelestialBody> bodies, StarData star)
    {
        if (bodies == null || bodies.Count == 0) return;

        foreach (var b in bodies) SpaceMoons(b);

        // Work outward. Sorting a copy means the caller's list order (which save/load and the UI rely
        // on) is left alone.
        var sorted = new List<CelestialBody>(bodies);
        sorted.Sort((x, y) => x.orbitRadius.CompareTo(y.orbitRadius));

        float starRadius = StarRadius(star);
        float prevOuter = starRadius + StarClearance;

        foreach (var b in sorted)
        {
            if (b == null) continue;
            float reach = SystemReach(b);
            float minRadius = prevOuter + reach;

            if (b.orbitRadius < minRadius)
            {
                b.orbitRadius = minRadius;
                b.distanceFromStar = minRadius;
                if (star != null) b.orbitSpeed = OrbitalMechanics.PlanetAngularSpeed(star, minRadius);
                // Moons share their planet's solar distance, and their climate is derived from it.
                if (b.moons != null)
                    foreach (var m in b.moons) if (m != null) m.distanceFromStar = minRadius;
            }

            prevOuter = b.orbitRadius + reach + LaneGap;
        }
    }

    /// The radius range a body may occupy without touching its neighbours in this system.
    /// Returns false when there's no legal room at all (so: don't move it).
    public static bool AllowedRange(List<CelestialBody> system, CelestialBody body, StarData star,
                                    out float lo, out float hi)
    {
        lo = 0f; hi = 0f;
        if (system == null || body == null) return false;

        float reach = SystemReach(body);
        float starRadius = StarRadius(star);
        lo = starRadius + StarClearance + reach;
        hi = float.MaxValue;

        foreach (var other in system)
        {
            if (other == null || other == body) continue;
            float otherReach = SystemReach(other);
            float need = reach + otherReach + LaneGap;

            if (other.orbitRadius < body.orbitRadius) lo = Mathf.Max(lo, other.orbitRadius + need);
            else hi = Mathf.Min(hi, other.orbitRadius - need);
        }
        return hi > lo;
    }

    /// Clamp a desired radius into the room the neighbours leave. Returns false if there's no room,
    /// in which case the body must not move at all.
    public static bool ClampRadius(List<CelestialBody> system, CelestialBody body, StarData star,
                                   float desired, out float safe)
    {
        safe = body != null ? body.orbitRadius : 0f;
        if (!AllowedRange(system, body, star, out float lo, out float hi)) return false;
        safe = Mathf.Clamp(desired, lo, hi);
        return true;
    }

    /// The min and max orbit RADIUS a planet may sensibly occupy around its star, derived from the star's
    /// SIZE and how far its light and gravity reach (its luminosity — the same distance scale the habitable
    /// zone uses). The Dev-Mode orbit editor uses this as the radius slider's range, so you can fly a planet
    /// from just clear of the star's surface out toward the edge of the system and back. The minimum keeps a
    /// planet from clipping into the star (star radius + the same StarClearance generation reserves); the
    /// maximum keeps it within the star's reach — a big, bright sun holds planets far further out than a dim
    /// red dwarf. Callers should still widen `max` to at least the body's current radius so an already-outer
    /// world isn't clamped inward.
    public static void OrbitLimits(StarData star, out float min, out float max)
    {
        float starRadius = StarRadius(star);
        min = starRadius + StarClearance;
        float lum = star != null ? Mathf.Max(0.05f, star.luminosity) : 1f;
        max = min + Mathf.Sqrt(lum) * StarDatabase.AU * 12f;
    }

    // ---- Diagnostics ----
    /// Any overlapping bands in this system? Used by the generators to assert their own output.
    public static bool Validate(List<CelestialBody> bodies, out string problem)
    {
        problem = null;
        if (bodies == null) return true;

        var sorted = new List<CelestialBody>(bodies);
        sorted.Sort((x, y) => x.orbitRadius.CompareTo(y.orbitRadius));

        for (int i = 1; i < sorted.Count; i++)
        {
            Band(sorted[i - 1], out _, out float prevOuter);
            Band(sorted[i], out float inner, out _);
            if (inner < prevOuter)
            {
                problem = $"{sorted[i].name} (band from {inner:F1}) overlaps {sorted[i - 1].name} " +
                          $"(band to {prevOuter:F1})";
                return false;
            }
        }

        // Moons inside their own planet, or through each other.
        foreach (var b in sorted)
        {
            if (b?.moons == null) continue;
            float planetEdge = DiscRadius(b);
            float prevEdge = planetEdge;
            var ms = new List<CelestialBody>(b.moons);
            ms.Sort((x, y) => x.orbitRadius.CompareTo(y.orbitRadius));
            foreach (var m in ms)
            {
                if (m == null) continue;
                float md = DiscRadius(m);
                if (m.orbitRadius - md < prevEdge)
                {
                    problem = $"moon {m.name} (inner edge {m.orbitRadius - md:F2}) intersects " +
                              $"{(prevEdge == planetEdge ? b.name + "'s surface" : "the moon inside it")} " +
                              $"(edge {prevEdge:F2})";
                    return false;
                }
                prevEdge = m.orbitRadius + md;
            }
        }
        return true;
    }
}
