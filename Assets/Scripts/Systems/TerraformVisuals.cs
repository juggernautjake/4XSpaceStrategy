using UnityEngine;

// ============================================================================================
// TERRAFORMING YOU CAN WATCH HAPPEN
//
// THE PROBLEM
// TickTerraform did exactly one thing: b.habitability += gain. A number went up. The world it described
// never changed — same continents, same ice, same colour, for the entire minutes-long process. You could
// take a frozen rock to 90% habitability and it still rendered as a frozen rock. Terraforming was a
// progress bar wearing a planet.
//
// THE IDEA
// A world's terrain is generated from terrainParams — how hot, how wet, how mountainous, how big its
// features are. Those are the same knobs the Dev Mode sandbox exposes, and the same ones GalaxyGenerator
// uses to make a species' HOME world match its biology (a Pyrothian cradle reads hot, a Cryithn one
// frozen). So terraforming already had somewhere to go: it should walk a world's terrainParams from what
// nature gave it toward what the species would build for itself.
//
// Then the map is not a readout of terraforming, it IS terraforming. Ice retreats, seas fill, green
// spreads — because the generator is being asked a slightly different question every second.
//
// WHY IT'S A LERP AND NOT A SCRIPT
// The world keeps its identity: same seed, same continents, same coastlines. Only the climate moves. A
// world you terraformed still looks like the world you found, which is what makes it YOURS rather than a
// new planet that replaced it. That falls straight out of holding terrainSeed fixed and moving only the
// amplitude knobs.
// ============================================================================================
public static class TerraformVisuals
{
    /// The climate this species would give a world if it could.
    ///
    /// The heat mapping is GalaxyGenerator's, deliberately — that's what makes a species' home world read
    /// as their kind of place, and terraforming is the act of making somewhere ELSE read that way. Two
    /// different curves for "what this species considers pleasant" would drift apart, and the tell would
    /// be a fully terraformed world that looked nothing like the homeworld.
    ///
    /// USE THE BODY-AWARE OVERLOAD BELOW WHERE A BODY IS IN HAND — this one cannot account for the
    /// greenhouse term, which depends on the world's own atmosphere.
    public static PlanetTerrainGenerator.NoiseParams Ideal(Species s)
    {
        var p = PlanetTerrainGenerator.NoiseParams.Default;
        if (s == null) return p;

        p.heat = Mathf.Lerp(0.55f, 1.7f, Mathf.Clamp01(s.idealTemp));

        // Wet species want water; dry ones want it gone. PrefersDry already encodes this as "would an
        // ocean world be a drowning hazard or a paradise" — the same question, so the same answer.
        p.moisture = s.PrefersDry ? 0.55f : 1.35f;

        // A balanced land/sea split and gentler ground. Nobody's ideal world is all mountain.
        //
        // Two knobs now, and they say different things: ordinary relief (so there ARE hills and valleys
        // to live among) with the sea sitting at a middling height (so roughly half of it is land). The
        // "balanced split" used to be expressed as elevation alone, which produced a flat world rather
        // than a temperate one.
        p.elevation = 1.0f;
        p.seaLevel = s.PrefersDry ? 0.42f : 0.52f;
        p.ridge = 0.75f;

        // NOTE: `scale` is meaningless here and Blend never reads it. Feature scale is the world's own
        // geography — how big its continents are — and terraforming does not move continents.
        return p;
    }

    /// The same ideal, but solved for THIS world's temperature rather than assigned as a raw heat value.
    ///
    /// WHY THE OVERLOAD EXISTS. `heat` is not a temperature — PlanetTemperature.BaseCelsius adds up to
    /// 45°C of greenhouse warming on top of it, scaled by the world's own atmosphere. GalaxyGenerator
    /// builds a species' cradle by solving for the temperature it wants (CradleHeat); if terraforming
    /// kept walking worlds toward the old raw-heat curve instead, the two would describe different
    /// climates for the same species — and the tell would be a fully terraformed world that ends up
    /// HOTTER than the homeworld it was supposed to resemble. For a Terran world that lands at 51°C,
    /// past the 50°C liquid-water ceiling, so maxing terraforming on your own capital would push it out
    /// of the band and kill the biosphere it started with.
    ///
    /// Pinned only when the SPECIES' own cradle is a living one AND the world could actually hold that
    /// climate — see the two-part gate in the body, which explains why each half is needed.
    public static PlanetTerrainGenerator.NoiseParams Ideal(Species s, CelestialBody b)
    {
        var p = Ideal(s);
        if (s == null || b == null) return p;

        // BOTH conditions, and each rules out a different absurdity.
        //
        // The SPECIES gate matches GalaxyGenerator.CradleHeat exactly, so the cradle and the terraform
        // target cannot drift apart. Without it, a Cryithn or Pyrothian working on a rocky world would
        // be pinned to a temperate target — the wrong species' idea of pleasant, on their own world.
        //
        // The BODY gate stops the solve being asked for something the world cannot be. A volcanic world
        // carries a +90°C type modifier, so demanding 23.5°C of it needs heat ≈ 0.44 — under the 0.45
        // floor this model is documented to work in, and low enough that the terrain classifier's
        // temperature field reads frozen while the °C readout says room temperature. You cannot make a
        // lava world temperate by wanting it; you remodel it into a rocky one first (Planetary
        // Remodelling), and the pin applies from the moment its TYPE changes.
        //
        // This costs nothing at the cradle, which is Rocky or Ocean by construction for exactly the
        // three species the species gate lets through — so parity with CradleHeat is exact.
        bool pinnable = b.type == CelestialBodyType.RockyPlanet || b.type == CelestialBodyType.OceanPlanet;
        if (GalaxyGenerator.CradleWantsLife(s) && pinnable)
            p.heat = PlanetTemperature.HeatForCelsius(
                GalaxyGenerator.CradleTargetCelsius(s), b.atmosphereThickness, b.type);

        return p;
    }

    /// Natural params blended toward the ideal by `t` (0 = untouched, 1 = fully terraformed).
    public static PlanetTerrainGenerator.NoiseParams Blend(
        PlanetTerrainGenerator.NoiseParams natural, PlanetTerrainGenerator.NoiseParams ideal, float t)
    {
        t = Mathf.Clamp01(t);
        var p = natural;
        p.heat = Mathf.Lerp(natural.heat, ideal.heat, t);
        p.moisture = Mathf.Lerp(natural.moisture, ideal.moisture, t);
        p.elevation = Mathf.Lerp(natural.elevation, ideal.elevation, t);
        // The sea moves with the rest of the climate. Blended from the neutral-safe accessor so a world
        // loaded from a pre-seaLevel save does not lerp from a literal zero and dry itself out.
        p.seaLevel = Mathf.Lerp(natural.SeaLevelOrNeutral, ideal.SeaLevelOrNeutral, t);
        p.ridge = Mathf.Lerp(natural.ridge, ideal.ridge, t);
        // scale is NOT blended — see Ideal. The continents stay put.
        return p;
    }

    /// The climate a world of a given TYPE tends to have — the terrainParams a directed remodel walks
    /// toward, so the map's temperature, moisture and relief match the world it is BECOMING. Amplitude
    /// knobs against the Default of 1.0 (PlanetTerrainGenerator.NoiseParams); `scale` (continent size) is
    /// left alone, exactly as Ideal does.
    public static PlanetTerrainGenerator.NoiseParams TypeClimate(CelestialBodyType t)
    {
        var p = PlanetTerrainGenerator.NoiseParams.Default;
        switch (t)
        {
            case CelestialBodyType.VolcanicPlanet: p.heat = 1.85f; p.moisture = 0.35f; p.elevation = 1.10f; p.ridge = 1.35f; p.seaLevel = 0.20f; break;
            // An ocean world is DROWNED, not FLAT — high sea level over ordinary relief, so its islands
            // are real mountain tops rather than the last bumps of a world sanded down.
            case CelestialBodyType.OceanPlanet:    p.heat = 1.05f; p.moisture = 1.70f; p.elevation = 0.95f; p.ridge = 0.70f; p.seaLevel = 0.80f; break;
            case CelestialBodyType.IcePlanet:      p.heat = 0.50f; p.moisture = 1.00f; p.elevation = 1.00f; p.ridge = 0.95f; p.seaLevel = 0.60f; break;
            case CelestialBodyType.RockyPlanet:    p.heat = 1.00f; p.moisture = 1.20f; p.elevation = 1.00f; p.ridge = 0.90f; p.seaLevel = 0.50f; break;
            case CelestialBodyType.BarrenPlanet:   p.heat = 1.25f; p.moisture = 0.20f; p.elevation = 1.00f; p.ridge = 1.05f; p.seaLevel = 0.12f; break;
            case CelestialBodyType.Moon:
            case CelestialBodyType.Asteroid:       p.heat = 0.90f; p.moisture = 0.15f; p.elevation = 1.00f; p.ridge = 1.15f; p.seaLevel = 0.08f; break;
            // GasGiant has no solid surface to reclassify toward; leave default.
        }
        return p;
    }

    /// The single writer of a world's terraformed climate. Everything that reshapes a world flows
    /// through here so the knobs never have two owners fighting over them:
    ///   • the species-ideal grind (Blend by habitability progress) — the background morph that was
    ///     already here, now capped by `power` so a low-tech empire can only nudge a world;
    ///   • plus each project's own signature push (TerraformClimate.Accumulated) — completed projects at
    ///     full strength, running ones scaled by their loading bar, which is what makes the ACTION you
    ///     took the thing you see change: seas fill under a Water Convoy, recede under Hydrosphere Venting.
    ///
    /// Pure and deterministic — reads only naturalParams, habitability, completed projects and live job
    /// progress — so it round-trips through save/load with no new per-tile state.
    public static PlanetTerrainGenerator.NoiseParams Compose(CelestialBody b, Species s)
    {
        var natural = b != null ? b.naturalParams : PlanetTerrainGenerator.NoiseParams.Default;
        if (b == null) return natural;

        float power = TerraformPower(b);

        // Background blend. A directed Planetary Remodelling walks the whole climate toward the TARGET
        // type's own profile (a lava world runs hot and dry, an ocean world wet and low) in lock-step with
        // the surface transition. It's a paid, top-tier project, so its reach is the full transition
        // progress, not the tech-gated `power` — once you can remodel a world, you remodel it fully.
        // Otherwise it's the ordinary species-ideal grind, capped by how much terraforming tech you have.
        PlanetTerrainGenerator.NoiseParams p =
            b.remodelToType >= 0
                ? Blend(natural, TypeClimate((CelestialBodyType)b.remodelToType), b.remodelT)
                // The BODY-AWARE ideal — see the overload. With the plain Ideal(s) here, a terraformed
                // world converged on a different climate than the one GalaxyGenerator built the
                // species' cradle with, and for a Terran world that meant drifting past the
                // liquid-water ceiling and losing the biosphere at full terraforming.
                : Blend(natural, Ideal(s, b), Progress(b) * power);

        // Foreground: the specific projects run on THIS world, also scaled by power.
        var d = TerraformClimate.Accumulated(b);
        p.heat      = Mathf.Clamp(p.heat      + d.heat      * power, 0.30f, 2.20f);
        p.moisture  = Mathf.Clamp(p.moisture  + d.moisture  * power, 0.20f, 2.00f);
        // The SEA is what water projects move — the land keeps its shape. See TerraformClimate.ClimateDelta.
        p.seaLevel = Mathf.Clamp01(p.SeaLevelOrNeutral + d.seaLevel * power);
        p.ridge     = Mathf.Clamp(p.ridge     + d.ridge     * power, 0.30f, 2.00f);
        return p;
    }

    /// How far your civilization can push a world away from what nature made it — the transformation
    /// MAGNITUDE, distinct from SpeedFactor's transformation SPEED. This is the "small nudges early, total
    /// reshaping only at max tech" curve the design asks for: an early empire can lift an almost-habitable
    /// world the last few points, but genuinely remaking a world waits on the deep Expansion tree.
    ///
    /// Ceiling bonus stands in for "how much terraforming tech you have" (it sums across the whole X-series
    /// and the Expansion doctrines), empire level for civilizational reach; a big or badly-suited world is
    /// harder to move than a small nearly-right one, so it needs more tech to reach the same power.
    public static float TerraformPower(CelestialBody b)
    {
        float techPart  = Mathf.Clamp01(TechEffects.TerraformCeilingBonus / 150f);
        float levelPart = Mathf.Clamp01((EmpireTech.Level - 1) / 10f);
        float progress  = 0.5f * techPart + 0.5f * levelPart;   // 0 raw start .. 1 deep tree + high level
        float power = Mathf.Lerp(0.15f, 1f, progress);

        // A big world is harder to reshape EARLY, but tech buys that back: the size penalty is itself
        // lerped away by `progress`, so a giant still reaches full power once the deep tree is in — it
        // just needs more tech to get there than a small world does, rather than being permanently capped.
        if (b != null)
        {
            float sizePenalty = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01((b.surfaceSize - 6f) / 12f));
            power *= Mathf.Lerp(sizePenalty, 1f, progress);
        }

        return Mathf.Clamp(power, 0.12f, 1f);
    }

    /// How far along the reshaping is, 0..1.
    ///
    /// Keyed on habitability against the colonisation floor rather than against 100: reaching the floor
    /// is the point of terraforming, so a world that just became liveable should LOOK liveable, not 40%
    /// liveable. Past the floor it keeps improving toward the ideal, more slowly.
    public static float Progress(CelestialBody b)
    {
        if (b == null) return 0f;
        float floor = Colony.FoundThreshold;
        if (b.habitability <= 0f) return 0f;
        if (b.habitability >= floor)
            return Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(floor, 100f, b.habitability));
        return Mathf.Lerp(0f, 0.75f, b.habitability / Mathf.Max(1f, floor));
    }

    /// Habitability must move this far before the surface is regenerated.
    ///
    /// Regenerating is ~12,000 cells x 6 octaves x 3 noise fields, so it is not a per-frame operation —
    /// but it's cheap enough to do every second or so, which is what makes the change readable as motion
    /// rather than as a jump. Small enough to look continuous, big enough not to melt the CPU.
    public const float RegenStep = 1.5f;

    /// Push the world's terrain toward the species' ideal for its current habitability, and rebuild the
    /// visuals if it moved enough to be worth redrawing. Returns true if it regenerated.
    ///
    /// Called from the terraform tick. Safe to call every tick: it does nothing until the world has
    /// actually changed by RegenStep.
    public static bool Advance(CelestialBody b, Species s, bool force = false)
    {
        if (b == null || b.surface == null) return false;
        if (!force && Mathf.Abs(b.habitability - b.lastTerraformRenderHab) < RegenStep) return false;

        b.lastTerraformRenderHab = b.habitability;
        b.terrainParams = Compose(b, s);

        // Same seed, so the same continents — only the climate on them changes.
        b.surface = PlanetTerrainGenerator.GenerateSurface(b);
        OreGenerator.Populate(b);

        // The survey indexes cache a per-world distribution derived from the terrain field. That field
        // just moved, so the cache now describes the planet this used to be — the overlays would be
        // scoring new ground against old statistics.
        SurfaceIndex.InvalidateStats(b);

        // Both places a world is drawn: the globe in space, and the map if you happen to be watching it.
        PlanetAppearance.RefreshTexture(b, b.visualObject);
        PlanetViewWindow.Instance?.RefreshIfShowing(b);
        return true;
    }

    /// Capture the world's untouched climate, once. Everything above lerps FROM this, so it has to be
    /// whatever generation produced — before terraforming has moved anything.
    public static void CaptureNatural(CelestialBody b)
    {
        if (b == null) return;
        b.naturalParams = b.terrainParams;
        b.naturalSeed = b.terrainSeed;   // the seed the world was generated with, for "Reset to default"
        b.lastTerraformRenderHab = b.habitability;
    }
}
