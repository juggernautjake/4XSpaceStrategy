// ============================================================================================
// PULL EVERY FINISHED MODEL OUT OF THE MESHY ACCOUNT
//
//   node tools/meshy-archive-tasks.mjs --token-file tools/meshy-token.txt
//   node tools/meshy-archive-tasks.mjs --token-file ... --out Art/Archive --formats glb,fbx
//
// A safety net, and it exists because of a real mistake: two good models were deleted off disk during
// a change of art direction, on the assumption they were reproducible. They were — but only because
// Meshy still had the tasks. Nothing generated should ever depend on that luck again.
//
// Meshy keeps every succeeded task, so the account is the real archive and the local folders are just
// a cache of it. This walks the whole task list and pulls down everything that produced geometry:
// the concept image, the render, the PBR maps and whichever formats are asked for.
//
// Named by TASK ID and date rather than by ship, because the account does not reliably know which
// hull a task belonged to — many were created with an empty `name`. An id is ugly and unambiguous,
// which is the right trade for an archive; `manifest.json` carries the prompt and timestamps so a
// model can be identified after the fact.
//
// Downloads cost no credits. This is safe to run as often as you like.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };

const TOKEN_FILE = arg('--token-file', '');
let TOKEN = arg('--token', process.env.MESHY_TOKEN || '');
if (TOKEN_FILE && fs.existsSync(TOKEN_FILE)) {
  const raw = fs.readFileSync(TOKEN_FILE, 'utf8').trim();
  if (raw) TOKEN = /^bearer\s/i.test(raw) || raw.startsWith('msy_') ? raw : 'Bearer ' + raw;
}
const OUT     = path.resolve(PROJ, arg('--out', path.join('Art', 'MeshyAccountArchive')));
const FORMATS = arg('--formats', 'glb').split(',').map(s => s.trim()).filter(Boolean);
const PAGES   = parseInt(arg('--pages', '10'), 10);

const API = 'https://www.meshy.ai/meshyd-api/web';

async function api(p) {
  const r = await fetch(API + p, { headers: { authorization: TOKEN, 'content-type': 'application/json' } });
  if (r.status === 401) throw new Error('TOKEN_EXPIRED — refresh the token file and re-run');
  return r.json().catch(() => null);
}

async function grab(url, file) {
  if (!url) return false;
  if (fs.existsSync(file) && fs.statSync(file).size > 0) return true;   // already archived
  const r = await fetch(url);
  if (!r.ok) return false;
  fs.writeFileSync(file, Buffer.from(await r.arrayBuffer()));
  return true;
}

// ---- collect every task -----------------------------------------------------------------------
const all = [];
for (let page = 1; page <= PAGES; page++) {
  const r = await api(`/v2/tasks?pageNum=${page}&pageSize=50&sortBy=-created_at`);
  const arr = r?.result || r || [];
  const list = Array.isArray(arr) ? arr : (arr.data || []);
  if (!list.length) break;
  all.push(...list);
  if (list.length < 50) break;
}

// Only phases that actually produce a downloadable mesh. `image` tasks are concept art and are
// archived alongside their model instead, via the model's own args.
const MESH_PHASES = new Set(['generate', 'texture', 'image-to-3d-texture', 'refine', 'upload']);
const done = all.filter(t => t.status === 'SUCCEEDED' && MESH_PHASES.has(t.phase));

console.log(`tasks seen        : ${all.length}`);
console.log(`finished meshes   : ${done.length}`);
console.log(`archiving to      : ${path.relative(PROJ, OUT)}`);
console.log(`formats           : ${FORMATS.join(', ')}\n`);

fs.mkdirSync(OUT, { recursive: true });
const manifest = [];
let saved = 0, skipped = 0;

for (const t of done) {
  const full = (await api('/v2/tasks/' + t.id))?.result;
  if (!full) { skipped++; continue; }

  const when = new Date(full.createdAt || Date.now()).toISOString().slice(0, 10);
  const label = (full.name || full.phase || 'task').replace(/[^A-Za-z0-9_-]+/g, '_').slice(0, 40);
  const dir = path.join(OUT, `${when}_${label}_${t.id.slice(0, 8)}`);
  fs.mkdirSync(dir, { recursive: true });

  const res = full.result || {};
  await grab(res.previewUrl, path.join(dir, 'render.png'));

  const tex = res.texture?.textureUrls?.[0];
  if (tex) {
    await grab(tex.colorMapUrl,     path.join(dir, 'albedo.png'));
    await grab(tex.normalMapUrl,    path.join(dir, 'normal.png'));
    await grab(tex.roughnessMapUrl, path.join(dir, 'roughness.png'));
    await grab(tex.metallicMapUrl,  path.join(dir, 'metallic.png'));
  }

  // the concept it was built from, if it was an image-to-3d
  const srcImg = full.args?.draft?.imageUrl;
  if (srcImg) await grab(srcImg, path.join(dir, 'concept.png'));

  for (const f of FORMATS) {
    const u = await api(`/v2/tasks/${t.id}/asset-url?type=Task&format=${f}`);
    const url = u?.result;
    if (!url) continue;
    await grab(url, path.join(dir, `model.${f}${/\.zip(\?|$)/i.test(url) ? '.zip' : ''}`));
  }

  const entry = {
    id: t.id, phase: full.phase, mode: full.mode, name: full.name,
    createdAt: full.createdAt, triangleCount: full.triangleCount, cost: full.cost,
    prompt: full.args?.draft?.prompt || full.args?.texture?.prompt || '',
    dir: path.relative(OUT, dir),
  };
  manifest.push(entry);
  fs.writeFileSync(path.join(dir, 'task.json'), JSON.stringify(entry, null, 2));
  saved++;
  console.log(`  ${String(saved).padStart(3)}  ${entry.dir}`);
}

fs.writeFileSync(path.join(OUT, 'manifest.json'), JSON.stringify(manifest, null, 2));
console.log(`\narchived ${saved}, skipped ${skipped}`);
console.log(`index -> ${path.relative(PROJ, path.join(OUT, 'manifest.json'))}`);
