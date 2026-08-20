# Ship Art, Livery Colours and Combat VFX

**Status:** In progress
**Opened:** 2026-08-19
**Requested by:** Jacob Maddux (live session)

Everything asked for across one long working session, in one place. Four workstreams that only
loosely depend on each other — **C (VFX)** needs no art and no Meshy credits, so it can be built
while **A** is blocked.

---

## 0. The thing that was nearly got wrong

**Almost the entire fleet already existed and nobody looked.** Before generating anything, check
`C:\Users\lando\Downloads\4X-Ship-Models` — 4.9 GB, 1,191 files, 145 unit folders, and a companion
prompt book at `Downloads\4X-Ship-Design-Prompts.md` (72 KB, 145 prompts derived from `Species.cs`
and `UnitType.cs`).

40 credits and a duplicate 179-prompt manifest were spent rediscovering this. The lesson is cheap to
write down and expensive to relearn: **inventory before you generate.**

---

## 1. What actually exists

Audited by reading every `.glb` and counting materials, not by trusting folder names.

| State | Count | Which |
|---|---:|---|
| Textured, good | 40 | Terran 02–29, Aquarii 01–12 |
| **Untextured — geometry only, no materials at all** | **100** | Aquarii 13–29, all Pyrothian, all Cryithn, all Sylvan |
| No folder at all | 5 | Sylvan 25–29 (Multi-Role, Terraforming, Deep-Space, Mega, Hyper-Relay) |
| Has concepts + `.blend` + print files but no `.glb` | 1 | Terran 01-Scout |

Every one of the 145 has *an image* in `concept/` — but see §1b: only 5 of those are true concept
art, the rest are grey renders of the mesh itself.

Per unit the tree holds `concept/`, `unity/*.glb`, `blender/*.fbx`, `obj/`, `textures/`, `print/`
and a **`PROMPT.txt`** recording the exact prompt used, the Meshy task id, and the generation
settings.

---

## 1b. CORRECTION — two assumptions that turned out to be wrong

Recorded because both cost real credits.

**"Every unit has concept art."** It does not. Only **5** units have true concept art (Terran Scout,
Fighter, Cruiser, Dreadnought and one more). The other **135 have only a grey render of their own
mesh**, which carries no colour and is useless as an image-to-3D source. So image-to-3D needs concept
art generated first — 12 credits a hull that was assumed to be already paid for.

**"Texturing the existing meshes will do."** It will not. 16 were textured and **1 was usable**
(160 credits). Meshy's texture step largely ignores livery instructions on geometry it did not
author: it returns a greyscale hull (`sat=0%`) or a single flat colour (`sat=97-100%`), rarely the
base+accent split. Measured, not guessed — see `tools/verify-textures.mjs`.

Several underlying meshes are also poor on their own terms — the Aquarii dreadnought is a grey
cylinder, its deep-space station a featureless jellyfish. No texture rescues those, which is what
settled the decision to regenerate geometry rather than repaint it.

**What did work:** where colour landed, mask keying was exact (Aquarii Fighter: 46% at 180° against a
173° key, 5.6% secondary). The livery scheme is sound; Meshy's prompt compliance was the problem.

## 2. How the good ships were actually made

The ones Jacob likes (`Terran/20-Dreadnought/concept/Terran_Dreadnought_concept.png` is the
reference — weathered steel-blue and rust plating, glowing blue engines, real panel detail) were
**not** made by text-to-3D. They came from:

```
text-to-image  (12 cr)  ->  gorgeous 2D concept
image-to-3d    (30 cr)  ->  mesh that inherits the concept's colouring
```

The surviving task confirms it: `mode: "api-image-to-3d"`, `cost: 30`, `shouldTransferImageStyle: true`.
That is the quality bar, and it came through the **public REST API**, not the web UI.

**Because the concepts already exist on disk, the 12-credit text-to-image step is already paid for.**
Re-running image-to-3D on each existing concept is 30 credits per ship.

Plain `texture` on an existing mesh (10 cr) was tested twice on the Pyrothian Scout and is the
cheaper fallback, but it needs its prompt shaped very deliberately — see §4.

### The API contract (captured from the live site)

| Step | Call | Cost |
|---|---|---|
| Upload a mesh | `POST /meshyd-api/web/v2/files/models` → `{id,url}` | free |
| Register upload as a task | `POST /v2/tasks` `{phase:"generate", mode:"upload", args:{generate:{modelId:"uploads/<id>"}}}` | free |
| Text→3D geometry | `POST /v2/tasks` `{phase:"draft", args:{draft:{aiModel:"avocado", modelType:"standard", prompt}}}` | 20 |
| Texture a mesh | `POST /v2/tasks` `{phase:"texture", parent:<id>, args:{texture:{prompt, artStyle:"realistic", aiModel:"blueberry", enablePBR:true}}}` | 10 |
| Image→3D, textured | `{phase:"image-to-3d-texture", args:{draft:{aiModel:"blueberry", imageIds:[...], shouldTransferImageStyle:true}, texture:{aiModel:"avocado", enablePBR:true, textureSize:4096}}}` | 30 |
| Export any format | `GET /v2/tasks/{id}/asset-url?type=Task&format=fbx\|obj\|glb\|usdz\|stl\|3mf\|blend\|dxf` | free |
| Thumbnail | `result.previewUrl` on any finished task | free |

`aiModel` codes: `avocado` = Meshy 6, `meshy-5.1` = Meshy 5, `blueberry` = Meshy 7.

The web session authenticates with a bearer JWT held in memory (not localStorage); it can be lifted
by patching `window.fetch`. The **REST API key** (`msy_…`, named "Claude Access") is the sane path
for a long batch and avoids token expiry entirely.

---

## 3. Workstream A — finish the fleet

- [ ] **A1.** Obtain the `MESHY_API_KEY`. *Blocked on Jacob — Meshy shows a key once at creation.*
      Not a convenience: the web JWT lives **15 minutes**, so a 145-model run needs ~20 manual token
      refreshes and stalls between every one. The key does not expire and makes the batch unattended.
- [ ] **A2.** Rebuild each unit **concept art → image-to-3D**, in lineage order, via
      `tools/meshy-rebuild-batch.mjs`. ~42 cr each. Supersedes the abandoned texture-the-mesh plan (§1b).
- [ ] **A3.** Generate the **5 missing Sylvan** stations (25–29): text-to-image → image-to-3D. ~210 cr.
- [ ] **A4.** Recover **Terran 01-Scout** — it has a 195 MB `.blend` but no `.glb`. Re-export or regenerate.
- [ ] **A5.** Download **every applicable format** per unit — `glb` + `fbx` + `obj` for the game and
      DCC tools, `stl` + `3mf` for printing. Exports are free once the task exists.
- [ ] **A6.** Download `previewUrl` per unit as the **UI thumbnail** (ship-selection list, and the
      panel shown when one or many ships are selected).
- [ ] **A7.** Write an **orientation manifest** entry per mesh. The `PROMPT.txt` files already state
      the convention — *"Bow forward +Z, dorsal up +Y, flat ventral underside"* — which matches
      `ShipMeshManifest`'s heuristic, so most hulls should need no correction. Record measured axis
      lengths so any hull that will fool the heuristic is caught before it flies sideways.

Budget: ~3,300 of 3,732 credits.

---

## 4. Workstream B — civilization livery (primary + secondary)

Jacob's ask: *"a base texture/coloration that is the default, but also highlights and stuff based on
the selected primary and secondary colors."* General tinting is acceptable **and** the
primary/secondary scheme is wanted.

The game currently washes the whole hull 30% toward the faction colour
(`UnitModelRenderer.cs:383`). That is the thing being replaced.

- [x] **B1.** Define the palette — `tools/civ-colors.json`. Each civ gets a **desaturated base** plus
      **two accents ≥95° apart in hue**. Hue separation is the whole trick: a baked texture has no
      layers, so the only way to know which pixels are "the stripe" is to key them by colour, and a
      teal stripe on a teal hull is unrecoverable.
- [ ] **B2.** Texture prompts must state role *and proportion* explicitly. Learned the hard way:

      BASE ~65%      mid-grey, clearly lit, "NOT black and NOT dark"
      PRIMARY ~30%   broad bold bands, "the first colour you notice"
      SECONDARY ~5%  small marks only, named features, "nowhere else"

      First attempt ("black obsidian, desaturated") returned a texture at **0.141 mean brightness**
      with the roles inverted — 24.85% cyan, 0.84% orange. Restating with explicit percentages and
      "NOT dark" fixed both.
- [ ] **B3.** `tools/extract-color-masks.mjs` — key the two accent hues out of each albedo into an
      RGB mask (R = primary, G = secondary), per `maskRules` in the palette file.
- [ ] **B4.** URP shader: base albedo untouched, masked regions recoloured from the player's chosen
      primary/secondary, shading and panel detail preserved via luminance.
- [ ] **B5.** UI for choosing a civ's two colours, persisted in the save.

**Viability is proven, not assumed:** one generated texture put 24.85% of its pixels in a single
keyed hue, so Meshy will lay down regions large and saturated enough to key.

---

## 4b. Lineage and civ identity — the two rules the rebuild has to honour

Encoded in `tools/ship-design.json`.

**A Mk II must look like the Mk I refitted**, not a different ship with the same name. Generating each
tier independently from text gives three unrelated hulls however carefully the words are chosen,
because the model has no memory of the Mk I. So chained lineages are generated IN ORDER and each tier
is an *image-to-image* edit of the one below it (`imagePromptStrength` 0.62) — hull, palette and
camera carry forward, and only the `upgrade` sentence changes. Chains: scout → Mk II → Mk III,
research → Mk II → Mk III → Science Vessel, fighter → Mk II → Mk III, frigate → cruiser → carrier →
dreadnought, and the station line.

**Civ identity must never eat the starship.** Each civ carries a `techMandate` alongside its
aesthetic, because unguarded prompts return a fish for the Aquarii and a tree for the Sylvans — the
first pass produced a literal jellyfish for a deep-space station and an ocean-going aircraft carrier
for the Terran carrier. Sea forms yes, but with hard-edged armour, engine nozzles and weapon housings
unmistakably present.

**Prompt budget is 800 characters and the reservation order matters.** The orientation clauses (which
`ShipMeshManifest`'s bounds heuristic depends on) and the two livery hexes (which the mask extractor
keys on) are reserved FIRST; the descriptive text is trimmed into what is left. Doing it the other way
round silently dropped the livery clauses on the first attempt.

## 5. Workstream C — "the ships look like they are running"

No credits, no art dependency. **Buildable right now.**

### C1 — Navigation lights, in rhythm — **BUILT** (`ShipLights.cs`)
- [x] Constant lights *and* blinking lights, several colours.
- [x] Different blink rates that still feel **in sync and in rhythm**.

  The mechanism: one global `FleetClock` beat. Every light's period is an **integer ratio** of that
  beat (¼, ½, 1, 2, 3…) and every phase offset is quantised to a fraction of it. Harmonically
  related periods with quantised phase realign on a common downbeat — which is precisely what "faster
  but still in time" means musically. Free-running random periods would drift and read as broken.

### C2 — Thrusters — **BUILT**
- [x] Engine nozzles light up when under way, dark when parked.
- [x] **Brightness scales with speed** — brighter faster, dimmer slower.
- [x] Spool up and down smoothly rather than snapping.
- [x] Read like conventional sci-fi drive plumes.

  Speed is available: `Unit.travelFrom/travelTo/travelDuration`, and `Unit.TravelProgress`
  (`Unit.cs:134`). Stern is derivable from the hull bounds after the orientation correction.

### C3 — Muzzle flash — **BUILT**
- [x] Guns light up while firing, **in sync with the projectiles leaving them**.

  Hook `ProjectileRenderer.Fire(shooter, target, w, from, to, damage)` — it already receives the
  shooter and the exact muzzle position (`CombatManager.cs:144`), so the flash can be triggered on
  the same call that spawns the round. No timing drift by construction.

### C4 — Projectile light — **BUILT**
- [x] Rounds **emit light that falls on nearby ships**, and notably on the ship they hit.

### C5 — Impact
- [ ] Small explosion on hit — **already built**: `ExplosionRenderer.Impact(at, w.colour)` is called
      from `CombatManager.cs:260`.
- [x] **Impact sound — BUILT.** `SimpleAudio` has `PlayWeapon` for firing and `PlayShipDestroyed`
      for deaths, but nothing for a round landing. Needs a `PlayImpact(WeaponClass, Vector3)`.

---

## 6. Workstream D — getting 900 MB of art into a Unity project

Raw art cannot ship as-is. One Terran Dreadnought is **1,996,570 triangles**, 4096² albedo + 4096²
normal + 2048² metallic-roughness, **99 MB on disk and 201 MB of GPU memory** — for a ship drawn at
**0.09–0.40 world units** against planets only 0.6–2.2 units across. Everything under `Resources/`
loads into the build whether used or not.

- [x] **D1.** Offline pipeline — `tools/import-ship-models.mjs` (weld → simplify → texture resize →
      prune). Proven: **1,996,570 → 11,976 triangles**, geometry 74.5 MB → **612 KB**.
- [x] **D2.** Pin `sharp` to one version — `tools/package.json` `overrides`. Two copies (0.34.5
      top-level, 0.35.3 under `ndarray-pixels`) load two native libvips into one process and every
      image op dies with `colourspace: parameter space not set`.
- [ ] **D3.** Decide GLB + glTFast vs FBX. GLB embeds materials (no hand-wiring 145 models) but needs
      `com.unity.cloud.gltfast`; FBX imports natively but leaves textures to wire up by hand.
- [ ] **D4.** Rewrite `UnitModelLibrary` so each civ + hull maps to its own mesh, replacing the three
      borrowed meshes and the size-only differentiation.

---

## 7. Done this session

- [x] **Pause-glyph console spam.** `GameHUD.cs:192` drew `❚❚` (U+275A), absent from LiberationSans
      SDF. TMP substitutes `□` and logs a warning on *every* measure and rebuild, so a label that
      repaints as the date ticks filled the console forever. Now reads `PAUSED`. Rule going forward:
      ASCII in UI strings unless a glyph is confirmed present in the SDF asset.
- [x] Palette defined (B1), decimation pipeline proven (D1), sharp pinned (D2), full Meshy API
      contract captured (§2).

---

## 8. Open questions

1. **A1 is the only hard blocker** — the API key.
2. **Poly budget.** 12k triangles/ship is the current target. Cheap at this draw size; revisit if a
   close-up ship inspector ever lands.
3. **Terran/19-Carrier is a literal naval aircraft carrier**, not a spaceship — the original prompt
   leaked. Worth regenerating regardless of texturing.
4. **`_Unidentified/`** holds a 130 MB `.stl`/`.3mf` pair matching no known hull (1,992,802 faces).
   Left in place rather than guessed at.
