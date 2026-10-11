// ============================================================================================
// WHY IS EVERY WORLD THE WAY IT IS? (2026-10-11)
//
//   node tools/world-causes-check.mjs [--systems 3000] [--quiet]
//
// "Please work on reasoning why certain planets generate in certain conditions and certain zones. Like,
// should a frozen planet generate within the habitable zone... What all factors are impacting why a
// planet is a lava planet, or a gas giant, or a frozen ocean, or have high pressure."
//
// A Node port of the WHOLE natural-world pipeline — PlacementRings + the ring loop and ChooseLane
// (as tools/system-composition-check.mjs ports them), then per body exactly what
// SolarSystemGenerator.ApplyWorldPipeline does: rotation, tidal lock, magnetic field, plates, terrain
// variance, starlight (InsolationRules), band water, air (AtmosphereRules, inner-orbit cut, runaway
// greenhouse), water loss, WorldClassifier.Physics, the biosphere, AmplifyBiome and finally
// WorldClassifier.Describe — plus EnsureHabitableWorld and WorldModifiers.Roll.
//
// It runs the pipeline twice over the same seeds: OLD (the rules before this change, reproduced here) and
// NEW (the rules now in the C#), and prints
//
//   1. how often each world class appears on each ring and around each star class,
//   2. the CAUSE of every world that sounds paradoxical — a frozen world inside the habitable zone, a
//      hot giant, a dead world with water — so each one can be read as "yes, and here is why",
//   3. the flags: combinations with NO valid cause. The NEW pipeline must produce none of them.
//
// Constants are READ OUT OF THE C# so the port cannot drift; a missing one fails loudly. Exits 1 if the
// NEW pipeline produces any unjustified world.
// ============================================================================================
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const N = parseInt(arg('--systems', '3000'), 10);
const QUIET = argv.includes('--quiet');

const read = f => fs.readFileSync(path.join(PROJ, f), 'utf8');
const STAR = read('Assets/Scripts/Data/StarData.cs');
const MASS = read('Assets/Scripts/Generation/MassRules.cs');
const RINGS = read('Assets/Scripts/Generation/PlacementRings.cs');
const GEN = read('Assets/Scripts/Generation/SolarSystemGenerator.cs');
const CLASS = read('Assets/Scripts/Generation/WorldClassifier.cs');
const SAFE = read('Assets/Scripts/Systems/OrbitSafety.cs');
const INSO = read('Assets/Scripts/Generation/InsolationRules.cs');
const ATMO = read('Assets/Scripts/Generation/AtmosphereRules.cs');
const TEMP = read('Assets/Scripts/Data/PlanetTemperature.cs');
const TERRA = read('Assets/Scripts/Systems/Terraforming.cs');
const ROT = read('Assets/Scripts/Generation/RotationRules.cs');
const MODS = read('Assets/Scripts/Data/WorldModifiers.cs');

function num(src, re, what) {
  const m = new RegExp(re).exec(src);
  if (!m) { console.error(`FAIL  could not read ${what} — the constant moved or was renamed.`); process.exit(1); }
  return parseFloat(m[1]);
}
const has = (src, re, what) => { if (!new RegExp(re).test(src)) { console.error(`FAIL  ${what} is not in the C# — the rule this check measures is gone.`); process.exit(1); } };

// ---- constants -------------------------------------------------------------------------------
const AU = num(STAR, String.raw`public const float AU = ([\d.]+)f`, 'StarDatabase.AU');
const FLUX_MIN = num(STAR, String.raw`FluxMin = ([\d.]+)f`, 'FluxMin');
const FLUX_MAX = num(STAR, String.raw`FluxMax = ([\d.]+)f`, 'FluxMax');
const HZ_IN = num(STAR, String.raw`HzInnerRel = ([\d.]+)f`, 'HzInnerRel');
const HZ_OUT = num(STAR, String.raw`HzOuterRel = ([\d.]+)f`, 'HzOuterRel');
const STAR_SCALE_MUL = num(STAR, String.raw`s\.visualScale = baseScale \* ([\d.]+)f \* lumFactor`, 'star visualScale multiplier');

const SSM = num(MASS, String.raw`SsmPerSolarMass = ([\d.]+)f`, 'SsmPerSolarMass');
const BUDGET_MIN = num(MASS, String.raw`BudgetMin = ([\d.]+)f`, 'BudgetMin');
const BUDGET_MAX = num(MASS, String.raw`BudgetMax = ([\d.]+)f`, 'BudgetMax');
const TERR_MIN = num(MASS, String.raw`TerrestrialMin = ([\d.]+)f`, 'TerrestrialMin');
const TERR_DEF = num(MASS, String.raw`TerrestrialDefault = ([\d.]+)f`, 'TerrestrialDefault');
const TERR_MAX = num(MASS, String.raw`TerrestrialMax = ([\d.]+)f`, 'TerrestrialMax');
const GIANT_MIN = num(MASS, String.raw`GasGiantMin = ([\d.]+)f`, 'GasGiantMin');
const GIANT_MAX = num(MASS, String.raw`GasGiantMax = ([\d.]+)f`, 'GasGiantMax');
const GIANT_STEP = num(MASS, String.raw`GasGiantStep = ([\d.]+)f`, 'GasGiantStep');
const AST_MIN = num(MASS, String.raw`AsteroidMin = ([\d.]+)f`, 'AsteroidMin');
const AST_MAX = num(MASS, String.raw`AsteroidMax = ([\d.]+)f`, 'AsteroidMax');
const MOON_TERR = num(MASS, String.raw`hostMass \* ([\d.]+)f;`, 'terrestrial moon share');

const RING_COUNT = num(RINGS, String.raw`public const int Count = (\d+)`, 'PlacementRings.Count');
const INNER_REACH = num(RINGS, String.raw`InnerBodyReach = ([\d.]+)f`, 'InnerBodyReach');
const INNER_RINGS = num(RINGS, 'InnerRings = ([0-9]+)', 'PlacementRings.InnerRings');
const MULTIPLES = (() => {
  const m = /static readonly float\[\] Multiples = \{([^}]+)\}/.exec(RINGS);
  if (!m) { console.error('FAIL  could not read PlacementRings.Multiples'); process.exit(1); }
  return m[1].split(',').map(s => parseFloat(s)).filter(v => !Number.isNaN(v));
})();
const STAR_CLEAR = num(SAFE, String.raw`StarClearance = ([\d.]+)f`, 'StarClearance');
const LANE_GAP = num(SAFE, String.raw`LaneGap = ([\d.]+)f`, 'LaneGap');

const GIANT_FLOOR = num(CLASS, String.raw`GasGiantMassFloor = ([\d.]+)f`, 'GasGiantMassFloor');
const AST_CEIL = num(CLASS, String.raw`AsteroidMassCeil = ([\d.]+)f`, 'AsteroidMassCeil');
const SCORCHING = num(CLASS, String.raw`ScorchingRel = ([\d.]+)f`, 'ScorchingRel');
const FROST = num(CLASS, String.raw`FrostLineRel = ([\d.]+)f`, 'FrostLineRel');
const HOT_GIANT = num(CLASS, String.raw`HotGiantChance = ([\d.]+)f`, 'HotGiantChance');
const OCEAN_WATER = num(CLASS, String.raw`OceanWater = ([\d.]+)f`, 'OceanWater');
const LAND_WATER = num(CLASS, String.raw`LandWater = ([\d.]+)f`, 'LandWater');
const MAGMA_MIN_C = num(CLASS, String.raw`MagmaMinC = ([\d.]+)f`, 'MagmaMinC');

const OUTER_GIANT = num(GEN, String.raw`OuterGiantChance = ([\d.]+)f`, 'OuterGiantChance');
const GIANT_MOON_MAX = num(GEN, String.raw`GiantMoonMassMax = ([\d.]+)f`, 'GiantMoonMassMax');
const SPARSE_CHANCE = num(GEN, String.raw`Random\.value < ([\d.]+)f\s*\n\s*\? Random\.Range\(1, 4\)`, 'sparse-system chance');
const MOONLESS = (() => {
  const m = /giantHost \? ([\d.]+)f : ([\d.]+)f\)\) moonPot = 0f/.exec(GEN);
  if (!m) { console.error('FAIL  could not read the moonless chances'); process.exit(1); }
  return { giant: parseFloat(m[1]), terr: parseFloat(m[2]) };
})();
const MOON_STOP = num(GEN, String.raw`if \(m > 0 && Random\.value < ([\d.]+)f\) break;`, 'moon early-stop');
const RING_WEIGHT = (() => {
  const m = /static readonly float\[\] RingWeight = \{([^}]+)\}/.exec(GEN);
  if (!m) { console.error('FAIL  could not read RingWeight'); process.exit(1); }
  return m[1].split(',').map(s => parseFloat(s)).filter(v => !Number.isNaN(v));
})();
const MAX_MOONS = (() => {
  const m = /int maxMoons = giantHost \? (\d+) : (\d+);/.exec(GEN);
  if (!m) { console.error('FAIL  could not read maxMoons'); process.exit(1); }
  return { giant: +m[1], terr: +m[2] };
})();

const RUNAWAY = num(INSO, String.raw`RunawayGain = ([\d.]+)f`, 'RunawayGain');
const INNER_FLOOR_C = num(INSO, String.raw`InnerFloorC = ([\d.]+)f`, 'InnerFloorC');
const SNOWBALL = num(INSO, String.raw`SnowballGain = ([\d.]+)f`, 'SnowballGain');
const OUTER_CEIL_C = -num(INSO, String.raw`OuterCeilingC = -([\d.]+)f`, 'OuterCeilingC');
const JITTER = num(INSO, String.raw`Jitter = ([\d.]+)f`, 'Jitter');
const BH_K = num(INSO, String.raw`BlackHoleKelvin = ([\d.]+)f`, 'InsolationRules.BlackHoleKelvin');

const REF_K = num(TEMP, String.raw`ReferenceKelvin = ([\d.]+)f`, 'ReferenceKelvin');
const GREENHOUSE = num(TEMP, String.raw`GreenhouseMaxC = ([\d.]+)f`, 'GreenhouseMaxC');
const DEEP_FROM = num(TEMP, String.raw`DeepGreenhouseFromAtmospheres = ([\d.]+)f`, 'DeepGreenhouseFromAtmospheres');
const DEEP_FULL = num(TEMP, String.raw`DeepGreenhouseFullAtmospheres = ([\d.]+)f`, 'DeepGreenhouseFullAtmospheres');
const DEEP_MAX = num(TEMP, String.raw`DeepGreenhouseMaxC = ([\d.]+)f`, 'DeepGreenhouseMaxC');
const INTERNAL_MAX = num(TEMP, String.raw`InternalMaxC = ([\d.]+)f`, 'InternalMaxC');

const THICK_REF = num(ATMO, String.raw`ThicknessReference = ([\d.]+)f`, 'ThicknessReference');
const LIFE_FLOOR = num(ATMO, String.raw`LifeFloor = ([\d.]+)f`, 'LifeFloor');
const NO_FIELD = num(ATMO, String.raw`NoFieldPenalty = ([\d.]+)f`, 'NoFieldPenalty');
const TECT_MIN = num(ATMO, String.raw`TectonicBonusMin = ([\d.]+)f`, 'TectonicBonusMin');
const TECT_MAX = num(ATMO, String.raw`TectonicBonusMax = ([\d.]+)f`, 'TectonicBonusMax');
const GH_MIN_MASS = num(ATMO, String.raw`GreenhouseMinMass = ([\d.]+)f`, 'GreenhouseMinMass');
const GH_CHANCE = num(ATMO, String.raw`GreenhouseChance = ([\d.]+)f`, 'GreenhouseChance');
const GH_CHANCE_R1 = num(ATMO, String.raw`GreenhouseChanceRingOne = ([\d.]+)f`, 'GreenhouseChanceRingOne');
const GH_CHANCE_FLOOR = num(ATMO, String.raw`GreenhouseChanceFloor = ([\d.]+)f`, 'GreenhouseChanceFloor');
const GH_INNERMOST = num(ATMO, String.raw`GreenhouseInnermostRel = ([\d.]+)f`, 'GreenhouseInnermostRel');
const GH_ATM_MIN = num(ATMO, String.raw`GreenhouseAtmospheresMin = ([\d.]+)f`, 'GreenhouseAtmospheresMin');
const GH_ATM_MAX = num(ATMO, String.raw`GreenhouseAtmospheresMax = ([\d.]+)f`, 'GreenhouseAtmospheresMax');
const GH_THRESHOLD = num(ATMO, String.raw`GreenhouseThresholdAtmospheres = ([\d.]+)f`, 'GreenhouseThresholdAtmospheres');

const BOIL_EARTH = num(TERRA, String.raw`BoilingAtEarth = ([\d.]+)f`, 'BoilingAtEarth');
const BOIL_FOUR = num(TERRA, String.raw`BoilingAtFourAtm = ([\d.]+)f`, 'BoilingAtFourAtm');
const MIN_LIQ = num(TERRA, String.raw`MinLiquidC = ([\d.-]+)f`, 'MinLiquidC');
const MAX_LIQ = num(TERRA, String.raw`MaxLiquidC = ([\d.]+)f`, 'MaxLiquidC');
const MIN_WATER = num(TERRA, String.raw`MinWaterLevel = ([\d.]+)f`, 'MinWaterLevel');

const FIELD_SPIN = num(ROT, String.raw`MagneticFieldSpin = ([\d.]+)f`, 'MagneticFieldSpin');
const MIN_SPIN = num(ROT, String.raw`MinSpin = ([\d.]+)f`, 'MinSpin');
const MAX_SPIN = num(ROT, String.raw`MaxSpin = ([\d.]+)f`, 'MaxSpin');

const WEATHER_MIN_ATM = num(MODS, String.raw`ExtremeWeatherMinAtmospheres = ([\d.]+)f`, 'ExtremeWeatherMinAtmospheres');
const TIDE_EARTH_CHANCE = num(MODS, String.raw`TidalLockEarthChance = ([\d.]+)f`, 'WorldModifiers.TidalLockEarthChance');
const TIDE_MIN = num(MODS, String.raw`TidalLockMinTide = ([\d.]+)f`, 'WorldModifiers.TidalLockMinTide');
const TIDE_MAX_CHANCE = num(MODS, String.raw`TidalLockMaxChance = ([\d.]+)f`, 'WorldModifiers.TidalLockMaxChance');
has(GEN, String.raw`StarAllowsLife\(`, 'SolarSystemGenerator\'s O/B sterility rule (StarAllowsLife)');

// ---- RNG ---------------------------------------------------------------------------------------
function mulberry32(a) {
  return () => { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
let rnd = mulberry32(1);
const range = (a, b) => a + rnd() * (b - a);
const bell = () => (rnd() + rnd()) * 0.5;
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const clamp01 = v => clamp(v, 0, 1);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const invLerp = (a, b, v) => a === b ? 0 : clamp01((v - a) / (b - a));
const q1 = v => Math.max(0, Math.round(v * 10) / 10);

// ---- stars -------------------------------------------------------------------------------------
const CLASSES = {
  O: { lum: 50000, scale: 8.0, mass: 20 }, B: { lum: 2000, scale: 6.1, mass: 8 },
  A: { lum: 20, scale: 4.2, mass: 2.5 }, F: { lum: 4, scale: 3.4, mass: 1.4 },
  G: { lum: 1, scale: 2.9, mass: 1.0 }, K: { lum: 0.3, scale: 2.4, mass: 0.7 },
  M: { lum: 0.05, scale: 1.9, mass: 0.3 },
};
const fluxScale = lum => clamp(Math.pow(Math.max(0.02, lum), 0.30), FLUX_MIN, FLUX_MAX);
function rollStar(cls) {
  if (cls === 'BH') {
    const ref = fluxScale(0) * AU;
    return { cls, lum: 0, mass: 14, visualScale: 4, ref, hzInner: 0, hzOuter: 0, hasZone: false, blackHole: true };
  }
  const c = CLASSES[cls];
  const lum = c.lum * range(0.45, 1.9);
  const mass = c.mass * range(0.65, 1.55);
  const visualScale = c.scale * STAR_SCALE_MUL * Math.pow(Math.max(0.001, lum / c.lum), 0.12) * range(0.85, 1.18);
  const ref = fluxScale(lum) * AU;
  return { cls, lum, mass, visualScale, ref, hzInner: HZ_IN * ref, hzOuter: HZ_OUT * ref, hasZone: cls !== 'O' && cls !== 'B', blackHole: false };
}
function ringRadii(star) {
  const r = Math.max(1, star.ref);
  const out = MULTIPLES.map(m => m * r);
  const need = star.visualScale * 0.5 + STAR_CLEAR + INNER_REACH;
  if (out[0] >= need) return out;
  const hi = (star.hasZone && star.hzInner > need) ? star.hzInner : out[INNER_RINGS];
  if (hi <= need) { const shift = need - out[0]; return out.map(v => v + shift); }
  for (let i = 0; i < INNER_RINGS; i++) out[i] = need + (hi - need) * ((i + 0.5) / INNER_RINGS);
  return out;
}
const relOf = (star, d) => Math.max(1, d) / Math.max(0.5, star.ref);

// ---- MassRules ---------------------------------------------------------------------------------
const qTerr = m => clamp(Math.round(m * 10) / 10, TERR_MIN, TERR_MAX);
const qGiant = m => clamp(Math.round(m / GIANT_STEP) * GIANT_STEP, GIANT_MIN, GIANT_MAX);
const qMoon = m => Math.max(AST_MIN, Math.round(m * 10) / 10);
function giantCeiling(cap) { const c = Math.floor(cap / GIANT_STEP) * GIANT_STEP; return c < GIANT_MIN ? 0 : Math.min(c, GIANT_MAX); }
function rollGiant(cap) { const ceil = giantCeiling(cap); if (ceil <= 0) return 0; return Math.min(qGiant(GIANT_MIN + (GIANT_MAX - GIANT_MIN) * bell()), ceil); }
function rollTerrestrial(bandMax, cap) {
  const hi = clamp(Math.min(bandMax, cap), TERR_MIN, TERR_MAX);
  if (hi <= TERR_MIN) return TERR_MIN;
  const t = bell() * 2 - 1;
  const m = t < 0 ? TERR_DEF + t * (TERR_DEF - TERR_MIN) : TERR_DEF + t * (hi - TERR_DEF);
  return clamp(qTerr(m), TERR_MIN, hi);
}
function rollAsteroid(cap) { const hi = clamp(cap, AST_MIN, AST_MAX); return clamp(clamp(Math.round(range(AST_MIN, AST_MAX) * 10) / 10, AST_MIN, AST_MAX), AST_MIN, hi); }
function rollMoon(remaining) { if (remaining < AST_MIN) return 0; let r = rnd(); r *= r; const m = qMoon(AST_MIN + (remaining - AST_MIN) * r); return m > remaining ? qMoon(remaining) : m; }
const moonBudget = h => h <= 0 ? 0 : (h >= GIANT_FLOOR ? h / 10 : h * MOON_TERR);
const terrestrialBandMax = rel => rel < HZ_IN ? 1.4 : rel <= HZ_OUT ? 2.6 : TERR_MAX;

// ---- temperature ------------------------------------------------------------------------------
const typeNudge = t => t === 'Volcanic' ? 90 : t === 'Ice' ? -50 : t === 'GasGiant' ? -40 : 0;
const heatForKelvin = k => Math.max(0.01, (Math.max(0, k) / REF_K) ** 2);
const deepC = (atm, type) => (type === 'GasGiant' || atm <= DEEP_FROM) ? 0 : clamp01(Math.log(atm / DEEP_FROM) / Math.log(DEEP_FULL / DEEP_FROM)) * DEEP_MAX;
const thick = atm => clamp01(atm / THICK_REF);
const starlightC = heat => REF_K * Math.sqrt(Math.max(1e-5, heat)) - 273.15;
const classifyC = b => starlightC(b.heat) + thick(b.atm) * GREENHOUSE + deepC(b.atm, b.type);
const internalC = b => b.type === 'Volcanic' ? clamp01(b.geo) * INTERNAL_MAX : 0;
const bodyAvgC = b => clamp(starlightC(b.heat) + typeNudge(b.type) + thick(b.atm) * GREENHOUSE + internalC(b) + deepC(b.atm, b.type), -270, 1000);
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
function rollHeat(star, rel, mode) {
  // NEW: a black hole gives no starlight — its worlds get only the faint accretion glow
  // (InsolationRules.RollHeat / BlackHoleKelvin). OLD: it was treated as a 0.45-flux star.
  if (mode === 'new' && star.blackHole) return heatForKelvin(BH_K * range(1 - JITTER, 1 + JITTER));
  let k = kelvinAt(rel) * range(1 - JITTER, 1 + JITTER);
  if (rel < HZ_IN) k = Math.max(k, INNER_FLOOR_C + 273.15); else if (rel > HZ_OUT) k = Math.min(k, OUTER_CEIL_C + 273.15);
  return heatForKelvin(k);
}
const boilC = atm => atm < LIFE_FLOOR ? -270 : BOIL_EARTH + (BOIL_FOUR - BOIL_EARTH) * Math.log(Math.max(0.01, atm)) / Math.log(4);
const freezeC = atm => atm < LIFE_FLOOR ? -270 : clamp(1 + (0 - 1) * (Math.max(0.01, atm) - 1) / 3, -8, 4);

// ---- rotation, lock, field, plates -------------------------------------------------------------
function rollSpin(mass, isMoon) {
  const lock = isMoon ? 0.34 : lerp(0.32, 0.06, invLerp(0.3, 4, mass));
  if (rnd() < lock) return clamp(q1(range(MIN_SPIN, FIELD_SPIN - 1.5)), MIN_SPIN, MAX_SPIN);
  const bias = invLerp(0.3, 12, mass);
  const t = lerp(rnd(), 0.45 + rnd() * 0.55, bias);
  return clamp(q1(lerp(FIELD_SPIN, MAX_SPIN, t)), MIN_SPIN, MAX_SPIN);
}
// The tide a star raises on a world, relative to the Sun's on Earth: star mass over the cube of the
// distance in AU. The game's ruler is AU (StarDatabase.AU) — WorldModifiers.TideStrength.
const tide = (star, d) => star.mass / Math.pow(Math.max(0.05, d / AU), 3);
function lockChance(type, rel, isMoon, star, d, mode) {
  if (isMoon || type === 'GasGiant' || type === 'Asteroid') return 0;
  if (mode === 'old') return rel < HZ_IN ? 0.10 : rel <= HZ_OUT ? 0.03 : 0.01;
  const t = tide(star, d);
  if (t < TIDE_MIN) return 0;
  return Math.min(TIDE_MAX_CHANCE, TIDE_EARTH_CHANCE * Math.sqrt(t));
}
const fieldOf = (type, mass, spin) => type === 'GasGiant' ? true : type === 'Asteroid' ? false : mass < TERR_MIN ? false : spin >= FIELD_SPIN;
function rollTectonics(type, mass) {
  if (type === 'GasGiant' || type === 'Asteroid') return false;
  if (type === 'Moon') { if (mass < 1.5) return false; return rnd() < lerp(0.10, 0.30, invLerp(1.5, 4, mass)); }
  return rnd() < lerp(0.20, 0.55, invLerp(TERR_DEF, TERR_MAX, mass));
}

// ---- air -------------------------------------------------------------------------------------
function heatRetention(mass, heat) {
  const excess = Math.max(0, heat - 1); if (excess <= 0) return 1;
  const tol = lerp(0.20, 1.25, invLerp(1, 4, mass));
  const over = Math.max(0, excess - tol); if (over <= 0) return 1;
  return 1 - clamp01(over / 0.8) * 0.85;
}
const innerRetention = rel => rel >= HZ_IN ? 1 : lerp(0.12, 1, rel / HZ_IN);
function rollAtm(b, rel) {
  if (b.type === 'Asteroid') return 0;
  const ceiling = Math.max(0, b.mass) * (b.field ? 1 : NO_FIELD) + b.tectBonus;
  if (ceiling <= 0) return 0;
  return q1(q1(ceiling * heatRetention(b.mass, b.heat) * range(0.85, 1)) * innerRetention(rel));
}
function ghChance(mass, rel) {
  if (mass < GH_MIN_MASS || rel >= HZ_IN) return 0;
  if (rel > SCORCHING) return GH_CHANCE;
  return lerp(GH_CHANCE_FLOOR, GH_CHANCE_R1, invLerp(GH_INNERMOST, SCORCHING, rel));
}
function rollRunaway(b, rel) {
  if (b.type === 'GasGiant' || b.type === 'Asteroid') return false;
  const c = ghChance(b.mass, rel);
  if (c <= 0 || rnd() >= c) return false;
  b.atm = q1(range(GH_ATM_MIN, GH_ATM_MAX)); b.water = Math.min(b.water, 0.04); b.bio = false; b.tect = false; b.tectBonus = 0;
  b.runaway = true;
  return true;
}
function waterLoss(b) {
  const c = starlightC(b.heat) + thick(b.atm) * GREENHOUSE;   // BaseCelsius(heat, thickness, Rocky): no deep term
  if (c < 0) return;
  const keep = b.atm >= LIFE_FLOOR ? 1 : clamp01(b.atm / LIFE_FLOOR);
  if (keep >= 1) return;
  b.water *= keep;
  if (b.atm < LIFE_FLOOR) b.bio = false;
}

// ---- WorldClassifier ---------------------------------------------------------------------------
// Physics also returns the branch it took — the C# WorldClassifier.Physics(.., out ClassCause) does the
// same, and WorldExplanation reads it.
function physics(b, rel, isMoon, mode) {
  if (b.mass >= GIANT_FLOOR) return ['GasGiant', 'giant-mass'];
  if (b.mass <= AST_CEIL) return isMoon ? ['Moon', 'small-moon'] : ['Asteroid', 'asteroid-mass'];
  const c = classifyC(b), fz = freezeC(b.atm), bl = boilC(b.atm);
  const liquid = c >= fz && c <= bl;
  if (rel < HZ_IN) return b.tect ? ['Volcanic', 'inner-plates'] : ['Barren', b.runaway ? 'inner-runaway' : 'inner-no-plates'];
  if (rel <= HZ_OUT) {
    if (c < fz) return b.water >= LAND_WATER ? ['Ice', 'zone-frozen-water'] : ['Barren', 'zone-frozen-dry'];
    if (liquid) {
      if (b.water >= OCEAN_WATER) return ['Ocean', 'zone-ocean'];
      if (b.water >= LAND_WATER) return ['Rocky', 'zone-temperate'];
    }
    return b.atm >= LIFE_FLOOR ? ['Rocky', liquid ? 'zone-dry' : 'zone-too-hot'] : ['Barren', 'zone-airless'];
  }
  if (b.water >= LAND_WATER) return ['Ice', 'outer-ice'];
  // NEW: a moon only stays the airless Moon class out here if it really is airless.
  return isMoon && (mode === 'old' || b.atm < LIFE_FLOOR) ? ['Moon', 'outer-moon'] : ['Barren', 'outer-rock'];
}

function describe(b, mode) {
  if (b.type === 'GasGiant') return 'gas giant';
  if (b.type === 'Asteroid') return 'asteroid';
  const c = classifyC(b), water = b.water, alive = b.bio, thickAir = b.atm >= 4;
  // NEW: an airless rock's frost line is 0 °C, not the "no liquid window" sentinel (-270) — so a -180 °C
  // airless rock reads "frozen rock" rather than plain "barren world".
  const frozenLine = mode === 'new' && b.atm < LIFE_FLOOR ? 0 : freezeC(b.atm);
  switch (b.type) {
    case 'Volcanic': return bodyAvgC(b) >= MAGMA_MIN_C ? 'molten world' : 'volcanic world';
    case 'Ice': return 'frozen world';
    case 'Ocean': return water >= OCEAN_WATER ? 'ocean world' : 'archipelago world';   // 2026-10-11: was 0.95
    case 'Barren':
      if (b.atm >= GH_THRESHOLD) return 'greenhouse world';
      if (thickAir && c > 60) return 'toxic world';
      if (c > 90) return 'scorched world';
      if (c < frozenLine) return 'frozen rock';
      return 'barren world';
    case 'Moon':
      // NEW: a small airless moon is named for what it is. It used to fall through to the temperate
      // branch and come out a "desert moon" or "barren rocky moon".
      if (mode === 'new') {
        if (c > 90) return 'scorched world';
        if (c < 0 && water >= LAND_WATER) return 'icy world';
        return 'airless world';
      }
    // falls through (OLD)
    default:
      if (!alive) return water < LAND_WATER ? 'desert world' : 'barren rocky world';
      if (c < 4) return 'tundra world';
      if (water >= OCEAN_WATER) return 'ocean world';
      if (water >= 0.62) return 'archipelago world';
      if (b.moist >= 1.05 && c >= 12 && water >= 0.3) return 'swamp world';
      if (water < 0.2 || b.moist < 0.85) return c > 24 ? 'desert world' : 'savanna world';
      if (water > 0.5) return 'continental world';
      return 'terran world';
  }
}
const pretty = (b, mode) => { const n = describe(b, mode); return !b.moon ? n : n.endsWith(' world') ? n.slice(0, -6) + ' moon' : n + ' moon'; };

function generatesBio(b) {
  if (!(b.type === 'Rocky' || b.type === 'Ocean')) return false;
  if (b.atm < LIFE_FLOOR) return false;
  const c = bodyAvgC(b), fz = freezeC(b.atm), bl = boilC(b.atm);
  if (!(bl > fz && c >= fz && c <= bl)) return false;
  if (!(c >= MIN_LIQ && c <= MAX_LIQ)) return false;
  return b.water >= MIN_WATER;
}

function amplify(b, rel, mode) {
  if (b.type !== 'Rocky') return;
  const cls = describe(b, mode);
  const { lo, hi } = rel > 0 ? heatBand(rel) : { lo: 0, hi: Infinity };
  if (cls === 'desert world') { b.moist = Math.min(b.moist, 0.55); b.heat = Math.max(b.heat, Math.min(1.15, hi)); }
  else if (cls === 'savanna world') b.moist = clamp(b.moist, 0.7, 1.0);
  else if (cls === 'swamp world') { b.moist = Math.max(b.moist, 1.2); b.water = clamp(b.water, 0.42, 0.58); }
  else if (cls === 'archipelago world') b.water = clamp(b.water, 0.62, 0.76);
  else if (cls === 'tundra world') b.heat = Math.min(b.heat, Math.max(0.75, lo));
}

// GeothermalMap.WorldIntensity, approximated: the strongest plate (4-13 plates, strength 0.35-1) or the
// hotspot roll (a dead zone under 0.42, then the type ceiling, x0.72 on a plate world).
function geothermal(b) {
  let plates = 0;
  if (b.tect) { const n = 4 + Math.floor(rnd() * 5); for (let i = 0; i < n; i++) plates = Math.max(plates, lerp(0.35, 1, rnd())); }
  const ceiling = b.type === 'GasGiant' || b.type === 'Asteroid' ? 0 : b.type === 'Ice' ? 0.8 : b.type === 'Moon' ? 0.7 : 1;
  const h = rnd();
  const hot = h < 0.42 ? 0 : invLerp(0.42, 1, h) * ceiling * (b.tect ? 0.72 : 1);
  b.hot = hot;
  return Math.max(plates, hot);
}

// ---- ApplyWorldPipeline ------------------------------------------------------------------------
function provisional(mass, isMoon) { if (mass >= GIANT_FLOOR) return 'GasGiant'; if (mass <= AST_CEIL) return isMoon ? 'Moon' : 'Asteroid'; return 'Rocky'; }
const starAllowsLife = star => !star.blackHole && star.hasZone;

function pipeline(b, star, rel, isMoon, mode) {
  b.moon = isMoon; b.rel = rel;
  b.type = provisional(b.mass, isMoon);
  b.spin = rollSpin(b.mass, isMoon);
  b.locked = rnd() < lockChance(b.type, rel, isMoon, star, b.d, mode);
  if (b.locked) b.spin = 0;
  b.field = fieldOf(b.type, b.mass, b.spin);
  b.tect = rollTectonics(b.type, b.mass);
  // TerrainVariance: scale, elevation, seaLevel, moisture, heat — heat and sea are overwritten below.
  range(0.75, 1.55); range(0.80, 1.35); range(0.36, 0.62); b.moist = range(0.65, 1.45); range(0.65, 1.45);
  b.tectBonus = b.tect ? lerp(TECT_MIN, TECT_MAX, rnd()) : 0;
  b.heat = rollHeat(star, rel, mode);
  b.water = rel < HZ_IN ? range(0.02, 0.15) : range(0.10, 1.00);
  b.atm = rollAtm(b, rel);
  b.runaway = false;
  rollRunaway(b, rel);
  waterLoss(b);
  [b.type, b.cause] = physics(b, rel, isMoon, mode);
  b.bio = generatesBio(b) && (mode === 'old' || starAllowsLife(star));
  amplify(b, rel, mode);
  b.geo = geothermal(b);
}

// EnsureHabitableWorld, for the default species (Terrans, band 0.89-1.10 R — Habitability.GetZone).
function ensureHabitable(system, star, mode) {
  if (!star.hasZone) return;
  const inner = Math.max(star.hzInner, 0.89 * star.ref), outer = Math.min(star.hzOuter, 1.10 * star.ref);
  if (system.some(b => b.d >= inner && b.d <= outer && (b.type === 'Rocky' || b.type === 'Ocean'))) return;
  const centre = (inner + outer) / 2;
  let best = null;
  for (const b of system) if (!b.belt && (!best || Math.abs(b.d - centre) < Math.abs(best.d - centre))) best = b;
  if (!best) return;
  const radii = ringRadii(star);
  let chosen = -1, cd = Infinity;
  for (const r of radii) if (r >= inner && r <= outer && Math.abs(r - centre) < cd) { cd = Math.abs(r - centre); chosen = r; }
  if (chosen > 0) best.d = chosen;
  const rel = relOf(star, best.d);
  best.mass = qTerr(range(0.9, 2.6));
  best.spin = Math.max(FIELD_SPIN + 2, rollSpin(best.mass, false));
  best.field = true; best.locked = false; best.type = 'Rocky';
  best.tect = rollTectonics('Rocky', best.mass);
  range(0.75, 1.55); range(0.80, 1.35); range(0.36, 0.62); best.moist = range(0.65, 1.45); range(0.65, 1.45);
  best.tectBonus = best.tect ? lerp(TECT_MIN, TECT_MAX, rnd()) : 0;
  best.heat = rollHeat(star, rel, mode);
  best.water = range(0.35, 0.75);
  best.atm = rollAtm(best, rel);
  best.runaway = false;
  waterLoss(best);
  [best.type, best.cause] = physics(best, rel, false, mode);
  best.bio = generatesBio(best);
  amplify(best, rel, mode);
  best.geo = geothermal(best);
  best.forced = true;
  for (const m of best.moons) m.d = best.d;
}

// ---- WorldModifiers.Roll --------------------------------------------------------------------
function rollModifiers(b, mode) {
  b.mods = new Set();
  if (b.type === 'GasGiant' || b.type === 'Asteroid') return;
  const active = b.tect || b.hot >= 0.35;
  if (mode === 'old') {
    if (rnd() < 0.06) b.mods.add('HQM');
    if (rnd() < 0.06) b.mods.add('VMD');
    if (b.atm >= WEATHER_MIN_ATM && rnd() < 0.08) b.mods.add('Weather');
    return;
  }
  if (rnd() < (active ? 0.09 : 0.03)) b.mods.add('HQM');
  if (rnd() < (b.tect ? 0.10 : 0.045)) b.mods.add('VMD');
  if (b.atm >= WEATHER_MIN_ATM && rnd() < weatherChance(b)) b.mods.add('Weather');
}
function weatherDrivers(b) {
  const d = [];
  if (b.locked) d.push('locked');
  if (b.spin >= 30) d.push('fast-spin');
  if (b.water >= 0.5 && bodyAvgC(b) >= 15) d.push('warm-seas');
  if (b.atm >= 3) d.push('thick-air');
  return d;
}
function weatherChance(b) {
  const d = weatherDrivers(b);
  return Math.min(0.25, 0.04 + (d.includes('locked') ? 0.10 : 0) + (d.includes('fast-spin') ? 0.03 : 0)
                       + (d.includes('warm-seas') ? 0.05 : 0) + (d.includes('thick-air') ? 0.03 : 0));
}

// ---- one system ------------------------------------------------------------------------------
function chooseLane(rel, budgetLeft, beltsAllowed, giantsPlaced) {
  const beyondFrost = rel >= FROST;
  if (giantCeiling(budgetLeft * 0.92) > 0 && rnd() < (beyondFrost ? OUTER_GIANT : HOT_GIANT)) return 'giant';
  if (!beltsAllowed) return 'terrestrial';
  let belt = 0.10;
  if (rel > FROST * 0.75 && rel < FROST * 1.45) belt = 0.28;
  if (budgetLeft < TERR_MAX) belt = 0.50;
  if (budgetLeft < TERR_MIN * 2) return 'belt';
  if (beyondFrost && giantsPlaced === 0) belt *= 1.5;
  return rnd() < belt ? 'belt' : 'terrestrial';
}
function chooseFilled(habitableRing) {
  const fill = new Array(RING_COUNT).fill(false);
  const target = rnd() < SPARSE_CHANCE ? 1 + Math.floor(rnd() * 3) : clamp(1 + Math.round(bell() * 8), 1, RING_COUNT);
  let chosen = 0;
  if (habitableRing >= 0) { fill[habitableRing] = true; chosen = 1; }
  while (chosen < target) {
    let total = 0; for (let i = 0; i < RING_COUNT; i++) if (!fill[i]) total += RING_WEIGHT[i];
    if (total <= 0) break;
    let r = rnd() * total;
    for (let i = 0; i < RING_COUNT; i++) { if (fill[i]) continue; r -= RING_WEIGHT[i]; if (r > 0) continue; fill[i] = true; chosen++; break; }
  }
  return fill;
}
function generate(cls, mode) {
  const star = rollStar(cls);
  let budget = clamp(SSM * Math.max(0.05, star.mass), BUDGET_MIN, BUDGET_MAX);
  const radii = ringRadii(star);
  const inZone = radii.map(r => star.hasZone && r >= star.hzInner && r <= star.hzOuter);
  let habitableRing = -1;
  if (star.hasZone) {
    habitableRing = inZone.indexOf(true);
    if (habitableRing < 0) { let bd = Infinity; const c = (star.hzInner + star.hzOuter) / 2; radii.forEach((r, i) => { if (Math.abs(r - c) < bd) { bd = Math.abs(r - c); habitableRing = i; } }); }
  }
  const fill = chooseFilled(habitableRing);
  for (let r = 0; r < INNER_RINGS; r++) if (inZone[r] && r !== habitableRing) fill[r] = false;
  const system = [];
  let clearedTo = 0, planets = 0, belts = 0, giants = 0;
  const beltCap = (() => { const r = rnd(); return r < 0.62 ? 1 : r < 0.87 ? 2 : r < 0.97 ? 3 : 4; })();
  for (let ring = 0; ring < RING_COUNT; ring++) {
    if (!fill[ring]) continue;
    const radius = radii[ring];
    if (radius < clearedTo && ring !== habitableRing) continue;
    const pending = habitableRing >= 0 && ring <= habitableRing;
    const reserve = (pending && ring < habitableRing) ? TERR_MIN : 0;
    let spendable = Math.max(0, budget - reserve);
    if (spendable < AST_MIN) { if (!pending) break; continue; }
    const rel = radius / Math.max(0.5, star.ref);
    const kind = ring === habitableRing ? 'terrestrial' : chooseLane(rel, spendable, planets >= 3 && belts < beltCap, giants);
    let reach = 0;
    if (kind === 'belt') {
      const wanted = 3 + Math.floor(rnd() * 5); let placed = 0;
      for (let a = 0; a < wanted; a++) {
        if (spendable < AST_MIN) break;
        const rock = { d: radius, ring, belt: true, moons: [] };
        rock.mass = rollAsteroid(spendable); budget -= rock.mass; spendable -= rock.mass;
        pipeline(rock, star, rel, false, mode);
        rollSpin(rock.mass, false);   // the belt re-rolls its spin after the pipeline
        system.push(rock); placed++; reach = Math.max(reach, 0.3);
      }
      if (!placed) break;
      belts++;
    } else {
      const body = { d: radius, ring, belt: false, moons: [] };
      if (kind === 'giant') { const g = rollGiant(spendable * 0.92); body.mass = g > 0 ? g : rollTerrestrial(terrestrialBandMax(rel), spendable); }
      else body.mass = rollTerrestrial(terrestrialBandMax(rel), spendable);
      budget -= body.mass; spendable -= body.mass;
      body.hotGiant = kind === 'giant' && rel < FROST;
      pipeline(body, star, rel, false, mode);
      const giantHost = body.mass >= GIANT_FLOOR;
      let pot = Math.min(moonBudget(body.mass), spendable);
      if (rnd() < (giantHost ? MOONLESS.giant : MOONLESS.terr)) pot = 0;
      for (let m = 0; m < (giantHost ? MAX_MOONS.giant : MAX_MOONS.terr); m++) {
        if (pot < AST_MIN) break;
        if (m > 0 && rnd() < MOON_STOP) break;
        const mm = rollMoon(giantHost ? Math.min(pot, GIANT_MOON_MAX) : pot);
        if (mm < AST_MIN) break;
        pot -= mm; budget -= mm;
        const moon = { d: radius, ring, belt: false, moons: [], mass: mm, host: body };
        pipeline(moon, star, rel, true, mode);
        body.moons.push(moon);
      }
      system.push(body);
      planets++; if (giantHost) giants++;
      reach = 2 + body.moons.length * 2.4;
    }
    clearedTo = radius + reach + LANE_GAP;
  }
  ensureHabitable(system, star, mode);
  const all = [];
  for (const b of system) { all.push(b); for (const m of b.moons) all.push(m); }
  for (const b of all) { rollModifiers(b, mode); b.name = pretty(b, mode); b.rel = relOf(star, b.d); }
  return { star, bodies: all };
}

// ---- the audit: every world's CAUSE, and the flags ----------------------------------------------
// A world is FLAGGED when nothing in the rules can explain it — the cases the request says must not
// generate. Every other world gets a named cause.
function flagsOf(b, star, mode) {
  const f = [];
  const liquid = b.type === 'Rocky' || b.type === 'Ocean';
  if (star.blackHole && (liquid || b.bio)) f.push('liquid water / life around a black hole, which gives no warmth');
  if (!star.blackHole && !star.hasZone && b.bio) f.push(`life around an ${star.cls}-type star, too young and fierce for a stable zone`);
  if (b.locked && tide(star, b.d) < TIDE_MIN) f.push('tidally locked where its star\'s tides are too weak to stop a spin');
  if (b.type === 'Moon' && /desert|barren rocky/.test(b.name)) f.push('an airless moon named as if it had dry land under air');
  if (b.type === 'Ice' && b.rel < HZ_IN && !b.forced) f.push('a frozen world inside the zone\'s inner edge');
  if (b.type === 'Volcanic' && b.rel >= HZ_IN && !b.forced) f.push('a volcanic world outside the inner edge');
  if (b.bio && star.hasZone && (b.rel < HZ_IN || b.rel > HZ_OUT)) f.push('life outside the habitable zone');
  if ((b.type === 'Ice' || /frozen|icy/.test(b.name)) && star.hasZone && b.rel >= HZ_IN && b.rel <= HZ_OUT && starlightC(b.heat) >= 0 && !b.forced)
    f.push('frozen in the zone although its starlight alone is above freezing');
  if (b.runaway && b.rel >= HZ_IN) f.push('a runaway greenhouse in or beyond the zone');
  if (b.type === 'Moon' && b.atm >= LIFE_FLOOR) f.push('air on a body classified airless');
  if (b.mods.has('Weather') && mode === 'new' && b.atm < WEATHER_MIN_ATM) f.push('storms without air');
  return f;
}

// The cause of every paradoxical-sounding world, in words the explanation builder also uses.
function causeOf(b, star) {
  const inZone = star.hasZone && b.rel >= HZ_IN && b.rel <= HZ_OUT;
  const sC = starlightC(b.heat);
  const frozen = b.type === 'Ice' || /frozen|icy/.test(b.name);
  if (frozen && inZone) {
    const g = thick(b.atm) * GREENHOUSE;
    const air = b.atm < LIFE_FLOOR ? (b.mass <= AST_CEIL ? 'too small to hold air' : !b.field ? 'no magnetic field, so its air was stripped' : 'thin air')
              : g < 10 ? 'its air adds under 10 °C' : 'its air adds 10-25 °C, still short';
    return `frozen IN the zone, ring ${b.ring + 1}: starlight ${sC < 0 ? 'below' : 'above'} freezing; ${air}`;
  }
  if (b.type === 'GasGiant') return b.rel < FROST ? 'hot giant: formed past the frost line, migrated inward' : 'giant: formed past the frost line';
  if ((b.type === 'Rocky' || b.type === 'Ocean') && !b.bio) {
    const c = bodyAvgC(b);
    if (!star.hasZone || star.blackHole) return 'sterile: its star allows no stable zone';
    if (b.atm < LIFE_FLOOR) return 'sterile: air too thin';
    if (b.water < MIN_WATER) return 'sterile: too dry';
    if (c > MAX_LIQ) return 'sterile: too hot for life to begin (over 50 °C)';
    if (c < MIN_LIQ) return 'sterile: too cold for life to begin';
    return 'sterile: other';
  }
  return null;
}

// ---- run ---------------------------------------------------------------------------------------
const CLS = ['M', 'K', 'G', 'F', 'A', 'B', 'O', 'BH'];
const ODDS = { M: 0.45 * 0.985, K: 0.20 * 0.985, G: 0.15 * 0.985, F: 0.10 * 0.985, A: 0.06 * 0.985, B: 0.03 * 0.985, O: 0.01 * 0.985, BH: 0.015 };

function sweep(mode) {
  const res = { byRing: {}, byStar: {}, total: {}, flags: {}, causes: {}, mods: { HQM: 0, VMD: 0, Weather: 0, solid: 0, locked: 0, planets: 0 },
                lockedByZone: { inside: [0, 0], zone: [0, 0], beyond: [0, 0] }, lockedByStar: {}, worlds: 0, examples: {} };
  CLS.forEach((cls, ci) => {
    rnd = mulberry32(9001 + ci * 7919);
    const runs = cls === 'BH' ? Math.max(200, Math.round(N / 4)) : N;
    for (let s = 0; s < runs; s++) {
      const { star, bodies } = generate(cls, mode);
      for (const b of bodies) {
        const w = (ODDS[cls] / runs);
        const key = b.name;
        res.worlds++;
        (res.byStar[cls] ||= {})[key] = (res.byStar[cls][key] || 0) + 1;
        const ring = b.ring + 1;
        (res.byRing[ring] ||= {})[key] = (res.byRing[ring][key] || 0) + w;
        res.total[key] = (res.total[key] || 0) + w;
        for (const f of flagsOf(b, star, mode)) { res.flags[f] = (res.flags[f] || 0) + 1; }
        const c = causeOf(b, star);
        if (c) res.causes[c.replace(/\(.*?\)/, '').replace(/\s+/g, ' ')] = (res.causes[c.replace(/\(.*?\)/, '').replace(/\s+/g, ' ')] || 0) + 1;
        if (b.type !== 'GasGiant' && b.type !== 'Asteroid') {
          res.mods.solid++;
          for (const m of b.mods) res.mods[m]++;
        }
        if (!b.moon && b.type !== 'GasGiant' && b.type !== 'Asteroid') {
          res.mods.planets++;
          const z = b.rel < HZ_IN ? 'inside' : b.rel <= HZ_OUT ? 'zone' : 'beyond';
          res.lockedByZone[z][1]++; if (b.locked) { res.lockedByZone[z][0]++; res.mods.locked++; }
          const ls = (res.lockedByStar[cls] ||= [0, 0]); ls[1]++; if (b.locked) ls[0]++;
        }
        if (!res.examples[key]) res.examples[key] = { cls, ring, c: bodyAvgC(b).toFixed(0), atm: b.atm, water: b.water.toFixed(2), mass: b.mass };
      }
    }
  });
  return res;
}

const OLD = sweep('old');
const NEW = sweep('new');

const pct = (n, d) => d > 0 ? (100 * n / d).toFixed(1) + '%' : '-';
const KINDS = [...new Set([...Object.keys(OLD.total), ...Object.keys(NEW.total)])].sort((a, b) => (NEW.total[b] || 0) - (NEW.total[a] || 0));

console.log(`${N} systems per star class (black holes: ${Math.max(200, Math.round(N / 4))}), single stars, weighted by RollStarType for the galaxy-wide share.\n`);

console.log('1. GALAXY-WIDE SHARE OF EVERY WORLD CLASS (planets, moons and belt rocks), OLD -> NEW');
{
  const tO = Object.values(OLD.total).reduce((a, b) => a + b, 0), tN = Object.values(NEW.total).reduce((a, b) => a + b, 0);
  for (const k of KINDS) console.log(`   ${k.padEnd(24)} ${pct(OLD.total[k] || 0, tO).padStart(7)} -> ${pct(NEW.total[k] || 0, tN).padStart(7)}`);
}

function ringTable(res, title) {
  console.log(`\n${title}`);
  const rings = Array.from({ length: RING_COUNT }, (_, i) => i + 1);
  const top = KINDS.filter(k => (res.total[k] || 0) > 0);
  console.log('   ' + 'class'.padEnd(24) + rings.map(r => `ring ${r}`.padStart(8)).join(''));
  for (const k of top) {
    const row = rings.map(r => { const t = Object.values(res.byRing[r] || {}).reduce((a, b) => a + b, 0); const v = (res.byRing[r] || {})[k] || 0; return (t && v / t >= 0.0005 ? (100 * v / t).toFixed(1) : '.').padStart(8); });
    console.log('   ' + k.padEnd(24) + row.join(''));
  }
  console.log(`   (rings 1-${INNER_RINGS} inside the zone's inner edge, 4-5 in the zone, 6-9 beyond; frost line at ${FROST}x — ring 7 on)`);
}
if (!QUIET) ringTable(OLD, '2a. BY RING, OLD (share of the worlds on that ring, %)');
ringTable(NEW, '2b. BY RING, NEW (share of the worlds on that ring, %)');

function starTable(res, title) {
  console.log(`\n${title}`);
  const top = KINDS.filter(k => (res.total[k] || 0) > 0);
  console.log('   ' + 'class'.padEnd(24) + CLS.map(c => c.padStart(7)).join(''));
  for (const k of top) {
    const row = CLS.map(c => { const t = Object.values(res.byStar[c] || {}).reduce((a, b) => a + b, 0); const v = (res.byStar[c] || {})[k] || 0; return (t && v / t >= 0.0005 ? (100 * v / t).toFixed(1) : '.').padStart(7); });
    console.log('   ' + k.padEnd(24) + row.join(''));
  }
}
if (!QUIET) starTable(OLD, '3a. BY STAR CLASS, OLD (share of that class\'s worlds, %)');
starTable(NEW, '3b. BY STAR CLASS, NEW (share of that class\'s worlds, %)');

console.log('\n4. TIDAL LOCK (planets only) — OLD: 10% / 3% / 1% by zone band, whatever the star. NEW: by the star\'s tide.');
for (const z of ['inside', 'zone', 'beyond'])
  console.log(`   ${z.padEnd(8)} ${pct(OLD.lockedByZone[z][0], OLD.lockedByZone[z][1]).padStart(7)} -> ${pct(NEW.lockedByZone[z][0], NEW.lockedByZone[z][1]).padStart(7)}`);
console.log('   by star: ' + CLS.filter(c => c !== 'BH').map(c => `${c} ${pct(OLD.lockedByStar[c][0], OLD.lockedByStar[c][1])}->${pct(NEW.lockedByStar[c][0], NEW.lockedByStar[c][1])}`).join('  '));

console.log('\n5. WORLD MODIFIERS (share of solid worlds), OLD -> NEW');
for (const m of ['HQM', 'VMD', 'Weather'])
  console.log(`   ${m.padEnd(8)} ${pct(OLD.mods[m], OLD.mods.solid).padStart(7)} -> ${pct(NEW.mods[m], NEW.mods.solid).padStart(7)}`);

console.log('\n6. THE CAUSE OF EVERY PARADOXICAL-SOUNDING WORLD (NEW, counts)');
for (const [k, v] of Object.entries(NEW.causes).sort((a, b) => b[1] - a[1])) console.log(`   ${String(v).padStart(7)}  ${k}`);

console.log('\n7. UNJUSTIFIED WORLDS — combinations no rule explains');
console.log('   OLD:');
if (!Object.keys(OLD.flags).length) console.log('        none');
for (const [k, v] of Object.entries(OLD.flags).sort((a, b) => b[1] - a[1])) console.log(`   ${String(v).padStart(7)}  ${k}`);
console.log('   NEW:');
if (!Object.keys(NEW.flags).length) console.log('        none');
for (const [k, v] of Object.entries(NEW.flags).sort((a, b) => b[1] - a[1])) console.log(`   ${String(v).padStart(7)}  ${k}`);

let fails = 0;
const ok = (c, m) => { console.log(`${c ? 'ok  ' : 'FAIL'}  ${m}`); if (!c) fails++; };
console.log('');
ok(Object.keys(NEW.flags).length === 0, 'the NEW pipeline generates no world without a cause');
ok((NEW.lockedByZone.beyond[0] / Math.max(1, NEW.lockedByZone.beyond[1])) < (NEW.lockedByZone.inside[0] / Math.max(1, NEW.lockedByZone.inside[1])), 'close worlds lock more often than far ones');
ok(NEW.lockedByStar.M[0] / NEW.lockedByStar.M[1] > NEW.lockedByStar.G[0] / NEW.lockedByStar.G[1], 'red-dwarf worlds lock more often than Sun-like ones (their zone is close in)');
const wN = NEW.mods.Weather / NEW.mods.solid;
ok(wN > 0.02 && wN < 0.10, `Extreme Weather stays uncommon (${(100 * wN).toFixed(1)}% of solid worlds)`);
process.exit(fails ? 1 : 0);
