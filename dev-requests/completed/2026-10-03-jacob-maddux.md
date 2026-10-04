# Requests from Jacob Maddux — 2026-10-03

**Source files:** `Index Changes and World Modifiers.txt`
**Status:** Complete (built locally in a Claude Code session, not by the CI bot)

## What they asked for

An overhaul of the survey indexes plus a new World Modifier system:

- **Floors.** No more universal 70% floor. Every index bottoms out at 40%, except Minerals, which stays strict (70%).
- **Hydro.** Water tiles read 100%. The index radiates 6 tiles inland: 90s close in, 80-89 at the third tile, 60-79 at the fourth, then a steep drop.
- **Solar.**
  - The whole surface is covered.
  - Distance from the star sets the maximum: up to 100% close in, 50-60% at the centre of the habitable zone, mid-40s or lower beyond it.
  - Each atmosphere takes 10 points off.
  - Each 1,500 m of elevation adds or removes 10 points.
  - Tidally locked worlds have a permanent day side and a dark night side.
- **Weather.**
  - Higher atmospheres give higher maximums.
  - Hotspots need a reason: they gather at the equator and are driven by temperature and altitude differences ("storm zones").
- **Fertility.**
  - The floor is 40.
  - Plant biomes no longer mean 90%+.
  - The maximum depends on how close the world is to 20-22 °C, with a bonus at 40%+ water.
- **World Modifiers.**
  - Tidally Locked, High Quality Minerals, Vast Mineral Deposits, Extreme Weather, Fertile World and Continental Plates.
  - Rolled by chance. Several can stack on one world.
  - Badges are bordered in the colour of their index, at 16×16 pixel art. They go across the top of the Overview's planet sphere and under the Planet View map's bottom-right corner, level with the tabs.
  - After a level-1 survey a badge shows "?" with a tooltip saying something is unique. After level 2 it shows the real icon with a description.
- **Follow-up.** The Fertility index icon becomes a green apple tree instead of the yellow grain.

**Interpretations I made:**

- **Hydro ring ranges:**

  | Tiles from water | Range |
  |---|---|
  | 1 | 94-98 |
  | 2 | 90-93 |
  | 3 | 80-89 |
  | 4 | 60-79 |
  | 5 | 50-59 |
  | 6 | 40-49 |

  Bigger bodies of water sit higher in each range.
- **Solar elevation** is proportional: 750 m gives +5. It is not stepped. Water tiles count as 0 m, the sea surface.
- **Solar distance curve:**
  - 100% at the innermost placement ring.
  - 55% at the centre of the habitable zone.
  - 46% at its outer edge.
  - Falls off further beyond that.
  - An Earth-distance world gets about 61% before its air, so roughly 51% at sea level under 1 atm.
- **Fertile World:** 18-25 °C and 40-60% water, on a living world. A world meeting that at generation has a 65% chance of being one. It stays one only while the climate holds.
- **Continental Plates** is derived from `hasTectonics` rather than rolled. **Tidal lock** is planets only, about 1-10% by orbit, and likelier close in.
- **Earthquakes still only damage 70%+ Geothermal ground,** even though plate lines (40%) are now harvestable. The Continental Plates description says so.
- **Worlds you own** show their modifier icons in full, the same way their indexes are already fully shown.

## Slices

### 1. Per-index floors
- [x] `SurfaceIndex.Floor(k)` sets Mineral to 70 and everything else to 40. Drawing, yield (`Productive(k, v)`), bands (`Steps`/`Band`) and placement all read it.
- [x] Survey level-2 still uses 3 passes per index. `Survey.ResolvedBand` maps those passes onto 3 or 6 bands. Overlay and plate-line reveal are updated to match.
- [x] The legend labels each index from its own floor. Planet View note text is updated, and the earthquake threshold is held at its own 70%.

### 2. Hydro
- [x] Water tiles read 100%, and the 6-tile ring falloff is in. Hydro is read absolutely.

### 3. Solar
- [x] The whole surface is covered, from orbit maximum minus 10 per atmosphere plus ±10 per 1,500 m. Solar is read absolutely.
- [x] Tidally locked worlds: the map-centre hemisphere is lit and the night side reads 0. `OrbitController` keeps that face turned toward the star or barycentre.
- [x] `SolarViable` and `Present` are based on whether the world's highest ground reaches 40. The Solar Array gate message and the Survey readout show the breakdown.

### 4. Weather
- [x] Storm zones combine equatorial weighting with elevation contrast and land/sea contrast, blurred into zones. Thicker air makes larger zones.

### 5. Fertility
- [x] The ceiling is set by climate (20-22 °C, plus water) and the band is bottom-heavy. Fertile World gets broad coverage, a 100% ceiling and a top-heavy band.

### 6. World Modifiers
- [x] Data model, rolls, save/load, and the effects on each index (coverage, ceiling and curve).
- [x] Badges in the Inspector Overview globe and the Planet View tab row: "?" at level 1, the real icon at level 2, tooltips at the cursor.
- [x] Seven 16×16 icons plus the apple-tree Fertility icon, from `tools/make-modifier-icons.mjs`. `import-index-icons.mjs` no longer copies the grain back.

## Closing note

**Not compiled. Please build in Unity before playing.** Unity isn't installed on the machine this was written on. `tools/Check-Scripts.ps1` is clean. Three review agents checked the diff for compile errors, logic and UI/visual issues. The compile pass found nothing. The other two found 9 real issues, all fixed:

- The homeworld could come out tidally locked.
- Checking whether Solar was present scanned and sorted the whole grid, and could go stale during orbit migrations.
- Extreme Weather outlived a thinned atmosphere.
- High Quality Minerals could invent seams on a world with none.
- Your own worlds showed "?" badges.
- The badge row kept stale layout when it rebuilt, and could leave a tooltip stuck on screen.
- The Fertility curve wasn't cached with the rest of its band.
- Asteroid-belt rocks were rolling modifiers.
- The quake wording didn't match the actual 70% damage rule.

**Look at these first:**

1. **Tidal-lock facing.** The face toward the star is the sphere vertex whose texture point is nearest the map centre, which can be a few degrees off. Check that the bright half of the Solar map is the half facing the sun.
2. **Six-band legends.** Non-mineral legends now have six swatches (40-90). Check the strip still reads at the card's width.
3. **Solar on Earth-like worlds.** Following the spec, an Earth-like world under 1 atm sits around 50% at sea level, and a 1-atm world at the habitable zone's outer edge (36%) only gets solar on high ground. That is intended, but it is a big balance shift.
4. **Old saves** load with no rolled modifiers; only Continental Plates shows, because it is derived. Start a new galaxy to see the rest.
