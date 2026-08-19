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
// The art is reduced to a LUMINANCE RATIO — each texel's brightness over the tile's own mean — and
// that ratio multiplies TerrainColorMap.Get(type). The mean of the pattern is 1 by construction, so
// a patch of terrain still averages to exactly the colour it has always been: TerrainColorMap stays
// the single source of truth, every view keeps agreeing, and no biome can drift hue because somebody
// redrew a tile. What the art contributes is the thing it is actually good for — the structure.
//
// The tiles are authored IN their palette colour anyway, so the folder is browsable, but that is a
// courtesy to whoever opens it rather than something the renderer depends on. Tint a tile any hue
// you like and the map will not change; only its light and dark will.
//
// ---- ONE MATERIAL LIBRARY, NOT FORTY-ONE DRAWINGS ---------------------------------------------
//
// Every type has its own file, and the set is built to two rules that make it read as one library:
//
//   SEAMLESS BY CONSTRUCTION. Each tile is periodic across 16 texels on both axes — value noise on an
//       integer lattice whose period divides 16, cellular distance measured the short way round the
//       torus, ripples at whole wavenumbers. That is what licenses the per-tile random offset in
//       SurfaceTextureRenderer: any window into the tile is a valid window, so a continent of one
//       biome is a continuous material rather than the same stamp repeated in a grid.
//
//   ONE CONTRAST LADDER. A tile's grain strength is the relative standard deviation of that luminance
//       ratio, and it may only take one of five values — from 0.060 for bare fractured rock down to
//       0.020 for snow and ice. Materials differ by how rough the surface genuinely is, not by how
//       much a given tile wanted attention. The set this replaced ran from 0.010 (beach: no visible
//       grain at all) to 0.409 (CrackedGround: a black net stamped over the colour), a forty-fold
//       spread, which is why no two biomes read as belonging to the same world.
//
// Energy is spread across all four octaves whose periods divide 16, deliberately. Pattern() box-filters
// the art down to `scale` when a big world can only afford 4 texels per cell, and grain built purely
// from single-texel speckle averages away to nothing there — the biggest worlds in the game would be
// the flattest-looking ones, which is exactly backwards.
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

    /// Which art file carries the pattern for a type. One file per type — nothing borrows any more —
    /// but the FAMILY each was generated from is shared, and that is where the consistency comes from:
    /// mountain, Highlands, Hills and rocky are all cellular rock at different roughnesses; Canyon and
    /// Badlands are the same rock with bedding planes through it; forest, jungle, Taiga and swamp are
    /// one clumped-canopy field. Two biomes that are the same material look like it, and two that are
    /// not, do not.
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

            // ---- the sixteen that once borrowed a relative's grain ----
            //
            // Canyon took rock, Dunes took sand, Storm took the plainest noise there was — so four
            // different kinds of rock all wore the same rock. They have their own now. The brief for
            // every tile in the set is the same: fine grain first, structure only as a hint, and any
            // direction (bedding, dune ripples, cloud bands) kept well under the noise it sits in. A
            // bold motif at this scale does not read as material — it reads as a symbol stamped once
            // per cell and repeated across a continent, which is what the map looked like before.
            case TerrainType.Volcano:       return "Volcano";
            case TerrainType.LavaRock:      return "LavaRock";
            case TerrainType.Island:        return "Island";
            case TerrainType.Hills:         return "Hills";
            case TerrainType.Crater:        return "Crater";
            case TerrainType.Highlands:     return "Highlands";
            case TerrainType.Canyon:        return "Canyon";
            case TerrainType.Badlands:      return "Badlands";
            case TerrainType.River:         return "River";
            case TerrainType.Reef:          return "Reef";
            case TerrainType.Glacier:       return "Glacier";
            case TerrainType.Dunes:         return "Dunes";
            case TerrainType.SaltFlat:      return "SaltFlat";
            case TerrainType.Wasteland:     return "Wasteland";
            case TerrainType.GasClouds:     return "GasClouds";
            case TerrainType.Storm:         return "Storm";
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
