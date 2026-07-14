using UnityEngine;

// Computes a 0-100 habitability rating for a body from a SPECIES' perspective. Each species shifts
// and widens the star's Goldilocks zone according to its ideal temperature and tolerance, and weights
// body types by its own biology. "Habitable" (green ring) means the body sits inside that species'
// shifted zone. Hostile worlds still score low but remain valuable for their ores and anomalies.
public static class Habitability
{
    // The species' preferred orbital band, derived by shifting/scaling the star's base zone.
    public static bool GetZone(StarData star, Species species, out float inner, out float outer)
    {
        inner = outer = 0f;
        if (star == null || !star.hasHabitableZone || species == null) return false;

        // Hotter-preferring species pull the band inward (closer = hotter); colder push it outward.
        // Range widened so cold species reach noticeably further out than warm ones.
        float tempShift = Mathf.Lerp(1.95f, 0.5f, Mathf.Clamp01(species.idealTemp));
        float center = star.HzCenter * tempShift;

        // Broader, more forgiving bands, and asymmetric so the zone extends further OUTWARD than in.
        float half = star.HzWidth * Mathf.Max(0.35f, species.tolerance) * 1.15f;

        inner = Mathf.Max(0.5f, center - half * 0.85f);
        outer = center + half * 1.45f;   // reaches further from the star
        return true;
    }

    public static bool InZone(StarData star, Species species, float distanceFromStar)
    {
        if (!GetZone(star, species, out float inner, out float outer)) return false;
        return distanceFromStar >= inner && distanceFromStar <= outer;
    }

    // 0..100 for the given species.
    public static float Rate(StarData star, Species species, CelestialBodyType type, float distanceFromStar)
    {
        if (!GetZone(star, species, out float inner, out float outer)) return 0f;

        float center = (inner + outer) * 0.5f;
        float half = Mathf.Max(0.001f, (outer - inner) * 0.5f);

        float positional;
        if (distanceFromStar >= inner && distanceFromStar <= outer)
        {
            float t = 1f - Mathf.Abs(distanceFromStar - center) / half; // 1 centre, 0 edge
            positional = 60f + 40f * t;
        }
        else
        {
            float over = (distanceFromStar < inner) ? (inner - distanceFromStar) : (distanceFromStar - outer);
            float k = over / half;
            positional = 60f * Mathf.Exp(-0.9f * k * k);
        }

        return Mathf.Clamp(positional * species.Affinity(type), 0f, 100f);
    }

    public static string Label(float rating, bool inZone)
    {
        if (inZone) return "Habitable";
        if (rating >= 45f) return "Marginal";
        if (rating >= 20f) return "Hostile";
        return "Uninhabitable";
    }

    // Smooth red -> yellow -> green gradient for the score number.
    public static Color ScoreColor(float rating)
    {
        float t = Mathf.Clamp01(rating / 100f);
        return t < 0.5f
            ? Color.Lerp(new Color(1f, 0.30f, 0.25f), new Color(1f, 0.82f, 0.25f), t * 2f)
            : Color.Lerp(new Color(1f, 0.82f, 0.25f), new Color(0.35f, 1f, 0.4f), (t - 0.5f) * 2f);
    }

    public static string ScoreColorHex(float rating)
        => "#" + ColorUtility.ToHtmlStringRGB(ScoreColor(rating));
}
