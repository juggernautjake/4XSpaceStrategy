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

---

# The recommended fixes, applied — 2026-08-22 (later)

## G. A compile error I introduced, and the tripwire that should have caught it

- [x] **G1.** Three tooltips in `FleetCommandBar` had **real newlines where `\n` escapes belonged** —
      `error CS1010: Newline in constant`, each followed by a cascade of spurious syntax errors
      pointing everywhere except at the fault. My own doing: a scripted edit written as a shell
      command whose `\n\n` bash expanded before node ever saw it
- [x] **G2.** `Check-Scripts.ps1` reported clean throughout. It checks structure, braces and enum
      ordering, and an unterminated string literal is none of those. In a project with no compiler,
      anything the tripwires miss is found by the person trying to play the game
- [x] **G3.** `tools/check-string-literals.mjs` closes it. Counts quotes per line having removed the
      ones that are not delimiters — escaped quotes, char literals, comments — and skips verbatim
      strings, which legitimately span lines. **Verified by re-injecting the exact fault**, because a
      checker that has only ever passed proves nothing

## H. LOD levels that were copies of each other

- [x] **H1.** Some meshes cannot be simplified. The Terran Carrier is **797,000 triangles across
      473,000 vertices with nothing welded** — every triangle its own island — and meshopt floors it
      at ~24,500 whatever ratio, error tolerance or border locking it is given. Measured, not assumed:
      error 0.04 → 24,542; error 1.0 with borders unlocked → 24,540
- [x] **H2.** The importer now writes a level only if it differs from the base by 30%, and deletes any
      stale sibling from a previous run. **Twenty levels turned out to be copies, seventeen of them
      stations** — which follows, since a station is masts and dishes and rings and so is the most
      fragmented geometry in the game
- [x] **H3.** The verifier reads the importer's own record to tell a deliberate skip from a missing
      file. It cannot derive that itself — whether a level was worth writing depends on the triangle
      count the simplifier actually reached, a fact about the source mesh only the import run knows.
      The first version guessed from the budgets and **cried wolf on exactly the two hulls the
      importer had handled correctly**

## I. The verifier was only checking half the fleet

- [x] **I1.** `inspect-ship-lod.mjs` scanned `Ships/` and never `Stations/`, so **nine hulls per
      civilisation went unexamined** — and they are precisely the ones with the fragmented meshes that
      needed examining. Now 29 hulls per civ instead of 20

## J. Git LFS

- [x] **J1.** The art is tracked by LFS. Git history stops accumulating a fresh copy of the fleet on
      every import, clones fetch only the version they check out, and pushes resume instead of
      disconnecting — the 170 MB push had failed twice and needed three attempts
- [x] **J2.** **History is NOT rewritten.** The existing blobs stay where they are. Shrinking the repo
      for real needs `git lfs migrate import`, which rewrites shared history and needs a force push —
      not a thing to do to a pushed branch unasked. Say the word and it is one command

### And the quota worry turned out to be mostly unfounded

**The importer is byte-deterministic.** Re-running it over unchanged source produces byte-identical
output and git reports **zero changes** — verified by re-importing all 87 hulls and comparing.

So the 172 MB is a **one-time** cost, not a per-import one. Storage only grows when the source art or
the import settings actually change, which is when it should. GitHub's 1 GB free LFS tier is
comfortable at that rate rather than five imports from exhaustion.

## K. Not done, and why

- [ ] **Mesh quantization** (`KHR_mesh_quantization`) would cut geometry roughly in half — call it
      40 MB off the fleet — with no visible loss. Not applied: there is no Unity here to confirm
      gltfast decodes it, and the failure mode is every ship in the game failing to load. Worth doing
      as a **one-hull trial** that can be looked at before the other 86 follow
- [ ] **Tangent generation.** The source meshes carry `POSITION, NORMAL, TEXCOORD_0` and no `TANGENT`,
      so gltfast computes them at load. Unity's generator is not mikktspace, which is what the normal
      maps were almost certainly baked against, so there is a small fidelity loss. Baking tangents
      would add ~16 bytes per vertex across three levels — a real size cost for a subtle gain
- [ ] **LOD thresholds are calibrated but not eyeballed.** They come from measured pixel footprints;
      whether the swap lands where it feels right is a question only Unity can answer

---

# The eighteen errors were already fixed — and the tripwire was not wired in — 2026-08-22 (later still)

> *"Assets\Scripts\UI\FleetCommandBar.cs(218,14): error CS1010: Newline in constant"* … and seventeen
> more, reported from Unity.

## L. The errors are the pre-fix file, and the fix is already on `main`

- [x] **L1.** All eighteen are `a71d7e5`'s copy of `FleetCommandBar.cs`. The line and column numbers
      match it **exactly** — 218,14 is the opening quote of
      `"hulls in front of a capital ship does not soak the fire meant for it.` in that revision, and
      220, 232, 234, 239, 241 and 254 line up the same way. In `a7ddc55` those lines carry `\n\n`
      escapes and the file is clean
- [x] **L2.** So nothing needed fixing in the code: the working tree is at `e2153ef`, which is
      `a7ddc55` plus a README line, and it is pushed. **The build being compiled is one commit stale.**
      `git pull` is the whole of it

## M. What DID need fixing: the checker was never called

- [x] **M1.** `tools/check-string-literals.mjs` was written to close exactly this gap and then left
      sitting beside the script nobody had wired it into. `Check-Scripts.ps1` — the one command this
      project's whole no-compiler workflow runs before a commit — never called it. A tripwire that has
      to be remembered separately is a tripwire that gets forgotten on the day it matters, which is
      the day somebody is in a hurry
- [x] **M2.** It is now the fourth check. Delegated rather than ported: two implementations of one
      rule drift, and the one that drifts is the one nobody is running
- [x] **M3.** **A skipped check exits 1.** If `node` is not on `PATH` the script says so and fails
      rather than printing "Clean." with a footnote. This script exists to stand in for a compiler
      that is not here; "Clean." has to mean all four checks ran
- [x] **M4.** **Verified in both directions.** Re-injected the exact fault — `\n\n` turned back into
      real newlines in that same tooltip — and the check reported `FleetCommandBar.cs:218` and `:220`,
      the same two lines Unity did, and exited 1. Then emptied `PATH` and confirmed the skip path
      fails with its reason rather than crashing
- [x] **M5.** And a fifth check, **STATIC**, written while sweeping for anything else that would fail
      to build: every `SomeType.Member` reference onto a project type must name a real member. It is
      the ENUMS check applied to classes, which is where a refactor actually bites — rename a static
      method and every call site still looks perfectly well-formed. `tools/check-static-refs.mjs`.
      **The codebase passes it**, which is the useful part of the answer to "is anything else broken":
      2,000-odd static references across 227 files, 227 checkable types, nothing dangling.
      Verified by dropping a file referring to `Squadrons.ThisMemberDoesNotExist` into the tree and
      watching it fail. Its three first-pass false positives are all C# shapes a naive member scan
      gets wrong, and are written down in the script's header
- [x] **M6.** One trap found on the way, and it is worth writing down: **`Check-Scripts.ps1` is UTF-8
      with no BOM, and Windows PowerShell 5.1 decodes it as ANSI.** A non-ASCII character inside a
      *code string* becomes mojibake the parser chokes on — the first version of this change died on
      an em dash. Inside comments it is harmless, which is why the ones already in the file are fine.
      Code strings in that file stay ASCII

### And the injector hit the original bug on the way to testing it

The first attempt to re-inject the fault was a heredoc containing `\\n`, and the shell collapsed it to
`\n` before node ever saw it — **the identical mechanism that produced the original defect**, in the
act of testing the check for it. The injector now builds the backslash from `String.fromCharCode(92)`.
The lesson is narrow and repeatable: never put an escape sequence in a shell-authored edit. Write the
file with a tool that does not interpret it.
