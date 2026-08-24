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
//    fixed 5x5 block every 4 seconds and a fixed 7x7 every 3.5 — and at a LITERAL 7x7, a 200x100 world
//    is 435 blocks, which is twenty-five minutes, and a 640x320 gas giant is four HOURS. The block size
//    is fixed anyway, because that is what was asked for; what floats instead is the DWELL (shortened
//    on big worlds, floored so the marker cannot strobe) and then the number of blocks the ship works
//    at once. This table is what says whether that lands in a sane range across every grid in the game.
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

// A parse failure is FATAL rather than falling back to a default. This file reported the OLD block
// behaviour for a while after the rework precisely because its regexes silently missed and its
// fallbacks were the previous numbers — a check that lies is worse than no check.
function must(re, what) {
  const m = new RegExp(re).exec(SRC);
  if (!m) { console.error(`FAIL  could not read ${what} from Survey.cs`); process.exit(1); }
  return parseFloat(m[1]);
}

const SCIENCE_UNITS = must(String.raw`ScoutUnits = [0-9]+, ScienceUnits = ([0-9]+)`, 'ScienceUnits');
const OTHER_UNITS   = must(String.raw`ScoutUnits = ([0-9]+)`, 'ScoutUnits');
const SCIENCE_SECS  = must(String.raw`ScienceBlockSeconds = ([0-9.]+)f`, 'ScienceBlockSeconds');
const OTHER_SECS    = must(String.raw`ScoutBlockSeconds = ([0-9.]+)f`, 'ScoutBlockSeconds');
const MIN_SECS      = must(String.raw`MinBlockSeconds = ([0-9.]+)f`, 'MinBlockSeconds');
const SOFT_CAP      = must(String.raw`SoftCapSeconds = ([0-9.]+)f`, 'SoftCapSeconds');
const COMPRESSION   = must(String.raw`SurveyCompression = ([0-9.]+)f`, 'SurveyCompression');
const SCI_ADV       = must(String.raw`ScienceAdvantage = ([0-9.]+)f`, 'ScienceAdvantage');
const MAX_HEADS     = must(String.raw`MaxHeads = ([0-9]+)`, 'MaxHeads');

// ---- Survey, ported ---------------------------------------------------------------------------
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

/// THE BLOCK IS THE SURVEY UNITS. No per-world scaling — see the header on Survey.BlockCells.
const blockCells = (w, h, units) => clamp(units, 2, Math.max(2, Math.min(w, h)));

const blocksAcross = (w, bc) => Math.max(1, Math.ceil(w / bc));
const bandCount = (h, bc) => Math.max(1, Math.ceil(h / bc));
const blockCount = (w, h, bc) => Math.max(1, blocksAcross(w, bc) * bandCount(h, bc));

/// Survey.BandY: every band is exactly bc tall and spread across [0, h - bc], so none hangs off the
/// map and the middle one is centred.
function bandY(h, bc, i) {
  bc = clamp(bc, 1, h);
  const ny = bandCount(h, bc);
  if (ny <= 1) return 0;
  return clamp(Math.round(i * (h - bc) / (ny - 1)), 0, h - bc);
}

/// Survey.WorldSurveySeconds — the world's own difficulty, quoted against a baseline scout.
function worldSurveySeconds(w, h) {
  const bc = clamp(OTHER_UNITS, 2, Math.max(2, Math.min(w, h)));
  const ideal = blockCount(w, h, bc) * OTHER_SECS;
  return ideal <= SOFT_CAP ? ideal : SOFT_CAP * Math.pow(ideal / SOFT_CAP, COMPRESSION);
}

/// Survey.SurveySeconds, at survey rate 1, tech 1 and a neutral world.
function surveySeconds(w, h, science) {
  const speed = (science ? SCI_ADV : 1) * (0.6 + 0.4 * 1);
  return Math.max(1, worldSurveySeconds(w, h) / speed);
}

/// Survey.BlockSeconds.
function blockSeconds(w, h, units, base, science) {
  const bc = blockCells(w, h, units);
  return clamp(surveySeconds(w, h, science) / blockCount(w, h, bc), MIN_SECS, base);
}

/// Survey.HeadSpeed — a FLOAT, which is what keeps blocks * dwell / speed exactly SurveySeconds.
function headSpeed(w, h, units, base, science) {
  const bc = blockCells(w, h, units);
  return Math.max(1, blockCount(w, h, bc) * blockSeconds(w, h, units, base, science) / surveySeconds(w, h, science));
}
const heads = (w, h, units, base, science) => clamp(Math.ceil(headSpeed(w, h, units, base, science) - 0.001), 1, MAX_HEADS);

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

/// Survey.ColOrigin / ColBlock: block 0 STRADDLES the middle column, so the first square a survey
/// draws sits centred on the map rather than starting at the middle and running off to the right.
const colOrigin = (w, bc) => (((Math.floor(w / 2) - Math.floor(bc / 2)) % w) + w) % w;
const colBlock = (w, bc, x) => Math.floor(((((x - colOrigin(w, bc)) % w) + w) % w) / bc);

/// The order every CELL of a world is uncovered in, as a block index.
function revealOrder(w, h, units, upFirst) {
  const bc = blockCells(w, h, units);
  const across = blocksAcross(w, bc);
  const bands = bandCount(h, bc);
  const order = bandOrder(bands, upFirst);

  const rank = new Int32Array(bands);
  for (let i = 0; i < order.length; i++) rank[order[i]] = i;

  // Which band owns a row. Bands overlap by a row or two where the world does not divide evenly, and
  // the first one containing the row wins — matching Survey.BlockRank.
  const bandOf = new Int32Array(h).fill(bands - 1);
  for (let y = 0; y < h; y++)
    for (let i = 0; i < bands; i++) {
      const y0 = bandY(h, bc, i);
      if (y >= y0 && y < y0 + bc) { bandOf[y] = i; break; }
    }

  const out = new Int32Array(w * h);
  for (let y = 0; y < h; y++)
    for (let x = 0; x < w; x++)
      out[y * w + x] = rank[bandOf[y]] * across + colBlock(w, bc, x);

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

console.log('constants read from Survey.cs: the block IS the survey units, literally ' +
            `(scout ${OTHER_UNITS}, science ${SCIENCE_UNITS}); dwell ${OTHER_SECS}s / ${SCIENCE_SECS}s, ` +
            `floored at ${MIN_SECS}s. A world past ${SOFT_CAP}s is compressed by ^${COMPRESSION}; ` +
            `a research hull is ${SCI_ADV}x a scout; the head is drawn as at most ${MAX_HEADS} blocks.\n`);

console.log('world            grid       scout blk    n  dwell  hd   time     sci blk    n  dwell  hd   time');
const rows = [];
for (const world of WORLDS) {
  const s = revealOrder(world.w, world.h, OTHER_UNITS, true);
  const c = revealOrder(world.w, world.h, SCIENCE_UNITS, true);
  const sD = blockSeconds(world.w, world.h, OTHER_UNITS, OTHER_SECS, false);
  const cD = blockSeconds(world.w, world.h, SCIENCE_UNITS, SCIENCE_SECS, true);
  const sH = heads(world.w, world.h, OTHER_UNITS, OTHER_SECS, false);
  const cH = heads(world.w, world.h, SCIENCE_UNITS, SCIENCE_SECS, true);
  const sT = surveySeconds(world.w, world.h, false), cT = surveySeconds(world.w, world.h, true);
  rows.push({ world, s, c, sT, cT, sD, cD, sH, cH });
  console.log(
    `  ${world.name.padEnd(13)} ${String(world.w + 'x' + world.h).padEnd(9)} ` +
    `${String(s.bc + 'x' + s.bc).padStart(8)} ${String(s.total).padStart(5)} ${sD.toFixed(2).padStart(6)}s ${String(sH).padStart(2)} ${sT.toFixed(0).padStart(5)}s  ` +
    `${String(c.bc + 'x' + c.bc).padStart(7)} ${String(c.total).padStart(5)} ${cD.toFixed(2).padStart(6)}s ${String(cH).padStart(2)} ${cT.toFixed(0).padStart(5)}s`);
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

// THE BOUNDS MOVED, DELIBERATELY, AND THIS IS THE TRADE THE FIXED BLOCK BOUGHT.
//
// It used to be 10s..200s for a scout, which was reachable because the block grew with the world — a
// gas giant was 32 bites of a 113x113 patch. A fixed 7x7 makes that same giant 4,232 bites, and no
// arrangement of dwell and sweep head turns four thousand steps into two minutes without the steps
// becoming invisible. So a survey is a longer job now: one to four minutes for a research hull, two to
// eight for a scout, and a scout on a gas giant is genuinely a slog — which is the correct answer to
// sending the wrong ship.
//
// The ceiling is what actually matters and it is what is checked: nothing may run past ten minutes.
ok('every world is surveyed in a time a player will sit through',
   rows.every(r => r.sT >= 30 && r.sT <= 600 && r.cT >= 20 && r.cT <= 300),
   'scout ' + rows.map(r => `${r.world.name} ${r.sT.toFixed(0)}s`).join(', ') +
   '\n     science ' + rows.map(r => `${r.cT.toFixed(0)}s`).join(', '));

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

// THE BLOCK IS THE SHIP'S UNITS, ON EVERY WORLD. This is the whole point of the rework and the one
// thing a future change must not quietly undo — the report was a science hull drawing 2x2 on an
// asteroid and 14x14 on a large world, and both of those are this check failing.
ok('a block is always exactly the survey units, whatever the world',
   rows.every(r => r.c.bc === Math.min(SCIENCE_UNITS, r.world.h) && r.s.bc === Math.min(OTHER_UNITS, r.world.h)),
   rows.map(r => `${r.world.name} ${r.c.bc}x${r.c.bc}`).join(', '));

// AND IT ALWAYS FITS. Every band is exactly bc tall and inside [0, h - bc], so a marker can never be
// drawn over ground that is not there — the "the entire 14x14 survey area was not even within the
// surveyable area to begin with" case.
{
  const bad = [];
  for (const { world, c } of rows) {
    for (let i = 0; i < bandCount(world.h, c.bc); i++) {
      const y0 = bandY(world.h, c.bc, i);
      if (y0 < 0 || y0 + c.bc > world.h) { bad.push(`${world.name} band ${i}`); break; }
    }
  }
  ok('every survey block is wholly inside the map', bad.length === 0,
     bad.length ? `off the map: ${bad.join(', ')}` : 'no band overhangs the top or bottom edge');
}

// AND IT STARTS IN THE MIDDLE. Block 0 of the running order must contain the centre cell.
{
  const bad = [];
  for (const { world, c } of rows) {
    const mid = c.out[Math.floor(world.h / 2) * world.w + Math.floor(world.w / 2)];
    if (mid !== 0) bad.push(`${world.name} (centre is block ${mid})`);
  }
  ok('the survey starts on the block holding the centre of the map', bad.length === 0,
     bad.length ? bad.join(', ') : 'block 0 holds the centre cell on every world tested');
}

// AND THE WHOLE MAP IS COVERED. Bands overlap where a world does not divide evenly, which is fine; a
// GAP would leave a stripe no survey ever reaches, i.e. a world that can never finish.
{
  const bad = [];
  for (const { world, c } of rows) {
    const seen = new Uint8Array(world.h);
    for (let i = 0; i < bandCount(world.h, c.bc); i++) {
      const y0 = bandY(world.h, c.bc, i);
      for (let y = y0; y < y0 + c.bc && y < world.h; y++) seen[y] = 1;
    }
    for (let y = 0; y < world.h; y++) if (!seen[y]) { bad.push(`${world.name} row ${y}`); break; }
  }
  ok('every row of every world falls in some band', bad.length === 0,
     bad.length ? `uncovered: ${bad.join(', ')}` : 'no gaps between bands');
}

console.log('');
let failed = 0;
for (const c of checks) { if (!c.p) failed++; console.log(`${c.p ? 'ok  ' : 'FAIL'} ${c.n}\n     ${c.d}`); }
console.log(`\n${path.relative(PROJ, OUT)}`);
console.log(failed ? `\n${failed} of ${checks.length} checks FAILED.` : `\nAll ${checks.length} checks pass.`);
process.exit(failed ? 1 : 0);
