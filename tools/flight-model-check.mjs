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
const OUT = path.resolve(PROJ, arg('--out', 'Art/AllModels/_flight-model.png'));

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
