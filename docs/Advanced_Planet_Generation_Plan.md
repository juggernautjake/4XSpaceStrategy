# Advanced Planet Generation — attributes first, type derived

Spec received 2026-07-24. The inversion: stop *picking* a world type and deriving its
attributes; instead set the attributes in a fixed order and let the type **emerge** from them.
The same path runs for planets and moons, so a big enough moon in the right place becomes a
temperate world exactly like a planet would — the spec's headline example.

**No Unity in this environment. Nothing below is compiled or run.** Existing saves store each
world's full state, so this changes only NEW galaxies — no save migration.

---

## The generation order (spec §1–6), one path for planets and moons

1. **Mass** — rolled from the orbital BAND and a size rank, not from a chosen type. Gas-giant-scale
   masses appear in the cool/cold bands; asteroid-scale at the small end.
2. **Magnetic field** — mass drives the odds (≥2 likely and rising with mass, <2 rare). A field
   allows 1 atmosphere per mass; without one the ceiling halves. *(Already built in AtmosphereRules.)*
3. **Tectonics** — rolled for terrestrial masses (~1/5). Active plates add atmosphere capacity
   (+2 if strong enough for volcanoes, else +1). *(TectonicsRules + AtmosphereRules.TectonicBonus.)*
4. **Atmosphere** — 1/mass, halved without a field, raised by tectonics, and cut hard for worlds
   closer to the star than the habitable zone. *(AtmosphereRules, extended with the inner-orbit cut.)*
5. **Temperature** — from the host star's warmth at this distance (`BiasHeat`). Inner = hot,
   outer = cold.
6. **Water level** — near-zero inside the habitable zone (too hot); ice in the cold outer band;
   liquid only where the temperature allows.
7. **BioSphere** — eligible only in the habitable zone, ≥1 atmosphere, moderate temperature, ≥25%
   water. Ceiling from the water/temperature average. *(BiosphereRules, already built.)*

Then **classify**: `WorldClassifier` reads the finished attributes and returns the physics type
(the existing 8-value enum) plus a descriptive **world-class name**.

## Slice 1 — `WorldClassifier` + the unified attribute-first path — **BUILT**

* New `WorldClassifier`: pure functions from attributes → `CelestialBodyType` (physics) and →
  a descriptive class name, plus a terrain-bias step so a named class actually looks like itself.
* `MassRules.ByBand` rolls mass from orbital band + size rank.
* `SolarSystemGenerator` rewired: both the planet loop and the moon loop set attributes in the
  order above and then classify. `RollBodyByTemperature` / `RollMoonType` retired.
* Moons are no longer a separate species — a qualifying moon classifies to the same temperate
  types a planet does.

## Slice 2 — Descriptive world classes (no enum surgery) — **BUILT**

The physics enum stays 8 values — growing it would mean touching `Species.typeAffinity[8]`, save
serialization and a dozen switch statements, all uncompilable here. Instead the **variety the spec
asks for is a DERIVED name over the attributes**, and the terrain is biased so each looks distinct:

* **Continental** — temperate, land > ocean.
* **Archipelago** — temperate, high water but broken into island chains.
* **Desert** — temperate-hot, dry, thin biosphere; oases where water collects.
* **Savanna** — warm, moderate water, grassland biosphere.
* **Swamp** — warm, wet, lush biosphere.
* **Tundra** — cold edge of habitable, some liquid water, sparse life.
* **Ocean** / **Terran** — as today, at the wet and balanced ends.
* **Toxic** — Venus-like: thick atmosphere (≥4) and very hot.
* **Molten** — extremely hot, essentially a lava world.

The name is computed on demand from live attributes, so it updates as a world is terraformed —
no stored field, no migration. It shows in the Overview, the inspector and the Dev sandbox.

## Slice 3 — Sandbox reads the classification live — **BUILT**

The Terrain Sandbox already exposes Mass, Magnetic Field, Tectonics, Atmosphere, Temperature,
Water Level and BioSphere sliders (built in the Atmosphere work). It now shows the world-class the
current slider settings produce, so a developer can dial in the prerequisites and watch the type
change — which is the spec's "prerequisite settings represented with sliders" ask.

## Review fixes — 2026-07-25

A hard review of the slices above (nothing compiles here, so this was by reading) found four defects,
now fixed:

* **Biome amplification was mostly dead.** `AmplifyBiome` ran BEFORE `biosphereActive` was set, in
  both `ApplyWorldPipeline` and `EnsureHabitableWorld`. The descriptive class it keys off
  (`WorldClassifier.Describe`) only names the lush/cold biomes — swamp, savanna, tundra — when the
  world is alive, so with the life flag still false every temperate world read as a lifeless
  desert/barren and only the desert amplification ever fired. Reordered to classify → set biosphere →
  amplify → capture-natural (capture must stay last, since amplify moves heat/moisture).
* **A mass-7 gas giant could land in the habitable zone.** The temperate mass band lerped to exactly
  7, and `Random.value`'s inclusive 1.0 made mass 7 reachable — which classifies as a gas giant, in the
  one band meant to guarantee landable worlds. Capped just below the gas-giant floor (6.99 → mass 6).
* **The inner-orbit atmosphere cut was capacity in name only.** Generation applied it to the rolled
  value but the ceiling terraforming reads (`AtmosphereRules.Ceiling(body)`) did not, so a scorched
  inner world could be terraformed straight back to its full mass-based air. The cut now lives on the
  ceiling itself — a standing cap, distinct from reversible heat boil-off — so honouring spec §3 needs
  the (still-unbuilt) "move the orbit outward" project to lift it. Temperate/outer worlds are untouched
  (`InnerOrbitRetention` is 1 there); the Dev sandbox breakdown notes the cut when it applies.
* Stale tectonics comment (said ~1/3; the rule is ~1/5 rising with size, per spec §2) corrected, and
  the orphaned `MassRules.ForType` — no caller since the switch to attribute-first — removed.

## Slice 4 — Terraforming can finally MOVE the atmosphere — **BUILT**

Reviewing the terraform side turned up a bigger hole than "the orbit project is missing": **nothing in
the game ever wrote `b.atmospheres` outside generation.** Core Ignition's description has always promised
"it raises the roof, and the atmosphere projects fill the room" — but there was no room-filling code
anywhere, so the roof went up and the room stayed empty. Which also means the standing caps this spec
added (no magnetic field halves it; a close orbit strips it) were capping a number terraforming could
never move in the first place. They looked enforced and were inert.

* **`AtmosphereRules.SustainableCeiling`** — the structural `Ceiling` (mass, field, tectonics, orbit) cut
  again by `HeatRetention` at the world's *current* heat. This is the limit terraforming reads. Generation
  already applied heat at roll time, so without the same cut here an air project could pump a hot world
  back up to a ceiling physics says it cannot keep — the identical bug the inner-orbit review fix closed,
  one term over. Heat being the *reversible* cap is the point: shading a world raises this number.
* **Air projects deliver air.** Atmospheric Processors 45% of the sustainable ceiling, Cometary
  Bombardment 30%, Oxygen Cascade 25%, Scrubbing 15%, Atmospheric Thinning −35%. A fraction rather than a
  flat number so one project reads the same on a mass-1 world as a mass-6 one. No project reaches the cap
  alone and the set does not quite sum to it — air is accumulated, not switched on. Falling below the 0.6
  floor runs the same `ApplyWaterLoss` generation uses, so bleeding a sky really does cost the oceans.
* **`TerraformFeasibility`** — the spec's missing failure warnings, as a single `Warning(body, project)`
  call `CanStart` makes. Air projects on a world with no headroom, water projects on a world too thin
  (boils to space) or too hot (arrives as steam), melted ice caps that would sublimate straight off.
  Every message names *the project that lifts the cap*, because the refusal is the tutorial: cool it →
  processors, restart the core → shield, move it out → air. Not waived in Dev Mode, unlike the tech and
  cost gates — those are about what the player has earned, this is about what the world can physically do,
  and forcing it through would mark a project complete having changed nothing.
* **`OrbitTooClose` is now diagnosed from the air cap**, species-independently. It was only ever raised
  from *starlight*, and only at the extreme (`over > 0.85`) — a species-relative test, so a world quietly
  held to a fraction of its mass's air was never diagnosed and Orbital Migration was never offered for the
  reason §3 cares about. The stellar wind does not care who is trying to live there. 0.9 deadband so worlds
  just inside the edge don't get told to move a planet over a 2% cut.
* Atmospheric Scrubbing is deliberately *not* headroom-gated — its job is taking poison out of a sky, and
  gating it would withdraw the fix for a toxic atmosphere from volcanic worlds, the only worlds that have one.
* Dev sandbox gains a **"Terraformable to"** readout (sustainable ceiling, % boiling off, headroom) next to
  the structural ceiling, so the gap between the two is visible as the Temperature slider moves.

This closes spec §3: the inner-orbit cut is a real limit on a real quantity, and Orbital Migration —
Outward is the project that lifts it, offered when it applies and described as doing so.

## Not attempted here, and why

* **New physics enum values** (a distinct Molten/Toxic *type* rather than a derived name). Would
  touch the affinity array, save format and every type switch; not safe without a compiler. The
  derived-name approach delivers the naming and the visual variety without that risk.
* ~~The inner-orbit orbit-shift project and the water/air terraform FAILURE warnings~~ — **built in
  Slice 4 above.**

* **Nothing here has been compiled or run.** Slice 4 changes what `CanStart` refuses, which is the kind
  of change that only shows itself in play: if some world type turns out to be unable to reach any of
  its air projects, the numbers to move are the `AirGainFraction` values and the 0.9 orbital deadband,
  not the structure.
