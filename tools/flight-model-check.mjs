// ============================================================================================
// DOES A REVERSAL LOOK LIKE A REVERSAL?
//
//   node tools/flight-model-check.mjs [--out <png>]
//
// A Node port of ShipPhysics + UnitModelRenderer.Steer, run on the hardest case the model has to get
// right: a ship at full speed told to go back the way it came. The claim being tested is that the arc
// falls out of the rules rather than being scripted — that a hull turns wide because it is fast and
// tightens as it slows, and that a dreadnought's arc is enormous next to a scout's.
//
// There is no Unity in this environment, so the alternative to this is shipping the flight model
// unseen and finding out in play. Ported by hand from the C#; if either side changes, both change.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/flight-model.png'));

// ---- the classes under test: ALL of them, read from the game's own tables ---------------------
//
// This started as eight rows copied out of UnitType.cs by hand, which tests the eight somebody
// thought of and silently stops covering a class the day its stats change. It now parses UnitType.cs
// for the hull stats and UnitModelRenderer.cs for the drawn sizes, so every class in the game is
// exercised and a stat edit shows up here without anyone having to remember to mirror it.
function parseShips() {
  const u = fs.readFileSync(new URL('../Assets/Scripts/Data/UnitType.cs', import.meta.url), 'utf8');
  const r = fs.readFileSync(new URL('../Assets/Scripts/Visual/UnitModelRenderer.cs', import.meta.url), 'utf8');

  const sizes = {};
  for (const m of r.matchAll(/Station\(UnitType\.(\w+),\s*([0-9.]+)f\)/g)) sizes[m[1]] = +m[2];
  for (const m of r.matchAll(/Ship\(UnitType\.(\w+),\s*([0-9.]+)f/g)) sizes[m[1]] = +m[2];
  for (const m of r.matchAll(/map\[UnitType\.(\w+)\] = new Entry \{ path = sciencePath, size = ([0-9.]+)f/g)) sizes[m[1]] = +m[2];
  sizes.ColonyShip = 0.33;

  // Stations are the ones registered through Station() in the size table — that is the single place
  // the distinction is made per class, and it cannot drift from what is actually drawn.
  const rx = /new UnitInfo\(UnitType\.(\w+),\s*"([^"]+)",\s*\r?\n?\s*"[\s\S]*?",\s*\r?\n?\s*([0-9]+),\s*([0-9]+),\s*([0-9.]+)f,\s*([0-9]+),\s*([0-9]+),\s*([0-9]+)/g;
  const stationNames = new Set();
  for (const m of r.matchAll(/Station\(UnitType\.(\w+),/g)) stationNames.add(m[1]);

  const out = [];
  for (const m of u.matchAll(rx)) {
    const type = m[1];
    out.push({
      name: m[2],
      health: +m[7],
      speed: +m[8],
      size: sizes[type] ?? 0.2,
      station: stationNames.has(type),
    });
  }
  return out;
}

const SHIPS = parseShips();

// ---- ShipPhysics ------------------------------------------------------------------------------
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const mass = s => Math.max(1, s.health) / 8 * (s.station ? 1.5 : 1);
const baseTurn = s => clamp(220 * Math.max(1, s.speed) / (10 * Math.sqrt(mass(s))), 7, 190);
const spool = s => clamp(0.8 * Math.sqrt(mass(s)), 0.9, 15);
const accelOf = (s, top) => Math.max(0.01, top / spool(s));
const TURN_PENALTY = 0.11;
const turnAt = (s, v) => baseTurn(s) / (1 + Math.max(0, v) * TURN_PENALTY);
const throttleFor = deg => (deg >= 90 ? 0 : clamp(1 - deg / 90, 0, 1));
const BRAKE = 0.75;

// ---- Steer, in 2D (the manoeuvre is planar) ---------------------------------------------------
const len = v => Math.hypot(v[0], v[1]);
const norm = v => { const l = len(v) || 1; return [v[0] / l, v[1] / l]; };
const angBetween = (a, b) => {
  const d = clamp(a[0] * b[0] + a[1] * b[1], -1, 1);
  return (Math.acos(d) * 180) / Math.PI;
};
function rotateTowards(from, to, maxRad) {
  const a = Math.atan2(from[1], from[0]), b = Math.atan2(to[1], to[0]);
  let d = b - a;
  while (d > Math.PI) d -= 2 * Math.PI;
  while (d < -Math.PI) d += 2 * Math.PI;
  const step = clamp(d, -maxRad, maxRad);
  return [Math.cos(a + step), Math.sin(a + step)];
}

function simulate(s, simSpeed, seconds, dt) {
  const top = Math.max(0.5, simSpeed * 1.6);
  const accel = accelOf(s, top);

  // Start at full speed heading +X, then order it to a marker back down -X.
  let pos = [0, 0];
  let vel = [simSpeed, 0];
  const marker = [-simSpeed * seconds * 0.55, 0];

  const trail = [];
  for (let t = 0; t < seconds; t += dt) {
    trail.push([pos[0], pos[1], len(vel)]);

    const to = [marker[0] - pos[0], marker[1] - pos[1]];
    const dist = len(to);
    let v = len(vel);
    let want = dist > 1e-4 ? norm(to) : [1, 0];
    let heading = v > 1e-4 ? norm(vel) : want;

    // A dead-astern reversal is exactly antiparallel, where a rotation has no preferred side and the
    // ship would sit there. Real hulls break the tie with a control input; this breaks it with a
    // hair of yaw, which is what the C# gets for free from float noise in three dimensions.
    if (angBetween(heading, want) > 179.5) want = rotateTowards(want, [-want[1], want[0]], 0.02);

    heading = rotateTowards(heading, want, (turnAt(s, v) * Math.PI) / 180 * dt);

    const off = angBetween(heading, want);
    const stop = Math.sqrt(2 * Math.max(1e-4, accel * BRAKE) * Math.max(0, dist));
    const target = Math.min(top * throttleFor(off), stop);

    v = target > v ? Math.min(target, v + accel * dt) : Math.max(target, v - accel * BRAKE * dt);
    vel = [heading[0] * v, heading[1] * v];
    pos = [pos[0] + vel[0] * dt, pos[1] + vel[1] * dt];
  }
  return { trail, marker };
}

// ---- render -----------------------------------------------------------------------------------
const W = 1300, ROW = 150, H = ROW * SHIPS.length + 30;
let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">
<rect width="${W}" height="${H}" fill="#12161c"/>`;

console.log('class          mass   turn deg/s   spool s   widest swing   time to reverse');
console.log(''.padEnd(80, '-'));

SHIPS.forEach((s, i) => {
  const simSpeed = 9;
  const { trail, marker } = simulate(s, simSpeed, 60, 0.05);

  const xs = trail.map(p => p[0]).concat([marker[0]]);
  const ys = trail.map(p => p[1]);
  const minX = Math.min(...xs), maxX = Math.max(...xs);
  const maxAbsY = Math.max(0.5, ...ys.map(Math.abs));

  const cy = i * ROW + ROW / 2 + 20;
  const sx = (W - 260) / Math.max(1e-6, maxX - minX);
  const sy = Math.min(sx, (ROW / 2 - 18) / maxAbsY);
  const px = x => 200 + (x - minX) * sx;
  const py = y => cy - y * sy;

  // when it is first pointing back the way it was told to go
  let reversedAt = null;
  for (let k = 1; k < trail.length; k++) {
    if (trail[k][0] < trail[k - 1][0] && trail[k][2] > 0.5) { reversedAt = k * 0.05; break; }
  }

  svg += `<line x1="200" y1="${cy}" x2="${W - 40}" y2="${cy}" stroke="#222a35"/>`;
  svg += `<path d="M ${trail.map(p => `${px(p[0]).toFixed(1)},${py(p[1]).toFixed(1)}`).join(' L ')}"
           fill="none" stroke="#4ec9b0" stroke-width="2"/>`;
  svg += `<circle cx="${px(0)}" cy="${py(0)}" r="5" fill="#ffd166"/>`;
  svg += `<circle cx="${px(marker[0])}" cy="${py(marker[1])}" r="5" fill="#e06c75"/>`;
  svg += `<text x="10" y="${cy - 6}" fill="#c9d5e1" font-family="monospace" font-size="13">${s.name}</text>`;
  svg += `<text x="10" y="${cy + 12}" fill="#7f8fa3" font-family="monospace" font-size="10">`
       + `turn ${baseTurn(s).toFixed(0)}deg/s  spool ${spool(s).toFixed(1)}s</text>`;
  svg += `<text x="10" y="${cy + 26}" fill="#7f8fa3" font-family="monospace" font-size="10">`
       + `swing ${maxAbsY.toFixed(1)}u  reversed ${reversedAt ? reversedAt.toFixed(1) + 's' : '—'}</text>`;

  console.log(
    s.name.padEnd(14) + mass(s).toFixed(0).padStart(5) + baseTurn(s).toFixed(0).padStart(12) +
    spool(s).toFixed(1).padStart(10) + maxAbsY.toFixed(1).padStart(15) + 'u' +
    (reversedAt ? reversedAt.toFixed(1) + 's' : '—').padStart(17));
});

svg += `</svg>`;
await sharp(Buffer.from(svg)).png().toFile(OUT);
console.log(`\n-> ${path.relative(PROJ, OUT)}`);
console.log('Gold = where it was when the order came. Red = where it was told to go.');

// ============================================================================================
// JINKING — does evading actually make a ship harder to hit?
//
// UnitModelRenderer.CombatWeave moves a ship that is in a fight, and Ballistics.DispersionDegrees
// charges a shooter for its target's CROSSING speed. Whether those two numbers meet in the middle is
// not obvious from either file, and it is the whole question: a weave that buys a tenth of a degree
// is decoration, and one that buys twenty is a hull nothing can hit.
//
// Both sets of constants are read from the source rather than typed here, so a tuning change on
// either side shows up in this table.
// ============================================================================================
{
  const rend = fs.readFileSync(new URL('../Assets/Scripts/Visual/UnitModelRenderer.cs', import.meta.url), 'utf8');
  const wep = fs.readFileSync(new URL('../Assets/Scripts/Data/Weaponry.cs', import.meta.url), 'utf8');

  // String.raw, because a template literal turns \b into a backspace and \s into a bare "s" — which
  // is exactly what happened on the first pass. Every lookup silently returned its default, the
  // defaults happened to equal the real values for the two renderer constants, and the two weapon
  // constants defaulted to zero and made the whole table read "0.0 -> 0.0 deg". A parser that fails
  // by returning plausible defaults is worse than one that throws.
  const c = (src, name, d) => {
    const m = new RegExp(String.raw`\b${name}\s*=\s*(-?[0-9.]+)f`).exec(src);
    if (!m) throw new Error(`could not read ${name} from the source — the parser is out of date`);
    return parseFloat(m[1]);
  };
  const MAX_R = c(rend, 'MaxWeaveRadius');
  const SLOW = c(rend, 'WeaveSlow');
  const FAST = c(rend, 'WeaveFast');

  // The two unguided mounts a jinking ship most wants to spoil, with their own spread terms.
  const mount = (name) => {
    const blk = new RegExp(String.raw`WeaponClass\.${name}[\s\S]*?\n    \};`).exec(wep);
    if (!blk) throw new Error(`could not find the ${name} mount in Weaponry.cs`);
    const body = blk[0];
    return {
      name,
      base: c(body, 'spreadDeg'),
      cross: c(body, 'spreadCrossFactor'),
    };
  };
  const PULSE = mount('PulseLaser'), PLASMA = mount('PlasmaCannon');

  console.log('\n\nJINKING — evasive flight for a ship in a fight');
  console.log('class            agility   weave u   peak u/s   pulse spread      plasma spread');
  console.log(''.padEnd(88, '-'));

  const rows = [];
  for (const s of SHIPS) {
    const turn = baseTurn(s);
    const agility = clamp((turn - 7) / (60 - 7), 0, 1);
    const radius = agility * MAX_R;
    const omega = SLOW + (FAST - SLOW) * agility;
    // The Lissajous runs at 2x and 3x the base rate; the faster axis sets the peak speed.
    const peak = radius * omega * 3;

    const p0 = PULSE.base, p1 = PULSE.base + peak * PULSE.cross;
    const q0 = PLASMA.base, q1 = PLASMA.base + peak * PLASMA.cross;
    rows.push({ s, agility, radius, peak, p0, p1, q0, q1 });
  }

  // Only the hulls worth reading — the extremes and a couple in between, or this is 29 lines nobody
  // scans. Sorted by agility so the spread across the roster is the shape of the table.
  rows.sort((a, b) => b.agility - a.agility);
  const show = [rows[0], rows[1], rows[Math.floor(rows.length / 2)], rows[rows.length - 2], rows[rows.length - 1]];
  for (const r of show)
    console.log(
      r.s.name.padEnd(16) + r.agility.toFixed(2).padStart(7) + r.radius.toFixed(2).padStart(10) +
      r.peak.toFixed(2).padStart(11) + `   ${r.p0.toFixed(1)} -> ${r.p1.toFixed(1)} deg`.padEnd(20) +
      `   ${r.q0.toFixed(1)} -> ${r.q1.toFixed(1)} deg`);

  const checks = [];
  const ok = (n, p, d) => checks.push({ n, p, d });

  const nimble = rows[0], heavy = rows[rows.length - 1];

  ok('the nimblest hull meaningfully spoils an unguided shot',
     nimble.p1 > nimble.p0 * 1.8,
     `${nimble.s.name}: pulse spread ${nimble.p0.toFixed(1)} -> ${nimble.p1.toFixed(1)} deg ` +
     `(${(nimble.p1 / nimble.p0).toFixed(2)}x)`);

  ok('and does not become untouchable doing it',
     nimble.p1 < 8 && nimble.q1 < 12,
     `worst case ${Math.max(nimble.p1, nimble.q1).toFixed(1)} deg of spread`);

  ok('the heaviest hulls barely move, so tonnage is still a liability',
     heavy.peak < 0.35,
     `${heavy.s.name} peaks at ${heavy.peak.toFixed(2)} u/s against ${nimble.s.name}'s ${nimble.peak.toFixed(2)}`);

  ok('evading is worth less than running',
     nimble.peak < 8,
     `weave peaks at ${nimble.peak.toFixed(2)} u/s; a hull in transit makes 10-16`);

  ok('nobody strays far enough from their station to be hard to click',
     rows.every(r => r.radius <= 0.9),
     `widest weave is ${Math.max(...rows.map(r => r.radius)).toFixed(2)} world units`);

  console.log('');
  let failed = 0;
  for (const k of checks) { if (!k.p) failed++; console.log(`${k.p ? 'ok  ' : 'FAIL'} ${k.n}\n     ${k.d}`); }
  console.log(failed ? `\n${failed} of ${checks.length} jinking checks FAILED.`
                     : `\nAll ${checks.length} jinking checks pass.`);
  if (failed) process.exitCode = 1;
}
