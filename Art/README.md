# Art — the ship generations, and which one is which

Three generations of ship art exist. **Nothing is ever deleted**: a hull that looks wrong today may be
the one that looks right once it is 40 pixels across in the game, and regenerating costs credits.
Everything is kept and labelled instead.

None of this folder is committed — see `.gitignore`. It is several gigabytes of binary that would
bloat the repository forever, and every file in it is reproducible from `tools/` plus credits. The
folder is the working store; the *recipe* is what is version-controlled.

---

## GEN 1 — the original library
**`C:\Users\lando\Downloads\4X-Ship-Models`** — 4.9 GB, 1,191 files, 145 unit folders.

The first pass, made before this session. **Left exactly where it is, untouched**, because it is the
only copy and 40 of its units are genuinely good.

Per unit: `concept/`, `unity/*.glb`, `blender/*.fbx`, `obj/`, `textures/`, `print/`, and a
`PROMPT.txt` recording the exact prompt, the Meshy task id and the generation settings.

- **40 units are textured and good** — Terran 02–29, Aquarii 01–12.
- **100 units are geometry only**, no materials at all.
- 5 Sylvan stations (25–29) were never made; Terran 01-Scout has no `.glb`.
- Quality is uneven even among the textured ones: `Terran/19-Carrier` is a literal naval aircraft
  carrier, and several Aquarii hulls are shapeless.

## GEN 2 — `Art/MeshyTextured/`
The abandoned experiment: uploading Gen 1 meshes back to Meshy and running the texture phase on them.

**16 units, 1 usable.** Meshy largely ignores livery instructions on geometry it did not author and
returns either a greyscale hull or one flat colour. Kept because `Aquarii/14-Fighter` came out well
and because the albedos are a useful before/after against Gen 3.

Do not build on this generation. It is a record of a dead end, not a source.

## GEN 3 — `Art/MeshyRebuilt/`
The current approach, and the one that matches the quality bar: **concept art → image-to-3D**, which
is how the best Gen 1 ships were made. Regenerates geometry as well as texture, so it also fixes hulls
that were bad on their own terms.

Generated in lineage order — each Mk II is an image-to-image refit of its Mk I, so an upgrade looks
like the same ship with more bolted on. Per unit:

    <Civ>_<Hull>_concept.png     the concept art
    <Civ>_<Hull>_thumbnail.png   Meshy's render — this is the UI thumbnail
    <Civ>_<Hull>_albedo.png      + _normal / _roughness / _metallic
    <Civ>_<Hull>.glb             the game asset
    <Civ>_<Hull>.fbx.zip         DCC tools
    <Civ>_<Hull>.obj.zip         anything that will not read glTF
    <Civ>_<Hull>.stl / .3mf      3D printing

`Art/MeshyRaw/` is scratch space for the decimation pipeline and holds nothing of record.

---

## Which one does the game use?

Neither directly. `tools/import-ship-models.mjs` decimates a chosen generation into
`Assets/Resources/SpaceAssets/`, because raw Meshy output is unusable in-engine — one Terran
Dreadnought is 1,996,570 triangles and 99 MB, for a ship drawn at 0.09–0.40 world units. The importer
takes it to ~12,000 triangles and a few hundred KB.

So the pipeline is: **Gen 3 → import → `Resources/`**, with Gen 1 as the fallback for any unit Gen 3
has not reached yet.

## Checking a generation

    node tools/verify-textures.mjs --dir Art/MeshyRebuilt --verbose

Scores every albedo for brightness, detail and whether both livery accents actually landed where the
mask extractor can find them, and writes a re-do worklist for the failures. Use it before importing —
it is much more reliable than judging 140 thumbnails by eye.
