using UnityEngine;

// Ship classes. The first four are the starting tier-1 classes (Cost order: Scout < Research/Fighter
// < Colony). The Mk II variants unlock at a level-2 shipyard, and the Terraformer at level 3.
// IMPORTANT: append new entries only — the ordinal is serialized in saves.
public enum UnitType
{
    // Tier-1 starters and their Mk II variants, Terraformer, Probe (indices 0-8, do not reorder).
    Scout, ResearchShip, Fighter, ColonyShip, ScoutII, ResearchShipII, FighterII, Terraformer, Probe,
    // Civilian / logistics hulls.
    Miner, Transport,
    // Combat hulls (escalating capital classes).
    Frigate, Cruiser, Carrier, Dreadnought,
    // Advanced utility / long-range hulls.
    ScienceVessel, Explorer,
    // Mk III refits.
    ScoutIII, FighterIII, ResearchShipIII,
    // Space stations — structures that orbit a body (or coast in deep space). Gated by Empire Tech Level.
    BattleStation, ResearchStation, RelayStation, SupplyStation, MultiStation,
    TerraformStation, DeepSpaceStation, MegaStation,
    // The Level-5 milestone: a hyper-speed relay that greatly extends and speeds fleet travel.
    HyperRelay
}

// The function a space station performs while deployed. Auras/effects are applied by the station
// systems and ColonyManager while the station is stationed (location == a body, or parked in deep space).
public enum StationRole { None, Battle, Research, Relay, Supply, MultiRole, Terraform, DeepSpace, Mega }

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

    public bool canExplore, canResearch, canColonize, canTerraform;

    // How much of a world this class can survey in ONE PASS, as a fraction of the whole.
    // A basic Scout's sensors only see so much before it has run out of things it knows how to look at:
    // it maps a slice, finishes its pass, and must be sent round again for another. Better sensor suites
    // raise the cap, and the best hulls (Mk III, Science Vessel) map a world completely in a single pass.
    // Points of interest only surface at a FULL survey, so a low-tier ship reveals them by repetition.
    public float surveyDepth = 1f;

    public int minShipyardLevel = 1;   // shipyard tier required to build this class (1-3)

    // Build power this hull occupies for as long as it is under construction, released the instant it
    // rolls out. Your shipyards pool their power (see BuildPower), so this decides how many of a class
    // you can lay down at once: two scouts (1 each) fit in a level-1 yard's 2 power, a terraformer (7)
    // needs a large yard plus shipyard research. Independent of minShipyardLevel — having the power to
    // build a class does not unlock it.
    public int buildPower = 1;

    // Base travel range in world units (0 = unlimited, e.g. probes). Ordered so scouts reach furthest,
    // then research ships, fighters, colony ships, terraformers. Scaled at runtime by ShipUpgrades.
    public int range = 220;
    public int visionRange = 0;        // probe sensor radius (0 = not a sensor platform)
    public bool isProbe = false;       // launch-and-forget scanner that coasts until it loses signal

    public int iconShape;      // 0 up-triangle, 1 diamond, 2 right-triangle, 3 circle
    public Color iconColor;

    // ---- Empire-tech gate + role/effect flags (set after construction; default to "plain ship") ----
    public int minEmpireLevel = 1;      // Empire Tech Level required to build (1 = from the start)

    public bool isWorker = false;       // civilian utility hull (miner/transport) — not a warship
    public bool isStation = false;      // a deployable structure, not a mobile ship
    public StationRole stationRole = StationRole.None;
    public bool deepSpace = false;      // deep-space station: parks in open space, runs on starlight
    public int stationLevel = 1;        // structure tier (mega = the "little moon")

    // Passive effects applied by StationEffects/ColonyManager while the unit is DEPLOYED (idle, not
    // travelling): at a body, or — for deep-space stations — parked in open space.
    public float mineBonus = 0f;        // extra metal per second while stationed (miners)
    public float supplyBonus = 0f;      // extra metal+energy per second while stationed (logistics)
    public float researchAura = 0f;     // research points per second while stationed
    public float terraformAura = 0f;    // added terraform-speed contribution at the body it orbits
    public float relayBoost = 0f;       // hyper-relay: adds to fleet travel range & speed while active

    public UnitInfo(UnitType type, string name, string description, int cm, int ce, float buildTime,
                    int armor, int health, int speed, int research, int attack,
                    bool explore, bool doResearch, bool colonize, int iconShape, Color iconColor,
                    int minShipyardLevel = 1, bool terraform = false,
                    int range = 220, int vision = 0, bool probe = false, int power = 1)
    {
        this.type = type; this.name = name; this.description = description;
        costMetal = cm; costEnergy = ce; this.buildTime = buildTime;
        this.armor = armor; this.health = health; this.speed = speed; this.research = research; this.attack = attack;
        canExplore = explore; canResearch = doResearch; canColonize = colonize; canTerraform = terraform;
        this.minShipyardLevel = minShipyardLevel;
        this.buildPower = power;
        this.range = range; this.visionRange = vision; this.isProbe = probe;
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
        _all = new UnitInfo[System.Enum.GetValues(typeof(UnitType)).Length];
        _all[(int)UnitType.Scout] = new UnitInfo(UnitType.Scout, "Scout",
            "Cheap, fast explorer with a survey BONUS. Surveys worlds and collects ore samples, but can't research them itself — bring samples to a research ship or a world with a research centre. Cannot colonize. Must return home from hostile worlds.",
            20, 10, 8f, 1, 20, 9, 2, 1, explore: true, doResearch: false, colonize: false, iconShape: 0, iconColor: new Color(0.5f, 0.85f, 1f), range: 270, power: 1);

        _all[(int)UnitType.ResearchShip] = new UnitInfo(UnitType.ResearchShip, "Research Ship",
            "A mobile laboratory. Slower and pricier than a scout, with no attack and only light defense, but researches a world far more deeply to unlock technologies.",
            60, 55, 18f, 2, 30, 5, 8, 0, explore: true, doResearch: true, colonize: false, iconShape: 1, iconColor: new Color(0.6f, 0.8f, 0.55f), range: 230, power: 2);

        _all[(int)UnitType.Fighter] = new UnitInfo(UnitType.Fighter, "Fighter",
            "A cheap warship with the strongest attack and defense of the starting ships, but short range and no research or exploration ability. Escorts fleets, defends your worlds, and grows deadlier with battle experience.",
            35, 25, 12f, 6, 80, 8, 0, 10, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(1f, 0.6f, 0.4f), range: 210, power: 1);

        _all[(int)UnitType.ColonyShip] = new UnitInfo(UnitType.ColonyShip, "Colony Ship",
            "A large, expensive settler vessel with heavy defenses and very little attack. Slow, but it sacrifices itself to found your first CITY on a habitable-enough world — after which you build shipyards, research centres and farms there.",
            220, 180, 42f, 6, 160, 3, 0, 1, explore: true, doResearch: false, colonize: true, iconShape: 3, iconColor: new Color(0.85f, 0.75f, 0.45f), range: 200, power: 2);

        // ---- Level-2 shipyard: upgraded Mk II variants (tougher, faster, more capable) ----
        _all[(int)UnitType.ScoutII] = new UnitInfo(UnitType.ScoutII, "Scout Mk II",
            "An upgraded scout: sturdier and faster, with a sharper survey. Still can't research its own samples, but ranges further and survives hostile worlds better. Requires a level-2 shipyard.",
            40, 20, 11f, 3, 40, 12, 4, 3, explore: true, doResearch: false, colonize: false, iconShape: 0, iconColor: new Color(0.35f, 0.95f, 1f), minShipyardLevel: 2, range: 310, power: 1);

        _all[(int)UnitType.ResearchShipII] = new UnitInfo(UnitType.ResearchShipII, "Research Ship Mk II",
            "An advanced mobile laboratory that researches worlds far faster and deeper than the base model, with better defenses. Requires a level-2 shipyard.",
            110, 100, 24f, 4, 55, 7, 13, 0, explore: true, doResearch: true, colonize: false, iconShape: 1, iconColor: new Color(0.5f, 1f, 0.6f), minShipyardLevel: 2, range: 260, power: 2);

        _all[(int)UnitType.FighterII] = new UnitInfo(UnitType.FighterII, "Fighter Mk II",
            "A heavier warship with markedly stronger attack and armor than the base fighter. The backbone of a real war fleet. Requires a level-2 shipyard.",
            65, 50, 17f, 10, 140, 10, 0, 16, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(1f, 0.45f, 0.3f), minShipyardLevel: 2, range: 220, power: 2);

        // ---- Level-3 shipyard: the Terraformer ----
        _all[(int)UnitType.Terraformer] = new UnitInfo(UnitType.Terraformer, "Terraformer",
            "A vast climate-engineering vessel. It can begin terraforming a world it is present at and, while it stays, greatly accelerates the project — turning hostile worlds livable. No attack. Requires a level-3 shipyard.",
            180, 200, 55f, 5, 120, 5, 0, 0, explore: true, doResearch: false, colonize: false, iconShape: 1, iconColor: new Color(0.4f, 0.9f, 0.75f), minShipyardLevel: 3, terraform: true, range: 180, power: 7);

        // ---- Level-2 shipyard: expendable deep-space probe ----
        _all[(int)UnitType.Probe] = new UnitInfo(UnitType.Probe, "Probe",
            "A cheap, expendable deep-space probe. Launch it in any direction and it coasts on starlight alone — no fuel — scanning everything within its sensor range and pulsing back what it finds, until it drifts too far, loses power or is destroyed and its signal goes dark. Ignores travel-range limits. Requires a level-2 shipyard.",
            15, 10, 7f, 0, 8, 7, 0, 0, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.85f, 0.92f, 0.5f), minShipyardLevel: 1, range: 0, vision: 140, probe: true, power: 1);

        // ================= CIVILIAN / LOGISTICS HULLS =================
        var miner = new UnitInfo(UnitType.Miner, "Mining Barge",
            "A civilian worker that anchors over an ore-rich world and steadily ships refined metal back to your stockpile while it stays. No attack, light hull.",
            45, 25, 12f, 2, 45, 6, 0, 0, explore: false, doResearch: false, colonize: false, iconShape: 1, iconColor: new Color(0.80f, 0.66f, 0.40f), minShipyardLevel: 1, range: 180, power: 2);
        miner.isWorker = true; miner.mineBonus = 0.9f;   // metal/sec while deployed
        _all[(int)UnitType.Miner] = miner;

        var transport = new UnitInfo(UnitType.Transport, "Transport",
            "A civilian hauler. Parked at one of your colonies it runs supply routes, adding a steady trickle of metal and energy to your economy. Requires a level-2 shipyard.",
            70, 40, 14f, 3, 70, 6, 0, 0, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.70f, 0.72f, 0.55f), minShipyardLevel: 2, range: 250, power: 2);
        transport.isWorker = true; transport.supplyBonus = 0.6f;
        _all[(int)UnitType.Transport] = transport;

        // ================= COMBAT HULLS (escalating capital classes) =================
        _all[(int)UnitType.Frigate] = new UnitInfo(UnitType.Frigate, "Frigate",
            "A nimble patrol warship — tougher and harder-hitting than a fighter, with better range. The workhorse escort of a growing fleet. Requires a level-2 shipyard.",
            90, 60, 20f, 12, 180, 9, 0, 22, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(1f, 0.55f, 0.35f), minShipyardLevel: 2, range: 230, power: 2);

        var cruiser = new UnitInfo(UnitType.Cruiser, "Cruiser",
            "A heavy warship with thick armor and a powerful battery. Anchors a battle line. Requires a level-3 shipyard and Empire Tech Level 3.",
            200, 150, 34f, 20, 340, 7, 0, 48, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(1f, 0.42f, 0.28f), minShipyardLevel: 3, range: 240, power: 3);
        cruiser.minEmpireLevel = 3;
        _all[(int)UnitType.Cruiser] = cruiser;

        var carrier = new UnitInfo(UnitType.Carrier, "Carrier",
            "A fleet command vessel with enormous hull integrity that fields strike craft. Durable rather than hard-hitting; the heart of a task force. Requires a level-3 shipyard and Empire Tech Level 4.",
            320, 240, 48f, 18, 460, 6, 0, 34, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(0.95f, 0.5f, 0.3f), minShipyardLevel: 3, range: 240, power: 4);
        carrier.minEmpireLevel = 4;
        _all[(int)UnitType.Carrier] = carrier;

        var dread = new UnitInfo(UnitType.Dreadnought, "Dreadnought",
            "A capital ship of the line — colossal armor and firepower that dominates any battle it enters. Ruinously expensive and slow to build. Requires a level-3 shipyard and Empire Tech Level 5.",
            600, 480, 95f, 35, 950, 5, 0, 92, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(0.9f, 0.35f, 0.25f), minShipyardLevel: 3, range: 230, power: 6);
        dread.minEmpireLevel = 5;
        _all[(int)UnitType.Dreadnought] = dread;

        // ================= ADVANCED UTILITY / LONG-RANGE HULLS =================
        var science = new UnitInfo(UnitType.ScienceVessel, "Science Vessel",
            "A dedicated deep-survey laboratory. Researches worlds faster than any research ship and, while deployed, radiates a steady research output to your empire. Requires a level-3 shipyard and Empire Tech Level 4.",
            260, 240, 40f, 6, 100, 7, 22, 0, explore: true, doResearch: true, colonize: false, iconShape: 1, iconColor: new Color(0.55f, 1f, 0.7f), minShipyardLevel: 3, range: 280, power: 3);
        science.minEmpireLevel = 4; science.researchAura = 0.8f;
        _all[(int)UnitType.ScienceVessel] = science;

        var explorer = new UnitInfo(UnitType.Explorer, "Explorer",
            "A long-range pathfinder built to reach the very edge of your range and beyond. The furthest-ranging crewed ship you can field. Requires a level-2 shipyard.",
            90, 60, 14f, 4, 60, 13, 6, 2, explore: true, doResearch: false, colonize: false, iconShape: 0, iconColor: new Color(0.4f, 0.98f, 0.95f), minShipyardLevel: 2, range: 360, power: 1);
        explorer.minEmpireLevel = 2;
        _all[(int)UnitType.Explorer] = explorer;

        // ================= Mk III REFITS (level-3 shipyard, Empire Level 3) =================
        var scout3 = new UnitInfo(UnitType.ScoutIII, "Scout Mk III",
            "The finest scout hull: blistering speed, a superb survey suite and the reach to cross between systems. Requires a level-3 shipyard and Empire Tech Level 3.",
            70, 40, 14f, 5, 70, 15, 6, 5, explore: true, doResearch: false, colonize: false, iconShape: 0, iconColor: new Color(0.3f, 1f, 1f), minShipyardLevel: 3, range: 360, power: 2);
        scout3.minEmpireLevel = 3;
        _all[(int)UnitType.ScoutIII] = scout3;

        var fighter3 = new UnitInfo(UnitType.FighterIII, "Fighter Mk III",
            "A top-line strike fighter — far tougher and deadlier than the Mk II, cheap enough to mass. Requires a level-3 shipyard and Empire Tech Level 3.",
            130, 110, 26f, 16, 240, 11, 0, 26, explore: false, doResearch: false, colonize: false, iconShape: 2, iconColor: new Color(1f, 0.4f, 0.25f), minShipyardLevel: 3, range: 230, power: 2);
        fighter3.minEmpireLevel = 3;
        _all[(int)UnitType.FighterIII] = fighter3;

        var research3 = new UnitInfo(UnitType.ResearchShipIII, "Research Ship Mk III",
            "A state-of-the-art mobile laboratory that unlocks a world's secrets faster and further afield than any prior model. Requires a level-3 shipyard and Empire Tech Level 3.",
            200, 180, 32f, 7, 100, 8, 20, 0, explore: true, doResearch: true, colonize: false, iconShape: 1, iconColor: new Color(0.5f, 1f, 0.65f), minShipyardLevel: 3, range: 290, power: 3);
        research3.minEmpireLevel = 3;
        _all[(int)UnitType.ResearchShipIII] = research3;

        // ================= SPACE STATIONS =================
        // Built at a shipyard, then towed to a target and left to anchor. Once deployed at a body it
        // orbits with that body and radiates its effect; a battle station also defends it.
        var battle = new UnitInfo(UnitType.BattleStation, "Battle Station",
            "A rudimentary orbital fortress. Immobile once anchored, but heavily armed and armored — a strong defensive anchor over a world you want to hold. Unlocks at Empire Tech Level 2.",
            260, 160, 38f, 30, 700, 3, 0, 60, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(1f, 0.5f, 0.45f), minShipyardLevel: 2, range: 350, power: 4);
        battle.isStation = true; battle.stationRole = StationRole.Battle; battle.minEmpireLevel = 2;
        _all[(int)UnitType.BattleStation] = battle;

        var researchSt = new UnitInfo(UnitType.ResearchStation, "Research Station",
            "An orbital laboratory. While anchored it steadily feeds research to your empire and deepens study of the world it orbits. Unlocks at Empire Tech Level 2.",
            240, 220, 36f, 10, 400, 3, 10, 0, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.55f, 0.95f, 1f), minShipyardLevel: 2, range: 350, power: 4);
        researchSt.isStation = true; researchSt.stationRole = StationRole.Research; researchSt.minEmpireLevel = 2; researchSt.researchAura = 1.4f;
        _all[(int)UnitType.ResearchStation] = researchSt;

        var relay = new UnitInfo(UnitType.RelayStation, "Relay Station",
            "A rudimentary comms-and-navigation relay. While active it lengthens and quickens your fleet's travel a little — the first step toward a fast-travel network. Unlocks at Empire Tech Level 2.",
            200, 180, 30f, 8, 300, 3, 0, 0, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.7f, 0.8f, 1f), minShipyardLevel: 2, range: 350, power: 4);
        relay.isStation = true; relay.stationRole = StationRole.Relay; relay.minEmpireLevel = 2; relay.relayBoost = 0.12f;
        _all[(int)UnitType.RelayStation] = relay;

        var supply = new UnitInfo(UnitType.SupplyStation, "Supply Station",
            "An orbital depot and fuel dump. Anchored at a colony it adds a steady stream of metal and energy and helps ships range a little further. Unlocks at Empire Tech Level 2.",
            220, 140, 32f, 10, 380, 3, 0, 0, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.85f, 0.85f, 0.55f), minShipyardLevel: 2, range: 350, power: 4);
        supply.isStation = true; supply.stationRole = StationRole.Supply; supply.minEmpireLevel = 2; supply.supplyBonus = 0.9f; supply.relayBoost = 0.05f;
        _all[(int)UnitType.SupplyStation] = supply;

        var multi = new UnitInfo(UnitType.MultiStation, "Multi-Role Station",
            "A substantial orbital complex that does a bit of everything — research, logistics, fleet support and defense. Unlocks at Empire Tech Level 4.",
            500, 420, 62f, 22, 900, 2, 8, 40, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.8f, 0.7f, 1f), minShipyardLevel: 3, range: 350, power: 6);
        multi.isStation = true; multi.stationRole = StationRole.MultiRole; multi.minEmpireLevel = 4;
        multi.researchAura = 1.2f; multi.supplyBonus = 0.7f; multi.relayBoost = 0.12f;
        _all[(int)UnitType.MultiStation] = multi;

        var terra = new UnitInfo(UnitType.TerraformStation, "Terraforming Station",
            "A colossal climate-engineering platform. Anchored over a world it dramatically accelerates terraforming there — many times faster than ships alone, and it stacks with terraformers. Unlocks at Empire Tech Level 6.",
            480, 460, 60f, 10, 500, 3, 0, 0, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.5f, 0.95f, 0.7f), minShipyardLevel: 3, range: 350, power: 7);
        terra.isStation = true; terra.stationRole = StationRole.Terraform; terra.minEmpireLevel = 6; terra.terraformAura = 4.5f;
        _all[(int)UnitType.TerraformStation] = terra;

        var deep = new UnitInfo(UnitType.DeepSpaceStation, "Deep-Space Station",
            "A self-sufficient station that needs no world to orbit — it coasts in open space on starlight alone, needing virtually no fuel. Place it anywhere as a research outpost and relay node. Unlocks at Empire Tech Level 3.",
            300, 120, 42f, 12, 450, 3, 0, 0, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.6f, 0.9f, 0.85f), minShipyardLevel: 3, range: 0, power: 5);
        deep.isStation = true; deep.stationRole = StationRole.DeepSpace; deep.deepSpace = true; deep.minEmpireLevel = 3;
        deep.researchAura = 0.5f; deep.relayBoost = 0.18f;
        _all[(int)UnitType.DeepSpaceStation] = deep;

        var mega = new UnitInfo(UnitType.MegaStation, "Mega-Station",
            "A do-everything orbital city the size of a small moon: prodigious research, logistics, a fast-travel hub, terraforming support and a fearsome defensive battery. The pinnacle of station engineering. Unlocks at Empire Tech Level 9.",
            1500, 1200, 150f, 60, 3000, 1, 20, 120, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.85f, 0.8f, 1f), minShipyardLevel: 3, range: 350, power: 9);
        mega.isStation = true; mega.stationRole = StationRole.Mega; mega.minEmpireLevel = 9; mega.stationLevel = 3;
        mega.researchAura = 3f; mega.supplyBonus = 1.6f; mega.relayBoost = 0.3f; mega.terraformAura = 1.5f;
        _all[(int)UnitType.MegaStation] = mega;

        // The Level-5 milestone: a hyper-speed relay. A big, expensive network node that greatly
        // extends and speeds fleet travel — the leap that opens up interstellar movement.
        var hyper = new UnitInfo(UnitType.HyperRelay, "Hyper-Speed Relay",
            "A massive fast-travel relay. Each one active dramatically lengthens and quickens your fleet's travel across the galaxy — the milestone that finally frees your ships from their home system. Unlocks at Empire Tech Level 5.",
            700, 620, 85f, 14, 600, 2, 0, 0, explore: false, doResearch: false, colonize: false, iconShape: 3, iconColor: new Color(0.55f, 0.75f, 1f), minShipyardLevel: 3, range: 0, power: 8);
        hyper.isStation = true; hyper.stationRole = StationRole.Relay; hyper.deepSpace = true; hyper.minEmpireLevel = 5; hyper.relayBoost = 0.45f;
        _all[(int)UnitType.HyperRelay] = hyper;

        // ---- Survey depth per pass (see UnitInfo.surveyDepth) ----
        // The progression that makes exploration a tech ladder rather than a formality: a starting Scout
        // maps just over half a world per pass and has to come back round for the rest, while a Mk III or
        // a Science Vessel maps the whole thing — and so reveals its points of interest — in one go.
        Depth(UnitType.Scout, 0.55f);
        Depth(UnitType.ScoutII, 0.80f);
        Depth(UnitType.ScoutIII, 1.00f);
        Depth(UnitType.Explorer, 0.90f);
        Depth(UnitType.ResearchShip, 0.60f);
        Depth(UnitType.ResearchShipII, 0.85f);
        Depth(UnitType.ResearchShipIII, 1.00f);
        Depth(UnitType.ScienceVessel, 1.00f);
        Depth(UnitType.ColonyShip, 0.40f);      // it can look, but it isn't a survey ship
        Depth(UnitType.Terraformer, 0.50f);
    }

    static void Depth(UnitType t, float d) { if (_all[(int)t] != null) _all[(int)t].surveyDepth = d; }
}
