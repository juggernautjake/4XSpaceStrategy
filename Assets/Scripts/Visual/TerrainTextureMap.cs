using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// PER-BIOME TILE TEXTURE — the grain that turns a flat colour into ground.
//
// The map used to draw one flat texel per cell, so a continent of Grassland was one unbroken slab
// of #85C257. This supplies each terrain type with a small repeating pattern instead, taken from the
// 16x16 tile art in Resources/SpaceAssets/Biomes.
//
// ---- WHY THE ART SUPPLIES THE PATTERN AND NOT THE COLOUR -------------------------------------
//
// The obvious thing is to blit the art tile straight onto the map and let it bring its own colours.
// Rendered side by side against the palette, that breaks two biomes outright: the mountain tile is
// BROWN where TerrainColorMap.Mountains is grey, and the crystal-field tile is magenta where
// CrystalField is pale cyan. A player reading the map would see dirt where the legend, the 3D globe,
// the moon thumbnails and the loading-screen morph all still said stone.
//
// So the art is reduced to a LUMINANCE RATIO — each texel's brightness over the tile's own mean —
// and that ratio multiplies TerrainColorMap.Get(type). The mean of the pattern is 1 by construction,
// so a patch of terrain still averages to exactly the colour it has always been: TerrainColorMap
// stays the single source of truth, every view keeps agreeing, and no biome can drift hue because
// somebody redrew a tile. What the art contributes is the thing it is actually good for — the
// structure. The cracks in CrackedGround and the sparkle in CrystalField come through intact.
//
// It also means all FORTY-ONE terrain types get texture from twenty-six files. A type with no art of
// its own borrows a relative's pattern (Hills takes the grass grain, Canyon takes rock) and keeps its
// own colour, which is what makes Hills and Grassland still readable as different things.
//
// ---- OPTIONAL, LIKE EVERY OTHER DROP-IN ART HOOK ---------------------------------------------
// Loaded through Resources, the same convention as AssetIntegration. If the folder is missing the
// grain is simply null and every caller falls back to the flat colour — the map then renders exactly
// as it did before this file existed. Nothing here can make a world fail to draw.
// ============================================================================================
public static class TerrainTextureMap
{
    const string Folder = "SpaceAssets/Biomes/";

    /// The art is authored at this size. Everything below downsamples FROM it, never up.
    public const int ArtSize = 16;

    /// Which art file carries the pattern for a type. Types not listed here have art of their own
    /// under the same name; see PatternFile.
    ///
    /// The donors are chosen by SURFACE, not by mood: Canyon and Badlands are broken rock, so they
    /// take the rock grain; Dunes is wind-blown sand, so it takes the desert grain. GasClouds and
    /// Storm take the plainest noise there is, because a gas giant has no ground to have a texture.
    static string PatternFile(TerrainType t)
    {
        switch (t)
        {
            // ---- types with their own tile ----
            case TerrainType.Plains:        return "Plains";
            case TerrainType.Mountains:     return "mountain";
            case TerrainType.Forest:        return "forest";
            case TerrainType.Ice:           return "ice";
            case TerrainType.MagmaField:    return "MagmaField";
            case TerrainType.Desert:        return "desert";
            case TerrainType.Ocean:         return "ocean";
            case TerrainType.Barren:        return "barren";
            case TerrainType.Grassland:     return "grass";
            case TerrainType.Jungle:        return "jungle";
            case TerrainType.Swamp:         return "swamp";
            case TerrainType.Savanna:       return "Savanna";
            case TerrainType.Steppe:        return "Steppe";
            case TerrainType.Tundra:        return "tundra";
            case TerrainType.Taiga:         return "Taiga";
            case TerrainType.Beach:         return "beach";
            case TerrainType.Lake:          return "lake";
            case TerrainType.Snow:          return "snow";
            case TerrainType.FrozenSea:     return "frozen_ocean";
            case TerrainType.AshWaste:      return "AsheWaste";
            case TerrainType.ObsidianFlat:  return "ObsidianFlat";
            case TerrainType.GeyserField:   return "GeyserField";
            case TerrainType.CrackedGround: return "CrackedGround";
            case TerrainType.CrystalField:  return "CrystalField";
            case TerrainType.MetallicCrust: return "MetallicCrust";

            // ---- types borrowing a relative's grain, keeping their own colour ----
            case TerrainType.Volcano:       return "MagmaField";
            case TerrainType.LavaRock:      return "ObsidianFlat";
            case TerrainType.Island:        return "grass";
            case TerrainType.Hills:         return "grass";
            case TerrainType.Crater:        return "rocky";
            case TerrainType.Highlands:     return "rocky";
            case TerrainType.Canyon:        return "rocky";
            case TerrainType.Badlands:      return "rocky";
            case TerrainType.River:         return "lake";
            case TerrainType.Reef:          return "ocean";
            case TerrainType.Glacier:       return "ice";
            case TerrainType.Dunes:         return "desert";
            case TerrainType.SaltFlat:      return "barren";
            case TerrainType.Wasteland:     return "barren";
            case TerrainType.GasClouds:     return "Plains";
            case TerrainType.Storm:         return "Plains";
        }
        return null;
    }

    // Cache the raw 16x16 ratios per FILE (several types share one), and the downsampled patterns per
    // (file, size). Both are tiny and both survive the life of the process, because the art does.
    static readonly Dictionary<string, float[]> artCache = new Dictionary<string, float[]>();
    static readonly Dictionary<string, float[]> sizedCache = new Dictionary<string, float[]>();

    /// The luminance ratio field of a type's pattern, at `size` x `size`, row 0 at the BOTTOM to match
    /// the texture the renderer is writing into. Mean 1. Null if the art could not be loaded.
    ///
    /// `size` must divide ArtSize (1, 2, 4, 8 or 16); anything else is rounded down to one that does,
    /// so the downsample below is an exact box filter rather than a resample with its own artefacts.
    public static float[] Pattern(TerrainType type, int size)
    {
        string file = PatternFile(type);
        if (file == null || size < 1) return null;

        size = Snap(size);
        string key = file + "#" + size;
        if (sizedCache.TryGetValue(key, out var cached)) return cached;

        float[] art = Art(file);
        if (art == null) { sizedCache[key] = null; return null; }

        float[] outp;
        if (size == ArtSize) outp = art;
        else
        {
            // Box-average, not point-sample. Point-sampling a 16x16 noise tile down to 4x4 throws away
            // fifteen texels in sixteen and turns a crack pattern into unrelated speckle; averaging keeps
            // the tile's structure and its mean, which is the thing that has to stay 1.
            int block = ArtSize / size;
            outp = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float sum = 0f;
                    for (int by = 0; by < block; by++)
                        for (int bx = 0; bx < block; bx++)
                            sum += art[(y * block + by) * ArtSize + (x * block + bx)];
                    outp[y * size + x] = sum / (block * block);
                }
        }

        sizedCache[key] = outp;
        return outp;
    }

    /// The largest divisor of ArtSize that is <= n. Keeps the box filter exact.
    public static int Snap(int n)
    {
        int best = 1;
        for (int s = 1; s <= ArtSize; s *= 2) if (s <= n) best = s;
        return best;
    }

    static float[] Art(string file)
    {
        if (artCache.TryGetValue(file, out var cached)) return cached;

        // The tiles ship named "grass_16x16"; a redraw dropped in as plain "grass" is just as welcome.
        // Resources.Load wants the name WITHOUT the extension, so both spellings are tried rather than
        // baking the artist's suffix into forty-one switch cases.
        //
        // `== null` rather than `??` on purpose: UnityEngine.Object overloads the equality operator and
        // the null-coalescing operator does not go through it.
        var tex = Resources.Load<Texture2D>($"{Folder}{file}_{ArtSize}x{ArtSize}");
        if (tex == null) tex = Resources.Load<Texture2D>(Folder + file);

        float[] ratio = null;

        if (tex != null && (tex.width != ArtSize || tex.height != ArtSize))
            Debug.LogWarning($"TerrainTextureMap: '{Folder}{file}' is {tex.width}x{tex.height}, not " +
                             $"{ArtSize}x{ArtSize}. Falling back to a flat colour for the biomes using it.");

        // A texture imported without Read/Write throws on GetPixels32 rather than returning null, and a
        // missing import setting must not take a planet's map down with it.
        if (tex != null && tex.width == ArtSize && tex.height == ArtSize)
        {
            try
            {
                var px = tex.GetPixels32();
                ratio = new float[ArtSize * ArtSize];
                float mean = 0f;
                for (int i = 0; i < ratio.Length; i++)
                {
                    // Rec. 601 luma: the art is flat-shaded pixel work, so perceived brightness is what
                    // the pattern is made of, and a plain RGB average would read the blue channel as
                    // brightly as the green one.
                    ratio[i] = (0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b) / 255f;
                    mean += ratio[i];
                }
                mean /= ratio.Length;
                if (mean < 0.001f) ratio = null;                       // a black tile carries no pattern
                else for (int i = 0; i < ratio.Length; i++) ratio[i] /= mean;
            }
            catch (UnityException)
            {
                Debug.LogWarning($"TerrainTextureMap: '{Folder}{file}' is not readable — tick Read/Write " +
                                 "Enabled on its import settings. Falling back to a flat colour.");
                ratio = null;
            }
        }

        artCache[file] = ratio;
        return ratio;
    }

    /// Drops every cached pattern. For the Dev sandbox, which can reimport art without a domain reload.
    public static void Invalidate() { artCache.Clear(); sizedCache.Clear(); }
}
