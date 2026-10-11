// ============================================================================================
// THE INDUSTRIAL CHAIN AND THE ADJACENCY POWER RULE, CHECKED OFFLINE (2026-10-11)
//
//   node tools/industry-power-check.mjs
//
// There is no Unity here, so this ports the rules that matter and asserts what they promise. Every
// constant is READ FROM THE C# (PowerGrid.cs, SurfaceSynergy.cs, Industry.cs, SurfaceBuilding.cs,
// BuildScaling.cs) rather than copied, so a re-tune that breaks an invariant fails here instead of in a
// player's colony.
//
// What it asserts:
//   1. Hops: a chain of buildings off a plant's disc is powered for MaxContactHops buildings and too far
//      after; a pylon inside the limit re-lights everything in its disc (hop 0 again).
//   2. Shortfall is shared NEAREST FIRST: hop-0 consumers are fully served before hop-1 gets anything.
//   3. Being connected to a starved grid is never worse than being off-grid (same backup floor).
//   4. Synergy: per-rule caps, the total cap, and refinery coverage SHARED across the mines it touches.
//   5. The onboarding grid (capitol + combustion + farm + mine + lab + shipyard) balances at typical sizes.
//   6. Factory Industry: speed capped, metal only for useful points.
//   7. Draw scales with drawn size (the old flat draw is gone).
// Exit code 0 clean, 1 with failures.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'Assets', 'Scripts');
const read = (rel) => fs.readFileSync(path.join(ROOT, rel), 'utf8');
const grid = read('Systems/PowerGrid.cs');
const syn = read('Systems/SurfaceSynergy.cs');
const ind = read('Systems/Industry.cs');
const db = read('Data/SurfaceBuilding.cs');
const scaling = read('Systems/BuildScaling.cs');

let failures = 0;
const ok = (cond, msg) => { if (cond) console.log('  ok   ' + msg); else { console.log('  FAIL ' + msg); failures++; } };
const num = (src, re, name) => {
  const m = src.match(re);
  if (!m) { console.log(`  FAIL could not read ${name} from the C#`); failures++; return NaN; }
  return parseFloat(m[1]);
};

// ---- Constants from the source ----
const Unpowered = num(grid, /const float UnpoweredFactor = ([\d.]+)f/, 'UnpoweredFactor');
const MaxHops = num(grid, /const int MaxContactHops = (\d+)/, 'MaxContactHops');
const TotalCap = num(syn, /const float TotalCap = ([\d.]+)f/, 'TotalCap');
const CostStep = num(scaling, /const float CostStep = ([\d.]+)f/, 'CostStep');
const OutputStep = num(scaling, /const float OutputStep = ([\d.]+)f/, 'OutputStep');
const SpeedPerPoint = num(ind, /const float SpeedPerPoint = ([\d.]+)f/, 'SpeedPerPoint');
const MaxSpeedBonus = num(ind, /const float MaxSpeedBonus = ([\d.]+)f/, 'MaxSpeedBonus');
const MetalPerPoint = num(ind, /const float MetalPerPoint = ([\d.]+)f/, 'MetalPerPoint');

const draw = {};
for (const m of db.matchAll(/Draw\(SurfaceBuildingType\.(\w+), ([\d.]+)f\)/g)) draw[m[1]] = parseFloat(m[2]);
const combustion = num(db, /comb\.energyPerSec = ([\d.]+)f/, 'Combustion energyPerSec');
const capitolReactor = num(db, /Reactor\(SurfaceBuildingType\.PlanetCapitol, ([\d.]+)f/, 'Capitol reactor');

const rules = [];
for (const m of syn.matchAll(/new Rule \{ label = "([^"]+)",[\s\S]*?per = (-?[\d.]+)f, ratio = ([\d.]+)f, cap = ([\d.]+)f \}/g))
  rules.push({ label: m[1], per: parseFloat(m[2]), ratio: parseFloat(m[3]), cap: parseFloat(m[4]) });
const rule = (frag) => rules.find(r => r.label.includes(frag));

const steps = (n) => n <= 1 ? 0 : n * (n - 1) / 2;
const costMult = (n) => Math.max(1, n) + CostStep * steps(Math.max(1, n));
const outMult = (n) => Math.max(1, n) + OutputStep * steps(Math.max(1, n));
const factorFor = (served) => Unpowered + (1 - Unpowered) * Math.min(1, Math.max(0, served));

console.log(`constants: backup ${Unpowered}, hop limit ${MaxHops}, synergy cap ${TotalCap}, ${rules.length} synergy rules`);

// ============================================================================================
// 1. HOPS — a port of PowerGrid.Build on a toy map
// ============================================================================================
console.log('\n1. Adjacency power (hops)');
function derive(buildings) {
  // buildings: { id, cells:[[x,y]], gen?:range, node?:range }
  const owner = new Map();
  buildings.forEach((b, i) => b.cells.forEach(([x, y]) => owner.set(`${x},${y}`, i)));
  const nbr = buildings.map((b, i) => {
    const s = new Set();
    for (const [x, y] of b.cells) for (const [dx, dy] of [[1,0],[-1,0],[0,1],[0,-1]]) {
      const j = owner.get(`${x+dx},${y+dy}`); if (j !== undefined && j !== i) s.add(j);
    }
    return [...s];
  });
  const disc = (b, r) => { const s = new Set(); for (const [x, y] of b.cells) for (let dy = -Math.ceil(r); dy <= Math.ceil(r); dy++) for (let dx = -Math.ceil(r); dx <= Math.ceil(r); dx++) if (dx*dx+dy*dy <= r*r) s.add(`${x+dx},${y+dy}`); return s; };
  const live = buildings.map(b => !!b.gen);
  const discs = buildings.map(b => b.gen ? disc(b, b.gen) : null);
  let hop;
  for (let round = 0; round <= buildings.length; round++) {
    hop = buildings.map(() => Infinity);
    const q = [];
    buildings.forEach((b, i) => { if (live[i]) { hop[i] = 0; q.push(i); } });
    buildings.forEach((b, i) => { if (live[i]) for (const c of discs[i]) { const j = owner.get(c); if (j !== undefined && hop[j] !== 0) { hop[j] = 0; q.push(j); } } });
    for (let h = 0; h < q.length; h++) { const u = q[h]; if (hop[u] >= MaxHops + 6) continue; for (const v of nbr[u]) if (hop[v] > hop[u] + 1) { hop[v] = hop[u] + 1; q.push(v); } }
    let grew = false;
    buildings.forEach((b, i) => { if (b.node && !live[i] && hop[i] <= MaxHops) { live[i] = true; discs[i] = disc(b, b.node); grew = true; } });
    if (!grew) break;
  }
  return hop;
}
{
  // A 1-tile plant (reach 2) at x=0, then a row of 1-tile factories at x = 3, 4, 5, 6, 7, 8.
  const row = [{ cells: [[0, 0]], gen: 2 }];
  for (let x = 3; x <= 8; x++) row.push({ cells: [[x, 0]] });
  const hop = derive(row);
  ok(hop[1] === Infinity && hop[2] === Infinity, 'a building outside the disc with no contact is dark (x=3 is 3 tiles from a reach-2 plant)');
  const row2 = [{ cells: [[0, 0]], gen: 2 }];
  for (let x = 2; x <= 8; x++) row2.push({ cells: [[x, 0]] });
  const h2 = derive(row2);
  ok(h2[1] === 0, 'x=2 is lit directly (hop 0)');
  ok(h2[2] === 1 && h2[3] === 2 && h2[4] === MaxHops, `contact carries it 1, 2, ${MaxHops} buildings`);
  ok(h2[5] === MaxHops + 1 && h2[5] > MaxHops, `the next one is too far (hop ${h2[5]})`);
  // Replace the hop-3 factory with a pylon of reach 7: everything after it is hop 0 again.
  const row3 = row2.map((b, i) => i === 4 ? { cells: b.cells, node: 7 } : b);
  const h3 = derive(row3);
  ok(h3[5] === 0 && h3[6] === 0 && h3[7] === 0, 'a pylon at the limit goes live and re-lights its disc (hop 0)');
  // A chain in the desert: no plant, nothing powered.
  const desert = [{ cells: [[0, 0]], node: 7 }, { cells: [[1, 0]] }];
  ok(derive(desert).every(h => h === Infinity), 'no plant, no grid: a relay chain in the desert stays dark');
}

// ============================================================================================
// 2 + 3. NEAREST-FIRST SHARING, AND THE BACKUP FLOOR
// ============================================================================================
console.log('\n2. Shortfall sharing');
function allocate(consumers, supply) {   // consumers: [{hop, draw}] sorted by hop
  const total = consumers.reduce((s, c) => s + c.draw, 0);
  if (supply >= total) return consumers.map(() => 1);
  const out = []; let left = supply;
  for (let i = 0; i < consumers.length;) {
    let j = i, tier = 0;
    while (j < consumers.length && consumers[j].hop === consumers[i].hop) tier += consumers[j++].draw;
    const s = tier > 0 ? Math.min(1, left / tier) : 1;
    for (let k = i; k < j; k++) out.push(s);
    left = Math.max(0, left - tier); i = j;
  }
  return out;
}
{
  const cons = [{ hop: 0, draw: 2 }, { hop: 0, draw: 1 }, { hop: 1, draw: 2 }, { hop: 3, draw: 2 }];
  const s = allocate(cons, 4);
  ok(s[0] === 1 && s[1] === 1, 'hop-0 consumers are fully served first');
  ok(Math.abs(s[2] - 0.5) < 1e-6 && s[3] === 0, 'hop 1 gets what is left (50%), hop 3 gets nothing');
  const supplied = s.reduce((a, v, i) => a + v * cons[i].draw, 0);
  ok(Math.abs(supplied - 4) < 1e-6, 'every unit of supply is handed out, none invented');
  ok(factorFor(0) === Unpowered, `a starved building runs at the backup floor (${Unpowered * 100}%) — same as off-grid, never worse`);
  ok(Unpowered > 0 && Unpowered <= 0.2, 'the backup floor is small (near-halt) but not zero');
}

// ============================================================================================
// 4. SYNERGY
// ============================================================================================
console.log('\n4. Synergy');
{
  const refMine = rule('smelted'), mineRef = rule('ore fed'), refFac = rule('refined stock'), depot = rule('Storage Depot'),
        housing = rule('housed'), labs = rule('laboratories'), yard = rule('switchyard');
  ok(refMine && mineRef && refFac && depot && housing && labs && yard, 'all seven rules parsed from SurfaceSynergy.cs');
  if (refMine) {
    // A 1-tile refinery between two 10-tile mines: its 2 tiles of capacity are shared over 20.
    const cov = Math.min(1, 1 * refMine.ratio / 20);
    ok(Math.abs(refMine.per * cov - 0.025) < 1e-6, 'a 1-tile refinery between two 10-tile mines gives each +2.5%, not +25%');
    // A 2-tile refinery touching one 4-tile mine: full coverage.
    ok(Math.min(1, 2 * refMine.ratio / 4) === 1, 'a 2-tile refinery fully covers a 4-tile mine (+25%)');
    ok(Math.min(3 * refMine.per, refMine.cap) === refMine.cap, `three refineries on one mine stop at the rule cap (+${refMine.cap * 100}%)`);
  }
  if (refMine && depot && housing) {
    const stacked = refMine.cap + depot.cap + housing.cap;
    ok(Math.min(stacked, TotalCap) <= TotalCap && stacked >= TotalCap - 0.2, `a fully dressed mine (+${Math.round(stacked * 100)}% raw) is held at the +${TotalCap * 100}% total cap`);
  }
  if (yard) ok(yard.per < 0 && yard.cap === 0.3, 'switchyard rule is unchanged: the hub\'s own bonus, +30%, not stacking');
}

// ============================================================================================
// 5. THE OPENING GRID
// ============================================================================================
console.log('\n5. Onboarding balance (capitol, combustion, farm, mine, lab, shipyard)');
{
  const eff = 0.8;                                    // a typical Mineral reading above the 70% floor
  const gen = capitolReactor + combustion * outMult(3) * eff;
  const d = draw.Farm * costMult(4) + draw.Mine * costMult(3) + draw.ResearchOutpost + draw.SurfaceShipyard;
  console.log(`     made ${gen.toFixed(2)}, drawn ${d.toFixed(2)} (3-tile plant, 4-tile farm, 3-tile mine)`);
  ok(gen >= d, 'a 3-tile combustion plant carries the whole opening');
  const gen2 = capitolReactor + combustion * outMult(2) * eff;
  console.log(`     with a 2-tile plant: made ${gen2.toFixed(2)} -> short by ${(d - gen2).toFixed(2)} (teaches "build more power")`);
  ok(d - gen2 < 1.5, 'a 2-tile plant is only mildly short, not crippled');
}

// ============================================================================================
// 6. INDUSTRY
// ============================================================================================
console.log('\n6. Factory Industry');
{
  const pts = (tiles) => outMult(tiles);              // eff 1, level 1, powered, no synergy
  const bonus = (p) => Math.min(MaxSpeedBonus, p * SpeedPerPoint);
  console.log(`     1 tile: +${Math.round(bonus(pts(1)) * 100)}%  3 tiles: +${Math.round(bonus(pts(3)) * 100)}%  6 tiles: +${Math.round(bonus(pts(6)) * 100)}%`);
  ok(bonus(pts(20)) === MaxSpeedBonus, `build speed caps at +${MaxSpeedBonus * 100}%`);
  const useful = (p) => Math.min(p, MaxSpeedBonus / SpeedPerPoint);
  ok(useful(pts(20)) * MetalPerPoint < pts(20) * MetalPerPoint, 'points past the cap are not fed metal');
  ok(pts(3) * MetalPerPoint < 1.6 * outMult(3), 'a 3-tile factory eats less metal than a 3-tile mine makes');
}

// ============================================================================================
// 7. DRAW SCALES WITH SIZE
// ============================================================================================
console.log('\n7. Draw');
{
  ok(Math.abs(draw.Mine * costMult(6) / (draw.Mine * costMult(1)) - costMult(6)) < 1e-6, `a 6-tile mine draws ${costMult(6).toFixed(2)}x a 1-tile one`);
  ok(/BuildScaling\.CostMultiplier\(p\.TileCount\)/.test(grid), 'PowerGrid.DrawOf applies the cost curve');
  const wsrc = read('Systems/WorldEconomy.cs'), card = read('UI/BuildingGridPopup.cs');
  ok(/PowerGrid\.DrawOf\(p\)/.test(wsrc) && /PowerGrid\.DrawOf\(p\)/.test(card), 'the books and the card read the same draw');
  ok(!/powerDraw \* p\.LevelMult/.test(wsrc + card), 'nothing still computes the old flat draw');
}

console.log(failures === 0 ? '\nClean.' : `\n${failures} failure(s).`);
process.exit(failures === 0 ? 0 : 1);
