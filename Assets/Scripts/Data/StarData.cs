using System.Collections.Generic;
using UnityEngine;

// Physical description of a star, derived from its spectral class.
// Drives light/heat, the habitable ("Goldilocks") zone, and body habitability ratings.
public class StarData
{
    public StarType type;
    public float temperatureK;     // surface temperature (heat)
    public float luminosity;       // relative to a G-type star (= 1.0)
    public float mass = 1f;        // solar masses (drives orbital speeds)
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

    public static StarData Get(StarType type)
    {
        var s = new StarData { type = type };

        switch (type)
        {
            case StarType.O:
                s.temperatureK = 40000f; s.luminosity = 50000f; s.color = new Color(0.60f, 0.72f, 1.00f); s.visualScale = 5.0f; break;
            case StarType.B:
                s.temperatureK = 20000f; s.luminosity = 2000f;  s.color = new Color(0.70f, 0.80f, 1.00f); s.visualScale = 4.2f; break;
            case StarType.A:
                s.temperatureK = 9000f;  s.luminosity = 20f;    s.color = new Color(0.90f, 0.92f, 1.00f); s.visualScale = 3.6f; break;
            case StarType.F:
                s.temperatureK = 7000f;  s.luminosity = 4f;     s.color = new Color(1.00f, 0.98f, 0.92f); s.visualScale = 3.3f; break;
            case StarType.G:
                s.temperatureK = 5700f;  s.luminosity = 1f;     s.color = new Color(1.00f, 0.95f, 0.75f); s.visualScale = 3.0f; break;
            case StarType.K:
                s.temperatureK = 4500f;  s.luminosity = 0.3f;   s.color = new Color(1.00f, 0.82f, 0.55f); s.visualScale = 2.7f; break;
            case StarType.M:
            default:
                s.temperatureK = 3200f;  s.luminosity = 0.05f;  s.color = new Color(1.00f, 0.60f, 0.40f); s.visualScale = 2.4f; break;
        }

        // Light intensity for the scene light — compressed so hot giants don't blow out the view.
        s.lightIntensity = Mathf.Clamp(0.6f + Mathf.Sqrt(s.luminosity) * 0.25f, 0.6f, 3.0f);

        // Habitable zone. Edge distances scale with sqrt(luminosity) (stellar flux law),
        // using the classic ~0.95–1.37 AU band for a Sun-like star.
        float sqrtL = Mathf.Sqrt(s.luminosity);
        s.hzInner = 0.95f * sqrtL * AU;
        s.hzOuter = 1.37f * sqrtL * AU;

        // Blue giants (O/B) are too hot and short-lived to hold a stable Goldilocks zone.
        s.hasHabitableZone = (type != StarType.O && type != StarType.B);

        s.mass = OrbitalMechanics.StarMass(type);
        return s;
    }

    // A rare central black hole: massive, dark, no habitable zone, faint accretion glow.
    public static StarData BlackHole()
    {
        return new StarData
        {
            type = StarType.O, isBlackHole = true, starCount = 1,
            temperatureK = 0f, luminosity = 0f, mass = 14f,
            color = new Color(0.05f, 0.02f, 0.08f), visualScale = 2.0f,
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
        float sqrtL = Mathf.Sqrt(lum);
        c.hzInner = 0.95f * sqrtL * AU;
        c.hzOuter = 1.37f * sqrtL * AU;
        return c;
    }
}
