using System.Collections.Generic;

// Plain serializable DTOs for JsonUtility. No dictionaries / 2D arrays / polymorphism.
// Terrain is NOT stored tile-by-tile: it regenerates deterministically from terrainSeed, so we only
// persist the seed plus the randomly-placed ores and points of interest.

[System.Serializable]
public class SaveGame
{
    public string saveName;
    public string savedAtIso;
    public string summary;          // short human description for the load list
    public int speciesIndex = 0;
    public int difficulty = 1;              // 0 easy, 1 medium, 2 hard
    public string factionName = "Your Empire";
    public int homeIndex = 0;
    public float timeScale = 1f;
    public List<SystemDTO> galaxySystems = new List<SystemDTO>();
    public ResearchDTO research = new ResearchDTO();

    // Player stockpile and fleet (ships + deployed stations).
    public float ecoMetal, ecoEnergy, ecoWater;
    public List<UnitDTO> units = new List<UnitDTO>();

    // In-progress work: hulls on the stocks and technologies under study, with their progress, order
    // and pause state, so a reload resumes exactly where it left off.
    public List<BuildOrderDTO> buildQueue = new List<BuildOrderDTO>();
    public List<ResearchOrderDTO> researchQueue = new List<ResearchOrderDTO>();
    public bool researchPaused = false;
    public bool organicCityGrowth = true;                   // the player's taste toggle, saved with the game
    public List<TerraformJobDTO> terraformJobs = new List<TerraformJobDTO>();
    public List<ControlGroupDTO> controlGroups = new List<ControlGroupDTO>();

    // Space background settings (kept constant per map).
    public int bgSeed = 12345;
    public bool bgEnabled = true;
    public bool bgSolid = false;
    public float bgR = 0.02f, bgG = 0.03f, bgB = 0.06f;
}

// One ship on the shipyard stocks (see BuildOrder). Order in the list is the power-allocation order.
[System.Serializable]
public class BuildOrderDTO
{
    public int type;
    public float elapsed, duration;
    public bool paused;
    public int metalPaid, energyPaid;   // exact refund if the player cancels it
}

// One technology under study (see ResearchOrder). Order in the list is the capacity-allocation order.
[System.Serializable]
public class ResearchOrderDTO
{
    public string id;
    public float progress;
    public bool paused;
}

// One numbered fleet control group (see ControlGroups): the unit ids bound to Ctrl+N.
[System.Serializable]
public class ControlGroupDTO
{
    public int group;
    public List<int> unitIds = new List<int>();
}

// One planetary-engineering project under way on a world (see TerraformJob).
[System.Serializable]
public class TerraformJobDTO
{
    public int bodyId;
    public int type;
    public float elapsed, duration;
    public bool paused;
    public int metalPaid, energyPaid, waterPaid;

    // Animated orbit migration (see TerraformJob). Default -1 so a pre-feature save deserializes as
    // "not an orbit migration" and completes via the legacy instant jump. JsonUtility leaves an absent
    // field at this initializer value, so old saves stay correct.
    public float orbitStart = -1f, orbitTarget = -1f;
}

[System.Serializable]
public class SystemDTO
{
    public string name;
    public float px, py, pz;                 // galaxy position
    public List<int> starTypes = new List<int>();
    public bool isBlackHole;
    public int ownerId = -1;                 // -1 == unclaimed
    public bool isHome;
    public List<BodyDTO> bodies = new List<BodyDTO>();
}

[System.Serializable]
public class BodyDTO
{
    public int id;
    public string name;
    public int type;
    public int ownerId = -1;                 // -1 == unclaimed
    public bool habitabilityLocked;
    public int surfaceSize;
    public float terrainSeed;
    public float continentFrequency;

    // The seed the world was generated with. Persisted so "Reset to default" can restore it after the
    // live seed has been rerolled in the Dev sandbox. Zero means a save written before this existed —
    // the loader falls back to terrainSeed there.
    public float naturalSeed;
    public float mass;                 // Mass Value (the player-facing size); surfaceSize derives from it

    // The world's UNTOUCHED climate. Must persist: terraforming lerps terrainParams away from this, so
    // re-deriving it on load would capture the already-terraformed values as "natural" and freeze all
    // further progress. Zero means a save written before this existed — see the loader.
    public float nScale, nElev, nMoist, nHeat, nRidge;
    public float tScale = 1f, tElev = 1f, tMoist = 1f, tHeat = 1f, tRidge = 1f; // terrain params

    public float orbitRadius, orbitSpeed, orbitPhase;
    public float naturalOrbitRadius;   // the generated orbit, for Dev-Mode "Reset orbit/system"
    public int orbitDirection;
    public float inclination, eccentricity, verticalOffset, spinSpeed;
    public bool showRing;

    public float distanceFromStar, habitability;
    public bool isHabitable;

    // Colony / development state.
    public List<int> buildings = new List<int>();
    public int shipyardLevel;
    public int researchCenterLevel;
    public int population;
    public int cities;
    public bool terraforming;
    public bool biosphereActive;    // did this world generate with (or get Microbial-Seeded into) plant life
    public float atmosphereThickness;   // 0 (vacuum) .. 1 (crushingly thick) — see AtmosphereRules
    public bool hasTectonics;       // active plate tectonics — see TectonicsRules
    public float terraformability;
    public List<int> terraformProjects = new List<int>();   // completed TerraformProjectType ids
    public List<PlacedBuilding> placedBuildings = new List<PlacedBuilding>();   // surface-grid structures
    public bool deepSurveyed;                               // unlocks the Heat/Fertile/Wind/Solar/Water indexes
    public float cityGrowthTimer;                           // progress toward this world's next settlement
    public bool birthrightClaim;
    public bool settled;            // people live here (Claim.cs). Distinct from owning it.
    public bool visited;
    public float explorationProgress;

    public List<ResourceDTO> resources = new List<ResourceDTO>();
    public List<OreCellDTO> ores = new List<OreCellDTO>();
    public List<POIDTO> pois = new List<POIDTO>();
    // Moons are stored FLAT in SystemDTO.bodies and linked back by this id (-1 = a top-level planet).
    //
    // They used to nest as a List<BodyDTO> inside BodyDTO. Unity's JsonUtility walks the TYPE graph, so
    // a class containing a list of ITSELF recurses forever and trips its hard depth limit of 10 —
    // "Serialization depth limit 10 exceeded at 'BodyDTO.buildings'" on every single save and load.
    // A flat list with a parent id has no recursive type, so the limit is never reached.
    public int parentId = -1;
}

[System.Serializable]
public class ResourceDTO
{
    public int type;
    public float amount;
}

[System.Serializable]
public class OreCellDTO
{
    public int x, y;
    public int ore;
    public float richness;
}

[System.Serializable]
public class POIDTO
{
    public int type;
    public float u, v;
    public string title;
    public string description;
    public bool explored;
    public int relatedOre;
    public string revealTitle;
    public string revealText;
    public string kind;
    public float researchDuration;
    public string reportText;
}

[System.Serializable]
public class ResearchDTO
{
    public List<int> discovered = new List<int>();
    public List<int> researched = new List<int>();
    public int points;
    public int empireLevel = 1;
    public List<string> tech = new List<string>();   // researched tech-tree node ids
    public int schematics;                            // ancient schematics recovered
}

// A ship or deployed station.
[System.Serializable]
public class UnitDTO
{
    public int id;
    public int type;
    public bool isPlayer = true;
    public int locationId = -1;      // body it is at (-1 = in open space)
    public bool inSpace;
    public float px, py, pz;         // park position when in open space
    public float experience;
    public int worldsExplored;
    public float serviceTime;
    public bool queuePaused;
    public List<int> samples = new List<int>();
    public List<OrderDTO> orders = new List<OrderDTO>();
}

// One queued ship order.
[System.Serializable]
public class OrderDTO
{
    public int kind;
    public int targetId = -1;        // target body (-1 = a point in space)
    public bool isPoint;
    public float px, py, pz;
}
