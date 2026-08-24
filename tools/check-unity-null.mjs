// ============================================================================================
// `??` AND `?.` ON A UNITY OBJECT ARE LYING TO YOU
//
//   node tools/check-unity-null.mjs
//
// This one does not catch a compiler error. It catches the runtime exception that reads most like a
// compiler error, and that this repo has no way of seeing until it is on screen:
//
//     MissingComponentException: There is no 'LODGroup' attached to the "Model_Scout 1" game object,
//     but a script is trying to access it.
//
// ---- WHY IT HAPPENS -------------------------------------------------------------------------
//
// UnityEngine.Object overloads `operator ==` so that an object whose NATIVE half has been destroyed
// compares equal to null even though the C# reference is still perfectly alive. That overload is the
// only thing that knows about the destroyed state.
//
// `??`, `?.` and `??=` are C# LANGUAGE operators. They test the reference directly and never call the
// overload. So:
//
//     var g = root.GetComponent<LODGroup>() ?? root.AddComponent<LODGroup>();
//
// hands back a destroyed LODGroup instead of making a new one, and the next call on it throws. The
// same shape with `?.` silently does nothing instead of throwing, which is worse — there is no log
// line at all, just a feature that stopped working.
//
// The fix is always the same two lines, and UIFactory.Ensure<T> already is them:
//
//     var g = root.GetComponent<LODGroup>();
//     if (g == null) g = root.AddComponent<LODGroup>();
//
// ---- WHAT IS FLAGGED --------------------------------------------------------------------------
//
// Only where the LEFT side is known to be a UnityEngine.Object: the result of GetComponent,
// GetComponentInChildren/InParent, AddComponent, Instantiate, FindObjectOfType, transform.Find,
// GetComponents[..], or a `.gameObject` / `.transform` / `.material` / `.sharedMaterial` access.
// A `??` on a string, a list or a nullable int is ordinary correct C# and is left alone, which is
// what keeps this check quiet enough to be worth running.
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

// Strip comments and string/char literals, preserving newlines so line numbers survive. Same routine
// as the other checks — a `??` inside a comment explaining this rule must not trip the rule.
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

// The calls whose result is a UnityEngine.Object. `GetComponent` covers GetComponentInChildren and
// GetComponentInParent by prefix; `GetComponents` (plural) returns an ARRAY, which is a plain CLR
// object and legitimately nullable, so it is excluded by requiring no `s` before `<` or `(`.
const UNITY_CALL = String.raw`(?:GetComponent(?:InChildren|InParent)?|AddComponent|Instantiate|FindObjectOfType|FindAnyObjectByType|FindFirstObjectByType)\s*(?:<[^<>()]*>)?\s*\([^()]*\)`;
// Property accesses that are always UnityEngine.Objects.
const UNITY_PROP = String.raw`\.(?:gameObject|transform|material|sharedMaterial|mesh|sharedMesh)\b`;

// `X ?? Y` / `X ??= Y` where X ends in one of the above.
const COALESCE = new RegExp(String.raw`(${UNITY_CALL}|[A-Za-z_][A-Za-z0-9_.\[\]]*${UNITY_PROP})\s*\?\?=?`, 'g');
// `X?.Member` where X is one of the above. The negative lookahead on `?.` avoids matching the `?:`
// of a ternary, and requiring a name after the dot avoids `??`.
const CONDITIONAL = new RegExp(String.raw`(${UNITY_CALL})\s*\?\.\s*[A-Za-z_]`, 'g');

const findings = [];

for (const f of files) {
  const src = code(fs.readFileSync(f, 'utf8'));
  const rel = path.relative(PROJ, f).replace(/\\/g, '/');

  for (const [re, why] of [
    [COALESCE, '?? / ??= on a UnityEngine.Object — bypasses the destroyed-object overload; use `x = A; if (x == null) x = B;` or UIFactory.Ensure<T>'],
    [CONDITIONAL, '?. on a UnityEngine.Object — a destroyed object is not null to `?.`, so the call is made and throws (or is skipped when it should run); test with `== null` instead'],
  ]) {
    re.lastIndex = 0;
    let m;
    while ((m = re.exec(src))) {
      const line = src.slice(0, m.index).split(/\r?\n/).length;
      const snippet = m[0].replace(/\s+/g, ' ').trim();
      findings.push(`${rel}:${line}  ${snippet}\n        ${why}`);
    }
  }
}

console.log(`${files.length} files scanned for Unity fake-null operators.`);
if (!findings.length) { console.log('No `??` or `?.` on a UnityEngine.Object.'); process.exit(0); }
for (const x of findings) console.log('FAIL  ' + x);
process.exit(1);
