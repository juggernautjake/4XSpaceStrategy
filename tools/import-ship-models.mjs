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

import { NodeIO, PropertyType } from '@gltf-transform/core';
import { weld, simplify, prune, dedup } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// ============================================================================================
// THE BLOBBY-SHIP PROBLEM, AND WHY ONE MESH COULD NEVER SOLVE IT
//
// The old settings were 12,000 triangles, a 512 base colour and a 256 normal map, justified by
// "ships are drawn at BETWEEN 0.09 AND 0.40 WORLD UNITS" and "four thousand pixels of texture on
// forty pixels of ship is about a hundredfold waste."
//
// Every word of that is true of the view it was written against, and it has one enormous hole:
// CameraController.minHeight is 0.04 WORLD UNITS. The camera can get closer to a hull than the hull
// is long. tools/inspect-ship-lod.mjs measures what that means at 1080p:
//
//     camera height        dreadnought on screen     512 base colour
//     framing a system              9 px             texture to spare
//     close orbit                  94 px             texture to spare
//     very close                  374 px             about right
//     free-look floor           1,069 px             magnified 2.1x  — BLURRY
//     absolute floor            9,353 px             magnified 18x   — BLURRY
//
// So the ship is not blobby because 12,000 triangles is too few for a 40-pixel ship. It is blobby
// because the same asset is ALSO being asked to be a 1,000-pixel ship, and at that size a 512-pixel
// texture is being magnified past two texels per pixel while a 256 normal map — which is where
// essentially all the surface detail lives — is carrying a quarter of what the view asks for.
//
// One asset cannot serve both ends of a 250:1 range of screen sizes. That is what LOD is for.
//
// ---- WHAT IS EMITTED NOW ---------------------------------------------------------------------
//
//   name_hi.glb    high geometry, NO textures     used when the hull is big on screen
//   name.glb       mid geometry, ALL the textures the fallback, and where the pixels live
//   name_lo.glb    low geometry, NO textures      used when it is a speck
//
// The textures live on the MID file rather than on the high one, and that is the load-bearing
// decision in this whole layout. It means the base file is SELF-SUFFICIENT: a civilisation that has
// not been re-imported, or a hull whose LOD siblings failed to write, still loads and still looks
// exactly as it does today. The LOD levels are a pure addition that can be absent. UnitModelRenderer
// hands the base file's materials to the other two at load time, so all three levels share one
// texture set and there is no resolution pop when they swap.
//
// ---- WHY THE ERROR TOLERANCE CAME DOWN --------------------------------------------------------
//
// SIMPLIFY_ERROR was 0.02 — a vertex could move two percent of the mesh's size. On a hull that is
// most of a metre of allowed drift on a shape whose panel lines are millimetres, and it is why
// silhouettes came out soft even at triangle counts that should have held them. The simplifier hits
// the triangle ratio first in most cases, so this mostly costs nothing; where it bites, it bites
// exactly on the hulls that were being rounded off.
// ============================================================================================

/// The three levels. `tris` is the triangle ceiling and `error` the tolerance for that level; only
/// the level with `textures` set carries any.
const LODS = [
  { suffix: '_hi', tris: 24000, error: 0.004, textures: false },
  { suffix: '',    tris:  9000, error: 0.010, textures: true  },
  { suffix: '_lo', tris:  2200, error: 0.040, textures: false },
];

/// Texture sizes for the one set that all three levels share.
///
/// 1024 on base colour and normal, which is what a hull filling most of a 1080p screen actually asks
/// for — going to 2048 would double the bytes to buy detail only visible below the free-look floor,
/// which is closer than anybody inspects a ship. Metallic-roughness stays at half: it carries broad
/// material zones rather than fine detail, and nobody has ever noticed a soft roughness map.
const TEX = {
  baseColorTexture:         1024,
  normalTexture:            1024,
  metallicRoughnessTexture:  512,
  emissiveTexture:           512,
  occlusionTexture:          512,
};

/// Base colour and the rest. Normals get their own, higher quality — a normal map is a VECTOR FIELD
/// stored as colour, so JPEG's chroma subsampling does not merely soften it, it tilts the normals and
/// the surface picks up a faint quilted shimmer under a moving light. 82 is fine for albedo and
/// visibly wrong for normals.
const JPEG_QUALITY = 84;
const NORMAL_JPEG_QUALITY = 95;

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
const PROJ_FOR_SRC = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DRY = argv.includes('--dry');
const srcIdx = argv.indexOf('--src');
// Defaults to Art/Active — the one folder holding the models the game is built from. It used to
// default to a path under Downloads, which meant the source of the shipped art was a folder outside
// the project that nobody else could see or check into anything.
const SRC = srcIdx >= 0 ? argv[srcIdx + 1] : path.join(PROJ_FOR_SRC, 'Art', 'Active');
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
  const isNormal = new Set();
  for (const mat of doc.getRoot().listMaterials()) {
    for (const [slot, max] of Object.entries(TEX)) {
      const getter = 'get' + slot.charAt(0).toUpperCase() + slot.slice(1);
      const tex = typeof mat[getter] === 'function' ? mat[getter]() : null;
      if (!tex) continue;
      slotOf.set(tex, Math.min(max, slotOf.get(tex) ?? Infinity));
      if (slot === 'normalTexture') isNormal.add(tex);
    }
  }
  for (const tex of doc.getRoot().listTextures()) {
    const img = tex.getImage();
    if (!img) continue;
    before += img.byteLength;
    const max = slotOf.get(tex) ?? 512;
    const quality = isNormal.has(tex) ? NORMAL_JPEG_QUALITY : JPEG_QUALITY;
    try {
      const out = await sharp(Buffer.from(img))
        .resize(max, max, { fit: 'inside', withoutEnlargement: true })
        .jpeg({ quality, chromaSubsampling: isNormal.has(tex) ? '4:4:4' : '4:2:0' })
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

/**
 * Remove every texture from a document, leaving the materials and geometry intact.
 *
 * For the LOD levels that are not the base one. They share the base file's material at load time, so
 * carrying their own copy of a 1024 normal map would triple the texture memory of the entire fleet to
 * store three identical images — and would reintroduce the resolution pop that sharing exists to
 * avoid, since each level's copy would be at that level's size.
 */
function stripTextures(doc) {
  const slots = ['BaseColorTexture', 'NormalTexture', 'MetallicRoughnessTexture',
                 'EmissiveTexture', 'OcclusionTexture'];
  for (const mat of doc.getRoot().listMaterials())
    for (const s of slots) {
      const setter = 'set' + s;
      if (typeof mat[setter] === 'function') mat[setter](null);
    }
  // prune() in the caller then disposes the now-unreferenced textures and their image buffers.
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

/**
 * Import one source hull as a three-level LOD chain.
 *
 * Each level is built from a FRESH READ of the source rather than by simplifying the previous level
 * further. Chained simplification is faster and compounds its error: the low level would inherit
 * every approximation the high level made and then approximate that, so the cheapest mesh — the one
 * drawn most often, on every distant ship in the system — would be the worst-served. Reading again
 * costs seconds in an offline tool and buys three levels that are each the best answer for their own
 * budget.
 */
async function convert(srcGlb, destDir, name) {
  const srcSize = fs.statSync(srcGlb).size;
  let trisBefore = 0, outTotal = 0, dims = null;
  const levels = [];

  for (const lod of LODS) {
    const destFile = path.join(destDir, name + lod.suffix + '.glb');

    const doc = await io.read(srcGlb);
    if (!trisBefore) trisBefore = triCount(doc);

    await doc.transform(
      weld(),
      simplify({
        simplifier: MeshoptSimplifier,
        ratio: Math.min(1, lod.tris / Math.max(1, trisBefore)),
        error: lod.error,
      }),
    );

    // ---- ORDER MATTERS HERE, AND GETTING IT WRONG COST THE TEXTURES ----
    //
    // The first version stripped textures and then ran a full prune(), which cleaned up the orphaned
    // images AND, entirely reasonably, the TEXCOORD_0 accessor that nothing referenced any more. So
    // the high-detail mesh shipped with no UVs at all — and since it adopts the base file's textured
    // material at load time, every one of its vertices would have sampled the same texel. The most
    // detailed hull in the game would have rendered as a flat, single-coloured blob: the exact defect
    // this whole change exists to fix, introduced by the change itself.
    //
    // So the geometry is cleaned FIRST, while the textures are still attached and the UVs are still
    // referenced, and the textures are dropped afterwards with a prune restricted to texture-shaped
    // properties. Accessors are never in scope for that pass and cannot be collected.
    await doc.transform(dedup(), prune());

    if (lod.textures) await resizeTextures(doc);
    else
    {
      stripTextures(doc);
      await doc.transform(prune({
        propertyTypes: [PropertyType.TEXTURE, PropertyType.TEXTURE_INFO],
      }));
    }

    const tris = triCount(doc);
    if (lod.textures) dims = measure(doc);

    if (!DRY) {
      fs.mkdirSync(destDir, { recursive: true });
      await io.write(destFile, doc);
    }
    const size = DRY ? 0 : fs.statSync(destFile).size;
    outTotal += size;
    levels.push({ suffix: lod.suffix || '(base)', tris, size });
  }

  report.push({ name, dest: path.relative(PROJ, path.join(destDir, name + '.glb')),
                srcSize, outSize: outTotal, trisBefore, trisAfter: levels[1].tris, dims, levels });

  const mb = n => (n / 1048576).toFixed(2) + ' MB';
  console.log(`  ${name.padEnd(30)} ${String(trisBefore).padStart(8)} src  ->  ` +
              levels.map(l => `${l.suffix} ${String(l.tris).padStart(5)}`).join('  ') +
              `   ${mb(srcSize)} -> ${mb(outTotal)}` +
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
