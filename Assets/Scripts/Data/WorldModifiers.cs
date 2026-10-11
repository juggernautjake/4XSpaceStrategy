using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WORLD MODIFIERS — "this world has something interesting, you might want to take it"
//
// A modifier is a fact about a whole world that bends one of its indexes. Most are rolled at generation
// with a small chance each, so they are uncommon enough to be worth seeking out; two are DERIVED from
// facts the world already carries (plates, and the tidal lock that is rolled with its rotation), because
// a modifier that could disagree with the world it describes would be a second source of truth.
//
// Several can sit on one world. A world with both mineral modifiers is meant to be a prize.
//
// WHAT THE PLAYER SEES, AND WHEN (see WorldModifierBadges):
//   level-1 survey  — a "?" badge, bordered in the colour of the index it bends. Something is here.
//   level-2 survey  — the real icon and its description. The indexes it bends are readable by then too.
// ============================================================================================
public enum WorldModifier
{
    TidallyLocked,
    HighQualityMinerals,
    VastMineralDeposits,
    ExtremeWeather,
    FertileWorld,
    ContinentalPlates,
}

public static class WorldModifiers
{
    public static readonly WorldModifier[] All =
    {
        WorldModifier.TidallyLocked, WorldModifier.HighQualityMinerals, WorldModifier.VastMineralDeposits,
        WorldModifier.ExtremeWeather, WorldModifier.FertileWorld, WorldModifier.ContinentalPlates,
    };

    // ---- The rolls -------------------------------------------------------------------------------
    //
    // Per world, independent. On a galaxy of ~50 solid worlds that is two or three of each rolled kind,
    // which is "uncommon, worth looking for" rather than "every other planet".
    // (The flat 6% / 6% / 8% mineral and weather chances are gone — see WHAT A MODIFIER COMES FROM.)
    /// Of the worlds that already meet the Fertile World climate, how many actually are one. The climate
    /// window is narrow on its own, so this is high.
    const float FertileWorldChance = 0.65f;

    /// Air needed for Extreme Weather to mean anything: below this the Weather index has no ceiling to
    /// raise.
    public const float ExtremeWeatherMinAtmospheres = 0.8f;

    // Fertile World criteria: 20-22 °C give or take 3, and 40-60% water.
    public const float FertileIdealLowC = 20f, FertileIdealHighC = 22f, FertileToleranceC = 3f;
    public const float FertileWaterMin = 0.40f, FertileWaterMax = 0.60f;

    // ---- TIDAL LOCK ------------------------------------------------------------------------------
    //
    // Rolled WITH ROTATION, inside the world pipeline, not afterwards — a locked world does not turn, a
    // world that does not turn runs no dynamo, and without a dynamo its air ceiling halves. Rolling it
    // later would leave a magnetosphere standing on a world that cannot have one.
    //
    // Planets only: the request is about facing the star, and a moon locked to its planet would face
    // the wrong thing.
    //
    // BY THE STAR'S TIDE, NOT BY THE ZONE BAND (2026-10-11). It used to be 10% inside the habitable zone,
    // 3% in it and 1% beyond, whatever the star — so a world at five AU from a Sun-like star could lock
    // (nothing there raises a tide strong enough) and a red dwarf's habitable world locked no more often
    // than Earth would. The tide a star raises goes as its mass over the cube of the distance, so that is
    // what the chance reads now: TideStrength, 1 for the Sun on Earth. Too weak (TidalLockMinTide) and
    // nothing locks at all; otherwise TidalLockEarthChance x sqrt(tide), capped. A red dwarf's habitable
    // ring is close in and locks several times more often than a Sun-like one, which is the real picture.
    // tools/world-causes-check.mjs prints the before and after.
    public const float TidalLockEarthChance = 0.03f;
    public const float TidalLockMinTide = 0.15f;
    public const float TidalLockMaxChance = 0.35f;

    /// The tide `star` raises on a world `distance` units out, relative to the Sun's on Earth: solar
    /// masses over AU cubed. The game's ruler is StarDatabase.AU. Pure.
    public static float TideStrength(StarData star, float distance)
    {
        if (star == null) return 0f;
        float au = Mathf.Max(0.05f, distance / StarDatabase.AU);
        return Mathf.Max(0f, star.mass) / (au * au * au);
    }

    /// The chance a planet at this distance is tidally locked to its star. Pure.
    public static float TidalLockChance(CelestialBodyType provisionalType, bool isMoon, StarData star, float distance)
    {
        if (isMoon) return 0f;
        if (provisionalType == CelestialBodyType.GasGiant || provisionalType == CelestialBodyType.Asteroid) return 0f;
        float tide = TideStrength(star, distance);
        if (tide < TidalLockMinTide) return 0f;
        return Mathf.Min(TidalLockMaxChance, TidalLockEarthChance * Mathf.Sqrt(tide));
    }

    /// One draw from the generation stream, whatever the chance — so every world after it rolls as before.
    public static bool RollTidalLock(CelestialBodyType provisionalType, bool isMoon, StarData star, float distance)
        => Random.value < TidalLockChance(provisionalType, isMoon, star, distance);

    // ---- WHAT A MODIFIER COMES FROM (2026-10-11) -------------------------------------------------
    //
    // These were flat dice — 6%, 6% and 8% on every solid world — so nothing about a world explained why
    // it had one. They are still uncommon and still rolled, but the odds now come from the world:
    //
    //   HIGH QUALITY MINERALS   hot fluids circulating through an ACTIVE crust (plates, or hotspots at
    //                           GeologyActiveIntensity and up) concentrate metals into rich seams. 9% on an
    //                           active world, 3% on a quiet one.
    //   VAST MINERAL DEPOSITS   plate collisions push ore-bearing rock up across wide areas. 10% with
    //                           plates, 4.5% without. Every starting world is granted it outright
    //                           (GalaxyGenerator.Finish) — it was settled for its mineral ground.
    //   EXTREME WEATHER         storms need energy and contrast: a day side that never cools (tidal lock),
    //                           a fast spin's strong jets, warm seas feeding moisture, a deep sky. 4% from
    //                           the ordinary equator-to-pole contrast alone, more for each driver present
    //                           (WeatherDrivers lists them; WorldExplanation names them).
    public const float GeologyActiveIntensity = 0.35f;
    public const float FastSpin = 30f;
    public const float WarmSeaWater = 0.5f, WarmSeaC = 15f;
    public const float DeepSkyAtmospheres = 3f;

    /// Is this world's crust active enough to concentrate ores — plates, or strong hotspots?
    public static bool GeologicallyActive(CelestialBody b)
        => b != null && (b.hasTectonics || GeothermalMap.HotspotIntensity(b) >= GeologyActiveIntensity);

    [System.Flags]
    public enum WeatherDriver { None = 0, TidalLock = 1, FastSpin = 2, WarmSeas = 4, DeepSky = 8 }

    /// What, beyond the plain equator-to-pole contrast every world with air has, drives storms here.
    public static WeatherDriver WeatherDrivers(CelestialBody b)
    {
        var d = WeatherDriver.None;
        if (b == null) return d;
        if (b.tidallyLocked) d |= WeatherDriver.TidalLock;
        if (b.spinSpeed >= FastSpin) d |= WeatherDriver.FastSpin;
        if (WaterLevel(b) >= WarmSeaWater && PlanetTemperature.BodyAverageCelsius(b) >= WarmSeaC) d |= WeatherDriver.WarmSeas;
        if (b.atmospheres >= DeepSkyAtmospheres) d |= WeatherDriver.DeepSky;
        return d;
    }

    public static float ExtremeWeatherChanceFor(CelestialBody b)
    {
        var d = WeatherDrivers(b);
        float c = 0.04f;
        if ((d & WeatherDriver.TidalLock) != 0) c += 0.10f;
        if ((d & WeatherDriver.FastSpin) != 0) c += 0.03f;
        if ((d & WeatherDriver.WarmSeas) != 0) c += 0.05f;
        if ((d & WeatherDriver.DeepSky) != 0) c += 0.03f;
        return Mathf.Min(0.25f, c);
    }

    /// The rest, rolled once the surface exists — Fertile World needs the finished climate to judge.
    /// Draws from UnityEngine.Random inside the generation stream, like every other generation roll —
    /// the same draws in the same order as before; only the odds come from the world now.
    public static void Roll(CelestialBody b)
    {
        if (b == null) return;
        b.worldModifierFlags = 0;
        if (!Solid(b)) return;

        if (Random.value < (GeologicallyActive(b) ? 0.09f : 0.03f)) Set(b, WorldModifier.HighQualityMinerals);
        if (Random.value < (b.hasTectonics ? 0.10f : 0.045f)) Set(b, WorldModifier.VastMineralDeposits);
        if (b.atmospheres >= ExtremeWeatherMinAtmospheres && Random.value < ExtremeWeatherChanceFor(b))
            Set(b, WorldModifier.ExtremeWeather);
        if (MeetsFertileClimate(b) && Random.value < FertileWorldChance) Set(b, WorldModifier.FertileWorld);
    }

    /// Worlds a modifier can sit on: solid, and big enough to be a world. Belt rocks are excluded so they
    /// do not inflate the count of each modifier the galaxy is meant to hold.
    static bool Solid(CelestialBody b)
        => b.type != CelestialBodyType.GasGiant && b.type != CelestialBodyType.Asteroid;

    static void Set(CelestialBody b, WorldModifier m) => b.worldModifierFlags |= 1 << (int)m;

    /// Give a world a modifier outright — for guarantees made at generation (every starting world has
    /// Vast Mineral Deposits; see GalaxyGenerator.Finish). Only for the bit-backed modifiers.
    public static void Grant(CelestialBody b, WorldModifier m)
    {
        if (b == null || !Solid(b)) return;
        Set(b, m);
    }

    /// Does this world carry this modifier RIGHT NOW?
    ///
    /// Live for the derived ones: terraform the plates away (or a remodel takes them) and the modifier
    /// goes with them. Fertile World likewise only holds while its climate does — terraform it out of the
    /// window and it stops being one.
    public static bool Has(CelestialBody b, WorldModifier m)
    {
        if (b == null || !Solid(b)) return false;
        switch (m)
        {
            case WorldModifier.TidallyLocked: return b.tidallyLocked;
            case WorldModifier.ContinentalPlates: return b.hasTectonics;
            case WorldModifier.FertileWorld:
                return (b.worldModifierFlags & (1 << (int)m)) != 0 && MeetsFertileClimate(b);
            // Storms need air. Thin the atmosphere out from under it and the modifier lapses with it.
            case WorldModifier.ExtremeWeather:
                return (b.worldModifierFlags & (1 << (int)m)) != 0 && b.atmospheres >= ExtremeWeatherMinAtmospheres;
            default: return (b.worldModifierFlags & (1 << (int)m)) != 0;
        }
    }

    /// Every modifier this world carries, in the canonical order. Fills `into` (cleared first).
    public static void List(CelestialBody b, List<WorldModifier> into)
    {
        into.Clear();
        foreach (var m in All) if (Has(b, m)) into.Add(m);
    }

    public static bool Any(CelestialBody b)
    {
        foreach (var m in All) if (Has(b, m)) return true;
        return false;
    }

    /// The Fertile World climate: a living world, near 20-22 °C, about half water.
    public static bool MeetsFertileClimate(CelestialBody b)
    {
        if (b == null || !b.biosphereActive) return false;
        float c = PlanetTemperature.BodyAverageCelsius(b);
        if (c < FertileIdealLowC - FertileToleranceC || c > FertileIdealHighC + FertileToleranceC) return false;
        float water = WaterLevel(b);
        return water >= FertileWaterMin && water <= FertileWaterMax;
    }

    /// The world's Water Level, 0..1 — the same figure the Overview prints.
    public static float WaterLevel(CelestialBody b)
        => b == null ? 0f : PlanetTerrainGenerator.WaterLevelFromSeaLevel(b.terrainParams.SeaLevelOrNeutral);

    // ---- Presentation ----------------------------------------------------------------------------

    public static string Name(WorldModifier m)
    {
        switch (m)
        {
            case WorldModifier.TidallyLocked: return "Tidally Locked";
            case WorldModifier.HighQualityMinerals: return "High Quality Minerals";
            case WorldModifier.VastMineralDeposits: return "Vast Mineral Deposits";
            case WorldModifier.ExtremeWeather: return "Extreme Weather";
            case WorldModifier.FertileWorld: return "Fertile World";
            case WorldModifier.ContinentalPlates: return "Continental Plates";
            default: return m.ToString();
        }
    }

    public static string Describe(WorldModifier m)
    {
        switch (m)
        {
            case WorldModifier.TidallyLocked: return "This world does not turn. One face always looks at its star and basks in endless day — a strong Solar Index — while the far side sits in permanent night with no sunlight at all.";
            case WorldModifier.HighQualityMinerals: return "Unusually rich seams. Where this world has mineral ground, it reads far higher than normal.";
            case WorldModifier.VastMineralDeposits: return "Mineral ground covers a great deal more of this world's surface than usual.";
            case WorldModifier.ExtremeWeather: return "Violent, persistent storm systems. More of the surface carries a Weather Index, in larger storm zones, at higher values.";
            case WorldModifier.FertileWorld: return "A living world close to 20-22 °C with about half its surface under water. Fertile ground is far more widespread here, and much of it reads 90% and above.";
            case WorldModifier.ContinentalPlates: return "Active plate tectonics. The plate boundaries carry a modest Geothermal Index along their whole length — low yield, but easy to reach. Quakes only damage what stands on the hottest ground (70% and up), so the quiet stretches of a boundary are safe to build on.";
            default: return "";
        }
    }

    /// The index a modifier bends — its badge is bordered in that index's colour, so even the "?" stage
    /// hints at what kind of thing it is.
    public static SurfaceIndexKind IndexOf(WorldModifier m)
    {
        switch (m)
        {
            case WorldModifier.TidallyLocked: return SurfaceIndexKind.Solar;
            case WorldModifier.HighQualityMinerals:
            case WorldModifier.VastMineralDeposits: return SurfaceIndexKind.Mineral;
            case WorldModifier.ExtremeWeather: return SurfaceIndexKind.Wind;
            case WorldModifier.FertileWorld: return SurfaceIndexKind.Fertile;
            case WorldModifier.ContinentalPlates: return SurfaceIndexKind.Geothermal;
            default: return SurfaceIndexKind.None;
        }
    }

    public static Color BorderColor(WorldModifier m) => SurfaceIndex.Outline(IndexOf(m));

    public static Texture2D Icon(WorldModifier m)
        => Resources.Load<Texture2D>($"SpaceAssets/ModifierIcons/Modifier_{m}");

    public static Texture2D UnknownIcon()
        => Resources.Load<Texture2D>("SpaceAssets/ModifierIcons/Modifier_Unknown");
}
