# Requests from Jacob Maddux — 2026-10-09

**Source files:** given in a Claude Code session as three pasted blocks, restated below.
**Status:** Complete. Built locally in a Claude Code session, not by the CI bot.

## What they asked for

Checked first against the code; none of these were already built.

**Survey and indexes**
1. Thicker air gives a wider Weather band. At 2.8 atm it should cover about 3/5 of the surface (it covered about 1/5).
2. Solar index floor lowered to 20%.
3. Worlds with geothermal hotspots should get larger spots, or a few more (at least 4–5), not one or two small ones.
4. The largest spots can grow volcanoes at their highest point.
5. Frozen worlds get a new CryoVolcano terrain on hotspots reading in the low-to-mid 90s.
6. Geothermal is about subsurface pressure, not ambient heat.
7. Ores disabled, including named ores in points of interest. POIs should be mostly anomalies.
8. Survey tab index text cut to 1–2 sentences ("show, don't tell").
9. The index icons on the map stop the tile readout showing through them, and their hover text becomes 1–2 sentences on why to build there.

**Terrain**
10. Fix the 2,000–4,000 m low lines along plate edges.
11. Rounder plates.
12. Smooth heights across plate borders that aren't colliding, keeping collision and shear zones.
13. A/D and the Left/Right arrows scroll the Surface View map.

**System view**
14. Nameplates over surveyed bodies, and over discovered systems at the barycentre, with the empire chevron on the left.
15. Orbit rings and habitable-zone rings stay visible when zoomed out.
16. Terrestrial planets, moons and asteroids at 2× diameter; stars and gas giants at 1.5×.
17. Camera auto-follow when zoomed in on a moving planet or an undocked ship.
18. Planet Zoom Field:
    - wider than the furthest moon's orbit, planets only;
    - shown in Dev Mode as a circle with a grid infill, with colour and grid settings;
    - acts as the planet's Orbit tab membership and the place built ships wait;
    - shown during orbital construction, with a toggle in the Orbit tab.
19. Mass in the planet Overview, before Atmosphere.

**New game**
20. No pre-placed buildings and no claimed planets.
21. The player lands in the home system with the species habitable zone on.
22. At least one world at 85%+ for the chosen species.
23. The player picks a world. "Are you sure you want <name>?", then a naming window: name box, the caption "What would you like your Home World to be called?" below it, Confirm and Back.
24. On confirm, open the Surface Map to place the capitol, with a level-2 survey granted.
25. Then guided placement, with flashing yellow "!" on the tab and on the building:
    - a farm of 3+ tiles;
    - a combustion plant of 2–3 tiles;
    - a mine of 3+ tiles;
    - at least 2 city buildings.
26. The capitol is not in the build menu.

**Interpretations I made**
- **"City buildings"** are Habitat Blocks. Settlements, towns and cities are grown by population and can't be placed, so Habitat Block is the only placeable housing.
- **"A frozen world"** is an Ice Planet, or any world whose average temperature is below its own freezing point.
- **Vents.** Only plumes of at least 3.5 tiles radius can roll a vent (60% chance), and only on worlds with at least modest hotspot activity. The cryovolcano line is 93%.
- **Orbital construction** has no placement mode in the game. Queuing a station at a planet's shipyard flies the camera to that planet and shows its field for 8 seconds instead.
- **Starting fleet.** It waits at the cradle and moves to whichever world is chosen.
- **Choosing a starting world.** Any planet or moon in the home system with a surface can be chosen (not gas giants or asteroids). The confirm window shows its habitability.

## Slices

### 1. Survey and indexes
- [x] Weather coverage now reaches 0.60 at 3 atm (about 0.56 at 2.8 atm). Its equatorial falloff loosens with thicker air, so the belt itself widens.
- [x] `SurfaceIndex.Floor(Solar)` is 20%. The legend, yield, placement and `SolarViable` all follow it.
- [x] Geothermal plumes: every hotspot world gets 4–8 seeded round plumes, sized to its grid and ragged at the rim. The old noise patches remain on top of them.
- [x] Volcanoes: plumes are capped at 92%, so only a rolled vent core (97%, the highest ground) becomes a volcano.
- [x] New `TerrainType.CryoVolcano`, appended to the enum and registered in every terrain table. It spawns on frozen worlds at 93%+ and reads only the pressure field, never temperature.
- [x] Ores off (`OreGenerator.Enabled = false`):
  - tiles are cleared;
  - saved ore cells are ignored on load;
  - no ore-named deposit POIs, and anomalies carry no ore reward;
  - 2–4 anomalies per solid world, and ruins are rarer.
- [x] The six index descriptions are one or two sentences each, and Geothermal now reads as pressure. The long Survey-tab notes are cut to one line each.
- [x] Index icons:
  - the tile hover and clicks are blocked while the pointer is over the icons (`IndexIconBar.PointerOverAny`);
  - their tooltips are one "build X here" line (`SurfaceIndex.Why`).

### 2. Terrain
- [x] **Root cause of the low lines:** in `TectonicsMap.Sample` the edge-roughness offset was added to a distance measured from whichever side of the fault the point was on. Wherever the offset went negative, both sides swapped owner, and a strip up to ~1.7 tiles wide was handed to the other plate, then dropped by the crust step. The distance is now measured from a fixed side (the lower plate id).
- [x] Convergence and shear are now measured at the fault's midpoint, so they are the same on both sides.
- [x] `CrustAtSmoothed` eases the continental/oceanic step over 5 tiles. Convergent margins keep their full step, and rift troughs and collision uplift are untouched.
- [x] Rounder plates: 4 cells per plate instead of 6, and the small-scale edge crinkle is 0.2 tiles instead of 0.5.
- [x] A/D and Left/Right scroll the Surface View map. This is skipped while typing or while a menu is open, and the old mini-grid cursor no longer takes those keys while the Surface View is open.

### 3. System view
- [x] `NameplateManager`:
  - plates over surveyed bodies, and over discovered systems at the pivot, which is the barycentre;
  - the player's crest, or a rival's colour swatch, on the left;
  - border in the owner's colour;
  - no raycasts.
- [x] Orbit, habitable, owner and habitable-zone rings keep a minimum on-screen width (`MinScreenWidthLine`).
- [x] Sizes:
  - terrestrial/moon diameter coefficients doubled; the gas giant factor is 1.5 so giants come out 1.5×;
  - stars ×6 instead of ×4 (1.5×), with `RefScale`, size words, the halo clamp and the Dev slider updated to match;
  - `StarClearance` 3.5 instead of 4.5, so ring 1 still fits;
  - old saves are re-spaced only where bands now overlap.
- [x] `ZoomField.cs`:
  - per-planet field radius;
  - drawn in Dev Mode, from the Orbit-tab toggle, or after a station is queued;
  - Dev colour, opacity, size and grid-style sliders, saved;
  - parked ships in the field are listed in both Orbit tabs;
  - ships rolled out from a planet's shipyard tab now appear docked at that planet.
- [x] Camera zoom lock:
  - zooming in over a planet's field, or near one of your undocked ships, glides into follow;
  - zooming back out past 1.6× the lock height releases it;
  - a follow you start yourself (F) is not released this way.
- [x] Mass shows in the planet Overview, before Atmospheres.

### 4. New game
- [x] `ForceHomeWorld` still builds the cradle for the species, rated at 85%+ and locked, but no longer claims, settles or builds on it. Its moons are named after it.
- [x] After the intro the home system is framed, every world in it is surveyed to level 1, and the species habitable zone is shown (`SetSpeciesMode`).
- [x] Clicking a world opens "Are you sure you want X?", then the naming window: prefilled name, the caption, Confirm, and Back. Back returns to choosing; Enter and Escape also work.
- [x] Confirming:
  - claims the world and grants a level-2 survey;
  - re-seeds the economy and moves the starting fleet to it;
  - opens the Surface Map on Build with the capitol in hand;
  - the capitol is free, can only be placed now, is never in the menu, and settles the world.
- [x] Guided steps: Farm (3+ tiles; the minimum is now 3), Combustion Plant (2–3; minimum now 2), Mine (3+), then 2 Habitat Blocks.
  - Flashing "!" on the category tab and the structure card.
  - A banner across the top of the screen shows the current step, with an Open button.
  - A step advances as soon as the job is queued.
- [x] Saving is refused until the capitol is placed.

### 5. Follow-ups (my own calls, asked for as "improve what synergises")
- [x] A ship you send with "move here" to a point inside a planet's orbital field, with nothing else queued, docks there and rides along with the planet. Before, it held a fixed point that the planet orbited away from.
  - The right-click menu now reads "move into <planet>'s orbit" for those points.
  - Moves the AI gives your squadrons (patrol, escort, intercept, rally) keep their exact points.
- [x] The home-moon perks (`cradleMoon`) move to the moons around the world you actually choose. For a moon capital, that is its planet's other moons.
- [x] Clicking the star you are already looking at no longer switches the habitable-zone band off. This used to hide the species zone while you were choosing.
- [x] The flashing "!" also sits on the Surface View's main Build tab whenever you are on another tab.
- [x] Queuing a station frames the whole orbital field, not just the moons (`CameraController.FrameRadius`).
- [x] Nameplates require a real survey even in Dev Mode, so Dev Mode doesn't plate every rock in the galaxy.
- [x] The ore sections (the Survey tab's Mineral Survey and the Inspector's Ores tab) are hidden while ores are off.
- [x] The Geothermal Plant's description now talks about pressure, matching the index.

### 6. Second batch (same day)
- [x] Gas giants are back to their original size (`GasGiantDiameterScale` = 1 on the doubled base). Rocky worlds and moons stay at 2× and stars at 1.5×.
- [x] WASD and rotation camera keys are ignored while a text box has focus.
- [x] Selecting a surveyed world no longer pops its name over its nameplate. The old name line is hidden wherever a plate exists; the type and habitability lines stay.
- [x] No ships spawn at the start, around any world. The first hulls come from a shipyard the player builds.
- [x] While choosing, green rings mark only the starting options. Afterwards a moon is ringed only if it is settleable (habitability ≥ the colony threshold).
- [x] Difficulty:

  | | Starting world | Second option in the zone |
  |---|---|---|
  | Easy | 95%+ | 85% chance, 90%+ |
  | Medium | 80%+ | 50% chance, 70%+ |
  | Hard | 70%+ | none |

  - The second option is another planet in the zone (not a giant), or else a moon of a world in the zone, or else one of the cradle's moons.
  - It gets the species' climate (`MakeHabitable`).
  - Only these options can be chosen (`HomeworldOnboarding.Options`).
  - The start menu labels say so.

### 7. Third batch (same day)
- [x] While a building is held, the cursor window shows only that building's own index (value, efficiency, percentile), not the tile's other indexes. This covers drawn and fixed-footprint buildings alike.
- [x] After naming the world, the Surface Map opens on Build > Civil. The Planet Capitol is in that list with a flashing "!", instead of already being in hand.
  - It is listed only while the world is waiting for one, so it can be placed once and then leaves the list.
  - A bordered prompt beside the tabs under the map reads "Choose where to place your Capital".
- [x] Every ghost building (snapped to the grid or loose on the cursor) has a black border along its outer edge.
- [x] The Spaceport is now 2×2 instead of 3×3. It draws 1.5 power and no longer generates any or lights ground; its metal output is unchanged.
  - Spaceports already in old saves take the new footprint.
- [x] W/S and Up/Down pan the Surface View map north and south (alongside A/D). This works on moon panes too, and is ignored while typing or with a dialog open.
- [x] Two pre-existing minus signs that rendered as boxes in TMP are now plain hyphens (`check-ui-glyphs` clean).

### 8. Fourth batch (same day)
- [x] **Root cause of the missing index toggles:** the bar deactivated its own GameObject whenever it had nothing to offer. That also stopped the bar's own `Update`, the code that checks for something new to offer, so it never came back. It now stays active and is just empty when there is nothing to show.
- [x] There is ONE index bar, inside the top right of the map window, for the planet and all its moons:
  - an index is offered if any of them generated with it (`SurfaceIndex.Present`), or always in Dev Mode;
  - pressing it switches that overlay on or off for all of them together (`IndexToggles.ToggleSystem`, also used by the Survey tab's cards);
  - moon panes no longer have their own bars.
  - **Moon panes now actually draw index highlights.** They never had. Overlays were painted for the host planet only, so the old per-moon bars toggled state that nothing drew. Each moon pane now has its own overlay layer, painted by the same `PaintIndexInto` as the planet, on the moon's own survey.
- [x] Highlights: the higher bands' fill moves toward that band's border colour and gains up to 15% more opacity at the top band, so the best ground is the brightest thing on the overlay.
- [x] A shortcut column inside the bottom right of the map window, from the bottom up:
  - **Demolish:** a white 16×16 bulldozer on black that turns red while demolition is on.
  - **Power grid:** a lightning bolt.
  - **Terrain heightmap:** a white arch over a darker flat line with a grey up-arrow. It toggles the 500 m contour lines (`SurfaceTextureRenderer.ShowContours`) and rebuilds the maps.
  - Active toggles get a bright frame. The icons are drawn in code (`PixelIcons.cs`), so no new image imports are needed.
  - The tile readout and moon zoom/pan ignore the cursor over either button column.

### 9. Fifth batch (same day)
- [x] **No minimum building sizes.** Every free-drawn class starts at one tile. The Geothermal Plant is now a 2×2-minimum square like the Fusion Reactor. The founding power plant still gets 2 tiles, so a new colony keeps its starting power.
- [x] **Pixel art for every building** (`BuildingArt.cs`, drawn in code):
  - 16×16 tiles with two variants for single-tile classes. A cell's variant is picked "at random" by a stable hash, so it is the same while drawing and once built.
  - 32×32 sites for 2×2 classes (Capitol, Spaceport, Colony Ship Base, Geothermal, Fusion) and a 48×48 site for the 3×3 Shipyard.
  - A 3×3 nine-slice for the Storage Depot, so any length reads as one long building.
  - The build cards show the art at the smallest placeable size, replacing the old tetromino shapes. Placed buildings and the placement ghost use the same art (red blocks still mark invalid spots).
- [x] **Production strip** under the map, right of the tabs, on the Overview of a world you own. For Metal, Water, Energy and Food it shows income, upkeep and net, green or red (`WorldEconomy.cs`, which mirrors the economy's own tick).
  - Upkeep is energy drawn and food eaten. There is no metal or water upkeep in the game, so those show a dash.
- [x] **Tabs:** four across (Overview, Build, Survey, Orbit). A second row starts with a new **Terraform** tab, followed by Terrain in Dev Mode only.
  - Everything about terraforming (habitability ceiling, toggle, fault list, projects console) moved there from Survey.
  - The capitol prompt sits on the second row.

### 10. Sixth batch (same day)
- [x] Moons are back to their original size (diameter coefficient 0.44); rocky planets and asteroids stay at 2×.
- [x] **Index toggles:**
  - A world offers an index only if some of its ground actually clears that index's floor (`IndexIconBar.Has`). Previously, airless moons showed Weather in Dev Mode, and moons with no mineral ground showed Mineral.
  - Only worlds whose maps are open count toward the bar. A moon opened on its own from the solar system shows only its own indexes.
- [x] WASD/arrow panning keeps slowing as the camera zooms in (floor 0.06× instead of 1× below height 20), so a tap in a gravity well no longer overshoots the planet.

## Closing note

**Not compiled. Unity isn't installed on this machine, so please build before playing.**

`tools/Check-Scripts.ps1` is clean, and `index-rules-check` (51/0), `survey-check` (9/9) and `verify-wiring` pass. `system-composition-check.mjs` was already failing before these changes; it can't find a constant it searches for.

Three review agents checked the diff. None found a compile error. They did find these logic bugs, all fixed:
- plumes made volcanoes even without a vent;
- convergence flipped across the fault inside the new eased border;
- the star halo reached into ring 1 once clearance shrank;
- load-time re-spacing undid Dev edits;
- zoom lock could grab hidden or enemy ships;
- the lock glide ended with a jump;
- the field renderer leaked materials;
- A/D scrolled the map behind the pause menu.

The third review covered the new-game flow. It found no compile errors and these problems, all fixed:
- Escape in the opening's dialogs also opened the pause menu.
- A moon capital loaded with the wrong home world.
- The banner covered toasts and swallowed map clicks. It now sits at the bottom of the screen and takes no clicks.
- A world with no fertile or mineral ground could stall the tutorial forever. Impossible steps are now skipped, and worlds with no dry ground for a capitol can't be chosen.
- Survey rows could undo the granted survey.
- If the intro threw an error, the opening never started.
- The capitol re-held itself during demolition mode.
- The confirm panel quoted a price for the free capitol.
- A space typed in the name box paused the game.

**Changed on purpose:** the game is paused while you choose your starting world, and resumes when you confirm. This stops rivals expanding and the starting colony ship settling a world before you have one.

**Look at these first**
1. **The new-game flow end to end.** It is the largest change. In particular, check that the held capitol shows its ghost on the map and can be placed.
2. **Plate edges on a new galaxy.** Old saves keep their saved plate layouts and terrain, so the fix only shows on newly generated worlds.
3. **Sizes.** The inner system with bigger stars. Ring-1 worlds may be pushed slightly outward around large stars.
4. **Ships parked in open space** (by AI moves, or before this change) appear in a planet's Orbit list only while the planet is near them. Ships moved into an orbit with "move here" dock, and always ride along.

A fourth review covered the follow-ups and found no compile errors. It found these problems, all fixed:
- Docking fired on AI patrol and escort moves. It now applies only to the player's own "move here".
- Switching stars could drop the species zone mode.
- The terraform guarantee didn't follow the home-moon flag to the new home moons.
