// ============================================================================================
// BUILD THE SHIP ART MANIFEST
//
// Emits `tools/ship-generation-manifest.json` — one entry per model we need Meshy to make, with
// the prompt already written. Two hundred-odd prompts is not something to hand-type: a prompt is
// a SPECIES STYLE crossed with a CLASS SILHOUETTE, so it is generated from those two tables and
// stays consistent across the whole fleet by construction.
//
// Every prompt ends with the same geometry clause. That clause is not decoration — it is what
// makes ShipMeshManifest's bounds heuristic land:
//
//   * "longer than it is wide"      -> longest axis is the length          (rule 1)
//   * "flat featureless underside"  -> shortest axis is up                 (rule 2)
//   * "engines massed at the rear"  -> heavier half is the stern           (rule 3)
//
// Get those three right in the prompt and the mesh auto-orients on import, which is the
// difference between dropping 200 hulls in and hand-writing 200 rotation lines.
//
//   node tools/build-ship-manifest.mjs
// ============================================================================================

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));

// ---- The geometry clause every prompt carries -------------------------------------------------
const GEOMETRY =
  'The hull is clearly longer than it is wide and wider than it is tall, with a flat featureless ' +
  'underside and all the greebling, towers and antennae on the top surface. Engines and reactor ' +
  'bulk are massed at the rear; the bow is slim and sensor-tipped. Single centred model, ' +
  'symmetrical, game-ready low-poly asset on a plain background.';

// A variant for things that genuinely are not hull-shaped — stations, asteroids, artifacts. They
// get to be radially symmetric, and the manifest marks them `spin` or `nospin` instead.
const GEOMETRY_RADIAL =
  'Radially symmetric, roughly as wide as it is tall, readable from every angle. ' +
  'Single centred model, game-ready low-poly asset on a plain background.';

// ============================================================================================
// SPECIES STYLE — the material, palette and build philosophy of each civilization's shipwrights.
// Drawn from Assets/Scripts/Data/Species.cs so the fleet matches the biology.
// ============================================================================================
const SPECIES = [
  {
    key: 'Terran',
    name: 'Terrans',
    style:
      'Terran human engineering: brushed white and gunmetal-grey plating with exposed structural ' +
      'ribs, angular utilitarian panels, black heat radiators, and cobalt-blue running lights and ' +
      'engine glow. Hard science-fiction, built rather than grown — riveted, modular, practical.',
  },
  {
    key: 'Aquarii',
    name: 'Aquarii',
    style:
      'Aquarii amphibious engineering: smooth streamlined organic hull like a manta ray or a ' +
      'nautilus shell, seamless pearlescent teal and turquoise surfaces, swept fins and gill-slit ' +
      'vents, softly glowing aquamarine bioluminescent seams. No hard corners anywhere.',
  },
  {
    key: 'Pyrothian',
    name: 'Pyrothians',
    style:
      'Pyrothian silicate engineering: massive blocky armoured hull of black obsidian and rough ' +
      'basalt slabs, thick overlapping ablative plates, cracks glowing molten orange and red like ' +
      'cooling lava, brass heat-exchanger fins. Brutally heavy, crystalline, furnace-forged.',
  },
  {
    key: 'Cryithn',
    name: 'Cryithn',
    style:
      'Cryithn cryogenic engineering: slender faceted hull of pale blue-white ice and frosted ' +
      'silver alloy, sharp crystalline shards and long spines, rime and frost crusting the plating, ' +
      'dim cold cyan light bleeding from deep narrow slots. Ancient, austere, patient.',
  },
  {
    key: 'Sylvan',
    name: 'Sylvans',
    style:
      'Sylvan photosynthetic engineering: a vessel GROWN rather than built — living pale wood and ' +
      'woven vine over a seed-pod hull, broad translucent green leaf-like solar sails, curling ' +
      'tendrils, amber sap-light glowing along the grain, moss and small blossoms on the plating.',
  },
];

// ============================================================================================
// CLASS SILHOUETTE — what each of the 29 hull classes IS, independent of who built it.
//
// `size` is a rough word for scale so the fleet reads at a glance, and `enum` is the UnitType
// enum name, which is what the filename keys on so code can look a mesh up without a table.
// ============================================================================================
const CLASSES = [
  // ---- Tier-1 starters ----
  { enum: 'Scout', silhouette: 'a tiny fast single-seat scout: a slim dart with oversized sensor dish and a big engine' },
  { enum: 'ResearchShip', silhouette: 'a small mobile laboratory: a slender hull with sensor booms, dish arrays and observation blisters' },
  { enum: 'Fighter', silhouette: 'a small agile warship: a compact arrowhead with forward gun barrels and stubby wings' },
  { enum: 'ColonyShip', silhouette: 'a huge slow settler vessel: a fat cargo-heavy hull carrying prefabricated habitat modules and a landing pod' },
  // ---- Mk II ----
  { enum: 'ScoutII', silhouette: 'an upgraded scout: the dart hull lengthened, twin engines and a second sensor mast' },
  { enum: 'ResearchShipII', silhouette: 'an upgraded mobile laboratory: a longer hull with a dorsal telescope array and twin dish booms' },
  { enum: 'FighterII', silhouette: 'a heavier strike fighter: a broader arrowhead with four gun barrels and armoured shoulders' },
  // ---- Level-3 utility ----
  { enum: 'Terraformer', silhouette: 'a vast climate-engineering vessel: an industrial hull dominated by huge atmosphere-processor nozzles and cooling towers' },
  { enum: 'Probe', silhouette: 'a tiny expendable deep-space probe: a small instrument core with a solar collector and a whip antenna' },
  // ---- Civilian ----
  { enum: 'Miner', silhouette: 'a civilian mining barge: a blunt industrial hull with ore hoppers, cutting arms and a refinery drum' },
  { enum: 'Transport', silhouette: 'a civilian freight hauler: a long spine strung with modular cargo containers behind a small bridge' },
  // ---- Combat capitals ----
  { enum: 'Frigate', silhouette: 'a nimble patrol warship: a lean armoured hull with turret blisters and missile racks' },
  { enum: 'Cruiser', silhouette: 'a heavy warship: a thick armoured battle-line hull bristling with turret batteries' },
  { enum: 'Carrier', silhouette: 'a fleet carrier: a long flat-topped hull with an open flight deck and strike-craft launch bays along the flanks' },
  { enum: 'Dreadnought', silhouette: 'a colossal capital ship of the line: an enormous slab-armoured hull with immense spinal cannon and layered gun batteries' },
  // ---- Advanced utility ----
  { enum: 'ScienceVessel', silhouette: 'a large deep-survey laboratory: a broad hull crowned with a great sensor ring and radio dishes' },
  { enum: 'Explorer', silhouette: 'a long-range pathfinder: a lean endurance hull with enormous fuel tanks and a wide sensor prow' },
  // ---- Mk III ----
  { enum: 'ScoutIII', silhouette: 'the finest scout hull: a swept needle-shaped racer with three engines and a faired sensor spine' },
  { enum: 'FighterIII', silhouette: 'a top-line strike fighter: a sleek predatory arrowhead with underslung cannon pods and swept wings' },
  { enum: 'ResearchShipIII', silhouette: 'a state-of-the-art mobile laboratory: an elegant hull with a rotating instrument ring and a forward scanning lens' },
];

// Stations are structures, not hulls — different geometry clause and different motion flags.
const STATIONS = [
  { enum: 'BattleStation', silhouette: 'a rudimentary orbital fortress: an armoured drum ringed with heavy gun turrets and missile silos', flag: 'spin' },
  { enum: 'ResearchStation', silhouette: 'an orbital laboratory: a habitat torus with dish arrays, telescopes and glowing lab windows', flag: 'spin' },
  { enum: 'RelayStation', silhouette: 'a comms-and-navigation relay: a slender mast carrying a huge parabolic dish and antenna clusters', flag: 'nospin' },
  { enum: 'SupplyStation', silhouette: 'an orbital depot: a cluster of fuel spheres and cargo drums around a docking spine', flag: 'spin' },
  { enum: 'MultiStation', silhouette: 'a large multi-role orbital complex: stacked habitat rings, dish arrays, docking arms and defensive turrets', flag: 'spin' },
  { enum: 'TerraformStation', silhouette: 'a colossal climate-engineering platform: a vast ring of atmosphere-processor nozzles around a reactor core', flag: 'spin' },
  { enum: 'DeepSpaceStation', silhouette: 'a self-sufficient deep-space outpost: a compact core wrapped in enormous solar collector wings', flag: 'spin' },
  { enum: 'MegaStation', silhouette: 'an orbital city the size of a small moon: vast nested habitat rings, spires, docking bays and a glowing reactor heart', flag: 'spin' },
  { enum: 'HyperRelay', silhouette: 'a massive fast-travel relay: a colossal open ring of arcing emitters around a blazing energy aperture', flag: 'nospin' },
];

// ============================================================================================
// THE NEUTRAL SET — everything that belongs to nobody. Enemies, the ancients, wrecks, rocks.
// These are one-offs rather than a species x class grid, so they carry their own full prompts.
// ============================================================================================
const NEUTRAL = [
  // ---- Marauders: the hostile raider faction ----
  { folder: 'Enemies', enum: 'Marauder_Raider', prompt: 'A pirate raider starship cobbled together from scavenged wreckage: mismatched scavenged hull plates in rust red and dirty yellow, welded-on armour, exposed pipework, a crude ram prow and oversized bolted-on engines. Battered, asymmetric, menacing.', geom: 'ship' },
  { folder: 'Enemies', enum: 'Marauder_Corsair', prompt: 'A pirate corsair warship: a lean scavenged hull with harpoon launchers and boarding clamps, rust-red and bone-white war paint, jagged blade-like fins and a slashed skull marking.', geom: 'ship' },
  { folder: 'Enemies', enum: 'Marauder_Brute', prompt: 'A heavy pirate brute-ship: a huge ugly slab of welded scrap armour around enormous engines, chained-on cargo pods, spiked ram prow, rust and soot streaked.', geom: 'ship' },
  { folder: 'Enemies', enum: 'Marauder_Carrier', prompt: 'A pirate carrier hulk: a gutted freighter converted to a strike-craft mothership, open ragged launch bays cut into the flanks, scavenged plating, rust red and dirty yellow.', geom: 'ship' },
  { folder: 'Enemies', enum: 'Marauder_Outpost', prompt: 'A pirate deep-space outpost: an asteroid hollowed out and studded with welded scrap docking arms, crude gun turrets and flickering red beacons.', geom: 'radial' },

  // ---- The Swarm: a mindless hostile bio-fleet ----
  { folder: 'Enemies', enum: 'Swarm_Drone', prompt: 'A small biomechanical swarm drone: a chitinous insectile body of glossy black carapace with violet veins, folded blade limbs and a single glowing magenta eye.', geom: 'ship' },
  { folder: 'Enemies', enum: 'Swarm_Ravager', prompt: 'A biomechanical swarm warbeast: a large chitinous predator hull of black and violet carapace, ribbed segments, spined talons and a gaping toothed maw at the prow.', geom: 'ship' },
  { folder: 'Enemies', enum: 'Swarm_Hive', prompt: 'A biomechanical swarm hive-ship: a colossal bulbous chitinous mass of black and violet carapace riddled with glowing magenta birthing orifices and clustered spines.', geom: 'ship' },

  // ---- The Ancients: a long-dormant precursor civilization ----
  { folder: 'Ancients', enum: 'Ancient_Sentinel', prompt: 'A dormant precursor sentinel drone: a smooth seamless obelisk of dark polished stone-metal with glowing gold glyph channels, floating geometric rings, no visible engines or seams. Utterly alien and machine-perfect.', geom: 'radial' },
  { folder: 'Ancients', enum: 'Ancient_Warden', prompt: 'A precursor warden warship: an enormous smooth blade-shaped monolith of dark polished stone-metal etched with glowing gold glyphs, weapon apertures that are simply openings in an unbroken surface.', geom: 'ship' },
  { folder: 'Ancients', enum: 'Ancient_Monolith', prompt: 'A dormant precursor monolith drifting in space: a vast featureless black slab inscribed with faintly glowing gold geometric glyphs, chipped and pitted by aeons of micrometeorites.', geom: 'radial' },
  { folder: 'Ancients', enum: 'Ancient_Gate', prompt: 'A dormant precursor jump gate: a colossal broken ring of dark polished stone-metal with glowing gold glyph channels, one segment missing, drifting inert.', geom: 'radial' },
  { folder: 'Ancients', enum: 'Ancient_Vault', prompt: 'A sealed precursor vault station: a smooth dark dodecahedral shell of stone-metal with recessed gold glyph seams and a single sealed circular door.', geom: 'radial' },
  { folder: 'Ancients', enum: 'Ancient_Observatory', prompt: 'A precursor observatory: a great dark ring holding a floating faceted crystal lens at its centre, gold glyphs tracking around the rim, silent and inert.', geom: 'radial' },

  // ---- Lost artifacts: the small things a survey turns up ----
  { folder: 'Artifacts', enum: 'Artifact_Obelisk', prompt: 'A small alien artifact floating in space: a tapered black obelisk covered in glowing violet alien script, edges worn smooth by time.', geom: 'radial' },
  { folder: 'Artifacts', enum: 'Artifact_Orb', prompt: 'A small alien artifact: a levitating polished sphere of dark iridescent metal split by glowing turquoise energy seams, with a slowly orbiting outer ring.', geom: 'radial' },
  { folder: 'Artifacts', enum: 'Artifact_Shard', prompt: 'A small alien artifact: a jagged shard of luminous crystal in amethyst and gold, fractured, radiating faint light from within.', geom: 'radial' },
  { folder: 'Artifacts', enum: 'Artifact_Codex', prompt: 'A small alien artifact: a folded metallic tablet of interlocking plates covered in engraved glowing glyphs, half-unfolded like a puzzle box.', geom: 'radial' },
  { folder: 'Artifacts', enum: 'Artifact_Beacon', prompt: 'A small derelict alien beacon: a slender spire of corroded alloy with a cracked glowing green lamp at its crown and three splayed anchor legs.', geom: 'radial' },
  { folder: 'Artifacts', enum: 'Artifact_Engine', prompt: 'A small alien artifact: an exposed alien drive core, a cage of curved struts around a suspended humming ball of blue-white light.', geom: 'radial' },

  // ---- Derelicts: dead hulls to salvage ----
  { folder: 'Derelicts', enum: 'Derelict_Freighter', prompt: 'A derelict starship wreck: a broken freighter hull snapped in half, hull plates torn open exposing ribs and decks, burnt scorched grey metal, dark and powerless.', geom: 'ship' },
  { folder: 'Derelicts', enum: 'Derelict_Warship', prompt: 'A derelict warship wreck: a gutted battle-scarred hull with shattered turrets, a gaping hole blown through the midsection, blackened and cold.', geom: 'ship' },
  { folder: 'Derelicts', enum: 'Derelict_Station', prompt: 'A derelict space station: a broken habitat ring with collapsed spokes, torn hull panels, drifting debris and dead dark windows.', geom: 'radial' },
  { folder: 'Derelicts', enum: 'Derelict_Colony', prompt: 'A derelict colony ship wreck: a huge settler vessel long dead, habitat modules ruptured and frozen, hull grey with dust and micrometeorite pitting.', geom: 'ship' },

  // ---- Asteroids: the furniture of every system ----
  { folder: 'Asteroids', enum: 'Asteroid_Rocky_Small', prompt: 'A small rocky asteroid: an irregular lumpy grey stone with deep craters and sharp fractured edges, dusty and pitted.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Rocky_Large', prompt: 'A large rocky asteroid: a massive irregular grey-brown boulder with a huge impact crater, ridges and scattered surface rubble.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Metallic', prompt: 'A metallic asteroid: a dense irregular chunk of raw nickel-iron with a dull silver sheen, sharp angular fracture faces and rusty ore veins.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Icy', prompt: 'An icy asteroid: an irregular mass of dirty blue-white ice and frozen rock, translucent facets, frost and sublimating vents.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Crystalline', prompt: 'A crystalline asteroid: a dark rocky core bristling with clusters of large glowing violet and cyan crystal spars.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Volcanic', prompt: 'A volcanic asteroid: a blackened irregular rock split by glowing molten orange fissures, cooling crust and scorched vents.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Ore_Rich', prompt: 'An ore-rich asteroid: a grey stone body veined with bright seams of gold, copper and green mineral crystal breaking through the surface.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Shattered', prompt: 'A shattered asteroid fragment: a wedge-shaped splinter of grey rock with one flat sheared fracture face and a jagged broken back.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Cometary', prompt: 'A comet nucleus: a dark irregular body of tar-black dust and dirty ice, riddled with vents and pits, faintly outgassing.', geom: 'radial' },
  { folder: 'Asteroids', enum: 'Asteroid_Cluster', prompt: 'A tight cluster of small asteroids: five or six irregular grey rocks of varying size drifting close together as one group.', geom: 'radial' },
];

// ============================================================================================
// ASSEMBLY
// ============================================================================================
// The silhouettes read as sentence fragments ("a tiny fast scout…") so they compose into the class
// tables cleanly; at the front of a prompt they just need their first letter raised.
const cap = s => s.charAt(0).toUpperCase() + s.slice(1);

const jobs = [];

for (const sp of SPECIES) {
  for (const c of CLASSES) {
    jobs.push({
      name: `${sp.key}_${c.enum}`,
      folder: `Ships/${sp.key}`,
      species: sp.key,
      unitType: c.enum,
      kind: 'ship',
      flag: null,
      prompt: `${cap(c.silhouette)}, in the style of the ${sp.name}. ${sp.style} ${GEOMETRY}`,
    });
  }
  for (const s of STATIONS) {
    jobs.push({
      name: `${sp.key}_${s.enum}`,
      folder: `Stations/${sp.key}`,
      species: sp.key,
      unitType: s.enum,
      kind: 'station',
      flag: s.flag,
      prompt: `${cap(s.silhouette)}, in the style of the ${sp.name}. ${sp.style} ${GEOMETRY_RADIAL}`,
    });
  }
}

for (const n of NEUTRAL) {
  jobs.push({
    name: n.enum,
    folder: n.folder,
    species: null,
    unitType: n.enum,
    kind: n.folder.toLowerCase(),
    flag: n.geom === 'radial' ? 'spin' : null,
    prompt: `${n.prompt} ${n.geom === 'radial' ? GEOMETRY_RADIAL : GEOMETRY}`,
  });
}

const out = {
  generated: 'tools/build-ship-manifest.mjs',
  format: 'fbx',
  counts: {
    species: SPECIES.length,
    shipClasses: CLASSES.length,
    stationClasses: STATIONS.length,
    perSpecies: CLASSES.length + STATIONS.length,
    civTotal: SPECIES.length * (CLASSES.length + STATIONS.length),
    neutral: NEUTRAL.length,
    total: jobs.length,
  },
  jobs,
};

writeFileSync(join(here, 'ship-generation-manifest.json'), JSON.stringify(out, null, 2));

console.log(`${jobs.length} jobs`);
console.log(`  ${out.counts.civTotal} civilization models ` +
            `(${SPECIES.length} species x ${out.counts.perSpecies})`);
console.log(`  ${out.counts.neutral} neutral models`);
for (const f of [...new Set(jobs.map(j => j.folder))]) {
  console.log(`    ${f}: ${jobs.filter(j => j.folder === f).length}`);
}
