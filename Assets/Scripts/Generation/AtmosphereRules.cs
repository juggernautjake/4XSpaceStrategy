using UnityEngine;

// How thick a body's atmosphere is at generation, from its Size (surfaceSize) and Type. Size decides
// whether a body has the mass to hold gas at all; Type nudges it from there (a furnace world outgasses
// more than a dead rock the same size — Venus, not Mercury).
//
// No Tectonics attribute exists yet (see the Advanced Planet Generation slice in the dev-request
// planning doc), so "a moon keeps more air if it has tectonics" isn't modeled here — only Size and Type
// are, because those are the only two attributes that actually exist right now.
public static class AtmosphereRules
{
    // A moon at or above this surfaceSize counts as "large" and can hold a real, if thin, atmosphere.
    // Below it, a moon has none — matching "most moons will not have atmosphere ... unless they are
    // large moons" from the request. Moon surfaceSize rolls 3..13 (SolarSystemGenerator.RollSurfaceSize),
    // so this sits in the upper third.
    public const float LargeMoonSurfaceSize = 9f;

    public static float ForBody(CelestialBodyType type, int surfaceSize)
    {
        switch (type)
        {
            // Gas Giants ARE their atmosphere — no ground terrain, extremely thick air.
            case CelestialBodyType.GasGiant:
                return 1f;

            // Too little mass to hold onto anything, ever, regardless of size within the asteroid range.
            case CelestialBodyType.Asteroid:
                return 0f;

            case CelestialBodyType.Moon:
                return surfaceSize >= LargeMoonSurfaceSize
                    ? Mathf.Clamp01(0.15f + (surfaceSize - LargeMoonSurfaceSize) * 0.03f)
                    : 0f;

            // Volcanic activity outgasses extra atmosphere on top of what Size alone would hold.
            case CelestialBodyType.VolcanicPlanet:
                return Mathf.Clamp01(0.30f + surfaceSize * 0.03f);

            // Rocky/Ocean/Ice/Barren planets: thin when small, thicker as they get larger, capped well
            // below a gas giant's.
            default:
                return Mathf.Clamp01(0.15f + surfaceSize * 0.03f);
        }
    }
}
