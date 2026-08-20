// ============================================================================================
// REBUILD A FLEET: concept art -> textured 3D, with lineage
//
//   node tools/meshy-rebuild-batch.mjs --plan
//   node tools/meshy-rebuild-batch.mjs --token "Bearer ..." --only Aquarii --lineage scout
//   node tools/meshy-rebuild-batch.mjs --token "..." --limit 6
//
// Texturing an uploaded mesh was tried first and abandoned: 1 usable result in 16, because Meshy's
// texture step largely ignores livery instructions on geometry it did not author — it returns a
// greyscale hull or a single flat colour. The path that produced the art actually worth having is the
// one the good ships already came from:
//
//     text-to-image   ~12 cr   a proper piece of concept art
//     image-to-3d     ~30 cr   a mesh that inherits that art's colour and detail
//
// It also regenerates the GEOMETRY, which matters because several existing hulls are poor
// independently of texture — the Aquarii dreadnought is a grey cylinder and its deep-space station a
// featureless jellyfish. No texture rescues those.
//
// ---- LINEAGE IS WHY THIS RUNS IN ORDER --------------------------------------------------------
//
// A Mk II has to look like the Mk I with more bolted on. Generating each tier independently from text
// gives three unrelated ships however carefully the words are chosen — the model has no memory of what
// the Mk I looked like.
//
// So a chained lineage feeds each tier's FINISHED CONCEPT into the next as an image-to-image
// reference: same hull, same palette, same camera, and only the `upgrade` sentence changes. Tiers are
// therefore strictly sequential and a failure partway through stops that lineage rather than silently
// producing an orphan Mk III. Unchained one-offs have no ancestor and run independently.
//
// The reference is passed as the previous task's own signed `previewUrl` — Meshy can read its own CDN,
// so nothing has to be downloaded and re-uploaded between tiers.
//
// Resumable through `meshy-rebuild-state.json`; the web token lives ~15 minutes, so a long run is
// expected to be fed fresh tokens in chunks. Nothing is ever paid for twice.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const has = n => argv.includes(n);

// ---- auth -------------------------------------------------------------------------------------
//
// The web JWT lives fifteen minutes and a full rebuild is hours, so a token passed once on the command
// line guarantees the run dies partway through with tasks still in flight. Instead the token is read
// from a FILE and re-read whenever the API rejects it: the batch parks, waits for that file to be
// refreshed from the browser, and picks up exactly where it stopped. Nothing already paid for is lost
// to an expiry, and the run survives however many refreshes it needs.
//
// An API key (`msy_…`) never expires and skips all of this — pass it as --token and the wait never
// triggers.
const TOKEN_FILE = arg('--token-file', '');
let TOKEN = arg('--token', process.env.MESHY_TOKEN || '');

function readTokenFile() {
  if (!TOKEN_FILE || !fs.existsSync(TOKEN_FILE)) return '';
  const raw = fs.readFileSync(TOKEN_FILE, 'utf8').trim();
  if (!raw) return '';
  return /^bearer\s/i.test(raw) || raw.startsWith('msy_') ? raw : 'Bearer ' + raw;
}
if (TOKEN_FILE) TOKEN = readTokenFile() || TOKEN;
const OUT     = arg('--out', path.join(PROJ, 'Art', 'Incoming'));
const STATE   = path.join(PROJ, 'tools', 'meshy-rebuild-state.json');
const ONLY    = arg('--only', '');
const LINEAGE = arg('--lineage', '');
const LIMIT   = parseInt(arg('--limit', '9999'), 10);
const PLAN    = has('--plan');
const CONC    = parseInt(arg('--concurrency', '2'), 10);

const API = 'https://www.meshy.ai/meshyd-api/web';
const FORMATS = ['glb', 'fbx', 'obj', 'stl', '3mf'];

const design  = JSON.parse(fs.readFileSync(path.join(PROJ, 'tools', 'ship-design.json'), 'utf8'));
const palette = JSON.parse(fs.readFileSync(path.join(PROJ, 'tools', 'civ-colors.json'), 'utf8'));
const CIVS = Object.keys(design.civilizations);

let state = fs.existsSync(STATE) ? JSON.parse(fs.readFileSync(STATE, 'utf8')) : { units: {} };
const save = () => fs.writeFileSync(STATE, JSON.stringify(state, null, 2));
const S = k => (state.units[k] ||= {});

const sleep = ms => new Promise(r => setTimeout(r, ms));

let waitingForToken = false;

async function api(p, opts = {}) {
  for (let attempt = 0; ; attempt++) {
    const r = await fetch(API + p, {
      ...opts,
      headers: { authorization: TOKEN, 'content-type': 'application/json', ...(opts.headers || {}) },
    });
    // 5xx is Meshy having a moment, not a bad request. One InternalError killed a whole lineage
    // because a failed tier stops the tiers that chain from it — so a transient blip cost the Mk II
    // and Mk III as well. Back off and try again; only give up after several.
    if (r.status >= 500 && attempt < 4) {
      const wait = 4000 * (attempt + 1);
      console.log(`  ~ ${r.status} from Meshy, retrying in ${wait / 1000}s`);
      await sleep(wait);
      continue;
    }
    if (r.status !== 401) return { status: r.status, body: await r.json().catch(() => null) };

    // 401: the token died mid-run. With no file to watch there is nothing to wait FOR, so fail fast
    // and let the caller stop cleanly; the state file means re-running loses nothing.
    if (!TOKEN_FILE) throw new Error('TOKEN_EXPIRED');

    const fresh = readTokenFile();
    if (fresh && fresh !== TOKEN) { TOKEN = fresh; continue; }   // already refreshed — carry on

    if (!waitingForToken) {
      waitingForToken = true;
      console.log(`\n  ⏸  token expired — waiting for ${path.basename(TOKEN_FILE)} to be refreshed...\n`);
    }
    await sleep(10000);
    const next = readTokenFile();
    if (next && next !== TOKEN) {
      TOKEN = next; waitingForToken = false;
      console.log('  ▶  token refreshed, resuming\n');
    }
  }
}

async function waitTask(id, tries = 150) {
  for (let i = 0; i < tries; i++) {
    const d = await api('/v2/tasks/' + id);
    const b = d.body?.result || d.body;
    if (b?.status === 'SUCCEEDED') return b;
    if (b?.status === 'FAILED') throw new Error('task FAILED: ' + (b.errMsg || b.errorCode || ''));
    await sleep(5000);
  }
  throw new Error('task timed out');
}

// ---- prompts ---------------------------------------------------------------------------------
const CAP = 800;

function conceptPrompt(civ, lin, tier, isChild) {
  const c = design.civilizations[civ];
  const p = palette.civilizations[civ];

  // A chained tier spends its words on WHAT CHANGED, because the reference image is already carrying
  // the hull, palette and camera. Repeating the full description there fights the reference and
  // produces a redesign instead of a refit.
  // The specific creature this hull is, where one is defined. Naming the animal is what makes the
  // ships distinct from each other: "an Aquarii fighter" returns a generic wedge every time, "a
  // hammerhead shark with bolted armour and four cannons" does not. It also carries the lineage on
  // its own — reef shark, hammerhead, megalodon are recognisably the same animal escalating.
  const creature = design.creatures?.[civ]?.[tier.unit] || '';

  // How much equipment is bolted to it. See techProgression: tier decides how built-up a hull looks,
  // so a Mk III reads as an upgrade rather than a sidegrade.
  const tech = design.techProgression?.[design.techTiers?.[tier.unit] ?? 2] || '';

  // A chained tier MUST still be told what the ship is made of. Saying only "keep it identical" and
  // then handing over two accent hexes gave a Mk II that was entirely amber and magenta with no teal
  // and no creature left in it — the model had no colour anchor, so it painted the whole hull in the
  // only colours the prompt named. Restating the base costs a dozen words and holds the identity.
  // ---- HOW HARD THE CHAIN PULLS -------------------------------------------------------------
  //
  // Two strengths, because a lineage means two different things depending on which lineage it is.
  //
  //   refit   (the default) — the SAME HULL upgraded. Keep the silhouette and the proportions. This
  //           is right for a Mk I -> Mk II -> Mk III, where looking alike IS the progression: the
  //           Aquarii reef shark, hammerhead and megalodon read as one animal growing up, and the
  //           Terran spyplane, twin-engine jet and hypersonic blackbird as one airframe maturing.
  //
  //   family  — the same NAVY, not the same ship. Keep the materials, the palette, the finish, the
  //           weathering and the camera; build a DIFFERENT CLASS of vessel.
  //
  // The second exists because the first collapsed the Terran battle line. A corvette, a guided-
  // missile cruiser, a fleet carrier and a battleship are four different kinds of warship, and their
  // per-hull descriptions say so in detail — but "keep its silhouette and proportions" is a stronger
  // instruction than any description, and it won: the first pass returned the same long grey slab
  // four times, with the carrier showing no flight deck at all.
  //
  // The Aquarii survived the same setting only because a lobster, a sawfish, a manta ray and a
  // leviathan cannot collapse into one another however hard a model tries to preserve a silhouette.
  // That was luck of the metaphor, not a property of the chain.
  //
  // Unchaining the line outright was the first fix and it was too blunt: the chain is also what keeps
  // a civilization's ships looking related, and dropping it buys variance by spending coherence. This
  // keeps both — same fleet, different ship.
  //
  // BOTH ARE SHORT, AND THE IMPORTANT HALF COMES FIRST. There is an 800-character cap on the whole
  // prompt, the livery and tail are reserved out of it first (they carry the orientation rules and the
  // key hues), and the descriptive head is trimmed into what is left — from the END. The first draft
  // of the family sentence put "this is a DIFFERENT CLASS OF VESSEL" after four clauses about
  // materials and palette, so the trim ate precisely the instruction the sentence existed for and the
  // prompt went out reading "...keep its materials, palette, surface finish, weathering and." Whatever
  // matters most goes at the front, where nothing can trim it.
  const chainMode = lin.chainMode || 'refit';
  const reference = chainMode === 'family'
    ? 'A DIFFERENT CLASS of vessel from the reference image — its own silhouette and proportions. ' +
      'Same navy: keep the reference materials, palette, finish and camera.'
    : 'This is the SAME ship as the reference image, refitted — keep its silhouette, proportions, ' +
      'base colour and camera angle.';

  // The head, in priority order — WHAT THE SHIP IS, then how it relates to its sibling, then how
  // built-up it should look. `tech` is last because it is the one clause the rest can survive without:
  // tier escalation is also carried by the per-hull text and by the chain itself, whereas losing the
  // hull's own description or the chain instruction loses the ship.
  // THE CIVILIZATION'S SIGNATURE, and it sits third on purpose — ahead of the tier clause, so it is
  // never the thing dropped to fit.
  //
  // Without it a fire civilization and an ice civilization come back as the same grey ship with a
  // different accent colour, which is exactly what happened: twenty-three Pyrothian hulls that should
  // have been charred basalt with magma cracks arrived as clean battleship grey. The hull description
  // says what SHAPE a ship is and the palette says what COLOUR it is; nothing was saying what it is
  // MADE OF or what state its surface is in. This does.
  const signature = p.signature || '';

  // SIGNATURE OUTRANKS THE CHAIN SENTENCE, which looks backwards and is not.
  //
  // A chained tier is an image-to-image generation: the parent's picture is ATTACHED to the request.
  // The sentence saying "keep the reference's silhouette" is reinforcement of something the model can
  // already see. The signature is the only place the civilization's surface identity appears at all.
  //
  // When the budget forced one out, it was taking the signature — so the chained Pyrothian stations
  // were being asked for a forge-city with no instruction anywhere that it should look burnt. Now the
  // redundant clause goes first and the irreplaceable one stays.
  const opening = creature ? creature.charAt(0).toUpperCase() + creature.slice(1) + '.' : '';
  const headParts = isChild
    ? [opening, signature, reference, tech]
    : [`${civ} starship concept art: ${creature || lin.family}.`, signature, tech,
       creature ? '' : c.aesthetic + '.'];

  // Stations get their own tail: telling a space station its bow must be distinct from its stern
  // produces a station with a nose cone, and the orientation heuristic treats them as spin-in-place
  // objects anyway.
  const tail = lin.key === 'station' ? design.stationTail : design.sharedTail;
  // "Accents ON a <base> hull", never a bare pair of hex codes. Naming the colours without saying
  // what they sit on invites the model to make them the entire paint job — a scout came back
  // entirely amber with no teal left anywhere.
  //
  // MOSTLY is doing real work in this sentence. Unlike the 3D texturer, the image model does respond
  // to a stated share, and without one the accent creeps until it has eaten the hull. The base is
  // also named twice on purpose: once as the thing the ship IS, once as the thing that stays.
  // ---- THE ACCENTS GO WHERE THE PALETTE SAYS THEY GO -----------------------------------------
  //
  // civ-colors.json has carried a `role` for every accent since it was written — "the glowing seams
  // between armour plates, vent throats" for Pyrothian magma, "crystal spine tips, drive glow" for
  // Cryithn gold — and none of them were ever sent. Every civilization got the same hardcoded
  // sentence: primary "on some armour panels", secondary "on small trim, seams and lights".
  //
  // That is most of why the first Pyrothian fleet came back looking like Terran ships painted orange.
  // It was given the Terran instruction. "Some armour panels" produces panels; "the glowing seams
  // between armour plates and the vent throats" produces a furnace.
  const livery = ` The hull is MOSTLY ${p.baseShort} — at least two thirds of it. Accents only: ` +
                 `${p.primary.hex} on ${p.primary.role}; ${p.secondary.hex} on ${p.secondary.role}. ` +
                 'Do not paint the whole ship in the accent colours.';

  // The tail and the livery hexes are NON-NEGOTIABLE — they carry the orientation rules the import
  // heuristic depends on and the two key hues the mask extractor looks for. So they are reserved
  // first and the descriptive head is trimmed into whatever is left, rather than the other way round.
  // Getting this backwards is what silently dropped the livery clauses on the first attempt.
  const fixed = livery + ' ' + tail;
  const room = CAP - fixed.length - 2;

  // ---- FITTING THE HEAD: DROP WHOLE CLAUSES, NEVER HALF A SENTENCE --------------------------
  //
  // This used to cut the head at the character limit and glue a full stop on, silently. That cost a
  // whole round of Terran capitals: the family-mode chain sentence ran a little long, its tail was
  // cut, and what went to the model was "...keep its materials, palette, surface finish, weathering
  // and." — with the instruction the sentence existed to give ("a DIFFERENT CLASS of vessel") removed
  // outright. Every hull came back looking like the one before it, and the prompt read plausibly
  // enough that nothing looked wrong until the art did.
  //
  // Now the least important clause is dropped ENTIRELY and the rest is left intact, which degrades to
  // a shorter true instruction rather than a longer false one. Mid-sentence truncation survives only
  // as the last resort, and it says so out loud.
  const parts = isChild ? [...headParts] : [...headParts, `${c.techMandate}.`];

  let body = parts.filter(Boolean).join(' ').replace(/\s+/g, ' ').trim();
  while (body.length > room && parts.length > 1)
  {
    // Last is least: `tech`, then the civ's aesthetic restatement. The hull's own description and the
    // chain instruction are at the front and are never the ones dropped.
    const dropped = parts.pop();
    if (dropped) console.log(`  ~ ${tier.unit}: dropped a clause to fit (${dropped.slice(0, 40)}...)`);
    body = parts.filter(Boolean).join(' ').replace(/\s+/g, ' ').trim();
  }

  if (body.length > room)
  {
    console.log(`  !! ${tier.unit}: STILL over by ${body.length - room} char(s) after dropping clauses — ` +
                'cutting mid-sentence. The hull description itself is too long; shorten it in ship-design.json.');
    body = body.slice(0, room).replace(/[\s,;:]+\S*$/, '') + '.';
  }

  return body + fixed;
}

// ---- worklist --------------------------------------------------------------------------------
//
// ORDER MATTERS, because the budget will run out before the fleet does. Whatever is generated first is
// what actually ships, so the sequence is a priority list rather than a convenience:
//
//   Civilizations in the order Jacob asked for them — Aquarii, then Terran, then the rest.
//   Lineages by how often a player looks at them: the ships you fight with, then the ones you open the
//   game with, then the battle line, then science, then civilian hulls, and stations last because they
//   sit still at a world and are the least-examined art in the game.
const CIV_ORDER = ['Aquarii', 'Terran', 'Pyrothian', 'Cryithn', 'Sylvan'];
// Stations moved up from last. They were bottom of the list on the reasoning that they sit still at a
// world and are the least-examined art in the game — which is true of a relay mast and completely
// untrue of the ones that are set pieces. A jellyfish deep-space outpost, a coral research bloom and a
// crab fortress-city are among the most distinctive hulls any civilization has, and leaving them until
// after four other civilizations' freighters meant they were the most likely thing to never get made.
const LINEAGE_ORDER = ['fighter', 'scout', 'capital', 'station', 'research', 'colony', 'explorer',
                       'miner', 'transport', 'terraformer', 'probe'];

const civRank = c => { const i = CIV_ORDER.indexOf(c); return i < 0 ? 99 : i; };
const linRank = k => { const i = LINEAGE_ORDER.indexOf(k); return i < 0 ? 99 : i; };

function worklist() {
  const jobs = [];
  const civs = [...CIVS].sort((a, b) => civRank(a) - civRank(b));
  const lineages = [...design.lineages].sort((a, b) => linRank(a.key) - linRank(b.key));
  for (const civ of civs) {
    if (ONLY && civ !== ONLY) continue;
    for (const lin of lineages) {
      if (LINEAGE && lin.key !== LINEAGE) continue;
      lin.tiers.forEach((tier, idx) => {
        jobs.push({
          key: `${civ}/${tier.unit}`,
          civ, lin, tier, idx,
          chained: lin.chain && idx > 0,
          prevKey: lin.chain && idx > 0 ? `${civ}/${lin.tiers[idx - 1].unit}` : null,
        });
      });
    }
  }
  return jobs;
}

// ============================================================================================
// THE PARENT'S CONCEPT URL GOES STALE, AND STALE IS INDISTINGUISHABLE FROM WRONG
//
// A chained tier is generated by handing Meshy its PARENT'S concept image as an image-to-image
// reference, and the parent's URL is a SIGNED CDN link with an expiry on it. That is invisible for a
// lineage generated in one sitting — the parent finished four minutes ago — and it is fatal for
// every other case:
//
//   * a re-roll of one hull whose siblings were generated hours or days earlier;
//   * a resume after the batch parked waiting for a token;
//   * any run that picks up a state file written in an earlier session.
//
// What comes back is `400 InvalidParameters: Image not found`, which reads like a bad request rather
// than an expiry, and it cost two Terran hulls before the cause was clear.
//
// So the stored URL is treated as a CACHE, not as the truth. When the parent still has its task id —
// which it always does, because that is where the URL came from — the task is read back and the
// current signed URL taken from it. That costs one cheap GET and cannot go stale, because it is
// fetched at the moment of use.
// ============================================================================================
async function freshRef(prevKey, prev) {
  if (!prev.conceptTask) return prev.conceptUrl;

  try {
    const info = await waitTask(prev.conceptTask, 1);
    const img = info.result?.image;
    const url = img?.items?.[0]?.url
             || (Array.isArray(img?.imageUrls) && img.imageUrls[0])
             || img?.imageUrl || info.result?.previewUrl || '';
    if (url && url !== prev.conceptUrl) {
      prev.conceptUrl = url;   // keep the cache warm for the next child in the chain
      save();
    }
    return url || prev.conceptUrl;
  } catch (e) {
    // A parent whose task can no longer be read is not a reason to abandon the child: the stored URL
    // may still be live, and if it is not the request fails with a message naming the parent.
    console.log(`  ~ could not refresh ${prevKey}'s reference (${e.message}); using the stored URL`);
    return prev.conceptUrl;
  }
}

// ---- one unit --------------------------------------------------------------------------------
async function runUnit(j) {
  const st = S(j.key);

  // 1. concept art
  if (!st.conceptUrl) {
    let refUrl = '';
    if (j.chained) {
      const prev = state.units[j.prevKey];
      if (!prev?.conceptUrl) throw new Error(`waiting on ${j.prevKey}`);
      refUrl = await freshRef(j.prevKey, prev);
    }
    const prompt = conceptPrompt(j.civ, j.lin, j.tier, !!refUrl);
    st.conceptPrompt = prompt;

    // `phase: 'image'` — NOT 'text-to-image'. That name appears when a finished task is read back, but
    // it is not accepted on creation ("Invalid phase"); the same trap as the upload task, where the
    // stored shape and the accepted shape differ.
    //
    // refinePrompt is OFF deliberately. Meshy rewrites the prompt when it is on, and what it rewrites
    // away is exactly the orientation and livery-placement wording the import heuristic and the mask
    // extractor depend on — the clauses that were carefully reserved against the 800-char cap.
    const body = {
      phase: 'image',
      args: { image: {
        _version: 'v1',
        prompt,
        aiModel: design.conceptModel,
        imageIds: [],
        imageUrls: refUrl ? [refUrl] : [],
        aspectRatio: design.aspectRatio,
        numImages: 1,
        refinePrompt: false,
      } },
    };
    const t = await api('/v2/tasks', { method: 'POST', body: JSON.stringify(body) });
    if (t.status !== 200 || !t.body?.result) throw new Error(`concept failed ${t.status} ${JSON.stringify(t.body).slice(0,200)}`);
    st.conceptTask = t.body.result; save();

    const info = await waitTask(st.conceptTask);
    // The image phase returns `result.image.items[]`, each `{id, mimeType, url}` — not the
    // `imageUrls` array the ARGUMENTS use. The other shapes are kept as fallbacks in case a
    // different aiModel answers differently.
    const img = info.result?.image;
    st.conceptUrl = img?.items?.[0]?.url
                 || (Array.isArray(img?.imageUrls) && img.imageUrls[0])
                 || img?.imageUrl || info.result?.previewUrl || '';
    // image-to-3d wants an imageID, not just a URL, and refuses the task without one. Meshy's own
    // generated image already carries one — so the concept never has to be downloaded and re-uploaded
    // just to be handed back to the service that made it.
    st.conceptId = img?.items?.[0]?.id || '';
    if (!st.conceptUrl) throw new Error('concept produced no image url: ' + JSON.stringify(info.result?.image ?? {}).slice(0, 200));
    save();
  }

  // A concept made before conceptId was recorded still has its task; re-reading it is free.
  if (!st.conceptId && st.conceptTask) {
    const info = await waitTask(st.conceptTask, 3);
    st.conceptId = info.result?.image?.items?.[0]?.id || '';
    save();
  }

  // 2. concept -> textured 3D
  if (!st.modelTask) {
    if (!st.conceptId) throw new Error('no concept image id');
    const body = {
      phase: 'image-to-3d-texture',
      args: {
        draft: {
          aiModel: 'blueberry', modelType: 'standard', topology: '',
          // Required even for image-to-3D — the API rejects an empty prompt here. It also earns its
          // place: the image carries the look, and this keeps the geometry rules (bow/stern, flat
          // ventral) in front of the model while it builds the mesh the import heuristic must read.
          prompt: `${j.civ} ${j.tier.unit.replace(/^\d+-/, '').replace(/-/g, ' ')}. ${design.sharedTail}`.slice(0, CAP),
          imageId: st.conceptId, imageIds: [st.conceptId], symmetryMode: 0, seed: 0, license: 'private',
          shouldTransferImageStyle: true,
          imageUrl: st.conceptUrl, imageUrls: [st.conceptUrl],
        },
        // Also required, and also worth having: the concept image supplies the look, and this states
        // the livery placement one more time at the moment the texture is actually baked. It is the
        // last chance to get the two accents onto separable surfaces, which is what the mask
        // extractor — and therefore player-chosen colours — depends on.
        texture: {
          prompt: `${palette.civilizations[j.civ].baseMaterial}. Primary ${palette.civilizations[j.civ].primary.hex} on large flank and spine panels; secondary ${palette.civilizations[j.civ].secondary.hex} only on small trim, edges and lights. Dense panel lines, rivets, vents, weathering, high detail.`.slice(0, CAP),
          imageId: '', imageIds: null, seed: 0, artStyle: 'realistic',
          aiModel: 'avocado', enablePBR: true, srMode: 'none', textureSize: 2048,
        },
      },
    };
    const t = await api('/v2/tasks', { method: 'POST', body: JSON.stringify(body) });
    if (t.status !== 200 || !t.body?.result) throw new Error(`image-to-3d failed ${t.status} ${JSON.stringify(t.body).slice(0,200)}`);
    st.modelTask = t.body.result; save();
  }

  const model = await waitTask(st.modelTask);

  // 3. everything down to disk
  const dest = path.join(OUT, j.civ, j.tier.unit);
  fs.mkdirSync(dest, { recursive: true });
  const base = `${j.civ}_${j.tier.unit.replace(/^\d+-/, '').replace(/-/g, '')}`;

  const grab = async (url, file) => {
    if (!url) return false;
    const r = await fetch(url);
    if (!r.ok) return false;
    fs.writeFileSync(path.join(dest, file), Buffer.from(await r.arrayBuffer()));
    return true;
  };

  await grab(st.conceptUrl, `${base}_concept.png`);            // the art, worth keeping
  await grab(model.result?.previewUrl, `${base}_thumbnail.png`); // the UI thumbnail

  const tex = model.result?.texture?.textureUrls?.[0];
  if (tex) {
    await grab(tex.colorMapUrl,     `${base}_albedo.png`);
    await grab(tex.normalMapUrl,    `${base}_normal.png`);
    await grab(tex.roughnessMapUrl, `${base}_roughness.png`);
    await grab(tex.metallicMapUrl,  `${base}_metallic.png`);
  }

  st.formats ||= {};
  for (const f of FORMATS) {
    if (st.formats[f]) continue;
    const u = await api(`/v2/tasks/${st.modelTask}/asset-url?type=Task&format=${f}`);
    const url = u.body?.result;
    if (!url) continue;
    if (await grab(url, `${base}.${f}${/\.zip(\?|$)/i.test(url) ? '.zip' : ''}`)) st.formats[f] = true;
  }

  st.done = true;
  st.tris = model.triangleCount;
  save();
}

// ---- main ------------------------------------------------------------------------------------
const jobs = worklist();
const todo = jobs.filter(j => !S(j.key).done);

console.log(`jobs           : ${jobs.length}`);
console.log(`already done   : ${jobs.length - todo.length}`);
console.log(`TO DO          : ${todo.length}  (~${todo.length * 42} credits)`);

if (PLAN) {
  const sample = todo.slice(0, 3);
  for (const j of sample) {
    console.log(`\n--- ${j.key}${j.chained ? '  (chained from ' + j.prevKey + ')' : ''} ---`);
    console.log(conceptPrompt(j.civ, j.lin, j.tier, j.chained));
  }
  process.exit(0);
}
if (!TOKEN) { console.error('\nNo --token.'); process.exit(1); }

// Chained lineages must run in order, so work is grouped: each worker takes a whole lineage and walks
// its tiers in sequence. Two lineages run side by side; two tiers of the SAME lineage never do.
const byLineage = new Map();
for (const j of todo.slice(0, LIMIT)) {
  const k = `${j.civ}/${j.lin.key}`;
  if (!byLineage.has(k)) byLineage.set(k, []);
  byLineage.get(k).push(j);
}
const groups = [...byLineage.values()];
console.log(`\n${groups.length} lineage group(s), concurrency ${CONC}\n`);

let ok = 0, failed = 0, expired = false;
const queue = [...groups];

async function worker() {
  while (queue.length && !expired) {
    const group = queue.shift();
    for (const j of group) {
      if (expired) break;
      try {
        await runUnit(j);
        ok++;
        console.log(`  OK    ${j.key}${j.chained ? '  <- ' + j.prevKey : ''}`);
      } catch (e) {
        if (e.message === 'TOKEN_EXPIRED') { expired = true; console.log('  !! token expired — stopping cleanly'); break; }
        failed++;
        S(j.key).lastError = e.message; save();
        console.log(`  FAIL  ${j.key}  ${e.message}`);
        break;   // a broken tier invalidates the rest of its lineage
      }
    }
  }
}

await Promise.all(Array.from({ length: Math.min(CONC, groups.length) }, worker));
console.log(`\ndone: ${ok} ok, ${failed} failed`);
if (expired) process.exit(2);
