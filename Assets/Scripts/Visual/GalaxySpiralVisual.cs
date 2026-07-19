using System.Collections.Generic;
using UnityEngine;

// Every procedural choice that makes one galaxy look unlike another.
//
// Rolled once from Galaxy.visualSeed, which is STORED on the galaxy rather than derived from its name —
// so a reloaded save shows the same sky it was generated with.
//
// The axes here are the ones that actually change the silhouette. Arm count and tightness dominate; two
// galaxies differing only in palette still read as the same galaxy, which is why colour is the last thing
// rolled and the structural fields come first.
public struct GalaxyShape
{
    public int armCount;            // 2-5 major arms
    public float handedness;        // +1 clockwise, -1 counter-clockwise
    public float tightness;         // how fast the arms wrap — low is open and sweeping, high is coiled
    public float armLength;         // 0.5-1.0 of the disc radius before the arms fade out
    public float armSharpness;      // high = thin defined lanes, low = broad diffuse arms
    public float wispiness;         // turbulence broken into the arms
    public float distortion;        // large-scale warping of the whole spiral, so it isn't perfectly regular
    public float density;           // overall brightness/opacity of the arm material
    public float coreSize;          // radius of the bright central bulge
    public bool counterArms;        // a second, fainter arm set winding the OTHER way
    public int counterArmCount;
    public float spinSpeed;         // degrees/sec of the whole disc
    public Color coreColor;
    public Color innerArm;
    public Color outerArm;
    public Color dust;              // the dark lane colour subtracted between arms

    public static GalaxyShape FromSeed(int seed)
    {
        // A private RNG stream so rolling a galaxy's look never disturbs the global sequence that
        // generation and gameplay draw from — generating a galaxy twice with the same world seed must
        // still produce the same worlds.
        var rng = new System.Random(seed);
        float F(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        var s = new GalaxyShape();
        s.armCount = 2 + rng.Next(0, 4);                       // 2..5
        s.handedness = rng.NextDouble() < 0.5 ? 1f : -1f;
        s.tightness = F(1.6f, 5.2f);
        s.armLength = F(0.55f, 1.0f);
        s.armSharpness = F(1.1f, 3.4f);
        s.wispiness = F(0.15f, 0.85f);
        s.distortion = F(0f, 0.55f);
        s.density = F(0.7f, 1.25f);
        s.coreSize = F(0.10f, 0.26f);
        // A minority of galaxies wind both ways at once — rarer than the plain case, so it stays a
        // "look at that one" rather than the norm.
        s.counterArms = rng.NextDouble() < 0.28;
        s.counterArmCount = 2 + rng.Next(0, 3);
        s.spinSpeed = F(0.6f, 2.2f) * (rng.NextDouble() < 0.5 ? 1f : -1f);

        // Palette. Cores run warm (old stars), arms run cool (young hot stars and ionised gas) — that
        // temperature contrast is what makes a galaxy image read as a galaxy rather than a colour wheel.
        float hueArm = F(0.52f, 0.78f);                        // cyan .. violet
        float hueCore = Mathf.Repeat(hueArm + F(0.35f, 0.55f), 1f);   // roughly complementary: amber/gold
        s.coreColor = Color.HSVToRGB(hueCore, F(0.25f, 0.55f), 1f);
        s.innerArm = Color.HSVToRGB(hueArm, F(0.35f, 0.7f), 1f);
        s.outerArm = Color.HSVToRGB(Mathf.Repeat(hueArm + F(-0.09f, 0.09f), 1f), F(0.55f, 0.9f), F(0.75f, 1f));
        s.dust = Color.HSVToRGB(Mathf.Repeat(hueArm + 0.5f, 1f), F(0.3f, 0.6f), F(0.10f, 0.25f));
        return s;
    }
}

// The galaxy as seen from outside it — the widest zoom level, where the whole playable map has collapsed
// to one slowly turning spiral wrapped around its central black hole.
//
// Built as a single generated texture on one quad rather than thousands of particle quads. That is a
// deliberate trade: a particle spiral would let individual stars move, but it costs thousands of draw
// calls for something that is decorative and on screen only at one zoom level, and this project already
// generates its nebulae exactly this way (SpaceBackground). One texture, one draw call, and the arm
// structure can be far richer than a particle budget would allow.
//
// The real system stars are the exception — they are actual sparkle quads pinned to their true galaxy
// positions, so the widest view still shows you where your empire is.
public class GalaxySpiralVisual : MonoBehaviour
{
    // 320, not 512.
    //
    // Generation is ~5 Perlin lookups plus an Atan2/Log/Pow/Exp per pixel, on the main thread. At 512 that
    // is 262k pixels and a visible multi-hundred-millisecond freeze; at 320 it is 102k, a bit under half
    // the work. The texture is a soft additive disc stretched over a very large quad and read at extreme
    // zoom, so bilinear filtering hides the difference — this is the one place in the file where detail
    // genuinely costs nothing to give up.
    const int TexSize = 320;

    Transform disc;          // the spinning part: spiral texture + system sparkles
    Texture2D spiralTex;     // owned by this instance; destroyed with it
    float baseRadius = 1f;

    /// Build the deep-view galaxy under `parent`. `radius` is the spiral's world radius at scale 1.
    public static GalaxySpiralVisual Build(Transform parent, Galaxy galaxy, float radius)
    {
        var root = new GameObject("DeepGalaxy");
        root.transform.SetParent(parent, false);
        var v = root.AddComponent<GalaxySpiralVisual>();
        v.baseRadius = Mathf.Max(1f, radius);

        var shape = GalaxyShape.FromSeed(galaxy != null ? galaxy.visualSeed : 1);

        // The spinning disc. Everything that should turn with the galaxy hangs off this.
        var discGo = new GameObject("Disc");
        discGo.transform.SetParent(root.transform, false);
        v.disc = discGo.transform;
        var spin = discGo.AddComponent<SelfSpin>();
        spin.speed = shape.spinSpeed;
        spin.unscaled = true;   // the map keeps turning while the simulation is paused

        // The spiral itself: one quad lying flat in XZ, so it reads as a disc under the game's fixed
        // overhead camera. A Unity quad faces +Z, so it is pitched 90 degrees to lie down.
        var quad = SpaceMaterials.Primitive(PrimitiveType.Quad, discGo.transform, "SpiralPlane");
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = Vector3.one * v.baseRadius * 2f;
        var qr = quad.GetComponent<Renderer>();
        if (qr != null)
        {
            var mat = SpaceMaterials.Additive(new Color(1f, 1f, 1f, Mathf.Clamp01(shape.density)));
            v.spiralTex = Generate(galaxy != null ? galaxy.visualSeed : 1, shape);
            mat.mainTexture = v.spiralTex;
            qr.material = mat;
        }

        // Real system stars as bright twinkles at their true positions, scaled into the disc.
        v.BuildSystemSparkles(galaxy, shape);

        // The core. Sized against the spiral rather than the system-view black hole, because here it is
        // standing in for an entire galactic centre rather than sitting in one system.
        BlackHoleVisual.Build(discGo.transform, v.baseRadius * 0.13f,
                              withLight: false, name: "GalacticCore");

        root.AddComponent<FadeGroup>();
        return v;
    }

    // A sparkle per system, at its real galaxy position mapped into the spiral's local space.
    //
    // Parented to the spinning disc rather than the static root: at this zoom the positions are landmarks
    // rather than targets (nothing is clickable out here), and a spiral turning underneath a fixed grid of
    // dots reads as two unrelated objects.
    void BuildSystemSparkles(Galaxy galaxy, GalaxyShape shape)
    {
        if (galaxy == null || galaxy.systems == null) return;

        // Map real galaxy coordinates into the spiral disc. Systems occupy the inner ~70% so they sit in
        // the arms rather than out past the visible edge.
        float worldR = 0f;
        foreach (var sys in galaxy.systems)
            worldR = Mathf.Max(worldR, new Vector2(sys.galaxyPosition.x, sys.galaxyPosition.z).magnitude);
        if (worldR <= 0.01f) worldR = 1f;
        float k = baseRadius * 0.7f / worldR;

        var dot = SparkleTexture();
        foreach (var sys in galaxy.systems)
        {
            var q = SpaceMaterials.Primitive(PrimitiveType.Quad, disc, "Sparkle_" + sys.name);
            q.transform.localPosition = new Vector3(sys.galaxyPosition.x * k, 0f, sys.galaxyPosition.z * k);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Home and owned systems burn brighter — at this range that is the only empire information
            // the view carries.
            bool mine = sys.isHome || sys.owner == FactionManager.Player;
            float size = baseRadius * (mine ? 0.075f : 0.05f);
            q.transform.localScale = Vector3.one * size;

            Color c = sys.isBlackHole ? new Color(0.85f, 0.6f, 1f)
                    : (sys.combinedStar != null ? sys.combinedStar.color : Color.white);
            c = Color.Lerp(c, Color.white, 0.45f);           // hot cores read white-ish at distance
            if (mine) c = Color.Lerp(c, new Color(0.6f, 1f, 0.7f), 0.35f);

            var r = q.GetComponent<Renderer>();
            if (r != null)
            {
                var m = SpaceMaterials.Additive(c);
                m.mainTexture = dot;
                r.material = m;
            }

            var tw = q.AddComponent<SparkleTwinkle>();
            tw.baseScale = size;
            // Detuned periods so the field shimmers instead of pulsing in unison — the same trick the
            // background star field uses.
            tw.speed = 0.5f + Mathf.Repeat(sys.name.GetHashCode() * 0.0001f, 1.4f);
            tw.amount = mine ? 0.45f : 0.3f;
        }
    }

    // Keep the whole thing facing up and sized to the caller's scale.
    public void SetScale(float s)
    {
        transform.localScale = Vector3.one * Mathf.Max(0.0001f, s);
    }

    // The spiral texture belongs to this instance. There WAS a static seed-keyed cache here; it was
    // removed because nothing ever evicted from it — every new game and every load added another
    // ~0.4 MB RGBA32 (320x320x4) pinned by a static reference that Resources.UnloadUnusedAssets cannot
    // reclaim. There is exactly one deep view alive at a time, so a cache was buying nothing to begin
    // with. (`sparkleTex` below is still static, but it is one shared 64x64 dot — bounded, not growing.)
    void OnDestroy()
    {
        if (spiralTex != null) Destroy(spiralTex);
    }

    // ---- Texture generation ---------------------------------------------------------------------

    // The spiral, drawn analytically into a texture.
    //
    // The arm term is a logarithmic spiral, which is the shape real spiral galaxies actually take: an arm
    // sits where `theta - ln(r)/tightness` is constant, so sweeping that expression through a cosine gives
    // `armCount` arms that wrap tighter toward the centre. Everything else is modulation on top of it —
    // turbulence for wispiness, a low-frequency warp for distortion, a radial envelope for arm length.
    static Texture2D Generate(int seed, GalaxyShape shape)
    {
        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var rng = new System.Random(seed);
        float nx = (float)rng.NextDouble() * 500f;
        float ny = (float)rng.NextDouble() * 500f;

        var px = new Color32[TexSize * TexSize];

        for (int y = 0; y < TexSize; y++)
        {
            for (int x = 0; x < TexSize; x++)
            {
                // Normalised disc coordinates, -1..1.
                float u = (x / (float)(TexSize - 1)) * 2f - 1f;
                float v = (y / (float)(TexSize - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);

                if (r > 1f) { px[y * TexSize + x] = new Color32(0, 0, 0, 0); continue; }

                float theta = Mathf.Atan2(v, u);

                // Large-scale warp so the spiral isn't a perfect mathematical figure. Applied to the ANGLE,
                // which bends the arms; applying it to the radius instead just makes the disc lumpy.
                if (shape.distortion > 0.001f)
                {
                    float warp = Mathf.PerlinNoise(nx + u * 1.4f, ny + v * 1.4f) - 0.5f;
                    theta += warp * shape.distortion * 2.2f;
                }

                float arms = ArmField(r, theta, shape.armCount, shape.handedness,
                                      shape.tightness, shape.armSharpness);

                if (shape.counterArms)
                {
                    float back = ArmField(r, theta, shape.counterArmCount, -shape.handedness,
                                          shape.tightness * 0.75f, shape.armSharpness);
                    arms = Mathf.Max(arms, back * 0.45f);
                }

                // Turbulence, broken into the arms rather than added over them — multiplying keeps the
                // structure and eats holes in it, which is what "wispy" looks like. Adding would just fog
                // the whole disc.
                if (shape.wispiness > 0.001f)
                {
                    float n = FBm(nx + u * 3.1f, ny + v * 3.1f, 4);
                    arms *= Mathf.Lerp(1f, 0.35f + n * 1.3f, shape.wispiness);
                }

                // Radial envelope: arms fade out past armLength, and everything fades at the rim so the
                // disc has no hard circular edge.
                float lengthFade = 1f - Mathf.SmoothStep(shape.armLength * 0.75f, shape.armLength, r);
                float rimFade = 1f - Mathf.SmoothStep(0.82f, 1f, r);
                arms *= lengthFade * rimFade;

                // Central bulge: bright, smooth, and it should swamp the arms rather than sit under them.
                float core = Mathf.Exp(-(r * r) / Mathf.Max(0.0004f, shape.coreSize * shape.coreSize));

                // Dust lanes — darker material between the arms, strongest mid-disc.
                float lanes = Mathf.Clamp01(1f - arms) * Mathf.SmoothStep(0f, 0.35f, r) * rimFade;

                Color c = Color.Lerp(shape.innerArm, shape.outerArm, Mathf.Clamp01(r / Mathf.Max(0.01f, shape.armLength)));
                Color outCol = c * arms;
                outCol += shape.coreColor * core * 1.35f;
                outCol += shape.dust * lanes * 0.5f;

                float a = Mathf.Clamp01(arms * 0.85f + core + lanes * 0.28f);

                // A sprinkle of individual stars through the disc, biased into the arms where the bright
                // young stars actually are.
                if (rng.NextDouble() < 0.0016 * (0.35f + arms))
                {
                    outCol += Color.white * 0.9f;
                    a = Mathf.Clamp01(a + 0.75f);
                }

                px[y * TexSize + x] = new Color(
                    Mathf.Clamp01(outCol.r), Mathf.Clamp01(outCol.g), Mathf.Clamp01(outCol.b), a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // One set of logarithmic spiral arms, as a 0..1 field.
    static float ArmField(float r, float theta, int count, float handedness, float tightness, float sharpness)
    {
        // Guard the log: r == 0 is the centre, where the arm angle is undefined and the bulge covers it.
        float lr = Mathf.Log(Mathf.Max(0.02f, r));
        float phase = theta * handedness - lr * tightness;
        float raw = Mathf.Cos(phase * count) * 0.5f + 0.5f;       // 0..1
        return Mathf.Pow(raw, sharpness);
    }

    static float FBm(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2.07f;      // not exactly 2, so octaves don't line up into visible grid artefacts
        }
        return norm > 0f ? sum / norm : 0f;
    }

    static Texture2D sparkleTex;

    // A soft round dot with a faint four-point diffraction cross — the shape a bright point source takes
    // through a real lens, and the cheapest way to make a dot read as a STAR rather than a circle.
    static Texture2D SparkleTexture()
    {
        if (sparkleTex != null) return sparkleTex;
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x / (float)(N - 1)) * 2f - 1f;
                float v = (y / (float)(N - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Clamp01(1f - r);
                core = core * core * core;
                float spike = Mathf.Clamp01(1f - Mathf.Abs(u) * 9f) * Mathf.Clamp01(1f - Mathf.Abs(v) * 1.4f)
                            + Mathf.Clamp01(1f - Mathf.Abs(v) * 9f) * Mathf.Clamp01(1f - Mathf.Abs(u) * 1.4f);
                float a = Mathf.Clamp01(core + spike * 0.35f);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        t.SetPixels32(px);
        t.Apply();
        sparkleTex = t;
        return t;
    }
}

// Breathes a sparkle's size so the star field shimmers. Scale rather than alpha, because these are
// additive quads over a bright disc, where a size change reads far more clearly than an opacity change.
public class SparkleTwinkle : MonoBehaviour
{
    public float baseScale = 1f;
    public float speed = 1f;
    public float amount = 0.35f;
    float phase;

    void Start() { phase = Random.Range(0f, Mathf.PI * 2f); }

    void Update()
    {
        float s = baseScale * (1f + Mathf.Sin(Time.unscaledTime * speed + phase) * amount);
        transform.localScale = Vector3.one * Mathf.Max(0.0001f, s);
    }
}
