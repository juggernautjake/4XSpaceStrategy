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
    Settlement, Town, City,
    // Electrical — the power grid (see PowerGrid.cs)
    PowerNode, Capacitor, CombustionPlant, SteamTurbine, FissionReactor, FusionReactor
}

// What a structure is FOR. The build menu groups by this, so a long catalogue stays navigable and you
// can find "the thing that makes power" without reading every card.
public enum SurfaceBuildingCategory { Government, Harvesting, Industry, Military, Electrical }

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

    // Opts OUT of SurfaceBuildManager.OneOfEachPerWorld — you may build as many of these as you like.
    // The power grid is the reason this exists: a grid whose whole point is chaining relays across a
    // continent is not a feature if the world is allowed exactly one relay. The blanket one-of-each cap
    // is an economy-tuning measure, and it cannot be allowed to cap infrastructure that is meant to be
    // built in quantity. (`uniquePerWorld` still wins — a second capitol is wrong for its own reasons.)
    public bool allowMultiple = false;

    // ---- Power ----
    // How far this structure LIGHTS GROUND, in tiles, measured from any cell of its own footprint.
    // 0 = it doesn't project a grid at all. See PowerGrid.cs: overlapping light is what makes two
    // projectors one grid, so this number is the whole topology of a world's electricity.
    public float powerRange = 0f;

    // What this structure DEMANDS to run at full output. A building on a grid that can't meet its draw
    // browns out; one no grid reaches at all falls back to PowerGrid.UnpoweredFactor.
    public float powerDraw = 0f;

    // Capacitor buffer: how much energy this can bank for its grid. Surplus charges it, a deficit drains
    // it. This is what lets an intermittent grid (solar, wind) carry a load it can't instantaneously meet.
    public float powerStorage = 0f;

    // The tech that unlocks this structure, or null for the ones you start with. Mirrors
    // TerraformProjectInfo.requiredTech — same field name, same null-means-free rule, checked in CanPlace.
    public string requiredTech = null;

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

    // Condition, 0..1 of this building's maximum. Earthquakes on tectonically-active worlds now damage
    // structures near fault lines (see EarthquakeManager), lowering this; a structure driven to 0 is
    // demolished. It scales OutputMult below, so a damaged building really does produce less until it's
    // rebuilt. Round-trips through save/load, so damage persists.
    public float health = 1f;

    // Energy banked in this structure, if it's a capacitor. Held per BUILDING rather than per grid
    // because a grid is derived and has no identity that could survive a save — or a merge. Charge
    // living in the capacitor means a grid's reserve is exactly the capacitors standing on it, so when
    // two grids join, their banks join too, with nothing to reconcile.
    public float stored = 0f;

    // ============================================================================================
    // THE FOOTPRINT THE PLAYER DREW
    //
    // Buildings are no longer fixed tetrominoes: you pick a type and paint the tiles it occupies, and
    // everything about the building — cost, build time, upkeep, output, the Labor it ties up — scales
    // with how many you painted (see BuildScaling).
    //
    // So the shape has to live on the BUILDING rather than on its type. Stored as two flat int lists
    // rather than a List<Vector2Int>: BodyDTO holds PlacedBuilding directly and JsonUtility walks the
    // TYPE graph, so nested container types are exactly the shape that tripped the serialization depth
    // limit on moons (see BodyDTO.parentId). Flat lists have no such graph.
    //
    // EMPTY MEANS "USE THE AUTHORED SHAPE". That is what makes every world built before drawing existed
    // load unchanged, and what lets CityGrowth keep placing settlements programmatically without
    // inventing a footprint for them.
    public List<int> cellsX = new List<int>();
    public List<int> cellsY = new List<int>();

    public bool HasDrawnShape => cellsX != null && cellsY != null
                              && cellsX.Count > 0 && cellsX.Count == cellsY.Count;

    /// How many tiles this building occupies — the number every scaled figure is derived from.
    public int TileCount => HasDrawnShape ? cellsX.Count
                          : (SurfaceBuildingDatabase.Get(Type)?.shape?.Length ?? 1);

    /// Output multiplier from SIZE, on top of efficiency, condition and level.
    ///
    /// This is what makes a big building worth drawing. Without it a ten-tile farm cost twelve times as
    /// much, took twelve times as long, tied up ten Labor — and grew exactly as much food as a one-tile
    /// farm. Strictly worse in every dimension, which would have made the whole drawing mechanic a way
    /// to waste resources.
    ///
    /// 1 for anything not drawn, so every authored footprint and every settlement CityGrowth places
    /// behaves exactly as it always did.
    public float SizeMult => HasDrawnShape ? BuildScaling.OutputMultiplier(TileCount) : 1f;

    public void SetDrawnShape(List<Vector2Int> cells)
    {
        cellsX.Clear(); cellsY.Clear();
        if (cells == null) return;
        foreach (var c in cells) { cellsX.Add(c.x); cellsY.Add(c.y); }
        // The origin stays meaningful — overlays, hover and the power grid all ask "where is this
        // building" — so it is the first painted cell rather than something invented.
        if (cells.Count > 0) { x = cells[0].x; y = cells[0].y; }
    }

    public const int MaxLevel = 3;

    public SurfaceBuildingType Type => (SurfaceBuildingType)type;
    public SurfaceBuildingInfo Info => SurfaceBuildingDatabase.Get(Type);

    /// Output multiplier from tech level: 1.0 / 1.35 / 1.75.
    public float LevelMult => 1f + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 0.375f;

    public int MaxHealth => Mathf.RoundToInt(Info.baseHealth * (1f + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 0.5f));
    public int CurrentHealth => Mathf.RoundToInt(Mathf.Clamp01(health) * MaxHealth);
    public bool CanUpgrade => level < MaxLevel;

    /// Everything that scales a building's production: where you sited it, how good it is, and its
    /// condition — earthquake-damaged structures produce proportionally less until repaired/rebuilt.
    /// SizeMult folded in HERE rather than at each consumer, because there are six of them —
    /// TickOutput, ResearchPerSec, PopGrowthPerSec, PowerGrid's generation and draw, and storage — and
    /// one missed call site is a building that scales its cost but not its yield. Unchanged (x1) for
    /// anything that was not drawn.
    public float OutputMult => Mathf.Clamp01(efficiency) * LevelMult * Mathf.Clamp01(health) * SizeMult;

    /// Repair the record after a load. JsonUtility fills MISSING fields with 0, so a save written
    /// before levels/health existed comes back as level 0 / health 0 — a dead, non-existent tier.
    public void NormalizeAfterLoad()
    {
        if (level < 1) level = 1;
        if (level > MaxLevel) level = MaxLevel;
        if (health <= 0f) health = 1f;
        health = Mathf.Clamp01(health);

        // An empty capacitor is a legitimate state, so unlike level/health there's nothing to repair
        // here — 0 means 0. Only guard against a negative, and against a save written when this thing
        // held more than the current tier allows.
        //
        // The bounds check guards the line below it: `Info` indexes the database by this ordinal, so a
        // record from a LATER build would throw rather than be normalized. The loader drops such records
        // before it gets here (GameStateSerializer), so this is the belt to that braces — it keeps this
        // method safe to call on a record from anywhere, not only from a load that already screened it.
        if (stored < 0f) stored = 0f;
        if (type < 0 || type >= SurfaceBuildingDatabase.All.Length) return;
        float cap = Info.powerStorage * LevelMult;
        if (stored > cap) stored = cap;
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
            "Feeds the colony and grows its population. Wants the greenest ground you can find — check the Fertile Index. Irrigation and processing need a modest amount of power, so keep it in reach of the grid.",
            S(0, 0, 1, 0, 0, 1, 1, 1, 0, 2, 1, 2), SurfaceIndexKind.Fertile, 45, 25, 14f, new Color(0.35f, 0.80f, 0.30f))
        { waterPerSec = 0.3f, popGrowthPerSec = 1.8f };

        // O-tetromino — a compact 2x2 plant.
        _all[(int)SurfaceBuildingType.GeothermalPlant] = new SurfaceBuildingInfo(SurfaceBuildingType.GeothermalPlant, SurfaceBuildingCategory.Harvesting, "Geothermal Plant",
            "Taps the heat under the crust. Sited on a volcano or a geyser field it is the best power in the game; sited on cold rock it is a waste of metal. Check the Heat Index.",
            S(0, 0, 1, 0, 0, 1, 1, 1), SurfaceIndexKind.Heat, 90, 40, 20f, new Color(1.00f, 0.45f, 0.15f))
        { energyPerSec = 2.6f };

        // I-tetromino — a long array of panels.
        _all[(int)SurfaceBuildingType.SolarArray] = new SurfaceBuildingInfo(SurfaceBuildingType.SolarArray, SurfaceBuildingCategory.Harvesting, "Solar Array",
            "A run of panels. Thin air and long polar days beat equatorial noon, and dry ground is cloudless ground. Output falls a quarter for every atmosphere above Earth-normal and reaches nothing at five, so a thick-skied world cannot use them at all. Check the Solar Index.",
            S(0, 0, 1, 0, 2, 0, 3, 0), SurfaceIndexKind.Solar, 55, 20, 12f, new Color(0.95f, 0.90f, 0.45f))
        { energyPerSec = 1.5f };

        // T-tetromino.
        _all[(int)SurfaceBuildingType.WindFarm] = new SurfaceBuildingInfo(SurfaceBuildingType.WindFarm, SurfaceBuildingCategory.Harvesting, "Wind Farm",
            "Turbines, which need AIR to turn: an airless world has no weather at all and a thick-aired one is a gale everywhere. Within that, wants exposure — ridgelines, coasts and open steppe. Check the Weather Index.",
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

        // ================= ELECTRICAL =================
        // The power grid (PowerGrid.cs). Two kinds of thing live here: what MAKES power, and what MOVES
        // or BANKS it. Everything in this category may be built in quantity — a grid built out of
        // relays you're allowed one of is not a grid.

        // The relay. No output of its own; its entire job is REACH. Seven tiles of it, which is what
        // turns two cities' separate grids into one grid, or carries a reactor's output to the mining
        // country on the far side of a mountain.
        var node = new SurfaceBuildingInfo(SurfaceBuildingType.PowerNode, SurfaceBuildingCategory.Electrical,
            "Power Node",
            "A relay pylon. Makes no power at all — it CARRIES it, seven tiles in every direction. Chain them and two isolated grids become one; lose the node in the middle of the chain and they are two again. The cheapest tile on this list and the one that decides the shape of everything else.",
            S(0, 0), SurfaceIndexKind.None, 30, 20, 8f, new Color(0.30f, 0.75f, 1.00f));
        node.powerRange = 7f;
        node.allowMultiple = true;
        _all[(int)SurfaceBuildingType.PowerNode] = node;

        // The bank. Turns an intermittent grid into a grid you can actually build on.
        var cap = new SurfaceBuildingInfo(SurfaceBuildingType.Capacitor, SurfaceBuildingCategory.Electrical,
            "Capacitor Bank",
            "Stores power for the grid it stands on. Surplus charges it; a shortfall drains it. This is what lets a grid running on sun and wind carry a load it cannot meet at every instant — without one, the moment demand passes generation everything on the grid browns out together.",
            S(0, 0, 1, 0, 2, 0), SurfaceIndexKind.None, 60, 40, 12f, new Color(0.45f, 0.90f, 0.95f));
        cap.powerStorage = 240f;
        cap.allowMultiple = true;
        _all[(int)SurfaceBuildingType.Capacitor] = cap;

        // L-tromino, same corner piece as the mine — it's the same industry, really.
        var comb = new SurfaceBuildingInfo(SurfaceBuildingType.CombustionPlant, SurfaceBuildingCategory.Electrical,
            "Combustion Plant",
            "Burns whatever is under it — coal, peat, timber. Primitive, dirty and cheap, and it will light a young colony's first grid on ground that could never support a reactor. Wants something worth burning: it reads the Mineral Index.",
            S(0, 0, 0, 1, 1, 0), SurfaceIndexKind.Mineral, 40, 10, 10f, new Color(0.75f, 0.45f, 0.25f));
        comb.energyPerSec = 1.0f;
        comb.powerRange = 1.5f;
        comb.allowMultiple = true;
        _all[(int)SurfaceBuildingType.CombustionPlant] = comb;

        // Domino.
        var steam = new SurfaceBuildingInfo(SurfaceBuildingType.SteamTurbine, SurfaceBuildingCategory.Electrical,
            "Steam Turbine",
            "A boiler hall and turbine. Needs water to raise steam with, so it wants a river or a coast — check the Hydro Index. Solid, unglamorous, moderate power anywhere wet.",
            S(0, 0, 0, 1), SurfaceIndexKind.Water, 70, 30, 16f, new Color(0.80f, 0.82f, 0.86f));
        steam.energyPerSec = 1.8f;
        steam.powerRange = 1.5f;
        steam.allowMultiple = true;
        _all[(int)SurfaceBuildingType.SteamTurbine] = steam;

        // Z-tetromino.
        var fission = new SurfaceBuildingInfo(SurfaceBuildingType.FissionReactor, SurfaceBuildingCategory.Electrical,
            "Fission Reactor",
            "A pressurised-water pile. Doesn't care what it sits on and doesn't care about the weather — the first generator that makes a grid genuinely reliable rather than merely present.",
            S(0, 1, 1, 1, 1, 0, 2, 0), SurfaceIndexKind.None, 120, 80, 22f, new Color(0.55f, 0.95f, 0.45f));
        fission.energyPerSec = 2.4f;
        fission.powerRange = 1.5f;
        fission.allowMultiple = true;
        fission.requiredTech = "F1";
        _all[(int)SurfaceBuildingType.FissionReactor] = fission;

        // O-tetromino.
        var fusion = new SurfaceBuildingInfo(SurfaceBuildingType.FusionReactor, SurfaceBuildingCategory.Electrical,
            "Fusion Reactor",
            "The real thing. Terrain-independent, weather-independent, and worth more than any two other plants on this list put together. Expensive enough that where you put it — and what you wire it to — matters.",
            S(0, 0, 1, 0, 0, 1, 1, 1), SurfaceIndexKind.None, 200, 150, 30f, new Color(0.60f, 0.85f, 1.00f));
        fusion.energyPerSec = 4.0f;
        fusion.powerRange = 1.5f;
        fusion.allowMultiple = true;
        fusion.requiredTech = "F2";
        _all[(int)SurfaceBuildingType.FusionReactor] = fusion;

        // ---- Power: who makes it, who moves it, who eats it ----
        //
        // GENERATORS light their own footprint and the ring around it (range 1.5 reaches the diagonals
        // too). That's deliberately a very short reach: a plant powers what's built AROUND it, and
        // getting its output anywhere else is what Power Nodes are for.
        //
        // INVARIANT: everything with energyPerSec > 0 must also have powerRange > 0, or its output
        // would have no grid to land in. Enforced below rather than left to whoever adds the next plant.
        Project(SurfaceBuildingType.GeothermalPlant, 1.5f);
        Project(SurfaceBuildingType.SolarArray, 1.5f);
        Project(SurfaceBuildingType.WindFarm, 1.5f);
        Project(SurfaceBuildingType.HydroPlant, 1.5f);

        // The switchyard was already the "pack your plants tightly" building. Now it relays as well —
        // modestly, three tiles, so it's a local tidy-up rather than a substitute for a node chain.
        Project(SurfaceBuildingType.PowerDistribution, 3f);

        // THE SEATS OF GOVERNMENT GENERATE, BUT THEY LIGHT ONLY THEIR OWN DOORSTEP.
        //
        // Both the landed colony ship and the capitol it upgrades into carry a reactor — a settled world
        // is never entirely dark — but each reaches exactly one tile.
        //
        // These used to project a fourteen-tile colony-scale disc, on the reasoning that a new colony
        // must be able to make a first move. In practice it meant a developed world never had a reason
        // to build a power plant at all: the capitol powered everything that mattered, forever, and the
        // whole grid system was decoration on the world you cared most about. One tile makes them
        // buildings that need connecting like any other, and makes the first power plant a real
        // decision rather than a formality.
        //
        // They MATCH each other deliberately. While the base was wide and the capitol narrow, upgrading
        // the base collapsed a colony's grid from fourteen tiles to one and blacked out everything built
        // inside the old radius — a trap laid for the player by their own progress.
        Reactor(SurfaceBuildingType.ColonyShipBase, 0.8f, 1f);
        Reactor(SurfaceBuildingType.PlanetCapitol, 1.2f, 1f);

        // Cities carry their own generation and light their own ground, so an inhabited world grows a
        // grid whether you planned one or not — and that grid is usually the one you end up hanging
        // your industry off.
        Project(SurfaceBuildingType.City, 1.5f);
        Project(SurfaceBuildingType.Spaceport, 1.5f);

        // ---- CONSUMERS: industry, and farmland ----
        //
        // HOUSING and the grown settlements draw NOTHING, which is a decision rather than an oversight.
        // Power is a new axis on a game that already has colonies in it; hanging it on POPULATION would
        // mean every world that existed before this system did — and every world whose people live
        // somewhere other than next to a reactor — quietly loses most of its growth, with the cause
        // invisible and the fix unavailable. It would also feed back on itself, since it's population
        // that builds the plants that would fix it. The spec's own line is that city blocks still work
        // as housing without power; they just don't do the extra.
        //
        // FARMLAND now draws, which this note previously called out as the one thing the rule got wrong
        // ("a world where farms don't care about electricity"). Irrigation, lighting and processing are
        // exactly the sort of thing a farm spends power on, and a farm is a DELIBERATE PLACEMENT with a
        // grid overlay in front of the player while they site it — so the cost is visible at the moment
        // the decision is made, which is the thing an invisible population tax could never be.
        //
        // It is set deliberately LOW — below a Mine, the cheapest industrial draw — and PowerFactor
        // floors a starved building rather than zeroing it, so an off-grid farm is a poor farm and never
        // a dead one. That keeps the feedback loop the paragraph above worries about from closing: a
        // colony whose grid collapses grows slowly, and can still grow its way back out.
        //
        // StorageDepot is left out for a different reason: its only output is storageCapacity, which
        // PlayerEconomy sums straight off the building and PowerFactor never touches. Giving it a draw
        // would be a tax with no mechanism behind it — an unpowered depot would still hold exactly as
        // much, so the only thing the player could observe is the bill.
        Draw(SurfaceBuildingType.SurfaceShipyard, 2.0f);
        Draw(SurfaceBuildingType.Factory, 1.6f);
        Draw(SurfaceBuildingType.Spaceport, 1.5f);
        Draw(SurfaceBuildingType.Refinery, 1.4f);
        Draw(SurfaceBuildingType.ResearchOutpost, 1.2f);
        Draw(SurfaceBuildingType.Mine, 0.8f);
        Draw(SurfaceBuildingType.Farm, 0.4f);

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
        Require(SurfaceBuildingType.CombustionPlant, 0.12f,
            "Needs something under it worth burning. Bare ice and open ocean have no fuel in them.");
        Require(SurfaceBuildingType.SteamTurbine, 0.15f,
            "Needs water to raise steam with. A desert has nothing to boil.");

        // The invariant from the power block above, enforced rather than trusted: a plant that made
        // power but lit no ground would pour its output into a grid that doesn't exist, and the loss
        // would be silent. Fail loudly at startup instead — this is a data error, not a game state.
        foreach (var info in _all)
            if (info != null && info.energyPerSec > 0f && info.powerRange <= 0f)
                Debug.LogError($"SurfaceBuildingDatabase: {info.name} generates {info.energyPerSec}/s but has no " +
                               $"powerRange, so its output has no grid to land in. Give it Project(...).");
    }

    static void Require(SurfaceBuildingType t, float minIndex, string why)
    {
        var info = _all[(int)t];
        if (info == null) return;
        info.minIndex = minIndex;
        info.siteRequirement = why;
    }

    /// This structure lights the ground around it, out to `range` tiles.
    static void Project(SurfaceBuildingType t, float range)
    {
        var info = _all[(int)t];
        if (info != null) info.powerRange = range;
    }

    /// This structure carries its own plant: it generates, and lights its own ground.
    static void Reactor(SurfaceBuildingType t, float energyPerSec, float range)
    {
        var info = _all[(int)t];
        if (info == null) return;
        info.energyPerSec = energyPerSec;
        info.powerRange = range;
    }

    /// This structure needs `mw` of grid power to run at full output.
    static void Draw(SurfaceBuildingType t, float mw)
    {
        var info = _all[(int)t];
        if (info != null) info.powerDraw = mw;
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

    /// The cells a PLACED building actually occupies.
    ///
    /// A drawn building carries its OWN footprint — the player painted it — so it is read back rather
    /// than re-derived from its type's authored shape. Anything without stored cells falls back to that
    /// shape, which is what keeps every world built before drawing existed standing exactly as it was,
    /// and what lets the grown settlements (placed programmatically by CityGrowth, never drawn) keep
    /// working unchanged.
    public static List<Vector2Int> Footprint(PlacedBuilding p)
    {
        if (p != null && p.HasDrawnShape)
        {
            var drawn = new List<Vector2Int>(p.cellsX.Count);
            for (int i = 0; i < p.cellsX.Count && i < p.cellsY.Count; i++)
                drawn.Add(new Vector2Int(p.cellsX[i], p.cellsY[i]));
            return drawn;
        }
        return Footprint(p.Type, p.x, p.y, p.rotation);
    }
}
