# Station models

`StationModel.Path` (`SpaceAssets/Stations/LP Space Station`) is loaded at runtime by
`StationModelRenderer` and used as the mesh for every deployed space station. If nothing importable
exists at that path, stations silently fall back to their billboard tokens — the game never breaks
over a missing model.

## Important: Unity needs Blender installed to import a `.blend`

Unity does not read `.blend` files itself. It **shells out to a local Blender install** to convert the
file to FBX at import time. On a machine without Blender, `LP Space Station.blend` will fail to import,
`Resources.Load` returns null, and you'll get billboard tokens plus one explanatory line in the console.

Two ways to get the model rendering:

1. **Install Blender** (any recent version; the file is Blender 4.04). Unity picks it up automatically
   and imports the `.blend` on the next asset refresh. Nothing else to change.
2. **Export to FBX** — open the `.blend`, `File > Export > FBX`, and save it into this folder as
   `LP Space Station.fbx`. FBX needs no external tool, so it imports anywhere, including on a build
   machine or a teammate's box that has no Blender. This is the better option if anyone else is going
   to build the project.

Either way the path stays the same, so no code changes are needed — `Resources.Load` finds whichever
importable asset sits at `SpaceAssets/Stations/LP Space Station`.

## Notes

- The renderer normalises scale: whatever size the model is authored at, it's fitted so its largest
  dimension is a known number of world units, then scaled up by the station's tier. So the model does
  not need to be built to any particular scale.
- Materials are tinted 35% toward the owning faction's colour so allegiance stays readable.
