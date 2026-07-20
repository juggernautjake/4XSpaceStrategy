using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The one place runtime space visuals get their materials and rings.
//
// SystemVisualizer and GalaxyLOD each carried their own private copy of the same unlit-material fallback
// chain and the same MakeRing, which had already drifted apart in width defaults. Both now call here.
//
// The other half of this file is FADING, which the render-tier crossfade needs. The project has no custom
// shaders and no Resources folder, so everything is built from Shader.Find against stock URP/builtin
// shaders. An unlit material is OPAQUE by default and ignores the alpha you set on its colour, so a
// material that will be faded has to be given a blend mode up front. TWO of the constructors here produce
// something that honours alpha: `Unlit(..., fadeable: true)` and `Additive(...)`. Only a plain
// `Unlit(c)` is opaque, and fading one of those silently does nothing.
public static class SpaceMaterials
{
    static Shader unlitShader;
    static Shader spriteShader;

    // Three-step fallback: URP first (this project renders through URP), then builtin unlit, then the
    // sprite shader, which is present in every project and is alpha-blended already.
    public static Shader UnlitShader()
    {
        if (unlitShader != null) return unlitShader;
        unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
        return unlitShader;
    }

    public static Shader SpriteShader()
    {
        if (spriteShader != null) return spriteShader;
        spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null) spriteShader = UnlitShader();
        return spriteShader;
    }

    /// An unlit material in `c`. Pass fadeable:true for anything the tier crossfade will dissolve.
    ///
    /// `keepDepth` keeps Z-writing on for a transparent material — the combination a solid object that
    /// merely needs to FADE wants. Without it a black hole's event horizon stops occluding, and its own
    /// accretion rings (same render queue, sorted by distance) draw straight through it.
    public static Material Unlit(Color c, bool fadeable = false, bool keepDepth = false)
    {
        var m = new Material(UnlitShader());
        ApplyColor(m, c);
        if (fadeable) MakeTransparent(m, keepDepth);
        return m;
    }

    /// Write a colour into whichever property this shader actually exposes.
    public static void ApplyColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);
        m.color = c;
    }

    /// Switch a material to alpha blending so its colour's alpha is honoured.
    ///
    /// URP's Unlit shader decides opaque-vs-transparent from _Surface plus a keyword plus the blend
    /// factors plus the render queue — setting the colour alone leaves it opaque and the fade does
    /// nothing. Every write is guarded, because the same call has to survive falling back to
    /// Unlit/Color (which has none of these) and to Sprites/Default (already transparent).
    public static void MakeTransparent(Material m, bool keepDepth = false)
    {
        if (m == null) return;
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // 0 opaque, 1 transparent
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // 0 alpha blend
        if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", keepDepth ? 1 : 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    /// Additive blending — for glows, sparkles and accretion light, which should brighten what is behind
    /// them rather than occlude it. Stacked additive sprites are what makes a star field read as hot.
    public static Material Additive(Color c)
    {
        var m = new Material(SpriteShader());
        ApplyColor(m, c);
        if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }

    /// A flat looped ring in the XZ plane — the galaxy and every orbit are viewed top-down.
    public static LineRenderer MakeRing(Transform parent, string name, float radius, Color color,
                                        float width, int seg = 72, bool additive = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = Mathf.Max(3, seg);
        lr.material = additive ? Additive(Color.white) : new Material(SpriteShader());
        lr.startColor = lr.endColor = color;
        lr.startWidth = lr.endWidth = Mathf.Max(0.03f, width);
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.View;
        int n = lr.positionCount;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        return lr;
    }

    /// Strip the collider a Unity primitive ships with. Proxies use one shared collider on their root
    /// instead of one per sphere, so every child sphere has to give its own up.
    ///
    /// `keepCollider` opts out, and it matters more than it looks. Destroy() is DEFERRED to end of frame,
    /// so a caller that strips the collider here and then asks `GetComponent<SphereCollider>()` in the
    /// same frame still gets the doomed one back, keeps it, and is left with nothing once the frame ends.
    /// That is exactly how the system-view black hole silently became unclickable.
    public static GameObject Primitive(PrimitiveType t, Transform parent, string name,
                                       bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name;
        go.transform.SetParent(parent, false);
        var col = go.GetComponent<Collider>();
        if (col != null && !keepCollider) Object.Destroy(col);
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return go;
    }
}

// Keeps a LineRenderer's width from collapsing to a hairline as the camera pulls away.
//
// A LineRenderer's width is in WORLD units, so its on-screen thickness falls off with distance like
// everything else. That is right for scenery and wrong for an INDICATOR: the coloured ring marking which
// empire holds a system is information, and at the galaxy's widest zoom it was thinning to a single
// shimmering pixel — present, technically, and unreadable.
//
// This holds a minimum ANGULAR width instead: the ring is never allowed to be thinner than
// `minScreenFraction` of the viewport height, whatever the distance. Close up the authored world width
// wins and nothing changes; far out this takes over and the ring stays legible.
[DisallowMultipleComponent]
public class MinScreenWidthLine : MonoBehaviour
{
    /// The authored world width, used whenever it is already thick enough on screen.
    public float baseWidth = 0.1f;

    /// Floor on apparent thickness, as a fraction of viewport height. ~0.0022 is about 2.4px at 1080p.
    public float minScreenFraction = 0.0022f;

    LineRenderer lr;
    Camera cam;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        if (baseWidth <= 0f && lr != null) baseWidth = lr.startWidth;
    }

    void LateUpdate()
    {
        if (lr == null) return;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        float dist = Vector3.Distance(cam.transform.position, transform.position);
        if (dist <= 0.001f) return;

        // World size that subtends `minScreenFraction` of the viewport at this distance. The vertical
        // FOV gives the full world height visible at `dist`; a fraction of that is the floor.
        float worldPerViewport = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float floorWorld = worldPerViewport * minScreenFraction;

        // LineRenderer width is multiplied by the transform's scale, and these rings hang off a parent
        // the zoom ramp scales — so a width computed in world units has to be divided back out, or the
        // floor would be applied twice over and the ring would balloon as you pull away.
        float s = Mathf.Max(0.0001f, transform.lossyScale.x);
        float floorLocal = floorWorld / s;

        lr.startWidth = lr.endWidth = Mathf.Max(baseWidth, floorLocal);
    }
}

// Fades a whole subtree by alpha, as one unit.
//
// Captures every Renderer and LineRenderer under itself once, remembers each one's authored colour, and
// scales only the alpha from there — so a fade never loses the original tint and fading out then back in
// is lossless. Renderers and LineRenderers need different treatment: a LineRenderer's visible colour comes
// from startColor/endColor, not from its material, so setting the material alpha on one does nothing.
//
// Written to be cheap enough to run every frame on every proxy in the galaxy: the capture happens once,
// and SetAlpha early-outs when the value hasn't moved.
[DisallowMultipleComponent]
public class FadeGroup : MonoBehaviour
{
    struct Entry
    {
        public Renderer rend;
        public Material mat;
        public LineRenderer line;
        public Color a, b;          // authored colours (material colour, or line start/end)
        public bool depthWriting;   // built with ZWrite on — must drop it while translucent
    }

    readonly List<Entry> entries = new List<Entry>();

    // Every per-renderer material this group instantiated, including the ones it does not fade (rings
    // driven by DiscBeaming, LineRenderers faded through start/end colour). Kept separately from
    // `entries` because ownership and fading are different concerns — the previous version destroyed only
    // what it faded, which left every MakeRing material alive forever.
    readonly List<Material> owned = new List<Material>();

    // Rings that drive their own colour every frame (relativistic beaming on an accretion disc) cannot be
    // faded by writing startColor/endColor — colorGradient overrides both, so the write would be discarded
    // on the next LateUpdate. Those components take a fade multiplier instead.
    DiscBeaming[] beamers;

    bool captured;
    float applied = -1f;

    /// Snapshot the subtree's authored colours. Call after the visuals are built; calling twice is safe
    /// but the SECOND call would capture already-faded colours, so it no-ops once captured.
    public void Capture()
    {
        if (captured) return;
        captured = true;
        entries.Clear();
        owned.Clear();
        beamers = GetComponentsInChildren<DiscBeaming>(true);

        // ONE pass over every renderer, so ownership is recorded for all of them and no category is
        // silently skipped. .material instantiates a per-renderer copy — which is what we want, since
        // fading one proxy must not fade every other proxy sharing the same material asset.
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            var m = r.material;
            if (m != null) owned.Add(m);

            if (r is LineRenderer lr)
            {
                // Self-colouring rings fade through their DiscBeaming multiplier instead — their
                // colorGradient overrides start/end colour, so writing those here would be discarded.
                if (lr.GetComponent<DiscBeaming>() != null) continue;
                entries.Add(new Entry { line = lr, a = lr.startColor, b = lr.endColor });
                continue;
            }

            if (m == null) continue;

            // Deliberately does NOT touch blend state.
            //
            // It used to force every material here through MakeTransparent, on the reasoning that an
            // opaque material ignores alpha. That was destructive in two directions. It overwrote
            // _DstBlend=One back to OneMinusSrcAlpha, silently converting every ADDITIVE material —
            // the spiral disc, the sparkles, the jets, every accretion ring — into plain alpha blending,
            // so the things whose entire look is "glow" stopped glowing. And it cleared _ZWrite on the
            // event horizon, which is opaque on purpose, so the black hole's own rings drew through it.
            //
            // Blend mode is now decided once, at creation, by whoever knows what the surface is meant to
            // be. Anything that needs to fade must be built fadeable (SpaceMaterials.Unlit) or additive
            // (SpaceMaterials.Additive) — both already honour alpha.
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            // A material built with keepDepth (the event horizon) writes depth. That is right when it is
            // opaque and wrong the moment it is not: a nearly-invisible surface that still writes depth
            // punches a hole in everything drawn behind it. SetAlpha turns it off below full opacity.
            bool depth = m.HasProperty("_ZWrite") && m.GetFloat("_ZWrite") > 0.5f;
            entries.Add(new Entry { rend = r, mat = m, a = c, b = c, depthWriting = depth });
        }
    }

    public void SetAlpha(float alpha)
    {
        if (!captured) Capture();
        alpha = Mathf.Clamp01(alpha);
        if (Mathf.Abs(alpha - applied) < 0.002f) return;
        applied = alpha;

        if (beamers != null)
            for (int i = 0; i < beamers.Length; i++)
                if (beamers[i] != null) beamers[i].fade = alpha;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.line != null)
            {
                var s = e.a; s.a = e.a.a * alpha;
                var t = e.b; t.a = e.b.a * alpha;
                e.line.startColor = s;
                e.line.endColor = t;
            }
            else if (e.mat != null)
            {
                var c = e.a; c.a = e.a.a * alpha;
                SpaceMaterials.ApplyColor(e.mat, c);

                // A translucent surface must stop writing depth, or it occludes what it no longer covers.
                // Without this the black hole's event horizon — deliberately depth-writing so its own
                // rings don't draw through it — stays a depth-writing sphere all the way down to alpha 0,
                // cutting an invisible hole out of the deep-view spiral behind it during the crossfade.
                if (e.depthWriting && e.mat.HasProperty("_ZWrite"))
                    e.mat.SetInt("_ZWrite", alpha > 0.995f ? 1 : 0);
            }
        }
    }

    // Runtime-created materials are NOT freed when their GameObject is destroyed — Unity only collects
    // assets it loaded itself. Every proxy rebuild allocates a fresh material per sphere, per ring and per
    // renderer-local copy `.material` makes, so without this a few galaxy reloads leak hundreds of them.
    void OnDestroy()
    {
        for (int i = 0; i < owned.Count; i++)
            if (owned[i] != null) Destroy(owned[i]);
        owned.Clear();
        entries.Clear();
    }
}
