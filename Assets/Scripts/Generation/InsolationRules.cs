using UnityEngine;

// ============================================================================================
// INSOLATION — how hot a world is because of WHERE IT ORBITS, anchored on the habitable zone
//
// "There is a reason a habitable zone is just that: it is the only place in orbit of a star that a
// world can sustain liquid water. Planets between the habitable zone and the sun(s) should be MUCH
// MUCH hotter, and it should definitely not be cool enough for planets to generate water. Planets on
// the opposite side should have the opposite problem, being too cold for liquid water, and any
// existing water should be frozen. It needs to influence planet temperature."
//
// ---- WHAT WAS THERE ----------------------------------------------------------------------------
//
// BiasHeat set heat = R / d, clamped to 0.45..1.85, and PlanetTemperature reads 288 K x sqrt(heat).
// The law itself is right — it IS the equilibrium-temperature law, T ~ d^-1/2 — and the clamp was the
// whole problem: at 1.85 the hottest a world could be from its star was 119 °C, and a world one ring
// inside the zone's edge sat at heat 1.43, which is 71 °C, which is liquid water. That is the reported
// "scorched planet barely 140 °C" and the "archipelago world at 55 °C between the suns and the zone".
//
// ---- WHAT IS HERE ------------------------------------------------------------------------------
//
// Three regimes, split at the star's own zone edges (StarDatabase.HzInnerRel / HzOuterRel — the same
// numbers the green rings are drawn from, so the picture and the physics cannot disagree):
//
//   INSIDE THE ZONE      the plain law, exactly as before. Every world already generated in a zone
//                        keeps the climate it has. 49 °C at the inner edge, -42 °C at the outer, plus
//                        up to 45 °C of greenhouse — which is the liquid-water window, near enough,
//                        and is what makes the zone the zone.
//
//   INSIDE THE INNER EDGE   a RUNAWAY GREENHOUSE. The real mechanism at the real place: past the
//                        moist-greenhouse limit water vapour feeds back on itself and the surface goes
//                        to Venus. Modelled as the law's own gradient steepened by RunawayGain, with a
//                        floor of InnerFloorC so that no world inside the edge can hold liquid water
//                        whatever its air — 120 °C base is over the 100 °C boiling point of a thin-aired
//                        world and, with the greenhouse a thick-aired one carries, over the 144 °C of a
//                        four-atmosphere one. The step at the edge is deliberate: a runaway IS a step.
//                        Ring 1 lands near 440 °C, ring 2 near 240 °C, ring 3 at the 120 °C floor.
//
//   BEYOND THE OUTER EDGE   a SNOWBALL. The gradient steepened by SnowballGain and capped at
//                        OuterCeilingC, which is cold enough that the warmest tile on the warmest
//                        thick-aired world out there (+45 greenhouse, +15 equator, +6 weather) is still
//                        under freezing. Ring 6 is around -60 °C, ring 9 around -190 °C.
//
// The RESULT is the heat parameter every other system already reads (terrain, atmosphere loss,
// PlanetTemperature, the classifier), converted through PlanetTemperature's own inverse so the
// Kelvin figure decided here is the Kelvin figure the readout shows. Nothing downstream changed.
// ============================================================================================
public static class InsolationRules
{
    /// How much steeper than the plain law the climb is, inside the zone's inner edge.
    public const float RunawayGain = 2.5f;
    /// The coolest a world inside the inner edge can be from its star alone, °C. Above every boiling
    /// point in the game once the world's own greenhouse is added.
    public const float InnerFloorC = 120f;
    /// How much steeper than the plain law the fall is, beyond the zone's outer edge.
    public const float SnowballGain = 1.5f;
    /// The warmest a world beyond the outer edge can be from its star alone, °C. Its warmest tile
    /// under the thickest air is still frozen.
    public const float OuterCeilingC = -66f;

    /// Per-world weather variance on the figure, as a fraction of Kelvin. Applied and then the regime's
    /// floor or ceiling re-imposed, so the roll can never carry a world across a guarantee.
    public const float Jitter = 0.05f;

    /// The plain equilibrium law: Earth-normal at the reference distance, falling as the inverse
    /// square root.
    static float PlainKelvin(float rel) => PlanetTemperature.ReferenceKelvin / Mathf.Sqrt(Mathf.Max(0.01f, rel));

    /// Surface temperature from starlight alone, in Kelvin, for a body at `rel` = distance / the star's
    /// reference distance. No greenhouse, no type, no internal heat — those are the body's business.
    public static float KelvinAt(float rel)
    {
        float xi = StarDatabase.HzInnerRel, xo = StarDatabase.HzOuterRel;
        if (rel < xi)
        {
            float k = PlainKelvin(xi) + (PlainKelvin(rel) - PlainKelvin(xi)) * RunawayGain;
            return Mathf.Max(k, InnerFloorC + 273.15f);
        }
        if (rel > xo)
        {
            float k = PlainKelvin(xo) + (PlainKelvin(rel) - PlainKelvin(xo)) * SnowballGain;
            return Mathf.Min(k, OuterCeilingC + 273.15f);
        }
        return PlainKelvin(rel);
    }

    public static float KelvinAt(StarData star, float distance)
        => KelvinAt(Mathf.Max(1f, distance) / Mathf.Max(0.5f, StarDatabase.ReferenceDistance(star)));

    /// The heat parameter a body at this distance is born with — the law above, jittered, with the
    /// regime's guarantee re-imposed after the jitter. Draws from UnityEngine.Random like the rest of
    /// generation, so it belongs in the generation stream and nowhere else.
    public static float RollHeat(StarData star, float distance)
    {
        float rel = Mathf.Max(1f, distance) / Mathf.Max(0.5f, StarDatabase.ReferenceDistance(star));
        float k = KelvinAt(rel) * Random.Range(1f - Jitter, 1f + Jitter);
        if (rel < StarDatabase.HzInnerRel) k = Mathf.Max(k, InnerFloorC + 273.15f);
        else if (rel > StarDatabase.HzOuterRel) k = Mathf.Min(k, OuterCeilingC + 273.15f);
        return PlanetTemperature.HeatForKelvin(k);
    }
}
