using System.Collections.Generic;
using UnityEngine;

public class CelestialBody
{
    public int id;                 // stable id for save/load and parent references
    public string name;
    public CelestialBodyType type;
    public ResourceDeposit resources;
    public int surfaceSize;
    public PlanetSurface surface;  // low-res grid (the "general" viewer + gameplay)
    public List<CelestialBody> moons = new List<CelestialBody>();

    // Terrain field identity. Both the low-res grid and the high-res detailed map are sampled from
    // this same seed/frequency/params, so the two views ALWAYS match (including live edits).
    public float terrainSeed = 0f;
    public float continentFrequency = 4f;
    public PlanetTerrainGenerator.NoiseParams terrainParams = PlanetTerrainGenerator.NoiseParams.Default;

    public List<PointOfInterest> pointsOfInterest = new List<PointOfInterest>();

    // --- Orbit parameters (authoritative data; the OrbitController reads these) ---
    public float orbitRadius = 10f;     // for planets: distance from star; for moons: distance from planet
    public float orbitSpeed = 20f;
    public float orbitPhase = 0f;       // starting angle in degrees
    public int orbitDirection = 1;      // +1 counter-clockwise, -1 clockwise
    public float inclination = 0f;      // orbital tilt in degrees
    public float eccentricity = 0f;     // 0 = circle, up to ~0.6 = ellipse
    public float verticalOffset = 0f;   // lifts the orbit plane up/down
    public float spinSpeed = 0f;        // axial rotation, degrees per second
    public bool showRing = true;

    // --- Habitability (relative to this body's host star) ---
    public float distanceFromStar = 0f; // absolute distance from the star, in orbit units
    public float habitability = 0f;     // 0..100
    public bool isHabitable = false;    // true if physically inside the Goldilocks zone
    public bool habitabilityLocked = false; // home world's difficulty-set rating won't be recomputed

    // --- Ownership ---
    public Faction owner;               // null == unclaimed

    // Claimed purely by BIRTHRIGHT (the home world and its moons). Such bodies count as fully claimed
    // just for being ours from the start — they bypass the usual survey/habitability/population/
    // building objectives that other worlds must satisfy to be fully established.
    public bool birthrightClaim = false;

    // Shipyard tier on this world (0 = none, 1-3). Building a Shipyard sets it to 1; it can be upgraded
    // to 2 (unlocks Mk II ships) and 3 (unlocks the Terraformer). Higher tiers build ships faster.
    public int shipyardLevel = 0;

    // --- Exploration / colonization ---
    public bool visited = false;             // a friendly ship has arrived here at least once
    public float explorationProgress = 0f;   // 0..1 survey completion

    // Reveal stages:
    //  * Visited  (a ship has arrived) -> the low-res mini map becomes viewable.
    //  * Surveyed (survey complete)    -> the detailed map, points of interest and full info unlock.
    public bool Visited  => GameMode.DevMode || visited || owner == FactionManager.Player;
    public bool Surveyed => GameMode.DevMode || explorationProgress >= 1f || owner == FactionManager.Player;

    public float claimProgress = 0f;         // 0..1 colonization toward full claim
    public Faction claimingFaction;          // who is colonizing (null if nobody)
    public int population = 0;
    public int cities = 0;

    // --- Colony ---
    public List<int> buildings = new List<int>();   // BuildingType ids constructed on this world

    // --- Terraforming ---
    public float terraformability = 0f;      // 0..100 potential to be made livable for the current species
    public bool terraforming = false;        // an active terraforming project raising habitability
    public float researchProgress = 0f;      // 0..1 deep-research completion (research ship / centre)

    [System.NonSerialized] public StarData hostStar;          // the star this body belongs to
    [System.NonSerialized] public StarSystemData system;      // the system this body belongs to
    [System.NonSerialized] public List<Unit> units = new List<Unit>();  // units currently here

    [System.NonSerialized]
    public GameObject visualObject;

    [System.NonSerialized]
    public CelestialBody parentBody;    // For moons only

    public CelestialBody(CelestialBodyType type)
    {
        this.type = type;
        this.name = type.ToString();
        this.resources = new ResourceDeposit();
        this.orbitRadius = 10f;
        this.orbitSpeed = 20f;
    }
}
