using System.Collections.Generic;
using UnityEngine;

// Structures placed ON a planet's surface grid, as opposed to the abstract colony buildings.
// IMPORTANT: append only — the ordinal is serialized.
public enum SurfaceBuildingType
{
    Mine, Farm, GeothermalPlant, SolarArray, WindFarm,
    Habitat, Factory, ResearchOutpost, Spaceport, Refinery
}

// One surface structure: its footprint SHAPE, what index makes it efficient, and what it produces.
//
// Footprints are tetromino-like so that developing a world is a packing puzzle: an L-shaped mine can be
// rotated to tuck around a 2x2 geothermal plant, and a dense city is one you fitted together well.
public class SurfaceBuildingInfo
{
    public SurfaceBuildingType type;
    public string name;
    public string description;

    // Footprint cells, relative to the building's origin, before rotation.
    public Vector2Int[] shape;

    // The survey index that decides how well this building performs where you put it. None = the
    // building doesn't care about terrain (a habitat is a habitat anywhere).
    public SurfaceIndexKind index = SurfaceIndexKind.None;

    public int costMetal, costEnergy;
    public float buildTime;

    // Output at 100% efficiency, per second. Efficiency is the average of `index` across the footprint,
    // so WHERE you put it matters as much as whether you built it.
    public float metalPerSec, energyPerSec, waterPerSec, researchPerSec, popGrowthPerSec;

    public bool allowsWater = false;   // most structures need dry ground

    public Color color;                // how it reads on the map

    public SurfaceBuildingInfo(SurfaceBuildingType t, string n, string d, Vector2Int[] shape,
                               SurfaceIndexKind index, int cm, int ce, float bt, Color color)
    {
        type = t; name = n; description = d; this.shape = shape; this.index = index;
        costMetal = cm; costEnergy = ce; buildTime = bt; this.color = color;
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

    public SurfaceBuildingType Type => (SurfaceBuildingType)type;
    public SurfaceBuildingInfo Info => SurfaceBuildingDatabase.Get(Type);
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

        // L-tromino — tucks into the corner left by a square plant.
        _all[(int)SurfaceBuildingType.Mine] = new SurfaceBuildingInfo(SurfaceBuildingType.Mine, "Mine",
            "Digs metal out of the ground. Put it on a rich seam: its yield is set by the Mineral Index under its footprint, and never changes once built.",
            S(0, 0, 0, 1, 1, 0), SurfaceIndexKind.Mineral, 60, 30, 16f, new Color(0.62f, 0.42f, 0.20f))
        { metalPerSec = 1.6f };

        // 2x3 block — farms want a lot of contiguous ground.
        _all[(int)SurfaceBuildingType.Farm] = new SurfaceBuildingInfo(SurfaceBuildingType.Farm, "Farmland",
            "Feeds the colony and grows its population. Wants the greenest ground you can find — check the Fertile Index.",
            S(0, 0, 1, 0, 0, 1, 1, 1, 0, 2, 1, 2), SurfaceIndexKind.Fertile, 45, 25, 14f, new Color(0.35f, 0.80f, 0.30f))
        { waterPerSec = 0.3f, popGrowthPerSec = 1.8f };

        // O-tetromino — a compact 2x2 plant.
        _all[(int)SurfaceBuildingType.GeothermalPlant] = new SurfaceBuildingInfo(SurfaceBuildingType.GeothermalPlant, "Geothermal Plant",
            "Taps the heat under the crust. Sited on a volcano or a geyser field it is the best power in the game; sited on cold rock it is a waste of metal. Check the Heat Index.",
            S(0, 0, 1, 0, 0, 1, 1, 1), SurfaceIndexKind.Heat, 90, 40, 20f, new Color(1.00f, 0.45f, 0.15f))
        { energyPerSec = 2.6f };

        // I-tetromino — a long array of panels.
        _all[(int)SurfaceBuildingType.SolarArray] = new SurfaceBuildingInfo(SurfaceBuildingType.SolarArray, "Solar Array",
            "A run of panels. Wants dry, cloudless, open ground — savanna and desert are ideal. Check the Weather Index.",
            S(0, 0, 1, 0, 2, 0, 3, 0), SurfaceIndexKind.Weather, 55, 20, 12f, new Color(0.95f, 0.90f, 0.45f))
        { energyPerSec = 1.5f };

        // T-tetromino.
        _all[(int)SurfaceBuildingType.WindFarm] = new SurfaceBuildingInfo(SurfaceBuildingType.WindFarm, "Wind Farm",
            "Turbines. Wants exposure — ridgelines, coasts and open steppe. Check the Weather Index.",
            S(0, 0, 1, 0, 2, 0, 1, 1), SurfaceIndexKind.Weather, 50, 25, 12f, new Color(0.70f, 0.90f, 1.00f))
        { energyPerSec = 1.3f };

        // S-tetromino — awkward on purpose; it's the piece that forces you to plan.
        _all[(int)SurfaceBuildingType.Habitat] = new SurfaceBuildingInfo(SurfaceBuildingType.Habitat, "Habitat Block",
            "Housing. Doesn't care what it sits on — put it wherever the awkward gaps are.",
            S(0, 0, 1, 0, 1, 1, 2, 1), SurfaceIndexKind.None, 40, 30, 12f, new Color(0.55f, 0.70f, 0.95f))
        { popGrowthPerSec = 1.2f };

        // 3x2 block.
        _all[(int)SurfaceBuildingType.Factory] = new SurfaceBuildingInfo(SurfaceBuildingType.Factory, "Factory",
            "Turns raw metal into more of it. Large, and indifferent to terrain.",
            S(0, 0, 1, 0, 2, 0, 0, 1, 1, 1, 2, 1), SurfaceIndexKind.None, 110, 70, 24f, new Color(0.75f, 0.55f, 0.85f))
        { metalPerSec = 1.1f };

        // J-tetromino.
        _all[(int)SurfaceBuildingType.ResearchOutpost] = new SurfaceBuildingInfo(SurfaceBuildingType.ResearchOutpost, "Research Outpost",
            "A surface laboratory feeding research to the empire. Terrain-agnostic.",
            S(0, 0, 0, 1, 0, 2, 1, 2), SurfaceIndexKind.None, 80, 70, 20f, new Color(0.45f, 0.85f, 1.00f))
        { researchPerSec = 0.6f };

        // Big square — needs real space cleared for it.
        _all[(int)SurfaceBuildingType.Spaceport] = new SurfaceBuildingInfo(SurfaceBuildingType.Spaceport, "Spaceport",
            "Ground-to-orbit traffic. Big, flat and hungry for space — plan the city around it.",
            S(0, 0, 1, 0, 2, 0, 0, 1, 1, 1, 2, 1, 0, 2, 1, 2, 2, 2), SurfaceIndexKind.None, 180, 140, 32f,
            new Color(0.85f, 0.85f, 0.90f))
        { metalPerSec = 0.4f, energyPerSec = 0.4f };

        // Z-tetromino.
        _all[(int)SurfaceBuildingType.Refinery] = new SurfaceBuildingInfo(SurfaceBuildingType.Refinery, "Refinery",
            "Refines ore on site. Best next to the rock it's processing — it reads the Mineral Index too.",
            S(1, 0, 2, 0, 0, 1, 1, 1), SurfaceIndexKind.Mineral, 95, 60, 22f, new Color(0.80f, 0.65f, 0.35f))
        { metalPerSec = 1.2f };
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
