using System.Collections.Generic;
using UnityEngine;

// Procedural, per-map space backdrop: layered nebula + starfield with parallax, twinkling stars,
// occasional shooting stars and a few distant galaxies. Generated once from a seed and kept constant
// until regenerated. Can be toggled off, or switched to a flat solid colour of the dev's choosing.
// Everything is parented to the camera so it always fills the view and sits behind the system, and
// it is deliberately dark so planets, rings and text stay readable.
public class SpaceBackground : MonoBehaviour
{
    public static SpaceBackground Instance;

    Camera cam;
    Transform rootFar, rootMid, rootNear;
    GameObject solidQuad;
    readonly List<TwinkleStar> twinkles = new List<TwinkleStar>();

    public int Seed { get; private set; } = 12345;
    public bool Enabled { get; private set; } = true;
    public bool SolidMode { get; private set; } = false;
    public Color SolidColor { get; private set; } = new Color(0.02f, 0.03f, 0.06f);

    float shootingTimer;
    Vector3 lastCamPos;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("SpaceBackground");
        Instance = go.AddComponent<SpaceBackground>();
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

    // ---- Shared unlit, double-sided material (Sprites/Default culls nothing) ----
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
        return distance * 2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 3f; // ×3 headroom for parallax
    }

    public void SetSeed(int seed) { Seed = seed; }

    public void Regenerate()
    {
        Seed = (Seed * 1103515245 + 12345) & 0x7fffffff;
        Rebuild();
    }

    public void SetEnabled(bool on) { Enabled = on; ApplyVisibility(); }
    public void SetSolidMode(bool on) { SolidMode = on; ApplyVisibility(); }
    public void SetSolidColor(Color c)
    {
        SolidColor = c;
        if (solidQuad != null) solidQuad.GetComponent<MeshRenderer>().material.color = c;
        if (cam != null) cam.backgroundColor = c;
    }

    void ApplyVisibility()
    {
        bool space = Enabled && !SolidMode;
        if (rootFar) rootFar.gameObject.SetActive(space);
        if (rootMid) rootMid.gameObject.SetActive(space);
        if (rootNear) rootNear.gameObject.SetActive(space);
        if (solidQuad) solidQuad.SetActive(Enabled && SolidMode);
    }

    public void Rebuild()
    {
        if (cam == null) cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (cam == null) return;

        // Clear previous
        foreach (Transform c in transform) Destroy(c.gameObject);
        twinkles.Clear();

        transform.SetParent(cam.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        var rng = new System.Random(Seed);

        var nebula = GenerateNebula(Seed, rng);
        var starsFar = GenerateStars(Seed + 7, rng, 900, 0.6f);
        var starsMid = GenerateStars(Seed + 31, rng, 500, 0.9f);
        var galaxyTex = GenerateGalaxy(Seed + 91);

        rootFar = new GameObject("Far").transform; rootFar.SetParent(transform, false);
        rootMid = new GameObject("Mid").transform; rootMid.SetParent(transform, false);
        rootNear = new GameObject("Near").transform; rootNear.SetParent(transform, false);

        float dFar = 950f, dMid = 720f, dNear = 520f;
        MakeQuad(rootFar, "Nebula", dFar, FovSize(dFar), nebula, Color.white);
        MakeQuad(rootFar, "StarsFar", dFar - 5f, FovSize(dFar), starsFar, Color.white);
        MakeQuad(rootMid, "StarsMid", dMid, FovSize(dMid), starsMid, Color.white);

        // Distant galaxies scattered on the far layer.
        int galaxies = 3;
        for (int i = 0; i < galaxies; i++)
        {
            float gs = FovSize(dFar) * 0.10f;
            var g = MakeQuad(rootFar, "Galaxy", dFar - 8f, gs, galaxyTex, new Color(1, 1, 1, 0.5f));
            float span = FovSize(dFar) * 0.35f;
            g.transform.localPosition += new Vector3((float)(rng.NextDouble() * 2 - 1) * span, (float)(rng.NextDouble() * 2 - 1) * span, 0);
            g.transform.localRotation = Quaternion.Euler(0, 0, (float)(rng.NextDouble() * 360));
        }

        // Bright twinkling stars on the near layer.
        var starDot = GenerateDot();
        int twinkleCount = 46;
        float nearSpan = FovSize(dNear) * 0.45f;
        for (int i = 0; i < twinkleCount; i++)
        {
            float s = FovSize(dNear) * (0.004f + (float)rng.NextDouble() * 0.006f);
            var dot = MakeQuad(rootNear, "Twinkle", dNear, s, starDot, StarTint(rng));
            dot.transform.localPosition += new Vector3((float)(rng.NextDouble() * 2 - 1) * nearSpan, (float)(rng.NextDouble() * 2 - 1) * nearSpan, 0);
            var tw = dot.AddComponent<TwinkleStar>();
            tw.Init((float)(rng.NextDouble() * 6.28), 0.6f + (float)rng.NextDouble() * 1.6f, dot.transform.localScale);
            twinkles.Add(tw);
        }

        // Solid-colour fallback quad.
        solidQuad = MakeQuad(transform, "SolidColor", dFar + 20f, FovSize(dFar + 20f), Texture2D.whiteTexture, SolidColor);

        ApplyVisibility();
        if (cam != null && SolidMode) cam.backgroundColor = SolidColor;

        // Optional: if the dev dropped in a CC0 skybox material, register it (visible when the
        // camera's Clear Flags are set to Skybox). Harmless otherwise.
        var sky = AssetIntegration.LoadSkybox();
        if (sky != null) RenderSettings.skybox = sky;
    }

    void LateUpdate()
    {
        if (cam == null) return;

        // Parallax: drift layers opposite to camera pan, scaled by depth.
        Vector3 delta = cam.transform.position - lastCamPos;
        lastCamPos = cam.transform.position;
        float dr = Vector3.Dot(delta, cam.transform.right);
        float du = Vector3.Dot(delta, cam.transform.up);
        Drift(rootFar, dr, du, 0.015f, 60f);
        Drift(rootMid, dr, du, 0.035f, 90f);
        Drift(rootNear, dr, du, 0.06f, 120f);

        // Occasional shooting star.
        shootingTimer -= Time.unscaledDeltaTime;
        if (shootingTimer <= 0f && Enabled && !SolidMode && rootNear != null)
        {
            shootingTimer = Random.Range(4f, 11f);
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
        var dot = GenerateDot();
        var q = MakeQuad(rootNear, "Shooting", dNear, FovSize(dNear) * 0.02f, dot, new Color(0.8f, 0.9f, 1f, 1f));
        float span = FovSize(dNear) * 0.4f;
        Vector3 start = new Vector3(-span, Random.Range(-span * 0.3f, span * 0.5f), dNear);
        q.transform.localPosition = start;
        var ss = q.AddComponent<ShootingStar>();
        ss.Init(new Vector3(1f, Random.Range(-0.4f, -0.1f), 0f).normalized * span * 2.2f, 1.1f);
    }

    static Color StarTint(System.Random rng)
    {
        float r = (float)rng.NextDouble();
        if (r < 0.6f) return Color.white;
        if (r < 0.8f) return new Color(0.7f, 0.8f, 1f);   // blue-white
        return new Color(1f, 0.85f, 0.7f);                // warm
    }

    // ---- Texture generation ----
    Texture2D GenerateNebula(int seed, System.Random rng)
    {
        int w = 512, h = 512;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];

        Color baseCol = new Color(0.015f, 0.02f, 0.04f);
        Color hueA = RandomNebulaHue(rng);
        Color hueB = RandomNebulaHue(rng);
        float so = seed % 1000 * 0.01f;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float n = FBm(u * 3f + so, v * 3f + so, 5);
                float n2 = FBm(u * 6f + so + 40f, v * 6f + so + 40f, 4);
                float cloud = Mathf.Clamp01((n - 0.45f) * 2.2f);
                float cloud2 = Mathf.Clamp01((n2 - 0.55f) * 2.4f);
                Color c = baseCol;
                c += hueA * cloud * 0.22f;
                c += hueB * cloud2 * 0.16f;
                px[y * w + x] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }

        // Dense faint background stars baked in.
        int starCount = 900;
        for (int i = 0; i < starCount; i++)
        {
            int x = rng.Next(w), y = rng.Next(h);
            float b = 0.4f + (float)rng.NextDouble() * 0.6f;
            px[y * w + x] = new Color(b, b, Mathf.Min(1f, b + 0.1f), 1f);
        }

        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    Texture2D GenerateStars(int seed, System.Random rng, int distanceHint, float brightness)
    {
        int w = 512, h = 512;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);

        int count = 500;
        for (int i = 0; i < count; i++)
        {
            int x = rng.Next(w), y = rng.Next(h);
            float b = brightness * (0.5f + (float)rng.NextDouble() * 0.5f);
            var tint = StarTint(rng);
            px[y * w + x] = new Color(tint.r * b, tint.g * b, tint.b * b, 1f);
        }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    Texture2D GenerateGalaxy(int seed)
    {
        int s = 128;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        Vector2 c = new Vector2(s / 2f, s / 2f);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                Vector2 d = new Vector2(x - c.x, y - c.y);
                float r = d.magnitude / (s * 0.5f);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * 0.8f;
                px[y * s + x] = new Color(0.8f, 0.8f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    Texture2D _dot;
    Texture2D GenerateDot()
    {
        if (_dot != null) return _dot;
        int s = 16;
        _dot = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        Vector2 c = new Vector2(s / 2f, s / 2f);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), c) / (s * 0.5f);
                float a = Mathf.Clamp01(1f - r);
                px[y * s + x] = new Color(1, 1, 1, a * a);
            }
        _dot.SetPixels(px); _dot.Apply();
        return _dot;
    }

    static Color RandomNebulaHue(System.Random rng)
    {
        Color[] hues =
        {
            new Color(0.4f, 0.2f, 0.8f), new Color(0.2f, 0.4f, 0.9f), new Color(0.8f, 0.25f, 0.5f),
            new Color(0.2f, 0.7f, 0.7f), new Color(0.7f, 0.4f, 0.2f)
        };
        return hues[rng.Next(hues.Length)];
    }

    static float FBm(float x, float y, int oct)
    {
        float amp = 1, freq = 1, sum = 0, norm = 0;
        for (int i = 0; i < oct; i++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
            norm += amp; amp *= 0.5f; freq *= 2f;
        }
        return norm > 0 ? sum / norm : 0;
    }
}

// Animates a star's brightness/size for a twinkle effect.
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
        transform.localScale = baseScale * (0.6f + 0.6f * t);
        if (mr != null)
        {
            var c = mr.material.color; c.a = 0.4f + 0.6f * t; mr.material.color = c;
        }
    }
}

// Streaks a shooting star across the near layer, then fades and self-destructs.
public class ShootingStar : MonoBehaviour
{
    Vector3 velocity;
    float life, age;
    MeshRenderer mr;

    public void Init(Vector3 velocity, float life)
    {
        this.velocity = velocity; this.life = life;
        mr = GetComponent<MeshRenderer>();
        // Point the quad along its travel direction and stretch it into a streak.
        float ang = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        transform.localRotation = Quaternion.Euler(0, 0, ang);
        transform.localScale = new Vector3(transform.localScale.x * 6f, transform.localScale.y * 0.4f, 1f);
    }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        transform.localPosition += velocity * (Time.unscaledDeltaTime / life);
        if (mr != null) { var c = mr.material.color; c.a = Mathf.Clamp01(1f - age / life); mr.material.color = c; }
        if (age >= life) Destroy(gameObject);
    }
}
