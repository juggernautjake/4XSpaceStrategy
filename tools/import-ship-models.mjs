// ============================================================================================
// IMPORT THE GENERATED SHIP FLEET INTO THE PROJECT
//
// Takes the raw Meshy output tree (`Downloads/4X-Ship-Models`) and produces game-ready `.glb`
// files under `Assets/Resources/SpaceAssets/`, named the way the game looks them up.
//
//   node tools/import-ship-models.mjs [--src <dir>] [--dry]
//
// ---- WHY THIS EXISTS AT ALL ------------------------------------------------------------------
//
// The raw art cannot go into the project as it stands. A single Terran Dreadnought is:
//
//     1,996,570 triangles     74.5 MB of vertex data
//     4096x4096 base colour   4096x4096 normal   2048x2048 metallic-roughness
//     99 MB on disk           201 MB of GPU memory
//
// That is one ship. There are 160. Nearly 900 MB of source art, and everything under `Resources/`
// is loaded into the build whether or not it is ever used — so shipping it raw would put close to a
// gigabyte of 4K textures into a game whose ships are drawn at BETWEEN 0.09 AND 0.40 WORLD UNITS.
// A planet is only ~0.6-2.2 units across (SystemVisualizer), so a dreadnought is a few dozen pixels.
// Four thousand pixels of texture on forty pixels of ship is about a hundredfold waste.
//
// So every model is decimated and its textures downscaled before it lands in the project. The
// numbers below are chosen against the size these things are actually DRAWN at, not against the
// size they were generated at.
//
// ---- WHAT IT DOES ----------------------------------------------------------------------------
//
//   weld       merge duplicate vertices so the simplifier has a connected surface to work on.
//              Meshy output is unwelded; skipping this makes simplify almost a no-op.
//   simplify   collapse to TARGET_TRIS. ~12k triangles is still far more than a 40-pixel ship
//              needs, and leaves headroom to look at these up close in a ship inspector later.
//   resize     textures down to the sizes in TEX. sharp is driven DIRECTLY here rather than through
//              gltf-transform's own textureCompress, which fails on Meshy's embedded JPEGs with
//              "colourspace: parameter space not set". sharp itself handles the same images fine,
//              so the bug is in the glue, not the encoder.
//   prune      drop anything the above orphaned.
//
// ---- ORIENTATION -----------------------------------------------------------------------------
//
// This script does NOT rotate anything. Orientation is ShipMeshManifest's job at load time, and it
// works off the mesh's own bounds — so what matters here is that the bounds survive intact, which
// they do. The script REPORTS each model's axis lengths so a hull that will confuse the heuristic
// (one that is taller than it is wide, say) is visible before anyone runs the game.
// ============================================================================================

import { NodeIO } from '@gltf-transform/core';
import { weld, simplify, prune, dedup } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// ---- Tuning ----------------------------------------------------------------------------------
const TARGET_TRIS = 12000;     // per model, before error-tolerance rounding
const SIMPLIFY_ERROR = 0.02;   // how far a vertex may move, as a fraction of the mesh's size
const TEX = {
  baseColorTexture:         512,
  normalTexture:            256,
  metallicRoughnessTexture: 256,
  emissiveTexture:          256,
  occlusionTexture:         256,
};
const JPEG_QUALITY = 82;

// ---- The 29 hulls, folder name -> UnitType enum name ------------------------------------------
// The folder names are human-readable and the enum names are what the game keys on, so the mapping
// has to be written down once. Order matches UnitType and the prompt book's canonical 29.
const HULLS = {
  '01-Scout': 'Scout',                          '02-Scout-MkII': 'ScoutII',
  '03-Scout-MkIII': 'ScoutIII',                 '04-Explorer': 'Explorer',
  '05-Probe': 'Probe',                          '06-Research-Ship': 'ResearchShip',
  '07-Research-Ship-MkII': 'ResearchShipII',    '08-Research-Ship-MkIII': 'ResearchShipIII',
  '09-Science-Vessel': 'ScienceVessel',         '10-Colony-Ship': 'ColonyShip',
  '11-Mining-Barge': 'Miner',                   '12-Transport': 'Transport',
  '13-Terraformer': 'Terraformer',              '14-Fighter': 'Fighter',
  '15-Fighter-MkII': 'FighterII',               '16-Fighter-MkIII': 'FighterIII',
  '17-Frigate': 'Frigate',                      '18-Cruiser': 'Cruiser',
  '19-Carrier': 'Carrier',                      '20-Dreadnought': 'Dreadnought',
  '21-Battle-Station': 'BattleStation',         '22-Research-Station': 'ResearchStation',
  '23-Relay-Station': 'RelayStation',           '24-Supply-Station': 'SupplyStation',
  '25-Multi-Role-Station': 'MultiStation',      '26-Terraforming-Station': 'TerraformStation',
  '27-Deep-Space-Station': 'DeepSpaceStation',  '28-Mega-Station': 'MegaStation',
  '29-Hyper-Speed-Relay': 'HyperRelay',
};
// Everything from 21 up is a structure rather than a hull, and lives under Stations/.
const isStation = folder => parseInt(folder, 10) >= 21;

const CIVS = ['Terran', 'Aquarii', 'Pyrothian', 'Cryithn', 'Sylvan'];

// _Extras is a two-level tree (group/unit) of faction-neutral props. Its groups map onto the
// destination folders the game will look in.
const EXTRA_DIRS = {
  '01-Asteroids': 'Asteroids', '02-Derelicts': 'Derelicts', '03-Artifacts': 'Artifacts',
  '04-Hostiles': 'Enemies',    '05-Machines': 'Ancients',
};

// ---- Args ------------------------------------------------------------------------------------
const argv = process.argv.slice(2);
const DRY = argv.includes('--dry');
const srcIdx = argv.indexOf('--src');
const SRC = srcIdx >= 0 ? argv[srcIdx + 1]
                        : 'C:/Users/lando/Downloads/4X-Ship-Models';
// fileURLToPath, not URL.pathname — the project lives under "Jacob Maddux", and pathname hands back
// the space percent-encoded, which then becomes a literal "%20" directory name.
const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DEST_ROOT = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets');

const io = new NodeIO();
await MeshoptSimplifier.ready;

/** Every file under a directory, recursively. */
function walk(dir, acc = []) {
  for (const f of fs.readdirSync(dir)) {
    const fp = path.join(dir, f);
    fs.statSync(fp).isDirectory() ? walk(fp, acc) : acc.push(fp);
  }
  return acc;
}

/** The .glb to use for a unit: the SMALLEST, because duplicates are re-exports of the same hull and
 *  the smaller one is invariably the one that was already cleaned up. */
function pickGlb(unitDir) {
  const glbs = walk(unitDir).filter(f => f.toLowerCase().endsWith('.glb'));
  if (!glbs.length) return null;
  return glbs.map(f => ({ f, size: fs.statSync(f).size })).sort((a, b) => a.size - b.size)[0].f;
}

/** Downscale every texture in the document, in place, using sharp directly. */
async function resizeTextures(doc) {
  let before = 0, after = 0;
  const slotOf = new Map();
  for (const mat of doc.getRoot().listMaterials()) {
    for (const [slot, max] of Object.entries(TEX)) {
      const getter = 'get' + slot.charAt(0).toUpperCase() + slot.slice(1);
      const tex = typeof mat[getter] === 'function' ? mat[getter]() : null;
      if (tex) slotOf.set(tex, Math.min(max, slotOf.get(tex) ?? Infinity));
    }
  }
  for (const tex of doc.getRoot().listTextures()) {
    const img = tex.getImage();
    if (!img) continue;
    before += img.byteLength;
    const max = slotOf.get(tex) ?? 512;
    try {
      const out = await sharp(Buffer.from(img))
        .resize(max, max, { fit: 'inside', withoutEnlargement: true })
        .jpeg({ quality: JPEG_QUALITY })
        .toBuffer();
      tex.setImage(new Uint8Array(out));
      tex.setMimeType('image/jpeg');
      after += out.byteLength;
    } catch (e) {
      after += img.byteLength;   // leave the original in place rather than lose the texture
      console.warn(`      ! texture resize failed (${e.message.split('\n')[0]}) — kept original`);
    }
  }
  return { before, after };
}

/** Axis lengths of the whole scene, so a hull that will fool the orientation heuristic is visible. */
function measure(doc) {
  let min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
  for (const mesh of doc.getRoot().listMeshes())
    for (const prim of mesh.listPrimitives()) {
      const pos = prim.getAttribute('POSITION');
      if (!pos) continue;
      const pmin = pos.getMin([]), pmax = pos.getMax([]);
      for (let i = 0; i < 3; i++) { min[i] = Math.min(min[i], pmin[i]); max[i] = Math.max(max[i], pmax[i]); }
    }
  if (!isFinite(min[0])) return null;
  return [max[0] - min[0], max[1] - min[1], max[2] - min[2]];
}

function triCount(doc) {
  let n = 0;
  for (const mesh of doc.getRoot().listMeshes())
    for (const prim of mesh.listPrimitives()) {
      const idx = prim.getIndices();
      n += idx ? idx.getCount() / 3 : (prim.getAttribute('POSITION')?.getCount() ?? 0) / 3;
    }
  return Math.round(n);
}

const report = [];

async function convert(srcGlb, destDir, name) {
  const destFile = path.join(destDir, name + '.glb');
  const srcSize = fs.statSync(srcGlb).size;

  const doc = await io.read(srcGlb);
  const trisBefore = triCount(doc);

  await doc.transform(
    weld(),
    simplify({ simplifier: MeshoptSimplifier, ratio: Math.min(1, TARGET_TRIS / Math.max(1, trisBefore)), error: SIMPLIFY_ERROR }),
  );
  const tex = await resizeTextures(doc);
  await doc.transform(dedup(), prune());

  const trisAfter = triCount(doc);
  const dims = measure(doc);

  if (!DRY) {
    fs.mkdirSync(destDir, { recursive: true });
    await io.write(destFile, doc);
  }
  const outSize = DRY ? 0 : fs.statSync(destFile).size;

  report.push({ name, dest: path.relative(PROJ, destFile), srcSize, outSize, trisBefore, trisAfter, dims });
  const mb = n => (n / 1048576).toFixed(2) + ' MB';
  console.log(`  ${name.padEnd(34)} ${String(trisBefore).padStart(9)} -> ${String(trisAfter).padStart(6)} tris   ` +
              `${mb(srcSize).padStart(9)} -> ${mb(outSize).padStart(8)}` +
              (dims ? `   dims ${dims.map(d => d.toFixed(2)).join(' x ')}` : ''));
}

// ---- Civilizations ---------------------------------------------------------------------------
for (const civ of CIVS) {
  const civDir = path.join(SRC, civ);
  if (!fs.existsSync(civDir)) { console.log(`\n${civ}: MISSING`); continue; }
  console.log(`\n=== ${civ} ===`);
  for (const folder of fs.readdirSync(civDir).sort()) {
    const unitDir = path.join(civDir, folder);
    if (!fs.statSync(unitDir).isDirectory()) continue;
    const hull = HULLS[folder];
    if (!hull) { console.log(`  ${folder}: unrecognised folder, skipped`); continue; }
    const glb = pickGlb(unitDir);
    if (!glb) { console.log(`  ${folder.padEnd(34)} NO .glb — skipped`); continue; }
    const destDir = path.join(DEST_ROOT, isStation(folder) ? 'Stations' : 'Ships', civ);
    await convert(glb, destDir, `${civ}_${hull}`);
  }
}

// ---- Neutral props ---------------------------------------------------------------------------
const extrasDir = path.join(SRC, '_Extras');
if (fs.existsSync(extrasDir)) {
  for (const group of fs.readdirSync(extrasDir).sort()) {
    const gDir = path.join(extrasDir, group);
    if (!fs.statSync(gDir).isDirectory()) continue;
    const dest = EXTRA_DIRS[group];
    if (!dest) { console.log(`\n_Extras/${group}: unrecognised group, skipped`); continue; }
    console.log(`\n=== ${dest} ===`);
    for (const unit of fs.readdirSync(gDir).sort()) {
      const uDir = path.join(gDir, unit);
      if (!fs.statSync(uDir).isDirectory()) continue;
      const glb = pickGlb(uDir);
      if (!glb) { console.log(`  ${unit.padEnd(34)} NO .glb — skipped`); continue; }
      // "01-Chondrite-Rubble-Pile" -> "Chondrite_Rubble_Pile"
      const name = unit.replace(/^\d+-/, '').replace(/-/g, '_');
      await convert(glb, path.join(DEST_ROOT, dest), name);
    }
  }
}

// ---- Summary ---------------------------------------------------------------------------------
const sum = (k) => report.reduce((t, r) => t + r[k], 0);
console.log(`\n${'='.repeat(70)}`);
console.log(`models written : ${report.length}`);
console.log(`source size    : ${(sum('srcSize') / 1073741824).toFixed(2)} GB`);
console.log(`project size   : ${(sum('outSize') / 1048576).toFixed(1)} MB`);
console.log(`triangles      : ${sum('trisBefore').toLocaleString()} -> ${sum('trisAfter').toLocaleString()}`);

// Flag anything whose shortest axis is not its height — the one shape the orientation heuristic
// gets wrong, and the reason a ship ends up flying on its side.
const odd = report.filter(r => r.dims && (r.dims[1] > r.dims[0] || r.dims[1] > r.dims[2]));
if (odd.length) {
  console.log(`\n${odd.length} model(s) are TALLER than they are wide or long — the bounds heuristic`);
  console.log(`will pick the wrong "up" for these. Add a line to ship-meshes.txt for each:`);
  for (const r of odd) console.log(`  ${r.name.padEnd(34)} dims ${r.dims.map(d => d.toFixed(2)).join(' x ')}`);
}

if (!DRY) {
  fs.writeFileSync(path.join(PROJ, 'tools', 'ship-import-report.json'), JSON.stringify(report, null, 2));
  console.log(`\nreport -> tools/ship-import-report.json`);
}
