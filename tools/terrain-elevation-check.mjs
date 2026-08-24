// ============================================================================================
// DOES A BIOME STILL SECRETLY MEAN "HOW HIGH"?  AND DO THE CONTOURS READ AS CONTOURS?
//
//   node tools/terrain-elevation-check.mjs [--out Art/_review/terrain-elevation.png]
//
// Two requests, one test, because they are two halves of the same change:
//
//   F20  "Biomes must not denote elevation... Past a certain elevation it should simply be mountain
//         terrain. A biome says what ground IS; how high it is, is a separate fact."
//   F22  "Black contour lines every 500 m ... giving the planet map a topographic read. This is what
//         replaces biomes-as-elevation."
//
// Taking altitude out of the biome names removes the only thing that showed relief, so the contours
// have to put it back. Checking either one alone would pass a map that is either flat or striped.
//
// ---- WHY THE TEST IS SHAPED THIS WAY -----------------------------------------------------------
//
// The bug in the screenshot is a geothermal vent ringed by concentric bands — Metallic Crust, then
// Badlands, then Canyon — and the give-away is that they are CONCENTRIC. A biome ring around a bump
// means the classifier is reading the bump.
//
// So the probe holds `ridge` (and moisture, and temperature) CONSTANT and sweeps `elev` alone. Under a
// correct classifier that sweep is nearly silent: nothing about the ground has changed except how far
// above the datum it sits, so the type must not change either — right up to the alpine line, where the
// request says it becomes mountain and stops. Every extra type the sweep produces is an altitude band
// wearing a biome's name, and the count is the measurement.
//
// This is deliberately a HARSHER test than a rendered world would be. On a real map elevation and
// roughness correlate, so an elevation-driven band is easy to mistake for a roughness-driven one. Here
// they are decoupled by construction and there is nowhere for the coupling to hide.
//
// The thresholds are READ OUT OF THE C# so this cannot drift from the game.
// ============================================================================================
import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/terrain-elevation.png'));

const read = f => fs.readFileSync(path.join(PROJ, f), 'utf8');
const GEN = read('Assets/Scripts/Generation/PlanetTerrainGenerator.cs');

function num(src, re, what) {
  const m = new RegExp(re).exec(src);
  if (!m) { console.error(`FAIL  could not read ${what} — the constant moved or was renamed.`); process.exit(1); }
  return parseFloat(m[1]);
}

// ---- the constants under test ------------------------------------------------------------------
const ALPINE   = num(GEN, String.raw`public const float AlpineAbove = ([\d.]+)f`, 'AlpineAbove');
const INTERVAL = num(GEN, String.raw`public const float ContourInterval = ([\d.]+)f`, 'ContourInterval');
const MPU      = num(GEN, String.raw`MetresPerElevationUnit = ([\d.]+)f`, 'MetresPerElevationUnit');
const DARKEN   = num(read('Assets/Scripts/Visual/SurfaceTextureRenderer.cs'),
                     String.raw`ContourDarken = ([\d.]+)f`, 'ContourDarken');

// The shoreline every band and every contour is measured from (ElevationBand / ElevationMetres).
const SHORE = 0.36;

// Sea shift is 0 at neutral water level, which is what every probe below uses. Verified rather than
// assumed: if SeaShift(0.5) stops being 0 these numbers all move and the test must be told.
const SEA = 0;

const mountainHeight = sea => SHORE + sea + ALPINE;
const metres = (h, sea) => (h - (SHORE + sea)) * MPU;
const contourBand = (h, sea) => Math.floor(metres(h, sea) / INTERVAL);

// ---- the classifiers, ported ------------------------------------------------------------------
// Only the solid-ground ones, and only the branches a constant-ridge elevation sweep can reach.
function terran(elev, sea, moist, temp, ridge) {
  if (elev < 0.36 + sea) return temp < 0.22 ? 'FrozenSea' : 'Ocean';
  if (elev < 0.40 + sea) return temp < 0.22 ? 'Snow' : 'Beach';
  if (elev >= mountainHeight(sea)) return 'Mountains';
  if (ridge > 0.82) return 'Mountains';
  if (temp < 0.28) { if (moist > 0.55) return 'Taiga'; return elev > 0.5 ? 'Snow' : 'Tundra'; }
  if (temp < 0.62) {
    if (elev < 0.44 && moist > 0.7) return 'Swamp';
    if (moist > 0.62) return 'Forest';
    if (moist > 0.4) return 'Grassland';
    if (moist > 0.25) return 'Plains';
    return 'Steppe';
  }
  if (moist > 0.66) return 'Jungle';
  if (moist > 0.42) return 'Savanna';
  if (moist > 0.25) return 'Plains';
  if (moist > 0.14) return 'Dunes';
  return 'Desert';
}

function barren(elev, sea, temp, ridge) {
  if (elev < 0.3 + sea) return temp < 0.22 ? 'FrozenSea' : 'Ocean';
  if (elev >= mountainHeight(sea)) return 'Mountains';
  if (ridge > 0.82) return 'Mountains';
  if (ridge > 0.7) return 'Canyon';
  if (elev < 0.3) return 'SaltFlat';
  if (ridge > 0.5) return 'Badlands';
  if (ridge > 0.34) return 'MetallicCrust';
  return 'Wasteland';
}

function airless(elev, sea, temp, ridge) {
  if (elev >= mountainHeight(sea)) return 'Mountains';
  if (ridge > 0.85) return 'Mountains';
  if (elev < 0.28) return 'Crater';
  if (ridge > 0.72) return 'CrystalField';
  if (ridge > 0.55) return 'MetallicCrust';
  if (elev < 0.4 + sea) return temp < 0.22 ? 'Ice' : 'CrackedGround';
  return 'Barren';
}

function volcanic(elev, sea, temp, ridge, lat) {
  const hot = temp + (1 - lat) * 0.2;
  if (hot > 0.9 && ridge > 0.7) return 'Volcano';
  if (hot > 0.78) return 'MagmaField';
  if (elev >= mountainHeight(sea)) return 'Mountains';
  if (ridge > 0.72) return 'Mountains';
  if (elev < 0.32) return 'ObsidianFlat';
  if (ridge > 0.58) return 'LavaRock';
  if (temp > 0.6) return 'AshWaste';
  if (ridge > 0.40) return 'CrackedGround';
  return 'GeyserField';
}

// ============================================================================================
// PROBE 1 — sweep elevation with everything else nailed down
// ============================================================================================
//
// `ridge` is swept separately across its own range because a classifier can hide an elevation band
// inside one roughness bracket and not another — Barren's old `elev > 0.55` only showed up below the
// Badlands cut, so a probe at a single ridge value could have missed it entirely.
//
// ABOVE-WATER ONLY. The waterline genuinely does move with elevation and is supposed to: a tile below
// it is sea, and that is a fact about the ground, not a band. The sweep starts above every
// classifier's shoreline so the sea is not counted as an elevation band.

function sweepTypes(fn, ridge, extra = {}) {
  const seen = new Set();
  const temp = extra.temp ?? 0.5, moist = extra.moist ?? 0.5, lat = extra.lat ?? 0.5;
  for (let e = 0.42; e <= 1.20; e += 0.002) {
    const t = fn === terran ? terran(e, SEA, moist, temp, ridge)
            : fn === barren ? barren(e, SEA, temp, ridge)
            : fn === airless ? airless(e, SEA, temp, ridge)
            : volcanic(e, SEA, temp, ridge, lat);
    seen.add(t);
  }
  return seen;
}

const WORLDS = [
  ['Terran',   terran],
  ['Barren',   barren],
  ['Airless',  airless],
  ['Volcanic', volcanic],
];

console.log(`AlpineAbove ${ALPINE}  -> mountain at ${mountainHeight(SEA).toFixed(2)} ` +
            `(${metres(mountainHeight(SEA), SEA).toFixed(0)} m)`);
console.log(`ContourInterval ${INTERVAL} m  over ${MPU} m of span  darken x${DARKEN}\n`);

console.log('Sweeping ELEVATION alone, with roughness and climate held fixed.');
console.log('A type that appears only because the ground got higher is an altitude band.\n');
console.log('world      ridge   types produced by elevation alone');

let bandFailures = 0;
for (const [name, fn] of WORLDS) {
  for (const ridge of [0.10, 0.30, 0.45, 0.60, 0.75]) {
    const seen = [...sweepTypes(fn, ridge)];
    // Mountains is the one type elevation is ALLOWED to introduce — that is the request.
    const fromElevation = seen.filter(t => t !== 'Mountains');
    const ok = fromElevation.length <= 1;
    if (!ok) bandFailures++;
    console.log(`${name.padEnd(10)} ${ridge.toFixed(2)}    ` +
                `${seen.join(', ')}${ok ? '' : '   <-- BAND'}`);
  }
}

// ============================================================================================
// PROBE 2 — the contours, drawn on a height field with a known answer
// ============================================================================================
//
// A CONE, because its contours have an answer you can check by hand: concentric rings, evenly spaced,
// closing on themselves, one texel wide, and none of them broken at the seam. Anything a real noise
// field would show, a cone shows more legibly — and unlike a noise field it cannot accidentally agree
// with a broken implementation.
//
// The renderer's PaintContours is ported verbatim rather than approximated. The point of the picture
// is to catch the things the arithmetic cannot state: a doubled line, a line on the wrong side of a
// step, a seam.

const W = 96, H = 48, SCALE = 8;
const TW = W * SCALE, TH = H * SCALE;

function heightAt(x, y) {
  // THE CONE SITS ON THE SEAM, and that is the whole reason for its position.
  //
  // Centred on the map instead, its contours run PARALLEL to the seam and every band has the same
  // value either side of x = W-1 -> 0, so nothing ever crosses and a broken wrap passes unnoticed —
  // which is exactly what the first version of this probe did. On the seam, the rings cut across it
  // radially and a wrap that does not join shows as a slit down the edge of the picture.
  const cx = 0, cy = H / 2;
  const dx = Math.min(Math.abs(x - cx), W - Math.abs(x - cx));   // wraps, like longitude
  const dy = y - cy;
  const cone = Math.max(0, 1 - Math.sqrt(dx * dx + dy * dy) / (H * 0.55));
  // A saddle across the middle so the map has slopes running both ways, not just one cone's flanks.
  const swell = 0.16 * Math.max(0, 1 - Math.abs(dy) / (H * 0.30)) * (x / W);
  return 0.20 + cone * 0.85 + swell;
}

const band = new Int32Array(W * H);
for (let y = 0; y < H; y++)
  for (let x = 0; x < W; x++)
    band[y * W + x] = contourBand(heightAt(x, y), SEA);

// Flat base colour so the only structure in the picture is the contours themselves.
const px = new Uint8Array(TW * TH * 3);
for (let y = 0; y < H; y++)
  for (let x = 0; x < W; x++) {
    // A gentle greyscale ramp by height, so it is obvious which way is up.
    const g = Math.round(90 + 120 * Math.min(1, Math.max(0, (heightAt(x, y) - 0.2) / 1.0)));
    for (let sy = 0; sy < SCALE; sy++)
      for (let sx = 0; sx < SCALE; sx++) {
        const i = ((y * SCALE + sy) * TW + x * SCALE + sx) * 3;
        px[i] = g; px[i + 1] = g; px[i + 2] = Math.round(g * 0.92);
      }
  }

// TWO SETS, NOT ONE.
//
// The first version of this probe kept a single set and called every repeat a defect, which reported
// 844 of them on a correct implementation. They were CORNERS: a cell drawing both its north edge and
// its east edge shares the one texel where the two meet, and two contours crossing at a corner is what
// contours do. Counting those as doubled lines would have forced a "fix" for something that is not
// wrong, and buried the two things that were.
//
// What IS a defect is the same edge drawn twice — a line two texels wide instead of one. So the
// orientations are tracked separately and a repeat only counts within its own orientation.
const drawnV = new Set(), drawnH = new Set();   // vertical (east edges), horizontal (north edges)
const drawn = new Set();                        // both, for the coverage figure
function darken(i) {
  drawn.add(i);
  px[i * 3] = Math.round(px[i * 3] * DARKEN);
  px[i * 3 + 1] = Math.round(px[i * 3 + 1] * DARKEN);
  px[i * 3 + 2] = Math.round(px[i * 3 + 2] * DARKEN);
}

let doubled = 0;
for (let y = 0; y < H; y++)
  for (let x = 0; x < W; x++) {
    const here = band[y * W + x];
    const ox = x * SCALE, oy = y * SCALE;

    const east = band[y * W + (x + 1 === W ? 0 : x + 1)];
    if (east !== here) {
      const col = here < east ? ox + SCALE - 1 : (x + 1 === W ? 0 : ox + SCALE);
      if (col < TW) for (let sy = 0; sy < SCALE; sy++) {
        const i = (oy + sy) * TW + col;
        if (drawnV.has(i)) doubled++;
        drawnV.add(i);
        darken(i);
      }
    }

    if (y + 1 < H) {
      const north = band[(y + 1) * W + x];
      if (north !== here) {
        const row = here < north ? oy + SCALE - 1 : oy + SCALE;
        for (let sx = 0; sx < SCALE; sx++) {
          const i = row * TW + ox + sx;
          if (drawnH.has(i)) doubled++;
          drawnH.add(i);
          darken(i);
        }
      }
    }
  }

// ---- what the picture should contain -----------------------------------------------------------
const bands = new Set([...band]);
// MEASURED over the grid, not sampled at two points I believe to be the extremes. The first version
// read heightAt(W/2, H/2) and heightAt(0, 0) as peak and floor — true of a centred cone and false the
// moment the cone moved onto the seam, at which point it reported a 33 m range across a map that
// spans 12 km and the check failed against its own arithmetic rather than against the code.
let peak = -Infinity, floor = Infinity;
for (let y = 0; y < H; y++)
  for (let x = 0; x < W; x++) {
    const hgt = heightAt(x, y);
    if (hgt > peak) peak = hgt;
    if (hgt < floor) floor = hgt;
  }
const expectedBands = contourBand(peak, SEA) - contourBand(floor, SEA) + 1;

// The seam: a contour crossing x = W-1 -> 0 must have drawn a texel in column 0 or column TW-1.
let seamDrawn = 0;
for (let y = 0; y < H; y++) {
  const here = band[y * W + (W - 1)], east = band[y * W];
  if (here !== east) seamDrawn++;
}
let seamTexels = 0;
for (let ty = 0; ty < TH; ty++) {
  if (drawn.has(ty * TW)) seamTexels++;
  if (drawn.has(ty * TW + TW - 1)) seamTexels++;
}

fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp(Buffer.from(px), { raw: { width: TW, height: TH, channels: 3 } }).png().toFile(OUT);

console.log(`\nContour probe: ${W}x${H} cells at ${SCALE} texels, cone from ` +
            `${metres(floor, SEA).toFixed(0)} m to ${metres(peak, SEA).toFixed(0)} m`);
console.log(`  distinct 500 m bands on the map: ${bands.size} (expected ${expectedBands})`);
console.log(`  contour texels drawn: ${drawn.size} of ${TW * TH} ` +
            `(${(100 * drawn.size / (TW * TH)).toFixed(1)}% of the map)`);
console.log(`  seam crossings: ${seamDrawn} rows, ${seamTexels} texels in the edge columns`);
console.log(`\nwrote ${path.relative(PROJ, OUT)}`);

// ---- the asserts --------------------------------------------------------------------------------
let bad = 0;
const check = (ok, msg) => { console.log(`${ok ? 'ok   ' : 'FAIL '} ${msg}`); if (!ok) bad++; };

check(bandFailures === 0,
  `no classifier turns elevation alone into more than one biome (${bandFailures} that do)`);
check([...sweepTypes(barren, 0.10)].includes('Mountains') &&
      [...sweepTypes(terran, 0.10)].includes('Mountains'),
  'the ramp ARRIVES somewhere: alpine ground is mountain, on smooth ground as well as broken');
check(bands.size === expectedBands,
  `every 500 m band between the floor and the peak is present (${bands.size}/${expectedBands})`);
check(doubled === 0,
  `no contour is drawn twice — a line is one texel, not two (${doubled} doubled)`);
check(seamDrawn > 0 && seamTexels > 0,
  `contours cross the longitude seam rather than stopping at it (${seamDrawn} rows)`);
check(drawn.size / (TW * TH) < 0.15,
  `the contours are a hairline over the terrain, not a mesh (${(100 * drawn.size / (TW * TH)).toFixed(1)}%)`);

process.exit(bad ? 1 : 0);
