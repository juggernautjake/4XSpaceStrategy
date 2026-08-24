// ============================================================================================
// A LOCAL THAT SHADOWS AN ENCLOSING ONE — CS0136
//
//   node tools/check-scope.mjs
//
// This reached `main` on 2026-08-23 in work that passed all eight existing checks, because every one
// of those asks about NAMES: does this enum member exist, does this static exist, does this string
// close. This is not a name problem. It is a SCOPE problem, and scope is the one thing a regex over
// `Type.Member` cannot see.
//
//   CS0136  A local or parameter named 'placed' cannot be declared in this scope because that name
//           is used in an enclosing local scope.
//
//           `SolarSystemGenerator` counted filled rings in `placed` and then, four hundred lines in,
//           counted asteroids in a second `placed` inside the belt branch. Both readable, both
//           obviously correct on their own, and illegal together. The fix was to name the inner one
//           `placedRocks`, which is what it always meant.
//
// ---- WHAT THIS WILL AND WILL NOT CLAIM ---------------------------------------------------------
//
// It models C# scoping as EXTENTS: every declaration gets the span of text it is live in, and one
// declaration shadows another exactly when the other's extent strictly contains its own. That gets
// the cases a brace-stack gets wrong — a `for` header's variable is scoped to its loop, not to the
// block the loop sits in — and it means two sequential `for (int i ...)` loops are correctly silent.
//
// TWO THINGS IT DELIBERATELY DOES NOT CLAIM:
//
//   * **Same-scope redeclaration** (CS0128). Different error, equally loud from the compiler, and
//     separating the two here would only add ways to be wrong.
//   * **Anything across a lambda boundary.** Whether a lambda may declare a local shadowing one from
//     the enclosing method has moved between language versions, and this repo has no compiler to ask.
//     Two such pairs exist in `InspectorBodyTabs` and `PlanetViewWindow` and Unity accepts both, so
//     the rule here is evidently not the C# 7 one. Rather than guess at which it is, declarations
//     separated from their shadow by a lambda body are skipped.
//
// ---- WHAT IT DOES NOT COVER AT ALL, AND WHY ----------------------------------------------------
//
// The same commit also shipped a **CS0103** — `ClimateCoherence` read a `body` that only the methods
// around it have. A check for that was written and thrown away: deciding "this identifier is declared
// nowhere" means resolving base classes, partials, extension methods and using-statics, and the
// version that only compared against other methods' locals produced 239 findings, essentially all of
// them declaration forms the parser had missed (`int w = ..., h = ...;` alone accounted for dozens).
//
// A check that cries wolf gets ignored and then gets deleted, so it is not here. That gap is real and
// is stated rather than papered over: catching CS0103 needs a resolver, and this is a tripwire.
// ============================================================================================
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ROOT = path.join(PROJ, 'Assets', 'Scripts');

/// Blank out comments, strings and chars, PRESERVING LENGTH AND LINE BREAKS.
///
/// Length-preserving matters: every finding below is reported at a line number derived from an offset
/// into this text, and a stripper that removed characters would report the wrong line for any method
/// that follows a long comment — which, in this codebase, is all of them.
function blank(src) {
  const out = src.split('');
  let i = 0, n = src.length;
  const kill = (from, to) => {
    for (let k = from; k < to && k < n; k++) if (out[k] !== '\n') out[k] = ' ';
  };
  while (i < n) {
    const c = src[i], d = src[i + 1];
    if (c === '/' && d === '/') { let j = i; while (j < n && src[j] !== '\n') j++; kill(i, j); i = j; continue; }
    if (c === '/' && d === '*') {
      let j = i + 2; while (j < n && !(src[j] === '*' && src[j + 1] === '/')) j++;
      kill(i, Math.min(j + 2, n)); i = j + 2; continue;
    }
    if (c === '@' && d === '"') {
      let j = i + 2;
      while (j < n) { if (src[j] === '"' && src[j + 1] === '"') { j += 2; continue; } if (src[j] === '"') break; j++; }
      kill(i, Math.min(j + 1, n)); i = j + 1; continue;
    }
    if (c === '"' || c === "'") {
      let j = i + 1;
      while (j < n) { if (src[j] === '\\') { j += 2; continue; } if (src[j] === c || src[j] === '\n') break; j++; }
      kill(i, Math.min(j + 1, n)); i = j + 1; continue;
    }
    i++;
  }
  return out.join('');
}

function walk(dir, acc = []) {
  for (const f of fs.readdirSync(dir)) {
    const fp = path.join(dir, f);
    fs.statSync(fp).isDirectory() ? walk(fp, acc) : (f.endsWith('.cs') && acc.push(fp));
  }
  return acc;
}

const lineAt = (src, off) => src.slice(0, off).split('\n').length;

// C# words that sit exactly where a type name sits and are not one.
const NOT_A_TYPE = new Set([
  'return', 'new', 'else', 'case', 'in', 'is', 'as', 'if', 'while', 'for', 'foreach', 'do', 'switch',
  'lock', 'using', 'yield', 'throw', 'catch', 'default', 'when', 'where', 'get', 'set', 'add', 'remove',
  'public', 'private', 'protected', 'internal', 'static', 'readonly', 'const', 'override', 'virtual',
  'abstract', 'sealed', 'partial', 'async', 'await', 'ref', 'out', 'params', 'this', 'base', 'checked',
]);

const KEYWORDS = new Set([
  ...NOT_A_TYPE, 'var', 'true', 'false', 'null', 'void', 'int', 'float', 'bool', 'string', 'char',
  'double', 'long', 'short', 'byte', 'uint', 'ulong', 'ushort', 'sbyte', 'decimal', 'object', 'try',
  'finally', 'break', 'continue', 'goto', 'sizeof', 'typeof', 'nameof', 'stackalloc', 'unchecked',
  'namespace', 'class', 'struct', 'interface', 'enum', 'delegate', 'event', 'operator', 'implicit',
  'explicit', 'extern', 'fixed', 'unsafe', 'volatile', 'from', 'select', 'let', 'orderby', 'group',
]);

// ---- finding the methods -----------------------------------------------------------------------
//
// A signature is `... Name(params) {`, and the body is the matched brace block. Expression-bodied
// members (`=> expr;`) are skipped: they declare nothing and cannot shadow.
//
// Constructors, properties with bodies, local functions and lambdas all get swept up by this and that
// is fine — every one of them is a scope, and a scope is what both checks are about.
function methodsOf(src) {
  const out = [];
  const re = /(?:^|[};\)])\s*((?:[A-Za-z_][A-Za-z0-9_<>,.\[\]?]*\s+)*)([A-Za-z_][A-Za-z0-9_]*)\s*\(([^()]*)\)\s*(?:where[^{;]*)?\{/g;
  let m;
  while ((m = re.exec(src)) !== null) {
    const name = m[2];
    if (KEYWORDS.has(name)) continue;              // `if (...) {`, `while (...) {`, `catch (...) {`
    const open = src.indexOf('{', m.index + m[0].length - 1);
    if (open < 0) continue;
    let depth = 0, i = open;
    for (; i < src.length; i++) {
      if (src[i] === '{') depth++;
      else if (src[i] === '}') { depth--; if (depth === 0) break; }
    }
    if (depth !== 0) continue;                     // unbalanced; BALANCE in Check-Scripts owns that
    out.push({ name, params: m[3], body: src.slice(open + 1, i), at: open + 1, sigAt: m.index });
  }
  return out;
}

/// Every name a parameter list introduces. `Type name`, `out Type name`, `this Type name`, defaults.
function paramNames(list) {
  const names = [];
  for (const part of list.split(',')) {
    const p = part.split('=')[0].trim();
    if (!p) continue;
    const words = p.replace(/\[[^\]]*\]/g, ' ').split(/\s+/).filter(Boolean);
    const last = words[words.length - 1];
    if (last && /^[A-Za-z_][A-Za-z0-9_]*$/.test(last) && !KEYWORDS.has(last) && words.length > 1)
      names.push(last);
  }
  return names;
}

// ============================================================================================
// SCOPE AS AN EXTENT, NOT AS A STACK
//
// The first version of this walked the body pushing a scope on `{` and popping on `}`, which is the
// obvious model and is wrong about the single most common declaration in the language.
//
//     for (int i = 0; i < n; i++) { ... }
//     for (int i = 0; i < n; i++) { ... }      // legal, and the stack model called it a redeclaration
//
// `int i` is declared BEFORE the brace, so a stack keyed on braces puts it in the enclosing block and
// leaves it there for the rest of the method — after which every later `i` in that method looks like
// a shadow. It reported 59 findings across code Unity compiles every day, all of them this.
//
// C# actually scopes a `for`/`foreach`/`using`/`fixed` header's declarations to THAT STATEMENT: the
// header plus the body it controls, whether that body is a block or a single statement. So each
// declaration gets an EXTENT — the half-open span of text it is live in — and one declaration shadows
// another exactly when the other's extent strictly contains its own. No stack, no ordering subtlety,
// and the sequential-`for` case falls out for free because neither extent contains the other.
//
// An `out var` in an `if (...)` is deliberately NOT treated as a header declaration: C# leaks those
// into the ENCLOSING block, which is what "no enclosing header" already gives it.
// ============================================================================================

/// Matching-brace map: offset of every `{` -> offset of its `}`.
function braceMap(body) {
  const pairs = [], stack = [];
  for (let i = 0; i < body.length; i++) {
    if (body[i] === '{') stack.push(i);
    else if (body[i] === '}' && stack.length) pairs.push([stack.pop(), i]);
  }
  return pairs.sort((a, b) => a[0] - b[0]);
}

/// Is the block opening at `o` a lambda or anonymous-method body? See the header: this repo's
/// compiler evidently permits shadowing across one, so those pairs are not claimed.
function isLambdaBlock(body, o) {
  let k = o - 1;
  while (k >= 0 && /\s/.test(body[k])) k--;
  if (k >= 1 && body[k] === '>' && body[k - 1] === '=') return true;      // `=> {`
  if (body[k] === ')') {                                                   // `delegate(...) {`
    let d = 0, j = k;
    for (; j >= 0; j--) {
      if (body[j] === ')') d++;
      else if (body[j] === '(') { d--; if (d === 0) break; }
    }
    let p = j - 1;
    while (p >= 0 && /\s/.test(body[p])) p--;
    return /delegate$/.test(body.slice(Math.max(0, p - 7), p + 1));
  }
  return false;
}

/// Where the statement beginning at `j` ends.
///
/// THE RECURSION IS THE POINT. A first attempt scanned to the next `;` outside brackets, which is
/// right for `for (...) total += w[i];` and badly wrong for the shape this codebase is full of:
///
///     for (int y = 0; y < h; y++)
///         for (int x = 0; x < w; x++)
///         { ... }
///
/// The outer loop's statement is the inner LOOP, which ends at a brace and carries no semicolon at
/// all — so the scan sailed past it and ran on to the next `;` anywhere below, swallowing whatever
/// followed. Every sequential pair of `for (int y ...)` loops in the file then looked nested, and the
/// second one looked like a shadow of the first. That was 30 of the first 59 findings.
function statementEnd(body, j) {
  while (j < body.length && /\s/.test(body[j])) j++;

  if (body[j] === '{') {
    let d = 0;
    for (let k = j; k < body.length; k++) {
      if (body[k] === '{') d++;
      else if (body[k] === '}') { d--; if (d === 0) return k; }
    }
    return body.length - 1;
  }

  // A nested control statement: skip its header, then ask the same question of ITS statement.
  if (/^(for|foreach|while|if|using|fixed|lock|switch)\s*\(/.test(body.slice(j, j + 40))) {
    const p = body.indexOf('(', j);
    let d = 0, k = p;
    for (; k < body.length; k++) {
      if (body[k] === '(') d++;
      else if (body[k] === ')') { d--; if (d === 0) break; }
    }
    return statementEnd(body, k + 1);
  }

  let d = 0;
  for (let k = j; k < body.length; k++) {
    const c = body[k];
    if (c === '(' || c === '[' || c === '{') d++;
    else if (c === ')' || c === ']' || c === '}') d--;
    else if (c === ';' && d <= 0) return k;
  }
  return body.length - 1;
}

/// Every `for (...)`, `foreach (...)`, `using (...)`, `fixed (...)` in this body, as
/// { parenFrom, parenTo, from, to } — where from..to is the whole statement the declarations live in.
function headersIn(body) {
  const out = [];
  for (const m of body.matchAll(/\b(for|foreach|using|fixed)\s*\(/g)) {
    const parenFrom = body.indexOf('(', m.index);
    let depth = 0, i = parenFrom;
    for (; i < body.length; i++) {
      if (body[i] === '(') depth++;
      else if (body[i] === ')') { depth--; if (depth === 0) break; }
    }
    if (depth !== 0) continue;
    const parenTo = i;

    out.push({ parenFrom, parenTo, from: m.index, to: statementEnd(body, parenTo + 1) });
  }
  return out;
}

// ---- declarations, with the offset they happen at ----------------------------------------------
//
// Offsets are what let an extent be assigned: a declaration is scoped by whichever header or block
// encloses its position, rather than by whichever scope the regex happened to be scanning.
// EVERY OFFSET IS THE NAME'S OWN, via the /d flag — not the offset the match started at.
//
// This mattered more than it looks. `foreach (var c in ...)` matched from the word `foreach`, which
// sits OUTSIDE the header parentheses, so `extentOf` could not see that the declaration belonged to
// the loop and handed it the enclosing block instead. The loop variable then appeared live for the
// rest of the block, and the next sibling `foreach (var c ...)` looked like a shadow of it. Ten of the
// remaining findings were that, all in code Unity compiles daily.
function declsIn(body) {
  const found = [];
  const push = (m, group) => {
    const name = m[group];
    if (name && !KEYWORDS.has(name)) found.push({ name, at: m.indices[group][0] });
  };
  for (const m of body.matchAll(/\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?==)/gd)) push(m, 1);
  for (const m of body.matchAll(/(?:^|[;{}(,]\s*)([A-Z][A-Za-z0-9_]*(?:<[^<>();]*>)?(?:\[\])?)\s+([a-z_][A-Za-z0-9_]*)\s*[=;,)]/gmd)) {
    if (NOT_A_TYPE.has(m[1])) continue;
    push(m, 2);
  }
  for (const m of body.matchAll(/\b(?:int|float|bool|string|char|double|long|short|byte|uint|ulong|object|decimal)\s+([a-z_][A-Za-z0-9_]*)\s*[=;,)]/gd))
    push(m, 1);
  for (const m of body.matchAll(/\bforeach\s*\(\s*(?:var|[A-Za-z_][A-Za-z0-9_.<>,\[\]?]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\b/gd))
    push(m, 1);
  for (const m of body.matchAll(/\b(?:out|ref)\s+(?:var|[A-Za-z_][A-Za-z0-9_.<>,\[\]?]*)\s+([A-Za-z_][A-Za-z0-9_]*)/gd))
    push(m, 1);
  for (const m of body.matchAll(/\bis\s+[A-Za-z_][A-Za-z0-9_.<>,\[\]?]*\s+([a-z_][A-Za-z0-9_]*)/gd))
    push(m, 1);
  return found.sort((a, b) => a.at - b.at);
}

// ---- run ----------------------------------------------------------------------------------------
const files = walk(ROOT);
const shadow = [];

for (const file of files) {
  const raw = fs.readFileSync(file, 'utf8').replace(/^﻿/, '');
  const src = blank(raw);
  const rel = path.relative(PROJ, file).replace(/\\/g, '/');

  const methods = methodsOf(src);
  if (!methods.length) continue;

  const perMethod = methods.map(mth => ({
    mth,
    params: paramNames(mth.params),
    decls: declsIn(mth.body),
  }));
  for (const pm of perMethod) {
    const { mth, params, decls } = pm;
    const body = mth.body;

    // ---- SHADOW: every declaration gets an extent, then containment decides ---------------------
    const blocks = braceMap(body);
    const headers = headersIn(body);

    /// The span a declaration at `at` is live in. Innermost header whose PARENS hold it wins — that
    /// is a loop variable, scoped to its loop — otherwise the innermost block.
    const extentOf = at => {
      let best = null;
      for (const h of headers)
        if (at >= h.parenFrom && at <= h.parenTo)
          if (!best || h.from > best.from) best = { from: h.from, to: h.to };
      if (best) return best;
      for (const [o, c] of blocks)
        if (at > o && at < c)
          if (!best || o > best.from) best = { from: o, to: c };
      // No enclosing block: the method's own body.
      return best || { from: -1, to: body.length };
    };

    const placedDecls = decls.map(d => ({ ...d, ext: extentOf(d.at) }));

    // A parameter is live across the whole method, so it contains everything.
    const outer = params.map(name => ({ name, at: -1, ext: { from: -1, to: body.length } }));
    const all = outer.concat(placedDecls);

    const lambdas = blocks.filter(([o]) => isLambdaBlock(body, o));

    for (const d of placedDecls) {
      for (const o of all) {
        if (o === d || o.name !== d.name) continue;
        // STRICT containment: o's scope wraps d's. Equal extents are two declarations in one scope,
        // which is CS0128 — a different error, and one the compiler reports just as loudly.
        const contains = o.ext.from <= d.ext.from && o.ext.to >= d.ext.to
                      && !(o.ext.from === d.ext.from && o.ext.to === d.ext.to);
        if (!contains || o.at >= d.at) continue;

        // A lambda body between the two? Then this is the case the header says is not claimed.
        const crossesLambda = lambdas.some(([lo, lc]) =>
          d.at > lo && d.at < lc && !(o.at > lo && o.at < lc));
        if (crossesLambda) continue;

        shadow.push({ rel, line: lineAt(raw, mth.at + d.at), name: d.name, method: mth.name });
        break;
      }
    }

  }
}

console.log(`Scanned ${files.length} C# files for scope errors the name checks cannot see.\n`);

let bad = 0;
if (shadow.length) {
  bad += shadow.length;
  console.log(`SHADOW  ${shadow.length} local(s) shadowing an enclosing scope (CS0136):`);
  for (const f of shadow) console.log(`  ${f.rel}:${f.line}  '${f.name}' in ${f.method}()`);
  console.log();
}
if (!bad) console.log('Clean. No local shadows a declaration from an enclosing scope.');
process.exit(bad ? 1 : 0);
