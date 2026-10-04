// ============================================================================================
// THE WORLD RULES, CHECKED AGAINST THE 2026-10-04 REPORTS
//
//   node tools/world-rules-check.mjs
//
// Ports of the arithmetic behind four fixes, asserted against what the reports asked for:
//
//   * Habitability.Rate      — a 500 °C airless world must not outrate a 40 °C living Terran world
//   * PlanetTemperature.TidalOffsetC — a tidally locked world's day side hot, night side far colder
//   * StarDatabase.Recombine — a multi-star system's zone comes from its HOTTEST star
//   * the zone itself        — fixed per star: the same star always gives the same band
//
// Species numbers are READ OUT OF Data/Species.cs so this cannot drift from the game. Exits 1 on any
// failure.
// ============================================================================================
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SPECIES = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Data/Species.cs'), 'utf8');

let fails = 0, passes = 0;
const ok = (cond, msg) => { if (cond) passes++; else { fails++; console.log(`  FAIL  ${msg}`); } };
const lerp = (a, b, t) => a + (b - a) * Math.min(1, Math.max(0, t));
const clamp01 = v => Math.min(1, Math.max(0, v));

// ---- species, parsed ------------------------------------------------------------------------
function species(name) {
  const i = SPECIES.indexOf(`name = "${name}"`);
  if (i < 0) throw new Error(`no species ${name}`);
  const block = SPECIES.slice(i, SPECIES.indexOf('};', i));
  const f = re => { const m = re.exec(block); if (!m) throw new Error(`${name}: ${re}`); return parseFloat(m[1]); };
  return {
    name,
    idealTemp: f(/idealTemp = ([\d.]+)f/), tolerance: f(/tolerance = ([\d.]+)f/),
    minAtm: f(/minAtmospheres = ([\d.]+)f/), maxAtm: f(/maxAtmospheres = ([\d.]+)f/),
  };
}
const Terran = species('Terrans'), Pyro = species('Pyrothians'), Cryithn = species('Cryithn');
// Body-type affinities and which species are liquid-water life, as Species.cs / CradleWantsLife state.
const AFF = {
  Terrans: { Rocky: 1.0, Volcanic: 0.25, Barren: 0.35 },
  Pyrothians: { Rocky: 0.5, Volcanic: 1.0, Barren: 0.8 },
  Cryithn: { Rocky: 0.5, Ice: 1.0, Barren: 0.7 },
};
const WANTS_LIFE = { Terrans: true, Pyrothians: false, Cryithn: false };
// Water and life are asked only of liquid-water life (species whose cradle is a living world).
const NEEDS_WATER = { Terrans: true, Pyrothians: false, Cryithn: false };
const NEEDS_LIFE = { Terrans: true, Pyrothians: false, Cryithn: false };
const CRADLE = { Terrans: 1.0, Pyrothians: 3.5, Cryithn: 0.9 };

// ---- Habitability.Rate, ported ----------------------------------------------------------------
const airSuit = (s, a) => {
  if (a >= s.minAtm && a <= s.maxAtm) return 1;
  const slack = Math.max(0.35, s.tolerance) * 1.5;
  const d = a < s.minAtm ? s.minAtm - a : a - s.maxAtm;
  return clamp01(1 - d / slack);
};
const idealC = s => WANTS_LIFE[s.name] ? lerp(0 + 5, 50 - 8, s.idealTemp) : lerp(-150, 450, s.idealTemp);
const width = (s, ideal) => 15 * Math.min(1.6, Math.max(0.6, s.tolerance)) + Math.abs(ideal - 20) * 0.25;
function rate(s, w) {
  const ideal = idealC(s);
  const off = Math.abs(w.c - ideal) / width(s, ideal);
  const temp = Math.exp(-0.7 * off * off);
  const air = lerp(0.25, 1, airSuit(s, w.atm));
  const water = NEEDS_WATER[s.name] && !w.liquid ? 0.45 : 1;
  const life = NEEDS_LIFE[s.name] && !w.bio ? 0.65 : 1;
  const home = CRADLE[s.name];
  const gravity = w.mass < home * 0.6 ? lerp(0.55, 1, w.mass / (home * 0.6))
                : w.mass > home * 1.8 ? lerp(1, 0.7, (w.mass - home * 1.8) / (home * 1.5)) : 1;
  const kind = lerp(0.6, 1, AFF[s.name][w.type] ?? 0.3);
  return Math.min(100, 100 * temp * air * water * life * gravity * kind);
}

console.log('habitability');
const furnace = { c: 500, atm: 0, liquid: false, bio: false, mass: 1, type: 'Volcanic' };
const living40 = { c: 40, atm: 1, liquid: true, bio: true, mass: 1, type: 'Rocky' };
const earth = { c: 22, atm: 1, liquid: true, bio: true, mass: 1, type: 'Rocky' };
const r1 = rate(Terran, furnace), r2 = rate(Terran, living40), r3 = rate(Terran, earth);
ok(r2 > r1, `the report's case: 40 °C living world (${r2.toFixed(0)}%) outrates a 500 °C airless one (${r1.toFixed(0)}%)`);
ok(r1 < 1, `a 500 °C airless world is uninhabitable for Terrans (${r1.toFixed(1)}%)`);
ok(r3 > 90, `an Earth-like world rates near the top for Terrans (${r3.toFixed(0)}%)`);
ok(r2 > 25 && r2 < 75, `40 °C is livable but uncomfortable for Terrans (${r2.toFixed(0)}%)`);
ok(rate(Terran, { ...earth, bio: false }) < r3, 'a sterile world rates below a living one');
ok(rate(Terran, { ...earth, atm: 0, liquid: false }) < 30, `no air (so no liquid water) is a heavy penalty (${rate(Terran, { ...earth, atm: 0, liquid: false }).toFixed(0)}%)`);
ok(rate(Terran, { ...earth, c: -40, liquid: false }) < 15, 'a frozen world rates poorly');
const pyroHome = { c: 360, atm: 5, liquid: false, bio: false, mass: 3.5, type: 'Volcanic' };
ok(rate(Pyro, pyroHome) > rate(Pyro, earth), `Pyrothians prefer a furnace (${rate(Pyro, pyroHome).toFixed(0)}%) to an Earth (${rate(Pyro, earth).toFixed(0)}%)`);
const iceHome = { c: -55, atm: 1.5, liquid: false, bio: false, mass: 0.9, type: 'Ice' };
ok(rate(Cryithn, iceHome) > rate(Cryithn, { ...earth, c: 60 }), 'Cryithn prefer the cold');
// The review's catch: species that are not liquid-water life must be able to rate their own kind of home
// above the colonisation (45) and rival-homeworld (40) gates.
ok(rate(Cryithn, iceHome) > 60, `a Cryithn ice world rates well (${rate(Cryithn, iceHome).toFixed(0)}%) — not capped by water/life it cannot have`);
ok(rate(Pyro, pyroHome) > 60, `a Pyrothian furnace on a 3.5-Earth world rates well (${rate(Pyro, pyroHome).toFixed(0)}%) — its own gravity is not crushing to it`);

// ---- PlanetTemperature.TidalOffsetC, ported -------------------------------------------------
console.log('tidal lock');
function tidal(atm, u, v) {
  const air = clamp01(atm / 3);
  const day = lerp(110, 35, air), night = lerp(170, 55, air);
  const cosZ = Math.cos((v - 0.5) * Math.PI) * Math.cos((u - 0.5) * 2 * Math.PI);
  return cosZ >= 0 ? day * Math.sqrt(cosZ) : -night * Math.min(1, -cosZ * 3);
}
ok(tidal(0, 0.5, 0.5) > 100, 'airless: the point under the star is far hotter than normal');
ok(tidal(0, 0.0, 0.5) < -150, 'airless: the far side is far colder than normal');
ok(tidal(3, 0.5, 0.5) < tidal(0, 0.5, 0.5) && tidal(3, 0, 0.5) > tidal(0, 0, 0.5), 'thick air evens the two faces out');
ok(Math.abs(tidal(1, 0.25, 0.5)) < 1, 'the terminator sits near the normal temperature');
let mono = true; for (let u = 0.5; u > 0; u -= 0.01) if (tidal(1, u - 0.01, 0.5) > tidal(1, u, 0.5) + 1e-9) mono = false;
ok(mono, 'temperature falls steadily from the hot point to the cold one');

// ---- StarDatabase.Recombine / ApplyZone, ported ---------------------------------------------
console.log('habitable zone');
const AU = 40, HZ_IN = 0.80, HZ_OUT = 1.55;
const flux = lum => Math.min(3, Math.max(0.45, Math.pow(Math.max(0.02, lum), 0.3)));
const zone = st => ({ inner: HZ_IN * flux(st.lum) * AU, outer: HZ_OUT * flux(st.lum) * AU, has: !['O', 'B'].includes(st.type) });
const recombine = stars => { const hot = stars.reduce((a, b) => (b.tempK > a.tempK ? b : a)); return zone({ ...hot }); };
const g = { type: 'G', tempK: 5700, lum: 1.0 }, k = { type: 'K', tempK: 4500, lum: 0.3 }, b = { type: 'B', tempK: 20000, lum: 2000 };
const zg = zone(g), zgk = recombine([k, g]);
ok(zgk.inner === zg.inner && zgk.outer === zg.outer, 'a G+K binary uses the G star (the hotter) for its zone');
ok(!recombine([g, b]).has, 'a system with a B companion has no stable zone (the hottest star is a blue giant)');
ok(JSON.stringify(zone(g)) === JSON.stringify(zone({ ...g })), 'the same star always gives the same zone');
// The temperature law the zone is anchored on: liquid water at the inner edge, freezing past the outer.
const kelvin = rel => 288.15 / Math.sqrt(rel);
ok(kelvin(HZ_IN) - 273.15 < 50 && kelvin(HZ_IN) - 273.15 > 40, `inner edge is ~${(kelvin(HZ_IN) - 273.15).toFixed(0)} °C before greenhouse — hot but liquid`);
ok(kelvin(HZ_OUT) - 273.15 < 0, `outer edge is ${(kelvin(HZ_OUT) - 273.15).toFixed(0)} °C before greenhouse — liquid only under real air`);

console.log(`\n${passes} passed, ${fails} failed`);
process.exit(fails ? 1 : 0);
