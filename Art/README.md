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
copy: source art is 1.8 GB of million-triangle meshes with 4K textures, and everything under
`Resources/` is loaded into the build whether or not it is used. The importer welds, decimates to
~12k triangles and downscales textures to 512/256, which is what makes the fleet 12 MB instead of a
couple of gigabytes.

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
| generate a fleet | `node tools/meshy-rebuild-batch.mjs --token-file tools/meshy-token.txt --only <Civ>` |
| import into the game | `node tools/import-ship-models.mjs` |
| check the LOD chains | `node tools/inspect-ship-lod.mjs --civ <Civ>` |
| check the wiring | `node tools/verify-wiring.mjs` |
| check the textures | `node tools/verify-textures.mjs` |
| look at a whole civ | `node tools/contact-sheet.mjs --dir Art/Active/<Civ> --match thumbnail` |
| find which way a hull faces | `node tools/ship-silhouettes.mjs` |
| re-pull everything from the Meshy account | `node tools/meshy-archive-tasks.mjs` |
