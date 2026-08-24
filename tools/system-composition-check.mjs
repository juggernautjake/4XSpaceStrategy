// ============================================================================================
// WHAT DOES A SYSTEM ACTUALLY COME OUT AS?
//
//   node tools/system-composition-check.mjs [--systems 4000]
//
// A Node port of PlacementRings + MassRules + SolarSystemGenerator's ring loop, run over thousands
// of systems per star class. There is no Unity here, so the alternative to this is asserting that
// "around 5 bodies" and "fewer than 3 is not uncommon" fell out of the constants — and they are
// exactly the kind of claim that reads perfectly sensibly in source and is wrong by a factor of two.
//
// It answers the five things the request asked for by number:
//
//   1. bodies per system          -> should sit around 5
//   2. share of systems under 3   -> should be "not uncommon", i.e. 15-25%
//   3. moons per planet           -> and the share of planets with NONE, which should be most of them
//   4. inclined worlds per system -> at most 1, and not in every system
//   5. worlds inside the zone     -> must be >= 1 essentially always, and rings 1-3 must be OUTSIDE it
//
// The constants are READ OUT OF THE C# so they cannot drift from the game. If a number below stops
// matching a source file the parse fails loudly rather than silently measuring the wrong thing.
// ============================================================================================
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const N = parseInt(arg('--systems', '4000'), 10);

const read = f => fs.readFileSync(path.join(PROJ, f), 'utf8');
const STAR = read('Assets/Scripts/Data/StarData.cs');
const MASS = read('Assets/Scripts/Generation/MassRules.cs');
const RINGS = read('Assets/Scripts/Generation/PlacementRings.cs');
const GEN = read('Assets/Scripts/Generation/SolarSystemGenerator.cs');
const CLASS = read('Assets/Scripts/Generation/WorldClassifier.cs');
const SAFE = read('Assets/Scripts/Systems/OrbitSafety.cs');

function num(src, re, what) {
  const m = new RegExp(re).exec(src);
  if (!m) { console.error(`FAIL  could not read ${what} — the constant moved or was renamed.`); process.exit(1); }
  return parseFloat(m[1]);
}

// ---- constants, straight from the source -----------------------------------------------------
const AU           = num(STAR, String.raw`public const float AU = ([\d.]+)f`, 'StarDatabase.AU');
const FLUX_MIN     = num(STAR, String.raw`FluxMin = ([\d.]+)f`, 'FluxMin');
const FLUX_MAX     = num(STAR, String.raw`FluxMax = ([\d.]+)f`, 'FluxMax');
const HZ_IN        = num(STAR, String.raw`s\.hzInner = ([\d.]+)f \* reach`, 'hzInner');
const HZ_OUT       = num(STAR, String.raw`s\.hzOuter = ([\d.]+)f \* reach`, 'hzOuter');

const SSM          = num(MASS, String.raw`SsmPerSolarMass = ([\d.]+)f`, 'SsmPerSolarMass');
const BUDGET_MIN   = num(MASS, String.raw`BudgetMin = ([\d.]+)f`, 'BudgetMin');
const BUDGET_MAX   = num(MASS, String.raw`BudgetMax = ([\d.]+)f`, 'BudgetMax');
const TERR_MIN     = num(MASS, String.raw`TerrestrialMin = ([\d.]+)f`, 'TerrestrialMin');
const TERR_DEF     = num(MASS, String.raw`TerrestrialDefault = ([\d.]+)f`, 'TerrestrialDefault');
const TERR_MAX     = num(MASS, String.raw`TerrestrialMax = ([\d.]+)f`, 'TerrestrialMax');
const GIANT_MIN    = num(MASS, String.raw`GasGiantMin = ([\d.]+)f`, 'GasGiantMin');
const GIANT_MAX    = num(MASS, String.raw`GasGiantMax = ([\d.]+)f`, 'GasGiantMax');
const GIANT_STEP   = num(MASS, String.raw`GasGiantStep = ([\d.]+)f`, 'GasGiantStep');
const AST_MIN      = num(MASS, String.raw`AsteroidMin = ([\d.]+)f`, 'AsteroidMin');
const AST_MAX      = num(MASS, String.raw`AsteroidMax = ([\d.]+)f`, 'AsteroidMax');
const MOON_TERR    = num(MASS, String.raw`hostMass \* ([\d.]+)f;`, 'terrestrial moon share');

const RING_COUNT   = num(RINGS, String.raw`public const int Count = (\d+)`, 'PlacementRings.Count');
const INNER_REACH  = num(RINGS, String.raw`InnerBodyReach = ([\d.]+)f`, 'InnerBodyReach');
const MULTIPLES    = (() => {
  const m = /static readonly float\[\] Multiples = \{([^}]+)\}/.exec(RINGS);
  if (!m) { console.error('FAIL  could not read PlacementRings.Multiples'); process.exit(1); }
  return m[1].split(',').map(s => parseFloat(s)).filter(v => !Number.isNaN(v));
})();

const STAR_CLEAR   = num(SAFE, String.raw`StarClearance = ([\d.]+)f`, 'StarClearance');
const LANE_GAP     = num(SAFE, String.raw`LaneGap = ([\d.]+)f`, 'LaneGap');
const GIANT_FLOOR  = num(CLASS, String.raw`GasGiantMassFloor = ([\d.]+)f`, 'GasGiantMassFloor');
const FROST        = num(CLASS, String.raw`FrostLineRel = ([\d.]+)f`, 'FrostLineRel');
const HOT_REL      = num(CLASS, String.raw`HotRel = ([\d.]+)f`, 'HotRel');
const TEMPERATE    = num(CLASS, String.raw`TemperateRelMax = ([\d.]+)f`, 'TemperateRelMax');
const HOT_GIANT    = num(CLASS, String.raw`HotGiantChance = ([\d.]+)f`, 'HotGiantChance');
const OUTER_GIANT  = num(GEN, String.raw`OuterGiantChance = ([\d.]+)f`, 'OuterGiantChance');
const GIANT_MOON_MAX = num(GEN, String.raw`GiantMoonMassMax = ([\d.]+)f`, 'GiantMoonMassMax');
const SPARSE_CHANCE  = num(GEN, String.raw`Random\.value < ([\d.]+)f\s*\n\s*\? Random\.Range\(1, 4\)`, 'sparse-system chance');
const MOONLESS = (() => {
  const m = /giantHost \? ([\d.]+)f : ([\d.]+)f\)\) moonPot = 0f/.exec(GEN);
  if (!m) { console.error('FAIL  could not read the moonless chances'); process.exit(1); }
  return { giant: parseFloat(m[1]), terr: parseFloat(m[2]) };
})();
const MOON_STOP    = num(GEN, String.raw`if \(m > 0 && Random\.value < ([\d.]+)f\) break;`, 'moon early-stop');
const RING_WEIGHT  = (() => {
  const m = /static readonly float\[\] RingWeight = \{([^}]+)\}/.exec(GEN);
  if (!m) { console.error('FAIL  could not read RingWeight'); process.exit(1); }
  return m[1].split(',').map(s => parseFloat(s)).filter(v => !Number.isNaN(v));
})();
const INCL_PLANET  = num(GEN, String.raw`float chance = isMoon \? [\d.]+f : ([\d.]+)f;`, 'planet inclination chance');
const INCL_MOON    = num(GEN, String.raw`float chance = isMoon \? ([\d.]+)f`, 'moon inclination chance');
const MAX_MOONS    = (() => {
  const m = /int maxMoons = giantHost \? (\d+) : (\d+);/.exec(GEN);
  if (!m) { console.error('FAIL  could not read maxMoons'); process.exit(1); }
  return { giant: +m[1], terr: +m[2] };
})();

// ---- the star table, from StarData.Roll ------------------------------------------------------
const CLASSES = {
  O: { lum: 50000, scale: 8.0, mass: 20 }, B: { lum: 2000, scale: 6.1, mass: 8 },
  A: { lum: 20,    scale: 4.2, mass: 2.5 }, F: { lum: 4,   scale: 3.4, mass: 1.4 },
  G: { lum: 1,     scale: 2.9, mass: 1.0 }, K: { lum: 0.3, scale: 2.4, mass: 0.7 },
  M: { lum: 0.05,  scale: 1.9, mass: 0.3 },
};
// The visualScale multiplier in StarData.Roll — read so the x2 -> x4 change is picked up here too.
const STAR_SCALE_MUL = num(STAR, String.raw`s\.visualScale = baseScale \* ([\d.]+)f \* lumFactor`, 'star visualScale multiplier');

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const rnd = () => Math.random();
const range = (a, b) => a + Math.random() * (b - a);
const bell = () => (rnd() + rnd()) * 0.5;
const fluxScale = lum => clamp(Math.pow(Math.max(0.02, lum), 0.30), FLUX_MIN, FLUX_MAX);

function rollStar(cls) {
  const c = CLASSES[cls];
  const lum = c.lum * range(0.45, 1.9);
  const mass = c.mass * range(0.65, 1.55);
  const lumFactor = Math.pow(Math.max(0.001, lum / c.lum), 0.12);
  const visualScale = c.scale * STAR_SCALE_MUL * lumFactor * range(0.85, 1.18);
  const reach = fluxScale(lum) * AU;
  return {
    cls, lum, mass, visualScale, reference: reach,
    hzInner: HZ_IN * reach, hzOuter: HZ_OUT * reach,
    hasZone: cls !== 'O' && cls !== 'B',
  };
}

// ---- PlacementRings ---------------------------------------------------------------------------
const INNER_RINGS = num(RINGS, 'InnerRings = ([0-9]+)', 'PlacementRings.InnerRings');
function ringRadii(star) {
  const r = Math.max(1, star.reference);
  const out = MULTIPLES.map(m => m * r);
  const need = star.visualScale * 0.5 + STAR_CLEAR + INNER_REACH;
  if (out[0] >= need) return out;

  const hi = (star.hasZone && star.hzInner > need) ? star.hzInner : out[INNER_RINGS];
  if (hi <= need) { const shift = need - out[0]; return out.map(v => v + shift); }
  for (let i = 0; i < INNER_RINGS; i++) out[i] = need + (hi - need) * ((i + 0.5) / INNER_RINGS);
  return out;
}

// ---- MassRules --------------------------------------------------------------------------------
const qTerr  = m => clamp(Math.round(m * 10) / 10, TERR_MIN, TERR_MAX);
const qGiant = m => clamp(Math.round(m / GIANT_STEP) * GIANT_STEP, GIANT_MIN, GIANT_MAX);
const qMoon  = m => Math.max(AST_MIN, Math.round(m * 10) / 10);

function giantCeiling(cap) {
  const c = Math.floor(cap / GIANT_STEP) * GIANT_STEP;
  return c < GIANT_MIN ? 0 : Math.min(c, GIANT_MAX);
}
function rollGiant(cap) {
  const ceil = giantCeiling(cap);
  if (ceil <= 0) return 0;
  return Math.min(qGiant(GIANT_MIN + (GIANT_MAX - GIANT_MIN) * bell()), ceil);
}
function rollTerrestrial(bandMax, cap) {
  const hi = clamp(Math.min(bandMax, cap), TERR_MIN, TERR_MAX);
  if (hi <= TERR_MIN) return TERR_MIN;
  const t = bell() * 2 - 1;
  const m = t < 0 ? TERR_DEF + t * (TERR_DEF - TERR_MIN) : TERR_DEF + t * (hi - TERR_DEF);
  return clamp(qTerr(m), TERR_MIN, hi);
}
function rollAsteroid(cap) {
  const hi = clamp(cap, AST_MIN, AST_MAX);
  return clamp(clamp(Math.round(range(AST_MIN, AST_MAX) * 10) / 10, AST_MIN, AST_MAX), AST_MIN, hi);
}
function rollMoon(remaining) {
  if (remaining < AST_MIN) return 0;
  let r = rnd(); r *= r;
  const m = qMoon(AST_MIN + (remaining - AST_MIN) * r);
  return m > remaining ? qMoon(remaining) : m;
}
const moonBudget = h => h <= 0 ? 0 : (h >= GIANT_FLOOR ? h / 10 : h * MOON_TERR);
const GIANT_VIS = num(MASS, 'GasGiantDiameterScale = ([0-9.]+)f', 'GasGiantDiameterScale');
const visualDiameter = (mass, isMoon) => {
  let d = Math.pow(Math.max(mass, 0.0001), 1 / 3) * (isMoon ? 0.44 : 0.62);
  if (!isMoon && mass >= GIANT_FLOOR) d *= GIANT_VIS;
  return Math.max(isMoon ? 0.10 : 0.18, d);
};

// ---- the ring loop ----------------------------------------------------------------------------
function terrestrialBandMax(rel) {
  if (rel < HOT_REL) return 1.4;
  if (rel <= TEMPERATE) return 2.6;
  return TERR_MAX;
}
// A belt needs three planets already down, and a system-wide cap. See SolarSystemGenerator.
const BELT_MIN_PLANETS = 3;
const BELT_CAP_MAX = 4;

function rollBeltCap() {
  const r = rnd();
  if (r < 0.62) return 1;
  if (r < 0.87) return 2;
  if (r < 0.97) return 3;
  return BELT_CAP_MAX;
}

function chooseLane(rel, budgetLeft, beltsAllowed, giantsPlaced) {
  const beyondFrost = rel >= FROST;
  if (giantCeiling(budgetLeft * 0.92) > 0) {
    if (rnd() < (beyondFrost ? OUTER_GIANT : HOT_GIANT)) return 'giant';
  }
  // Inside the third planet, or the cap is spent: a refused belt is a terrestrial, NOT an empty ring.
  if (!beltsAllowed) return 'terrestrial';

  let belt = 0.10;
  if (rel > FROST * 0.75 && rel < FROST * 1.45) belt = 0.28;
  if (budgetLeft < TERR_MAX) belt = 0.50;
  if (budgetLeft < TERR_MIN * 2) return 'belt';
  if (beyondFrost && giantsPlaced === 0) belt *= 1.5;
  return rnd() < belt ? 'belt' : 'terrestrial';
}

function chooseFilledRings(habitableRing) {
  const fill = new Array(RING_COUNT).fill(false);
  const target = rnd() < SPARSE_CHANCE
    ? 1 + Math.floor(rnd() * 3)
    : clamp(1 + Math.round(bell() * 8), 1, RING_COUNT);

  let chosen = 0;
  if (habitableRing >= 0) { fill[habitableRing] = true; chosen = 1; }

  while (chosen < target) {
    let total = 0;
    for (let i = 0; i < RING_COUNT; i++) if (!fill[i]) total += RING_WEIGHT[i];
    if (total <= 0) break;
    let r = rnd() * total;
    for (let i = 0; i < RING_COUNT; i++) {
      if (fill[i]) continue;
      r -= RING_WEIGHT[i];
      if (r > 0) continue;
      fill[i] = true; chosen++; break;
    }
  }
  return fill;
}

function generate(cls) {
  const star = rollStar(cls);
  let budget = clamp(SSM * Math.max(0.05, star.mass), BUDGET_MIN, BUDGET_MAX);
  const radii = ringRadii(star);

  const inZone = radii.map(r => star.hasZone && r >= star.hzInner && r <= star.hzOuter);
  let habitableRing = -1;
  for (let i = 0; i < RING_COUNT; i++) if (inZone[i]) { habitableRing = i; break; }
  if (habitableRing < 0 && star.hasZone) {
    let bd = Infinity;
    const c = (star.hzInner + star.hzOuter) / 2;
    for (let i = 0; i < RING_COUNT; i++) { const d = Math.abs(radii[i] - c); if (d < bd) { bd = d; habitableRing = i; } }
  }

  const fill = chooseFilledRings(habitableRing);
  const out = { slots: 0, bodies: 0, planets: 0, giants: 0, belts: 0, moons: 0,
                moonless: 0, hosts: 0, inZone: 0, inclined: 0, ring1Filled: 0,
                budget, spent: 0, maxRadius: 0, ringsInZone: inZone.filter(Boolean).length,
                innerRingsInZone: (inZone[0] ? 1 : 0) + (inZone[1] ? 1 : 0) + (inZone[2] ? 1 : 0) };
  let clearedTo = 0, inclinedAlready = false;
  let planetsPlaced = 0, beltsPlaced = 0, giantsPlaced = 0;
  const beltCap = rollBeltCap();
  out.beltCap = beltCap;
  out.firstBeltAfter = 99;

  for (let ring = 0; ring < RING_COUNT; ring++) {
    if (!fill[ring]) continue;
    const radius = radii[ring];
    if (radius < clearedTo && ring !== habitableRing) continue;

    const reserve = (habitableRing >= 0 && ring < habitableRing) ? TERR_MIN : 0;
    let spendable = Math.max(0, budget - reserve);
    if (spendable < AST_MIN) { if (!(habitableRing >= 0 && ring <= habitableRing)) break; continue; }

    const rel = radius / Math.max(0.5, star.reference);
    const beltsAllowed = planetsPlaced >= BELT_MIN_PLANETS && beltsPlaced < beltCap;
    const kind = ring === habitableRing
      ? 'terrestrial'
      : chooseLane(rel, spendable, beltsAllowed, giantsPlaced);
    if (kind === 'belt') out.firstBeltAfter = Math.min(out.firstBeltAfter, planetsPlaced);
    let laneReach = 0;

    if (kind === 'belt') {
      const wanted = 3 + Math.floor(rnd() * 5);
      let placedRocks = 0;
      for (let a = 0; a < wanted; a++) {
        if (spendable < AST_MIN) break;
        const m = rollAsteroid(spendable);
        budget -= m; spendable -= m; out.bodies++; placedRocks++;
        laneReach = Math.max(laneReach, visualDiameter(m, false) / 2);
      }
      if (placedRocks) { out.belts++; out.slots++; beltsPlaced++; }
      if (placedRocks === 0) break;
    } else {
      let mass;
      if (kind === 'giant') {
        const g = rollGiant(spendable * 0.92);
        mass = g > 0 ? g : rollTerrestrial(terrestrialBandMax(rel), spendable);
      } else mass = rollTerrestrial(terrestrialBandMax(rel), spendable);
      budget -= mass; spendable -= mass;
      out.bodies++; out.planets++; out.slots++;
      planetsPlaced++;
      if (mass >= GIANT_FLOOR) { out.giants++; giantsPlaced++; }
      if (star.hasZone && radius >= star.hzInner && radius <= star.hzOuter) out.inZone++;
      if (ring === 0) out.ring1Filled++;

      if (rnd() < INCL_PLANET && !inclinedAlready) { inclinedAlready = true; out.inclined++; }

      // moons
      const giantHost = mass >= GIANT_FLOOR;
      let pot = Math.min(moonBudget(mass), spendable);
      if (rnd() < (giantHost ? MOONLESS.giant : MOONLESS.terr)) pot = 0;
      const maxM = giantHost ? MAX_MOONS.giant : MAX_MOONS.terr;
      let planetRadius = visualDiameter(mass, false) / 2;
      let moonR = planetRadius + 0.4 + 0.9, mine = 0;

      for (let m = 0; m < maxM; m++) {
        if (pot < AST_MIN) break;
        if (m > 0 && rnd() < MOON_STOP) break;
        const cap = giantHost ? Math.min(pot, GIANT_MOON_MAX) : pot;
        const mm = rollMoon(cap);
        if (mm < AST_MIN) break;
        pot -= mm; budget -= mm; spendable -= mm; out.bodies++; out.moons++; mine++;
        if (rnd() < INCL_MOON && !inclinedAlready) { inclinedAlready = true; out.inclined++; }
        moonR += 0.4 * 2 + range(1.6, 2.6);
      }
      out.hosts++;
      if (mine === 0) out.moonless++;
      laneReach = Math.max(planetRadius, moonR);
    }
    clearedTo = radius + laneReach + LANE_GAP;
    out.maxRadius = Math.max(out.maxRadius, radius);
  }

  out.spent = out.budget - budget;
  return out;
}

// ---- run ---------------------------------------------------------------------------------------
console.log(`AU=${AU}  SSM=${SSM}  budget ${BUDGET_MIN}..${BUDGET_MAX}  rings=${RING_COUNT}  starScale x${STAR_SCALE_MUL}`);
console.log(`terrestrial moon share ${MOON_TERR}  giant moon cap ${GIANT_MOON_MAX}  maxMoons ${MAX_MOONS.terr}/${MAX_MOONS.giant}`);
console.log(`moonless ${MOONLESS.terr} terrestrial / ${MOONLESS.giant} giant   early-stop ${MOON_STOP}   sparse ${SPARSE_CHANCE}\n`);

const hdr = ['star', 'budget', 'slots', '<3', 'planets', 'giants', 'belts', 'moons', 'moonless', 'incl', 'inZone', 'outerR'];
console.log(hdr.map((h, i) => h.padEnd(i === 0 ? 6 : 9)).join(''));

const weighted = [];   // galaxy-wide, using the single-star roll weights from RollStarType
const ODDS = { M: 0.45, K: 0.20, G: 0.15, F: 0.10, A: 0.06, B: 0.03, O: 0.01 };

let zoneFailures = 0, innerRingFailures = 0;
// F32/F33: how many belts a system gets, and how many planets were down before the first one.
const beltHist = [0, 0, 0, 0, 0, 0];
let beltTooClose = 0, beltOverCap = 0, systemsWithBelt = 0;

for (const cls of ['M', 'K', 'G', 'F', 'A', 'B', 'O']) {
  const acc = { slots: 0, bodies: 0, planets: 0, giants: 0, belts: 0, moons: 0, moonless: 0, hosts: 0,
                inclined: 0, inZone: 0, under3: 0, budget: 0, maxRadius: 0, withZoneWorld: 0 };
  for (let i = 0; i < N; i++) {
    const s = generate(cls);
    acc.slots += s.slots; acc.bodies += s.bodies; acc.planets += s.planets; acc.giants += s.giants;
    acc.belts += s.belts; acc.moons += s.moons; acc.moonless += s.moonless;
    acc.hosts += s.hosts; acc.inclined += s.inclined; acc.inZone += s.inZone;
    acc.budget += s.budget; acc.maxRadius = Math.max(acc.maxRadius, s.maxRadius);
    if (s.slots < 3) acc.under3++;
    if (s.inZone > 0) acc.withZoneWorld++;
    if (s.innerRingsInZone > 0) innerRingFailures++;
    beltHist[Math.min(s.belts, 5)]++;
    if (s.belts > 0) systemsWithBelt++;
    if (s.belts > BELT_CAP_MAX) beltOverCap++;
    if (s.firstBeltAfter < BELT_MIN_PLANETS) beltTooClose++;
    weighted.push({ cls, slots: s.slots });
  }
  const has = cls !== 'O' && cls !== 'B';
  if (has && acc.withZoneWorld / N < 0.90) zoneFailures++;

  console.log([
    cls,
    (acc.budget / N).toFixed(0),
    (acc.slots / N).toFixed(1),
    (100 * acc.under3 / N).toFixed(0) + '%',
    (acc.planets / N).toFixed(1),
    (acc.giants / N).toFixed(1),
    (acc.belts / N).toFixed(1),
    (acc.moons / N).toFixed(1),
    (100 * acc.moonless / Math.max(1, acc.hosts)).toFixed(0) + '%',
    (acc.inclined / N).toFixed(2),
    has ? (100 * acc.withZoneWorld / N).toFixed(0) + '%' : '-',
    acc.maxRadius.toFixed(0),
  ].map((v, i) => String(v).padEnd(i === 0 ? 6 : 9)).join(''));
}

// ---- the ring ladder, printed for a G so the table in PlacementRings can be checked ------------
{
  const star = { cls: 'G', lum: 1, mass: 1, visualScale: 2.9 * STAR_SCALE_MUL,
                 reference: AU, hzInner: HZ_IN * AU, hzOuter: HZ_OUT * AU, hasZone: true };
  const r = ringRadii(star);
  console.log(`\nG-type ladder (R=${AU}, zone ${star.hzInner.toFixed(0)}-${star.hzOuter.toFixed(0)}, star reach ${(star.visualScale / 2 + STAR_CLEAR).toFixed(1)}):`);
  console.log('  ' + r.map((v, i) => `${i + 1}:${v.toFixed(0)}${v >= star.hzInner && v <= star.hzOuter ? '*' : ''}`).join('  ') + '     (* = in zone)');
}

// ---- the asserts --------------------------------------------------------------------------------
const all = weighted.map(w => w.slots);
const galaxyMean = all.reduce((a, b) => a + b, 0) / all.length;
const galaxyUnder3 = 100 * all.filter(b => b < 3).length / all.length;
console.log(`\nAcross every class: mean ${galaxyMean.toFixed(2)} filled rings, ${galaxyUnder3.toFixed(0)}% under three.`);
console.log(`(a filled ring is ONE celestial body or ONE asteroid field — the request’s own unit)`);

let bad = 0;
const check = (ok, msg) => { console.log(`${ok ? 'ok   ' : 'FAIL '} ${msg}`); if (!ok) bad++; };

check(galaxyMean >= 4.0 && galaxyMean <= 6.5, `mean bodies per system is ~5 (got ${galaxyMean.toFixed(2)})`);
check(galaxyUnder3 >= 12 && galaxyUnder3 <= 30, `"fewer than 3" is not uncommon (got ${galaxyUnder3.toFixed(0)}%, want 12-30%)`);
check(zoneFailures === 0, 'every zone-bearing star class puts a world in its habitable zone >=90% of the time');
check(innerRingFailures === 0, 'rings 1-3 are never inside the habitable zone (the Earthlike is never the innermost world)');

// ---- F32 / F33: where belts may go, and how many ----------------------------------------------
const totalSystems = weighted.length;
const pct = n => (100 * n / totalSystems).toFixed(1) + '%';
console.log('');
console.log('Belts per system:  ' +
  beltHist.slice(0, 5).map((n, i) => i + ':' + pct(n)).join('  ') +
  (beltHist[5] ? '  5+:' + pct(beltHist[5]) : ''));
const withBelt = Math.max(1, systemsWithBelt);
console.log('Of systems that get a belt at all:  ' +
  [1, 2, 3, 4].map(i => i + ':' + (100 * beltHist[i] / withBelt).toFixed(0) + '%').join('  '));

check(beltTooClose === 0,
  'no belt is placed before the 3rd planet (' + beltTooClose + ' of ' + totalSystems + ' systems)');
check(beltOverCap === 0,
  'no system has more than ' + BELT_CAP_MAX + ' belts (' + beltOverCap + ' over cap)');
check(beltHist[1] > beltHist[2] && beltHist[2] > beltHist[3] && beltHist[3] >= beltHist[4],
  '1 belt is far more common than 2, 2 than 3, and 4 is the rarest');
check(beltHist[4] / totalSystems < 0.01,
  '4 belts is very rare (' + pct(beltHist[4]) + ' of systems)');

process.exit(bad ? 1 : 0);
