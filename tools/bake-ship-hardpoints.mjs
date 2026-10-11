// ============================================================================================
// WHERE THE ENGINES AND THE GUNS ARE — baked from each hull's own geometry
//
//   node tools/bake-ship-hardpoints.mjs            # write ship-hardpoints.json + print the table
//   node tools/bake-ship-hardpoints.mjs --check    # write nothing; exit 1 if the json is stale
//   node tools/bake-ship-hardpoints.mjs --table    # also print every hull's points
//
// Output: Assets/Resources/SpaceAssets/Ships/ship-hardpoints.json, read by ShipHardpoints.cs.
//
// ---- THE FRAME EVERYTHING HERE IS MEASURED IN --------------------------------------------------
//
// glTF is right-handed and Unity is left-handed, and glTFast converts by NEGATING X — positions and
// normals alike (com.unity.cloud.gltfast 6.10.1, Runtime/Scripts/Jobs.cs,
// ConvertVector3FloatToFloatInterleavedJob: `tmp.x *= -1;`), node translations the same way
// (NodeExtension.GetTransform: `-node.translation[0]`) and rotations as (x, -y, -z, w). So a hull whose
// nose is on glTF -X arrives in Unity with its nose on +X.
//
// Every point below is therefore converted to UNITY MESH SPACE first (x = -x), which is the space a
// child of the instantiated prefab's root lives in: the generated hulls are one node at identity, so
// the root's local frame IS the mesh's vertex frame. That is what lets ShipLights parent a plume
// straight at a baked position.
//
// The manifest rotation (ship-meshes.txt) then carries mesh space into SHIP SPACE — bow +Z, dorsal +Y,
// starboard +X — which is where "the rear face", "the bow" and "the flanks" mean anything. Points are
// FOUND in ship space and STORED in mesh space, together with the rotation they were found under, so
// the game can tell when a manifest line has changed since the bake and fall back to the bounds
// instead of putting a plume on the nose.
//
// ---- WHAT IS FOUND -------------------------------------------------------------------------------
//
//   nozzles   the rear-most surface of the hull, weighted toward what GLOWS (meshy-6 bakes an emission
//             map, and a drive bell is usually the brightest thing at the stern), clustered into one
//             to four engines by weighted k-means across the beam. Stations get none.
//   guns      ship hulls: the bow tip, the two flanks, a dorsal pair along the spine and the keel —
//             each the real vertex furthest out in that direction, so a shot leaves the hull rather
//             than empty space. Stations: eight points round the rim plus the crown and the keel.
//   nose      measure-bow's two numbers taken along SHIP +Z after the manifest rotation: which end is
//             sharper and which end is bulkier. They are not a verdict (a swordfish's bill and a
//             shrimp's tail are both "the sharp end"), so they only FLAG a line for a look.
// ============================================================================================

import { NodeIO } from '@gltf-transform/core';
import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const RES = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets');
const OUT = path.join(RES, 'Ships', 'ship-hardpoints.json');
const MANIFEST = path.join(RES, 'Ships', 'ship-meshes.txt');
const argv = process.argv.slice(2);
const CHECK = argv.includes('--check');
const TABLE = argv.includes('--table');

const CIVS = ['Terran', 'Aquarii', 'Pyrothian', 'Cryithn', 'Sylvan'];

// ---- the manifest, parsed exactly as ShipMeshManifest.Reload does ----------------------------------
function readManifest() {
  const map = new Map();
  for (const raw of fs.readFileSync(MANIFEST, 'utf8').split('\n')) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    const tok = line.split(/\s+/);
    let first = -1;
    for (let i = 1; i < tok.length; i++) if (!isNaN(parseFloat(tok[i])) && /^[-+]?\d/.test(tok[i])) { first = i; break; }
    if (first < 1 || first + 2 >= tok.length) continue;
    const name = tok.slice(0, first).join(' ');
    const rot = [parseFloat(tok[first]), parseFloat(tok[first + 1]), parseFloat(tok[first + 2])];
    const flags = tok.slice(first + 3).map(t => t.toLowerCase());
    map.set(name, { rot, spin: flags.includes('spin'), nospin: flags.includes('nospin') });
  }
  return map;
}

// ---- Unity's Quaternion.Euler as a matrix: applied Z, then X, then Y (v' = Ry * Rx * Rz * v) ------
// Left-handed: Euler(0,90,0) * forward = right, Euler(90,0,0) * forward = down, Euler(0,0,90) * right = up.
function eulerMatrix([px, py, pz]) {
  const r = d => d * Math.PI / 180;
  const cx = Math.cos(r(px)), sx = Math.sin(r(px));
  const cy = Math.cos(r(py)), sy = Math.sin(r(py));
  const cz = Math.cos(r(pz)), sz = Math.sin(r(pz));
  const Rx = [[1, 0, 0], [0, cx, -sx], [0, sx, cx]];
  const Ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]];
  const Rz = [[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]];
  return mul(Ry, mul(Rx, Rz));
}
function mul(a, b) {
  const o = [[0, 0, 0], [0, 0, 0], [0, 0, 0]];
  for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) for (let k = 0; k < 3; k++) o[i][j] += a[i][k] * b[k][j];
  return o;
}
const apply = (m, v) => [
  m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
  m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
  m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2]];
const transpose = m => [[m[0][0], m[1][0], m[2][0]], [m[0][1], m[1][1], m[2][1]], [m[0][2], m[1][2], m[2][2]]];

// ---- geometry ---------------------------------------------------------------------------------------
function nodeMatrix(node) {
  // World matrix of a node as a column-major 4x4 (gltf-transform gives getWorldMatrix()).
  return node.getWorldMatrix();
}
function xform(m4, v) {
  return [
    m4[0] * v[0] + m4[4] * v[1] + m4[8] * v[2] + m4[12],
    m4[1] * v[0] + m4[5] * v[1] + m4[9] * v[2] + m4[13],
    m4[2] * v[0] + m4[6] * v[1] + m4[10] * v[2] + m4[14]];
}

async function emissionSampler(prim) {
  const mat = prim.getMaterial();
  if (!mat) return null;
  const tex = mat.getEmissiveTexture();
  if (!tex) return null;
  const img = tex.getImage();
  if (!img) return null;
  try {
    const { data, info } = await sharp(Buffer.from(img)).removeAlpha().raw().toBuffer({ resolveWithObject: true });
    const W = info.width, H = info.height, C = info.channels;
    const f = mat.getEmissiveFactor() || [1, 1, 1];
    return (u, v) => {
      const x = Math.min(W - 1, Math.max(0, Math.floor((u - Math.floor(u)) * W)));
      const y = Math.min(H - 1, Math.max(0, Math.floor((v - Math.floor(v)) * H)));
      const i = (y * W + x) * C;
      const r = data[i] / 255 * f[0], g = data[i + 1] / 255 * f[1], b = data[i + 2] / 255 * f[2];
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
  } catch { return null; }
}

/** Every triangle as { c: centroid, a: area, e: emissive 0..1, v: [3 verts] } in UNITY MESH space. */
async function triangles(doc) {
  const out = [];
  for (const node of doc.getRoot().listNodes()) {
    const mesh = node.getMesh();
    if (!mesh) continue;
    const m4 = nodeMatrix(node);
    for (const prim of mesh.listPrimitives()) {
      const pos = prim.getAttribute('POSITION');
      if (!pos) continue;
      const uv = prim.getAttribute('TEXCOORD_0');
      const sample = uv ? await emissionSampler(prim) : null;
      const idx = prim.getIndices();
      const n = idx ? idx.getCount() : pos.getCount();
      const P = [0, 0, 0], T = [0, 0];
      const vert = i => {
        const k = idx ? idx.getScalar(i) : i;
        pos.getElement(k, P);
        const w = xform(m4, P);
        return [-w[0], w[1], w[2]];                    // glTFast: x *= -1
      };
      const tex = i => { const k = idx ? idx.getScalar(i) : i; uv.getElement(k, T); return [T[0], T[1]]; };
      for (let i = 0; i + 2 < n; i += 3) {
        const a = vert(i), b = vert(i + 1), c = vert(i + 2);
        const ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
        const vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
        const area = 0.5 * Math.hypot(uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx);
        if (!(area > 0)) continue;
        let e = 0;
        if (sample) {
          const t0 = tex(i), t1 = tex(i + 1), t2 = tex(i + 2);
          e = sample((t0[0] + t1[0] + t2[0]) / 3, (t0[1] + t1[1] + t2[1]) / 3);
        }
        out.push({ v: [a, b, c], c: [(a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3, (a[2] + b[2] + c[2]) / 3], a: area, e });
      }
    }
  }
  return out;
}

function boundsOf(points) {
  const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
  for (const p of points) for (let k = 0; k < 3; k++) { if (p[k] < lo[k]) lo[k] = p[k]; if (p[k] > hi[k]) hi[k] = p[k]; }
  return { lo, hi, size: [hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]], center: [(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2] };
}

// ---- nose: measure-bow's two numbers, along ship +Z ------------------------------------------------
function noseStats(tris, b) {
  const L = Math.max(1e-6, b.size[2]), mid = b.center[2], half = L / 2;
  let ws = 0, w = 0;
  const S = 20;
  const span = Array.from({ length: S }, () => ({ lo: [Infinity, Infinity], hi: [-Infinity, -Infinity] }));
  for (const t of tris) {
    ws += t.a * (t.c[2] - mid); w += t.a;
    let s = Math.floor(((t.c[2] - b.lo[2]) / L) * S); s = Math.min(S - 1, Math.max(0, s));
    for (const v of t.v) for (let o = 0; o < 2; o++) {
      const k = o === 0 ? 0 : 1;
      if (v[k] < span[s].lo[o]) span[s].lo[o] = v[k];
      if (v[k] > span[s].hi[o]) span[s].hi[o] = v[k];
    }
  }
  const girth = s => span[s].hi[0] < span[s].lo[0] ? 0 : Math.hypot(span[s].hi[0] - span[s].lo[0], span[s].hi[1] - span[s].lo[1]);
  const gMax = Math.max(...span.map((_, i) => girth(i))) || 1;
  const back = (girth(0) + girth(1)) / 2 / gMax, front = (girth(S - 1) + girth(S - 2)) / 2 / gMax;
  return { massSkew: (ws / Math.max(1e-9, w)) / half, tipSharp: back - front };   // tipSharp > 0: +Z end is sharper
}

// ---- weighted k-means on the (x, y) cross-section --------------------------------------------------
function kmeans(pts, k, iters = 18) {
  // pts: [{x, y, z, w}]. Seed along the beam (x), which is where twin and quad engines spread.
  const xs = pts.map(p => p.x).sort((a, b) => a - b);
  let cent = [];
  for (let i = 0; i < k; i++) {
    const q = xs[Math.min(xs.length - 1, Math.floor((i + 0.5) / k * xs.length))];
    const near = pts.reduce((best, p) => Math.abs(p.x - q) < Math.abs(best.x - q) ? p : best, pts[0]);
    cent.push([near.x, near.y]);
  }
  let assign = new Array(pts.length).fill(0);
  for (let it = 0; it < iters; it++) {
    for (let i = 0; i < pts.length; i++) {
      let best = 0, bd = Infinity;
      for (let c = 0; c < k; c++) { const d = (pts[i].x - cent[c][0]) ** 2 + (pts[i].y - cent[c][1]) ** 2; if (d < bd) { bd = d; best = c; } }
      assign[i] = best;
    }
    const acc = cent.map(() => [0, 0, 0]);
    for (let i = 0; i < pts.length; i++) { const a = acc[assign[i]]; a[0] += pts[i].x * pts[i].w; a[1] += pts[i].y * pts[i].w; a[2] += pts[i].w; }
    cent = acc.map((a, c) => a[2] > 0 ? [a[0] / a[2], a[1] / a[2]] : cent[c]);
  }
  let sse = 0;
  for (let i = 0; i < pts.length; i++) sse += pts[i].w * ((pts[i].x - cent[assign[i]][0]) ** 2 + (pts[i].y - cent[assign[i]][1]) ** 2);
  return { cent, assign, sse };
}

function findNozzles(tris, b) {
  const L = b.size[2], W = Math.max(1e-6, b.size[0]), H = Math.max(1e-6, b.size[1]);
  const rearBand = 0.18 * L;
  const zmin = b.lo[2];
  const pts = [];
  let emisTotal = 0;
  for (const t of tris) {
    const depth = t.c[2] - zmin;
    if (depth > rearBand) continue;
    const rear = 1 - depth / rearBand;
    const w = t.a * (1 + 6 * t.e) * rear * rear;
    emisTotal += t.a * t.e;
    pts.push({ x: t.c[0], y: t.c[1], z: t.c[2], w, e: t.e });
  }
  if (pts.length < 3) return { nozzles: [], emissive: false };

  // Choose k: start at one engine and add another only while it buys a real drop in spread. Capped at
  // four, because nothing in the fleet is drawn big enough for a fifth plume to read.
  let best = kmeans(pts, 1), k = 1;
  for (let kk = 2; kk <= 4 && pts.length >= kk * 4; kk++) {
    const r = kmeans(pts, kk);
    if (r.sse < best.sse * 0.52) { best = r; k = kk; } else break;
  }

  const totalW = pts.reduce((s, p) => s + p.w, 0);
  const out = [];
  for (let c = 0; c < k; c++) {
    const mine = pts.filter((_, i) => best.assign[i] === c);
    const wsum = mine.reduce((s, p) => s + p.w, 0);
    if (wsum < totalW * 0.08) continue;                       // a stray fin, not an engine
    const [cx, cy] = best.cent[c];
    let vr = 0;
    for (const p of mine) vr += p.w * ((p.x - cx) ** 2 + (p.y - cy) ** 2);
    const radius = Math.min(0.3 * W, Math.max(0.04 * W, Math.sqrt(vr / wsum)));
    // The rear face of THIS cluster: a low percentile of its z, so one stray spike does not pull the
    // plume out into space behind the hull.
    const zs = mine.map(p => p.z).sort((a, b) => a - b);
    const z = zs[Math.min(zs.length - 1, Math.floor(zs.length * 0.04))];
    const glow = mine.reduce((s, p) => s + p.w * p.e, 0) / wsum;
    out.push({ s: [cx, cy, z], r: radius, glow, share: wsum / totalW });
  }
  out.sort((a, b) => a.s[0] - b.s[0]);
  return { nozzles: out, emissive: emisTotal > 1e-9 };
}

/** The real vertex furthest along `dir` among vertices passing `filter` (ship space). */
function extreme(verts, dir, filter) {
  let best = null, bd = -Infinity;
  for (const v of verts) {
    if (filter && !filter(v)) continue;
    const d = v[0] * dir[0] + v[1] * dir[1] + v[2] * dir[2];
    if (d > bd) { bd = d; best = v; }
  }
  return best;
}
const norm = v => { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; };

function findGuns(verts, b, station) {
  const guns = [];
  const L = b.size[2], zlo = b.lo[2];
  const along = v => (v[2] - zlo) / Math.max(1e-6, L);
  const add = (p, d) => { if (p) guns.push({ s: p, d: norm(d) }); };

  if (station) {
    // Rim, crown and keel. The rim is taken in the station's equatorial band so a spire does not win
    // every direction.
    const yMid = b.center[1], band = b.size[1] * 0.35;
    for (let i = 0; i < 8; i++) {
      const a = i / 8 * Math.PI * 2;
      const d = [Math.cos(a), 0, Math.sin(a)];
      add(extreme(verts, d, v => Math.abs(v[1] - yMid) <= band), d);
    }
    add(extreme(verts, [0, 1, 0]), [0, 1, 0]);
    add(extreme(verts, [0, -1, 0]), [0, -1, 0]);
    return guns;
  }

  // Bow: the forward tip, nudged to fire straight ahead.
  add(extreme(verts, [0, 0, 1]), [0, 0, 1]);
  // Flanks, in the forward 65% so a swept wingtip at the stern is not the "broadside".
  add(extreme(verts, [-1, 0, 0.15], v => along(v) > 0.35), [-1, 0, 0.35]);
  add(extreme(verts, [1, 0, 0.15], v => along(v) > 0.35), [1, 0, 0.35]);
  // A dorsal pair along the spine, fore and aft of amidships.
  add(extreme(verts, [0, 1, 0], v => along(v) > 0.5 && along(v) < 0.85), [0, 1, 0.4]);
  add(extreme(verts, [0, 1, 0], v => along(v) > 0.2 && along(v) <= 0.5), [0, 1, 0.1]);
  // Keel.
  add(extreme(verts, [0, -1, 0], v => along(v) > 0.3 && along(v) < 0.8), [0, -1, 0.3]);
  return guns;
}

// ---- the run ---------------------------------------------------------------------------------------
const manifest = readManifest();
const io = new NodeIO();
const entries = [];
const rows = [];

for (const civ of CIVS) {
  for (const sub of ['Ships', 'Stations']) {
    const dir = path.join(RES, sub, civ);
    if (!fs.existsSync(dir)) continue;
    for (const f of fs.readdirSync(dir).sort()) {
      if (!f.endsWith('.glb') || /_(hi|lo)\.glb$/.test(f)) continue;
      const name = f.replace(/\.glb$/, '');
      const station = sub === 'Stations';
      const line = manifest.get(name);
      const rot = line ? line.rot : [0, 0, 0];
      const R = eulerMatrix(rot), Rt = transpose(R);

      const doc = await io.read(path.join(dir, f));
      const meshTris = await triangles(doc);
      if (!meshTris.length) { console.log(`  !! ${name}: no triangles`); continue; }

      // Ship space.
      const tris = meshTris.map(t => ({ ...t, c: apply(R, t.c), v: t.v.map(v => apply(R, v)) }));
      const verts = [];
      for (const t of tris) for (const v of t.v) verts.push(v);
      const b = boundsOf(verts);
      const mb = boundsOf(meshTris.flatMap(t => t.v));

      const nose = noseStats(tris, b);
      const { nozzles, emissive } = station ? { nozzles: [], emissive: meshTris.some(t => t.e > 0) } : findNozzles(tris, b);
      const guns = findGuns(verts, b, station);

      const toMesh = s => apply(Rt, s).map(x => +x.toFixed(4));
      entries.push({
        name,
        rot,
        station,
        boundsCenter: mb.center.map(x => +x.toFixed(4)),
        boundsSize: mb.size.map(x => +x.toFixed(4)),
        nozzles: nozzles.map(n => ({ p: toMesh(n.s), r: +n.r.toFixed(4), glow: +n.glow.toFixed(3) })),
        guns: guns.map(g => ({ p: toMesh(g.s), d: toMesh(g.d) })),
      });

      // Where does the raw glTF frame end up? The bow is ship +Z; R^-1 * (0,0,1) is the mesh-space
      // (Unity) axis that becomes the bow, and glTF is that with X negated back.
      const bowMesh = apply(Rt, [0, 0, 1]).map(x => Math.round(x));
      const axisName = v => (v[0] ? (v[0] > 0 ? '+X' : '-X') : v[1] ? (v[1] > 0 ? '+Y' : '-Y') : (v[2] > 0 ? '+Z' : '-Z'));
      const bowGltf = [-bowMesh[0], bowMesh[1], bowMesh[2]];
      const upMesh = apply(Rt, [0, 1, 0]).map(x => Math.round(x));
      const longest = mb.size.indexOf(Math.max(...mb.size));
      rows.push({
        name, station, rot, nospin: line?.nospin, spin: line?.spin, authored: !!line,
        bowGltf: axisName(bowGltf), bowUnity: axisName(bowMesh), up: axisName(upMesh),
        dims: mb.size, longest: 'XYZ'[longest],
        nose, nozzles: nozzles.length, emissive,
      });
    }
  }
}

entries.sort((a, b) => a.name.localeCompare(b.name));
const json = JSON.stringify({
  note: 'Generated by tools/bake-ship-hardpoints.mjs from the .glb geometry. Positions are UNITY MESH SPACE (glTF x negated, as glTFast imports it); rot is the ship-meshes.txt line they were found under. Do not hand-edit; re-run the tool.',
  frame: 'unity-mesh',
  entries,
}, null, 1) + '\n';

if (CHECK) {
  const have = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : '';
  if (have !== json) { console.log('ship-hardpoints.json is STALE — run node tools/bake-ship-hardpoints.mjs'); process.exit(1); }
  console.log(`ship-hardpoints.json is current (${entries.length} hulls).`);
} else {
  fs.writeFileSync(OUT, json);
  console.log(`wrote ${path.relative(PROJ, OUT)} — ${entries.length} hulls`);
}

// ---- the orientation table --------------------------------------------------------------------------
//
// conf: how far the geometry agrees that ship +Z is the bow. "ok" = the +Z end is the sharper one AND
// the bulk sits aft; "??" = they disagree (normal for fish, birds and blunt-nosed hulls — check by eye);
// "LOW" = both say the bow is behind. Stations are judged on attitude instead: upright = long on Y.
console.log('\nhull                         rot (p y r)    bow<-glTF  bow<-Unity  up    dims (Unity mesh, x y z)    tipSharp massSkew  conf  nozzles');
console.log(''.padEnd(140, '-'));
for (const r of rows) {
  let conf;
  if (r.station) conf = r.longest === 'Y' ? 'upright' : 'flat';
  else {
    const sharpFront = r.nose.tipSharp < 0;   // +Z end narrower -> tipSharp = back - front > 0 means back wider... see below
    // tipSharp = girth(back) - girth(front): positive means the BACK is wider, i.e. the front is sharper.
    const frontSharper = r.nose.tipSharp > 0.02;
    const bulkAft = r.nose.massSkew < -0.02;
    const frontBlunter = r.nose.tipSharp < -0.02, bulkFore = r.nose.massSkew > 0.02;
    conf = frontSharper && bulkAft ? 'ok' : (frontBlunter && bulkFore ? 'LOW' : '??');
    void sharpFront;
  }
  console.log(
    r.name.padEnd(29) + `${r.rot.join(' ')}`.padEnd(15) + r.bowGltf.padEnd(11) + r.bowUnity.padEnd(12) + r.up.padEnd(6) +
    r.dims.map(d => d.toFixed(2)).join(' x ').padEnd(28) +
    r.nose.tipSharp.toFixed(3).padStart(8) + r.nose.massSkew.toFixed(3).padStart(9) + '  ' + conf.padEnd(8) +
    (r.station ? '-' : String(r.nozzles)) + (r.emissive ? '' : '  (no emission map)') + (r.authored ? '' : '  NO MANIFEST LINE'));
}

if (TABLE) for (const e of entries) {
  console.log(`\n${e.name}`);
  for (const n of e.nozzles) console.log(`  nozzle ${n.p.join(', ')}  r ${n.r}  glow ${n.glow}`);
  for (const g of e.guns) console.log(`  gun    ${g.p.join(', ')}  dir ${g.d.join(', ')}`);
}
