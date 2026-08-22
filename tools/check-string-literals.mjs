// ============================================================================================
// DOES EVERY STRING LITERAL CLOSE ON THE LINE IT OPENS?
//
//   node tools/check-string-literals.mjs
//
// C# regular string literals cannot span lines. A stray newline inside one is
// `error CS1010: Newline in constant`, and the compiler then loses its place and reports a cascade of
// spurious syntax errors on the following twenty lines — so the real fault is buried under noise that
// points everywhere except at it.
//
// ---- WHY THIS EXISTS ---------------------------------------------------------------------------
//
// Because it happened. A scripted edit to FleetCommandBar.cs was written as a shell command whose
// `\n\n` was expanded by bash before node ever saw it, so three tooltips got REAL newlines where they
// should have had escape sequences. Check-Scripts.ps1 reported clean — it checks structure, braces
// and enum ordering, and an unterminated string literal is none of those. The first anyone knew was
// eighteen compiler errors in Unity.
//
// That is the exact class of failure this project cannot afford: there is no compiler here, so
// anything the tripwires do not catch is found by the person trying to play the game.
//
// ---- HOW IT DECIDES ----------------------------------------------------------------------------
//
// Count the double quotes on a line, having first removed the ones that are not delimiters: escaped
// quotes, character literals, and comments. An odd count means a literal opened and never closed.
//
// Verbatim strings (@"...") legitimately span lines and are skipped along with everything between
// their delimiters. Interpolated strings are ordinary in this respect — `$"..."` still cannot contain
// a raw newline — so they are checked like any other.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ROOT = path.join(PROJ, 'Assets', 'Scripts');

const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p);
    else if (e.name.endsWith('.cs')) files.push(p);
  }
})(ROOT);

const problems = [];

for (const file of files) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  let inBlockComment = false;
  let inVerbatim = false;

  for (let i = 0; i < lines.length; i++) {
    let s = lines[i];

    // ---- strip what is not code ----
    if (inBlockComment) {
      const end = s.indexOf('*/');
      if (end < 0) continue;
      s = s.slice(end + 2);
      inBlockComment = false;
    }

    // A verbatim string may legitimately be open across lines; skip until it closes. `""` inside one
    // is an escaped quote, so it is removed before looking for the terminator.
    if (inVerbatim) {
      const rest = s.replace(/""/g, '');
      const end = rest.indexOf('"');
      if (end < 0) continue;
      inVerbatim = false;
      s = rest.slice(end + 1);
    }

    // Block comment opening on this line.
    const bc = s.indexOf('/*');
    if (bc >= 0 && s.indexOf('*/', bc) < 0) { inBlockComment = true; s = s.slice(0, bc); }

    // Character literals first — '"' is a quote that is not a delimiter, and so is '\''.
    s = s.replace(/'(?:\\.|[^'\\])'/g, "''");

    // Escaped quotes inside a literal. Backslash pairs go first so a trailing `\\` is not mistaken
    // for an escape of the quote that follows it.
    s = s.replace(/\\\\/g, '').replace(/\\"/g, '');

    // A verbatim string that OPENS here and does not close.
    const vStart = s.search(/@"/);
    if (vStart >= 0) {
      const after = s.slice(vStart + 2).replace(/""/g, '');
      if (!after.includes('"')) { inVerbatim = true; s = s.slice(0, vStart); }
    }

    // Line comment — but only one outside a string, so count quotes up to it.
    const q = (s.match(/"/g) || []).length;
    if (q % 2 === 0) continue;

    // An odd count with a `//` after the last quote is a comment containing an apostrophe-free quote;
    // check the code portion alone before complaining.
    const lc = s.indexOf('//');
    if (lc >= 0) {
      const code = s.slice(0, lc);
      if (((code.match(/"/g) || []).length) % 2 === 0) continue;
    }

    problems.push({ file: path.relative(PROJ, file), line: i + 1, text: lines[i].trim() });
  }
}

console.log(`${files.length} C# files scanned`);

if (problems.length === 0) {
  console.log('Every string literal closes on the line it opens.');
  process.exit(0);
}

for (const p of problems)
  console.log(`FAIL  ${p.file}:${p.line}\n      ${p.text.slice(0, 110)}`);
console.log(`\n${problems.length} unterminated string literal(s) — each one is a CS1010 and a cascade ` +
            `of spurious syntax errors after it.`);
process.exit(1);
