# Art

Four folders. That is the whole system.

```
Art/
  Active/         the models we are using
  Alternatives/   models we generated and kept, but are not using
  Incoming/       where the generator writes, before anything is chosen
  _review/        contact sheets and diagnostic renders, not models
```

`Active/` and `Alternatives/` both hold **one folder per civilization**, and inside each, one folder
per ship class:

```
Art/Active/Aquarii/14-Fighter/          <- the fighter we use
Art/Alternatives/Aquarii/14-Fighter/    <- other fighters we generated
    cyborg-rebuild/
    account-archive/
```

An `Active/<Civ>/<Unit>/` folder holds one model in every format it was downloaded in — `.glb` for
the game, `.fbx` and `.obj` for DCC tools, `.stl` and `.3mf` for printing, all four PBR maps, the
concept art it was generated from, and the render Meshy made of it.

`Alternatives/<Civ>/<Unit>/` holds the same thing again, one folder per alternative, each named for
where it came from: `superseded-2026-08-20`, `reroll-2026-08-20-not-used`, `pre-creature`,
`lineage-drift`, `account-archive`, and so on.

`Alternatives/Misc/` is for anything whose civilization or ship class could not be determined —
mostly raw Meshy task folders named `texture`, `generate` or `image-to-3d-texture`, which are
experiments that were never tied to a hull.

## Where the game actually loads from

**Not from here.** The game loads `.glb` files out of
`Assets/Resources/SpaceAssets/Ships/<Civ>/` and `.../Stations/<Civ>/`.

Those are **built from `Art/Active/`** by the importer, which is a real transformation rather than a
copy: `Art/Active/` is ~17 GB of million-triangle meshes in five formats with 2-4K textures, and
everything under `Resources/` is loaded into the build whether or not it is used. The importer welds,
simplifies and writes a three-level LOD chain per hull (see
`dev-requests/planning/2026-08-22-ship-lod-and-rendering.md`):

| file | triangles | textures |
|---|---|---|
| `<Civ>_<Type>_hi.glb` | ~24,000 | none (borrows the base file's) |
| `<Civ>_<Type>.glb` | ~9,000 | base colour 1024, normal 1024, metallic-roughness 512 |
| `<Civ>_<Type>_lo.glb` | ~2,200 | none |

A level that would come out within 30% of the base is skipped (mostly fragmented stations).

**Measured 2026-10-11, after the second re-roll pass: 145 hulls, 409 `.glb` files, ~297 MB** (Ships 193 MB, Stations 104 MB) — about
2 MB per hull. The old "12 MB" figure was the pre-LOD pipeline (12k tris, 512/256 textures). That includes
Cryithn and Sylvan, added 2026-10-11. The files are tracked by Git LFS
(`Assets/Resources/SpaceAssets/**/*.glb` in `.gitattributes`), and the importer is byte-deterministic,
so re-importing unchanged art adds nothing to LFS storage.

```
node tools/import-ship-models.mjs          # Art/Active  ->  Assets/Resources/SpaceAssets/
node tools/import-ship-models.mjs --dry    # say what it would do, write nothing
```

So the rule is: **change what is in `Active/`, then re-run the importer.** Swapping a folder in
`Active/` and forgetting to import changes nothing the game can see.

## Promoting an alternative

To put an alternative into service, swap the folders and re-import:

```
# keep what is there now
mv Art/Active/Aquarii/19-Carrier Art/Alternatives/Aquarii/19-Carrier/previous-active

# put the alternative in its place
mv Art/Alternatives/Aquarii/19-Carrier/some-draw Art/Active/Aquarii/19-Carrier

node tools/import-ship-models.mjs
node tools/verify-wiring.mjs
```

Then check the orientation line for that hull in
`Assets/Resources/SpaceAssets/Ships/ship-meshes.txt` — a different draw can face a different way, and
that file is what stops it flying backwards. `node tools/ship-silhouettes.mjs` re-renders the sheet
to read it off.

## Nothing is deleted

Every model that has ever been generated is kept, in every format it was downloaded in. When a hull
is replaced, the old one moves to `Alternatives/` rather than going away — a re-roll is a fresh draw,
not an improvement, and on 2026-08-20 two of six came back worse and were put straight back.

The one thing that does get removed is a byte-identical duplicate: once a staged model in `Incoming/`
has been promoted into `Active/`, the staging copy is the same file twice and is cleared.

## The tools

| what | command |
|---|---|
| generate a fleet | `node tools/meshy-rebuild-batch.mjs --api-key-file tools/meshy-api-key.txt --only <Civ>` |
| import into the game | `node tools/import-ship-models.mjs` |
| check the LOD chains | `node tools/inspect-ship-lod.mjs --civ <Civ>` |
| check the wiring | `node tools/verify-wiring.mjs` |
| check the textures | `node tools/verify-textures.mjs` |
| look at a whole civ | `node tools/contact-sheet.mjs --dir Art/Active/<Civ> --match thumbnail` |
| find which way a hull faces | `node tools/ship-silhouettes.mjs [--match "^<Civ>_"]` and `node tools/measure-bow.mjs` |
| re-pull everything from the Meshy account | `node tools/meshy-archive-tasks.mjs` |
| draw the command symbols | `node tools/make-command-icons.mjs` |
| check every control has a symbol | `node tools/verify-command-ui.mjs` |
| look at the formations | `node tools/formation-check.mjs` |

The last three are here rather than with the code checks because what they produce is a **picture**,
and the picture is the point. `make-command-icons` renders a contact sheet at 24 pixels as well as 72
— the size the bar actually draws them — which is what catches an icon too thin to survive the
downscale, or two controls that have quietly ended up sharing a silhouette. `formation-check` draws
all seven formations at three squadron sizes, and has now found two real faults nobody could have seen
in source: a Globe packing two ships closer than the collision rule allows, and the cheapest hull
being assigned to a wingtip instead of the point.

## Fleet status and Meshy credits

*Last updated 2026-10-11, after the second re-roll pass (see "Re-roll pass 2" below).* `verify-textures`: **ok 85, weak 60, FAIL 0**. Before the pass it was ok 75, weak 68, FAIL 2.

| civ | hulls with art | notes |
|---|---:|---|
| Terran | 29/29 | |
| Aquarii | 29/29 | 19, 26 and 28 re-rolled 2026-10-11. Second pass: 12, 13, 16, 21, 25 and 27 replaced. Amber now reads 5.9-28% on those. Amber is still the fleet-wide weak point: 13 hulls are still WEAK on it |
| Pyrothian | 29/29 | the six pre-"Mordor" hulls (05, 12, 13, 27, 28, 29) regenerated, and 01-Scout and 14-Fighter re-rolled, 2026-10-11. Every Pyrothian hull passes. Second pass: 28-Mega-Station is now a stepped ziggurat and 29-Hyper-Speed-Relay an upright gate. They are no longer two look-alike spiked discs |
| Cryithn | 29/29 | generated 2026-10-11. Second pass: the two real FAILs were re-rolled and now pass. 20-Dreadnought went from 1.2% violet to 10.6%, and 28-Mega-Station from 0.7% to 10.2%. 11, 12 and 13 were also replaced. No hull reads *secondary swamped* any more: Glacier Light is now also keyed on brightness (`keyMinValue`) |
| Sylvan | 29/29 | generated 2026-10-11. 18, 22, 23 and 26 are second draws. The first draws failed on Blossom Violet and are kept in `Alternatives/`. Second pass: 10, 17, 28 and 29 replaced. Only 14 and 19 are still WEAK |

`verify-textures` rewrites `tools/retexture-worklist.json` on every run (it used to skip the write when
nothing failed, which left a fixed fleet listed as broken). After the second pass it is empty.

**The Cryithn palette conflict** (history: it is resolved now, because `keyMinValue: 0.85` keys Glacier Light on brightness as well as hue). The Cryithn hull is glacier blue (`baseHue` 205) and its secondary,
Glacier Light, keys at hue 190: 15 degrees apart, inside the 28-degree key tolerance. So every blue hull
pixel counts as "secondary", and the hulls read 46-90% secondary however good the art is. The art is
what was asked for (blue light inside blue ice), so re-rolling can't fix it. The mask extractor will
have the same problem: the glow can't be recoloured separately from the hull by hue. Fix it in
`civ-colors.json`. Either move Glacier Light off blue (and lose the blue glow), or key the Cryithn
secondary on brightness instead of hue. `verify-textures` now prints a `PALETTE CONFLICT` line for any
civ in this state.

**Ember Crimson fix (Pyrothian).** The Pyrothian secondary `role` used to read "iron banding and
riveted straps", and the model painted those as black iron. It now reads "DEEP CRIMSON lacquered armour
bands and riveted plates (blood-red, not orange)", and every hull generated with it passes.

**Credits spent so far:** ~3,400 on the web-API rebuild (81 hulls, `tools/meshy-rebuild-state.json`),
~40 on the texture batch, ~200 on early experiments, and **2,964 on 2026-10-11** through the public API
(balance 3,002 -> 38). That was 76 hulls at exactly 39 credits each (9 for the nano-banana-pro concept,
30 for meshy-6 image-to-3D with PBR):

| batch | hulls | credits |
|---|---:|---:|
| Pyrothian, the six unfinished hulls | 6 | 234 |
| Pyrothian stations 27-29 again (the first chained draws were the same disc three times, kept as `station-clone-2026-10-11-not-used`) | 3 | 117 |
| re-rolls: Aquarii 19/26/28, Pyrothian 01/14 | 5 | 195 |
| Cryithn, full fleet | 29 | 1,131 |
| Sylvan, full fleet | 29 | 1,131 |
| Sylvan re-rolls of four FAILs: 18, 22, 23, 26 | 4 | 156 |

**Still to generate:** nothing.

### Re-roll pass 2 (2026-10-11)

Balance **1,038 -> 63**: 25 hulls at 39 credits each, 975 credits, run as two batches (12, then 13).
Each draw was checked with `verify-textures --dir Art/Incoming --verbose` and on a sheet of new and old
thumbnails side by side. A draw was kept only if it verified better **and** looked at least as good.
**17 were kept and 8 were not used.**

- Replaced models: `Alternatives/<Civ>/<Unit>/superseded-2026-10-11b` (batch 1) or `superseded-2026-10-11c`
  (batch 2).
- Rejected draws: `.../reroll-2026-10-11b-not-used` or `reroll-2026-10-11c-not-used`. Their state entries
  carry the same `#tag`.
- The replaced hulls' state entries are tagged `#pre-reroll-2026-10-11b` or `#pre-reroll-2026-10-11c`.

| hull | main accent, before -> after | result |
|---|---|---|
| Cryithn 20-Dreadnought | 1.2% FAIL -> 10.6% ok | kept |
| Cryithn 28-Mega-Station | 0.7% FAIL -> 10.2% ok | kept |
| Cryithn 11-Mining-Barge | 4.7% -> 10.1% ok | kept |
| Cryithn 12-Transport | 3.3% -> 7.4% WEAK | kept |
| Cryithn 13-Terraformer | 4.6% -> 8.1% ok | kept |
| Cryithn 25-Multi-Role-Station | 5.4% -> 5.1% | not used (worse) |
| Cryithn 29-Hyper-Speed-Relay | 5.4% -> 2.7% | not used (worse) |
| Pyrothian 28-Mega-Station | ok -> ok | kept: now a stepped ziggurat with the eye on its spire |
| Pyrothian 29-Hyper-Speed-Relay | ok -> ok | kept: now an upright gate, not a disc |
| Aquarii 12-Transport | 5.0% -> 7.6% WEAK | kept |
| Aquarii 13-Terraformer | 3.8% -> 18.7% ok | kept. This was the second draw; the first came back as a fish (not used) |
| Aquarii 16-Fighter-MkIII | 2.9% -> 28.1% ok | kept |
| Aquarii 21-Battle-Station | 3.4% -> 5.9% WEAK | kept |
| Aquarii 25-Multi-Role-Station | 3.8% -> 7.9% WEAK | kept |
| Aquarii 27-Deep-Space-Station | 4.5% -> 7.6% WEAK | kept: a truer glass-sponge lattice |
| Aquarii 28-Mega-Station | 3.7% -> 12.5%, but magenta 8.3% -> 0.67% | not used: one flat gold block, and it lost its lights |
| Aquarii 05-Probe | 4.0% -> 16.7% | not used: came back as a fish, not a nautilus |
| Aquarii 10-Colony-Ship | 5.2% -> 5.5% | not used: no real gain, and it lost the habitat dome |
| Aquarii 15-Fighter-MkII | 3.4% -> 13.1% | not used: no hammerhead, and 16 was refitted from the old 15 |
| Aquarii 29-Hyper-Speed-Relay | 5.4% -> 8.9% | not used: a flat starfish, weaker than the curled arms |
| Sylvan 10-Colony-Ship | 3.3% -> 18.7% ok | kept |
| Sylvan 17-Frigate | 5.4% -> 17.0% ok | kept |
| Sylvan 28-Mega-Station | 3.7% -> 6.1% WEAK | kept |
| Sylvan 29-Hyper-Speed-Relay | 5.3% -> 10.9% ok | kept |

What changed in the prompts. Every change was kept short, because a longer accent `role` pushes the
civ signature out of the 800-character cap.

- **`civ-colors.json` roles.** These primary roles changed:
  - Aquarii: "BRIGHT AMBER-GOLD armour plates over the back, flanks and dorsal fin".
  - Cryithn: "VIOLET facet inlays and the violet dorsal spine".
  - Sylvan: "the broad VIOLET leaf-sail and canopy panels".

  The Cryithn secondary, the blue light in the ice, is unchanged.
- **`ship-design.json` creatures.**
  - Cryithn 20 and 28 no longer say "Great CLEAR CRYSTAL shards", which pulled the hull away from
    violet. They now say "A VIOLET crystal spine runs its length" and "VIOLET-banded rings".
  - Pyrothian 28 is now "a towering stepped ZIGGURAT forge-city", and 29 is "a colossal UPRIGHT GATE
    standing on edge".
  - The Aquarii 13 jellyfish now says "(a domed bell, NOT a fish)".

**Two new stations would not simplify.** Their meshes are too fragmented, like the Terran Carrier's, so
the importer wrote no `_hi` or `_lo` level for either:

- Pyrothian 29-Hyper-Speed-Relay ships as one 121,000-triangle level (6.9 MB).
- Pyrothian 28-Mega-Station ships as one 60,000-triangle level (3.9 MB).

Each is a single static station. If one shows up in profiling, move `superseded-2026-10-11b` back into
`Active/` for that hull.

63 credits remain, all permanent; the monthly allowance is used up.

### Running the pipeline with the API key (the way it works now)

`tools/meshy-api-key.txt` holds a Meshy developer key (`msy_...`). It is gitignored. Never print it,
copy it into another file, or send it anywhere but `https://api.meshy.ai`. The batch reads it at runtime:

```
node tools/meshy-rebuild-batch.mjs --plan --api-key-file tools/meshy-api-key.txt --only Cryithn   # prompts, free
node tools/meshy-rebuild-batch.mjs --api-key-file tools/meshy-api-key.txt --only Cryithn --concurrency 5
node tools/meshy-rebuild-batch.mjs --api-key-file tools/meshy-api-key.txt --units Sylvan/18-Cruiser,Sylvan/22-Research-Station
```

It prints the balance before and after. What each hull costs on the public API (`/openapi/v1`):

| step | endpoint | settings | credits |
|---|---|---|---:|
| concept, root tier | `POST /text-to-image` | `nano-banana-pro` (from `ship-design.json` `conceptModel`), 16:9 | 9 |
| concept, chained tier | `POST /image-to-image` | `input_task_id` = the parent's concept task, or the parent's local `_concept.png` as a data URI if the parent was made on the web API | 9 |
| concept -> 3D | `POST /image-to-3d` | `input_task_id` = the concept, `ai_model: meshy-6`, `enable_pbr`, `image_enhancement: false`, no texture prompt (so the texture comes from the concept), all five formats | 30 |

Things that differ from the web pipeline:

- **Only refit lineages chain.** The scout, fighter and research lines still pass each tier's concept to
  the next. The public image-to-image is an *edit* of its reference, much stronger than the web app's
  referenced generation. Chaining the stations made three identical discs. So the stations and the
  family-mode battle line are generated from text, and each one runs as its own parallel job.
  `--chain-all` puts the old behaviour back.
- **meshy-6 adds an emission map** (`_emission.png`, also embedded in the `.glb`), and the importer keeps
  it at 512 px. That is the lava glow, the light inside the ice and the bioluminescent veins.
  `meshy-7.1`/`latest` return no emission map. Change the model with `--model`.
- **Resume and pay-once rules are unchanged.** Every task id goes into `meshy-rebuild-state.json` as soon
  as it is created (entries carry `api: true`), and a re-run polls it instead of paying again. A `FAILED`
  task (refunded by Meshy) is forgotten so the next run makes a fresh one. A `402` (out of credits) or
  `401` (bad key) stops the whole run cleanly, and re-running carries on.
- The same state file and the `#tag` re-roll rule apply (below). `--units` takes a comma list of
  `Civ/NN-Unit` keys.

The old web-token mode (`--token-file tools/meshy-token.txt`) is still in the script. It needs a browser
JWT that lives 15 minutes. `meshy-texture-batch.mjs` and `meshy-archive-tasks.mjs` have **not** been
ported and still only speak the web API.

### After a batch

The output lands in `Art/Incoming/<Civ>/<Unit>/`. Then:

1. `node tools/verify-textures.mjs --dir Art/Incoming --verbose` and
   `node tools/contact-sheet.mjs --dir Art/Incoming/<Civ> --match thumbnail`. Look before you promote.
   (Running the verifier on `Incoming` rewrites the worklist, so finish with a run on `Active`.)
2. Move it to `Art/Active/<Civ>/<Unit>/`. Anything it replaces goes to `Art/Alternatives/<Civ>/<Unit>/<reason-date>/`.
3. `node tools/import-ship-models.mjs`
4. Add one line per new hull to `Assets/Resources/SpaceAssets/Ships/ship-meshes.txt`, read off
   `node tools/ship-silhouettes.mjs --match "^<Civ>_"`, `node tools/measure-bow.mjs` and the concept
   sheet (`contact-sheet.mjs --match concept`). Every fleet so far exports with the nose on **-X**, so
   `0 90 0` is the default and the exceptions get a comment. **Do not re-run
   `make-orientation-manifest.mjs`**: it rewrites the whole file from bounding boxes and would wipe the
   silhouette-verified lines. If you want its suggestions, copy `ship-meshes.txt` aside, run it, take
   only the new lines, and restore your copy.
5. `node tools/verify-textures.mjs`, `node tools/verify-wiring.mjs`, `node tools/inspect-ship-lod.mjs --civ <Civ>`.
6. `git check-attr filter -- <new .glb>` should say `lfs`. Everything under
   `Assets/Resources/SpaceAssets/**/*.glb` does.

`UnitModelRenderer.CivFolders`, the importer and the verifier already know about `Cryithn` and `Sylvan`
(note the folder is `Sylvan`, though the species is shown as "Sylvans"). No code change was needed for
the new fleets to load.

To re-roll a hull that is already marked `done`, first move its entry in `meshy-rebuild-state.json`
aside, for example by renaming the key to `Aquarii/19-Carrier#2026-08-20`. If you don't, the batch
skips it.
