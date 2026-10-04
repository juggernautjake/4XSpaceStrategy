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
    const float HighQualityMineralChance = 0.06f;
    const float VastMineralChance = 0.06f;
    const float ExtremeWeatherChance = 0.08f;
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
    // the wrong thing. Closer worlds lock far more readily, which is the real physics of it.
    public static bool RollTidalLock(CelestialBodyType provisionalType, float rel, bool isMoon)
    {
        if (isMoon) return false;
        if (provisionalType == CelestialBodyType.GasGiant || provisionalType == CelestialBodyType.Asteroid) return false;
        float chance = rel < StarDatabase.HzInnerRel ? 0.10f
                     : rel <= StarDatabase.HzOuterRel ? 0.03f
                     : 0.01f;
        return Random.value < chance;
    }

    /// The rest, rolled once the surface exists — Fertile World needs the finished climate to judge.
    /// Draws from UnityEngine.Random inside the generation stream, like every other generation roll.
    public static void Roll(CelestialBody b)
    {
        if (b == null) return;
        b.worldModifierFlags = 0;
        if (!Solid(b)) return;

        if (Random.value < HighQualityMineralChance) Set(b, WorldModifier.HighQualityMinerals);
        if (Random.value < VastMineralChance) Set(b, WorldModifier.VastMineralDeposits);
        if (b.atmospheres >= ExtremeWeatherMinAtmospheres && Random.value < ExtremeWeatherChance)
            Set(b, WorldModifier.ExtremeWeather);
        if (MeetsFertileClimate(b) && Random.value < FertileWorldChance) Set(b, WorldModifier.FertileWorld);
    }

    /// Worlds a modifier can sit on: solid, and big enough to be a world. Belt rocks are excluded so they
    /// do not inflate the count of each modifier the galaxy is meant to hold.
    static bool Solid(CelestialBody b)
        => b.type != CelestialBodyType.GasGiant && b.type != CelestialBodyType.Asteroid;

    static void Set(CelestialBody b, WorldModifier m) => b.worldModifierFlags |= 1 << (int)m;

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
