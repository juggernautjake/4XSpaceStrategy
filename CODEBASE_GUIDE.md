# 🛠️ Codebase Guide — 4X Space Strategy

A per-file, per-function map of the whole game so you can find and change anything fast.
Grouped by folder under `Assets/Scripts/`. **Bold** = the important entry points.

> Architecture in one paragraph: A **data model** (bodies, tiles, ores, stars, species) is produced
> by the **generators**, made physical by the **visual** layer, and edited/inspected through a
> **runtime-built UI** that self-assembles via `GameBootstrap` (no Inspector wiring needed for the
> new features). `SystemContext` is the global handle to the current system; `SpeciesManager`,
> `ResearchManager` and `SaveSystem` hold cross-cutting state.

---

## Data (`Assets/Scripts/Data/`)

### CelestialTypes.cs
Enums: `CelestialBodyType` (Asteroid, Moon, RockyPlanet, IcePlanet, VolcanicPlanet, GasGiant,
BarrenPlanet, OceanPlanet), `StarType` (G,F,K,M,A,B,O), `ResourceType` (Metal, Energy, Water).

### TerrainTypes.cs
Enum `TerrainType` — ~40 biomes (core + temperate + cold + hot + volcanic + exotic). Every value
must have a colour in `TerrainColorMap`.

### CelestialBody.cs
The core runtime model for a planet/moon/star-body.
- Fields: `id, name, type, resources, surfaceSize, surface`, `moons`, terrain identity
  (`terrainSeed, continentFrequency`), `pointsOfInterest`, orbit params
  (`orbitRadius, orbitSpeed, orbitPhase, orbitDirection, inclination, eccentricity, verticalOffset,
  spinSpeed, showRing`), habitability (`distanceFromStar, habitability, isHabitable`), and
  non-serialized `visualObject`, `parentBody`.
- `CelestialBody(type)` — ctor, sets defaults.

### TerrainTile.cs  *(note: lives in Scripts root — see below)*

### OreTypes.cs
Enum `OreType` (None + 14 ores from Ferralite → Xenocryst).

### OreDatabase.cs
- `class OreInfo` — one ore's data (name, tier, value, description, uses, refining, researchCost, color).
- **`OreDatabase.Get(type)`** — returns the `OreInfo` (builds the DB lazily).
- **`OreDatabase.All()`** — enumerates all ores (skips None).
- `Build()` — defines all 14 ores.

### PointOfInterest.cs
- Enum `POIType` (AncientRuins, Settlement, SpecialResource, Mystery).
- `class PointOfInterest` — type, normalized `u,v`, title/description, `explored`, `relatedOre`,
  mystery `revealTitle/revealText`.
- **`HoverText()`** — tooltip string; hides mystery details until explored.

### StarData.cs
- `class StarData` — `temperatureK, luminosity, lightIntensity, color, visualScale`,
  `hasHabitableZone, hzInner, hzOuter`; helpers `HzCenter`, `HzWidth`.
- **`StarDatabase.Get(type)`** — builds a `StarData` from spectral class; computes light + base
  habitable zone (`hzInner/Outer` scale with √luminosity); O/B stars get no zone. `AU` maps
  astronomical units → game units.

### Habitability.cs  *(species-aware)*
- **`GetZone(star, species, out inner, out outer)`** — the species' shifted/scaled Goldilocks band.
- **`InZone(star, species, distance)`** — is a body physically inside that band.
- **`Rate(star, species, type, distance)`** — 0..100 score (positional falloff × species type affinity).
- `Label(rating, inZone)` — "Habitable/Marginal/Hostile/Uninhabitable".
- **`ScoreColor(rating)` / `ScoreColorHex(rating)`** — red→yellow→green gradient for the score number.

### Species.cs
- `class Species` — name, signature, description, **biology/habitat/strengths/weaknesses**,
  attributes (`iq, longevity, fertility, durability, adaptability`), `idealTemp`, `tolerance`,
  per-body-type `Affinity`. `AttributeLine()` formats the stats.
- **`SpeciesDatabase.All` / `Get(index)`** — the 5 species (Terrans, Aquarii, Pyrothians, Cryithn, Sylvans).
- **`SpeciesManager`** — `Current`, `CurrentIndex`, event `OnSpeciesChanged`.
  - **`Select(index)`** — switch species, recompute the world, fire the event.
  - **`RecomputeWorld()`** — re-score every body's habitability for the current species + refresh the zone.

---

## Generation (`Assets/Scripts/Generation/`)

### PlanetTerrainGenerator.cs  *(resolution-independent, deterministic)*
- `struct NoiseParams` / `struct Sample`.
- **`GenerateSurface(body)`** — low-res tile grid for the viewer/gameplay.
- **`GenerateSurfaceWithParams(...)`** — same but with the Terrain Editor's slider values.
- **`SampleNormalized(body, u, v, params, octaves)`** — THE shared sampler; both the grid and the
  detailed texture call it, which is why both views share the same continents.
- `IsWater(type)`, `FBm(...)`, and per-type biome classifiers (`Terran, OceanWorld, Ice, Volcanic,
  Barren, Airless, GasGiant`).

### SolarSystemGenerator.cs
- **`GenerateSystem()`** — rolls a star, lays out bodies with distances/orbits, generates surfaces,
  ores, resources, POIs, moons, habitability, and procedural names. Sets `currentStar`.
- `MakeBody(type)` / `SeedTerrain(body)` — create a body and give it a stable terrain seed BEFORE
  generating its surface.
- `ApplyHabitability(body)` — scores a body for the current species.
- `RollMoonCount, RollBodyByDistance, RollStarType, RollSurfaceSize`.

### ResourceGenerator.cs
- **`GenerateResources(body)`** — bulk Metal/Energy/Water per body type (now covers every type).

### OreGenerator.cs
- **`Populate(body)`** — seeds mineral-rich tiles; ore choice depends on planet type; higher tiers
  are gated so exotics stay rare.
- `TerrainAffinity, PoolFor, TierAcceptance, WeightedPick`.
- **`OresOnBody(body)`** — distinct ores present on a body.

### POIGenerator.cs
- **`Populate(body)`** — places SpecialResource (from real high-tier ore), Settlement (habitable
  worlds only), AncientRuins, and Mystery POIs. `TryFindLand` rejection-samples non-water spots.

---

## Managers (`Assets/Scripts/Managers/`)

### GameManager.cs
- **`Instance`**, `CurrentBodies`, `CurrentStar`.
- **`GenerateStartingSystem()`** — fresh game: reset research, generate + visualize.
- **`LoadSystem(bodies, star)`** — display a loaded/deserialized system.
- `Visualize()` — hands bodies+star to the `SystemVisualizer`.

### CameraController.cs
WASD pan + mouse-wheel height, fixed 55° pitch, ignores input over UI.
- **`UpdateClipPlanes()`** — drives the far/near planes from the current height every frame. The scene
  shipped with Unity's default 1000 far plane, which silently clipped the whole galaxy away and made
  zooming out look broken; this is what actually lets you pull back to the full map.
- **Proportional zoom** — each scroll notch scales height by a percentage, so one notch feels the same
  at a 4-unit moon close-up and a 2,500-unit galaxy view. A fixed step can't serve both.
- **`ViewWholeGalaxy()`** (HUD "Galaxy" / **Home** key) — centres on the galaxy and frames every system.
- **`GalaxyRadius()` / `HeightToFrame(r)` / `ZoomCeiling()`** — the zoom-out limit is derived from the
  galaxy's real extent, so you can always reach the whole map and never further.
- Panning stays enabled at wide zoom even with a planet selected (the mini-map cursor only owns WASD up close).

---

## Systems (`Assets/Scripts/Systems/`)

### TimeController.cs  *(legacy; Space toggles 1×/5×)*
Writes `Time.timeScale = timeScale` every frame — the HUD sets `TimeController.timeScale` to stay in sync.

### SystemContext.cs
Global handle to the current system.
- **`Set(bodies, star, starT, parent, vis)`** — called by the visualizer; fires `OnSystemChanged`.
- **`AllBodies()`** — flattens planets + moons.
- Fields: `Bodies, Star, StarTransform, SystemParent, Visualizer, Zone`.

### OrbitalMechanics.cs  *(Kepler defaults, clamped)*
- `StarMass(type)`, `BodyMass(body)`.
- **`AngularSpeedDeg(primaryMass, radius)`** — deg/sec from √(mass/r³), clamped 0.4–30 (stable).
- **`PlanetAngularSpeed(star, r)` / `MoonAngularSpeed(planet, r)`**.
- `OrbitalVelocity`, `PeriodSeconds`, `Spin(body, variance)` — display + spin defaults.

### ResearchManager.cs
- **`NewGame()`**, `IsDiscovered/IsResearched`, **`Discover(ore)`**, **`CanResearch/Research(ore)`**,
  `AwardExploration`, `AddPoints`, `ResearchPoints`, event `OnChanged`.
- `ExportDiscovered/ExportResearched/Import` — save/load bridge.

### FacilityPower.cs  *(the two parallelism pools)*
A facility's LEVEL buys **parallelism**, not just speed.
- **`BuildPower.ForLevel(level)`** — `level + 1 + TechEffects.ShipyardPowerBonus` (Lv1 = 2, Lv3 = 4, Lv5 = 6).
  `ForBody(b)`, **`PlayerTotal()`** (every owned shipyard pools; memoized per frame), `PlayerYardCount()`.
- **`ResearchCapacity.*`** — the identical curve for research centres.
Each hull costs `UnitInfo.buildPower` while on the stocks; each tech costs `Tech.CapacityCost` while studied.

### Terraforming.cs  *(what's wrong with a world, and what fixes it)*
Two layers: **projects raise a world's CEILING**; terraformer ships then grind habitability toward it.
- `enum TerraformProblem` — TooHot/TooCold, NoWater/**TooMuchWater**, NoAtmosphere/AtmosphereTooThick,
  ToxicAtmosphere, NoBiosphere, NoMagnetosphere, Day too long/short, Orbit too close/far, NoSurface,
  UnstableAxis, LowGravity, **WrongWorldType**.
- **`TerraformDiagnosis.Analyze(body, species)`** — the species-relative fault list. This is why an ocean
  world reads 86% to the Aquarii and 24% to the Pyrothians. `SeverityOf`, `Describe`, `Pretty`.
- **`TerraformProjectDatabase.All / Get / For(body, species, techGated)`** — ~23 projects (haul water,
  melt caps, tap aquifers, comet bombardment, processors, scrubbers, forests, mirrors/shades, core
  cooling/ignition, spin up/down, orbit migration, moon capture/disassembly, hydrosphere venting,
  shellworlds, **planetary remodelling**). Each gates on a tech and fixes one problem.
- **`TerraformProjects.CeilingBonus / ReachableCeiling / PotentialCeiling`**, **`SpeedFactor()`**
  (empire level × Expansion tech — slow early, ~a minute late), cost/duration scale by size × severity.

### TerraformManager.cs
- **`CanStart/Start(body, type)`**, `SetPaused`, `Cancel` (full refund), `JobsFor`, `Export/Import`.
- `ApplyPhysicalEffect` — projects really change the world (water added/vented, spin, orbit, and
  **`Reshape`** converts the body TYPE and re-scores habitability).

### Visibility.cs  *(hiding, and WHY a thing is hidden)*
Concealment is a **game mechanic** with a dev tool on top, not a boolean.
- `enum HideReason` — `None · Dev · Cloaked · Undiscovered`. They render identically today, on purpose:
  the reason costs nothing to carry now, and a cloaking tech or a discovery event later becomes a flag
  change rather than a rewrite. What it must not become is one bool — "drop every cloak" and "un-hide
  what I hid in the editor" are then the same operation, and they are not.
- **Concealed is not absent.** A hidden body keeps orbiting, keeps being ticked, keeps its colony and
  its units. Only renderers, colliders and lights go — which is why this never uses `SetActive(false)`.
- **`ConcealBinding`** — the component that does it. **Records** what it disabled and re-enables exactly
  that, because half the galaxy's renderers are legitimately off for somebody else's reasons (a ring the
  player turned off, an airless world's missing atmosphere shell). Re-sweeps on `OnEnable` and on a
  repeat conceal, so a subtree that GREW while hidden (fog swap, atmosphere rebuild) stays hidden.
- **`VisibilityService`** — the one place concealment is decided, and the chokepoint that would become
  server-authoritative if this ever goes multiplayer. `Hide/Reveal` for a `CelestialBody`, a `StarData`
  (one sun, or a black hole), a `Unit` (the cloaking tech's target), `HideOrbitLine/RevealOrbitLine`,
  `HideSystem/RevealSystem`, `HideAll/RevealAll`, `ListHidden()`, `ApplyAll()`.
- `ReasonFor(...)` — the EFFECTIVE reason: a system-wide conceal outranks a body's own flag, because
  that is the one that has to be undone first for anything to reappear. Hiding a world conceals its
  orbit line too (`ReasonForOrbitLine`) — a ring drawn around nothing announces what is there.
- State lives on the DATA (`CelestialBody.hideReason/ringHideReason`, `StarData.hideReason`,
  `StarSystemData.hideReason`, `Unit.hideReason`), so it survives a re-visualize and a save/load. It has
  to be **pushed back** at freshly built visuals — `SystemVisualizer.VisualizeGalaxy` and both unit
  renderers' `Rebuild` do that.
- Three places had to learn about it because they draw things the visualizer doesn't:
  `OrbitController.SetRingConcealed` (the ring hangs off the system container, not the body),
  `GalaxyStarProxy.SetAlpha` and `GalaxyLOD.ApplyProxies` (galaxy zoom replaces a system with a proxy —
  without this you hide a system, zoom out, and there it is).
- Generation seeds a **rare undiscovered world** (`GalaxyGenerator.SeedUndiscoveredWorlds`, ~1.2% of
  unclaimed non-home planets), concealed as `Undiscovered` so a later discovery event can reveal
  exactly those and nothing a developer hid by hand.

### GenesisReveal.cs  *(the galaxy arrives)*
The whole galaxy is generated up front and almost all of it starts **concealed**; only the home system's
sun(s) are drawn. The homeworld joins them when the camera takes over, and everything else arrives at
the very end, a beat behind the orbit lines.
- **`Begin()`** — called from `GameManager.GenerateGalaxyRoutine` right after `Visualize()`, and
  **after `FactionAI.NewGame`**. That ordering is load-bearing: `FactionAI` won't plant a capital on a
  concealed world, so concealing first means it finds no candidate anywhere and the game generates with
  no rival civilisations at all.
- **`RevealHomeworld(body)`** — from `SnapCameraToHome`, the instant the camera is framed on it and
  while the panel still covers everything.
- **`Finish()`** — from the last beat of `LoadingScreen.Finale`, **and again** as a backstop once
  generation returns. Idempotent on purpose: a galaxy left invisible is the worst way this could fail,
  so its existence must not depend on a coroutine reaching its last line.
- Hidden as `HideReason.Sequence` and revealed by that reason alone (`VisibilityService.RevealAll(reason)`),
  which is why the fourth reason exists — a blanket reveal would hand the player the rare `Undiscovered`
  world on turn one and there would be nothing left to find.
- Concealed is not paused: every hidden system is orbiting, ticking and thinking from the moment
  generation finishes.
- **`Sequence` is never saved.** `GameStateSerializer.Persist/Restore` flatten it to "visible" in both
  directions. A save taken mid-sequence would otherwise restore every system as concealed, and nothing
  on a loaded game ever clears it — an invisible galaxy with the Dev panel as the only way out. The
  pause menu is also gated on `GameManager.IsGenerating` now, so that save can't be taken at all.
- The reveal backstop lives in `GenerateGalaxyRoutine`'s **`finally`**, not at the end of the body. The
  finale is driven by `while (fin.MoveNext())` on the same stack, so a throw inside it skips every
  later line — which would have left the galaxy invisible *and* latched `generating`, so New Game
  silently did nothing forever after.

### GalaxyTrash.cs  *(deletion, with a way back)*
Deleting genuinely removes: out of `galaxy.systems`, `sys.bodies`, `body.moons`. Nothing iterates it,
nothing ticks it, and the save no longer carries it.
- **`DeleteSystem / DeleteBody / DeleteStar`** (each `out string why`), **`Restore / Purge / PurgeAll`**,
  `OnGameReplaced` (a new galaxy or a loaded save empties the bin — restoring a system from a dead
  galaxy into a live one is not a thing).
- **A bin rather than a confirmation dialog.** The mistake you make with a tool like this is deleting
  the wrong row, not clicking the button by accident, and a modal on every delete protects against
  neither while making the tool unusable.
- `DeleteStar` refuses a system's LAST sun: `StarDatabase.Combine` on an empty list rolls a fresh
  G-type, so the system would quietly grow a new star. Delete the system instead.
- Fixes up the two things stored as POSITIONS rather than references — `Galaxy.homeIndex` and
  `Derelict.systemIndex` — on both delete and restore.
- Redraws via `GameManager.RebuildVisuals()`, the same full rebuild a save load does (which is why it
  is safe): everything under `systemParent` is destroyed and rebuilt from what is still in the galaxy,
  so a deleted object needs no destruction of its own.

### ControlGroups.cs
- **`Assign(g, units)` / `Members(g)` / `GroupOf(unit)`**, `Export/Import`.
- **`ControlGroupInput`** — `Ctrl+1..9` bind, `1..9` select + fly camera, `Shift+1..9` add.

---

## Visual (`Assets/Scripts/Visual/`)

### OrbitController.cs  *(the orbit fix + all orbit params)*
- **`Setup(...)` / `SetupFromData(parent, body)`** — configure orbit from data.
- `Update()` — advances the orbit (deg/sec) and axial spin.
- **`UpdatePosition()`** — positions the body on its (elliptical, inclined) orbit; keeps the ring
  glued to the parent WITHOUT inheriting parent scale (the old ring-mismatch bug).
- Live setters: **`SetRadius, SetSpeed, SetSpin, SetPhase, SetDirection, SetInclination,
  SetEccentricity, SetVerticalOffset, SetRingVisible, SetRingColor`**.
- **`SetHabitableHighlight(on)`** — green ring around a habitable body.
- `BuildRing, DrawEllipse, RedrawRing, RestoreParent, ForceRingRedraw`.

### SystemVisualizer.cs
- **`VisualizeSystem(bodies, star)`** — clears + spawns star (with light/emission/collider), planets
  and moons (data-driven orbits + `PlanetAppearance`), builds the habitable-zone visualizer, and
  registers `SystemContext`. Overload accepts `StarType`.

### HabitableZoneVisualizer.cs
- **`Build(star, starT, bodies)`** — build the green band for the current species' zone.
- **`Refresh()`** — rebuild after a species change.
- **`Toggle()` / `SetVisible(state)`** — show/hide band + green rings on in-zone bodies.

### StarInteraction.cs
`OnMouseDown` on the star → opens `StarInfoPanel`.

### PlanetGridVisualizer.cs  *(low-res "general" viewer)*
- **`ShowSurface(surface)`** — build the chunky tile grid, auto-fit tile size, resize the window.
- `BuildGrid, ResizeWindow, SelectTile, ClearGrid`. Colour comes from `SurfaceTileUI`/`TerrainColorMap`.

### SurfaceTextureRenderer.cs
- **`Build(body)`** — renders the detailed surface to a point-filtered `Texture2D` by sampling the
  same noise field densely; tints ore regions. Used by the detailed map AND the 3D globe.

### PlanetAppearance.cs
- **`Apply(body, go)`** — textures the sphere with its surface map, sets material params, adds a
  per-type **atmosphere** shell (airless bodies get none), and layers optional CC0 detail maps.

### PlanetTemperature.cs  *(°C, derived from heat — and the inverse)*
- **`BaseCelsius(heat, atmosphere, type)`** — `288.15·√heat − 273.15 + typeNudge + atmosphere·45`. The
  greenhouse term is the one that catches people out: it adds up to **45 °C** on top of what `heat`
  alone says, so `heat` and "how warm is this world" are not interchangeable.
- **`HeatForCelsius(targetC, atmosphere, type)`** — the algebraic inverse. Anything choosing a world's
  climate by TEMPERATURE must solve through this rather than assigning `heat` directly.
  `GalaxyGenerator.CradleHeat` is why it exists: the home world's heat was set from the species'
  `idealTemp` by a blind lerp, the greenhouse term pushed the result past the 50 °C liquid-water
  ceiling, and `BiosphereRules` then correctly refused — so two Terran homeworlds in three, and every
  Sylvan one, generated sterile. Bigger cradles now get a lower heat to offset their thicker air.

### SpaceBackground.cs  *(procedural sky)*
- **`Create()`**, **`Rebuild()`**, **`Regenerate()`**, `SetEnabled/SetSolidMode/SetSolidColor/SetSeed`.
- `LateUpdate` — parallax drift + shooting-star timer.
- Texture gen: `GenerateNebula, GenerateStars, GenerateGalaxy, GenerateDot`.
- **`StarTint`** draws from `StarDatabase.ColorFromTemperature` — the same blackbody ramp every sun in
  the game is coloured from — rather than from three hardcoded tints, so the sky is made of the same
  stars the galaxy is. Weighted for a NAKED-EYE sky rather than a census: by population the galaxy is
  ~76% M-class red dwarfs, but almost none are visible, which is why a real night sky reads blue-white
  with a scattering of orange. Brightness comes back with the colour (`lumBias`) because rolling the
  two independently is what makes a procedural star field look like static.
- Helper classes **`TwinkleStar`** (brightness pulse) and **`ShootingStar`** (streak + fade).

### PostFxController.cs
- **`Create()`** + `Start()` — enables URP **Bloom, Vignette, ACES tonemapping, color grading** in
  code (no downloads). Guarded so it degrades gracefully.

### AssetIntegration.cs
- **`ApplyPlanetDetail(material, type)`** — auto-applies optional CC0 detail/normal maps from
  `Resources/SpaceAssets/Detail/`.
- **`LoadSkybox()`** — optional `Resources/SpaceAssets/Skybox` material. See `SPACE_ASSETS_SETUP.md`.

---

## UI (`Assets/Scripts/UI/`)

### PlanetUI.cs  *(scene-wired hub)*
- **`Show(body)`** — info panel + grid window; broadcasts `OnBodySelected`; builds the body readout
  (orbit, spin, habitability with gradient colour, resources, ores, POIs).
- Static: **`Selected`, `OnBodySelected`, `OnClosed`**.
- **`ShowTerrainHover(tile)` / `HideTerrainHover()`** — terrain tooltip above the viewer.
- `ShowTileInfo(tile)` — terrain + ore + research status. `CloseAll/CloseGridOnly`, `RefreshInfo`.

### SurfaceTileUI.cs
- **`Init(tile)`** — colour (TerrainColorMap × per-tile shade) + ore marker.
- `OnClick` (discovers ore + selects), `SetHighlight`, `OnPointerEnter/Exit` (hover tooltip).

### UITheme.cs
Shared palette + font sizes for all runtime UI.

### UIFactory.cs  *(builds all UI in code)*
`CreateCanvas, EnsureEventSystem, Panel, Window` (draggable shell), `VerticalLayout, Text, Label,
Button, Toggle, LabeledSlider, Slider, InputField, ScrollView`, plus `Stretch/AddLayout` helpers.
> **Buttons sit at their normal colour and only light up while pressed.** Hover and post-click
> selection deliberately do not tint, and navigation is off. Windows rebuild as the economy ticks, and
> a hover/selected tint restarts its fade on every rebuild — which read as a blue strobe.

### QueueDragHandle.cs
The "≡" grip on a queue widget. Drag it to reorder the build/research queue; dragging the row body
still scrolls the list. Reordering moves the MODEL then re-sorts existing rows by sibling index — it
never destroys them, because destroying the row under the cursor would cancel the drag.

### DraggableWindow.cs
Drag a window by its title bar; brings it to front on click.

### TooltipManager.cs
- **`Instance`** (self-creating). **`ShowAboveRect(target, text)`**, `ShowAtCursor`, `Hide`.

### OrbitControlPanel.cs  *(real-time orbit editor)*
- **`ShowFor(body)`** — follows the selection; loads slider values.
- Live edits: size, radius (updates habitability for planets), speed, phase, inclination,
  eccentricity, vertical offset, spin, direction, ring visibility.
- **`RecomputeRealistic()`** — reset speed to the Kepler default. `Toggle/Hide`.

### StarInfoPanel.cs
- **`Show(star)`** — star attributes + a **Show Habitable Zone** toggle (disabled if the star has none).

### PlanetViewWindow.cs  *(HUD "Surface" — the world you develop)*
Seven tabs over one shared surface grid:
- **Info** — name, type, size class, climate/weather prose, development density, survey state.
- **Sites** — the points of interest on this world (what the retired detailed-map window used to hang).
- **Build** — click a structure to pick it up; it rides the mouse as a loose ghost, then SNAPS to the
  grid over the map (green = fits, red = doesn't). **Right-click rotates** at any point; left-click
  commits; Esc drops it. Footprints are tetromino-like, so packing a dense city is a real puzzle.
  Grouped by category — Government · Harvesting · Industry · Military · **Electrical Engineering**.
- **Infrastructure** — everything standing here: tier, condition, siting, and its grid.
- **Power** — the grid overlay (yellow = reach, blue = plants and relays, dull = dark) plus a balance per
  grid and a list of what's in the dark. Diagnostic only; the plants are placed from **Build**, since a
  second placement surface would mean a second copy of the ghost/confirm/rotation handling.
- **Survey** — the index overlays, each with its own colour ramp, plus a live per-tile readout.
- **Terrain** — the sandbox terrain editor. The one tab Dev Mode *gates* rather than opens.

`TabAvailable(tab, out why)` is the single gate, and it answers **in words** — a greyed tab that won't
say what's missing is a dead end. Milestones: visited → surveyed → deep-surveyed → claimed → settled.

Drawing: overlays are ONE point-filtered texture the size of the grid (a texel per cell), not a
GameObject per tile — a 40×20 world is 800 cells. Structures and the ghost are a few quads on top.
Hover is POLLED (`ScreenToCell`), not `IPointerMoveHandler`, whose dispatch depends on the input module.

### SurfaceIndex.cs
Per-tile survey overlays, **derived not stored** — a stable hash of the terrain seed + position means
they cost nothing to save and survive a reload untouched (the same guarantee terrain already makes).
- `SurfaceIndexKind` — Mineral · Heat · Fertile · Weather. `Get / Ramp / Name / Describe`.
- **`Unlocked(b, kind)`** — Mineral needs a survey (you see seams from orbit); Heat/Fertile/Weather
  need `body.deepSurveyed`, set when a research ship studies the world. That's the reason to go back.
- Ramps: brown (mineral), orange→red (heat), dark→vibrant green (fertile), blue→white (weather).

### SurfaceBuilding.cs + SurfaceBuildManager.cs
- `SurfaceBuildingType` / `SurfaceBuildingInfo` — footprint `shape`, driving `index`, cost, output.
  Mine = L, Farm = 2×3, Geothermal = O, Solar = I, Wind = T, Habitat = S, Factory/Spaceport = blocks.
- **`Rotated / CellsOf / Footprint`** — 90° rotation, re-normalized so the piece stays under the cursor.
- **`CanPlace / Place / Demolish`** (60% refund), `Occupied`, `Density`.
- **`EfficiencyAt`** — the average of the driving index across the footprint, **locked in at placement**.
  A mine on a seam pays forever; one on dead rock is a permanent mistake. `TickOutput` scales by it.
- `requiredTech` — gates a structure on research (`TerraformProjectInfo.requiredTech`'s twin). Checked in
  `CanPlace`; Fission needs F1, Fusion needs F2.
- `allowMultiple` — opts out of `OneOfEachPerWorld`. Power infrastructure only: a grid you may build one
  relay for is not a grid. `uniquePerWorld` still wins over it.

### PowerGrid.cs
**Electricity as a place rather than a number.** A grid is a **connected component of projectors**, where
a projector is anything with `powerRange > 0` (a plant lights its footprint + the 8-ring; a Power Node is
a 1×1 relay reaching 7; the capitol's founding reactor reaches `ColonyReactorRange`). Two projectors are
one grid **iff the ground they light overlaps** — that single rule is the whole system.
- **Derived, never maintained.** No merge code, no split code: `Compute` union-finds every grid on a world
  from the buildings standing on it, memoized per (world, frame). Chain nodes between two cities and they
  *were never two grids*; lose the middle node and they are two again. Every mutation calls `Invalidate()`.
- `PowerFactor(b, p)` — `1` if it draws nothing, `UnpoweredFactor` (0.35) if no grid reaches it, else
  `Lerp(0.35, 1, served)`. `TickOutput` ticks the grid **first**, then scales every output by this.
- `served` vs `SteadyServed` — `Tick` runs on ColonyManager's ~1s step, the UI reads every frame, so
  `served` is **derived from state** rather than left as a tick artifact. Otherwise 59 frames in 60 would
  show a number computed without the capacitors in it.
- `Dead` — a grid with no plant on it. Relays with nothing behind them; distinct from having no grid.
- Capacitor charge lives on `PlacedBuilding.stored`, so merging grids needs no reconciliation.
- **Consumers are industry only** (mine/factory/refinery/lab/spaceport/shipyard). Housing and farms draw
  nothing, deliberately — see the comment in `SurfaceBuilding.cs`; hanging power on population would
  strangle every pre-existing colony and feed back on the growth that would fix it.

### InspectorWindow.cs + InspectorBodyTabs / InspectorUnitTabs / InspectorFacilityTabs / InspectorStarTabs
**One tabbed window for everything you can click on.** `partial class InspectorWindow`.
- `InspectorTarget` — a tagged union: Body · Unit · Fleet · City · Shipyard · ResearchCenter · Structure · Star.
- **`Inspect(target)` / `Drill(target)` / `Back()`** — drilling into a planet's shipyard re-targets the
  window and leaves a breadcrumb, so a facility is a subject in its own right, not a sub-panel.
- Tabs — Planet: Overview · Climate · Ores · Society · Production · Objects · Terraform.
  Ship: Overview · Orders · Stats · Effects. Fleet: Ships · Orders. Star: Overview · Zone · Worlds.
  Shipyard: Overview · Build · Stocks. Research Centre: Overview · Research · Projects.
- Rebuilds ONLY when `Signature()` changes (subject, tab, structural facts); every value refreshes in
  place through a `LiveSet`. Values are deliberately absent from the signature — that's the strobe fix.
- Shared helpers: `Card / Header / Stat / Note / DrillRow / Bar / UnitRow`.

### UILive.cs
`LiveSet` — the "refresh in place" half of the rebuild rule. `Text / Button / Bar / Custom` bindings,
`Tick()` once per frame. A `Button` binding dims and re-captions itself when it can't be used.

### ResearchWindow.cs  *(Ore Codex, tech tree + research queue)*
- **`Toggle()`**, `Signature()`/`Rebuild()`, `BuildResearchQueuePanel`, `BuildTechTree`, `BuildCard(ore)`.
- The **research queue** is the laboratory twin of the shipyard stocks: capacity readout, per-project
  drag-reorder / pause / abandon (refunds sunk RP), progress bars. Several technologies study at once.
- Rebuilds only on a **signature** change (researched set, empire level, capacity, discovered ores).
  Point-dependent captions refresh in place via `dynamics` — this is what stops the button strobe.

### ShipyardWindow.cs  *(catalogue + live stocks)*
- Top: stockpile, pooled **build power** (free/total) and the selected yard's own contribution.
- **Catalogue** — icon, name, resources, build power, time. Bright + clickable when affordable, dimmed
  and inert when not, with the reason on the button. Clicking queues one; click again to queue another.
- **On the stocks** — every ship building or queued, each with a progress bar, a drag grip, pause
  (hands its power to the next ship) and cancel (full refund).
- Same rebuild discipline as ResearchWindow: signature-gated structure, per-frame values in place.

### ObjectVisibilityWindow.cs  *(HUD "Objects" — every object in the galaxy)*
The tool that makes `Visibility.cs` and `GalaxyTrash.cs` usable. A tree grouped by system: the system,
its suns, its planets, their moons, and each world's **orbit line as its own row** — every row carrying
the same hide switch and delete button, because the requirement was universal rather than a list of
special cases.
- A **system row acts on everything under it**, which is what makes "delete an entire solar system" one
  click. Collapsed by default (a twelve-system galaxy is several hundred rows); the filter box expands
  whatever matches.
- **HIDE AS** picks which reason the hide buttons write — `Dev` by default, with `Cloaked` and
  `Undiscovered` selectable so the other two states can actually be looked at.
- **Bin** shows what was deleted, newest first, with Restore and Delete forever.
- Controls that are deliberately unavailable **say why** (a single sun can't be deleted on its own; an
  orbit line has nothing to delete because it is drawn from the body's orbit) — same rule as
  `PlanetViewWindow.TabAvailable`.
- Dev Mode only, and unlike the terrain sandbox it **closes itself** when Dev Mode goes off rather than
  greying out: it deletes things, and a delete button reachable in normal play is a way to lose a save.

### SaveLoadMenu.cs
- **`Toggle()`**, `DoSave` (named), `RefreshList`, `BuildRow`, `DoLoad`, `DoDelete`. Multiple saves supported.

### SpeciesWindow.cs
- **`Toggle()`**, `Refresh`, `BuildCard(species)` — full dossier (biology/habitat/strengths/
  weaknesses + attribute bars) and a "View worlds as …" selector. `AttrBar` draws stat bars.

### BackgroundSettingsWindow.cs
- **`Toggle()`** — show/hide, solid-colour mode, **Regenerate**, and RGB sliders + swatch.

### DetailedSurfaceWindow.cs  *(expanded map)*
- **`Open(body)`** — renders the high-detail texture + POI markers; legend.
- Helper `MapHoverProbe` (terrain-under-cursor tooltip) and **`POIMarker`** (hover details; click a
  mystery to explore → awards research + may discover an ore).

### GameHUD.cs  *(top command bar)*
- **`Build(canvas)`** — buttons for New System, Save/Load, Species, Ore Codex, Background, Zone,
  Orbit, Detailed Map, and time controls (❚❚ / 1× / 2× / 5×) + a species/research/speed status readout.
- `SetSpeed` keeps `TimeController` in sync; context buttons enable on selection.

### GameBootstrap.cs  *(auto-wiring)*
- `[RuntimeInitializeOnLoadMethod]` **`Init()`** — spawns the tooltip, canvas, all windows, the
  background, post-FX and the HUD at play time (no Inspector setup).
- `OnSystemChanged` — gives each system its own sky and re-syncs habitability. `DeriveSeed` for the sky.

---

## Save / Load (`Assets/Scripts/SaveLoad/`)

### SaveData.cs
Serializable DTOs for `JsonUtility`: `SaveGame` (star, species, timeScale, background settings,
bodies, research), `BodyDTO`, `ResourceDTO`, `OreCellDTO`, `POIDTO`, `ResearchDTO`. Terrain is NOT
stored tile-by-tile — it regenerates from `terrainSeed`; only ores + POIs are stored.

### SaveSystem.cs
- **`Save(game)`, `Load(name)`, `Delete(name)`, `Exists(name)`, `ListSaves()`** — JSON files under
  `persistentDataPath/Saves`. `Sanitize` cleans file names.

### GameStateSerializer.cs
- **`Capture(name)`** — live game → `SaveGame` (bodies, ores, POIs, research, species, background).
- **`Apply(game)`** — `SaveGame` → live game: rebuild bodies (regen terrain from seed, re-apply
  ores/POIs), restore research, load the system, restore species + background + time scale.

---

## Scripts root (`Assets/Scripts/`)

- **TerrainColorMap.cs** — `Get(type)` single source of truth for terrain colours (fixed the magenta
  bug); `Describe(type)` for tooltips.
- **TerrainTile.cs** — one surface cell: `type, occupied, ore, oreRichness, shade`, `HasOre`.
- **ResourceDeposit.cs** — `Add/Get` over a `ResourceType→float` dictionary.
- **PlanetSurface.cs** — `width, height, tiles[,]` grid container.
- **PlanetClick.cs** — `OnMouseDown` → `PlanetUI.Show(data)`.
- **ShipOrbit.cs / ShipTransfer.cs** — legacy ship motion stubs.
- **SystemTester.cs** — press **R** to regenerate the system (debug).
- **MouseRaycastDebugger.cs** — logs what the mouse ray hits (debug).
- **SandboxEditorPanel.cs** — legacy scene panel; now edits size/radius/speed **live** (real-time
  sliders) as well as on Apply.
- **TerrainEditorPanel.cs** — legacy scene panel; regenerates terrain from slider params and
  re-seeds ores.

---

## Debugging aids (kept intentionally)
`SystemTester` (R = regen), `MouseRaycastDebugger`, and the many `Debug.Log` calls in the generators,
visualizers and save system are deliberately retained for tracing. The HUD status line and
`PostFxController`/`SpaceBackground` also log their state on start.
