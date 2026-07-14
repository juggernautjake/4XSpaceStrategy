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

### CameraController.cs  *(unchanged legacy)*
WASD pan + mouse-wheel height, fixed pitch, ignores input over UI.

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

### ResearchWindow.cs  *(Ore Codex)*
- **`Toggle()`**, `Refresh`, `BuildCard(ore)` — undiscovered "???", discovered → Research button,
  researched → uses + refining. Header shows research points.

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
