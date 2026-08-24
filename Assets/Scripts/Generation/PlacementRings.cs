using UnityEngine;

// ============================================================================================
// WHERE A SYSTEM IS ALLOWED TO PUT THINGS
//
// A system used to be laid out by WALKING OUTWARD: start just past the star, put something down,
// measure how much room it and its moons need, add a gap, and repeat. Every radius was therefore a
// consequence of everything inside it — which has two failures the request names directly.
//
//   EVERYTHING PILES UP AT THE CENTRE. With five bodies instead of nine, the walk simply stops after
//   five steps and the outer system is empty. There is no such thing as a gap in a walk; the fifth
//   planet is always the fifth step out, however few there are.
//
//   THE HABITABLE ZONE IS WHEREVER IT LANDS. The zone is a property of the STAR and sits at a fixed
//   multiple of its reference distance. The walk knew nothing about it, so whether any lane fell
//   inside it was luck — and once the star's own clearance grew past the zone's outer edge, it became
//   luck that had run out. See StarDatabase.AU for the arithmetic.
//
// So placement is now a fixed LADDER of nine rings per star, and the generator's only decision per
// ring is fill it or skip it. An empty ring is a real gap at a real distance, so a three-body system
// reads as sparse rather than as huddled, and the ring the habitable zone falls on is known before
// anything is placed rather than discovered afterwards.
//
// ---- WHY THESE NINE MULTIPLES ----------------------------------------------------------------
//
// Quoted as multiples of R = StarDatabase.ReferenceDistance(star), the distance at which THIS star's
// warmth is Earth-normal — so the ladder scales with the star automatically and a red dwarf's rings
// huddle in exactly as its zone does. A star's own habitable zone is 0.80R .. 1.55R (StarData).
//
// The ladder is roughly geometric at 1.4x, which is the spacing real systems actually have (the
// Titius-Bode observation), and it is anchored so that:
//
//     RINGS 1, 2, 3 ARE INSIDE THE ZONE.    RINGS 4 AND 5 ARE IN IT.    6 THROUGH 9 ARE BEYOND.
//
// which is the request's own worked example — "the habitable zone for our real sun encompasses Earth
// and even Mars, but does not encompass the closest 2 planets to the sun":
//
//     ring   xR     G-type (R=40)   in 32-62?   Sol
//      1    0.36        14.4           no       Mercury  0.39 AU
//      2    0.52        20.8           no       Venus    0.72
//      3    0.70        28.0           no       -
//      4    0.95        38.0          YES       Earth    1.00
//      5    1.30        52.0          YES       Mars     1.52
//      6    1.80        72.0           no       the belt, at the frost line
//      7    2.55       102.0           no       Jupiter
//      8    3.60       144.0           no       Saturn
//      9    5.10       204.0           no       Uranus / Neptune
//
// ---- THE RINGS ARE GENERATION-TIME ONLY ------------------------------------------------------
//
// Nothing at runtime snaps to them. The Dev orbit slider and terraforming's orbit-moving project both
// write `orbitRadius` directly and are bounded by OrbitSafety, which knows nothing about this file —
// deliberately, because "move this planet to a warmer orbit" means any radius that is safe, not the
// nearest ring. What the rings do give those two is a meaningful home to return to:
// `naturalOrbitRadius` records the ring a world was born on.
// ============================================================================================
public static class PlacementRings
{
    /// The most rings a system can have, and therefore the most orbital lanes. "Lets have up to 9
    /// placement rings for up to 9 celestial bodies (max) or asteroid fields."
    public const int Count = 9;

    /// Ring radii as multiples of the star's reference distance. See the header for the derivation.
    static readonly float[] Multiples = { 0.36f, 0.52f, 0.70f, 0.95f, 1.30f, 1.80f, 2.55f, 3.60f, 5.10f };

    /// Room reserved on ring 1 for the body that will sit there, on top of the star's own clearance.
    /// A small inner world plus a little air — enough that ring 1 is never a radius OrbitSafety will
    /// immediately have to push outward.
    const float InnerBodyReach = 1.2f;

    /// This star's nine ring radii, written into `into` (which must hold at least `Count`).
    ///
    /// Returns the number written, which is always `Count` — the array is handed in rather than
    /// allocated because generation asks for it once per system and there is no reason to make
    /// garbage doing it.
    /// How many rings sit inside the habitable zone. Rings 1-3 (indices 0-2); the zone lands on 4 and 5.
    /// The one invariant this whole file exists to hold: an Earthlike world is never the closest thing
    /// to its sun.
    public const int InnerRings = 3;

    public static int Radii(StarData star, float[] into)
    {
        if (into == null || into.Length < Count) return 0;

        float r = Mathf.Max(1f, StarDatabase.ReferenceDistance(star));
        for (int i = 0; i < Count; i++) into[i] = Multiples[i] * r;

        // ---- WHEN THE INNER RINGS DO NOT FIT, COMPRESS THEM. DO NOT SHIFT THE LADDER. -------------
        //
        // Around a dim star the first rings can fall inside the star's own clearance, and there are
        // three ways to deal with that. Two of them are wrong:
        //
        //   DROP THEM. Changes how many rings a red dwarf has AND which index its zone lands on, so
        //   "ring 4 is the Earth ring" stops being true exactly where systems are most cramped.
        //
        //   SHIFT THE WHOLE LADDER OUTWARD BY THE DEFICIT. This is what this did first, and the
        //   composition check caught it: with stars at 4x, an M dwarf needs ~10.5 units of clearance
        //   against a natural ring 1 of 6.5, so everything moved out by 4 — which pushed ring 3 from
        //   12.6 to 16.6, and that star's zone starts at 14.4. Ring 3 landed IN the zone, and the
        //   Earthlike world was third from the sun. That is a milder version of the exact bug this
        //   file was written to fix, reintroduced by the fix for it.
        //
        // So the OUTER rings never move — they are the ones whose relationship to the zone matters —
        // and the inner three are compressed into whatever room actually exists between the star and
        // the zone's inner edge. Physically that is also the honest answer: a dim star's inner system
        // IS squeezed up against it.
        float need = OrbitSafety.StarRadius(star) + OrbitSafety.StarClearance + InnerBodyReach;
        if (into[0] >= need) return Count;                 // the common case: K-type and up, nothing to do

        // The ceiling for the compressed rings: the zone's inner edge if this star has a zone, and
        // otherwise the first outer ring, which is the same question asked of a star with no zone.
        float hi = (star != null && star.hasHabitableZone && star.hzInner > need)
                 ? star.hzInner
                 : into[InnerRings];

        if (hi <= need)
        {
            // No room between the star and the zone at all — a very large, very dim star, which the
            // class tables do not actually produce. Fall back to the rigid shift: an inner ring inside
            // the zone is bad, but a ring inside the STAR is worse, and OrbitSafety would push it out
            // anyway.
            float shift = need - into[0];
            for (int i = 0; i < Count; i++) into[i] += shift;
            return Count;
        }

        // Spaced across [need, hi) at the midpoints of equal slices, so the outermost compressed ring
        // stays strictly below `hi` and the innermost stays strictly above `need`.
        for (int i = 0; i < InnerRings; i++)
            into[i] = Mathf.Lerp(need, hi, (i + 0.5f) / InnerRings);

        return Count;
    }

    /// Which rings fall inside this star's own habitable zone. Written into `into` as booleans.
    ///
    /// Asked BEFORE anything is placed, which is the whole point of a fixed ladder: the generator can
    /// guarantee a world in the zone by choosing to fill a ring, rather than by placing worlds and
    /// then moving one — which is what used to fight OrbitSafety and lose.
    public static void InZone(StarData star, float[] radii, bool[] into)
    {
        if (radii == null || into == null) return;
        for (int i = 0; i < Count && i < into.Length; i++) into[i] = false;
        if (star == null || !star.hasHabitableZone) return;

        for (int i = 0; i < Count && i < radii.Length && i < into.Length; i++)
            into[i] = radii[i] >= star.hzInner && radii[i] <= star.hzOuter;
    }

    /// The ring nearest this star's habitable-zone centre, or -1 if it has no zone.
    ///
    /// The fallback when no ring lands INSIDE the zone — which the ladder is built so as to avoid, but
    /// which a heavily-shifted dim star could still produce. A world on the nearest ring to the centre
    /// is the best available answer and is never a bad one, because the zone's edges are soft
    /// (Habitability.Rate scores outside the band rather than zeroing).
    public static int NearestToZone(StarData star, float[] radii)
    {
        if (star == null || !star.hasHabitableZone || radii == null) return -1;

        float centre = star.HzCenter;
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < Count && i < radii.Length; i++)
        {
            float d = Mathf.Abs(radii[i] - centre);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// The first ring inside the zone, or `NearestToZone` if none is. Never -1 for a star with a zone.
    ///
    /// The one call generation actually makes: "which ring must I fill to guarantee a liveable world".
    public static int HabitableRing(StarData star, float[] radii, bool[] inZone)
    {
        if (star == null || !star.hasHabitableZone) return -1;
        if (inZone != null)
            for (int i = 0; i < Count && i < inZone.Length; i++)
                if (inZone[i]) return i;
        return NearestToZone(star, radii);
    }
}
