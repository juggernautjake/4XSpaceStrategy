// ============================================================================================
// DID THE LIVERY ACTUALLY COME OUT RIGHT?
//
//   node tools/verify-textures.mjs [--dir Art/Active] [--verbose]
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
const DIR = path.resolve(PROJ, arg('--dir', path.join('Art', 'Active')));

const palette = JSON.parse(fs.readFileSync(path.join(PROJ, 'tools', 'civ-colors.json'), 'utf8'));
const RULES = palette.maskRules;

// ---- what counts as acceptable ---------------------------------------------------------------
// Deliberately loose. These are meant to catch textures that FAILED, not to enforce a house style —
// a ship that squeaks past at 6% primary is a judgement call for a human, and gets flagged WEAK
// rather than FAIL so it shows up in the list without forcing a re-spend.
// RECALIBRATED against art that was actually accepted, rather than against the percentages the prompt
// asks for. Those turned out to be different numbers, and the prompt's are the wrong ones to test.
//
// The first pass demanded 14% primary before it stopped saying "weak", on the theory that the prompt
// asks for a third of the hull. Then a fleet came back that looks right — teal hulls, amber panels,
// magenta trim, obviously one civilization — and it scored 3 FAIL and 8 WEAK out of 15. A checker
// that flags good art is worse than no checker: it burns credits re-rolling ships that were fine and
// it trains you to ignore it.
//
// What actually matters is not how MUCH accent there is, it is whether there is enough of a coherent
// region for extract-color-masks.mjs to key and recolour. A few percent of contiguous panel is plenty;
// zero is not. So the floors sit where an accent has genuinely failed to appear, and the weak band
// flags "worth a look" rather than "wrong".
//
// Brightness and detail are unchanged — those caught the near-black Pyrothian and they were right to.
const LIMITS = {
  minBrightness: 0.16,   // below this it is the near-black failure
  maxBrightness: 0.82,   // above this it is blown out and detail is gone
  minPrimaryPct: 2.5,    // below this the accent effectively did not land
  weakPrimaryPct: 8.0,   // present but sparse — worth a glance, not a re-roll
  minSecondaryPct: 0.4,
  weakSecondaryPct: 1.5,
  maxSecondaryPct: 45.0, // secondary swamping the hull means the roles inverted
  minDetail: 0.035,      // stdev of luminance; a flat fill has almost none

  // ---- IS IT COLOURED AT ALL --------------------------------------------------------------
  //
  // The cheapest and most decisive test, added after auditing the models already sitting in the
  // Meshy account to see which could be reused instead of regenerated. The answer was almost none,
  // and this is the number that said so: the archived Terran Dreadnought — a genuinely good mesh —
  // carries an albedo that is ENTIRELY GREYSCALE. It scores 0.0% here.
  //
  // That is the documented failure mode of texturing an uploaded mesh: Meshy largely ignores livery
  // instructions on geometry it did not author and hands back a grey hull or one flat colour. The
  // rest of the archive has no albedo at all.
  //
  // THE FLOOR IS LOW ON PURPOSE, and the first attempt at it was wrong in a way worth writing down.
  // It was set at 20% from the Aquarii numbers alone, where hulls score 64-85% — and it immediately
  // failed both accepted TERRAN hulls, which score 12.6% and 16.9%. They are not greyscale; they are
  // military aircraft, and desaturated steel-blue with a little orange trim is exactly the design
  // language that civilization is supposed to have. A saturation floor calibrated on the most vivid
  // civ would quietly condemn the most restrained one.
  //
  // What the number has to separate is PAINTED from NOT PAINTED, and that gap is still enormous:
  // every real hull, vivid or restrained, clears 12%, and the greyscale failure sits at 0.0% with
  // 0.0% of both accent hues. Four per cent sits in the middle of an empty gap.
  //
  // It gets its own verdict rather than being left to fall out of "primary missing", because the two
  // are different problems: a hull that missed its accent hues still has a paint job and might be
  // worth keeping, and a greyscale hull is not textured at all.
  minColourPct: 4.0,

  /// Below this stdev-of-luminance a DARK hull is judged empty rather than moody. Sits between the
  /// two genuine failures (0.061, 0.065) and the darkest hulls that are fine (0.118, 0.129).
  darkDetailFloor: 0.09,
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
  // ---- DARK, OR DARK AND EMPTY? --------------------------------------------------------------
  //
  // Brightness alone was failing nine hulls, and looking at them, most were not failures: the big
  // stations are charcoal structures with lit panels, and the Terran hyper-relay is near-black with
  // blazing blue emitters — that one is striking, not broken. Meanwhile the two genuine failures sit
  // at the same brightness. Brightness cannot tell them apart, because the thing that separates a
  // designed dark hull from a generation that returned a near-black blob is not how dark it is. It is
  // whether there is anything IN it.
  //
  //   Terran hyper-relay      0.136 bright, 0.118 detail   dark and full of structure  -> fine
  //   Terran mega-station     0.149 bright, 0.129 detail   dark and full of structure  -> fine
  //   Aquarii mega-station    0.122 bright, 0.061 detail   dark AND flat               -> a failure
  //   Aquarii terraforming    0.125 bright, 0.065 detail   dark AND flat               -> a failure
  //
  // So darkness is only a failure when it comes with no detail to go with it. This is a refinement
  // rather than a loosening, and the test still fails the two hulls that deserve it — which is how I
  // know it has not just been moved out of the way.
  {
    const dark = m.brightness < LIMITS.minBrightness;
    const empty = m.detail < LIMITS.darkDetailFloor;
    if (dark && empty)
      fails.push(`too dark AND flat (${m.brightness.toFixed(3)} bright, ${m.detail.toFixed(3)} detail) ` +
                 '— this reads as a failed generation, not a dark design');
    else if (dark)
      warns.push(`dark (${m.brightness.toFixed(3)}), but detailed (${m.detail.toFixed(3)}) — ` +
                 'check it is meant to be');
  }
  if (m.brightness > LIMITS.maxBrightness) fails.push(`blown out (${m.brightness.toFixed(3)})`);
  if (m.detail < LIMITS.minDetail) fails.push(`flat, no detail (sd ${m.detail.toFixed(3)})`);

  // Checked BEFORE the accent tests and reported instead of them: on a greyscale hull "primary
  // missing" and "secondary missing" are both true, both trivially implied, and neither says the
  // thing that matters, which is that this model was never painted.
  if (m.saturatedPct < LIMITS.minColourPct)
  {
    fails.push(`GREYSCALE — no livery at all (${m.saturatedPct.toFixed(1)}% coloured). ` +
               `Regenerate; texturing an existing mesh does not fix this`);
    return { pass: false, fails, warns };
  }
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
console.log(`\n${'unit'.padEnd(34)} ${'bright'.padStart(6)} ${'detail'.padStart(6)} ${'colour%'.padStart(7)} ${'prim%'.padStart(6)} ${'sec%'.padStart(6)}  verdict`);
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
                `${m.saturatedPct.toFixed(1).padStart(7)} ` +
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
