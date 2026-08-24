using UnityEngine;

// ============================================================================================
// THE GREAT RED SPOT
//
// "Lets give Gas Giants the ability to make large storm cells in the grid map out of the storm grids
// (which should show up on the solar system view of the planet). The storm cells should vary in size
// and should generate within the bands of Storm that the grid generates. The GasClouds grids and
// other Storm Grids bands should flow around these large storm cells. Think Jupiters Great Red Spot."
//
// ---- WHAT WAS THERE BEFORE, AND WHY IT WAS NOT THIS -------------------------------------------
//
// PlanetTerrainGenerator.GasGiant had one line with the right intent and the wrong mechanism:
//
//     if (elev > 0.78f) return TerrainType.Storm;      // great-spot style storm
//
// `elev` on a gas giant is fractal noise. Thresholding fractal noise does not give you a spot, it
// gives you SPECKLE — a scatter of small ragged patches wherever the field happens to peak, with no
// size, no shape and no relationship to the bands they sit in. It read as static over the deck.
//
// A spot is not a threshold. It is an OBJECT: it has a position, a size, an aspect ratio, and — the
// part that actually sells it — the cloud lanes around it are deflected by its presence. All four of
// those have to be stated, so this file states them.
//
// ---- THE THREE RULES THE REQUEST ASKS FOR -----------------------------------------------------
//
//   IN A STORM BAND. A spot's latitude is snapped to the centre of a band the generator would already
//   have drawn as Storm. A great spot is a storm that grew inside the belt it belongs to; one sitting
//   in the middle of a pale cloud zone would read as a sticker.
//
//   VARYING IN SIZE. Widely — one world's spot is a sixth of its circumference, another's is a
//   freckle. A range this broad is deliberate: if every spot were the same size it would read as a
//   feature of the ENGINE rather than of the world.
//
//   THE BANDS FLOW AROUND IT. See the halo below. This is the expensive one and it is the one that
//   matters: a spot with the bands running straight through it looks painted on, and a spot with the
//   bands bowing round it looks like weather.
//
// ---- DETERMINISTIC, AND STORED NOWHERE ---------------------------------------------------------
//
// Derived from the body's own seed and id with the same hash shape GasGiantPalette.Of uses, so a
// world's spots survive a save, a reload and a sandbox regeneration without occupying a field — and
// two giants in one system cannot roll the same spots by sharing a random stream.
// ============================================================================================
public static class GasGiantStorms
{
    /// A great spot: an ellipse on the (u, v) surface, wider than it is tall because the band it sits
    /// in is.
    public struct Spot
    {
        public float u, v;     // centre, in 0..1 surface coordinates
        public float ru, rv;   // radii, same units
    }

    /// Three is the ceiling. Jupiter has one famous spot and a handful of white ovals; a giant covered
    /// in great spots has no great spot.
    public const int MaxSpots = 3;

    /// How far outside a spot the cloud lanes are still bent, as a fraction of its own radius. The
    /// bands do not stop dead at the edge of a storm — they crowd against it and stream past.
    public const float FlowHalo = 0.55f;

    /// The pale collar a great spot sits in, as a fraction of its own radius beyond the edge.
    ///
    /// ADDED AFTER LOOKING AT THE RENDER (tools/gas-giant-check.mjs). A spot is made of Storm tiles and
    /// it is snapped into the middle of a Storm band — so on the first pass it was a dark ellipse inside
    /// a dark belt, i.e. invisible. Twelve panels of contact sheet, and the ones with three spots were
    /// indistinguishable from the ones with none.
    ///
    /// The fix is the thing Jupiter actually has: the Great Red Spot sits in a pale HOLLOW punched out
    /// of the belt around it. One ring of cloud, and the storm reads instantly.
    public const float Hollow = 0.30f;

    /// How many bands of cloud a gas giant is divided into. Must match the multiplier in
    /// PlanetTerrainGenerator.GasGiant — spots are snapped to THESE band centres, and if the two
    /// disagree every spot lands in a pale zone instead of a dark belt, which is the one placement
    /// rule the request was explicit about.
    // THREE AND A HALF, not six. At six the render came out as wood grain — twelve thin ribbons across
    // the disc, which is nothing like a gas giant. Jupiter shows seven or eight broad belts and zones.
    // Latitude is mirrored about the equator, so this is HALF the number of visible bands.
    public const float BandCycles = 3.5f;

    // ---- The per-body cache ---------------------------------------------------------------------
    //
    // Asked once per CELL while a surface is being baked — 200,000 times on a large giant — so it
    // cannot re-derive the spots per call, and it must not allocate.
    //
    // A single-entry memo rather than a Dictionary, because every caller works one body at a time:
    // the terrain bake runs a whole world before moving on, and so does each of the three renderers.
    // Two giants alternating would thrash it, and the cost of that is a recompute of four floats
    // times three — which is why this is a memo and not a correctness mechanism.
    static CelestialBody cachedBody;
    static float cachedSeed;
    static int cachedCount;
    static readonly Spot[] cachedSpots = new Spot[MaxSpots];

    /// This world's great spots. Returns how many are in `spots`, which is a shared buffer the caller
    /// must read immediately and must not hold.
    public static int Spots(CelestialBody b, out Spot[] spots)
    {
        spots = cachedSpots;
        if (b == null || b.type != CelestialBodyType.GasGiant) return 0;

        if (!ReferenceEquals(cachedBody, b) || !Mathf.Approximately(cachedSeed, b.terrainSeed))
        {
            Build(b);
            cachedBody = b;
            cachedSeed = b.terrainSeed;
        }
        return cachedCount;
    }

    /// Drop the memo. Called when a world is regenerated under the same reference — the sandbox's
    /// re-roll changes the seed, which the check above catches, but a resize does not.
    public static void Invalidate() { cachedBody = null; }

    static void Build(CelestialBody b)
    {
        uint n = (uint)((b.id * 73856093) ^ Mathf.RoundToInt(b.terrainSeed * 131f));
        float Next()
        {
            n = (n ^ (n >> 13)) * 1274126177u;
            n ^= n >> 16;
            return (n & 0xFFFFFF) / (float)0x1000000;
        }

        // 15% of giants have no great spot at all. Not zero, because "this one has none" is what makes
        // "this one has three" mean something.
        float roll = Next();
        cachedCount = roll < 0.15f ? 0 : roll < 0.55f ? 1 : roll < 0.85f ? 2 : 3;

        for (int i = 0; i < cachedCount; i++)
        {
            // ---- SNAP THE LATITUDE TO A STORM BAND ----
            //
            // The generator draws Storm where frac(lat * BandCycles) >= 0.5, so a band's dark half is
            // centred on frac == 0.75. Picking the band index and rebuilding the latitude from it puts
            // the spot in the middle of a belt rather than straddling its edge.
            //
            // `lat` runs 0 at the equator to 1 at the pole and the surface is mirrored about the
            // equator, so the last step is choosing a hemisphere.
            int band = Mathf.FloorToInt(Next() * BandCycles);
            float lat = Mathf.Clamp01((band + 0.75f) / BandCycles);
            bool north = Next() < 0.5f;
            float v = north ? 0.5f + lat * 0.5f : 0.5f - lat * 0.5f;

            // ---- SIZE ----
            //
            // The real Great Red Spot spans about a ninth of Jupiter's circumference. The range here
            // reaches well either side of that: a `rv` of 0.03 is a white oval you have to look for,
            // and 0.085 is a spot you can see from the system view without zooming.
            // A FLAT spread across the range rather than a bell. "The storm cells should vary in size"
            // is asking for variety, and a bell would put most spots at the same middling size with the
            // interesting ends rare — which is the opposite of the ask.
            // SIZED AGAINST THE BAND, not in the abstract. A band is 1/(2*BandCycles) of the disc tall
            // — about 14% at 3.5 cycles — and a spot narrower than that disappears into it whatever
            // colour it is. So the range runs from a little over one band tall to a little over two,
            // which is the proportion the Great Red Spot has against the South Equatorial Belt.
            float bandHeight = 1f / (2f * BandCycles);
            float rv = bandHeight * Mathf.Lerp(0.45f, 0.85f, Next());
            float aspect = Mathf.Lerp(1.4f, 2.2f, Next());   // always wider than tall: the band is

            cachedSpots[i] = new Spot
            {
                u = Next(),
                v = Mathf.Clamp(v, 0.06f, 0.94f),   // never so close to a pole that it wraps over it
                rv = rv,
                // CAPPED. rv * aspect at the top of both ranges was 0.53 — a spot slightly WIDER than the
                // whole world, which the contact sheet duly drew as a band-coloured stripe with a pale
                // outline. The real Great Red Spot spans about an ninth of Jupiter's circumference; 0.15
                // is a radius, so this allows up to three tenths, which is still a monster.
                ru = Mathf.Min(rv * aspect, 0.18f),
            };
        }
    }

    /// How far a point is from a spot's centre, in units of that spot's own radii: below 1 is inside
    /// the storm, 1 is exactly on its edge.
    ///
    /// LONGITUDE WRAPS and latitude does not — the same asymmetry every other surface pass uses. A spot
    /// near u = 0 has to reach round to u = 1 or it would be cut in half by the date line.
    public static float Distance(in Spot s, float u, float v)
    {
        float du = Mathf.Abs(u - s.u);
        if (du > 0.5f) du = 1f - du;
        float dv = v - s.v;

        float a = du / Mathf.Max(0.0001f, s.ru);
        float c = dv / Mathf.Max(0.0001f, s.rv);
        return Mathf.Sqrt(a * a + c * c);
    }
}
