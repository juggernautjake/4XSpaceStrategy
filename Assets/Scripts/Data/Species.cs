using System;
using System.Collections.Generic;
using UnityEngine;

// A playable species. Each has signature attributes and, crucially, its own environmental
// preferences — so the "habitable zone" and per-world habitability scores differ by whose eyes you
// look through. A blistering volcanic world that is lethal to Terrans is home to the Pyrothians.
public class Species
{
    public string name;
    public string signature;      // the attribute they are famous for
    public string description;
    public string biology;        // biological make-up
    public string habitat;        // the kinds of worlds they inhabit naturally
    public string strengths;      // propensities
    public string weaknesses;     // vulnerabilities
    public Color color;

    // Attributes, 1..10.
    public int iq, longevity, fertility, durability, adaptability;

    // Environmental preference.
    public float idealTemp;       // 0 = frigid, 0.5 = temperate, 1 = scorching
    public float tolerance;       // habitable-zone width multiplier (durable/adaptable = wider)
    float[] typeAffinity = new float[8]; // indexed by (int)CelestialBodyType, 0..1 ceiling

    public float Affinity(CelestialBodyType t)
    {
        int i = (int)t;
        return (i >= 0 && i < typeAffinity.Length) ? typeAffinity[i] : 0.5f;
    }

    public void SetAffinity(CelestialBodyType t, float v) { typeAffinity[(int)t] = v; }

    public string AttributeLine()
        => $"IQ {iq} · Longevity {longevity} · Fertility {fertility} · Durability {durability} · Adaptability {adaptability}";
}

public static class SpeciesDatabase
{
    static List<Species> _all;

    public static List<Species> All { get { if (_all == null) Build(); return _all; } }

    public static Species Get(int index)
    {
        var a = All;
        return a[Mathf.Clamp(index, 0, a.Count - 1)];
    }

    static void Build()
    {
        _all = new List<Species>();

        var terrans = new Species
        {
            name = "Terrans", signature = "Intelligence",
            description = "Balanced, curious tool-makers. Masters of adaptation through technology rather than biology.",
            biology = "Carbon-based warm-blooded bipeds; oxygen-nitrogen breathers with liquid water metabolism.",
            habitat = "Temperate rocky and ocean worlds with moderate climates and breathable air.",
            strengths = "Brilliant researchers and engineers; flexible generalists who thrive through technology.",
            weaknesses = "Fragile bodies and unremarkable lifespans; poorly suited to extreme heat, cold or vacuum.",
            color = new Color(0.4f, 0.7f, 1f),
            iq = 9, longevity = 5, fertility = 5, durability = 5, adaptability = 6,
            idealTemp = 0.50f, tolerance = 1.0f
        };
        SetAff(terrans, rocky: 1.0f, ocean: 0.9f, ice: 0.4f, volcanic: 0.25f, barren: 0.35f, gas: 0.15f, moon: 0.5f, ast: 0.2f);

        var aquarii = new Species
        {
            name = "Aquarii", signature = "Fertility",
            description = "Amphibious and fast-breeding. Their colonies bloom across any world with liquid or frozen water.",
            biology = "Amphibious, gilled semi-aquatics with permeable skin; require constant moisture.",
            habitat = "Ocean worlds, coastal shallows, reefs and thawing ice worlds — anywhere with water.",
            strengths = "Explosive population growth and rapid colonization of any wet world.",
            weaknesses = "Desiccate quickly on arid or scorching worlds; physically frail and short-lived.",
            color = new Color(0.3f, 0.85f, 0.8f),
            iq = 6, longevity = 5, fertility = 9, durability = 4, adaptability = 5,
            idealTemp = 0.42f, tolerance = 1.15f
        };
        SetAff(aquarii, rocky: 0.6f, ocean: 1.0f, ice: 0.7f, volcanic: 0.2f, barren: 0.2f, gas: 0.1f, moon: 0.4f, ast: 0.15f);

        var pyrothians = new Species
        {
            name = "Pyrothians", signature = "Durability",
            description = "Silicate-bodied and heat-loving. They flourish in furnace worlds that would kill anyone else.",
            biology = "Silicon-based crystalline lifeforms; metabolize heat and sulphur, need no liquid water.",
            habitat = "Volcanic and barren worlds, magma fields, and hot rocky planets close to their star.",
            strengths = "Nearly indestructible; shrug off heat, radiation and pressure that vaporize others.",
            weaknesses = "Sluggish breeders that struggle in cold or wet climates; slow to expand.",
            color = new Color(1f, 0.5f, 0.25f),
            iq = 5, longevity = 7, fertility = 3, durability = 10, adaptability = 7,
            idealTemp = 0.85f, tolerance = 1.5f
        };
        SetAff(pyrothians, rocky: 0.5f, ocean: 0.15f, ice: 0.1f, volcanic: 1.0f, barren: 0.8f, gas: 0.2f, moon: 0.55f, ast: 0.45f);

        var cryithn = new Species
        {
            name = "Cryithn", signature = "Longevity",
            description = "Slow, ancient and patient minds of the cold dark. They thrive far from the warmth of stars.",
            biology = "Cryogenic ammonia-based metabolism; subsurface dwellers that burrow beneath ice and rock.",
            habitat = "Frozen worlds, glaciers, and cold barren outer planets — often living underground.",
            strengths = "Extraordinarily long-lived and wise; centuries of accumulated knowledge.",
            weaknesses = "Barely reproduce and abhor heat; warm worlds are lethal to them.",
            color = new Color(0.6f, 0.85f, 1f),
            iq = 8, longevity = 10, fertility = 3, durability = 6, adaptability = 6,
            idealTemp = 0.16f, tolerance = 1.35f
        };
        SetAff(cryithn, rocky: 0.5f, ocean: 0.4f, ice: 1.0f, volcanic: 0.1f, barren: 0.7f, gas: 0.2f, moon: 0.6f, ast: 0.4f);

        var sylvans = new Species
        {
            name = "Sylvans", signature = "Adaptability",
            description = "Photosynthetic and endlessly resourceful. They take root almost anywhere and spread with ease.",
            biology = "Plant-like photosynthetic collective organisms; draw energy from starlight and soil.",
            habitat = "Lush jungles, grasslands, forested rocky worlds and warm shallow seas — but they tolerate most climates.",
            strengths = "Astonishingly adaptable; can gain a foothold on nearly any world with light.",
            weaknesses = "Physically delicate and dependent on adequate sunlight; wither in the dark outer system.",
            color = new Color(0.5f, 0.9f, 0.4f),
            iq = 5, longevity = 6, fertility = 8, durability = 4, adaptability = 9,
            idealTemp = 0.55f, tolerance = 1.6f
        };
        SetAff(sylvans, rocky: 0.9f, ocean: 0.9f, ice: 0.5f, volcanic: 0.4f, barren: 0.5f, gas: 0.3f, moon: 0.55f, ast: 0.3f);

        _all.Add(terrans); _all.Add(aquarii); _all.Add(pyrothians); _all.Add(cryithn); _all.Add(sylvans);
    }

    static void SetAff(Species s, float rocky, float ocean, float ice, float volcanic, float barren, float gas, float moon, float ast)
    {
        s.SetAffinity(CelestialBodyType.RockyPlanet, rocky);
        s.SetAffinity(CelestialBodyType.OceanPlanet, ocean);
        s.SetAffinity(CelestialBodyType.IcePlanet, ice);
        s.SetAffinity(CelestialBodyType.VolcanicPlanet, volcanic);
        s.SetAffinity(CelestialBodyType.BarrenPlanet, barren);
        s.SetAffinity(CelestialBodyType.GasGiant, gas);
        s.SetAffinity(CelestialBodyType.Moon, moon);
        s.SetAffinity(CelestialBodyType.Asteroid, ast);
    }
}

// Tracks which species' perspective the player is viewing worlds through.
public static class SpeciesManager
{
    public static int CurrentIndex { get; private set; } = 0;
    public static Species Current => SpeciesDatabase.Get(CurrentIndex);

    // Fired after the species changes AND all body habitability has been recomputed.
    public static event Action OnSpeciesChanged;

    public static void Select(int index)
    {
        CurrentIndex = Mathf.Clamp(index, 0, SpeciesDatabase.All.Count - 1);
        RecomputeWorld();
        OnSpeciesChanged?.Invoke();
    }

    // Recomputes every body's habitability from the current species' perspective and refreshes the
    // habitable-zone visuals.
    public static void RecomputeWorld()
    {
        var fallback = GameManager.Instance != null ? GameManager.Instance.CurrentStar : null;
        foreach (var b in SystemContext.AllBodies())
        {
            if (b.habitabilityLocked) continue;      // home world keeps its difficulty rating
            var star = b.hostStar != null ? b.hostStar : fallback;
            if (star == null) continue;
            b.isHabitable = Habitability.IsHabitable(star, Current, b.type, b.distanceFromStar);
            b.habitability = Habitability.Rate(star, Current, b.type, b.distanceFromStar);
            b.terraformability = Habitability.Terraformability(star, Current, b);
        }

        if (SystemContext.Zone != null) SystemContext.Zone.Refresh();
    }
}
