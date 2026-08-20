using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// PLAYER COLOURS ON THE SHIPS THEMSELVES
//
// Every hull is generated with its civilization's two accent colours painted on NAMED SURFACES —
// "on some armour panels", "on small trim, seams and lights" — deliberately kept far apart in hue
// from each other and from the base material (see tools/civ-colors.json). That separation was the
// whole point of the prompt design: it makes the accents KEYABLE. A pixel is part of the livery if
// its hue sits within a few degrees of an accent's key hue and it is saturated enough to be paint
// rather than shading, and everything else is hull.
//
// So the player picks two colours when they pick their race, and this repaints exactly those pixels.
//
// ---- WHY THE CPU, AND NOT A SHADER ---------------------------------------------------------
//
// A mask texture plus a URP shader that tints two channels is the textbook answer and is cheaper at
// draw time. It is not the right answer HERE, for a reason that has nothing to do with elegance:
// there is no Unity in this environment, so a shader cannot be compiled, and a shader that has never
// been compiled is not a feature — it is a file that will turn every ship in the fleet magenta the
// first time anybody presses play.
//
// Recolouring the texture in C# is testable by reading it, costs nothing at draw time, and happens
// once. The work is 512x512 per hull class, on a colour change only — a change the player makes at
// the species screen and then, in most games, never again. Caching is per class and per colour pair,
// so switching back to a scheme already used is free.
//
// ---- LUMINANCE IS PRESERVED, HUE IS REPLACED -----------------------------------------------
//
// The naive version writes the chosen colour flat into every masked pixel, and what comes back is a
// ship with two solid plastic stickers on it: every panel line, rivet, scorch mark and bevel inside
// the accent areas is gone, because all of that detail lived in the LIGHT and the flat fill threw the
// light away. So each masked pixel keeps its own brightness and takes only the new hue: paint over
// the panel, not over the panelling.
// ============================================================================================
public static class CivLivery
{
    // ---- The key hues, mirroring tools/civ-colors.json -------------------------------------------
    //
    // Duplicated from the JSON rather than parsed from it, because the JSON is an offline art-pipeline
    // file that is not shipped in Resources, and five pairs of numbers is a smaller thing to keep in
    // step than a loader plus a parse failure mode. If the palette moves, these move with it — the
    // civ-colors.json entry names each hue in a comment beside it for exactly that reason.
    struct Keys { public float primary, secondary; }

    static readonly Keys[] keys =
    {
        new Keys { primary = 217f, secondary =  32f },   // Terran     — Cobalt / Ember
        new Keys { primary =  40f, secondary = 305f },   // Aquarii    — Amber / Orchid
        new Keys { primary =  20f, secondary = 188f },   // Pyrothian  — Magma / Cryo
        new Keys { primary = 292f, secondary =  42f },   // Cryithn    — Aurora Violet / Gold
        new Keys { primary = 285f, secondary =  40f },   // Sylvan     — Blossom Violet / Pollen
    };

    /// A pixel joins a mask only if it clears these floors AND lands within the tolerance of a key hue.
    /// The same numbers as civ-colors.json's maskRules, and for the same reason: below them a pixel is
    /// shadow or bare metal that happens to lean warm, not paint.
    const float MinSaturation = 0.35f, MinValue = 0.12f, KeyTolerance = 28f;

    // ---- The player's chosen colours -------------------------------------------------------------

    /// Set when the player has actually chosen. Until then the art keeps the colours it was generated
    /// with, which is the correct default: the fleet already looks right.
    public static bool Chosen { get; private set; }

    public static Color Primary { get; private set; } = new Color(0.18f, 0.44f, 0.88f);
    public static Color Secondary { get; private set; } = new Color(0.95f, 0.66f, 0.12f);

    public static event System.Action OnChanged;

    public static void Set(Color primary, Color secondary)
    {
        Primary = primary; Secondary = secondary; Chosen = true;
        OnChanged?.Invoke();
    }

    /// Back to the colours the art was generated with — the fleet's own factory livery.
    public static void ClearChoice()
    {
        Chosen = false;
        OnChanged?.Invoke();
    }

    public static void Reset()
    {
        Chosen = false;
        Primary = new Color(0.18f, 0.44f, 0.88f);
        Secondary = new Color(0.95f, 0.66f, 0.12f);
        cache.Clear();
        OnChanged?.Invoke();
    }

    // ---- Repainting ------------------------------------------------------------------------------

    /// Keyed on the source texture AND the colour pair, so two hulls sharing an atlas share a repaint,
    /// and going back to a scheme already used costs nothing.
    struct CacheKey : System.IEquatable<CacheKey>
    {
        public int texture; public int civ; public Color a, b;
        public bool Equals(CacheKey o) => texture == o.texture && civ == o.civ && a == o.a && b == o.b;
        public override bool Equals(object o) => o is CacheKey k && Equals(k);
        public override int GetHashCode() => texture * 397 ^ civ ^ a.GetHashCode() ^ b.GetHashCode();
    }

    static readonly Dictionary<CacheKey, Texture2D> cache = new Dictionary<CacheKey, Texture2D>();

    /// Repaint every material on this model into the player's colours. Silent no-op when the player has
    /// not chosen, or when the civ index is not one this knows about.
    public static void Apply(GameObject model, int civIndex)
    {
        if (!Chosen || model == null) return;
        if (civIndex < 0 || civIndex >= keys.Length) return;

        foreach (var mr in model.GetComponentsInChildren<MeshRenderer>())
            foreach (var mat in mr.materials)
            {
                var src = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                        : mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex")
                        : null;
                if (src == null) continue;

                var painted = Repaint(src, civIndex);
                if (painted == null) continue;

                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", painted);
                else mat.SetTexture("_MainTex", painted);
            }
    }

    static Texture2D Repaint(Texture src, int civIndex)
    {
        var key = new CacheKey { texture = src.GetInstanceID(), civ = civIndex, a = Primary, b = Secondary };
        if (cache.TryGetValue(key, out var hit) && hit != null) return hit;

        var readable = ReadableCopy(src);
        if (readable == null) return null;

        var px = readable.GetPixels32();
        var k = keys[civIndex];

        Color.RGBToHSV(Primary, out float ph, out float ps, out _);
        Color.RGBToHSV(Secondary, out float sh, out float ss, out _);

        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < MinSaturation || v < MinValue) continue;      // shadow or bare hull: leave it

            float hue, sat;
            if (HueDistance(h * 360f, k.primary) <= KeyTolerance) { hue = ph; sat = ps; }
            else if (HueDistance(h * 360f, k.secondary) <= KeyTolerance) { hue = sh; sat = ss; }
            else continue;                                        // not livery: leave it

            // The pixel keeps its own VALUE — the panel lines, rivets, scorches and bevels inside the
            // accent area all live in that channel, and a flat fill would erase every one of them.
            // Saturation is taken from the chosen colour, scaled by how saturated the original was, so
            // a washed-out edge stays washed out instead of snapping to full paint.
            float blended = sat * Mathf.Clamp01(s / Mathf.Max(0.001f, 1f));
            var outc = Color.HSVToRGB(hue, Mathf.Clamp01(Mathf.Lerp(blended, sat, 0.5f)), v);
            px[i] = new Color32((byte)(outc.r * 255), (byte)(outc.g * 255), (byte)(outc.b * 255), c.a == 0 ? (byte)255 : (byte)(c.a * 255));
        }

        readable.SetPixels32(px);
        readable.Apply(false, false);
        cache[key] = readable;
        return readable;
    }

    static float HueDistance(float a, float b)
    {
        float d = Mathf.Abs(Mathf.Repeat(a - b, 360f));
        return d > 180f ? 360f - d : d;
    }

    /// A CPU-readable copy of a texture, via a RenderTexture blit.
    ///
    /// NOT GetPixels on the original, and that is not defensiveness — it is the difference between
    /// working and throwing. Textures that arrive at runtime are not guaranteed to be marked readable:
    /// the ship albedos come out of .glb files via gltfast, and the symbol masks come out of Resources
    /// with whatever import settings Unity generated for a PNG that has no .meta in the repo, which
    /// means Read/Write Disabled. GetPixels on either throws.
    ///
    /// Blitting through a RenderTexture goes via the GPU and works on any texture the GPU can sample,
    /// which by definition includes every texture already being drawn. Public because CivEmblem needs
    /// exactly the same thing for exactly the same reason.
    public static Texture2D ReadableCopy(Texture src)
    {
        if (src == null || src.width <= 0 || src.height <= 0) return null;

        var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                                            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tex.Apply(false, false);
            tex.wrapMode = src.wrapMode;
            tex.filterMode = src.filterMode;
            return tex;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"CivLivery: could not read a ship texture for repainting ({e.Message}). " +
                             "That hull keeps its generated colours.");
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    // ---- A palette to choose from ----------------------------------------------------------------
    //
    // A fixed set rather than a colour wheel, and that is a design decision rather than a shortcut.
    // Every one of these is saturated enough to survive being written into a texture at the original
    // pixel's brightness — pick a near-black or a near-white on a wheel and the livery simply vanishes
    // into the hull, and the player is left thinking the feature is broken. They are also far enough
    // apart from each other that two empires never look the same across a map.
    public static readonly (string name, Color color)[] Palette =
    {
        ("Cobalt",    new Color32(0x2F, 0x6F, 0xE0, 0xFF)),
        ("Azure",     new Color32(0x24, 0xB4, 0xE8, 0xFF)),
        ("Teal",      new Color32(0x18, 0xC0, 0xA8, 0xFF)),
        ("Jade",      new Color32(0x2E, 0xC4, 0x5E, 0xFF)),
        ("Lime",      new Color32(0x9C, 0xD1, 0x2A, 0xFF)),
        ("Gold",      new Color32(0xF2, 0xA8, 0x1E, 0xFF)),
        ("Ember",     new Color32(0xF2, 0x6A, 0x1E, 0xFF)),
        ("Crimson",   new Color32(0xE0, 0x30, 0x3C, 0xFF)),
        ("Rose",      new Color32(0xF0, 0x5C, 0x9A, 0xFF)),
        ("Orchid",    new Color32(0xD2, 0x4F, 0xC8, 0xFF)),
        ("Violet",    new Color32(0x8B, 0x5C, 0xF0, 0xFF)),
        ("Ice",       new Color32(0xBF, 0xE4, 0xF5, 0xFF)),
    };
}
