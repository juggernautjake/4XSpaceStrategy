using UnityEngine;

// A believable Celsius reading for a world, built ON TOP OF the existing `terrainParams.heat`
// multiplier (0.45 cold .. 1.85 hot) rather than as a second, disconnected number — heat already
// drives the terrain classifier and is already what TerraformVisuals blends toward the species'
// ideal as a world is terraformed, so reading through it here means this reading moves correctly
// as a world terraforms too, for free.
//
// DERIVED, NEVER STORED (same discipline as SurfaceIndex) — a world's heat can change post-generation
// (terraforming), so caching a temperature would just mean a second, staler copy of it.
public static class PlanetTemperature
{
    // T_eq(K) = 288.15 * sqrt(heat), i.e. the equilibrium-temperature law T ~ (L/d^2)^0.25 with heat
    // already standing in for L/d^2 (see BiasHeat in SolarSystemGenerator) and calibrated so heat=1
    // (this star's temperate band) reads as Earth's ~15C average.
    const float ReferenceKelvin = 288.15f;

    // A tile's local reading: the body's baseline plus a small equator-warmer/pole-cooler swing. The
    // swing is deliberately small next to the type nudges below, so it can vary a world's own tiles
    // without ever flipping a hot world cold or vice versa.
    public static float CelsiusAt(CelestialBody b, int y)
    {
        if (b == null) return 0f;
        float baseC = BaseCelsius(b);

        int h = b.surface != null ? Mathf.Max(1, b.surface.height) : 1;
        float latAbs = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;   // 0 at the equator, 1 at the poles
        float latitudeSwingC = Mathf.Lerp(15f, -15f, Mathf.Clamp01(latAbs));

        return Mathf.Clamp(baseC + latitudeSwingC, -200f, 400f);
    }

    // The body's overall average — no latitude swing, just the climate its heat and type describe.
    // This is the explicit "planet Temperature setting" the request asks for.
    public static float BodyAverageCelsius(CelestialBody b) => Mathf.Clamp(BaseCelsius(b), -200f, 400f);

    // How much extra warmth a thick atmosphere traps on top of what raw distance/heat would give —
    // Venus, not Mercury, despite Venus sitting farther from the sun. A vacuum world gets none of this.
    const float GreenhouseMaxC = 45f;

    static float BaseCelsius(CelestialBody b)
        => BaseCelsius(b.terrainParams.heat, b.atmosphereThickness, b.type);

    // The same law from raw inputs, so terrain GENERATION can judge a tile's climate against the exact
    // figure this class shows the player — greenhouse warming and the type nudge included — and the map
    // (frozen seas, no jungle in a furnace) can never disagree with the °C readout.
    public static float BaseCelsius(float heat, float atmosphereThickness, CelestialBodyType type)
    {
        float kelvin = ReferenceKelvin * Mathf.Sqrt(Mathf.Max(0.01f, heat));
        float greenhouseC = Mathf.Clamp01(atmosphereThickness) * GreenhouseMaxC;
        return kelvin - 273.15f + TypeModifierC(type) + greenhouseC;
    }

    // Hot planet TYPES run hot everywhere (a furnace world's own internal heat), cold types run cold
    // everywhere (high albedo, no greenhouse) — independent of where they happen to orbit. This is what
    // makes "a volcanic world will likely never read white/blue, an ice world will likely never read
    // red/orange" hold even though both are ultimately built from the same distance-driven heat value.
    static float TypeModifierC(CelestialBodyType t)
    {
        switch (t)
        {
            case CelestialBodyType.VolcanicPlanet: return 90f;
            case CelestialBodyType.IcePlanet: return -50f;
            case CelestialBodyType.GasGiant: return -40f;
            default: return 0f;
        }
    }

    public static string Label(float celsius) => $"{celsius:F0}°C";

    // Fixed, global anchors — deliberately NOT re-normalized per planet, so a planet's type and
    // distance decide which end of the scale it lands on rather than every world spanning the same
    // white-to-red range regardless of how hot or cold it actually is.
    // The coldest an IcePlanet's body average could read at heat's floor of 0.45 with no atmosphere.
    // Real generated ice worlds now run warmer than this floor thanks to greenhouse warming (every ice
    // world has SOME atmosphere per AtmosphereRules), so this is a safety anchor rather than a value any
    // world actually reaches — GradientColor just never gets asked to show anything colder.
    const float StopWhite = -130f;
    const float StopIceBlue = -30f;
    const float StopYellowOrange = 70f;
    const float StopRed = 180f;

    static readonly Color ColorWhite = new Color(1.00f, 1.00f, 1.00f);
    static readonly Color ColorIceBlue = new Color(0.62f, 0.85f, 1.00f);
    static readonly Color ColorYellowOrange = new Color(1.00f, 0.62f, 0.12f);
    static readonly Color ColorRed = new Color(0.95f, 0.14f, 0.10f);

    public static Color GradientColor(float celsius)
    {
        if (celsius <= StopWhite) return ColorWhite;
        if (celsius >= StopRed) return ColorRed;

        if (celsius <= StopIceBlue)
            return Color.Lerp(ColorWhite, ColorIceBlue, Mathf.InverseLerp(StopWhite, StopIceBlue, celsius));
        if (celsius <= StopYellowOrange)
            return Color.Lerp(ColorIceBlue, ColorYellowOrange, Mathf.InverseLerp(StopIceBlue, StopYellowOrange, celsius));
        return Color.Lerp(ColorYellowOrange, ColorRed, Mathf.InverseLerp(StopYellowOrange, StopRed, celsius));
    }
}
