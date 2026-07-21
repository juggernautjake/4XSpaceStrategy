using System.Collections.Generic;
using UnityEngine;

// The research branches. Foundations is the prerequisite spine; the next five are the core paths.
// Doctrine holds the strategic doctrine paths (War/Science/Exploration/Expansion/Prosperity). Ancients
// is the secret branch, gated by ancient schematics recovered from precursor ruins.
public enum TechBranch { Foundations, Warfare, Science, Expansion, Exploration, Industry, Doctrine, Ancients }

// A single node in the tech tree. Researching it spends Research Points and applies its persistent,
// stacking effects to the empire. Nodes require their prerequisite node(s), a minimum empire Tech
// Level, and optionally that a particular ore has been discovered in the field.
public class Tech
{
    public string id;
    public string name;
    public string desc;
    public TechBranch branch;
    public int tier;             // 1-3 (roughly tracks the empire level you research it at)
    public int cost;             // research points
    public int minEmpireLevel;   // empire Tech Level required
    public string[] prereqs;     // ids that must be researched first
    public OreType reqOre = OreType.None;   // an ore that must be discovered first (None = no requirement)
    public int reqSchematics = 0;   // ancient schematics that must be recovered first (Ancients branch)

    // Persistent empire effects (all optional; summed across every researched node).
    public float researchRate;   // +fractional research rate (0.15 = +15%)
    public float buildCostCut;   // fractional reduction to build costs (0.10 = -10%)
    public float buildTimeCut;   // fractional reduction to build times
    public float terraCeiling;   // +points added to every world's terraform ceiling
    public float terraSpeed;     // +fractional terraforming speed
    public float rangeMult;      // +fractional travel range (multiplies with the empire level bonus)
    public float oreYield;       // +fractional ore/metal yield
    public int shipyardPower;    // +build power on EVERY shipyard you own
    public int researchCap;      // +research capacity on EVERY research centre you own
    public string unlockNote;    // human-readable "this also enables …" (hulls/stations land later)

    // Research capacity this project occupies while it is being studied — the research-side twin of a
    // hull's build power. Derived from its point cost, so a cheap node is something a single small lab
    // can take on while a 480 RP precursor project needs most of your laboratories at once.
    public int CapacityCost => Mathf.Clamp(Mathf.CeilToInt(cost / 90f), 1, 6);

    public Tech(string id, string name, TechBranch branch, int tier, int cost, int minEmpireLevel, string[] prereqs, string desc)
    { this.id = id; this.name = name; this.branch = branch; this.tier = tier; this.cost = cost; this.minEmpireLevel = minEmpireLevel; this.prereqs = prereqs ?? new string[0]; this.desc = desc; }
}

// Aggregated, live empire modifiers derived from everything researched. Systems read these each frame.
public static class TechEffects
{
    public static float ResearchRateMult = 1f;
    public static float BuildCostMult = 1f;      // multiply a base cost by this
    public static float BuildTimeMult = 1f;
    public static float TerraformCeilingBonus = 0f;
    public static float TerraformSpeedMult = 1f;
    public static float OreYieldMult = 1f;

    // Per-facility parallelism bonuses: added to EVERY shipyard / research centre the player owns.
    public static int ShipyardPowerBonus = 0;
    public static int ResearchCapacityBonus = 0;

    public static void Reset()
    {
        ResearchRateMult = 1f; BuildCostMult = 1f; BuildTimeMult = 1f;
        TerraformCeilingBonus = 0f; TerraformSpeedMult = 1f; OreYieldMult = 1f;
        ShipyardPowerBonus = 0; ResearchCapacityBonus = 0;
    }
}

public static class TechDatabase
{
    static List<Tech> _all;
    static Dictionary<string, Tech> _byId;

    public static List<Tech> All { get { if (_all == null) Build(); return _all; } }
    public static Tech Get(string id) { if (_byId == null) Build(); return _byId.TryGetValue(id, out var t) ? t : null; }

    public static IEnumerable<Tech> InBranch(TechBranch b)
    { foreach (var t in All) if (t.branch == b) yield return t; }

    static Tech T(string id, string name, TechBranch br, int tier, int cost, int lvl, string[] pre, string desc)
        => new Tech(id, name, br, tier, cost, lvl, pre, desc);

    static void Build()
    {
        _all = new List<Tech>();

        // ---- Foundations (prerequisite spine) ----
        _all.Add(T("F1", "Applied Materials", TechBranch.Foundations, 1, 60, 1, null,
            "Stronger structural alloys. The root of the warfare and industry branches.")
            .With(oreYield: 0.15f));
        _all.Add(T("F2", "Fusion Power", TechBranch.Foundations, 1, 80, 1, new[] { "F1" },
            "Reliable fusion reactors power your colonies and drives.")
            .With(buildCostCut: 0.05f));
        _all.Add(T("F3", "Computing Cores", TechBranch.Foundations, 1, 90, 1, new[] { "F1" },
            "Faster laboratories and control systems.")
            .With(researchRate: 0.15f));
        _all.Add(T("F4", "Orbital Construction", TechBranch.Foundations, 2, 120, 2, new[] { "F1", "F2" },
            "Build in orbit — enables level-2 shipyards and medium hulls.")
            .With(buildTimeCut: 0.10f, unlock: "Enables medium hulls (Frigate, Miner, Hauler) once shipyards allow."));

        // ---- Warfare (unlock-focused; hulls arrive with the ship roster) ----
        _all.Add(T("W1", "Ballistics", TechBranch.Warfare, 1, 70, 1, new[] { "F1" },
            "Mass-driver weaponry.").With(unlock: "Enables the Frigate warship."));
        _all.Add(T("W2", "Armour Plating", TechBranch.Warfare, 1, 90, 1, new[] { "W1" },
            "Ablative hull armour for your warships.").With(unlock: "Enables the Fighter Mk II."));
        _all.Add(T("W3", "Energy Shields", TechBranch.Warfare, 2, 140, 2, new[] { "W2", "F2" },
            "Deflector shields absorb incoming fire.").With(unlock: "Enables the Cruiser."));
        _all.Add(T("W4", "Point-Defence Grids", TechBranch.Warfare, 2, 160, 2, new[] { "W3" },
            "Automated close-in batteries swat down strike craft and missiles before they land, and the fire-control computers make every hull cheaper to fit out.")
            .With(buildCostCut: 0.06f, unlock: "Hardens warships and stations against strike craft."));
        var w5 = T("W5", "Antimatter Warheads", TechBranch.Warfare, 3, 300, 4, new[] { "W3" },
            "Warheads that annihilate rather than explode. Fearsomely expensive, and nothing survives contact with them.");
        w5.reqOre = OreType.Helium3;
        _all.Add(w5.With(unlock: "Enables the Dreadnought's main battery."));
        _all.Add(T("W6", "Adaptive Damage Control", TechBranch.Warfare, 3, 260, 3, new[] { "W4", "I3" },
            "Self-sealing hulls and drone repair swarms — your fleet stops needing a drydock for every scratch.")
            .With(buildTimeCut: 0.08f, unlock: "Warships repair themselves in the field."));

        // ---- Science ----
        _all.Add(T("S1", "Deep Sensors", TechBranch.Science, 1, 70, 1, new[] { "F3" },
            "Sharper survey scanners; reveal more anomalies.").With(researchRate: 0.05f));
        _all.Add(T("S2", "Laboratory Networks", TechBranch.Science, 2, 110, 2, new[] { "F3" },
            "Linked research labs across your colonies.").With(researchRate: 0.25f));
        _all.Add(T("S3", "Xenoarchaeology", TechBranch.Science, 2, 150, 2, new[] { "S1" },
            "Study ancient ruins — the door to precursor secrets.").With(researchRate: 0.10f, unlock: "Opens the Ancients research path."));
        var s4 = T("S4", "Exotic Matter Studies", TechBranch.Science, 3, 220, 3, new[] { "S2" },
            "Harness exotic ores — enables level-3 research facilities."); s4.reqOre = OreType.Neutronium;
        _all.Add(s4.With(researchRate: 0.40f));

        // ---- Research capacity (parallel projects) ----
        // The research-side twin of the shipyard slipway line: each adds a wing to EVERY research centre,
        // letting you study more small technologies at once — or take on one genuinely enormous project.
        _all.Add(T("S5", "Parallel Research Wings", TechBranch.Science, 2, 170, 2, new[] { "S2" },
            "A second research wing in every laboratory — study another technology alongside the first.")
            .With(researchCap: 1, unlock: "+1 research capacity at every research centre you own."));
        _all.Add(T("S6", "Distributed Cognition", TechBranch.Science, 3, 280, 3, new[] { "S5" },
            "Your laboratories think as one mind across the empire, dividing a problem between every wing at once.")
            .With(researchCap: 1, researchRate: 0.10f, unlock: "+1 research capacity at every research centre you own."));
        var s7 = T("S7", "Quantum Think-Tanks", TechBranch.Science, 3, 400, 4, new[] { "S6", "S4" },
            "Laboratories that reason in superposition — enough capacity to attempt the largest projects your species can conceive of.");
        s7.reqOre = OreType.Neutronium;
        _all.Add(s7.With(researchCap: 1, researchRate: 0.15f, unlock: "+1 research capacity at every research centre you own."));

        // ---- Expansion & Terraforming ----
        _all.Add(T("X1", "Closed-Loop Life Support", TechBranch.Expansion, 1, 80, 1, new[] { "F2" },
            "Self-sustaining colonies; cheaper city founding.").With(terraCeiling: 5f));
        _all.Add(T("X2", "Atmospheric Processing", TechBranch.Expansion, 2, 130, 2, new[] { "X1" },
            "Generate breathable atmospheres on hostile worlds.").With(terraCeiling: 12f));
        var x3 = T("X3", "Hydrosphere Seeding", TechBranch.Expansion, 2, 150, 2, new[] { "X2" },
            "Import comets and ice to give arid worlds oceans."); x3.reqOre = OreType.Cryonite;
        _all.Add(x3.With(terraCeiling: 12f));
        _all.Add(T("X4", "Climate Engineering", TechBranch.Expansion, 2, 190, 2, new[] { "X2" },
            "Orbital mirrors and shades tune a world's temperature.").With(terraCeiling: 12f, terraSpeed: 0.25f));
        _all.Add(T("X5", "Xeno-Adaptation", TechBranch.Expansion, 3, 260, 3, new[] { "X3", "X4" },
            "Adapt your colonists to harsher worlds.").With(terraCeiling: 10f, terraSpeed: 0.35f, unlock: "Enables the Colony Ship Mk II."));

        // ---- Terraforming projects ----
        // Each node below unlocks specific PLANETARY ENGINEERING PROJECTS (see Terraforming.cs). A
        // project fixes one diagnosed fault on one world — its ice caps, its poisoned sky, its dead
        // core, its orbit — and permanently raises that world's habitability ceiling. This is the line
        // that turns "this planet can never be settled" into "this planet is expensive but possible".
        _all.Add(T("X6", "Orbital Solar Management", TechBranch.Expansion, 2, 150, 2, new[] { "X2" },
            "Gossamer mirrors and statite shades let you dial how much starlight a world receives.")
            .With(terraCeiling: 6f, unlock: "PROJECTS: Orbital Mirror Swarm (warm a frozen world) · Orbital Shade Array (cool a scorched one)."));

        _all.Add(T("X7", "Cometary Redirection", TechBranch.Expansion, 2, 180, 2, new[] { "X3" },
            "Tugs and mass drivers to steer ice bodies wherever you want them — including down.")
            .With(terraSpeed: 0.15f, unlock: "PROJECTS: Water Convoy · Cometary Bombardment (water and atmosphere in one blow)."));

        var x8 = T("X8", "Deep Crust Engineering", TechBranch.Expansion, 2, 210, 3, new[] { "X4" },
            "Drill through a planet's crust to reach what is trapped beneath it: fossil water, or the heat of the mantle itself.");
        x8.reqOre = OreType.Adamantine;
        _all.Add(x8.With(terraCeiling: 6f, unlock: "PROJECTS: Tap the Deep Aquifers · Core Heat Extraction (calm a volcanic world)."));

        _all.Add(T("X14", "Directed Ecopoiesis", TechBranch.Expansion, 2, 230, 3, new[] { "X2", "S1" },
            "Purpose-built life — bacteria that make soil out of rock, algae that flood a sky with oxygen, forests that hold a climate steady without any help from you.")
            .With(terraCeiling: 8f, terraSpeed: 0.20f, unlock: "PROJECTS: Microbial Seeding · Oxygen Cascade · Forest Seeding."));

        var x10 = T("X10", "Rotational Engineering", TechBranch.Expansion, 3, 300, 4, new[] { "X8" },
            "Equatorial mass drivers big enough to change how fast a planet turns — ending the tidally-locked world's baked face and frozen back.");
        x10.reqOre = OreType.Neutronium;
        _all.Add(x10.With(terraCeiling: 8f, unlock: "PROJECTS: Rotational Acceleration · Rotational Braking · Axial Correction."));

        var x11 = T("X11", "Magnetospheric Engineering", TechBranch.Expansion, 3, 340, 4, new[] { "X8", "F2" },
            "Give a dead world a magnetic field again — either by restarting its core or by hanging an artificial one in front of it. Without this, every atmosphere you build is eventually stripped away.");
        x11.reqOre = OreType.Neutronium;
        _all.Add(x11.With(terraCeiling: 10f, unlock: "PROJECTS: Core Ignition · Magnetospheric Shield."));

        var x13 = T("X13", "Moon Engineering", TechBranch.Expansion, 3, 380, 5, new[] { "X10" },
            "Moons are tools. Capture one to steady a wobbling world's axis and stir its oceans — or take a troublesome one apart entirely.");
        x13.reqSchematics = 1;
        _all.Add(x13.With(terraCeiling: 6f, unlock: "PROJECTS: Capture a Moon · Lunar Disassembly."));

        var x9 = T("X9", "Stellar Engineering", TechBranch.Expansion, 3, 520, 7, new[] { "X10", "E3" },
            "Move the planet. Centuries of asteroid slingshots walk a whole world along its orbit, into the light or out of the fire. There is no other answer for a world in the wrong place.");
        x9.reqOre = OreType.Neutronium; x9.reqSchematics = 2;
        _all.Add(x9.With(terraCeiling: 14f, terraSpeed: 0.25f, unlock: "PROJECTS: Orbital Migration, inward and outward."));

        // Terraforming is not only about ADDING. Half the galaxy's worlds are wrong for your species in
        // the opposite direction — drowned when you want arid, thick-skied when you need to stand up in
        // it. This node is the "take it away" counterpart to Hydrosphere Seeding.
        _all.Add(T("X15", "Planetary Desiccation", TechBranch.Expansion, 3, 280, 4, new[] { "X7", "X4" },
            "Crack an ocean into gas and let the solar wind take it, or drive it down into the crust as hydrates. To a species that needs dry ground, a water world is a problem to be solved — and the water you strip off it is worth having.")
            .With(terraCeiling: 8f, unlock: "PROJECTS: Hydrosphere Venting · Crustal Sequestration · Atmospheric Thinning."));

        // The answer to the deepest terraforming problem there is: the world is simply the wrong KIND
        // of place. No amount of shades or scrubbers changes a species' affinity for a world type —
        // only rebuilding the world does.
        var x16 = T("X16", "Planetary Remodelling", TechBranch.Expansion, 3, 560, 7, new[] { "X15", "X11", "X14" },
            "Stop adjusting worlds and start rebuilding them. Boil off a sea or fill a basin, ignite a dead world's volcanism or quench a furnace, thaw a glacier world or freeze a temperate one — until the planet is the kind of place your species was made for. The difference between a 24% world and an 88% one is no longer luck; it is budget.");
        x16.reqSchematics = 2;
        _all.Add(x16.With(terraCeiling: 16f, terraSpeed: 0.30f,
            unlock: "PROJECT: Planetary Remodelling — convert a world to your species' ideal type."));

        var x12 = T("X12", "Shellworld Architecture", TechBranch.Expansion, 3, 620, 9, new[] { "X9", "I4" },
            "Wrap a solid shell around a gas giant and live on the outside of it; sink degenerate-matter anchors into a pebble to give it real gravity. The point at which your species stops looking for worlds and starts making them.");
        x12.reqSchematics = 3;
        _all.Add(x12.With(terraCeiling: 15f, unlock: "PROJECTS: Shellworld Construction (settle a gas giant) · Gravity Anchors."));

        // ---- Exploration (drives that extend travel range, stacking with empire level) ----
        _all.Add(T("E1", "Ion Drives", TechBranch.Exploration, 1, 60, 1, new[] { "F2" },
            "Efficient ion propulsion — more range on every ship.").With(rangeMult: 0.12f, unlock: "Enables the Scout Mk II."));
        _all.Add(T("E2", "Long-Range Scanners", TechBranch.Exploration, 1, 90, 1, new[] { "E1" },
            "See farther; find more points of interest.").With(rangeMult: 0.05f));
        _all.Add(T("E3", "Warp Coils", TechBranch.Exploration, 2, 160, 2, new[] { "E1" },
            "Warp-assisted travel greatly extends fleet range.").With(rangeMult: 0.35f, unlock: "Enables the Explorer."));
        var e4 = T("E4", "Jump Drives", TechBranch.Exploration, 3, 280, 3, new[] { "E3" },
            "Near-instant jumps across vast distances."); e4.reqOre = OreType.Helium3;
        _all.Add(e4.With(rangeMult: 0.60f));
        _all.Add(T("E5", "Autonomous Survey Drones", TechBranch.Exploration, 2, 140, 2, new[] { "E2", "S1" },
            "Every scout carries a swarm of drones that fan out and map a world in a fraction of the time a crew would need.")
            .With(researchRate: 0.08f, unlock: "Surveys finish markedly faster."));
        _all.Add(T("E6", "Self-Sufficient Hulls", TechBranch.Exploration, 2, 190, 3, new[] { "E3", "X1" },
            "Closed-loop life support and fuel scoops aboard every ship — your fleet stops needing to come home.")
            .With(rangeMult: 0.22f, unlock: "Ships endure far longer on hostile worlds."));
        var e7 = T("E7", "Wormhole Cartography", TechBranch.Exploration, 3, 420, 6, new[] { "E4" },
            "Chart the natural folds in space that were always there, and step between stars through them.");
        e7.reqOre = OreType.Neutronium;
        _all.Add(e7.With(rangeMult: 0.80f, unlock: "The galaxy stops being a place you cross and becomes a place you step through."));

        // ---- Industry ----
        _all.Add(T("I1", "Automated Mining", TechBranch.Industry, 1, 70, 1, new[] { "F1" },
            "Robotic miners boost every ore and metal yield.").With(oreYield: 0.25f, unlock: "Enables the Miner ship."));
        _all.Add(T("I2", "Refineries", TechBranch.Industry, 1, 110, 1, new[] { "I1" },
            "Efficient refining lowers build costs.").With(buildCostCut: 0.10f, unlock: "Enables the Hauler."));
        _all.Add(T("I3", "Modular Shipyards", TechBranch.Industry, 2, 150, 2, new[] { "I2", "F4" },
            "Prefabricated modules speed construction; enables level-3 shipyards.").With(buildTimeCut: 0.15f));
        var i4 = T("I4", "Nanofabrication", TechBranch.Industry, 3, 240, 3, new[] { "I3" },
            "Atom-precise fabrication makes even capital hulls cheap."); i4.reqOre = OreType.Adamantine;
        _all.Add(i4.With(buildCostCut: 0.15f, buildTimeCut: 0.10f, unlock: "Enables Mk III refits."));

        // ---- Shipyard build power (parallel construction) ----
        // These are the "extra power here and there" line: each adds a slipway to EVERY shipyard you own,
        // so a well-researched level-2 yard can out-build a neglected level-4 one.
        _all.Add(T("I5", "Parallel Slipways", TechBranch.Industry, 2, 160, 2, new[] { "I3" },
            "A second construction cradle on every shipyard — lay down another hull alongside the first.")
            .With(shipyardPower: 1, unlock: "+1 build power at every shipyard you own."));
        _all.Add(T("I6", "Robotic Assembly Swarms", TechBranch.Industry, 3, 260, 3, new[] { "I5" },
            "Self-directing construction swarms work several hulls at once without getting in each other's way.")
            .With(shipyardPower: 1, buildTimeCut: 0.05f, unlock: "+1 build power at every shipyard you own."));
        var i7 = T("I7", "Autonomous Drydocks", TechBranch.Industry, 3, 380, 4, new[] { "I6", "I4" },
            "Shipyards that run themselves — a yard becomes a fleet factory able to hold the largest hulls on the stocks.");
        i7.reqOre = OreType.Adamantine;
        _all.Add(i7.With(shipyardPower: 1, unlock: "+1 build power at every shipyard you own. Enough power for the biggest hulls."));

        // ---- Doctrines (strategic paths — commit resources toward a way of playing) ----
        // Each doctrine is a strong, focused investment. They are deliberately expensive; you can afford
        // to follow only one or two deeply, shaping your empire toward war, science, exploration,
        // expansion or prosperity.
        _all.Add(T("D_WAR", "War Doctrine", TechBranch.Doctrine, 2, 200, 2, new[] { "W2" },
            "Put the empire on a war footing: militarized shipyards turn out warships far faster.")
            .With(buildTimeCut: 0.15f, unlock: "War focus. Deepen with Total-War Mobilization."));
        _all.Add(T("D_WAR2", "Total-War Mobilization", TechBranch.Doctrine, 3, 340, 4, new[] { "D_WAR" },
            "A war economy: mass-produced fleets, cheaper and quicker to build.")
            .With(buildTimeCut: 0.15f, buildCostCut: 0.12f));

        _all.Add(T("D_SCI", "Science Doctrine", TechBranch.Doctrine, 2, 200, 2, new[] { "S2" },
            "Enshrine research above all: your laboratories work markedly faster.")
            .With(researchRate: 0.30f, unlock: "Science focus. Deepen with Grand Unified Theory."));
        _all.Add(T("D_SCI2", "Grand Unified Theory", TechBranch.Doctrine, 3, 340, 4, new[] { "D_SCI" },
            "A civilization organized around discovery — research pours in.")
            .With(researchRate: 0.45f));

        _all.Add(T("D_EXP", "Exploration Doctrine", TechBranch.Doctrine, 2, 200, 2, new[] { "E2" },
            "A culture of pathfinders: every ship reaches substantially farther.")
            .With(rangeMult: 0.30f, unlock: "Exploration focus. Deepen with Boundless Frontier."));
        _all.Add(T("D_EXP2", "Boundless Frontier", TechBranch.Doctrine, 3, 340, 4, new[] { "D_EXP" },
            "No horizon is out of reach — fleets range across the galaxy.")
            .With(rangeMult: 0.50f));

        _all.Add(T("D_TER", "Expansion Doctrine", TechBranch.Doctrine, 2, 200, 2, new[] { "X2" },
            "Bend every world to habitability: terraforming reaches higher and finishes sooner.")
            .With(terraCeiling: 12f, terraSpeed: 0.30f, unlock: "Expansion focus. Deepen with Worldshaping."));
        _all.Add(T("D_TER2", "Worldshaping", TechBranch.Doctrine, 3, 340, 4, new[] { "D_TER" },
            "Routine planetary engineering makes even dead worlds gardens.")
            .With(terraCeiling: 15f, terraSpeed: 0.40f));

        _all.Add(T("D_PROS", "Prosperity Doctrine", TechBranch.Doctrine, 2, 200, 2, new[] { "I2" },
            "A doctrine of peace and plenty: richer yields and cheaper construction.")
            .With(oreYield: 0.25f, buildCostCut: 0.10f, unlock: "Prosperity focus. Deepen with Post-Scarcity Economy."));
        _all.Add(T("D_PROS2", "Post-Scarcity Economy", TechBranch.Doctrine, 3, 340, 4, new[] { "D_PROS" },
            "Automation and abundance: everything you build costs a fraction.")
            .With(oreYield: 0.30f, buildCostCut: 0.18f));

        // ---- Ancients (secret branch — gated by schematics recovered from precursor ruins) ----
        // Unlocked in the field by surveying worlds with Ancient Ruins. Xenoarchaeology (S3) opens the
        // path; each node also demands recovered schematics.
        var a1 = T("A1", "Precursor Alloys", TechBranch.Ancients, 2, 220, 3, new[] { "S3" },
            "Reverse-engineered precursor metallurgy — impossibly strong, impossibly cheap.");
        a1.reqSchematics = 1;
        _all.Add(a1.With(oreYield: 0.30f, buildCostCut: 0.12f, unlock: "Opens the precursor technology tree."));

        var a2 = T("A2", "Precursor Drives", TechBranch.Ancients, 3, 300, 4, new[] { "A1", "E3" },
            "Fragments of a faster-than-thought drive — fleets leap across the void.");
        a2.reqSchematics = 2;
        _all.Add(a2.With(rangeMult: 0.55f));

        var a3 = T("A3", "Precursor Ecoforming", TechBranch.Ancients, 3, 300, 4, new[] { "A1", "X4" },
            "Living machines that reshape a whole biosphere in a heartbeat.");
        a3.reqSchematics = 2;
        _all.Add(a3.With(terraCeiling: 18f, terraSpeed: 0.50f));

        var a4 = T("A4", "Precursor Ascendancy", TechBranch.Ancients, 3, 480, 6, new[] { "A2", "A3" },
            "The precursors' final synthesis. Your civilization inherits their mantle — mastery of every art at once.");
        a4.reqSchematics = 3;
        _all.Add(a4.With(researchRate: 0.35f, rangeMult: 0.35f, oreYield: 0.20f, terraSpeed: 0.25f,
            unlock: "The pinnacle of the tech tree — the legacy of the ancients is yours."));

        _byId = new Dictionary<string, Tech>();
        foreach (var t in _all) _byId[t.id] = t;
    }
}

// Fluent helper so node definitions read cleanly.
public static class TechExtensions
{
    public static Tech With(this Tech t, float researchRate = 0f, float buildCostCut = 0f, float buildTimeCut = 0f,
        float terraCeiling = 0f, float terraSpeed = 0f, float rangeMult = 0f, float oreYield = 0f,
        int shipyardPower = 0, int researchCap = 0, string unlock = null)
    {
        t.researchRate = researchRate; t.buildCostCut = buildCostCut; t.buildTimeCut = buildTimeCut;
        t.terraCeiling = terraCeiling; t.terraSpeed = terraSpeed; t.rangeMult = rangeMult; t.oreYield = oreYield;
        t.shipyardPower = shipyardPower; t.researchCap = researchCap;
        t.unlockNote = unlock;
        return t;
    }
}

// Why a queued project isn't currently being studied — drives the label on its queue widget.
public enum ResearchState { Researching, Paused, WaitingForCapacity, WaitingForPrereq, Impossible }

// One technology in the research queue. Several can be Researching at once, each occupying its
// CapacityCost of the empire's pooled research capacity.
public class ResearchOrder
{
    public string id;
    public float progress;      // research points banked into this project so far
    public bool paused;
    public ResearchState state = ResearchState.WaitingForCapacity;

    /// Research points already paid for this project, charged in full when it was queued.
    ///
    /// Research used to be billed CONTINUOUSLY — every project drained the shared bank a few points a
    /// second as its bar advanced. Two consequences, both bad: N parallel projects emptied the bank N
    /// times as fast, and the instant it hit zero every bar in the queue froze at once with no
    /// explanation. That is the "the first one finishes and everything else stops" bug.
    ///
    /// Paying up front, exactly as the shipyard does (UnitManager.QueueBuild), makes each bar
    /// independent: once a project is bought, nothing but pause, capacity or its own clock can slow it.
    public int pointsPaid;

    // Fractional research waiting to become a whole point. PER PROJECT, not shared: with one shared
    // accumulator whichever project happened to tick first would swallow the points meant for the
    // others, and parallel research would crawl on every project but one.
    [System.NonSerialized] public float carry;

    public bool Active => state == ResearchState.Researching;
    public Tech Def => TechDatabase.Get(id);
    public int Cost => Def != null ? Def.CapacityCost : 1;
    public float Progress01 { get { var t = Def; return t != null && t.cost > 0 ? Mathf.Clamp01(progress / t.cost) : 0f; } }
}

// Tracks researched technologies, gates what can be researched next, and pushes the aggregated effects
// into TechEffects / ShipUpgrades. Part of the save file.
public static class TechManager
{
    static readonly HashSet<string> researched = new HashSet<string>();
    public static event System.Action OnChanged;

    public static bool IsResearched(string id) => researched.Contains(id);

    // ---- Research queue (parallel, timed, pausable, editable — the twin of the shipyard queue) ----
    // Your research centres pool their RESEARCH CAPACITY (see ResearchCapacity); each project occupies
    // its CapacityCost while it is being studied and releases it on completion. So a big laboratory can
    // study several small technologies at once, or commit everything to one enormous project.
    static readonly List<ResearchOrder> queue = new List<ResearchOrder>();
    // Each ResearchOrder carries its own fractional research (see ResearchOrder.carry).
    public static bool Paused { get; private set; }   // global pause (the whole research effort)
    // Base RP/sec funnelled from the bank into EACH active project.
    //
    // This is a SPEED LIMIT, not the actual rate — a project can never absorb points faster than this,
    // but it's usually your income that binds, not this. It was 6, which let a 60 RP opening tech
    // resolve in ten seconds flat off the starting bank: the entire Foundations tier was gone before
    // the first scout had finished surveying anything. At 2.5 an opening tech is ~24s of dedicated
    // effort, a tier-2 node a minute or so, and the 480 RP precursor projects several minutes — long
    // enough that queue ORDER is a decision rather than a formality.
    //
    // Science technologies raise it (ResearchRateMult), so an empire that invests in laboratories
    // really does pull ahead rather than just banking points faster.
    const float DrainRate = 2.5f;

    public static IReadOnlyList<ResearchOrder> Queue => queue;
    public static bool IsQueued(string id) => Find(id) != null;
    public static int QueuePosition(string id) { for (int i = 0; i < queue.Count; i++) if (queue[i].id == id) return i; return -1; }

    public static ResearchOrder Find(string id)
    { foreach (var o in queue) if (o.id == id) return o; return null; }

    // The first project actually being studied (for compact readouts), or null.
    public static ResearchOrder ActiveOrder
    { get { foreach (var o in queue) if (o.Active) return o; return null; } }

    public static string Active => ActiveOrder?.id;

    // ---- Research capacity ----
    // Dev Mode has unlimited lab capacity — otherwise CanQueue would admit a tech that Schedule then
    // marks Impossible because your one starting laboratory can't hold it, which is the same
    // available-but-unfinishable trap PrereqsMet had.
    public static int TotalCapacity => GameMode.DevMode ? 9999 : ResearchCapacity.PlayerTotal();

    public static int UsedCapacity
    { get { int n = 0; foreach (var o in queue) if (o.Active) n += o.Cost; return n; } }

    public static int FreeCapacity => Mathf.Max(0, TotalCapacity - UsedCapacity);

    // Same allocation rule as the shipyard: walk the queue in order, hand out capacity, and let the
    // first project that doesn't fit hold its place rather than be leapfrogged. Paused projects and
    // ones that outsize the whole empire are skipped so the queue can't deadlock; a project whose
    // prerequisites haven't landed yet also waits without blocking the projects behind it.
    static void Schedule()
    {
        int total = TotalCapacity;
        int free = total;
        bool blocked = false;

        foreach (var o in queue)
        {
            var t = TechDatabase.Get(o.id);
            if (t == null) { o.state = ResearchState.Impossible; continue; }
            if (o.paused || Paused) { o.state = ResearchState.Paused; continue; }
            if (!PrereqsMet(t)) { o.state = ResearchState.WaitingForPrereq; continue; }
            if (t.CapacityCost > total) { o.state = ResearchState.Impossible; continue; }
            if (blocked) { o.state = ResearchState.WaitingForCapacity; continue; }
            if (t.CapacityCost <= free) { o.state = ResearchState.Researching; free -= t.CapacityCost; }
            else { o.state = ResearchState.WaitingForCapacity; blocked = true; }
        }
    }

    public static void Reschedule() => Schedule();

    public static float ActiveProgress01 => ActiveOrder != null ? ActiveOrder.Progress01 : 0f;
    public static float ActiveProgressRP => ActiveOrder != null ? ActiveOrder.progress : 0f;

    public static bool CanQueue(Tech t, out string reason)
    {
        reason = null;
        if (t == null) { reason = "unknown"; return false; }
        if (researched.Contains(t.id)) { reason = "researched"; return false; }
        if (IsQueued(t.id)) { reason = "queued"; return false; }

        // DEV MODE: everything is available. Only the two checks above survive, because they aren't
        // gates — "already researched" and "already queued" are statements about the queue itself, and
        // bypassing them would let you queue the same tech twice.
        //
        // The rest (empire level, lab capacity, prerequisites, ore discovery, ancient schematics) are the
        // progression, and Dev Mode exists to skip the progression. This had NO Dev Mode bypass at all,
        // which is why Dev Mode could afford any tech and still not research it.
        if (GameMode.DevMode) return true;

        if (EmpireTech.Level < t.minEmpireLevel) { reason = $"needs Empire Tech Level {t.minEmpireLevel}"; return false; }
        if (t.CapacityCost > TotalCapacity)
        { reason = $"needs {t.CapacityCost} research capacity (your labs total {TotalCapacity})"; return false; }
        foreach (var p in t.prereqs)                       // prereq must be researched OR queued ahead
            if (!researched.Contains(p) && !IsQueued(p)) { reason = "needs " + PrereqNames(t); return false; }
        if (t.reqOre != OreType.None && !ResearchManager.IsDiscovered(t.reqOre))
        { reason = $"discover {OreDatabase.Get(t.reqOre).displayName} first"; return false; }
        if (t.reqSchematics > 0 && AncientLore.SchematicsFound < t.reqSchematics)
        { reason = $"recover {t.reqSchematics} ancient schematic(s) — {AncientLore.SchematicsFound}/{t.reqSchematics}"; return false; }
        // AFFORDABILITY, checked here rather than discovered by Enqueue failing silently. A project is
        // bought in full the moment it is queued, so "can I queue this" and "can I pay for this" are now
        // the same question and the button must say so.
        if (ResearchManager.ResearchPoints < t.cost)
        { reason = $"needs {t.cost} research points (have {ResearchManager.ResearchPoints})"; return false; }
        return true;
    }

    public static void NotifyChanged() => OnChanged?.Invoke();

    public static bool Enqueue(string id)
    {
        var t = TechDatabase.Get(id);
        if (t == null || !CanQueue(t, out _)) return false;

        // Bought NOW, in full — see ResearchOrder.pointsPaid. Cancelling refunds the unspent share.
        int paid = 0;
        if (!GameMode.DevMode)
        {
            if (ResearchManager.ResearchPoints < t.cost) return false;
            ResearchManager.AddPoints(-t.cost);
            paid = t.cost;
        }
        queue.Add(new ResearchOrder { id = id, pointsPaid = paid });
        Schedule();
        OnChanged?.Invoke();
        return true;
    }

    // Pausing a project freezes its progress where it is and hands its capacity to the next project in
    // line; resuming picks it up exactly where it left off.
    public static void SetOrderPaused(ResearchOrder o, bool paused)
    {
        if (o == null || o.paused == paused) return;
        o.paused = paused;
        Schedule();
        OnChanged?.Invoke();
    }

    // Abandoning a project refunds the UNSPENT share of what it cost — the part of the work not yet
    // done. Mirrors scrapping a half-built hull, and it is the inverse of paying up front: a project
    // cancelled at 30% gives back 70% of its price.
    //
    // (It used to refund `progress`, which under the old pay-as-you-go model WAS the amount sunk in.
    // With the cost charged at queue time that same expression would refund the part already consumed
    // and keep the part that hadn't been — backwards, and profitable to cancel a nearly-finished
    // project.)
    static int UnspentRefund(ResearchOrder o)
    {
        if (o == null || o.pointsPaid <= 0) return 0;
        return Mathf.Clamp(Mathf.RoundToInt(o.pointsPaid * (1f - o.Progress01)), 0, o.pointsPaid);
    }

    public static void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= queue.Count) return;
        var o = queue[index];
        int refund = UnspentRefund(o);
        if (refund > 0) ResearchManager.AddPoints(refund);
        queue.RemoveAt(index);
        PruneQueue();
        Schedule();
        OnChanged?.Invoke();
    }

    public static void RemoveOrder(ResearchOrder o) { int i = queue.IndexOf(o); if (i >= 0) RemoveFromQueue(i); }

    // Drag-to-reorder, so a project you suddenly need can be pulled to the front of the labs.
    public static void MoveOrder(int from, int to)
    {
        if (from < 0 || from >= queue.Count) return;
        to = Mathf.Clamp(to, 0, queue.Count - 1);
        if (from == to) return;
        var o = queue[from];
        queue.RemoveAt(from);
        queue.Insert(to, o);
        Schedule();
        OnChanged?.Invoke();
    }

    public static void SetPaused(bool p) { Paused = p; Schedule(); OnChanged?.Invoke(); }

    public static void ClearQueue()
    {
        for (int i = queue.Count - 1; i >= 0; i--) RemoveFromQueue(i);
        queue.Clear();
        OnChanged?.Invoke();
    }

    // Drop any queued tech whose prerequisites can no longer ever be satisfied from the queue (e.g.
    // after its prerequisite was removed), refunding what had been sunk into it.
    static void PruneQueue()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < queue.Count; i++)
            {
                var t = TechDatabase.Get(queue[i].id); if (t == null) continue;
                bool ok = true;
                foreach (var p in t.prereqs)
                {
                    bool satisfied = researched.Contains(p);
                    if (!satisfied) for (int j = 0; j < queue.Count; j++) if (queue[j].id == p) { satisfied = true; break; }
                    if (!satisfied) { ok = false; break; }
                }
                if (!ok)
                {
                    int refund = UnspentRefund(queue[i]);
                    if (refund > 0) ResearchManager.AddPoints(refund);
                    queue.RemoveAt(i); changed = true; break;
                }
            }
        }
    }

    // Advance every project being studied (called each frame by ResearchTaskManager). Each active
    // project draws its own stream of research points from the bank, so running several at once really
    // does cost several times as much per second — capacity lets you do it, income decides if you can.
    public static void Tick(float dt)
    {
        if (queue.Count == 0 || dt <= 0f) return;
        Schedule();

        float rate = DrainRate * TechEffects.ResearchRateMult;

        // Walk FORWARD so that when the bank is too thin to feed everything, the front of the queue is
        // served first — that is what the player's ordering means. (Walking backwards quietly handed a
        // scarce bank to whatever was queued last.) Completions are collected and applied afterwards so
        // the list isn't mutated mid-walk.
        List<Tech> finished = null;

        foreach (var o in queue)
        {
            if (!o.Active) continue;
            var t = o.Def;
            if (t == null) continue;

            // Progress is now PURELY TIME. The project was paid for in full when it was queued, so
            // nothing here touches the shared bank — which is what makes parallel bars independent.
            // Each keeps its own progress toward its own cost, so two started together finish at their
            // own separate times and a cheap tech never rides in on an expensive one.
            //
            // Capacity is still the limit on how many may run at once (Schedule decides that); the
            // bank is now the limit on how many you can BUY, which is a decision the player makes at
            // the moment of queueing rather than a rug pulled out from under a bar already moving.
            o.progress += rate * dt;

            if (o.progress >= t.cost) (finished ??= new List<Tech>()).Add(t);
        }

        if (finished != null)
        {
            foreach (var t in finished)
            {
                var done = Find(t.id);
                if (done != null) queue.Remove(done);
                MarkResearched(t);
            }
            PruneQueue();
            Schedule();
        }
    }

    public static bool PrereqsMet(Tech t)
    {
        // Dev Mode skips the tree. Without this, CanQueue would let a tech in and then Schedule would
        // park it on WaitingForPrereq forever — available to queue and impossible to finish, which is
        // worse than not offering it.
        if (GameMode.DevMode) return true;
        foreach (var p in t.prereqs) if (!researched.Contains(p)) return false;
        return true;
    }

    public static bool CanResearch(Tech t, out string reason)
    {
        reason = null;
        if (t == null) { reason = "unknown"; return false; }
        if (researched.Contains(t.id)) { reason = "researched"; return false; }
        if (EmpireTech.Level < t.minEmpireLevel) { reason = $"needs Empire Tech Level {t.minEmpireLevel}"; return false; }
        if (!PrereqsMet(t)) { reason = "needs " + PrereqNames(t); return false; }
        if (t.reqOre != OreType.None && !ResearchManager.IsDiscovered(t.reqOre))
        { reason = $"discover {OreDatabase.Get(t.reqOre).displayName} first"; return false; }
        if (t.reqSchematics > 0 && AncientLore.SchematicsFound < t.reqSchematics)
        { reason = $"recover {t.reqSchematics} ancient schematic(s) — {AncientLore.SchematicsFound}/{t.reqSchematics}"; return false; }
        if (ResearchManager.ResearchPoints < t.cost) { reason = $"need {t.cost} RP"; return false; }
        return true;
    }

    static string PrereqNames(Tech t)
    {
        var missing = new List<string>();
        foreach (var p in t.prereqs) if (!researched.Contains(p)) { var pt = TechDatabase.Get(p); missing.Add(pt != null ? pt.name : p); }
        return string.Join(", ", missing);
    }

    public static bool Research(string id)   // instant-complete (dev / fallback; the UI queues instead)
    {
        var t = TechDatabase.Get(id);
        if (t == null || !CanResearch(t, out _)) return false;
        ResearchManager.AddPoints(-t.cost);
        MarkResearched(t);
        return true;
    }

    static void MarkResearched(Tech t)
    {
        researched.Add(t.id);
        Recompute();
        SimpleAudio.Instance?.PlayNotify(NotifKind.Research);
        NotificationManager.Instance?.Push($"Researched: {t.name}",
            t.desc + (string.IsNullOrEmpty(t.unlockNote) ? "" : "  " + t.unlockNote), null, NotifKind.Research);
        OnChanged?.Invoke();
    }

    // Sum every researched node's effects into the live modifier tables.
    public static void Recompute()
    {
        TechEffects.Reset();
        float rr = 0f, bc = 0f, bt = 0f, tc = 0f, ts = 0f, rm = 0f, oy = 0f;
        int sp = 0, rc = 0;
        foreach (var id in researched)
        {
            var t = TechDatabase.Get(id); if (t == null) continue;
            rr += t.researchRate; bc += t.buildCostCut; bt += t.buildTimeCut;
            tc += t.terraCeiling; ts += t.terraSpeed; rm += t.rangeMult; oy += t.oreYield;
            sp += t.shipyardPower; rc += t.researchCap;
        }
        // THE VAEL LEGACY — recovering all ten Vael fragments grants their whole making as a broad, permanent
        // surge, folded straight into the same effect tables every system already reads (AncientClues).
        if (AncientClues.AllFound)
        {
            rr += 0.6f; oy += 0.5f; rm += 0.6f; ts += 0.6f; tc += 15f; bc += 0.2f; bt += 0.2f;
        }
        TechEffects.ShipyardPowerBonus = sp;
        TechEffects.ResearchCapacityBonus = rc;
        TechEffects.ResearchRateMult = 1f + rr;
        TechEffects.BuildCostMult = Mathf.Clamp(1f - bc, 0.4f, 1f);
        TechEffects.BuildTimeMult = Mathf.Clamp(1f - bt, 0.4f, 1f);
        TechEffects.TerraformCeilingBonus = tc;
        TechEffects.TerraformSpeedMult = 1f + ts;
        TechEffects.OreYieldMult = 1f + oy;
        ShipUpgrades.TechRange = 1f + rm;   // multiplies the empire-level range bonus
    }

    public static void Reset()
    {
        researched.Clear();
        queue.Clear(); Paused = false;
        Recompute();
        OnChanged?.Invoke();
    }

    // ---- Save / load ----
    public static List<string> Export() => new List<string>(researched);
    public static void Import(List<string> ids)
    {
        researched.Clear();
        queue.Clear(); Paused = false;
        if (ids != null) foreach (var id in ids) researched.Add(id);
        Recompute();
        OnChanged?.Invoke();
    }

    // The in-progress research queue is saved separately from the researched set, so a reload resumes
    // half-finished projects exactly where they were (progress, order and pause state intact).
    public static List<ResearchOrderDTO> ExportQueue()
    {
        var list = new List<ResearchOrderDTO>();
        foreach (var o in queue) list.Add(new ResearchOrderDTO { id = o.id, progress = o.progress, paused = o.paused, pointsPaid = o.pointsPaid });
        return list;
    }

    public static void ImportQueue(List<ResearchOrderDTO> dtos, bool paused)
    {
        queue.Clear();
        Paused = paused;
        if (dtos != null)
            foreach (var d in dtos)
                if (TechDatabase.Get(d.id) != null && !researched.Contains(d.id))
                    queue.Add(new ResearchOrder
                    {
                        id = d.id, progress = d.progress, paused = d.paused,
                        // Old saves predate paying up front, so their in-flight projects were never
                        // charged. Treat them as fully paid — the alternative is billing the player a
                        // second time for work they already have on the bar.
                        pointsPaid = d.pointsPaid > 0 ? d.pointsPaid : TechDatabase.Get(d.id).cost
                    });
        Schedule();
        OnChanged?.Invoke();
    }
}
