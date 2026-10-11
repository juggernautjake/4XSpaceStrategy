using UnityEngine;

// A believable Celsius reading for a world, built ON TOP OF the existing `terrainParams.heat`
// multiplier (~0.08 at the outermost ring to ~6 at the innermost — see InsolationRules; the old
// 0.45..1.85 clamp is gone) rather than as a second, disconnected number — heat already
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
    public const float ReferenceKelvin = 288.15f;

    /// The heat parameter that reads as this many Kelvin from starlight alone — the inverse of the line
    /// above. InsolationRules decides a world's climate in Kelvin and stores it through this.
    public static float HeatForKelvin(float kelvin)
    {
        float root = Mathf.Max(0f, kelvin) / ReferenceKelvin;
        return Mathf.Max(0.01f, root * root);
    }

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
        float swing = LatitudeSwingC(b.atmospheres);
        float latitudeSwingC = Mathf.Lerp(swing, -swing, Mathf.Clamp01(latAbs));

        return Mathf.Clamp(baseC + latitudeSwingC, MinCelsius, MaxCelsius);
    }

    // ============================================================================================
    // THIN AIR, BIG SWINGS; THICK AIR, EVEN HEAT (2026-10-11)
    //
    // "Please make sure the temperatures and stuff make sense based on atmospheres and distance from stars."
    //
    // The equator-to-pole swing was a flat ±15 °C on every world. Air is what carries heat from where the
    // sun is to where it is not: the Moon's surface swings by hundreds of degrees, Earth's poles and
    // equator differ by tens, and Venus under ninety atmospheres is the same temperature at its poles as
    // at its equator. So the swing now falls with pressure — ±15 at Earth's one atmosphere, exactly as
    // before for every Earth-like world, ±25 on an airless one, ±8 under a crushing sky — the same
    // direction the tidally-locked day/night contrast (TidalOffsetC) already runs. The terrain generator
    // reads this same figure for every tile (PlanetTerrainGenerator), so caps and readouts agree.
    // ============================================================================================
    public const float SwingAirlessC = 25f, SwingEarthC = 15f, SwingThickC = 8f;
    /// Atmospheres at and above which the swing is as small as it gets.
    const float SwingThickAtmospheres = 4f;

    /// Half the equator-to-pole temperature difference on a world with this much air, °C. Pure.
    public static float LatitudeSwingC(float atmospheres)
    {
        float a = Mathf.Max(0f, atmospheres);
        return a < 1f ? Mathf.Lerp(SwingAirlessC, SwingEarthC, a)
                      : Mathf.Lerp(SwingEarthC, SwingThickC, (a - 1f) / (SwingThickAtmospheres - 1f));
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
        float tidal = TidalOffsetC(b, (x + 0.5f) / b.surface.width, (y + 0.5f) / b.surface.height);
        return Mathf.Clamp(CelsiusAt(b, y) - altDelta * PlanetTerrainGenerator.AltitudeLapseC + tidal,
                           MinCelsius, MaxCelsius);
    }

    // ============================================================================================
    // TIDALLY LOCKED: A HOT FACE AND A FROZEN ONE
    //
    // "Tidally locked worlds should have their sunny side be hot and the opposite side of the world much
    // colder than normal, since it never receives sunlight to warm it."
    //
    // The face toward the star is the middle of the surface map (u = 0.5) — the same point
    // SurfaceIndex.SolarDaySide lights and OrbitController keeps turned to the star — so the hottest
    // ground, the brightest solar ground and the visible lit face are one place.
    //
    // HOW HARD THE CONTRAST BITES DEPENDS ON THE AIR. With nothing to carry heat round to the dark side,
    // an airless locked world is a furnace on one face and a deep freeze on the other; a thick atmosphere
    // moves heat across the terminator and evens the two out. Between them is the terminator ring, close
    // to the world's ordinary temperature — the band where a locked world is most livable.
    //
    // An OFFSET on the world's normal figure, applied per tile by both the terrain generator (so biomes
    // freeze and bake to match) and the readout above (so the number under the cursor agrees with them).
    // BodyAverageCelsius does not carry it: a world's overall climate class is still its average.
    // ============================================================================================
    public const float TidalDayAirlessC = 110f, TidalDayThickC = 35f;
    public const float TidalNightAirlessC = 170f, TidalNightThickC = 55f;
    /// Atmospheres at which heat transport is as good as it gets.
    const float TidalFullTransportAtmospheres = 3f;

    /// Degrees to add at surface point (u, v), 0..1 each. Zero unless the world is tidally locked.
    public static float TidalOffsetC(CelestialBody b, float u, float v)
    {
        if (b == null || !b.tidallyLocked) return 0f;
        float air = Mathf.Clamp01(b.atmospheres / TidalFullTransportAtmospheres);
        float day = Mathf.Lerp(TidalDayAirlessC, TidalDayThickC, air);
        float night = Mathf.Lerp(TidalNightAirlessC, TidalNightThickC, air);

        float lon = (u - 0.5f) * 2f * Mathf.PI;
        float lat = (v - 0.5f) * Mathf.PI;
        float cosZ = Mathf.Cos(lat) * Mathf.Cos(lon);       // 1 under the star, -1 at the far point
        // The day side warms with the sun's height; the night side drops to its full cold within a short
        // way past the terminator, because once the sun is gone it is simply gone.
        return cosZ >= 0f ? day * Mathf.Sqrt(cosZ) : -night * Mathf.Min(1f, -cosZ * 3f);
    }

    // The body's overall average — no latitude swing, just the climate its heat and type describe.
    // This is the explicit "planet Temperature setting" the request asks for.
    public static float BodyAverageCelsius(CelestialBody b) => Mathf.Clamp(BaseCelsius(b), MinCelsius, MaxCelsius);

    // How much extra warmth a thick atmosphere traps on top of what raw distance/heat would give —
    // Venus, not Mercury, despite Venus sitting farther from the sun. A vacuum world gets none of this.
    const float GreenhouseMaxC = 45f;

    // ============================================================================================
    // THE DEEP GREENHOUSE — Venus, not a thick Earth (2026-10-10)
    //
    // "An inner planet that close to its sun should be a very hot world. Venus-like probably."
    //
    // The term above saturates at seven atmospheres (AtmosphereRules.ThicknessReference), and for every
    // world a mass can hold that is the right shape: past a few atmospheres the air is already opaque
    // to infrared and more of it changes little. A RUNAWAY GREENHOUSE is a different regime. Venus is
    // ninety atmospheres of carbon dioxide; the column is so deep that the surface sits ~400 °C above
    // what its sunlight alone would give it — which forty-five degrees of "thick air" cannot describe.
    //
    // So a second term, for pressures no world reaches by holding air with its mass (the ceiling tops
    // out near six — four Earths plus outgassing). It starts at DeepGreenhouseFromAtmospheres and climbs
    // LOGARITHMICALLY, the shape a greenhouse has against the amount of absorber, to DeepGreenhouseMaxC
    // at Venus's ninety. A runaway world (AtmosphereRules.RollRunawayGreenhouse, rings 1-3 only) lands
    // 300-750 °C: ring 3 just over 300, ring 2 around Venus's 465, ring 1 a little under the 800 °C magma
    // line — and since it has no internal heat (a stagnant lid, see AtmosphereRules) its ground never
    // melts, it bakes.
    //
    // Never for a gas giant: its 10-40 "atmospheres" are the planet itself, not a lid of gas over rock.
    // ============================================================================================
    public const float DeepGreenhouseFromAtmospheres = 10f;
    public const float DeepGreenhouseFullAtmospheres = 90f;
    public const float DeepGreenhouseMaxC = 250f;

    /// The extra warming of a runaway-deep atmosphere, in °C, on top of the saturated GreenhouseMaxC.
    /// Zero below DeepGreenhouseFromAtmospheres, so nothing a mass can hold is affected. Pure.
    public static float DeepGreenhouseC(float atmospheres, CelestialBodyType type)
    {
        if (type == CelestialBodyType.GasGiant || atmospheres <= DeepGreenhouseFromAtmospheres) return 0f;
        float t = Mathf.Log(atmospheres / DeepGreenhouseFromAtmospheres)
                / Mathf.Log(DeepGreenhouseFullAtmospheres / DeepGreenhouseFromAtmospheres);
        return Mathf.Clamp01(t) * DeepGreenhouseMaxC;
    }

    /// All the warming a world's air gives it, in °C: the saturating term plus the deep one. What the
    /// consistency check quotes when it explains a hot world by its sky.
    public static float GreenhouseCelsius(CelestialBody b)
        => b == null ? 0f : Mathf.Clamp01(b.atmosphereThickness) * GreenhouseMaxC + DeepGreenhouseC(b.atmospheres, b.type);

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

    /// How much of THIS world's temperature comes from inside it, in °C. Public because the terrain
    /// classifier has to be able to tell a world melted from below from one baked from outside — see
    /// PlanetTerrainGenerator's magma gate, and the Geothermal index in SurfaceIndex.
    ///
    /// The distinction is the whole of the reported bug. A world close enough to its sun to sit at
    /// 800 °C grew liquid rock across its entire surface, and the Geothermal index then read that
    /// liquid rock as evidence of crustal heat and reported 95% everywhere. Neither is true: starlight
    /// heats the SURFACE, and there is nothing under it for a geothermal plant to tap.
    public static float InternalCelsius(CelestialBody b)
        => b == null ? 0f : InternalC(b.type, GeothermalMap.WorldIntensity(b));

    // ============================================================================================
    // A VOLCANIC HOMEWORLD'S VENTING — fixed, not rolled (2026-10-11)
    //
    // Internal heat runs 0 to InternalMaxC (620 °C) on a volcanic world, from its seed and its plates —
    // and a plate world's strongest plate is nearly always 0.9+, so a volcanic world WITH plates runs
    // ~560 °C over its starlight. The Pyrothian cradle used to take whatever it rolled: anywhere from its
    // starlight's figure to six hundred degrees above it, which no orbit choice could make "their ideal".
    //
    // So a volcanic species' homeworld vents a stated amount: HomeworldVentingC, within
    // HomeworldVentingSlackC (GalaxyGenerator re-rolls the cradle's seed until its hotspots give that,
    // and gives it no plates — a stagnant lid with hotspot volcanism, Io's kind rather than Earth's). The
    // species' band (Habitability.ZoneRel) assumes the same figure, so the band is computed for the world
    // the cradle actually is. Every other type vents nothing (InternalC is gated on Volcanic).
    // ============================================================================================
    public const float HomeworldVentingC = 120f;
    public const float HomeworldVentingSlackC = 20f;

    /// The internal heat a homeworld of this type is built with — what a species' band is computed for.
    public static float HomeworldVenting(CelestialBodyType type)
        => type == CelestialBodyType.VolcanicPlanet ? HomeworldVentingC : 0f;

    /// The geothermal intensity (0..1) that gives `internalC` of venting on a volcanic world — the inverse
    /// of InternalC, for re-rolling a homeworld's seed toward a wanted figure.
    public static float IntensityForInternal(float internalC) => Mathf.Clamp01(internalC / InternalMaxC);

    static float BaseCelsius(CelestialBody b)
        => BaseCelsius(b.terrainParams.heat, b.atmosphereThickness, b.type, GeothermalMap.WorldIntensity(b), b.atmospheres);

    /// The full law including the DEEP greenhouse, which needs the pressure in atmospheres — the 0..1
    /// thickness every other overload takes is clamped at seven and cannot tell 8 from 90. The terrain
    /// generator calls this one so a runaway world's map (no seas, nothing green) is judged against the
    /// same figure the readout shows.
    public static float BaseCelsius(float heat, float atmosphereThickness, CelestialBodyType type,
                                    float geothermal01, float atmospheres)
        => BaseCelsius(heat, atmosphereThickness, type, geothermal01) + DeepGreenhouseC(atmospheres, type);

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
    ///
    /// `atmospheres` is only needed for a runaway-deep sky (DeepGreenhouseC); every existing caller solves
    /// for worlds a mass can hold, where that term is zero, so it defaults to nothing.
    public static float HeatForCelsius(float targetC, float atmosphereThickness, CelestialBodyType type,
                                       float atmospheres = 0f)
    {
        float greenhouseC = Mathf.Clamp01(atmosphereThickness) * GreenhouseMaxC + DeepGreenhouseC(atmospheres, type);
        float kelvin = targetC + 273.15f - TypeModifierC(type) - greenhouseC;
        float root = Mathf.Max(0f, kelvin) / ReferenceKelvin;
        // Floored at the same 0.01 BaseCelsius clamps to, so the round trip is stable at the cold end.
        return Mathf.Max(0.01f, root * root);
    }

    // Hot planet TYPES run hot everywhere (a furnace world's own internal heat), cold types run cold
    // everywhere (high albedo, no greenhouse) — independent of where they happen to orbit. This is what
    // makes "a volcanic world will likely never read white/blue, an ice world will likely never read
    // red/orange" hold even though both are ultimately built from the same distance-driven heat value.
    // Public since 2026-10-11 so WorldExplanation can show it as its own line of a world's temperature
    // (ice albedo, lava, cloud tops) rather than folding it silently into "starlight".
    public static float TypeModifierC(CelestialBodyType t)
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
