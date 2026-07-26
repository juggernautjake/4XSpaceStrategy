# Master plan — every request, in slices

**Compiled 2026-07-22. Checkboxes brought back in line with the code on 2026-07-26.**
Covers everything asked for from the Genesis Sequence brief onward.
`[x]` = built and pushed · `[~]` = partly built · `[ ]` = not started

> **This doc went stale almost immediately** and its boxes lied for four days: Parts D and E were both
> written as "not started" while they were in fact shipped (`d1f196f` Build Mode, `9886afe` the research
> ladder), as was C1 (`01102d4`). If you are reading it to find out what is left, **check the code** —
> that is how the 2026-07-26 pass established the list below.
>
> **As of 2026-07-26 exactly one item here is open, and it is one only you can do: C3's ultrawide check.**
> Everything else in this document is built.

> **Nothing here is compiled by the agent that writes it** — there is no Unity in that environment.
> Every slice is reviewed by independent agents instead, which has caught real bugs on every pass,
> including two wrong diagnoses of my own. **Build before playing.**

---

## Where the code lives

| Branch | Holds |
|---|---|
| `main` | All the standalone fixes. Pushed. Your buddy pulls this. |
| `visibility-and-genesis-reveal` | Visibility system, genesis reveal, terrain transformation, sky palette, camera foundation. Pushed, not merged. |

---

## PART A — Fixes (all built, all on `main`)

- [x] **Homeworld biosphere.** `heat` is calibrated so heat = 1 reads ~15 °C, but the temperature the
      game reads adds greenhouse warming on top (atmosphere × 45 °C) and the cradle's heat never knew.
      Terrans ran 47/51/55 °C and Sylvans 55/59/63 °C against a 50 °C liquid-water ceiling — so two
      Terran homeworlds in three, and *every* Sylvan one, generated sterile. Now the temperature is
      chosen and the heat solved for it (`PlanetTemperature.HeatForCelsius`).
- [x] **Terraforming no longer undoes that fix.** It solved for the old raw-heat curve, so maxing
      terraforming on your own capital walked it back to 51 °C and killed the biosphere.
- [x] **`CS0128` duplicate `row`** in `InspectorBodyTabs`.
- [x] **`MissingReferenceException` every frame in the finale.** `RevealAtmosphere` handed every moon
      its real texture before any had begun developing, and `RefreshTexture` destroys the texture already
      on the material — which was the one being painted.
- [x] **Focused planet changing size as it orbits.** Zoom height was absolute, so ±7° inclination went
      straight into camera distance: ~1.65× apparent size once per lap. Height is body-relative now.
- [x] **Unfollow lurching the camera** — the fix above needed a rebase on release.
- [x] **Docked ships unselectable**, then **planets unselectable** when I overcorrected. Final rule: the
      planet's *drawn* disc wins; a ship only wins outside it.
- [x] **Black hole showed an empty system panel.** Now a Galaxy readout: name, core name where it
      differs, systems, worlds, habitable-for-you, holdings, derelicts, and every system drillable.
      Clickable at galaxy zoom too.
- [x] **Ship focus** — select a ship and the camera follows it. Followed by *identity*, because a ship's
      token is destroyed and rebuilt whenever the fleet changes.
- [x] **"Send to…"** — every charted world, with distance and an estimate, behind a confirmation.
- [x] **Stars too dim.** An ordinary sun computed to **0.975** emission — *under 1.0*, so bloom never
      saw it and every G/K/M star rendered as a flat painted ball. Now 1.73–4.50.
- [x] **Stars casting and receiving shadows.** A sun could be darkened by a planet passing in front of
      it. Lights are `Point` (all directions) with `LightShadows.None`.
- [x] **`CS0122` `WorldPos` inaccessible.**

---

## PART B — Visibility, hiding, deletion (built, on the feature branch)

- [x] `HideReason` — Dev · Cloaked · Undiscovered · Sequence. Identical rendering today; the reason is
      what lets a cloak-breaking tech and a discovery event undo only their own concealment later.
- [x] Hide/delete **anything**: planets, moons, suns, black holes, orbit lines, ships, whole systems.
- [x] `ConcealBinding` — records exactly what it disabled, so reveal restores the prior state.
- [x] Six systems that draw things the visualizer doesn't own, each taught about concealment: orbit
      rings, owner rings, habitable rings, galaxy-zoom proxies, derelict hulls, floating labels.
- [x] `GalaxyTrash` — real deletion with a restoring bin; `homeIndex` and derelict indices fixed up.
- [x] `ObjectVisibilityWindow` — the Dev-Mode tree that makes it usable.
- [x] Rare naturally-hidden worlds at generation (~1.2%).
- [x] Save/load persistence, with `Sequence` deliberately never written to disk.

---

## PART C — The intro

### Built
- [x] Loading bar bound to **real** generation progress (the generator is phased).
- [x] Real home star(s), with binary/trinary pop-out on the real cluster geometry.
- [x] Terrain as a **transformation** — a wet world starts drowned and grows continents; each tile
      passes through several biomes; the last frame is the exact surface you then play.
- [x] Moons get the identical treatment.
- [x] Galaxy concealed during generation and arriving at the end (`GenesisReveal`).
- [x] Orbit lines as the "you have control" cue.
- [x] Backdrop: nebulae, coloured shimmering stars, shooting stars, parallax. Star colours now drawn
      from the same blackbody ramp the real suns use.
- [x] **`GenesisCamera`** — exclusive control of the real camera; frames real bodies at a given apparent
      size and screen anchor. `d = r / tan(f·V/2)`, re-solved every frame because the subject orbits.
- [x] **Consistent view angle** — pitch is the game's own 55°, yaw pinned to one bearing for every beat
      and carried across the handover, so nothing tilts or swings when control passes to the player.
- [x] **`TerrainDevelopment`** — the stage maths, shared so planet and moons cannot drift.
- [x] **`TerrainMorph`** — drives an *actual* body's material, then hands back its real surface.
- [x] **`GenesisSequence`** — the nine beats in one readable place.

### Slice C1 — Wire the sequence into the live boot path
- [x] `GameManager.GenerateGalaxyBody` calls `GenesisSequence` instead of `LoadingScreen.Finale`
- [x] `FrameHomeStar` as soon as the home system's visuals exist
- [x] Bar keeps reporting real progress underneath the live camera
- [x] Verify the handover: no snap, no tilt, no zoom jump — *as far as reading it can verify; the pose is
      re-solved every frame and `Release` hands the rig the framing the last beat composed*

Shipped as `01102d4` "The intro films the real world".

**This is the change that makes the intro real.** After it, the planet you watch form *is* the
homeworld, the moons *are* your moons on their generated orbits, and the world gets a real terminator
from its real star — the cue that it is orbiting at all.

### Slice C2 — Retire the preview stage
- [x] Delete the private stage: sphere, corona quads, key light, `RenderTexture`, `RawImage`
- [x] Delete `AlignToReal` / `MatchChildRotation` — they exist only to make a fake match the real
- [x] Delete the cosmetic `MoonPreview` system
- [x] Delete the cross-fade handoff and `HandoffScreenFraction`
- [x] ~600 lines out of `LoadingScreen` — **it came to 1,881.** The estimate counted the stage and the
      cross-fade but not the second implementation of world generation's visuals hanging off them: the
      binary/trinary pop-out (`StepSunCluster`, companion suns, coronae, `LoadingBillboard`), the
      tile-by-tile terrain morph (`MorphStages`, `BuildStage`, `PaintStage`, `BuildJitter`), the cosmetic
      moons, the atmosphere shell, and the whole dead `Finale`. `LoadingScreen` is 2,326 lines → 445.

`Finale`, `HandoffScreenFraction`, `SetHomePlanet`, `SetHomeMoons` and `StagedMoonCount` were already dead
— C1 stopped calling them and nothing else ever did.

**Knock-on to `GameManager`, which is the part to look at if this fails to compile.** `LoadingScreen.Subject`
existed only to tell the preview which model to show, so it and the 3-argument `Report` are gone and its
nine call sites now use `Report(t, stage)`. `SetHomeCluster` and the `PopBeat`/`PopGrow` hold that paced the
pop-out are gone with it.

**One visible change for the player, and it is deliberate:** the first ~60% of the load is now the
starfield and the bar rather than a spinning stand-in. There was never a real galaxy to film during that
stretch — the stage's whole reason for existing — and the honest version of "nothing to show yet" is not
showing a stand-in for it. If you want something there, that is a new request, not a regression.

### Slice C3 — Scale and framing spec
- [x] Homeworld ≈ 9% of viewport height at 35% width (resolution- and aspect-independent)
- [x] One compression curve for relative sizes (star ≈ 1.9× the homeworld)
- [x] 1.0× → 1.3× → 1.0× across the closing beats
- [ ] **Verify on an ultrawide — THIS IS THE ONE OPEN ITEM IN THE DOCUMENT, and it is yours.** It cannot be
      done by reading: the solve is height-based specifically so aspect ratio does not move the subject's
      size, and the only way to know it holds is to run it at 21:9 and look. What to watch: the homeworld
      should stay the same apparent size as it does at 16:9, and only the horizontal anchor (35% across)
      should place it further left.

### Slice C4 — Conveying the orbit
- [x] Terminator sweep — free, once the real planet is lit by its real star
- [x] Starfield parallax — exists
- [x] Slight camera drift during the forming beat — `GenesisCamera.Drift(WorldForms)`, a 5% push in and a
      4% rise over the beat, smoothstepped. A **push and a rise, not an arc**: yaw is pinned to
      `SequenceYaw` for every beat precisely so nothing swings at the handover, so drifting the bearing
      would spend the one invariant the framing spec is built on. Both persist when the drift ends and are
      cleared by the next `Frame`/`EaseTo`, which recompose from the live pose — unwinding them would walk
      the shot backwards at the exact moment the world finishes forming. `Release` hands the rig the
      *drifted* fraction, so a skip mid-creep cannot pop the planet.

### Slice C5 — Skip and abort
- [x] Skip button, always available — bottom right of the panel, live for the whole load. Generation
      itself cannot be skipped (the galaxy has to exist first), so pressed early it means "don't play the
      intro when you get there", which is what the first check in `Play` does with it.
- [x] `Esc` aborts to the end state. No contest for the key: `EscapeMenu` already refuses to open while
      `GameManager.IsGenerating`, which covers the whole load.
- [x] Never runs when loading a save — **already true, and no code was added for it.** `SaveLoadMenu.DoLoad`
      goes to `GameStateSerializer.Apply` and never touches `GenerateGalaxyAsync`, so the sequence has no
      path into a load. Verified rather than built.

**The mechanism, because it is not the obvious one.** A skip is a static flag (`RequestSkip` /
`SkipRequested`), checked at the top of every beat, and *not* a `StopCoroutine`. `Play` is not run as a
coroutine on `GenesisSequence` at all — `GameManager` pumps the enumerator itself with
`while (play.MoveNext())` so it can walk the bar across the sequence's own clock — and `StopAllCoroutines`
cannot reach an enumerator somebody else is driving. `Abort`'s own call to it was therefore already a no-op
against `Play`; its doc comment now says so.

---

## PART D — Build Mode overhaul  *(SHIPPED — `d1f196f` "Build Mode foundation: drawn footprints, Labor, and a per-planet build queue". The boxes below were never ticked; the code is there. D6 remains deliberately deferred.)*

Drawn footprints instead of fixed tetrominoes; Labor; per-planet build queues.

**The formulas.** For a building drawn across **N** tiles, block *i* 1-indexed:

| | Per block | Total for N |
|---|---|---|
| Cost, build time, upkeep | `base × (1 + 0.05·(i−1))` | `base × (N + 0.05·N(N−1)/2)` |
| Output | `base × (1 + 0.10·(i−1))` | `base × (N + 0.10·N(N−1)/2)` |
| Labor | `laborPerTile` (default 1) | `laborPerTile × N` |

A 3-tile farm costs **3.15×** and produces **3.3×**; a 4-tile farm **4.3×** / **4.6×**.

**Labor**, modelled on `FacilityPower.BuildPower` which already does this job for shipyards:
Capitol 2 (+1 per upgrade) · city blocks 0.5 each · Storage Depot 1 **per tile**. Projects hold
`laborPerTile × tiles` until they finish, cancel or pause. A shortfall stretches build time rather than
blocking; freed Labor flows to the next queued project.

- [ ] **D1** `PlacedBuilding` carries its own cells; save/load round-trips them (flat parallel int
      lists — JsonUtility will not nest a `List<Vector2Int>` safely); existing saves fall back to the
      authored shape so worlds already built keep standing
- [ ] **D2** Click-drag painting, live validity, contiguity rule, cancel-and-redraw
- [ ] **D3** The scaling curves, in one tunable place; `EfficiencyAt` averages over the drawn cells
- [ ] **D4** `SurfaceLabor` — max, used, per-building `laborPerTile`, shortfall stretching build time
- [ ] **D5** Real build times for every building + the per-planet queue (progress, pause,
      cancel-with-refund, drag-reorder — the affordances `ShipyardWindow` already uses)
- [ ] **D6** *(later, explicitly deferred)* ship and station parts

---

## PART E — Survey and Deep Research  *(SHIPPED — `9886afe` "Survey once; Deep Research three times, each earned". The boxes below were never ticked; the code is there.)*

Basic survey once. "Deep survey" → **Deep Research**, also once, with tiers II and III unlocked by tech.

**The problem today:** `SurfaceIndex.Unlocked` gates Mineral on a survey and **all five other overlays**
on one `deepSurveyed` bool. One ship order gives away everything; there is nothing left to earn.

**The ladder** — the six indexes are the backbone, split 1 → 2 → 2 → 1:

| Stage | Gate | Overlays | Also reveals | The question it answers |
|---|---|---|---|---|
| Visited | ship arrives | — | name, type, mass, orbit, low-res map | worth stopping for? |
| Surveyed | once | **Mineral** | POIs located, bulk resources, habitability, °C | should I claim it? |
| Deep Research I | once, from start | **Heat + Fertile** | atmosphere, biosphere, tectonics, terraform diagnosis | where do things go, can it be fixed? |
| Deep Research II | Empire 4 | **Wind + Solar** | exact ore richness, POI *contents*, terraform ceiling, fault lines | how do I power it? |
| Deep Research III | Empire 7 | **Water** | Vael fragments, anomalies, post-terraform projections, subsurface ore | what's left that nobody found? |

Heat + Fertile land together because they are exactly the two that decide where a geothermal plant and
a farm go. Wind + Solar are the power-siting pair. The tiers follow the decisions, not the alphabet.

- [ ] **E1** `researchLevel` (0–3) replaces `deepSurveyed`; kept as `=> researchLevel >= 1` so the 15
      call sites compile while they migrate; old saves load `true` as level 1
- [ ] **E2** Once and only once — the UI stops offering a completed survey or a held tier
- [ ] **E3** Rename every user-visible string to Deep Research I / II / III
- [ ] **E4** Two tech nodes gating tiers II and III; `SurfaceIndex.Unlocked` reads the level per index
- [ ] **E5** Each reveal actually gated at its tier; the Survey tab shows the ladder and what unlocks next

**Two consequences worth knowing:** Vael fragments move to Stage 4, so the Codex becomes a genuine
late-game hunt rather than a side effect of an early ship order. And the homeworld must start at max
research level, or your capital loses overlays it has always had on turn one.

---

## Order I intend to build in

*All done, in a different order than planned — D and E went before C2–C5.*

1. ~~**C1** — wire the sequence in.~~ `01102d4`
2. ~~**D1–D5** — Build Mode.~~ `d1f196f`
3. ~~**E1–E5** — the research ladder.~~ `9886afe`
4. ~~**C2** — retire the preview stage.~~ 2026-07-26
5. ~~**C4, C5** — drift, skip/abort.~~ 2026-07-26
6. **C3** — the ultrawide check. Needs a running build; over to you.

---

## What is left, in one place (2026-07-26)

* **C3's ultrawide check** — yours, needs a build.
* **D6** — ship and station parts. Explicitly deferred, not forgotten.
* **Shape changes to buildings that are already placed** — deferred on save safety: changing a placed
  building's `shape` moves its footprint under saves that already have one standing. Only new buildings got
  their spec'd tetromino shapes.

Nothing else. **None of the 2026-07-26 work is compiled** — there is no Unity in the environment it was
written in. Build before playing; a `LoadingScreen`/`GameManager` compile error is the likeliest failure,
because C2 deleted 1,881 lines and changed nine call sites across the two files.
