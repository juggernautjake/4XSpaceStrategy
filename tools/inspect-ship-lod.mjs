// ============================================================================================
// HOW MUCH SHIP IS ACTUALLY THERE, AND HOW MUCH DOES THE SCREEN ASK FOR?
//
//   node tools/inspect-ship-lod.mjs [--civ Terran]
//
// The import pipeline decimates every hull to 12,000 triangles and squashes its textures to 512 and
// 256, and the header justifying that says ships are "drawn at BETWEEN 0.09 AND 0.40 WORLD UNITS"
// and "four thousand pixels of texture on forty pixels of ship is about a hundredfold waste."
//
// That reasoning is sound for the view it was written against and it has one enormous hole:
// CameraController.minHeight is 0.04 WORLD UNITS. The camera can be brought closer to a ship than the
// ship is long. At that point a hull is not forty pixels, it is the whole screen — and a 512-pixel
// base colour stretched across a 1080-pixel view is being magnified past two texels per pixel, which
// is exactly what "blobby" looks like.
//
// So this measures both halves and puts them side by side:
//
//   WHAT IS IN THE FILE   triangles and texture sizes per hull, read from the glb itself
//   WHAT THE SCREEN WANTS the pixel footprint of a hull at a range of camera heights, from the
//                         framing distance down to the closest the camera will go
//
// Wherever the second number is bigger than the first, the model is being asked for detail it does
// not have. That is the whole diagnosis, and it is a number rather than an opinion.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const CIV = arg('--civ', 'Terran');

const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);

// ---- the drawn sizes, read from the game ------------------------------------------------------
const rend = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Visual/UnitModelRenderer.cs'), 'utf8');
const sizes = {};
for (const m of rend.matchAll(/Station\(UnitType\.(\w+),\s*([0-9.]+)f\)/g)) sizes[m[1]] = +m[2];
for (const m of rend.matchAll(/Ship\(UnitType\.(\w+),\s*([0-9.]+)f/g)) sizes[m[1]] = +m[2];

// ---- the camera, read from the game -----------------------------------------------------------
const camSrc = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Managers/CameraController.cs'), 'utf8');
const camNum = (name, d) => {
  const m = new RegExp(String.raw`\b${name}\s*=\s*([0-9.]+)f`).exec(camSrc);
  return m ? parseFloat(m[1]) : d;
};
const MIN_H = camNum('minHeight', 0.04);
const FREE_MIN_H = camNum('freeLookMinHeight', 0.35);

// ---- what the last import actually decided -----------------------------------------------------
//
// The importer writes a record of every hull it wrote and every level it deliberately skipped. That
// record is the only place the reason for a missing level exists — it depends on the triangle count
// the simplifier reached, which cannot be recovered from the shipped files.
const importReport = new Map();
{
  const p = path.join(PROJ, 'tools/ship-import-report.json');
  if (fs.existsSync(p)) {
    const raw = JSON.parse(fs.readFileSync(p, 'utf8'));
    const rows = Array.isArray(raw) ? raw : (raw.models ?? raw.report ?? []);
    for (const r of rows) importReport.set(r.name, r);
  }
}

// A 60-degree vertical field of view at 1080p is the reference view. What matters is the ratio, and
// this is the one every other resolution scales from.
const FOV_DEG = 60, SCREEN_H = 1080;

/// How many pixels tall a thing of `size` world units is, seen from `height` world units away.
function pixelsAt(size, height) {
  const halfFov = (FOV_DEG / 2) * Math.PI / 180;
  const worldHeightAtDistance = 2 * height * Math.tan(halfFov);
  return (size / worldHeightAtDistance) * SCREEN_H;
}

// ============================================================================================
async function inspect(file) {
  const doc = await io.read(file);
  const root = doc.getRoot();

  let tris = 0, verts = 0;
  const attrs = new Set();
  for (const mesh of root.listMeshes())
    for (const prim of mesh.listPrimitives()) {
      const idx = prim.getIndices();
      const pos = prim.getAttribute('POSITION');
      tris += idx ? idx.getCount() / 3 : (pos ? pos.getCount() / 3 : 0);
      verts += pos ? pos.getCount() : 0;
      for (const s of prim.listSemantics()) attrs.add(s);
    }

  // Texture sizes, by the slot each one is plugged into — a 512 base colour and a 256 normal are very
  // different facts and reporting one number for "textures" would hide which.
  const slots = {};
  for (const mat of root.listMaterials()) {
    const put = (name, tex) => {
      if (!tex) return;
      const img = tex.getImage();
      let w = 0, h = 0;
      const size = tex.getSize?.();
      if (size) { w = size[0]; h = size[1]; }
      slots[name] = Math.max(slots[name] ?? 0, Math.max(w, h));
      slots[`${name}_bytes`] = (slots[`${name}_bytes`] ?? 0) + (img ? img.byteLength : 0);
    };
    put('base', mat.getBaseColorTexture());
    put('normal', mat.getNormalTexture());
    put('mr', mat.getMetallicRoughnessTexture());
    put('emissive', mat.getEmissiveTexture());
    put('occlusion', mat.getOcclusionTexture());
  }

  return { tris: Math.round(tris), verts, slots, attrs, bytes: fs.statSync(file).size };
}

// ============================================================================================
// THE CHECKS
//
// A LOD chain fails silently in ways that only show on screen, and each of these is one that
// actually happened while this was being built.
// ============================================================================================
const notes = [];

function checkChain(civ, type, base, hi, lo, problems) {
  const at = `${civ} ${type}`;

  // ---- THE ONE THAT BIT ----
  //
  // The LOD levels carry no textures and adopt the base file's material at load time. That only works
  // if they still have UVs — and stripping the textures orphaned the TEXCOORD_0 accessor, which the
  // next prune() then collected, entirely reasonably. The high-detail hull shipped with no UVs at
  // all, so every vertex would have sampled the same texel and the MOST detailed mesh in the game
  // would have rendered as a flat single-coloured blob. Which is precisely the defect the whole LOD
  // change exists to fix.
  for (const [name, doc] of [['_hi', hi], ['_lo', lo]]) {
    if (!doc) continue;
    if (!doc.attrs.has('TEXCOORD_0'))
      problems.push(`${at}${name} has no TEXCOORD_0 — it adopts the base material and would sample ` +
                    `one texel across the whole hull`);
    if ((doc.slots.base ?? 0) > 0)
      problems.push(`${at}${name} carries its own textures — it should share the base file's, ` +
                    `and three copies of a 1024 normal map is three times the texture memory`);
  }

  // The base file has to stand alone: it is the fallback when the siblings are missing, and it is
  // where every texture in the chain lives.
  if (!(base.slots.base > 0))
    problems.push(`${at} base file has no base-colour texture — nothing in the chain would be textured`);
  if (!base.attrs.has('TEXCOORD_0'))
    problems.push(`${at} base file has no TEXCOORD_0`);

  // A chain whose levels are not ordered is not a chain. Simplification cannot always hit its target
  // on a mesh made of many disconnected shells, so this only complains when the order is actually
  // wrong rather than when a level missed its budget.
  if (hi && hi.tris < base.tris)
    problems.push(`${at}: _hi has FEWER triangles than the base (${hi.tris} < ${base.tris})`);
  // A chain whose three levels are all much the same size is three copies of one mesh doing one
  // mesh's job. It happens on hulls built from many disconnected shells — the simplifier cannot
  // collapse an edge that is not shared, so it stops well short of its budget. Reported rather than
  // failed: the chain still WORKS, it just is not buying anything, and the fix is in the source art.
  if (hi && lo && lo.tris > hi.tris * 0.6)
    notes.push(`${at}: all three levels are within a whisker of each other (${lo.tris}/${base.tris}/${hi.tris}) ` +
               `— the mesh is too fragmented to simplify, so the LOD chain costs bytes and saves nothing`);

  if (lo && lo.tris > base.tris)
    problems.push(`${at}: _lo has MORE triangles than the base (${lo.tris} > ${base.tris})`);
}

// ============================================================================================
// ---- BOTH folders, and that took a bug to notice ----
//
// This scanned Ships/ only, so the entire Stations/ tree went unchecked. Which is exactly the tree
// that needed checking: a station is built from separate structural pieces — masts, dishes, rings —
// so its mesh is the most fragmented and the least simplifiable in the game. Seventeen of the twenty
// levels the importer decided were redundant were stations, and this report had nothing to say about
// any of them because it had never looked.
const DIRS = ['Ships', 'Stations']
  .map(sub => path.join(PROJ, 'Assets/Resources/SpaceAssets', sub, CIV))
  .filter(d => fs.existsSync(d));

if (DIRS.length === 0) { console.log(`no shipped art for ${CIV}`); process.exit(0); }

// Only the BASE files are hulls; _hi and _lo are levels of one of them.
const files = DIRS.flatMap(dir =>
  fs.readdirSync(dir)
    .filter(f => f.endsWith('.glb') && !/_(hi|lo)\.glb$/.test(f))
    .map(f => ({ dir, f })))
  .sort((a, b) => a.f.localeCompare(b.f));

console.log(`SHIPPED — ${CIV}, ${files.length} hulls\n`);
console.log('hull                    _lo     mid     _hi     base  normal   m/r   chain KB   drawn u');
console.log(''.padEnd(92, '-'));

const problems = [];
const rows = [];
let chainBytes = 0;

for (const { dir, f } of files) {
  const type = f.replace(/\.glb$/, '').replace(`${CIV}_`, '');
  const stem = path.join(dir, f.replace(/\.glb$/, ''));

  const base = await inspect(stem + '.glb');
  const hi = fs.existsSync(stem + '_hi.glb') ? await inspect(stem + '_hi.glb') : null;
  const lo = fs.existsSync(stem + '_lo.glb') ? await inspect(stem + '_lo.glb') : null;

  checkChain(CIV, type, base, hi, lo, problems);

  // ---- A MISSING LEVEL IS NOT AUTOMATICALLY A FAULT ----
  //
  // The importer skips a level that came out within LOD_MIN_SEPARATION of the base one, because a
  // hull the simplifier cannot reduce produces three near-identical meshes and writing all three
  // costs megabytes for three ways to draw the same thing.
  //
  // Whether that applies to a given hull is NOT derivable from the shipped files: it depends on the
  // triangle count the simplifier actually reached, which is a fact about the source mesh that only
  // the import run knows. The first version of this check guessed from the budgets and cried wolf on
  // exactly the two hulls the importer had handled correctly.
  //
  // So it reads the importer's own record. A level absent from disk AND marked skipped in the report
  // is a decision; a level absent from disk with no such record is a fault.
  const rec = importReport.get(`${CIV}_${type}`);
  const skipped = (suffix) => rec?.levels?.some(l => l.suffix === suffix && l.skipped) ?? false;

  if (!hi && !skipped('_hi'))
    problems.push(`${CIV} ${type}: no _hi level and the importer did not skip one — it is missing`);
  if (!lo && !skipped('_lo'))
    problems.push(`${CIV} ${type}: no _lo level and the importer did not skip one — it is missing`);
  if ((!hi && skipped('_hi')) || (!lo && skipped('_lo')))
    notes.push(`${CIV} ${type}: ships with fewer levels — its mesh is too fragmented to simplify ` +
               `(base ${base.tris} tris), so the others would have been copies`);

  const bytes = base.bytes + (hi?.bytes ?? 0) + (lo?.bytes ?? 0);
  chainBytes += bytes;
  const drawn = sizes[type] ?? 0.2;
  rows.push({ type, ...base, drawn, hi, lo, bytes });

  console.log(
    `  ${type.padEnd(20)} ${String(lo?.tris ?? 0).padStart(5)}  ${String(base.tris).padStart(6)}` +
    `  ${String(hi?.tris ?? 0).padStart(6)}     ${String(base.slots.base ?? 0).padStart(4)}` +
    `    ${String(base.slots.normal ?? 0).padStart(4)}  ${String(base.slots.mr ?? 0).padStart(4)}` +
    `     ${String(Math.round(bytes / 1024)).padStart(6)}    ${drawn.toFixed(2)}`);
}

console.log(`\n  ${files.length} hulls, ${(chainBytes / 1048576).toFixed(1)} MB of ${CIV} art ` +
            `(${(chainBytes / files.length / 1048576).toFixed(2)} MB per hull, all three levels)`);

// ---- what the source art still holds ----------------------------------------------------------
const srcDir = path.join(PROJ, 'Art/Active', CIV);
if (fs.existsSync(srcDir)) {
  const units = fs.readdirSync(srcDir).filter(d =>
    fs.statSync(path.join(srcDir, d)).isDirectory());
  let found = null;
  for (const u of units) {
    const glb = fs.readdirSync(path.join(srcDir, u)).find(f => f.endsWith('.glb'));
    if (glb) { found = path.join(srcDir, u, glb); break; }
  }
  if (found) {
    const r = await inspect(found);
    console.log(`\nSOURCE — one sample from Art/Active/${CIV}`);
    console.log(`  ${path.basename(path.dirname(found))}: ${r.tris.toLocaleString()} tris, ` +
                `base ${r.slots.base ?? 0}, normal ${r.slots.normal ?? 0}, ` +
                `${(r.bytes / 1048576).toFixed(1)} MB`);
    console.log(`  the shipped version keeps ${(100 * (rows[0]?.tris ?? 0) / Math.max(1, r.tris)).toFixed(1)}% ` +
                `of the triangles and ${(100 * (rows[0]?.slots.base ?? 0) / Math.max(1, r.slots.base ?? 1)).toFixed(0)}% ` +
                `of the base-colour resolution`);
  }
}

// ============================================================================================
// WHAT THE SCREEN ASKS FOR
// ============================================================================================
console.log('\n\nWHAT THE SCREEN ASKS FOR — a hull at 1080p, 60 degree vertical FOV\n');
console.log('camera height    fighter 0.19u        dreadnought 0.40u     mega-station 0.52u');
console.log(''.padEnd(84, '-'));

const HEIGHTS = [
  ['framing a system', 40],
  ['close orbit', 4],
  ['very close', 1],
  ['free-look floor', FREE_MIN_H],
  ['absolute floor', MIN_H],
];

for (const [label, h] of HEIGHTS) {
  const f = pixelsAt(0.19, h), d = pixelsAt(0.40, h), m = pixelsAt(0.52, h);
  console.log(`  ${label.padEnd(16)} ${Math.round(f).toString().padStart(6)} px` +
              `        ${Math.round(d).toString().padStart(7)} px` +
              `           ${Math.round(m).toString().padStart(7)} px`);
}

// ---- the verdict --------------------------------------------------------------------------------
const baseTex = rows.length ? (rows[0].slots.base ?? 0) : 0;
const normTex = rows.length ? (rows[0].slots.normal ?? 0) : 0;
const medianTris = rows.length ? rows.map(r => r.tris).sort((a, b) => a - b)[rows.length >> 1] : 0;

console.log('\n\nTHE MISMATCH\n');
for (const [label, h] of HEIGHTS) {
  const px = pixelsAt(0.40, h);
  const texRatio = px / Math.max(1, baseTex);
  const verdict = texRatio <= 0.5 ? 'texture to spare'
                : texRatio <= 1.0 ? 'texture about right'
                : `texture magnified ${texRatio.toFixed(1)}x — BLURRY`;
  console.log(`  ${label.padEnd(16)} dreadnought ${Math.round(px).toString().padStart(6)} px   ${verdict}`);
}

console.log(`\n  Shipped: ${medianTris.toLocaleString()} tris (median), ` +
            `${baseTex} base colour, ${normTex} normal.`);
console.log(`  A hull filling a 1080p screen wants roughly 1024-2048 of base colour and a normal map ` +
            `at least as large.`);
console.log(`  At ${normTex} the normal map is carrying about ` +
            `${(100 * normTex / 1024).toFixed(0)}% of the surface detail a close view asks for, which is ` +
            `where "blobby" comes from as much as the triangle count does.`);


// ---- verdict ------------------------------------------------------------------------------------
console.log('');
for (const n of notes) console.log(`note  ${n}`);
if (notes.length) console.log('');
if (problems.length === 0) {
  console.log('Every LOD chain is complete, ordered, textured on the base only, and keeps its UVs.');
  process.exit(0);
}
for (const p2 of problems) console.log(`FAIL  ${p2}`);
console.log(`\n${problems.length} problem(s) in the LOD chains.`);
process.exit(1);
