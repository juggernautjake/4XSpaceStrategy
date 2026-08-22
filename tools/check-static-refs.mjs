// ============================================================================================
// DOES EVERY `SomeProjectType.Member` REFERENCE NAME A MEMBER THAT EXISTS?
//
//   node tools/check-static-refs.mjs
//
// Check-Scripts.ps1 already does this for ENUMS, and enums were the easy half. A CLASS is where a
// refactor actually bites: rename or delete a static method and every call site still looks perfectly
// well-formed — balanced, correctly quoted, sensible. There is no compiler here to notice, so the
// first sign is Unity refusing to build on the machine that has one.
//
// ---- IT IS DELIBERATELY CONSERVATIVE -----------------------------------------------------------
//
// It reports only what it is sure about, because a check that cries wolf gets ignored and then gets
// deleted:
//   * only types DECLARED IN THIS PROJECT — a Unity or BCL type is invisible to it and is skipped
//   * only types whose name is unambiguous (exactly one declaration)
//   * a type with a base class from OUTSIDE the project is skipped entirely, since a member could be
//     inherited from something this script cannot see
//   * partial declarations of one type are merged before anything is checked
//
// Comments, strings, verbatim strings and char literals are stripped first, so a capitalised word in
// a tooltip cannot look like a reference.
//
// ---- VERIFIED IN BOTH DIRECTIONS ---------------------------------------------------------------
//
// Written 2026-08-22 and immediately tested by dropping a file into Assets/Scripts referring to
// `Squadrons.ThisMemberDoesNotExist` and `ControlGroups.AlsoNotReal` — both were reported, and the
// file was deleted. A checker that has only ever passed proves nothing.
//
// The three false positives its first version produced are all worth knowing about, because each is
// a C# shape a naive member scan gets wrong: multi-declarator consts (`const float A = 0f, B = 1f`),
// generic methods (`static T Ensure<T>(...)`), and tuple types (`(string a, Color b)[] Palette`).
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

// Strip comments and string bodies so a word inside a message cannot look like a reference.
function code(src) {
  let out = '', i = 0;
  while (i < src.length) {
    const c = src[i], d = src[i + 1];
    if (c === '/' && d === '/') { while (i < src.length && src[i] !== '\n') i++; continue; }
    if (c === '/' && d === '*') { i += 2; while (i + 1 < src.length && !(src[i] === '*' && src[i + 1] === '/')) { if (src[i] === '\n') out += '\n'; i++; } i += 2; continue; }
    if (c === '@' && d === '"') { i += 2; while (i < src.length) { if (src[i] === '"' && src[i + 1] === '"') { i += 2; continue; } if (src[i] === '"') { i++; break; } if (src[i] === '\n') out += '\n'; i++; } continue; }
    if (c === '"') { i++; while (i < src.length) { if (src[i] === '\\') { i += 2; continue; } if (src[i] === '"') { i++; break; } i++; } continue; }
    if (c === "'") { i++; while (i < src.length) { if (src[i] === '\\') { i += 2; continue; } if (src[i] === "'") { i++; break; } i++; } continue; }
    out += c; i++;
  }
  return out;
}

const sources = new Map();
for (const f of files) sources.set(f, code(fs.readFileSync(f, 'utf8')));

// ---- collect declared types, their bases, and their member names ----
const types = new Map();      // name -> { count, bases:Set, members:Set, enum:boolean }
const declRe = /\b(?:public|internal|private|protected|static|sealed|abstract|partial|\s)*\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*(?::\s*([^{]+))?\{/g;

function typeEntry(name) {
  if (!types.has(name)) types.set(name, { count: 0, bases: new Set(), members: new Set(), isEnum: false });
  return types.get(name);
}

// Find the matching close brace for the block opening at `open`.
function block(src, open) {
  let depth = 0;
  for (let i = open; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}') { depth--; if (depth === 0) return src.slice(open + 1, i); }
  }
  return src.slice(open + 1);
}

// Parens are allowed in the type portion so a tuple return type (`(string a, Color b)[] Palette`)
// still finds its name, and an optional <...> is allowed after the name so a generic method
// (`static T Ensure<T>(...)`) does. Both were false positives in the first version.
// ALL leading modifiers are consumed as a group, not just one. With only one consumed, the lazy type
// portion could stop at the very next modifier and the tail would happily match the `(` of a tuple
// type — `public static readonly (string name, Color color)[] Palette` yielded the member "readonly"
// and then resumed PAST the real name, losing it. That was the last false positive in this check.
const memberRe = /\b(?:(?:public|internal|protected|private|static|readonly|const|virtual|override|abstract|sealed|new|extern|unsafe|partial|event|async)\s+)+[^;{}=]*?\b([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*(?:[({=;]|=>)/g;

// Field and const statements are re-scanned whole, because one statement can declare several names:
// `public const float MinLiquidC = 0f, MaxLiquidC = 50f;` only ever yielded the first above.
const fieldRe = /\b(?:public|internal|protected|private|static|readonly|const|new)\b[^;{}]*;/g;

for (const [, src] of sources) {
  declRe.lastIndex = 0;
  let m;
  while ((m = declRe.exec(src))) {
    const kind = m[1], name = m[2], bases = m[3] || '';
    const e = typeEntry(name);
    e.count++;
    if (kind === 'enum') { e.isEnum = true; continue; }
    for (const b of bases.split(',')) {
      const t = b.trim().replace(/<.*/, '');
      if (t) e.bases.add(t);
    }
    const body = block(src, m.index + m[0].length - 1);
    memberRe.lastIndex = 0;
    let mm;
    while ((mm = memberRe.exec(body))) e.members.add(mm[1]);
    fieldRe.lastIndex = 0;
    while ((mm = fieldRe.exec(body)))
      for (const id of mm[0].matchAll(/([A-Za-z_][A-Za-z0-9_]*)\s*(?==|,|;)/g)) e.members.add(id[1]);
    // Nested types and fields declared without an access modifier.
    for (const nm of body.matchAll(/\b(?:class|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)/g)) e.members.add(nm[1]);
  }
}

// A type is checkable only if it is unambiguous, not an enum, and every base is also in the project.
function checkable(name, seen = new Set()) {
  const e = types.get(name);
  if (!e || e.isEnum) return false;
  if (seen.has(name)) return true;
  seen.add(name);
  for (const b of e.bases) {
    if (!types.has(b)) return false;         // base outside the project — members may come from it
    if (!checkable(b, seen)) return false;
  }
  return true;
}

function has(name, member, seen = new Set()) {
  const e = types.get(name);
  if (!e || seen.has(name)) return false;
  seen.add(name);
  if (e.members.has(member)) return true;
  for (const b of e.bases) if (has(b, member, seen)) return true;
  return false;
}

// ---- check every Type.Member reference ----
const refRe = /(?<![.A-Za-z0-9_])([A-Z][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)/g;
const findings = [];

for (const [f, src] of sources) {
  const lines = src.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    refRe.lastIndex = 0;
    let m;
    while ((m = refRe.exec(lines[i]))) {
      const [type, member] = [m[1], m[2]];
      const e = types.get(type);
      if (!e || e.count !== 1 || !checkable(type)) continue;
      if (has(type, member)) continue;
      findings.push(`${path.relative(PROJ, f)}:${i + 1}  ${type}.${member}`);
    }
  }
}

console.log(`${files.length} files, ${types.size} declared types, ${[...types.keys()].filter(t => checkable(t)).length} checkable`);
if (!findings.length) { console.log('Every static reference onto a project type names a real member.'); process.exit(0); }
for (const f of findings) console.log('FAIL  ' + f);
console.log(`\n${findings.length} suspect reference(s).`);
process.exit(1);
