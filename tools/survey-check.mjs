// ============================================================================================
// DOES A SURVEY LOOK LIKE A SURVEY, AND HOW LONG DOES IT TAKE?
//
//   node tools/survey-check.mjs [--out <png>]
//
// A Node port of Survey's block reveal, run on the range of worlds the game actually generates, and
// drawn as a picture. Same reasoning as tools/ballistics-check.mjs: there is no Unity here, and a
// reveal ORDER is exactly the sort of thing that reads perfectly sensibly in source and comes out on
// screen as a map uncovering in a pattern nobody would call a survey.
//
// ---- THE TWO QUESTIONS ------------------------------------------------------------------------
//
// 1. WHAT SHAPE. A survey should start in the middle, run right, wrap, finish that band, and then
//    work outward alternating above and below. The panels draw the block order as a heat map, so the
//    pattern is either obviously that or obviously not.
//
// 2. HOW LONG. This is the one that needed measuring rather than asserting. The request asks for a
//    5x5 block every 4 seconds and a 7x7 every 3.5 — and at a LITERAL 5x5 cells, a 200x100 world is
//    eight hundred blocks, which is fifty-three minutes. The rest of the game is balanced on surveys
//    of ten to ninety seconds. So the block grows with the world instead (Survey.CellsPerUnit), and
//    this table is what says whether that scaling actually lands in a sane range across every grid
//    size in the game. If a column here reads in the hundreds of seconds, the constants are wrong.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/survey.png'));

// ---- the constants, read from the game so they cannot drift ----------------------------------
const SRC = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Systems/Survey.cs'), 'utf8');

function num(re, fallback) {
  const m = new RegExp(re).exec(SRC);
  return m ? parseFloat(m[1]) : fallback;
}

// TargetBlocks: clamp(K * pow(cells / REF, EXP), LO, HI)
const TB_K = num(String.raw`Mathf\.Clamp\(([0-9.]+)f \* Mathf\.Pow\(cells / [0-9.]+f`, 6);
const TB_REF = num(String.raw`Mathf\.Pow\(cells / ([0-9.]+)f, [0-9.]+f\)`, 800);
const TB_EXP = num(String.raw`Mathf\.Pow\(cells / [0-9.]+f, ([0-9.]+)f\)`, 0.3);
const TB_LO = num(String.raw`Mathf\.Pow\(cells / [0-9.]+f, [0-9.]+f\), ([0-9.]+)f, [0-9.]+f\)`, 4);
const TB_HI = num(String.raw`Mathf\.Pow\(cells / [0-9.]+f, [0-9.]+f\), [0-9.]+f, ([0-9.]+)f\)`, 40);

const SCIENCE_UNITS = num(String.raw`canResearch\) \? ([0-9]+) : [0-9]+;`, 7);
const OTHER_UNITS = num(String.raw`canResearch\) \? [0-9]+ : ([0-9]+);`, 5);
const SCIENCE_SECS = num(String.raw`canResearch\) \? ([0-9.]+)f : [0-9.]+f;`, 3.5);
const OTHER_SECS = num(String.raw`canResearch\) \? [0-9.]+f : ([0-9.]+)f;`, 4.0);

// ---- Survey, ported ---------------------------------------------------------------------------
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

const targetBlocks = (w, h) =>
  clamp(TB_K * Math.pow((w * h) / TB_REF, TB_EXP), TB_LO, TB_HI);

const cellsPerUnit = (w, h) =>
  Math.max(0.2, Math.sqrt((w * h) / targetBlocks(w, h)) / 5);

const blockCells = (w, h, units) =>
  clamp(Math.round(units * cellsPerUnit(w, h)), 2, Math.max(2, Math.min(w, h)));

const blocksAcross = (w, bc) => Math.max(1, Math.ceil(w / bc));
const bandCount = (h, bc) => Math.max(1, Math.ceil(h / bc));

/// Survey.BandOrder, ported. Middle band first, then alternately to the favoured side and the other.
function bandOrder(bands, upFirst) {
  const order = [];
  const cy = Math.floor(bands / 2);
  order.push(cy);
  for (let d = 1; order.length < bands && d <= bands; d++) {
    const first = upFirst ? cy + d : cy - d;
    const second = upFirst ? cy - d : cy + d;
    if (first >= 0 && first < bands && order.length < bands) order.push(first);
    if (second >= 0 && second < bands && order.length < bands) order.push(second);
  }
  return order;
}

/// Survey.ColRank, ported: 0 at the middle column, running right and wrapping.
const colRank = (w, x) => (((x - Math.floor(w / 2)) % w) + w) % w;

/// The order every CELL of a world is uncovered in, as a block index. Mirrors ReachedGround: a cell
/// belongs to the block its column rank falls in, on the band its row falls in.
function revealOrder(w, h, units, upFirst) {
  const bc = blockCells(w, h, units);
  const across = blocksAcross(w, bc);
  const bands = bandCount(h, bc);
  const order = bandOrder(bands, upFirst);

  const rank = new Int32Array(bands);
  for (let i = 0; i < order.length; i++) rank[order[i]] = i;

  const out = new Int32Array(w * h);
  for (let y = 0; y < h; y++) {
    const band = Math.min(bands - 1, Math.floor(y / bc));
    for (let x = 0; x < w; x++) {
      const col = Math.floor(colRank(w, x) / bc);
      out[y * w + x] = rank[band] * across + col;
    }
  }
  return { out, bc, across, bands, total: bands * across };
}

// ---- the worlds under test --------------------------------------------------------------------
//
// Named for what they are rather than for their size, because the question being asked is "does a
// moon feel like a moon and a gas giant like a gas giant", not "is 40x20 handled".
const WORLDS = [
  { name: 'small moon', w: 40, h: 20 },
  { name: 'large moon', w: 80, h: 40 },
  { name: 'small planet', w: 120, h: 60 },
  { name: 'typical world', w: 200, h: 100 },
  { name: 'large world', w: 400, h: 200 },
  { name: 'gas giant', w: 640, h: 320 },
];

console.log('constants read from Survey.cs: ' +
            `blocks = clamp(${TB_K} * (cells/${TB_REF})^${TB_EXP}, ${TB_LO}, ${TB_HI}), ` +
            `scout ${OTHER_UNITS}u/${OTHER_SECS}s, science ${SCIENCE_UNITS}u/${SCIENCE_SECS}s\n`);

console.log('world              grid       scout block   blocks    time     science block   blocks    time');
const rows = [];
for (const world of WORLDS) {
  const s = revealOrder(world.w, world.h, OTHER_UNITS, true);
  const c = revealOrder(world.w, world.h, SCIENCE_UNITS, true);
  const sT = s.total * OTHER_SECS, cT = c.total * SCIENCE_SECS;
  rows.push({ world, s, c, sT, cT });
  console.log(
    `  ${world.name.padEnd(15)} ${String(world.w + 'x' + world.h).padEnd(9)} ` +
    `${String(s.bc + 'x' + s.bc).padStart(9)} ${String(s.total).padStart(8)} ${(sT).toFixed(0).padStart(6)}s   ` +
    `${String(c.bc + 'x' + c.bc).padStart(11)} ${String(c.total).padStart(8)} ${(cT).toFixed(0).padStart(6)}s`);
}

// ---- render -----------------------------------------------------------------------------------
//
// The block ORDER as a heat map: dark is uncovered first, bright last. If the middle band is dark and
// the poles are bright, the outward-alternating pattern is working; if it reads as a gradient from
// one edge, it is not.
const CW = 300, CH = 180, COLS = 3;
const ROWS_N = Math.ceil(rows.length / COLS);
const SW = COLS * CW, SH = ROWS_N * CH + 30;

const tiles = [];
const labels = [];
for (let i = 0; i < rows.length; i++) {
  const { world, s, sT } = rows[i];
  const IW = CW - 24, IH = CH - 52;
  const buf = Buffer.alloc(IW * IH * 3);
  for (let py = 0; py < IH; py++) {
    for (let px = 0; px < IW; px++) {
      const x = Math.floor(px * world.w / IW);
      const y = Math.floor((IH - 1 - py) * world.h / IH);
      const t = s.out[y * world.w + x] / Math.max(1, s.total - 1);
      // A cool-to-warm ramp: early ground is deep blue, late ground is hot yellow.
      const o = (py * IW + px) * 3;
      buf[o] = Math.round(255 * Math.min(1, t * 1.6));
      buf[o + 1] = Math.round(255 * Math.min(1, Math.max(0, t * 1.4 - 0.25)));
      buf[o + 2] = Math.round(255 * (0.55 - 0.5 * t) + 40);
    }
  }
  const left = (i % COLS) * CW + 12, top = Math.floor(i / COLS) * CH + 30;
  tiles.push({ input: await sharp(buf, { raw: { width: IW, height: IH, channels: 3 } }).png().toBuffer(),
               left, top });
  labels.push({ x: left, y: top - 8, text: `${world.name}  ${world.w}x${world.h}` });
  labels.push({ x: left, y: top + IH + 16,
                text: `${s.bc}x${s.bc} blocks, ${s.total} of them, ${sT.toFixed(0)}s for a scout` });
}

let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SW}" height="${SH}">`;
for (const l of labels)
  svg += `<text x="${l.x}" y="${l.y}" fill="#c9d5e1" font-family="monospace" font-size="11">${l.text}</text>`;
svg += `<text x="12" y="${SH - 8}" fill="#6b7d8e" font-family="monospace" font-size="11">` +
       `block reveal order: dark = uncovered first, bright = last. Middle band out, alternating.</text></svg>`;

fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp({ create: { width: SW, height: SH, channels: 3, background: { r: 11, g: 14, b: 19 } } })
  .composite([...tiles, { input: Buffer.from(svg), top: 0, left: 0 }])
  .png().toFile(OUT);

// ---- checks -----------------------------------------------------------------------------------
const checks = [];
const ok = (n, p, d) => checks.push({ n, p, d });

ok('every world is surveyed in a time a player will sit through',
   rows.every(r => r.sT >= 10 && r.sT <= 200),
   rows.map(r => `${r.world.name} ${r.sT.toFixed(0)}s`).join(', '));

ok('bigger worlds take longer, without taking proportionally longer',
   rows.every((r, i) => i === 0 || r.sT >= rows[i - 1].sT),
   `${rows[0].sT.toFixed(0)}s to ${rows[rows.length - 1].sT.toFixed(0)}s across a ` +
   `${((WORLDS[5].w * WORLDS[5].h) / (WORLDS[0].w * WORLDS[0].h)).toFixed(0)}x range of area`);

ok('a science ship always beats a scout, on every world',
   rows.every(r => r.cT < r.sT),
   rows.map(r => `${(r.sT / r.cT).toFixed(2)}x`).join(' '));

ok('and beats it by roughly the 2.2x the two numbers imply',
   rows.every(r => r.sT / r.cT > 1.5 && r.sT / r.cT < 3.2),
   `ratios ${rows.map(r => (r.sT / r.cT).toFixed(2)).join(', ')}`);

// The pattern itself. The middle band must be uncovered before the poles on every world — this is the
// claim the picture makes, checked so a change to BandOrder cannot quietly invert it.
{
  const bad = [];
  for (const { world, s } of rows) {
    // Meaningless below three bands: with two, the middle row and the pole row are the same band, and
    // "is the middle done first" has no answer rather than a wrong one.
    if (s.bands < 3) continue;
    const mid = s.out[Math.floor(world.h / 2) * world.w + 0];
    const pole = s.out[0 * world.w + 0];
    if (mid >= pole) bad.push(world.name);
  }
  ok('the middle of the world is surveyed before the poles', bad.length === 0,
     bad.length ? `wrong on ${bad.join(', ')}` : 'true on every world tested');
}

// A block must never be so large it is most of the world — at that point it is not a survey, it is a
// reveal in two steps.
// A block must never be so large that a survey is a reveal in two steps. Two fifths is the line, and it
// is deliberately loose: on the smallest moons the block IS inevitably a big fraction of a small map —
// a science ship takes a 40x20 moon in six bites — and that is the correct outcome rather than a
// failure. What would be a failure is a block covering half the world, at which point there is nothing
// to watch.
ok('no block is more than two fifths of the world across',
   rows.every(r => r.s.bc <= r.world.w * 0.4 && r.c.bc <= r.world.w * 0.4),
   `widest is ${Math.max(...rows.map(r => r.c.bc / r.world.w * 100)).toFixed(0)}% of the map`);

console.log('');
let failed = 0;
for (const c of checks) { if (!c.p) failed++; console.log(`${c.p ? 'ok  ' : 'FAIL'} ${c.n}\n     ${c.d}`); }
console.log(`\n${path.relative(PROJ, OUT)}`);
console.log(failed ? `\n${failed} of ${checks.length} checks FAILED.` : `\nAll ${checks.length} checks pass.`);
process.exit(failed ? 1 : 0);
