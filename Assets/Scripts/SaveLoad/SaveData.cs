using System.Collections.Generic;

// Plain serializable DTOs for JsonUtility. No dictionaries / 2D arrays / polymorphism.
//
// ---- WHAT A SAVE IS FOR --------------------------------------------------------------------
// Loading a save must give back the game that was saved. Not a game generated from the same seeds —
// THE SAME GAME. Those two used to be the same thing and quietly stopped being it: terrain, plates
// and everything downstream of them were re-derived on load from `terrainSeed`, so the day a
// generation algorithm was improved, every existing save's continents moved. Buildings kept their
// grid coordinates while the ground under them changed biome; the tectonic overlay stopped agreeing
// with the mountains it had raised.
//
// So DERIVED STATE THE PLAYER CAN SEE IS NOW STORED, and the loader trusts the save over the
// generator. That is `terrain` (the biome of every cell) and `tectonics` (the plate layout) below.
// Both are compact — see GridCodec for why the terrain grid costs kilobytes rather than megabytes.
//
// The seeds are still stored and still used: for a body a save predates, for a world the Dev sandbox
// rerolls, and for everything genuinely cosmetic and stable — per-tile shade, the survey index
// overlays — which are pure functions of position and seed and have no memory to lose.
//
// A field added here must ALSO be readable when absent, because older saves exist. The convention
// throughout is a sentinel the generator cannot produce (-1 for a value that is legally 0, an empty
// string, an empty list) plus a fallback in GameStateSerializer that says what an old save meant.

/// Just the fields the load menu shows. JsonUtility ignores everything else in the file, so listing
/// a folder of saves no longer has to allocate three hundred BodyDTOs per save to read a date.
[System.Serializable]
public class SaveHeader
{
    public string saveName;
    public string savedAtIso;
    public string summary;
    public int formatVersion;
}

[System.Serializable]
public class SaveGame
{
    public string saveName;
    public string savedAtIso;
    public string summary;          // short human description for the load list

    /// What this file is known to contain. Read it as "at least": a save at version N has every field
    /// versions 1..N added. 0 means a save written before versioning, i.e. before terrain and plates
    /// were stored — the loader regenerates those and says so.
    ///
    ///   1  terrain grids, tectonic layouts, ship travel/mission state, colony claim progress
    ///
    /// NO INITIALIZER, deliberately. JsonUtility leaves a field the JSON does not mention at whatever
    /// the initializer set, so defaulting this to CurrentVersion would make every save ever written
    /// claim to be current — which is precisely the question it exists to answer. Capture sets it.
    public const int CurrentVersion = 1;
    public int formatVersion;
    public int speciesIndex = 0;
    public int difficulty = 1;              // 0 easy, 1 medium, 2 hard
    public string factionName = "Your Empire";
    public int homeIndex = 0;
    public float timeScale = 1f;

    /// The date, as days elapsed since Year 0001 Month 01 Day 01 (see GameCalendar). A double rather
    /// than a float because a float starts losing whole days a few in-game centuries in, and a calendar
    /// that stops advancing is a strange thing to explain. Absent in an older save, where JsonUtility
    /// leaves it 0 — which is Year 0001 Day 01, the correct answer for a game that predates the clock.
    public double calendarDays;
    // The galaxy's name and the seed its deep-view spiral was generated from. Stored, not derived: the
    // spiral has to come back identical on reload. An older save has neither, so both are re-rolled on
    // load rather than left blank (see GameStateSerializer.Apply).
    public string galaxyName = "";
    public int galaxySeed = 0;
    // The galactic core is hideable like anything else, but it is rebuilt from scratch on load
    // (StarDatabase.BlackHole()) rather than deserialized — so its concealment needs its own field or it
    // comes back visible. 0 = visible; see HideReason.
    public int centerHideReason;
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
    public List<FactionAIDTO> factionAI = new List<FactionAIDTO>();   // each rival civilisation's race + personality
    public List<DerelictDTO> derelicts = new List<DerelictDTO>();     // ancient derelict stations and their contents

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

    /// What was paid for this project up front. Needed so cancelling after a reload refunds the unspent
    /// share rather than nothing; old saves carry 0 and are backfilled from the tech's cost on load.
    public int pointsPaid;
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

    /// Has the player ever had presence in this system? Drives the system-level fog of war. Absent in
    /// an older save, where false is safe: SystemPresence re-derives it the moment the player has
    /// anything here, so a system they are already standing in comes back known on the first frame.
    public bool visited;

    /// A Dev-tuned detection radius, or 0 for "use the rule". Saved because somebody dragged it on
    /// purpose and a value set by hand should outlive the session that set it; a generated system never
    /// writes anything but 0 here.
    public float detectionRadiusOverride;

    // Concealment (see Visibility.cs): 0 = visible, 1 = Dev, 2 = Cloaked, 3 = Undiscovered. Held as an
    // int because JsonUtility serializes enums as ints anyway and an int is what an older save missing
    // this field deserializes to — 0, i.e. visible, which is the correct reading of a save written
    // before anything could be hidden.
    public int hideReason;
    // Per-sun concealment, parallel to starTypes. Empty in an older save, and the loader treats a short
    // list as "the rest are visible" rather than indexing off the end.
    public List<int> starHideReasons = new List<int>();

    public List<BodyDTO> bodies = new List<BodyDTO>();
}

[System.Serializable]
public class DerelictDTO
{
    public int id;
    public int systemIndex;
    public int orbit;
    public float dsX, dsY, dsZ;
    public float orbitRadius, orbitPhase, orbitSpeed;
    public int clueIndex;
    public int rewardMetal, rewardEnergy, rewardResearch;
    public bool studied;
}

[System.Serializable]
public class FactionAIDTO
{
    public int factionId;
    public int speciesIndex;
    public float expansionism;
    public float growthDrive;
    public float hardiness;
    public float expandCooldown;
    public string temperament;
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

    /// The untouched sea level. Same negative-means-unset convention as tSea.
    public float nSea = -1f;
    public float tScale = 1f, tElev = 1f, tMoist = 1f, tHeat = 1f, tRidge = 1f; // terrain params

    /// Sea level, 0..1. NEGATIVE means "written before this existed" — zero is a legal dry world — GameStateSerializer converts such a
    /// save by reading the water level its elevation amplitude used to encode, so old worlds keep their
    /// oceans instead of loading bone dry.
    public float tSea = -1f;

    public float orbitRadius, orbitSpeed, orbitPhase;
    public float naturalOrbitRadius;   // the generated orbit, for Dev-Mode "Reset orbit/system"
    public int orbitDirection;
    public float inclination, eccentricity, verticalOffset, spinSpeed;

    /// +1 prograde, -1 retrograde — the body's AXIAL rotation, not its orbit. Absent in an older save,
    /// where JsonUtility leaves it 0; the loader reads 0 as prograde, which is what every world
    /// generated before rotation had a direction actually was.
    public int rotationDirection;

    /// Which asteroid belt this body shares a lane with, 0 for none. See CelestialBody.beltId — the
    /// orbit-safety pass needs it to know that a shared radius is deliberate here.
    public int beltId;

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
    public float atmospheres;           // ATMOSPHERES, 1 = Earth-normal — the stored truth
    public bool hasMagneticField;       // halves the atmosphere ceiling when absent — see AtmosphereRules
    // The derived 0..1 form. Still written so a save from this build stays loadable by an older one, and
    // still READ on load to migrate saves written before `atmospheres` existed.
    public float atmosphereThickness;
    public bool hasTectonics;       // active plate tectonics — see TectonicsRules
    public float terraformability;
    public List<int> terraformProjects = new List<int>();   // completed TerraformProjectType ids
    public List<PlacedBuilding> placedBuildings = new List<PlacedBuilding>();   // surface-grid structures
    public bool deepSurveyed;                               // LEGACY: "has tier I been done". Kept so an
                                                            // older save still loads; researchLevel is the truth.
    /// How far Deep Research has gone on this world, 0-3. Absent in an older save, where JsonUtility
    /// leaves it 0 and the loader falls back to `deepSurveyed` — so a world studied under the old single
    /// flag comes back at tier I rather than losing its overlays.
    public int researchLevel;
    public int clueIndex = -1;                              // which Vael fragment this world hides, -1 = none
    public float cityGrowthTimer;                           // progress toward this world's next settlement
    public bool birthrightClaim;
    /// A moon of the cradle: guaranteed terraformable, claimable at tech 1, and NOT owned to start with.
    /// Absent from saves written before the moons stopped being free, where it reads false — which is
    /// exactly right, because in those saves the moons carry `birthrightClaim` and are already yours.
    public bool cradleMoon;
    public bool settled;            // people live here (Claim.cs). Distinct from owning it.
    public bool visited;
    public float explorationProgress;

    // Why this world (and, separately, its orbit line) is not drawn — 0 visible, 1 Dev, 2 Cloaked,
    // 3 Undiscovered. See SystemDTO.hideReason for why these are ints. A galaxy is regenerated from
    // seeds on load, so without these the rare undiscovered worlds would quietly reappear the first
    // time the player saved and came back.
    public int hideReason;
    public int ringHideReason;

    /// Colonisation and deep-research progress on this world, 0..1. Both were missing, and both are
    /// real elapsed player time: a colony ship two thirds of the way through a claim, or a research
    /// ship most of the way through a survey, had its work silently reset to zero by a reload.
    public float claimProgress;
    public float researchProgress;

    /// How far the level-2 survey has got: 0..1 across every index and every band of every index, in
    /// the order SurfaceIndex.All lists them. Absent in an older save, where 0 is the honest reading —
    /// those saves gated the overlays on `researchLevel` instead, and the loader converts from it so a
    /// world that had already earned its overlays does not lose them.
    public float deepProgress;

    /// The level-1 survey per grid ROW, one quantised byte each, run-length encoded. Empty in an older
    /// save and for a world nobody has started — Survey.Rows re-seeds from deepProgress-s sibling,
    /// explorationProgress, so a converted save looks like a survey caught mid-sweep.
    public string surveyRows = "";

    // ---- The surface, as it actually is -------------------------------------------------------
    //
    // The biome of every cell, run-length encoded and base64'd by GridCodec, row-major from (0,0).
    // Empty in a save written before this existed, and empty for a body with no surface — the loader
    // falls back to generating one, which is what every save used to do.
    //
    // The DIMENSIONS travel with it. They are recomputed on load from the body's mass, and a stored
    // grid whose size no longer matches is refused rather than stretched over the new one — see
    // GridCodec.Decode.
    //
    // Only `type` is stored. A tile's `shade` is a pure function of position and seed with no history
    // to lose, `occupied` is re-stamped from the buildings standing on it, and `ore` has always had
    // its own list below.
    public string terrain = "";
    public int terrainW, terrainH;

    /// The plate layout this world's geology was built from. Stored so the tectonic overlay, the
    /// motion arrows and the earthquake belts come back as the ones that raised the mountains in
    /// `terrain` above, rather than as whatever the current algorithm would derive from the seed.
    /// Empty for a world without tectonics, and for a save written before this existed.
    public TectonicsDTO tectonics = new TectonicsDTO();

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

// ============================================================================================
// A world's plate layout, flattened.
//
// Held as flat float lists rather than a list of little structs on purpose: JsonUtility writes a
// nested serializable class as a full JSON object per element, so eighty cells would cost eighty
// `{"x":..,"y":..,"z":..}` blocks where three flat numbers do. The stride is fixed and documented
// per list, and TectonicsMap.Export/Import are the only two places that pack or unpack it.
//
// Everything here is geometry the layout cannot re-derive: where the Voronoi cells sit, which plate
// each belongs to, how each plate is moving, and the two noise bases that bend and roughen the
// margins. The band widths are stored too, because they are calibrated against the grid height and
// a world whose size changed should not silently redraw its faults at a different thickness.
// ============================================================================================
[System.Serializable]
public class TectonicsDTO
{
    public int plateCount;
    public int heightTiles;                              // the grid this was calibrated against
    public float faultTiles, beltTiles, minCos;

    public List<float> cellSites = new List<float>();    // stride 3: x, y, z (unit vector)
    public List<int> cellPlate = new List<int>();        // one per cell: which plate it belongs to
    public List<float> plates = new List<float>();       // stride 7: site xyz, motion xyz, strength
    public List<float> warp = new List<float>();         // stride 8: freq xyz, dir xyz, amp, phase
    public List<float> edge = new List<float>();         // stride 5: freq xyz, amp, phase

    /// A layout that actually describes something. An absent field deserializes to empty lists, which
    /// is exactly what a save written before this existed, and a world with no tectonics, both mean.
    public bool HasLayout => plateCount > 0 && cellSites.Count >= 3 && cellPlate.Count > 0;
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

    // These three were missing, and their absence was silently destructive: every save/load reset a
    // site's cost and reward to the class defaults and, worse, cleared yieldsSchematic — so the major
    // ancient ruins that are the ONLY source of precursor schematics permanently lost the ability to
    // yield one the first time a game was reloaded, closing off the Ancients tech branch.
    public int researchPointCost = 20;
    public int researchReward = 25;
    public bool yieldsSchematic;

    /// Charted by a deep survey, and so offerable as a job. Without this a save would forget which
    /// worlds had been studied and quietly withdraw every excavation the player had unlocked.
    public bool surveyed;
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
    public List<int> cluesFound = new List<int>();    // recovered Vael fragment indices (0..9)
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

    /// Hit points as they stand. -1 means a save written before ships could be damaged; the loader
    /// restores such a ship to full, which is exactly what those saves recorded — nothing could hurt one.
    public float hp = -1f;

    public int worldsExplored;
    public float serviceTime;
    public bool queuePaused;
    public List<int> samples = new List<int>();
    public List<OrderDTO> orders = new List<OrderDTO>();

    /// The ship's name. It used to be rebuilt on load as "<class> <id>", which is what a ship is
    /// called when it rolls off the stocks — so a renamed ship lost its name, and any ship whose id
    /// had been reused read as a different vessel.
    public string name = "";

    /// Service record. `experience` was saved and these two were not, so a veteran's battle count and
    /// research contribution reset to zero every reload while its rank stayed.
    public int battles;
    public float researchContributed;

    // ---- What the ship was in the middle of doing --------------------------------------------
    //
    // All of this was dropped: every unit loaded Idle, at its destination or its park position, with
    // its timers cleared. A ship three quarters of the way across a system restarted the crossing; a
    // research ship most of the way through a survey began again. The order queue was saved, so the
    // work restarted rather than being lost outright — which made it look like a stutter rather than
    // a bug, and is why it survived this long.
    //
    // `status` is the UnitStatus enum as an int. -1 means a save from before this existed, and the
    // loader falls back to Idle, which is what those saves effectively recorded.
    public int status = -1;
    public int travelTargetId = -1;               // -1 = travelling to a point, or not travelling
    public float travelElapsed, travelDuration;
    public float fromX, fromY, fromZ;             // travelFrom
    public float toX, toY, toZ;                   // travelTo
    public float missionTimer;
    public float researchTimer;
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
