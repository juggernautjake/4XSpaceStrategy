// ============================================================================================
// DOES EVERY MESH THE GAME WILL ASK FOR ACTUALLY EXIST?
//
//   node tools/verify-wiring.mjs
//
// UnitModelLibrary.CivPath builds a Resources path from a unit's civilization and its class:
//
//     SpaceAssets/{Ships|Stations}/{Civ}/{Civ}_{UnitType}
//
// If that file is not there, Resources.Load returns null and the ship silently falls back to a
// borrowed hull. Silently is the problem — a typo in a folder name, a hull whose enum is `Miner` but
// whose art got written as `MiningBarge`, an importer that skipped a unit, all look identical in
// game: the ship just keeps flying the old mesh and nobody notices for a week.
//
// So this reconstructs exactly the paths the C# will build — from the UnitType enum itself, not from
// a list typed out here — and reports which resolve and which do not.
//
// It CANNOT tell you the mesh imports correctly in Unity. .glb needs com.unity.cloud.gltfast, and
// whether that package resolves and produces a loadable GameObject is a question only the editor can
// answer. This checks the half that is checkable: naming and presence.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const RES = path.join(PROJ, 'Assets', 'Resources');

// ---- the enum, read from the source so it cannot drift ----------------------------------------
const src = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Data/UnitType.cs'), 'utf8');
// Comments FIRST. The enum body is heavily annotated and several comments contain capitalised words
// after a comma ("Terraformer, Probe (indices 0-8, do not reorder)"), which a naive scan reads as two
// extra members — giving 31 classes where there are 29, and reporting phantom missing meshes.
const enumStart = src.indexOf('enum UnitType');
const enumBody = src.slice(enumStart, src.indexOf('}', enumStart))
  .replace(/\/\/[^\n]*/g, '')
  .replace(/\/\*[\s\S]*?\*\//g, '');
const TYPES = [...new Set(
  enumBody.split(/[\n,]/).map(s => s.trim()).filter(s => /^[A-Z]\w*$/.test(s))
)].filter(t => t !== 'UnitType');

// Which classes are stations — also read from source, so the Ships/ vs Stations/ split matches C#.
const dbSrc = fs.readFileSync(path.join(PROJ, 'Assets/Scripts/Data/UnitType.cs'), 'utf8');
const STATIONS = new Set([...dbSrc.matchAll(/(\w+)\.isStation\s*=\s*true/g)].map(m => {
  // `battle.isStation = true;` -> find the local's UnitType from its construction line
  const local = m[1];
  const re = new RegExp(`var\\s+${local}\\s*=\\s*new UnitInfo\\(UnitType\\.(\\w+)`);
  const hit = re.exec(dbSrc);
  return hit ? hit[1] : null;
}).filter(Boolean));

const CIVS = ['Terran', 'Aquarii', 'Pyrothian', 'Cryithn', 'Sylvan'];
// Meshes Unity can load. .glb only counts if gltfast is in the manifest.
const manifest = JSON.parse(fs.readFileSync(path.join(PROJ, 'Packages/manifest.json'), 'utf8'));
const hasGltf = !!manifest.dependencies['com.unity.cloud.gltfast'];
const EXT = ['.fbx', '.obj', ...(hasGltf ? ['.glb', '.gltf'] : [])];

// The four classes a new game starts with (see the UnitType comment).
const STARTERS = ['Scout', 'ResearchShip', 'Fighter', 'ColonyShip'];

function resolves(rel) {
  return EXT.some(e => fs.existsSync(path.join(RES, rel + e)));
}

console.log(`${TYPES.length} unit classes, ${CIVS.length} civilizations`);
console.log(`gltfast in manifest: ${hasGltf ? 'yes — .glb counts' : 'NO — .glb will not load'}`);
console.log(`stations detected  : ${STATIONS.size}\n`);

let anyStarterMissing = false;
const per = {};

for (const civ of CIVS) {
  const present = [], missing = [];
  for (const t of TYPES) {
    const folder = STATIONS.has(t) ? 'Stations' : 'Ships';
    const rel = `SpaceAssets/${folder}/${civ}/${civ}_${t}`;
    (resolves(rel) ? present : missing).push(t);
  }
  per[civ] = { present, missing };

  const starters = STARTERS.map(t => {
    const folder = STATIONS.has(t) ? 'Stations' : 'Ships';
    return { t, ok: resolves(`SpaceAssets/${folder}/${civ}/${civ}_${t}`) };
  });
  // A civilization with NO art at all is not broken — every class falls back to a shared hull, which
  // is the designed behaviour while the fleet lands one civ at a time. What would be broken is a civ
  // that has art for most of its roster but is missing the four ships a new game hands you.
  const started = present.length > 0;
  const starterLine = started
    ? starters.map(s => `${s.ok ? 'ok' : 'MISSING'} ${s.t}`).join('   ')
    : '(no art yet — flies borrowed hulls)';
  if (started && starters.some(s => !s.ok)) anyStarterMissing = true;

  console.log(`${civ.padEnd(10)} ${String(present.length).padStart(2)}/${TYPES.length} meshes`);
  console.log(`  starting four: ${starterLine}`);
  if (missing.length && missing.length < TYPES.length)
    console.log(`  missing: ${missing.join(', ')}`);
  console.log();
}

// ---- the legacy fallbacks, which everything without art still leans on -------------------------
const LEGACY = [
  'SpaceAssets/Ships/LP Colony Ship',
  'SpaceAssets/Ships/LP Science Ship',
  'SpaceAssets/Stations/LP Space Station',
];
console.log('legacy fallback meshes:');
for (const l of LEGACY) console.log(`  ${resolves(l) ? 'ok     ' : 'MISSING'} ${l}`);

// ---- orientation manifest coverage -------------------------------------------------------------
const manifestPath = path.join(RES, 'SpaceAssets/Ships/ship-meshes.txt');
if (fs.existsSync(manifestPath)) {
  const named = new Set(fs.readFileSync(manifestPath, 'utf8').split('\n')
    .filter(l => l.trim() && !l.trim().startsWith('#'))
    .map(l => {
      const tok = l.trim().split(/\s+/);
      let i = 1; while (i < tok.length && isNaN(parseFloat(tok[i]))) i++;
      return tok.slice(0, i).join(' ');
    }));
  const withArt = [];
  for (const civ of CIVS) for (const t of per[civ].present) withArt.push(`${civ}_${t}`);
  const unoriented = withArt.filter(n => !named.has(n));
  console.log(`\norientation manifest: ${named.size} entries, ${withArt.length} meshes with art`);
  if (unoriented.length)
    console.log(`  ${unoriented.length} rely on the bounds heuristic: ${unoriented.join(', ')}`);

  // ---- ORPHANED LINES ------------------------------------------------------------------------
  //
  // The check above catches a mesh with NO line, which flies on the bounds heuristic and may come out
  // backwards. This catches the other direction: a line naming a mesh that is not there.
  //
  // That one is worse, because it is silent in both places. The manifest loader skips a name it never
  // sees, and the fleet renderer never asks for a mesh that does not exist, so an orphan sits in the
  // file looking like the hull is handled. It is exactly what a rename or a reorganisation produces —
  // and the art library was just reorganised — so it is worth a line of output rather than a shrug.
  const legacyNames = new Set(LEGACY.map(l => l.split('/').pop()));
  const orphans = [...named].filter(n => !legacyNames.has(n) && !withArt.includes(n));
  if (orphans.length)
    console.log(`  ${orphans.length} ORPHANED line(s) naming a mesh that is not on disk: ${orphans.join(', ')}`);
  else
    console.log('  every line matches a mesh, and every mesh has a line');
}

// ---- SHIPS UNDER Ships/, STATIONS UNDER Stations/ ----------------------------------------------
//
// UnitModelLibrary.CivPath picks the folder from UnitInfo.isStation and looks in exactly one place. A
// station mesh filed under Ships/ is therefore not "in the wrong folder but findable" — it is INVISIBLE,
// and that hull silently falls back to a borrowed placeholder while every file-count check in this
// script still reports it present.
{
  const misfiled = [];
  for (const civ of CIVS) {
    for (const sub of ['Ships', 'Stations']) {
      const dir = path.join(RES, 'SpaceAssets', sub, civ);
      if (!fs.existsSync(dir)) continue;
      for (const f of fs.readdirSync(dir)) {
        if (!f.endsWith('.glb')) continue;
        const type = f.replace(/\.glb$/, '').replace(`${civ}_`, '');
        const wantStation = STATIONS.has(type);
        const want = wantStation ? 'Stations' : 'Ships';
        if (want !== sub) misfiled.push(`${sub}/${civ}/${f} should be under ${want}/`);
      }
    }
  }
  console.log(`\nfolder placement: ${misfiled.length ? misfiled.length + ' MISFILED' : 'every mesh is in the folder its class is looked up in'}`);
  for (const m of misfiled) console.log(`  ${m}`);
}

console.log(anyStarterMissing
  ? '\nFAIL — at least one civilization is missing a starting ship mesh.'
  : '\nAll starting ships resolve for every civilization that has art.');
process.exit(anyStarterMissing ? 1 : 0);
