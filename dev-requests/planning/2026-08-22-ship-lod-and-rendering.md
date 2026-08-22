# Why the ships look blobby, and what actually fixes it — 2026-08-22

> *"It seems like the models for the ships are looking kind of blobish in unity... could we work on
> optimizing the ship models so that when we zoom further out the ships have less detail, but as we
> zoom further in, the ships gain more and more detail dynamically."*
>
> *"So do we just need to make everything much much bigger? Can we do that easily? Can we scale the
> planets and asteroids and effects and stuff all to be way bigger models?"*

---

## A. The diagnosis, measured rather than guessed

`tools/inspect-ship-lod.mjs` reads the shipped `.glb` files and computes what the screen actually asks
for. At 1080p, a 0.40-unit dreadnought:

| camera height | on screen | against a 512 base colour |
|---|---|---|
| framing a system | 9 px | texture to spare |
| close orbit | 94 px | texture to spare |
| very close | 374 px | about right |
| free-look floor | **1,069 px** | **magnified 2.1x — blurry** |
| absolute floor | **9,353 px** | **magnified 18x — blurry** |

The old pipeline shipped **12,000 triangles, a 512 base colour and a 256 normal map**, justified in its
own header by "ships are drawn at between 0.09 and 0.40 world units" and "four thousand pixels of
texture on forty pixels of ship is about a hundredfold waste."

Every word of that is true of the view it was written against, and it has one hole:
**`CameraController.minHeight` is 0.04 world units.** The camera can get closer to a hull than the hull
is long. The same asset was being asked to be a nine-pixel speck AND a thousand-pixel hero, and at the
second one a 256 normal map — where essentially all surface detail lives — carries a quarter of what
the view needs. **That is the blob.** It was never mainly a triangle-count problem.

## B. No, making everything bigger would not have helped

Scaling ships, planets, orbits and the camera together is a **no-op**. What decides how many pixels a
thing covers is the RATIO of its size to the camera's distance, not either number alone — scale both
by ten and every pixel on screen is identical. It would have been a large, risky refactor of
generation, orbits, camera limits and collider floors in exchange for exactly no visual change.

Two things a scale change WOULD do, neither of which is the problem here:

- **Depth precision.** At a camera height of 0.04 the near plane has to be tiny, which compresses the
  depth buffer and invites z-fighting. Real, but it bites at extreme zoom only.
- **Relative size is art direction, not optimisation.** If ships should look *bigger next to planets*,
  that is one table — the size ladder in `UnitModelLibrary.Build()` — and it is a five-minute change
  with no pipeline work at all. Worth doing if the fleet reads too small; it is a separate question
  from sharpness.

## C. What was built

- [x] **A three-level LOD chain per hull**, emitted by the importer:

  | file | triangles | textures | when |
  |---|---|---|---|
  | `name_hi.glb` | ~24,000 | none | hull is big on screen |
  | `name.glb` | ~9,000 | **all of them** | ordinary view, and the fallback |
  | `name_lo.glb` | ~2,200 | none | a speck among many |

- [x] **Textures live on the MID file**, and that is the load-bearing decision. It makes the base file
      self-sufficient: a civ not yet re-imported, or a hull whose siblings failed to write, loads and
      looks exactly as it does today. LOD is a pure addition that can be absent
- [x] **One texture set shared by all three levels.** `ShipLOD` hands the base file's materials to the
      others at load. Three copies of a 1024 normal map would triple texture memory to store the same
      image three times — and would reintroduce the exact artefact LOD exists to hide, since the levels
      would visibly change resolution as they swapped
- [x] **Base colour and normal 512/256 → 1024/1024**, metallic-roughness 256 → 512
- [x] **Simplifier error tolerance 0.02 → 0.004** at the top level. Two percent of a mesh's size is an
      enormous licence on a hull whose panel lines are millimetres, and it is why silhouettes came out
      soft even at triangle counts that should have held them
- [x] **Normal maps encoded at quality 95 with no chroma subsampling.** A normal map is a vector field
      stored as colour; JPEG's chroma subsampling does not merely soften it, it TILTS the normals and
      the surface picks up a quilted shimmer under a moving light
- [x] **Cross-fade between levels** so the swap is not a pop

## D. Two bugs found on the way, one of them mine

**The high-detail mesh shipped with no UVs.** Stripping textures orphaned the `TEXCOORD_0` accessor,
and the next `prune()` collected it — entirely reasonably. Since the LOD levels adopt the base file's
textured material, every vertex would have sampled the same texel: the most detailed hull in the game
would have rendered as a **flat single-coloured blob**, which is precisely the defect this whole change
exists to fix. Geometry is now cleaned first, while the UVs are still referenced, and textures are
dropped afterwards with a prune restricted to texture-shaped properties.

**Every player-liveried ship had lost its mipmaps.** `CivLivery.ReadableCopy` built its copy with
`mipChain: false`, so the repainted texture had no mip levels at all — it sampled one texel out of a
thousand and picked a different one every frame the ship moved. That is the shimmering, crawling noise
that reads as "the model is buzzing", and it affected **the player's own fleet and nobody else's**. It
also got four times worse the moment base colour went to 1024. Now built with mips, regenerated from
the repainted pixels (without that, a liveried ship would fade back to its factory colours at
distance), trilinear, 4x anisotropic, and DXT-compressed — which takes the player's civ from ~150 MB of
uncompressed texture to about a quarter of that.

## E. Cost, honestly

**Ship art went from 12 MB to 170 MB** for 87 hulls — about 2 MB per hull for all three levels. At the
planned 145 hulls that is roughly 290 MB.

That is the price of a hull that holds up at a thousand pixels. The knob is `LODS` at the top of
`tools/import-ship-models.mjs`: dropping `_hi` to 16,000 triangles or base colour to 768 would take a
large bite out of it. Nothing else needs to change to try it — re-run the importer.

`Terran Carrier` is flagged as a chain that buys nothing: its mesh is built from so many disconnected
shells that the simplifier cannot collapse it, so all three levels come out ~24,000 triangles. The fix
for that one is in the source art, not the pipeline.

## F. Still open

- [ ] **Meshes have no TANGENT attribute** — the Meshy source ships `POSITION, NORMAL, TEXCOORD_0`
      only. glTF says a renderer should compute them, and gltfast does, but tangents generated from
      smoothed normals are not mikktspace-exact and normal-mapped detail suffers slightly for it
- [ ] No **impostor level** below `_lo` — a hull under a few pixels still draws 2,200 triangles when a
      textured quad would do
- [ ] The **LOD thresholds are calibrated but not eyeballed.** They come from measured pixel footprints;
      whether the swap happens where it feels right is a thing only Unity can answer
