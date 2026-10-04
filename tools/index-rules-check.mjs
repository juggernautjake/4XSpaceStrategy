// ============================================================================================
// THE INDEX RULES, CHECKED AGAINST THE SPEC
//
//   node tools/index-rules-check.mjs
//
// A port of the arithmetic in SurfaceIndex.cs / Survey.cs / WorldModifiers.cs that the 2026-10-03
// index overhaul introduced, asserted against the numbers the request asked for. There is no Unity on
// the machines this project is often worked on, so this is the closest thing to running the rules:
// if one of the constants in the C# drifts, port it here too and this will say whether the spec
// still holds. Exits 1 on any failure.
// ============================================================================================

let fails = 0, passes = 0;
const ok = (cond, msg) => { if (cond) { passes++; } else { fails++; console.log(`  FAIL  ${msg}`); } };
const near = (a, b, eps = 0.005) => Math.abs(a - b) <= eps;
const lerp = (a, b, t) => a + (b - a) * Math.min(1, Math.max(0, t));
const invLerp = (a, b, v) => (a === b ? 0 : Math.min(1, Math.max(0, (v - a) / (b - a))));
const clamp01 = v => Math.min(1, Math.max(0, v));

// ---- Floors and bands (SurfaceIndex.Floor / Steps / Band) ----------------------------------
const BandStep = 0.10, ShowFloor = 0.70, BaseFloor = 0.40;
const Floor = k => (k === 'Mineral' ? ShowFloor : BaseFloor);
const Steps = k => Math.max(1, Math.round((1 - Floor(k)) / BandStep));
const Band = (k, v) => {
  const f = Floor(k); if (v < f) return 0;
  const s = Steps(k);
  const i = Math.min(s - 1, Math.max(0, Math.floor((v - f) / BandStep + 0.0001)));
  return s > 1 ? i / (s - 1) : 1;
};
console.log('floors and bands');
ok(Floor('Mineral') === 0.70, 'Mineral floor is 70%');
for (const k of ['Geothermal', 'Fertile', 'Wind', 'Solar', 'Water']) ok(Floor(k) === 0.40, `${k} floor is 40%`);
ok(Steps('Mineral') === 3, 'Mineral has 3 bands (70s, 80s, 90s)');
ok(Steps('Water') === 6, 'other indexes have 6 bands (40s..90s)');
ok(Band('Water', 0.40) === 0 && Band('Water', 0.49) === 0, '40-49 is the bottom band');
ok(near(Band('Water', 0.50), 0.2), '50 starts band 2 of 6');
ok(Band('Water', 1.0) === 1 && Band('Water', 0.95) === 1, '90-100 is the top band');
ok(Band('Mineral', 0.69) === 0 && Band('Mineral', 0.85) === 0.5, 'mineral bands sit on 70/80/90');

// ---- Survey passes onto bands (Survey.ResolvedBand) ----------------------------------------
const Bands = 3;
const ResolvedBand = (pass, steps) => Math.trunc((pass + 1) * steps / Bands) - 1;
console.log('survey passes');
ok([-1, 0, 1, 2].map(p => ResolvedBand(p, 3)).join() === '-1,0,1,2', '3-band index: one band per pass');
ok([-1, 0, 1, 2].map(p => ResolvedBand(p, 6)).join() === '-1,1,3,5', '6-band index: two bands per pass');

// ---- Hydro (SurfaceIndex.Water + the chamfer field) -----------------------------------------
console.log('hydro');
const HydroRings = [[0.94, 0.98], [0.90, 0.93], [0.80, 0.89], [0.60, 0.79], [0.50, 0.59], [0.40, 0.49]];
function hydroMap(w, h, isWater) {
  const n = w * h, d = new Float64Array(n).fill(Infinity);
  for (let i = 0; i < n; i++) if (isWater(i % w, Math.floor(i / w))) d[i] = 0;
  const relax = (x, y, nx, ny, c) => {
    if (ny < 0 || ny >= h) return;
    nx = ((nx % w) + w) % w;
    const from = ny * w + nx, to = y * w + x;
    if (d[from] + c < d[to]) d[to] = d[from] + c;
  };
  const D2 = Math.SQRT2;
  for (let pass = 0; pass < 2; pass++) {
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      relax(x, y, x - 1, y, 1); relax(x, y, x, y - 1, 1); relax(x, y, x - 1, y - 1, D2); relax(x, y, x + 1, y - 1, D2);
    }
    for (let y = h - 1; y >= 0; y--) for (let x = w - 1; x >= 0; x--) {
      relax(x, y, x + 1, y, 1); relax(x, y, x, y + 1, 1); relax(x, y, x + 1, y + 1, D2); relax(x, y, x - 1, y + 1, D2);
    }
  }
  return d;
}
const hydroAt = (dist, size, hash) => {
  if (dist === 0) return 1;
  const ring = Math.max(1, Math.round(dist));
  if (ring > 6) return 0;
  const [lo, hi] = HydroRings[ring - 1];
  const q = clamp01(Math.sqrt(size) / 15) * 0.8 + hash * 0.2;
  return lerp(lo, hi, q);
};
{
  // A lake on the left third of a 40x20 map.
  const W = 40, H = 20, d = hydroMap(W, H, x => x < 10);
  const along = x => hydroAt(d[10 * W + x], 200, 0.5);
  ok(along(5) === 1, 'a water tile reads 100%');
  const ranges = [[0.94, 0.98], [0.90, 0.93], [0.80, 0.89], [0.60, 0.79], [0.50, 0.59], [0.40, 0.49]];
  for (let r = 1; r <= 6; r++) {
    const v = along(9 + r);
    ok(v >= ranges[r - 1][0] && v <= ranges[r - 1][1], `tile ${r} from water reads ${(v * 100).toFixed(0)}% (spec ${ranges[r - 1][0] * 100}-${ranges[r - 1][1] * 100})`);
  }
  ok(along(16) === 0, 'tile 7 from water reads nothing');
  // Diagonal neighbour of a single-tile pond counts as ring 1.
  const d2 = hydroMap(9, 9, (x, y) => x === 4 && y === 4);
  ok(Math.max(1, Math.round(d2[5 * 9 + 5])) === 1, 'a diagonal neighbour is 1 tile away');
  // Every land value inside the reach is at or above the 40% floor.
  let minLand = 1;
  for (let i = 0; i < W * H; i++) { const v = hydroAt(d[i], 1, 0); if (v > 0 && v < 1) minLand = Math.min(minLand, v); }
  ok(minLand >= 0.40, `no hydro value falls between 0 and the 40% floor (min ${(minLand * 100).toFixed(0)}%)`);
}

// ---- Solar (SurfaceIndex.SolarRegionMax / SolarSurfaceMax / Solar / SolarDaySide) -------------
console.log('solar');
const HzInnerRel = 0.80, HzOuterRel = 1.55, hzC = (HzInnerRel + HzOuterRel) / 2;
const regionMax = rel => {
  if (rel <= 0.36) return 1.0;
  if (rel <= hzC) return lerp(1.0, 0.55, invLerp(Math.log(0.36), Math.log(hzC), Math.log(rel)));
  if (rel <= HzOuterRel) return lerp(0.55, 0.46, invLerp(hzC, HzOuterRel, rel));
  return 0.46 * Math.pow(HzOuterRel / rel, 1.2);
};
const solar = (rel, atm, metres) => clamp01(Math.max(0, regionMax(rel) - 0.10 * atm) + 0.10 * (metres / 1500));
ok(regionMax(0.36) === 1.0 && regionMax(0.2) === 1.0, 'innermost orbit allows 100%');
ok(regionMax(hzC) >= 0.50 && regionMax(hzC) <= 0.60, `HZ centre max ${(regionMax(hzC) * 100).toFixed(0)}% is in 50-60`);
ok(regionMax(HzOuterRel) >= 0.43 && regionMax(HzOuterRel) <= 0.47, `HZ outer edge max ${(regionMax(HzOuterRel) * 100).toFixed(0)}% is mid-40s`);
ok(regionMax(2.55) < regionMax(HzOuterRel) && regionMax(5.1) < regionMax(2.55), 'beyond the HZ it keeps falling');
let mono = true; for (let r = 0.2; r < 6; r += 0.01) if (regionMax(r + 0.01) > regionMax(r) + 1e-9) mono = false;
ok(mono, 'the maximum never rises with distance');
ok(near(Math.max(0, 0.46 - 0.10 * 1), 0.36), "spec example: 46% max under 1 atm reads 36%");
ok(near(solar(0.36, 0, 0), 1.0), 'airless world at the inner ring reads 100% at the datum');
ok(near(solar(hzC, 1, 1500) - solar(hzC, 1, 0), 0.10), '+1,500 m adds 10 points');
ok(near(solar(hzC, 1, 0) - solar(hzC, 1, -1500), 0.10), '-1,500 m takes 10 points');
ok(solar(1.0, 10, 0) === 0, 'ten atmospheres block all sunlight at the datum');
const daySide = (u, v) => clamp01(Math.cos((v - 0.5) * Math.PI) * Math.cos((u - 0.5) * 2 * Math.PI) * 4);
ok(daySide(0.5, 0.5) === 1, 'tidally locked: the map centre is full day');
ok(daySide(0.0, 0.5) === 0 && daySide(0.9, 0.5) === 0, 'tidally locked: the far side is dark');
let lit = 0; for (let i = 0; i < 100; i++) if (daySide((i + 0.5) / 100, 0.5) > 0) lit++;
ok(lit === 50, `tidally locked: half the equator is lit (${lit}%)`);

// ---- Fertility ceiling (SurfaceIndex.FertileClimateCeiling) ---------------------------------
console.log('fertility');
const fertCeil = (c, water) => {
  const off = Math.max(0, Math.abs(c - 21) - 1);
  return clamp01(lerp(0.55, 0.88, 1 - clamp01(off / 25)) + (water >= 0.40 ? 0.06 : 0));
};
ok(near(fertCeil(21, 0.5), 0.94), 'a 21 C world with water caps at 94% (only a Fertile World reaches 100)');
ok(fertCeil(21, 0.3) < fertCeil(21, 0.5), 'having 40%+ water raises the cap');
ok(fertCeil(40, 0.5) < fertCeil(25, 0.5) && fertCeil(25, 0.5) < fertCeil(21, 0.5), 'the cap falls away from 20-22 C');
ok(fertCeil(-30, 0.1) >= 0.40, 'even a harsh living world can reach the 40% floor');
// Ordinary band curve 1.8 vs Fertile World 0.25: how much of the usable band reads 90+.
const share90 = (ceil, curve) => { let n = 0; for (let i = 0; i < 1000; i++) if (lerp(0.40, ceil, Math.pow(i / 999, curve)) >= 0.90) n++; return n / 10; };
const ord = share90(0.94, 1.8), fw = share90(1.0, 0.25);
ok(ord < 15, `ordinary world: ${ord.toFixed(0)}% of its farmland reads 90+ (rare)`);
ok(fw > 50, `Fertile World: ${fw.toFixed(0)}% of its farmland reads 90+ (abundant)`);

// ---- Fertile World criteria (WorldModifiers.MeetsFertileClimate) -----------------------------
console.log('fertile world criteria');
const meets = (bio, c, water) => bio && c >= 17 && c <= 25 && water >= 0.40 && water <= 0.60;
ok(meets(true, 21, 0.5), '21 C, 50% water, living: qualifies');
ok(!meets(true, 27, 0.5) && !meets(true, 21, 0.65) && !meets(false, 21, 0.5), 'too hot / too wet / sterile: does not');

// ---- Weather storm zones favour the equator (SurfaceIndex.BuildStormField's weighting) ----------
console.log('weather');
const eq = lat => Math.pow(1 - lat, 1.5) * (0.4 + 0.6 * 0.5);
ok(eq(0) > eq(0.5) && eq(0.5) > eq(0.9), 'equal contrast storms harder at the equator than toward the poles');
const air = a => (a <= 0.15 ? 0 : a < 1 ? 0.92 * Math.pow(invLerp(0.15, 1, a), 0.55) : lerp(0.92, 1, invLerp(1, 4, a)));
ok(air(0.1) === 0 && air(0.5) < air(1) && air(1) < air(3), 'thicker air raises the weather ceiling');

// ---- Contour lines fade out when zoomed out (SurfaceTextureRenderer.BuildMips) -----------------
//
// A port of the mip builder on a synthetic map: a 32x32-cell world at 8 texels per cell with a
// contour on every other cell boundary (a steep mountain front — the worst case for smearing).
console.log('contour mips');
{
  const scale = 8, W = 32, tw = W * scale, ContourMinCellTexels = 4, Dark = 0.22;
  let src = new Float64Array(tw * tw).fill(1), mask = new Uint8Array(tw * tw);
  for (let y = 0; y < tw; y++) for (let x = 0; x < tw; x++) {
    const cx = Math.floor(x / scale);
    if (cx % 2 === 0 && x % scale === scale - 1) { src[y * tw + x] = Dark; mask[y * tw + x] = 1; }
  }
  let sw = tw, level = 1;
  const report = [];
  while (sw > 1) {
    if ((scale >> level) < ContourMinCellTexels) mask = null;
    const dw = sw >> 1, dst = new Float64Array(dw * dw), dmask = mask ? new Uint8Array(dw * dw) : null;
    for (let y = 0; y < dw; y++) for (let x = 0; x < dw; x++) {
      const ids = [(2 * y) * sw + 2 * x, (2 * y) * sw + 2 * x + 1, (2 * y + 1) * sw + 2 * x, (2 * y + 1) * sw + 2 * x + 1];
      let s = 0, n = 0;
      if (mask) for (const i of ids) if (mask[i]) { s += src[i]; n++; }
      if (n > 0) dmask[y * dw + x] = 1; else { s = ids.reduce((a, i) => a + src[i], 0); n = 4; }
      dst[y * dw + x] = s / n;
    }
    let darkest = 1, dark = 0;
    for (const v of dst) { darkest = Math.min(darkest, v); if (v < 0.5) dark++; }
    report.push({ level, cell: scale / 2 ** level, darkest, darkShare: dark / dst.length });
    src = dst; mask = dmask; sw = dw; level++;
  }
  const at = l => report[l - 1];
  ok(at(1).darkest < 0.3 && at(1).darkShare <= 0.25, 'zoomed out a little: lines stay solid and thin');
  ok(at(3).darkest > 0.5, `zoomed out far (cell ${at(3).cell} texel): lines faded to ${(at(3).darkest * 100).toFixed(0)}% brightness`);
  ok(at(4).darkest > 0.75, `further out: lines all but gone (${(at(4).darkest * 100).toFixed(0)}%)`);
  ok(report.every(r => r.darkShare <= 0.25), 'no level turns into a dark smear');
  for (const r of report.slice(0, 5))
    console.log(`    level ${r.level}: cell ${r.cell} texels, darkest ${(r.darkest * 100).toFixed(0)}%, dark share ${(r.darkShare * 100).toFixed(0)}%`);
}

console.log(`\n${passes} passed, ${fails} failed`);
process.exit(fails ? 1 : 0);
