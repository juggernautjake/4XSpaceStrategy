// ============================================================================================
// WHICH END IS THE BOW — measured, not guessed
//
//   node tools/measure-bow.mjs
//
// make-orientation-manifest.mjs settles which AXIS a hull is long on, from its bounding box. That is
// the half of orientation a box can answer. It cannot answer the other half — a box is symmetric, so
// nothing in it says which end the engines are on — and a hull with the axis right and the ends
// swapped flies backwards, tail first, all the way across the system.
//
// This measures the half the box cannot. Two independent numbers per mesh, both taken along the
// hull's own long axis:
//
//   massSkew   where the TRIANGLE AREA sits, as a fraction of the half-length from the centre.
//              Positive means the model is bulkier toward +axis. A ship is heavier aft (engines,
//              armour, reactor) and tapers forward; a fish is heavier forward (head, body) and tapers
//              to a tail. Either way the taper and the bulk are at opposite ends, which is the fact
//              being measured
//
//   tipSharp   how much narrower the mesh's cross-section is in its last tenth at each end. The
//              SHARPER end is the point — a bow, a bill, a snout, a nose
//
// Neither is a truth. Together, and read across a whole fleet that came off ONE pipeline, they say
// something much more useful than either: whether the fleet AGREES. If every hull skews the same way
// then Meshy's export frame is consistent, one 180 decides the whole fleet, and the manifest can be
// corrected in a single pass instead of twenty-nine judgement calls. That is the question this
// answers.
// ============================================================================================

import { NodeIO } from '@gltf-transform/core';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ROOTS = [
  path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'Ships'),
  path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'Stations'),
];

const io = new NodeIO();

function walk(dir, acc = []) {
  if (!fs.existsSync(dir)) return acc;
  for (const f of fs.readdirSync(dir)) {
    const fp = path.join(dir, f);
    fs.statSync(fp).isDirectory() ? walk(fp, acc) : acc.push(fp);
  }
  return acc;
}

/** Every triangle of the document, in world space, as [a,b,c] vertex triples. */
function triangles(doc) {
  const out = [];
  for (const mesh of doc.getRoot().listMeshes()) {
    for (const prim of mesh.listPrimitives()) {
      const pos = prim.getAttribute('POSITION');
      if (!pos) continue;
      const idx = prim.getIndices();
      const n = idx ? idx.getCount() : pos.getCount();
      const get = i => { const v = [0, 0, 0]; pos.getElement(idx ? idx.getScalar(i) : i, v); return v; };
      for (let i = 0; i + 2 < n; i += 3) out.push([get(i), get(i + 1), get(i + 2)]);
    }
  }
  return out;
}

const area = (a, b, c) => {
  const u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
  const v = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
  const x = u[1] * v[2] - u[2] * v[1], y = u[2] * v[0] - u[0] * v[2], z = u[0] * v[1] - u[1] * v[0];
  return 0.5 * Math.hypot(x, y, z);
};

const rows = [];

for (const root of ROOTS) {
  for (const f of walk(root)) {
    if (!f.toLowerCase().endsWith('.glb')) continue;
    const name = path.basename(f, '.glb');
    let doc;
    try { doc = await io.read(f); } catch (e) { console.log(`  !! ${name}: ${e.message}`); continue; }

    const tris = triangles(doc);
    if (!tris.length) { console.log(`  !! ${name}: no triangles`); continue; }

    // Bounds, and therefore the long axis.
    const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
    for (const t of tris) for (const v of t) for (let k = 0; k < 3; k++) {
      if (v[k] < lo[k]) lo[k] = v[k];
      if (v[k] > hi[k]) hi[k] = v[k];
    }
    const dims = [hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]];
    const L = dims.indexOf(Math.max(...dims));
    const mid = (hi[L] + lo[L]) / 2, half = Math.max(1e-6, dims[L] / 2);

    // massSkew: area-weighted centroid along the long axis.
    let wSum = 0, w = 0;
    // cross-section extent per slice, for the tip test.
    const SLICES = 20;
    const spanPerSlice = new Array(SLICES).fill(0).map(() => ({ lo: [Infinity, Infinity], hi: [-Infinity, -Infinity] }));
    const others = [0, 1, 2].filter(k => k !== L);

    for (const t of tris) {
      const a = area(...t);
      if (!(a > 0)) continue;
      const cx = (t[0][L] + t[1][L] + t[2][L]) / 3;
      wSum += a * (cx - mid);
      w += a;
      let s = Math.floor(((cx - lo[L]) / Math.max(1e-9, dims[L])) * SLICES);
      s = Math.min(SLICES - 1, Math.max(0, s));
      for (const v of t) for (let oi = 0; oi < 2; oi++) {
        const k = others[oi];
        if (v[k] < spanPerSlice[s].lo[oi]) spanPerSlice[s].lo[oi] = v[k];
        if (v[k] > spanPerSlice[s].hi[oi]) spanPerSlice[s].hi[oi] = v[k];
      }
    }

    const massSkew = (wSum / Math.max(1e-9, w)) / half;

    // Mean cross-section girth of the first two slices vs the last two.
    const girth = s => {
      const g = spanPerSlice[s];
      if (g.hi[0] < g.lo[0]) return 0;
      return Math.hypot(g.hi[0] - g.lo[0], g.hi[1] - g.lo[1]);
    };
    const gMax = Math.max(...spanPerSlice.map((_, i) => girth(i))) || 1;
    const negTip = (girth(0) + girth(1)) / 2 / gMax;
    const posTip = (girth(SLICES - 1) + girth(SLICES - 2)) / 2 / gMax;
    // Positive means the +axis end is the SHARPER one.
    const tipSharp = negTip - posTip;

    rows.push({ name, axis: 'XYZ'[L], dims: dims.map(d => d.toFixed(2)).join(' x '), massSkew, tipSharp });
  }
}

rows.sort((a, b) => a.name.localeCompare(b.name));

console.log('\nname                          axis   massSkew   tipSharp   bulk / point');
console.log(''.padEnd(86, '-'));
for (const r of rows) {
  const bulk = r.massSkew >= 0 ? '+' : '-';
  const point = r.tipSharp >= 0 ? '+' : '-';
  console.log(
    r.name.padEnd(30) + r.axis.padEnd(7) +
    r.massSkew.toFixed(3).padStart(8) + r.tipSharp.toFixed(3).padStart(11) +
    `     bulk ${bulk}${r.axis}, point ${point}${r.axis}` +
    (bulk === point ? '   ??' : ''));
}

const posSkew = rows.filter(r => r.massSkew > 0).length;
const posTipS = rows.filter(r => r.tipSharp > 0).length;
console.log(''.padEnd(86, '-'));
console.log(`${rows.length} meshes.  bulk toward +axis: ${posSkew}/${rows.length}` +
            `   point toward +axis: ${posTipS}/${rows.length}`);
console.log(posSkew === 0 || posSkew === rows.length
  ? 'The fleet AGREES on where its mass sits — one 180 decides all of them.'
  : 'The fleet DISAGREES — the export frame is not consistent and these want checking by eye.');
console.log('"??" marks a hull whose bulk and whose point are at the SAME end, where neither test helps.');
