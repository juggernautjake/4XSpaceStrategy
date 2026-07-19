using System.Collections.Generic;
using UnityEngine;

// The geometry of a gravitationally-bound star cluster — where each sun sits and orbits relative to the
// system's barycenter. ONE authority for this, used by both the renderer (to place and orbit the suns)
// and OrbitSafety (to keep planets clear of the whole cluster, not just one sun).
//
// MODEL
//   Single  — the lone sun sits at the barycenter (handled by the caller, not here).
//   Binary  — two suns orbit their shared barycenter. The barycenter divides the line between them in
//             INVERSE proportion to mass, so the heavier sun orbits on a SMALLER circle (closer in) and
//             the lighter one swings wider — r_heavy / r_light = m_light / m_heavy. Same angular speed,
//             180 deg apart, so they stay diametrically opposite.
//   Trinary — Alpha-Centauri style. Suns [0] and [1] form a CLOSE inner pair orbiting their own local
//             barycenter (fast). That pair's barycenter, carrying the pair's combined mass, then orbits
//             the SYSTEM barycenter together with the third sun (slow) — again split by mass, so the
//             heavier side (usually the pair) rides the smaller, closer circle.
//
// All radii are in world/orbit units and are laid out from the suns' own render sizes so they never
// overlap. `reach` is how far the outermost sun's SURFACE gets from the system barycenter — the number
// orbit spacing needs.
public class StarCluster
{
    // One sun's orbit within the cluster.
    public struct Orbit
    {
        public float radius;    // orbit radius about its immediate centre
        public float phase;     // starting angle, degrees
        public float speed;     // angular speed, deg/sec
        public bool aboutPair;  // true = orbits the inner-pair barycenter; false = orbits the system barycenter
    }

    public Orbit[] orbits;      // one per sun, in the same order as the input list
    public bool hasPair;        // trinary: an inner-pair barycenter exists and itself orbits the system centre
    public float pairRadius, pairPhase, pairSpeed;   // that inner-pair barycenter's orbit about the system centre
    public float reach;         // farthest a sun's surface reaches from the system barycenter

    // Air between two bound suns' surfaces, and between the inner pair and the third sun.
    const float PairGap = 6f;

    // Visual angular speeds (deg/sec) — tuned for appeal, not real Kepler: an inner pair whirls quickly,
    // an outer orbit drifts slowly.
    const float BinarySpeed = 12f;
    const float InnerPairSpeed = 18f;
    const float OuterSpeed = 6f;

    // Separation between two suns of render size (diameter) a and b: their two radii plus a gap.
    static float Separation(float a, float b) => (a + b) * 0.5f + PairGap;

    public static StarCluster Layout(List<StarData> stars)
    {
        var c = new StarCluster();
        int n = stars != null ? stars.Count : 0;

        if (n <= 1)
        {
            c.orbits = new Orbit[0];
            c.reach = n == 1 && stars[0] != null ? stars[0].visualScale * 0.5f : 1f;
            return c;
        }

        float M(int i) => Mathf.Max(0.001f, stars[i].mass);
        float S(int i) => Mathf.Max(0.05f, stars[i].visualScale);   // diameter
        float Disc(int i) => S(i) * 0.5f;

        if (n == 2)
        {
            float d = Separation(S(0), S(1));
            float tot = M(0) + M(1);
            // Heavier sun (larger mass) gets the SMALLER radius: its radius is proportional to the OTHER's mass.
            float r0 = d * M(1) / tot;
            float r1 = d * M(0) / tot;
            c.orbits = new[]
            {
                new Orbit { radius = r0, phase = 0f,   speed = BinarySpeed, aboutPair = false },
                new Orbit { radius = r1, phase = 180f, speed = BinarySpeed, aboutPair = false },
            };
            c.hasPair = false;
            c.reach = Mathf.Max(r0 + Disc(0), r1 + Disc(1));
            return c;
        }

        // Trinary (n >= 3): suns [0],[1] are the inner pair, [2] is the third. Generation never rolls more
        // than three, so any beyond the third are ignored here (guarded, not silently dropped in practice).
        float din = Separation(S(0), S(1));
        float mPair = M(0) + M(1);
        float ri0 = din * M(1) / mPair;   // inner pair, split by mass about their local barycenter
        float ri1 = din * M(0) / mPair;
        float innerReach = Mathf.Max(ri0 + Disc(0), ri1 + Disc(1));   // how far the pair extends from its own barycenter

        // Outer orbit: the pair (as a blob of radius innerReach, carrying mass mPair) and the third sun orbit
        // the system barycenter, split by mass. The pair is usually heavier, so it rides the closer circle.
        float m3 = M(2);
        float dout = innerReach + Disc(2) + PairGap;
        float totOut = mPair + m3;
        c.pairRadius = dout * m3 / totOut;    // pair barycenter's distance from the system centre
        c.pairPhase = 0f;
        c.pairSpeed = OuterSpeed;
        c.hasPair = true;

        float rThird = dout * mPair / totOut; // the third sun's distance from the system centre

        c.orbits = new[]
        {
            new Orbit { radius = ri0,    phase = 0f,   speed = InnerPairSpeed, aboutPair = true  },
            new Orbit { radius = ri1,    phase = 180f, speed = InnerPairSpeed, aboutPair = true  },
            new Orbit { radius = rThird, phase = 180f, speed = OuterSpeed,     aboutPair = false },
        };
        // The whole pair swings out to pairRadius + innerReach; the third sun's surface reaches rThird + its disc.
        c.reach = Mathf.Max(rThird + Disc(2), c.pairRadius + innerReach);
        return c;
    }
}
