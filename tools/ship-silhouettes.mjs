// ============================================================================================
// LOOK AT THE FLEET — silhouettes rendered straight from the geometry
//
//   node tools/ship-silhouettes.mjs [--out <png>] [--cols 5]
//
// Orientation has two halves. Which AXIS a hull is long on is answerable from its bounding box, and
// make-orientation-manifest.mjs answers it. WHICH END IS THE BOW is not: a box is symmetric, and
// measuring where the mass sits (measure-bow.mjs) only moves the question — a swordfish's sharp end
// is its bill and a shrimp's is its tail, so "the pointed end" is the bow on one and the stern on
// the next. The fleet genuinely disagrees, and no statistic settles it.
//
// What settles it is LOOKING, and there is no Unity in this environment to look in. So the mesh is
// rasterised here instead, with no GL involved at all: every triangle is projected onto the plane of
// the hull's two longest axes and filled, which is exactly the top-down profile in which a bow is
// obvious. Each cell is labelled and carries an arrow marking the +axis direction, so the verdict
// reads straight off the picture and into ship-meshes.txt:
//
//     nose points the SAME way as the arrow   ->   the generated line is right
//     nose points AGAINST the arrow           ->   add 180 to that line's yaw
//
// Filled, not wireframe: at this size a wireframe of twelve thousand triangles is a grey rectangle.
// ============================================================================================

import { NodeIO } from '@gltf-transform/core';
import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };

const OUT  = path.resolve(PROJ, arg('--out', 'Art/_review/silhouettes.png'));
const COLS = parseInt(arg('--cols', '5'), 10);
const CELL = parseInt(arg('--cell', '300'), 10);
const LABEL = 26;

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

function triangles(doc) {
  const out = [];
  for (const mesh of doc.getRoot().listMeshes())
    for (const prim of mesh.listPrimitives()) {
      const pos = prim.getAttribute('POSITION');
      if (!pos) continue;
      const idx = prim.getIndices();
      const n = idx ? idx.getCount() : pos.getCount();
      const get = i => { const v = [0, 0, 0]; pos.getElement(idx ? idx.getScalar(i) : i, v); return v; };
      for (let i = 0; i + 2 < n; i += 3) out.push([get(i), get(i + 1), get(i + 2)]);
    }
  return out;
}

/** Fill one triangle into an 8-bit coverage buffer, darkening where the hull is deeper. */
function fillTri(buf, W, H, p0, p1, p2) {
  const minX = Math.max(0, Math.floor(Math.min(p0[0], p1[0], p2[0])));
  const maxX = Math.min(W - 1, Math.ceil(Math.max(p0[0], p1[0], p2[0])));
  const minY = Math.max(0, Math.floor(Math.min(p0[1], p1[1], p2[1])));
  const maxY = Math.min(H - 1, Math.ceil(Math.max(p0[1], p1[1], p2[1])));
  const d = (a, b, c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
  const areaAll = d(p0, p1, p2);
  if (Math.abs(areaAll) < 1e-9) return;
  for (let y = minY; y <= maxY; y++)
    for (let x = minX; x <= maxX; x++) {
      const p = [x + 0.5, y + 0.5];
      const w0 = d(p1, p2, p), w1 = d(p2, p0, p), w2 = d(p0, p1, p);
      const inside = areaAll > 0 ? (w0 >= 0 && w1 >= 0 && w2 >= 0) : (w0 <= 0 && w1 <= 0 && w2 <= 0);
      if (!inside) continue;
      const i = y * W + x;
      if (buf[i] < 255) buf[i] = Math.min(255, buf[i] + 26);
    }
}

const cells = [];

for (const root of ROOTS) {
  for (const f of walk(root)) {
    if (!f.toLowerCase().endsWith('.glb')) continue;
    const name = path.basename(f, '.glb');
    let doc;
    try { doc = await io.read(f); } catch (e) { console.log(`  !! ${name}: ${e.message}`); continue; }
    const tris = triangles(doc);
    if (!tris.length) continue;

    const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
    for (const t of tris) for (const v of t) for (let k = 0; k < 3; k++) {
      if (v[k] < lo[k]) lo[k] = v[k];
      if (v[k] > hi[k]) hi[k] = v[k];
    }
    const dims = [hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]];
    const order = [0, 1, 2].sort((a, b) => dims[b] - dims[a]);
    const A = order[0], B = order[1];          // the two longest: the profile plane

    const W = CELL, H = CELL - LABEL;
    const pad = 12;
    const sc = Math.min((W - pad * 2) / Math.max(1e-6, dims[A]), (H - pad * 2) / Math.max(1e-6, dims[B]));
    const ox = W / 2 - ((lo[A] + hi[A]) / 2) * sc;
    const oy = H / 2 + ((lo[B] + hi[B]) / 2) * sc;

    const buf = new Uint8Array(W * H);
    for (const t of tris) {
      const p = t.map(v => [v[A] * sc + ox, oy - v[B] * sc]);
      fillTri(buf, W, H, p[0], p[1], p[2]);
    }

    cells.push({ name, axis: 'XYZ'[A], up: 'XYZ'[B], buf, W, H });
    console.log(`  ${name.padEnd(28)} profile ${'XYZ'[A]}/${'XYZ'[B]}   ${dims.map(d => d.toFixed(2)).join(' x ')}`);
  }
}

cells.sort((a, b) => a.name.localeCompare(b.name));

const rows = Math.ceil(cells.length / COLS);
const SHEET_W = COLS * CELL, SHEET_H = rows * CELL;
const sheet = Buffer.alloc(SHEET_W * SHEET_H * 3);
for (let i = 0; i < SHEET_W * SHEET_H; i++) { sheet[i * 3] = 18; sheet[i * 3 + 1] = 22; sheet[i * 3 + 2] = 28; }

cells.forEach((c, i) => {
  const cx = (i % COLS) * CELL, cy = Math.floor(i / COLS) * CELL;
  for (let y = 0; y < c.H; y++)
    for (let x = 0; x < c.W; x++) {
      const v = c.buf[y * c.W + x];
      if (!v) continue;
      const o = ((cy + y) * SHEET_W + (cx + x)) * 3;
      sheet[o] = Math.min(255, 40 + v * 0.55);
      sheet[o + 1] = Math.min(255, 190 + v * 0.25);
      sheet[o + 2] = Math.min(255, 170 + v * 0.30);
    }
});

// Labels and the +axis arrow, as one SVG overlay.
let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SHEET_W}" height="${SHEET_H}">`;
cells.forEach((c, i) => {
  const cx = (i % COLS) * CELL, cy = Math.floor(i / COLS) * CELL;
  const ly = cy + CELL - 8;
  svg += `<rect x="${cx}" y="${cy + CELL - LABEL}" width="${CELL}" height="${LABEL}" fill="#0d1117"/>`;
  svg += `<text x="${cx + 6}" y="${ly}" fill="#c9d5e1" font-family="monospace" font-size="12">${c.name}</text>`;
  // The arrow runs left-to-right because +A was mapped to +x when projecting.
  const ay = cy + 16;
  svg += `<line x1="${cx + CELL - 74}" y1="${ay}" x2="${cx + CELL - 12}" y2="${ay}" stroke="#ffd166" stroke-width="2"/>`
       + `<polygon points="${cx + CELL - 12},${ay} ${cx + CELL - 22},${ay - 5} ${cx + CELL - 22},${ay + 5}" fill="#ffd166"/>`
       + `<text x="${cx + CELL - 92}" y="${ay + 4}" fill="#ffd166" font-family="monospace" font-size="12">+${c.axis}</text>`;
  svg += `<rect x="${cx + 0.5}" y="${cy + 0.5}" width="${CELL - 1}" height="${CELL - 1}" fill="none" stroke="#232b36"/>`;
});
svg += `</svg>`;

fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp(sheet, { raw: { width: SHEET_W, height: SHEET_H, channels: 3 } })
  .composite([{ input: Buffer.from(svg), top: 0, left: 0 }])
  .png()
  .toFile(OUT);

console.log(`\n${cells.length} silhouettes -> ${path.relative(PROJ, OUT)}  ${SHEET_W}x${SHEET_H}`);
console.log('Nose along the arrow = the manifest line is right. Nose against it = add 180 to that yaw.');
