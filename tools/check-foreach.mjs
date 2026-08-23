// Can you actually foreach over that?
//
// CS1579 — "foreach statement cannot operate on variables of type X because X does not contain a
// public instance or extension definition for GetEnumerator".
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

// ---- collect project types ----
const types = new Map();   // name -> { count, bases:[], members: Map(name -> declaredType), enumerable:bool }
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
  if (!types.has(n)) types.set(n, { count: 0, bases: [], members: new Map(), enumerable: false, isEnum: false });
  return types.get(n);
};

// A member declaration: modifiers, a TYPE, a NAME, then ( = ; or =>
const memRe = /\b(?:(?:public|internal|protected|private|static|readonly|const|virtual|override|abstract|sealed|new|extern|unsafe|partial|event|async)\s+)+([A-Za-z_][A-Za-z0-9_.<>,\[\]\s]*?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*([({=;]|=>)/g;

for (const [, src] of sources) {
  declRe.lastIndex = 0;
  let m;
  while ((m = declRe.exec(src))) {
    const kind = m[1], name = m[2], bases = (m[3] || '').trim();
    const e = entry(name);
    e.count++;
    if (kind === 'enum') { e.isEnum = true; continue; }
    for (const b of bases.split(',')) { const t = b.trim(); if (t) e.bases.push(t); }
    const body = block(src, m.index + m[0].length - 1);

    memRe.lastIndex = 0;
    let mm;
    while ((mm = memRe.exec(body))) {
      const type = mm[1].trim(), member = mm[2];
      if (member === 'GetEnumerator') e.enumerable = true;
      if (!e.members.has(member)) e.members.set(member, type);
    }
  }
}

// A type is enumerable if it says GetEnumerator, or any base looks like a collection interface.
const COLLECTION = /^(List|IList|IReadOnlyList|ICollection|IReadOnlyCollection|IEnumerable|HashSet|ISet|Dictionary|IReadOnlyDictionary|Queue|Stack|LinkedList|SortedList|SortedSet|Array|IEnumerator|IGrouping|IOrderedEnumerable|Span|ReadOnlySpan|NativeArray|IQueryable)\b/;

function enumerableType(name, seen = new Set()) {
  if (!name) return true;                        // unknown: say yes, stay quiet
  name = name.trim();
  if (name.endsWith('[]')) return true;
  if (COLLECTION.test(name)) return true;
  if (name.startsWith('(')) return true;         // tuple-ish, give up
  const bare = name.replace(/<.*/, '').replace(/^.*\./, '');
  if (COLLECTION.test(bare)) return true;
  const t = types.get(bare);
  if (!t || t.isEnum || seen.has(bare)) return true;   // unknown or enum: stay quiet
  seen.add(bare);
  if (t.enumerable) return true;
  for (const b of t.bases) if (enumerableType(b, seen)) return true;
  return false;                                   // a project type we can see all of, and it has none
}

// Only complain about types we fully understand: declared exactly once, in the project.
function known(name) {
  const bare = (name || '').replace(/<.*/, '').replace(/^.*\./, '').replace(/\[\]$/, '').trim();
  const t = types.get(bare);
  return t && t.count === 1 && !t.isEnum;
}

// ---- resolve the type of a foreach source expression ----
function resolve(expr, src) {
  expr = expr.trim();

  // Type.Member  /  Type.Member()
  let m = /^([A-Z][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)\s*(\(\s*\))?$/.exec(expr);
  if (m) {
    const t = types.get(m[1]);
    if (!t || t.count !== 1) return null;
    return t.members.get(m[2]) || null;
  }

  // a bare local: find `var NAME = <expr>;` in this file and resolve that instead
  m = /^([a-z_][A-Za-z0-9_]*)$/.exec(expr);
  if (m) {
    const re = new RegExp(String.raw`\bvar\s+${m[1]}\s*=\s*([^;]+);`);
    const hit = re.exec(src);
    if (hit) return resolve(hit[1], src);
    return null;
  }
  return null;
}

const findings = [];
const feRe = /\bforeach\s*\(\s*(?:var|[A-Za-z_][A-Za-z0-9_.<>,\[\]]*)\s+[A-Za-z_][A-Za-z0-9_]*\s+in\s+([^)]+)\)/g;

for (const [f, src] of sources) {
  const lines = src.split(/\r?\n/);
  feRe.lastIndex = 0;
  let m;
  while ((m = feRe.exec(src))) {
    const t = resolve(m[1], src);
    if (!t || !known(t)) continue;
    if (enumerableType(t)) continue;
    const line = src.slice(0, m.index).split(/\r?\n/).length;
    findings.push(`${path.relative(PROJ, f)}:${line}  foreach over ${m[1].trim()} -> ${t}, which has no GetEnumerator`);
  }
}

console.log(`${files.length} files, ${types.size} types`);
if (!findings.length) { console.log('Every foreach iterates something iterable.'); process.exit(0); }
for (const x of findings) console.log('FAIL  ' + x);
process.exit(1);
