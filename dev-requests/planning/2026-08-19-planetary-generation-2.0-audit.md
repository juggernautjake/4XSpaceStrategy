# Planetary Generation 2.0 — build audit

**Date:** 2026-08-19
**Source:** Jacob's "Planetary Generation 2.0" spec (mass, magnetic field, tectonics, earthquakes,
terrain, water/temperature, time scale)

Every clause of the spec was checked against the code, not against notes. The verdict is that the
spec is **built**, with one real defect (now fixed), one open engineering decision, and one place
where the spec contradicts itself and the code had to pick.

There is no Unity in this environment, so nothing below has been compiled or run. This is a
read-of-the-source audit plus numeric validation through Node ports, which is the same standard the
rest of this work has been held to.

---

## 1. Mass — built

| Clause | Where | Verdict |
|---|---|---|
| Terran cradle always 1 Mass | `Species.cradleMass = 1f`; `GalaxyGenerator.ForceHomeWorld:295` | built |
| Terran home star is G-type | `Species.solHomeStar = true` pins `StarType.G` (`GalaxyGenerator:76`) | built |
| Gas giants 10–40, multiples of 5, clustered on the mean | `MassRules.RollGasGiant` + `Bell()` — measured 25 at 31%, 10 and 40 at 1.4% each | built |
| SSM = 100 per solar mass, a **ceiling** not a target | `MassRules.SystemBudget`, clamped 45–400; the lane loop breaks when the pot cannot fund a 0.1 asteroid | built |
| Terrestrial default 1, floor 0.6, ceiling 4, one decimal | `MassRules.RollTerrestrial` / `QuantizeTerrestrial` | built |
| Big bodies further out, rare exceptions | `ChooseLane`: `OuterGiantChance` 0.42 past the frost line, `HotGiantChance` 0.05 inside it | built |
| Small rocky worlds close in | `TerrestrialBandMax`: 1.4 inside the hot line, 2.6 in the zone, 4 outside | built |
| Moons: terrestrial mass / 2, giant mass / 10 | `MassRules.MoonBudget` | built |
| Not every planet gets moons; the allowance is a maximum | 15% of giants and 34% of terrestrials roll none; a 0.42 stop-chance per extra moon | built |
| Mass ≤ 0.5 and not a moon ⇒ asteroid | `WorldClassifier.Physics` — `<=`, so a body exactly on 0.5 is an asteroid | built |
| Asteroids share an orbit line at an identical speed | `beltSpeed` taken once outside the loop and copied; inclination and eccentricity forced to 0 | built |
| Remove the "Average planets per system" slider | zero references anywhere in `Assets/Scripts` | built |

## 2. Magnetic field — built

`RotationRules`. Spin is rolled at generation as two populations (tidally braked / spun up), the
field is a consequence of the rate (`MagneticFieldSpin = 12°/s`), direction is stored separately so
retrograde never accidentally means "no dynamo", and the Dev orbit panel carries both a spin slider
and a prograde/retrograde toggle (`OrbitControlPanel:68`).

## 3. Tectonics and the Geothermal Index — built

The Heat Index and the Tectonics overlay are one field (`GeothermalMap`). Every number the spec
gives is a named constant and was validated by Node port against the real noise distribution:

- `PlateLineBase = 0.40` — a quiet margin reads 40.
- A head-on convergent margin reads **100** on the line.
- `RadiatedFloor = 0.70` over `RadiateTiles = 3` — measured minimum anywhere inside the band, 70.0;
  measured width of the ≥70 band, 3.01 tiles either side.
- Hotspots on plate-less worlds are focused plumes with a 90+ core tapering through 80+ to 70+, every
  band strictly smaller than the one below it at every intensity.
- `VolcanoIndex = 0.97` — the spec's 97–100 vent range.

The calibration note in `GeothermalMap` is worth keeping: the first cut of the hotspot shaping
thresholded against a *nominal* 0–1 range rather than the noise field's measured distribution, and
would have produced **no volcano anywhere in the galaxy, ever**, without erroring.

## 4. Earthquakes — built

`EarthquakeManager`. Return periods of 25 / 50 / 100 in-game years as three independent rolls, damage
scaled by severity, and — the part that matters — a quake damages **only** structures standing on
ground at or above `SurfaceIndex.ShowFloor` (0.70), the same band the survey overlay paints red.
Checked per building on its own tile, so the promise is one the player can verify by looking at the
map before they build.

## 5. Terrain — built, with one defect (fixed)

The pipeline runs in the spec's order and each step only ever adds to a flat sheet: plates give
continents (Voronoi, the same partition the fault overlay draws) → convergent margins lift and rifts
drop → hotspot vents pile domes → *only then* a small variation pass → the Elevation slider scales the
deviation from the mid-line. Nothing downstream reads temperature, water or air, so terraforming
cannot move a tile's height. Ruggedness and Elevation Range are gone; `ridge` is derived from the
geological lift rather than rolled beside it, so the only two things that make a mountain are a
collision and a vent. Hills and Highlands are no longer emitted as biomes — they are an elevation
band reported under the cursor alongside metres and °C.

**Defect found and fixed:** `ClimateCoherence` promoted a tile to `MagmaField` at ≥ 650 °C but never
demoted one below it, while the per-type `Volcanic` classifier laid magma down from its own
*normalized* heat field (`hot > 0.78`), which knows nothing about °C. A volcanic world sitting at,
say, 540 °C therefore grew a band of liquid rock across its equator while the readout under the cursor
correctly said the ground was three hundred degrees too cold to melt. The gate now runs both ways;
sub-650 magma demotes to `LavaRock`, which is the word that world's own classifier already uses for
solidified flows.

## 6. Water and temperature — built

Range is −270 … 1000 °C. The liquid-water window is a function of pressure, not two constants:
`BoilingC(P) = 100 + 44·log₄(P)` through the spec's anchors, enforced **per tile** so a world can hold
an ocean at its poles and salt flats at its equator. Below freezing, seas become `FrozenSea` and more
water means more ice. Elevation moves a tile's temperature by up to ±70 °C at the same lapse rate the
generator classified it with, which is what pools magma in valleys instead of sheeting the equator.

> **The spec contradicts itself here and the code had to choose.** It says "liquid water can exist on
> planets with atmosphere of 4 up to 200 C" and then, four lines later, "At 4 atmosphere the range of
> liquid water is 0 C to 144 C". The code took **144**, because it is the more specific statement and
> the one given as an anchor pair with the 1-atmosphere figure. If you meant 200, `BiosphereRules
> .BoilingAtFourAtm` is the single constant to change and the whole curve moves with it.

## 7. Time scale — built

`GameCalendar`: one second of game time is one in-game day, 30-day months, 360-day years, starting
Year 0001. Construction, travel and research durations all read through it.

---

## Post-audit correctness review

The audit above asks "does the code do what the spec says". This asks "is the code right". Three
defects, all fixed, all validated numerically through Node ports rather than by reading.

**1. Mountains from pure noise on any pre-rework save.** `RidgeFromRelief` computed
`Max(shaped, rough) * ridgeScale`, so the noise floor scaled with `ridge` along with the geology. The
floor is 0.62 and every Mountains threshold is 0.82, so the guarantee held only while `ridge` stayed
at or below 1.32. Nothing rolls it away from 1 any more — but `ridge` is *in the save format*,
`TerrainVariance` used to roll it as a per-world ruggedness reaching about 1.5, and the loader
restores whatever the file says. At 1.5 the floor came back as 0.93, and every geologically dead
world in every old save grew mountain ranges out of noise the moment it loaded.

Now `Max(shaped * ridgeScale, min(rough * ridgeScale, NoiseRoughnessMax))`. Measured: a dead world
caps at 0.620 at *every* scale from 0.30 to 2.0; a flattening project still calms the background;
real geology still makes mountains at every scale; and at `ridge = 1` — every world generated today —
the output is bit-identical over 200,000 random samples.

**2. A guaranteed major earthquake on any rebuilt colony.** `EarthquakeManager` skips a world with no
buildings without stamping `lastChecked`, so its clock keeps running. `expected` is
`elapsed / periodDays` used directly as a probability, so once elapsed passes the return period that
probability exceeds 1 and the roll stops being a roll. A colony levelled and rebuilt a century later
ate a certain major quake in its first month — as did any world that only *became* geothermally
active later, which a Dev reseed or a remodel to a volcanic type can do at any time. Elapsed is now
capped at one check interval, which errs downward.

**3. Every galaxy the session ever generated, kept alive.** `EarthquakeManager.lastChecked` is keyed
on `CelestialBody` references and was never cleared. `GameManager` documents this exact hazard where
it clears `SurfaceIndex`, `TectonicsMap` and `GeothermalMap` on generate and on load — the quake
clock was simply missed, and each retained body holds its whole surface grid. It now clears with the
others. The same sweep also gained a one-comparison gate at the top: it walked every body in the
galaxy *every frame* to look for something that moves on a scale of decades.

Two things that looked wrong and are not, checked rather than assumed: `GeothermalMap.Hash01` is a
`sin`-based hash fed arguments up to ~31,000, where float precision usually degenerates — measured
over 20,000 worlds it stays uniform, decorrelates between neighbouring seeds, and puts 41.5% below
the dead cut, matching the "a little under half" the design asks for. And `GameCalendar`'s month/day
arithmetic is exact because 360 is a whole number of 30-day months.

## Open items

### A. The 3D globe and the 2D map draw the same terrain differently

`PlanetAppearance` textures the globe with `SurfaceTextureRenderer.BuildGrid` — one flat texel per
cell, no biome grain. The Planet View map uses `BuildGridTextured`, which fills each cell with the
biome's pattern. Same world, same tiles, two different surfaces, and the difference is visible once
you zoom the globe in far enough to resolve a cell.

Unifying them is not free, and that is the decision:

| Texels per cell | 400×200 world | Grain visible? |
|---|---|---|
| 1 (today) | 320 KB | no |
| 2 | 1.3 MB | barely — the 16×16 art box-filters to 2×2 |
| 4 | 5.1 MB | yes |
| 8 | 20 MB | fully |

That is **per body**, and `PlanetAppearance` runs for every body in the system (roughly twenty), and
`RefreshTexture` reallocates about once a second while a world is terraforming. Options: leave it,
raise only the focused body, or accept ~4 texels per cell everywhere. **Needs your call.**

### B. `Hills` and `Highlands` are dead types that must not be removed

Neither is generated any more. Both are still in `TerrainType`, `TileCatalog` (where they share the
display name "Highland"), `TerrainColorMap` and `TerrainTextureMap` — correctly, because
`GameStateSerializer:676` writes terrain cells as **enum ordinals**, so removing or reordering
`TerrainType` breaks every existing save. Leave them.

### C. The three new scripts have no `.meta` files — **done**

`GeothermalMap.cs`, `RotationRules.cs` and `GameCalendar.cs` now have them, with GUIDs checked
against all 479 already in the project.

### D. Still outstanding from the 2026-07-26 build-mode spec

Not part of Planetary Generation 2.0. Two of the four are now closed:

- **Un-own the cradle's moons; hide Society/Satisfaction until settled — done.** The moons are
  surveyed and reachable but unclaimed. The birthright was quietly carrying two guarantees
  (guaranteed terraformable, claimable at tech 1) that had nothing to do with ownership; those moved
  to a new `CelestialBody.cradleMoon` flag so the moons stop being free without becoming unreachable.
  Society is gated on `settled` in both the Inspector tab and the Planet View panel.
- **The abstract Research Centre — done, and the answer was that the migration had already
  happened.** `BuildingInfo.researchFacility` had no readers at all: ore-sample research reads
  `CelestialBody.researchCenterLevel`, and `SurfaceBuildManager.SyncFacilityTiers` derives that from
  the surface `ResearchCenter` standing on the map. Same for `BuildingInfo.shipyard`. Both dead flags
  are deleted and the descriptions no longer promise capabilities the abstract entry does not supply.

Still open:

- The grey-metal orbital station model over the homeworld — needs art in the editor.
- Fixed-footprint buildings (spaceport, shipyard, capitol, colony base) still bypass the build queue,
  so they get no construction ghost and no queue row. Routing them through it means `PlaceDrawn`
  stores a drawn shape, so a 9-tile spaceport picks up `BuildScaling` cost ×10.8 and output ×12.6.
  **That is a balance decision, not a refactor.**
- Master plan C3 — verify framing on a 21:9 ultrawide. Needs a running build; only Jacob can do it.

---

## Terrain textures — regenerated as one library

Measured against the old set, the art was inconsistent in a specific, measurable way. Grain strength —
the relative standard deviation of the luminance ratio, which is the only thing
`TerrainTextureMap` actually reads — ran from **0.010** (beach, snow, ice: no visible grain at all) to
**0.409** (CrackedGround: a black net stamped over the terrain colour). A forty-fold spread. Several
tiles were not material grain at all but repeating motifs — an ornate scroll on `grass`, glyph blocks
on `ObsidianFlat`, hard diagonal corduroy on `Canyon`, `GasClouds` and `Island` — which read as a
symbol stamped once per cell and tiled across a continent.

All 42 files were regenerated to two rules:

- **Seamless by construction.** Value noise on an integer lattice whose period divides 16, cellular
  distance measured the short way round the torus, ripples at whole wavenumbers. Verified: every
  tile's wrap edge now falls within normal pixel-edge range measured *along its own axis* — which is
  what licenses the renderer's per-tile random offset, and therefore what stops a continent of one
  biome reading as wallpaper.
- **One contrast ladder.** Five permitted grain strengths, 0.060 (bare fractured rock) down to 0.020
  (snow and ice). Materials differ by how rough the surface genuinely is, not by how loud the tile is.

Families are shared, which is where the consistency comes from: mountain / Highlands / Hills / rocky
are cellular rock at four roughnesses; Canyon and Badlands are that rock with bedding planes through
it; forest / jungle / Taiga / swamp are one clumped-canopy field. Energy is spread across all four
octaves whose periods divide 16 on purpose — `Pattern()` box-filters the art down to 4 texels per cell
on the biggest worlds, and grain built only from single-texel speckle would average away to nothing
there, making the largest worlds the flattest-looking ones.

Tiles are authored in their palette colour so the folder is browsable, but the renderer divides the
mean out, so the tint changes nothing on the map. `TerrainColorMap` remains the single source of truth
for colour.

The previous art is recoverable from git at `0c71223`.
