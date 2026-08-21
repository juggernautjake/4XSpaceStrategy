// ============================================================================================
// DOES EVERY CONTROL HAVE A SYMBOL, AND DOES EVERY SYMBOL EXIST?
//
//   node tools/verify-command-ui.mjs
//
// Icons are loaded by NAME at runtime, out of Resources. A name that is wrong by one character does
// not throw, does not warn, and does not draw — Resources.Load returns null and the button comes up
// blank. That is the worst kind of failure this project can have: it is invisible in source review,
// invisible in the structural check, and only visible in the game, on the one control nobody happened
// to look at.
//
// So this reconstructs the paths the C# will build, from the ENUMS themselves rather than from a list
// typed here, and reports which resolve. The same reasoning as tools/verify-wiring.mjs, which does it
// for ship meshes.
//
// It also checks the half that is about the player rather than the files: every control must carry a
// tooltip. A twenty-five-icon bar where one icon is a mystery is a bar with a mystery in it, and the
// tooltip is the only place the answer can live.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (p) => fs.readFileSync(path.join(PROJ, p), 'utf8');

const problems = [];
const notes = [];

// ---- the enums, read from source ---------------------------------------------------------------
function enumMembers(src, name) {
  const at = src.indexOf(`enum ${name}`);
  if (at < 0) return [];
  const open = src.indexOf('{', at);
  const close = src.indexOf('}', open);
  return src.slice(open + 1, close)
    .replace(/\/\/[^\n]*/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .split(',')
    .map(s => s.trim())
    .filter(s => /^[A-Z]\w*$/.test(s));
}

const squadSrc = read('Assets/Scripts/Systems/Squadrons.cs');
const formSrc = read('Assets/Scripts/Visual/FleetFormation.cs');
const indexSrc = read('Assets/Scripts/Data/SurfaceIndex.cs');
const barSrc = read('Assets/Scripts/UI/FleetCommandBar.cs');

// Both enums live in Squadrons.cs. formSrc is read for FleetFormation's own helpers, not the enum —
// looking for the enum there found nothing and reported zero formations, cheerfully, as a pass.
const FORMATIONS = enumMembers(squadSrc, 'FleetFormationKind');
const PROTOCOLS = enumMembers(squadSrc, 'SquadronProtocol');
const INDEXES = enumMembers(indexSrc, 'SurfaceIndexKind').filter(k => k !== 'None');

const CMD_DIR = 'Assets/Resources/SpaceAssets/CommandIcons';
const IDX_DIR = 'Assets/Resources/SpaceAssets/IndexIcons';

const has = (dir, file) => fs.existsSync(path.join(PROJ, dir, file));

// ---- 1. every enum value is mapped to an icon, and that icon exists ----------------------------
function mappedIcon(fnName, member) {
  // Matches the switch-expression arms: `FleetFormationKind.Screen => "Form_Screen",` and the
  // `_ => "Form_Wedge",` default.
  const fn = new RegExp(`static string ${fnName}\\([^)]*\\)[\\s\\S]*?\\n    \\};`).exec(barSrc);
  if (!fn) return null;
  const body = fn[0];
  const direct = new RegExp(`\\.${member}\\s*=>\\s*"([^"]+)"`).exec(body);
  if (direct) return direct[1];
  const fallback = /_\s*=>\s*"([^"]+)"/.exec(body);
  return fallback ? fallback[1] : null;
}

console.log(`formations ${FORMATIONS.length}, protocols ${PROTOCOLS.length}, indexes ${INDEXES.length}\n`);

console.log('formation icons');
for (const f of FORMATIONS) {
  const icon = mappedIcon('FormIcon', f);
  const ok = icon && has(CMD_DIR, `Cmd_${icon}.png`);
  if (!ok) problems.push(`FleetFormationKind.${f} -> ${icon ?? 'NO MAPPING'} (missing)`);
  console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${f.padEnd(14)} ${icon ?? '—'}`);
}

console.log('\nprotocol icons');
for (const p of PROTOCOLS) {
  const icon = mappedIcon('ProtIcon', p);
  const ok = icon && has(CMD_DIR, `Cmd_${icon}.png`);
  if (!ok) problems.push(`SquadronProtocol.${p} -> ${icon ?? 'NO MAPPING'} (missing)`);
  console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${p.padEnd(16)} ${icon ?? '—'}`);
}

console.log('\nindex icons');
for (const k of INDEXES) {
  const ok = has(IDX_DIR, `Index_${k}.png`);
  if (!ok) problems.push(`SurfaceIndexKind.${k} has no Index_${k}.png`);
  console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${k}`);
}

// ---- 2. every icon NAMED anywhere in C# exists -------------------------------------------------
//
// Catches the reverse direction: a control that names an icon nobody generated. Fed by the literal
// strings, because that is exactly what Resources.Load will be handed.
{
  const files = [];
  const walk = (dir) => {
    for (const e of fs.readdirSync(path.join(PROJ, dir), { withFileTypes: true })) {
      if (e.isDirectory()) walk(path.join(dir, e.name));
      else if (e.name.endsWith('.cs')) files.push(path.join(dir, e.name));
    }
  };
  walk('Assets/Scripts');

  const named = new Map();
  for (const f of files) {
    const src = read(f);
    // Both the direct Resources path and the Icon("Name", ...) helper.
    for (const m of src.matchAll(/CommandIcons\/Cmd_(\w+)/g)) named.set(m[1], f);
    // ---- ANY string literal shaped like an icon name counts as a reference -------------------
    //
    // Because the NAME is what Resources.Load is eventually handed, and it can get there any number of
    // ways: passed to Icon(), returned from a switch arm in FormIcon/ProtIcon, or handed to AddRow as
    // a plain argument. Chasing each call shape in turn produced a verifier that reported thirteen
    // used icons as unused, then two, then one — always the shape nobody had thought of yet, and each
    // false alarm making the whole report a little easier to ignore.
    //
    // Matching the naming convention instead is one rule that covers all of them. It can only fail in
    // the harmless direction: a string that happens to look like an icon name and is not one marks a
    // real file as used, which costs nothing.
    for (const m of src.matchAll(/"((?:Form|Prot|Order|Act|Unit)_[A-Za-z]\w*)"/g)) named.set(m[1], f);
    for (const m of src.matchAll(/CmdIcon\.Make\([^,]+,\s*"([A-Z]\w+_\w+)"/g)) named.set(m[1], f);
  }

  console.log(`\n${named.size} command icon(s) named in C#`);
  for (const [name, where] of named) {
    if (name === 'Cmd') continue;                       // the prefix itself, from the interpolated path
    if (!has(CMD_DIR, `Cmd_${name}.png`))
      problems.push(`${path.basename(where)} asks for Cmd_${name}.png, which does not exist`);
  }

  // ...and the other way: generated art nobody uses. Not a failure — an unused icon costs four
  // kilobytes and may be about to be used — but worth saying, because it is usually a rename.
  for (const f of fs.readdirSync(path.join(PROJ, CMD_DIR))) {
    if (!f.endsWith('.png')) continue;
    const n = f.replace(/^Cmd_/, '').replace(/\.png$/, '');
    if (!named.has(n)) notes.push(`Cmd_${n}.png is generated but nothing references it`);
  }
}

// ---- 3. every control carries a tooltip --------------------------------------------------------
//
// The Icon() helper's last argument IS the tooltip, so a control with none is a call whose final
// argument is null. Checked by counting calls rather than by parsing arguments properly: this is a
// tripwire, and a tripwire that is occasionally too strict is far better than one that is subtly too
// lax.
{
  const calls = [...barSrc.matchAll(/\bIcon\(\s*"[^"]+"/g)].length;
  const nullTips = [...barSrc.matchAll(/\bIcon\([^;]*?,\s*null\s*\);/gs)].length;
  console.log(`\n${calls} icon control(s) in the command bar`);
  if (nullTips > 0) problems.push(`${nullTips} command-bar control(s) have no tooltip`);
}

// ---- report ------------------------------------------------------------------------------------
if (notes.length) {
  console.log('');
  for (const n of notes) console.log(`note  ${n}`);
}

console.log('');
if (problems.length === 0) {
  console.log('Every control has a symbol, every symbol exists, and every control explains itself.');
  process.exit(0);
}
for (const p of problems) console.log(`FAIL  ${p}`);
console.log(`\n${problems.length} problem(s).`);
process.exit(1);
