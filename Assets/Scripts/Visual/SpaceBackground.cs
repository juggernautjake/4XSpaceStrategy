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
    Texture2D _dot, _galaxyRound, _galaxySpiral, _cloud;

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

    static Material MakeMat(Texture2D tex, Color tint)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        m.color = tint;
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

        transform.SetParent(cam.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        var rng = new System.Random(Seed);

        var nebula = GenerateNebula(Seed, rng);
        var starsFar = GenerateStars(Seed + 7, rng, 0.5f);
        var starsMid = GenerateStars(Seed + 31, rng, 0.75f);
        _galaxyRound = GenerateGalaxyRound(Seed + 91);
        _galaxySpiral = GenerateGalaxySpiral(Seed + 133);
        _cloud = GenerateCloud(Seed + 200);
        _dot = GenerateDot();

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
        int galaxies = 8;
        for (int i = 0; i < galaxies; i++)
        {
            bool spiral = rng.NextDouble() < 0.5;
            float gs = FovSize(dFar) * (0.05f + (float)rng.NextDouble() * 0.06f);
            var g = MakeQuad(rootFar, spiral ? "Spiral" : "Galaxy", dFar - 8f, gs,
                             spiral ? _galaxySpiral : _galaxyRound, new Color(1, 1, 1, 0.42f));
            Scatter(g, rng, FovSize(dFar) * 0.38f);
            g.transform.localRotation = Quaternion.Euler(0, 0, (float)(rng.NextDouble() * 360));
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
        float dr = Vector3.Dot(delta, cam.transform.right);
        float du = Vector3.Dot(delta, cam.transform.up);
        Drift(rootFar, dr, du, 0.015f, 60f);
        Drift(rootMid, dr, du, 0.035f, 90f);
        Drift(rootNear, dr, du, 0.06f, 120f);

        shootingTimer -= Time.unscaledDeltaTime;
        if (shootingTimer <= 0f && rootNear != null)
        {
            shootingTimer = Random.Range(5f, 13f);
            SpawnShootingStar();
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
                Color c = baseCol + hueA * cloud * 0.16f + hueB * cloud2 * 0.12f + hueC * cloud3 * 0.10f;
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

    Texture2D GenerateGalaxySpiral(int seed)
    {
        int s = 128; var tex = NewTex(s);
        var px = new Color[s * s]; Vector2 c = new Vector2(s / 2f, s / 2f);
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            Vector2 d = new Vector2(x - c.x, y - c.y);
            float r = d.magnitude / (s * 0.5f);
            float ang = Mathf.Atan2(d.y, d.x);
            // Two-arm logarithmic spiral.
            float arm = Mathf.Cos(2f * (ang - r * 5f));
            float core = Mathf.Clamp01(1f - r) * 0.9f;
            float arms = Mathf.Clamp01(arm) * Mathf.Clamp01(1f - r) * 0.7f;
            float a = Mathf.Clamp01(core * 0.5f + arms);
            px[y * s + x] = new Color(0.85f, 0.85f, 1f, a * 0.8f);
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

    public void Init(float phase, float speed, Vector3 baseScale)
    {
        this.phase = phase; this.speed = speed; this.baseScale = baseScale;
        mr = GetComponent<MeshRenderer>();
    }

    void Update()
    {
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed + phase);
        transform.localScale = baseScale * (0.8f + 0.35f * t);   // small variation
        if (mr != null) { var c = mr.material.color; c.a = 0.55f + 0.45f * t; mr.material.color = c; }
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
}
