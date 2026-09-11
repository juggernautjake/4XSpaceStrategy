// ============================================================================================
// HOW ROUGH IS A WORLD FROM ONE TILE TO THE NEXT — and does a small moon read as a small moon?
//
//   node tools/terrain-relief-check.mjs [--out Art/_review/terrain-relief.png]
//
// Two reports drove this:
//
//   "Some worlds have extremely varied altitudes between grids. This is far too inconsistent and
//    should be smoothed out quite a bit... especially for what are essentially tiny moons."
//   "A tiny moon but all across its surface is mountainous terrain, 11,000 to nearly 17,000 m."
//
// Both are properties of the VARIATION PASS in PlanetTerrainGenerator.SampleNormalized — the noise a
// world with no plates and no plumes gets its whole relief from — and neither can be seen by reading
// it. The first is a sampling fact: the pass is six octaves of Perlin at a fixed frequency, so on a
// 24-cell moon the top three octaves have a period UNDER one cell and are pure per-tile static. The
// second is a scale fact: the same amplitude is applied to a 0.1-mass moon and a 4-mass super-earth.
//
// So this ports the pass, builds dead-world grids at four real masses, and measures the two things
// the player actually sees:
//
//   JUMP   — the mean height step between two adjacent tiles, in metres. The contour lines are drawn
//            wherever adjacent tiles fall in different 500 m bands, so this IS the density of lines.
//   SPREAD — the 5th-to-95th percentile height range across the world, in metres.
//
// ...before and after the two changes (a per-world octave cap at the grid's own Nyquist limit, and a
// relief scale that falls with mass), and renders both as the game would: flat grey tiles with the
// contour pass over them. The constants are READ OUT OF THE C# so this cannot drift from the game.
// ============================================================================================
import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/terrain-relief.png'));

const read = f => fs.readFileSync(path.join(PROJ, f), 'utf8');
const GEN = read('Assets/Scripts/Generation/PlanetTerrainGenerator.cs');
const METRICS = read('Assets/Scripts/Visual/MapMetrics.cs');

function num(src, re, what) {
  const m = new RegExp(re).exec(src);
  if (!m) { console.error(`FAIL  could not read ${what} — the constant moved or was renamed.`); process.exit(1); }
  return parseFloat(m[1]);
}

const DEAD_GAIN   = num(GEN, 'DeadWorldVariationGain = ([0-9.]+)f', 'DeadWorldVariationGain');
const OCTAVES     = num(GEN, 'public const int Octaves = ([0-9]+)', 'Octaves');
const METRES      = num(GEN, 'MetresPerElevationUnit = ([0-9.]+)f', 'MetresPerElevationUnit');
const CONTOUR     = num(GEN, 'ContourInterval = ([0-9.]+)f', 'ContourInterval');
const MIN_PERIOD  = num(GEN, 'MinVariationPeriodCells = ([0-9.]+)f', 'MinVariationPeriodCells');
const RELIEF_LO   = num(GEN, 'SmallBodyReliefFloor = ([0-9.]+)f', 'SmallBodyReliefFloor');
const MIN_BASE    = num(GEN, 'MinBasePeriodCells = ([0-9.]+)f', 'MinBasePeriodCells');
const RELIEF_CELLS = num(GEN, 'FullReliefPeriodCells = ([0-9.]+)f', 'FullReliefPeriodCells');

// ---- Perlin, classic improved 2D. Unity's Mathf.PerlinNoise is the same family; the exact table
// differs, which does not matter for a distribution measurement (see the memory note on ports).
const perm = new Uint8Array(512);
{
  const p = Array.from({ length: 256 }, (_, i) => i);
  let s = 1234567;
  const rnd = () => { s = (Math.imul(s, 1103515245) + 12345) >>> 0; return s / 4294967296; };
  for (let i = 255; i > 0; i--) { const j = Math.floor(rnd() * (i + 1)); [p[i], p[j]] = [p[j], p[i]]; }
  for (let i = 0; i < 512; i++) perm[i] = p[i & 255];
}
const fade = t => t * t * t * (t * (t * 6 - 15) + 10);
function grad(h, x, y) { switch (h & 3) { case 0: return x + y; case 1: return -x + y; case 2: return x - y; default: return -x - y; } }
function perlin(x, y) {
  const X = Math.floor(x) & 255, Y = Math.floor(y) & 255;
  const xf = x - Math.floor(x), yf = y - Math.floor(y);
  const u = fade(xf), v = fade(yf);
  const aa = perm[perm[X] + Y], ab = perm[perm[X] + Y + 1], ba = perm[perm[X + 1] + Y], bb = perm[perm[X + 1] + Y + 1];
  const x1 = grad(aa, xf, yf) + u * (grad(ba, xf - 1, yf) - grad(aa, xf, yf));
  const x2 = grad(ab, xf, yf - 1) + u * (grad(bb, xf - 1, yf - 1) - grad(ab, xf, yf - 1));
  return 0.5 + 0.5 * (x1 + v * (x2 - x1)) / 1.0;
}
function fbm(x, y, octaves) {
  let amp = 1, freq = 1, sum = 0, norm = 0;
  for (let o = 0; o < octaves; o++) { sum += amp * perlin(x * freq, y * freq); norm += amp; amp *= 0.5; freq *= 2; }
  return sum / norm;
}
// WrapU, ported: variance-preserving blend of two samples one period apart.
function wrapU(u, span, mult, y, offX, offY, oct) {
  const period = span * mult, x = u * period;
  const a = fbm(x + offX, y + offY, oct), b = fbm(x - period + offX, y + offY, oct);
  const w2 = u, w1 = 1 - u, k = Math.sqrt(w1 * w1 + w2 * w2);
  return 0.5 + ((a - 0.5) * w1 + (b - 0.5) * w2) / k;
}

// ---- The grid sizes the game actually builds (MapMetrics.WidthForMass, ported) -------------------
const SMALL_MAX  = num(METRICS, 'SmallMassMax = ([0-9.]+)f', 'SmallMassMax');
const PLANET_MIN = num(METRICS, 'PlanetMassMin = ([0-9.]+)f', 'PlanetMassMin');
const CPM_SMALL  = num(METRICS, 'CellsPerMassSmall = ([0-9.]+)f', 'CellsPerMassSmall');
const CPM_PLANET = num(METRICS, 'CellsPerMassPlanet = ([0-9.]+)f', 'CellsPerMassPlanet');
const KNEE       = num(METRICS, 'KneeWidth = ([0-9.]+)f', 'KneeWidth');
const MIN_W      = num(METRICS, 'MinWidth = ([0-9]+)', 'MinWidth');
const MAX_W      = num(METRICS, 'MaxWidth = ([0-9.]+)f', 'MaxWidth');
function widthForMass(mass) {
  let raw;
  if (mass <= SMALL_MAX) raw = mass * CPM_SMALL;
  else if (mass >= PLANET_MIN) raw = mass * CPM_PLANET;
  else raw = SMALL_MAX * CPM_SMALL + (PLANET_MIN * CPM_PLANET - SMALL_MAX * CPM_SMALL) * ((mass - SMALL_MAX) / (PLANET_MIN - SMALL_MAX));
  const w = raw <= KNEE ? raw : KNEE + Math.sqrt(raw - KNEE) * 8;
  return Math.max(MIN_W, Math.min(Math.round(MAX_W), Math.round(w)));
}
// continentFrequency as SeedTerrain rolls it, then the sampler's own cap by the grid's cell count.
const freqFor = (mass, w, fixed) => { const f = Math.min(8, Math.max(2.5, mass * 6 * 0.32)); return fixed ? f : Math.min(f, w / (2 * MIN_BASE)); };

// ---- The two changes under test, ported from the C# -----------------------------------------------
// Octave cap: the finest octave the variation pass may use is the one whose period is still at
// least MinVariationPeriodCells cells wide on THIS world's grid.
function variationOctaves(w, freq, cap) {
  const cellsPerPeriod = w / (freq * 2);
  let n = 1;
  while (n < cap && cellsPerPeriod / Math.pow(2, n) >= MIN_PERIOD) n++;
  return n;
}
const reliefScale = cellsPerPeriod => Math.min(1, Math.max(RELIEF_LO, cellsPerPeriod / RELIEF_CELLS));

// ---- Build a dead world and measure it ----------------------------------------------------------
function build(mass, seed, fixed) {
  const w = widthForMass(mass), h = Math.max(1, Math.floor(w / 2));
  const freq = freqFor(mass, w, fixed);
  const oct = fixed ? OCTAVES : variationOctaves(w, freq, OCTAVES);
  const scale = fixed ? 1 : reliefScale(w / (freq * 2));
  const land = new Float32Array(w * h);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const u = (x + 0.5) / w, v = (y + 0.5) / h;
    const raw = wrapU(u, freq * 2, 1, v * freq, seed, seed * 1.3, oct);
    land[y * w + x] = 0.5 + (raw - 0.5) * 2 * DEAD_GAIN * scale;
  }
  // JUMP and band-crossing fraction over every east and north neighbour pair.
  let jump = 0, cross = 0, pairs = 0;
  const band = hgt => Math.floor(hgt * METRES / CONTOUR);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const a = land[y * w + x];
    const e = land[y * w + ((x + 1) % w)];
    jump += Math.abs(a - e); pairs++; if (band(a) !== band(e)) cross++;
    if (y + 1 < h) { const n = land[(y + 1) * w + x]; jump += Math.abs(a - n); pairs++; if (band(a) !== band(n)) cross++; }
  }
  const sorted = Float32Array.from(land).sort();
  const p = q => sorted[Math.min(sorted.length - 1, Math.floor(q * sorted.length))];
  return { w, h, oct, freq, scale, land, jumpM: jump / pairs * METRES, cross: cross / pairs, spreadM: (p(0.95) - p(0.05)) * METRES };
}

const CASES = [
  { name: 'tiny moon',  mass: 0.15 },
  { name: 'large moon', mass: 0.5 },
  { name: 'earth',      mass: 1.0 },
  { name: 'super-earth', mass: 3.0 },
];

console.log(`variation gain ${DEAD_GAIN}  octaves ${OCTAVES}  min period ${MIN_PERIOD} cells  relief floor ${RELIEF_LO}, full at ${RELIEF_CELLS} cells per feature\n`);
console.log('world         grid     oct  freq  scale   JUMP m   lines/edge   SPREAD m (5-95%)');
const rows = [];
for (const c of CASES) {
  for (const fixed of [true, false]) {
    const r = build(c.mass, 17.3 + c.mass * 100, fixed);
    rows.push({ ...c, fixed, ...r });
    console.log(`${(c.name + (fixed ? ' (before)' : ' (after)')).padEnd(22)} ${String(r.w + 'x' + r.h).padEnd(8)} ${String(r.oct).padEnd(4)} ${r.freq.toFixed(2).padEnd(5)} ${r.scale.toFixed(2).padEnd(6)} ${r.jumpM.toFixed(0).padStart(6)}   ${(100 * r.cross).toFixed(0).padStart(5)}%      ${r.spreadM.toFixed(0).padStart(6)}`);
  }
}

// ---- Render: grey tiles, the game's own contour pass, before over after -----------------------------
// Each row gets the cell size that fits its grid into a fixed panel, so a 15-cell moon and a 513-cell
// super-earth are both legible on one sheet. The lines are drawn one PIXEL wide either way, which is
// what the game does at any zoom now that the map texture is mipmapped.
const PANEL = 700, PAD = 12, LABEL = 14;
const cellFor = w => Math.max(1, Math.floor(PANEL / w));
const panelW = PANEL;
const W = 2 * panelW + 3 * PAD;
let H = PAD;
for (const c of CASES) { const w = widthForMass(c.mass); H += Math.floor(w / 2) * cellFor(w) + LABEL + PAD; }
H = Math.ceil(H);
const img = Buffer.alloc(W * H * 3, 18);
function put(px, py, r, g, b) {
  if (px < 0 || py < 0 || px >= W || py >= H) return;
  const o = (py * W + px) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b;
}
let cy = PAD;
for (const c of CASES) {
  const pair = rows.filter(r => r.name === c.name);
  let cx = PAD;
  for (const r of pair) {
    const CELL = cellFor(r.w);
    const band = new Int32Array(r.w * r.h);
    for (let i = 0; i < band.length; i++) band[i] = Math.floor(r.land[i] * METRES / CONTOUR);
    for (let y = 0; y < r.h; y++) for (let x = 0; x < r.w; x++) {
      const i = y * r.w + x;
      // The elevation shade the renderer applies: the same 5% per band as the game.
      const shade = Math.max(0.55, Math.min(1.45, 1 + 0.05 * band[i]));
      const g = Math.max(0, Math.min(255, 128 * shade));
      for (let sy = 0; sy < CELL; sy++) for (let sx = 0; sx < CELL; sx++) put(cx + x * CELL + sx, cy + LABEL + (r.h - 1 - y) * CELL + sy, g, g, g);
    }
    for (let y = 0; y < r.h; y++) for (let x = 0; x < r.w; x++) {
      const here = band[y * r.w + x];
      const east = band[y * r.w + ((x + 1) % r.w)];
      if (east !== here) for (let sy = 0; sy < CELL; sy++) put(cx + x * CELL + CELL - 1, cy + LABEL + (r.h - 1 - y) * CELL + sy, 28, 28, 28);
      if (y + 1 < r.h && band[(y + 1) * r.w + x] !== here) for (let sx = 0; sx < CELL; sx++) put(cx + x * CELL + sx, cy + LABEL + (r.h - 1 - y) * CELL, 28, 28, 28);
    }
    cx += panelW + PAD;
  }
  { const w = widthForMass(c.mass); cy += Math.floor(w / 2) * cellFor(w) + LABEL + PAD; }
}
fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp(img, { raw: { width: W, height: H, channels: 3 } }).png().toFile(OUT);
console.log(`\nleft column = before, right = after, top to bottom: ${CASES.map(c => c.name).join(', ')}`);

let bad = 0;
const check = (ok, msg) => { console.log(`${ok ? 'ok   ' : 'FAIL '} ${msg}`); if (!ok) bad++; };
const after = n => rows.find(r => r.name === n && !r.fixed);
const before = n => rows.find(r => r.name === n && r.fixed);
check(after('tiny moon').cross < before('tiny moon').cross * 0.5, `a tiny moon has under half the contour lines it had (${(100 * after('tiny moon').cross).toFixed(0)}% of edges, was ${(100 * before('tiny moon').cross).toFixed(0)}%)`);
check(after('tiny moon').spreadM < 3000, `a tiny moon's relief spans under 3 km (${after('tiny moon').spreadM.toFixed(0)} m)`);
check(after('earth').spreadM > 3000, `an earth-mass dead world still has real relief (${after('earth').spreadM.toFixed(0)} m)`);
check(after('earth').cross < before('earth').cross, `an earth-mass world lost some per-tile static (${(100 * after('earth').cross).toFixed(0)}% of edges, was ${(100 * before('earth').cross).toFixed(0)}%)`);
console.log(`\nwrote ${path.relative(PROJ, OUT)}`);
process.exit(bad ? 1 : 0);
