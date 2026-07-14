# Unit models

`UnitModelLibrary` maps a `UnitType` to a model under `Assets/Resources/SpaceAssets/`, and
`UnitModelRenderer` instantiates it in the world. Anything with a model is skipped by
`UnitTokenRenderer`, so it never double-renders as a billboard as well.

Current mapping:

| Class | Model | Motion |
|---|---|---|
| Every station (`UnitInfo.isStation`) | `SpaceAssets/Stations/LP Space Station` | orbits its host body + axial spin |
| Colony Ship | `SpaceAssets/Ships/LP Colony Ship` | faces its course, idle bob |

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
clip), the renderer plays it and skips its procedural motion. Neither current model has clips, so both
are animated procedurally: stations orbit their host and spin on their axis; the colony ship turns to
face its course and bobs gently. Drop in an animated replacement at the same path and it takes over
automatically.
