// ============================================================================================
// HOW EVERY HULL WILL ACTUALLY FLY — drawn in the game's own frame
//
//   node tools/ship-orientation-sheet.mjs [--match "^Cryithn_"] [--out Art/_review/orientation.png]
//
// ship-silhouettes.mjs draws the RAW glTF geometry, which is the right picture for reading a hull's
// shape and the wrong one for checking a manifest line: glTFast mirrors X on import (see
// bake-ship-hardpoints.mjs for the source), so "the nose points along the +X arrow" on that sheet is the
// nose on Unity -X, and every yaw read off it has to be negated in your head.
//
// This sheet does that conversion for you. Each hull is taken through exactly what the game does to it
// — glTF x negated, then the ship-meshes.txt rotation — and drawn in SHIP SPACE:
//
//   left   TOP VIEW    starboard to the right, BOW UP
//   right  SIDE VIEW   dorsal to the right,    BOW UP
//
// So every cell should show its nose at the top. A hull whose tail is at the top is flying backwards;
// a hull lying on its side in the side view is rolled. Brighter = more surface stacked there; orange =
// the emission map (lava, drive bells, bioluminescence), which is where the plumes and the glow go.
// Baked nozzles are marked with cyan dots at the bottom of each top view; guns with small red ticks.
// ============================================================================================

import { NodeIO } from '@gltf-transform/core';
import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const RES = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.resolve(PROJ, arg('--out', 'Art/_review/orientation.png'));
const MATCH = arg('--match', '');
const MATCH_RE = MATCH ? new RegExp(MATCH) : null;
const COLS = parseInt(arg('--cols', '6'), 10);
const HALF = parseInt(arg("--size", "150"), 10), CELL_W = HALF * 2, LABEL = 30, CELL_H = HALF + LABEL;

const CIVS = ['Terran', 'Aquarii', 'Pyrothian', 'Cryithn', 'Sylvan'];

function readManifest() {
  const map = new Map();
  for (const raw of fs.readFileSync(path.join(RES, 'Ships', 'ship-meshes.txt'), 'utf8').split('\n')) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    const tok = line.split(/\s+/);
    let first = -1;
    for (let i = 1; i < tok.length; i++) if (/^[-+]?\d/.test(tok[i])) { first = i; break; }
    if (first < 1 || first + 2 >= tok.length) continue;
    map.set(tok.slice(0, first).join(' '), [0, 1, 2].map(k => parseFloat(tok[first + k])));
  }
  return map;
}

function eulerMatrix([px, py, pz]) {
  const r = d => d * Math.PI / 180;
  const cx = Math.cos(r(px)), sx = Math.sin(r(px)), cy = Math.cos(r(py)), sy = Math.sin(r(py)), cz = Math.cos(r(pz)), sz = Math.sin(r(pz));
  const Rx = [[1, 0, 0], [0, cx, -sx], [0, sx, cx]], Ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]], Rz = [[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]];
  const mul = (a, b) => a.map((row, i) => [0, 1, 2].map(j => row[0] * b[0][j] + row[1] * b[1][j] + row[2] * b[2][j]));
  return mul(Ry, mul(Rx, Rz));
}
const apply = (m, v) => [0, 1, 2].map(i => m[i][0] * v[0] + m[i][1] * v[1] + m[i][2] * v[2]);

async function emissionSampler(prim) {
  const mat = prim.getMaterial(); const tex = mat?.getEmissiveTexture(); const img = tex?.getImage();
  if (!img) return null;
  try {
    const { data, info } = await sharp(Buffer.from(img)).removeAlpha().raw().toBuffer({ resolveWithObject: true });
    return (u, v) => {
      const x = Math.min(info.width - 1, Math.max(0, Math.floor((u - Math.floor(u)) * info.width)));
      const y = Math.min(info.height - 1, Math.max(0, Math.floor((v - Math.floor(v)) * info.height)));
      const i = (y * info.width + x) * info.channels;
      return (0.2126 * data[i] + 0.7152 * data[i + 1] + 0.0722 * data[i + 2]) / 255;
    };
  } catch { return null; }
}

function fill(buf, emi, W, H, p0, p1, p2, e) {
  const minX = Math.max(0, Math.floor(Math.min(p0[0], p1[0], p2[0]))), maxX = Math.min(W - 1, Math.ceil(Math.max(p0[0], p1[0], p2[0])));
  const minY = Math.max(0, Math.floor(Math.min(p0[1], p1[1], p2[1]))), maxY = Math.min(H - 1, Math.ceil(Math.max(p0[1], p1[1], p2[1])));
  const d = (a, b, c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
  const A = d(p0, p1, p2);
  if (Math.abs(A) < 1e-9) return;
  for (let y = minY; y <= maxY; y++) for (let x = minX; x <= maxX; x++) {
    const p = [x + 0.5, y + 0.5];
    const w0 = d(p1, p2, p), w1 = d(p2, p0, p), w2 = d(p0, p1, p);
    if (A > 0 ? (w0 < 0 || w1 < 0 || w2 < 0) : (w0 > 0 || w1 > 0 || w2 > 0)) continue;
    const i = y * W + x;
    if (buf[i] < 255) buf[i] = Math.min(255, buf[i] + 22);
    if (e > emi[i]) emi[i] = e;
  }
}

const manifest = readManifest();
const hardpoints = (() => {
  try { return new Map(JSON.parse(fs.readFileSync(path.join(RES, 'Ships', 'ship-hardpoints.json'), 'utf8')).entries.map(e => [e.name, e])); }
  catch { return new Map(); }
})();
const io = new NodeIO();
const cells = [];

for (const civ of CIVS) for (const sub of ['Ships', 'Stations']) {
  const dir = path.join(RES, sub, civ);
  if (!fs.existsSync(dir)) continue;
  for (const f of fs.readdirSync(dir).sort()) {
    if (!f.endsWith('.glb') || /_(hi|lo)\.glb$/.test(f)) continue;
    const name = f.replace(/\.glb$/, '');
    if (MATCH_RE && !MATCH_RE.test(name)) continue;
    const rot = manifest.get(name) || [0, 0, 0];
    const R = eulerMatrix(rot);
    const doc = await io.read(path.join(dir, f));
    const tris = [];
    for (const node of doc.getRoot().listNodes()) {
      const mesh = node.getMesh(); if (!mesh) continue;
      for (const prim of mesh.listPrimitives()) {
        const pos = prim.getAttribute('POSITION'), uv = prim.getAttribute('TEXCOORD_0'), idx = prim.getIndices();
        if (!pos) continue;
        const sample = uv ? await emissionSampler(prim) : null;
        const n = idx ? idx.getCount() : pos.getCount();
        const P = [0, 0, 0], T = [0, 0];
        for (let i = 0; i + 2 < n; i += 3) {
          const t = [], uvs = [];
          for (let k = 0; k < 3; k++) {
            const j = idx ? idx.getScalar(i + k) : i + k;
            pos.getElement(j, P);
            t.push(apply(R, [-P[0], P[1], P[2]]));      // glTFast x-negation, then the manifest
            if (sample) { uv.getElement(j, T); uvs.push([T[0], T[1]]); }
          }
          const e = sample ? sample((uvs[0][0] + uvs[1][0] + uvs[2][0]) / 3, (uvs[0][1] + uvs[1][1] + uvs[2][1]) / 3) : 0;
          tris.push({ t, e });
        }
      }
    }
    const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
    for (const { t } of tris) for (const v of t) for (let k = 0; k < 3; k++) { lo[k] = Math.min(lo[k], v[k]); hi[k] = Math.max(hi[k], v[k]); }
    const ext = Math.max(hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]) || 1;
    const sc = (HALF - 16) / ext;
    const c = [(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2];
    // top: screen x = ship x, screen up = ship z.  side: screen x = ship y, screen up = ship z.
    const top = v => [HALF / 2 + (v[0] - c[0]) * sc, HALF / 2 - (v[2] - c[2]) * sc];
    const side = v => [HALF / 2 + (v[1] - c[1]) * sc, HALF / 2 - (v[2] - c[2]) * sc];
    const views = [top, side].map(proj => {
      const buf = new Uint8Array(HALF * HALF), emi = new Float32Array(HALF * HALF);
      for (const { t, e } of tris) fill(buf, emi, HALF, HALF, proj(t[0]), proj(t[1]), proj(t[2]), e);
      return { buf, emi };
    });
    const marks = [];
    const hp = hardpoints.get(name);
    if (hp) {
      for (const nz of hp.nozzles) marks.push({ p: top(apply(R, nz.p)), kind: 'nozzle' });
      for (const g of hp.guns) marks.push({ p: top(apply(R, g.p)), kind: 'gun' });
    }
    cells.push({ name, rot, views, marks });
  }
}

const rows = Math.ceil(cells.length / COLS);
const SW = COLS * CELL_W, SH = rows * CELL_H;
const sheet = Buffer.alloc(SW * SH * 3);
for (let i = 0; i < SW * SH; i++) { sheet[i * 3] = 16; sheet[i * 3 + 1] = 19; sheet[i * 3 + 2] = 25; }
cells.forEach((cell, i) => {
  const cx = (i % COLS) * CELL_W, cy = Math.floor(i / COLS) * CELL_H;
  cell.views.forEach((v, vi) => {
    for (let y = 0; y < HALF; y++) for (let x = 0; x < HALF; x++) {
      const k = y * HALF + x, b = v.buf[k];
      if (!b) continue;
      const o = ((cy + y) * SW + cx + vi * HALF + x) * 3;
      const e = Math.min(1, v.emi[k] * 1.6);
      const g = 60 + b * 0.75;
      sheet[o] = Math.min(255, g * (1 - e) + 255 * e);
      sheet[o + 1] = Math.min(255, g * (1 - e) + 140 * e);
      sheet[o + 2] = Math.min(255, (g + 20) * (1 - e) + 30 * e);
    }
  });
});
let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SW}" height="${SH}">`;
cells.forEach((cell, i) => {
  const cx = (i % COLS) * CELL_W, cy = Math.floor(i / COLS) * CELL_H;
  svg += `<rect x="${cx}" y="${cy + HALF}" width="${CELL_W}" height="${LABEL}" fill="#0b0e13"/>`;
  svg += `<text x="${cx + 5}" y="${cy + HALF + 13}" fill="#d6e2ee" font-family="monospace" font-size="12">${cell.name}</text>`;
  svg += `<text x="${cx + 5}" y="${cy + HALF + 26}" fill="#8aa0b6" font-family="monospace" font-size="11">rot ${cell.rot.join(' ')}   top | side   BOW UP</text>`;
  svg += `<line x1="${cx + HALF}" y1="${cy}" x2="${cx + HALF}" y2="${cy + HALF}" stroke="#2a3340"/>`;
  svg += `<polygon points="${cx + 8},${cy + 4} ${cx + 3},${cy + 14} ${cx + 13},${cy + 14}" fill="#ffd166"/>`;
  for (const m of cell.marks)
    svg += m.kind === 'nozzle'
      ? `<circle cx="${cx + m.p[0]}" cy="${cy + m.p[1]}" r="3" fill="#36e0ff"/>`
      : `<rect x="${cx + m.p[0] - 2}" y="${cy + m.p[1] - 2}" width="4" height="4" fill="#ff4d5e"/>`;
  svg += `<rect x="${cx + 0.5}" y="${cy + 0.5}" width="${CELL_W - 1}" height="${CELL_H - 1}" fill="none" stroke="#232b36"/>`;
});
svg += '</svg>';
fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp(sheet, { raw: { width: SW, height: SH, channels: 3 } }).composite([{ input: Buffer.from(svg), top: 0, left: 0 }]).png().toFile(OUT);
console.log(`${cells.length} hulls -> ${path.relative(PROJ, OUT)} (${SW}x${SH}). Every nose should point UP.`);
