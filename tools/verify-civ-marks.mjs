// ============================================================================================
// DOES EVERY EMPIRE CREST THE GAME WILL ASK FOR ACTUALLY EXIST?
//
//   node tools/verify-civ-marks.mjs
//
// `CivEmblem.Mask` builds a Resources path from the `Symbols` array:
//
//     SpaceAssets/Symbols/Symbol_{Name}
//
// and `Resources.Load<Texture2D>` returns **null** for a name that is not there. Null is not an
// exception: `Build` returns null, `CivMarkBadge.Attach` returns null, and the badge simply never
// appears. A crest renamed by one character therefore takes the mark off every colony marker, every
// system marker and every hull in the game, and the only symptom is an absence.
//
// This is the same failure the command icons had — loaded by NAME, wrong by one character, silent —
// and `tools/verify-command-ui.mjs` exists because of it. Same rule, different directory.
//
// BOTH DIRECTIONS, for the same reason that one does: a symbol the C# asks for and the folder does
// not have is a missing mark, and a file in the folder the C# never asks for is either a typo in the
// array or a crest nobody can pick. The second is much easier to create than to notice.
// ============================================================================================
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = f => fs.readFileSync(path.join(PROJ, f), 'utf8');

const SRC = read('Assets/Scripts/Visual/CivEmblem.cs');

// ---- the array and the prefix, read from the source so they cannot drift ------------------------
const arr = /public static readonly string\[\] Symbols\s*=\s*\{([\s\S]*?)\}/.exec(SRC);
if (!arr) { console.error('FAIL  could not read CivEmblem.Symbols'); process.exit(1); }
const SYMBOLS = [...arr[1].matchAll(/"([^"]+)"/g)].map(m => m[1]);

const pre = /const string ResourceDir = "([^"]+)"/.exec(SRC);
if (!pre) { console.error('FAIL  could not read CivEmblem.ResourceDir'); process.exit(1); }
const PREFIX = pre[1];                       // e.g. SpaceAssets/Symbols/Symbol_

const dir = path.posix.dirname(PREFIX);      // SpaceAssets/Symbols
const stem = path.posix.basename(PREFIX);    // Symbol_
const RES = path.join(PROJ, 'Assets', 'Resources', ...dir.split('/'));

console.log(`CivEmblem.Symbols: ${SYMBOLS.length} marks, loaded from Resources/${dir}/${stem}*`);

let bad = 0;
const check = (ok, msg) => { console.log(`${ok ? 'ok   ' : 'FAIL '} ${msg}`); if (!ok) bad++; };

if (!fs.existsSync(RES)) {
  console.error(`FAIL  the symbol directory does not exist: ${path.relative(PROJ, RES)}`);
  process.exit(1);
}

// Resources.Load takes a path with NO extension and resolves whatever importable file is there, so
// the check has to match on stem rather than on a fixed ".png".
const onDisk = fs.readdirSync(RES)
  .filter(f => !f.endsWith('.meta'))
  .map(f => f.replace(/\.[^.]+$/, ''));

const missing = SYMBOLS.filter(s => !onDisk.includes(stem + s));
const unused = onDisk.filter(f => f.startsWith(stem) && !SYMBOLS.includes(f.slice(stem.length)));

if (missing.length) console.log(`  asked for, not on disk: ${missing.join(', ')}`);
if (unused.length) console.log(`  on disk, never asked for: ${unused.join(', ')}`);

check(missing.length === 0,
  `every symbol in CivEmblem.Symbols has a file (${SYMBOLS.length - missing.length}/${SYMBOLS.length})`);
check(unused.length === 0,
  `every ${stem}* file on disk is reachable from CivEmblem.Symbols`);
check(new Set(SYMBOLS).size === SYMBOLS.length,
  'no symbol name appears twice in the array');

// ---- the badge is wired to something ------------------------------------------------------------
//
// The crest existing is half the claim. B12 asked for it ON WORLDS, and a CivMarkBadge that nothing
// calls is exactly as invisible as a missing PNG — with the added trap that it looks built.
const BADGE = 'Assets/Scripts/Visual/CivMarkBadge.cs';
check(fs.existsSync(path.join(PROJ, BADGE)), 'CivMarkBadge.cs exists');

const callers = ['Assets/Scripts/Visual/OrbitController.cs', 'Assets/Scripts/Visual/GalaxyLOD.cs'];
for (const c of callers) {
  check(read(c).includes('CivMarkBadge'),
    `${path.basename(c)} attaches a badge — the mark reaches the map, not just the hulls`);
}

// The player's mark must never be drawn over a rival's ground. Both attach sites gate on the player.
check(/sys\.owner == FactionManager\.Player/.test(read('Assets/Scripts/Visual/GalaxyLOD.cs')),
  'the galaxy map badges only systems the PLAYER holds (rival marks are B13, not yet built)');
check(/bool mine/.test(read('Assets/Scripts/Visual/OrbitController.cs')),
  'the owner ring takes ownership explicitly rather than inferring it from the ring colour');

process.exit(bad ? 1 : 0);
