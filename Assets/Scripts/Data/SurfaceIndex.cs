using System.Collections.Generic;
using UnityEngine;

// The map overlays you survey a world for. Each is a 0..1 score per surface tile that says how well a
// given kind of building would do there.
public enum SurfaceIndexKind { None, Mineral, Heat, Fertile, Wind, Solar, Water }

// ============================================================================================
// PER-TILE SURVEY INDEXES
//
// DERIVED FROM THE TERRAIN ITSELF, never stored and never invented.
//
// PlanetTerrainGenerator already builds a coherent field per tile — elevation, moisture, temperature
// (which falls off with latitude and scales with the planet's distance from its star), ridge — and
// CLASSIFIES the biome from it. These indexes read that same field back. That's what makes the results
// make sense rather than look random:
//
//   * An ocean is cooler than a desert on the same world because the ONE temperature value that made
//     one an ocean and the other a desert is the same value the Heat index reports.
//   * Poles are cold, equators are hot, because temperature is (1 - latitude) weighted.
//   * A world close to its star is hotter EVERYWHERE, because BiasHeat scales terrainParams.heat by
//     distance — so its coldest tile can still out-produce another world's hottest.
//   * Mountains are windy and mineral-rich because elevation and ridge are high there.
//
// So there are two different questions, and the UI answers both:
//   ABSOLUTE — "what will this actually yield?"  -> Get()
//   RELATIVE — "where on THIS world is best?"    -> Percentile()/TopFraction(), which is why a cold
//              world still highlights its ten hottest tiles even though they're all poor.
//
// Costs nothing to save, survives a reload untouched, and a world re-rolled from the same seed reads
// identically — the same guarantee the terrain already makes.
// ============================================================================================
public static class SurfaceIndex
{
    public static readonly SurfaceIndexKind[] All =
    {
        SurfaceIndexKind.Mineral, SurfaceIndexKind.Heat, SurfaceIndexKind.Fertile,
        SurfaceIndexKind.Wind, SurfaceIndexKind.Solar, SurfaceIndexKind.Water
    };

    // ---- The shared field ----
    // Read straight from the generator, so the index and the pixel you're looking at can never disagree.
    static PlanetTerrainGenerator.Sample Field(CelestialBody b, int x, int y)
    {
        float u = (x + 0.5f) / Mathf.Max(1, b.surface.width);
        float v = (y + 0.5f) / Mathf.Max(1, b.surface.height);
        return PlanetTerrainGenerator.SampleNormalized(b, u, v, b.terrainParams, 4);
    }

    public static float Get(CelestialBody b, SurfaceIndexKind kind, int x, int y)
    {
        if (b?.surface == null || x < 0 || y < 0 || x >= b.surface.width || y >= b.surface.height) return 0f;
        var f = Field(b, x, y);
        var t = b.surface.tiles[x, y];

        switch (kind)
        {
            case SurfaceIndexKind.Mineral: return Mineral(f, t);
            case SurfaceIndexKind.Heat: return Heat(b, f);
            case SurfaceIndexKind.Fertile: return Fertile(b, f);
            case SurfaceIndexKind.Wind: return Wind(b, f);
            case SurfaceIndexKind.Solar: return Solar(b, f);
            case SurfaceIndexKind.Water: return Water(b, f, x, y);
            default: return 0f;
        }
    }

    // ---- MINERAL: where a mine pays ----
    // Ore comes up where the crust is broken and raised. A real deposit on the tile beats any of it.
    static float Mineral(PlanetTerrainGenerator.Sample f, TerrainTile t)
    {
        if (f.water) return 0.03f;                       // you can't sink a shaft into an ocean

        float v = f.ridge * 0.55f                        // broken ground exposes seams
                + f.elevation * 0.30f                    // uplift brings them within reach
                + BiomeMineral(f.terrain) * 0.35f;

        if (t != null && t.HasOre) v = Mathf.Max(v, 0.6f + t.oreRichness * 0.4f);
        return Mathf.Clamp01(v);
    }

    static float BiomeMineral(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.MetallicCrust: return 1.0f;
            case TerrainType.CrystalField: return 0.95f;
            case TerrainType.Mountains: return 0.8f;
            case TerrainType.Canyon: case TerrainType.Badlands: return 0.65f;
            case TerrainType.Highlands: case TerrainType.Crater: return 0.6f;
            case TerrainType.LavaRock: case TerrainType.ObsidianFlat: return 0.55f;
            case TerrainType.Hills: return 0.45f;
            case TerrainType.Barren: case TerrainType.Wasteland: return 0.35f;
            default: return 0.12f;
        }
    }

    // ---- HEAT: where a geothermal plant pays ----
    // Two separate sources, and keeping them apart is what makes this read correctly:
    //   SURFACE heat — the sun. Latitude and distance from the star. A desert is hot, a pole is not.
    //   GEOTHERMAL heat — the crust. Volcanoes and geysers, which are hot even at a frozen pole.
    // A geothermal plant mostly cares about the second, which is why a volcano on an ice world is still
    // the best site on it.
    static float Heat(CelestialBody b, PlanetTerrainGenerator.Sample f)
    {
        float crust = CrustHeat(f.terrain);

        // A volcanic world is hot underneath everywhere, not just at its vents.
        float planetCrust = b.type == CelestialBodyType.VolcanicPlanet ? 0.45f : 0.06f;

        // Deep ocean bleeds heat away; you don't build a geothermal plant on the sea floor.
        float waterPenalty = f.water ? 0.55f : 1f;

        float v = Mathf.Max(crust, planetCrust) * 0.72f     // the crust dominates
                + f.temperature * 0.28f;                    // the sun contributes a little
        return Mathf.Clamp01(v * waterPenalty);
    }

    static float CrustHeat(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Volcano: return 1.0f;
            case TerrainType.MagmaField: return 0.95f;
            case TerrainType.GeyserField: return 0.92f;
            case TerrainType.LavaRock: return 0.72f;
            case TerrainType.AshWaste: return 0.55f;
            case TerrainType.CrackedGround: return 0.5f;    // fissures = accessible heat
            case TerrainType.ObsidianFlat: return 0.48f;
            case TerrainType.Mountains: case TerrainType.Highlands: return 0.2f;   // some tectonism
            default: return 0.05f;
        }
    }

    // ---- FERTILE: where farmland pays ----
    // Crops want warmth, water and flat ground — all three, which is why this multiplies rather than
    // adds. A soaking tundra and a warm desert are both useless; you need the overlap.
    static float Fertile(CelestialBody b, PlanetTerrainGenerator.Sample f)
    {
        // NO BIOSPHERE, NO FERTILITY — ZERO, not "a bit". Fertility is not a property of dirt: it is soil,
        // and soil is the product of things having lived and died in it. A sterile world has warm, flat,
        // even damp ground and nothing whatever to farm, so the index reads nothing at all until a
        // biosphere exists (Microbial Seeding is the project that starts one — see BiosphereRules).
        //
        // It used to score a dead world on warmth and flatness alone. The moisture flooring in
        // SampleNormalized held the number down on Rocky worlds, but only there and only partly, so
        // barren, ice and volcanic worlds surveyed as usefully farmable and a Rocky one still read ~25%
        // on ground where nothing could grow.
        if (b == null || !b.biosphereActive) return 0f;

        if (f.water) return 0.02f;

        // A temperate optimum: too cold OR too hot both kill it.
        float warmth = 1f - Mathf.Abs(f.temperature - 0.62f) / 0.62f;
        warmth = Mathf.Clamp01(warmth);

        float wet = Mathf.Clamp01(f.moisture * 1.25f);
        float flat = Mathf.Clamp01(1f - f.ridge * 0.9f);          // you can't plough a mountainside

        float v = warmth * 0.45f + wet * 0.35f + flat * 0.2f;
        v *= Mathf.Lerp(0.35f, 1f, BiomeFertile(f.terrain));      // the biome confirms or vetoes it
        return Mathf.Clamp01(v * 1.35f);
    }

    static float BiomeFertile(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Grassland: return 1.0f;
            case TerrainType.Plains: return 0.92f;
            case TerrainType.Jungle: return 0.85f;
            case TerrainType.Forest: return 0.8f;
            case TerrainType.Swamp: return 0.7f;
            case TerrainType.Savanna: case TerrainType.Steppe: return 0.5f;
            case TerrainType.Taiga: return 0.45f;
            case TerrainType.Beach: return 0.35f;
            case TerrainType.Hills: return 0.4f;
            case TerrainType.Tundra: return 0.15f;
            case TerrainType.Highlands: return 0.18f;
            case TerrainType.Desert: case TerrainType.Dunes: case TerrainType.SaltFlat: return 0.05f;
            case TerrainType.Badlands: case TerrainType.Wasteland: case TerrainType.Barren: return 0.04f;
            default: return 0.08f;
        }
    }

    // ---- WEATHER: where turbines pay, and how violent the sky is ----
    //
    // Formerly the Wind Index. Renamed because atmosphere now drives it properly, and what it measures
    // is no longer just "is it breezy here" but how much weather this world HAS — which is a fact about
    // the whole planet before it is a fact about any tile.
    //
    // Weather is impossible without air, and its severity scales with how much air there is. A world
    // with a trace atmosphere is dead calm everywhere; a thick one is violently stormy, which is why a
    // gas giant (and a heavy moon like Titan) is the stormiest thing in the game.
    static float Wind(CelestialBody b, PlanetTerrainGenerator.Sample f)
    {
        float severity = WeatherSeverity(b);
        if (severity <= 0f) return 0f;

        float exposure = f.elevation * 0.5f + f.ridge * 0.18f;
        float open = 1f - Shelter(f.terrain);
        float thermal = Mathf.Abs(f.temperature - 0.5f) * 0.5f;   // hot/cold extremes stir the air
        float polar = f.latitude * 0.28f;                          // roaring forties

        float v = exposure * 0.42f + open * 0.24f + thermal * 0.2f + polar * 0.24f;
        if (f.water) v += 0.18f;                                   // nothing to break the wind at sea
        return Mathf.Clamp01(v * 1.25f) * severity;
    }

    /// How much weather this world has AT ALL, 0..1 — the planet-wide multiplier under every tile of the
    /// Weather index, and what the overlay's opacity now carries.
    ///
    /// Below a trace atmosphere there is nothing to move and the index is flat zero. From there it
    /// climbs with pressure and saturates around 6 atmospheres, past which more air does not make the
    /// storms meaningfully worse — it is already as bad as a turbine can survive.
    public static float WeatherSeverity(CelestialBody b)
    {
        if (b == null) return 0f;
        const float TraceAir = 0.15f;
        if (b.atmospheres <= TraceAir) return 0f;
        return Mathf.Clamp01(Mathf.InverseLerp(TraceAir, 6f, b.atmospheres));
    }

    /// Plain-language severity, for the Survey status line.
    public static string WeatherLabel(CelestialBody b)
    {
        float s = WeatherSeverity(b);
        if (s <= 0f) return "airless — no weather at all";
        if (s < 0.2f) return "near-calm";
        if (s < 0.45f) return "mild";
        if (s < 0.7f) return "stormy";
        if (s < 0.9f) return "violent";
        return "catastrophic";
    }

    static float Shelter(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Jungle: case TerrainType.Forest: case TerrainType.Taiga: return 0.85f;
            case TerrainType.Swamp: return 0.6f;
            case TerrainType.Canyon: return 0.7f;
            case TerrainType.Mountains: case TerrainType.Highlands: return 0f;
            default: return 0.3f;
        }
    }

    // ---- SOLAR: where panels pay ----
    //
    // Cloudless, high, and — now — POLAR. Moisture means cloud, so dry ground is bright ground, and
    // altitude helps because there is less air above you.
    //
    // The polar preference is a deliberate reversal of the old equatorial one. A panel at the pole of a
    // world with any axial tilt sees continuous summer daylight for months and never suffers the
    // overhead-noon-then-nothing cycle the equator gets; the equator's advantage in peak angle is worth
    // less than the pole's advantage in HOURS. It also makes the Solar and Weather maps disagree, which
    // is what makes siting a decision instead of a formality.
    static float Solar(CelestialBody b, PlanetTerrainGenerator.Sample f)
    {
        float polar = 0.35f + f.latitude * 0.65f;                  // long polar days beat high equatorial noon
        float clear = Mathf.Clamp01(1f - f.moisture * 1.15f);      // moisture = cloud
        float altitude = f.elevation * 0.2f;

        // How much light the planet gets AT ALL. terrainParams.heat is set from its distance to its
        // star, so a far, cold world's sunniest desert still can't match a close one's.
        float insolation = Mathf.Clamp01(b.terrainParams.heat / 1.4f);

        float v = (polar * 0.45f + clear * 0.4f + altitude) * Mathf.Lerp(0.45f, 1.15f, insolation)
                  * SolarPressureFactor(b.atmospheres);
        if (f.terrain == TerrainType.Storm) v *= 0.25f;            // permanent cloud
        return Mathf.Clamp01(v);
    }

    /// What a world's air pressure does to solar output, as a multiplier on a 1.0 baseline.
    ///
    /// Above Earth-normal, output falls linearly and reaches EXACTLY ZERO at the dead line.
    ///
    /// THE SPEC'S OWN NUMBERS DO NOT CLOSE, so this picks the reading that stays self-consistent. It
    /// asks for "every Atmosphere added will remove 20%", then gives 2 atm -> 80% (which is 20 a step)
    /// and 4 atm -> 20% (which is 26.7 a step), then says 5 is worthless (which a 20-a-step slope only
    /// reaches at 6). Taking the 5-atmosphere cutoff as the fixed point and solving back gives 25 a
    /// step: 2 atm -> 75%, 3 -> 50%, 4 -> 25%, 5 -> 0. That lands within five points of both worked
    /// examples AND makes the build-menu cutoff true rather than approximate — which matters more,
    /// because a gate that fires while output is still 20% is a gate the player will read as a bug.
    ///
    /// Below Earth pressure it runs the other way, gaining 10 points per 0.1 atmosphere under: a 0.5-atm
    /// world runs at 150%, an airless one at 200% with nothing between the panel and the star.
    ///
    /// Returned UNCLAMPED above 1 on purpose. The bonus is real and the caller decides what to do with
    /// it — the index map clamps for display, but the build-menu gate and the efficiency readout want
    /// to know a thin world genuinely beats Earth.
    public static float SolarPressureFactor(float atmospheres)
    {
        if (atmospheres >= 1f)
            return Mathf.Max(0f, 1f - (atmospheres - 1f) / (SolarDeadAtmospheres - 1f));
        return 1f + (1f - atmospheres) * 1.0f;
    }

    /// Atmospheres at which solar output reaches zero, and so the point past which panels are not
    /// offered at all. SolarPressureFactor is solved against this, so the two can never drift apart.
    public const float SolarDeadAtmospheres = 5f;

    /// Is solar worth building on this world? False above the dead line — and true again the moment
    /// terraforming brings the air back down to 4, which is what makes thinning an atmosphere a
    /// strategic move rather than a cosmetic one.
    public static bool SolarViable(CelestialBody b) =>
        b != null && b.atmospheres < SolarDeadAtmospheres;

    // ============================================================================================
    // WATER: where anything that needs water pays
    //
    // THE OLD VERSION SCORED THE WATER ITSELF, which made it useless for the one thing it is for.
    // It read the 3x3 neighbourhood, so only a tile touching the sea got any credit at all, and the
    // highest numbers on the map were on open water — where nothing can be built. A Steam Turbine
    // needs water to raise steam with; it does not need to be standing IN it. The practical effect was
    // that the whole Electrical category's wettest buildings had to be jammed onto the shoreline in a
    // single-tile ring, and once that ring was full the world had no more sites.
    //
    // SO WATER PROJECTS, exactly as a power plant projects a grid. Every connected body of water throws
    // an influence outward across the land around it, and the two numbers that matter both scale with
    // HOW BIG THAT BODY IS:
    //
    //   REACH      how far inland it carries. A pond wets its own doorstep; an ocean supplies a
    //              hinterland. Scaled by the SQUARE ROOT of area, because that is the body's linear
    //              size — a lake of four times the area is twice as wide, and twice as wide is the
    //              honest reading of "twice the water". Linear in area would let one sea cover a
    //              hemisphere.
    //   STRENGTH   how good the best site near it is. Also root-scaled, and saturating: a pond is a
    //              poor site however close you stand to it.
    //
    // ---- THE BUFFER, and why the shore is not the best place ----
    //
    // The request is explicit: leave a gap so a row of turbines does not have to be crammed onto the
    // waterline. So the shore is VIABLE but deliberately not optimal (ShoreFraction of the peak), the
    // peak sits one tile further in, and it falls away from there to nothing at the reach. You can
    // build on the beach; you do slightly better a step back from it, and you can keep building
    // inland for a long way before it stops being worth it.
    //
    // Measured against a rendered map at 160x80 with a plausible spread of bodies: about half the land
    // has some hydro, a fifth of it clears 50%, and under 5% clears 80% — so the good ground is
    // findable and finite rather than everywhere or nowhere.
    // ============================================================================================

    /// The shortest reach any body has, in tiles, and the longest — an ocean does not supply a planet.
    const float WaterReachMin = 3f, WaterReachMax = 14f;

    /// Tiles of reach per unit of sqrt(area).
    const float WaterReachPerRoot = 0.8f;

    /// Distance out to which the shoreline discount applies. 1 = only the tiles touching the water.
    const int WaterBuffer = 1;

    /// What the shore gets, as a fraction of the peak. Under 1 so the best ground is a step inland.
    const float WaterShoreFraction = 0.72f;

    static float Water(CelestialBody b, PlanetTerrainGenerator.Sample f, int x, int y)
    {
        if (f.water) return 0.02f;                                 // you cannot build on it

        var field = WaterFieldFor(b);
        if (field == null) return 0.02f;

        int i = y * b.surface.width + x;
        int id = field.nearestBody[i];
        if (id < 0) return 0.02f;                                  // no water anywhere on this world

        float d = field.distance[i];
        float reach = ReachOf(field.bodySize[id]);
        if (d > reach) return 0.02f;

        float peak = StrengthOf(field.bodySize[id]);

        float v;
        if (d <= WaterBuffer) v = peak * WaterShoreFraction;
        else
        {
            float t = (d - (WaterBuffer + 1)) / Mathf.Max(0.001f, reach - (WaterBuffer + 1));
            v = peak * (1f - Mathf.Clamp01(t));
        }

        // RELIEF IS A TIEBREAK NOW, NOT A GATE. It used to be nearly half the score, which is right for
        // a hydro DAM (you need head to drop the water through) and wrong for everything else that
        // reads this index — a boiler hall wants water, not a waterfall. Kept as a modest bonus so a
        // hilly shore still beats a flat marsh, and so the Hydro Plant's own minIndex gate still tends
        // to land it somewhere with a gradient.
        float relief = Mathf.Clamp01(f.elevation * 0.7f + f.ridge * 0.5f);
        v *= Mathf.Lerp(0.85f, 1.15f, relief);

        return Mathf.Clamp01(v);
    }

    static float ReachOf(int tiles)
        => Mathf.Min(WaterReachMax, WaterReachMin + Mathf.Sqrt(Mathf.Max(1, tiles)) * WaterReachPerRoot);

    static float StrengthOf(int tiles)
        => Mathf.Min(1f, 0.35f + Mathf.Sqrt(Mathf.Max(1, tiles)) / 22f);

    // ============================================================================================
    // THE WATER DISTANCE FIELD
    //
    // For every land tile: how far it is from open water, and which BODY of water that is — because the
    // answer depends on how big that body is, and the nearest water is not always the biggest.
    //
    // A CHAMFER DISTANCE TRANSFORM rather than a per-tile search. The obvious implementation asks each
    // tile "how far to the nearest water" and scans outward, which is O(reach^2) per tile and is asked
    // for every tile of every overlay repaint and every efficiency calculation. Two sweeps over the grid
    // — one forward, one back — produce the whole field in O(w*h), and the diagonal step costing
    // sqrt(2) puts it within a couple of percent of true Euclidean distance, which is close enough that
    // no player will ever see the difference between the two.
    //
    // LONGITUDE WRAPS, so the sweeps run twice: a single forward-and-back pair cannot propagate a
    // distance the long way around the seam. Two pairs is enough for any feature narrower than the map,
    // which every water body is.
    // ============================================================================================
    class WaterField
    {
        public float[] distance;     // tiles to the nearest water, per cell
        public int[] nearestBody;    // which connected body that water belongs to, or -1
        public int[] bodySize;       // tiles in each body
    }

    // KEYED ON THE BODY OBJECT, NOT b.id. `id` is not unique across a galaxy — SolarSystemGenerator
    // restarts its counter for every system — so two worlds in different systems share one. The
    // reference is exact and collision-free (CelestialBody overrides neither Equals nor GetHashCode).
    static readonly Dictionary<CelestialBody, WaterField> waterFields
        = new Dictionary<CelestialBody, WaterField>();

    static WaterField WaterFieldFor(CelestialBody b)
    {
        if (b?.surface == null) return null;
        if (waterFields.TryGetValue(b, out var f)) return f;
        f = BuildWaterField(b);
        waterFields[b] = f;
        return f;
    }

    static WaterField BuildWaterField(CelestialBody b)
    {
        int w = b.surface.width, h = b.surface.height;
        int n = w * h;

        var field = new WaterField
        {
            distance = new float[n],
            nearestBody = new int[n]
        };

        // ---- Label the connected bodies ----
        var label = new int[n];
        for (int i = 0; i < n; i++) label[i] = -1;
        var sizes = new List<int>();
        var stack = new Stack<int>();

        for (int start = 0; start < n; start++)
        {
            if (label[start] >= 0) continue;
            if (!IsWaterAt(b, start % w, start / w)) continue;

            int id = sizes.Count;
            int count = 0;
            label[start] = id;
            stack.Push(start);

            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                count++;
                int cx = cur % w, cy = cur / w;

                // Orthogonal only, and longitude wraps — the same connectivity every other rule in the
                // project uses, so "one body of water" means the same thing here as everywhere else.
                PushWater(b, label, stack, id, cx + 1, cy);
                PushWater(b, label, stack, id, cx - 1, cy);
                PushWater(b, label, stack, id, cx, cy + 1);
                PushWater(b, label, stack, id, cx, cy - 1);
            }
            sizes.Add(count);
        }

        field.bodySize = sizes.ToArray();

        // ---- The distance transform ----
        for (int i = 0; i < n; i++)
        {
            bool wet = label[i] >= 0;
            field.distance[i] = wet ? 0f : float.MaxValue;
            field.nearestBody[i] = wet ? label[i] : -1;
        }

        if (sizes.Count == 0)
        {
            for (int i = 0; i < n; i++) field.distance[i] = 0f;   // no water: the field means nothing
            return field;
        }

        const float D1 = 1f, D2 = 1.41421356f;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Relax(field, w, h, x, y, x - 1, y, D1);
                    Relax(field, w, h, x, y, x, y - 1, D1);
                    Relax(field, w, h, x, y, x - 1, y - 1, D2);
                    Relax(field, w, h, x, y, x + 1, y - 1, D2);
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    Relax(field, w, h, x, y, x + 1, y, D1);
                    Relax(field, w, h, x, y, x, y + 1, D1);
                    Relax(field, w, h, x, y, x + 1, y + 1, D2);
                    Relax(field, w, h, x, y, x - 1, y + 1, D2);
                }
        }

        return field;
    }

    static bool IsWaterAt(CelestialBody b, int x, int y)
    {
        var t = b.surface.tiles[x, y];
        return t != null && PlanetTerrainGenerator.IsWater(t.type);
    }

    static void PushWater(CelestialBody b, int[] label, Stack<int> stack, int id, int x, int y)
    {
        int w = b.surface.width, h = b.surface.height;
        if (y < 0 || y >= h) return;                 // latitude does not wrap; the poles are edges
        x = ((x % w) + w) % w;                       // longitude does
        int i = y * w + x;
        if (label[i] >= 0 || !IsWaterAt(b, x, y)) return;
        label[i] = id;
        stack.Push(i);
    }

    /// One step of the chamfer sweep: can (nx,ny) offer (x,y) a shorter route to water?
    static void Relax(WaterField f, int w, int h, int x, int y, int nx, int ny, float cost)
    {
        if (ny < 0 || ny >= h) return;
        nx = ((nx % w) + w) % w;
        int from = ny * w + nx, to = y * w + x;
        if (f.distance[from] == float.MaxValue) return;
        float d = f.distance[from] + cost;
        if (d < f.distance[to]) { f.distance[to] = d; f.nearestBody[to] = f.nearestBody[from]; }
    }

    // ============================================================================
    // RELATIVE RANKING — "where on THIS world is best?"
    //
    // Absolute yield alone can't answer that: on a frozen world EVERY tile is a poor geothermal site,
    // and a fixed threshold would highlight nothing at all. So the "best places" highlight is a
    // PERCENTILE of this planet's own distribution — its ten hottest tiles are its ten hottest tiles
    // whether or not they're any good in absolute terms. The yield readout tells you the hard truth
    // separately.
    // ============================================================================
    class Stats { public float[] sorted; public float min, max; }

    // KEYED ON THE BODY OBJECT, NOT b.id — the same correction the water field above carries, and for
    // the same reason. `id` restarts at 0 for every system SolarSystemGenerator makes, so the third
    // world of system 1 and the third world of system 7 shared a cache entry: whichever was surveyed
    // first decided what "the best ground on this world" meant for both of them, and the second world's
    // overlays highlighted tiles chosen from a distribution belonging to a planet in another star
    // system. PowerGrid hit this exact bug and documents it at length.
    static readonly Dictionary<(CelestialBody, SurfaceIndexKind), Stats> statsCache
        = new Dictionary<(CelestialBody, SurfaceIndexKind), Stats>();

    static Stats GetStats(CelestialBody b, SurfaceIndexKind k)
    {
        var key = (b, k);
        if (statsCache.TryGetValue(key, out var s)) return s;

        int w = b.surface.width, h = b.surface.height;
        var vals = new float[w * h];
        int i = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                vals[i++] = Get(b, k, x, y);

        System.Array.Sort(vals);
        s = new Stats { sorted = vals, min = vals[0], max = vals[vals.Length - 1] };
        statsCache[key] = s;
        return s;
    }

    /// Drop cached distributions for a world — call when its terrain actually changes (terraforming,
    /// planetary remodelling), or the overlays would describe the world it used to be.
    ///
    /// The water field goes with them. It is derived from which tiles are wet, and terraforming is
    /// precisely the thing that floods and drains them — a stale field would keep supplying hydro to a
    /// desert that used to be a coast, and deny it to a sea that has just appeared.
    public static void InvalidateStats(CelestialBody b)
    {
        if (b == null) return;
        foreach (var k in All) statsCache.Remove((b, k));
        waterFields.Remove(b);
    }

    public static void InvalidateAll()
    {
        statsCache.Clear();
        waterFields.Clear();
    }

    /// Where this tile ranks on this world, 0 (worst) .. 1 (best).
    public static float Percentile(CelestialBody b, SurfaceIndexKind k, int x, int y)
    {
        if (b?.surface == null || k == SurfaceIndexKind.None) return 0f;
        var s = GetStats(b, k);
        float v = Get(b, k, x, y);
        int lo = 0, hi = s.sorted.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (s.sorted[mid] < v) lo = mid + 1; else hi = mid; }
        return s.sorted.Length > 1 ? lo / (float)(s.sorted.Length - 1) : 1f;
    }

    /// The value at which the top `fraction` of this world's tiles begins (0.1 = the best 10%).
    public static float TopFractionThreshold(CelestialBody b, SurfaceIndexKind k, float fraction)
    {
        if (b?.surface == null || k == SurfaceIndexKind.None) return 0f;
        var s = GetStats(b, k);
        int idx = Mathf.Clamp(Mathf.FloorToInt((1f - fraction) * (s.sorted.Length - 1)), 0, s.sorted.Length - 1);
        return s.sorted[idx];
    }

    /// Is this tile in the best `fraction` of this world for this index?
    public static bool IsTopFraction(CelestialBody b, SurfaceIndexKind k, int x, int y, float fraction)
    {
        if (k == SurfaceIndexKind.None || b?.surface == null) return false;

        // AN INDEX THAT IS ZERO EVERYWHERE HAS NO BEST TILES — the best 10% of nothing is nothing.
        //
        // Without this, the threshold on such a world is itself 0 and the `>=` below is true for every
        // tile, so the best-sites overlay lights up the entire map. Fertile on a world with no biosphere
        // is exactly that case now that it reads a flat zero: the index correctly says "you cannot farm
        // here" while the overlay said "farm anywhere".
        if (Best(b, k) <= 0f) return false;

        return Get(b, k, x, y) >= TopFractionThreshold(b, k, fraction);
    }

    public static float Best(CelestialBody b, SurfaceIndexKind k)
        => b?.surface == null || k == SurfaceIndexKind.None ? 0f : GetStats(b, k).max;

    // ============================================================================================
    // WHAT AN OVERLAY ACTUALLY DRAWS — the consolidation rule
    //
    // Every index used to paint EVERY tile. A ramp that fades toward transparent still tints the whole
    // world, so a map whose fertility ran 8% to 30% came out uniformly green and said, in effect, "farm
    // wherever you like, it is all a bit farmable". That is the opposite of what a siting overlay is for,
    // and it made the survey ladder pointless: if everywhere works, where you build does not matter.
    //
    // So an index is now drawn in two bands and nowhere else:
    //
    //   THE BEST QUARTER of this world's tiles for that index — always drawn, always the brightest thing
    //   on the map. RELATIVE, so a frozen world still shows you its hottest ground and a barely-fertile
    //   one still shows you its greenest. This is the "unless the planet just is not abundant" clause:
    //   the best sites a world HAS are always findable, however poor they are in absolute terms.
    //
    //   THE REST OF THE WORLD'S BETTER HALF, where it also scores 50% or better — drawn dimmer, so on a
    //   rich world you can still see the good ground around the best ground and choose between sites for
    //   reasons other than rank. Both cuts have to be passed: absolute keeps poor ground off the map,
    //   relative keeps an exceptionally rich world from being painted corner to corner again.
    //
    // Below both, nothing is drawn at all. An 18% tile is not a faint opportunity, it is somewhere you
    // should not put a farm, and the map now says so by leaving it blank.
    //
    // ONE RULE, EVERY READOUT. The survey overlay, the highlight while you hold a building, and the
    // under-the-cursor readout all ask this, so they can never disagree about whether a tile counts.
    // ============================================================================================

    /// Below this an index is not drawn — unless the tile is in this world's best quarter anyway.
    public const float ShowFloor = 0.5f;

    /// The fraction of a world's tiles that always count as its best ground, however poor they are.
    public const float TopBand = 0.25f;

    /// The most of a world any one index may paint: its better half, and only the part of that half
    /// which also clears ShowFloor.
    public const float ShownFraction = 0.5f;

    /// Should this tile be drawn for this index, and how brightly (0 dim .. 1 the world's best)?
    public static bool Shown(CelestialBody b, SurfaceIndexKind k, int x, int y, out float t)
        => ShownFor(b, k, Get(b, k, x, y), out t);

    /// As above, for a value already read — the overlay walks every tile and must not pay for Get twice
    /// (each call re-samples the terrain noise field).
    public static bool ShownFor(CelestialBody b, SurfaceIndexKind k, float v, out float t)
    {
        t = 0f;
        if (b?.surface == null || k == SurfaceIndexKind.None) return false;

        float max = Best(b, k);
        if (max <= 0f || v <= 0.001f) return false;   // an index that is zero everywhere has no best ground

        // The value at which the best quarter begins. Compared with >= rather than by percentile so that
        // a PLATEAU works: on a world where a third of the tiles sit at exactly the maximum, every one of
        // them is the best ground there is, and a rank-based test would have called none of them top.
        float cut = TopFractionThreshold(b, k, TopBand);

        if (v >= cut)
        {
            // The bright band. Spread across the top of the ramp so the very best tile on the world is
            // unmistakably the very best, rather than one of a uniformly glowing crowd.
            t = max > cut ? Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(cut, max, v)) : 1f;
            return true;
        }

        // TWO CUTS, NOT ONE, and they do different jobs.
        //
        //   ABSOLUTE (ShowFloor)  — is this tile any good? An 18% tile is not worth drawing on any world.
        //   RELATIVE (the median) — is it good FOR HERE? Without this, an exceptionally rich world goes
        //                           back to being painted corner to corner, because nearly all of it
        //                           clears 50% — and "everywhere is fine" is the exact answer these
        //                           overlays exist to stop giving.
        //
        // So no more than half of any world is ever painted, and on most worlds it is far less.
        if (v < ShowFloor || v < TopFractionThreshold(b, k, ShownFraction)) return false;

        // The dim band: good ground that simply is not this world's best. Its top end stops short of the
        // bright band's floor, so the two never blur into each other.
        float top = Mathf.Max(ShowFloor + 0.01f, cut);
        t = Mathf.Lerp(0.12f, 0.66f, Mathf.InverseLerp(ShowFloor, top, v));
        return true;
    }

    // ---- Presentation ----
    public static string Name(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Mineral Index";
            case SurfaceIndexKind.Heat: return "Heat Index";
            case SurfaceIndexKind.Fertile: return "Fertile Index";
            case SurfaceIndexKind.Wind: return "Weather Index";
            case SurfaceIndexKind.Solar: return "Solar Index";
            case SurfaceIndexKind.Water: return "Hydro Index";
            default: return "None";
        }
    }

    public static string Describe(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return "Broken, raised crust — mountains, canyons and exposed seams. Mines want the highest they can get.";
            case SurfaceIndexKind.Heat: return "Heat in the CRUST, not the air: volcanoes and geyser fields. A volcano on an ice world is still that world's best geothermal site.";
            case SurfaceIndexKind.Fertile: return "Warm AND wet AND flat — farmland needs all three at once, not any one of them. Needs a LIVING world above all: no biosphere, no soil, and the index reads nothing.";
            case SurfaceIndexKind.Wind: return "Needs AIR: no atmosphere, no weather. Within that — high open ground, coasts and poles.";
            case SurfaceIndexKind.Solar: return "Thin air and long polar days. Dry ground is bright ground; a world far from its star is dim everywhere.";
            case SurfaceIndexKind.Water: return "How much water is within reach. Every lake and sea supplies the land AROUND it — the bigger the body, the further inland it carries and the better the best sites near it. The shore itself is good; one step back from it is better.";
            default: return "";
        }
    }

    /// The colour ramp for each overlay. Alpha rises with the score so weak tiles fade and the good
    /// patches are what your eye lands on.
    public static Color Ramp(SurfaceIndexKind k, float t)
    {
        t = Mathf.Clamp01(t);
        Color c;
        switch (k)
        {
            // Brighter at the top than the old muddy tan, which was the one ramp whose best ground read as
            // dirt rather than as a find. Orange, and clearly not Heat's red.
            case SurfaceIndexKind.Mineral: c = Color.Lerp(new Color(0.28f, 0.16f, 0.06f), new Color(1.00f, 0.60f, 0.16f), t); break;
            case SurfaceIndexKind.Heat: c = Color.Lerp(new Color(0.85f, 0.45f, 0.10f), new Color(1.00f, 0.10f, 0.05f), t); break;
            case SurfaceIndexKind.Fertile: c = Color.Lerp(new Color(0.05f, 0.22f, 0.08f), new Color(0.30f, 1.00f, 0.25f), t); break;
            // PURPLE. It was a slate-to-whitish blue, which failed twice over: a pale desaturated blue
            // barely separates from the terrain underneath it, and it was near enough to Water's
            // ramp that the two overlays read as the same map. Purple is the one hue nothing else here
            // uses — Mineral is brown, Heat orange-red, Fertile green, Solar yellow, Water blue — so a
            // glance at the colour is enough to know which overlay you're looking at.
            case SurfaceIndexKind.Wind: c = Color.Lerp(new Color(0.16f, 0.05f, 0.28f), new Color(0.80f, 0.36f, 1.00f), t); break;
            case SurfaceIndexKind.Solar: c = Color.Lerp(new Color(0.40f, 0.34f, 0.10f), new Color(1.00f, 0.95f, 0.40f), t); break;
            // Saturation RISES with the score, rather than falling. This ran navy -> pale sky blue, so
            // the best ground got the weakest, most washed-out colour on the map — the ramp was reading
            // as "more index = whiter", which is the opposite of intensity. Now weak ground is a muted
            // grey-blue that sinks into the terrain and strong ground is a deep, fully saturated blue
            // that sits on top of it. Alpha (below) climbs alongside, so the two reinforce instead of
            // fighting.
            case SurfaceIndexKind.Water: c = Color.Lerp(new Color(0.34f, 0.44f, 0.56f), new Color(0.00f, 0.34f, 1.00f), t); break;
            default: return new Color(0, 0, 0, 0);
        }
        c.a = Mathf.Lerp(0.12f, 0.88f, t);
        return c;
    }

    /// The fill for a tile the overlay has decided to draw (see Shown). The index's own hue, taken from
    /// the UPPER half of its ramp — a tile that made the cut is never painted in the muddy bottom end,
    /// because everything muddy is now simply absent — and with alpha that climbs hard, so this world's
    /// best ground is close to solid colour.
    public static Color Highlight(SurfaceIndexKind k, float t)
    {
        t = Mathf.Clamp01(t);
        var c = Ramp(k, Mathf.Lerp(0.45f, 1f, t));
        c.a = Mathf.Lerp(0.45f, 0.92f, t);
        return c;
    }

    /// The line drawn around a patch of highlighted ground: the same hue, brightened past anything the
    /// fill can reach, and fully opaque.
    ///
    /// This is what makes a patch READ AS A PLACE. A translucent wash over textured terrain has no edge —
    /// you can see roughly where it is strong but not where it stops, which is exactly the question when
    /// you are about to draw a footprint. An outline in the index's own colour also keeps the map legible
    /// with two overlays up: the shape is bounded in purple, so it is weather, whatever is under it.
    public static Color Outline(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return new Color(1.00f, 0.68f, 0.22f, 1f);   // bright orange
            case SurfaceIndexKind.Heat:    return new Color(1.00f, 0.30f, 0.20f, 1f);   // bright red
            case SurfaceIndexKind.Fertile: return new Color(0.48f, 1.00f, 0.38f, 1f);   // bright green
            case SurfaceIndexKind.Wind:    return new Color(0.88f, 0.52f, 1.00f, 1f);   // bright purple
            case SurfaceIndexKind.Solar:   return new Color(1.00f, 0.97f, 0.42f, 1f);   // bright yellow
            case SurfaceIndexKind.Water:   return new Color(0.38f, 0.76f, 1.00f, 1f);   // bright blue
            default:                       return new Color(1f, 1f, 1f, 1f);
        }
    }

    // Minerals you can see from orbit; everything else needs someone on the ground.
    // ============================================================================================
    // WHICH TIER EACH OVERLAY BELONGS TO
    //
    // The six indexes are the backbone of the research ladder, split 1 - 2 - 2 - 1. The pairing is not
    // alphabetical, it follows the DECISION each tier lets you make:
    //
    //   Survey (0)          Mineral            — should I claim this? You can see seams from orbit.
    //   Deep Research I     Heat + Fertile     — where do things GO? These two decide where a geothermal
    //                                            plant and a farm belong, so they arrive together.
    //   Deep Research II    Wind + Solar       — how do I POWER it? The power-siting pair.
    //   Deep Research III   Water              — the last one, with the late-game secrets.
    // ============================================================================================
    public static int RequiredLevel(SurfaceIndexKind k)
    {
        switch (k)
        {
            case SurfaceIndexKind.Mineral: return 0;
            case SurfaceIndexKind.Heat:
            case SurfaceIndexKind.Fertile: return 1;
            case SurfaceIndexKind.Wind:
            case SurfaceIndexKind.Solar: return 2;
            case SurfaceIndexKind.Water: return 3;
            default: return 0;
        }
    }

    public static bool Unlocked(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return false;
        if (GameMode.DevMode) return true;
        if (!b.Surveyed) return false;
        return b.researchLevel >= RequiredLevel(k);
    }

    /// Why an overlay is locked — and it names the TIER, because "needs a deep survey" was useless once
    /// there was more than one of them. A greyed control that will not say what is missing is a dead end.
    public static string LockReason(CelestialBody b, SurfaceIndexKind k)
    {
        if (b == null) return "no world selected";
        if (!b.Surveyed) return "survey this world first";

        int need = RequiredLevel(k);
        if (b.researchLevel >= need) return null;

        return $"needs {DeepResearch.Name(need)} — send a research ship to study this world";
    }
}
