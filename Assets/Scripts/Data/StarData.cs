using System.Collections.Generic;
using UnityEngine;

// Physical description of a star, derived from its spectral class.
// Drives light/heat, the habitable ("Goldilocks") zone, and body habitability ratings.
public class StarData
{
    public string name;            // unique per star (assigned at generation)
    public StarType type;
    public float temperatureK;     // surface temperature (heat)
    public float luminosity;       // relative to a G-type star (= 1.0)
    public float mass = 1f;        // solar masses (drives orbital speeds)
    // How tightly the mass is packed: density = mass / (radius/RefScale)^3, ~1 for a typical star. Not an
    // independent roll — it's the RELATIONSHIP between mass and radius, so a heavy-but-small star reads as
    // dense and a light-but-big one as sparse. Stored so the Dev star editor can show and drive it. Kept in
    // sync via StarDatabase.DensityOf whenever mass or visualScale changes.
    public float density = 1f;
    public float lightIntensity;   // suggested Unity Light intensity
    public Color color;            // visible colour of the star
    public float visualScale;      // relative render size
    public bool isBlackHole;       // rare central black hole

    // For a bound cluster (binary/ternary) this is how far the OUTERMOST sun's surface reaches from the
    // system barycenter — so orbit spacing keeps planets clear of the WHOLE cluster, not just one sun. It
    // is 0 for a single star, where callers use visualScale * 0.5 (one sun's radius) instead. Set by
    // StarDatabase.Combine from the StarCluster layout, so it always matches how the suns are rendered.
    public float clusterRadius;

    public int starCount = 1;      // 1 = single, 2 = binary, 3 = trinary (combined view)

    public bool hasHabitableZone;  // O/B giants burn too hot/short for a stable zone
    public float hzInner;          // inner edge, in game orbit units
    public float hzOuter;          // outer edge, in game orbit units

    public float HzCenter => (hzInner + hzOuter) * 0.5f;
    public float HzWidth  => Mathf.Max(0.001f, hzOuter - hzInner);

    // Why this sun is not drawn — Dev, Cloaked, Undiscovered, or None. Per-SUN rather than per-system,
    // so one member of a binary can be concealed while its companion stays lit. See Visibility.cs.
    public HideReason hideReason = HideReason.None;

    // The rendered sun (or, for a black hole, its event-horizon root). Set by SystemVisualizer as it
    // builds them; NonSerialized because it is scene state, not star physics — and because the whole
    // galaxy's visuals are destroyed and rebuilt whenever a system is deleted or a save is loaded.
    [System.NonSerialized] public GameObject visualObject;
}

public static class StarDatabase
{
    // 1 astronomical unit expressed in the game's orbit-radius units.
    // Planets are laid out starting ~8 units out and stepping outward, so this keeps the
    // Sun-like habitable zone landing among the inner-to-mid planets.
    public const float AU = 14f;

    // The render radius (visualScale) a "typical" star sits at, after the 2x-larger change below. Density
    // is measured relative to it: a star of this size and one solar mass has density 1. It's the anchor for
    // the mass<->radius<->density triangle the Dev editor exposes.
    public const float RefScale = 6f;

    // The three ways of reading the mass/radius/density triangle. density = mass / (radius/RefScale)^3, so
    // any two give the third. The Dev star sliders use these to keep themselves consistent: change the size
    // with mass held and density follows; change the density with radius held and mass follows; and so on.
    public static float DensityOf(float mass, float scale) => mass / Mathf.Pow(Mathf.Max(0.05f, scale) / RefScale, 3f);
    public static float MassFrom(float density, float scale) => density * Mathf.Pow(Mathf.Max(0.05f, scale) / RefScale, 3f);
    public static float ScaleFrom(float mass, float density) => RefScale * Mathf.Pow(Mathf.Max(0.001f, mass) / Mathf.Max(0.001f, density), 1f / 3f);

    /// How far a star's climate reaches, as a multiple of the reference distance — the ONE number that
    /// decides where its habitable zone sits, where its worlds' temperatures fall off, and how far apart
    /// its planets are laid out.
    ///
    /// PHYSICS SAYS sqrt(LUMINOSITY). WE DO NOT, AND THIS IS WHY.
    ///
    /// The flux law is right and unplayable. An O-type star is fifty thousand times the Sun's output, so
    /// its Goldilocks zone sits at 223x Earth's orbit. Its planets, meanwhile, were laid out on the same
    /// absolute scale as every other system's — starting a few units out and stepping ~20 at a time. The
    /// result was a habitable zone hundreds of units past the outermost planet: nothing could ever sit in
    /// it, and on the rare occasion something was moved into it, that world ended up ten times further
    /// out than its siblings and off the edge of the screen. You could only find it through the menus.
    ///
    /// So the exponent is compressed to 0.30 and the whole thing is CLAMPED at both ends. A brighter star
    /// still has a visibly wider system — that ordering is preserved, which is what the player actually
    /// reads — but the range is squeezed from 500:1 down to about 5:1, which is a range that fits on a
    /// screen. The clamp is what reins in the O and B giants specifically: past a few thousand solar
    /// luminosities, more output stops pushing the zone further out.
    ///
    ///   M dwarf  0.05 L  ->  0.45    K  0.3 L  ->  0.70    G  1 L  ->  1.00
    ///   F        4    L  ->  1.52    A   20 L  ->  2.46    B/O    ->  3.00 (clamped)
    ///
    /// The FLOOR is 0.45 rather than something tidier because M dwarfs run 0.02-0.10 luminosity, giving
    /// 0.32-0.50 — a floor of 0.55 would have clamped every single one of them to the same value, and M
    /// dwarfs are 45% of the galaxy's stars. That would have made "a brighter star has a wider system"
    /// inert across almost half the map.
    ///
    /// The SAME scale multiplies planet spacing (SolarSystemGenerator), so the two can never drift apart
    /// again: a star that pushes its habitable zone out also pushes its planets out, by construction.
    public const float FluxMin = 0.45f, FluxMax = 3.0f;

    public static float FluxScale(float luminosity)
        => Mathf.Clamp(Mathf.Pow(Mathf.Max(0.02f, luminosity), 0.30f), FluxMin, FluxMax);

    public static float FluxScale(StarData s) => FluxScale(s != null ? s.luminosity : 1f);

    /// The distance at which a star's warmth is Earth-normal. Every climate calculation measures against
    /// this, so it and the habitable zone move together automatically.
    public static float ReferenceDistance(StarData s) => FluxScale(s) * AU;

    /// Pull a raw blackbody colour toward white so a sun reads as tinted rather than as a vivid crayon,
    /// and so the light it throws on its worlds stays believable.
    ///
    /// HOW FAR toward white now depends on how EXTREME the star is. A flat 50% was washing out exactly
    /// the stars that most need to look different: an O-type's deep blue and an M-dwarf's ember red both
    /// arrived as pale off-white, so a blue giant and a yellow dwarf were nearly the same colour on
    /// screen. Sun-like stars still get the full pull (they genuinely are near-white), while the
    /// extremes keep two-thirds of their saturation — which is the single clearest signal that the
    /// player is looking at a different KIND of star, without needing to know what "O-type" means.
    public static Color SubtleTint(Color raw, float temperatureK)
    {
        // 0 at Sun-like, 1 at either extreme of the range we generate.
        float extremity = temperatureK >= 5700f
            ? Mathf.InverseLerp(5700f, 26000f, temperatureK)
            : Mathf.InverseLerp(5700f, 2800f, temperatureK);

        float toWhite = Mathf.Lerp(0.5f, 0.16f, Mathf.Clamp01(extremity));
        return Color.Lerp(raw, Color.white, toWhite);
    }

    // How strongly a star's surface glows (emission multiplier), tied to its LIGHT INTENSITY so the Dev
    // editor's intensity slider visibly brightens or dims the sun itself, not only the light it casts.
    // One formula, shared by the renderer and the editor, so an edited star matches a generated one.
    //
    // THE FLOOR IS ABOVE 1.0, AND THAT IS THE WHOLE FIX.
    //
    // This used to be `0.45 + lightIntensity * 0.7`, which put an ordinary sun at **0.975** — under one.
    // An unlit material writes its colour straight into the HDR buffer, so a value below 1 is just a
    // normal-brightness surface: bloom's threshold never sees it, the ACES tonemapper has no headroom to
    // work with, and the star renders as a flat painted ball. Only rare O/B giants ever crossed 1, which
    // is why the galaxy overview had to multiply this by 2.2 to make its markers read as hot at all.
    //
    // Now the DIMMEST red dwarf sits at ~1.75 and the brightest blue giant at ~6.5, so:
    //   * everything glows, because everything is genuinely HDR;
    //   * the spread is ~3.7x — widened from 2.6x specifically to separate the spectral classes on
    //     sight, so a red dwarf is plainly a lesser thing than an F-type;
    //   * the ceiling is deliberately modest. Bloom spills as a function of how far past the threshold a
    //     pixel is, so pushing the top much higher stops reading as "bright" and starts reading as a
    //     white hole with the system behind it washed out.
    public static float EmissionStrength(StarData s)
        => s == null ? 1f : Mathf.Clamp(1.05f + s.lightIntensity * 1.55f, 1.25f, 6.5f);

    /// How big a star's corona halo is, as a multiple of the star's own diameter.
    ///
    /// The halo is the second brightness cue after raw emission, and it does something emission cannot:
    /// it stays legible when the star is small on screen, where bloom gives up.
    ///
    /// IT MUST NOT REACH THE FIRST PLANET. The quad is a child of the star transform, so its scale is
    /// multiplied by visualScale — meaning a fixed multiple grows in ABSOLUTE terms with the star. At a
    /// flat 3.1x an O-type's halo came out with a 24.8-unit radius while its innermost world orbits at
    /// about 15, so the star's own glow swallowed the first two planets. The clamp below keeps the halo
    /// inside the clearance that generation reserves between a star's surface and its nearest orbit
    /// (OrbitSafety.StarClearance, 4.5, minus a margin), whatever the star's size.
    ///
    /// The practical effect is that a giant's halo is proportionally TIGHTER than a dwarf's. That is the
    /// right trade: a giant is already eight times the dwarf's diameter on screen, so it needs no help
    /// reading as enormous, and the "this one is fierce" signal is carried by CoronaAlpha and emission
    /// instead — neither of which can collide with anything.
    public static float CoronaScale(StarData s)
    {
        if (s == null) return 1.6f;

        float want = Mathf.Lerp(1.55f, 2.60f, Mathf.InverseLerp(0.45f, 3.2f, s.lightIntensity));

        // Radius of the halo is starRadius * scale, and starRadius is visualScale * 0.5. Keeping
        // (scale - 1) * starRadius under 4.0 leaves the halo comfortably short of the innermost orbit.
        float starRadius = Mathf.Max(0.5f, s.visualScale * 0.5f);
        float ceiling = 1f + 4.0f / starRadius;

        return Mathf.Clamp(Mathf.Min(want, ceiling), 1.25f, 2.60f);
    }

    /// How strongly the corona reads.
    ///
    /// This carries most of the brightness signal now that the halo's SIZE is constrained by the orbit
    /// clearance above. Kept under half opacity so the halo is an aura around a solid sun rather than a
    /// smear that swallows it.
    public static float CoronaAlpha(StarData s)
        => s == null ? 0.20f : Mathf.Lerp(0.13f, 0.48f, Mathf.InverseLerp(0.45f, 3.2f, s.lightIntensity));

    /// Size and brightness in plain words, for a player who does not know what an "O-type" is.
    ///
    /// The spectral class, the temperature in kelvin and the luminosity multiple are all already on the
    /// panel, and between them they tell an astronomer everything and a newcomer nothing. This is the
    /// same information as a sentence — and it is deliberately phrased in terms the map is ALSO showing,
    /// so the words and the picture teach each other: what you read as "giant, blazing blue" is the big
    /// bright blue thing on screen.
    public static string PlainDescription(StarData s)
    {
        if (s == null) return "";
        if (s.isBlackHole) return "a black hole — no light, enormous gravity";

        // Thresholds set against the ACTUAL rolled distribution rather than round numbers: the classes
        // span 2.9 (smallest M) to 20.4 (largest O), and adjacent classes overlap, so these name what is
        // on screen rather than pretending to name the spectral class. A small G honestly is a small
        // star, and saying so is better than promoting it to "average" to protect a tidy mapping.
        string size = s.visualScale >= 12f ? "a giant"
                    : s.visualScale >= 8f  ? "a large star"
                    : s.visualScale >= 4.5f ? "an average-sized star"
                    : "a small star";

        string glare = s.luminosity >= 500f  ? "blazing"
                     : s.luminosity >= 10f   ? "very bright"
                     : s.luminosity >= 0.6f  ? "steady"
                     : s.luminosity >= 0.15f ? "dim"
                     : "very dim";

        string hue = s.temperatureK >= 20000f ? "deep blue"
                   : s.temperatureK >= 9000f  ? "blue-white"
                   : s.temperatureK >= 6300f  ? "white"
                   : s.temperatureK >= 5000f  ? "yellow"
                   : s.temperatureK >= 3800f  ? "orange"
                   : "red";

        return $"{size}, {glare} {hue}";
    }

    // A short human classification of a system from its combined star: "Single G-type", "Binary system",
    // "Ternary system", or "Black hole".
    public static string SystemClass(StarData combined)
    {
        if (combined == null) return "Star";
        if (combined.isBlackHole) return "Black hole";
        return combined.starCount >= 3 ? "Ternary system"
             : combined.starCount == 2 ? "Binary system"
             : $"Single {combined.type}-type";
    }

    public static StarData Get(StarType type)
    {
        var s = new StarData { type = type };

        // Typical (mean) values for the class.
        float baseTemp, baseLum, baseScale;
        switch (type)
        {
            // SIZE SPREAD WIDENED. The classes used to run 2.4 -> 5.0, barely a 2x span, and after the
            // per-star variance below (0.72x .. 1.38x, nearly 2x on its own) a big M dwarf routinely came
            // out LARGER than a small B giant. The ordering the player is supposed to read straight off
            // the screen — that one of these is a monster and the other is an ember — was being drowned
            // by the noise. Now it runs 1.9 -> 8.0, a 4x span that survives the variance intact.
            case StarType.O: baseTemp = 40000f; baseLum = 50000f; baseScale = 8.0f; break;
            case StarType.B: baseTemp = 20000f; baseLum = 2000f;  baseScale = 6.1f; break;
            case StarType.A: baseTemp = 9000f;  baseLum = 20f;    baseScale = 4.2f; break;
            case StarType.F: baseTemp = 7000f;  baseLum = 4f;     baseScale = 3.4f; break;
            case StarType.G: baseTemp = 5700f;  baseLum = 1f;     baseScale = 2.9f; break;
            case StarType.K: baseTemp = 4500f;  baseLum = 0.3f;   baseScale = 2.4f; break;
            case StarType.M:
            default:         baseTemp = 3200f;  baseLum = 0.05f;  baseScale = 1.9f; break;
        }

        // Per-star variance so no two suns are alike (wide spread in heat, brightness, size, mass). Mass and
        // size are rolled INDEPENDENTLY and both wide, so density (mass over size^3) genuinely varies: some
        // stars come out heavy-but-small (dense), others light-but-big (sparse), which is what the request
        // asks for.
        s.temperatureK = baseTemp * Random.Range(0.78f, 1.22f);
        s.luminosity   = baseLum  * Random.Range(0.45f, 1.9f);
        s.mass         = OrbitalMechanics.StarMass(type) * Random.Range(0.65f, 1.55f);

        // Colour is DERIVED from the (varied) temperature so appearance tracks how hot the star is, then
        // pulled toward white by an amount that depends on how extreme it is (SubtleTint) — Sun-like
        // stars go nearly white, the hot and cold extremes keep their blue and their ember red. A small
        // jitter keeps no two suns identical.
        Color tc = SubtleTint(ColorFromTemperature(s.temperatureK), s.temperatureK);
        s.color = new Color(
            Mathf.Clamp01(tc.r + Random.Range(-0.05f, 0.05f)),
            Mathf.Clamp01(tc.g + Random.Range(-0.05f, 0.05f)),
            Mathf.Clamp01(tc.b + Random.Range(-0.05f, 0.05f)));

        // Render size tracks the class, nudged up for the more luminous members, then DOUBLED so the sun
        // dominates the centre of its system, plus an independent variance.
        //
        // The variance is NARROWER than it was (0.85-1.18 rather than 0.72-1.38). Combined with the wider
        // class spread above, that turns size from noise into a usable signal.
        //
        // Adjacent classes DO still overlap — a big G (7.4) outsizes a small A (6.5), and that is fine.
        // The failure being fixed was the extreme one: under the old numbers a big M dwarf could outsize
        // a small B giant, which made size actively misleading across the whole range. Now the ends are
        // cleanly separated — the smallest O is 12.4 against the largest M at 4.8 — so a giant always
        // reads as a giant even if neighbouring classes shade into each other.
        float lumFactor = Mathf.Pow(Mathf.Max(0.001f, s.luminosity / Mathf.Max(0.0001f, baseLum)), 0.12f);
        s.visualScale = baseScale * 2f * lumFactor * Random.Range(0.85f, 1.18f);

        // Density is the relationship between the two independent rolls above (~1 typical, varying either
        // side). Kept in sync with mass/visualScale here and by the Dev editor.
        s.density = DensityOf(s.mass, s.visualScale);

        // Scene-light brightness scales with luminosity (compressed so hot giants don't blow out).
        s.lightIntensity = Mathf.Clamp(0.5f + Mathf.Sqrt(s.luminosity) * 0.25f, 0.45f, 3.2f);

        // Habitable zone, off the shared compressed distance law (see FluxScale) rather than the raw
        // flux law — so it lands where this star's planets are actually laid out, because their spacing
        // is scaled by the same number.
        //
        // WIDENED from 0.95-1.37 to 0.80-1.55. A narrow band is fine when the zone is guaranteed to be
        // seeded with a world, but it makes every OTHER planet a near-miss, and it made the zone ring a
        // thin line on screen. A band you can see, and that two neighbouring planets can both sit in, is
        // worth more than a precise one.
        float reach = ReferenceDistance(s);
        s.hzInner = 0.80f * reach;
        s.hzOuter = 1.55f * reach;

        // Blue giants (O/B) are too hot and short-lived to hold a stable Goldilocks zone.
        s.hasHabitableZone = (type != StarType.O && type != StarType.B);
        return s;
    }

    // Approximate visible colour of a star from its surface temperature (blackbody-ish): cool stars
    // are orange-red, Sun-like are warm white, hot stars are blue-white.
    public static Color ColorFromTemperature(float k)
    {
        // Ascending temperature anchors.
        float[] temps  = { 2600f, 3200f, 4000f, 5000f, 5700f, 6600f, 8000f, 10000f, 16000f, 30000f, 44000f };
        Color[] colors =
        {
            new Color(1.00f, 0.48f, 0.28f), new Color(1.00f, 0.58f, 0.38f), new Color(1.00f, 0.72f, 0.48f),
            new Color(1.00f, 0.84f, 0.62f), new Color(1.00f, 0.93f, 0.78f), new Color(1.00f, 0.98f, 0.92f),
            // The hot end runs DEEPER BLUE than a strict blackbody would. A real O-type is only slightly
            // blue-white to the eye, which on screen is indistinguishable from an A-type — and the whole
            // point of these colours is to tell a player who does not know spectral classes that they are
            // looking at something different and dangerous. Exaggerated for legibility, deliberately.
            new Color(0.94f, 0.96f, 1.00f), new Color(0.80f, 0.88f, 1.00f), new Color(0.58f, 0.74f, 1.00f),
            new Color(0.38f, 0.58f, 1.00f), new Color(0.26f, 0.46f, 1.00f)
        };

        if (k <= temps[0]) return colors[0];
        if (k >= temps[temps.Length - 1]) return colors[colors.Length - 1];
        for (int i = 0; i < temps.Length - 1; i++)
        {
            if (k < temps[i + 1])
            {
                float t = Mathf.InverseLerp(temps[i], temps[i + 1], k);
                return Color.Lerp(colors[i], colors[i + 1], t);
            }
        }
        return colors[colors.Length - 1];
    }

    // A rare central black hole: massive, dark, no habitable zone, faint accretion glow.
    public static StarData BlackHole()
    {
        return new StarData
        {
            type = StarType.O, isBlackHole = true, starCount = 1,
            temperatureK = 0f, luminosity = 0f, mass = 14f, density = DensityOf(14f, 4.0f),
            color = new Color(0.05f, 0.02f, 0.08f), visualScale = 4.0f,
            lightIntensity = 0.5f, hasHabitableZone = false, hzInner = 0f, hzOuter = 0f
        };
    }

    // Combine a cluster (binary/ternary) into one StarData for lighting, orbits and the habitable zone.
    public static StarData Combine(List<StarData> stars)
    {
        if (stars == null || stars.Count == 0) return Get(StarType.G);
        if (stars.Count == 1) return stars[0];

        float lum = 0f, mass = 0f;
        Color col = Color.black;
        float scale = 0f;
        StarData bright = stars[0];
        foreach (var s in stars)
        {
            lum += s.luminosity;
            mass += s.mass;
            col += s.color * Mathf.Max(0.1f, s.luminosity);
            scale = Mathf.Max(scale, s.visualScale);
            if (s.luminosity > bright.luminosity) bright = s;
        }

        var c = new StarData
        {
            type = bright.type,
            starCount = stars.Count,
            luminosity = lum,
            mass = mass,
            temperatureK = bright.temperatureK,
            visualScale = scale,
            color = col / Mathf.Max(0.1f, lum),
            lightIntensity = Mathf.Clamp(0.6f + Mathf.Sqrt(lum) * 0.25f, 0.6f, 3.5f),
            hasHabitableZone = true
        };
        c.density = DensityOf(c.mass, c.visualScale);
        // How far the bound suns physically spread from the barycenter, so orbit spacing clears the whole
        // cluster. Same layout the renderer uses (StarCluster), so clearance and visuals never disagree.
        c.clusterRadius = StarCluster.Layout(stars).reach;
        // Through the same compressed law as a single star, so a binary's zone sits where its planets
        // are for the same reason a single sun's does. It matters more here, not less: combined
        // luminosity ADDS, so a pair of bright suns used to throw the zone even further out than either
        // would alone — the worst case of the off-screen problem, in the systems most worth visiting.
        float reach = ReferenceDistance(c);
        c.hzInner = 0.80f * reach;
        c.hzOuter = 1.55f * reach;
        return c;
    }
}
