# Terraforming Overhaul — Jacob Maddux — 2026-07-16

**Source:** Direct request in a Claude Code session (not the dev-requests pipeline).
**Status:** Planning. The planet **temperature model** this depends on (`Data/PlanetTemperature.cs`)
has **landed on `main`**. The BUILD is held until the "Planet View UI Update" bot run
(`2026-07-16-raptok.md`) fully completes, so its edits to `PlanetViewWindow.cs` and mine don't
collide. This doc lives in `dev-requests/planning/` on purpose: `in-progress/` would trigger the Dev
Requests workflow and have the autonomous bot build this in parallel.

> **Cannot be compiled here.** Unity, no Editor in CI. Nothing here is "tested" or "known to build."
> Each slice is reviewed by subagents against the real declarations before its box is ticked; commits
> say "not compiled — please build before playing."

---

## What was asked for

**Round 1.** Send ships to planets to terraform them; better tech terraforms more and faster; a
started action disappears from the options list; initiating a project starts a loading process during
which the **map's tiles change value and colour in real time** — water importation grows lakes/oceans,
water then enables atmosphere, planting turns things green, and a dry-loving species can push the
water underground / dry it up so the seas visibly recede. Lots of options, cool visuals, up to jungle
world → lava world. Push to the fork for a buddy to pull.

**Round 2 (added).**
- At **max terraforming research, turn ANY planet type totally into ANY other type.**
- **Shift a planet's orbit and see it move in real time**, with **collision avoidance** so a shifted
  orbit's path never crosses another orbit/planet.
- **Every option has a real effect.** Many change the planet's **materials, surface composition, and
  even its shape** — raise mountains, level them, turn a water world into a desert, etc.
- A **tech-magnitude curve**: earliest terraforming makes only small changes — maybe enough to nudge an
  almost-habitable world into the habitable range — while most moons and planets need much higher tech
  before you can significantly reshape them. Big transformations are a late-game capability.

## What already exists (extend, do NOT rebuild)

- **Progressive real-time morph** — `TerraformVisuals.Advance(body, species)` lerps `terrainParams`
  (heat/moisture/elevation/ridge) from `naturalParams` toward `Ideal(species)` by habitability progress,
  regenerates `surface` from the SAME seed (continents stay, climate moves), repaints the 2D map
  (`PlanetViewWindow.RefreshIfShowing`) and 3D globe (`PlanetAppearance.RefreshTexture`), throttled by
  `RegenStep = 1.5f` habitability points. Driven by `ColonyManager.TickTerraform` on the ~1s tick.
- **Temperature (NEW, just landed)** — `PlanetTemperature` is DERIVED from `terrainParams.heat`, never
  stored, so it moves automatically as a world terraforms. `BodyAverageCelsius`, `CelsiusAt(b, y)`
  (latitude swing), type nudges (Volcanic +90 °C, Ice −50). Heating/cooling projects that move `heat`
  move the temperature and the hover readout for free.
- **Ship-based terraforming** — `UnitType.Terraformer` + `TerraformStation`; `OrderKind.Terraform` →
  `BeginAction` → `ColonyManager.ToggleTerraform` → `TickTerraform`. Rate = `min(1+terraformers,6)+aura`;
  presence (owner or any player ship in `body.units`) required to keep running.
- **Tech scaling** — `SpeedFactor()` = `TechEffects.TerraformSpeedMult` × empire-level factor (≤4×);
  `terraSpeed` shortens `Duration()`; `terraCeiling` + completed projects raise `Colony.TerraformCeiling`;
  `requiredTech` (X1–X16 in Tech.cs) unlocks projects. Adding a tech is a data edit.
- **Projects raise the ceiling** — 25 `TerraformProjectInfo`s → `Begin` → timer → `Complete` →
  `ApplyPhysicalEffect` (adds water, spins, **migrates orbit**) + `Reshape` (converts body TYPE,
  regenerates surface, re-applies appearance).
- **Orbit safety already exists** — `OrbitSafety.ClampRadius(siblings, b, star, desired, out final)`
  clamps a migration to the room neighbours leave (won't move a world PAST a neighbour), and
  `EnforceSystem` re-asserts spacing. `TerraformManager.MigrateTo` already uses both — but the move is
  INSTANT at completion, not animated.
- **Started projects already leave the options list** (`TerraformWindow.cs:245` skips `IsRunning`;
  `For()` skips `IsDone`), and running jobs show under "UNDER WAY" with live progress bars.
- **Save/load** — `terrainParams`, `naturalParams`, `terraformProjects`, `terraforming`,
  `terraformability` on `BodyDTO`; surface regenerates from seed+params on load; in-progress projects
  round-trip via `TerraformJobDTO`.

## The real gaps this overhaul builds

1. Morph is **generic** (species-ideal), not **per-action** — the centrepiece is per-project morphs.
2. Projects only change the world **at completion**; the change should advance with the loading bar.
3. Orbit migration is **instant**; it should **animate in real time**, collision-checked throughout.
4. Remodelling always targets the **species' best type**; there's no **directed any→any** transform.
5. No **tech-magnitude curve** — how FAR you can reshape a world isn't gated by tech, only how fast.
6. No **terraform verb** on the map right-click menu.
7. Temperature isn't yet consulted for **freezing water / biosphere gating** (now cheap — see above).

---

## Architecture spine (slice 1)

Replace "several writers push `terrainParams`" with ONE derived function:

> `terrainParams = Compose(naturalParams, completedProjects, runningJobs, habitabilityProgress, species, power)`

- each **completed** project contributes its full climate/shape delta;
- each **running** job contributes its delta × `job.Progress` — the live preview that fills in as the
  bar loads (water appears WHILE Comet Bombardment runs; seas recede WHILE Hydrosphere Venting runs);
- plus the species-ideal blend by habitability progress;
- all clamped by **`power`**, a tech-gated **transformation cap** (the magnitude curve, below).

Deterministic and **save/load-safe with no new per-tile fields** — everything it reads is already
persisted or rebuilt on load. Both 2D and 3D update because the change flows through `terrainParams` →
`GenerateSurface`, the path both renderers already read. Recompose+regen stays throttled by `RegenStep`.

### The tech-magnitude curve (`power`)

`power` ∈ ~[0.15 .. 1.0] scales how far `Compose` may push a world away from its natural params, and it
rises with terraforming tech + empire level:

- **Early** (no/low Expansion tech): `power ≈ 0.15–0.3`. Only small nudges — enough to lift an
  almost-habitable world a few points, not to change what it fundamentally is.
- **Mid** (water/air/life + a couple of Expansion tiers): `power ≈ 0.4–0.6`. Real climate change on
  most worlds; mountains and seas move meaningfully.
- **High** (orbital/core/shape tech): `power ≈ 0.7–0.9`. Significant reshaping — water world → desert,
  raise/level mountain ranges.
- **Max** (X16 Planetary Remodelling + D_TER2 Worldshaping + Precursor Ecoforming): `power = 1.0` and
  **directed any→any** is unlocked — total transformation of any type into any other.

This is also the natural place to enforce "most moons/planets need higher tech": `power` is further
scaled DOWN by world size/severity so a big, badly-suited world needs more tech to move than a small,
nearly-right one.

---

## Slices

### 1a. Per-project climate/shape/material profiles
- [ ] For each `TerraformProjectType`, define which of heat/moisture/elevation/ridge it drives and by
      how much: water projects → +moisture (seas fill low ground first → lakes in basins); mirrors/core
      ignition → +heat; shades/core cooling → −heat; forests/microbes → temperate+moist (green);
      venting/sequestration/thinning → −moisture (seas recede); mountain-building → +elevation/+ridge;
      levelling → −elevation/−ridge; remodelling → the target type's climate.
- [ ] Materials/surface composition follow automatically: the generator's biome classifier turns those
      knob values into biomes (ocean/desert/forest/lava/etc.), so "changes materials" falls out of the
      knobs. Verify every reachable biome has a `TerrainColorMap` colour (no magenta).
- [ ] House this in a new `TerraformClimate` table beside `TerraformVisuals` (new file; avoids touching
      the actively-changing `PlanetViewWindow`).

**Notes:** Pure data + one helper; reviews in isolation.

### 1b. Compose terrainParams from world state (with the `power` cap)
- [ ] `TerraformVisuals.Compose(body, species)` per the spine: natural + Σ completed deltas + Σ
      running-job deltas × progress + species-ideal by habitability, all clamped by tech-gated `power`.
- [ ] Add `TerraformPower(body)` deriving `power` from researched Expansion tech + empire level, scaled
      down by size/severity.
- [ ] Route `TerraformVisuals.Advance` AND completion through `Compose` so there is ONE writer of
      `terrainParams` (the generic morph becomes the no-running-jobs special case).

**Notes:** The spine. Depends on 1a.

### 1c. Drive the morph from a running project's loading bar
- [ ] In `TerraformManager.Update`, as each unpaused job advances, recompose+regenerate (throttled
      across ALL jobs on a body to ~1/sec) so the project's delta previews in step with `job.Progress`.
- [ ] Make `Complete`'s existing `ApplyPhysicalEffect`/`Reshape` visually seamless — the world is
      already most of the way there; completion snaps the last step and updates resource/type numbers.

**Notes:** Headline behaviour. Depends on 1b.

### 2. Temperature coupling (model already landed)
- [ ] Consult `PlanetTemperature` so water freezes to ice below freezing and melts above, and
      biosphere/green only forms in the temperate band — kept consistent with the tile hover readout.
      Mostly free: temperature already reads through `terrainParams.heat`, which the morph drives.

### 3a. Directed any→any transformation
- [ ] Let remodelling target a CHOSEN world type (not just the species' best), with dramatic per-type
      climate/shape targets (Volcanic = very hot/dry/high-ridge; Ocean = wet/low; Ice = cold; Desert =
      hot/dry/flat; etc.). Gate the FULL any→any range behind max-tier tech via the `power` cap so early
      tech can only partly convert.
- [ ] Target chooser in the Terraforming console.

### 3b. Dramatic new options
- [ ] Add a few new `TerraformProjectType`s with strong, distinct morphs (e.g. Volcanic Ignition → lava
      fields; Great Flooding → ocean; Orogenesis → mountain ranges; Continental Levelling → plains).
      Append-only enum (ordinal is serialized). Gate on new/high tech.

### 4. Real-time animated orbit migration + live collision avoidance
- [ ] Convert `OrbitShiftOut`/`OrbitShiftIn` (and any orbit-moving project) from an instant jump to an
      animation over the job's duration: tween `body.orbitRadius` from start toward the
      `OrbitSafety.ClampRadius` target, each step calling `OrbitController.SetRadius` + ring redraw so
      the orbit is SEEN to move; re-score habitability continuously as distance changes.
- [ ] Collision avoidance: the tween endpoint is the CLAMPED radius (never past a neighbour), and
      `OrbitSafety.EnforceSystem` is re-asserted as it moves, so the path never crosses another orbit. If
      neighbours leave no room, it migrates as far as it safely can and says so (existing behaviour).
- [ ] Persist in-flight migration so a mid-migration save/load resumes (store start/target radius on the
      job DTO, or derive from project + elapsed).

**Notes:** Depends on 1c only for the throttle/refresh plumbing; otherwise independent.

### 5. First-class "send terraformer → terraform on arrival" order
- [ ] Add the Terraform verb to the map right-click menu (`FleetMovementController`, beside
      survey/research/colonize), enabled only when the selected fleet has a `canTerraform` ship, issuing
      `OrderKind.Terraform` so the ship travels then starts terraforming on arrival.

### 6. Options-list + console polish
- [ ] Confirm started projects are absent from the available list everywhere; if any inspector UI grows
      a project list, add a running-jobs term to its `Signature()`.
- [ ] Each UNDER WAY card names its specific effect ("seas filling", "green spreading", "orbit
      migrating") so the loading bar reads as an action, not just a percentage.

### 7. Tech growth / leveling
- [ ] Add Expansion nodes that (a) raise the `power` cap in tiers, (b) add `terraSpeed`/`terraCeiling`,
      and (c) gate the new dramatic options and directed any→any. Pure data edits in `TechDatabase.Build()`.
- [ ] Tune the curve end-to-end: early = small nudge into habitable range; max = fast, total any→any.
- [ ] Scale cost/time with transformation magnitude so cross-type transforms are the most expensive
      thing short of a shellworld (extends the existing `SizeScale`×`SeverityScale`).

### 8. Whole-diff review + save/load verification
- [ ] Review the complete change as one diff with subagents. Confirm the derived `terrainParams` model
      round-trips with no new per-tile fields; a job interrupted mid-morph or mid-migration resumes
      correctly; regen/animation throttling holds the frame budget; and the water→air→life dependency
      chain (existing `applies` predicates) still reads sensibly with the new visuals.

---

## Other considerations folded in

- **Reversibility.** Any→any means a world can be re-terraformed toward a different target later;
  `Compose` handles this because it's derived from the CURRENT set of projects, so switching targets
  just changes the destination the world morphs toward. Worth a note in the console so it's discoverable.
- **Dependency chains.** Water→atmosphere→life is partly encoded already (`OxygenSeeding` needs
  water ≥ 80, `PlantForests` needs water ≥ 100). Keep and reinforce these so the visual order matches the
  physical order (you see water before green).
- **Performance.** One recompose+regen per body per ~second regardless of how many jobs run; orbit
  animation is a cheap per-frame transform update (no regen). Never regen a body nobody is viewing more
  often than the globe needs.
- **Determinism / saves.** No new per-tile save data. The only new persisted state is in-flight orbit
  migration (slice 4). Everything else is derived from already-saved fields.

## Build log

- **Slice 1 (1a+1b+1c) — built 2026-07-16.** `TerraformClimate.cs` (per-project knob deltas +
  `Accumulated`), `TerraformVisuals.Compose`/`TerraformPower`, and the throttled morph + completion bake
  in `TerraformManager`. Reviewed by two subagents (compile-clean; logic sound). Review caught that the
  generator gates **open water on elevation, not moisture** — so water-in projects now LOWER elevation
  (basins flood) and water-out RAISE it (land emerges); moisture is vegetation only. Also made the
  `power` size-penalty tech-buy-back-able so big worlds still reach full power at max tech.
  **Scope note:** this is WITHIN-TYPE morph. Worlds with no water biome (barren/airless) or frozen-only
  water (ice) can't grow liquid seas from a knob alone — that needs the directed TYPE transition, which
  makes **slice 3a a priority**, not an afterthought. Not compiled (no Unity in this env).

- **Slice 4 — real-time animated orbit migration — built 2026-07-16.** `OrbitShiftOut/In` now walk the
  world from its start radius to the `OrbitSafety.ClampRadius` target over the project's duration
  (animated per frame in `TerraformManager.Update`, finalized in `Complete`; the instant jump was removed
  from `ApplyPhysicalEffect`). `TerraformJob`/`TerraformJobDTO` carry `orbitStart`/`orbitTarget` so a
  mid-migration save resumes exactly (the animation is a stateless lerp from `elapsed/duration`).
  Reviewed clean by a subagent; hardened past its one caveat by re-clamping every frame so two worlds
  migrating in the same system at once can never overlap. Not compiled (no Unity in this env).

## Order & dependencies

1a → 1b → 1c is the critical path. 2, 3, 4, 5, 6, 7 build on it and can land in any order after 1c
(4 is nearly independent). 8 is last. Every slice is committed AND pushed to the fork's `main` as it
lands, so the buddy can pull at any checkpoint and a partial run loses nothing.

- **Slice 3a — directed cross-type transition (engine) — built 2026-07-16.** Planetary Remodelling now
  transforms a world's TYPE progressively: a low-frequency dither mask in `PlanetTerrainGenerator`
  classifies each tile under the old type or the target type by `body.remodelT` (0..1), so the new world
  (lava/ocean/ice) SPREADS across the old as the bar loads instead of snapping at completion. `Compose`
  walks the climate toward `TerraformVisuals.TypeClimate(target)` in lockstep, so flipped tiles heat/wet
  to match. `TerraformManager` drives the transient `remodelToType`/`remodelT` each frame from the running
  WorldRemodelling job, finalizes at completion (clears the transition, sets `naturalParams` to the target
  climate so it doesn't revert, `Reshape` to the new type), and reverts on cancel. Transient state is
  NonSerialized (rebuilt from the resumed job on load). Reviewed clean by a subagent (compile + logic);
  three low-severity cosmetic notes, no fix needed. Targets the species' best-fit type for now; the
  DIRECTED any→any chooser (pick any target regardless of species) is the next sub-slice (3a-ui). Not
  compiled (no Unity in this env).

## Scope note (user, 2026-07-16)

Reaffirmed/expanded: terraforming to be FULLY fleshed — costs, tech levels, species requirements,
possible actions, habitability changes, and the per-tile real-time morph — "enjoyable to see the changes
in real time." Costs (size×severity×tech), tech gating + the `power` magnitude curve, and species-relative
diagnosis already exist; slices 1 + 3a deliver the real-time tile morph; slice 7 rounds out tech growth.
Separately: consolidate ALL UI panels (not just planet ones) — tracked in the UI-consolidation doc, now
extended to the empire/ship windows too.

- **Slice 5 — map "terraform on arrival" order — built 2026-07-16.** Added the Terraform verb to the map
  right-click order menu (`FleetMovementController`), beside survey/research/colonize — enabled when the
  selected fleet has a terraformer, the world isn't already terraforming, and it's not a gas giant. Issues
  `OrderKind.Terraform`; `ToggleTerraform` still has the final say on feasibility. Reviewed.
- **Slice 2 — temperature-driven water — built 2026-07-16.** Open water now freezes to FrozenSea/Snow when
  a world runs cold (`Classify` Terran + OceanWorld read the same `temp` PlanetTemperature reads), so
  cooling projects visibly ice a world's seas over and warming thaws them. Biosphere/green was already
  temperature-gated in the classifier. Reviewed.
