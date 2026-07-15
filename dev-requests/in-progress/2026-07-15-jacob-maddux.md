# Requests from Jacob Maddux — 2026-07-15

**Source files:** `test.md`
**Status:** In progress

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
- [ ] The **Reset to default** button restores `body.terrainSeed` to the original seed and `body.terrainParams` to `body.naturalParams`, then regenerates and rebuilds the panel so the sliders and seed readout reflect the restored values

**Notes:** Depends on 1a for the stored seed.
