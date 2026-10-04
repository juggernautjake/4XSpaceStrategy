# Requests from Jacob Maddux (with Raptok) — 2026-10-04

**Source files:** given in a Claude Code session, recorded in full below. Raptok's storm-cell screenshot was attached in-session and is not in the repo.
**Status:** Complete. Built locally in a Claude Code session, not by the CI bot.

## What they asked for

1. **Tidally locked temperature.** Tidally locked worlds should be hot on the sunny side and much colder than normal on the far side.
2. **Mountains everywhere.** Stop covering planets, moons and asteroids in Mountain tiles. Mountains should mean a drastic rise above the surrounding terrain. A grey rocky Barren type, or a lighter grey dusty Regolith, is fine instead.
3. **Habitability is broken.** A ~500 °C airless planet rated 13%, while a ~40 °C Terran rocky world with a full atmosphere and a biosphere rated 6%.
4. **The habitable zone.** It should be where water can be liquid (warm enough that ice melts, not so cold that all water freezes). It should be consistent for a given star; it keeps changing. With multiple stars, use the hottest one.
5. **Gas giant storm cells (Raptok).** They should look like wider, more rounded sections of the storm belt. Today the edges look pinched off, the top and bottom curve is far too clean (as if a smooth slice was cut out), and the shape is a stretched football. Jupiter's Great Red Spot is the reference.

## What was wrong, and what was built

### 1. Tidal lock temperature
- [x] New `PlanetTemperature.TidalOffsetC`:
  - The day side is hottest under the star, at the map centre. That is the same point the Solar index lights and the globe keeps facing the star.
  - The night side drops to full cold just past the terminator.
  - Airless worlds swing about +110 / −170 °C. Thick air evens that out to about +35 / −55 °C.
- [x] It feeds both terrain generation (biomes freeze and bake to match) and the per-tile °C readout.
  - On hot worlds the night side only *freezes* if it actually gets cold. A 130 °C night is not snow.
- [x] Spin Up redraws the ground once the lock is broken, without wiping terraform progress.

### 2. Mountains
- [x] **Root cause:** the alpine "this is mountain" line was measured from the sea level. On a dry world the sea is deliberately parked below the deepest basin, so the line sank under the ground and almost every tile became Mountains. Only the low basins survived, which were the salt flats and craters in the report. The readout had already been moved onto a proper datum; the classifier never was. It now measures from the same datum.
- [x] This also fixes the "Ocean over Mountains" readout, where the ground under every sea was being classified as mountain.
- [x] **New `Regolith` terrain:** light grey dust on smooth airless ground, with Barren rock where the ground is broken. It is registered in every terrain table and reuses the existing desert grain art.

### 3. Habitability
- [x] **Root cause:** the score was orbital position × body-type preference. It never read temperature, water or life.
- [x] It now scores the world as it is, multiplied together:
  - temperature against the species' ideal °C
  - air
  - liquid water and a biosphere, only for liquid-water species
  - gravity against the species' own home mass
  - body-type preference, as a modest weight
- [x] Black-hole systems rate near zero.
- [x] Moving a planet's orbit now re-derives its starlight, and the ground is redrawn when it arrives. The sandbox re-rates after every edit.
- [x] Your example now comes out the right way round: the 40 °C living world rates about 47% and the 500 °C airless world about 0%.

### 4. Habitable zone
- [x] **Root causes:**
  - Saves stored only each star's *class*, so every load re-rolled its luminosity and the zone moved under planets that didn't.
  - The green band drawn was the current species' preference, so switching species moved it.
  - Multi-star systems summed every sun's luminosity.
- [x] Each star's rolled physics (and name) are now saved.
- [x] The green band is the star's own liquid-water band (`StarDatabase.ApplyZone`).
- [x] Multi-star systems take the zone from the **hottest** sun. An O or B companion means no stable zone.
- [x] The "Habitable" label uses the same band as the green ring.
- [x] Homeworlds are placed inside the overlap of the species' band and the star's band, so they're always inside the green.
- [x] Dim binaries whose inner rings fall inside the zone leave those rings empty, so an ordinary world is never closer in than the habitable one.

### 5. Gas giant storms
- [x] **Root cause:** a storm was a pointed lens (the pinched tips and football shape) inside a ruled pale ring (the clean slice).
- [x] A storm is now a **swelling of its belt**. The band's latitude is remapped, so the belt widens into a rounded oval and the pale zones bow around it.
  - Its edge is the belt's own ragged edge, so there are no points and no slice.
  - Proportions are like the Great Red Spot, about 1.5:1.
  - Overlapping storms are moved apart, and storm hearts are always solid.

## Verification
- **Not compiled.** Unity isn't installed here. Please build before playing.
- `tools/Check-Scripts.ps1` is clean.
- **Check tools** (all pass):
  - New `tools/world-rules-check.mjs`: your habitability example, every species' own kind of home rating well, tidal day/night, hottest-star zone.
  - `tools/terrain-elevation-check.mjs`: new dry-world probe, where ordinary ground is not mountain at any water level.
  - `tools/gas-giant-check.mjs`: rewritten for the new storms, checking that every storm centre is storm, every storm out-swells its belt, and there are no holes.
  - `index-rules-check` and `survey-check`.
- **Review agents:** 2 on the first pass, which found 12 issues, all fixed. A final whole-diff pass found one more real bug, now fixed: orbit heat was *reset* to the orbit formula rather than *shifted* by the move, so the first nudge would have frozen a homeworld. It also found that Dev "Reset orbit" didn't restore heat (fixed), and that the storm-overlap fix needed a retry (fixed).
- **Known and left as is:** in a sparse system around a dim binary, leaving the in-zone inner rings empty can reduce the world count by one or two.

## Look at these first
- **Old saves keep their old ground.** The saved terrain wins on load, so worlds already generated keep their mountains. Start a new galaxy to see the fix.
- **Old saves of multi-star systems** will see their zone shift once, because it now comes from the hottest sun.
- **Terraforming vs. the new rating:** the terraforming ceiling (`Terraformability`) is still the old position-based figure, while the rating now reads real conditions. Re-rating on load or a species switch can therefore pull a terraformed world's number back toward what its climate actually is. That interplay predates this change, but the gap between the two is wider now.
