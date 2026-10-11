# Unit models

`UnitModelLibrary` maps a `UnitType` to a model under `Assets/Resources/SpaceAssets/`, and
`UnitModelRenderer` instantiates it in the world. Anything with a model is skipped by
`UnitTokenRenderer`, so it never double-renders as a billboard as well.

## Where a unit's model comes from

Every civilization has its own art. For each unit, `UnitModelRenderer.CivPath` looks for:

    SpaceAssets/Stations/<Civ>/<Civ>_<UnitType>.glb    (any class with UnitInfo.isStation)
    SpaceAssets/Ships/<Civ>/<Civ>_<UnitType>.glb       (everything else)

`<Civ>` is one of `Terran`, `Aquarii`, `Pyrothian`, `Cryithn` or `Sylvan`
(`UnitModelRenderer.CivFolders`). The player's ships use the species the player picked; other
factions are mapped by `faction.id % 5`, so a given empire always looks the same.

Each hull ships as a three-file LOD chain: `_hi` (~24k tris), the base file (~9k tris, the only one
that carries textures) and `_lo` (~2.2k tris). `ShipLOD` gives the base file's materials to the other
two levels. A level that would barely differ from the base is not written, which is common for
stations. All of these files are produced by `tools/import-ship-models.mjs` from `Art/Active/`. Do
not hand-edit them; see `Art/README.md`. Orientation for every mesh is set in
`SpaceAssets/Ships/ship-meshes.txt`.

**As of 2026-10-10:** Terran, Aquarii and Pyrothian have all 29 classes (20 ships, 9 stations).
**Cryithn and Sylvan have none yet.** Their folders are empty, so those civilizations fly the legacy
fallback below.

### Legacy fallback (only when a civ's mesh is missing)

`UnitModelLibrary.Build()` still maps every class to one of the three meshes that shipped with the
project. It sets each class's size, motion and spin for every civ, but its mesh is only used when
`CivPath` finds nothing:

| Class | Fallback model | Motion |
|---|---|---|
| Every station (`UnitInfo.isStation`) | `SpaceAssets/Stations/LP Space Station` | orbits its host body + axial spin |
| Colony Ship, Frigate, Cruiser, Carrier, Dreadnought and other large hulls | `SpaceAssets/Ships/LP Colony Ship` | faces its course |
| Research line, Scouts, Explorer, Probe, Fighters | `SpaceAssets/Ships/LP Science Ship` | faces its course |

Size still varies by class (for example the research line runs 0.20 to 0.30), so tiers are
distinguishable even on borrowed hulls.

## Everything here is optional

If a model is missing or won't import, `Resources.Load` returns null and that class quietly falls back
to its billboard token, with one explanatory line in the console. Art is never load-bearing — the game
runs fine on a checkout with no models at all.

## Prefer .fbx over .blend

Unity does **not** read `.blend` itself: it shells out to a **local Blender install** to convert the
file to FBX at import time. On a machine without Blender the asset simply won't import. FBX needs no
external tool and imports anywhere, including on a build machine or a teammate's box.

That's why `LP Space Station.blend` lives in `Assets/ModelSource~/` rather than here. Unity ignores any
folder whose name ends in `~`, so the source file stays in version control without producing an import
error — and without colliding with the FBX (two assets at the same Resources path make
`Resources.Load` ambiguous).

## Sizing

Do not author to a particular scale — the renderer normalises it. `UnitModelRenderer.FitTo` scales each
model so its largest dimension matches `UnitModelLibrary.Entry.size` in world units.

Those sizes are deliberately small. `SystemVisualizer` scales a planet to `surfaceSize * 0.08` (min 0.6)
and a moon to `* 0.05` (min 0.35), so a **whole world is only ~0.6–2.2 world units across**. Anything
orbiting one has to be a few tenths of a unit or it dwarfs the planet. A Mega-Station tops out around
0.37 — roughly the size of a small moon, which is exactly what its description promises.

## Animation

If an FBX ships with its own clip (an `Animator` with a controller, or a legacy `Animation` with a
clip), the renderer plays it and skips its procedural motion. None of the current models (the
generated `.glb` fleet or the LP fallbacks) has clips, so all are animated procedurally: stations
orbit their host and spin on their axis; ships turn to face their course. Drop in an animated replacement at the same path and it takes over
automatically.
