// ============================================================================================
// DID THE LIVERY ACTUALLY COME OUT RIGHT?
//
//   node tools/verify-textures.mjs [--dir Art/MeshyTextured] [--verbose]
//
// Two questions, asked of every textured ship, measured rather than eyeballed:
//
//   1. IS IT TEXTURED WELL?  A generated albedo fails in recognisable ways — it comes back nearly
//      black, or blown out, or one flat colour with no detail. All three are measurable.
//
//   2. CAN THE PLAYER STILL RECOLOUR IT?  The livery scheme only works if the two accent colours can
//      be found again in the baked albedo and separated from each other. If Meshy ignored an accent,
//      or smeared both across the same hue, the mask comes out empty or ambiguous and that ship can
//      never take a player's colours.
//
// Question 2 is the one that cannot wait. A ship that merely looks a bit dull can be lived with; a
// ship whose accents did not land has to be re-textured BEFORE the mask extractor and the shader are
// built on top of it, or the whole recolouring feature quietly has holes in it.
//
// Judging this by looking at 140 thumbnails is exactly the sort of thing eyes are bad at — "is that
// 4% coral or 0.4% coral" is not a judgement anyone makes reliably a hundred and forty times. The
// numbers below are the same ones that caught the first failed Pyrothian test (0.141 brightness,
// 24.85% cyan where cyan was meant to be a 5% trim).
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const VERBOSE = argv.includes('--verbose');
const DIR = path.resolve(PROJ, arg('--dir', path.join('Art', 'MeshyTextured')));

const palette = JSON.parse(fs.readFileSync(path.join(PROJ, 'tools', 'civ-colors.json'), 'utf8'));
const RULES = palette.maskRules;

// ---- what counts as acceptable ---------------------------------------------------------------
// Deliberately loose. These are meant to catch textures that FAILED, not to enforce a house style —
// a ship that squeaks past at 6% primary is a judgement call for a human, and gets flagged WEAK
// rather than FAIL so it shows up in the list without forcing a re-spend.
const LIMITS = {
  minBrightness: 0.16,   // below this it is the near-black failure
  maxBrightness: 0.82,   // above this it is blown out and detail is gone
  minPrimaryPct: 6.0,    // aiming for ~30
  weakPrimaryPct: 14.0,
  minSecondaryPct: 0.4,  // aiming for ~5
  weakSecondaryPct: 1.5,
  maxSecondaryPct: 45.0, // secondary swamping the hull means the roles inverted
  minDetail: 0.035,      // stdev of luminance; a flat fill has almost none
};

function hsv(r, g, b) {
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
  let h = 0;
  if (d > 1e-6) {
    if (mx === r) h = 60 * (((g - b) / d) % 6);
    else if (mx === g) h = 60 * ((b - r) / d + 2);
    else h = 60 * ((r - g) / d + 4);
    if (h < 0) h += 360;
  }
  return [h, mx <= 1e-6 ? 0 : d / mx, mx];
}

const hueDist = (a, b) => { const d = Math.abs(a - b) % 360; return d > 180 ? 360 - d : d; };

async function analyse(albedoPath, civ) {
  const c = palette.civilizations[civ];
  const { data, info } = await sharp(albedoPath)
    .resize(256, 256, { fit: 'fill' })
    .removeAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });

  let n = 0, lumSum = 0, lumSqSum = 0, prim = 0, sec = 0, satCount = 0;
  const ch = info.channels;

  for (let i = 0; i < data.length; i += ch) {
    const r = data[i] / 255, g = data[i + 1] / 255, b = data[i + 2] / 255;
    const [h, s, v] = hsv(r, g, b);
    const lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
    n++; lumSum += lum; lumSqSum += lum * lum;

    if (s >= RULES.minSaturation && v >= RULES.minValue) {
      satCount++;
      if (hueDist(h, c.primary.keyHue) <= RULES.keyTolerance) prim++;
      else if (hueDist(h, c.secondary.keyHue) <= RULES.keyTolerance) sec++;
    }
  }

  const mean = lumSum / n;
  const detail = Math.sqrt(Math.max(0, lumSqSum / n - mean * mean));
  return {
    brightness: mean,
    detail,
    saturatedPct: (100 * satCount) / n,
    primaryPct: (100 * prim) / n,
    secondaryPct: (100 * sec) / n,
  };
}

function verdict(m) {
  const fails = [], warns = [];
  if (m.brightness < LIMITS.minBrightness) fails.push(`too dark (${m.brightness.toFixed(3)})`);
  if (m.brightness > LIMITS.maxBrightness) fails.push(`blown out (${m.brightness.toFixed(3)})`);
  if (m.detail < LIMITS.minDetail) fails.push(`flat, no detail (sd ${m.detail.toFixed(3)})`);
  if (m.primaryPct < LIMITS.minPrimaryPct) fails.push(`primary missing (${m.primaryPct.toFixed(1)}%)`);
  else if (m.primaryPct < LIMITS.weakPrimaryPct) warns.push(`primary weak (${m.primaryPct.toFixed(1)}%)`);
  if (m.secondaryPct < LIMITS.minSecondaryPct) fails.push(`secondary missing (${m.secondaryPct.toFixed(2)}%)`);
  else if (m.secondaryPct < LIMITS.weakSecondaryPct) warns.push(`secondary weak (${m.secondaryPct.toFixed(2)}%)`);
  if (m.secondaryPct > LIMITS.maxSecondaryPct) fails.push(`secondary swamped the hull (${m.secondaryPct.toFixed(1)}%) — roles inverted`);
  return { pass: fails.length === 0, fails, warns };
}

// ---- walk the output -----------------------------------------------------------------------
if (!fs.existsSync(DIR)) { console.error(`no such directory: ${DIR}`); process.exit(1); }

const rows = [];
for (const civ of fs.readdirSync(DIR).sort()) {
  const civDir = path.join(DIR, civ);
  if (!fs.statSync(civDir).isDirectory()) continue;
  if (!palette.civilizations[civ]) { console.log(`(skipping ${civ}: no palette entry)`); continue; }

  for (const unit of fs.readdirSync(civDir).sort()) {
    const uDir = path.join(civDir, unit);
    if (!fs.statSync(uDir).isDirectory()) continue;
    const albedo = fs.readdirSync(uDir).find(f => /_albedo\.png$/i.test(f));
    if (!albedo) { rows.push({ civ, unit, missing: true }); continue; }
    try {
      const m = await analyse(path.join(uDir, albedo), civ);
      rows.push({ civ, unit, m, v: verdict(m) });
    } catch (e) {
      rows.push({ civ, unit, error: e.message.split('\n')[0] });
    }
  }
}

// ---- report ---------------------------------------------------------------------------------
const c = palette.civilizations;
console.log(`\n${'unit'.padEnd(34)} ${'bright'.padStart(6)} ${'detail'.padStart(6)} ${'prim%'.padStart(6)} ${'sec%'.padStart(6)}  verdict`);
console.log('-'.repeat(96));

let pass = 0, warn = 0, fail = 0, other = 0;
for (const r of rows) {
  const label = `${r.civ}/${r.unit}`;
  if (r.missing) { console.log(`${label.padEnd(34)} ${'—'.padStart(6)} ${'—'.padStart(6)} ${'—'.padStart(6)} ${'—'.padStart(6)}  NO ALBEDO`); other++; continue; }
  if (r.error)   { console.log(`${label.padEnd(34)} ERROR ${r.error}`); other++; continue; }
  const { m, v } = r;
  const tag = !v.pass ? 'FAIL' : v.warns.length ? 'WEAK' : 'ok';
  if (!v.pass) fail++; else if (v.warns.length) warn++; else pass++;
  const notes = [...v.fails, ...v.warns].join('; ');
  if (VERBOSE || tag !== 'ok')
    console.log(`${label.padEnd(34)} ${m.brightness.toFixed(3).padStart(6)} ${m.detail.toFixed(3).padStart(6)} ` +
                `${m.primaryPct.toFixed(1).padStart(6)} ${m.secondaryPct.toFixed(2).padStart(6)}  ${tag}${notes ? '  — ' + notes : ''}`);
}

console.log('-'.repeat(96));
console.log(`ok ${pass}   weak ${warn}   FAIL ${fail}   other ${other}   (of ${rows.length})`);
console.log(`\nkey hues — ${Object.entries(c).map(([k, v]) => `${k}: ${v.primary.keyHue}/${v.secondary.keyHue}`).join('  ')}`);

const redo = rows.filter(r => r.v && !r.v.pass).map(r => `${r.civ}/${r.unit}`);
if (redo.length) {
  fs.writeFileSync(path.join(PROJ, 'tools', 'retexture-worklist.json'), JSON.stringify(redo, null, 2));
  console.log(`\n${redo.length} need re-texturing -> tools/retexture-worklist.json`);
}
