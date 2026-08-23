// ============================================================================================
// EVERY SCRIPT NEEDS A .meta, AND THIS ENVIRONMENT CANNOT MAKE ONE
//
//   node tools/make-script-metas.mjs            report what is missing
//   node tools/make-script-metas.mjs --write    create them
//
// Unity identifies an asset by the GUID in its `.meta` sidecar, not by its path. A script with no
// sidecar is not broken — the editor generates one on import — but it is generated on WHOSE machine?
// Every checkout that imports the project first invents a DIFFERENT GUID for the same file, and from
// then on those checkouts disagree about the identity of every script in the list. The moment one of
// them is dragged onto a prefab or a scene object, that reference is a GUID nobody else has.
//
// Files written in this environment never get one, because there is no editor here to write it. 27
// had accumulated by 2026-08-22 — every script added since the last time the project was opened on a
// machine with Unity on it.
//
// ---- THE GUID IS DERIVED FROM THE PATH, NOT RANDOM ---------------------------------------------
//
// MD5 of the asset path, which makes this script byte-deterministic: run it twice and the second run
// is a no-op, run it on two machines and they agree. A random GUID would mean two people fixing the
// same gap produce two different answers and one of them has to lose. It also means a file that is
// MOVED gets a new identity, which is exactly what `git mv` of an un-metaed script already implies.
//
// A collision against a GUID already in the project is checked for rather than assumed away.
//
// ---- SAFE ONLY WHILE NOTHING REFERENCES THEM ---------------------------------------------------
//
// Assigning a GUID to a file that ALREADY has one somewhere else would break every reference to it.
// That is why this only ever writes a sidecar that is missing, never touches one that exists, and
// why the 2026-08-22 run was preceded by checking that SampleScene.unity referenced none of the 27
// (it references ten project scripts, all of which had their sidecars already).
// ============================================================================================
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ASSETS = path.join(PROJ, 'Assets');
const WRITE = process.argv.includes('--write');

// Scripts only. Art, meshes and folders have their own importer blocks with settings that matter, and
// inventing those from nothing is a different and much riskier job than stamping an identity on a
// source file.
const EXT = '.cs';

const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      // TextMesh Pro ships its own sidecars; anything under a package folder is not ours to stamp.
      if (e.name === 'TextMesh Pro') continue;
      walk(p);
    } else if (e.name.endsWith(EXT)) files.push(p);
  }
})(ASSETS);

// ---- every GUID already in the project, so a new one cannot collide ----
const taken = new Set();
(function walkMeta(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walkMeta(p);
    else if (e.name.endsWith('.meta')) {
      const m = fs.readFileSync(p, 'utf8').match(/^guid:\s*([0-9a-f]{32})/m);
      if (m) taken.add(m[1]);
    }
  }
})(ASSETS);

/// Unity's own MonoImporter block, written in full rather than as the two-line stub some of the
/// existing sidecars use. The stub works — the editor fills the rest in — but it rewrites the file to
/// do it, which turns opening the project into a diff.
const body = guid =>
  `fileFormatVersion: 2\n` +
  `guid: ${guid}\n` +
  `MonoImporter:\n` +
  `  externalObjects: {}\n` +
  `  serializedVersion: 2\n` +
  `  defaultReferences: []\n` +
  `  executionOrder: 0\n` +
  `  icon: {instanceID: 0}\n` +
  `  userData: \n` +
  `  assetBundleName: \n` +
  `  assetBundleVariant: \n`;

const missing = [];
for (const f of files) {
  if (fs.existsSync(f + '.meta')) continue;
  // The path Unity itself would use as the asset key: forward slashes, relative to the project.
  const assetPath = path.relative(PROJ, f).split(path.sep).join('/');
  const guid = crypto.createHash('md5').update(assetPath).digest('hex');
  missing.push({ file: f, assetPath, guid });
}

const collisions = missing.filter(m => taken.has(m.guid));
if (collisions.length) {
  for (const c of collisions) console.log(`COLLISION  ${c.assetPath} -> ${c.guid} is already in use`);
  console.log(`\n${collisions.length} GUID collision(s). Nothing written.`);
  process.exit(1);
}

if (missing.length === 0) {
  console.log(`${files.length} scripts, every one has a .meta.`);
  process.exit(0);
}

for (const m of missing) {
  console.log(`${WRITE ? 'WROTE ' : 'MISSING'}  ${m.assetPath}  ${m.guid}`);
  if (WRITE) fs.writeFileSync(m.file + '.meta', body(m.guid));
}

console.log(`\n${missing.length} script(s) ${WRITE ? 'given a sidecar' : 'have no .meta'}` +
            `${WRITE ? '.' : ' — re-run with --write to create them.'}`);
process.exit(WRITE ? 0 : 1);
