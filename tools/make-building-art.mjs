// ============================================================================================
// SURFACE BUILDING ART — every structure that stands on a planet's tile grid
//
//   node tools/make-building-art.mjs                 write the PNGs (+ any missing .meta)
//   node tools/make-building-art.mjs --sheets DIR    also render review contact sheets into DIR
//
// Writes Assets/Resources/SpaceAssets/Buildings/Building_<Type>_<kind>.png, which BuildingArt.cs loads
// in place of its old code-drawn tiles. kind is:
//
//   tile0 / tile1   16x16, one map cell — the two variants a free-drawn class alternates between
//   site            32x32 (2x2) or 48x48 (3x3) — a fixed or square class, cut into cells at runtime
//   slice           48x48 nine-slice — the storage depot, which may be any rectangle
//   card            32x32 — the depot's build-menu picture (its slice's four corners)
//
// ---- THE STYLE ------------------------------------------------------------------------------
//
// TRANSPARENT. A building is an object standing ON the biome tile, not a square of its own ground
// pasted over it. Only things that genuinely are ground — a field, a landing pad, a plaza — carry a
// footprint, and even those stop a pixel short of the cell edge so the terrain frames them.
//
// One light, from the top-left. Every sprite is drawn in a 3/4 view (roof on top, the south wall
// below it), outlined in one dark ink, and casts a one-pixel shadow down and to the right — which is
// what lets a white habitat read on snow and a dark refinery read on basalt.
//
// One palette (PAL below). The two ACCENT colours are keys, not decoration: BuildingArt.cs swaps them
// for the owning empire's colour at load, so a world's buildings wear its owner's livery. Nothing else
// in the palette may use those exact RGB values — the generator checks.
//
// 16 texels per cell, the same as the biome tiles (TerrainTextureMap.ArtSize), drawn with Point
// filtering. A cell is ~8 px across at "Fit" and ~70 px fully zoomed in, so the silhouettes are kept
// bold enough for the first and the detail is for the second.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const OUT = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'Buildings');
const BIOMES = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'Biomes');
const sheetArg = process.argv.indexOf('--sheets');
const SHEETS = sheetArg > 0 ? process.argv[sheetArg + 1] : null;

// ---- palette ------------------------------------------------------------------------------------
const PAL = {
  ink: [22, 26, 36],
  // hull whites, light to dark
  w0: [246, 248, 250], w1: [218, 224, 232], w2: [172, 182, 198], w3: [118, 128, 148],
  // steel
  s0: [176, 192, 212], s1: [126, 144, 170], s2: [86, 100, 126], s3: [58, 68, 90],
  // glass
  g0: [214, 248, 255], g1: [128, 212, 244], g2: [64, 148, 206], g3: [38, 90, 150],
  // light
  lit: [255, 226, 124], lit2: [255, 170, 64],
  // concrete pad
  p0: [196, 194, 184], p1: [164, 162, 154], p2: [128, 126, 120], p3: [96, 94, 92],
  // hazard / signal
  y0: [250, 208, 64], y1: [196, 146, 36], r0: [232, 76, 60], r1: [156, 40, 40],
  // crops and soil
  c0: [150, 216, 92], c1: [96, 170, 64], c2: [56, 118, 48],
  h0: [244, 214, 112], h1: [206, 164, 70], h2: [150, 112, 46],
  d0: [128, 94, 64], d1: [98, 72, 50], d2: [72, 52, 38],
  // brick / rust
  b0: [176, 104, 76], b1: [134, 76, 56], b2: [96, 54, 42],
  // ore
  o0: [214, 150, 84], o1: [160, 104, 60], o2: [110, 74, 50],
  // solar cells
  v0: [126, 176, 246], v1: [58, 92, 178], v2: [36, 56, 120],
  // heat
  e0: [255, 246, 170], e1: [255, 168, 60], e2: [222, 86, 36],
  // plasma
  q0: [236, 255, 255], q1: [140, 232, 255], q2: [70, 170, 240],
  // water / foam
  f0: [236, 246, 255], f1: [170, 210, 240],
  // smoke / steam
  m0: [240, 242, 246], m1: [196, 200, 210],
  // greenery
  t0: [92, 176, 76], t1: [52, 122, 52],
};
// The owner's livery. KEYS — BuildingArt.Recolour matches these exact values.
const ACCENT = { a0: [64, 196, 170], a1: [36, 128, 112] };
Object.assign(PAL, ACCENT);
for (const [k, v] of Object.entries(PAL))
  if (!(k in ACCENT) && Object.values(ACCENT).some(a => a.every((n, i) => n === v[i])))
    throw new Error(`palette entry ${k} collides with an accent key`);

// ---- canvas --------------------------------------------------------------------------------------
class Cv {
  constructor(w, h) { this.w = w; this.h = h; this.d = new Uint8ClampedArray(w * h * 4); }
  in(x, y) { return x >= 0 && y >= 0 && x < this.w && y < this.h; }
  set(x, y, c, a = 255) {
    x = Math.round(x); y = Math.round(y);
    if (!c || !this.in(x, y)) return;
    if (typeof c === 'string') c = PAL[c];
    const i = (y * this.w + x) * 4, d = this.d;
    if (a >= 255 || d[i + 3] === 0) { d[i] = c[0]; d[i + 1] = c[1]; d[i + 2] = c[2]; d[i + 3] = Math.max(a, d[i + 3]); return; }
    const t = a / 255;
    d[i] = d[i] * (1 - t) + c[0] * t; d[i + 1] = d[i + 1] * (1 - t) + c[1] * t; d[i + 2] = d[i + 2] * (1 - t) + c[2] * t;
    d[i + 3] = Math.max(d[i + 3], a);
  }
  a(x, y) { return this.in(x, y) ? this.d[(y * this.w + x) * 4 + 3] : 0; }
  clear(x, y) { if (this.in(x, y)) this.d.fill(0, (y * this.w + x) * 4, (y * this.w + x) * 4 + 4); }
  rect(x, y, w, h, c) { for (let j = y; j < y + h; j++) for (let i = x; i < x + w; i++) this.set(i, j, c); }
  hl(x0, x1, y, c) { for (let x = Math.min(x0, x1); x <= Math.max(x0, x1); x++) this.set(x, y, c); }
  vl(x, y0, y1, c) { for (let y = Math.min(y0, y1); y <= Math.max(y0, y1); y++) this.set(x, y, c); }
  line(x0, y0, x1, y1, c) {
    let dx = Math.abs(x1 - x0), dy = -Math.abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
    for (let g = 0; g < 512; g++) {
      this.set(x0, y0, c);
      if (x0 === x1 && y0 === y1) break;
      const e2 = 2 * err;
      if (e2 >= dy) { err += dy; x0 += sx; }
      if (e2 <= dx) { err += dx; y0 += sy; }
    }
  }
  // Ellipse centred on a (possibly half-integer) point; fn(x, y, nx, ny) picks the colour from the
  // normalised offset, so shapes shade themselves.
  ell(cx, cy, rx, ry, fn) {
    for (let y = Math.floor(cy - ry); y <= Math.ceil(cy + ry); y++)
      for (let x = Math.floor(cx - rx); x <= Math.ceil(cx + rx); x++) {
        const nx = (x + 0.5 - cx) / rx, ny = (y + 0.5 - cy) / ry;
        if (nx * nx + ny * ny <= 1.0) { const c = typeof fn === 'function' ? fn(x, y, nx, ny) : fn; if (c) this.set(x, y, c); }
      }
  }
  blit(src, ox, oy) {
    for (let y = 0; y < src.h; y++) for (let x = 0; x < src.w; x++) {
      const i = (y * src.w + x) * 4;
      if (src.d[i + 3]) this.set(ox + x, oy + y, [src.d[i], src.d[i + 1], src.d[i + 2]], src.d[i + 3]);
    }
  }
}

// ---- shading helpers -------------------------------------------------------------------------------
// Light from the top-left: a normal pointing up-left is lit.
const lightOf = (nx, ny) => -(nx * 0.62 + ny * 0.78);
const ramp = (r, t) => r[Math.max(0, Math.min(r.length - 1, Math.floor(t * r.length)))];

/// A shaded sphere/dome. `r` is a ramp light -> dark.
function sphere(c, cx, cy, rx, ry, r, spec = true) {
  c.ell(cx, cy, rx, ry, (x, y, nx, ny) => {
    const l = lightOf(nx, ny);
    if (spec && nx < -0.15 && nx > -0.6 && ny < -0.2 && ny > -0.7) return r[0];
    return ramp(r, 1 - (l + 1) / 2 * 1.0);
  });
}

/// A 3/4-view box: a roof `roofH` deep with a south wall `wallH` tall below it.
function box(c, x, y, w, roofH, wallH, roof, wall) {
  c.rect(x, y, w, roofH, roof[1]);
  c.hl(x, x + w - 1, y, roof[0]); c.vl(x, y, y + roofH - 1, roof[0]);
  if (roof[2]) c.vl(x + w - 1, y + 1, y + roofH - 1, roof[2]);
  c.rect(x, y + roofH, w, wallH, wall[0]);
  if (wall[1]) c.vl(x + w - 1, y + roofH, y + roofH + wallH - 1, wall[1]);
  if (wall[1] && wallH > 1) c.hl(x, x + w - 1, y + roofH + wallH - 1, wall[1]);
}

/// A vertical cylinder seen from 3/4: an elliptical top and a shaded body.
function cyl(c, x, y, w, h, r, capH = 2) {
  const rr = Array.isArray(r) ? r : [PAL.w0, PAL.w1, PAL.w2, PAL.w3];
  for (let i = 0; i < w; i++) {
    const t = i / Math.max(1, w - 1);
    const col = t < 0.25 ? rr[1] : t < 0.7 ? rr[1] : t < 0.9 ? rr[2] : rr[3];
    c.vl(x + i, y + capH / 2, y + h - 1, i === 1 && w > 3 ? rr[0] : col);
  }
  c.ell(x + w / 2, y + capH / 2 + 0.01, w / 2, capH / 2 + 0.01, rr[0]);
}

function windows(c, x, y, cols, rows, sx, sy, col = 'lit', off = null) {
  for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
    const dark = off && off(i, j);
    c.set(x + i * sx, y + j * sy, dark ? 'g3' : col);
  }
}

function puff(c, x, y, r = 1.6) {
  c.ell(x, y, r, r * 0.9, (px, py, nx, ny) => (lightOf(nx, ny) > -0.2 ? PAL.m0 : PAL.m1));
}

// ---- finishing: ink outline + drop shadow -----------------------------------------------------------
//
// `fx` is a second canvas composited on top AFTER the outline — smoke, steam, sparks, the beacon glow:
// things that are not solid and look wrong with a hard ink line round them.
function finish(body, fx, { outline = true, shadow = true } = {}) {
  const w = body.w, h = body.h;
  const out = new Cv(w, h);
  out.d.set(body.d);
  if (outline) {
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      if (body.a(x, y)) continue;
      if (body.a(x - 1, y) > 128 || body.a(x + 1, y) > 128 || body.a(x, y - 1) > 128 || body.a(x, y + 1) > 128)
        out.set(x, y, PAL.ink);
    }
  }
  const res = new Cv(w, h);
  if (shadow) {
    // Soft: a strong one-texel shadow down-right, and a faint second one beyond it.
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      if (out.a(x, y)) continue;
      if (out.a(x - 1, y - 1) > 128 || out.a(x, y - 1) > 128 && out.a(x - 1, y) > 128) res.set(x, y, [8, 10, 16], 110);
      else if (out.a(x - 2, y - 2) > 128 || out.a(x - 1, y - 2) > 128 && out.a(x - 2, y - 1) > 128) res.set(x, y, [8, 10, 16], 46);
    }
  }
  res.blit(out, 0, 0);
  if (fx) res.blit(fx, 0, 0);
  return res;
}

// ---- the buildings -----------------------------------------------------------------------------------
// Each entry draws into (b, fx) where b is the solid body and fx the unoutlined overlay.
const W = [PAL.w0, PAL.w1, PAL.w2, PAL.w3];
const S = [PAL.s0, PAL.s1, PAL.s2, PAL.s3];
const GL = [PAL.g0, PAL.g1, PAL.g2, PAL.g3];

const TILE = {
  // ----- INDUSTRY -----
  Mine: [
    (b, fx) => {   // headframe over a shaft, with an ore heap
      // ore heap
      b.ell(4.5, 11.5, 3.6, 2.6, (x, y, nx, ny) => ramp([PAL.o0, PAL.o1, PAL.o2], 1 - (lightOf(nx, ny) + 1) / 2));
      b.set(3, 10, 'o0'); b.set(5, 11, 'y0');
      // shaft collar
      box(b, 7, 10, 6, 2, 2, [PAL.p0, PAL.p1, PAL.p2], [PAL.p2, PAL.p3]);
      // headframe A legs
      b.line(8, 10, 10, 2, 's1'); b.line(12, 10, 10, 2, 's2'); b.line(9, 6, 11, 6, 's1');
      b.vl(10, 2, 10, 's3');
      // sheave wheel + accent cab
      b.ell(10.5, 2.5, 1.6, 1.6, (x, y, nx, ny) => (nx * nx + ny * ny < 0.3 ? PAL.s3 : PAL.s0));
      b.rect(12, 7, 2, 3, 'a0'); b.set(13, 9, 'a1'); b.set(12, 8, 'lit');
    },
    (b, fx) => {   // drill rig and an ore cart
      box(b, 2, 9, 8, 2, 3, [PAL.a0, PAL.a0, PAL.a1], [PAL.s1, PAL.s2]);   // rig deck
      b.vl(5, 1, 9, 's0'); b.vl(6, 1, 9, 's2'); b.hl(4, 7, 1, 's1'); b.hl(4, 7, 5, 's1');  // derrick
      b.set(5, 3, 's3'); b.set(6, 3, 's3');
      b.vl(5, 12, 13, 's3'); b.set(5, 14, 'o0');   // drill bit into the ground
      // ore cart
      box(b, 10, 10, 4, 2, 2, [PAL.o0, PAL.o1], [PAL.s2, PAL.s3]);
      b.set(10, 14, 'ink'); b.set(13, 14, 'ink');
      b.set(8, 10, 'lit');
      fx.set(7, 0, 'r0');
    },
  ],
  Factory: [
    (b, fx) => {   // sawtooth hall + chimney
      box(b, 1, 6, 12, 3, 4, [PAL.w1, PAL.w1, PAL.w2], [PAL.s1, PAL.s2]);
      for (let x = 1; x < 13; x += 3) { b.set(x, 5, 'w0'); b.set(x + 1, 5, 'w0'); b.set(x, 4, 'w0'); b.set(x + 2, 6, 'g1'); b.set(x + 2, 7, 'g2'); }
      b.hl(1, 12, 9, 'a0');
      b.rect(3, 11, 3, 2, 's3'); b.hl(3, 5, 11, 'y0');
      windows(b, 8, 11, 3, 1, 1, 1, 'lit');
      cyl(b, 11, 1, 2, 5, [PAL.b0, PAL.b0, PAL.b1, PAL.b2], 1);
      b.set(11, 2, 'w1'); b.set(12, 2, 'w2');
      puff(fx, 13.2, 0.8, 1.3);
    },
    (b, fx) => {   // assembly block with a loading bay and crates
      box(b, 2, 3, 10, 4, 5, [PAL.w0, PAL.w1, PAL.w2], [PAL.w2, PAL.w3]);
      b.rect(4, 4, 6, 2, 's1'); b.hl(4, 9, 4, 's0');      // roof vent
      b.hl(2, 11, 7, 'a0');
      b.rect(5, 9, 4, 3, 's3'); for (let x = 5; x < 9; x++) if (x % 2) b.vl(x, 9, 11, 's2');
      b.set(3, 9, 'lit'); b.set(10, 9, 'lit');
      box(b, 12, 9, 3, 1, 2, [PAL.o0, PAL.o0], [PAL.o1, PAL.o2]);
      box(b, 12, 6, 2, 1, 2, [PAL.a0, PAL.a0], [PAL.a1, PAL.a1]);
    },
  ],
  Refinery: [
    (b, fx) => {   // two spherical tanks and a pipe rack
      b.hl(2, 13, 12, 's2'); b.hl(2, 13, 13, 's3');
      b.vl(4, 10, 12, 's2'); b.vl(11, 10, 12, 's2');
      sphere(b, 4.5, 6.5, 3.6, 3.4, W);
      sphere(b, 11, 8, 3.1, 2.9, W);
      b.hl(2, 7, 7, 'a0'); b.hl(8, 13, 8, 'a0');
      b.vl(8, 2, 12, 's1'); b.set(8, 1, 'e1');
      fx.set(8, 0, 'e0'); fx.set(7, 0, 'e1', 180);
    },
    (b, fx) => {   // distillation column + tank + flare
      cyl(b, 3, 1, 4, 12, W, 2);
      for (let y = 4; y < 12; y += 3) b.hl(3, 6, y, 's2');
      b.set(4, 5, 'lit'); b.set(4, 8, 'lit');
      cyl(b, 9, 7, 5, 6, W, 2);
      b.hl(9, 13, 10, 'a0');
      b.hl(7, 8, 9, 's2'); b.hl(7, 8, 11, 's2');
      b.vl(13, 2, 6, 's1');
      fx.set(13, 1, 'e1'); fx.set(13, 0, 'e0'); fx.set(14, 1, 'e2', 160);
    },
  ],

  // ----- AGRICULTURE -----
  Farm: [
    (b) => {   // green crop rows on tilled soil — ground, so it fills most of the cell
      b.rect(1, 1, 14, 14, 'd1');
      for (let y = 2; y < 15; y += 3) { b.hl(1, 14, y, 'c1'); b.hl(1, 14, y + 1, 'c2'); for (let x = 1 + (y % 2); x < 15; x += 3) b.set(x, y, 'c0'); }
      b.hl(1, 14, 1, 'd0');
    },
    (b) => {   // golden grain
      b.rect(1, 1, 14, 14, 'd1');
      for (let x = 2; x < 15; x += 3) { b.vl(x, 1, 14, 'h1'); b.vl(x + 1, 1, 14, 'h2'); for (let y = 1 + (x % 2); y < 15; y += 3) b.set(x, y, 'h0'); }
      b.vl(1, 1, 14, 'd0');
    },
  ],

  // ----- CIVIL -----
  Habitat: [
    (b) => {   // a long hab module with a rounded roof
      b.rect(2, 4, 11, 3, 'w1'); b.hl(3, 11, 3, 'w0'); b.hl(2, 12, 4, 'w0');
      b.set(2, 3, null); b.vl(12, 4, 6, 'w2');
      b.rect(2, 7, 11, 5, 'w2'); b.vl(12, 7, 11, 'w3'); b.hl(2, 12, 11, 'w3');
      b.hl(2, 12, 7, 'a0'); b.hl(2, 12, 8, 'a1');
      windows(b, 3, 9, 5, 1, 2, 1, 'lit', (i) => i === 3);
      b.rect(13, 8, 2, 4, 's1'); b.set(13, 8, 's0');   // airlock
    },
    (b) => {   // twin pods with a connecting tube
      b.rect(5, 9, 6, 2, 's1'); b.hl(5, 10, 9, 's0');
      sphere(b, 4.5, 8.5, 3.5, 3.5, W);
      sphere(b, 11.5, 7.5, 3.0, 3.0, W);
      b.hl(2, 7, 9, 'a0'); b.hl(9, 14, 8, 'a0');
      b.set(4, 10, 'lit'); b.set(5, 10, 'lit'); b.set(11, 9, 'lit');
    },
  ],
  Settlement: [
    (b) => {   // two prefab huts
      box(b, 1, 6, 6, 3, 4, [PAL.b0, PAL.b0, PAL.b1], [PAL.w1, PAL.w2]);
      b.set(3, 10, 'lit'); b.set(5, 11, 'd2'); b.set(5, 12, 'd2');
      box(b, 8, 3, 6, 3, 4, [PAL.a0, PAL.a0, PAL.a1], [PAL.w1, PAL.w2]);
      b.set(10, 7, 'lit'); b.set(12, 8, 'd2'); b.set(12, 9, 'd2');
      b.ell(11.5, 12.5, 2, 1.6, (x, y, nx, ny) => (lightOf(nx, ny) > 0 ? PAL.t0 : PAL.t1));
    },
    (b) => {   // a hut and a little garden dome
      box(b, 2, 3, 7, 3, 4, [PAL.b0, PAL.b0, PAL.b1], [PAL.w1, PAL.w2]);
      b.set(4, 7, 'lit'); b.set(7, 8, 'd2'); b.set(7, 9, 'd2');
      sphere(b, 11.5, 10.5, 2.6, 2.4, GL);
      b.set(11, 11, 't0'); b.set(12, 11, 't1');
      b.ell(4, 12.5, 2, 1.5, (x, y, nx, ny) => (lightOf(nx, ny) > 0 ? PAL.t0 : PAL.t1));
    },
  ],
  Town: [
    (b) => {   // a three-storey block with a roof garden
      box(b, 2, 2, 11, 4, 8, [PAL.w1, PAL.w1, PAL.w2], [PAL.w2, PAL.w3]);
      b.rect(4, 3, 4, 2, 't0'); b.set(5, 3, 'c0'); b.hl(4, 7, 4, 't1');
      b.rect(9, 3, 3, 2, 's1');
      b.hl(2, 12, 6, 'a0');
      windows(b, 3, 7, 5, 3, 2, 1, 'lit', (i, j) => (i + j * 2) % 4 === 1);
      b.rect(6, 9, 2, 3, 's3');
    },
    (b) => {   // a cluster of three houses
      box(b, 1, 3, 6, 3, 3, [PAL.b0, PAL.b0, PAL.b1], [PAL.w1, PAL.w2]);
      box(b, 8, 1, 6, 3, 4, [PAL.a0, PAL.a0, PAL.a1], [PAL.w1, PAL.w2]);
      box(b, 4, 8, 7, 3, 4, [PAL.s0, PAL.s0, PAL.s1], [PAL.w1, PAL.w2]);
      b.set(3, 7, 'lit'); b.set(10, 5, 'lit'); b.set(12, 6, 'lit'); b.set(6, 12, 'lit'); b.set(8, 12, 'lit');
    },
  ],
  City: [
    (b) => {   // a tower and a low block
      box(b, 7, 1, 6, 2, 12, [PAL.g1, PAL.g1, PAL.g2], [PAL.g2, PAL.g3]);
      b.set(7, 1, 'g0'); b.vl(9, 0, 0, 's1'); b.set(9, 0, 'r0');
      for (let y = 4; y < 14; y += 2) { b.hl(7, 11, y, 'g3'); b.set(8 + (y % 3), y, 'lit'); }
      b.vl(12, 3, 14, 'g3');
      box(b, 1, 7, 6, 2, 7, [PAL.w1, PAL.w1, PAL.w2], [PAL.w2, PAL.w3]);
      b.hl(1, 6, 9, 'a0');
      windows(b, 2, 11, 3, 2, 2, 2, 'lit', (i, j) => i === 1 && j === 0);
    },
    (b) => {   // twin towers with a sky bridge
      box(b, 1, 3, 5, 2, 11, [PAL.w0, PAL.w1, PAL.w2], [PAL.w2, PAL.w3]);
      box(b, 9, 1, 5, 2, 13, [PAL.g1, PAL.g1, PAL.g2], [PAL.g2, PAL.g3]);
      for (let y = 6; y < 14; y += 2) { b.set(2, y, 'lit'); b.set(4, y + 1, 'lit'); b.hl(9, 12, y, 'g3'); b.set(10 + (y % 2), y - 1, 'lit'); }
      b.hl(6, 8, 7, 's1'); b.hl(6, 8, 8, 'a0');
      b.hl(1, 5, 5, 'a0');
      b.set(11, 0, 'r0');
    },
  ],

  // ----- SCIENCE -----
  ResearchOutpost: [
    (b) => {   // a dome and a radio dish
      sphere(b, 6, 9, 4.6, 4.2, W);
      b.hl(2, 10, 11, 'a0'); b.hl(3, 9, 12, 'a1');
      b.set(5, 10, 'lit'); b.set(7, 10, 'lit');
      b.vl(12, 6, 12, 's2'); b.hl(11, 13, 12, 's2');
      b.ell(12.5, 4.5, 2.6, 2.2, (x, y, nx, ny) => (nx + ny < -0.2 ? PAL.w0 : nx + ny < 0.5 ? PAL.w1 : PAL.w2));
      b.set(13, 3, 's3'); b.set(12, 4, 'r0');
    },
    (b) => {   // two linked domes and an antenna
      b.rect(5, 9, 5, 2, 's1');
      sphere(b, 4, 9, 3.2, 3.0, GL);
      sphere(b, 10.5, 9.5, 3.8, 3.4, W);
      b.hl(8, 13, 11, 'a0');
      b.vl(10, 1, 6, 's1'); b.set(10, 0, 'r0'); b.hl(9, 11, 3, 's1');
      b.set(4, 10, 't0');
    },
  ],
  ResearchCenter: [
    (b) => {   // a glass-roofed laboratory with a big dish
      box(b, 1, 6, 10, 4, 4, [PAL.g0, PAL.g1, PAL.g2], [PAL.w1, PAL.w2]);
      for (let x = 3; x < 11; x += 3) b.vl(x, 6, 9, 's1');
      b.hl(1, 10, 10, 'a0');
      windows(b, 2, 12, 4, 1, 2, 1, 'lit');
      b.vl(13, 6, 13, 's2'); b.hl(12, 14, 13, 's2');
      b.ell(12.5, 3.5, 3.2, 2.8, (x, y, nx, ny) => (nx + ny < -0.3 ? PAL.w0 : nx + ny < 0.4 ? PAL.w1 : PAL.w2));
      b.set(12, 3, 's3'); b.set(13, 2, 'a0');
    },
    (b) => {   // an observatory dome with an open slit
      box(b, 2, 9, 12, 2, 4, [PAL.w1, PAL.w1, PAL.w2], [PAL.w2, PAL.w3]);
      sphere(b, 8, 8, 5, 5, W);
      b.vl(8, 3, 9, 'g3'); b.vl(9, 3, 9, 'g3'); b.set(8, 5, 'q1');
      b.hl(2, 13, 11, 'a0');
      windows(b, 3, 12, 6, 1, 2, 1, 'lit', (i) => i === 2);
    },
  ],

  // ----- ELECTRICAL -----
  SolarArray: [
    (b) => {   // four tilted panels on legs, ground showing between them
      for (const [x, y] of [[1, 2], [9, 2], [1, 9], [9, 9]]) {
        b.rect(x, y, 5, 3, 'v1');
        b.hl(x, x + 4, y, 'v0'); b.vl(x + 2, y, y + 2, 'v2');
        b.set(x + 1, y + 1, 'v0');
        b.hl(x, x + 4, y + 3, 's1');
        b.set(x + 1, y + 4, 's3'); b.set(x + 3, y + 4, 's3');
      }
      b.rect(7, 7, 1, 2, 'a0');   // inverter
    },
    (b) => {   // two long rows
      for (const y of [2, 9]) {
        b.rect(2, y, 11, 3, 'v1');
        b.hl(2, 12, y, 'v0');
        for (let x = 5; x < 13; x += 3) b.vl(x, y, y + 2, 'v2');
        b.set(3, y + 1, 'v0'); b.set(9, y + 1, 'v0');
        b.hl(2, 12, y + 3, 's1');
        b.set(3, y + 4, 's3'); b.set(11, y + 4, 's3');
      }
      b.rect(13, 6, 1, 2, 'a0');
    },
  ],
  WindFarm: [
    (b, fx) => {   // a turbine, blades at rest
      b.ell(8, 13.5, 2.2, 1.1, 'p1');
      b.vl(8, 5, 13, 'w1'); b.vl(9, 6, 13, 'w2');
      b.rect(7, 4, 3, 2, 'w1'); b.set(9, 4, 'a0'); b.set(9, 5, 'a1');
      b.line(8, 4, 8, 0, 'w0'); b.line(8, 4, 3, 7, 'w0'); b.line(8, 4, 13, 7, 'w0');
      b.set(8, 4, 's1');
      b.set(8, 0, 'r0');
    },
    (b) => {
      b.ell(8, 13.5, 2.2, 1.1, 'p1');
      b.vl(8, 5, 13, 'w1'); b.vl(9, 6, 13, 'w2');
      b.rect(7, 4, 3, 2, 'w1'); b.set(9, 4, 'a0'); b.set(9, 5, 'a1');
      b.line(8, 4, 13, 1, 'w0'); b.line(8, 4, 2, 2, 'w0'); b.line(8, 4, 8, 9, 'w0');
      b.set(8, 4, 's1');
    },
  ],
  PowerDistribution: [
    (b) => {   // substation: two transformers on a pad
      b.rect(1, 9, 14, 5, 'p1'); b.hl(1, 14, 9, 'p0'); b.hl(1, 14, 13, 'p2');
      box(b, 2, 5, 5, 3, 4, [PAL.s0, PAL.s1, PAL.s2], [PAL.s1, PAL.s3]);
      box(b, 9, 5, 5, 3, 4, [PAL.s0, PAL.s1, PAL.s2], [PAL.s1, PAL.s3]);
      for (const x of [3, 5, 10, 12]) { b.vl(x, 2, 4, 'w1'); b.set(x, 2, 'y0'); }
      b.hl(3, 12, 2, 'y1');
      b.set(4, 10, 'y0'); b.set(11, 10, 'y0');
      b.hl(2, 6, 8, 'a0'); b.hl(9, 13, 8, 'a0');
    },
    (b) => {   // a big transformer with a bolt
      b.rect(2, 10, 12, 4, 'p1'); b.hl(2, 13, 10, 'p0'); b.hl(2, 13, 13, 'p2');
      box(b, 3, 4, 10, 3, 6, [PAL.s0, PAL.s1, PAL.s2], [PAL.s1, PAL.s3]);
      for (let x = 4; x < 12; x += 2) b.vl(x, 8, 11, 's2');
      b.line(9, 7, 7, 9, 'y0'); b.hl(7, 9, 9, 'y0'); b.line(9, 9, 7, 11, 'y0');
      for (const x of [4, 8, 11]) { b.vl(x, 1, 3, 'w1'); b.set(x, 1, 'y0'); }
      b.hl(3, 12, 7, 'a0');
    },
  ],
  Capacitor: [
    (b) => {   // two battery cells
      for (const x of [2, 9]) {
        cyl(b, x, 2, 5, 11, S, 2);
        b.hl(x, x + 4, 5, 'a0'); b.hl(x, x + 4, 6, 'a1');
        b.set(x + 1, 9, 'q1'); b.set(x + 1, 10, 'q1'); b.set(x + 1, 11, 'q2');
        b.set(x + 2, 1, 'y0');
      }
    },
    (b) => {   // three slimmer cells
      for (const x of [1, 6, 11]) {
        cyl(b, x, 3, 4, 10, S, 2);
        b.hl(x, x + 3, 6, 'a0');
        b.set(x + 1, 9, 'q1'); b.set(x + 1, 10, 'q1'); b.set(x + 1, 11, 'q2');
        b.set(x + 1, 2, 'y0');
      }
    },
  ],
  CombustionPlant: [
    (b, fx) => {   // brick boiler house and a tall stack
      box(b, 1, 7, 9, 3, 5, [PAL.b0, PAL.b0, PAL.b1], [PAL.b1, PAL.b2]);
      for (let x = 2; x < 9; x += 2) b.set(x, 11, 'lit');
      b.hl(1, 9, 10, 'a0');
      cyl(b, 11, 2, 3, 12, [PAL.w1, PAL.w1, PAL.w2, PAL.w3], 2);
      b.hl(11, 13, 4, 'r0'); b.hl(11, 13, 7, 'r0');
      puff(fx, 12.5, 1.2, 1.5); puff(fx, 14.2, 0.4, 1.1);
    },
    (b, fx) => {
      box(b, 5, 6, 10, 3, 6, [PAL.b0, PAL.b0, PAL.b1], [PAL.b1, PAL.b2]);
      for (let x = 6; x < 14; x += 2) b.set(x, 10, 'lit');
      b.hl(5, 14, 9, 'a0');
      cyl(b, 1, 1, 3, 13, [PAL.w1, PAL.w1, PAL.w2, PAL.w3], 2);
      b.hl(1, 3, 3, 'r0'); b.hl(1, 3, 6, 'r0');
      puff(fx, 3.4, 0.6, 1.5);
    },
  ],
  SteamTurbine: [
    (b, fx) => {   // turbine hall with steam vents
      box(b, 1, 6, 14, 3, 5, [PAL.w1, PAL.w1, PAL.w2], [PAL.w2, PAL.w3]);
      b.ell(5, 7.5, 2.4, 1.2, 's1'); b.ell(11, 7.5, 2.4, 1.2, 's1');
      b.hl(1, 14, 9, 'a0');
      windows(b, 3, 11, 6, 1, 2, 1, 'lit', (i) => i === 4);
      puff(fx, 5, 4.5, 1.7); puff(fx, 6.5, 2.4, 1.4); puff(fx, 11, 4.6, 1.5);
    },
    (b, fx) => {   // a horizontal turbine casing
      b.rect(1, 11, 14, 3, 'p1'); b.hl(1, 14, 11, 'p0');
      b.ell(7, 8, 6, 3, (x, y, nx, ny) => ramp(S, 1 - (lightOf(nx, ny * 1.6) + 1) / 2));
      b.vl(4, 5, 11, 's2'); b.vl(10, 5, 11, 's2');
      b.hl(2, 12, 9, 'a0');
      box(b, 12, 6, 3, 2, 5, [PAL.w1, PAL.w1], [PAL.w2, PAL.w3]);
      puff(fx, 13.5, 3.5, 1.6);
    },
  ],
  FissionReactor: [
    (b, fx) => {   // a cooling tower venting steam
      for (let y = 3; y < 14; y++) {
        const t = (y - 3) / 10, half = 4.4 - 1.8 * Math.sin(t * Math.PI * 0.85);
        const x0 = Math.round(8 - half), x1 = Math.round(8 + half);
        for (let x = x0; x <= x1; x++) {
          const u = (x - x0) / Math.max(1, x1 - x0);
          b.set(x, y, u < 0.18 ? 'w0' : u < 0.62 ? 'w1' : u < 0.85 ? 'w2' : 'w3');
        }
      }
      b.ell(8, 3, 4.4, 1.3, (x, y, nx, ny) => (ny < 0 ? PAL.w2 : PAL.w3));
      b.hl(5, 11, 11, 'a0'); b.hl(5, 11, 12, 'a1');
      puff(fx, 8, 1.4, 2.4); puff(fx, 10.6, 0.4, 1.6);
    },
    (b, fx) => {   // containment dome and a vent stack
      box(b, 1, 10, 13, 1, 3, [PAL.p0, PAL.p1], [PAL.p2, PAL.p3]);
      sphere(b, 6, 9, 5, 5, W);
      b.hl(1, 11, 10, 'a0');
      b.ell(6, 6, 1.4, 1.2, 'y0'); b.set(6, 6, 'ink');
      cyl(b, 12, 2, 3, 9, [PAL.w1, PAL.w1, PAL.w2, PAL.w3], 2);
      b.hl(12, 14, 4, 'r0');
      puff(fx, 13.4, 1.0, 1.3);
    },
  ],
  HydroPlant: [
    (b, fx) => {   // turbine house on piers, foam at the outflow — the water itself is the tile under it
      box(b, 2, 2, 12, 3, 5, [PAL.p0, PAL.p1, PAL.p2], [PAL.p1, PAL.p3]);
      b.hl(2, 13, 6, 'a0');
      windows(b, 4, 8, 4, 1, 2, 1, 'lit');
      for (const x of [3, 7, 11]) b.rect(x, 10, 2, 3, 'p2');
      fx.rect(5, 11, 2, 2, 'f1'); fx.rect(9, 11, 2, 2, 'f1');
      fx.hl(2, 13, 13, 'f1', 200); fx.set(5, 13, 'f0'); fx.set(10, 13, 'f0'); fx.set(7, 14, 'f0', 180); fx.set(12, 14, 'f1', 160);
    },
    (b, fx) => {   // a curved dam section with a spillway
      for (let x = 1; x < 15; x++) {
        const y = 4 + Math.round(1.5 * Math.cos((x - 8) / 7 * 1.2));
        b.vl(x, y, y + 1, 'p0'); b.vl(x, y + 2, y + 5, x < 6 ? 'p1' : 'p2');
      }
      b.rect(6, 1, 4, 3, 's1'); b.hl(6, 9, 1, 's0'); b.hl(6, 9, 3, 'a0');
      fx.rect(7, 10, 2, 4, 'f0'); fx.hl(5, 10, 13, 'f1', 200); fx.set(4, 12, 'f1', 150); fx.set(11, 12, 'f1', 150);
    },
  ],
  PowerNode: [
    (b, fx) => {   // a lattice pylon
      b.line(5, 14, 8, 1, 's1'); b.line(11, 14, 8, 1, 's2');
      b.line(6, 10, 10, 10, 's2'); b.line(6, 7, 9, 13, 's3'); b.line(10, 7, 7, 13, 's3');
      b.hl(3, 13, 3, 's0'); b.hl(4, 12, 6, 's1');
      for (const x of [3, 13]) { b.set(x, 4, 'w1'); }
      b.set(4, 7, 'w1'); b.set(12, 7, 'w1');
      b.set(8, 1, 'a0');
      fx.set(3, 5, 'y0'); fx.set(13, 5, 'y0');
    },
    (b, fx) => {   // a relay mast with a glowing coil
      b.ell(8, 13, 4, 1.6, 'p1'); b.hl(5, 11, 12, 'p0');
      b.vl(7, 3, 12, 's1'); b.vl(8, 3, 12, 's2');
      sphere(b, 7.5, 3, 2.6, 2.4, S);
      b.hl(6, 9, 8, 'a0'); b.hl(6, 9, 9, 'a1');
      fx.set(7, 2, 'q0'); fx.set(5, 1, 'q1', 200); fx.set(10, 2, 'q1', 200);
    },
  ],
};

// ---- the big sites ------------------------------------------------------------------------------------
const SITE = {
  // THE CAPITAL. A colony's seat: a raised octagonal plaza; a great glass dome ringed in the owner's
  // colour; a comm spire through its crown with a beacon; two wings either side; a landing pad.
  PlanetCapitol: (b, fx) => {
    // The plaza: ground, so it may carry a footprint — an octagon of paving with a kerb, a ring of
    // lamps, and a processional path down to the doors.
    const oct = (dx, dy, r) => Math.abs(dx) <= r && Math.abs(dy) <= r && Math.abs(dx) + Math.abs(dy) <= r * 1.42;
    for (let y = 0; y < 32; y++) for (let x = 0; x < 32; x++) {
      const dx = x - 15.5, dy = y - 16;
      if (!oct(dx, dy, 14.5)) continue;
      b.set(x, y, !oct(dx, dy, 13.5) ? 'p2' : (x * 3 + y * 5) % 7 === 0 ? 'p0' : 'p1');
    }
    b.rect(14, 26, 4, 5, 'p0'); b.vl(14, 26, 30, 'w1'); b.vl(17, 26, 30, 'p2');
    for (const [x, y] of [[4, 9], [27, 9], [4, 23], [27, 23], [9, 4], [22, 4], [10, 28], [21, 28]]) b.set(x, y, 'lit');

    // Landing pad, lower right, and a garden bed lower left
    b.ell(25, 25.5, 3.6, 2.8, 'p3'); b.ell(25, 25.5, 2.8, 2.1, 's2');
    b.vl(24, 24, 27, 'y0'); b.vl(26, 24, 27, 'y0'); b.set(25, 25, 'y0');
    b.ell(6, 25.5, 3.4, 2.4, (x, y, nx, ny) => (lightOf(nx, ny) > 0.1 ? PAL.t0 : PAL.t1));
    b.set(5, 25, 'c0'); b.set(7, 26, 'h0');

    // The two wings
    for (const x0 of [3, 21]) {
      box(b, x0, 13, 8, 3, 6, [PAL.w0, PAL.w1, PAL.w2], [PAL.w1, PAL.w3]);
      b.hl(x0, x0 + 7, 16, 'a0'); b.hl(x0, x0 + 7, 17, 'a1');
      windows(b, x0 + 1, 19, 4, 1, 2, 1, 'lit');
      b.rect(x0 + 2, 14, 4, 1, 's1');             // rooftop plant
    }
    // banners on their outer ends
    for (const x of [5, 26]) { b.vl(x, 3, 13, 's2'); b.set(x, 2, 'w0'); const fx0 = x < 16 ? x + 1 : x - 3; b.rect(fx0, 3, 3, 2, 'a0'); b.hl(fx0, fx0 + 2, 5, 'a1'); }

    // The rotunda the dome sits on: a colonnaded drum
    box(b, 9, 17, 14, 1, 8, [PAL.w0, PAL.w0], [PAL.w1, PAL.w3]);
    b.vl(8, 17, 25, 'ink'); b.vl(23, 17, 25, 'ink');
    b.hl(9, 22, 17, 'a0'); b.hl(9, 22, 18, 'a1');           // the owner's band
    for (let x = 10; x < 22; x += 2) { b.vl(x, 19, 24, 'w0'); b.vl(x + 1, 19, 24, 'w2'); }
    b.rect(14, 21, 4, 4, 'g3'); b.rect(15, 22, 2, 3, 'lit'); b.hl(14, 17, 20, 'y0');   // great doors
    b.hl(9, 22, 25, 'w3');

    // The dome: a true hemisphere, glazed, with ribs
    const cx = 16, base = 17, rx = 8.6, ry = 11;
    for (let y = Math.floor(base - ry); y < base; y++)
      for (let x = Math.floor(cx - rx); x <= Math.ceil(cx + rx); x++) {
        const nx = (x + 0.5 - cx) / rx, ny = (y + 0.5 - base) / ry;
        if (nx * nx + ny * ny > 1) continue;
        const l = lightOf(nx, ny * 0.7);
        let c = l > 0.45 ? PAL.g0 : l > -0.05 ? PAL.g1 : l > -0.55 ? PAL.g2 : PAL.g3;
        const rib = Math.abs(nx) < 0.06 || Math.abs(Math.abs(nx) - 0.58) < 0.06;
        if (rib) c = l > -0.2 ? PAL.w0 : PAL.w2;
        // an ink rim where the dome meets the sky, so it stands off the wings behind it
        const ox = (x + 0.5 - cx + (x + 0.5 < cx ? -1 : 1)) / rx, oy = (y - 0.5 - base) / ry;
        if (ox * ox + ny * ny > 1 || nx * nx + oy * oy > 1) c = PAL.ink;
        b.set(x, y, c);
      }
    b.set(11, 11, 'w0'); b.set(12, 10, 'w0'); b.set(11, 12, 'g0');
    // the oculus ring at the crown
    b.hl(14, 17, 7, 'a0');

    // Comm spire through the crown, with a crossbar and a beacon
    b.vl(15, 1, 6, 'w0'); b.vl(16, 1, 6, 'w2');
    b.hl(13, 18, 3, 's1'); b.set(13, 3, 'a0'); b.set(18, 3, 'a0');
    fx.set(15, 0, 'q0'); fx.set(16, 0, 'q1', 220); fx.set(14, 0, 'q1', 120); fx.set(17, 0, 'q1', 120);
  },

  ColonyShipBase: (b, fx) => {
    // scorched landing ground
    b.ell(16, 25, 12, 5, (x, y, nx, ny) => (nx * nx + ny * ny > 0.6 ? PAL.d1 : PAL.d2));
    // the ship, standing on three legs
    b.line(9, 26, 12, 20, 's2'); b.line(23, 26, 20, 20, 's3'); b.vl(16, 22, 27, 's2');
    b.rect(8, 26, 3, 1, 's3'); b.rect(22, 26, 3, 1, 's3');
    for (let y = 5; y < 23; y++) {
      const half = y < 11 ? Math.round((y - 4) * 0.95) : 6;
      for (let x = 16 - half; x < 16 + half; x++) {
        const u = (x - (16 - half)) / Math.max(1, 2 * half - 1);
        b.set(x, y, u < 0.2 ? 'w0' : u < 0.6 ? 'w1' : u < 0.85 ? 'w2' : 'w3');
      }
    }
    b.set(15, 4, 'w0'); b.set(16, 4, 'w1');
    b.hl(10, 21, 13, 'a0'); b.hl(10, 21, 14, 'a1');
    b.rect(13, 8, 4, 3, 'g1'); b.hl(13, 16, 8, 'g0'); b.vl(16, 9, 10, 'g2');
    windows(b, 12, 16, 4, 1, 2, 1, 'lit');
    b.rect(14, 19, 4, 4, 's2'); b.hl(14, 17, 19, 's1');   // ramp
    // fins
    b.line(9, 17, 7, 23, 'a0'); b.line(10, 17, 8, 23, 'a1'); b.line(22, 17, 24, 23, 'a1');
    // cargo
    box(b, 2, 23, 4, 2, 2, [PAL.o0, PAL.o0], [PAL.o1, PAL.o2]);
    box(b, 26, 22, 4, 2, 2, [PAL.s0, PAL.s0], [PAL.s1, PAL.s2]);
    box(b, 27, 26, 3, 1, 2, [PAL.a0, PAL.a0], [PAL.a1, PAL.a1]);
    fx.set(16, 3, 'r0');
  },

  Spaceport: (b, fx) => {
    // the pad
    b.ell(13.5, 18, 12.5, 11, (x, y, nx, ny) => { const r = nx * nx + ny * ny; return r > 0.85 ? PAL.p2 : r > 0.62 ? PAL.p1 : PAL.p0; });
    b.ell(13.5, 18, 9.5, 8.2, (x, y, nx, ny) => { const r = nx * nx + ny * ny; return r > 0.8 ? ((x + y) % 2 ? PAL.y0 : PAL.ink) : null; });
    // a shuttle on it
    b.rect(11, 12, 5, 11, 'w1'); b.vl(11, 12, 22, 'w0'); b.vl(15, 12, 22, 'w2');
    b.rect(12, 9, 3, 3, 'w1'); b.set(13, 8, 'w0'); b.set(12, 9, 'w0');
    b.line(10, 17, 6, 22, 'a0'); b.line(10, 18, 7, 22, 'a1'); b.line(16, 17, 20, 22, 'a1'); b.line(16, 18, 19, 22, 'a1');
    b.rect(12, 11, 3, 2, 'g1'); b.set(12, 11, 'g0');
    b.hl(11, 15, 16, 'a0');
    b.rect(12, 23, 3, 1, 's2');
    // control tower, top right
    b.vl(27, 6, 27, 's1'); b.vl(28, 6, 27, 's2');
    b.rect(25, 3, 6, 4, 'g1'); b.hl(25, 30, 3, 'g0'); b.vl(30, 3, 6, 'g2'); b.hl(25, 30, 7, 'a0');
    b.hl(24, 31, 2, 'w1');
    fx.set(27, 1, 'r0'); fx.set(28, 1, 'r0', 200);
    // fuel tank
    cyl(b, 26, 22, 5, 7, W, 2); b.hl(26, 30, 25, 'a0');
    fx.set(2, 18, 'lit'); fx.set(25, 18, 'lit'); fx.set(13, 7, 'lit');
  },

  SurfaceShipyard: (b, fx) => {
    // slipway
    b.rect(3, 6, 42, 38, 'p1'); b.hl(3, 44, 6, 'p0'); b.vl(3, 6, 43, 'p0'); b.hl(3, 44, 43, 'p3'); b.vl(44, 6, 43, 'p2');
    for (let y = 9; y < 42; y += 4) b.hl(6, 41, y, 'p2');
    // hull under construction
    for (let y = 11; y < 41; y++) {
      const half = y < 18 ? Math.round((y - 10) * 1.2) : 9;
      for (let x = 24 - half; x < 24 + half; x++) {
        const u = (x - (24 - half)) / Math.max(1, 2 * half - 1);
        const ribs = (y % 4 === 0) && y > 26;
        b.set(x, y, ribs ? 's3' : u < 0.2 ? 'w0' : u < 0.6 ? 'w1' : u < 0.85 ? 'w2' : 'w3');
      }
    }
    b.hl(15, 32, 22, 'a0'); b.hl(15, 32, 23, 'a1');
    b.rect(21, 15, 6, 3, 'g1'); b.hl(21, 26, 15, 'g0');
    for (let y = 28; y < 40; y += 4) for (let x = 16; x < 32; x += 3) b.set(x, y + 1, 'e1');
    // gantries
    for (const x of [8, 39]) { b.vl(x, 2, 44, 'y1'); b.vl(x + 1, 2, 44, 'y0'); for (let y = 4; y < 44; y += 4) b.set(x + (y % 8 ? 0 : 1), y, 'ink'); }
    b.rect(8, 2, 33, 2, 'y0'); b.hl(8, 40, 3, 'y1');
    // the crane trolley and its cable
    b.rect(20, 1, 6, 4, 's1'); b.hl(20, 25, 1, 's0'); b.vl(23, 5, 10, 's3');
    fx.set(17, 30, 'e0'); fx.set(29, 34, 'e0'); fx.set(18, 31, 'e1', 200); fx.set(30, 33, 'e1', 200);
  },

  FusionReactor: (b, fx) => {
    // base pad
    b.rect(2, 4, 28, 26, 'p1'); b.hl(2, 29, 4, 'p0'); b.vl(2, 4, 29, 'p0'); b.hl(2, 29, 29, 'p3'); b.vl(29, 4, 29, 'p2');
    // the torus
    b.ell(16, 17, 12, 10.5, (x, y, nx, ny) => {
      const r = Math.sqrt(nx * nx + ny * ny);
      if (r < 0.52) return null;
      const t = (r - 0.52) / 0.48;
      const l = lightOf(nx, ny) * (t < 0.5 ? -1 : 1);
      return ramp(S, 0.5 - l * 0.5);
    });
    // coils round the ring
    for (let a = 0; a < 12; a++) {
      const t = a / 12 * Math.PI * 2;
      b.set(Math.round(16 + Math.cos(t) * 9), Math.round(17 + Math.sin(t) * 8), 'a0');
      b.set(Math.round(16 + Math.cos(t) * 10), Math.round(17 + Math.sin(t) * 9), 'a1');
    }
    // plasma core
    b.ell(16, 17, 5.6, 4.6, (x, y, nx, ny) => { const r = nx * nx + ny * ny; return r < 0.25 ? PAL.q0 : r < 0.6 ? PAL.q1 : PAL.q2; });
    fx.ell(16, 17, 2.2, 1.6, PAL.q0);
    // control blocks in the corners
    box(b, 3, 5, 5, 2, 3, [PAL.w0, PAL.w1], [PAL.w2, PAL.w3]);
    box(b, 24, 24, 5, 2, 3, [PAL.w0, PAL.w1], [PAL.w2, PAL.w3]);
    b.set(4, 9, 'lit'); b.set(26, 28, 'lit');
  },

  GeothermalPlant: (b, fx) => {
    // the vent field
    b.ell(8, 22, 6, 4.5, (x, y, nx, ny) => { const r = nx * nx + ny * ny; return r > 0.7 ? PAL.o2 : r > 0.3 ? PAL.e2 : r > 0.1 ? PAL.e1 : PAL.e0; });
    b.ell(24, 26, 4, 2.6, (x, y, nx, ny) => { const r = nx * nx + ny * ny; return r > 0.6 ? PAL.o2 : r > 0.2 ? PAL.e2 : PAL.e1; });
    // plant hall
    box(b, 13, 8, 16, 4, 9, [PAL.w0, PAL.w1, PAL.w2], [PAL.w2, PAL.w3]);
    b.hl(13, 28, 12, 'a0'); b.hl(13, 28, 13, 'a1');
    windows(b, 15, 15, 6, 2, 2, 2, 'lit', (i, j) => (i + j) % 3 === 2);
    // cooling stacks on the roof
    cyl(b, 15, 3, 4, 7, W, 2); cyl(b, 22, 4, 4, 6, W, 2);
    // pipes from the vents
    b.line(11, 20, 13, 18, 's1'); b.hl(8, 11, 20, 's1'); b.vl(8, 18, 20, 's1');
    b.line(22, 25, 22, 21, 's1');
    puff(fx, 17, 1.6, 2.4); puff(fx, 19.5, 0.4, 1.5); puff(fx, 24, 2.4, 2); puff(fx, 6, 16.5, 2); puff(fx, 4.5, 14.6, 1.4);
  },
};

// ---- the storage depot nine-slice -----------------------------------------------------------------------
// One 48x48 warehouse; the runtime cuts it into a 3x3 grid of cells and picks corner/edge/middle per
// cell, so whatever its size it has walls round the outside, a ridged roof, and loading doors on its
// south face. Drawn 1 px in from the outer edge so the ground shows round it.
function depotSlice(b) {
  box(b, 1, 1, 46, 32, 13, [PAL.w1, PAL.w1, PAL.w2], [PAL.w2, PAL.w3]);
  for (let y = 4; y < 33; y += 4) b.hl(2, 45, y, 'w2');
  for (let y = 5; y < 33; y += 4) b.hl(2, 45, y, 'w0');
  b.hl(1, 46, 33, 'a0'); b.hl(1, 46, 34, 'a1');
  // doors repeat in every slice column so a long depot has them all along
  for (const x of [5, 21, 37]) { b.rect(x, 37, 6, 8, 's2'); for (let y = 38; y < 45; y += 2) b.hl(x, x + 5, y, 's3'); b.hl(x, x + 5, 36, 'y0'); }
  // skylights in the roof, on each slice
  for (const x of [8, 24, 40]) for (const y of [10, 26]) { b.rect(x - 1, y, 3, 2, 'g1'); b.set(x - 1, y, 'g0'); }
}

// ---- output ---------------------------------------------------------------------------------------------
async function writePng(cv, file) {
  await sharp(Buffer.from(cv.d), { raw: { width: cv.w, height: cv.h, channels: 4 } }).png().toFile(file);
}

function render(fn, n) {
  const b = new Cv(n, n), fx = new Cv(n, n);
  fn(b, fx);
  return finish(b, fx);
}

// Unity import settings: Sprite, Point, no compression, no mips, alpha is transparency, 16 px per unit,
// readable (BuildingArt recolours the accent keys on the CPU). Patched from a biome tile's own .meta —
// a format this project's editor has already written — rather than typed from memory, so Unity reads it
// the way it reads the rest. GUID derived from the asset path, like make-script-metas.mjs, so
// regenerating is byte-stable and two machines agree.
const META_TEMPLATE = fs.readFileSync(path.join(BIOMES, 'Plains_16x16.png.meta'), 'utf8');
function metaFor(rel) {
  const guid = crypto.createHash('md5').update(rel.replace(/\\/g, '/')).digest('hex');
  const set = (s, key, val) => {
    const re = new RegExp('^(\\s*' + key + ': ).*$', 'm');
    if (!re.test(s)) throw new Error('meta template has no ' + key);
    return s.replace(re, '$1' + val);
  };
  let m = META_TEMPLATE;
  m = set(m, 'guid', guid);
  m = set(m, 'isReadable', 1);
  m = set(m, 'filterMode', 0);
  m = set(m, 'wrapU', 1); m = set(m, 'wrapV', 1); m = set(m, 'wrapW', 1);
  m = set(m, 'spriteMode', 1);
  m = set(m, 'spritePixelsToUnits', 16);
  m = set(m, 'alphaIsTransparency', 1);
  m = set(m, 'textureType', 8);
  m = m.replace(/textureCompression: \d+/g, 'textureCompression: 0');
  return m;
}

const SITE_SIZE = { SurfaceShipyard: 48 };
export const ART = {};   // name -> Cv, for the contact sheets

fs.mkdirSync(OUT, { recursive: true });
if (!fs.existsSync(OUT + '.meta')) {
  const guid = crypto.createHash('md5').update(path.relative(PROJ, OUT).replace(/\\/g, '/')).digest('hex');
  fs.writeFileSync(OUT + '.meta', fs.readFileSync(BIOMES + '.meta', 'utf8').replace(/^guid: .*$/m, `guid: ${guid}`));
}
const written = [];
async function emit(name, cv) {
  ART[name] = cv;
  const file = path.join(OUT, `Building_${name}.png`);
  await writePng(cv, file);
  const rel = path.relative(PROJ, file);
  if (!fs.existsSync(file + '.meta')) fs.writeFileSync(file + '.meta', metaFor(rel));
  written.push(path.basename(file));
}

for (const [type, vs] of Object.entries(TILE))
  for (let v = 0; v < 2; v++) await emit(`${type}_tile${v}`, render(vs[v], 16));
for (const [type, fn] of Object.entries(SITE)) await emit(`${type}_site`, render(fn, SITE_SIZE[type] ?? 32));
{
  const slice = render(depotSlice, 48);
  await emit('StorageDepot_slice', slice);
  const card = new Cv(32, 32);
  for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++)
    for (const [dx, dy, sx, sy] of [[0, 0, 0, 0], [16, 0, 32, 0], [0, 16, 0, 32], [16, 16, 32, 32]]) {
      const i = ((sy + y) * 48 + sx + x) * 4, j = ((dy + y) * 32 + dx + x) * 4;
      for (let k = 0; k < 4; k++) card.d[j + k] = slice.d[i + k];
    }
  await emit('StorageDepot_card', card);
}
console.log(`${written.length} sprites -> ${path.relative(PROJ, OUT)}`);

if (SHEETS) await makeSheets(SHEETS);

// ============================================================================================
// REVIEW SHEETS — the art as the map will show it, since there is no Unity here to look with
//
//   catalogue.png  every tile and both variants on six biomes, 4x
//   sites.png      the multi-cell sites on the same biomes, 4x, and the capital in three liveries
//   colony.png     a small colony on a mixed-biome grid with the map's overlays drawn the way
//                  PlanetViewWindow draws them: the faint footprint line every building carries, the
//                  hover frame, the selected frame, a valid ghost and a refused one — at 4x (close
//                  zoom) and at 8 px a cell (the "Fit" zoom of the screenshot)
// ============================================================================================
async function makeSheets(dir) {
  fs.mkdirSync(dir, { recursive: true });
  const loadBiome = async (n) => {
    const { data, info } = await sharp(path.join(BIOMES, `${n}_16x16.png`)).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
    const c = new Cv(info.width, info.height); c.d.set(data); return c;
  };
  const BIO = ['grass', 'desert', 'snow', 'LavaRock', 'ocean', 'rocky'];
  const bio = {};
  for (const n of [...BIO, 'Plains', 'forest', 'Hills', 'lake', 'Steppe']) bio[n] = await loadBiome(n);

  // Nearest-neighbour blit of a 16-texel canvas at `s` screen px per texel... or any scale.
  function up(dst, src, ox, oy, s, alpha = 1, sx0 = 0, sy0 = 0, sw = src.w, sh = src.h) {
    for (let y = 0; y < sh; y++) for (let x = 0; x < sw; x++) {
      const i = ((sy0 + y) * src.w + sx0 + x) * 4;
      const a = src.d[i + 3] * alpha;
      if (a <= 0) continue;
      const c = [src.d[i], src.d[i + 1], src.d[i + 2]];
      for (let j = 0; j < s; j++) for (let k = 0; k < s; k++) dst.set(ox + x * s + k, oy + y * s + j, c, a);
    }
  }
  // Downsample like a point-filtered texture drawn smaller than its texels: pick, don't average.
  function pick(dst, src, ox, oy, size, alpha = 1) {
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
      const sx = Math.floor((x + 0.5) * src.w / size), sy = Math.floor((y + 0.5) * src.h / size);
      const i = (sy * src.w + sx) * 4, a = src.d[i + 3] * alpha;
      if (a > 0) dst.set(ox + x, oy + y, [src.d[i], src.d[i + 1], src.d[i + 2]], a);
    }
  }
  const fill = (dst, x, y, w, h, c, a = 255) => { for (let j = y; j < y + h; j++) for (let i = x; i < x + w; i++) dst.set(i, j, c, a); };
  function recolour(src, col) {
    const out = new Cv(src.w, src.h); out.d.set(src.d);
    const dark = col.map(v => Math.round(v * 0.62));
    for (let i = 0; i < out.d.length; i += 4) {
      const r = out.d[i], g = out.d[i + 1], b = out.d[i + 2];
      const k = r === ACCENT.a0[0] && g === ACCENT.a0[1] && b === ACCENT.a0[2] ? col
              : r === ACCENT.a1[0] && g === ACCENT.a1[1] && b === ACCENT.a1[2] ? dark : null;
      if (k) { out.d[i] = k[0]; out.d[i + 1] = k[1]; out.d[i + 2] = k[2]; }
    }
    return out;
  }
  const BG = [16, 20, 28];
  async function save(cv, labels, name) {
    let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${cv.w}" height="${cv.h}">`;
    for (const [x, y, t, size = 13, col = '#c9d5e1'] of labels)
      svg += `<text x="${x}" y="${y}" fill="${col}" font-family="Consolas, monospace" font-size="${size}">${t}</text>`;
    svg += '</svg>';
    const f = path.join(dir, name);
    await sharp(Buffer.from(cv.d), { raw: { width: cv.w, height: cv.h, channels: 4 } })
      .composite([{ input: Buffer.from(svg), top: 0, left: 0 }]).png().toFile(f);
    console.log(`sheet -> ${f}`);
  }

  // ---- catalogue ----
  {
    const types = Object.keys(TILE), S4 = 4, CELL = 16 * S4, GAP = 6, LW = 150;
    const W2 = LW + BIO.length * 2 * (CELL + GAP) + BIO.length * 10, H2 = 30 + types.length * (CELL + GAP);
    const cv = new Cv(W2, H2); fill(cv, 0, 0, W2, H2, BG);
    const labels = [];
    BIO.forEach((n, bi) => labels.push([LW + bi * (2 * (CELL + GAP) + 10), 18, n]));
    types.forEach((t, ri) => {
      const y = 30 + ri * (CELL + GAP);
      labels.push([6, y + CELL / 2 + 4, t]);
      BIO.forEach((n, bi) => {
        for (let v = 0; v < 2; v++) {
          const x = LW + bi * (2 * (CELL + GAP) + 10) + v * (CELL + GAP);
          up(cv, bio[n], x, y, S4);
          up(cv, ART[`${t}_tile${v}`], x, y, S4);
        }
      });
    });
    await save(cv, labels, 'catalogue.png');
  }

  // ---- sites ----
  {
    const sites = [...Object.keys(SITE).map(k => `${k}_site`), 'StorageDepot_card'];
    const S4 = 4, GAP = 10, LW = 150;
    let y = 30; const rows = [];
    for (const s of sites) { rows.push([s, y]); y += ART[s].h * S4 + GAP; }
    const W2 = LW + BIO.length * (48 * S4 + GAP), H2 = y + 32 * S4 + 40;
    const cv = new Cv(W2, H2); fill(cv, 0, 0, W2, H2, BG);
    const labels = [];
    BIO.forEach((n, bi) => labels.push([LW + bi * (48 * S4 + GAP), 18, n]));
    for (const [s, ry] of rows) {
      labels.push([6, ry + 20, s.replace(/_site|_card/, '')]);
      const art = ART[s], cells = art.w / 16;
      BIO.forEach((n, bi) => {
        const x = LW + bi * (48 * S4 + GAP);
        for (let j = 0; j < cells; j++) for (let i = 0; i < cells; i++) up(cv, bio[n], x + i * 64, ry + j * 64, S4);
        up(cv, art, x, ry, S4);
      });
    }
    // the capital in three liveries
    labels.push([6, y + 20, 'Capitol liveries']);
    [[89, 255, 115, 'Your Empire'], [89, 179, 255, 'The Concord'], [255, 89, 77, 'Vurl Dominion']].forEach(([r, g, b2, nm], i) => {
      const x = LW + i * (48 * S4 + GAP);
      for (let j = 0; j < 2; j++) for (let k = 0; k < 2; k++) up(cv, bio[['grass', 'desert', 'snow'][i]], x + k * 64, y + j * 64, S4);
      up(cv, recolour(ART.PlanetCapitol_site, [r, g, b2]), x, y, S4);
      labels.push([x, y + 146, nm]);
    });
    await save(cv, labels, 'sites.png');
  }

  // ---- the colony ----
  {
    const GW = 18, GH = 11;
    // terrain: grass with forest, a lake, rocky hills to the east, steppe to the south
    const ter = [];
    for (let y = 0; y < GH; y++) { ter.push([]); for (let x = 0; x < GW; x++) {
      let t = 'grass';
      if ((x - 3) ** 2 + (y - 8) ** 2 < 5) t = 'lake';
      else if (x > 13 || (x > 11 && y < 4)) t = 'rocky';
      else if (y > 8 && x > 5) t = 'Steppe';
      else if ((x - 7) ** 2 * 0.5 + (y - 1) ** 2 < 2.2) t = 'forest';
      else if (x === 0 || (x < 3 && y < 2)) t = 'forest';
      ter[y].push(t);
    } }
    // buildings: [art kind, cells]
    const B = [];
    const put = (type, cells, how = 'tile') => B.push({ type, cells, how });
    put('PlanetCapitol', [[7, 4], [8, 4], [7, 5], [8, 5]], 'site');
    put('Spaceport', [[10, 6], [11, 6], [10, 7], [11, 7]], 'site');
    put('Farm', [[2, 2], [3, 2], [4, 2], [2, 3], [3, 3], [4, 3], [3, 4], [4, 4]]);
    put('Mine', [[14, 2], [15, 2], [15, 3]]);
    put('Refinery', [[13, 5]]);
    put('Factory', [[12, 4], [13, 4]]);
    put('City', [[6, 4], [6, 5], [6, 6], [7, 6]]);
    put('Town', [[9, 3], [5, 6]]);
    put('Settlement', [[5, 3]]);
    put('Habitat', [[9, 4], [9, 5]]);
    put('SolarArray', [[7, 9], [8, 9], [9, 9]]);
    put('WindFarm', [[11, 9], [12, 9], [12, 10]]);
    put('HydroPlant', [[3, 8]]);
    put('PowerNode', [[5, 7], [10, 2]]);
    put('ResearchOutpost', [[11, 2]]);
    put('ResearchCenter', [[16, 6]]);
    put('FusionReactor', [[14, 8], [15, 8], [14, 9], [15, 9]], 'site');
    put('StorageDepot', [[1, 5], [2, 5], [3, 5], [1, 6], [2, 6], [3, 6]], 'slice');
    put('Capacitor', [[13, 7]]);
    put('FissionReactor', [[16, 9]]);
    const hover = [3, 7];          // empty grass
    const hoverB = 5;              // ...and, separately, a hovered building (the factory)
    const selected = 0;            // the capitol
    const ghostOk = [[16, 2], [16, 3]];   // a mine being drawn on rock
    const ghostBad = [[1, 9]];            // refused (on water's edge)

    const variant = (x, y) => {   // BuildingArt.Variant
      let h = (Math.imul(x, 73856093) ^ Math.imul(y, 19349663)) >>> 0;
      h = (h ^ (h >>> 13)) >>> 0; h = Math.imul(h, 0x5bd1e995) >>> 0; h = (h ^ (h >>> 15)) >>> 0;
      return h & 1;
    };
    const cellArt = (bd, cx, cy) => {
      if (bd.how === 'tile') return { src: ART[`${bd.type}_tile${variant(cx, cy)}`], sx: 0, sy: 0 };
      const set = new Set(bd.cells.map(c => c.join()));
      if (bd.how === 'site') {
        const minX = Math.min(...bd.cells.map(c => c[0])), minY = Math.min(...bd.cells.map(c => c[1]));
        return { src: ART[`${bd.type}_site`], sx: (cx - minX) * 16, sy: (cy - minY) * 16 };
      }
      const l = set.has([cx - 1, cy].join()), r = set.has([cx + 1, cy].join());
      const u = set.has([cx, cy - 1].join()), d = set.has([cx, cy + 1].join());
      return { src: ART.StorageDepot_slice, sx: (!l ? 0 : !r ? 2 : 1) * 16, sy: (!u ? 0 : !d ? 2 : 1) * 16 };
    };
    const owner = [89, 255, 115];
    const livery = new Map();
    const liv = (src) => { if (!livery.has(src)) livery.set(src, recolour(src, owner)); return livery.get(src); };

    function edge(dst, cells, s, ox, oy, col, a, px) {
      const set = new Set(cells.map(c => c.join()));
      for (const [x, y] of cells) {
        const X = ox + x * s, Y = oy + y * s;
        if (!set.has([x, y - 1].join())) fill(dst, X, Y, s, px, col, a);
        if (!set.has([x, y + 1].join())) fill(dst, X, Y + s - px, s, px, col, a);
        if (!set.has([x - 1, y].join())) fill(dst, X, Y, px, s, col, a);
        if (!set.has([x + 1, y].join())) fill(dst, X + s - px, Y, px, s, col, a);
      }
    }
    function drawColony(dst, ox, oy, s) {
      const px = s >= 32 ? 2 : 1;
      for (let y = 0; y < GH; y++) for (let x = 0; x < GW; x++) {
        if (s >= 16) up(dst, bio[ter[y][x]], ox + x * s, oy + y * s, s / 16); else pick(dst, bio[ter[y][x]], ox + x * s, oy + y * s, s);
      }
      // the always-on footprint line (PlanetViewWindow.FootprintLine), under the art
      for (const bd of B) edge(dst, bd.cells, s, ox, oy, [0, 0, 0], 77, px);
      for (const bd of B) for (const [x, y] of bd.cells) {
        const { src, sx, sy } = cellArt(bd, x, y);
        const tex = liv(src);
        if (s >= 16) up(dst, tex, ox + x * s, oy + y * s, s / 16, 1, sx, sy, 16, 16);
        else { const tmp = new Cv(16, 16); up(tmp, tex, 0, 0, 1, 1, sx, sy, 16, 16); pick(dst, tmp, ox + x * s, oy + y * s, s); }
      }
      // hover: the cell under the cursor
      edge(dst, [hover], s, ox, oy, [255, 255, 255], 204, px);
      // hover over a building: its whole footprint, a little softer than the cell frame
      edge(dst, B[hoverB].cells, s, ox, oy, [255, 255, 255], 128, px);
      // selected: the capital's whole footprint, a gold frame with a dark keyline outside it
      const sel = B[selected].cells;
      const selPx = Math.round(Math.min(2.5, Math.max(1, s * 0.1)));   // PlanetViewWindow.DrawSelectionMarker
      edge(dst, sel, s, ox, oy, [0, 0, 0], 140, selPx + Math.min(2, selPx));
      edge(dst, sel, s, ox, oy, [255, 235, 107], 255, selPx);
      // valid ghost: a pale wash, the art, a black perimeter
      for (const [x, y] of ghostOk) fill(dst, ox + x * s, oy + y * s, s, s, [255, 255, 255], 51);
      for (const [x, y] of ghostOk) {
        const tex = liv(ART[`Mine_tile${variant(x, y)}`]);
        if (s >= 16) up(dst, tex, ox + x * s, oy + y * s, s / 16, 0.88); else pick(dst, tex, ox + x * s, oy + y * s, s, 0.88);
      }
      edge(dst, ghostOk, s, ox, oy, [0, 0, 0], 240, px);
      for (const [x, y] of ghostBad) fill(dst, ox + x * s + 1, oy + y * s + 1, s - 2, s - 2, [255, 64, 51], 217);
      edge(dst, ghostBad, s, ox, oy, [0, 0, 0], 240, px);
    }
    const S1 = 64, S2 = 8;
    const W2 = GW * S1 + 40, H2 = GH * S1 + GH * S2 * 2 + 120;
    const cv = new Cv(W2, H2); fill(cv, 0, 0, W2, H2, BG);
    drawColony(cv, 20, 30, S1);
    const y2 = 30 + GH * S1 + 40;
    drawColony(cv, 20, y2, S2);
    // and the same at 16 px a cell — roughly one zoom step in from Fit
    drawColony(cv, 20 + GW * S2 + 30, y2, 16);
    await save(cv, [
      [20, 20, 'Colony at close zoom (64 px a cell) -- gold: selected Capitol, white: hovered tile / hovered factory, pale wash + black edge: a mine being placed, red: refused tile'],
      [20, y2 - 8, '8 px a cell (Fit)'], [20 + GW * S2 + 30, y2 - 8, '16 px a cell'],
    ], 'colony.png');
  }
}
