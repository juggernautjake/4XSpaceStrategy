// ============================================================================================
// ARE THE FORMATIONS ACTUALLY THE SHAPES THEY CLAIM TO BE?
//
//   node tools/formation-check.mjs [--out <png>]
//
// A Node port of FleetFormation.Station, drawn for every formation at every squadron size, because
// nobody has ever LOOKED at them. The icons diagram the intent and the tooltips describe it, but the
// stations themselves have only ever been read as source — and a formation is geometry, which is the
// one kind of thing source review is worst at.
//
// The failures this is looking for are all invisible in code:
//
//   OVERLAP        two ships assigned the same station, or close enough that the separation rule
//                  will spend the whole flight shoving them apart. A formation that fights the
//                  collision avoidance is worse than no formation.
//   COLLAPSE       a shape that works at six and degenerates at three or at twelve — the screen
//                  losing its arc, the globe losing its shell, a rank wrapping wrongly.
//   THE WRONG WAY  a screen whose "screen" is behind the body it is protecting, or an echelon that
//                  stairs to port. Both are one sign flip away at all times.
//
// FleetFormation.PreviewStation feeds the in-game preview from the same Station() this ports, so a
// shape that is right here is the shape the player is shown and the shape the fleet flies.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/formations.png'));

// ---- the constants, read from the game -------------------------------------------------------
const SRC = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Visual/FleetFormation.cs'), 'utf8');
const num = (name) => {
  const m = new RegExp(String.raw`\b${name}\s*=\s*(-?[0-9.]+)f`).exec(SRC);
  if (!m) throw new Error(`could not read ${name} from FleetFormation.cs`);
  return parseFloat(m[1]);
};
const int = (name) => {
  const m = new RegExp(String.raw`\b${name}\s*=\s*(-?[0-9]+)\s*;`).exec(SRC);
  if (!m) throw new Error(`could not read ${name} from FleetFormation.cs`);
  return parseInt(m[1], 10);
};

const LATERAL_STEP = num('LateralStep');
const SWEEP_BACK = num('SweepBack');
const RANK_DEPTH = num('RankDepth');
const RANK_WIDTH = int('RankWidth');

// The formation names, read from the enum so a new one cannot be quietly untested.
const KINDS = (() => {
  const sq = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Systems/Squadrons.cs'), 'utf8');
  const at = sq.indexOf('enum FleetFormationKind');
  const body = sq.slice(sq.indexOf('{', at) + 1, sq.indexOf('}', at));
  return body.replace(/\/\/[^\n]*/g, '').split(',').map(s => s.trim()).filter(s => /^[A-Z]\w*$/.test(s));
})();

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

// ---- FleetFormation.Pair, ported --------------------------------------------------------------
function pair(slot) {
  slot = Math.max(0, slot);
  const rank = Math.floor(slot / RANK_WIDTH);
  const inRank = slot % RANK_WIDTH;
  return { rank, pair: Math.floor((inRank + 1) / 2), side: inRank % 2 === 1 ? -1 : 1 };
}

// ---- FleetFormation.Station, ported -----------------------------------------------------------
function station(kind, slot, count) {
  let lateral = 0, back = 0, lift = 0;
  if (slot <= 0 && kind !== 'Screen' && kind !== 'Globe') return { lateral, back, lift };

  switch (kind) {
    case 'LineAbreast': {
      const p = pair(slot);
      lateral = p.side * p.pair;
      back = p.rank * RANK_DEPTH;
      break;
    }
    case 'LineAstern':
      back = slot * 0.9;
      break;
    case 'Echelon':
      lateral = slot * 0.8;
      back = slot * 0.8;
      break;
    case 'Screen': {
      const screen = clamp(Math.floor(count / 2), 1, 8);
      if (slot < screen) {
        const t = screen === 1 ? 0 : (slot / (screen - 1)) * 2 - 1;
        lateral = t * 2.2;
        back = -1.8 + Math.abs(t) * 1.0;
      } else {
        const p = pair(slot - screen);
        lateral = p.side * p.pair * 0.9;
        back = 1.2 + p.rank * RANK_DEPTH + p.pair * 0.3;
      }
      break;
    }
    case 'Globe': {
      const shell = clamp(Math.floor((count * 2) / 3), 1, 12);
      if (slot < shell) {
        const ang = (slot * Math.PI * 2) / shell;
        lateral = Math.sin(ang) * 2.0;
        back = Math.cos(ang) * 2.0;
        lift = (slot % 2 === 0 ? 0.7 : -0.7) * Math.abs(Math.sin(ang * 0.5));
      } else {
        // A compact grid three abreast, so the interior never reaches the shell around it.
        const inner = slot - shell, row = Math.floor(inner / 3), col = inner % 3;
        lateral = col === 0 ? 0 : (col === 1 ? 1 : -1);
        back = row * 1.0;
      }
      break;
    }
    case 'Free':
      break;
    default: {   // Wedge
      const p = pair(slot);
      lateral = p.side * p.pair;
      back = p.pair * SWEEP_BACK + p.rank * RANK_DEPTH;
      break;
    }
  }
  return { lateral, back, lift };
}

/// The world offset, matching PreviewStation: +Z forward, +X to starboard, +Y lift.
function offset(kind, slot, count, step) {
  if (count <= 1 || kind === 'Free') return { x: 0, y: 0, z: 0 };
  const s = station(kind, slot, count);
  return { x: s.lateral * step, y: s.lift * step, z: -s.back * step };
}

// ============================================================================================
const COUNTS = [3, 6, 11];
const STEP = 0.34 + 950 / 2600;    // a squadron with a dreadnought in it, per FormationPreview

const results = [];
for (const kind of KINDS) {
  for (const count of COUNTS) {
    const pts = [];
    for (let slot = 0; slot < count; slot++) pts.push(offset(kind, slot, count, STEP));
    results.push({ kind, count, pts });
  }
}

// ---- checks ------------------------------------------------------------------------------------
const problems = [];
const notes = [];

// The separation rule pushes hulls apart inside the sum of their clearance radii. The biggest hull is
// 0.52 across and clearance is 0.55 of that, so two stations closer than about 0.57 units will be in
// a permanent shoving match with each other for the whole flight.
const MIN_GAP = 0.57;

for (const r of results) {
  if (r.kind === 'Free') continue;

  let worst = Infinity, worstPair = null;
  for (let i = 0; i < r.pts.length; i++)
    for (let j = i + 1; j < r.pts.length; j++) {
      const a = r.pts[i], b = r.pts[j];
      const d = Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
      if (d < worst) { worst = d; worstPair = [i, j]; }
    }
  r.worst = worst;
  if (worst < MIN_GAP)
    problems.push(`${r.kind} at ${r.count}: slots ${worstPair[0]} and ${worstPair[1]} are ` +
                  `${worst.toFixed(2)}u apart, inside the ${MIN_GAP}u the separation rule will fight over`);
}

// A screen must put its screening ships IN FRONT. `back` is astern, so the screen slots need a
// positive z and the protected body a negative one — one sign flip from being a formation that puts
// the cheap hulls safely behind the expensive ones.
for (const r of results.filter(x => x.kind === 'Screen')) {
  const screen = clamp(Math.floor(r.count / 2), 1, 8);
  const frontZ = Math.min(...r.pts.slice(0, screen).map(p => p.z));
  const bodyZ = Math.max(...r.pts.slice(screen).map(p => p.z));
  if (!(frontZ > bodyZ))
    problems.push(`Screen at ${r.count}: the screen is not in front of what it is screening ` +
                  `(screen z ${frontZ.toFixed(2)}, body z ${bodyZ.toFixed(2)})`);
}

// An echelon must stair consistently to ONE side, and it is documented as starboard.
for (const r of results.filter(x => x.kind === 'Echelon' && x.count > 2)) {
  const xs = r.pts.map(p => p.x);
  const rising = xs.every((v, i) => i === 0 || v > xs[i - 1]);
  if (!rising) problems.push(`Echelon at ${r.count}: the stair does not run consistently to starboard`);
}

// A globe must actually surround something: its shell needs stations on both sides and both ends.
for (const r of results.filter(x => x.kind === 'Globe' && x.count >= 6)) {
  const shell = clamp(Math.floor((r.count * 2) / 3), 1, 12);
  const s = r.pts.slice(0, shell);
  const spans = Math.max(...s.map(p => p.x)) > 0.2 && Math.min(...s.map(p => p.x)) < -0.2 &&
                Math.max(...s.map(p => p.z)) > 0.2 && Math.min(...s.map(p => p.z)) < -0.2;
  if (!spans) problems.push(`Globe at ${r.count}: the shell does not enclose the body`);
}

// Nothing should be so far from the centre that the squadron stops reading as one formation.
for (const r of results) {
  const far = Math.max(0, ...r.pts.map(p => Math.hypot(p.x, p.y, p.z)));
  r.far = far;
  if (far > 14) notes.push(`${r.kind} at ${r.count} spans ${far.toFixed(1)}u from centre`);
}

// ---- render --------------------------------------------------------------------------------------
const CW = 210, CH = 190, COLS = COUNTS.length;
const ROWS = KINDS.length;
const SW = COLS * CW + 90, SH = ROWS * CH + 40;

let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SW}" height="${SH}">
  <rect width="${SW}" height="${SH}" fill="#0b0e13"/>`;

COUNTS.forEach((c, i) => {
  svg += `<text x="${90 + i * CW + CW / 2}" y="22" fill="#c9d5e1" font-family="monospace" ` +
         `font-size="12" text-anchor="middle">${c} ships</text>`;
});

KINDS.forEach((kind, row) => {
  const oy = row * CH + 34;
  svg += `<text x="10" y="${oy + CH / 2}" fill="#c9d5e1" font-family="monospace" font-size="12">${kind}</text>`;

  COUNTS.forEach((count, col) => {
    const r = results.find(x => x.kind === kind && x.count === count);
    const ox = 90 + col * CW;

    svg += `<rect x="${ox + 3}" y="${oy + 3}" width="${CW - 8}" height="${CH - 10}" rx="5"
              fill="#12161c" stroke="#232c37"/>`;

    const far = Math.max(0.8, r.far);
    const s = (Math.min(CW, CH) / 2 - 26) / far;
    const cx = ox + CW / 2, cy = oy + CH / 2;

    // The heading, so "in front" is unambiguous. +Z is forward and the panel draws forward as UP.
    svg += `<line x1="${cx}" y1="${cy + 12}" x2="${cx}" y2="${cy - far * s - 8}"
              stroke="#2c3a48" stroke-width="1" stroke-dasharray="3 3"/>`;

    r.pts.forEach((p, slot) => {
      const x = cx + p.x * s, y = cy - p.z * s;
      // Lift is the third dimension, drawn as size — a globe's shell is above and below as well as
      // around, and a flat plot would show it as a ring with mysterious duplicates.
      const rad = 4 + p.y * s * 0.25;
      const col = slot === 0 ? '#ffd23f' : '#5fb8ff';
      svg += `<circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="${Math.max(2.2, rad).toFixed(1)}"
                fill="none" stroke="${col}" stroke-width="1.8"/>`;
    });

    const bad = r.worst !== undefined && r.worst < MIN_GAP;
    svg += `<text x="${ox + 10}" y="${oy + CH - 12}" fill="${bad ? '#e2664f' : '#7f8fa3'}"
              font-family="monospace" font-size="9">closest ${r.worst === undefined || r.worst === Infinity ? '—' : r.worst.toFixed(2) + 'u'}` +
           `   span ${r.far.toFixed(1)}u</text>`;
  });
});

svg += `<text x="10" y="${SH - 10}" fill="#6b7d8e" font-family="monospace" font-size="10">` +
       `forward is UP. gold = slot 0, which Assign fills with the CHEAPEST hull. circle size carries ` +
       `vertical lift.</text></svg>`;

fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp(Buffer.from(svg)).png().toFile(OUT);

// ---- report ---------------------------------------------------------------------------------------
console.log(`${KINDS.length} formations x ${COUNTS.length} squadron sizes, ` +
            `step ${STEP.toFixed(2)}u (a squadron with a dreadnought in it)\n`);
console.log('formation      3 ships          6 ships          11 ships');
for (const kind of KINDS) {
  let line = '  ' + kind.padEnd(13);
  for (const c of COUNTS) {
    const r = results.find(x => x.kind === kind && x.count === c);
    const gap = r.worst === undefined || r.worst === Infinity ? '  —  ' : r.worst.toFixed(2);
    line += `gap ${gap} span ${r.far.toFixed(1).padStart(4)}  `;
  }
  console.log(line);
}

if (notes.length) { console.log(''); for (const n of notes) console.log(`note  ${n}`); }

console.log('');
if (problems.length === 0) {
  console.log(`Every formation holds its shape at every size. -> ${path.relative(PROJ, OUT)}`);
  process.exit(0);
}
for (const p of problems) console.log(`FAIL  ${p}`);
console.log(`\n${problems.length} problem(s). -> ${path.relative(PROJ, OUT)}`);
process.exit(1);
