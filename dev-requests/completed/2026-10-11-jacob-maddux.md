# Requests, 2026-10-10 / 2026-10-11 — Jacob Maddux

Playtester: Raptok (Michael). Rule for this stretch: push to `main` only work that is complete and checked;
in-progress features stay local until done.

## Done and pushed

**Living surface** (4eb6c02, 292aa94)
- Building animations on the Surface View: smoke, steam, turbines, mine and shipyard sparks, a farm
  harvester, beacons, and window lights. A building that needs power and lacks it stands still and blinks red.
- Area of effect on hover, selection or placement: power plant and pylon reach (PowerGrid.CoverageOf),
  and the Power Distribution boost ring.
- Animated power flow when a building on a grid is selected: spans and pulses coloured by grid health.

**Building card** (4eb6c02, 292aa94)
- Click a placed building to see its type, how many of that type are on its grid, and the grid's output
  and upkeep for that type. Closes on any click elsewhere; clicking the building again reopens it.

**Map and overlay fixes**
- Clicking a star no longer shows the old floating name and "Star system · owner" labels (4eb6c02).
- The power grid shortcut works on every tab, and the grid highlight is electric blue (95113ee).
- Moon maps wrap without a seam: the mirror copies carry the index highlights and the survey fog (7304677).

**Starts** (4eb6c02)
- Every starting world has Vast Mineral Deposits.
- Starting stock is at least 900 metal, 650 energy and 300 water.
- The opening adds a Shipyard step.

**Ships** (4eb6c02)
- RaceVfx: per-race plume colours, hull glow and pulse (Pyrothian lava glow), nav lights (Terran, Cryithn)
  and weapon tints.
- Tint and livery now look up glTFast material properties.

**Worlds** (292aa94, aa9169d, 0e643e0, 3819107)
- Worlds moved by the orbit-spacing backstop now have their climate re-derived. This fixes the
  "[WorldConsistency] starlight heat outside what its orbit gives" warnings. It runs one world per step,
  which fixes the GenProfile stall.
- One habitability rule for every species and star:
  - A species' zone is the set of orbits where its own kind of world sits within its temperature tolerance.
  - Every homeworld is built at its ideal and rates excellent for its own species.
  - Pyrothians start on ring 3 at 350-370 C; Cryithn start at -54 C; Sylvans start on a rocky world.
- Runaway-greenhouse (Venus-like) worlds inside the inner edge, with a yellow haze and full overcast.
- Temperature chain audited: farther out is colder, more air is warmer, and liquid water exists only in
  the habitable rings.

**Build hygiene** (af82903)
- Fixed a compile error caused by pushing a half-finished edit.
- OreGenerator.Enabled is now static readonly, which clears the two CS0162 warnings.

**Later the same day (all pushed)**
- Power card: grid ledger and the building's role (2689626). A pylon shows Grid power and Grid draw
  (db9145d).
- Fog of war in player mode, the Gravity Wells toggle and the Solar System Grid (7a70d47). Rival ships,
  derelicts, comets and effects from hidden ships are now hidden; the camera stops softly at an unexplored
  system's edge (930d99b, 3a0f904).
- Generation stalls: moved worlds are built in steps (db9145d), and GenProfile names the slow work
  (db97e3a).
- Planet and moon viewers rebuilt, and the placement controls and failure messages audited (393da6f).
- Industry chain and placement synergies; power through adjacent buildings (3 hops), with the nearest
  buildings served first when a grid runs short; grid tags and a grid list (e2fc20e).
- Survey progress bars in the Planet View and above ships, scan effects, survey and research fixes,
  Dev "survey fully now" (Ctrl+Shift+U); ship markers, selection, the fleet window and orbit lists
  (5347f6a).
- "Why is it like this?" explanations for every world; generation fixes for worlds with no valid cause
  (980067c).
- Full 29-hull fleets for every race via the Meshy API (6fdbe20); Cryithn texture check keyed on
  brightness (5a4838f).

## Open decisions for Jacob
- Spaceports and shipyards are now queued builds (they were instant). Reverting is one line in DoPlace.
- Clicking a ship no longer flies the camera to it (F, Tab and the lists do).
- The Aquarii homeworld reads as an "archipelago world" (36-62% water).
- Cryithn Dreadnought and Mega-Station need a re-roll after the Meshy credit reset (38 credits left; a
  hull costs 39).
