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

### SpaceBackground.cs  *(procedural sky)*
- **`Create()`**, **`Rebuild()`**, **`Regenerate()`**, `SetEnabled/SetSolidMode/SetSolidColor/SetSeed`.
- `LateUpdate` — parallax drift + shooting-star timer.
- Texture gen: `GenerateNebula, GenerateStars, GenerateGalaxy, GenerateDot`.
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
Three tabs over one shared surface grid:
- **Info** — name, type, size class, climate/weather prose, development density, survey state.
- **Build** — click a structure to pick it up; it rides the mouse as a loose ghost, then SNAPS to the
  grid over the map (green = fits, red = doesn't). **Right-click rotates** at any point; left-click
  commits; Esc drops it. Footprints are tetromino-like, so packing a dense city is a real puzzle.
- **Survey** — the index overlays, each with its own colour ramp, plus a live per-tile readout.

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
