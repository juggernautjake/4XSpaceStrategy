// ============================================================================================
// CAN EVERY STRUCTURE ACTUALLY BE BUILT?
//
//   node tools/build-placement-audit.mjs
//
// Reads the structure table straight out of Assets/Scripts/Data/SurfaceBuilding.cs (so it cannot drift
// from the data) and walks every placeable class through the rules the placement path enforces:
//
//   SurfaceBuildManager.CanPlaceType / CanPlace / CellBuildable   ownership, settlement, tech, uniqueness,
//                                                                 the index highlight (SurfaceIndex.Floor)
//   BuildShapeRules / BuildPlacement                              draw mode, minimum tiles, box shapes
//   BuildScaling / SurfaceLabor                                   cost at the minimum size, Labor stretch
//   PlayerEconomy                                                 the starting stock and storage ceiling
//
// Then sweeps a handful of representative worlds for whether each index-driven class has ANY legal
// ground — the presence rules ported from SurfaceIndex.Present / WeatherAirCeiling / SolarViable, and the
// geothermal band width ported from GeothermalMap.FaultAt (does a 2x2 square fit across a fault line?).
//
// Exits 1 when a class is unplaceable everywhere, or its rules contradict each other in a way the
// player cannot work around.
// ============================================================================================
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const src = readFileSync(join(root, 'Assets/Scripts/Data/SurfaceBuilding.cs'), 'utf8');
const techSrc = readFileSync(join(root, 'Assets/Scripts/Systems/Tech.cs'), 'utf8');
const idxSrc = readFileSync(join(root, 'Assets/Scripts/Data/SurfaceIndex.cs'), 'utf8');
const econSrc = readFileSync(join(root, 'Assets/Scripts/Systems/PlayerEconomy.cs'), 'utf8');
const laborSrc = readFileSync(join(root, 'Assets/Scripts/Systems/SurfaceLabor.cs'), 'utf8');
const scaleSrc = readFileSync(join(root, 'Assets/Scripts/Systems/BuildScaling.cs'), 'utf8');

let fails = 0, warns = 0;
const FAIL = m => { fails++; console.log(`  FAIL  ${m}`); };
const WARN = m => { warns++; console.log(`  warn  ${m}`); };

const num = (re, s, d) => { const m = s.match(re); return m ? parseFloat(m[1]) : d; };

// ---- Constants read from the C# -------------------------------------------------------------
const ShowFloor = num(/ShowFloor\s*=\s*([\d.]+)f/, idxSrc, 0.7);
const BaseFloor = num(/BaseFloor\s*=\s*([\d.]+)f/, idxSrc, 0.4);
const SolarFloor = num(/SolarFloor\s*=\s*([\d.]+)f/, idxSrc, 0.2);
const TraceAir = num(/TraceAir\s*=\s*([\d.]+)f/, idxSrc, 0.15);
const BaseCapacity = num(/BaseCapacity\s*=\s*([\d.]+)f/, econSrc, 3000);
const StartMetal = num(/StartFloorMetal\s*=\s*([\d.]+)f/, econSrc, 900);
const StartEnergy = num(/StartFloorEnergy\s*=\s*([\d.]+)f/, econSrc, 650);
const CapitolLabor = num(/CapitolBase\s*=\s*([\d.]+)f/, laborSrc, 2);
const CostStep = num(/CostStep\s*=\s*([\d.]+)f/, scaleSrc, 0.05);
const Penalty = num(/MissingLaborTimePenalty\s*=\s*([\d.]+)f/, scaleSrc, 0.35);
const Floor = k => k === 'Mineral' ? ShowFloor : k === 'Solar' ? SolarFloor : BaseFloor;
const CostMult = n => n + CostStep * (n <= 1 ? 0 : n * (n - 1) / 2);

// ---- The structure table ----------------------------------------------------------------------
const B = {};
const ctor = /new SurfaceBuildingInfo\(SurfaceBuildingType\.(\w+),\s*SurfaceBuildingCategory\.(\w+),[\s\S]*?S\(([\d,\s]*)\),\s*SurfaceIndexKind\.(\w+),\s*(\d+),\s*(\d+),\s*([\d.]+)f/g;
for (let m; (m = ctor.exec(src));) {
  const cells = m[3].split(',').map(s => s.trim()).filter(Boolean).length / 2;
  B[m[1]] = { type: m[1], cat: m[2], cells, index: m[4], metal: +m[5], energy: +m[6], time: +m[7],
              drawMode: 'Free', minTiles: 1, minIndex: 0, tech: null, unique: false, draw: 0, water: false };
}
for (const m of src.matchAll(/Drawn\(SurfaceBuildingType\.(\w+),\s*BuildDrawMode\.(\w+),\s*(\d+)\)/g))
  if (B[m[1]]) { B[m[1]].drawMode = m[2]; B[m[1]].minTiles = +m[3]; }
for (const m of src.matchAll(/Require\(SurfaceBuildingType\.(\w+),\s*([\d.]+)f/g)) if (B[m[1]]) B[m[1]].minIndex = +m[2];
for (const m of src.matchAll(/Draw\(SurfaceBuildingType\.(\w+),\s*([\d.]+)f\)/g)) if (B[m[1]]) B[m[1]].draw = +m[2];
// requiredTech / uniquePerWorld are set on a local variable after construction; map local -> type.
const locals = {};
for (const m of src.matchAll(/var (\w+) = new SurfaceBuildingInfo\(SurfaceBuildingType\.(\w+)/g)) locals[m[1]] = m[2];
for (const m of src.matchAll(/(\w+)\.requiredTech = "(\w+)"/g)) if (B[locals[m[1]]]) B[locals[m[1]]].tech = m[2];
for (const m of src.matchAll(/(\w+)\.uniquePerWorld = true/g)) if (B[locals[m[1]]]) B[locals[m[1]]].unique = true;
for (const m of src.matchAll(/(\w+)\.allowsWater = true/g)) if (B[locals[m[1]]]) B[locals[m[1]]].water = true;
for (const m of src.matchAll(/(\w+)\.requiresWater = true/g)) if (B[locals[m[1]]]) B[locals[m[1]]].requiresWater = true;

const techs = new Set([...techSrc.matchAll(/T\("(\w+)",\s*"([^"]+)"/g)].map(m => m[1]));
const techName = Object.fromEntries([...techSrc.matchAll(/T\("(\w+)",\s*"([^"]+)"/g)].map(m => [m[1], m[2]]));

// Grown or upgraded into, never offered in the tray — mirrors PlanetViewWindow.PlaceableFromTray.
const GROWN = new Set(['Settlement', 'Town', 'City']);
const SPECIAL = { PlanetCapitol: 'placed once, free, by the new-game opening only (founding)',
                  ColonyShipBase: 'placed only by a colony ship landing' };

// ---- Labor per tile (BuildScaling.LaborPerTile) -------------------------------------------------
const laborPerTile = t => ({ FissionReactor: 3, FusionReactor: 3, SteamTurbine: 2, CombustionPlant: 2,
                             Spaceport: 2, SurfaceShipyard: 2, PowerNode: 0.5 }[t] ?? 1);

// ---- Geothermal: how wide is the >=40 band across a fault, by activity (GeothermalMap.FaultAt) ----
const smooth = t => t * t * (3 - 2 * t);
function faultAt(d, act) {
  const onLine = 0.4 + (1 - 0.4) * act, edge = 0.7 * act, reach = 1.1 + (3 - 1.1) * act;
  if (d >= reach) return 0;
  return edge + (onLine - edge) * smooth(1 - d / reach);
}
function halfWidth(act, floor) { let d = 0; while (d < 4 && faultAt(d, act) >= floor) d += 0.01; return d; }
// A 2x2 square of cell centres spans |cos|+|sin| across a line at angle θ: worst case √2 at 45°.
let squareFitsFrom = null;
for (let a = 0; a <= 1.0001; a += 0.01) if (2 * halfWidth(a, BaseFloor) >= Math.SQRT2) { squareFitsFrom = a; break; }
let squareFitsAxis = null;   // the best case, a fault running along a grid axis: needs a 1-tile span
for (let a = 0; a <= 1.0001; a += 0.01) if (2 * halfWidth(a, BaseFloor) >= 1) { squareFitsAxis = a; break; }

// ---- Representative worlds -------------------------------------------------------------------
// Presence rules from SurfaceIndex.Present: Solar needs a tile over its floor, Geothermal needs plates or
// plumes, Water needs water on the ground, Fertile needs a biosphere. Wind's usable band needs the air
// ceiling over the floor (WeatherAirCeiling). Mineral is present everywhere; whether its ceiling clears
// 70% depends on the noise, so it is reported as "usually".
function airCeiling(atm) {
  if (atm <= TraceAir) return 0;
  if (atm < 1) return 0.92 * Math.pow((atm - TraceAir) / (1 - TraceAir), 0.55);
  return 0.92 + 0.08 * Math.min(1, (atm - 1) / 3);
}
const worlds = [
  { name: 'temperate garden (1 atm, seas, life, plates)', atm: 1, water: true, life: true, geo: true, solar: 0.55 },
  { name: 'dry rocky (0.4 atm, no water, no life, plates)', atm: 0.4, water: false, life: false, geo: true, solar: 0.55 },
  { name: 'airless moon (no air, no plates)', atm: 0, water: false, life: false, geo: false, solar: 0.50 },
  { name: 'ocean world (1.5 atm, life)', atm: 1.5, water: true, life: true, geo: true, solar: 0.50 },
  { name: 'thick hothouse (6 atm, quiet crust)', atm: 6, water: false, life: false, geo: false, solar: 0.75 },
  { name: 'cold outer ice moon (0.2 atm, frozen)', atm: 0.2, water: false, life: false, geo: false, solar: 0.25 },
];
function indexUsable(w, k) {
  switch (k) {
    case 'None': return 'yes';
    case 'Mineral': return 'usually';
    case 'Geothermal': return w.geo ? 'yes' : 'no';
    case 'Fertile': return w.life ? 'yes' : 'no';
    case 'Water': return w.water ? 'yes' : 'no';
    case 'Wind': return airCeiling(w.atm) >= Floor('Wind') ? 'yes' : 'no';
    case 'Solar': return Math.max(0, w.solar - 0.1 * w.atm) + 0.1 >= SolarFloor ? 'yes' : 'no';   // +1 step for high ground
    default: return '?';
  }
}

// ============================================================================================
console.log(`Floors: Mineral ${ShowFloor * 100}%, Solar ${SolarFloor * 100}%, others ${BaseFloor * 100}% · ` +
            `storage ${BaseCapacity} · start ${StartMetal}m/${StartEnergy}e · capitol Labor ${CapitolLabor}\n`);

const rows = [];
for (const b of Object.values(B)) {
  const issues = [];
  let placeable = 'yes';

  if (GROWN.has(b.type)) { rows.push({ b, placeable: 'grown', issues: ['grown by CityGrowth, never placed (correct)'] }); continue; }
  if (SPECIAL[b.type]) placeable = 'special';

  // Tech gate names a real technology.
  if (b.tech && !techs.has(b.tech)) { FAIL(`${b.type}: requiredTech ${b.tech} does not exist`); issues.push(`tech ${b.tech} missing`); placeable = 'NO'; }

  // Shape: the minimum must be drawable in this family.
  if (b.drawMode === 'Fixed' && b.cells !== b.minTiles) { FAIL(`${b.type}: Fixed with ${b.cells} cells but minTiles ${b.minTiles}`); placeable = 'NO'; }
  if (b.drawMode === 'Square' && !Number.isInteger(Math.sqrt(b.minTiles)))
    issues.push(`square min ${b.minTiles} is not a perfect square — the smallest legal box is ${Math.ceil(Math.sqrt(b.minTiles)) ** 2}`);
  if (b.drawMode === 'Rectangle' && b.minTiles < 4) issues.push('rectangle min under 4 is unreachable (2x2 is the smallest)');

  // The minIndex backstop vs the per-cell highlight gate.
  if (b.index !== 'None') {
    const f = Floor(b.index);
    if (b.minIndex > 1) { FAIL(`${b.type}: minIndex ${b.minIndex} can never be met`); placeable = 'NO'; }
    else if (b.minIndex > 0 && b.minIndex < f)
      issues.push(`minIndex ${Math.round(b.minIndex * 100)}% is below the ${b.index} highlight floor ${Math.round(f * 100)}% — the floor is the real rule`);
  }

  // Water rules.
  if (b.requiresWater && !b.water) { FAIL(`${b.type}: requiresWater but not allowsWater — every tile is refused`); placeable = 'NO'; }

  // Cost at the minimum size against the starting stock and the storage ceiling.
  const mult = b.drawMode === 'Fixed' ? 1 : CostMult(b.minTiles);
  const m = Math.round(b.metal * mult), e = Math.round(b.energy * mult);
  if (m > BaseCapacity || e > BaseCapacity) { FAIL(`${b.type}: minimum cost ${m}m/${e}e is over the ${BaseCapacity} base storage`); placeable = 'NO'; }
  if (m > StartMetal || e > StartEnergy) issues.push(`minimum cost ${m}m/${e}e is over the starting stock`);

  // Labor: the stretch a fresh colony (capitol only) puts on the minimum build. A fixed structure holds
  // one structure's worth (SurfaceBuildQueue.EnqueueFixed); a drawn one, its tiles' worth. The two that
  // settle a world (SPECIAL) go up at once and never queue.
  const labor = (b.drawMode === 'Fixed' ? 1 : Math.max(1, b.minTiles)) * laborPerTile(b.type);
  const stretch = 1 + Math.max(0, labor - CapitolLabor) * Penalty;
  if (!SPECIAL[b.type] && stretch > 1.01)
    issues.push(`needs ${labor} Labor; a capitol-only colony (${CapitolLabor}) builds it at ${Math.round(100 / stretch)}% speed`);

  // Geothermal squares across a fault line.
  if (b.type === 'GeothermalPlant')
    issues.push(`2x2 fits across a fault only at activity >= ${squareFitsFrom?.toFixed(2)} (diagonal) / ${squareFitsAxis?.toFixed(2)} (axis-aligned); ` +
                'hotspot plumes (radius >= 2 tiles) always fit');

  // Where it can stand.
  const where = worlds.map(w => indexUsable(w, b.index));
  if (b.index !== 'None' && where.every(x => x === 'no')) { FAIL(`${b.type}: no sample world has usable ${b.index} ground`); placeable = 'NO'; }

  rows.push({ b, placeable, issues, cost: `${m}m/${e}e`, where });
}

const pad = (s, n) => String(s).padEnd(n);
console.log(pad('Structure', 18), pad('Tab', 12), pad('Mode', 10), pad('Min', 4), pad('Index', 11), pad('Tech', 18), pad('Min cost', 11), 'Placeable');
for (const r of rows) {
  const b = r.b;
  console.log(pad(b.type, 18), pad(b.cat, 12), pad(b.drawMode, 10), pad(b.minTiles, 4), pad(b.index, 11),
              pad(b.tech ? `${b.tech} ${techName[b.tech] ?? '?'}` : '-', 18), pad(r.cost ?? '-', 11),
              r.placeable + (b.unique ? ' (unique)' : ''));
  for (const i of r.issues) console.log(`      - ${i}`);
}

console.log('\nWhere each index-driven class can stand (yes / usually / no):');
console.log(pad('', 18), worlds.map((w, i) => `W${i + 1}`).join('  '));
for (const r of rows) if (r.where && r.b.index !== 'None') console.log(pad(r.b.type, 18), r.where.map(x => pad(x, 3)).join(' '));
worlds.forEach((w, i) => console.log(`  W${i + 1} ${w.name}`));

console.log(`\n${fails ? `${fails} FAIL` : 'No blockers'}${warns ? `, ${warns} warnings` : ''}.`);
process.exit(fails ? 1 : 0);
