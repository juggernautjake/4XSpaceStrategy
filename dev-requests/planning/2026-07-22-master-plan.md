# Master plan — every request, in slices

**Compiled 2026-07-22.** Covers everything asked for from the Genesis Sequence brief onward.
`[x]` = built and pushed · `[~]` = partly built · `[ ]` = not started

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

### Slice C1 — Wire the sequence into the live boot path  ← NEXT
- [ ] `GameManager.GenerateGalaxyBody` calls `GenesisSequence` instead of `LoadingScreen.Finale`
- [ ] `FrameHomeStar` as soon as the home system's visuals exist
- [ ] Bar keeps reporting real progress underneath the live camera
- [ ] Verify the handover: no snap, no tilt, no zoom jump

**This is the change that makes the intro real.** After it, the planet you watch form *is* the
homeworld, the moons *are* your moons on their generated orbits, and the world gets a real terminator
from its real star — the cue that it is orbiting at all.

### Slice C2 — Retire the preview stage
- [ ] Delete the private stage: sphere, corona quads, key light, `RenderTexture`, `RawImage`
- [ ] Delete `AlignToReal` / `MatchChildRotation` — they exist only to make a fake match the real
- [ ] Delete the cosmetic `MoonPreview` system
- [ ] Delete the cross-fade handoff and `HandoffScreenFraction`
- [ ] ~600 lines out of `LoadingScreen`

### Slice C3 — Scale and framing spec
- [x] Homeworld ≈ 9% of viewport height at 35% width (resolution- and aspect-independent)
- [x] One compression curve for relative sizes (star ≈ 1.9× the homeworld)
- [x] 1.0× → 1.3× → 1.0× across the closing beats
- [ ] Verify on an ultrawide — the solve is height-based for exactly this reason, unverified in practice

### Slice C4 — Conveying the orbit
- [x] Terminator sweep — free, once the real planet is lit by its real star
- [x] Starfield parallax — exists
- [ ] Slight camera drift during the forming beat

### Slice C5 — Skip and abort
- [ ] Skip button, always available
- [ ] `Esc` aborts to the end state (`GenesisSequence.Abort` exists; not yet bound)
- [ ] Never runs when loading a save

---

## PART D — Build Mode overhaul  *(not started)*

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

## PART E — Survey and Deep Research  *(not started)*

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

1. **C1** — wire the sequence in. Everything else in the intro is downstream of it.
2. **C2** — retire the preview stage, once C1 is proven.
3. **D1–D5** — Build Mode. Largest gameplay feature outstanding.
4. **E1–E5** — the research ladder.
5. **C3–C5** — framing verification, drift, skip/abort.
