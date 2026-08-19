using System.Collections.Generic;
using UnityEngine;

// Structures a colony can build once it has a city. Each produces resources per second (scaled by
// population) and/or unlocks a capability. Ids are stored on CelestialBody.buildings as ints.
public enum BuildingType { City, Farm, Mine, PowerPlant, ResearchCenter, Shipyard }

public class BuildingInfo
{
    public BuildingType type;
    public string name;
    public string description;
    public int costMetal, costEnergy;
    public float buildTime;

    // Per-second output (before the population multiplier).
    public float metalPerSec, energyPerSec, waterPerSec, researchPerSec;
    public float popGrowthPerSec;    // Farms feed population growth

    // `researchFacility` and `shipyard` USED TO LIVE HERE, and both are gone rather than deprecated,
    // because both had already stopped being read by anything.
    //
    // The capabilities they described did not disappear — they moved onto the STRUCTURES, where a
    // capability now has to be standing somewhere on the map to count. Ore-sample research is gated on
    // `CelestialBody.researchCenterLevel` (ColonyManager.TickColony), shipbuilding on
    // `shipyardLevel`, and SurfaceBuildManager.SyncFacilityTiers derives BOTH from the surface
    // ResearchCenter and SurfaceShipyard actually built on the world. So the answer to "does this world
    // research samples" is a thing you can point at on the surface grid, which is the whole direction
    // the facility rework went in.
    //
    // Keeping two unread booleans that name real capabilities is worse than having neither: the next
    // person to add a capability finds them, assumes this is where capabilities are declared, and adds a
    // third one nothing reads.

    public BuildingInfo(BuildingType t, string n, string d, int cm, int ce, float bt)
    { type = t; name = n; description = d; costMetal = cm; costEnergy = ce; buildTime = bt; }
}

public static class BuildingDatabase
{
    static BuildingInfo[] _all;

    public static BuildingInfo Get(BuildingType t) { if (_all == null) Build(); return _all[(int)t]; }
    public static BuildingInfo[] All { get { if (_all == null) Build(); return _all; } }

    static void Build()
    {
        _all = new BuildingInfo[6];

        _all[(int)BuildingType.City] = new BuildingInfo(BuildingType.City, "Colony City",
            "The heart of the colony, founded by sacrificing a colony ship. Houses the population and yields a trickle of every resource.",
            0, 0, 0f)
        { metalPerSec = 0.25f, energyPerSec = 0.25f, waterPerSec = 0.2f, popGrowthPerSec = 0.6f };

        _all[(int)BuildingType.Farm] = new BuildingInfo(BuildingType.Farm, "Farms / Crops",
            "Feeds the colony so its population grows faster; also yields a little water.",
            40, 20, 12f)
        { waterPerSec = 0.3f, popGrowthPerSec = 1.6f };

        _all[(int)BuildingType.Mine] = new BuildingInfo(BuildingType.Mine, "Mine",
            "Extracts metal from the crust (richer on ore-heavy worlds).",
            60, 30, 16f)
        { metalPerSec = 1.4f };

        _all[(int)BuildingType.PowerPlant] = new BuildingInfo(BuildingType.PowerPlant, "Power Plant",
            "Generates energy for the colony and its industry.",
            50, 40, 14f)
        { energyPerSec = 1.6f };

        _all[(int)BuildingType.ResearchCenter] = new BuildingInfo(BuildingType.ResearchCenter, "Research Centre",
            "Generates research. Ore-sample analysis is done by the research campus standing on the surface — this " +
            "entry is the legacy colony-level record of one.",
            80, 70, 22f)
        { researchPerSec = 0.7f };

        _all[(int)BuildingType.Shipyard] = new BuildingInfo(BuildingType.Shipyard, "Shipyard",
            "Lets you build ships at this world. The yard that actually does it is the one on the surface; " +
            "this entry is the legacy colony-level record of one.",
            120, 90, 26f);
    }
}
