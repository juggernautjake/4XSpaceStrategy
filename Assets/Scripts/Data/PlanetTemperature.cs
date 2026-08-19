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
    // ============================================================================================
    // THE RANGE
    //
    // −270 °C to 1000 °C. The cold end is a shade above absolute zero — a rock in deep space, far from
    // any sun, with nothing to keep it warm. The hot end is a MOLTEN world: a surface of liquid rock,
    // which is roughly where basalt melts and therefore roughly where "planet" stops meaning what it
    // usually means.
    //
    // The old clamp was −200 to 400, and it was not merely narrow: it was the reason a molten world and
    // a merely volcanic one read as the same temperature. Both saturated at 400.
    public const float MinCelsius = -270f;
    public const float MaxCelsius = 1000f;

    public static float CelsiusAt(CelestialBody b, int y)
    {
        if (b == null) return 0f;
        float baseC = BaseCelsius(b);

        int h = b.surface != null ? Mathf.Max(1, b.surface.height) : 1;
        float latAbs = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;   // 0 at the equator, 1 at the poles
        float latitudeSwingC = Mathf.Lerp(15f, -15f, Mathf.Clamp01(latAbs));

        return Mathf.Clamp(baseC + latitudeSwingC, MinCelsius, MaxCelsius);
    }

    /// The reading for a specific TILE — latitude and ELEVATION both.
    ///
    /// The latitude-only version above is what every readout used, and on the new terrain it is visibly
    /// wrong: elevation moves a tile's temperature by up to ±65 °C (PlanetTerrainGenerator's altitude
    /// lapse), which is what puts snow on a mountain in the tropics and magma in a valley on a world
    /// whose highlands are bare rock. A readout that ignored it would tell the player a peak and the
    /// plain beside it were the same temperature while the map plainly showed otherwise.
    ///
    /// Uses the SAME lapse rate the terrain generator classified the tile with, so the number under the
    /// cursor is the number the biome was decided by.
    public static float CelsiusAt(CelestialBody b, int x, int y)
    {
        if (b?.surface == null) return CelsiusAt(b, y);
        if (x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) return CelsiusAt(b, y);

        float altDelta = b.surface.tiles[x, y].elevation - 0.5f;
        return Mathf.Clamp(CelsiusAt(b, y) - altDelta * PlanetTerrainGenerator.AltitudeLapseC,
                           MinCelsius, MaxCelsius);
    }

    // The body's overall average — no latitude swing, just the climate its heat and type describe.
    // This is the explicit "planet Temperature setting" the request asks for.
    public static float BodyAverageCelsius(CelestialBody b) => Mathf.Clamp(BaseCelsius(b), MinCelsius, MaxCelsius);

    // How much extra warmth a thick atmosphere traps on top of what raw distance/heat would give —
    // Venus, not Mercury, despite Venus sitting farther from the sun. A vacuum world gets none of this.
    const float GreenhouseMaxC = 45f;

    // ============================================================================================
    // INTERNAL HEAT — the half of a molten world's temperature that has nothing to do with its star
    //
    // Everything above this line is about SUNLIGHT: how bright the star is, how far away the world sits,
    // how much of the outgoing infrared its air traps. That law tops out around 250 °C for a world as
    // close to its sun as these systems put one, and no amount of tuning it will produce a lava world,
    // because a lava world is not hot for that reason. Io is nine hundred million kilometres from the
    // Sun and is the most volcanically active body in the solar system; the heat is coming from
    // UNDERNEATH.
    //
    // So it is a separate term, driven by the same geothermal field the survey overlay draws and the
    // earthquakes shake (GeothermalMap.WorldIntensity). A body venting at full intensity runs six
    // hundred degrees hotter than its orbit says it should, which — stacked on a close orbit and a thick
    // greenhouse — is what lands a genuinely molten world in the 650-1000 °C band where magma fields
    // form. A quiet one gets nothing, and its temperature is its orbit's business alone.
    //
    // GATED ON THE VOLCANIC TYPE, not applied to everything with a hotspot. A rocky world with an
    // active fault has volcanoes on it and is not five hundred degrees; what the type says is that the
    // venting is planet-wide rather than local.
    public const float InternalMaxC = 620f;

    static float InternalC(CelestialBodyType type, float geothermal01)
        => type == CelestialBodyType.VolcanicPlanet ? Mathf.Clamp01(geothermal01) * InternalMaxC : 0f;

    static float BaseCelsius(CelestialBody b)
        => BaseCelsius(b.terrainParams.heat, b.atmosphereThickness, b.type, GeothermalMap.WorldIntensity(b));

    // The same law from raw inputs, so terrain GENERATION can judge a tile's climate against the exact
    // figure this class shows the player — greenhouse warming and the type nudge included — and the map
    // (frozen seas, no jungle in a furnace) can never disagree with the °C readout.
    //
    // The three-argument form carries no internal heat, which is the right default for every caller that
    // does not have a body to read one off — WorldClassifier deliberately asks this question with the
    // type held at RockyPlanet precisely to keep the classification from being circular, and a molten
    // world must not be molten because it was already classified molten.
    public static float BaseCelsius(float heat, float atmosphereThickness, CelestialBodyType type)
        => BaseCelsius(heat, atmosphereThickness, type, 0f);

    public static float BaseCelsius(float heat, float atmosphereThickness, CelestialBodyType type,
                                    float geothermal01)
    {
        // The floor is far below anything generation produces (a distant world runs ~0.07) and exists so
        // the cold end of the range is reachable at all: at 0.01 the law bottoms out near −245 °C, which
        // is warmer than the −270 the range is supposed to reach.
        float kelvin = ReferenceKelvin * Mathf.Sqrt(Mathf.Max(0.00001f, heat));
        float greenhouseC = Mathf.Clamp01(atmosphereThickness) * GreenhouseMaxC;
        return kelvin - 273.15f + TypeModifierC(type) + greenhouseC + InternalC(type, geothermal01);
    }

    /// The `heat` value that would give a world of this type and atmosphere the requested average
    /// temperature. The exact algebraic inverse of BaseCelsius above.
    ///
    /// WHY THIS IS NEEDED. `heat` is calibrated so that heat = 1 reads as Earth's ~15°C — but that is
    /// BEFORE the greenhouse term, which adds up to 45°C on top depending on how much air the world
    /// holds. So "set heat from the species' ideal temperature" does not produce the temperature the
    /// species wants: a thicker-atmosphere world of the same heat runs up to 23°C hotter, which is
    /// enough to carry a cradle straight past the liquid-water ceiling (see BiosphereRules). Anything
    /// choosing a world's climate by TEMPERATURE has to solve for heat rather than assign it.
    public static float HeatForCelsius(float targetC, float atmosphereThickness, CelestialBodyType type)
    {
        float greenhouseC = Mathf.Clamp01(atmosphereThickness) * GreenhouseMaxC;
        float kelvin = targetC + 273.15f - TypeModifierC(type) - greenhouseC;
        float root = Mathf.Max(0f, kelvin) / ReferenceKelvin;
        // Floored at the same 0.01 BaseCelsius clamps to, so the round trip is stable at the cold end.
        return Mathf.Max(0.01f, root * root);
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
    const float StopWhite = -240f;
    const float StopIceBlue = -30f;
    const float StopYellowOrange = 70f;
    const float StopRed = 250f;
    /// ...and past red, a world hot enough to GLOW. Rock at 900 °C is a dull incandescent orange in the
    /// dark, which is exactly what a magma field is, and a molten world needs somewhere on this ramp to
    /// live that a merely scorching one does not reach. Without it every temperature over 250 read as
    /// the same red and the whole new upper half of the range was invisible.
    const float StopMolten = 900f;

    static readonly Color ColorWhite = new Color(1.00f, 1.00f, 1.00f);
    static readonly Color ColorIceBlue = new Color(0.62f, 0.85f, 1.00f);
    static readonly Color ColorYellowOrange = new Color(1.00f, 0.62f, 0.12f);
    static readonly Color ColorRed = new Color(0.95f, 0.14f, 0.10f);
    static readonly Color ColorMolten = new Color(1.00f, 0.86f, 0.55f);   // incandescent

    public static Color GradientColor(float celsius)
    {
        if (celsius <= StopWhite) return ColorWhite;
        if (celsius >= StopMolten) return ColorMolten;

        if (celsius <= StopIceBlue)
            return Color.Lerp(ColorWhite, ColorIceBlue, Mathf.InverseLerp(StopWhite, StopIceBlue, celsius));
        if (celsius <= StopYellowOrange)
            return Color.Lerp(ColorIceBlue, ColorYellowOrange, Mathf.InverseLerp(StopIceBlue, StopYellowOrange, celsius));
        if (celsius <= StopRed)
            return Color.Lerp(ColorYellowOrange, ColorRed, Mathf.InverseLerp(StopYellowOrange, StopRed, celsius));
        return Color.Lerp(ColorRed, ColorMolten, Mathf.InverseLerp(StopRed, StopMolten, celsius));
    }
}
