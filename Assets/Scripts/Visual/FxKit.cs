using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// FX KIT — the shared parts every combat and ship effect is drawn with
//
// One additive HDR shader (Resources/Shaders/FxAdditive.shader, "FX/Additive"), a handful of
// procedural textures, ONE shared material per texture, and per-renderer colour through a
// MaterialPropertyBlock — so a battle with four hundred sparks in the air is four hundred renderers
// sharing five materials, not four hundred material copies nobody frees.
//
// ---- SCREEN-SIZE FLOORS ---------------------------------------------------------------------------
//
// Ships are drawn at 0.07-0.52 world units and the camera works from a moon close-up to a whole system,
// so an effect sized in world units alone is either a fireball the size of a planet up close or a
// sub-pixel speck at system zoom. Every effect takes its authored world size AND a minimum size in
// PIXELS, and draws at whichever is larger (WorldPerPixel). Up close the art wins; far out the effect
// stays readable instead of vanishing.
//
// ---- WHAT IT IS NOT -----------------------------------------------------------------------------
//
// Not a particle system. The pools live with their owners (ExplosionRenderer, ProjectileRenderer,
// ShipLights) because each has its own lifetime rules; this is only what they all draw with.
// ============================================================================================
public static class FxKit
{
    public static readonly int ColorId = Shader.PropertyToID("_Color");
    public static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    static Shader additive, sprite;
    static readonly Dictionary<Texture, Material> sharedAdditive = new Dictionary<Texture, Material>();
    static readonly Dictionary<Texture, Material> sharedBlend = new Dictionary<Texture, Material>();
    static MaterialPropertyBlock block;

    /// The additive effect shader, or the stock sprite shader if the asset is missing (which is
    /// alpha-blended rather than additive: dimmer, never broken).
    public static Shader AdditiveShader()
    {
        if (additive != null) return additive;
        additive = Resources.Load<Shader>("Shaders/FxAdditive");
        if (additive == null) additive = Shader.Find("FX/Additive");
        if (additive == null || !additive.isSupported) additive = SpriteShader();
        return additive;
    }

    /// Alpha-blended, for things that must DARKEN what is behind them: smoke, debris, soot.
    public static Shader SpriteShader()
    {
        if (sprite != null) return sprite;
        sprite = Shader.Find("Sprites/Default");
        if (sprite == null) sprite = SpaceMaterials.UnlitShader();
        return sprite;
    }

    /// One additive material per texture, shared by every renderer that draws with it. Colour goes
    /// through SetColour, never into the material.
    public static Material SharedAdditive(Texture tex)
    {
        if (tex == null) tex = Texture2D.whiteTexture;
        if (sharedAdditive.TryGetValue(tex, out var m) && m != null) return m;
        m = new Material(AdditiveShader()) { name = "FX Additive (" + tex.name + ")" };
        m.mainTexture = tex;
        if (m.HasProperty(ColorId)) m.SetColor(ColorId, Color.white);
        m.renderQueue = 3100;
        sharedAdditive[tex] = m;
        return m;
    }

    /// One alpha-blended material per texture, for smoke and debris.
    public static Material SharedBlend(Texture tex)
    {
        if (tex == null) tex = Texture2D.whiteTexture;
        if (sharedBlend.TryGetValue(tex, out var m) && m != null) return m;
        m = new Material(SpriteShader()) { name = "FX Blend (" + tex.name + ")" };
        m.mainTexture = tex;
        m.renderQueue = 3050;
        sharedBlend[tex] = m;
        return m;
    }

    /// Write a colour into one renderer's property block. HDR values (over 1) are what bloom catches.
    public static void SetColour(Renderer r, Color c)
    {
        if (r == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        block.Clear();
        block.SetColor(ColorId, c);
        r.SetPropertyBlock(block);
    }

    // ---- the camera, cached once per frame ----------------------------------------------------
    static Camera cam;
    static int camFrame = -1;
    static float pixelK;

    public static Camera Cam
    {
        get
        {
            if (camFrame != Time.frameCount || cam == null)
            {
                camFrame = Time.frameCount;
                cam = Camera.main;
                if (cam != null)
                    pixelK = cam.orthographic
                        ? 2f * cam.orthographicSize / Mathf.Max(1, Screen.height)
                        : 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            }
            return cam;
        }
    }

    /// How many world units one screen pixel spans at this point.
    public static float WorldPerPixel(Vector3 at)
    {
        var c = Cam;
        if (c == null) return 0.01f;
        if (c.orthographic) return pixelK;
        var t = c.transform;
        float depth = Vector3.Dot(at - t.position, t.forward);
        return Mathf.Max(0.01f, depth) * pixelK;
    }

    /// The larger of an authored world size and a size in pixels at this point.
    public static float Floor(Vector3 at, float worldSize, float minPixels)
        => Mathf.Max(worldSize, minPixels * WorldPerPixel(at));

    /// Screen-aligned billboard rotation, optionally spun about the view axis.
    public static Quaternion Billboard(float spinDeg = 0f)
    {
        var c = Cam;
        if (c == null) return Quaternion.Euler(0f, 0f, spinDeg);
        return c.transform.rotation * Quaternion.Euler(0f, 0f, spinDeg);
    }

    /// A camera-facing quad whose long (local Y) axis lies along `dir` as seen on screen. For bolts,
    /// tracers and streaking sparks.
    public static Quaternion Streak(Vector3 dir)
    {
        var c = Cam;
        if (c == null) return dir.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(Vector3.forward, dir) : Quaternion.identity;
        Vector3 fwd = c.transform.forward;
        Vector3 up = Vector3.ProjectOnPlane(dir, fwd);
        if (up.sqrMagnitude < 1e-10f) up = c.transform.up;
        return Quaternion.LookRotation(fwd, up.normalized);
    }

    /// Are effects worth drawing at all? Not at galaxy zoom, where the hulls themselves are gone.
    public static bool SystemView => GalaxyLOD.SystemMode;

    // ---- textures --------------------------------------------------------------------------------
    static Texture2D streak, ring, beam, shard, smoke;

    /// A soft round dot. The one every star glow already uses.
    public static Texture2D Dot() => SpaceMaterials.SoftDot();

    /// Long and thin, hot along its centre line, soft at both tips — bolts, tracers, spark streaks.
    public static Texture2D StreakTex()
    {
        if (streak != null) return streak;
        const int W = 16, H = 64;
        streak = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FxStreak" };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / W * 2f - 1f);
                float v = Mathf.Abs((y + 0.5f) / H * 2f - 1f);
                float across = Mathf.Clamp01(1f - u); across *= across;
                float along = Mathf.Clamp01(1f - v * v);
                float a = across * along;
                // A white-hot core line: the centre stays saturated however thin the quad gets.
                float core = u < 0.25f ? 1f : 0f;
                px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a + core * along * 0.4f));
            }
        streak.SetPixels32(px); streak.Apply();
        return streak;
    }

    /// A thin bright ring with a soft inner glow — shockwaves and intercept bursts.
    public static Texture2D RingTex()
    {
        if (ring != null) return ring;
        const int N = 128;
        ring = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "FxRing" };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float band = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.06f, 2f));
                float inner = r < 0.86f ? Mathf.Pow(r / 0.86f, 6f) * 0.25f : 0f;
                px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(band + inner));
            }
        ring.SetPixels32(px); ring.Apply();
        return ring;
    }

    /// The cross-section of a beam: a hard core and a soft halo, across V. Along U it is flat, so a
    /// LineRenderer can stretch it over any length.
    public static Texture2D BeamTex()
    {
        if (beam != null) return beam;
        const int W = 4, H = 64;
        beam = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FxBeam" };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = Mathf.Abs((y + 0.5f) / H * 2f - 1f);
            float halo = Mathf.Pow(Mathf.Clamp01(1f - v), 2.2f);
            float core = v < 0.18f ? 1f : 0f;
            var c = new Color(1f, 1f, 1f, Mathf.Clamp01(halo * 0.75f + core));
            for (int x = 0; x < W; x++) px[y * W + x] = c;
        }
        beam.SetPixels32(px); beam.Apply();
        return beam;
    }

    /// A hard-edged triangular shard — the pieces a collapsing shield breaks into, and hull fragments.
    /// Hard-edged on purpose: it reads as pixel-art debris rather than as a soft glow.
    public static Texture2D ShardTex()
    {
        if (shard != null) return shard;
        const int N = 16;
        shard = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "FxShard" };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool inside = x >= y / 2 && x <= N - 1 - y / 2 && y < N - 2;
                bool edge = inside && (x == y / 2 || x == N - 1 - y / 2 || y == 0);
                px[y * N + x] = inside ? new Color(1f, 1f, 1f, edge ? 1f : 0.6f) : new Color(1f, 1f, 1f, 0f);
            }
        shard.SetPixels32(px); shard.Apply();
        return shard;
    }

    /// A lumpy soft puff for smoke: a few overlapping blobs, so it does not read as a perfect disc.
    public static Texture2D SmokeTex()
    {
        if (smoke != null) return smoke;
        const int N = 64;
        smoke = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "FxSmoke" };
        var px = new Color32[N * N];
        var blobs = new[] { new Vector3(0.0f, 0.0f, 0.62f), new Vector3(-0.28f, 0.18f, 0.42f), new Vector3(0.3f, -0.12f, 0.4f), new Vector3(0.12f, 0.3f, 0.36f) };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float a = 0f;
                foreach (var b in blobs)
                {
                    float d = Mathf.Sqrt((u - b.x) * (u - b.x) + (v - b.y) * (v - b.y)) / b.z;
                    a = Mathf.Max(a, Mathf.Clamp01(1f - d));
                }
                a = a * a;
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        smoke.SetPixels32(px); smoke.Apply();
        return smoke;
    }

    /// A camera-facing quad with no collider and no shadows, parented to `parent`.
    public static Renderer MakeQuad(Transform parent, string name, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return r;
    }

    /// A pooled-style LineRenderer with the beam texture, world space, two points.
    public static LineRenderer MakeLine(Transform parent, string name, Texture tex)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.numCapVertices = 2;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.sharedMaterial = SharedAdditive(tex != null ? tex : BeamTex());
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }
}
