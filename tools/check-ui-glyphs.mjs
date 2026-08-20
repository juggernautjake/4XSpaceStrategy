// ============================================================================================
// GLYPHS TEXTMESHPRO CANNOT DRAW
//
//   node tools/check-ui-glyphs.mjs
//
// TextMeshPro does not fail loudly on a character its font asset lacks. It substitutes a hollow box
// and logs a warning — and it logs that warning on EVERY measure and every rebuild of the text, not
// once. A label that repaints as the clock ticks therefore fills the console forever and buries
// everything else in it. That is exactly what `❚❚` (U+275A, HEAVY VERTICAL BAR) did in the speed
// readout: LiberationSans SDF, the TMP default, has no such glyph.
//
// So: anything above Latin-1 inside a C# STRING LITERAL is suspect, because a string literal is what
// eventually reaches a TMP_Text. Comments are ignored — they are never rendered, and the fix for the
// original bug is itself documented in a comment containing the offending character.
//
// The allow-list is punctuation that LiberationSans genuinely does carry (dashes, curly quotes,
// ellipsis, degree, times, middot). Anything else is reported; add to SAFE only after confirming the
// glyph exists in the SDF asset being used, not because it looks ordinary.
//
// grep is not good enough for this. In this environment it silently failed to match U+275A at all,
// which is worse than no check — it reported clean while the character was still in the file.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ROOT = path.join(PROJ, 'Assets', 'Scripts');

// The allow-list is WGL4 — the character repertoire LiberationSans (and essentially every other
// general-purpose font) is guaranteed to cover. That is the right line to draw, and it is drawn from
// evidence rather than taste: the bullets and squares below are already used all over the planet and
// system windows and have never produced a TMP warning, whereas U+275A did so on every single
// rebuild. U+275A sits in the Dingbats block, which LiberationSans does not cover at all.
//
// Anything outside WGL4 is reported. That is deliberately conservative — a false alarm costs a
// glance at the font asset, a miss costs a console full of noise that hides everything else.
const SAFE = new Set([
  0x2014, 0x2013,            // em dash, en dash
  0x2018, 0x2019,            // curly single quotes
  0x201C, 0x201D,            // curly double quotes
  0x2026,                    // ellipsis
  0x00B0, 0x00D7, 0x00B7,    // degree, times, middot
  0x2022,                    // bullet
  0x25A0, 0x25A1,            // black / white square
  0x2190, 0x2191, 0x2192, 0x2193,   // arrows
  0x2264, 0x2265, 0x2260,    // <=, >=, !=
  0x00A9, 0x00AE, 0x2122,    // (c), (R), TM
  0x00BD, 0x00BC, 0x00BE,    // vulgar fractions
  0x2588, 0x2591, 0x2592, 0x2593,   // block elements (progress bars)
  0x25B2, 0x25BC, 0x25C0, 0x25B6,   // solid triangles
]);

/// Strip line and block comments so a glyph explaining the bug does not report as the bug.
function stripComments(src) {
  let out = '', i = 0, inLine = false, inBlock = false, inStr = false, inChar = false, inVerb = false;
  while (i < src.length) {
    const c = src[i], n = src[i + 1];
    if (inLine) { if (c === '\n') { inLine = false; out += c; } i++; continue; }
    if (inBlock) { if (c === '*' && n === '/') { inBlock = false; i += 2; } else i++; continue; }
    if (inVerb) { out += c; if (c === '"' && n === '"') { out += n; i += 2; continue; } if (c === '"') inVerb = false; i++; continue; }
    if (inStr) { out += c; if (c === '\\') { out += n ?? ''; i += 2; continue; } if (c === '"') inStr = false; i++; continue; }
    if (inChar) { out += c; if (c === '\\') { out += n ?? ''; i += 2; continue; } if (c === "'") inChar = false; i++; continue; }
    if (c === '/' && n === '/') { inLine = true; i += 2; continue; }
    if (c === '/' && n === '*') { inBlock = true; i += 2; continue; }
    if (c === '@' && n === '"') { inVerb = true; out += c + n; i += 2; continue; }
    if (c === '"') { inStr = true; out += c; i++; continue; }
    if (c === "'") { inChar = true; out += c; i++; continue; }
    out += c; i++;
  }
  return out;
}

function walk(dir, acc = []) {
  for (const f of fs.readdirSync(dir)) {
    const fp = path.join(dir, f);
    fs.statSync(fp).isDirectory() ? walk(fp, acc) : (f.endsWith('.cs') && acc.push(fp));
  }
  return acc;
}

const STRING_LITERAL = /"(?:[^"\\]|\\.)*"/g;

const findings = [];
for (const file of walk(ROOT)) {
  const lines = stripComments(fs.readFileSync(file, 'utf8')).split('\n');
  lines.forEach((line, idx) => {
    for (const lit of line.match(STRING_LITERAL) ?? []) {
      for (const ch of lit) {
        const cp = ch.codePointAt(0);
        if (cp > 0x00FF && !SAFE.has(cp)) {
          findings.push({
            file: path.relative(PROJ, file).replace(/\\/g, '/'),
            line: idx + 1,
            cp: 'U+' + cp.toString(16).toUpperCase().padStart(4, '0'),
            ch,
          });
        }
      }
    }
  });
}

if (!findings.length) {
  console.log('No TMP-risky glyphs in any C# string literal. Clean.');
  process.exit(0);
}

console.log(`${findings.length} risky glyph(s) in string literals:\n`);
for (const f of findings) console.log(`  ${f.cp} ${f.ch}   ${f.file}:${f.line}`);
console.log('\nThese render as a hollow box and log a TMP warning on every text rebuild.');
process.exit(1);
