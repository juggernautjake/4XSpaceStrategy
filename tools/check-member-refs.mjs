// ============================================================================================
// DOES EVERY MEMBER ACCESS NAME A MEMBER THAT EXISTS?
//
//   node tools/check-member-refs.mjs
//
// check-static-refs.mjs answers this for `SomeType.Member`, which is the easy half — the type is
// written right there. This one resolves the type of a LOCAL first and then asks the same question,
// which is where the answer is actually worth having: `var b = SystemContext.Bodies;` followed two
// hundred lines later by `b.SomethingThatIsNotThere`.
//
// Deliberately conservative, for the same reason as its sibling: a check that cries wolf gets ignored
// and then gets deleted. It resolves a local ONLY when the initialiser is unambiguous —
//
//     var x = new Thing(...)        -> Thing
//     var x = Type.Member           -> that member's declared type
//     var x = Type.Member(...)      -> that method's declared return type
//     Thing x = ...                 -> Thing
//
// — and it checks the access ONLY when the resolved type is declared exactly once in this project and
// inherits nothing from outside it. Anything else is skipped in silence.
//
// It knows nothing about generics beyond stripping them, so `List<Foo>` is a List and the Foo is not
// followed. That is fine: the point is to catch a NAME that does not exist, and a name that does not
// exist on the container does not exist whatever it contains.
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
    if (e.isDirectory()) walk(p); else if (e.name.endsWith('.cs')) files.push(p);
  }
})(ROOT);

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

// ---- collect every declared type, its bases, and its members (name -> declared type) ----
const types = new Map();
const declRe = /\b(?:public|internal|private|protected|static|sealed|abstract|partial|\s)*\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*(?::\s*([^{]+))?\{/g;

function block(src, open) {
  let depth = 0;
  for (let i = open; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}') { depth--; if (depth === 0) return src.slice(open + 1, i); }
  }
  return src.slice(open + 1);
}

const entry = n => {
  if (!types.has(n)) types.set(n, { count: 0, bases: [], members: new Map(), isEnum: false });
  return types.get(n);
};

const MOD = 'public|internal|protected|private|static|readonly|const|virtual|override|abstract|sealed|new|extern|unsafe|partial|event|async';
const memRe = new RegExp(String.raw`\b(?:(?:${MOD})\s+)+([A-Za-z_][A-Za-z0-9_.<>,\[\]\s]*?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*([({=;]|=>)`, 'g');
const fieldRe = new RegExp(String.raw`\b(?:${MOD})\b[^;{}]*;`, 'g');

for (const [, src] of sources) {
  declRe.lastIndex = 0;
  let m;
  while ((m = declRe.exec(src))) {
    const kind = m[1], name = m[2], bases = (m[3] || '').trim();
    const e = entry(name);
    e.count++;
    if (kind === 'enum') { e.isEnum = true; continue; }
    for (const b of bases.split(',')) { const t = b.trim(); if (t) e.bases.push(t.replace(/<.*/, '')); }

    const body = block(src, m.index + m[0].length - 1);
    memRe.lastIndex = 0;
    let mm;
    while ((mm = memRe.exec(body))) if (!e.members.has(mm[2])) e.members.set(mm[2], mm[1].trim());
    fieldRe.lastIndex = 0;
    while ((mm = fieldRe.exec(body)))
      for (const id of mm[0].matchAll(/([A-Za-z_][A-Za-z0-9_]*)\s*(?==|,|;)/g))
        if (!e.members.has(id[1])) e.members.set(id[1], '');
    for (const nm of body.matchAll(/\b(?:class|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)/g))
      if (!e.members.has(nm[1])) e.members.set(nm[1], '');
  }
}

const bare = n => (n || '').replace(/<.*/, '').replace(/\[\]$/, '').replace(/\?$/, '').replace(/^.*\./, '').trim();

// Checkable only if we can see the whole type: one declaration, not an enum, every base also ours.
function checkable(name, seen = new Set()) {
  const t = types.get(name);
  if (!t || t.isEnum) return false;
  if (seen.has(name)) return true;
  seen.add(name);
  for (const b of t.bases) {
    if (!types.has(b)) return false;
    if (!checkable(b, seen)) return false;
  }
  return true;
}

/// Everything inherits from System.Object, and a project class that declares none of these still has
/// all of them. Without this the check reports `unit.GetHashCode` as a missing member.
const OBJECT_MEMBERS = new Set(['ToString', 'GetHashCode', 'Equals', 'GetType', 'ReferenceEquals',
                                'MemberwiseClone', 'Finalize']);

function has(name, member, seen = new Set()) {
  if (OBJECT_MEMBERS.has(member)) return true;
  const t = types.get(name);
  if (!t || seen.has(name)) return false;
  seen.add(name);
  if (t.members.has(member)) return true;
  for (const b of t.bases) if (has(b, member, seen)) return true;
  return false;
}

// ---- resolve an expression to a type name ----
function resolveExpr(expr, src, depth = 0) {
  if (depth > 3) return null;
  expr = expr.trim();

  let m = /^new\s+([A-Za-z_][A-Za-z0-9_.]*)\s*(?:<[^<>]*>)?\s*[({]/.exec(expr);
  if (m) return bare(m[1]);

  m = /^([A-Z][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)\s*(\(\s*\))?$/.exec(expr);
  if (m) {
    const t = types.get(m[1]);
    if (!t || t.count !== 1) return null;
    const d = t.members.get(m[2]);
    return d ? bare(d) : null;
  }
  return null;
}

/// Locals declared in a file, name -> type.
///
/// ---- WHY A NAME DECLARED TWICE IS THROWN AWAY -------------------------------------------------
///
/// This does not track scopes, and C# code reuses short names constantly: `d` is a `BuildOrderDTO` in
/// one loop of UnitManager and a `UnitDTO` in the next, `m` is a moon in most of PlanetViewWindow and
/// a WrapMirror in three places. Resolving whole-file, the first version reported 26 findings and
/// every one of them was two different variables sharing a letter.
///
/// So EVERY declaration of a name is counted — `var`, an explicit type, a foreach variable, a method
/// parameter, an out/is pattern — and a name declared more than once anywhere in the file is dropped
/// entirely, whether or not the second one could be resolved. That leaves only names that mean one
/// thing in the whole file, which is the only case this can be sure about.
function localsOf(src) {
  const decls = new Map();     // name -> count
  const typeOf = new Map();    // name -> resolved type, when there is one

  const seen = (n, t) => {
    decls.set(n, (decls.get(n) || 0) + 1);
    if (t) typeOf.set(n, t);
  };

  for (const m of src.matchAll(/\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([^;]+);/g))
    seen(m[1], resolveExpr(m[2], src));

  // `Thing x = ...` / `Thing x;` — an explicit type is its own answer.
  for (const m of src.matchAll(/(?:^|[;{}()]\s*)([A-Z][A-Za-z0-9_]*)\s+([a-z_][A-Za-z0-9_]*)\s*[=;]/gm)) {
    if (['return', 'new', 'else', 'case', 'in', 'is', 'as'].includes(m[1])) continue;
    seen(m[2], bare(m[1]));
  }

  // Everything else that introduces a name, counted but never trusted for its type.
  for (const m of src.matchAll(/\bforeach\s*\(\s*(?:var|[A-Za-z_][A-Za-z0-9_.<>,\[\]]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\b/g))
    seen(m[1], null);
  for (const m of src.matchAll(/\b(?:out|ref|in)\s+(?:var|[A-Za-z_][A-Za-z0-9_.<>,\[\]]*)\s+([A-Za-z_][A-Za-z0-9_]*)/g))
    seen(m[1], null);
  for (const m of src.matchAll(/\bis\s+[A-Za-z_][A-Za-z0-9_.<>,\[\]]*\s+([a-z_][A-Za-z0-9_]*)/g))
    seen(m[1], null);
  // Parameter lists: every `Type name` pair inside a declaration's parentheses.
  for (const m of src.matchAll(/\)\s*(?:=>|\{)/g)) { /* no-op: shape guard for the next pass */ }
  for (const m of src.matchAll(/\(([^()]*)\)\s*(?:=>|\{)/g))
    for (const p of m[1].matchAll(/\b[A-Za-z_][A-Za-z0-9_.<>,\[\]?]*\s+([a-z_][A-Za-z0-9_]*)\s*(?=[,)]|$)/g))
      seen(p[1], null);
  // Lambda parameters without types: `x => ...` and `(a, b) => ...`
  for (const m of src.matchAll(/(?:^|[(,=\s])([a-z_][A-Za-z0-9_]*)\s*=>/g)) seen(m[1], null);

  const found = new Map();
  for (const [n, t] of typeOf) if (decls.get(n) === 1) found.set(n, t);
  return found;
}

const findings = [];
const accessRe = /(?<![.A-Za-z0-9_])([a-z_][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)/g;

for (const [f, src] of sources) {
  const locals = localsOf(src);
  if (locals.size === 0) continue;
  const lines = src.split(/\r?\n/);

  for (let i = 0; i < lines.length; i++) {
    accessRe.lastIndex = 0;
    let m;
    while ((m = accessRe.exec(lines[i]))) {
      const [, name, member] = m;
      const t = locals.get(name);
      if (!t) continue;
      const e = types.get(t);
      if (!e || e.count !== 1 || !checkable(t)) continue;
      if (has(t, member)) continue;
      findings.push(`${path.relative(PROJ, f)}:${i + 1}  ${name} is ${t}, which has no member ${member}`);
    }
  }
}

console.log(`${files.length} files, ${types.size} types, ${[...types.keys()].filter(t => checkable(t)).length} checkable`);
if (!findings.length) { console.log('Every resolved member access names a real member.'); process.exit(0); }
for (const x of findings) console.log('FAIL  ' + x);
console.log(`\n${findings.length} suspect access(es).`);
process.exit(1);
