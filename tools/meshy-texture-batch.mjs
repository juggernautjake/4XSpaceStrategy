// ============================================================================================
// TEXTURE THE FLEET
//
//   node tools/meshy-texture-batch.mjs --plan                 # what needs doing, changes nothing
//   node tools/meshy-texture-batch.mjs --token "Bearer ..." --limit 10
//   node tools/meshy-texture-batch.mjs --token "..." --only Pyrothian
//
// A hundred ship meshes exist with no materials on them at all. This uploads each one back to Meshy,
// runs the texture phase against a prompt built from that hull's own recorded description plus its
// civilization's livery scheme, and pulls the result down in every format the game and the 3D printer
// need — plus the render Meshy makes of it, which becomes the ship's UI thumbnail.
//
// ---- RESUMABLE, BECAUSE THE TOKEN DIES EVERY FIFTEEN MINUTES ---------------------------------
//
// The web session's JWT lives 15 minutes (exp - iat = 900s). A hundred models at two to four minutes
// each is hours of work, so a single run CANNOT hold one token to the end — it will 401 partway
// through and lose everything still in flight.
//
// So every unit's progress is written to `meshy-batch-state.json` the moment each step succeeds, and
// re-running skips whatever is already done. The intended shape is a chunk at a time: hand it a fresh
// token, let it do ten, hand it another. Nothing is lost to an expiry and nothing is paid for twice —
// a unit that already has a `textureTask` never fires a second 10-credit job.
//
// ---- WHY TEXTURE-EXISTING AND NOT IMAGE-TO-3D ------------------------------------------------
//
// The best-looking ships in the library came from concept-art -> image-to-3D. That path is not
// available here: only FIVE units have real concept art, the other 135 have nothing but a grey render
// of their own mesh, which carries no colour to transfer. Generating concepts first would be 12 + 30
// credits a hull — 4,200 for the hundred, against a balance of 3,732.
//
// Texturing the mesh that already exists is 10 credits, keeps the geometry that has already been
// checked and oriented, and leaves budget for the five missing Sylvan stations and for retries.
//
// ---- THE PROMPT IS THREE PARTS, AND THE PROPORTIONS MATTER ------------------------------------
//
// Learned by burning 20 credits getting it wrong. "black obsidian, desaturated" came back at 0.141
// mean brightness with the accent roles inverted: 24.85% cyan where cyan was meant to be a 5% trim,
// and 0.84% orange where orange was meant to be the dominant livery.
//
// What fixed it was stating the SHARE OF SURFACE each role gets, and saying "NOT dark" out loud:
//
//     BASE      ~65%   the hull's own material, mid-tone, clearly lit
//     PRIMARY   ~30%   broad bold bands — "the first colour you notice"
//     SECONDARY  ~5%   small named features only — "nowhere else"
//
// Those two accent colours are also what makes the livery RECOLOURABLE later: they are ≥95 degrees
// apart in hue from each other and from the base, so tools/extract-color-masks.mjs can key them out
// of the baked albedo into a mask the shader recolours per player choice. Accents that blend into the
// hull cannot be separated back out afterwards, so the separation has to be designed in here.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const has = n => argv.includes(n);

const SRC       = arg('--src', 'C:/Users/lando/Downloads/4X-Ship-Models');
const OUT       = arg('--out', path.join(PROJ, 'Art', 'MeshyTextured'));
const STATE     = path.join(PROJ, 'tools', 'meshy-batch-state.json');
// A file rather than a flag, for the same reason as the rebuild batch: the web JWT lives fifteen
// minutes, so a token fixed at launch cannot survive a long run. Re-read on every 401.
const TOKEN_FILE = arg('--token-file', '');
const readTokenFile = () => {
  if (!TOKEN_FILE || !fs.existsSync(TOKEN_FILE)) return '';
  const raw = fs.readFileSync(TOKEN_FILE, 'utf8').trim();
  if (!raw) return '';
  return /^bearer\s/i.test(raw) || raw.startsWith('msy_') ? raw : 'Bearer ' + raw;
};
let TOKEN = arg('--token', process.env.MESHY_TOKEN || '') || readTokenFile();
const LIMIT     = parseInt(arg('--limit', '9999'), 10);
const ONLY      = arg('--only', '');
const PLAN      = has('--plan');
const CONCURRENCY = parseInt(arg('--concurrency', '3'), 10);

const API = 'https://www.meshy.ai/meshyd-api/web';
const FORMATS = ['glb', 'fbx', 'obj', 'stl', '3mf'];   // game, DCC, and 3D printing

const palette = JSON.parse(fs.readFileSync(path.join(PROJ, 'tools', 'civ-colors.json'), 'utf8'));
const CIVS = Object.keys(palette.civilizations);

// ---- state ----------------------------------------------------------------------------------
let state = fs.existsSync(STATE) ? JSON.parse(fs.readFileSync(STATE, 'utf8')) : { units: {} };
const saveState = () => fs.writeFileSync(STATE, JSON.stringify(state, null, 2));
const unitState = k => (state.units[k] ||= {});

// ---- helpers --------------------------------------------------------------------------------
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function api(pathname, opts = {}) {
  const r = await fetch(API + pathname, {
    ...opts,
    headers: { authorization: TOKEN, ...(opts.body instanceof FormData ? {} : { 'content-type': 'application/json' }), ...(opts.headers || {}) },
  });
  const body = await r.json().catch(() => null);
  if (r.status === 401) {
    // Park and wait for a refreshed token rather than losing the tasks already in flight.
    if (!TOKEN_FILE) throw new Error('TOKEN_EXPIRED');
    const fresh = readTokenFile();
    if (fresh && fresh !== TOKEN) { TOKEN = fresh; return api(pathname, opts); }
    console.log(`\n  ⏸  token expired — waiting for ${path.basename(TOKEN_FILE)} to be refreshed...\n`);
    for (;;) {
      await sleep(10000);
      const next = readTokenFile();
      if (next && next !== TOKEN) { TOKEN = next; console.log('  ▶  resumed\n'); return api(pathname, opts); }
    }
  }
  return { status: r.status, body };
}

function walk(dir, acc = []) {
  for (const f of fs.readdirSync(dir)) {
    const fp = path.join(dir, f);
    fs.statSync(fp).isDirectory() ? walk(fp, acc) : acc.push(fp);
  }
  return acc;
}

/** The hull's own description, lifted out of the PROMPT.txt the generator left behind. */
function hullDescription(unitDir) {
  const p = path.join(unitDir, 'PROMPT.txt');
  if (!fs.existsSync(p)) return '';
  const txt = fs.readFileSync(p, 'utf8');
  const m = txt.match(/PROMPT USED:\s*([\s\S]*?)(?:\n\s*\n|FULL PROMPT)/);
  let s = (m ? m[1] : '').trim();
  // Keep the SHAPE words and drop the ORIENTATION boilerplate — the mesh already exists, so telling
  // the texturer where the bow is only wastes prompt budget it could spend on materials.
  s = s.replace(/Bow forward[^.]*\./gi, '')
       .replace(/Flat ventral[^.]*\./gi, '')
       .replace(/Engine nozzles only[^.]*\./gi, '')
       .replace(/Bilaterally symmetric[^.]*\./gi, '')
       .replace(/Single watertight mesh[^.]*\./gi, '')
       .replace(/\s+/g, ' ').trim();
  return s.slice(0, 420);
}

/// Meshy hard-caps a prompt at 800 characters, and the cap is not generous — the first draft of this
/// template ran to 1,295. So the parts are ordered by how much they matter and the hull's own
/// description, which is the longest and the least important, is fitted into whatever is left over.
/// Losing some flavour words costs a little character; losing the proportion clauses costs the whole
/// livery scheme, which is the one thing that had to be got right.
const PROMPT_CAP = 800;

function liveryPrompt(civ, unitDir, hullName) {
  const c = palette.civilizations[civ];

  // Rewritten after the first pass returned 1 usable texture in 16. Two changes did the work:
  //
  //   NAME SURFACES, NOT PERCENTAGES. "~30% of the surface" was ignored every time; "on the flanks
  //   and dorsal spine" is followed. Meshy reasons about places, not proportions.
  //
  //   SAY "NEVER WHITE" OUT LOUD. The old base was pearl-white/pale-ice by design, so the accents
  //   could be keyed cleanly — and when an accent failed to land the ship was simply white. The base
  //   now carries the civ's own colour and the prompt refuses pale explicitly.
  const fixed =
    `${civ} starship livery, ${hullName}. ` +
    `HULL: ${c.baseMaterial}. Mid-tone, clearly lit, crisp panel lines, rivets, vents, subtle wear. ` +
    `STRONG SATURATED COLOUR — never white, never pale, never washed-out grey, never bare metal. ` +
    `PRIMARY, bold and unmissable: vivid ${c.primary.name} ${c.primary.hex} on ${c.primary.role}. ` +
    `SECONDARY, small accents only: bright ${c.secondary.name} ${c.secondary.hex} on ${c.secondary.role}, nowhere else. ` +
    `Keep ${c.primary.name} and ${c.secondary.name} on separate surfaces so they never blend together. ` +
    `Bright even lighting, crisp readable detail at small size, clean game-ready PBR.`;

  const room = PROMPT_CAP - fixed.length - 8;
  if (room < 60) return fixed.slice(0, PROMPT_CAP);

  let desc = hullDescription(unitDir);
  if (!desc) return fixed;
  if (desc.length > room) desc = desc.slice(0, room).replace(/[\s,;]+\S*$/, '') + '.';

  // The hull description goes AFTER the name and BEFORE the colour rules, so if anything is lost to
  // the cap it is the tail of the flavour text rather than a colour instruction.
  const head = `${civ} starship livery, ${hullName}. Hull: ${desc} `;
  const out = head + fixed.slice(fixed.indexOf('BASE ~65%'));
  return out.length <= PROMPT_CAP ? out : fixed;
}

// ---- discover the work ------------------------------------------------------------------------
function pickGlb(unitDir) {
  const g = walk(unitDir).filter(f => f.toLowerCase().endsWith('.glb'));
  if (!g.length) return null;
  return g.map(f => ({ f, size: fs.statSync(f).size })).sort((a, b) => a.size - b.size)[0].f;
}

function buildWorklist() {
  const work = [];
  for (const civ of CIVS) {
    const civDir = path.join(SRC, civ);
    if (!fs.existsSync(civDir)) continue;
    if (ONLY && civ !== ONLY) continue;
    for (const unit of fs.readdirSync(civDir).sort()) {
      const unitDir = path.join(civDir, unit);
      if (!fs.statSync(unitDir).isDirectory()) continue;
      const glb = pickGlb(unitDir);
      const key = `${civ}/${unit}`;
      work.push({ key, civ, unit, unitDir, glb, hullName: unit.replace(/^\d+-/, '').replace(/-/g, ' ') });
    }
  }
  return work;
}

/** Does this .glb already carry materials? Cheap structural check — parse the glTF JSON chunk only. */
function isTextured(glbPath) {
  if (!glbPath) return false;
  const fd = fs.openSync(glbPath, 'r');
  try {
    const head = Buffer.alloc(12); fs.readSync(fd, head, 0, 12, 0);
    if (head.toString('utf8', 0, 4) !== 'glTF') return false;
    const clen = Buffer.alloc(8); fs.readSync(fd, clen, 0, 8, 12);
    const jsonLen = clen.readUInt32LE(0);
    const json = Buffer.alloc(Math.min(jsonLen, 8 << 20));
    fs.readSync(fd, json, 0, json.length, 20);
    const doc = JSON.parse(json.toString('utf8').replace(/\0+$/, ''));
    return Array.isArray(doc.materials) && doc.materials.length > 0
        && Array.isArray(doc.images) && doc.images.length > 0;
  } catch { return false; }
  finally { fs.closeSync(fd); }
}

// ---- the per-unit pipeline --------------------------------------------------------------------
async function processUnit(w) {
  const st = unitState(w.key);

  // 1. upload the mesh (free)
  if (!st.modelId) {
    const buf = fs.readFileSync(w.glb);
    const fd = new FormData();
    fd.append('file', new Blob([buf], { type: 'model/gltf-binary' }), path.basename(w.glb));
    const up = await api('/v2/files/models', { method: 'POST', body: fd });
    if (up.status !== 200 || !up.body?.result?.id) throw new Error(`upload failed ${up.status} ${JSON.stringify(up.body).slice(0,200)}`);
    st.modelId = up.body.result.id;
    saveState();
  }

  // 2. register it as a task (free)
  //
  // `phase: 'upload'` with a BARE model id. Both parts were found the hard way: `phase: 'generate'`
  // with `mode: 'upload'` — which is how the finished task reads back once it exists — is rejected
  // with "Invalid parent ID", because a generate phase wants a draft to continue from and an upload
  // has none. And the id must NOT carry the `uploads/` prefix that the stored args display; that
  // prefix is added server-side.
  if (!st.baseTask) {
    const t = await api('/v2/tasks', {
      method: 'POST',
      body: JSON.stringify({
        phase: 'upload',
        args: { generate: { modelId: st.modelId, name: `${w.civ}_${w.hullName}` } },
      }),
    });
    if (t.status !== 200 || !t.body?.result) throw new Error(`base task failed ${t.status} ${JSON.stringify(t.body).slice(0, 300)}`);
    st.baseTask = t.body.result;
    saveState();
  }

  // 2b. WAIT for the upload task to finish before using it as a parent.
  //
  // Registering an upload returns a task id immediately, but the mesh is still being ingested behind
  // it — and a texture job pointed at one that has not landed yet is rejected with "Parent task not
  // found", which reads like a bad id rather than a race. It is only a few seconds.
  if (!st.baseReady) {
    let ready = false;
    for (let i = 0; i < 60; i++) {
      const d = await api('/v2/tasks/' + st.baseTask);
      const b = d.body?.result || d.body;
      if (b?.status === 'SUCCEEDED') { ready = true; break; }
      if (b?.status === 'FAILED') throw new Error('upload task FAILED: ' + (b.errMsg || ''));
      await sleep(3000);
    }
    if (!ready) throw new Error('upload task never became ready');
    st.baseReady = true;
    saveState();
  }

  // 3. texture it (10 credits) — the only step that costs anything
  if (!st.textureTask) {
    const prompt = liveryPrompt(w.civ, w.unitDir, w.hullName);
    st.prompt = prompt;
    const t = await api('/v2/tasks', {
      method: 'POST',
      body: JSON.stringify({
        phase: 'texture', parent: st.baseTask,
        args: { texture: { prompt, imageId: '', artStyle: 'realistic', aiModel: 'blueberry', enablePBR: true } },
      }),
    });
    if (t.status !== 200 || !t.body?.result) throw new Error(`texture task failed ${t.status} ${JSON.stringify(t.body).slice(0,200)}`);
    st.textureTask = t.body.result;
    saveState();
  }

  // 4. wait for it
  let info = null;
  for (let i = 0; i < 120; i++) {
    const d = await api('/v2/tasks/' + st.textureTask);
    const b = d.body?.result || d.body;
    if (b?.status === 'SUCCEEDED') { info = b; break; }
    if (b?.status === 'FAILED') throw new Error('texture FAILED: ' + (b.errMsg || ''));
    await sleep(5000);
  }
  if (!info) throw new Error('texture timed out');

  // 5. pull everything down
  const dest = path.join(OUT, w.civ, w.unit);
  fs.mkdirSync(dest, { recursive: true });
  const base = `${w.civ}_${w.hullName.replace(/\s+/g, '')}`;

  const grab = async (url, file) => {
    if (!url) return false;
    const r = await fetch(url);
    if (!r.ok) return false;
    fs.writeFileSync(path.join(dest, file), Buffer.from(await r.arrayBuffer()));
    return true;
  };

  // the render Meshy makes of the finished model — this is the UI thumbnail
  if (await grab(info.result?.previewUrl, `${base}_thumbnail.png`)) st.thumbnail = true;

  // the PBR maps, loose, for anyone who wants to rewire materials by hand
  const tex = info.result?.texture?.textureUrls?.[0];
  if (tex) {
    await grab(tex.colorMapUrl,     `${base}_albedo.png`);
    await grab(tex.normalMapUrl,    `${base}_normal.png`);
    await grab(tex.roughnessMapUrl, `${base}_roughness.png`);
    await grab(tex.metallicMapUrl,  `${base}_metallic.png`);
  }

  // every format, game and print alike
  st.formats ||= {};
  for (const fmt of FORMATS) {
    if (st.formats[fmt]) continue;
    const u = await api(`/v2/tasks/${st.textureTask}/asset-url?type=Task&format=${fmt}`);
    const url = u.body?.result;
    if (!url) continue;
    const isZip = /\.zip(\?|$)/i.test(url);
    if (await grab(url, `${base}.${fmt}${isZip ? '.zip' : ''}`)) st.formats[fmt] = true;
  }

  st.done = true;
  st.tris = info.triangleCount;
  saveState();
  return st;
}

// ---- main --------------------------------------------------------------------------------------
const work = buildWorklist();
for (const w of work) {
  const st = unitState(w.key);
  st.textured ??= isTextured(w.glb);
  st.hasGlb = !!w.glb;
}
saveState();

const todo = work.filter(w => {
  const st = state.units[w.key];
  return w.glb && !st.done && !st.textured;
});

console.log(`units total      : ${work.length}`);
console.log(`already textured : ${work.filter(w => state.units[w.key].textured).length}`);
console.log(`already done here: ${work.filter(w => state.units[w.key].done).length}`);
console.log(`no .glb          : ${work.filter(w => !w.glb).length}`);
console.log(`TO DO            : ${todo.length}   (${todo.length * 10} credits)`);

if (PLAN) {
  console.log('\n--- sample prompt ---');
  if (todo[0]) console.log(liveryPrompt(todo[0].civ, todo[0].unitDir, todo[0].hullName));
  console.log('\n--- first 15 to do ---');
  todo.slice(0, 15).forEach(w => console.log('  ' + w.key));
  process.exit(0);
}

if (!TOKEN) { console.error('\nNo --token given. Pass the Bearer token from the browser session.'); process.exit(1); }

const batch = todo.slice(0, LIMIT);
console.log(`\nrunning ${batch.length} (concurrency ${CONCURRENCY})\n`);

let ok = 0, failed = 0, expired = false;
const queue = [...batch];

async function worker(id) {
  while (queue.length && !expired) {
    const w = queue.shift();
    try {
      await processUnit(w);
      ok++;
      console.log(`  [${ok + failed}/${batch.length}] OK    ${w.key}`);
    } catch (e) {
      if (e.message === 'TOKEN_EXPIRED') { expired = true; queue.unshift(w); console.log('  !! token expired — stopping cleanly, re-run with a fresh one'); break; }
      failed++;
      unitState(w.key).lastError = e.message;
      saveState();
      console.log(`  [${ok + failed}/${batch.length}] FAIL  ${w.key}  ${e.message}`);
    }
  }
}

await Promise.all(Array.from({ length: Math.min(CONCURRENCY, batch.length) }, (_, i) => worker(i)));

console.log(`\ndone: ${ok} ok, ${failed} failed, ${queue.length} left in this batch`);
console.log(`state -> ${path.relative(PROJ, STATE)}`);
if (expired) process.exit(2);
