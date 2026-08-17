using System.Collections.Generic;

// ============================================================================================
// A byte-per-cell grid, packed small enough to live in a JSON save.
//
// Run-length encoded, then base64'd. RLE because terrain is the ideal case for it — a biome map is
// large contiguous regions of one value, not noise — and measured against generated worlds it comes
// out 3x smaller on a 100x50 moon and 11-14x smaller on a 400x200 planet, where the runs are longer.
// A whole galaxy's terrain lands around 1-3 MB of text, which is the price of a save that reloads as
// the world the player left rather than as whatever the current generator would build.
//
// Base64 because JsonUtility can only carry a string, and a byte array written as a JSON number list
// would cost three to four characters per byte instead of four per three.
//
// The format is deliberately dull: pairs of (value, count), count 1..255. A run longer than 255 is
// split across pairs. Worst case — every cell different from its neighbour — is twice the raw size,
// which on the largest grid in the game is 400 KB, so there is no case where this needs a fallback
// path to raw bytes.
// ============================================================================================
public static class GridCodec
{
    /// RLE + base64. Null or empty input gives an empty string, which Decode reads back as null.
    public static string Encode(byte[] cells)
    {
        if (cells == null || cells.Length == 0) return "";

        var packed = new List<byte>(cells.Length / 4 + 8);
        int i = 0;
        while (i < cells.Length)
        {
            byte v = cells[i];
            int run = 1;
            while (i + run < cells.Length && cells[i + run] == v && run < 255) run++;
            packed.Add(v);
            packed.Add((byte)run);
            i += run;
        }
        return System.Convert.ToBase64String(packed.ToArray());
    }

    /// Unpacks to exactly `expectedLength` bytes, or null if the string is empty, malformed, or
    /// describes a different number of cells.
    ///
    /// The length check is the load-bearing part. A save whose world has since been resized — the Dev
    /// sandbox can do it, and so can a change to how mass maps to a grid — would otherwise smear the
    /// old terrain diagonally across the new grid. Returning null there puts the loader back on the
    /// generator, which is the honest answer when the stored grid is not a grid of this world.
    public static byte[] Decode(string s, int expectedLength)
    {
        if (string.IsNullOrEmpty(s) || expectedLength <= 0) return null;

        byte[] packed;
        try { packed = System.Convert.FromBase64String(s); }
        catch (System.FormatException) { return null; }
        if (packed.Length < 2 || (packed.Length & 1) != 0) return null;

        var outp = new byte[expectedLength];
        int w = 0;
        for (int i = 0; i < packed.Length; i += 2)
        {
            byte v = packed[i];
            int run = packed[i + 1];
            if (run == 0 || w + run > expectedLength) return null;   // truncated, padded or corrupt
            for (int k = 0; k < run; k++) outp[w++] = v;
        }
        return w == expectedLength ? outp : null;
    }
}
