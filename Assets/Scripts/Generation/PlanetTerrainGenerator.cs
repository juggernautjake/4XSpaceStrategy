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

        /// WHERE THE SEA SITS, 0 (bone dry) .. 1 (everything drowned). 0.5 is neutral.
        ///
        /// Separate from `elevation`, which is now purely how much RELIEF a world has. These used to be
        /// the same number: water level was implemented by scaling the elevation amplitude, so asking
        /// for more water squashed the whole terrain toward the mid-line and the mountains flattened
        /// out instead of being flooded. A world at maximum water had no relief left to drown.
        ///
        /// Now they are independent. Relief is the shape of the land; sea level slides up and down
        /// across that fixed shape, so raising it swallows the lowlands first, then the hills, and at
        /// maximum covers even the peaks — while the peaks themselves never change height.
        public float seaLevel;

        /// NEGATIVE means "never set" — a default-constructed struct or a save written before this
        /// existed. Zero is a legal, meaningful value (a bone-dry world) and must not be confused with
        /// absence: treating 0 as unset made the driest setting on the slider silently snap back to
        /// half-flooded, made draining a world past a point re-flood it, and persisted a dry world as
        /// a wet one.
        public float SeaLevelOrNeutral => seaLevel >= 0f ? Mathf.Clamp01(seaLevel) : 0.5f;

        public bool HasSeaLevel => seaLevel >= 0f;

        public static NoiseParams Default => new NoiseParams
        { scale = 1f, elevation = 1f, moisture = 1f, heat = 1f, ridge = 1f, seaLevel = 0.5f };
    }

    // "Water Level" bounds — the Terrain Sandbox's Elevation slider's old range, kept as the amplitude
    // window the Water Level slider maps onto (see WaterLevelFromElevation/ElevationFromWaterLevel).
    public const float ElevationMin = 0.3f, ElevationMax = 2f;

    // Water Level is now its OWN axis (NoiseParams.seaLevel), not a re-reading of elevation amplitude.
    // These two remain the shared translation between "how much water does this world have" (0..1, the
    // slider and the terraforming gates) and the parameter that expresses it, so every caller —
    // PlanetViewWindow's sandbox, BiosphereRules, the generators — still goes through one place.
    //
    // The mapping is now the identity, because seaLevel IS the water level. Kept as named functions
    // rather than deleted: they document the relationship, and the old names meant the change did not
    // have to ripple through every call site.
    public static float WaterLevelFromSeaLevel(float seaLevel) => Mathf.Clamp01(seaLevel);
    public static float SeaLevelFromWaterLevel(float waterLevel) => Mathf.Clamp01(waterLevel);

    // ---- Where the sea stands, in landHeight units --------------------------------------------------
    //
    // THE ONE RULE: water level moves the WATERLINE, never the land. Every classifier below gets the
    // ground's real height plus this offset, and adds the offset ONLY to its water tests. So filling a
    // world drowns its lowest ground first and leaves the shape of everything above the water untouched;
    // draining it uncovers exactly the terrain that was always there.
    //
    // 0.5 must map to 0. Every threshold in this file — Terran's 0.36 shoreline, Ice's 0.3, Barren's salt
    // flats — was tuned against a neutral sea, and a neutral sea has to keep meaning what it meant.
    //
    // THE MIDDLE IS THE OLD MAPPING, EXACTLY: `sea = waterLevel - 0.5`, slope 1. Every world already
    // generated or saved sits in here, and its coastline has to come back identical — a mapping that
    // merely "felt right" would silently reflood the whole galaxy on the next load.
    //
    // Only the last few percent at each END is stretched, and only because a slope-1 mapping cannot quite
    // reach: landHeight spans roughly -0.5..1.5 at maximum Elevation Range, so a linear −0.5 still leaves
    // water lying in the deepest basins of a world set to NO water, and a linear +0.5 still leaves summits
    // dry on one set to FULL. Inside the ramp zone the ends run out to clear both extremes, so 0 really is
    // a dry world and 1 really is a drowned one, at any relief setting.
    const float SeaShiftDry = -1.1f;      // below the deepest basin at max relief
    const float SeaShiftDrowned = 1.6f;   // above the highest summit at max relief
    const float SeaRamp = 0.08f;          // how much of each end is stretched

    public static float SeaShift(float waterLevel)
    {
        float w = Mathf.Clamp01(waterLevel);

        // The dry end: from the linear value at the top of the ramp, out to fully drained at 0.
        if (w < SeaRamp)
            return Mathf.Lerp(SeaShiftDry, SeaRamp - 0.5f, w / SeaRamp);

        // The drowned end: from the linear value at the bottom of the ramp, out to fully flooded at 1.
        if (w > 1f - SeaRamp)
            return Mathf.Lerp((1f - SeaRamp) - 0.5f, SeaShiftDrowned, (w - (1f - SeaRamp)) / SeaRamp);

        return w - 0.5f;
    }

    // Back-compat shims for callers that still speak in elevation. A world loaded from a save made
    // before seaLevel existed had its water baked into its elevation amplitude, so this recovers the
    // water level that amplitude used to mean.
    public static float WaterLevelFromElevation(float elevation) => Mathf.InverseLerp(ElevationMax, ElevationMin, elevation);
    public static float ElevationFromWaterLevel(float waterLevel) => Mathf.Lerp(ElevationMax, ElevationMin, waterLevel);

    public struct Sample
    {
        public TerrainType terrain;

        /// THE GROUND UNDERNEATH, when `terrain` is water, ice or snow.
        ///
        /// "Snow biomes and ice biomes are really just types of water biomes, only frozen, so biomes such
        /// as these should not be THE biome, but more like a modifier. Like when an ocean grows in grid
        /// size, the terrain that was enveloped still exists — if I wanted to remove the water, it will
        /// still be there."
        ///
        /// It genuinely still is there, because elevation is decided by geology alone and no terraforming
        /// touches it (see the elevation pipeline in SampleNormalized). This field just makes that
        /// legible: the tile readout says "Ocean over Steppe" rather than only "Ocean", so the player can
        /// see what draining a sea would uncover before spending a project on finding out.
        ///
        /// Equal to `terrain` on a tile that is neither flooded nor frozen. Never itself a water type.
        public TerrainType ground;

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

    // ---- THE ELEVATION BUDGET ---------------------------------------------------------------------
    //
    // Four terms, deliberately in descending order of authority, so that reading them tells you what
    // decides a world's shape: its plates first, then its faults and vents, then noise last and least.
    // Every one of them is measured in the same 0..1 landHeight units the classifiers threshold against,
    // so their sizes can be compared directly against each other and against those thresholds.

    /// How far a continental plate rides above — or an oceanic plate sits below — the mean surface.
    ///
    /// The single largest term, and it should be: on a world with plates, WHICH PLATE you are standing on
    /// is the first-order fact about how high you are. At 0.26 a continental plate lands around 0.76 and
    /// an ocean basin around 0.24, so a neutral sea (which stands at 0.36 in these units) drowns the
    /// basins and leaves the continents dry — a world with real coastlines, drawn by its own geology,
    /// before a single line of noise is involved.
    const float ContinentalRelief = 0.26f;

    // How much a convergent plate boundary lifts the ridge (mountain-building) field at the fault. A
    // strong head-on collision (belt≈1, convergence≈1) adds this much, enough to push raised ground
    // over the Mountains threshold; a divergent boundary subtracts it (a thinning rift).
    const float TectonicRidgeGain = 0.6f;

    /// How high a volcanic hotspot piles its dome. Applied to the SQUARE of the hotspot field, so the
    /// 97%+ vent gets nearly all of it and the surrounding 70% skirt gets about half — a cone, rather
    /// than a plateau with a mountain somewhere on it.
    const float HotspotUpliftGain = 0.30f;

    // ---- THE VARIATION PASS ------------------------------------------------------------------------
    //
    // "And then the terrain generation can apply some more variation to elevation across the grid map."
    // Its job is BASINS, not peaks: somewhere for water to collect that the plates did not already
    // provide. It cannot raise a mountain at any setting, because `ridge` is computed from the GEOLOGY
    // lift and never sees this term at all (see RidgeFromRelief).
    //
    // SIZED AGAINST THE MEASURED NOISE, not against its nominal range. The field has a standard
    // deviation of 0.124, not 0.5, so a "gain of 0.30" moves the ground by ±0.075 in practice — a
    // twentieth of what the waterline sweeps across. Measured (Node port, four worlds of 200x100, the
    // water level rolled across its full 0.10..1.00 generation range):
    //
    //                                  share of water rolls giving a 15-85% ocean world
    //     dead world, gain 0.30                   17%     <- almost every world bone dry or drowned
    //     dead world, gain 0.45                   26%
    //     dead world, gain 0.55                   32%
    //
    // A plate world does not have this problem — its continental and oceanic crust are half a unit apart
    // by construction, so it sits at 63% either way — which is exactly why the two gains differ. On a
    // world with no plates the noise is the ONLY thing drawing a coastline, so it has to do more of the
    // work. At 0.55 a dead world runs 18% ocean at water level 0.5, 40% at 0.6 and 71% at 0.7: a
    // gradient you can terraform along rather than a switch.
    const float VariationGain = 0.24f;
    const float DeadWorldVariationGain = 0.55f;

    // How much a plate boundary moves the GROUND ITSELF, as a fraction of the world's relief.
    //
    // Ridge and elevation are two different statements and the terrain needs both. `ridge` says the ground
    // is BROKEN — it is what turns a tile into Mountains or Canyon once it is high enough — but it never
    // raised the land, so a fault crossing a plain produced rugged lowland and a fault crossing the sea
    // produced rugged seabed. The ranges followed the fault lines only in the sense that the roughness
    // did; the CONTOURS were still pure noise, which is why the mountains on a tectonic world looked
    // scattered rather than folded.
    //
    // Now a convergent margin lifts the land and a divergent one drops it, which is the actual mechanism:
    // two plates driving together have nowhere to put the crust but up, and two pulling apart leave a
    // trough between them that fills with sea. At 0.22 of the 0..1 relief band a head-on collision lifts
    // low ground into the highland range and highland into mountains, and a full rift drops a coastal
    // plain under the waterline — big enough to read as the dominant feature of a tectonic world's map,
    // small enough that the underlying continents are still the continents the elevation noise drew.
    const float TectonicUpliftGain = 0.22f;

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
            ridge = Mathf.Max(0.1f, ridgeStrength),
            // Carried over rather than left at the struct default: this rebuilds terrainParams wholesale
            // from five arguments, and omitting sea level silently reset every world it touched to a
            // half-flooded default.
            seaLevel = body.terrainParams.SeaLevelOrNeutral
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
                // The ground under any water/ice and the tile's elevation travel with it: the readout
                // wants both every frame the cursor moves, and re-deriving them means re-running the
                // whole noise field for one cell.
                surface.tiles[x, y] = new TerrainTile(s.terrain, s.ground, s.shade, s.elevation);
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

        // ---- 3) Keep `ground` honest ----
        //
        // Both passes above rewrite `type` — a lake becomes an ocean, a jungle becomes a beach — without
        // touching `ground`, which is right for the water cases (the seabed did not change when we
        // decided the pool was part of the sea) and wrong for the land ones: a tile that is now a beach
        // is not a beach lying ON something, it IS a beach, and leaving `ground` pointing at the jungle
        // it used to be would have the readout announce "Beach over Jungle" forever.
        //
        // The rule is the invariant `ground` is defined by: a tile with no cover on it is its own ground.
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (!IsCover(tiles[x, y].type)) tiles[x, y].ground = tiles[x, y].type;
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
        // ============================================================================================
        // ELEVATION COMES FROM GEOLOGY. THE WORLD STARTS FLAT.
        //
        // This used to be one noise field: continents, mountains, basins and all, drawn straight out of
        // Perlin and then decorated with a tectonic uplift on top. That produced terrain that looked
        // plausible and meant nothing — mountains in the middle of plains, coastlines with no reason to
        // be where they were, and a world whose shape had no relationship to anything else about it.
        //
        // It is built in the order the request lays out, and each step only ever ADDS to a flat sheet:
        //
        //   1. FLAT. A world with no plates and no volcanism really is a featureless ball, and the only
        //      relief it gets is step 4's variation.
        //   2. CONTINENTS, FROM THE PLATES. A tectonic world's plates are its landmasses: each plate
        //      carries continental crust (which rides high) or oceanic crust (which sits low), so the
        //      shape of a continent IS the shape of a plate. This is the Voronoi/cell continent
        //      generation the request asks for, and it is the same partition the fault overlay draws.
        //   3. FAULTS FOLD IT. Two plates driving together have nowhere to put the crust but up; two
        //      pulling apart leave a trough between them. Volcanic hotspots pile a dome over their vent,
        //      the hottest cell being the highest — which is the whole of "if there are no continents,
        //      look to the geothermal hotspots".
        //   4. ...AND ONLY THEN, VARIATION. A gentle noise field so the ground is not glass. It is
        //      deliberately too small to make a mountain: mountains come from step 3 or they do not
        //      exist, which is the request's "I don't want mountains appearing from points that should
        //      be lower".
        //
        // WHY TERRAFORMING CANNOT MOVE ANY OF IT. Not one term here reads temperature, water level or
        // atmosphere. Those decide what the ground IS — sea, ice, magma, jungle — but the ground is
        // already the height it is by the time they are consulted. Heat a frozen world until its oceans
        // boil away and its mountains are still exactly where they were, standing over a dry basin.
        //
        // MEASURED (Node port of this pipeline; a neutral sea stands at 0.36 in these units, and the
        // Mountains threshold is ridge > 0.82):
        //
        //                                  land    lift   ridge
        //   dead world, noise floor       -0.050   0.000   0.62   submerged — an ocean basin
        //   dead world, noise ceiling      1.050   0.000   0.62   land, and NOT a mountain
        //   ...the same at Elevation 2     1.600   0.000   0.62   taller, and still not a mountain
        //   oceanic plate                  0.240  -0.260   0.62   submerged
        //   continental plate              0.760   0.260   0.67   dry land, and not a mountain
        //   continent + head-on fault      1.076   0.480   1.95   MOUNTAINS
        //   ...the same at Elevation 2     1.652   0.480   1.95   taller mountains, same classification
        //   oceanic plate + full rift     -0.076  -0.480   0.62   submerged — a trench
        //   volcanic vent, no plates       0.800   0.300   1.31   MOUNTAINS
        //
        // So water collects in ocean basins and rift valleys, continents stand clear of it, and the only
        // two things that produce a mountain are a collision and a vent. The highest ridge reachable
        // anywhere with NO geology at all, at any texture value and any Elevation setting, is 0.62 —
        // comfortably under every Mountains threshold in the file, by construction rather than by tuning.
        // ============================================================================================

        // ---- 2) THE PLATES, if this world has any ----
        // Sampled ONCE. This is the most expensive call in world generation and every later use of it
        // below reads this same hit.
        bool hasPlates = TectonicsMap.Active(body);
        TectonicsMap.Hit tec = default;
        if (hasPlates) tec = TectonicsMap.Sample(body, u, v);

        // HOW FAR GEOLOGY LIFTED THIS GROUND, kept apart from the finished height all the way through.
        //
        // This is the number `ridge` is computed from, and separating it is what makes "mountains come
        // from collisions and vents, never from noise" a structural guarantee rather than a matter of
        // keeping the noise small enough. See RidgeFromRelief.
        float geologyLift = 0f;

        if (hasPlates)
        {
            // CONTINENT OR OCEAN FLOOR. A per-plate property, so the boundary between a continent and an
            // ocean basin is a plate margin — which is exactly where a real continental shelf is.
            geologyLift += TectonicsMap.CrustAt(body, tec) * ContinentalRelief;

            // A convergent margin (convergence > 0) lifts the crust; a divergent one drops it. `belt`,
            // NOT `boundary`: the red line the Survey overlay draws is a one-to-three tile annotation,
            // while an orogenic belt is a wide ragged skirt either side of it, and reading the drawn
            // line here would confine every range to the width of its own map symbol.
            geologyLift += tec.belt * tec.convergence * TectonicUpliftGain;
        }

        // ---- 3) VOLCANIC HOTSPOTS pile a dome over their vent ----
        // Read from the HOTSPOT field specifically rather than from the finished Geothermal Index: on a
        // plate world the index is mostly the fault field, and the faults have already had their say two
        // lines up. Adding the index whole would count a convergent margin's uplift twice and, worse,
        // would raise the ground along a RIFT — where the same index is high and the land should be
        // dropping into a trough.
        //
        // Squared, so the highest ground is the 97%+ vent itself and the skirt falls away fast. That is
        // the request's "the highest geothermal index grids being the highest in elevation", and it is
        // what makes a hotspot world read as a scatter of individual cones rather than as a plateau.
        float hotspot = GeothermalMap.HotspotAt(body, u, v);
        if (hotspot > 0f) geologyLift += hotspot * hotspot * HotspotUpliftGain;

        // The ground, so far: everything geology did to it, and nothing else.
        float landHeight = 0.5f + geologyLift;

        // ---- 4) ...AND ONLY NOW, SOME VARIATION ----
        // A world with neither plates nor plumes gets a LARGER share of this, and that is not a fudge:
        // without it such a world is a perfect sphere, every tile at exactly the same height, and adding
        // any water at all floods the entire surface in one step rather than filling its low ground.
        // Basins have to come from somewhere, and on a dead world the only thing left to draw them with
        // is noise. It is still far too small to raise a mountain (see RidgeFromRelief).
        float rawElev = WrapU(u, freq * 2f, 1f, fy, seed, seed * 1.3f, octaves);
        float variationGain = hasPlates ? VariationGain : DeadWorldVariationGain;
        landHeight += (rawElev - 0.5f) * 2f * variationGain;

        // ---- THE ELEVATION SLIDER: accentuate what is already there ----
        //
        // Scaling the DEVIATION from the mid-line is the whole trick, and it is what the request is
        // asking for when it says the slider should "just accentuate the already existing hills and low
        // points into higher and lower points". High ground goes higher, low ground goes lower, and
        // ground at the mid-line does not move at all — so turning it up cannot make a mountain appear
        // in the middle of a plain or in the middle of an ocean, because there was nothing there to
        // accentuate. Turning it down flattens the world toward the sheet it started as.
        //
        // NOT clamped. At high settings this spans roughly -0.5..1.5, and that headroom is the point:
        // clamping here would pin every peak to exactly 1 and every basin to exactly 0, turning both
        // ends of the world into flat plateaus at precisely the setting chosen to get dramatic terrain.
        //
        landHeight = 0.5f + (landHeight - 0.5f) * p.elevation;

        // WHERE THE SEA STANDS, in the same units as landHeight. The classifiers get the ground's REAL
        // height and this line separately, and only their WATER tests add it — so raising the Water Level
        // floods the lowest ground first and leaves every land threshold (hills, highlands, mountains,
        // badlands, salt flats) exactly where it was.
        //
        // This used to subtract the sea from the elevation field before classifying, which moved the
        // land thresholds too: filling a world's oceans quietly demoted its mountains to highlands and
        // its highlands to plains, and draining it promoted plains into mountains. The terrain appeared
        // to change shape as the water moved, when the only thing that should change is how much of it
        // is under water.
        float seaShift = SeaShift(p.SeaLevelOrNeutral);
        float moisture  = WrapU(u, freq * 2f,        1.3f, fy * 1.3f, seed + 31f, seed + 17f,  octaves) * p.moisture;

        // ============================================================================================
        // RIDGE IS DERIVED FROM THE GROUND NOW, not rolled beside it
        //
        // `ridge` is the field every classifier tests to decide Mountains, Canyon, Badlands, Cracked
        // Ground — "is this ground BROKEN". It used to be its own independent noise field, which is
        // precisely why mountains turned up in places that made no sense: the field peaked wherever it
        // felt like, including over flat plains and over the sea floor, and the classifier dutifully put
        // a mountain range there.
        //
        // Broken ground is a CONSEQUENCE of the ground having been pushed up. So it is computed from the
        // height the geology just produced, plus the two things that do the pushing — a convergent
        // margin and a volcanic vent — plus a small noise term for texture, so a range has peaks and
        // saddles along its length instead of reading as an extruded wall.
        //
        // `p.ridge` survives as a multiplier because it is in the save format and in every world's
        // natural params, but nothing rolls it away from 1 any more and the Ruggedness slider that used
        // to drive it is gone: an axis that moves mountains around independently of the ground they
        // stand on is the exact thing this rework exists to remove.
        // `geologyLift`, NOT the finished height — see RidgeFromRelief. Neither the variation pass nor
        // the Elevation slider is in it, so neither can make a mountain.
        //
        // `tec` is already the zeroed default on a world with no plates, so it is passed as-is: a
        // conditional here would only restate that, and an `in` parameter fed by a ternary allocates a
        // temporary to do it.
        float ridgeTexture = WrapU(u, freq * 2f, 2.2f, fy * 2.2f, seed + 91f, seed + 53f, octaves);
        float ridge = RidgeFromRelief(geologyLift, tec, hotspot, ridgeTexture, p.ridge);

        // THE FINISHED GEOTHERMAL INDEX at this point — the same 0..1 the Survey overlay paints and the
        // earthquakes shake. Assembled from the two halves already in hand (the plate sample and the
        // hotspot field) rather than re-derived: this runs once per cell of every world in the galaxy,
        // and re-deriving the hotspot field would cost six more Perlin lookups on each of them.
        // Used below to decide where a volcano actually stands.
        float geothermal = GeothermalMap.Combine(body, tec, hotspot);

        float lat = Mathf.Abs(v - 0.5f) * 2f;                 // 0 equator, 1 pole
        float heatNoise = WrapU(u, freq * 2f,        0.9f, fy * 0.9f, seed + 11f, seed + 7f,   2);

        // ============================================================================================
        // ELEVATION MOVES THE TEMPERATURE, IN BOTH DIRECTIONS
        //
        // This used to be `max(0, landHeight - 0.6)` — cooling for high ground, and nothing at all for
        // low ground. Half of the effect was missing, and it is the half that makes a volcanic world
        // interesting: "higher elevation will have cooler temperatures, and lower elevations will have
        // higher temperatures", so a molten world's magma collects in its valleys and runs between its
        // highlands as rivers rather than covering its entire equator in a sheet.
        //
        // Measured from the MID-LINE, so it is symmetric by construction and a world at rest (every tile
        // at 0.5) gets no shift anywhere. Read off landHeight, not off the sea-relative figure: altitude
        // cooling is about how high the GROUND is, not how deep the water over it is, and using the
        // sea-relative value would make every world colder as it flooded.
        float altDelta = landHeight - 0.5f;                   // + high (cooler), - low (warmer)

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
        // Symmetric in altitude, matching the °C reading below — high ground colder, low ground warmer.
        band = Mathf.Clamp01(band - altDelta * 0.55f);

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

        // `body.biosphereActive` is threaded in because CORAL IS ALIVE. A reef on a sterile world is the
        // same category error as a forest on one — it was being drawn purely from "shallow and warm",
        // so a dead ocean world grew coral shallows with nothing in the galaxy to have built them.
        TerrainType t = Classify(classifyType, landHeight, seaShift, moisture, temperature, ridge, lat,
                                 body.biosphereActive);

        // ============================================================================================
        // A VOLCANO IS WHERE THE GEOTHERMAL INDEX SAYS IT IS
        //
        // The request draws the line precisely: "if there are Geothermal hotspots that would generate a
        // volcano (in the 97-100 range on Geothermal Index)". So the vent is not a separate roll on top
        // of the terrain — it is the same field the overlay paints, read at its own threshold. Whatever
        // the map shows as 97%+ has a cone standing on it, on a fault margin and on a plate-less hotspot
        // world alike.
        //
        // Not on a gas giant (no surface) and not under water (a submarine vent is not a mountain the
        // player can see or build on). Everything else is fair game, including an ice world — Enceladus
        // is a real place, and a cryovolcano on a frozen world is exactly the kind of thing the
        // geothermal survey exists to find.
        if (geothermal >= GeothermalMap.VolcanoIndex &&
            classifyType != CelestialBodyType.GasGiant && !IsWater(t))
            t = TerrainType.Volcano;

        // CLIMATE COHERENCE. The classifier's `temperature` is latitude-dominated (equator warm, poles cold)
        // and only lightly scaled by the world's heat, so on its own a globally FRIGID world could still show
        // a liquid equatorial sea, and a globally SCORCHING one could still grow jungle. Re-judge the water
        // and vegetation tiles against the tile's ACTUAL temperature in °C — the very figure PlanetTemperature
        // shows the player — so the two always agree: a −70°C world's seas read as ice, and a 100°C world
        // grows no rainforest. Computed from the SAME heat/atmosphere/type/internal-heat the readout uses,
        // plus the standard ±15°C equator→pole swing and a small local-weather wobble from the heat noise.
        float baseC = PlanetTemperature.BaseCelsius(p.heat, body.atmosphereThickness, classifyType,
                                                    GeothermalMap.WorldIntensity(body));
        float tileC = baseC + Mathf.Lerp(15f, -15f, lat) + (heatNoise - 0.5f) * 12f - altDelta * AltitudeLapseC;

        // THE LIQUID-WATER WINDOW IS THIS WORLD'S OWN, and it depends on its air: at one atmosphere water
        // runs 1°C to 100°C, at four it runs 0°C to 144°C (BiosphereRules). Passing both ends in rather
        // than baking constants into the coherence pass is what makes "higher atmospheres allow for
        // liquid water at higher temperatures" true of the actual map instead of only of a readout.
        BiosphereRules.LiquidRange(body, out float freezeC, out float boilC);
        t = ClimateCoherence(t, tileC, freezeC, boilC);

        // ============================================================================================
        // WHAT IS UNDERNEATH — water and ice as MODIFIERS rather than as the biome
        //
        // Asked by running the very same classifier a second time against a DRAINED, THAWED reading of
        // this tile: the sea pushed below the deepest basin and the temperature lifted just past
        // freezing. Everything else — the elevation, the ridge, the moisture, the latitude — is the tile's
        // own, so what comes back is literally "what this ground is, with the water and the ice taken
        // away". That is the request's point exactly: the terrain an ocean swallowed did not stop
        // existing, and draining the ocean would uncover it unchanged.
        //
        // A SECOND CLASSIFIER CALL, NOT A SECOND SAMPLE. Every expensive input — the noise fields, the
        // plate hit — is already in hand; this is a few dozen float comparisons. Re-deriving the fields
        // would have doubled the cost of the hottest function in world generation.
        //
        // Skipped entirely when the tile is neither flooded nor frozen, which is most of them.
        TerrainType groundUnder = t;
        if (IsCover(t))
        {
            groundUnder = Classify(classifyType, landHeight, SeaShiftDry, moisture,
                                   Mathf.Max(temperature, ThawedTemperature), ridge, lat,
                                   body.biosphereActive);
            // A drained, thawed world should never classify back to water — but the Ice classifier can
            // still return a Lake from its moisture test, and a bare rock is a better answer than a
            // recursion. Guarded rather than trusted.
            if (IsCover(groundUnder)) groundUnder = TerrainType.Barren;
        }

        return new Sample
        {
            ground = groundUnder,
            // landHeight, not the sea-relative figure: callers asking a tile's elevation want the
            // ground's real height, which does not change when the tide comes in.
            // Clamped only HERE, on the way out: Sample.elevation is documented 0..1 and SurfaceIndex
            // and the overlays rely on that, while the generator above needs the unclamped range.
            terrain = t, shade = fine, elevation = Mathf.Clamp01(landHeight), water = IsWater(t),
            temperature = temperature, moisture = Mathf.Clamp01(moisture),
            ridge = Mathf.Clamp01(ridge), latitude = lat
        };
    }

    // ============================================================================================
    // HOW BROKEN IS THIS GROUND — derived from how it got here
    //
    // `ridge` is what every classifier thresholds to decide Mountains / Canyon / Badlands / Cracked
    // Ground. It was an independent noise field, and that is the single line that produced the terrain
    // the rework exists to replace: the field peaked wherever it liked, including over flat plains and
    // over the sea floor, and the classifier put a mountain range there because that is what it was
    // told. Ranges "followed the fault lines" only in the sense that the roughness did — the contours
    // themselves were pure noise.
    //
    // Broken ground is a consequence of the ground having been PUSHED, so this reads the three things
    // that push it — and it reads the GEOLOGY LIFT, not the finished height:
    //
    //   LIFT        — how far plates and plumes raised this ground above the mean. Steepness is what
    //                 breaks rock, and this is the only part of a tile's height that came from a force.
    //                 Ground the geology did not raise contributes nothing, so a basin is never rugged.
    //   COLLISION   — a convergent margin does both things at once: it lifts the crust AND shatters it,
    //                 which is why a range comes out as high ground that is also rough. A RIFT is not
    //                 included: it drops the land and thins it, and what it makes is a valley.
    //   A VENT      — a volcano is broken ground by definition.
    //
    // WHY THE LIFT AND NOT THE HEIGHT. Reading total height couples ruggedness to two things that have
    // nothing to do with force: the variation noise and the Elevation slider. Measured (Node port), that
    // coupling put ridge at 1.17 on a plate-less, volcano-less world with the slider at 1.5 and 1.56 at
    // 2 — over the 0.82 Mountains threshold — so a geologically dead world grew a mountain range out of
    // pure noise, which is precisely the artefact this rework exists to remove.
    //
    // Reading the lift makes it structural instead of a matter of keeping the noise small: `geologyLift`
    // is zero on a dead world at every noise value and every slider setting, so no mountain is reachable
    // there by any route. That is what freed the variation pass to be big enough to carve real ocean
    // basins (see VariationGain) without the two trading off against each other.
    //
    // TEXTURE DOES TWO JOBS. It modulates the geological terms by ±18%, so a range has peaks and saddles
    // along its length instead of reading as an extruded wall — and it supplies a small floor of its own
    // so that a world with no geology still has canyons and badlands rather than being glass. That floor
    // is capped BELOW the Mountains threshold by construction, and combined with `Max` rather than added,
    // so it can never push geological ground over a line it would not have crossed anyway.
    static float RidgeFromRelief(float geologyLift, in TectonicsMap.Hit tec, float hotspot,
                                 float texture, float ridgeScale)
    {
        float t = Mathf.Clamp01(texture);

        float lift = Mathf.Max(0f, geologyLift) * 2.2f;
        float collision = Mathf.Max(0f, tec.belt * tec.convergence) * TectonicRidgeGain;
        float vent = hotspot * 0.45f;

        float shaped = (lift + collision + vent) * (0.82f + 0.36f * t);

        // The noise floor: broken, weathered ground anywhere, but never a range. 0.62 sits under every
        // classifier's Mountains test (0.82 on Terran/Barren, 0.85 on Airless) with room to spare, while
        // still clearing Barren's Badlands cut at 0.5 — so a dead world reads as varied rock rather than
        // as one flat biome, which is what it looked like when ridge was geology-only.
        //
        // ...AND THE CEILING IS APPLIED AFTER `ridgeScale`, WHICH IS THE WHOLE POINT.
        //
        // `Max(shaped, rough) * ridgeScale` scaled the noise floor along with the geology, so the
        // guarantee this function advertises — "no mountain is reachable on a dead world by any route" —
        // held only while ridgeScale stayed at or below 1.32. Nothing rolls it away from 1 any more, so
        // that reads safe, and for a newly generated world it is.
        //
        // It is not safe on a LOADED one. `ridge` is in the save format, TerrainVariance used to roll it
        // as a per-world "ruggedness" reaching about 1.5, and GameStateSerializer faithfully restores
        // whatever the file says. At 1.5 the floor comes back as 0.93, over every Mountains threshold in
        // the file — so every geologically dead world in every pre-rework save grew mountain ranges out
        // of pure noise the moment it was loaded. The exact artefact this rework exists to remove,
        // reintroduced silently, on precisely the worlds nobody would think to re-examine.
        //
        // Scaling first and capping second keeps both halves honest: a project that genuinely flattens a
        // world (ridgeScale below 1) still calms the background rock, and no value above 1 can push the
        // noise past a ceiling whose name says it is one.
        float rough = Mathf.Min(t * NoiseRoughnessMax * ridgeScale, NoiseRoughnessMax);

        return Mathf.Clamp(Mathf.Max(shaped * ridgeScale, rough), 0f, 2f);
    }

    /// The most `ridge` the texture noise may reach on its own. Deliberately below every Mountains
    /// threshold in this file: a world with no plates and no volcanism has canyons and badlands, and does
    /// not have mountain ranges, however the noise falls.
    const float NoiseRoughnessMax = 0.62f;

    /// The temperature the "what is underneath" pass thaws a tile to — just past the classifiers' shared
    /// 0.22 freezing line, and no further. Lifting it higher would not reveal more ground, it would
    /// reveal a WARMER world's ground: a tundra would come back as jungle, which is a different claim
    /// than the one being made.
    const float ThawedTemperature = 0.32f;

    /// Is this terrain a COVER over ground rather than ground itself? Water, sea ice, snow and glacier —
    /// the things the request calls modifiers. Everything under one of these has a real biome beneath it
    /// (Sample.ground), and removing the cover would uncover exactly that.
    ///
    /// Snow and Glacier are here and Ice is not, deliberately: `Ice` is used by the Ice-world classifier
    /// for the bulk ice SHEET, which is kilometres thick and is, for every purpose the game has, the
    /// ground. Snow lies on top of something.
    public static bool IsCover(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Snow:
            case TerrainType.Glacier:
                return true;
            default:
                return IsWater(t);
        }
    }

    // ============================================================================================
    // ELEVATION AS A READABLE BAND
    //
    // "I don't want Highlands or Hills etc to be considered biomes, as these are more so elevation
    // levels. We can go a step further and really show elevation levels by putting an elevation number in
    // the mouse window information when hovering over a grid. So you could see information such as
    // 'Mountain, [elevation], [Temperature]'."
    //
    // So the words survive — a player still wants to be told they are looking at highlands — but as a
    // second, independent fact about a tile rather than as the tile's identity. A grassland at 0.79 is
    // grassland AND highland; under the old scheme it stopped being grassland at 0.74.
    //
    // The bands are read against the SEA, not against the raw field, so they mean what a person means by
    // them: "lowland" is ground barely above the water, not ground below some absolute number that has
    // nothing to do with where the water on this particular world happens to stand.
    // ============================================================================================
    public static string ElevationBand(float landHeight, float waterLevel)
    {
        float above = landHeight - (0.36f + SeaShift(waterLevel));   // Terran's shoreline is the zero
        if (above < 0f) return "submerged";
        if (above < 0.06f) return "coastal";
        if (above < 0.16f) return "lowland";
        if (above < 0.28f) return "plains";
        if (above < 0.40f) return "uplands";
        if (above < 0.52f) return "highland";
        return "alpine";
    }

    /// The band for a body's tile, from its own water level. The form nearly every caller wants.
    public static string ElevationBand(CelestialBody b, float landHeight)
        => ElevationBand(landHeight, b == null ? 0.5f : b.terrainParams.SeaLevelOrNeutral);

    /// Elevation as a NUMBER for the readout: metres above (or below) this world's waterline.
    ///
    /// The internal field is a 0..1-ish abstraction and always was; multiplying it by a height gives the
    /// player something they can compare against a number they already have a feel for. 12,000 m of span
    /// puts a typical continental plateau around 2,000 m and a fully-accentuated peak near 10,000 —
    /// Earth's own range, near enough, which is the only calibration this needs.
    public const float MetresPerElevationUnit = 12000f;

    public static float ElevationMetres(float landHeight, float waterLevel)
        => (landHeight - (0.36f + SeaShift(waterLevel))) * MetresPerElevationUnit;

    public static float ElevationMetres(CelestialBody b, float landHeight)
        => ElevationMetres(landHeight, b == null ? 0.5f : b.terrainParams.SeaLevelOrNeutral);

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
    const float DeepFreezeC = -25f;  // below this even hardy groundcover is buried — snow, not tundra
    const float LushMaxC = 55f;      // above this, rainforest & wetland thin to hardier tropical cover
    const float ScorchC = 75f;       // above this, no vegetation survives — bare, sun-baked ground
    const float BakedC = 100f;       // above this, that bare ground reads as wasteland, not just desert

    /// °C gained or lost per unit of elevation away from the mid-line — the atmospheric lapse rate, and
    /// its mirror image below the mid-line. Raised from 55 when it became symmetric: it now has to carry
    /// both ends, and it is what makes a molten world's magma pool in its valleys and leave its highlands
    /// as bare rock rather than covering the entire equator in a sheet of lava.
    public const float AltitudeLapseC = 70f;

    // ============================================================================================
    // CLIMATE COHERENCE — the pass that makes the map agree with the thermometer
    //
    // Re-judges one classified tile against its real temperature. Only water and vegetation are touched;
    // rock, mountains, sand and already-frozen tiles are left exactly as the per-type classifier decided.
    // Deterministic and resolution-independent (a pure function of the tile type and its numbers), so the
    // grid and the detailed globe stay identical.
    //
    // THE WINDOW IS PASSED IN, and that is the change. It used to be two constants — freezing at 0°C,
    // and no boiling test at all — and both were wrong for the same reason: how hot water can get before
    // it stops being water is a property of the PRESSURE above it, not of water. So the caller solves
    // this world's own window (BiosphereRules.FreezingC / BoilingC: 1-100°C at one atmosphere, 0-144°C
    // at four) and hands both ends in.
    //
    // Three regimes, and the ends of the range are new:
    //   BELOW FREEZING  — seas become ice, vegetation dies back. As before.
    //   ABOVE BOILING   — seas are GONE, not ice and not sea. What they leave is the bed they were
    //                     standing in: salt flats. This is the request's "water should not generate on
    //                     grids over the boiling point", and it is enforced per TILE rather than per
    //                     world, so a world can hold an ocean at its poles and none at its equator.
    //   ABOVE MAGMA     — the ground itself is liquid. See WorldClassifier.MagmaMinC.
    static TerrainType ClimateCoherence(TerrainType t, float tileC, float freezeC, float boilC)
    {
        // --- Liquid rock. The hottest thing on the scale, and it outranks everything below. ---
        if (tileC >= WorldClassifier.MagmaMinC && !IsWater(t))
            return TerrainType.MagmaField;

        // ...AND THE SAME GATE IN REVERSE, which is the half that was missing.
        //
        // The per-type Volcanic classifier lays down magma from its own NORMALIZED heat field ("hot >
        // 0.78"), which is a latitude-and-noise number that knows nothing about °C. So a volcanic world
        // whose internal heat left it at, say, 540 °C — well short of molten — still grew a band of
        // liquid rock across its equator, while the readout under the cursor said the ground was three
        // hundred degrees too cold to melt. The promotion above was gated on MagmaMinC and the
        // classifier's own path was not, so the two disagreed on exactly the worlds the band is for.
        //
        // Cooled magma is LavaRock — the same word the Volcanic classifier already uses for solidified
        // flows one threshold down, so the demotion lands somewhere that world's vocabulary already has.
        if (t == TerrainType.MagmaField && tileC < WorldClassifier.MagmaMinC)
            return TerrainType.LavaRock;

        // --- Above boiling: the sea is not here any more, and what it left behind is its own bed ---
        if (tileC > boilC)
        {
            switch (t)
            {
                case TerrainType.Ocean:
                case TerrainType.Lake:
                case TerrainType.River:
                case TerrainType.Reef:
                case TerrainType.FrozenSea:
                    // Evaporites: what is left when a body of water boils off is everything that was
                    // dissolved in it. A dry seabed reads as salt, which is also what a dry seabed
                    // reads as everywhere else in this file (see Barren).
                    return TerrainType.SaltFlat;
            }
        }

        // --- Below freezing: liquid water turns to ice; warm vegetation dies back to frozen ground ---
        if (tileC < freezeC)
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
    /// A seamless 0..1 noise field over a world's whole surface, for anything OUTSIDE this file that
    /// needs one — the survey indexes' hotspot fields, principally.
    ///
    /// `cells` is roughly how many blobs fit around the equator, which is the number a caller actually
    /// wants to reason about: 3 gives a handful of continent-sized patches, 20 gives a fine scatter. The
    /// 2:1 map aspect is applied here so a blob comes out round rather than stretched, and the wrap goes
    /// through the same WrapU every terrain field uses, so a patch crossing the date line is one patch.
    ///
    /// EXPOSED RATHER THAN COPIED. A second seamless-noise implementation living next door would drift
    /// from this one, and the two would then disagree about where the seam is — which is exactly the class
    /// of bug WrapU exists to make impossible.
    public static float WorldNoise(CelestialBody body, float u, float v, float cells, float salt, int octaves)
    {
        float seed = body != null ? body.terrainSeed : 0f;
        float span = Mathf.Max(0.5f, cells);
        return Mathf.Clamp01(WrapU(u, span, 1f, v * span * 0.5f, seed + salt, seed * 1.7f + salt, octaves));
    }

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
    /// `elev` is the ground's REAL height (landHeight) and never moves with the water. `sea` is where the
    /// waterline stands in those same units — see SeaShift. Classifiers add `sea` to their WATER tests
    /// and to the shoreline band that hugs them, and to nothing else: a mountain is a mountain at any
    /// tide, and is simply submerged once the water is over it.
    static TerrainType Classify(CelestialBodyType planet, float elev, float sea, float moist, float temp,
                                float ridge, float lat, bool living)
    {
        switch (planet)
        {
            case CelestialBodyType.GasGiant:       return GasGiant(lat, elev, moist);
            case CelestialBodyType.VolcanicPlanet: return Volcanic(elev, temp, ridge, lat);
            case CelestialBodyType.IcePlanet:      return Ice(elev, sea, moist, temp, ridge, lat);
            // `moist` is the jitter field: independent of latitude, so it can actually break up a
            // latitude threshold. See PolarIceEdge.
            case CelestialBodyType.OceanPlanet:    return OceanWorld(elev, sea, temp, lat, moist, living);
            case CelestialBodyType.BarrenPlanet:   return Barren(elev, sea, temp, ridge);
            case CelestialBodyType.Moon:
            case CelestialBodyType.Asteroid:       return Airless(elev, sea, temp, ridge);
            case CelestialBodyType.RockyPlanet:
            default:                               return Terran(elev, sea, moist, temp, ridge);
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

    static TerrainType Ice(float elev, float sea, float moist, float temp, float ridge, float lat)
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
        if (elev < 0.3f + sea) return frozen ? TerrainType.FrozenSea : TerrainType.Ocean;
        if (ridge > 0.8f)  return TerrainType.Mountains;
        // Glacier is a real terrain — a permanent ice sheet is a thing you stand on. Its thawed twin is
        // NOT "Highlands", which is an altitude rather than a biome (see the note in Terran): melted, this
        // ground falls through to the climate tests below and reads as whatever its temperature and
        // moisture actually make it.
        if (elev > 0.72f && frozen) return TerrainType.Glacier;
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

    static TerrainType OceanWorld(float elev, float sea, float temp, float lat, float noise, bool living)
    {
        // WHAT IS ABOVE THE WATER, and then what that ground is. The waterline is the only part that
        // moves: once a tile is out of the sea, its own height decides whether it reads as a mountain, an
        // island or a beach, at the same fixed heights every other world uses. Drain an ocean world and
        // its exposed seabed reads as the low ground it always was, rather than every tile being promoted
        // to a mountain because the sea left.
        if (elev >= 0.64f + sea)
        {
            if (elev > 0.80f) return TerrainType.Mountains;
            if (elev > 0.70f) return TerrainType.Island;
            return TerrainType.Beach;
        }

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
        // REEFS ARE A BIOSPHERE FEATURE, not a depth feature.
        //
        // Coral is a colony of animals and the algae living in it — a dead world's warm shallows are just
        // warm shallows. This used to draw from elevation and temperature alone, so a sterile ocean world
        // came out ringed with "coral shallows" that nothing had ever grown. Gated on the same
        // biosphereActive flag that decides whether the LAND grows anything, so the sea and the land now
        // tell the player the same story about whether the world is alive.
        //
        // Shallow AND warm, both: coral needs sunlight to reach it and will not survive cold water. That
        // is also why an ocean world is where they cluster — a drowned world is nearly all shallows near
        // its islands, which is the spec's "very high water levels ... can spawn algae in its oceans or
        // coral reefs".
        if (living && elev < 0.40f + sea && temp > 0.6f) return TerrainType.Reef;
        return TerrainType.Ocean;
    }

    static TerrainType Barren(float elev, float sea, float temp, float ridge)
    {
        // A BARREN WORLD CAN HOLD WATER. It is barren for want of life and air, not for want of a basin —
        // and until now this classifier had no water terrain in it at all, so the Water Level slider had
        // nowhere to put the sea. All it could do was push more ground under the old fixed `elev < 0.3`
        // line and widen the SALT FLATS: pouring an ocean onto a dead world made it drier-looking.
        //
        // Water first, before ridge, exactly as Terran and Ice do it — a drowned fault line stays sea
        // rather than folding into a mountain range that happens to be underwater.
        if (elev < 0.3f + sea) return temp < 0.22f ? TerrainType.FrozenSea : TerrainType.Ocean;

        if (ridge > 0.82f) return TerrainType.Mountains;
        if (ridge > 0.7f)  return TerrainType.Canyon;

        // SALT FLATS ARE WHAT A DRIED SEABED LEAVES BEHIND — so this threshold stays FIXED, and the water
        // test above does all the moving. The two together give exactly the right behaviour for free:
        //
        //   * Dry the world out and the sea retreats below this line, so the flats spread across the
        //     seabed it uncovered. The less water, the more evaporite — which is what a salt flat IS.
        //   * Flood it and the sea rises past this line, so there are no flats at all. Correct: you
        //     cannot have a dried lake bed under a lake.
        //
        // The old code had this line and no water test at all, so the only thing the Water Level slider
        // could do on a barren world was push more ground under a FIXED salt-flat threshold. Adding water
        // made the world look drier, which is exactly backwards.
        if (elev < 0.3f)   return TerrainType.SaltFlat;

        if (ridge > 0.5f)  return TerrainType.Badlands;
        if (elev > 0.55f)  return TerrainType.MetallicCrust;
        return TerrainType.Wasteland;
    }

    static TerrainType Airless(float elev, float sea, float temp, float ridge)
    {
        // An airless world has no weathering, so its broken ground stays broken: bare rock, not an
        // altitude band. Mountains rather than Highlands, for the same reason as everywhere else.
        if (ridge > 0.85f) return TerrainType.Mountains;
        if (elev > 0.7f)   return TerrainType.MetallicCrust;
        if (elev < 0.28f)  return TerrainType.Crater;
        if (ridge > 0.72f) return TerrainType.CrystalField;
        // A moon's own orbital heat (SolarSystemGenerator.BiasHeat sets terrainParams.heat from its
        // distance from the star, same as its parent planet's band) used to be computed and stored but
        // never actually read here — every moon showed frost in its low ground regardless of how hot its
        // orbit ran. Same freeze threshold Terran/Ice already use, so a moon's look agrees with its own
        // °C reading (PlanetTemperature) the same way a planet's does.
        // An airless body holds no LIQUID water — nothing here becomes Ocean at any water level, which is
        // why this line reads Ice rather than sea. But ice on an airless world is real (Europa, Pluto:
        // AtmosphereRules.ApplyWaterLoss deliberately spares frozen water for exactly this reason), so the
        // frozen band still moves with the world's water: more water, more ice in the low ground.
        if (elev < 0.4f + sea) return temp < 0.22f ? TerrainType.Ice : TerrainType.CrackedGround;
        return TerrainType.Barren;
    }

    static TerrainType Terran(float elev, float sea, float moist, float temp, float ridge)
    {
        // Open water freezes when the world runs cold — so cooling a world (orbital shades, core cooling,
        // moving it outward) visibly ices its seas over, and warming one thaws them back. Temperature is
        // the same value PlanetTemperature reads, so the map and the °C readout always agree.
        //
        // These two are the only lines here that move with the water: the sea itself, and the strip of
        // beach that hugs it. Everything below is ground, and ground does not care where the tide is.
        if (elev < 0.36f + sea) return temp < 0.22f ? TerrainType.FrozenSea : TerrainType.Ocean;
        if (elev < 0.40f + sea) return temp < 0.22f ? TerrainType.Snow : TerrainType.Beach;

        if (ridge > 0.82f) return TerrainType.Mountains;

        // HIGHLANDS AND HILLS USED TO BE RETURNED HERE, at elev > 0.74 and > 0.66, and they are gone.
        //
        // They are not biomes. "Highland" says how high the ground is; it says nothing about whether it
        // is forest, grassland, desert or tundra, and returning it as a terrain TYPE meant that every
        // world's high ground was one undifferentiated brown band with no climate in it — the moment
        // ground crossed 0.66 its temperature and moisture stopped mattering at all.
        //
        // High ground now falls through to the same climate tests as everything else, so a wet warm
        // upland is forest and a dry cold one is steppe, and how high it is is reported SEPARATELY —
        // as an elevation band and a number in the tile readout (see ElevationBand). One tile, two
        // independent facts, which is what they always were.
        //
        // Mountains stay, because a mountain genuinely is a terrain rather than an altitude: bare rock,
        // too steep and too broken to be anything else. That is why the test above it is `ridge` — how
        // BROKEN the ground is — and not elevation.

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
