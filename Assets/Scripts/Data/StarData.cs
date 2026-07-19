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

    public int starCount = 1;      // 1 = single, 2 = binary, 3 = trinary (combined view)

    public bool hasHabitableZone;  // O/B giants burn too hot/short for a stable zone
    public float hzInner;          // inner edge, in game orbit units
    public float hzOuter;          // outer edge, in game orbit units

    public float HzCenter => (hzInner + hzOuter) * 0.5f;
    public float HzWidth  => Mathf.Max(0.001f, hzOuter - hzInner);
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

    public static StarData Get(StarType type)
    {
        var s = new StarData { type = type };

        // Typical (mean) values for the class.
        float baseTemp, baseLum, baseScale;
        switch (type)
        {
            case StarType.O: baseTemp = 40000f; baseLum = 50000f; baseScale = 5.0f; break;
            case StarType.B: baseTemp = 20000f; baseLum = 2000f;  baseScale = 4.2f; break;
            case StarType.A: baseTemp = 9000f;  baseLum = 20f;    baseScale = 3.6f; break;
            case StarType.F: baseTemp = 7000f;  baseLum = 4f;     baseScale = 3.3f; break;
            case StarType.G: baseTemp = 5700f;  baseLum = 1f;     baseScale = 3.0f; break;
            case StarType.K: baseTemp = 4500f;  baseLum = 0.3f;   baseScale = 2.7f; break;
            case StarType.M:
            default:         baseTemp = 3200f;  baseLum = 0.05f;  baseScale = 2.4f; break;
        }

        // Per-star variance so no two suns are alike (wide spread in heat, brightness, size, mass). Mass and
        // size are rolled INDEPENDENTLY and both wide, so density (mass over size^3) genuinely varies: some
        // stars come out heavy-but-small (dense), others light-but-big (sparse), which is what the request
        // asks for.
        s.temperatureK = baseTemp * Random.Range(0.78f, 1.22f);
        s.luminosity   = baseLum  * Random.Range(0.45f, 1.9f);
        s.mass         = OrbitalMechanics.StarMass(type) * Random.Range(0.65f, 1.55f);

        // Colour is DERIVED from the (varied) temperature so appearance tracks how hot the star is, with a
        // more noticeable jitter (was ±0.05) so stars — and the individual suns in a binary/trinary, which
        // each cast their own coloured light — read as distinctly tinted rather than all near-white.
        Color tc = ColorFromTemperature(s.temperatureK);
        s.color = new Color(
            Mathf.Clamp01(tc.r + Random.Range(-0.11f, 0.11f)),
            Mathf.Clamp01(tc.g + Random.Range(-0.11f, 0.11f)),
            Mathf.Clamp01(tc.b + Random.Range(-0.11f, 0.11f)));

        // Render size tracks the class, nudged up for the more luminous members, then DOUBLED so the sun
        // dominates the centre of its system as the request wants, plus a wide independent variance.
        float lumFactor = Mathf.Pow(Mathf.Max(0.001f, s.luminosity / Mathf.Max(0.0001f, baseLum)), 0.12f);
        s.visualScale = baseScale * 2f * lumFactor * Random.Range(0.72f, 1.38f);

        // Density is the relationship between the two independent rolls above (~1 typical, varying either
        // side). Kept in sync with mass/visualScale here and by the Dev editor.
        s.density = DensityOf(s.mass, s.visualScale);

        // Scene-light brightness scales with luminosity (compressed so hot giants don't blow out).
        s.lightIntensity = Mathf.Clamp(0.5f + Mathf.Sqrt(s.luminosity) * 0.25f, 0.45f, 3.2f);

        // Habitable zone. Edge distances scale with sqrt(luminosity) (stellar flux law).
        float sqrtL = Mathf.Sqrt(s.luminosity);
        s.hzInner = 0.95f * sqrtL * AU;
        s.hzOuter = 1.37f * sqrtL * AU;

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
            new Color(0.96f, 0.96f, 1.00f), new Color(0.88f, 0.92f, 1.00f), new Color(0.76f, 0.84f, 1.00f),
            new Color(0.66f, 0.77f, 1.00f), new Color(0.60f, 0.72f, 1.00f)
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
        float sqrtL = Mathf.Sqrt(lum);
        c.hzInner = 0.95f * sqrtL * AU;
        c.hzOuter = 1.37f * sqrtL * AU;
        return c;
    }
}
