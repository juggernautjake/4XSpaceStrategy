using UnityEngine;

// Generates a planet's surface from a resolution-independent, deterministic noise field.
//
// The whole point: SampleNormalized(u,v) returns the same terrain for the same (u,v) no matter how
// many tiles/pixels you sample. So the low-res grid viewer and the high-res detailed map render the
// SAME continents and oceans — the detailed view just samples more densely (and with extra octaves)
// to reveal finer coastlines and features. Both are driven by body.terrainSeed + continentFrequency.
public static class PlanetTerrainGenerator
{
    public struct NoiseParams
    {
        public float scale;      // frequency multiplier (feature density)
        public float elevation, moisture, heat, ridge; // amplitude multipliers
        public static NoiseParams Default => new NoiseParams
        { scale = 1f, elevation = 1f, moisture = 1f, heat = 1f, ridge = 1f };
    }

    // "Water Level" bounds — the Terrain Sandbox's Elevation slider's old range, kept as the amplitude
    // window the Water Level slider maps onto (see WaterLevelFromElevation/ElevationFromWaterLevel).
    public const float ElevationMin = 0.3f, ElevationMax = 2f;

    // Lower elevation amplitude means more of the noise field falls below the biome classifiers' water
    // thresholds (Terran/OceanWorld/Ice all threshold low elev as water), so "more water" is the LOW end
    // of elevation. Water Level reads the opposite way round (full = fully covered), hence the inversion.
    // Public/shared so both the sandbox UI (PlanetViewWindow) and terraforming gates (BiosphereRules) read
    // "how much water" through the one place that knows how elevation maps to it.
    public static float WaterLevelFromElevation(float elevation) => Mathf.InverseLerp(ElevationMax, ElevationMin, elevation);
    public static float ElevationFromWaterLevel(float waterLevel) => Mathf.Lerp(ElevationMax, ElevationMin, waterLevel);

    public struct Sample
    {
        public TerrainType terrain;
        public float shade;      // 0..1 per-pixel brightness jitter
        public float elevation;  // 0..1
        public bool water;

        // The rest of the field the biome was CLASSIFIED from. Exposed so gameplay (SurfaceIndex) can
        // read the same numbers the terrain was made of, instead of inventing a parallel noise field
        // that happens to disagree with what the map shows. This is what makes an ocean reliably cooler
        // than a desert on the same world: they aren't two guesses, they're one temperature value —
        // the ocean is an ocean BECAUSE of it.
        public float temperature;  // 0..1, scaled by the planet's own heat (distance from its star)
        public float moisture;     // 0..1
        public float ridge;        // 0..1 — broken, mountainous ground
        public float latitude;     // 0 equator .. 1 pole
    }

    // How much a convergent plate boundary lifts the ridge (mountain-building) field at the fault. A
    // strong head-on collision (boundary≈1, convergence≈1) adds this much, enough to push mid-ridge ground
    // over the Mountains threshold; a divergent boundary subtracts it (a thinning rift). Tuned by eye —
    // there is no Editor here to calibrate against.
    const float TectonicRidgeGain = 0.6f;

    // Octaves of noise the SURFACE GRID is built from.
    //
    // Six, matching what SurfaceTextureRenderer has always used to draw the detail map. It was four,
    // which was the right call while the grid was six times coarser than the render — there is no point
    // resolving detail finer than a cell. Now that a cell IS a detail texel, those two extra octaves are
    // the coastlines and fine features the map exists to show.
    public const int Octaves = 6;

    // ---- The surface grid ----
    // The one grid: gameplay builds on it, and every map renders one texel per cell of it.
    // Uses the body's own terrainParams so live edits are reflected everywhere consistently.
    public static PlanetSurface GenerateSurface(CelestialBody body)
    {
        return Build(body, body.terrainParams, Octaves);
    }

    public static PlanetSurface GenerateSurfaceWithParams(
        CelestialBody body, float noiseScale, float elevationStrength,
        float moistureStrength, float heatStrength, float ridgeStrength)
    {
        body.terrainParams = new NoiseParams
        {
            scale = Mathf.Clamp(noiseScale <= 0f ? 1f : noiseScale, 0.3f, 4f),
            elevation = Mathf.Max(0.1f, elevationStrength),
            moisture = Mathf.Max(0.1f, moistureStrength),
            heat = Mathf.Max(0.1f, heatStrength),
            ridge = Mathf.Max(0.1f, ridgeStrength)
        };
        return Build(body, body.terrainParams, Octaves);
    }

    static PlanetSurface Build(CelestialBody body, NoiseParams p, int octaves)
    {
        // Dimensions come from MapMetrics, which every map renderer also reads. They used to be computed
        // here as `surfaceSize * 2` while the detail renderer independently used `surfaceSize * 2 * 6`,
        // and the two silently disagreed by a factor of six on each axis — which is exactly why a 1x1
        // building was drawn six terrain pixels wide.
        int width = MapMetrics.SurfW(body.surfaceSize);
        int height = MapMetrics.SurfH(body.surfaceSize);

        PlanetSurface surface = new PlanetSurface(width, height);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                Sample s = SampleNormalized(body, u, v, p, octaves);
                surface.tiles[x, y] = new TerrainTile(s.terrain, s.shade);
            }
        return surface;
    }

    // ---- The shared, resolution-independent sampler ----
    public static Sample SampleNormalized(CelestialBody body, float u, float v, NoiseParams p, int octaves)
    {
        float seed = body.terrainSeed;
        float freq = Mathf.Max(1f, body.continentFrequency) * p.scale;

        // 2:1 map aspect -> stretch u so continents stay roughly square.
        float fx = u * freq * 2f;
        float fy = v * freq;

        float elevation = FBm(fx + seed, fy + seed * 1.3f, octaves) * p.elevation;
        float moisture  = FBm(fx * 1.3f + seed + 31f, fy * 1.3f + seed + 17f, octaves) * p.moisture;
        float ridge     = FBm(fx * 2.2f + seed + 91f, fy * 2.2f + seed + 53f, octaves) * p.ridge;

        float lat = Mathf.Abs(v - 0.5f) * 2f;                 // 0 equator, 1 pole
        float heatNoise = FBm(fx * 0.9f + seed + 11f, fy * 0.9f + seed + 7f, 2);
        float temperature = Mathf.Clamp01(((1f - lat) * 0.75f + heatNoise * 0.45f) * p.heat);

        float fine = Mathf.PerlinNoise(fx * 6f + seed, fy * 6f + seed);

        // A directed Planetary Remodelling project spreads a NEW world type across the old one. Tiles
        // whose low-frequency mask value has been overtaken by the transition progress (body.remodelT,
        // 0..1) are classified as the target type, so the new world grows as smooth, contiguous regions —
        // lava creeping across a jungle — rather than the whole planet snapping over at completion. The
        // mask is a stable function of position + seed, so the transition is deterministic and identical
        // at any resolution (grid and globe agree), and survives save/load.
        CelestialBodyType classifyType = body.type;
        if (body.remodelToType >= 0 && body.remodelT > 0.001f && body.remodelToType != (int)body.type)
        {
            float mask = FBm(fx * 0.6f + seed + 211f, fy * 0.6f + seed + 173f, 3);
            if (mask < body.remodelT) classifyType = (CelestialBodyType)body.remodelToType;
        }

        // A world with no active biosphere (CelestialBody.biosphereActive — see BiosphereRules) has no
        // plant cover regardless of how wet its moisture noise field rolled. Clamped HERE, before both the
        // classifier and the exposed Sample, so the rendered tile and the moisture value SurfaceIndex's
        // Fertile/Solar overlays read never disagree with each other. Only Terran-classified (RockyPlanet)
        // worlds care about this — GasGiant's cloud bands and Ice's crystal fields use moisture for
        // reasons that have nothing to do with plant life, so they're deliberately left untouched.
        if (classifyType == CelestialBodyType.RockyPlanet && !body.biosphereActive)
            moisture = Mathf.Min(moisture, 0.1f);

        // Plate tectonics fold mountains up ALONG the fault lines, exactly where the Survey overlay draws
        // them (both read this one TectonicsMap, so the map and the overlay can never disagree). A
        // convergent boundary (two plates driving together) adds ridge in proportion to how close the tile
        // is to the fault AND how hard they collide; a divergent rift subtracts a little. This concentrates
        // ranges and volcanoes at the boundaries instead of scattering them by noise, on top of the
        // whole-world ruggedness TectonicsRules.BoostRidge already gave the world. Faults that run under
        // the sea stay sea — every classifier tests elevation for water BEFORE ridge, so a low, drowned
        // fault reads as ocean (Earth's mid-ocean ridges), and only a fault crossing high ground becomes a
        // mountain belt. Derived per-sample, so it costs no save state and a remodel/reseed re-derives it.
        if (TectonicsMap.Active(body))
        {
            var tec = TectonicsMap.Sample(body, u, v);
            ridge = Mathf.Clamp(ridge + tec.boundary * tec.convergence * TectonicRidgeGain, 0f, 2f);
        }

        TerrainType t = Classify(classifyType, elevation, moisture, temperature, ridge, lat);

        // CLIMATE COHERENCE. The classifier's `temperature` is latitude-dominated (equator warm, poles cold)
        // and only lightly scaled by the world's heat, so on its own a globally FRIGID world could still show
        // a liquid equatorial sea, and a globally SCORCHING one could still grow jungle. Re-judge the water
        // and vegetation tiles against the tile's ACTUAL temperature in °C — the very figure PlanetTemperature
        // shows the player — so the two always agree: a −70°C world's seas read as ice, and a 100°C world
        // grows no rainforest. Computed from the SAME heat/atmosphere/type the readout uses, plus the standard
        // ±15°C equator→pole swing and a small local-weather wobble from the heat noise.
        float baseC = PlanetTemperature.BaseCelsius(p.heat, body.atmosphereThickness, classifyType);
        float tileC = baseC + Mathf.Lerp(15f, -15f, lat) + (heatNoise - 0.5f) * 12f;
        t = ClimateCoherence(t, tileC);

        return new Sample
        {
            terrain = t, shade = fine, elevation = elevation, water = IsWater(t),
            temperature = temperature, moisture = Mathf.Clamp01(moisture),
            ridge = Mathf.Clamp01(ridge), latitude = lat
        };
    }

    public static bool IsWater(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Ocean:
            case TerrainType.Lake:
            case TerrainType.River:
            case TerrainType.Reef:
            case TerrainType.FrozenSea:
                return true;
            default:
                return false;
        }
    }

    // ---- Climate coherence thresholds (°C, matching PlanetTemperature so the map agrees with the readout) ----
    // The game's °C runs a little warm (greenhouse + calibration), so an Earth-like world's tropics read
    // ~40-50°C and still deserve jungle — the vegetation ceilings sit above that, and only bite on genuinely
    // hostile worlds. Easy knobs if the balance wants nudging.
    const float FreezeC = 0f;        // liquid water / warm vegetation can't persist below this
    const float DeepFreezeC = -25f;  // below this even hardy groundcover is buried — snow, not tundra
    const float LushMaxC = 55f;      // above this, rainforest & wetland thin to hardier tropical cover
    const float ScorchC = 75f;       // above this, no vegetation survives — bare, sun-baked ground
    const float BakedC = 100f;       // above this, that bare ground reads as wasteland, not just desert

    // Re-judge one classified tile against its real temperature. Only water and vegetation are touched;
    // rock, mountains, sand, lava and already-frozen tiles are left exactly as the per-type classifier
    // decided. Deterministic and resolution-independent (pure function of the tile type and its °C), so the
    // grid and the detailed globe stay identical.
    static TerrainType ClimateCoherence(TerrainType t, float tileC)
    {
        // --- Below freezing: liquid water turns to ice; warm vegetation dies back to frozen ground ---
        if (tileC < FreezeC)
        {
            switch (t)
            {
                case TerrainType.Ocean:
                case TerrainType.Lake:
                case TerrainType.River:
                case TerrainType.Reef:
                    return TerrainType.FrozenSea;
                case TerrainType.Beach:
                    return TerrainType.Snow;
                case TerrainType.Jungle:
                case TerrainType.Forest:
                case TerrainType.Swamp:
                case TerrainType.Grassland:
                case TerrainType.Savanna:
                case TerrainType.Plains:
                case TerrainType.Steppe:
                    return tileC < DeepFreezeC ? TerrainType.Snow : TerrainType.Tundra;
            }
            return t;
        }

        // --- Too hot for lush growth: rainforest & wetland step down to hardy cover ---
        if (tileC > ScorchC)
        {
            switch (t)
            {
                case TerrainType.Jungle:
                case TerrainType.Forest:
                case TerrainType.Swamp:
                case TerrainType.Grassland:
                case TerrainType.Savanna:
                case TerrainType.Taiga:
                case TerrainType.Steppe:
                case TerrainType.Plains:
                    return tileC > BakedC ? TerrainType.Wasteland : TerrainType.Desert;
            }
            return t;
        }

        if (tileC > LushMaxC)
        {
            switch (t)
            {
                case TerrainType.Jungle: return TerrainType.Savanna;    // rainforest -> tropical grassland
                case TerrainType.Swamp:  return TerrainType.Grassland;  // wetland dries out
                case TerrainType.Forest: return TerrainType.Grassland;
            }
        }
        return t;
    }

    static float FBm(float x, float y, int octaves)
    {
        float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return norm > 0f ? sum / norm : 0f;
    }

    // ---- Biome classification (deterministic; identical logic at any resolution) ----
    static TerrainType Classify(CelestialBodyType planet, float elev, float moist, float temp, float ridge, float lat)
    {
        switch (planet)
        {
            case CelestialBodyType.GasGiant:       return GasGiant(lat, elev, moist);
            case CelestialBodyType.VolcanicPlanet: return Volcanic(elev, temp, ridge, lat);
            case CelestialBodyType.IcePlanet:      return Ice(elev, moist, temp, ridge, lat);
            case CelestialBodyType.OceanPlanet:    return OceanWorld(elev, temp, lat);
            case CelestialBodyType.BarrenPlanet:   return Barren(elev, ridge);
            case CelestialBodyType.Moon:
            case CelestialBodyType.Asteroid:       return Airless(elev, temp, ridge);
            case CelestialBodyType.RockyPlanet:
            default:                               return Terran(elev, moist, temp, ridge);
        }
    }

    static TerrainType GasGiant(float lat, float elev, float moist)
    {
        float band = Mathf.Repeat((lat + moist * 0.3f) * 6f, 1f);
        if (elev > 0.78f) return TerrainType.Storm;      // great-spot style storm
        return band < 0.5f ? TerrainType.GasClouds : TerrainType.Storm;
    }

    static TerrainType Volcanic(float elev, float temp, float ridge, float lat)
    {
        float hot = temp + (1f - lat) * 0.2f;
        if (hot > 0.9f && ridge > 0.7f) return TerrainType.Volcano;
        if (hot > 0.78f) return TerrainType.MagmaField;
        if (ridge > 0.72f) return TerrainType.Mountains;
        if (elev > 0.62f)  return TerrainType.LavaRock;
        if (elev < 0.32f)  return TerrainType.ObsidianFlat;
        if (temp > 0.6f)   return TerrainType.AshWaste;
        if (ridge > 0.55f) return TerrainType.CrackedGround;
        return TerrainType.GeyserField;
    }

    static TerrainType Ice(float elev, float moist, float temp, float ridge, float lat)
    {
        // Same liquid-water threshold Terran freezes its oceans at (elev<0.36 -> FrozenSea below 0.22),
        // so warming an Ice world through terraforming melts these tiles at the point Terran would
        // refreeze them — one shared threshold rather than two climates that quietly disagree. This is
        // what turns a maxed-out Water Level slider from an ice-covered world into an ocean world as the
        // Temperature slider (or terraforming) pushes it above freezing.
        bool frozen = temp < 0.22f;
        // Low ground is (frozen) sea BEFORE ridge is considered, matching Terran — otherwise a
        // mountain-building fault line (or a stray ridge-noise peak) crossing the low band would raise
        // Mountains straight out of the frozen sea. A drowned fault stays sea, as Earth's mid-ocean
        // ridges do; only faults over high ground fold up into ranges.
        if (elev < 0.3f)   return frozen ? TerrainType.FrozenSea : TerrainType.Ocean;
        if (ridge > 0.8f)  return TerrainType.Mountains;
        if (elev > 0.72f)  return frozen ? TerrainType.Glacier : TerrainType.Highlands;
        if (moist > 0.72f) return frozen ? TerrainType.CrystalField : TerrainType.Lake;
        if (lat < 0.25f && elev > 0.5f) return frozen ? TerrainType.Snow : TerrainType.Beach;
        // The bulk mid-elevation band is genuine ice SHEET, not a frozen sea — only the low-elevation
        // band above is actually water once melted (elev<0.3, handled above). Melting the rest into
        // Tundra keeps "how much of the map is water" tied to elevation the same way every other
        // classifier in this file does, instead of quietly turning a third of the map into ocean.
        return frozen ? TerrainType.Ice : TerrainType.Tundra;
    }

    static TerrainType OceanWorld(float elev, float temp, float lat)
    {
        if (elev > 0.80f) return TerrainType.Mountains;
        if (elev > 0.70f) return TerrainType.Island;
        if (elev > 0.64f) return TerrainType.Beach;
        if (lat > 0.85f)  return TerrainType.FrozenSea;
        if (temp < 0.25f) return TerrainType.FrozenSea;   // a cooled ocean world freezes over, pole to pole
        if (elev < 0.40f && temp > 0.6f) return TerrainType.Reef;
        return TerrainType.Ocean;
    }

    static TerrainType Barren(float elev, float ridge)
    {
        if (ridge > 0.82f) return TerrainType.Mountains;
        if (ridge > 0.7f)  return TerrainType.Canyon;
        if (elev > 0.66f)  return TerrainType.Highlands;
        if (elev < 0.3f)   return TerrainType.SaltFlat;
        if (ridge > 0.5f)  return TerrainType.Badlands;
        if (elev > 0.55f)  return TerrainType.MetallicCrust;
        return TerrainType.Wasteland;
    }

    static TerrainType Airless(float elev, float temp, float ridge)
    {
        if (ridge > 0.85f) return TerrainType.Highlands;
        if (elev > 0.7f)   return TerrainType.MetallicCrust;
        if (elev < 0.28f)  return TerrainType.Crater;
        if (ridge > 0.72f) return TerrainType.CrystalField;
        // A moon's own orbital heat (SolarSystemGenerator.BiasHeat sets terrainParams.heat from its
        // distance from the star, same as its parent planet's band) used to be computed and stored but
        // never actually read here — every moon showed frost in its low ground regardless of how hot its
        // orbit ran. Same freeze threshold Terran/Ice already use, so a moon's look agrees with its own
        // °C reading (PlanetTemperature) the same way a planet's does.
        if (elev < 0.4f)   return temp < 0.22f ? TerrainType.Ice : TerrainType.CrackedGround;
        return TerrainType.Barren;
    }

    static TerrainType Terran(float elev, float moist, float temp, float ridge)
    {
        // Open water freezes when the world runs cold — so cooling a world (orbital shades, core cooling,
        // moving it outward) visibly ices its seas over, and warming one thaws them back. Temperature is
        // the same value PlanetTemperature reads, so the map and the °C readout always agree.
        if (elev < 0.36f) return temp < 0.22f ? TerrainType.FrozenSea : TerrainType.Ocean;
        if (elev < 0.40f) return temp < 0.22f ? TerrainType.Snow : TerrainType.Beach;

        if (ridge > 0.82f) return TerrainType.Mountains;
        if (elev > 0.74f)  return TerrainType.Highlands;
        if (elev > 0.66f)  return TerrainType.Hills;

        // No-biosphere flooring (CelestialBody.biosphereActive) already happened in SampleNormalized
        // before moist reached here, so this function doesn't need to know about it at all — moist is
        // just moist, and the Sample exposed to SurfaceIndex's Fertile/Solar overlays used the same
        // floored value, so the map and the overlays can't disagree.

        if (temp < 0.28f)
        {
            if (moist > 0.55f) return TerrainType.Taiga;
            return (elev > 0.5f) ? TerrainType.Snow : TerrainType.Tundra;
        }

        if (temp < 0.62f)
        {
            if (elev < 0.44f && moist > 0.7f) return TerrainType.Swamp;
            if (moist > 0.62f) return TerrainType.Forest;
            if (moist > 0.4f)  return TerrainType.Grassland;
            if (moist > 0.25f) return TerrainType.Plains;
            return TerrainType.Steppe;
        }

        if (moist > 0.66f) return TerrainType.Jungle;
        if (moist > 0.42f) return TerrainType.Savanna;
        if (moist > 0.25f) return TerrainType.Plains;
        if (moist > 0.14f) return TerrainType.Dunes;
        return TerrainType.Desert;
    }
}
