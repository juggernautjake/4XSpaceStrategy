using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE EMPIRE'S MARK
//
// The player picks a symbol alongside their two colours, and it is the same choice: a mark and a
// livery are one identity, and splitting them across two screens would let a player build a crest
// they never see against the colours it will actually be worn in.
//
// The ten symbols ship as REGION MASKS rather than as pictures (tools/make-civ-symbols.mjs):
//
//     red channel    belongs to the primary colour
//     green channel  belongs to the secondary colour
//     alpha          how much of the pixel the mark covers
//
// This composites the two chosen colours through those channels once per colour change and caches the
// result. Pre-rendering instead would mean ten symbols across twelve primaries and twelve secondaries
// — 1,440 textures to ship and keep in step. This ships ten and multiplies.
//
// Anti-aliasing survives for free: a pixel on the edge of a stroke carries partial channel values and
// composites to a partial colour at partial alpha, which is exactly what it should look like.
// ============================================================================================
public static class CivEmblem
{
    /// The symbol files, in the order the chooser shows them. Names match tools/make-civ-symbols.mjs.
    public static readonly string[] Symbols =
    {
        "Chevron", "Star", "Delta", "Orbit", "Cross",
        "Talon", "Eye", "Anvil", "Sunburst", "Shield",
    };

    const string ResourceDir = "SpaceAssets/Symbols/Symbol_";

    /// Which symbol the player chose. Defaults to the first, so an empire always has a mark.
    public static int SymbolIndex { get; private set; }

    public static string SymbolName =>
        Symbols[Mathf.Clamp(SymbolIndex, 0, Symbols.Length - 1)];

    public static event System.Action OnChanged;

    public static void SetSymbol(int index)
    {
        int i = Mathf.Clamp(index, 0, Symbols.Length - 1);
        if (i == SymbolIndex) return;
        SymbolIndex = i;
        OnChanged?.Invoke();
    }

    public static void Reset()
    {
        SymbolIndex = 0;
        composed.Clear();
        OnChanged?.Invoke();
    }

    // ---- Compositing ------------------------------------------------------------------------------

    struct Key : System.IEquatable<Key>
    {
        public int symbol; public Color a, b;
        public bool Equals(Key o) => symbol == o.symbol && a == o.a && b == o.b;
        public override bool Equals(object o) => o is Key k && Equals(k);
        public override int GetHashCode() => symbol * 397 ^ a.GetHashCode() ^ b.GetHashCode();
    }

    static readonly Dictionary<Key, Texture2D> composed = new Dictionary<Key, Texture2D>();
    static readonly Dictionary<int, Texture2D> masks = new Dictionary<int, Texture2D>();

    /// The chosen mark in the chosen colours. Null only if the symbol textures are missing from
    /// Resources, in which case callers keep whatever they were drawing before.
    public static Texture2D Current => Build(SymbolIndex, CivLivery.Primary, CivLivery.Secondary);

    /// Any symbol in any colours — the chooser draws every swatch through this.
    public static Texture2D Preview(int symbol, Color primary, Color secondary)
        => Build(symbol, primary, secondary);

    static Texture2D Build(int symbol, Color primary, Color secondary)
    {
        symbol = Mathf.Clamp(symbol, 0, Symbols.Length - 1);
        var key = new Key { symbol = symbol, a = primary, b = secondary };
        if (composed.TryGetValue(key, out var hit) && hit != null) return hit;

        var mask = Mask(symbol);
        if (mask == null) return null;

        // Through a blit, not GetPixels directly — these PNGs carry no .meta in the repo, so Unity
        // imports them with Read/Write Disabled and reading one straight would throw. See
        // CivLivery.ReadableCopy.
        var readable = CivLivery.ReadableCopy(mask);
        if (readable == null) return null;

        var src = readable.GetPixels32();
        var dst = new Color32[src.Length];

        for (int i = 0; i < src.Length; i++)
        {
            float r = src[i].r / 255f;      // primary share
            float g = src[i].g / 255f;      // secondary share
            float a = src[i].a / 255f;

            // Straight sum, not a lerp. The two regions never overlap in a generated mask, so one of
            // the two shares is always zero and a sum is both correct and cheaper. Where a stroke edge
            // does put a little of each in one pixel — which happens where the regions touch — summing
            // blends them, which is what the eye expects at a seam.
            float cr = primary.r * r + secondary.r * g;
            float cg = primary.g * r + secondary.g * g;
            float cb = primary.b * r + secondary.b * g;

            dst[i] = new Color32(
                (byte)(Mathf.Clamp01(cr) * 255f),
                (byte)(Mathf.Clamp01(cg) * 255f),
                (byte)(Mathf.Clamp01(cb) * 255f),
                (byte)(Mathf.Clamp01(a) * 255f));
        }

        var tex = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixels32(dst);
        tex.Apply(false, false);

        composed[key] = tex;
        return tex;
    }

    static Texture2D Mask(int symbol)
    {
        if (masks.TryGetValue(symbol, out var hit) && hit != null) return hit;

        var t = Resources.Load<Texture2D>(ResourceDir + Symbols[symbol]);
        if (t == null)
        {
            Debug.LogWarning($"CivEmblem: no symbol texture at Resources/{ResourceDir}{Symbols[symbol]}. " +
                             "Run tools/make-civ-symbols.mjs.");
            return null;
        }
        masks[symbol] = t;
        return t;
    }

    /// Drop every composited texture — called when the colours change, since each cached image is
    /// keyed on the pair that made it and the old ones will never be asked for again.
    public static void InvalidateColours() => composed.Clear();
}
