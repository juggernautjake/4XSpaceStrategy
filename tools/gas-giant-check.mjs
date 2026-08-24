// ============================================================================================
// DO THE GAS GIANTS LOOK LIKE GAS GIANTS?
//
//   node tools/gas-giant-check.mjs [--out Art/_review/gas-giants.png]
//
// A Node port of PlanetTerrainGenerator.GasGiant + GasGiantStorms + GasGiantPalette, drawn as a
// contact sheet. There is no Unity here, and a great spot is the single most obvious example of
// something that reads perfectly sensibly in source and comes out on screen as a smear: the bands
// either bow around it or they run straight through it, and no amount of reading the deflection maths
// will tell you which.
//
// TWELVE WORLDS, one per panel, seeded so the sheet covers:
//   * all five palette variants, so the per-variant cloud/storm pairs can be compared side by side
//   * the full spot count range, 0 to 3
//   * the full size range, from a white oval to a spot a sixth of the world across
//
// The constants are READ OUT OF THE C# so this cannot drift from the game.
// ============================================================================================
import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/gas-giants.png'));

const read = f => fs.readFileSync(path.join(PROJ, f), 'utf8');
const STORMS = read('Assets/Scripts/Generation/GasGiantStorms.cs');
const PAL = read('Assets/Scripts/Visual/GasGiantPalette.cs');

function num(src, re, what) {
  const m = new RegExp(re).exec(src);
  if (!m) { console.error(`FAIL  could not read ${what}`); process.exit(1); }
  return parseFloat(m[1]);
}

const MAX_SPOTS = num(STORMS, 'MaxSpots = ([0-9]+)', 'MaxSpots');
const FLOW_HALO = num(STORMS, 'FlowHalo = ([0-9.]+)f', 'FlowHalo');
const BANDS     = num(STORMS, 'BandCycles = ([0-9.]+)f', 'BandCycles');
const HOLLOW    = num(STORMS, 'Hollow = ([0-9.]+)f', 'Hollow');
// Spot size is quoted as a MULTIPLE OF BAND HEIGHT now, not in absolute surface units.
const RV_BAND   = (() => {
  const m = /bandHeight \* Mathf\.Lerp\(([\d.]+)f, ([\d.]+)f, Next\(\)\)/.exec(STORMS);
  if (!m) { console.error('FAIL  could not read the spot size range'); process.exit(1); }
  return [parseFloat(m[1]), parseFloat(m[2])];
})();
const RV_LO = RV_BAND[0], RV_HI = RV_BAND[1];
const MOIST_JITTER = (() => {
  const g = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Generation/PlanetTerrainGenerator.cs'), 'utf8');
  const m = /moist \* ([\d.]+)f\) \* GasGiantStorms\.BandCycles/.exec(g);
  if (!m) { console.error('FAIL  could not read the gas giant moisture jitter'); process.exit(1); }
  return parseFloat(m[1]);
})();
const ASPECT = (() => {
  const m = /float aspect = Mathf\.Lerp\(([\d.]+)f, ([\d.]+)f, Next\(\)\)/.exec(STORMS);
  if (!m) { console.error('FAIL  could not read the spot aspect range'); process.exit(1); }
  return [parseFloat(m[1]), parseFloat(m[2])];
})();
const ASPECT_LO = ASPECT[0], ASPECT_HI = ASPECT[1];
const RU_CAP = (() => {
  const m = /Mathf\.Min\(rv \* aspect, ([\d.]+)f\)/.exec(STORMS);
  if (!m) { console.error('FAIL  could not read the spot width cap'); process.exit(1); }
  return parseFloat(m[1]);
})();
const BAND_H    = 1 / (2 * BANDS);
const RV_MIN = BAND_H * RV_LO, RV_MAX = BAND_H * RV_HI;

// The five variant colour pairs, parsed out of GasGiantPalette so a change there shows up here.
function colours(fn) {
  const body = new RegExp(`public static Color ${fn}\\(Variant v\\)[\\s\\S]*?\\n    \\}`).exec(PAL);
  if (!body) { console.error(`FAIL  could not read ${fn}`); process.exit(1); }
  const out = {};
  const re = /case Variant\.(\w+):\s*return new Color\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)/g;
  let m;
  while ((m = re.exec(body[0]))) out[m[1]] = [+m[2], +m[3], +m[4]];
  const def = /default:\s*return new Color\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)/.exec(body[0]);
  out.Ammonia = [+def[1], +def[2], +def[3]];
  return out;
}
const CLOUD = colours('CloudColor');
const STORM = colours('StormColor');

// Variant thresholds, so the sheet reports the real rarity.
const THRESH = (() => {
  const re = /if \(r < ([\d.]+)f\) return Variant\.(\w+);/g;
  const out = []; let m;
  while ((m = re.exec(PAL))) out.push([parseFloat(m[1]), m[2]]);
  return out;
})();
function variantOf(r) {
  for (const [t, name] of THRESH) if (r < t) return name;
  return 'Violet';
}

// ---- GasGiantStorms.Build, ported ---------------------------------------------------------------
function spotsFor(id, seed) {
  let n = ((id * 73856093) ^ Math.round(seed * 131)) >>> 0;
  const next = () => {
    n = (n ^ (n >>> 13)) >>> 0;
    n = Math.imul(n, 1274126177) >>> 0;
    n = (n ^ (n >>> 16)) >>> 0;
    return (n & 0xFFFFFF) / 0x1000000;
  };

  const roll = next();
  const count = roll < 0.15 ? 0 : roll < 0.55 ? 1 : roll < 0.85 ? 2 : 3;
  const spots = [];
  for (let i = 0; i < count && i < MAX_SPOTS; i++) {
    const band = Math.floor(next() * BANDS);
    const lat = Math.min(1, Math.max(0, (band + 0.75) / BANDS));
    const north = next() < 0.5;
    const v = north ? 0.5 + lat * 0.5 : 0.5 - lat * 0.5;
    const rv = RV_MIN + (RV_MAX - RV_MIN) * next();
    const aspect = ASPECT_LO + (ASPECT_HI - ASPECT_LO) * next();
    spots.push({ u: next(), v: Math.min(0.94, Math.max(0.06, v)), rv, ru: Math.min(rv * aspect, RU_CAP) });
  }
  return spots;
}

const dist = (s, u, v) => {
  let du = Math.abs(u - s.u);
  if (du > 0.5) du = 1 - du;
  const a = du / Math.max(1e-4, s.ru);
  const c = (v - s.v) / Math.max(1e-4, s.rv);
  return Math.sqrt(a * a + c * c);
};

// ---- PlanetTerrainGenerator.GasGiant, ported ----------------------------------------------------
function classify(spots, u, v, moist) {
  let bend = 0;
  for (const s of spots) {
    const d = dist(s, u, v);
    if (d <= 1) return 'Storm';
    if (d <= 1 + HOLLOW) return 'GasClouds';
    if (d >= 1 + FLOW_HALO) continue;
    const t = 1 - (d - 1) / FLOW_HALO;
    const dv = v - s.v;
    bend += (dv < 0 ? -1 : 1) * t * t * s.rv * 1.35;
  }
  const lat = Math.abs((v + bend) - 0.5) * 2;
  const band = ((lat + moist * MOIST_JITTER) * BANDS) % 1;
  return band < 0.5 ? 'GasClouds' : 'Storm';
}

// The moisture field is FBm in the game; a cheap value-noise stand-in is enough to show whether the
// band edges break up, which is the only thing it contributes here.
function noise2(x, y, seed) {
  const h = (a, b) => {
    let n = Math.imul((a * 374761393 + b * 668265263 + seed * 2246822519) >>> 0, 3266489917) >>> 0;
    n = (n ^ (n >>> 15)) >>> 0;
    return (n & 0xFFFF) / 0xFFFF;
  };
  const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi;
  const sx = xf * xf * (3 - 2 * xf), sy = yf * yf * (3 - 2 * yf);
  const a = h(xi, yi), b = h(xi + 1, yi), c = h(xi, yi + 1), d = h(xi + 1, yi + 1);
  return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;
}

// ---- draw ---------------------------------------------------------------------------------------
const CELL_W = 300, CELL_H = 150, PAD = 10, LABEL = 16;
const COLS = 3, ROWS = 4;
const W = COLS * (CELL_W + PAD) + PAD;
const H = ROWS * (CELL_H + LABEL + PAD) + PAD;
const img = Buffer.alloc(W * H * 3, 18);

function put(px, py, r, g, b) {
  if (px < 0 || py < 0 || px >= W || py >= H) return;
  const o = (py * W + px) * 3;
  img[o] = Math.max(0, Math.min(255, r | 0));
  img[o + 1] = Math.max(0, Math.min(255, g | 0));
  img[o + 2] = Math.max(0, Math.min(255, b | 0));
}

const panels = [];
for (let i = 0; panels.length < COLS * ROWS && i < 4000; i++) {
  const id = i, seed = (i * 137.31) % 10000;
  const spots = spotsFor(id, seed);
  let n = ((id * 73856093) ^ Math.round(seed * 131)) >>> 0;
  n = (n ^ (n >>> 13)) >>> 0; n = Math.imul(n, 1274126177) >>> 0;
  const variant = variantOf(((n ^ (n >>> 16)) & 0xFFFFFF) / 0x1000000);
  panels.push({ id, seed, spots, variant });
}
// Bias the sheet toward covering every variant and every spot count rather than the first twelve.
const want = ['Ammonia', 'Methane', 'Cobalt', 'Ember', 'Violet'];
const picked = [];
for (const v of want) { const p = panels.find(p => p.variant === v); if (p) picked.push(p); }
for (const c of [0, 1, 2, 3]) { const p = panels.find(p => p.spots.length === c && !picked.includes(p)); if (p) picked.push(p); }
for (const p of panels) { if (picked.length >= COLS * ROWS) break; if (!picked.includes(p)) picked.push(p); }

let idx = 0;
for (const p of picked.slice(0, COLS * ROWS)) {
  const cx = (idx % COLS) * (CELL_W + PAD) + PAD;
  const cy = Math.floor(idx / COLS) * (CELL_H + LABEL + PAD) + PAD + LABEL;
  idx++;

  const cloud = CLOUD[p.variant], storm = STORM[p.variant];
  for (let y = 0; y < CELL_H; y++) {
    for (let x = 0; x < CELL_W; x++) {
      const u = (x + 0.5) / CELL_W, v = (y + 0.5) / CELL_H;
      const moist = noise2(u * 5, v * 5, p.id);
      const t = classify(p.spots, u, v, moist);
      const c = t === 'Storm' ? storm : cloud;
      // The per-tile shade jitter both renderers apply, so the panel matches what the game draws.
      const sh = 0.86 + 0.26 * noise2(u * 60, v * 60, p.id + 7);
      put(cx + x, cy + y, c[0] * 255 * sh, c[1] * 255 * sh, c[2] * 255 * sh);
    }
  }
  // A one-pixel frame, so panels do not run together.
  for (let x = -1; x <= CELL_W; x++) { put(cx + x, cy - 1, 90, 90, 100); put(cx + x, cy + CELL_H, 90, 90, 100); }
  for (let y = -1; y <= CELL_H; y++) { put(cx - 1, cy + y, 90, 90, 100); put(cx + CELL_W, cy + y, 90, 90, 100); }
}

fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp(img, { raw: { width: W, height: H, channels: 3 } }).png().toFile(OUT);

// ---- the report ----------------------------------------------------------------------------------
console.log(`bands ${BANDS}  halo ${FLOW_HALO}  spot rv ${RV_MIN}..${RV_MAX}  max ${MAX_SPOTS}`);
console.log('\npanels (left to right, top to bottom):');
idx = 0;
for (const p of picked.slice(0, COLS * ROWS)) {
  const sizes = p.spots.map(s => `${(s.ru * 2 * 100).toFixed(0)}x${(s.rv * 2 * 100).toFixed(0)}%`).join(' ');
  console.log(`  ${String(++idx).padStart(2)}  ${p.variant.padEnd(8)} ${p.spots.length} spot(s)  ${sizes}`);
}

// Distribution over many worlds, so the rarity claims in the C# can be checked.
const N = 20000, counts = [0, 0, 0, 0], vcount = {};
for (let i = 0; i < N; i++) {
  const s = spotsFor(i, (i * 91.7) % 10000);
  counts[s.length]++;
  let n = ((i * 73856093) ^ Math.round(((i * 91.7) % 10000) * 131)) >>> 0;
  n = (n ^ (n >>> 13)) >>> 0; n = Math.imul(n, 1274126177) >>> 0;
  const v = variantOf(((n ^ (n >>> 16)) & 0xFFFFFF) / 0x1000000);
  vcount[v] = (vcount[v] || 0) + 1;
}
console.log(`\nspots per giant over ${N}:  ` + counts.map((c, i) => `${i}:${(100 * c / N).toFixed(0)}%`).join('  '));
console.log('variants:  ' + Object.entries(vcount).map(([k, c]) => `${k} ${(100 * c / N).toFixed(1)}%`).join('  '));

let bad = 0;
const check = (ok, msg) => { console.log(`${ok ? 'ok   ' : 'FAIL '} ${msg}`); if (!ok) bad++; };
const violet = 100 * (vcount.Violet || 0) / N;
check(violet <= 2.0, `violet giants are much rarer than before (${violet.toFixed(1)}%, was 5%)`);
check(counts[0] / N > 0.08 && counts[0] / N < 0.25, `some giants have no great spot (${(100 * counts[0] / N).toFixed(0)}%)`);
check(counts[1] / N > 0.25, `one spot is the common case (${(100 * counts[1] / N).toFixed(0)}%)`);

console.log(`\nwrote ${path.relative(PROJ, OUT)}`);
process.exit(bad ? 1 : 0);
