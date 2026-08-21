using UnityEngine;

// ============================================================================================
// WHAT UNSURVEYED GROUND LOOKS LIKE
//
// It was solid black — alpha 255, deliberately, on the reasoning that a partly transparent blackout
// reads as a DIMMED map rather than a covered one, and a map that is merely dim reads as a rendering
// fault.
//
// That reasoning was sound about the failure it was fixing and wrong about the fix. The problem with
// the old translucent version was that it was a flat grey wash at a low alpha, which is exactly what
// a rendering fault looks like. The answer is not to make it opaque; it is to make it look like
// SOMETHING — cloud, over a world that has air, in the colour that world's air actually is.
//
// So the veil is translucent again, and it is now:
//
//   * the colour of the body's own atmosphere, so an orange world is under orange cloud and an ice
//     giant is under blue;
//   * thicker where the air is thicker, so a dense world genuinely is harder to see through and the
//     opacity is telling the player something true before the survey has told them anything;
//   * nearly black and nearly opaque where there is no air at all, because an airless rock is not
//     hidden under weather — it is hidden under not having been looked at, and that should read as
//     absence rather than as atmosphere.
//
// The last point is what saves the original argument. The failure mode being avoided was a uniform
// grey haze over every world; what replaced it is a veil whose colour and density vary per world, so
// it never reads as a filter laid over the interface. It reads as the sky.
// ============================================================================================
public static class SurveyVeil
{
    /// How opaque the veil is over a world with no atmosphere worth the name. High, because there is
    /// nothing to see through — but not 1, so the coastlines underneath are hinted at rather than
    /// erased, which is what makes uncovering one satisfying rather than surprising.
    const float AirlessAlpha = 0.88f;

    /// The range an atmosphere's density maps onto. Even the thinnest air is more opaque than vacuum,
    /// because cloud is the thing doing the hiding.
    const float ThinAlpha = 0.80f, ThickAlpha = 0.96f;

    /// Below this many atmospheres a body is treated as airless. The same threshold PlanetAppearance
    /// uses to decide whether to draw an atmosphere shell at all — a world with a visible shell and no
    /// coloured veil, or the reverse, would be the two systems disagreeing in front of the player.
    const float AirlessBelow = 0.06f;

    /// The veil's colour and opacity over this body.
    public static Color ColorFor(CelestialBody b)
    {
        if (b == null) return new Color(0.012f, 0.016f, 0.028f, AirlessAlpha);

        // A gas giant is nothing but atmosphere, and its colour is per-body — see GasGiantPalette.
        if (b.type == CelestialBodyType.GasGiant)
        {
            var g = GasGiantPalette.Atmosphere(b);
            // Darkened well below the shell colour. The shell is a highlight seen against space; this
            // is cloud seen from above with a world under it, and at the shell's own brightness it
            // would be lighter than the map it is covering.
            return new Color(g.r * 0.42f, g.g * 0.42f, g.b * 0.42f, ThickAlpha);
        }

        float atm = Mathf.Max(0f, b.atmospheres);
        if (atm < AirlessBelow || !PlanetAppearance.AtmosphereColorOf(b.type, out Color air))
            return new Color(0.012f, 0.016f, 0.028f, AirlessAlpha);

        // ---- thicker air, denser cloud ----
        //
        // On the body's own `atmosphereThickness`, which is already the normalised measure everything
        // else in the game reads, so a terraforming project that thickens a world's air also makes an
        // unsurveyed one harder to see through without anybody wiring the two together.
        float t = Mathf.Clamp01(b.atmosphereThickness);
        float alpha = Mathf.Lerp(ThinAlpha, ThickAlpha, t);

        // Darkened toward night, and MORE so where the air is thin. A thick atmosphere is a bright
        // cloud deck lit from above; a wisp of air over a rock is barely lighter than the rock. Without
        // this, thin-atmosphere worlds came out under a pale haze that looked like the old grey wash.
        float lift = Mathf.Lerp(0.22f, 0.52f, t);
        return new Color(air.r * lift, air.g * lift, air.b * lift, alpha);
    }

    /// The white marker that frames the block a ship is working on, pulsing so it reads as a machine
    /// running rather than as a rectangle somebody left on the map.
    ///
    /// On the fleet beat, like the running lights, the drive plumes and the plasma bolts — everything
    /// in the game that breathes, breathes to one clock. A marker on a clock of its own is the one
    /// thing on screen that is out of step with everything else, and the eye finds it.
    public static Color MarkerColor()
    {
        float pulse = 0.55f + 0.45f * Mathf.Sin(FleetClock.Beats * Mathf.PI * 2f);
        return new Color(0.92f, 0.96f, 1f, Mathf.Lerp(0.14f, 0.42f, pulse));
    }

    /// The marker's border, which pulses with the fill but stays firmly visible at its dimmest — the
    /// edge is what says how big the block is, and a border that faded out with the fill would make the
    /// block's size the thing hardest to read about it.
    public static Color MarkerEdgeColor()
    {
        float pulse = 0.55f + 0.45f * Mathf.Sin(FleetClock.Beats * Mathf.PI * 2f);
        return new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 0.95f, pulse));
    }
}
