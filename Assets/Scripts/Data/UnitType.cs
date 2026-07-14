using UnityEngine;

// The starting ship classes. Cost order: Scout < Research/Fighter < Colony.
public enum UnitType { Scout, ResearchShip, Fighter, ColonyShip }

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

    public bool canExplore, canResearch, canColonize;

    public int iconShape;      // 0 up-triangle, 1 diamond, 2 right-triangle, 3 circle
    public Color iconColor;

    public UnitInfo(UnitType type, string name, string description, int cm, int ce, float buildTime,
                    int armor, int health, int speed, int research, int attack,
                    bool explore, bool doResearch, bool colonize, int iconShape, Color iconColor)
    {
        this.type = type; this.name = name; this.description = description;
        costMetal = cm; costEnergy = ce; this.buildTime = buildTime;
        this.armor = armor; this.health = health; this.speed = speed; this.research = research; this.attack = attack;
        canExplore = explore; canResearch = doResearch; canColonize = colonize;
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
        _all = new UnitInfo[4];
        _all[(int)UnitType.Scout] = new UnitInfo(UnitType.Scout, "Scout",
            "Cheap, fast explorer. Surveys worlds and does limited research. Can plant a small outpost but cannot colonize. Must return home from hostile worlds.",
            20, 10, 6f, 1, 20, 9, 2, 1, explore: true, doResearch: true, colonize: false, iconShape: 0, iconColor: new Color(0.5f, 0.85f, 1f));

        _all[(int)UnitType.ResearchShip] = new UnitInfo(UnitType.ResearchShip, "Research Ship",
            "A mobile laboratory. Slower and pricier than a scout, with no attack and only light defense, but researches a world far more deeply to unlock technologies.",
            60, 55, 16f, 2, 30, 5, 8, 0, explore: true, doResearch: true, colonize: false, iconShape: 1, iconColor: new Color(0.6f, 0.8f, 0.55f));

        _all[(int)UnitType.Fighter] = new UnitInfo(UnitType.Fighter, "Fighter",
            "A cheap warship with the strongest attack and defense of the starting ships, but short range and no research or exploration ability. Escorts fleets, defends your worlds, and grows deadlier with battle experience.",
            35, 25, 12f, 6, 80, 8, 0, 10, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(1f, 0.6f, 0.4f));

        _all[(int)UnitType.ColonyShip] = new UnitInfo(UnitType.ColonyShip, "Colony Ship",
            "A large, expensive settler vessel with heavy defenses, very little attack, and some research capacity. Slow but carries the population to fully claim a world (objectives: habitability, population, cities, exploration).",
            220, 180, 40f, 6, 160, 3, 3, 1, explore: true, doResearch: true, colonize: true, iconShape: 3, iconColor: new Color(0.85f, 0.75f, 0.45f));
    }
}
