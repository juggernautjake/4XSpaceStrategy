using System.Collections.Generic;
using UnityEngine;

// Procedural, per-map space backdrop: layered nebulae + gas clouds, many small sparkling stars,
// distant galaxies (round + spiral), occasional small shooting stars, and parallax. Deliberately dim
// so it never competes with the foreground system.
//
// Two modes: SPACE (the procedural sky) or SOLID (a flat colour from the RGB sliders). Turning the
// space background OFF simply shows the solid colour — the colour sliders always apply.
public class SpaceBackground : MonoBehaviour
{
    public static SpaceBackground Instance;

    Camera cam;
    Transform rootFar, rootMid, rootNear;
    GameObject solidQuad;
    readonly List<TwinkleStar> twinkles = new List<TwinkleStar>();

    public int Seed { get; private set; } = 12345;
    public bool SpaceEnabled { get; private set; } = true;    // false => show the solid colour
    public Color SolidColor { get; private set; } = new Color(0.02f, 0.03f, 0.06f);

    // Back-compat alias used by the save system.
    public bool Enabled => SpaceEnabled;
    public bool SolidMode => !SpaceEnabled;

    float shootingTimer;
    Vector3 lastCamPos;
    // No shared spiral texture any more — each distant spiral generates its own (GenerateDistantSpiral),
    // so the deep field doesn't repeat the same galaxy nine times at nine rotations.
    Texture2D _dot, _galaxyRound, _cloud;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("SpaceBackground").AddComponent<SpaceBackground>();
    }

    void Awake()
    {
        Instance = this;
        cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
    }

    void Start()
    {
        if (cam != null) lastCamPos = cam.transform.position;
        Rebuild();
    }

    // Not static any more: each material is recorded so a rebuild can free the previous set, exactly like
    // the textures. One material per quad, ~110 quads, leaked on every "Regenerate Space" otherwise.
    Material MakeMat(Texture2D tex, Color tint)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        m.color = tint;
        materials.Add(m);
        return m;
    }

    GameObject MakeQuad(Transform parent, string name, float distance, float size, Texture2D tex, Color tint)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
        q.transform.SetParent(parent, false);
        q.transform.localPosition = new Vector3(0, 0, distance);
        q.transform.localScale = new Vector3(size, size, 1f);
        var mr = q.GetComponent<MeshRenderer>();
        mr.material = MakeMat(tex, tint);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return q;
    }

    float FovSize(float distance)
    {
        float fov = (cam != null && !cam.orthographic) ? cam.fieldOfView : 60f;
        return distance * 2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 3f;
    }

    public void SetSeed(int seed) { Seed = seed; }

    public void Regenerate()
    {
        Seed = (Seed * 1103515245 + 12345) & 0x7fffffff;
        Rebuild();
    }

    public void SetEnabled(bool spaceOn) { SpaceEnabled = spaceOn; ApplyVisibility(); }

    // Kept for save-compat: solid mode == space off.
    public void SetSolidMode(bool solid) { SpaceEnabled = !solid; ApplyVisibility(); }

    public void SetSolidColor(Color c)
    {
        SolidColor = c;
        if (solidQuad != null) solidQuad.GetComponent<MeshRenderer>().material.color = c;
        if (cam != null && !SpaceEnabled) cam.backgroundColor = c;
    }

    void ApplyVisibility()
    {
        if (rootFar) rootFar.gameObject.SetActive(SpaceEnabled);
        if (rootMid) rootMid.gameObject.SetActive(SpaceEnabled);
        if (rootNear) rootNear.gameObject.SetActive(SpaceEnabled);
        if (solidQuad) solidQuad.SetActive(!SpaceEnabled);
        if (cam != null) cam.backgroundColor = SpaceEnabled ? Color.black : SolidColor;
    }

    public void Rebuild()
    {
        if (cam == null) cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (cam == null) return;

        foreach (Transform c in transform) Destroy(c.gameObject);
        twinkles.Clear();
        // The dim targets point at renderers about to be destroyed, and hold their authored alphas.
        // Re-collected at the end of this method once the new quads exist.
        dimTargets.Clear();
        appliedDim = 1f;
        ReleaseGenerated();  // the previous set is about to be orphaned by the quads we just destroyed

        transform.SetParent(cam.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        var rng = new System.Random(Seed);

        var nebula = Track(GenerateNebula(Seed, rng));
        var starsFar = Track(GenerateStars(Seed + 7, rng, 0.5f));
        var starsMid = Track(GenerateStars(Seed + 31, rng, 0.75f));
        _galaxyRound = Track(GenerateGalaxyRound(Seed + 91));
        _cloud = Track(GenerateCloud(Seed + 200));
        _dot = Track(GenerateDot());

        rootFar = new GameObject("Far").transform; rootFar.SetParent(transform, false);
        rootMid = new GameObject("Mid").transform; rootMid.SetParent(transform, false);
        rootNear = new GameObject("Near").transform; rootNear.SetParent(transform, false);

        float dFar = 950f, dMid = 720f, dNear = 520f;
        MakeQuad(rootFar, "Nebula", dFar, FovSize(dFar), nebula, Color.white);
        MakeQuad(rootFar, "StarsFar", dFar - 5f, FovSize(dFar), starsFar, Color.white);
        MakeQuad(rootMid, "StarsMid", dMid, FovSize(dMid), starsMid, Color.white);

        // Extra faint gas clouds scattered around (background depth, low alpha).
        int clouds = 7;
        for (int i = 0; i < clouds; i++)
        {
            float cs = FovSize(dFar) * (0.14f + (float)rng.NextDouble() * 0.14f);
            var tint = RandomNebulaHue(rng); tint.a = 0.10f + (float)rng.NextDouble() * 0.06f;
            var g = MakeQuad(rootMid, "GasCloud", dMid + (float)rng.NextDouble() * 60f, cs, _cloud, tint);
            Scatter(g, rng, FovSize(dMid) * 0.4f);
        }

        // Distant galaxies — a mix of round and spiral, faint and small.
        //
        // Each spiral gets its OWN generated texture rather than every one of them sharing a single
        // two-arm image. Copies of one spiral at different rotations read as wallpaper the moment you
        // notice it; varying arm count, wind direction and tightness is what makes them look like a real
        // deep field. Roughly 62% of the nine roll as spirals, so this generates ~6 textures per rebuild.
        //
        // They are also foreshortened: a real galaxy is a disc seen at a random angle, so most of them
        // present as ellipses rather than face-on circles. That single non-uniform scale does more for the
        // illusion of distance than any amount of extra texture detail.
        int galaxies = 9;
        for (int i = 0; i < galaxies; i++)
        {
            bool spiral = rng.NextDouble() < 0.62;
            float gs = FovSize(dFar) * (0.045f + (float)rng.NextDouble() * 0.075f);
            var tex = spiral ? Track(GenerateDistantSpiral(rng)) : _galaxyRound;

            // Faint, and tinted — distant galaxies redden with distance and the variation stops the
            // field reading as one flat grey wash.
            var tint = Color.Lerp(new Color(0.82f, 0.86f, 1f), new Color(1f, 0.82f, 0.7f),
                                  (float)rng.NextDouble());
            tint.a = 0.26f + (float)rng.NextDouble() * 0.26f;

            var g = MakeQuad(rootFar, spiral ? "Spiral" : "Galaxy", dFar - 8f, gs, tex, tint);
            Scatter(g, rng, FovSize(dFar) * 0.40f);
            g.transform.localRotation = Quaternion.Euler(0, 0, (float)(rng.NextDouble() * 360));

            // Inclination: squash one axis. Kept above 0.22 so none of them collapses to a bare line.
            float squash = 0.22f + (float)rng.NextDouble() * 0.78f;
            var sc = g.transform.localScale;
            g.transform.localScale = new Vector3(sc.x, sc.y * squash, 1f);
        }

        // Many small, sparkling stars on the near layer.
        int twinkleCount = 90;
        for (int i = 0; i < twinkleCount; i++)
        {
            float s = FovSize(dNear) * (0.0016f + (float)rng.NextDouble() * 0.0026f); // small
            var dot = MakeQuad(rootNear, "Twinkle", dNear, s, _dot, StarTint(rng));
            Scatter(dot, rng, FovSize(dNear) * 0.46f);
            var tw = dot.AddComponent<TwinkleStar>();
            tw.Init((float)(rng.NextDouble() * 6.28), 1.0f + (float)rng.NextDouble() * 2.4f, dot.transform.localScale);
            twinkles.Add(tw);
        }

        solidQuad = MakeQuad(transform, "SolidColor", dFar + 20f, FovSize(dFar + 20f), Texture2D.whiteTexture, SolidColor);

        // After every quad exists, so each one's authored alpha is captured before any dimming happens.
        CollectDimTargets();

        ApplyVisibility();

        var sky = AssetIntegration.LoadSkybox();
        if (sky != null) RenderSettings.skybox = sky;
    }

    void Scatter(GameObject go, System.Random rng, float span)
    {
        go.transform.localPosition += new Vector3(
            (float)(rng.NextDouble() * 2 - 1) * span,
            (float)(rng.NextDouble() * 2 - 1) * span, 0);
    }

    void LateUpdate()
    {
        if (cam == null || !SpaceEnabled) { if (cam != null) lastCamPos = cam.transform.position; return; }

        Vector3 delta = cam.transform.position - lastCamPos;
        lastCamPos = cam.transform.position;

        // Parallax normalised by camera HEIGHT, which is what makes it work at every zoom.
        //
        // The drift used to be driven by raw world movement against a fixed clamp. Near a planet, panning a
        // few units moved the layers a satisfying amount. In the galaxy view, where a single pan crosses
        // hundreds of units, the layers slammed into their clamp on the first frame of movement and then
        // sat there — so the backdrop was dead in exactly the view with the most empty space to sell.
        //
        // Dividing by height converts world movement into SCREEN-RELATIVE movement: panning by one
        // screen-width produces the same parallax whether that screen-width is 4 units or 4,000. The gain
        // is set so behaviour near a system matches roughly what the old constants gave.
        float h = Mathf.Max(1f, cam.transform.position.y);
        float norm = ParallaxGain / h;
        float dr = Vector3.Dot(delta, cam.transform.right) * norm;
        float du = Vector3.Dot(delta, cam.transform.up) * norm;
        Drift(rootFar, dr, du, 0.015f, 60f);
        Drift(rootMid, dr, du, 0.035f, 90f);
        Drift(rootNear, dr, du, 0.06f, 120f);

        UpdateZoomDepth(h);

        shootingTimer -= Time.unscaledDeltaTime;
        if (shootingTimer <= 0f && rootNear != null)
        {
            shootingTimer = Random.Range(5f, 13f);
            SpawnShootingStar();
        }
    }

    // Tuned so parallax near a system feels like the old fixed-gain version did — at the ~200-unit height
    // where you sit looking at one system, ParallaxGain/h is about 1, i.e. unchanged.
    const float ParallaxGain = 200f;

    // Parallax along the ZOOM axis, and the handover to the deep view.
    //
    // Two things happen as you pull back. The near star layer spreads slightly relative to the far nebula,
    // which is what depth looks like when you move away from a field of objects at different distances —
    // panning parallax alone leaves the sky feeling like a painted backdrop. And the whole backdrop dims
    // as the deep view fades in, because the procedural spiral out there is the subject at that zoom and a
    // full-brightness star field in front of it just muddies it.
    void UpdateZoomDepth(float h)
    {
        if (rootNear == null || rootFar == null || rootMid == null) return;

        // Reference height: roughly where a single system fills the view.
        float t = Mathf.Clamp01(Mathf.Log10(Mathf.Max(1f, h / 60f)) / 2.2f);

        float nearSpread = Mathf.Lerp(1f, 1.16f, t);
        rootNear.localScale = Vector3.one * nearSpread;
        rootMid.localScale = Vector3.one * Mathf.Lerp(1f, 1.07f, t);

        // Dim once the wide views start carrying the picture.
        //
        // Driven by the CONTINUOUS fade alphas, not by the discrete tier. Keying off GalaxyLOD.Tier meant
        // the backdrop held full brightness until the boundary flipped and then lurched over half a second
        // — visibly a beat behind the crossfade it was supposed to be reacting to. Reading the alphas
        // makes the sky recede in lockstep with the spiral coming in.
        float dim = Mathf.Lerp(1f, 0.72f, GalaxyLOD.ProxyAlpha);
        dim = Mathf.Lerp(dim, 0.32f, GalaxyLOD.DeepAlpha);
        if (Mathf.Abs(dim - appliedDim) > 0.005f)
        {
            appliedDim = dim;
            ApplyDim(appliedDim);
        }
    }

    float appliedDim = 1f;

    // One dimmable backdrop quad, with its AUTHORED alpha captured once so repeated dimming can never
    // compound into permanent invisibility.
    //
    // Collected once at Rebuild rather than walked per frame. The dim now tracks a continuous fade, so it
    // changes on most frames of a scroll gesture — and GetComponentsInChildren over three roots allocates
    // three arrays each time, during exactly the moment the player is moving the camera.
    struct DimTarget
    {
        public MeshRenderer rend;
        public Material mat;
        public TwinkleStar twinkle;   // non-null: dim via its multiplier, it owns its own alpha
        public float baseAlpha;
    }

    readonly List<DimTarget> dimTargets = new List<DimTarget>();

    void CollectDimTargets()
    {
        dimTargets.Clear();
        CollectDimTargetsIn(rootFar);
        CollectDimTargetsIn(rootMid);
        CollectDimTargetsIn(rootNear);
    }

    void CollectDimTargetsIn(Transform root)
    {
        if (root == null) return;
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            // A shooting star fades on its own timer and destroys itself, so it is never a dim target —
            // dimming would fight its fade, and it is spawned after this runs anyway.
            if (mr.GetComponent<ShootingStar>() != null) continue;
            var tw = mr.GetComponent<TwinkleStar>();
            // sharedMaterial, NOT material. Every quad here was already given its own material by
            // MakeMat, which tracked it for release — but the `.material` getter instantiates ANOTHER
            // per-renderer copy that nothing tracks, so reading it here would leak one instance per quad
            // per rebuild while ReleaseGenerated dutifully freed the originals.
            var m = mr.sharedMaterial;
            dimTargets.Add(new DimTarget
            {
                rend = mr,
                mat = m,
                twinkle = tw,
                baseAlpha = m != null ? m.color.a : 1f
            });
        }
    }

    void ApplyDim(float k)
    {
        for (int i = 0; i < dimTargets.Count; i++)
        {
            var d = dimTargets[i];
            if (d.rend == null) continue;
            if (d.twinkle != null) { d.twinkle.dim = k; continue; }
            if (d.mat == null) continue;
            var c = d.mat.color;
            c.a = Mathf.Clamp01(d.baseAlpha * k);
            d.mat.color = c;
        }
    }

    void Drift(Transform t, float dr, float du, float factor, float clamp)
    {
        if (t == null) return;
        Vector3 p = t.localPosition;
        p.x = Mathf.Clamp(p.x - dr * factor, -clamp, clamp);
        p.y = Mathf.Clamp(p.y - du * factor, -clamp, clamp);
        t.localPosition = p;
    }

    void SpawnShootingStar()
    {
        float dNear = 520f;
        var q = MakeQuad(rootNear, "Shooting", dNear, FovSize(dNear) * 0.012f, _dot, new Color(0.85f, 0.92f, 1f, 1f));
        float span = FovSize(dNear) * 0.4f;
        q.transform.localPosition = new Vector3(-span, Random.Range(-span * 0.3f, span * 0.5f), dNear);
        var ss = q.AddComponent<ShootingStar>();
        ss.Init(new Vector3(1f, Random.Range(-0.4f, -0.1f), 0f).normalized * span * 2.2f, 1.0f);
    }

    static Color StarTint(System.Random rng)
    {
        float r = (float)rng.NextDouble();
        if (r < 0.6f) return Color.white;
        if (r < 0.8f) return new Color(0.7f, 0.8f, 1f);
        return new Color(1f, 0.85f, 0.7f);
    }

    // ---- Texture generation ----
    Texture2D GenerateNebula(int seed, System.Random rng)
    {
        int w = 512, h = 512;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        Color baseCol = new Color(0.012f, 0.016f, 0.032f);
        Color hueA = RandomNebulaHue(rng), hueB = RandomNebulaHue(rng), hueC = RandomNebulaHue(rng);
        float so = seed % 1000 * 0.01f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float cloud = Mathf.Clamp01((FBm(u * 3f + so, v * 3f + so, 5) - 0.45f) * 2.2f);
                float cloud2 = Mathf.Clamp01((FBm(u * 6f + so + 40f, v * 6f + so + 40f, 4) - 0.55f) * 2.4f);
                float cloud3 = Mathf.Clamp01((FBm(u * 1.6f + so + 90f, v * 1.6f + so + 90f, 3) - 0.5f) * 1.8f);

                // FILAMENTS. Plain FBm gives soft blobs, and blobs are what made the old sky read as fog
                // rather than as a nebula. Ridged noise — 1 minus the distance from the midpoint, raised
                // to a power — inverts the field so its ZERO CROSSINGS become bright thin lines, which is
                // what the shock fronts and gas lanes in a real nebula look like.
                float ridge = FBm(u * 4.5f + so + 210f, v * 4.5f + so + 210f, 4);
                ridge = 1f - Mathf.Abs(ridge * 2f - 1f);
                ridge = Mathf.Pow(Mathf.Clamp01(ridge), 5f);
                // Confined to where there is already gas, so filaments run THROUGH the clouds instead of
                // hanging in empty space.
                ridge *= Mathf.Clamp01(cloud + cloud3 * 0.7f);

                Color c = baseCol + hueA * cloud * 0.16f + hueB * cloud2 * 0.12f + hueC * cloud3 * 0.10f;
                // Filaments emit hotter than the gas around them — pushed toward white rather than tinted,
                // so they read as brightness instead of another colour in the mix.
                c += Color.Lerp(hueA, Color.white, 0.55f) * ridge * 0.30f;
                px[y * w + x] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }
        // Faint baked background stars (small/dim; the sparkle comes from the near-layer sprites).
        for (int i = 0; i < 700; i++)
        {
            int x = rng.Next(w), y = rng.Next(h);
            float b = 0.3f + (float)rng.NextDouble() * 0.45f;
            px[y * w + x] = new Color(b, b, Mathf.Min(1f, b + 0.1f), 1f);
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    Texture2D GenerateStars(int seed, System.Random rng, float brightness)
    {
        int w = 512, h = 512;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
        for (int i = 0; i < 420; i++)
        {
            int x = rng.Next(w), y = rng.Next(h);
            float b = brightness * (0.4f + (float)rng.NextDouble() * 0.5f);
            var tint = StarTint(rng);
            px[y * w + x] = new Color(tint.r * b, tint.g * b, tint.b * b, 1f);
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    Texture2D GenerateGalaxyRound(int seed)
    {
        int s = 128; var tex = NewTex(s);
        var px = new Color[s * s]; Vector2 c = new Vector2(s / 2f, s / 2f);
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            float r = new Vector2(x - c.x, y - c.y).magnitude / (s * 0.5f);
            float a = Mathf.Clamp01(1f - r); a = a * a * 0.85f;
            px[y * s + x] = new Color(0.8f, 0.82f, 1f, a);
        }
        tex.SetPixels(px); tex.Apply(); return tex;
    }

    // One distant spiral galaxy, varied per call — arm count, wind direction, tightness and bulge size.
    // Same logarithmic-spiral maths as the deep-view galaxy (GalaxySpiralVisual), at a fraction of the
    // resolution, because these are a few dozen pixels across on screen.
    // All variation comes from `rng`, which is the map's single seeded stream — so the deep field is
    // reproducible for a given map seed without needing a per-galaxy seed of its own.
    Texture2D GenerateDistantSpiral(System.Random rng)
    {
        int s = 128; var tex = NewTex(s);
        var px = new Color[s * s];
        Vector2 c = new Vector2(s / 2f, s / 2f);

        int arms = 2 + rng.Next(0, 3);                              // 2..4
        float hand = rng.NextDouble() < 0.5 ? 1f : -1f;
        float tight = 3.0f + (float)rng.NextDouble() * 3.5f;
        float bulge = 0.14f + (float)rng.NextDouble() * 0.16f;
        float sharp = 1.0f + (float)rng.NextDouble() * 1.6f;

        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                Vector2 d = new Vector2(x - c.x, y - c.y);
                float r = d.magnitude / (s * 0.5f);
                if (r > 1f) { px[y * s + x] = new Color(0, 0, 0, 0); continue; }

                float ang = Mathf.Atan2(d.y, d.x);
                float phase = ang * hand - Mathf.Log(Mathf.Max(0.03f, r)) * tight;
                float arm = Mathf.Pow(Mathf.Cos(phase * arms) * 0.5f + 0.5f, sharp);

                float fade = 1f - Mathf.SmoothStep(0.55f, 1f, r);
                float core = Mathf.Exp(-(r * r) / (bulge * bulge));
                float a = Mathf.Clamp01(arm * fade * 0.7f + core);

                // Cool arms, warm core — the same temperature contrast the deep view uses.
                Color col = Color.Lerp(new Color(0.72f, 0.82f, 1f), new Color(1f, 0.92f, 0.78f), core);
                px[y * s + x] = new Color(col.r, col.g, col.b, a * 0.85f);
            }

        tex.SetPixels(px); tex.Apply(); return tex;
    }

    Texture2D GenerateCloud(int seed)
    {
        int s = 128; var tex = NewTex(s);
        var px = new Color[s * s]; Vector2 c = new Vector2(s / 2f, s / 2f);
        float so = seed % 500 * 0.01f;
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            float r = new Vector2(x - c.x, y - c.y).magnitude / (s * 0.5f);
            float n = FBm(x * 0.05f + so, y * 0.05f + so, 4);
            float a = Mathf.Clamp01((1f - r)) * n;
            px[y * s + x] = new Color(1f, 1f, 1f, a * a);
        }
        tex.SetPixels(px); tex.Apply(); return tex;
    }

    Texture2D GenerateDot()
    {
        int s = 16; var tex = NewTex(s);
        var px = new Color[s * s]; Vector2 c = new Vector2(s / 2f, s / 2f);
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            float r = Vector2.Distance(new Vector2(x, y), c) / (s * 0.5f);
            float a = Mathf.Clamp01(1f - r);
            px[y * s + x] = new Color(1, 1, 1, a * a);
        }
        tex.SetPixels(px); tex.Apply(); return tex;
    }

    // Every texture this class generates, so a rebuild can free the previous set.
    //
    // Runtime-created textures are not collected when the GameObjects using them are destroyed — Unity
    // only reclaims assets it loaded itself. Rebuild() already destroyed the quads but left their textures
    // resident, so each "Regenerate Space" leaked a 512x512 nebula, two 512x512 star fields and a handful
    // of smaller ones. Now that each distant spiral generates its OWN texture there are ~6 more per
    // rebuild, which turns a slow leak into a noticeable one.
    readonly List<Texture2D> generated = new List<Texture2D>();
    readonly List<Material> materials = new List<Material>();

    Texture2D Track(Texture2D t)
    {
        if (t != null) generated.Add(t);
        return t;
    }

    // Both sets are freed together, and only ever AFTER the quads referencing them have been destroyed —
    // Rebuild destroys its children first, and both destructions resolve at the same end-of-frame point,
    // so nothing is ever drawn against a freed asset.
    void ReleaseGenerated()
    {
        for (int i = 0; i < generated.Count; i++)
            if (generated[i] != null) Destroy(generated[i]);
        generated.Clear();

        for (int i = 0; i < materials.Count; i++)
            if (materials[i] != null) Destroy(materials[i]);
        materials.Clear();
    }

    void OnDestroy() { ReleaseGenerated(); }

    static Texture2D NewTex(int s) => new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

    static Color RandomNebulaHue(System.Random rng)
    {
        Color[] hues =
        {
            new Color(0.4f, 0.2f, 0.8f), new Color(0.2f, 0.4f, 0.9f), new Color(0.8f, 0.25f, 0.5f),
            new Color(0.2f, 0.7f, 0.7f), new Color(0.7f, 0.4f, 0.2f), new Color(0.5f, 0.3f, 0.7f)
        };
        return hues[rng.Next(hues.Length)];
    }

    static float FBm(float x, float y, int oct)
    {
        float amp = 1, freq = 1, sum = 0, norm = 0;
        for (int i = 0; i < oct; i++) { sum += amp * Mathf.PerlinNoise(x * freq, y * freq); norm += amp; amp *= 0.5f; freq *= 2f; }
        return norm > 0 ? sum / norm : 0;
    }
}

// Subtle sparkle: gentle brightness/size pulse.
public class TwinkleStar : MonoBehaviour
{
    float phase, speed;
    Vector3 baseScale;
    MeshRenderer mr;

    // Backdrop dim multiplier, written by SpaceBackground as the wide views fade in. It has to live here
    // rather than being applied to the material directly, because this component rewrites its own alpha
    // every frame and would overwrite anything set from outside.
    [HideInInspector] public float dim = 1f;

    public void Init(float phase, float speed, Vector3 baseScale)
    {
        this.phase = phase; this.speed = speed; this.baseScale = baseScale;
        mr = GetComponent<MeshRenderer>();
    }

    void Update()
    {
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed + phase);
        transform.localScale = baseScale * (0.8f + 0.35f * t);   // small variation
        if (mr != null) { var c = mr.material.color; c.a = (0.55f + 0.45f * t) * dim; mr.material.color = c; }
    }
}

// Small shooting star: a short streak that fades and self-destructs.
public class ShootingStar : MonoBehaviour
{
    Vector3 velocity; float life, age; MeshRenderer mr;

    public void Init(Vector3 velocity, float life)
    {
        this.velocity = velocity; this.life = life;
        mr = GetComponent<MeshRenderer>();
        float ang = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        transform.localRotation = Quaternion.Euler(0, 0, ang);
        transform.localScale = new Vector3(transform.localScale.x * 5f, transform.localScale.y * 0.3f, 1f);
    }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        transform.localPosition += velocity * (Time.unscaledDeltaTime / life);
        if (mr != null) { var c = mr.material.color; c.a = Mathf.Clamp01(1f - age / life); mr.material.color = c; }
        if (age >= life) Destroy(gameObject);
    }

    // A shooting star spawns every 5-13 seconds and destroys itself, but its runtime material would
    // outlive it — Unity does not free materials with their GameObject. Over a long session that is
    // hundreds of orphans, and unlike the backdrop's own quads there is no rebuild to sweep them up.
    void OnDestroy()
    {
        if (mr != null && mr.material != null) Destroy(mr.material);
    }
}
