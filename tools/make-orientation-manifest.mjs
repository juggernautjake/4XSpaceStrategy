// ============================================================================================
// A STARTER ORIENTATION MANIFEST, FROM MEASURED BOUNDS
//
//   node tools/make-orientation-manifest.mjs
//
// Writes ship-meshes.txt lines for every model the importer measured, so a fleet arrives with its
// orientation WRITTEN DOWN rather than guessed afresh at load.
//
// ---- WHY BOTHER, WHEN ShipMeshManifest ALREADY GUESSES ----------------------------------------
//
// The runtime heuristic is good and it is not consistent, which for a fleet is worse than being
// uniformly wrong. It picks the longest axis as the length — and these hulls measure things like
// 1.90 x 0.54 x 1.67, where the two longest axes are within ten percent of each other. A carrier at
// 1.83 x 0.46 x 1.90 tips the other way and gets oriented across its own beam while its escorts fly
// nose-first. One ship flying sideways in a formation reads as a bug in a way that a whole fleet
// facing the same odd direction does not.
//
// So the axis choice is made ONCE here, from the same numbers, and written to a file. Every hull then
// agrees, and any that is wrong is one line to fix with F10 rather than a code change.
//
// ---- WHAT THIS CANNOT KNOW --------------------------------------------------------------------
//
// BOW FROM STERN. Bounds are symmetric; nothing in a box says which end the engines are on. The
// runtime heuristic guesses from where the mass sits, which is right more often than not and is
// exactly the guess that produces a ship flying backwards when it is wrong.
//
// A backwards ship is obvious and is one `180` away from fixed, so this emits its best guess and says
// so per line. Do not read the numbers here as verified — they are a starting point that removes the
// INCONSISTENCY, not a substitute for looking at the fleet.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPORT = path.join(PROJ, 'tools', 'ship-import-report.json');
const OUT = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'Ships', 'ship-meshes.txt');

if (!fs.existsSync(REPORT)) { console.error('no import report — run tools/import-ship-models.mjs first'); process.exit(1); }
const report = JSON.parse(fs.readFileSync(REPORT, 'utf8'));

const AXES = ['X', 'Y', 'Z'];
const longest  = d => d.indexOf(Math.max(...d));
const shortest = d => d.indexOf(Math.min(...d));

/// Euler angles that bring the mesh's own (forward, up) onto the game's (+Z, +Y).
/// Only the axis SWAP is derived; the 180-degree bow/stern flip is not knowable here.
function rotationFor(dims) {
  const f = longest(dims), u = shortest(dims);
  // forward already +Z
  if (f === 2) return u === 1 ? [0, 0, 0] : [0, 0, 90];        // up is X -> roll it onto Y
  // forward is +X -> yaw it onto +Z
  if (f === 0) return u === 1 ? [0, -90, 0] : [0, -90, 90];
  // forward is +Y (a tower, not a hull) -> pitch it down onto +Z
  return [90, 0, 0];
}

const lines = [];
const notes = [];
for (const r of report) {
  if (!r.dims) continue;
  const [p, y, ro] = rotationFor(r.dims);
  const f = longest(r.dims), u = shortest(r.dims);
  const d = r.dims.map(n => n.toFixed(2)).join(' x ');
  // Ambiguous when the two longest axes are close: the axis pick could flip on a re-import.
  const sorted = [...r.dims].sort((a, b) => b - a);
  const tight = sorted[0] > 0 && (sorted[0] - sorted[1]) / sorted[0] < 0.15;
  lines.push({ name: r.name, rot: [p, y, ro], note: `len ${AXES[f]}, up ${AXES[u]}, dims ${d}${tight ? '  AMBIGUOUS' : ''}` });
  if (tight) notes.push(r.name);
}

const header = `# ============================================================================================
# SHIP MESH ORIENTATION
#
# One line per mesh: <name> <pitch> <yaw> <roll> [scale] [spin|nospin]
# Edited and reloaded while the game runs (F10) — no recompile.
#
# The game's convention is Unity's: +Z is forward (the bow), +Y is up (the dorsal surface).
#
#   flying sideways?      try  0 90 0   or  0 -90 0
#   flying belly-first?   try  -90 0 0
#   flying backwards?     add  180 to the yaw
#
# ---- GENERATED, AND ONLY HALF-TRUSTED ------------------------------------------------------
#
# Everything below the legacy block was written by tools/make-orientation-manifest.mjs from the
# bounds the importer measured. It fixes the AXIS choice so the whole fleet agrees — the runtime
# heuristic decides per mesh, and these hulls measure things like 1.90 x 0.54 x 1.67 where the two
# longest axes are within ten percent, so a carrier at 1.83 x 0.46 x 1.90 tips the other way and
# flies across its own beam while its escorts fly nose-first.
#
# It CANNOT know bow from stern: a bounding box is symmetric and says nothing about which end the
# engines are on. A ship flying backwards is one "+180 yaw" away from fixed. Lines marked AMBIGUOUS
# are the ones whose two longest axes are close enough that the axis pick is a coin toss — check
# those first.
# ============================================================================================

# ---- The three meshes that shipped with the project ----------------------------------------
LP Colony Ship              -90 0 0
LP Science Ship               0 90 0

# ---- Generated fleet -----------------------------------------------------------------------
`;

const body = lines
  .sort((a, b) => a.name.localeCompare(b.name))
  .map(l => `${l.name.padEnd(30)} ${String(l.rot[0]).padStart(4)} ${String(l.rot[1]).padStart(4)} ${String(l.rot[2]).padStart(4)}    # ${l.note}`)
  .join('\n');

fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, header + body + '\n');

console.log(`${lines.length} orientation lines -> ${path.relative(PROJ, OUT)}`);
if (notes.length) console.log(`${notes.length} AMBIGUOUS (check these first): ${notes.join(', ')}`);
