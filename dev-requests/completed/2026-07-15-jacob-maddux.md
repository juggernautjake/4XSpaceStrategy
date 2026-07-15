# Requests from Jacob Maddux — 2026-07-15

**Source files:** `test.md`
**Status:** Complete

## What they asked for

In the **Terrain** tab of Planet View (Dev Mode only — the "Terrain Sandbox"), there is a
**Reset to default** button. Today it resets the terrain sliders to `NoiseParams.Default`
(all 1.00) and regenerates. Jacob wants it to **instead restore the terrain seed the planet
started with** and regenerate from that.

**Interpretation / assumptions.** The literal complaint is "it moves all sliders to 1.00."
The real intent is a button that returns the world to how it was *generated* — the sandbox
lets you drag sliders and hit **Randomize (new world, same settings)**, and there's currently
no way back to the planet's original terrain. So I read "Reset to default" as **restore the
planet's original generated state**: its original **terrain seed** and its original **natural
params** (the per-world varied values `TerrainVariance` rolled at generation, captured into
`naturalParams`), not the generic `NoiseParams.Default`. That both satisfies "regenerate the
seed the planet started with" and fixes the "sliders to 1.00" complaint, since the sliders
snap back to the world's own defaults rather than a flat 1.00.

The original seed is not stored anywhere today (`terrainSeed` is mutated in place by the
Randomize button), so it has to be captured at generation and persisted through save/load —
mirroring exactly how `naturalParams` is already handled.

## Slices

### 1a. Store the planet's original terrain seed
- [x] `CelestialBody` gains a `naturalSeed` field capturing the seed the world was generated with
- [x] `TerraformVisuals.CaptureNatural` records `naturalSeed` alongside `naturalParams`
- [x] `naturalSeed` persists through save/load (`BodyDTO` + serializer), with an honest fallback to the current `terrainSeed` for saves written before the field existed

**Notes:** `CaptureNatural` is the single point where the natural baseline is snapshotted, and
it is called after the seed is assigned in both `SolarSystemGenerator.SeedTerrain` and
`GalaxyGenerator` (including the home-world re-capture), so capturing there covers every body.
Depends on nothing; 1b depends on this.

### 1b. Make "Reset to default" restore the original seed
- [x] The **Reset to default** button restores `body.terrainSeed` to the original seed and `body.terrainParams` to `body.naturalParams`, then regenerates and rebuilds the panel so the sliders and seed readout reflect the restored values

**Notes:** Depends on 1a for the stored seed.

### 1c. Fix: capture the world's TRUE natural climate (found in final review)
- [x] `SolarSystemGenerator` re-captures `naturalParams`/`naturalSeed` after `BiasHeat` (planet and moon paths), so Reset restores the world's actual generated heat rather than the pre-bias variance value

**Notes:** Found during the whole-change review. `SeedTerrain` captures the natural state, but the
system generator then biases `terrainParams.heat` by distance from the star and regenerates the
surface — so for every non-home world `naturalParams.heat` was stale, and 1b's Reset would have
restored the wrong temperature. The home-world path in `GalaxyGenerator` already re-captured after
its own heat override; this brings the system generator in line. As a bonus this also corrects the
origin terraforming lerps FROM, which read the same stale value.

## Closing note

**Built:** The Terrain Sandbox "Reset to default" button (Terrain tab, Dev Mode) now restores the
world as it was generated — its original terrain seed and natural per-world params — instead of
flattening the sliders to 1.00. To make that faithful, `CelestialBody` now records the generation
seed (`naturalSeed`, persisted through save/load with a fallback to `terrainSeed` for older saves),
and the system generator was fixed to capture the post-`BiasHeat` climate as the world's natural
state.

**Could not verify:** Not compiled — this is a Unity project with no Editor in CI. Please build
before playing. The logic was reviewed by independent review agents (1a: 1, 1b: 1, whole-change
pass: 1, 1c fix: 1) but not compiled or run.

**Look at first:** In the Terrain tab, use **Randomize** and/or drag the sliders, then **Reset to
default** — it should return the exact world you started with (same continents, same climate). On a
world loaded from a save written before this change, `naturalSeed` falls back to the saved
`terrainSeed`, so Reset restores that seed (correct unless the seed had been rerolled in a Dev
session before saving, which old saves have no record of).
