using UnityEngine;

// Ship classes. The first four are the starting tier-1 classes (Cost order: Scout < Research/Fighter
// < Colony). The Mk II variants unlock at a level-2 shipyard, and the Terraformer at level 3.
// IMPORTANT: append new entries only — the ordinal is serialized in saves.
public enum UnitType { Scout, ResearchShip, Fighter, ColonyShip, ScoutII, ResearchShipII, FighterII, Terraformer }

// Static definition of a ship class: costs, build time, base stats, capabilities, and a placeholder
// icon (a coloured geometric token so each class is recognizable).
public class UnitInfo
{
    public UnitType type;
    public string name;
    public string description;

    public int costMetal, costEnergy;
    public float buildTime;    // seconds to build

    public int armor, health, speed, research, attack;

    public bool canExplore, canResearch, canColonize, canTerraform;

    public int minShipyardLevel = 1;   // shipyard tier required to build this class (1-3)

    public int iconShape;      // 0 up-triangle, 1 diamond, 2 right-triangle, 3 circle
    public Color iconColor;

    public UnitInfo(UnitType type, string name, string description, int cm, int ce, float buildTime,
                    int armor, int health, int speed, int research, int attack,
                    bool explore, bool doResearch, bool colonize, int iconShape, Color iconColor,
                    int minShipyardLevel = 1, bool terraform = false)
    {
        this.type = type; this.name = name; this.description = description;
        costMetal = cm; costEnergy = ce; this.buildTime = buildTime;
        this.armor = armor; this.health = health; this.speed = speed; this.research = research; this.attack = attack;
        canExplore = explore; canResearch = doResearch; canColonize = colonize; canTerraform = terraform;
        this.minShipyardLevel = minShipyardLevel;
        this.iconShape = iconShape; this.iconColor = iconColor;
    }
}

public static class UnitDatabase
{
    static UnitInfo[] _all;

    public static UnitInfo Get(UnitType t)
    {
        if (_all == null) Build();
        return _all[(int)t];
    }

    public static UnitInfo[] All { get { if (_all == null) Build(); return _all; } }

    static void Build()
    {
        _all = new UnitInfo[System.Enum.GetValues(typeof(UnitType)).Length];
        _all[(int)UnitType.Scout] = new UnitInfo(UnitType.Scout, "Scout",
            "Cheap, fast explorer with a survey BONUS. Surveys worlds and collects ore samples, but can't research them itself — bring samples to a research ship or a world with a research centre. Cannot colonize. Must return home from hostile worlds.",
            20, 10, 6f, 1, 20, 9, 2, 1, explore: true, doResearch: false, colonize: false, iconShape: 0, iconColor: new Color(0.5f, 0.85f, 1f));

        _all[(int)UnitType.ResearchShip] = new UnitInfo(UnitType.ResearchShip, "Research Ship",
            "A mobile laboratory. Slower and pricier than a scout, with no attack and only light defense, but researches a world far more deeply to unlock technologies.",
            60, 55, 16f, 2, 30, 5, 8, 0, explore: true, doResearch: true, colonize: false, iconShape: 1, iconColor: new Color(0.6f, 0.8f, 0.55f));

        _all[(int)UnitType.Fighter] = new UnitInfo(UnitType.Fighter, "Fighter",
            "A cheap warship with the strongest attack and defense of the starting ships, but short range and no research or exploration ability. Escorts fleets, defends your worlds, and grows deadlier with battle experience.",
            35, 25, 12f, 6, 80, 8, 0, 10, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(1f, 0.6f, 0.4f));

        _all[(int)UnitType.ColonyShip] = new UnitInfo(UnitType.ColonyShip, "Colony Ship",
            "A large, expensive settler vessel with heavy defenses and very little attack. Slow, but it sacrifices itself to found your first CITY on a habitable-enough world — after which you build shipyards, research centres and farms there.",
            220, 180, 40f, 6, 160, 3, 0, 1, explore: true, doResearch: false, colonize: true, iconShape: 3, iconColor: new Color(0.85f, 0.75f, 0.45f));

        // ---- Level-2 shipyard: upgraded Mk II variants (tougher, faster, more capable) ----
        _all[(int)UnitType.ScoutII] = new UnitInfo(UnitType.ScoutII, "Scout Mk II",
            "An upgraded scout: sturdier and faster, with a sharper survey. Still can't research its own samples, but ranges further and survives hostile worlds better. Requires a level-2 shipyard.",
            40, 20, 8f, 3, 40, 12, 4, 3, explore: true, doResearch: false, colonize: false, iconShape: 0, iconColor: new Color(0.35f, 0.95f, 1f), minShipyardLevel: 2);

        _all[(int)UnitType.ResearchShipII] = new UnitInfo(UnitType.ResearchShipII, "Research Ship Mk II",
            "An advanced mobile laboratory that researches worlds far faster and deeper than the base model, with better defenses. Requires a level-2 shipyard.",
            110, 100, 20f, 4, 55, 7, 13, 0, explore: true, doResearch: true, colonize: false, iconShape: 1, iconColor: new Color(0.5f, 1f, 0.6f), minShipyardLevel: 2);

        _all[(int)UnitType.FighterII] = new UnitInfo(UnitType.FighterII, "Fighter Mk II",
            "A heavier warship with markedly stronger attack and armor than the base fighter. The backbone of a real war fleet. Requires a level-2 shipyard.",
            65, 50, 16f, 10, 140, 10, 0, 16, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(1f, 0.45f, 0.3f), minShipyardLevel: 2);

        // ---- Level-3 shipyard: the Terraformer ----
        _all[(int)UnitType.Terraformer] = new UnitInfo(UnitType.Terraformer, "Terraformer",
            "A vast climate-engineering vessel. It can begin terraforming a world it is present at and, while it stays, greatly accelerates the project — turning hostile worlds livable. No attack. Requires a level-3 shipyard.",
            180, 200, 34f, 5, 120, 5, 0, 0, explore: true, doResearch: false, colonize: false, iconShape: 1, iconColor: new Color(0.4f, 0.9f, 0.75f), minShipyardLevel: 3, terraform: true);
    }
}
