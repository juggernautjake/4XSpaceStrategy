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
        // Counts farms from BOTH systems (see ColonyFacilities): this used to read only the abstract
        // buildings list, so a world covered in surface farms still counted as having no food — its
        // people went hungry on paper while standing in a wheat field.
        int foodN = ColonyFacilities.FoodSources(b);
        float foodLvl = ColonyFacilities.FoodLevel(b);
        list.Add(new SatisfactionFactor
        {
            label = "Food supply",
            delta = foodN <= 0 ? -14f : Mathf.Lerp(4f, 16f, foodLvl),
            detail = foodN <= 0 ? "No farms anywhere — the colony lives on imported rations"
                   : foodN == 1 ? "One farm — the colony is fed, but only just"
                   : $"{foodN} farms — a comfortable surplus"
        });

        // ---- Power ----
        // Any generator counts: the abstract plant, or a solar array, wind farm, geothermal plant or
        // hydro plant placed on the surface. They're all the same answer to "are the lights on?".
        int powerN = ColonyFacilities.PowerSources(b);
        float powerLvl = ColonyFacilities.PowerLevel(b);
        list.Add(new SatisfactionFactor
        {
            label = "Power",
            delta = powerN <= 0 ? -9f : Mathf.Lerp(3f, 11f, powerLvl),
            detail = powerN <= 0 ? "No generation of any kind — rolling blackouts"
                   : powerN == 1 ? "One generator — the grid holds, mostly"
                   : $"{powerN} generators — a solid grid"
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
                detail = $"{Population.Short(b.population)} of {Population.Short(target)} — housing is running out"
            });

        // ---- Amenities: somewhere to work, and something to work on ----
        // Counts surface industry (mines, factories, refineries, the spaceport, the shipyard) as well as
        // the colony-level facilities — a world's whole economy, not half of it.
        int amenities = ColonyFacilities.ResearchSources(b) + ColonyFacilities.IndustrySources(b);
        if (amenities > 0)
            list.Add(new SatisfactionFactor
            {
                label = "Industry & science",
                delta = Mathf.Min(14f, amenities * 2f),
                detail = $"{amenities} tier(s) of skilled work — jobs, and somewhere to go in the evening"
            });

        // ---- Housing ----
        // Habitats, the capitol and the settlements the population grew for itself. A colony with more
        // people than homes is a colony living in tents.
        int housing = ColonyFacilities.HousingSources(b);
        if (housing <= 0 && b.population > 0)
            list.Add(new SatisfactionFactor
            {
                label = "Housing",
                delta = -8f,
                detail = "Nowhere proper to live — no capitol, habitats or settlements"
            });
        else if (housing > 1)
            list.Add(new SatisfactionFactor
            {
                label = "Housing",
                delta = Mathf.Min(9f, (housing - 1) * 2.5f),
                detail = $"{housing} places to live"
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
