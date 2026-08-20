// ============================================================================================
// DO SHIPS GET OUT OF EACH OTHER'S WAY, OR JUST OUT OF EACH OTHER?
//
//   node tools/separation-check.mjs [--out <png>]
//
// A Node port of UnitModelRenderer.Separate, run on the three arrangements that break naive
// avoidance, and drawn as a picture. Same reasoning as tools/ballistics-check.mjs: there is no Unity
// here, and a flocking rule is exactly the sort of thing that reads perfectly sensibly in source and
// produces a fleet vibrating against itself on screen.
//
// ---- THE MODEL BEING TESTED -------------------------------------------------------------------
//
// Every hull has a STATION — where the formation says it should be — and a correction, `sep`, which
// is the only thing this system may change. Stations are planned and do not overlap; the correction
// exists because reality is not the plan. It is carried between frames and eased, it is capped at
// about a hull's width, and it decays back to zero when nothing is near.
//
// Two terms produce it:
//
//   REACTIVE    hulls that are touching now are pushed apart along the line between them.
//   PREDICTIVE  hulls that WILL be touching, at their present velocities, ease apart now — and
//               perpendicular to their closing velocity, which is the only direction that buys any
//               clearance at all.
//
// ---- WHY THE SECOND TERM IS NOT OPTIONAL ------------------------------------------------------
//
// Take two ships closing head-on. The vector between them is parallel to the way they are both
// travelling, so the reactive push is entirely ALONG TRACK: it tells one to speed up and the other to
// slow down and moves neither out of the other's path. They converge, interpenetrate, and are shoved
// apart only once they are already the same pixels. The `head-on pass` panel is that case, and the
// numbers under it are the whole argument.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/separation.png'));

const DT = 1 / 60;

// ---- the constants, read from the game so they cannot drift ----------------------------------
const SRC = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Visual/UnitModelRenderer.cs'), 'utf8');
const constOf = (name, fallback) => {
  const m = new RegExp(`const float [^=]*\\b${name}\\s*=\\s*([0-9.]+)f`).exec(SRC);
  return m ? parseFloat(m[1]) : fallback;
};
const CLEARANCE = constOf('ClearanceRadius', 0.55);
const YIELD = constOf('YieldSpeed', 1.6);
const RETURN = constOf('ReturnSpeed', 0.9);
const MAX_STANDOFF = constOf('MaxStandoff', 1.1);
const LOOKAHEAD = constOf('LookAheadSeconds', 1.1);
const PREDICT_W = constOf('PredictWeight', 0.75);

// ---- vectors, flat in the XZ plane -----------------------------------------------------------
const v = (x = 0, z = 0) => ({ x, z });
const add = (a, b) => v(a.x + b.x, a.z + b.z);
const sub = (a, b) => v(a.x - b.x, a.z - b.z);
const mul = (a, s) => v(a.x * s, a.z * s);
const dot = (a, b) => a.x * b.x + a.z * b.z;
const len = a => Math.hypot(a.x, a.z);
const norm = a => { const l = len(a); return l < 1e-8 ? v(1, 0) : mul(a, 1 / l); };

/// Vector3.MoveTowards.
function moveTowards(from, to, maxStep) {
  const d = sub(to, from), l = len(d);
  return l <= maxStep || l < 1e-8 ? to : add(from, mul(d, maxStep / l));
}

/// The deterministic tie-break for a pair with nothing to work with. Ported from ScatterDirection —
/// what matters is that it is STABLE for the pair, not what angle it picks.
const scatter = (a, b) => {
  const ang = (((a.id * 73856093) ^ (b.id * 19349663)) & 1023) / 1023 * Math.PI * 2;
  return v(Math.cos(ang), Math.sin(ang));
};

// ============================================================================================
// ONE FRAME OF Separate()
// ============================================================================================
function separate(ships, dt, predictive) {
  const pushes = ships.map(() => v());

  for (let i = 0; i < ships.length; i++) {
    for (let j = i + 1; j < ships.length; j++) {
      const a = ships[i], b = ships[j];
      const ri = Math.max(0.02, a.size * CLEARANCE);
      const rj = Math.max(0.02, b.size * CLEARANCE);
      const want = ri + rj;

      const d = sub(add(b.station, b.sep), add(a.station, a.sep));
      const sq = dot(d, d);

      const total = a.value + b.value;
      const shareI = total > 0.001 ? b.value / total : 0.5;

      // ---- reactive ----
      if (sq < want * want) {
        const dir = sq < 1e-8 ? scatter(a, b) : mul(d, 1 / Math.sqrt(sq));
        const overlap = want - Math.sqrt(Math.max(0, sq));
        pushes[i] = sub(pushes[i], mul(dir, overlap * shareI));
        pushes[j] = add(pushes[j], mul(dir, overlap * (1 - shareI)));
      }

      if (!predictive) continue;

      // ---- predictive ----
      const vrel = sub(b.vel, a.vel);
      const vrelSq = dot(vrel, vrel);
      if (vrelSq < 1e-6) continue;

      const tca = -dot(d, vrel) / vrelSq;
      if (tca <= 0 || tca > LOOKAHEAD) continue;

      const atClosest = add(d, mul(vrel, tca));
      const missSq = dot(atClosest, atClosest);
      if (missSq >= want * want) continue;

      const lateral = missSq > 1e-6 ? mul(atClosest, 1 / Math.sqrt(missSq)) : scatter(a, b);
      const strength = (want - Math.sqrt(missSq)) * (1 - tca / LOOKAHEAD) * PREDICT_W;

      pushes[i] = sub(pushes[i], mul(lateral, strength * shareI));
      pushes[j] = add(pushes[j], mul(lateral, strength * (1 - shareI)));
    }
  }

  for (let i = 0; i < ships.length; i++) {
    const s = ships[i];
    let want = pushes[i];
    const cap = Math.max(0.05, s.size * MAX_STANDOFF);
    if (dot(want, want) > cap * cap) want = mul(norm(want), cap);
    // Off station quickly, back to station gently: getting clear is urgent, returning is not.
    const rate = dot(want, want) > dot(s.sep, s.sep) ? YIELD : RETURN;
    s.sep = moveTowards(s.sep, want, rate * dt);
  }
}

// ============================================================================================
// SCENARIOS
//
// Stations move on rails at a constant velocity — that is the FORMATION PLAN, and it is deliberately
// dumb, because the whole question is what the correction does on top of a plan that does not know
// anybody else exists. The drawn position is station + sep.
// ============================================================================================
let nextId = 1;
const ship = (station, vel, size = 0.30, value = 100) =>
  ({ id: nextId++, station, vel, size, value, sep: v(), track: [] });

const SCENARIOS = {
  'head-on pass': () => [
    ship(v(-14, 0), v(9, 0)),
    ship(v(14, 0.02), v(-9, 0)),   // 2cm off dead-centre: a real fleet is never exactly aligned
  ],

  'four-way crossing': () => [
    ship(v(-14, 0), v(9, 0)),
    ship(v(14, 0), v(-9, 0)),
    ship(v(0, -14), v(0, 9)),
    ship(v(0, 14), v(0, -9)),
  ],

  // A wedge of six flying through three parked hulls. This is the case the reactive term was written
  // for and handles well; it is here to prove the predictive term does not BREAK it by scattering a
  // formation that was fine.
  'squadron through a picket': () => {
    const out = [];
    for (let k = 0; k < 6; k++)
      out.push(ship(v(-16 - Math.abs(k - 2.5) * 1.1, (k - 2.5) * 0.9), v(9, 0)));
    for (let k = 0; k < 3; k++)
      out.push(ship(v(2 + k * 0.2, (k - 1) * 0.8), v(0, 0), 0.42, 400));   // parked, and expensive
    return out;
  },
};

function run(make, predictive, seconds = 3.4) {
  nextId = 1;
  const ships = make();
  let worst = Infinity, overlapFrames = 0, frames = 0, maxSep = 0;

  for (let t = 0; t < seconds; t += DT) {
    for (const s of ships) s.station = add(s.station, mul(s.vel, DT));
    separate(ships, DT, predictive);
    for (const s of ships) s.track.push(add(s.station, s.sep));

    frames++;
    let touching = false;
    for (let i = 0; i < ships.length; i++) {
      maxSep = Math.max(maxSep, len(ships[i].sep));
      for (let j = i + 1; j < ships.length; j++) {
        const want = Math.max(0.02, ships[i].size * CLEARANCE) + Math.max(0.02, ships[j].size * CLEARANCE);
        const gap = len(sub(add(ships[j].station, ships[j].sep), add(ships[i].station, ships[i].sep)));
        // Reported as a FRACTION of the clearance the pair wants, so hulls of different sizes are
        // comparable and 1.0 means "exactly touching, never closer".
        worst = Math.min(worst, gap / want);
        if (gap < want) touching = true;
      }
    }
    if (touching) overlapFrames++;
  }
  return { ships, worst, overlapPct: 100 * overlapFrames / frames, maxSep };
}

// ============================================================================================
const results = [];
for (const [name, make] of Object.entries(SCENARIOS))
  results.push({ name, off: run(make, false), on: run(make, true) });

// ---- render -----------------------------------------------------------------------------------
const CW = 380, CH = 300, PAD = 30;
const COLS = 2, ROWS = results.length;
const SW = COLS * CW, SH = ROWS * CH + 26;

function panel(res, title, ox, oy) {
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (const s of res.ships) for (const p of s.track) {
    minX = Math.min(minX, p.x); maxX = Math.max(maxX, p.x);
    minZ = Math.min(minZ, p.z); maxZ = Math.max(maxZ, p.z);
  }
  // Never let a nearly-flat scenario blow the vertical scale up to nothing — a head-on pass moves a
  // few centimetres sideways against thirty units along, and auto-fitting that draws noise.
  const cz = (minZ + maxZ) / 2, spanX = Math.max(1, maxX - minX);
  const spanZ = Math.max(spanX * 0.42, maxZ - minZ);
  const s = Math.min((CW - PAD * 2) / spanX, (CH - PAD * 2 - 26) / spanZ);
  const X = p => ox + PAD + (p.x - minX) * s;
  const Y = p => oy + PAD + 16 + (p.z - (cz - spanZ / 2)) * s;

  let g = `<rect x="${ox + 4}" y="${oy + 4}" width="${CW - 8}" height="${CH - 8}" rx="6"
             fill="#12161c" stroke="#2b3540"/>`;
  g += `<text x="${ox + 15}" y="${oy + 24}" fill="#cfe0ee" font-family="monospace" font-size="12">${title}</text>`;

  res.ships.forEach((sh, k) => {
    const col = sh.value > 200 ? '#7fd4ff' : ['#ffd23f', '#9fe8b0', '#ff9f1c', '#c86bff', '#e2664f', '#6fd8c0'][k % 6];
    g += `<polyline points="${sh.track.map(p => `${X(p).toFixed(1)},${Y(p).toFixed(1)}`).join(' ')}"
            fill="none" stroke="${col}" stroke-width="1.7" opacity="0.95"/>`;
    g += `<circle cx="${X(sh.track[0])}" cy="${Y(sh.track[0])}" r="2.8" fill="${col}"/>`;
  });

  const bad = res.worst < 1;
  g += `<text x="${ox + 15}" y="${oy + CH - 16}" fill="${bad ? '#e2664f' : '#8fa3b5'}"
          font-family="monospace" font-size="11">closest ${res.worst.toFixed(2)} of wanted   ` +
       `touching ${res.overlapPct.toFixed(0)}% of frames</text>`;
  return g;
}

let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SW}" height="${SH}"><rect width="${SW}" height="${SH}" fill="#0b0e13"/>`;
results.forEach((r, i) => {
  svg += panel(r.off, `${r.name} — reactive only`, 0, i * CH);
  svg += panel(r.on, `${r.name} — with look-ahead`, CW, i * CH);
});
svg += `<text x="12" y="${SH - 8}" fill="#6b7d8e" font-family="monospace" font-size="11">`;
svg += `blue = parked and expensive; others = moving. 1.00 of wanted clearance means touching but never closer.</text></svg>`;

fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp(Buffer.from(svg)).png().toFile(OUT);

// ---- report -----------------------------------------------------------------------------------
console.log(`constants read from UnitModelRenderer.cs: clearance ${CLEARANCE}, yield ${YIELD}, ` +
            `return ${RETURN}, standoff ${MAX_STANDOFF}, look-ahead ${LOOKAHEAD}s, weight ${PREDICT_W}\n`);

console.log('scenario                      closest approach (1.00 = just touching)   frames touching');
for (const r of results)
  console.log(`  ${r.name.padEnd(26)} reactive ${r.off.worst.toFixed(2)}  ->  look-ahead ${r.on.worst.toFixed(2)}` +
              `      ${r.off.overlapPct.toFixed(0)}% -> ${r.on.overlapPct.toFixed(0)}%`);

const checks = [];
const ok = (n, p, d) => checks.push({ n, p, d });

const headon = results.find(r => r.name === 'head-on pass');
ok('look-ahead fixes the head-on pass the reactive term cannot',
   headon.on.worst > headon.off.worst * 1.5,
   `closest approach ${headon.off.worst.toFixed(2)} -> ${headon.on.worst.toFixed(2)} of wanted clearance`);

ok('and no arrangement is made worse by it',
   results.every(r => r.on.worst >= r.off.worst - 0.02),
   results.map(r => `${r.name}: ${r.off.worst.toFixed(2)}->${r.on.worst.toFixed(2)}`).join('  '));

ok('nobody is dragged further off station than the cap allows',
   results.every(r => r.on.maxSep <= 0.42 * MAX_STANDOFF + 0.01),
   `largest correction ${Math.max(...results.map(r => r.on.maxSep)).toFixed(3)}u ` +
   `against a cap of ${(0.42 * MAX_STANDOFF).toFixed(3)}u for the biggest hull here`);

const picket = results.find(r => r.name === 'squadron through a picket');
ok('a formation that was already fine is not scattered by the new term',
   Math.abs(picket.on.maxSep - picket.off.maxSep) < 0.25,
   `squadron correction ${picket.off.maxSep.toFixed(3)}u -> ${picket.on.maxSep.toFixed(3)}u`);

console.log('');
let failed = 0;
for (const c of checks) { if (!c.p) failed++; console.log(`${c.p ? 'ok  ' : 'FAIL'} ${c.n}\n     ${c.d}`); }
console.log(`\n${path.relative(PROJ, OUT)}`);
console.log(failed ? `\n${failed} of ${checks.length} checks FAILED.` : `\nAll ${checks.length} checks pass.`);
process.exit(failed ? 1 : 0);
