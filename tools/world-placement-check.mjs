// ============================================================================================
// DOES EVERY WORLD MAKE SENSE WHERE IT ORBITS? (the 2026-10-10 Mirfak report)
//
//   node tools/world-placement-check.mjs [--seeds 2000]
//
// "Mirfak II, despite being very near to the sun, has an average surface temperature of 35 C. The
// habitability is 60% and there is greenery on this planet... Planets that are predetermined types still
// need to make sense where they are placed within a solar system."
//
// A Node port of the arithmetic behind the fix — InsolationRules (the law and its HeatBand),
// PlacementRings (the ladder and RingForHeat), Habitability.PlacementZone, GalaxyGenerator.CradleHeat
// and the starting-option choice, and WorldConsistency's two rules — run three ways:
//
//   1. THE LAW        starlight falls outward; nothing inside the zone's inner edge can be temperate.
//   2. MIRFAK         the reported system rebuilt from the screenshots: what the OLD pipeline did (home
//                     system generated around a red dwarf, then the G sun swapped in under it) and what
//                     the NEW one does (generated around the G sun from the start), and what the
//                     consistency check makes of the old Mirfak II.
//   3. THE SWEEP      thousands of home systems across species, difficulties and home clusters, OLD
//                     against NEW: start options outside the zone, forced climates their orbit could not
//                     give, cradles on belt rocks, natural worlds whose climate belongs to another star.
//                     Since 2026-10-11 every species' band is ONE rule (Habitability.GetZone: the orbits
//                     where a world of its own kind sits within its temperature tolerance) and every
//                     cradle must land in it at its ideal — Pyrothians on ring 3, Cryithn on ring 5.
//                     Worlds pushed by OrbitSafety after their climate was rolled must be re-derived
//                     (RederiveMoved): the Mirfak Eta II report, which this check used to miss.
//                     tools/habitability-audit.mjs tabulates the bands per species and star class.
//   4. THE RUNAWAY    the Venus-like greenhouse worlds (AtmosphereRules.RollRunawayGreenhouse +
//                     PlanetTemperature.DeepGreenhouseC): how many by ring, how hot, how deep — and none
//                     in or beyond the habitable zone, none with liquid water, none living.
//
// Constants are READ OUT OF THE C# so the port cannot drift from the game; a missing one fails loudly.
// The sweep's ring layout is SIMPLIFIED (which rings fill, and with what, is drawn from plain odds rather
// than the full mass-budget loop — tools/system-composition-check.mjs ports that). What it checks does
// not depend on those odds: it is about where the forced worlds go and what climate they are given.
// Exits 1 on any failure of the NEW pipeline.
// ============================================================================================
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const SEEDS = parseInt(arg('--seeds', '2000'), 10);

const read = f => fs.readFileSync(path.join(PROJ, f), 'utf8');
const STAR = read('Assets/Scripts/Data/StarData.cs');
const SPECIES = read('Assets/Scripts/Data/Species.cs');
const INSO = read('Assets/Scripts/Generation/InsolationRules.cs');
const RINGS = read('Assets/Scripts/Generation/PlacementRings.cs');
const ATMO = read('Assets/Scripts/Generation/AtmosphereRules.cs');
const TEMP = read('Assets/Scripts/Data/PlanetTemperature.cs');
const TERRA = read('Assets/Scripts/Systems/Terraforming.cs');
const SAFE = read('Assets/Scripts/Systems/OrbitSafety.cs');
const CONS = read('Assets/Scripts/Generation/WorldConsistency.cs');
const GAL = read('Assets/Scripts/Generation/GalaxyGenerator.cs');

function num(src, re, what) {
  const m = new RegExp(re).exec(src);
  if (!m) { console.error(`FAIL  could not read ${what} — the constant moved or was renamed.`); process.exit(1); }
  return parseFloat(m[1]);
}

const AU = num(STAR, String.raw`public const float AU = ([\d.]+)f`, 'StarDatabase.AU');
const FLUX_MIN = num(STAR, String.raw`FluxMin = ([\d.]+)f`, 'FluxMin');
const FLUX_MAX = num(STAR, String.raw`FluxMax = ([\d.]+)f`, 'FluxMax');
const HZ_IN = num(STAR, String.raw`HzInnerRel = ([\d.]+)f`, 'HzInnerRel');
const HZ_OUT = num(STAR, String.raw`HzOuterRel = ([\d.]+)f`, 'HzOuterRel');
const STAR_SCALE_MUL = num(STAR, String.raw`s\.visualScale = baseScale \* ([\d.]+)f \* lumFactor`, 'star visualScale multiplier');
const RUNAWAY = num(INSO, String.raw`RunawayGain = ([\d.]+)f`, 'RunawayGain');
const INNER_FLOOR_C = num(INSO, String.raw`InnerFloorC = ([\d.]+)f`, 'InnerFloorC');
const SNOWBALL = num(INSO, String.raw`SnowballGain = ([\d.]+)f`, 'SnowballGain');
const OUTER_CEIL_C = -num(INSO, String.raw`OuterCeilingC = -([\d.]+)f`, 'OuterCeilingC');
const JITTER = num(INSO, String.raw`Jitter = ([\d.]+)f`, 'Jitter');
const REF_K = num(TEMP, String.raw`ReferenceKelvin = ([\d.]+)f`, 'ReferenceKelvin');
const GREENHOUSE = num(TEMP, String.raw`GreenhouseMaxC = ([\d.]+)f`, 'GreenhouseMaxC');
const THICK_REF = num(ATMO, String.raw`ThicknessReference = ([\d.]+)f`, 'ThicknessReference');
const MIN_LIQ = num(TERRA, String.raw`MinLiquidC = ([\d.-]+)f`, 'MinLiquidC');
const MAX_LIQ = num(TERRA, String.raw`MaxLiquidC = ([\d.]+)f`, 'MaxLiquidC');
const STAR_CLEAR = num(SAFE, String.raw`StarClearance = ([\d.]+)f`, 'StarClearance');
const INNER_REACH = num(RINGS, String.raw`InnerBodyReach = ([\d.]+)f`, 'InnerBodyReach');
const INNER_RINGS = num(RINGS, 'InnerRings = ([0-9]+)', 'InnerRings');
const MULTIPLES = (() => {
  const m = /static readonly float\[\] Multiples = \{([^}]+)\}/.exec(RINGS);
  if (!m) { console.error('FAIL  could not read PlacementRings.Multiples'); process.exit(1); }
  return m[1].split(',').map(s => parseFloat(s)).filter(v => !Number.isNaN(v));
})();
const HEAT_SLACK = num(CONS, String.raw`HeatSlack = ([\d.]+)f`, 'WorldConsistency.HeatSlack');
const HAB = read('Assets/Scripts/Data/Habitability.cs');
const ZONE_TOL = num(HAB, String.raw`ZoneTolerance = ([\d.]+)f`, 'Habitability.ZoneTolerance');
const VENTING = num(TEMP, String.raw`HomeworldVentingC = ([\d.]+)f`, 'PlanetTemperature.HomeworldVentingC');
const INTERNAL_MAX = num(TEMP, String.raw`InternalMaxC = ([\d.]+)f`, 'PlanetTemperature.InternalMaxC');
const VENT_SLACK = num(TEMP, String.raw`HomeworldVentingSlackC = ([\d.]+)f`, 'PlanetTemperature.HomeworldVentingSlackC');
const OPTION_SLACK = num(GAL, String.raw`OptionClimateSlack = ([\d.]+)f`, 'GalaxyGenerator.OptionClimateSlack');
const CLASSIFIER = read('Assets/Scripts/Generation/WorldClassifier.cs');
const SCORCHING_REL = num(CLASSIFIER, String.raw`ScorchingRel = ([\d.]+)f`, 'WorldClassifier.ScorchingRel');
const MAGMA_MIN_C = num(CLASSIFIER, String.raw`MagmaMinC = ([\d.]+)f`, 'WorldClassifier.MagmaMinC');
const GH_MIN_MASS = num(ATMO, String.raw`GreenhouseMinMass = ([\d.]+)f`, 'GreenhouseMinMass');
const GH_CHANCE = num(ATMO, String.raw`GreenhouseChance = ([\d.]+)f`, 'GreenhouseChance');
const GH_CHANCE_R1 = num(ATMO, String.raw`GreenhouseChanceRingOne = ([\d.]+)f`, 'GreenhouseChanceRingOne');
const GH_CHANCE_FLOOR = num(ATMO, String.raw`GreenhouseChanceFloor = ([\d.]+)f`, 'GreenhouseChanceFloor');
const GH_INNERMOST = num(ATMO, String.raw`GreenhouseInnermostRel = ([\d.]+)f`, 'GreenhouseInnermostRel');
const GH_ATM_MIN = num(ATMO, String.raw`GreenhouseAtmospheresMin = ([\d.]+)f`, 'GreenhouseAtmospheresMin');
const GH_ATM_MAX = num(ATMO, String.raw`GreenhouseAtmospheresMax = ([\d.]+)f`, 'GreenhouseAtmospheresMax');
const GH_THRESHOLD = num(ATMO, String.raw`GreenhouseThresholdAtmospheres = ([\d.]+)f`, 'GreenhouseThresholdAtmospheres');
const DEEP_FROM = num(TEMP, String.raw`DeepGreenhouseFromAtmospheres = ([\d.]+)f`, 'DeepGreenhouseFromAtmospheres');
const DEEP_FULL = num(TEMP, String.raw`DeepGreenhouseFullAtmospheres = ([\d.]+)f`, 'DeepGreenhouseFullAtmospheres');
const DEEP_MAX = num(TEMP, String.raw`DeepGreenhouseMaxC = ([\d.]+)f`, 'DeepGreenhouseMaxC');
const BOIL_EARTH = num(TERRA, String.raw`BoilingAtEarth = ([\d.]+)f`, 'BoilingAtEarth');
const BOIL_FOUR = num(TERRA, String.raw`BoilingAtFourAtm = ([\d.]+)f`, 'BoilingAtFourAtm');

let fails = 0, passes = 0;
const ok = (cond, msg) => { if (cond) passes++; else { fails++; if (fails <= 25) console.log(`  FAIL  ${msg}`); } };
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const clamp01 = v => clamp(v, 0, 1);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);

// A seeded PRNG, so a run is reproducible and a failing seed can be named.
function mulberry32(a) {
  return () => { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
let rnd = mulberry32(1);
const range = (a, b) => a + rnd() * (b - a);

// ---- species, parsed ------------------------------------------------------------------------
function species(name) {
  const i = SPECIES.indexOf(`name = "${name}"`);
  if (i < 0) throw new Error(`no species ${name}`);
  const block = SPECIES.slice(i, SPECIES.indexOf('};', i));
  const f = (re, d) => { const m = re.exec(block); if (!m) { if (d !== undefined) return d; throw new Error(`${name}: ${re}`); } return parseFloat(m[1]); };
  const affLine = new RegExp(String.raw`SetAff\(\w+, rocky: ([\d.]+)f, ocean: ([\d.]+)f, ice: ([\d.]+)f, volcanic: ([\d.]+)f, barren: ([\d.]+)f`)
    .exec(SPECIES.slice(i));
  const aff = { Rocky: +affLine[1], Ocean: +affLine[2], Ice: +affLine[3], Volcanic: +affLine[4], Barren: +affLine[5] };
  // GalaxyGenerator.BestTypeFor: candidates in this order, strictly-greater wins.
  let best = 'Rocky', bestA = -1;
  // Rocky before Ocean since 2026-10-11, matching Species.BestType (the Sylvans' tie is Rocky).
  for (const t of ['Rocky', 'Ocean', 'Ice', 'Volcanic', 'Barren']) if (aff[t] > bestA) { bestA = aff[t]; best = t; }
  return {
    name, idealTemp: f(/idealTemp = ([\d.]+)f/), tolerance: f(/tolerance = ([\d.]+)f/),
    minAtm: f(/minAtmospheres = ([\d.]+)f/), maxAtm: f(/maxAtmospheres = ([\d.]+)f/),
    cradleMass: f(/cradleMass = ([\d.]+)f/, 1), type: best, aff,
  };
}
const ALL_SPECIES = ['Terrans', 'Aquarii', 'Pyrothians', 'Cryithn', 'Sylvans'].map(species);
const TERRANS = ALL_SPECIES[0];
const wantsLife = s => s.type === 'Rocky' || s.type === 'Ocean';

// ---- PlanetTemperature + InsolationRules -----------------------------------------------------
const typeNudge = t => t === 'Volcanic' ? 90 : t === 'Ice' ? -50 : 0;
const heatForKelvin = k => Math.max(0.01, (Math.max(0, k) / REF_K) ** 2);
const baseC = (heat, thick, type = 'Rocky') => REF_K * Math.sqrt(Math.max(1e-5, heat)) - 273.15 + typeNudge(type) + clamp01(thick) * GREENHOUSE;
const heatForC = (c, thick, type) => heatForKelvin(c + 273.15 - typeNudge(type) - clamp01(thick) * GREENHOUSE);
const baseCWith = (heat, thick, type, internal) => baseC(heat, thick, type) + internal;
const plainK = rel => REF_K / Math.sqrt(Math.max(0.01, rel));
function kelvinAt(rel) {
  if (rel < HZ_IN) return Math.max(plainK(HZ_IN) + (plainK(rel) - plainK(HZ_IN)) * RUNAWAY, INNER_FLOOR_C + 273.15);
  if (rel > HZ_OUT) return Math.min(plainK(HZ_OUT) + (plainK(rel) - plainK(HZ_OUT)) * SNOWBALL, OUTER_CEIL_C + 273.15);
  return plainK(rel);
}
function heatBand(rel) {
  const k = kelvinAt(rel);
  let lo = k * (1 - JITTER), hi = k * (1 + JITTER);
  if (rel < HZ_IN) { lo = Math.max(lo, INNER_FLOOR_C + 273.15); hi = Math.max(hi, INNER_FLOOR_C + 273.15); }
  else if (rel > HZ_OUT) { hi = Math.min(hi, OUTER_CEIL_C + 273.15); lo = Math.min(lo, hi); }
  return { lo: heatForKelvin(lo), hi: heatForKelvin(hi) };
}
function rollHeat(rel) {
  let k = kelvinAt(rel) * range(1 - JITTER, 1 + JITTER);
  if (rel < HZ_IN) k = Math.max(k, INNER_FLOOR_C + 273.15); else if (rel > HZ_OUT) k = Math.min(k, OUTER_CEIL_C + 273.15);
  return heatForKelvin(k);
}
const mismatch = (heat, rel) => { const { lo, hi } = heatBand(rel); return heat < lo ? (lo - heat) / lo : heat > hi ? (heat - hi) / hi : 0; };
const clampToOrbit = (heat, rel) => { const { lo, hi } = heatBand(rel); return clamp(heat, lo, hi); };

// ---- stars and rings -------------------------------------------------------------------------
const CLASSES = { O: [50000, 8.0], B: [2000, 6.1], A: [20, 4.2], F: [4, 3.4], G: [1, 2.9], K: [0.3, 2.4], M: [0.05, 1.9] };
const fluxScale = lum => clamp(Math.pow(Math.max(0.02, lum), 0.30), FLUX_MIN, FLUX_MAX);
function makeStar(cls, lumMul = range(0.45, 1.9)) {
  const [baseLum, scale] = CLASSES[cls];
  const lum = baseLum * lumMul;
  const visualScale = scale * STAR_SCALE_MUL * Math.pow(Math.max(0.001, lum / baseLum), 0.12) * range(0.85, 1.18);
  const ref = fluxScale(lum) * AU;
  return { cls, lum, visualScale, ref, hzInner: HZ_IN * ref, hzOuter: HZ_OUT * ref, hasZone: cls !== 'O' && cls !== 'B', radius: visualScale * 0.5 };
}
// The hottest sun sets the zone (StarDatabase.Recombine); the cluster reaches further than one sun.
function combine(stars) {
  if (stars.length === 1) return stars[0];
  const hot = stars.reduce((a, b) => (CLASSES[a.cls][0] >= CLASSES[b.cls][0] ? a : b));
  return { ...hot, radius: Math.max(...stars.map(s => s.radius)) * 1.8 };
}
const relOf = (star, d) => Math.max(1, d) / Math.max(0.5, star.ref);
function ringRadii(star) {
  const r = Math.max(1, star.ref);
  const out = MULTIPLES.map(m => m * r);
  const need = star.radius + STAR_CLEAR + INNER_REACH;
  if (out[0] >= need) return out;
  const hi = (star.hasZone && star.hzInner > need) ? star.hzInner : out[INNER_RINGS];
  if (hi <= need) { const shift = need - out[0]; return out.map(v => v + shift); }
  for (let i = 0; i < INNER_RINGS; i++) out[i] = need + (hi - need) * ((i + 0.5) / INNER_RINGS);
  return out;
}
function ringForHeat(star, radii, inner, outer, heat) {
  let best = -1, bestMiss = Infinity, bestOff = Infinity;
  radii.forEach((r, i) => {
    if (r < inner || r > outer) return;
    const rel = relOf(star, r), miss = mismatch(heat, rel), { lo, hi } = heatBand(rel), off = Math.abs(heat - (lo + hi) / 2);
    if (miss < bestMiss - 1e-4 || (Math.abs(miss - bestMiss) <= 1e-4 && off < bestOff)) { best = i; bestMiss = miss; bestOff = off; }
  });
  return best;
}

// ---- Habitability.GetZone / PlacementZone / CradleZone + GalaxyGenerator.CradleHeat (2026-10-11) ----
// ONE rule: a species' band is the orbits where a world of its own kind (its cradle's type and air, and
// a volcanic homeworld's venting) sits within ZoneTolerance of Rate's temperature width of its ideal.
const quantize = a => Math.max(0, Math.round(a * 10) / 10);
const cradleAtm = s => quantize(clamp(Math.round(s.cradleMass * 10) / 10, s.minAtm, s.maxAtm));
const cradleThick = s => clamp01(cradleAtm(s) / THICK_REF);
const venting = type => type === 'Volcanic' ? VENTING : 0;
const idealC = s => wantsLife(s) ? lerp(MIN_LIQ + 5, MAX_LIQ - 8, s.idealTemp) : lerp(-150, 450, s.idealTemp);
const widthC = s => 15 * clamp(s.tolerance, 0.6, 1.6) + Math.abs(idealC(s) - 20) * 0.25;
const targetC = s => idealC(s);
const cradleHeat = (s, thick, type, internal = venting(type)) => heatForC(idealC(s) - internal, thick, type);
function firstRelAtOrBelow(k, strictly = false) {
  const below = r => strictly ? kelvinAt(r) < k : kelvinAt(r) <= k;
  const LO = 0.02, HI = 60;
  if (below(LO)) return LO;
  if (!below(HI)) return HI;
  let a = Math.log(LO), b = Math.log(HI);
  for (let i = 0; i < 48; i++) { const m = (a + b) / 2; if (below(Math.exp(m))) b = m; else a = m; }
  return Math.exp(b);
}
function zoneRel(s) {
  const w = widthC(s) * ZONE_TOL, th = cradleThick(s), v = venting(s.type);
  const kHot = REF_K * Math.sqrt(heatForC(idealC(s) + w - v, th, s.type));
  const kCold = REF_K * Math.sqrt(heatForC(idealC(s) - w - v, th, s.type));
  let inner = firstRelAtOrBelow(kHot), outer = firstRelAtOrBelow(kCold, true);
  if (inner > outer) [inner, outer] = [outer, inner];
  return { inner, outer };
}
function speciesZone(star, s) {
  const z = zoneRel(s), r = Math.max(0.5, star.ref);
  return { inner: Math.max(0.5, z.inner * r), outer: z.outer * r };
}
function placementZone(star, s) {
  const sz = speciesZone(star, s);
  let inner = Math.max(star.hzInner, sz.inner), outer = Math.min(star.hzOuter, sz.outer);
  if (inner > outer) { inner = star.hzInner; outer = star.hzOuter; }
  return { inner, outer };
}
// The rule before 2026-10-11, kept for the OLD side of the sweep and the Mirfak rebuild: the star's band
// shifted by idealTemp and scaled by tolerance, and the raw heat curve for furnace and ice cradles.
function placementZoneOld(star, s) {
  const centre = (star.hzInner + star.hzOuter) / 2 * lerp(1.25, 0.80, s.idealTemp);
  const half = (star.hzOuter - star.hzInner) * 0.5 * clamp(s.tolerance, 0.6, 1.4);
  let inner = Math.max(star.hzInner, Math.max(0.5, centre - half * 0.9)), outer = Math.min(star.hzOuter, centre + half * 1.2);
  if (inner > outer) { inner = star.hzInner; outer = star.hzOuter; }
  return { inner, outer };
}
const cradleHeatOld = (s, thick, type) => wantsLife(s) ? heatForC(idealC(s), thick, type) : lerp(0.55, 1.7, s.idealTemp);
const cradleZone = (star, s) => wantsLife(s) ? placementZone(star, s) : speciesZone(star, s);
const inSpeciesZone = (star, s, d) => { const z = speciesZone(star, s); return d >= z.inner && d <= z.outer; };
const inCradleZone = (star, s, d) => { if (inZone(star, d)) return true; const z = cradleZone(star, s); return d >= z.inner && d <= z.outer; };
const inZone = (star, d) => star.hasZone && d >= star.hzInner && d <= star.hzOuter;

// ============================================================================================
// 1. THE LAW
// ============================================================================================
console.log('1. The law');
{
  let pk = Infinity, plo = Infinity, phi = Infinity;
  for (let rel = 0.2; rel <= 6; rel += 0.005) {
    const k = kelvinAt(rel), { lo, hi } = heatBand(rel);
    ok(k <= pk + 1e-6 && lo <= plo + 1e-9 && hi <= phi + 1e-9, `starlight rose outward at rel ${rel.toFixed(3)}`);
    if (rel < HZ_IN) ok(baseC(lo, 0) >= INNER_FLOOR_C - 0.5, `rel ${rel.toFixed(2)} inside the zone could be ${baseC(lo, 0).toFixed(0)} °C`);
    pk = k; plo = lo; phi = hi;
  }
  // The standard ladder never inverts, jitter included: each ring's hottest roll is cooler than the
  // coolest roll of the ring inside it.
  for (let i = 1; i < MULTIPLES.length; i++)
    ok(heatBand(MULTIPLES[i]).hi < heatBand(MULTIPLES[i - 1]).lo, `ring ${i + 1} can roll hotter than ring ${i}`);
  console.log(`   ok   starlight falls outward; inside the inner edge nothing is under ${INNER_FLOOR_C} °C; adjacent standard rings never invert`);
}

// ============================================================================================
// 2. MIRFAK
// ============================================================================================
console.log('\n2. Mirfak, rebuilt from the screenshots');
{
  rnd = mulberry32(20261010);
  // Measured off "Planet out of place.png": the drawn zone's inner edge is 352 px from the star, so a G
  // sun's 32-unit edge is 11 px a unit; Mirfak I's orbit sits at ~12.5 units, II at ~19, III at ~29, the
  // IV belt at ~44. Mirfak II's 35 °C under 0.8 atmospheres is heat 1.11 from starlight — the plain law
  // at 0.90x the Earth-warmth distance of WHATEVER STAR IT WAS CLIMATISED AGAINST.
  const G = makeStar('G', 1); G.radius = 2.9 * STAR_SCALE_MUL * 0.5;
  const M = makeStar('M', 1.9); M.radius = 1.9 * STAR_SCALE_MUL * 0.5;     // the brightest red dwarf the table makes
  const thick08 = 0.8 / THICK_REF;
  const heatII = heatForC(35, thick08, 'Rocky');
  const dII = 19;
  console.log(`   G sun:  zone ${G.hzInner.toFixed(1)}-${G.hzOuter.toFixed(1)}, rings ${ringRadii(G).map(r => r.toFixed(1)).join(' ')}`);
  console.log(`   M dwarf: zone ${M.hzInner.toFixed(1)}-${M.hzOuter.toFixed(1)}, rings ${ringRadii(M).map(r => r.toFixed(1)).join(' ')}`);
  console.log(`   Mirfak II: 35 °C at 0.8 atm is starlight heat ${heatII.toFixed(3)} (${baseC(heatII, 0).toFixed(0)} °C before greenhouse)`);

  const relG = relOf(G, dII), relM = relOf(M, dII);
  const bandG = heatBand(relG), bandM = heatBand(relM);
  console.log(`   at ${dII} units — G sun: rel ${relG.toFixed(2)}, band ${bandG.lo.toFixed(2)}-${bandG.hi.toFixed(2)} (${baseC(bandG.lo, 0).toFixed(0)}-${baseC(bandG.hi, 0).toFixed(0)} °C)`);
  console.log(`                  red dwarf: rel ${relM.toFixed(2)}, band ${bandM.lo.toFixed(2)}-${bandM.hi.toFixed(2)} (${baseC(bandM.lo, 0).toFixed(0)}-${baseC(bandM.hi, 0).toFixed(0)} °C)`);
  ok(mismatch(heatII, relG) > HEAT_SLACK, 'Mirfak II is NOT flagged against the G sun it actually orbits');
  ok(mismatch(heatII, relM) <= HEAT_SLACK, 'Mirfak II\'s climate is not explained by a red dwarf either — the reconstruction is wrong');
  ok(relM >= HZ_IN && relM <= HZ_OUT, 'Mirfak II would not have been in a red dwarf\'s zone');
  console.log('   =>   its 35 °C is exactly what a red dwarf gives its habitable ring, and impossible from the G sun:');
  console.log('        the OLD pipeline generated the home system around a rolled star (45% red dwarfs) and then');
  console.log('        swapped the species\' G/K sun in underneath it (ForceHomeWorld), re-deriving nothing.');

  // What the consistency check makes of it, and what the NEW pipeline puts at that orbit.
  const repaired = [];
  for (let i = 0; i < 400; i++) repaired.push(baseC(rollHeat(relG), 0.1 / THICK_REF));
  repaired.sort((a, b) => a - b);
  ok(repaired[0] > 100, `re-derived Mirfak II can still be ${repaired[0].toFixed(0)} °C`);
  console.log(`   NEW: a world on Mirfak II's orbit of the G sun runs ${repaired[0].toFixed(0)}-${repaired[repaired.length - 1].toFixed(0)} °C from starlight alone`);
  console.log('        — a scorched world (or volcanic, with plates, or — 40% of the time for its mass — a runaway');
  console.log('        greenhouse world, section 4); never liquid water, never a biosphere.');

  // The owner's "fine" cases.
  const ringsG = ringRadii(G);
  const iRel = relOf(G, ringsG[0]);
  console.log(`   NEW: ring 1 (Mirfak I's place) ${baseC(heatBand(iRel).lo, 0).toFixed(0)}-${baseC(heatBand(iRel).hi, 0).toFixed(0)} °C before greenhouse, `
    + `+90 and up to +620 internal for a volcanic world: still molten.`);
  const r4 = relOf(G, ringsG[3]);
  const hot4 = baseC(heatBand(r4).hi, 2 / THICK_REF);
  ok(hot4 > 40 && hot4 < MAX_LIQ + 10, `ring 4 cannot hold a hot desert (${hot4.toFixed(0)} °C)`);
  console.log(`   NEW: ring 4, just inside the zone, reaches ${hot4.toFixed(0)} °C under 2 atmospheres — a hot desert like Mirfak III stays possible there.`);
  const relIII = relOf(G, 29);
  console.log(`        (Mirfak III measures ~29 units, rel ${relIII.toFixed(2)}: ${relIII < HZ_IN ? 'just INSIDE the inner edge, so on the G sun it would be scorched' : 'inside the zone'} — see the report.)`);

  // And the cradle: a belt rock nearest the zone centre.
  const z = placementZoneOld(G, TERRANS), zn = cradleZone(G, TERRANS);
  console.log(`   OLD cradle = the body nearest the zone centre ${((z.inner + z.outer) / 2).toFixed(1)}: the Mirfak IV belt at ~44 — a belt rock (Mirfak IV a).`);
  const ring = ringForHeat(G, ringsG, zn.inner, zn.outer, cradleHeat(TERRANS, cradleThick(TERRANS), TERRANS.type));
  ok(ring === 3, `Terran cradle ring should be 4, got ${ring + 1}`);
  console.log(`   NEW cradle = the non-belt world nearest ring ${ring + 1} (${ringsG[ring].toFixed(1)} units), whose starlight gives a Terran its ${targetC(TERRANS).toFixed(1)} °C unclamped.`);
}

// ============================================================================================
// 3. THE SWEEP — OLD against NEW
// ============================================================================================
console.log(`\n3. ${SEEDS} home systems per species x difficulty, OLD against NEW`);
const DIFFS = [{ name: 'Easy', chance: 0.85 }, { name: 'Medium', chance: 0.5 }, { name: 'Hard', chance: 0 }];
const ROLL = () => { const r = rnd(); return r < 0.45 ? 'M' : r < 0.65 ? 'K' : r < 0.80 ? 'G' : r < 0.90 ? 'F' : r < 0.96 ? 'A' : r < 0.99 ? 'B' : 'O'; };

// A simplified home-system layout around `star`: rings filled by plain odds, the first in-zone ring
// always a terrestrial, a belt possible after three planets, giants past the frost line. Every natural
// body's heat is rolled from its orbit, exactly as SolarSystemGenerator.BiasHeat does.
function layout(star) {
  const radii = ringRadii(star);
  const habitable = radii.findIndex(r => inZone(star, r));
  const bodies = []; let planets = 0, belts = 0;
  radii.forEach((r, i) => {
    if (i !== habitable && rnd() > 0.55) return;
    const rel = relOf(star, r);
    const belt = i !== habitable && planets >= 3 && belts === 0 && rnd() < 0.25;
    const giant = !belt && i !== habitable && rel > 2.2 && rnd() < 0.7;
    const n = belt ? 3 + Math.floor(rnd() * 4) : 1;
    for (let k = 0; k < n; k++) {
      const temperate = rel >= HZ_IN && rel <= HZ_OUT;
      bodies.push({ d: r, heat: rollHeat(rel), belt, giant, mass: giant ? 20 : range(0.6, 2.4),
        moons: giant || rnd() < 0.38 ? [{ mass: range(0.2, 0.6) }] : [],
        temperateType: !belt && !giant && temperate && rnd() < 0.6 });
    }
    if (belt) belts++; else planets++;
  });
  // A zoneless star (O/B) forces no ring, and the plain odds can leave it empty: give it one world.
  if (!bodies.length) bodies.push({ d: radii[4], heat: rollHeat(relOf(star, radii[4])), belt: false, giant: false, mass: 1, moons: [], temperateType: false });
  return bodies;
}

const tally = { old: {}, neu: {} };
const clampedTo = {};
const innerCradle = {};   // species whose cradle went inside the star's inner edge: rings and climate reached
const bump = (side, key) => { tally[side][key] = (tally[side][key] || 0) + 1; };

for (const s of ALL_SPECIES)
  for (const diff of DIFFS)
    for (let seed = 1; seed <= SEEDS; seed++) {
      rnd = mulberry32(seed * 7919 + ALL_SPECIES.indexOf(s) * 104729 + DIFFS.indexOf(diff) * 15485863);
      const n = (() => { const c = rnd(); return c < 0.04 ? 3 : c < 0.14 ? 2 : 1; })();
      const home = combine(Array.from({ length: n }, (_, i) => makeStar(i === 0 && s.name === 'Terrans' ? 'G' : (rnd() < 0.5 ? 'G' : 'K'))));
      const z = placementZoneOld(home, s);
      const zc = cradleZone(home, s);
      const thick = cradleThick(s);
      const want = cradleHeat(s, thick, s.type);

      // ---- OLD: generated around a rolled star, the home sun swapped in afterwards ----
      {
        const orig = makeStar(ROLL());
        const bodies = layout(orig);
        // Natural worlds keep the climate of the star they were made for.
        for (const b of bodies) {
          const rel = relOf(home, b.d);
          if (mismatch(b.heat, rel) > HEAT_SLACK) bump('old', 'natural world with another star\'s climate');
          if (b.temperateType && !inZone(home, b.d) && relOf(orig, b.d) <= HZ_OUT) bump('old', 'temperate world outside the zone');
        }
        // Cradle: nearest the zone centre, belts included; moved in only if outside; climate painted.
        const centre = (z.inner + z.outer) / 2;
        let cradle = bodies.reduce((a, b) => (Math.abs(b.d - centre) < Math.abs(a.d - centre) ? b : a), bodies[0]);
        if (cradle.d < z.inner || cradle.d > z.outer) {
          const rings = ringRadii(home).filter(r => r >= z.inner && r <= z.outer);
          if (rings.length) cradle.d = rings.reduce((a, r) => (Math.abs(r - centre) < Math.abs(a - centre) ? r : a));
        }
        if (cradle.belt) bump('old', 'cradle is a belt rock');
        if (mismatch(cradleHeatOld(s, thick, s.type), relOf(home, cradle.d)) > HEAT_SLACK) bump('old', 'cradle climate its orbit cannot give');
        if (!inZone(home, cradle.d)) bump('old', 'start option outside the zone');
      }

      // ---- NEW: generated around the home sun; cradle on its climate's ring; climates clamped ----
      {
        const bodies = layout(home);
        for (const b of bodies) ok(mismatch(b.heat, relOf(home, b.d)) <= HEAT_SLACK, `${s.name} ${diff.name} seed ${seed}: natural heat off its orbit`);

        // ORBITSAFETY PUSHES (2026-10-11, the Mirfak Eta II report). EnforceSystem walks the system
        // outward after every climate is rolled and pushes crowded worlds out; a pushed world used to keep
        // the heat of the radius it was rolled at. Modelled as a share of worlds pushed out by 2-35%, then
        // SolarSystemGenerator.RederiveMoved (a natural world re-rolls from its new orbit). The count of
        // worlds that would have been wrong WITHOUT the re-derive is reported on the OLD side; after it,
        // every world must match its final orbit — the assertion this check lacked while the bug lived.
        for (const b of bodies) {
          if (rnd() >= 0.12) continue;
          b.d *= range(1.02, 1.35);
          if (mismatch(b.heat, relOf(home, b.d)) > HEAT_SLACK) bump('old', 'world pushed by OrbitSafety keeps the climate of its old orbit');
          b.heat = rollHeat(relOf(home, b.d));                       // RederiveMoved -> ReconcileClimate
        }
        for (const b of bodies) ok(mismatch(b.heat, relOf(home, b.d)) <= HEAT_SLACK, `${s.name} ${diff.name} seed ${seed}: a pushed world's heat is off its final orbit`);

        const radii = ringRadii(home);
        const ring = ringForHeat(home, radii, zc.inner, zc.outer, want);
        const wantD = ring >= 0 ? radii[ring] : (zc.inner + zc.outer) / 2;
        let cradle = null;
        for (const b of bodies) if (!b.belt && (!cradle || Math.abs(b.d - wantD) < Math.abs(cradle.d - wantD))) cradle = b;
        if (ring >= 0 && Math.abs(cradle.d - wantD) > 0.5) cradle.d = wantD;   // MoveCradleIntoZone (free ring or swap)
        if (cradle.d < zc.inner || cradle.d > zc.outer) cradle.d = wantD;
        cradle.belt = false;

        // The cradle's climate, as ForceHomeWorld builds it: a volcanic cradle's venting is re-rolled to
        // what its orbit needs (TameHomeworldVents — modelled as landing anywhere within the slack), and
        // its starlight is solved for whatever is left of the ideal, then clamped to the orbit.
        const rel = relOf(home, cradle.d);
        let internal = 0;
        if (venting(s.type) > 0) {
          const { lo, hi } = heatBand(rel);
          internal = clamp(idealC(s) - baseC((lo + hi) / 2, thick, s.type), 0, INTERNAL_MAX)
                   + range(-VENT_SLACK, VENT_SLACK);
          internal = clamp(internal, 0, INTERNAL_MAX);
        }
        cradle.heat = clampToOrbit(cradleHeat(s, thick, s.type, internal), rel);
        const cradleC = baseCWith(cradle.heat, thick, s.type, internal);

        const options = [cradle];
        // AddSecondOption: a planet of its band whose orbit admits the species' climate, else its moon,
        // else a cradle moon (which shares the cradle's orbit).
        if (diff.chance > 0 && rnd() < diff.chance) {
          let host = null, best = Infinity;
          for (const b of bodies) {
            if (b === cradle || b.belt || b.d < zc.inner || b.d > zc.outer) continue;
            const miss = mismatch(cradleHeat(s, thick, s.type), relOf(home, b.d));
            if (miss <= OPTION_SLACK && miss < best) { best = miss; host = b; }
          }
          const pick = host && (!host.giant || host.moons.length) ? { d: host.d } : { d: cradle.d };
          { pick.heat = clampToOrbit(cradleHeat(s, thick, s.type), relOf(home, pick.d)); options.push(pick); }
        }

        // ONE RULE, every species: the cradle is in the band species mode draws, and at its ideal within
        // its tolerance. (A ladder compressed by a big multi-star clearance can leave no ring of a narrow
        // band; then the cradle takes the nearest and is counted, not failed.)
        const inBand = inSpeciesZone(home, s, cradle.d);
        const offC = Math.abs(cradleC - idealC(s)), tol = widthC(s) * ZONE_TOL;
        const c = innerCradle[s.name] || (innerCradle[s.name] = { lo: Infinity, hi: -Infinity, ideal: idealC(s), tol, rings: {}, type: s.type });
        if (inBand && offC <= tol) {
          c.lo = Math.min(c.lo, cradleC); c.hi = Math.max(c.hi, cradleC); c.rings[ring + 1] = (c.rings[ring + 1] || 0) + 1;
        } else {
          ok(ring < 0 || !radii.some(r => inSpeciesZone(home, s, r) && r >= zc.inner && r <= zc.outer && mismatch(want, relOf(home, r)) < mismatch(want, relOf(home, radii[ring])) - 1e-4),
            `${s.name} ${diff.name} seed ${seed}: cradle missed a ring of its band nearer its ideal`);
          const key = `${s.name} cradle: no ring of its band gives its ideal (compressed multi-star ladder); nearest ring taken`;
          bump('neu', key);
          const k = clampedTo[key] || (clampedTo[key] = { lo: Infinity, hi: -Infinity });
          k.lo = Math.min(k.lo, cradleC); k.hi = Math.max(k.hi, cradleC);
        }

        // ...and a push after ForceHomeWorld (its own EnforceSystem) clamps a forced world to the orbit
        // it ends on, never leaving it off its band.
        for (const o of options) {
          if (rnd() < 0.05) { o.d *= range(1.0, 1.08); o.heat = clampToOrbit(o.heat, relOf(home, o.d)); }
          ok(home.hasZone && inCradleZone(home, s, o.d) || !inBand, `${s.name} ${diff.name} seed ${seed}: start option at ${o.d.toFixed(1)} outside ${home.hzInner.toFixed(1)}-${home.hzOuter.toFixed(1)} and its cradle band`);
          if (wantsLife(s)) ok(inZone(home, o.d) || !inBand, `${s.name} ${diff.name} seed ${seed}: a living start option outside the star's zone`);
          ok(mismatch(o.heat, relOf(home, o.d)) <= HEAT_SLACK, `${s.name} ${diff.name} seed ${seed}: start option climate off its orbit`);
        }
        ok(!cradle.belt, `${s.name} ${diff.name} seed ${seed}: cradle is a belt rock`);
      }
    }

const total = SEEDS * ALL_SPECIES.length * DIFFS.length;
console.log(`   OLD (per ${total} home systems):`);
for (const [k, v] of Object.entries(tally.old)) console.log(`        ${String(v).padStart(6)}  ${k}`);
console.log('   NEW:');
for (const [k, v] of Object.entries(tally.neu)) console.log(`        ${String(v).padStart(6)}  ${k} (${clampedTo[k].lo.toFixed(0)}-${clampedTo[k].hi.toFixed(0)} °C)`);
for (const [name, c] of Object.entries(innerCradle)) {
  const rings = Object.entries(c.rings).map(([r, n]) => `ring ${r} x${n}`).join(', ');
  console.log(`   ok   ${name.padEnd(10)} ${c.type.padEnd(8)} cradle in its own band at ${c.lo.toFixed(0)}-${c.hi.toFixed(0)} °C `
    + `(ideal ${c.ideal.toFixed(0)} ±${c.tol.toFixed(0)}): ${rings}`);
}
console.log(`        every start option in its band, every forced climate inside its orbit's band, no belt cradles,`);
console.log(`        every world pushed by orbit spacing re-derived for the orbit it ended on`);

// ============================================================================================
// 4. THE RUNAWAY — Venus-like worlds, by ring
// ============================================================================================
// AtmosphereRules.RunawayGreenhouseChance / RollRunawayGreenhouse and PlanetTemperature.DeepGreenhouseC,
// ported. Every terrestrial (mass 0.6-2.4 — the sweep's simplified odds; the real budget loop is
// tools/system-composition-check.mjs) on every ring of every star class rolls exactly as
// ApplyWorldPipeline does: heat from its orbit, then the runaway chance. A runaway world is a barren
// world (its plates seize), so its temperature carries no type nudge and no internal heat.
console.log(`\n4. The runaway greenhouse — ${SEEDS * 10} systems per star class`);
{
  const ghChance = (mass, rel) => {
    if (mass < GH_MIN_MASS || rel >= HZ_IN) return 0;
    if (rel > SCORCHING_REL) return GH_CHANCE;
    return lerp(GH_CHANCE_FLOOR, GH_CHANCE_R1, (rel - GH_INNERMOST) / (SCORCHING_REL - GH_INNERMOST));
  };
  const deepC = atm => atm <= DEEP_FROM ? 0 : clamp01(Math.log(atm / DEEP_FROM) / Math.log(DEEP_FULL / DEEP_FROM)) * DEEP_MAX;
  const boilC = atm => BOIL_EARTH + (BOIL_FOUR - BOIL_EARTH) * Math.log(Math.max(0.01, atm)) / Math.log(4);
  const byRing = Array.from({ length: MULTIPLES.length }, () => ({ worlds: 0, gh: 0, lo: Infinity, hi: -Infinity, aLo: Infinity, aHi: -Infinity }));
  let ghTotal = 0;
  rnd = mulberry32(4242);
  for (const cls of ['G', 'K', 'M', 'F', 'A'])
    for (let n = 0; n < SEEDS * 10; n++) {
      const star = makeStar(cls);
      ringRadii(star).forEach((r, i) => {
        const rel = relOf(star, r), mass = range(0.6, 2.4);
        const heat = rollHeat(rel);
        const slot = byRing[i];
        slot.worlds++;
        const chance = ghChance(mass, rel);
        if (rel >= HZ_IN) ok(chance === 0, `a ${cls} star's ring ${i + 1} (rel ${rel.toFixed(2)}, in or beyond the zone) can run away`);
        if (!(chance > 0 && rnd() < chance)) return;
        const atm = quantize(range(GH_ATM_MIN, GH_ATM_MAX));
        const c = baseC(heat, atm / THICK_REF, 'Barren') + deepC(atm);
        ghTotal++; slot.gh++;
        slot.lo = Math.min(slot.lo, c); slot.hi = Math.max(slot.hi, c);
        slot.aLo = Math.min(slot.aLo, atm); slot.aHi = Math.max(slot.aHi, atm);
        ok(rel < HZ_IN && !inZone(star, r), `a runaway on ring ${i + 1} of a ${cls} star sits in or beyond the zone`);
        ok(atm >= GH_THRESHOLD, `a runaway of ${atm} atm would not count as one (threshold ${GH_THRESHOLD})`);
        ok(c > boilC(atm) + 30, `a runaway on ring ${i + 1} at ${c.toFixed(0)} °C could hold liquid water under ${atm} atm (boils at ${boilC(atm).toFixed(0)})`);
        ok(c > MAX_LIQ, `a runaway on ring ${i + 1} is in life's comfort band (${c.toFixed(0)} °C)`);
        ok(c < MAGMA_MIN_C, `a runaway on ring ${i + 1} reaches the magma line (${c.toFixed(0)} °C) without internal heat`);
      });
    }
  console.log('   ring  worlds  greenhouse   share   surface °C     atmospheres');
  byRing.forEach((r, i) => {
    const share = r.worlds ? (100 * r.gh / r.worlds).toFixed(1) + '%' : '-';
    const temps = r.gh ? `${r.lo.toFixed(0)}-${r.hi.toFixed(0)}` : '-';
    const atms = r.gh ? `${r.aLo.toFixed(0)}-${r.aHi.toFixed(0)}` : '-';
    console.log(`   ${String(i + 1).padStart(4)}  ${String(r.worlds).padStart(6)}  ${String(r.gh).padStart(10)}  ${share.padStart(6)}   ${temps.padEnd(13)}  ${atms}`);
  });
  for (let i = PlacementRings_INNER(); i < byRing.length; i++) ok(byRing[i].gh === 0, `ring ${i + 1} grew ${byRing[i].gh} runaway worlds`);
  for (let i = 1; i < PlacementRings_INNER(); i++) {
    const share = byRing[i].gh / Math.max(1, byRing[i].worlds);
    ok(share > 0.25 && share < 0.5, `ring ${i + 1}'s runaway share ${(100 * share).toFixed(0)}% is outside the 25-50% asked for`);
  }
  ok(byRing[0].gh / Math.max(1, byRing[0].worlds) < byRing[1].gh / Math.max(1, byRing[1].worlds), 'ring 1 runs away as often as ring 2');
  console.log(`   ok   ${ghTotal} runaway worlds, every one inside the inner edge, over its boiling point, sterile, under the magma line;`);
  console.log(`        none on rings ${PlacementRings_INNER() + 1}-${MULTIPLES.length} (in or beyond the zone)`);
}
function PlacementRings_INNER() { return INNER_RINGS; }

console.log(`\n${passes} passed, ${fails} failed`);
process.exit(fails ? 1 : 0);
