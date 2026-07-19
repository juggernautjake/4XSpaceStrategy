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
    public float armLength;         // normalised radius at which the arms begin fading toward the rim
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
        // Where the arms START fading toward the rim (see the envelope in Generate). A compact galaxy
        // draws in at 0.6, a sprawling one carries structure almost to the edge at 0.95.
        s.armLength = F(0.6f, 0.95f);
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
// This is a BACKDROP, not the whole view. Galaxy mode's enlarged system stars stay lit at full alpha in
// front of it, at their true positions — so the widest zoom shows you the galaxy and where you are in it
// at the same time. The faint stars baked into the texture are the galaxy's own uncounted billions.
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
            // density rolls 0.7-1.25 (see FromSeed) — remap onto the alpha range rather than clamping,
            // since clamping collapsed every roll above 1.0 (almost half the range) to the same opaque quad.
            float alpha = Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(0.7f, 1.25f, shape.density));
            var mat = SpaceMaterials.Additive(new Color(1f, 1f, 1f, alpha));
            v.spiralTex = Generate(galaxy != null ? galaxy.visualSeed : 1, shape);
            mat.mainTexture = v.spiralTex;
            qr.material = mat;
        }

        // NO separate sparkle field for the systems.
        //
        // There used to be one — a twinkling quad per system, mapped into the disc at 0.7x its real
        // position. It existed because the star proxies used to fade OUT as this view faded in, so
        // something had to stand in for them. They no longer do: galaxy mode keeps its enlarged stars lit
        // at full alpha all the way to the zoom ceiling, at their TRUE positions. Drawing sparkles as
        // well would double every system, at two slightly different places, one of them turning with the
        // disc and one not. The real stars are the better marker: they are correctly placed, correctly
        // coloured, and they can still be hovered and clicked.
        //
        // The faint stars baked into the spiral texture stay — those are the galaxy's own uncounted
        // billions, not the dozen systems you can visit.

        // The core. Sized against the spiral rather than the system-view black hole, because here it is
        // standing in for an entire galactic centre rather than sitting in one system.
        BlackHoleVisual.Build(discGo.transform, v.baseRadius * 0.13f,
                              withLight: false, name: "GalacticCore");

        root.AddComponent<FadeGroup>();
        return v;
    }

    // No SetScale. The deep view is deliberately FIXED at true galaxy scale so its arms stay registered
    // with the system positions in front of it — see the note in GalaxyLOD.ApplyDeep. A scale setter here
    // would advertise a knob that must not be turned.

    // The spiral texture belongs to this instance. There WAS a static seed-keyed cache here; it was
    // removed because nothing ever evicted from it — every new game and every load added another
    // ~0.4 MB RGBA32 (320x320x4) pinned by a static reference that Resources.UnloadUnusedAssets cannot
    // reclaim. There is exactly one deep view alive at a time, so a cache was buying nothing to begin
    // with.
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

                // Radial envelope: arms are at full strength out to armLength, then ramp to nothing at
                // the rim, with a second ramp over the last 18% so the disc has no hard circular edge.
                //
                // armLength is the START of the fade, not its end. The disc is sized at 1.3x the
                // outermost system, so that system sits at r = 1/1.3 = 0.77; with armLength in
                // [0.6, 0.95] the arms are still at roughly 60% there in the worst case and full
                // strength in the best. They are never absent, which is the point — the outer third of
                // the map used to sit on bare black, outside the galaxy it belongs to.
                //
                // Both ramps go through Ramp(), NOT Mathf.SmoothStep. See the note on Ramp: passing edges
                // to Mathf.SmoothStep silently gives a smoothed lerp between those two VALUES instead,
                // which attenuated the whole disc and inverted armLength's meaning.
                float lengthFade = 1f - Ramp(shape.armLength, 1f, r);
                float rimFade = 1f - Ramp(0.82f, 1f, r);
                arms *= lengthFade * rimFade;

                // Central bulge: bright, smooth, and it should swamp the arms rather than sit under them.
                float core = Mathf.Exp(-(r * r) / Mathf.Max(0.0004f, shape.coreSize * shape.coreSize));

                // Dust lanes — darker material between the arms, strongest mid-disc.
                float lanes = Mathf.Clamp01(1f - arms) * Ramp(0f, 0.35f, r) * rimFade;

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

    /// A 0..1 S-curve ramp between two edges — GLSL's `smoothstep(lo, hi, x)`.
    ///
    /// THIS IS NOT WHAT Mathf.SmoothStep DOES, and the difference is easy to miss because the signatures
    /// match. Unity's `Mathf.SmoothStep(a, b, t)` is a smoothed LERP: it returns a value between `a` and
    /// `b`, using `t` as the blend. It does not treat `a` and `b` as edges.
    ///
    /// So `Mathf.SmoothStep(0.82f, 1f, r)` — which reads exactly like a rim fade — actually returns
    /// 0.82..1 for r in 0..1, i.e. it never approaches zero and it starts ramping at r = 0 rather than at
    /// 0.82. Used as an envelope it attenuates the ENTIRE disc to <=18% and inverts the parameter's
    /// meaning, so a "longer" arm setting produced fainter arms. That is what was hollowing out the
    /// spiral and leaving the outer systems on bare black.
    ///
    /// `Mathf.SmoothStep(0f, 1f, t)` is the one form that behaves as expected, because lerping 0..1 by a
    /// smoothed t IS the S-curve. Feeding it an InverseLerp gives the two-edge version.
    static float Ramp(float lo, float hi, float x)
        => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo, hi, x));

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

}
