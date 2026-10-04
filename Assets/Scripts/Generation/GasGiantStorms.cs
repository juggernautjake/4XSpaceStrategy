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
    /// A great spot: an oval on the (u, v) surface, wider than it is tall because the band it sits in is.
    public struct Spot
    {
        public float u, v;     // centre, in 0..1 surface coordinates
        public float ru, rv;   // radii, same units
        /// A slight lean: the centre line drifts by this fraction of rv per ru across the storm.
        public float skew;
    }

    // ---- THE SHAPE OF A STORM: THE BELT SWELLS --------------------------------------------------
    //
    // "Storm cells need to look like they are just wider, more rounded sections of the storm belt they
    // generate in. The edges look pinched off on either side, the top and bottom curve is far too clean,
    // as if a very smooth slice was taken out of them, and it has a very stretched football shape."
    //
    // All three complaints had one cause: the storm was a SHAPE CUT INTO the belt. A pointed lens
    // (pinched tips, football outline) inside a ruled pale collar (the clean slice) is a sticker, however
    // well it is drawn.
    //
    // So a storm is no longer a shape at all. It is a DISTORTION OF THE BELT. Across a storm's width the
    // latitude the band test reads is remapped so the belt's own half-height grows from its normal value
    // to the storm's: everything inside the oval samples the belt, and the ground just outside it is
    // pushed back onto the belt's edge and the pale zone beyond. Three things follow for free:
    //
    //   * The storm's edge IS the belt's edge, so it carries the same ragged moisture jitter every band
    //     edge has — no clean curve anywhere.
    //   * Where the oval is no taller than the belt there is no distortion, so the belt flows into the
    //     storm through rounded shoulders rather than pinching to a point.
    //   * The pale zones and the next belts bow around the swelling and close up again past it, which is
    //     the "flows around it" the original request asked for.
    //
    // Proportioned like the Great Red Spot: about one and a half times as wide as it is tall.
    public const float SkewMax = 0.10f;

    /// How far beyond the storm's edge the lanes are still bowed, as a multiple of how much the storm
    /// swells past the belt. Larger is a gentler, wider flow-around.
    public const float FlowReach = 1.6f;

    /// The band-edge jitter: the band test reads (lat + moisture * MoistJitter). PlanetTerrainGenerator
    /// uses this same constant, and spots are snapped to where the jitter puts the belt on average.
    public const float MoistJitter = 0.15f;

    /// Half the height of a dark belt, in v (0..1 surface) units. A belt is half a band cycle tall in
    /// latitude, and latitude is twice v.
    public static float BeltHalfHeight => 0.25f / BandCycles * 0.5f;

    /// Three is the ceiling. Jupiter has one famous spot and a handful of white ovals; a giant covered
    /// in great spots has no great spot.
    public const int MaxSpots = 3;

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
            //
            // ONLY BELTS THAT ARE ON THE DISC. At 3.5 cycles a fourth "belt" would centre past the pole,
            // and the old clamp parked those spots at the pole itself. And snapped to where the belt
            // ACTUALLY is: the band test adds moisture * MoistJitter to latitude, which on average moves
            // every belt equatorward by half that — enough, before, to sit spots on their belt's edge.
            int belts = Mathf.FloorToInt(BandCycles - 0.75f) + 1;
            int band = Mathf.Min(belts - 1, Mathf.FloorToInt(Next() * belts));
            float lat = Mathf.Clamp01((band + 0.75f) / BandCycles - 0.5f * MoistJitter);
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
            float aspect = Mathf.Lerp(1.3f, 1.7f, Next());   // Great Red Spot proportions, not a football

            float su = Next();
            float sskew = (Next() * 2f - 1f) * SkewMax;
            v = Mathf.Clamp(v, 0.06f, 0.94f);   // never so close to a pole that it wraps over it
            // Never so tall that it reaches across the equator (latitude is mirrored there, so the far
            // half would fold back on itself) or off a pole — and always taller than the belt, or there
            // is no storm to see.
            float hb = BeltHalfHeight;
            rv = Mathf.Min(rv, Mathf.Abs(v - 0.5f) - hb * 0.5f, v - 0.03f, 0.97f - v);
            rv = Mathf.Max(rv, hb * 1.3f);

            // NO TWO STORMS IN ONE PLACE. Two spots in the same belt whose ovals overlap would stack their
            // pulls and fold the bands into slivers. Move the newcomer round to the far side of the world —
            // the draws are already made, so the stream every later spot reads is unchanged.
            // Up to three tries a third of the way round each time, re-checking every earlier spot, so a
            // newcomer moved off one storm cannot land on another.
            for (int attempt = 0; attempt < 3 && Overlaps(i, su, v, rv, Mathf.Min(rv * aspect, 0.18f)); attempt++)
                su = Mathf.Repeat(su + 1f / 3f, 1f);

            cachedSpots[i] = new Spot
            {
                u = su,
                v = v,
                rv = rv,
                skew = sskew,
                // CAPPED. rv * aspect at the top of both ranges was 0.53 — a spot slightly WIDER than the
                // whole world, which the contact sheet duly drew as a band-coloured stripe with a pale
                // outline. The real Great Red Spot spans about an ninth of Jupiter's circumference; 0.15
                // is a radius, so this allows up to three tenths, which is still a monster.
                ru = Mathf.Min(rv * aspect, 0.18f),
            };
        }
    }

    /// Would a storm at (u, v) with these radii overlap any of the first `count` spots already built?
    static bool Overlaps(int count, float u, float v, float rv, float ru)
    {
        for (int j = 0; j < count; j++)
        {
            var o = cachedSpots[j];
            float gap = Mathf.Abs(u - o.u); if (gap > 0.5f) gap = 1f - gap;
            if (Mathf.Abs(v - o.v) < rv + o.rv && gap < ru + o.ru) return true;
        }
        return false;
    }

    /// How far to move the latitude the band test reads at (u, v) so the belt swells into this storm:
    /// the returned value is ADDED to v. Zero away from the storm and wherever the storm is no taller than
    /// its belt.
    ///
    /// Inside the oval, |dv| in [0, storm half-height] is squeezed onto [0, belt half-height] — the whole
    /// interior reads as belt. Outside it, the first FlowReach * (swell) of ground is pulled back by up to
    /// the swell, smoothly easing to nothing — the pale zone and the next lanes bow around the storm. The
    /// two halves meet exactly at the oval's edge, so the map is continuous and the edge is wherever the
    /// band's own jitter puts it.
    ///
    /// LONGITUDE WRAPS and latitude does not — the same asymmetry every other surface pass uses.
    public static float Swell(in Spot s, float u, float v)
    {
        float du = u - s.u;
        if (du > 0.5f) du -= 1f; else if (du < -0.5f) du += 1f;
        float a = du / Mathf.Max(0.0001f, s.ru);
        if (a <= -1f || a >= 1f) return 0f;

        float hb = BeltHalfHeight;
        float hO = s.rv * Mathf.Sqrt(1f - a * a);          // the oval's half-height at this longitude
        float extra = hO - hb;
        if (extra <= 0f) return 0f;                         // the belt already covers it: no swelling

        float centre = s.v + s.skew * s.rv * a;
        float dv = v - centre;
        float adv = Mathf.Abs(dv);
        float side = dv < 0f ? -1f : 1f;

        float target;
        // Squared, not linear: the storm's middle maps to the belt's CENTRE, far from either edge, so the
        // band-edge jitter can rough up the rim but cannot punch pale holes in the heart of the storm.
        // Still exactly hb at the rim, so the inside and outside meet without a seam.
        if (adv <= hO) { float q = adv / hO; target = hb * q * q; }
        else
        {
            float reach = extra * FlowReach;
            if (adv >= hO + reach) return 0f;
            float t = 1f - (adv - hO) / reach;
            target = adv - extra * t * t * (3f - 2f * t);   // smoothstep: no visible ring where it ends
        }
        return centre + side * target - v;
    }
}
