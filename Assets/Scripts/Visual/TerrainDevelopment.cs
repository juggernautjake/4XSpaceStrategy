using UnityEngine;

// ============================================================================================
// A WORLD'S DEVELOPMENT, AS A SEQUENCE OF REAL SURFACES
//
// The maths behind watching a planet form. Shared, because two things need it and they must not drift:
// the loading sequence morphs the REAL homeworld through it, and the moons run the identical process at
// their own resolution.
//
// THE IDEA, WHICH IS THE REQUEST'S OWN. Terrain is generated from `terrainParams` — how hot, how wet,
// how much relief, where the sea sits — the same knobs the Dev Mode sandbox exposes. So "watch the
// world be built" is those sliders being driven, with the panel hidden: lerp the params from a
// primordial state toward the world's real ones and re-sample the SAME noise field at each step.
//
// That gives a genuine TRANSFORMATION rather than a reveal. Continents rise out of a global ocean;
// mountains fold up; ice creeps down from the poles. Every tile passes through several biomes on its
// way to what it ends up as. A reveal — uncovering the finished map a tile at a time — can only ever
// look like a picture loading, because every tile that appears is already final.
//
// THE LAST FRAME IS NOT COMPUTED. It is the world's REAL generated surface, handed in by the caller.
// The surface is gameplay data — buildings get placed on those tiles — so what the world settles into
// has to be exactly what the player then plays on, not a close match drawn with fewer octaves.
// ============================================================================================
public static class TerrainDevelopment
{
    /// How many steps a world takes from featureless to finished.
    public const int Stages = 8;

    /// Octaves for the INTERMEDIATE frames only. Fewer than the real grid's six: these are transient
    /// frames shown for under a second each, the fine coastline detail six buys is invisible at that
    /// size, and each extra octave is another full pass of noise over every texel.
    public const int Octaves = 3;

    /// What a world looks like before it is a world.
    ///
    /// One primordial state, and each body type resolves it into its own kind of "featureless" through
    /// the classifier it already has: flatten the relief and drown it, and a terran world reads as open
    /// ocean, an airless one as uniform cratered rock, a volcanic one as obsidian flats, an ice world as
    /// frozen sea. That is exactly the "just ocean, or just barren rock like the moon" the request
    /// describes — and it falls out of the world's own nature rather than being special-cased per type.
    public static PlanetTerrainGenerator.NoiseParams Primordial(PlanetTerrainGenerator.NoiseParams f)
        => new PlanetTerrainGenerator.NoiseParams
        {
            scale = f.scale,      // the SAME feature size, so the continents that grow are the real ones
            elevation = 0.12f,    // almost no relief — an orb, not a landscape
            moisture = f.moisture,
            heat = f.heat,        // where it orbits is a fact about the world, not something it develops
            ridge = 0.05f,        // no mountains yet
            seaLevel = 1f         // drowned; every classifier's low ground is its own featureless state
        };

    public static PlanetTerrainGenerator.NoiseParams Lerp(
        PlanetTerrainGenerator.NoiseParams a, PlanetTerrainGenerator.NoiseParams b, float t)
        => new PlanetTerrainGenerator.NoiseParams
        {
            scale = Mathf.Lerp(a.scale, b.scale, t),
            elevation = Mathf.Lerp(a.elevation, b.elevation, t),
            moisture = Mathf.Lerp(a.moisture, b.moisture, t),
            heat = Mathf.Lerp(a.heat, b.heat, t),
            ridge = Mathf.Lerp(a.ridge, b.ridge, t),
            seaLevel = Mathf.Lerp(a.SeaLevelOrNeutral, b.SeaLevelOrNeutral, t)
        };

    /// One intermediate frame of a world's development.
    public static Color[] BuildStage(CelestialBody body, int w, int h, int stage)
    {
        var final = body.terrainParams;
        var p = Lerp(Primordial(final), final, stage / (float)(Stages - 1));

        var frame = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                var s = PlanetTerrainGenerator.SampleNormalized(body, (x + 0.5f) / w, v, p, Octaves);
                Color c = TerrainColorMap.Get(s.terrain);
                float b = Mathf.Lerp(0.86f, 1.12f, s.shade);
                frame[y * w + x] = new Color(c.r * b, c.g * b, c.b * b, 1f);
            }
        }
        return frame;
    }

    /// The world's REAL finished surface, downsampled to the morph resolution. This is the last frame,
    /// and it is a read of the ALREADY-GENERATED grid — never a re-sample of the noise field — so it
    /// carries the neighbour-aware clean-up (connected oceans, inland lakes, beaches) that per-pixel
    /// sampling cannot reproduce.
    public static Color[] FinalFrame(CelestialBody body, int w, int h)
    {
        var frame = new Color[w * h];
        var barren = TerrainColorMap.Get(TerrainType.Barren);
        var surf = body.surface;
        if (surf == null) { for (int i = 0; i < frame.Length; i++) frame[i] = barren; return frame; }

        int sw = surf.width, sh = surf.height;
        for (int y = 0; y < h; y++)
        {
            int sy = Mathf.Clamp(y * sh / h, 0, sh - 1);
            for (int x = 0; x < w; x++)
            {
                int sx = Mathf.Clamp(x * sw / w, 0, sw - 1);
                var tile = surf.tiles[sx, sy];
                Color c = barren;
                if (tile != null)
                {
                    c = TerrainColorMap.Get(tile.type);
                    float b = Mathf.Lerp(0.86f, 1.12f, tile.shade);
                    c = new Color(c.r * b, c.g * b, c.b * b, 1f);
                }
                frame[y * w + x] = c;
            }
        }
        return frame;
    }

    /// An empty stage set with only its first and last frames filled. The rest fill in one per frame —
    /// building all of them at once is ~40k noise samples on a single frame, and the frame it would land
    /// on is the exact moment the world is handed over and the reveal begins.
    public static Color[][] NewStages(CelestialBody body, int w, int h)
    {
        var stages = new Color[Stages][];
        stages[0] = BuildStage(body, w, h, 0);
        stages[Stages - 1] = FinalFrame(body, w, h);
        return stages;
    }

    /// Fill in the next unbuilt stage, if any. Cheap no-op once they all exist.
    public static void BuildNext(CelestialBody body, int w, int h, Color[][] stages)
    {
        if (stages == null) return;
        for (int s = 1; s < Stages - 1; s++)
            if (stages[s] == null) { stages[s] = BuildStage(body, w, h, s); return; }
    }

    /// Per-texel offsets in stage units, so the map does not flip over all at once — each tile crosses
    /// into its next state at its own moment, and the change reads as tiles forming rather than as a
    /// slideshow.
    ///
    /// From System.Random and never UnityEngine.Random: this is cosmetic, and it must not perturb the
    /// shared generation stream that the generators and FactionAI keep drawing from while it plays.
    public static float[] BuildJitter(int n, int seed)
    {
        var j = new float[n];
        var rng = new System.Random(seed);
        for (int i = 0; i < n; i++) j[i] = (float)rng.NextDouble();
        return j;
    }

    /// Write the frame for a world partway through its development. `t` is 0..1 across the whole morph.
    public static void Paint(Color[][] stages, float[] jitter, Color[] into, float t)
    {
        if (stages == null || jitter == null || into == null) return;

        float g = Mathf.Clamp01(t) * (Stages - 1);
        for (int i = 0; i < into.Length; i++)
        {
            // Hard switch rather than a colour blend between stages. Blending smears two biomes into a
            // colour that is neither, which reads as a dissolve; switching keeps every texel a real
            // terrain colour at every instant, and that is what makes it read as TILES.
            int s = Mathf.Clamp(Mathf.FloorToInt(g + jitter[i]), 0, Stages - 1);

            // Stages fill in one per frame, so one can in principle be reached before it exists. Walking
            // back to the newest built frame keeps the world a beat behind rather than throwing.
            while (s > 0 && stages[s] == null) s--;
            if (stages[s] == null) continue;

            into[i] = stages[s][i];
        }
    }
}
