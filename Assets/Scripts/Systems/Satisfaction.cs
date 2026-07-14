using System.Collections.Generic;
using UnityEngine;

// One contributing factor to how content a colony's population is.
public struct SatisfactionFactor
{
    public string label;
    public float delta;      // points added to (or taken from) the base score
    public string detail;    // plain-language "why"
}

// How happy a colony's people are, and why.
//
// Satisfaction is derived, never stored: it falls out of the world the colonists live on and what you
// have built for them. It reads through the CURRENT SPECIES' eyes, like everything else here — a
// volcanic world that miserable Terrans would riot on is home to the Pyrothians.
//
// It matters: unhappy colonies grow slowly or not at all (see ColonyManager.TickColony), so a world
// you have neglected quietly stops producing people.
public static class Satisfaction
{
    const float Baseline = 50f;   // a colony with nothing built and a merely tolerable world

    public static List<SatisfactionFactor> Breakdown(CelestialBody b)
    {
        var list = new List<SatisfactionFactor>();
        if (b == null) return list;

        // ---- The world itself. The single biggest factor: people want somewhere they can breathe. ----
        // Measured against the colonization threshold, so "just barely livable" is neutral-ish and a
        // properly terraformed world is a genuine pleasure to live on.
        float habDelta = Mathf.Clamp((b.habitability - Colony.FoundThreshold) * 0.6f, -35f, 25f);
        list.Add(new SatisfactionFactor
        {
            label = "Habitability",
            delta = habDelta,
            detail = $"{b.habitability:F0}% for {SpeciesManager.Current.name} (livable from {Colony.FoundThreshold:F0}%)"
        });

        // ---- Food ----
        bool farms = b.buildings.Contains((int)BuildingType.Farm);
        list.Add(new SatisfactionFactor
        {
            label = "Food supply",
            delta = farms ? 12f : -10f,
            detail = farms ? "Farms feed the colony well" : "No farms — the colony lives on imported rations"
        });

        // ---- Power ----
        bool power = b.buildings.Contains((int)BuildingType.PowerPlant);
        list.Add(new SatisfactionFactor
        {
            label = "Power",
            delta = power ? 8f : -6f,
            detail = power ? "Reliable power" : "No power plant — rolling blackouts"
        });

        // ---- Crowding ----
        // A colony pressed against its population ceiling is a colony with a housing crisis.
        int target = Colony.PopTarget(b);
        float crowd = target > 0 ? b.population / (float)target : 0f;
        float crowdDelta = crowd > 0.9f ? -Mathf.Lerp(0f, 18f, Mathf.Clamp01((crowd - 0.9f) / 0.4f)) : 0f;
        if (crowdDelta < 0f)
            list.Add(new SatisfactionFactor
            {
                label = "Crowding",
                delta = crowdDelta,
                detail = $"{b.population}/{target} — housing is running out"
            });

        // ---- Amenities: research centres and shipyards mean jobs and prospects ----
        int amenities = 0;
        if (b.researchCenterLevel >= 1) amenities += b.researchCenterLevel;
        if (b.shipyardLevel >= 1) amenities += b.shipyardLevel;
        if (amenities > 0)
            list.Add(new SatisfactionFactor
            {
                label = "Industry & science",
                delta = Mathf.Min(14f, amenities * 2.5f),
                detail = "Skilled work and somewhere to go in the evening"
            });

        // ---- Terraforming is disruptive to live through ----
        if (b.terraforming)
            list.Add(new SatisfactionFactor
            {
                label = "Terraforming under way",
                delta = -6f,
                detail = "Rebuilding the sky is loud, dusty work to live beneath"
            });

        // ---- Being properly established ----
        if (Colony.IsFullyEstablished(b))
            list.Add(new SatisfactionFactor
            {
                label = "Established colony",
                delta = 8f,
                detail = "A real settlement rather than an outpost"
            });

        return list;
    }

    public static float For(CelestialBody b)
    {
        if (b == null) return 0f;
        float v = Baseline;
        foreach (var f in Breakdown(b)) v += f.delta;
        return Mathf.Clamp(v, 0f, 100f);
    }

    // Growth multiplier: a miserable colony stops having children, a happy one booms.
    public static float GrowthMultiplier(CelestialBody b)
    {
        float s = For(b);
        if (s < 25f) return 0f;                                  // nobody is starting a family here
        return Mathf.Lerp(0.25f, 1.6f, Mathf.InverseLerp(25f, 100f, s));
    }

    public static string Label(float v)
    {
        if (v >= 85f) return "Thriving";
        if (v >= 70f) return "Content";
        if (v >= 50f) return "Settled";
        if (v >= 35f) return "Restless";
        if (v >= 20f) return "Unhappy";
        return "Rioting";
    }

    public static Color Color(float v) => Habitability.ScoreColor(v);
    public static string ColorHex(float v) => "#" + ColorUtility.ToHtmlStringRGB(Color(v));
}
