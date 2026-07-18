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

    // The terrain seed the world was GENERATED with, captured once at generation and never touched
    // after. terrainSeed itself can be rerolled live in the Dev Mode terrain sandbox (Randomize), so
    // this is the only record of the world you started with — it's what "Reset to default" restores.
    public float naturalSeed = 0f;

    // The climate nature gave this world, before any terraforming moved it. Terraforming lerps
    // terrainParams from HERE toward what the species would build for itself (TerraformVisuals), so this
    // has to be the untouched original — once terrainParams starts moving, the origin is gone otherwise.
    public PlanetTerrainGenerator.NoiseParams naturalParams = PlanetTerrainGenerator.NoiseParams.Default;

    // Habitability the last time the surface was actually rebuilt. Regenerating is ~12,000 cells of
    // noise, so it happens per RegenStep of progress rather than per frame.
    [System.NonSerialized] public float lastTerraformRenderHab = -999f;
    public float continentFrequency = 4f;
    public PlanetTerrainGenerator.NoiseParams terrainParams = PlanetTerrainGenerator.NoiseParams.Default;

    // --- Directed remodelling transition (transient render state) ---
    // While a Planetary Remodelling project runs, the surface is dithered from the world's CURRENT type
    // toward remodelToType as remodelT (0..1) rises — so the new world (lava, ocean, ice…) spreads across
    // the old one on the map rather than snapping at completion. NonSerialized: rebuilt from the resumed
    // job on load (the job is saved), and cleared when the project finishes (the world then simply IS the
    // new type, so no dithering is needed).
    [System.NonSerialized] public int remodelToType = -1;   // (CelestialBodyType) ordinal; -1 = no transition
    [System.NonSerialized] public float remodelT = 0f;      // 0..1 transition progress

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
    // TWO STAGES, and they are genuinely different things. See Claim.cs.
    //
    //   CLAIMED  (owner != null)  — the world is legally yours. You surveyed it, you have a ship there,
    //                               and you planted a beacon. It does NOT mean anyone lives there. A
    //                               claim is what lets you terraform a world and keep rivals off it
    //                               while you make it liveable.
    //   SETTLED  (settled)        — people actually live here. Needs the world to be habitable FIRST,
    //                               which for most worlds means terraforming a claim you already hold.
    //
    // `owner` has always meant "claimed" — the home world's moons are owner=Player from turn one and
    // are deliberately NOT settled — but nothing recorded the second half, so "is this world colonised"
    // was inferred from side effects like `cities > 0` or a City in `buildings`. Those are consequences
    // of settling, not the fact of it, and inferring a fact from its consequences is how a moon ended up
    // with a free city (see ColonyManager.TickColony).
    public Faction owner;               // null == unclaimed

    /// People live here. Set by settling; never by owning.
    public bool settled = false;

    // Claimed purely by BIRTHRIGHT (the home world and its moons) rather than by going and doing it.
    // Skips the claim CONDITIONS — it's already yours — but grants no settlement: a birthright moon is
    // a claim you still have to make liveable.
    public bool birthrightClaim = false;

    // Shipyard tier on this world (0 = none, 1-5). Building a Shipyard sets it to 1; it can be upgraded
    // to 2 (unlocks Mk II ships) and 3 (unlocks the Terraformer). Higher tiers build ships faster and,
    // above all, grant more BUILD POWER — how many ships this yard can work on at once (see BuildPower).
    public int shipyardLevel = 0;

    // Research centre tier on this world (0 = none, 1-5). Building a Research Centre sets it to 1.
    // Higher tiers grant more RESEARCH CAPACITY — how many technologies can be studied at once, or how
    // big a single project can be (see ResearchCapacity).
    public int researchCenterLevel = 0;

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
    // Population in UNITS of 100,000 people (see Population). A homeworld starts around 10 — a million.
    public int population = 0;

    // Fractional people waiting to become a whole unit. Growth is far slower than one unit per tick, so
    // without this the remainder is discarded every frame and a colony never grows at all — or, as the
    // original code did, is rounded up to a whole unit and grows 100,000 people a second.
    [System.NonSerialized] public float popAccum = 0f;
    public int cities = 0;

    // --- Colony ---
    public List<int> buildings = new List<int>();   // BuildingType ids constructed on this world

    // Structures physically placed on the surface GRID (see SurfaceBuildManager) — mines, farms,
    // geothermal plants and so on, each occupying a tetromino-like footprint of tiles. Distinct from
    // `buildings` above, which are the abstract colony-wide facilities.
    public List<PlacedBuilding> placedBuildings = new List<PlacedBuilding>();

    // A DEEP survey (a research ship actually studying the world, not just mapping it from orbit) is
    // what unlocks the Heat, Fertile and Weather indexes. Minerals you can see from orbit; knowing
    // where the geothermal vents are takes someone on the ground.
    public bool deepSurveyed = false;

    // Seconds accumulated toward this world's next organic settlement (see CityGrowth). Per-body so a
    // paradise and a marginal world grow on their own clocks.
    public float cityGrowthTimer = 0f;

    // --- Terraforming ---
    public float terraformability = 0f;      // 0..100 potential to be made livable for the current species

    // Completed TerraformProjectType ids. Each finished project permanently raises this world's
    // habitability CEILING (see TerraformProjects) — melting its ice caps, hanging orbital shades,
    // restarting its core. Terraformers then raise habitability toward that ceiling.
    public List<int> terraformProjects = new List<int>();
    public bool terraforming = false;        // an active terraforming project raising habitability
    public float researchProgress = 0f;      // 0..1 deep-research completion (research ship / centre)

    // Does this world have living plant life at all? Set once at generation time (see
    // BiosphereRules.GeneratesWithBiosphere) for worlds that started out warm and wet enough to sustain
    // one; everything else generates sterile. Meeting the water/temperature conditions LATER through
    // terraforming does not turn this on by itself — a barren world needs Microbial Seeding (or Forest
    // Seeding) to actually take the first step, same as in reality. See BiosphereRules.
    public bool biosphereActive = false;

    // 0 (vacuum) .. 1 (crushingly thick, gas-giant grade). Set once at generation from Size and Type
    // (see AtmosphereRules) — asteroids never hold one, small moons hold none-to-thin, larger bodies
    // hold thicker air. Feeds the Solar/Wind survey indexes and the BioSphere atmosphere gate.
    public float atmosphereThickness = 0f;

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
