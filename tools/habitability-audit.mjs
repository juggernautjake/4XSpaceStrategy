// ============================================================================================
// HABITABILITY AUDIT — every species around every star class, BEFORE and AFTER the one rule
//
//   node tools/habitability-audit.mjs [--before] [--after]      (both by default)
//
// "Please make sure the habitability zones make sense from a game play perspective. They should be
// consistent and star and race dependent. Please think through what would be a consistent way of
// generating different planets, especially homeworlds for different races and make it work."
//
// For each of the five species and each star class this prints where the species' band falls (in
// multiples of the star's Earth-warmth distance R, and in map units), which placement rings it covers,
// where the cradle lands (ring, °C), its type, and the rating it scores for its own species
// (Habitability.Rate, ported) — first under the rule that shipped before 2026-10-11, then under the rule
// that replaced it:
//
//   BEFORE  GetZone = the star's band shifted ±25% by idealTemp and scaled by tolerance — a POSITIONAL
//           shade with no temperature in it. The cradle went on the in-zone ring nearest its heat; a
//           Pyrothian or Cryithn cradle's heat was the raw Lerp(0.55, 1.7, idealTemp) curve, not their
//           ideal temperature; a volcanic cradle's internal heat (0-620 °C) was never accounted for.
//   AFTER   ONE rule (Habitability.GetZone): a species' zone is the set of orbits where a world of ITS
//           kind — its homeworld's type, air and venting — would sit within its temperature tolerance
//           (the same width Habitability.Rate scores with). Hot-lovers land closer in, cold-lovers
//           further out, dim stars pull every zone inward, and the cradle is built at the species'
//           ideal on the ring of that zone whose starlight gives it.
//
// Constants are READ OUT OF THE C# (a missing one fails loudly). The AFTER model must reproduce what
// the C# now does; the BEFORE model is frozen here as the record of what was replaced. Exits 1 if any
// AFTER cradle is outside its own band, misses its ideal by more than its tolerance allows, or rates
// under the "excellent" line for its own species.
// ============================================================================================
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const SHOW_BEFORE = argv.includes('--before') || !argv.includes('--after');
const SHOW_AFTER = argv.includes('--after') || !argv.includes('--before');
const read = f => fs.readFileSync(path.join(PROJ, f), 'utf8');
const STAR = read('Assets/Scripts/Data/StarData.cs');
const SPECIES = read('Assets/Scripts/Data/Species.cs');
const INSO = read('Assets/Scripts/Generation/InsolationRules.cs');
const RINGS = read('Assets/Scripts/Generation/PlacementRings.cs');
const ATMO = read('Assets/Scripts/Generation/AtmosphereRules.cs');
const TEMP = read('Assets/Scripts/Data/PlanetTemperature.cs');
const TERRA = read('Assets/Scripts/Systems/Terraforming.cs');
const SAFE = read('Assets/Scripts/Systems/OrbitSafety.cs');
const HAB = read('Assets/Scripts/Data/Habitability.cs');

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
const VENTING = num(TEMP, String.raw`HomeworldVentingC = ([\d.]+)f`, 'PlanetTemperature.HomeworldVentingC');
const VENT_SLACK = num(TEMP, String.raw`HomeworldVentingSlackC = ([\d.]+)f`, 'PlanetTemperature.HomeworldVentingSlackC');
const INTERNAL_MAX = num(TEMP, String.raw`InternalMaxC = ([\d.]+)f`, 'InternalMaxC');
const THICK_REF = num(ATMO, String.raw`ThicknessReference = ([\d.]+)f`, 'ThicknessReference');
const LIFE_FLOOR = num(ATMO, String.raw`LifeFloor = ([\d.]+)f`, 'LifeFloor');
const MIN_LIQ = num(TERRA, String.raw`MinLiquidC = ([\d.-]+)f`, 'MinLiquidC');
const MAX_LIQ = num(TERRA, String.raw`MaxLiquidC = ([\d.]+)f`, 'MaxLiquidC');
const BOIL_EARTH = num(TERRA, String.raw`BoilingAtEarth = ([\d.]+)f`, 'BoilingAtEarth');
const BOIL_FOUR = num(TERRA, String.raw`BoilingAtFourAtm = ([\d.]+)f`, 'BoilingAtFourAtm');
const STAR_CLEAR = num(SAFE, String.raw`StarClearance = ([\d.]+)f`, 'StarClearance');
const INNER_REACH = num(RINGS, String.raw`InnerBodyReach = ([\d.]+)f`, 'InnerBodyReach');
const INNER_RINGS = num(RINGS, 'InnerRings = ([0-9]+)', 'InnerRings');
const ZONE_TOL = num(HAB, String.raw`ZoneTolerance = ([\d.]+)f`, 'Habitability.ZoneTolerance');
const EXCELLENT = num(HAB, String.raw`ExcellentRating = ([\d.]+)f`, 'Habitability.ExcellentRating');
const MULTIPLES = (() => {
  const m = /static readonly float\[\] Multiples = \{([^}]+)\}/.exec(RINGS);
  if (!m) { console.error('FAIL  could not read PlacementRings.Multiples'); process.exit(1); }
  return m[1].split(',').map(s => parseFloat(s)).filter(v => !Number.isNaN(v));
})();

let fails = 0;
const fail = msg => { fails++; console.log(`  FAIL  ${msg}`); };
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const clamp01 = v => clamp(v, 0, 1);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const quantize = a => Math.max(0, Math.round(a * 10) / 10);

// ---- species --------------------------------------------------------------------------------
const TYPES = ['Rocky', 'Ocean', 'Ice', 'Volcanic', 'Barren'];
function species(name) {
  const i = SPECIES.indexOf(`name = "${name}"`);
  const block = SPECIES.slice(i, SPECIES.indexOf('};', i));
  const f = (re, d) => { const m = re.exec(block); if (!m) { if (d !== undefined) return d; throw new Error(`${name}: ${re}`); } return parseFloat(m[1]); };
  const a = new RegExp(String.raw`SetAff\(\w+, rocky: ([\d.]+)f, ocean: ([\d.]+)f, ice: ([\d.]+)f, volcanic: ([\d.]+)f, barren: ([\d.]+)f`).exec(SPECIES.slice(i));
  const aff = { Rocky: +a[1], Ocean: +a[2], Ice: +a[3], Volcanic: +a[4], Barren: +a[5] };
  const best = order => { let b = 'Rocky', v = -1; for (const t of order) if (aff[t] > v) { v = aff[t]; b = t; } return b; };
  return {
    name, idealTemp: f(/idealTemp = ([\d.]+)f/), tolerance: f(/tolerance = ([\d.]+)f/),
    minAtm: f(/minAtmospheres = ([\d.]+)f/), maxAtm: f(/maxAtmospheres = ([\d.]+)f/),
    cradleMass: f(/cradleMass = ([\d.]+)f/, 1), aff,
    // GalaxyGenerator.BestTypeFor. BEFORE: Ocean listed first, so the Sylvans' 0.9/0.9 tie went to Ocean
    // while Species.BestType (the remodelling target) said Rocky. AFTER: Rocky first, agreeing with it.
    typeBefore: best(['Ocean', 'Rocky', 'Ice', 'Volcanic', 'Barren']),
    typeAfter: best(['Rocky', 'Ocean', 'Ice', 'Volcanic', 'Barren']),
  };
}
const ALL = ['Terrans', 'Aquarii', 'Pyrothians', 'Cryithn', 'Sylvans'].map(species);
const wantsLife = t => t === 'Rocky' || t === 'Ocean';

// ---- temperature ----------------------------------------------------------------------------
const nudge = t => t === 'Volcanic' ? 90 : t === 'Ice' ? -50 : 0;
const heatForKelvin = k => Math.max(0.01, (Math.max(0, k) / REF_K) ** 2);
const starlightC = heat => REF_K * Math.sqrt(Math.max(1e-5, heat)) - 273.15;
const tempC = (heat, atm, type, internal = 0) => starlightC(heat) + nudge(type) + clamp01(atm / THICK_REF) * GREENHOUSE + internal;
const heatForC = (c, atm, type, internal = 0) => heatForKelvin(c + 273.15 - nudge(type) - clamp01(atm / THICK_REF) * GREENHOUSE - internal);
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
const mismatch = (heat, rel) => { const { lo, hi } = heatBand(rel); return heat < lo ? (lo - heat) / lo : heat > hi ? (heat - hi) / hi : 0; };
const clampToOrbit = (heat, rel) => { const { lo, hi } = heatBand(rel); return clamp(heat, lo, hi); };
const boilC = atm => atm < LIFE_FLOOR ? -270 : BOIL_EARTH + (BOIL_FOUR - BOIL_EARTH) * Math.log(Math.max(0.01, atm)) / Math.log(4);
const freezeC = atm => atm < LIFE_FLOOR ? -270 : clamp(1 + (0 - 1) * (atm - 1) / 3, -8, 4);

// ---- stars and rings (nominal luminosity, nominal size) -------------------------------------
const CLASSES = { A: [20, 4.2], F: [4, 3.4], G: [1, 2.9], K: [0.3, 2.4], M: [0.05, 1.9] };
const fluxScale = lum => clamp(Math.pow(Math.max(0.02, lum), 0.30), FLUX_MIN, FLUX_MAX);
function makeStar(cls) {
  const [lum, scale] = CLASSES[cls];
  const ref = fluxScale(lum) * AU;
  return { cls, ref, hzInner: HZ_IN * ref, hzOuter: HZ_OUT * ref, radius: scale * STAR_SCALE_MUL * 0.5 };
}
const relOf = (star, d) => Math.max(1, d) / Math.max(0.5, star.ref);
function ringRadii(star) {
  const r = Math.max(1, star.ref);
  const out = MULTIPLES.map(m => m * r);
  const need = star.radius + STAR_CLEAR + INNER_REACH;
  if (out[0] >= need) return out;
  const hi = star.hzInner > need ? star.hzInner : out[INNER_RINGS];
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

// ---- Habitability.Rate, ported ----------------------------------------------------------------
const cradleAir = s => quantize(clamp(Math.round(s.cradleMass * 10) / 10, s.minAtm, s.maxAtm));
const idealC = (s, type) => wantsLife(type) ? lerp(MIN_LIQ + 5, MAX_LIQ - 8, s.idealTemp) : lerp(-150, 450, s.idealTemp);
const widthC = (s, ideal) => 15 * clamp(s.tolerance, 0.6, 1.6) + Math.abs(ideal - 20) * 0.25;
function rate(s, type, c, atm, mass) {
  const ideal = idealC(s, type);
  const off = Math.abs(c - ideal) / widthC(s, ideal);
  const temp = Math.exp(-0.7 * off * off);
  const suit = atm >= s.minAtm && atm <= s.maxAtm ? 1 : clamp01(1 - (atm < s.minAtm ? s.minAtm - atm : atm - s.maxAtm) / (Math.max(0.35, s.tolerance) * 1.5));
  const air = lerp(0.25, 1, suit);
  const life = wantsLife(type);
  const liquid = c >= freezeC(atm) && c <= boilC(atm) && boilC(atm) > freezeC(atm);
  const biosphere = life && liquid && c >= MIN_LIQ && c <= MAX_LIQ && atm >= LIFE_FLOOR;
  const water = life && s.aff.Ocean >= 0.3 && !liquid ? 0.45 : 1;
  const lifeK = life && s.aff.Rocky >= 0.45 && !biosphere ? 0.65 : 1;
  const home = Math.max(0.2, s.cradleMass);
  let gravity = 1;
  if (mass < home * 0.6) gravity = lerp(0.55, 1, mass / (home * 0.6));
  else if (mass > home * 1.8) gravity = lerp(1, 0.7, (mass - home * 1.8) / (home * 1.5));
  const kind = lerp(0.6, 1, s.aff[type]);
  return clamp(100 * temp * air * water * lifeK * gravity * kind, 0, 100);
}

// ---- BEFORE: the rule that shipped until 2026-10-11 --------------------------------------------
function zoneBefore(star, s) {
  const centre = (star.hzInner + star.hzOuter) / 2 * lerp(1.25, 0.80, s.idealTemp);
  const half = (star.hzOuter - star.hzInner) * 0.5 * clamp(s.tolerance, 0.6, 1.4);
  return { inner: Math.max(0.5, centre - half * 0.9), outer: centre + half * 1.2 };
}
function cradleBefore(star, s) {
  const type = s.typeBefore, atm = cradleAir(s);
  const sz = zoneBefore(star, s);
  let inner = Math.max(star.hzInner, sz.inner), outer = Math.min(star.hzOuter, sz.outer);
  if (inner > outer) { inner = star.hzInner; outer = star.hzOuter; }
  const want = wantsLife(type) ? heatForC(idealC(s, type), atm, type) : lerp(0.55, 1.7, s.idealTemp);
  const radii = ringRadii(star);
  const ring = ringForHeat(star, radii, inner, outer, want);
  const heat = clampToOrbit(want, relOf(star, radii[ring]));
  return { zone: sz, ring, type, atm, heat, c: tempC(heat, atm, type), radii,
           // a volcanic cradle's venting was whatever its seed and plates gave: 0 to InternalMaxC
           internalSpan: type === 'Volcanic' ? [0, INTERNAL_MAX] : [0, 0] };
}

// ---- AFTER: the one rule ---------------------------------------------------------------------
// The world a species' zone is judged for: its homeworld's type and air, and — for a volcanic kind —
// the moderate planet-wide venting a homeworld is built with (PlanetTemperature.HomeworldVentingC).
const ventingFor = type => type === 'Volcanic' ? VENTING : 0;
function zoneRelAfter(s) {
  const type = s.typeAfter, atm = cradleAir(s), ideal = idealC(s, type), w = widthC(s, ideal) * ZONE_TOL;
  const kAt = c => c + 273.15 - nudge(type) - clamp01(atm / THICK_REF) * GREENHOUSE - ventingFor(type);
  const kHi = kAt(ideal + w), kLo = kAt(ideal - w);
  // KelvinAt never rises outward, so each edge is a binary search on a log scale.
  const LO = 0.02, HI = 60, search = test => {
    let a = Math.log(LO), b = Math.log(HI);
    for (let i = 0; i < 60; i++) { const m = (a + b) / 2; if (test(Math.exp(m))) b = m; else a = m; }
    return Math.exp(b);
  };
  let inner = kelvinAt(LO) <= kHi ? LO : kelvinAt(HI) > kHi ? HI : search(r => kelvinAt(r) <= kHi);
  let outer = kelvinAt(HI) >= kLo ? HI : kelvinAt(LO) < kLo ? LO : search(r => kelvinAt(r) < kLo);
  if (inner > outer) [inner, outer] = [outer, inner];
  return { inner, outer };
}
function zoneAfter(star, s) {
  const z = zoneRelAfter(s), ref = Math.max(0.5, star.ref);
  return { inner: Math.max(0.5, z.inner * ref), outer: z.outer * ref };
}
function cradleZoneAfter(star, s) {
  const sz = zoneAfter(star, s);
  if (!wantsLife(s.typeAfter)) return sz;
  let inner = Math.max(star.hzInner, sz.inner), outer = Math.min(star.hzOuter, sz.outer);
  if (inner > outer) { inner = star.hzInner; outer = star.hzOuter; }
  return { inner, outer };
}
function cradleAfter(star, s) {
  const type = s.typeAfter, atm = cradleAir(s), ideal = idealC(s, type);
  const z = cradleZoneAfter(star, s);
  const want = heatForC(ideal, atm, type, ventingFor(type));
  const radii = ringRadii(star);
  const ring = ringForHeat(star, radii, z.inner, z.outer, want);
  const rel = relOf(star, radii[ring]);
  // The venting a volcanic cradle actually carries is re-rolled to what its orbit needs (GalaxyGenerator.
  // TameHomeworldVents), within HomeworldVentingSlackC; modelled here as landing anywhere in that window,
  // with the starlight then solved for what is left and clamped to the orbit.
  let internal = 0, cLo = Infinity, cHi = -Infinity;
  if (type === 'Volcanic') {
    const { lo, hi } = heatBand(rel), mid = (lo + hi) / 2;
    internal = clamp(ideal - tempC(mid, atm, type), 0, INTERNAL_MAX);
    for (const d of [-VENT_SLACK, 0, VENT_SLACK]) {
      const iv = clamp(internal + d, 0, INTERNAL_MAX);
      const h = clampToOrbit(heatForC(ideal, atm, type, iv), rel), c = tempC(h, atm, type, iv);
      cLo = Math.min(cLo, c); cHi = Math.max(cHi, c);
    }
  } else {
    const h = clampToOrbit(want, rel); cLo = cHi = tempC(h, atm, type);
  }
  const heat = clampToOrbit(heatForC(ideal, atm, type, internal), rel);
  return { zone: zoneAfter(star, s), ring, type, atm, heat, c: tempC(heat, atm, type, internal), cLo, cHi, internal, radii, rel };
}

// ---- print ---------------------------------------------------------------------------------
const ringsIn = (radii, z) => radii.map((r, i) => (r >= z.inner && r <= z.outer ? i + 1 : 0)).filter(Boolean).join(',') || '-';
const pad = (v, n) => String(v).padEnd(n);
const rpad = (v, n) => String(v).padStart(n);

if (SHOW_BEFORE) {
  console.log('BEFORE (positional species band; raw heat curve for furnace/ice cradles; volcanic venting unaccounted)');
  console.log('  species     star  band xR        band units     rings   cradle  type      °C (+venting)     own rating');
  for (const s of ALL) for (const cls of Object.keys(CLASSES)) {
    const star = makeStar(cls), cr = cradleBefore(star, s), z = cr.zone;
    const ideal = idealC(s, cr.type);
    const span = cr.internalSpan[1] > 0 ? `${cr.c.toFixed(0)} +0..${cr.internalSpan[1]}` : cr.c.toFixed(0);
    const r0 = rate(s, cr.type, cr.c, cr.atm, s.cradleMass), r1 = rate(s, cr.type, cr.c + cr.internalSpan[1], cr.atm, s.cradleMass);
    const rating = cr.internalSpan[1] > 0 ? `${r0.toFixed(0)}..${r1.toFixed(0)}` : r0.toFixed(0);
    console.log(`  ${pad(s.name, 11)} ${pad(cls, 4)}  ${pad(`${(z.inner / star.ref).toFixed(2)}-${(z.outer / star.ref).toFixed(2)}`, 13)}  `
      + `${pad(`${z.inner.toFixed(1)}-${z.outer.toFixed(1)}`, 13)}  ${pad(ringsIn(cr.radii, z), 6)}  ${rpad(cr.ring + 1, 6)}  ${pad(cr.type, 8)}  `
      + `${pad(span, 16)}  ${rpad(rating, 7)}   (ideal ${ideal.toFixed(0)} °C)`);
  }
  console.log('');
}

if (SHOW_AFTER) {
  console.log('AFTER (one rule: the orbits where a world of the species\' own kind sits within its tolerance)');
  console.log('  species     star  band xR        band units     rings   cradle  type      °C (range)        own rating');
  for (const s of ALL) for (const cls of Object.keys(CLASSES)) {
    const star = makeStar(cls), cr = cradleAfter(star, s), z = cr.zone;
    const ideal = idealC(s, cr.type), w = widthC(s, ideal);
    const r = Math.min(rate(s, cr.type, cr.cLo, cr.atm, s.cradleMass), rate(s, cr.type, cr.cHi, cr.atm, s.cradleMass));
    const range = cr.cLo === cr.cHi ? cr.c.toFixed(0) : `${cr.cLo.toFixed(0)}-${cr.cHi.toFixed(0)}`;
    console.log(`  ${pad(s.name, 11)} ${pad(cls, 4)}  ${pad(`${(z.inner / star.ref).toFixed(2)}-${(z.outer / star.ref).toFixed(2)}`, 13)}  `
      + `${pad(`${z.inner.toFixed(1)}-${z.outer.toFixed(1)}`, 13)}  ${pad(ringsIn(cr.radii, z), 6)}  ${rpad(cr.ring + 1, 6)}  ${pad(cr.type, 8)}  `
      + `${pad(range, 16)}  ${rpad(r.toFixed(0), 7)}   (ideal ${ideal.toFixed(0)} ±${w.toFixed(0)} °C${cr.internal ? `, venting ${cr.internal.toFixed(0)}` : ''})`);
    const d = cr.radii[cr.ring];
    if (cr.ring < 0) fail(`${s.name} @ ${cls}: no ring for the cradle`);
    else if (d < z.inner || d > z.outer) fail(`${s.name} @ ${cls}: cradle at ${d.toFixed(1)} outside its own band ${z.inner.toFixed(1)}-${z.outer.toFixed(1)}`);
    if (Math.max(Math.abs(cr.cLo - ideal), Math.abs(cr.cHi - ideal)) > w * ZONE_TOL)
      fail(`${s.name} @ ${cls}: cradle ${range} °C is outside ${ideal.toFixed(0)} ±${(w * ZONE_TOL).toFixed(0)}`);
    if (r < EXCELLENT) fail(`${s.name} @ ${cls}: cradle rates ${r.toFixed(0)} for its own species (< ${EXCELLENT})`);
  }
  // Predictability: hotter-loving species never sit further out than colder-loving ones, and a dimmer
  // star never puts a species' zone further out than a brighter one does.
  // Compared at the orbit where a world of the species' kind sits AT its ideal — the band's heart.
  // (Edges also carry tolerance: the adaptable Sylvans' band reaches further out than the Terrans'
  // although they like it a little warmer, and that is the tolerance talking, not a contradiction.)
  const idealRel = s => {
    const type = s.typeAfter, k = heatForC(idealC(s, type), cradleAir(s), type, ventingFor(type));
    const kk = REF_K * Math.sqrt(k);
    let a = Math.log(0.02), b = Math.log(60);
    for (let i = 0; i < 60; i++) { const m = (a + b) / 2; if (kelvinAt(Math.exp(m)) <= kk) b = m; else a = m; }
    return Math.exp(b);
  };
  const byIdeal = [...ALL].sort((a, b) => idealC(a, a.typeAfter) - idealC(b, b.typeAfter));
  // Species whose ideals are within a few degrees are ordered by their AIR instead: the Sylvans like it
  // 2 °C warmer than Terrans but breathe 1.8 atmospheres to the Terrans' 1, and their thicker greenhouse
  // does the extra work, so their ideal orbit is a shade further out. Only a real difference in taste
  // (ComparableIdealC) must put the hotter species closer in.
  const COMPARABLE_C = 10;
  for (let i = 0; i < byIdeal.length; i++) for (let j = i + 1; j < byIdeal.length; j++) {
    const cold = byIdeal[i], hot = byIdeal[j];
    if (idealC(hot, hot.typeAfter) - idealC(cold, cold.typeAfter) < COMPARABLE_C) continue;
    if (idealRel(hot) > idealRel(cold) + 1e-6) fail(`${hot.name} (hotter) has its ideal orbit further out than ${cold.name}`);
  }
  console.log('\n  ideal orbit (xR), hot to cold: ' + byIdeal.slice().reverse().map(s => `${s.name} ${idealRel(s).toFixed(2)}`).join(', '));
  for (const s of ALL) {
    const order = ['A', 'F', 'G', 'K', 'M'].map(c => zoneAfter(makeStar(c), s).inner);
    for (let i = 1; i < order.length; i++) if (order[i] > order[i - 1] + 1e-6) fail(`${s.name}: a dimmer star put the zone further out`);
  }
  console.log(`\n  zones in xR are the same around every star (only R changes), ordered hot to cold: `
    + byIdeal.slice().reverse().map(s => { const z = zoneRelAfter(s); return `${s.name} ${z.inner.toFixed(2)}-${z.outer.toFixed(2)}`; }).join(', '));
}

// ============================================================================================
// THE TEMPERATURE CHAIN — distance, air, type, venting (2026-10-11)
// "Please make sure the temperatures and stuff make sense based on atmospheres and distance from stars."
//
// A rocky world on each ring of a G sun (the same in xR around every star), at nominal starlight, under
// several pressures: its average °C (PlanetTemperature.BodyAverageCelsius, ported — saturating greenhouse
// plus the deep runaway term), whether water can be liquid at that pressure, and the equator-to-pole
// swing (PlanetTemperature.LatitudeSwingC). Asserted: further out is never warmer, more air is never
// colder, liquid water only inside the zone (rings 4-5) — and a runaway sky never holds it — and the
// swing never grows with air.
// ============================================================================================
{
  const SW_AIRLESS = num(TEMP, String.raw`SwingAirlessC = ([\d.]+)f`, 'SwingAirlessC');
  const SW_EARTH = num(TEMP, String.raw`SwingEarthC = ([\d.]+)f`, 'SwingEarthC');
  const SW_THICK = num(TEMP, String.raw`SwingThickC = ([\d.]+)f`, 'SwingThickC');
  const DEEP_FROM = num(TEMP, String.raw`DeepGreenhouseFromAtmospheres = ([\d.]+)f`, 'DeepGreenhouseFromAtmospheres');
  const DEEP_FULL = num(TEMP, String.raw`DeepGreenhouseFullAtmospheres = ([\d.]+)f`, 'DeepGreenhouseFullAtmospheres');
  const DEEP_MAX = num(TEMP, String.raw`DeepGreenhouseMaxC = ([\d.]+)f`, 'DeepGreenhouseMaxC');
  const deep = atm => atm <= DEEP_FROM ? 0 : clamp01(Math.log(atm / DEEP_FROM) / Math.log(DEEP_FULL / DEEP_FROM)) * DEEP_MAX;
  const swing = atm => atm < 1 ? lerp(SW_AIRLESS, SW_EARTH, atm) : lerp(SW_EARTH, SW_THICK, (atm - 1) / 3);
  const AIRS = [0, 0.6, 1, 3, 6, 60];
  console.log('\nTEMPERATURE CHAIN — a rocky world on each ring (xR), nominal starlight; °C average, * = water can be liquid');
  console.log('  ring   xR    starlight   ' + AIRS.map(a => `${String(a).padStart(4)} atm`).join('  ') + '    (60 atm = runaway, rings 1-3 only)');
  const prev = {};
  MULTIPLES.forEach((m, i) => {
    const k = kelvinAt(m), heat = heatForKelvin(k);
    const cells = AIRS.map(a => {
      if (a >= 30 && m >= HZ_IN) return '      -';
      const c = tempC(heat, a, 'Rocky') + deep(a);
      const liquid = c >= freezeC(a) && c <= boilC(a) && boilC(a) > freezeC(a);
      if (prev[a] !== undefined && c > prev[a] + 1e-6) fail(`ring ${i + 1} at ${a} atm is warmer than ring ${i}`);
      prev[a] = c;
      if (liquid && (m < HZ_IN || m > HZ_OUT)) fail(`ring ${i + 1} (outside the zone) holds liquid water at ${a} atm (${c.toFixed(0)} °C)`);
      if (liquid && a >= 30) fail(`a runaway sky on ring ${i + 1} holds liquid water`);
      return `${c.toFixed(0)}${liquid ? '*' : ' '}`.padStart(8);
    });
    for (let j = 1; j < AIRS.length; j++) {
      if (AIRS[j] >= 30 && m >= HZ_IN) continue;
      if (tempC(heat, AIRS[j], 'Rocky') + deep(AIRS[j]) < tempC(heat, AIRS[j - 1], 'Rocky') + deep(AIRS[j - 1]) - 1e-6) fail(`more air is colder on ring ${i + 1}`);
    }
    console.log(`  ${String(i + 1).padStart(4)}  ${m.toFixed(2).padStart(4)}  ${(k - 273.15).toFixed(0).padStart(8)}   ${cells.join('  ')}`);
  });
  console.log('  type offsets: volcanic +90 (+ venting 0-620, a homeworld ' + VENTING + '), ice -50, gas giant -40 (PlanetTemperature.TypeModifierC)');
  console.log('  equator-to-pole swing: ' + AIRS.map(a => `${a} atm ±${swing(a).toFixed(0)}`).join(', ') + ' °C');
  for (let j = 1; j < AIRS.length; j++) if (swing(AIRS[j]) > swing(AIRS[j - 1]) + 1e-6) fail('the equator-to-pole swing grows with air');
  console.log('  boiling point: ' + AIRS.filter(a => a > 0).map(a => `${a} atm ${boilC(a).toFixed(0)}`).join(', ') + ' °C; below 0.6 atm no liquid window at all');
}

console.log(fails ? `\n${fails} failed` : '\nok');
process.exit(fails ? 1 : 0);
