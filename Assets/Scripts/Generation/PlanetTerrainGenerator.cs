using System.Collections;
using System.Collections.Generic;
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
        // Drain the stepped version. ONE implementation of the terrain build, two entry points — the same
        // pattern GalaxyGenerator and SolarSystemGenerator already use for their own stepped twins. A
        // second copy of this loop would be a second place for the two views of a world to drift apart.
        PlanetSurface result = null;
        var it = BuildStepped(body, p, octaves, s => result = s);
        while (it.MoveNext()) { }
        return result;
    }

    /// How long a single frame may spend building terrain before yielding, in milliseconds.
    ///
    /// A TIME budget rather than a row count, because grids run from 10x5 to 640x320 — any fixed number
    /// of rows gives a moon five frames and a gas giant three hundred. Six milliseconds leaves room for
    /// the loading screen's own work inside a 16ms frame.
    const double StepBudgetMs = 6.0;

    /// The terrain build, time-sliced.
    ///
    /// This is the fix for the loading screen's framerate. A world's whole surface used to be built
    /// between two yields, so one frame lasted as long as generating an entire planet — 100ms for a
    /// small one, several hundred for a gas giant. The dots, the bar and the star's pop-out all animate
    /// per frame, so at three to eight frames a second they crawled. Yielding inside the loop turns one
    /// enormous frame into dozens of ordinary ones; the same total work, spread where it can be seen.
    ///
    /// Hands the finished surface back through a callback because C# iterators cannot have out params.
    public static IEnumerator BuildStepped(CelestialBody body, NoiseParams p, int octaves,
                                           System.Action<PlanetSurface> done)
    {
        // Dimensions come from MapMetrics, which every map renderer also reads. They used to be computed
        // here as `surfaceSize * 2` while the detail renderer independently used `surfaceSize * 2 * 6`,
        // and the two silently disagreed by a factor of six on each axis — which is exactly why a 1x1
        // building was drawn six terrain pixels wide.
        int width = MapMetrics.SurfW(body);
        int height = MapMetrics.SurfH(body);

        PlanetSurface surface = new PlanetSurface(width, height);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                Sample s = SampleNormalized(body, u, v, p, octaves);
                surface.tiles[x, y] = new TerrainTile(s.terrain, s.shade);
            }

            // Checked per COLUMN, not per cell: Stopwatch.Elapsed is not free, and a single column is a
            // few hundred samples at most — fine grain enough to hold the budget without measuring it
            // into the ground.
            if (clock.Elapsed.TotalMilliseconds >= StepBudgetMs)
            {
                yield return null;
                clock.Restart();
            }
        }

        // Neighbour-aware clean-up the per-cell noise can't do on its own: connect water bodies (a small
        // pool touching the open sea IS the sea) and ring the oceans with beaches. Runs on the GRID — the
        // surface the Planet View map and gameplay read — so those agree; the distant 3D globe (a separate
        // per-pixel render) keeps the smooth noise view, which reads the same from orbit.
        // Remove speckle BEFORE the water/shore pass, so flood-fill and beaches run on the terrain the
        // player will actually see rather than on a noisier draft of it.
        //
        // Each gets its own frame. They are O(w*h) too — around twenty times cheaper per cell than
        // sampling, but on a 640-wide world that is still long enough to be a visible hitch on its own.
        yield return null;
        DespeckleTerrain(surface);
        yield return null;
        ApplyWaterAndShores(surface);

        done?.Invoke(surface);
    }

    // Neighbour coherence: a tile with no neighbour of its own kind becomes the local majority.
    //
    // The classifier decides each cell independently from continuous fields, so wherever a field sits on
    // a threshold it flickers cell to cell and the map speckles — one tundra pixel in a desert, a lone
    // jungle tile on a glacier. Individually each is a defensible reading of the noise; together they
    // read as static, and they make terrain look random rather than placed.
    //
    // Only ISOLATED cells are touched — zero orthogonal neighbours matching — so genuine two-cell
    // features, coastlines and thin ridges all survive. Water is left alone entirely: an isolated water
    // tile is a pond, which is a real thing, and ApplyWaterAndShores is the pass that judges water bodies
    // by size on purpose.
    //
    // Decided from a SNAPSHOT rather than in place, so the result does not depend on which corner the
    // loop started from — an in-place filter feeds its own output forward and smears features east.
    /// Tiles that are SUPPOSED to appear alone, and so must survive a filter that deletes lone tiles.
    ///
    /// A volcano is a single cell where a fault crosses high ground — that is what it is. Despeckling
    /// treats "no neighbour like me" as evidence of noise, which is right for a stray tundra pixel in a
    /// desert and exactly wrong for these: it would quietly delete the rarest and most interesting
    /// features on a world, and they are the ones the survey overlays and ore generation key off.
    static bool IsRareFeature(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Volcano:
            case TerrainType.GeyserField:
            case TerrainType.CrystalField:
                return true;
            default:
                return false;
        }
    }

    static void DespeckleTerrain(PlanetSurface surf)
    {
        int w = surf.width, h = surf.height;
        if (w < 3 || h < 3) return;   // too small for "isolated" to mean anything

        var src = new TerrainType[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                src[x, y] = surf.tiles[x, y].type;

        // A flat array indexed by the enum, NOT a Dictionary<TerrainType,int>.
        //
        // On Unity's Mono runtime there is no default comparer for a user enum, so a dictionary keyed by
        // one falls back to ObjectEqualityComparer and BOXES the key on every lookup and every write.
        // This inner loop does up to eight of each per land cell, which on a 45,000-cell planet is a few
        // hundred thousand throwaway allocations — enough to force a collection mid-load, and a GC pause
        // is precisely the stutter this pass is supposed to be invisible for.
        int typeCount = System.Enum.GetValues(typeof(TerrainType)).Length;
        var counts = new int[typeCount];

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                var t = src[x, y];
                if (IsWater(t)) continue;
                if (IsRareFeature(t)) continue;

                // Longitude wraps, latitude does not — the same asymmetry the flood fill uses.
                int xl = (x - 1 + w) % w, xr = (x + 1) % w;
                System.Array.Clear(counts, 0, counts.Length);

                bool anySame = false;
                void Consider(TerrainType n)
                {
                    if (n == t) anySame = true;
                    if (IsWater(n)) return;              // never promote a land tile to water
                    counts[(int)n]++;
                }

                Consider(src[xl, y]);
                Consider(src[xr, y]);
                if (y > 0) Consider(src[x, y - 1]);
                if (y < h - 1) Consider(src[x, y + 1]);

                if (anySame) continue;                   // not isolated — leave it alone

                TerrainType best = t; int bestCount = 0;
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > bestCount) { bestCount = counts[i]; best = (TerrainType)i; }

                // Needs a real majority: two of the four neighbours agreeing. One vote is not a consensus,
                // and at a genuine three-way border every choice is arbitrary — better to leave the
                // classifier's answer than to pick one at random.
                if (bestCount >= 2) surf.tiles[x, y].type = best;
            }
    }

    // Post-classification clean-up that needs to see a tile's NEIGHBOURS (the per-cell sampler can't).
    //   1. Water bodies are flood-filled: a large connected body is OCEAN, a small isolated one is a LAKE —
    //      so a lake that touches the ocean is part of the ocean (same body, classed by total size), and a
    //      pond cut off from the sea reads as its own lake. Frozen water stays frozen; open-ocean reefs are
    //      kept when their body is a sea.
    //   2. Shorelines: soft lowland immediately touching the open ocean becomes BEACH, so coasts read as
    //      beaches rather than jungle/desert running straight into the surf. Cold/rocky shores are left as
    //      the classifier set them (a snowy or cliff coast, not a sandy one).
    // Longitude wraps in x (a 2:1 map); latitude does not (the poles are edges). Deterministic — the same
    // grid in gives the same grid out — so save/load and terraform re-runs reproduce it exactly.
    static void ApplyWaterAndShores(PlanetSurface surf)
    {
        int w = surf.width, h = surf.height;
        if (w <= 0 || h <= 0) return;
        var tiles = surf.tiles;

        // ---- 1) Water bodies -> Ocean (large) or Lake (small enclosed) ----
        var visited = new bool[w, h];
        // A water body at least this big is open sea rather than an enclosed lake.
        //
        // The floor used to be a flat 10 cells, which was fine when the smallest grid was 96x48 (4,608
        // cells) and is nonsense now that grid size tracks mass: a mass-0.1 moon gets 10x5, where a
        // 10-cell minimum means a fifth of the entire globe must be one connected body of water before
        // anything counts as an ocean — so tiny worlds came out with no oceans, and since beaches only
        // ring Ocean tiles, no coastlines either.
        //
        // ONLY the floor changed. The old expression was `Max(10, (w*h)/18)`, and it is worth being
        // precise about which half of it was doing the work: at the old 96x48 minimum, (w*h)/18 is 256,
        // so the divisor always won and the flat 10 never bound at all. 1/18 IS the rule, and it stays.
        // Lowering the floor from 10 to 3 changes behaviour only below ~180 cells — grids that could not
        // exist before and now can.
        int seaMin = Mathf.Max(3, (w * h) / 18);
        var stack = new Stack<int>();
        var bodyCells = new List<int>();

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (visited[x, y] || !IsWater(tiles[x, y].type)) continue;

                bodyCells.Clear();
                stack.Clear();
                stack.Push(x * h + y);
                visited[x, y] = true;
                while (stack.Count > 0)
                {
                    int packed = stack.Pop();
                    int cx = packed / h, cy = packed % h;
                    bodyCells.Add(packed);
                    PushWater(tiles, visited, stack, (cx + 1) % w, cy, h);
                    PushWater(tiles, visited, stack, (cx - 1 + w) % w, cy, h);
                    if (cy + 1 < h) PushWater(tiles, visited, stack, cx, cy + 1, h);
                    if (cy - 1 >= 0) PushWater(tiles, visited, stack, cx, cy - 1, h);
                }

                bool sea = bodyCells.Count >= seaMin;
                foreach (int packed in bodyCells)
                {
                    int cx = packed / h, cy = packed % h;
                    var tt = tiles[cx, cy].type;
                    if (tt == TerrainType.FrozenSea) continue;             // ice stays ice, sea or lake
                    if (tt == TerrainType.Reef && sea) continue;          // reefs belong to open ocean
                    tiles[cx, cy].type = sea ? TerrainType.Ocean : TerrainType.Lake;
                }
            }

        // ---- 2) Beaches ring the open ocean ----
        // Snapshot the ocean mask first so a newly-made beach doesn't seed another beach one tile inland.
        var isOcean = new bool[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                isOcean[x, y] = tiles[x, y].type == TerrainType.Ocean;

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!BeachEligible(tiles[x, y].type)) continue;
                bool coastal = isOcean[(x + 1) % w, y] || isOcean[(x - 1 + w) % w, y]
                            || (y + 1 < h && isOcean[x, y + 1]) || (y - 1 >= 0 && isOcean[x, y - 1]);
                if (coastal) tiles[x, y].type = TerrainType.Beach;
            }
    }

    static void PushWater(TerrainTile[,] tiles, bool[,] visited, Stack<int> stack, int nx, int ny, int h)
    {
        if (visited[nx, ny]) return;
        if (!IsWater(tiles[nx, ny].type)) return;
        visited[nx, ny] = true;
        stack.Push(nx * h + ny);
    }

    // The soft lowland biomes that read as a sandy/gentle coast when they meet the sea. Mountains, hills,
    // highlands, volcanoes, ice, rock and cold biomes are excluded — those make cliffs, snowy or rocky
    // shores, not beaches.
    static bool BeachEligible(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Plains:
            case TerrainType.Grassland:
            case TerrainType.Savanna:
            case TerrainType.Steppe:
            case TerrainType.Forest:
            case TerrainType.Jungle:
            case TerrainType.Swamp:
            case TerrainType.Desert:
            case TerrainType.Dunes:
            case TerrainType.Wasteland:
            case TerrainType.Badlands:
                return true;
            default:
                return false;
        }
    }

    // ---- The shared, resolution-independent sampler ----
    public static Sample SampleNormalized(CelestialBody body, float u, float v, NoiseParams p, int octaves)
    {
        float seed = body.terrainSeed;
        float freq = Mathf.Max(1f, body.continentFrequency) * p.scale;

        // 2:1 map aspect -> stretch u so continents stay roughly square. The u term is folded into WrapU
        // (which needs the span, not the coordinate), so only the v term is precomputed here.
        float fy = v * freq;

        // Every longitude field goes through WrapU so the map JOINS: the value at u=1 is identical to the
        // value at u=0, by construction rather than by luck. Wrapped on a globe the two edges are the same
        // meridian, so anything sampled on a flat plane leaves a hard seam there — a continent chopped in
        // half, a coastline that stops dead, a climate band that jumps. See WrapU.
        float elevation = WrapU(u, freq * 2f,        1f,   fy,        seed,       seed * 1.3f, octaves) * p.elevation;
        float moisture  = WrapU(u, freq * 2f,        1.3f, fy * 1.3f, seed + 31f, seed + 17f,  octaves) * p.moisture;
        float ridge     = WrapU(u, freq * 2f,        2.2f, fy * 2.2f, seed + 91f, seed + 53f,  octaves) * p.ridge;

        float lat = Mathf.Abs(v - 0.5f) * 2f;                 // 0 equator, 1 pole
        float heatNoise = WrapU(u, freq * 2f,        0.9f, fy * 0.9f, seed + 11f, seed + 7f,   2);
        // Altitude cools the surface (atmospheric lapse rate): ground high above sea level runs colder than
        // lowland at the same latitude, so mountains and highlands cap with snow and ice even in a warm
        // band, and the coldest peaks freeze outright. Only ground ABOVE the mid-elevation is cooled, so
        // seas and lowlands are untouched. `altCool` is reused for the °C reading below so the map and the
        // temperature readout agree on how cold the heights are.
        float altCool = Mathf.Max(0f, elevation - 0.6f);

        // THE FLAT LATITUDE BAND BUG.
        //
        // This used to be `Clamp01(((1-lat)*0.75 + heatNoise*0.45) * p.heat - altCool*0.55)`, and the
        // bracket peaks at 0.75 + 0.45 = 1.20 at the equator. Multiply by any p.heat >= 1 and the result
        // passes 1 well before the equator, so Clamp01 pinned an entire latitude band to exactly 1.0 —
        // noise, altitude and all. Inside that band every tile got an identical temperature, so the
        // classifier returned one terrain for the whole strip: a perfectly rectangular horizontal bar
        // across the map, with hard straight edges at the latitudes where the expression crossed 1.
        //
        // The fix is to make saturation impossible rather than to tune around it. The latitude+noise term
        // is normalised to 0..1 BY CONSTRUCTION, and heat is applied as a power curve instead of a
        // multiplier. A power curve is strictly monotonic on 0..1: it warms or cools the whole world and
        // maps the endpoints to themselves, but it can never map two different inputs to the same output,
        // so a flat band cannot form at any heat setting.
        const float LatWeight = 0.75f;
        const float NoiseWeight = 0.45f;
        float band = ((1f - lat) * LatWeight + heatNoise * NoiseWeight) / (LatWeight + NoiseWeight);
        band = Mathf.Clamp01(band - altCool * 0.55f);

        // heat > 1 -> exponent < 1 -> curve bends up (warmer); heat < 1 -> exponent > 1 -> cooler.
        float heatExp = Mathf.Clamp(1f / Mathf.Max(0.05f, p.heat), 0.2f, 5f);
        float temperature = Mathf.Pow(band, heatExp);

        // Wrapped too. This is the highest-frequency field, so an unwrapped one shows as a thin line of
        // mismatched per-tile detail down the join even when the continents themselves line up.
        float fine = WrapU(u, freq * 2f, 6f, fy * 6f, seed, seed, 1);

        // A directed Planetary Remodelling project spreads a NEW world type across the old one. Tiles
        // whose low-frequency mask value has been overtaken by the transition progress (body.remodelT,
        // 0..1) are classified as the target type, so the new world grows as smooth, contiguous regions —
        // lava creeping across a jungle — rather than the whole planet snapping over at completion. The
        // mask is a stable function of position + seed, so the transition is deterministic and identical
        // at any resolution (grid and globe agree), and survives save/load.
        CelestialBodyType classifyType = body.type;
        if (body.remodelToType >= 0 && body.remodelT > 0.001f && body.remodelToType != (int)body.type)
        {
            float mask = WrapU(u, freq * 2f, 0.6f, fy * 0.6f, seed + 211f, seed + 173f, 3);
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
        bool volcanicHotspot = false;
        if (TectonicsMap.Active(body))
        {
            var tec = TectonicsMap.Sample(body, u, v);
            ridge = Mathf.Clamp(ridge + tec.boundary * tec.convergence * TectonicRidgeGain, 0f, 2f);
            // Volcanoes cluster where plates DRIVE TOGETHER hardest (subduction). The strongest convergent
            // boundaries on a tectonically active world get a scattering of volcanoes among their peaks —
            // so some rocky worlds come out "somewhat volcanic" without being full Volcanic-type worlds.
            volcanicHotspot = tec.boundary * tec.convergence > 0.72f;
        }

        TerrainType t = Classify(classifyType, elevation, moisture, temperature, ridge, lat);

        // A raised, actively-converging tile can be a volcano rather than a plain peak. Deterministic
        // (heatNoise is a stable field), so it's a sparse scatter along the belt, and only on Rocky worlds
        // — the dedicated Volcanic classifier already handles furnace worlds, and freezing/ocean/gas worlds
        // shouldn't sprout stray cones.
        if (volcanicHotspot && classifyType == CelestialBodyType.RockyPlanet &&
            (t == TerrainType.Mountains || t == TerrainType.Highlands) && heatNoise > 0.55f)
            t = TerrainType.Volcano;

        // CLIMATE COHERENCE. The classifier's `temperature` is latitude-dominated (equator warm, poles cold)
        // and only lightly scaled by the world's heat, so on its own a globally FRIGID world could still show
        // a liquid equatorial sea, and a globally SCORCHING one could still grow jungle. Re-judge the water
        // and vegetation tiles against the tile's ACTUAL temperature in °C — the very figure PlanetTemperature
        // shows the player — so the two always agree: a −70°C world's seas read as ice, and a 100°C world
        // grows no rainforest. Computed from the SAME heat/atmosphere/type the readout uses, plus the standard
        // ±15°C equator→pole swing and a small local-weather wobble from the heat noise.
        float baseC = PlanetTemperature.BaseCelsius(p.heat, body.atmosphereThickness, classifyType);
        float tileC = baseC + Mathf.Lerp(15f, -15f, lat) + (heatNoise - 0.5f) * 12f - altCool * AltitudeLapseC;
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
    const float AltitudeLapseC = 55f;// °C lost per unit of elevation above the mid-line (mountain lapse rate)

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

    // FBm that TILES SEAMLESSLY along u — the map's east/west join.
    //
    // A planet map is a cylinder: u = 0 and u = 1 are the same meridian. Perlin noise sampled on a plane
    // has no idea about that, so the two edges carry unrelated values and wrapping the map onto a globe
    // leaves a visible seam — a continent sliced in half, a coastline that stops mid-ocean, a desert that
    // becomes tundra across one pixel.
    //
    // The fix is the standard tileable-noise construction: sample the field twice, one period apart, and
    // cross-fade between them using u itself as the blend. At u = 0 the result is exactly sample A; at
    // u = 1 the second sample has slid onto A's starting coordinate, so the result is exactly the same
    // value. Seamless by construction, not by tuning.
    //
    //     u = 0  ->  lerp(A(0),   B(-P),  0) = A(0)
    //     u = 1  ->  lerp(A(P),   B(0),   1) = B(0) = A(0)
    //
    // The cost is two Perlin lookups per octave instead of one, and slightly softer contrast near the
    // middle of the map where the two samples are blended most evenly. The alternative — writing a real
    // 3D noise and sampling the actual cylinder — has no contrast loss but means hand-rolling gradient
    // noise, since Unity only ships a 2D PerlinNoise.
    //
    // `mult` is the per-field frequency multiplier (elevation 1, ridge 2.2, fine detail 6 …). It has to be
    // folded into the PERIOD as well as the coordinate, or a field sampled at 2.2x the base frequency
    // would tile every 1/2.2 of the map and produce a repeating pattern rather than one seamless wrap.
    static float WrapU(float u, float baseSpanX, float mult, float y, float offX, float offY, int octaves)
    {
        float period = baseSpanX * mult;
        float x = u * period;
        float a = FBm(x + offX, y + offY, octaves);
        float b = FBm(x - period + offX, y + offY, octaves);

        // VARIANCE-PRESERVING blend, not a plain Lerp.
        //
        // A straight Lerp(a, b, u) is seamless but flattens the middle of every map. At u = 0.5 it
        // averages two independent noise fields, and averaging halves the variance while leaving the mean
        // at ~0.5 — so a vertical band down the centre meridian of every world gets pushed toward the
        // middle of the range. That is not a subtle contrast loss: deep ocean and high mountain are
        // threshold tests on these fields, so that band would systematically grow fewer of both. A
        // longitude-dependent bias in what terrain exists is far worse than the seam being fixed.
        //
        // Weighting each sample by w/sqrt(w1^2 + w2^2) instead of by w keeps the combined deviation
        // constant across the whole map. The endpoints are untouched — at u = 0 and u = 1 one weight is
        // 1 and the other 0, so the seam is still exact.
        float w2 = u, w1 = 1f - u;
        float k = Mathf.Sqrt(w1 * w1 + w2 * w2);
        if (k < 0.0001f) return a;
        return 0.5f + ((a - 0.5f) * w1 + (b - 0.5f) * w2) / k;
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
            // `moist` is the jitter field: independent of latitude, so it can actually break up a
            // latitude threshold. See PolarIceEdge.
            case CelestialBodyType.OceanPlanet:    return OceanWorld(elev, temp, lat, moist);
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
        // THE EQUATOR IS THE MELT ZONE. This used to be the other way round — high ground near the
        // equator took fresh Snow while the mid-latitudes stayed Tundra — which drew a white band across
        // the middle of every ice world and had the climate backwards: the equator is the warmest part of
        // any world, so on a frozen one it is where the ice gives way FIRST, not last.
        //
        // Flipped, the belt reads as what it is: a thawed band around the middle of a frozen world.
        // Grassland is only nominal here — ClimateCoherence re-judges it against the tile's actual °C
        // immediately after, so it survives only where the world genuinely is above freezing and reverts
        // to frozen ground otherwise. One rule decides what "warm enough for plants" means, not two.
        if (lat < EquatorMeltEdge(moist))
            return frozen ? TerrainType.Tundra : TerrainType.Grassland;

        // ...and the deep snow belongs at the POLES, on ground high enough to hold it.
        if (lat > PolarSnowEdge(moist) && elev > 0.5f)
            return TerrainType.Snow;
        // The bulk mid-elevation band is genuine ice SHEET, not a frozen sea — only the low-elevation
        // band above is actually water once melted (elev<0.3, handled above). Melting the rest into
        // Tundra keeps "how much of the map is water" tied to elevation the same way every other
        // classifier in this file does, instead of quietly turning a third of the map into ocean.
        return frozen ? TerrainType.Ice : TerrainType.Tundra;
    }

    // Where a latitude belt ends, as a latitude — perturbed by an INDEPENDENT noise field.
    //
    // The independence is the whole point. Any value that carries a latitude term of its own (`temp`
    // does: it is mostly (1-lat)) produces a jitter that is itself smooth in latitude, so the boundary
    // it draws is still a smooth horizontal line — a ruled edge moved slightly, not a ragged one. The
    // caller must pass a field with no latitude component: moisture or ridge, not temperature.
    //
    // Both are centred on the old fixed thresholds, so belts are the same size on average as before and
    // only their EDGES changed. Shared so every classifier drawing a belt ripples the same way rather
    // than one being ragged and its neighbour ruled.
    const float EdgeJitter = 0.18f;

    static float Belt(float centre, float noise)
        => centre - EdgeJitter * 0.5f + Mathf.Clamp01(noise) * EdgeJitter;

    /// Where open water gives way to permanent sea ice, as a latitude.
    static float PolarIceEdge(float noise) => Belt(0.85f, noise);

    /// How far from the equator an ice world's thawed band reaches.
    static float EquatorMeltEdge(float noise) => Belt(0.25f, noise);

    /// Where an ice world's high ground starts holding deep snow. Well inside the ice cap, so the snow
    /// reads as the coldest part of an already-frozen world rather than as its own separate band.
    static float PolarSnowEdge(float noise) => Belt(0.62f, noise);

    static TerrainType OceanWorld(float elev, float temp, float lat, float noise)
    {
        if (elev > 0.80f) return TerrainType.Mountains;
        if (elev > 0.70f) return TerrainType.Island;
        if (elev > 0.64f) return TerrainType.Beach;

        // The polar ice edge, perturbed rather than flat.
        //
        // This was a bare `lat > 0.85f`, and lat is a pure function of the row — so the test flipped at
        // exactly the same latitude for every column and the ice cap ended in a dead-straight horizontal
        // line across the top and bottom of the map. Real ice edges are ragged: they follow water
        // temperature, which varies along the coast.
        //
        // `temp` already carries the heat-noise field, so using it to move the threshold makes the
        // boundary wander with local climate at no extra sampling cost. A colder patch freezes further
        // toward the equator, a warmer one holds open water closer to the pole.
        if (lat > PolarIceEdge(noise)) return TerrainType.FrozenSea;
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
