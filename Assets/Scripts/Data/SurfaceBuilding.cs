using System.Collections.Generic;
using UnityEngine;

// Structures placed ON a planet's surface grid, as opposed to the abstract colony buildings.
// IMPORTANT: append only — the ordinal is serialized.
public enum SurfaceBuildingType
{
    Mine, Farm, GeothermalPlant, SolarArray, WindFarm,
    Habitat, Factory, ResearchOutpost, Spaceport, Refinery,
    // Government
    ColonyShipBase, PlanetCapitol,
    // Industry
    StorageDepot, PowerDistribution,
    // Military
    SurfaceShipyard,
    // Harvesting
    HydroPlant,
    // Government — grown by the population itself, not placed (see CityGrowth)
    Settlement, Town, City
}

// What a structure is FOR. The build menu groups by this, so a long catalogue stays navigable and you
// can find "the thing that makes power" without reading every card.
public enum SurfaceBuildingCategory { Government, Harvesting, Industry, Military }

// One surface structure: its footprint SHAPE, what index makes it efficient, and what it produces.
//
// Footprints are tetromino-like so that developing a world is a packing puzzle: an L-shaped mine can be
// rotated to tuck around a 2x2 geothermal plant, and a dense city is one you fitted together well.
public class SurfaceBuildingInfo
{
    public SurfaceBuildingType type;
    public SurfaceBuildingCategory category;
    public string name;
    public string description;

    // Footprint cells, relative to the building's origin, before rotation.
    public Vector2Int[] shape;

    // One per world (a capitol, a shipyard). Upgrades replace rather than stack.
    public bool uniquePerWorld = false;

    // The type this upgrades INTO, if any. A Colony Ship Base becomes a Planet Capitol in place —
    // same footprint, so the upgrade never has to find new room for itself.
    public SurfaceBuildingType? upgradesTo = null;
    public int upgradeMetal, upgradeEnergy;

    // Storage: how much this adds to the empire's stockpile ceiling, per resource.
    public float storageCapacity = 0f;

    // Power distribution: fractional output boost given to ENERGY producers whose footprint touches
    // this one. The reason to pack a grid tightly rather than sprawl.
    public float adjacencyPowerBonus = 0f;

    // The survey index that decides how well this building performs where you put it. None = the
    // building doesn't care about terrain (a habitat is a habitat anywhere).
    public SurfaceIndexKind index = SurfaceIndexKind.None;

    // ---- Siting requirements ----
    // The hard floor on `index` below which this thing cannot be built AT ALL — a geothermal plant on
    // cold rock isn't inefficient, it's pointless. This is an ABSOLUTE threshold, deliberately: a
    // frozen world genuinely may not have a single viable geothermal site, and pretending otherwise by
    // grading on a curve would be a lie. The "best 10% here" highlight is separate and relative, so you
    // can always see where a world's best sites ARE even when none of them qualify.
    public float minIndex = 0f;

    // Must sit on (or off) water. Hydro plants need it; nothing else may touch it.
    public bool requiresWater = false;

    /// Why this class needs the ground it needs — shown on the card so a refusal is never a mystery.
    public string siteRequirement;

    public int costMetal, costEnergy;
    public float buildTime;

    // Output at 100% efficiency, per second. Efficiency is the average of `index` across the footprint,
    // so WHERE you put it matters as much as whether you built it.
    public float metalPerSec, energyPerSec, waterPerSec, researchPerSec, popGrowthPerSec;

    public bool allowsWater = false;   // most structures need dry ground

    // Hit points at level 1. Bigger, heavier structures are tougher.
    public int baseHealth = 100;

    public Color color;                // how it reads on the map

    public SurfaceBuildingInfo(SurfaceBuildingType t, SurfaceBuildingCategory cat, string n, string d,
                               Vector2Int[] shape, SurfaceIndexKind index, int cm, int ce, float bt, Color color)
    {
        type = t; category = cat; name = n; description = d; this.shape = shape; this.index = index;
        costMetal = cm; costEnergy = ce; buildTime = bt; this.color = color;
        // Toughness follows footprint: a nine-tile spaceport is a far harder thing to knock down than
        // a three-tile mine. Individual entries can override this after construction.
        baseHealth = 60 + shape.Length * 25;
    }

    public int Cells => shape.Length;
}

// A structure actually standing on a world.
[System.Serializable]
public class PlacedBuilding
{
    public int type;          // SurfaceBuildingType
    public int x, y;          // origin cell
    public int rotation;      // 0..3, in 90° steps
    public float efficiency;  // 0..1, locked in at placement time from the ground it sits on

    // Tech level, 1..MaxLevel. Upgrading raises output and toughness — the same "a tier buys you more"
    // ladder the shipyard and research centre already use.
    public int level = 1;

    // Condition, 0..1 of this building's maximum. Damage is not yet dealt to surface structures, so in
    // practice this reads 100% — but it's stored per building so the readout is real rather than a
    // hardcoded label, and so raids/decay have somewhere to land.
    public float health = 1f;

    public const int MaxLevel = 3;

    public SurfaceBuildingType Type => (SurfaceBuildingType)type;
    public SurfaceBuildingInfo Info => SurfaceBuildingDatabase.Get(Type);

    /// Output multiplier from tech level: 1.0 / 1.35 / 1.75.
    public float LevelMult => 1f + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 0.375f;

    public int MaxHealth => Mathf.RoundToInt(Info.baseHealth * (1f + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 0.5f));
    public int CurrentHealth => Mathf.RoundToInt(Mathf.Clamp01(health) * MaxHealth);
    public bool CanUpgrade => level < MaxLevel;

    /// Everything that scales a building's production: where you sited it, and how good it is.
    public float OutputMult => Mathf.Clamp01(efficiency) * LevelMult;

    /// Repair the record after a load. JsonUtility fills MISSING fields with 0, so a save written
    /// before levels/health existed comes back as level 0 / health 0 — a dead, non-existent tier.
    public void NormalizeAfterLoad()
    {
        if (level < 1) level = 1;
        if (level > MaxLevel) level = MaxLevel;
        if (health <= 0f) health = 1f;
        health = Mathf.Clamp01(health);
    }
}

public static class SurfaceBuildingDatabase
{
    static SurfaceBuildingInfo[] _all;

    public static SurfaceBuildingInfo[] All { get { if (_all == null) Build(); return _all; } }
    public static SurfaceBuildingInfo Get(SurfaceBuildingType t) { if (_all == null) Build(); return _all[(int)t]; }

    static Vector2Int[] S(params int[] xy)
    {
        var list = new Vector2Int[xy.Length / 2];
        for (int i = 0; i < list.Length; i++) list[i] = new Vector2Int(xy[i * 2], xy[i * 2 + 1]);
        return list;
    }

    static void Build()
    {
        _all = new SurfaceBuildingInfo[System.Enum.GetValues(typeof(SurfaceBuildingType)).Length];

        // ================= GOVERNMENT =================
        // The colony ship doesn't vanish when it settles — it lands and becomes the colony's first
        // administration, and stays that until you can afford to build a real capitol around it.
        var shipBase = new SurfaceBuildingInfo(SurfaceBuildingType.ColonyShipBase, SurfaceBuildingCategory.Government,
            "Colony Ship Base",
            "The grounded hull of the colony ship that settled this world, pressed into service as its first seat of government. Cramped and improvised — upgrade it to a Planet Capitol when the colony can afford one.",
            S(0, 0, 1, 0, 0, 1, 1, 1), SurfaceIndexKind.None, 0, 0, 0f, new Color(0.70f, 0.72f, 0.60f));
        shipBase.uniquePerWorld = true;
        shipBase.upgradesTo = SurfaceBuildingType.PlanetCapitol;
        shipBase.upgradeMetal = 180; shipBase.upgradeEnergy = 140;
        shipBase.popGrowthPerSec = 0.4f;
        shipBase.storageCapacity = 400f;   // the ship's own holds
        _all[(int)SurfaceBuildingType.ColonyShipBase] = shipBase;

        var capitol = new SurfaceBuildingInfo(SurfaceBuildingType.PlanetCapitol, SurfaceBuildingCategory.Government,
            "Planet Capitol",
            "The seat of this world's government. Administers the colony properly: faster growth, real warehousing, and somewhere for the population to take its grievances.",
            S(0, 0, 1, 0, 0, 1, 1, 1), SurfaceIndexKind.None, 180, 140, 26f, new Color(0.95f, 0.85f, 0.45f));
        capitol.uniquePerWorld = true;
        capitol.popGrowthPerSec = 1.4f;
        capitol.storageCapacity = 1200f;
        _all[(int)SurfaceBuildingType.PlanetCapitol] = capitol;

        // ================= HARVESTING =================
        // L-tromino — tucks into the corner left by a square plant.
        _all[(int)SurfaceBuildingType.Mine] = new SurfaceBuildingInfo(SurfaceBuildingType.Mine, SurfaceBuildingCategory.Harvesting, "Mine",
            "Digs metal out of the ground. Put it on a rich seam: its yield is set by the Mineral Index under its footprint, and never changes once built.",
            S(0, 0, 0, 1, 1, 0), SurfaceIndexKind.Mineral, 60, 30, 16f, new Color(0.62f, 0.42f, 0.20f))
        { metalPerSec = 1.6f };

        // 2x3 block — farms want a lot of contiguous ground.
        _all[(int)SurfaceBuildingType.Farm] = new SurfaceBuildingInfo(SurfaceBuildingType.Farm, SurfaceBuildingCategory.Harvesting, "Farmland",
            "Feeds the colony and grows its population. Wants the greenest ground you can find — check the Fertile Index.",
            S(0, 0, 1, 0, 0, 1, 1, 1, 0, 2, 1, 2), SurfaceIndexKind.Fertile, 45, 25, 14f, new Color(0.35f, 0.80f, 0.30f))
        { waterPerSec = 0.3f, popGrowthPerSec = 1.8f };

        // O-tetromino — a compact 2x2 plant.
        _all[(int)SurfaceBuildingType.GeothermalPlant] = new SurfaceBuildingInfo(SurfaceBuildingType.GeothermalPlant, SurfaceBuildingCategory.Harvesting, "Geothermal Plant",
            "Taps the heat under the crust. Sited on a volcano or a geyser field it is the best power in the game; sited on cold rock it is a waste of metal. Check the Heat Index.",
            S(0, 0, 1, 0, 0, 1, 1, 1), SurfaceIndexKind.Heat, 90, 40, 20f, new Color(1.00f, 0.45f, 0.15f))
        { energyPerSec = 2.6f };

        // I-tetromino — a long array of panels.
        _all[(int)SurfaceBuildingType.SolarArray] = new SurfaceBuildingInfo(SurfaceBuildingType.SolarArray, SurfaceBuildingCategory.Harvesting, "Solar Array",
            "A run of panels. Wants dry, cloudless, open ground — savanna and desert are ideal. Check the Weather Index.",
            S(0, 0, 1, 0, 2, 0, 3, 0), SurfaceIndexKind.Solar, 55, 20, 12f, new Color(0.95f, 0.90f, 0.45f))
        { energyPerSec = 1.5f };

        // T-tetromino.
        _all[(int)SurfaceBuildingType.WindFarm] = new SurfaceBuildingInfo(SurfaceBuildingType.WindFarm, SurfaceBuildingCategory.Harvesting, "Wind Farm",
            "Turbines. Wants exposure — ridgelines, coasts and open steppe. Check the Weather Index.",
            S(0, 0, 1, 0, 2, 0, 1, 1), SurfaceIndexKind.Wind, 50, 25, 12f, new Color(0.70f, 0.90f, 1.00f))
        { energyPerSec = 1.3f };

        // S-tetromino — awkward on purpose; it's the piece that forces you to plan.
        _all[(int)SurfaceBuildingType.Habitat] = new SurfaceBuildingInfo(SurfaceBuildingType.Habitat, SurfaceBuildingCategory.Government, "Habitat Block",
            "Housing. Doesn't care what it sits on — put it wherever the awkward gaps are.",
            S(0, 0, 1, 0, 1, 1, 2, 1), SurfaceIndexKind.None, 40, 30, 12f, new Color(0.55f, 0.70f, 0.95f))
        { popGrowthPerSec = 1.2f };

        // 3x2 block.
        _all[(int)SurfaceBuildingType.Factory] = new SurfaceBuildingInfo(SurfaceBuildingType.Factory, SurfaceBuildingCategory.Industry, "Factory",
            "Turns raw metal into more of it. Large, and indifferent to terrain.",
            S(0, 0, 1, 0, 2, 0, 0, 1, 1, 1, 2, 1), SurfaceIndexKind.None, 110, 70, 24f, new Color(0.75f, 0.55f, 0.85f))
        { metalPerSec = 1.1f };

        // J-tetromino.
        _all[(int)SurfaceBuildingType.ResearchOutpost] = new SurfaceBuildingInfo(SurfaceBuildingType.ResearchOutpost, SurfaceBuildingCategory.Industry, "Research Outpost",
            "A surface laboratory feeding research to the empire. Terrain-agnostic.",
            S(0, 0, 0, 1, 0, 2, 1, 2), SurfaceIndexKind.None, 80, 70, 20f, new Color(0.45f, 0.85f, 1.00f))
        { researchPerSec = 0.6f };

        // Big square — needs real space cleared for it.
        _all[(int)SurfaceBuildingType.Spaceport] = new SurfaceBuildingInfo(SurfaceBuildingType.Spaceport, SurfaceBuildingCategory.Military, "Spaceport",
            "Ground-to-orbit traffic. Big, flat and hungry for space — plan the city around it.",
            S(0, 0, 1, 0, 2, 0, 0, 1, 1, 1, 2, 1, 0, 2, 1, 2, 2, 2), SurfaceIndexKind.None, 180, 140, 32f,
            new Color(0.85f, 0.85f, 0.90f))
        { metalPerSec = 0.4f, energyPerSec = 0.4f };

        // ================= INDUSTRY =================
        // Storage is what lets you SAVE for something. Without depots your stockpile tops out and any
        // income above the ceiling is simply wasted — the megaprojects are unaffordable by design until
        // you have somewhere to put the materials.
        var depot = new SurfaceBuildingInfo(SurfaceBuildingType.StorageDepot, SurfaceBuildingCategory.Industry,
            "Storage Depot",
            "Warehousing. Raises how much metal, energy and water your empire can hold at once — anything you produce above that ceiling is thrown away. Build these before you try to bank for a terraformer or a mega-station.",
            S(0, 0, 1, 0, 2, 0, 0, 1, 1, 1, 2, 1), SurfaceIndexKind.None, 70, 40, 16f, new Color(0.60f, 0.62f, 0.70f));
        depot.storageCapacity = 2500f;
        _all[(int)SurfaceBuildingType.StorageDepot] = depot;

        // T-tetromino. The payoff for packing tightly rather than sprawling.
        var grid = new SurfaceBuildingInfo(SurfaceBuildingType.PowerDistribution, SurfaceBuildingCategory.Industry,
            "Power Distribution",
            "A switchyard and transmission hub. Every power plant whose footprint TOUCHES this one runs markedly better — the reason to interlock a dense industrial block instead of scattering your generators across the map.",
            S(0, 0, 1, 0, 2, 0, 1, 1), SurfaceIndexKind.None, 85, 55, 18f, new Color(0.95f, 0.95f, 0.55f));
        grid.adjacencyPowerBonus = 0.30f;
        _all[(int)SurfaceBuildingType.PowerDistribution] = grid;

        // ================= MILITARY =================
        // Placing this is what gives a world a shipyard at all — it sets the world's shipyard tier,
        // which is then upgraded from the Inspector like any other. One per world.
        var yard = new SurfaceBuildingInfo(SurfaceBuildingType.SurfaceShipyard, SurfaceBuildingCategory.Military,
            "Shipyard",
            "Ground-based slipways and their orbital tether. Placing one gives this world a level-1 shipyard, adding its build power to your empire's pool; upgrade its tier from the world's Production tab. One per world.",
            S(0, 0, 1, 0, 2, 0, 0, 1, 1, 1, 2, 1, 0, 2, 1, 2), SurfaceIndexKind.None, 140, 100, 28f,
            new Color(0.65f, 0.80f, 1.00f));
        yard.uniquePerWorld = true;
        _all[(int)SurfaceBuildingType.SurfaceShipyard] = yard;

        // Z-tetromino.
        _all[(int)SurfaceBuildingType.Refinery] = new SurfaceBuildingInfo(SurfaceBuildingType.Refinery, SurfaceBuildingCategory.Industry, "Refinery",
            "Refines ore on site. Best next to the rock it's processing — it reads the Mineral Index too.",
            S(1, 0, 2, 0, 0, 1, 1, 1), SurfaceIndexKind.Mineral, 95, 60, 22f, new Color(0.80f, 0.65f, 0.35f))
        { metalPerSec = 1.2f };

        // Hydro — the one thing that WANTS water, and needs relief to drop it through.
        var hydro = new SurfaceBuildingInfo(SurfaceBuildingType.HydroPlant, SurfaceBuildingCategory.Harvesting,
            "Hydro Plant",
            "A dam and turbine hall. Needs flowing water with somewhere to fall — a river or a coast WITH relief. A flat open sea has no head to work with and won't do.",
            S(0, 0, 1, 0, 2, 0, 2, 1), SurfaceIndexKind.Water, 80, 35, 18f, new Color(0.35f, 0.75f, 1.00f));
        hydro.energyPerSec = 2.1f;
        _all[(int)SurfaceBuildingType.HydroPlant] = hydro;

        // ---- Grown, not placed ----
        // The population houses itself (CityGrowth). You never build these; they appear near what's
        // already there and thicken over time. They occupy real tiles and compete for the ground you
        // wanted to mine, which is the point — a living world costs you something.
        var settlement = new SurfaceBuildingInfo(SurfaceBuildingType.Settlement, SurfaceBuildingCategory.Government,
            "Settlement",
            "A handful of homes that grew up on their own as the colony spread out. Given people and time it will become a town.",
            S(0, 0, 1, 0), SurfaceIndexKind.None, 0, 0, 0f, new Color(0.62f, 0.66f, 0.78f));
        settlement.popGrowthPerSec = 0.25f;
        settlement.storageCapacity = 120f;
        _all[(int)SurfaceBuildingType.Settlement] = settlement;

        var town = new SurfaceBuildingInfo(SurfaceBuildingType.Town, SurfaceBuildingCategory.Government,
            "Town",
            "A proper town, grown from a settlement. Houses people, and puts a little of everything back into the colony.",
            S(0, 0, 1, 0, 0, 1, 1, 1), SurfaceIndexKind.None, 0, 0, 0f, new Color(0.74f, 0.78f, 0.90f));
        town.popGrowthPerSec = 0.6f;
        town.metalPerSec = 0.15f;
        town.storageCapacity = 350f;
        _all[(int)SurfaceBuildingType.Town] = town;

        var city = new SurfaceBuildingInfo(SurfaceBuildingType.City, SurfaceBuildingCategory.Government,
            "City",
            "A full city. Only a genuinely habitable world grows these — and a world that grows enough of them becomes one continuous city.",
            S(0, 0, 1, 0, 2, 0, 0, 1, 1, 1, 2, 1, 1, 2), SurfaceIndexKind.None, 0, 0, 0f, new Color(0.88f, 0.92f, 1.00f));
        city.popGrowthPerSec = 1.1f;
        city.metalPerSec = 0.3f;
        city.energyPerSec = 0.2f;
        city.researchPerSec = 0.15f;
        city.storageCapacity = 700f;
        _all[(int)SurfaceBuildingType.City] = city;

        // ---- Siting requirements ----
        // The floor below which a site is not merely poor but POINTLESS. Absolute, not graded on a
        // curve: a world genuinely may have no viable geothermal site, and the honest answer is to say
        // so. The "best here" highlight is relative and separate, so you can always see where a world's
        // best sites are even when none of them clear the bar.
        Require(SurfaceBuildingType.GeothermalPlant, 0.35f,
            "Needs real heat in the crust — a volcano, magma field or geyser field. Cold rock yields nothing at all.");
        Require(SurfaceBuildingType.Farm, 0.22f,
            "Needs ground that is warm, wet and flat enough to plough. Rock, ice and desert cannot be farmed.");
        Require(SurfaceBuildingType.Mine, 0.18f,
            "Needs rock with something in it. Deep soil and open water have no seams to work.");
        Require(SurfaceBuildingType.Refinery, 0.15f,
            "Wants to sit on the ore it processes.");
        Require(SurfaceBuildingType.SolarArray, 0.25f,
            "Needs clear, well-lit ground. Permanent cloud or a distant, dim star make panels worthless.");
        Require(SurfaceBuildingType.WindFarm, 0.25f,
            "Needs exposure — ridgelines, coasts, open steppe. Sheltered forest and canyon floors are still.");
        Require(SurfaceBuildingType.HydroPlant, 0.3f,
            "Needs flowing water and relief for it to fall through.");
    }

    static void Require(SurfaceBuildingType t, float minIndex, string why)
    {
        var info = _all[(int)t];
        if (info == null) return;
        info.minIndex = minIndex;
        info.siteRequirement = why;
    }

    // ---- Geometry ----
    // Rotate a footprint by 90° steps and re-normalize it so its origin is back at (0,0). Without the
    // normalize, a rotated piece would drift away from the cursor.
    public static Vector2Int[] Rotated(Vector2Int[] shape, int rotation)
    {
        rotation = ((rotation % 4) + 4) % 4;
        var outCells = new Vector2Int[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            int x = shape[i].x, y = shape[i].y;
            for (int r = 0; r < rotation; r++) { int nx = y; int ny = -x; x = nx; y = ny; }
            outCells[i] = new Vector2Int(x, y);
        }
        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (var c in outCells) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); }
        for (int i = 0; i < outCells.Length; i++) outCells[i] -= new Vector2Int(minX, minY);
        return outCells;
    }

    public static Vector2Int[] CellsOf(SurfaceBuildingType t, int rotation) => Rotated(Get(t).shape, rotation);

    /// The absolute cells a building would occupy at this origin and rotation.
    public static List<Vector2Int> Footprint(SurfaceBuildingType t, int x, int y, int rotation)
    {
        var list = new List<Vector2Int>();
        foreach (var c in CellsOf(t, rotation)) list.Add(new Vector2Int(x + c.x, y + c.y));
        return list;
    }

    public static List<Vector2Int> Footprint(PlacedBuilding p) => Footprint(p.Type, p.x, p.y, p.rotation);
}
