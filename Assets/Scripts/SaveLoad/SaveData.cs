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

    // Space background settings (kept constant per map).
    public int bgSeed = 12345;
    public bool bgEnabled = true;
    public bool bgSolid = false;
    public float bgR = 0.02f, bgG = 0.03f, bgB = 0.06f;
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
    public float tScale = 1f, tElev = 1f, tMoist = 1f, tHeat = 1f, tRidge = 1f; // terrain params

    public float orbitRadius, orbitSpeed, orbitPhase;
    public int orbitDirection;
    public float inclination, eccentricity, verticalOffset, spinSpeed;
    public bool showRing;

    public float distanceFromStar, habitability;
    public bool isHabitable;

    public List<ResourceDTO> resources = new List<ResourceDTO>();
    public List<OreCellDTO> ores = new List<OreCellDTO>();
    public List<POIDTO> pois = new List<POIDTO>();
    public List<BodyDTO> moons = new List<BodyDTO>();
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
}
