// ============================================================================================
// WHICH TERRAIN TYPES ARE ACTUALLY BALANCED, AND WHICH JUST FELL THROUGH TO `default`
//
//   node tools/audit-terrain-balance.mjs
//
// Forty-one terrain types are scored across several yield tables in SurfaceIndex.cs — minerals, crust
// heat, fertility, shelter — and each table is a `switch` with a `default`. A type nobody remembered
// to list does not error; it silently takes the default and becomes indistinguishable from every
// other forgotten type. That is exactly what unbalanced looks like from inside the game: a swamp and
// an obsidian flat quietly yielding the same thing.
//
// So this reads the switch tables out of the source and reports, per terrain type, which ones named
// it and which let it fall through. It also flags the reverse — art with no enum, and enum with no
// art — because a terrain that generates with no tile is a missing texture at runtime.
//
// Not a compiler and not a balance opinion. It finds HOLES; whether a value is right is a judgement
// for whoever fills them.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = p => fs.readFileSync(path.join(PROJ, p), 'utf8');

// ---- the enum ---------------------------------------------------------------------------------
const terrainSrc = read('Assets/Scripts/Data/TerrainTypes.cs');
const enumBody = terrainSrc.slice(terrainSrc.indexOf('enum TerrainType'));
const TYPES = [...enumBody.slice(0, enumBody.indexOf('}')).matchAll(/^\s{4}([A-Z]\w*),?\s*$/gm)].map(m => m[1]);

// ---- the tables -------------------------------------------------------------------------------
// Each entry: the function that scores terrain, and what a missing type means for the player.
const TABLES = [
  { fn: 'BiomeMineral',  file: 'Assets/Scripts/Data/SurfaceIndex.cs', means: 'ore yield' },
  { fn: 'CrustHeat',     file: 'Assets/Scripts/Data/SurfaceIndex.cs', means: 'geothermal yield' },
  { fn: 'BiomeFertile',  file: 'Assets/Scripts/Data/SurfaceIndex.cs', means: 'food yield' },
  { fn: 'Shelter',       file: 'Assets/Scripts/Data/SurfaceIndex.cs', means: 'wind exposure' },
];

/// The body of a static function, from its signature to the matching closing brace.
function functionBody(src, name) {
  const sig = new RegExp(`static\\s+float\\s+${name}\\s*\\(`);
  const m = sig.exec(src);
  if (!m) return null;
  let i = src.indexOf('{', m.index);
  if (i < 0) return null;
  let depth = 0, start = i;
  for (; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}') { depth--; if (depth === 0) return src.slice(start, i + 1); }
  }
  return null;
}

const covered = {};   // table -> Set of terrain named
for (const t of TABLES) {
  const body = functionBody(read(t.file), t.fn);
  if (!body) { console.log(`(could not find ${t.fn} in ${t.file})`); covered[t.fn] = new Set(); continue; }
  covered[t.fn] = new Set([...body.matchAll(/TerrainType\.(\w+)/g)].map(m => m[1]));
}

// ---- art coverage -----------------------------------------------------------------------------
const artDir = path.join(PROJ, 'Assets/Resources/SpaceAssets/Biomes');
const art = new Set(fs.readdirSync(artDir).filter(f => f.endsWith('.png'))
  .map(f => f.replace(/_16x16\.png$/, '').toLowerCase()));

const mapSrc = read('Assets/Scripts/Visual/TerrainTextureMap.cs');
const tileFor = {};
for (const m of mapSrc.matchAll(/case\s+TerrainType\.(\w+):\s*return\s+"([^"]+)"/g)) tileFor[m[1]] = m[2];

// ---- report -----------------------------------------------------------------------------------
const W = 16;
console.log(`${TYPES.length} terrain types\n`);
console.log('type'.padEnd(W) + TABLES.map(t => t.fn.slice(0, 12).padEnd(14)).join('') + 'tile');
console.log('-'.repeat(W + TABLES.length * 14 + 18));

const gaps = [];
for (const ty of TYPES) {
  const cells = TABLES.map(t => (covered[t.fn].has(ty) ? '  set' : '  DEFAULT').padEnd(14));
  const tile = tileFor[ty];
  const tileState = !tile ? 'NO MAPPING' : art.has(tile.toLowerCase()) ? tile : `MISSING ART (${tile})`;
  const missing = TABLES.filter(t => !covered[t.fn].has(ty)).map(t => t.means);
  if (missing.length || !tile || (tile && !art.has(tile.toLowerCase()))) {
    gaps.push({ ty, missing, tileState });
  }
  console.log(ty.padEnd(W) + cells.join('') + tileState);
}

console.log('\n' + '='.repeat(70));
const fully = TYPES.filter(ty => TABLES.every(t => covered[t.fn].has(ty)));
console.log(`fully scored in every table : ${fully.length} / ${TYPES.length}`);
for (const t of TABLES) {
  const n = TYPES.filter(ty => covered[t.fn].has(ty)).length;
  console.log(`  ${t.fn.padEnd(14)} names ${String(n).padStart(2)} / ${TYPES.length}   (rest take the default ${t.means})`);
}

const noArt = TYPES.filter(ty => !tileFor[ty] || !art.has(tileFor[ty].toLowerCase()));
if (noArt.length) console.log(`\nterrain with no usable tile: ${noArt.join(', ')}`);

const usedTiles = new Set(Object.values(tileFor).map(s => s.toLowerCase()));
const orphanArt = [...art].filter(a => !usedTiles.has(a));
if (orphanArt.length) console.log(`art never referenced by any terrain: ${orphanArt.join(', ')}`);
