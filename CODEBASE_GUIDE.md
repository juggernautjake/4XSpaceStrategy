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
- Fields: `id, name, type, resources, **mass**, surfaceSize, surface`, `moons`, terrain identity
  (`terrainSeed, continentFrequency`), `pointsOfInterest`, orbit params
  (`orbitRadius, orbitSpeed, orbitPhase, orbitDirection, inclination, eccentricity, verticalOffset,
  spinSpeed, rotationDirection, beltId, showRing`), habitability
  (`distanceFromStar, habitability, isHabitable`), and non-serialized `visualObject`, `parentBody`.
- **`mass` is in EARTHS** (default 1) and is the thing everything else derives from: grid size, visual
  size, atmosphere ceiling, tectonics odds, size class.
- **`spinSpeed` is a RATE and is always positive**; `rotationDirection` carries prograde/retrograde
  separately, because the dynamo that gives a world its magnetic field cares how fast the core is
  stirred and not which way. A signed spin would have made "turns backwards" and "has no magnetosphere"
  the same fact.
- **`beltId`** — which asteroid belt this body shares an orbital lane with, 0 for none. See `OrbitSafety`.
- `CelestialBody(type)` — ctor, sets defaults.

### TerrainTile.cs  *(note: lives in Scripts root — see below)*
One surface cell. Beyond `type / occupied / ore / oreRichness / shade` it now carries:
- **`ground`** — the biome UNDER any water, sea ice, snow or glacier. Those are COVERS, not biomes:
  elevation is decided by geology alone and no terraforming moves it, so draining a sea really would
  uncover exactly this. `HasCover` is `ground != type`, and the tile readout says "Ocean over Steppe".
- **`elevation`** — the ground's own height, 0..1. Stored rather than re-derived because the hover
  readout wants it every frame and re-deriving means re-running the whole noise field for one cell.

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
  habitable zone; O/B stars get no zone.
- **`AU = 40`** maps astronomical units → game units, and it is the number the whole layout hangs off.
  It was 14, which made a Sun-like star's rendered radius 0.21 AU against the real Sun's 0.005 — so
  `OrbitSafety`'s clearance swallowed the entire inner system and no planet could physically orbit
  inside the habitable zone. That is the "green habitable zone is far too close to the star" report,
  and its root cause was the ruler rather than the zone. See the header in the file.
- Star render size is `baseScale * 4` (was `* 2`), and `RefScale` doubled with it so every density
  readout in the Dev editor keeps meaning what it meant.

### Habitability.cs  *(species-aware)*
- **`GetZone(star, species, out inner, out outer)`** — ONE RULE for every species and star (2026-10-11):
  the orbits where a world of the species' OWN kind (its cradle's type and air — `CradleAtmospheres` —
  plus a volcanic homeworld's venting, `PlanetTemperature.HomeworldVentingC`) sits within
  `ZoneTolerance` × Rate's temperature width of `IdealCelsius`. Found by inverting
  `InsolationRules.KelvinAt` (`ZoneRel`), so it is the same interval in multiples of R around every star:
  Pyrothians ~0.53-0.99R (ring 3), Sylvans 0.85-1.21, Terrans 0.89-1.10, Aquarii 0.90-1.15 (ring 4),
  Cryithn 0.91-1.55 (ring 5). It used to be the star's band shifted ±25% by idealTemp — positional, no
  temperature in it, so a Terran band reached ring 5 (−20 °C) and no Pyrothian band held 360 °C.
  `tools/habitability-audit.mjs` tabulates every species × star class, before and after.
- **`CradleZone`** — where a cradle / second starting option may orbit: `GetZone`, intersected with the
  star's band for liquid-water life. **`InCradleZone`** is what `WorldConsistency` checks options against.
- **`InZone(star, species, distance)`** — is a body physically inside that band.
- **`Rate(star, species, body)`** — 0..100 from the world AS IT IS (2026-10-04): average °C against the
  species' `IdealCelsius` (smooth falloff, width from tolerance), air (`AtmosphereSuitability`), liquid
  water (if `NeedsWater`), a biosphere (if `NeedsBiosphere`), gravity, and body-type affinity as a modest
  weight. It used to be orbital position × affinity, which let a 500 °C airless world outrate a living one.
  The `(star, species, type, distance)` overload is the old positional what-if, kept for `IsHabitable`.
- **`PlacementZone`** — overlap of the species' band and the star's physical band, for placing homeworlds
  and the guaranteed habitable world so they always sit inside the green ring.
- **The drawn green band is the STAR'S liquid-water band** (`star.hzInner/hzOuter`, `StarDatabase.ApplyZone`),
  not a species' band. Multi-star systems take it from their HOTTEST sun (`StarDatabase.Recombine`), and
  star physics are saved (`StarPhysicsDTO`) so a reload no longer re-rolls the star and moves the zone.
- `Label(rating, inZone)` — "Habitable/Marginal/Hostile/Uninhabitable".
- **`ScoreColor(rating)` / `ScoreColorHex(rating)`** — red→yellow→green gradient for the score number.

### Species.cs
- `class Species` — name, signature, description, **biology/habitat/strengths/weaknesses**,
  attributes (`iq, longevity, fertility, durability, adaptability`), `idealTemp`, `tolerance`,
  per-body-type `Affinity`. `AttributeLine()` formats the stats.
- **`SpeciesDatabase.All` / `Get(index)`** — the 5 species (Terrans, Aquarii, Pyrothians, Cryithn, Sylvans).
- **`aquatic`** (2026-10-11, Aquarii only) — the species builds on water: `SurfaceBuildManager.WaterAllowed` lets
  every class flagged `SurfaceBuildingInfo.aquaticWater` (capitol, housing and grown settlements, depots, labs,
  factory/refinery, spaceport/shipyard, grid relays/banks/reactors, steam turbine, geothermal, farm) stand on water
  tiles; `SurfaceIndex` reads open water as Fertile for them (kelp beds, `KelpFertile`). Mines, combustion,
  solar, wind and hydro still need land, and every other species still needs dry ground. The builder is always
  `SpeciesManager.Current` (only the player places structures); `SpeciesManager.Select` invalidates the index
  caches. Their cradle is a true ocean world (`GalaxyGenerator.SettleOceanCradle`: water level 0.86-0.93, the
  baked map 85%+ sea with a few percent of islands for the mines), named "ocean world" by `WorldClassifier`
  (the Ocean class is "ocean world" from 85%, its physics line) and explained by `WorldExplanation.AquaticNote`.
  Placement says so: card note, cursor/confirm "Underwater · Aquarii can build on water", and a sea-blue wash
  on art standing on water (`PlanetViewWindow.AddBuildingArt`).
- **`SpeciesManager`** — `Current`, `CurrentIndex`, event `OnSpeciesChanged`.
  - **`Select(index)`** — switch species, recompute the world, fire the event.
  - **`RecomputeWorld()`** — re-score every body's habitability for the current species + refresh the zone.

---

## Generation (`Assets/Scripts/Generation/`)

### MassRules.cs  *(the scale everything else is measured against)*
**1 Mass is 1 Earth.** Terrestrial worlds 0.6–4 to one decimal, gas giants 10–40 in multiples of five,
anything at or under 0.5 not orbiting a planet is an asteroid. `SurfaceSize = mass × 6` (so an Earth is
6, the same size class every downstream threshold was tuned against back when Earth was mass 2).
- **`SystemBudget(stars)`** — the SOLAR SYSTEM MASS allowance: **70 per solar mass**, clamped 32–280. This
  replaced the New Game menu's "average planets per system" slider entirely. A system is built by
  SPENDING it outward — planets, their moons and every asteroid out of one pot — and stops when it can
  no longer afford anything. A binary really is richer than a red dwarf, by arithmetic rather than by
  a setting.
- **`RollGasGiant / RollTerrestrial / RollAsteroid / RollMoon`**, `MoonBudget(hostMass)` (**a quarter**
  of a terrestrial's mass, a tenth of a giant's), and the `Quantize*` family.
- **`GasGiantDiameterScale`** — a giant renders at twice what the cube root alone gives it. The cube
  root is physically right and puts a mass-30 giant at only 3.1 Earth diameters, where Jupiter is
  eleven; the flat factor brings the class back to reading as a giant beside the rocky worlds.
- **`VisualDiameter`** is a CUBE root now, not a square root: mass is a volume, and the square root put a
  40-mass giant far enough across that OrbitSafety pushed every outer orbit off the screen.
- Measured distributions and per-star-class system census are recorded in the file.

### PlacementRings.cs  *(where a system is allowed to put things)*
Nine fixed radii per star, quoted as multiples of `StarDatabase.ReferenceDistance`, and the generator's
only decision per ring is fill it or skip it. This replaced an outward WALK, which had two failures the
ladder does not: a five-body system piled up against the star (a walk has no concept of a gap), and
whether any lane fell in the habitable zone was luck. The ladder is anchored so **rings 1–3 sit inside
the zone, 4 and 5 are in it, 6–9 are beyond** — Mercury and Venus inside, Earth and Mars within, which
is the arrangement the request asked for. Around a dim star the inner three are COMPRESSED into the
room between the star and the zone rather than the whole ladder being shifted, because shifting pushed
ring 3 into the zone and put the Earthlike world third from its sun.
- **`RingForHeat(star, radii, inner, outer, heat)`** (2026-10-10) — the ring in a range whose starlight
  band holds a wanted heat. It is how the species' cradle is placed: ring 4, the Earth ring, for a Terran,
  where its ideal climate is simply what its sun gives it (it used to go on ring 5 and be painted 45 °C
  warmer than its orbit).
- **`Radii(star, into)` / `InZone` / `HabitableRing`**. Generation-time only — nothing at runtime snaps
  to a ring, so the Dev orbit slider and terraforming's orbit-moving are unaffected.

### GasGiantStorms.cs  *(the Great Red Spot)*
0–3 storm cells per giant, deterministic from the body's seed, snapped to the centre of a `Storm` belt
(only belts actually on the disc, offset by the band-edge jitter's mean so the spot is centred in the
belt as drawn). **A storm is a swelling of its belt, not a shape cut into it** (2026-10-04, replacing a
pointed lens in a pale collar that read as a football with a clean slice round it). `Swell(spot, u, v)`
returns how far to move the latitude the band test reads: inside the oval the belt's half-height is
stretched to the storm's, and the first `FlowReach` × swell of ground outside is eased back so the pale
zones and next belts bow around it. The storm's edge is the belt's own edge, so it is as ragged as every
band edge (`MoistJitter`), and the belt flows into it through rounded shoulders. Great-Red-Spot
proportions (aspect 1.3–1.7), a slight lean (`SkewMax`), radius clamped so the oval never crosses the
equator or a pole.
- **`Spots(b, out spots)` / `Swell` / `BeltHalfHeight` / `Invalidate`**. `tools/gas-giant-check.mjs` draws
  twelve and asserts every storm's centre is storm and every storm out-swells its belt.

### GenProfiler.cs  *(which part of generation ate the frame)*
`Watch(label, iterator)` times every unyielded span in a generation coroutine and logs any over 50 ms;
`Section(label, work)` brackets straight-line work. Added for a reported 84-second single frame that
the loading screen could report the total of but not attribute.

### RotationRules.cs  *(rotation, and the magnetic field it drives)*
A magnetic field is a CONSEQUENCE of spin now, not a coin flip: **`GeneratesField(type, mass, spin)`**
gates on `MagneticFieldSpin` (12°/s). `Roll` draws from two populations — spun-up and tidally braked —
weighted by mass, so bigger worlds still usually have fields but you can see why. `RotationPeriodDays`,
`Describe`. **Spin direction FOLLOWS the orbit** ("prograde orbit, prograde rotation"): the generator sets
`rotationDirection = orbitDirection` once the orbit is rolled, for planets, belt rocks, moons (whose
"prograde" is their host's spin), the guaranteed world and the cradle. `RollDirection` survives only as
the pipeline's placeholder before an orbit exists. And `OrbitController` now NEGATES the spin it applies:
the orbit advances counter-clockwise from above, but a positive `Rotate` about +Y in left-handed Unity is
clockwise, so every world used to turn against its own orbit.

### InsolationRules.cs  *(how hot a world is because of where it orbits)*
The distance→heat law, **anchored on the habitable zone**. Inside the zone (`StarDatabase.HzInnerRel`
0.80 .. `HzOuterRel` 1.55 — one pair of constants now, shared by the green rings, `WorldClassifier`'s
bands, `AtmosphereRules`' inner-orbit cut and this) it is the plain T ∝ d^-½ law, unchanged. Inside the
inner edge it is a **runaway greenhouse**: the gradient times `RunawayGain` with a floor of `InnerFloorC`
(120 °C, over every boiling point once the world's greenhouse is added), so ring 3 sits at 120 °C, ring 2
near 240, ring 1 near 440. Beyond the outer edge it is a **snowball**: `SnowballGain` and a ceiling of
`OuterCeilingC` (−66 °C, so the warmest tile under the thickest air is still frozen). `BiasHeat`'s old
0.45..1.85 clamp — the reason a world a ring inside the zone had a 55 °C archipelago — is gone; heat
now runs ~0.08..6 and `PlanetTemperature.HeatForKelvin` is the inverse it is stored through.
- **A black hole gives no starlight** (2026-10-11): `RollHeat` returns `BlackHoleKelvin` (40 K, accretion glow) ±jitter.
- **`KelvinAt(rel)` / `KelvinAt(star, distance)`** (pure) and **`RollHeat(star, distance)`** (±5% jitter,
  guarantee re-imposed).
- **`HeatBand(rel)` / `ClampToOrbit` / `OrbitMismatch` / `RelOf`** (2026-10-10, pure) — the range of heat
  `RollHeat` can give at an orbit. Every heat a world is generated with must be inside it: a world chosen
  FOR its climate (the cradle, a second starting option) has its climate solved for and then clamped into
  it, and `WorldConsistency` checks every world against it. `WorldClassifier.AmplifyBiome(b, rel)` keeps
  its desert/tundra heat nudges inside it too (the desert lift was a flat 36 °C even on the cool edge). `TerraformVisuals.Compose` caps heat at `max(2.2, natural)` so a furnace world
  is not cooled by three hundred degrees the moment it is surveyed.

### GeothermalMap.cs  *(the merged Heat Index + Tectonics field)*
One 0..1 field per surface point, from two sources, whichever is stronger:
- **FAULTS** — a plate margin reads 40%, up to 100% head-on, radiating up to 3 tiles and never under
  70% inside that band (all four figures measured and recorded in the file).
- **HOTSPOTS** — focused mantle plumes: a small 90%+ core, an 80%+ ring, a 70%+ skirt, and a vent at
  95%+ IS a volcano. Present with or without plates, so a plate-less world can be covered in volcanoes.
- **PLUMES** (2026-10-09) — every world with hotspot activity also gets 4–8 seeded round plumes sized to
  its grid, capped at 92% except a rolled VENT core (97%) at the centre of the larger ones. On a frozen
  world a vent reading `CryoVolcanoIndex` (93%) becomes `TerrainType.CryoVolcano` instead of a volcano.
  The index is described to the player as subsurface PRESSURE, not ambient heat.
- **`At / HotspotAt / FaultActivity / WorldIntensity / Active / Label`**, `Invalidate`.
- Read by FOUR systems, which is the point: the survey overlay, the terrain generator's elevation, the
  earthquakes, and `PlanetTemperature`'s internal-heat term. They cannot disagree.

### PlanetTerrainGenerator.cs  *(resolution-independent, deterministic)*
- `struct NoiseParams` / `struct Sample` (now carries `ground` — what is under any water/ice/snow).
- **`GenerateSurface(body)`** / **`BuildStepped(...)`** — the tile grid; stepped version time-slices.
- **`SampleNormalized(body, u, v, params, octaves)`** — THE shared sampler.
- **ELEVATION IS GEOLOGY-FIRST.** A world starts FLAT. Its plates draw its continents (continental
  crust rides high, oceanic sits low — this is the Voronoi continent generation); convergent margins
  fold mountains up and rifts drop troughs; volcanic hotspots pile domes; and only THEN a small noise
  pass adds variation. The Elevation slider scales deviation from sea level, so it accentuates what is
  there and cannot invent a peak where the ground was flat.
- **`RidgeFromRelief`** — "how broken is this ground" is DERIVED from how the geology raised it, not a
  separate noise field. That field is what used to put mountains in the middle of plains and on the sea
  floor. Computed from the PRE-slider height, so turning Elevation up makes peaks taller without
  manufacturing new ones.
- `ElevationBand / ElevationMetres` — the words Hills/Highlands used to be, as a separate fact.
- `IsWater(type)`, `IsCover(type)` (sea/ice/snow are modifiers over real ground), `FBm`, `WorldNoise`,
  and per-type biome classifiers (`Terran, OceanWorld, Ice, Volcanic, Barren, Airless, GasGiant`).
- `ClimateCoherence(t, tileC, freezeC, boilC)` — the liquid-water window is the WORLD'S OWN and depends
  on its pressure (1 atm → 1–100 °C, 4 atm → 0–144 °C); above boiling a sea leaves salt flats, at
  650 °C+ the ground itself is magma.

### SolarSystemGenerator.cs
- **`GenerateSystemStepped()`** — rolls a star cluster, takes its Solar System Mass allowance, and lays
  out LANES outward until the allowance runs out (or `MaxLanes`). Each lane is a gas giant, a
  terrestrial world or an ASTEROID BELT.
- `ChooseLane(rel, budget)` — the frost line, made mechanical: past `WorldClassifier.FrostLineRel` a
  lane is usually a giant; inside it a giant is a 5% surprise (hot Jupiters). `TerrestrialBandMax(rel)`
  keeps inner worlds small.
- **Belts share one orbit**: same radius AND same angular speed, different phase, so the rocks hold
  formation forever and can never converge. `CelestialBody.beltId` is how `OrbitSafety` knows a shared
  lane is deliberate.
- `ApplyWorldPipeline` — mass → rotation → magnetic field → tectonics → seed → climate → water → air →
  CLASSIFY. `SeedTerrain(body)`, `ApplyHabitability(body)`, `RollStarType`.

### WorldConsistency.cs  *(does every world make sense where it orbits? 2026-10-10)*
The Mirfak report: a 35 °C grassland just outside a molten world, inside its G sun's runaway zone. Cause:
the home system was generated around whatever star `RollStarSystem` rolled (a red dwarf 45% of the time)
and `ForceHomeWorld` then swapped the species' G/K sun in underneath it, re-deriving nothing — that world
was the red dwarf's habitable-ring world. Fixed at the source (`SolarSystemGenerator.forcedStars`: the
home system is built around its real sun), and this class is the backstop, run once at the end of
`GalaxyGenerator.Finish`:
- **`Problem(b, star, species, out exception)`** (pure) — heat outside its orbit's `HeatBand` (beyond
  `HeatSlack`, 3%, for OrbitSafety's nudges), or a temperate type / living biosphere / home-grade rating
  outside the habitable zone. A liquid climate outside the zone that really comes from its greenhouse or
  internal heat is an EXCEPTION, recorded with its reason on `CelestialBody.climateException`.
- **`Enforce(galaxy, species)`** — checks every body, repairs (a natural world is re-derived from its
  orbit by `SolarSystemGenerator.ReconcileClimate`; a forced one has its heat clamped to its orbit),
  re-rates, checks every starting option is in the zone, and logs with `[WorldConsistency]` in
  development builds. `LastFound` is how many it had to fix.
- Related, same change: the cradle is never a belt rock (Mirfak IV a was one); it goes on the ring that
  suits its species (`PlacementRings.RingForHeat`); a world traded out of that lane, and moons moved by
  `EnsureHabitableWorld`, are re-derived for where they land; the cradle's moons take their heat from the
  orbit instead of `TerrainVariance`; a second starting option must orbit where its species' climate is
  what the sun gives. Tests: `Assets/Tests/Editor/WorldConsistencyTests.cs` (EditMode) and
  `tools/world-placement-check.mjs`.

### ResourceGenerator.cs
- **`GenerateResources(body)`** — bulk Metal/Energy/Water per body type (now covers every type).

### OreGenerator.cs
- **`Enabled = false` since 2026-10-09**: ores are switched off (they speckled the Mineral Index).
  `Populate` clears tiles instead, the loader ignores saved ore cells, and POIs carry no named ore.
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

### WorldExplanation.cs  *(why is this world like this? 2026-10-11)*
One builder for every body's explanation, gated by `Survey.LevelOf` and the fog (`SystemPresence.Revealed`):
- **`ForDisplay(b)`** — the whole rich-text explanation, cached per body against a signature of every field it reads.
  **`Basic(b)`** (always): kind + main reasons in plain words (orbit band, mass, air, plates, belt, host planet,
  homeworld). **`Measured(b)`** (level 1): temperature split starlight / air / surface (`PlanetTemperature.TypeModifierC`)
  / internal heat, the regime floor or ceiling, equator-pole swing, tidal day/night; air built up from mass, field,
  outgassing, stellar wind and heat (`AtmosphereRules`); magnetic field and why; water and its state; plates and their
  odds (`TectonicsRules.Chance`); tidal lock and the tide that caused it. **`Deep(b)`** (level 2): geology and hotspots,
  mineral modifiers and why, life (`BiosphereRules` gates in order), storm drivers, terraforming outlook.
- Reads `WorldClassifier.Physics(.., out ClassCause)` — the branch that decided the type — so the words and the rule agree.
- UI: `UI/WorldExplanationPanel.cs` (`Build` = one live label; `TooltipOnLastRow` = basic text on the Type row), used by
  the Planet View Overview (under Climate & Weather) and the Inspector planet Overview.

### HomeworldOnboarding.cs  *(the new-game opening, 2026-10-09)*
A new game owns nothing. `GalaxyGenerator.ForceHomeWorld` still builds the species' CRADLE (≥85%, locked)
but no longer claims, settles or builds on it. **`BeginNewGame(home)`** → `ChooseWorld` (home system framed,
species zone on via `HabitableZoneVisualizer.SetSpeciesMode`) → **`Claim(b, name)`** (owned, level-2 survey,
economy + starting fleet re-homed with `UnitManager.SetHomePlanet`) → `PlaceCapitol` (the capitol is placeable
and free only here — `SurfaceBuildManager.Founding`) → **`CapitolPlaced`** settles it → Farm → Combustion →
Mine → 2 Habitat Blocks (`Tick` advances when the job is built or queued; `Possible` asks the placement rule itself,
species-aware and cached, so an Aquarii kelp farm on water counts; on a world with no mineral ground the power step
asks for the first plant that fits — steam turbine, geothermal, wind, solar, hydro — instead of the Combustion
Plant, and a step nothing can satisfy is skipped). Runtime-only; `BlocksSaving` gates
`SaveLoadMenu` until the capitol is down. UI in `HomeworldOnboardingUI.cs` (banner, "Are you sure?", naming
window) and `AlertMarker` (the flashing "!" on the Build category tab and structure card).

### GameCalendar.cs  *(one second of game time is one in-game DAY)*
30-day months, 12-month years, the game opens on **Year 0001, Month 01, Day 01**.
- **`TotalDays`** (the only state, a `double`; saved as `SaveGame.calendarDays`), `Year / Month / Day`,
  `Stamp()` / `Short()`, `Reset()` / `SetTotalDays()`, `OnDayChanged`.
- **`Duration(seconds)`** — the conversion every readout goes through: "18 days", "4 months", "1 year
  3 months". Nothing about the SIMULATION changed; durations were always in seconds of scaled game
  time, and this changes only the language they are reported in.
- **`GameClock`** — the one component allowed to advance it, so it cannot be double-counted.

### TimeController.cs  *(legacy; Space toggles 1×/5×)*
Writes `Time.timeScale = timeScale` every frame — the HUD sets `TimeController.timeScale` to stay in sync.

### EarthquakeManager.cs  *(on a geological clock, and only on marked ground)*
Return periods, not a per-second chance: **small ~every 25 years, moderate ~50, major ~100** (three
independent rolls, so the numbers mean what they say). A quake damages ONLY what stands on ground the
Geothermal Index has highlighted (≥70%) — a promise the player can verify by turning the overlay on,
which is what makes a fault line a decision rather than a tax. Damage is repairable and nothing healthy
is destroyed outright.

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
- **Surveys feed research (2026-10-11):** a level-1 survey pays `PointsPerExploration` (25 RP,
  `AwardExploration`, which nothing used to call), a deep survey `PointsPerIndexRead` (12) per index the
  world has (`AwardDeepSurvey`). **`IncomePerSecond()`** — labs on settled worlds + deployed research auras
  (`StationEffects.ResearchAuraTotal`) x `ResearchRateMult`, memoised 1 s; the HUD shows it as "(+x/day)".
- Survey speed tech: `Tech.surveySpeed` → `TechEffects.SurveySpeedMult` (Deep Sensors +15%, Autonomous
  Survey Drones +35%), folded into `Survey.TechSpeedMultiplier` with the Empire Tech level's +20%/tier.
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
- **THE SYSTEM FOG OF WAR (2026-10-11)** — `VisibilityService.Unexplored(sys | body)`: in player mode a system
  `SystemPresence.Known` says nobody has visited is drawn as its star only — worlds, moons, orbit lines,
  belts, owner rings, ships parked there, nameplates, gravity wells and the system grid all concealed. A
  DERIVED concealment folded into `Apply(body)` / `Apply(unit)` on top of any stored reason; NOT in
  `IsHidden`/`ReasonFor` (FactionAI and the clues ask those about the world, not the player's view) and never
  saved. **`SystemFog`** (Systems/SystemFog.cs) polls 4×/s and re-applies a system the moment its answer flips
  (a ship crosses the detection circle; Dev Mode toggles) — no reload. **`CameraController.EnforceFogFloor`**
  holds the zoom just above `GalaxyLOD.SystemViewCeiling` whenever the look-at point is inside an unexplored
  system's circle, which covers every way in (wheel, Focus, F, zoom lock, Go-to, ViewSystem, notifications,
  loads); `JumpTo` applies it up front. The ROOT CAUSE of "I can zoom into unvisited systems" was
  `SystemPresence.Scan` counting ANY settled world as the player's presence — FactionAI settles every rival
  capital, so every rival system was stamped `visited` (sticky, saved). Now only the player's own (or an
  ownerless legacy colony) counts.
  - **Things in open space** — `FoggedAt(point)` = inside an unexplored circle (`UnexploredSystemAt`, deepest
    circle wins) and outside the player's sensors (`PlayerSensorsSee`: `SquadronAI.SensorRange` or a probe's
    `visionRange`). `FoggedForPlayer(unit)`: never the player's own; docked goes with its world; in space,
    `FoggedAt` its position — `SystemFog.PollShips` re-applies a ship when that answer changes. Comets use
    `FoggedAt` (CometManager), derelicts their system's `Unexplored` (DerelictRenderer.ConcealedSystem).
  - **Effects of hidden ships** — `VisibilityService.ShownToPlayer(unit)` (the hull's actual ConcealBinding
    state if it has a visual, else the rules). A round fired by a hidden ship is simulated but never drawn
    (`ProjectileRenderer` `Shot.hidden`: no bolt, beam, light, muzzle flash, launch sound or intercept burst).
    Impacts and death explosions are keyed on the TARGET (`CombatManager.ResolveHit/Destroy`), so a visible
    ship shot by an unseen one still visibly takes the hits. Scrap flash (`UnitManager.DestroyUnit`) and the
    focus-fire ring (`FocusTargetMarker`) skip hidden ships. Engine plumes and nav lights are children of
    the hull (`ShipLights`) and are concealed with it.
  - The habitable-zone band never moves to an unexplored system (`StarOverview.Open` skips the Retarget;
    `VisualizeGalaxyStepped` falls back to the home system).
  - **Saves before format 2** run `SystemPresence.RepairLegacyVisited`: only systems holding a rival-settled
    world are re-examined, and one stays visited if anything saved shows the player there (a player-owned
    or player-claimed world, any survey/deep-survey progress, a player ship there now). Otherwise it
    returns to the fog. Format 2 saves are never touched.
  - **Panning** at close zoom toward an unexplored system meets soft resistance at its circle
    (`CameraController.ResistPanIntoFog`: the inward part of the move fades to zero across a band outside
    the edge, the along-edge part is kept) with a throttled hint, instead of crossing and being lifted out.
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

### ZoomField.cs  *(a planet's own space, 2026-10-09)*
`ZoomFieldRules` — every planet (not moons, not belt rocks) has a flat disc a bit wider than its outermost
moon's orbit (`Radius`). It is the camera's zoom-lock zone (`CameraController.TryAutoLock`, which also
locks onto the player's undocked ships), the planet's orbit (`ParkedIn` adds parked ships to the Orbit
tabs), and where new hulls wait (`BuildOrder.yardBodyId` / `UnitManager.YardFor`). `ZoomFieldRenderer`
draws it (circle + grid, 50% alpha) for every planet while the **Gravity Wells** switch is on (`MapGrids.Show`,
default on, PlayerPrefs — this replaced "always in Dev Mode"), and otherwise from the Orbit tab toggle or for
8 s after a station is queued (`FocusForConstruction`). Never in an unexplored system. Fields are kept across
the galaxy/system switch and only hidden (they used to be destroyed and rebuilt every crossing). Render queue
`ZoomFieldRenderer.Queue` (3000). Per-planet colour / grid / size are Dev sliders in `OrbitControlPanel`, saved.

### SystemGridVisual.cs  *(the solar-system grid, 2026-10-11)*
One per system, child of `sys.pivot` (built beside `DetectionRingVisual` in `VisualizeGalaxyStepped`): a faint
line-only grid centred on the star out to `SystemPresence.DetectionRadius`, plus a stronger 2 px edge ring on
that circle. `MeshTopology.Lines` (1 px, pixel-art friendly, nothing to alias); major lines fade out and a minor
subdivision fades in by ON-SCREEN spacing, so it never moirés. Drawn UNDER the gravity wells by render queue
(`Queue` = wells − 10; both ZWrite Off, so no z-fighting whatever the orbital inclination). Rebuilt only when
the radius changes. Same switch as the wells; hidden in unexplored / concealed systems and during the intro.

### MapGridToggle.cs  *(UI — the Gravity Wells switch)*
`MapGrids` — `Show` / `Set` / `Toggle` / `OnChanged`, persisted as `MapGrids.GravityWells`. `MapGridToggle` — the
38 px button in the bottom-right corner of the main canvas (`PixelIcons.Grid` at exactly 2x, black plate, the
shortcut column's bright square "on" frame). Nothing else anchors to that corner; it is built before the windows
so they draw over it.

### NameplateManager.cs  *(UI)*
Bordered name plates over every surveyed body and every discovered system (anchored to `sys.pivot`, the
barycentre), with the player's crest or a rival's colour swatch on the left. Screen-space, no raycasts.

### Seeing, selecting and finding ships  *(2026-10-11)*
- **`ShipMarkers`** (Visual/ShipMarkers.cs) — a screen-space marker for every ship `VisibilityService.ShownToPlayer`
  allows, in system view only: a 9x9 pixel-art role glyph (`ShipGlyphs`: warship, scout, science, colony,
  terraformer, worker, station, probe) in the owner's colour, drawn at an integer pixel scale. It fades out as
  the hull grows past 22-44 px on screen. Ships docked at one world (per owner) that would overlap collapse into one
  badge with a count, set just beside the world so the world stays clickable; overlapping ships in open space
  collapse where they are. Selected ships get pulsing corner brackets (around the hull once it is big). Hover
  shows name, class and status. No GraphicRaycaster: **`Pick(screen, out Hit)`** picks by distance (14 px, a 7 px
  "core"), so markers never count as UI to the click guards.
- **Selection priority** (`ClickPriority`): a marker under the cursor wins first. Off the world's drawn disc, anywhere
  in its 14 px radius wins; on the disc, only its 7 px core does. Then the old rule: the body's drawn disc beats a
  ship collider, and a ship collider in the inflated pick halo beats the body. `UnitUnderCursor` (right-click targets,
  T) asks the markers first too. `ShipMarkers.Update` handles marker clicks over open space; `BoxSelectController`
  treats a marker as a collider, so clicking one does not clear the selection.
- **`ShipSelect`** (Systems/ShipSelect.cs) — one click path for tokens, hulls, markers and list rows. Click selects,
  Shift toggles, double-click selects all your ships of that class on screen, a stack click selects your ships in it.
  **No click moves the camera** — map clicks and, since 2026-10-11, list rows too (`SelectRow`: Fleet window, both
  Orbit tabs, the Fleet window's place headers). F/Q frame the selection whatever it came from; Tab frames each ship
  it steps to; the Inspector ship "Focus Camera" button goes through `ShipSelect.Focus` (identity follow, as F).
  One click per frame (`handledFrame`), so a click seen by two handlers is not read as a double-click.
- **`ShipHotkeys`** — Tab / Shift+Tab cycle your ships (build order) and frame each one; "." jumps to the next idle ship.
- **`ShipRoster`** (Systems/ShipRoster.cs) — **`CollectAt(body)`**: every visible ship docked at a world or one of its
  moons, or parked inside its orbital field, any owner, yours first. `StatusWord / StatusLine / StatusColor`,
  `PlaceOf / SystemOf / IsIdle / PlayerShips`, and `Signature(body)` for window rebuilds.
- **`ShipRosterUI`** (UI/ShipRosterUI.cs) — the one ship row (glyph, name, squadron, class, owner, status, hull bar,
  "Send to…") and **`OrbitSections`**, the body of both Orbit tabs (Inspector + Planet View). The Inspector keeps the
  planet on its breadcrumb when a row is clicked. **`FleetWindow`** (HUD "Fleet") is the player's ships grouped by
  system, then place, with All / Idle / Moving / Working filters.

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
- **`BuildGrid(body)`** — one texel per cell, from the tiles; the globe, thumbnails and moon panes.
- **`BuildGridTextured(body[, budget])`** — N×N texels per cell with the biome grain, plus the 500 m
  **contours** (`PaintContours`, not on a gas giant) and a **mip chain built by hand** (`BuildMips`): a 2×2
  block holding any contour texel becomes a contour texel, so a line stays one dark pixel at every zoom.
  The texture used to have no mips, and at fit zoom Point sampling strode over the one-texel lines —
  that was "the topography lines are not all being rendered when zoomed out".
- **`ElevationShade(body, elevation)`** — the brightness shifter: ±`ShadePerBand` (5%) per 500 m band
  off the datum, clamped `ShadeMin`..`ShadeMax`, applied in all three builds. Sea level keeps the biome's
  own colour; peaks go bright, trenches go dark.
- `Build(body)` — the legacy sampled render, same shading. Used by nothing on the main paths now.

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
- **`DeepGreenhouseC(atm, type)`** (2026-10-10) — a runaway sky's extra warming past the saturating 45 °C:
  0 below 10 atm, logarithmic to +250 °C at 90 atm, never for gas giants. Read by `BodyAverageCelsius`,
  the 5-argument `BaseCelsius` (the terrain generator), `WorldClassifier.ClassifyC` and
  `TerraformFeasibility.HoldsWaterWarning`. `GreenhouseCelsius(b)` is the total.
- **`LatitudeSwingC(atm)`** (2026-10-11) — equator-to-pole half-swing: ±25 airless, ±15 at 1 atm (as
  before), ±8 at 4+. Readout and terrain generator share it.
- **`HomeworldVentingC` / `HomeworldVenting(type)`** (2026-10-11) — the internal heat a volcanic homeworld
  is built with (120 ± 20 °C); `GalaxyGenerator.TameHomeworldVents` re-rolls the cradle's seed (no plates)
  to what its orbit needs. A plate world's venting is ~560 °C, which used to put a Pyrothian cradle
  anywhere from 150 to 800 °C.

### AtmosphereRules — the runaway greenhouse (2026-10-10)
- **`RollRunawayGreenhouse(b, rel)`** — inside the zone's inner edge only, a terrestrial of mass ≥ 0.7
  runs away with `RunawayGreenhouseChance` (42% on rings 2-3, ~11-16% on ring 1): 30-90 atm, seas boiled
  off, sterile, no plates (a stagnant lid, so it classifies Barren). Called from `ApplyWorldPipeline` and
  `ReconcileClimate`. **`IsRunawayGreenhouse(b)`** is derived from pressure (≥ 20 atm, not a gas giant),
  so there is no new saved field. `WorldClassifier.Describe` names it "greenhouse world";
  `TerraformDiagnosis` diagnoses Toxic + AtmosphereTooThick instead of "airless"; `WorldConsistency`
  records it as the world's `climateException` inside the edge and repairs one anywhere else.
  `tools/world-placement-check.mjs` section 4 gives the distribution by ring.

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

### RaceVfx.cs  *(per-race ship effects, data-driven)*
- **`RaceVfxProfile`** — one civ's drive plume colours/flicker, hull emissive multiplier + pulse
  (beats, depth, throttle boost, **`emissiveFlicker`** — a Perlin gutter on top of the pulse), nav
  lights on/off + colours + blink tempo, weapon tint + blend amount, **shield colour** (the ShieldBubble
  and its splash). Defaults: Pyrothian lava ×3 throbbing on a 2-beat pulse, guttering (flicker 0.18)
  and burning hotter under power; Aquarii ×1.8 and Sylvan ×1.6 slow bioluminescent breath, no lamps;
  Terran ×1 (no glow) with aviation lamps; Cryithn ×1.4 shimmer with lamps.
- **Does the emission actually show? (checked 2026-10-11)** glTFast 6.10's shader graphs gate emission
  behind the `_EMISSIVE` keyword (URP/Lit uses `_EMISSION`), which its importer enables whenever the glTF
  has an emissive map or a non-black factor; every meshy hull ships an emissive map at factor 1.
  **`HullMaterials.EmissionLive`** checks the right keyword per family before a glow is lifted. The project
  is LINEAR and URP converts bloom's 1.05 threshold from gamma (~1.11 linear), so a texel at 1.0 never
  blooms at ×1 — which is why Terran hulls stay paint and the lava/bioluminescent races are multiplied over.
- **`RaceVfx.For(unit)`** — by `UnitModelLibrary.CivFolder(u)`, the SAME rule the mesh uses (player
  = chosen species, others = `owner.id % 5`). **`WeaponColour(shooter, classColour)`** — 40% lean
  toward the race tint; ownerless shooters keep the class colour. **`SnapBeats`** keeps hand-edited
  periods on the FleetClock grid.
- Optional override **`Resources/SpaceAssets/race-vfx.json`** (keys = civ folder names, only named
  fields change, via `JsonUtility.FromJsonOverwrite` onto the defaults). Re-read on **F10** with
  ship-meshes.txt (`UnitModelRenderer.Update`).
- **`HullMaterials`** — property lookup across URP/Lit (`_BaseColor`/`_BaseMap`/`_EmissionColor`) AND
  glTFast's graphs (`baseColorFactor`/`baseColorTexture`/`emissiveFactor`); the generated .glb hulls
  only have the latter, so tint and livery used to do nothing on them. `Prepare` shares one tinted /
  liveried copy per (material, owner tint, livery) instead of cloning per ship.
- Consumers: `ShipLights` (plumes, nav lights, hull glow via a per-renderer MaterialPropertyBlock so a
  shared material can still pulse per ship), `ProjectileRenderer.Fire` (`Shot.colour`), `CivLivery.ApplyTo`,
  `ShieldBubble` / `ExplosionRenderer.ShieldSplash` (shield colour).
- **Which race a ship is**: `UnitModelLibrary.CivFolder` — the player's species for the player; for a rival,
  its FactionAI profile's `speciesIndex` (saved), falling back to `owner.id % 5`.

### Ship effects, shields and combat visuals  *(2026-10-11)*
Everything below draws with **`FxKit`** (Visual/FxKit.cs): one additive HDR shader
(`Resources/Shaders/FxAdditive.shader`, "FX/Additive"), a few procedural textures (dot, streak, ring, beam,
shard, smoke), ONE shared material per texture, colour per renderer through a MaterialPropertyBlock
(`SetColour`), and **`Floor(at, world, px)`** — every effect is the larger of its world size and a size in
PIXELS, so a fight stays readable at system zoom. `SystemView` (= `GalaxyLOD.SystemMode`) gates all of it.
- **`ShipHardpoints`** (Visual/ShipHardpoints.cs) — loads `Resources/SpaceAssets/Ships/ship-hardpoints.json`,
  baked offline by `tools/bake-ship-hardpoints.mjs` from the .glb geometry: per hull, engine NOZZLES (rear-most
  emissive clusters: position, radius, glow) and GUN points (bow tip, flanks, dorsal pair, keel; a station's
  rim) with a facing, in Unity mesh space (= the model root's local frame). Each entry records the
  ship-meshes.txt rotation it was baked under and is REFUSED if the manifest has changed since (an F10 edit),
  so a stale plume never sits on a nose — the rig falls back to the bounds.
- **`ShipLights`** (Visual/ShipLights.cs; `FleetClock` lives at its top) — one per drawn hull, built by
  `UnitModelRenderer.Build` (`Init(u, modelRotation, ownerColour, meshPath)`):
  - **Plumes** at the baked nozzles (biggest 6), else the stern of the bounds: a white-hot core, three dots
    trailing astern and one streak along the hull's on-screen axis, in the race's core→tail colours, sized off
    the bell. Length and brightness follow throttle (`SetThrottle`, from the trip ramp in `TickShip`); a
    **flare** (white-hot, long) while the drive is still spooling up toward what it was asked for.
  - **Running lights** for the races that carry them (aviation red/green, strobes, bow beacon, two owner-colour
    lamps); stations get a **rim chase** on their rim hardpoints plus a crown beacon. All on FleetClock.
  - **Hull glow** (`CollectGlow`/`GlowFactor`): the race's multiplier × pulse × ember flicker × throttle boost.
  - **Guns**: **`TryMuzzle(target, mount, out muzzle)`** — a baked gun facing the target, rotating through the
    good ones mount by mount (CombatManager.FireOne, ProjectileRenderer's point-defence tracers);
    **`FlashMuzzle(pos, colour, dir, size)`** — a flash and a barrel streak that ride the hull;
    **`Recoil(dir, fraction)`** — kicks the hull's own child parts back (never the root, which the renderer owns)
    on a stiff spring. Railgun 10% of hull length, plasma 6%, torpedo 5%, autocannon 2.5%.
  - **Damage**: under 70% hull, smoke from hull points (more, darker, longer as it falls); under 45% sparks;
    under 30% a guttering fire on one point. `DamageEffects` switch.
  - **Shield**: attaches the `ShieldBubble`; **`ShieldStruck(inbound, strength, collapsed)`** (CombatManager),
    **`ShieldContact(from, out p)`** (a beam stops on the shell), **`HullContact(from)`** (impacts land on the
    side that was hit).
  - Detail by on-screen size: under 2.5 px nothing (ShipMarkers' glyph has it), under 12 px plumes only.
    Asks `VisibilityService.ShownToPlayer` 4×/s and draws nothing for a hidden hull. Zero allocation per frame.
  - **`HullRenderers`** — the hull's own renderers (all LOD levels), captured before any effect exists.
- **`ShieldBubble`** (Visual/ShieldBubble.cs, `Resources/Shaders/ShieldBubble.shader`) — an ellipsoid round the
  hull in the race's shield colour: OFF out of combat with a full screen (costs nothing); a faint fresnel rim in
  a fight; a HIT lights the pixel-cell pattern and a ripple ring spreading from the impact point (4 at once);
  under 25% it shimmers unsteadily; on COLLAPSE it stutters out and shatters into pixel shards
  (`ExplosionRenderer.ShieldShatter`); on REGEN it shimmers back in from the keel up behind a bright band.
  `Enabled` switch (Settings).
- **`ExplosionRenderer`** — the pooled particle hub (640 quads, 6 lights): `Burst` (a round swatted down),
  `Flak` (a heavy round swatted down: bigger, with a hanging puff), `Impact` (flash in the shot colour, sparks
  thrown back along the round's path, a scorch that rides the hull, debris, a light for a heavy hit),
  `ShieldSplash`, `ShieldShatter`, `Smoke`, `Spark`, `Glint`, and `Death` — flash + light, a fireball cooling
  white→yellow→orange→smoke, tumbling chunks shedding embers, a shockwave ring in the ecliptic, a pall of
  smoke; a STATION goes up in a run of secondary blasts and then the big one.
- **`ProjectileRenderer`** — one look per weapon class (all race-tinted via `RaceVfx.WeaponColour`, all from the
  shooter's real gun hardpoint):
  - **Beam laser**: a charge-up glint, then a held beam (white-hot core inside a coloured glow, width
    flickering) that tracks its target for 0.2 s, with a scorch spark where it bites.
  - **Railgun**: a bigger capacitor glint, a needle-thin white flash and an ion trail that widens and fades.
  - **Pulse laser**: short bright bolts with a ghost trail. **Autocannon**: rapid thin tracers.
  - **Plasma**: a breathing halo round a hot streak with a long trail (the bottled-gas wander is kept).
  - **Missile / torpedo**: a body and a bright motor (brighter in boost), an exhaust TrailRenderer that fades
    where it is when the round dies, a slight weave (drawing only), a smoke puff at launch.
  - **Point defence**: when a mount catches a round, a burst of rapid tracers flicks from the mount to it; the
    round pops (`Burst`) or, for plasma and torpedoes, goes up as `Flak`.
  - **Instant shots let go after their charge**: hit or miss is still decided at the trigger, but the damage
    lands at discharge (0.06 s beam, 0.10 s railgun) so the beam and the impact arrive together.
  - Pool pieces are retired by switching off their RENDERER, never the GameObject (MapTierVisibility re-activates
    the root's children on the way back from galaxy zoom).
- **Hits on a shield** (`CombatManager.ResolveHit`): the shield absorbs first (`Shields.Absorb`), the splash and
  ripple go on the bubble and the absorbed part never reaches the hull; only what is left goes through armour.
  Impacts are keyed on the TARGET's visibility (fog of war), as before.

### Shields — the rules  *(Systems/Shields.cs, 2026-10-11)*
- **Capacity** is a share of the hull's own maximum (`EffectiveHealth`, so it scales with class and rank):
  **warships 30%**, **stations 40%**, civilian / science / colony / worker hulls **15%**, probes **none**.
  `Unit.MaxShield`, `Unit.Shield` (runtime only, not saved: a loaded game starts with every screen full),
  `Unit.ShieldFraction`.
- **Absorbs first**, ignoring armour, at a per-weapon rate (`VsShield`): beam ×1.2, pulse ×1.1, plasma /
  missile / point defence ×1.0, torpedo ×0.9, autocannon ×0.8, railgun ×0.75. What the shield could not hold
  carries on to the hull, through armour, as before.
- **Regenerates** only out of combat and only after **6 days** (seconds) without a hit (`RegenDelay`), at
  **10% of capacity per day** (`RegenPerSecond`): a full recharge takes ten quiet days. No regen in a fight.
- **Collapse** at zero: the bubble shatters, and it stays down until the regen rule brings it back.
- Shown as a blue strip beside every hull bar: the Fleet window / Orbit tab row (`ShipRosterUI.Row`), the Order
  of Battle (`FleetRosterPanel`, fleet / squadron / ship rows, `Shields.GroupFraction`), the ship panel
  (`UnitInfoPanel`: hull and shield bars side by side) and the Inspector ship Overview (CONDITION) and Stats.
  `Shields.BarColour`. There are no world-space health bars in the game.

### ShipPixelation.cs  *(the hulls, slightly pixelated — 2026-10-11)*
The ship meshes (and only they) are drawn at HALF resolution with point filtering and composited back over the
scene, depth-tested, so planets, effects and UI stay sharp. **Runtime-injected, no renderer-feature asset**:
`ShipPixelPass` (a URP 17 RenderGraph `ScriptableRenderPass`, `AfterRenderingOpaques`) is enqueued on every
base Game/SceneView camera from `RenderPipelineManager.beginCameraRendering`. Hull renderers
(`ShipLights.HullRenderers`) are moved to **layer 8** (`ShipPixelation.Layer`, unnamed, used by nothing else)
while it is on, and **both `Assets/Settings/PC_Renderer.asset` and `Mobile_Renderer.asset` leave layer 8 out of
`m_OpaqueLayerMask`** (4294967039), so URP's own opaque pass does not draw them. The pass draws that layer from
the camera's own cull results into a half-res RGBA16F + D32 pair (setting `_ScaledScreenParams` to the half
size for the draw, which Forward+'s light clusters divide by), then `Resources/Shaders/ShipPixelComposite.shader`
point-samples both, clips empty texels and writes SV_Depth under a normal ZTest. Scene view / non-main cameras /
a backbuffer target get a full-resolution draw of the same layer. **Watchdog**: if the pass is queued 60 times
without running, it switches itself off for the session and puts every hull back on its old layer — sharp,
never invisible. Setting "Pixelated ships" (on by default, PlayerPrefs `Graphics.ShipPixelation`).

### Movement life  *(UnitModelRenderer, 2026-10-11 — drawing only)*
- **Docked**: a LOW ORBIT inside the world's gravity well (`ZoomFieldRules.Radius`): planet radius + a fifth of
  the well (0.4..1.6), never past half of it. One clock per world (`OrbitClock` + a per-world phase), so the
  anchorage spacing holds whoever arrives when; rate falls off with radius (ω ∝ r^-1.5, 2..8°/s). Nose along
  the orbit, banked 9° into the curve.
- **Parked inside a well without docking** (AI / Dev fleets, point moves): the drawn hull keeps the offset it
  parked at and circles the world with it (the sim point stays put; combat, markers and box-select read the
  drawn transform — `BoxSelectController` now projects `UnitVisuals.TransformOf`).
- **Parked in open space**: a slow Perlin drift round its spot, the bob, a nose swaying a few degrees.
- **Arrivals settle** (`Settle`, 1.4 s): the jump from flight to orbit/park is eased away, so a ship flies into
  orbit. **Departures** start from where the hull is drawn (flightPos follows it while parked) with an initial
  heading toward the destination, not from the world's centre where the sim marker sets off.
- **Stations** circle their world at 0.3× and spin at 0.5× their old rates — slow and stately.

### Dev: Ctrl+Shift+E — start a fight  *(Systems/DevCheats.cs, `DevBattle`)*
Dev Mode only. Spawns a hostile squadron of the first faction at war with the player (Dreadnought, Cruiser,
Frigate, Fighter III, Fighter II, Fighter — every weapon class) 14 units off the first selected player ship,
else the player's ship nearest the camera; if no player WARSHIP is within 30 units (or there are no player ships)
it spawns a matching player squadron as well. Ordinary units (saved, selectable); press again for another wave.

### Settings → SHIPS
"Pixelated ships" (`ShipPixelation.Set`), "Show shield bubbles" (`ShieldBubble.Enabled`), "Battle damage smoke &
fire" (`ShipLights.DamageEffects`) — all persisted (`ShipFxPrefs`, PlayerPrefs `Graphics.*`).

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
- Live edits: **mass** (quantized to whichever class it lands in), radius (updates habitability for
  planets), orbital speed, phase, inclination, eccentricity, vertical offset, **rotation speed**,
  **rotation direction (prograde/retrograde)**, orbit direction, ring visibility.
- **Rotation is not cosmetic.** Dragging spin below `RotationRules.MagneticFieldSpin` costs the world
  its magnetic field, which halves its atmosphere ceiling and sheds the air above it — and a live note
  under the slider says so. Spinning it back up returns all of it.
- **`RecomputeRealistic()`** — reset speed to the Kepler default. `Toggle/Hide`.

### StarInfoPanel.cs
- **`Show(star)`** — star attributes + a **Show Habitable Zone** toggle (disabled if the star has none).

### PlanetViewWindow.cs  *(HUD "Surface" — the world you develop)*
Seven tabs over one shared surface grid:
- **Info** — name, type, size class, climate/weather prose, development density, survey state.
- **Sites** — the points of interest on this world (what the retired detailed-map window used to hang).
- **Build** — click a structure to pick it up; it rides the mouse as a loose ghost, then SNAPS to the
  grid over the map (its art = fits, red = doesn't). Controls (2026-10-11): left-click places a fixed
  structure (then Build confirms) or starts a drawn one (drag paints / drags the box, Confirm builds);
  **R rotates** (fixed only, and only advertised when the shape isn't square); **right-click puts the
  piece down** in one step, **Esc** backs out one step (confirm → shape → piece); **X toggles
  demolition** (fixed structures come down whole). Categories: Civil · Electrical · Agriculture ·
  Science · Industry · Military.
  **Refusals are always said where the player is looking** — one path for every reason: the cursor
  window's verdict line ("Can't build here: Mineral Index here 34%, needs 70% — try the highlighted
  ground"), the red ghost, a floating label at the tile on a refused click (`BuildPlacement.Flash`,
  shared by the brush, fixed clicks and pylon runs), the Confirm panels' "Can't build:" line, and the
  card's red/amber line (`CanPlaceType` reason, have-vs-need shortfall, index not surveyed / world's
  best below the floor). The hover verdict is `PlacementCheck`: CanPlace for fixed classes,
  `SurfaceBuildManager.CanStartAt` (this cell + the minimum footprint's cost) for drawn ones.
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

### Survey.cs — the two survey levels  *(rules + readouts, 2026-10-11)*
- **Level 1 (Survey)**: any `canExplore` hull at the world (docked in orbit). **Level 2 (Deep Survey)**:
  `canResearch` hulls only; a research hull ordered to deep-survey an UNMAPPED world maps it first (the
  Research order stays at the front while it runs level 1). Progress lives on the WORLD
  (`explorationProgress` + `surveyRows`, `deepProgress`), so a ship leaving or dying pauses it and any
  ship resumes it. Only the player has fleets, so nothing leaks through rival surveys.
- **`CanSurvey / CanDeepSurvey(b, u, out why)`** — the one order rule (ship panel, Inspector, right-click
  menu). The old three-tier `DeepResearch` ladder (Empire Tech 4/7 gates on a "tier" that no longer
  unlocked anything) is deleted.
- Readouts: `Active(b, out deep)`, `Progress`, `RatePerSecond`, `EtaSeconds`, `LeadShip`, `JobName`.
- **`Grant(b, level, discoverOres)`** — silent granted surveys (cradle moons level 2 at generation, the
  chosen homeworld level 2 with ores into the Codex). Charts every site, which the old hand-set grants
  did not (the home sites could never be commissioned).
- **Completions are one path each**: `UnitManager.CompleteGround(u, b)` / `CompleteDeep(u, b)` (u may be
  null) — rows filled, RP paid, ruins announced, sites charted, ores discovered, Vael clue revealed,
  Planet View refreshed. A research hull with nothing queued carries on from level 1 into level 2.
  **`UnitManager.DevCompleteSurvey(b)`** (Dev Mode only): Planet View Survey card button, Inspector
  overview button, hotkey **Ctrl+Shift+U** (Planet View world, else the selected world).
- `BandRank` is the COMPACT order (= index in `BandOrder`); the old formula left a gap on even band
  counts, so the level-2 pass never reached one latitude until it completed. `RowBlocks` uses `BandY`.
- Front geometry for effects: `FrontOf(b, u, out row01, out along01)`, `DeepFrontBlocks(b, into)`,
  `SweepRevealOf` (the sweep without the owned/Dev visibility shortcut).
- Save format 3: `deepProgress` is authoritative; the "surveyed shows the Mineral index" inference runs
  only for format < 3 saves (it used to re-grant part of the Mineral index on every load).

### Survey rendering  *(UI/SurveyProgressUI.cs, UI/SurveyMapScan.cs, UI/SurveyShipBars.cs, Visual/SurveyScanFx.cs)*
- **`SurveyProgressUI.BuildSection`** — the SURVEY card at the top of the Planet View's Survey tab (world +
  moons with a survey running/part-done; live bar, ETA, ship; Dev button). `StatusLine(b)` is shared
  with the Inspector overview.
- **`SurveyPaneStrip`** — compact bar top-left of every map pane (host + each moon), visible from any tab,
  "complete" note for 5 s.
- **`SurveyMapScan`** — on each map content rect (map-normalised anchors): a scan line + shimmer trail at
  the level-1 front (moon panes get the block frame too), and the deep-survey front framed in the colour
  of the index being read (it had no marker unless that overlay was on).
- **`SurveyShipBars`** — pooled screen-space bars over surveying ships (10 pixel cells, cyan Survey / green
  Deep), hidden at galaxy zoom, during the intro, and when the hull is concealed.
- **`SurveyScanFx`** — pooled LineRenderers per surveying ship: a sweep ring at the working latitude
  (coarse 24-gon for level 1, fine 64-gon pair in the index colour for level 2) and a scan fan from the
  hull; per type (gas giants probe above the cloud tops with two rings, runaway greenhouses scan through
  haze, ice/ocean/volcanic tints). Brightness steps on `FleetClock`.

### SurfaceIndex.cs
Per-tile survey overlays, **derived not stored** — a stable hash of the terrain seed + position means
they cost nothing to save and survive a reload untouched (the same guarantee terrain already makes).
- `SurfaceIndexKind` — Mineral · **Geothermal** · Fertile · Weather · Solar · Water.
  `Get / Ramp / Name / Describe`.
- **`Geothermal` was `Heat`, and it absorbed the tectonics overlay.** One index for "where is the crust
  hot and moving", because two systems describing the same ground could and did disagree — a red fault
  line could run across ground the Heat Index called cold. The field lives in `GeothermalMap`; selecting
  this index also draws the plate lines and the per-plate push arrows over it.
- **Per-index floors** (`Floor(k)`): **Mineral 70%, Solar 20% (since 2026-10-09), everything else 40%**. Under the floor an index is
  not drawn, yields nothing (`Productive(k, v)`) and refuses placement. Bands are 10% steps from the
  floor (`Steps(k)`: 3 for minerals, 6 for the rest). `ShowFloor` is now the MINERAL floor only;
  `EarthquakeManager` keeps its own 70% constant.
- **Three indexes are read ABSOLUTELY** (`IsAbsolute`), bypassing the per-world percentile
  consolidation: **Geothermal** (40 on a plate line, 70 radiated, 97+ a volcano — four systems compute
  from these), **Hydro** (water tiles 100; land rings 1-6 at 94-98 / 90-93 / 80-89 / 60-79 / 50-59 /
  40-49) and **Solar** (whole surface: orbit maximum from `SolarRegionMax` — ~100 at ring 1, ~55 at HZ
  centre, mid-40s at the HZ outer edge — minus 10 per atmosphere, ±10 per 1,500 m of elevation; a
  tidally locked world's night hemisphere reads 0, `SolarDaySide`). `SolarViable` = does any tile
  reach the floor; it gates both `Present` and the Solar Array.
- **Weather** is consolidated, but its patches are **storm zones** (`BuildStormField`, cached per world):
  equatorial weighting × (elevation contrast + land/sea contrast), box-blurred into zones.
- **Fertility**'s ceiling comes from climate (`FertileClimateCeiling`: closeness to 20-22 °C, +6 with
  ≥40% water) and its band is bottom-heavy (`BandCurve` 1.8), so 90s are rare except on a Fertile World.
- **World modifiers bend the consolidation** — coverage, ceiling and `BandCurve` (see WorldModifiers.cs).
- **`Unlocked(b, kind)`** — Mineral needs a survey (you see seams from orbit); the rest fill in as a
  science ship reads the world. That's the reason to go back.
- Ramps: brown (mineral), orange→red (geothermal), dark→vibrant green (fertile), blue→white (weather).

### WorldModifiers.cs + WorldModifierBadges.cs  *(this world has something interesting)*
- `WorldModifier` — TidallyLocked · HighQualityMinerals · VastMineralDeposits · ExtremeWeather ·
  FertileWorld · ContinentalPlates. Several can sit on one world.
- **`Has(b, m)`** is the only way to ask. TidallyLocked reads `b.tidallyLocked`, ContinentalPlates
  reads `b.hasTectonics` (derived, never stored); the rest are bits in `b.worldModifierFlags`, and
  FertileWorld additionally requires its climate to still hold (`MeetsFertileClimate`).
- **Tidal lock is rolled WITH ROTATION** in `SolarSystemGenerator.ApplyWorldPipeline` (spin 0 → no
  dynamo → no field), planets only, likelier close in. `OrbitController.FaceParent` keeps the map's
  centre meridian (the vertex with UV nearest 0.5,0.5) toward what it orbits. Spin Up breaks the lock.
- **Since 2026-10-11 the lock chance reads the star's TIDE** (`TideStrength` = solar masses / AU³, `TidalLockChance`):
  none under `TidalLockMinTide`, so red-dwarf habitable worlds lock more and far outer worlds never.
- The rest are rolled in **`GalaxyGenerator.Finish`** (`WorldModifiers.Roll`) once every surface exists.
  The odds come from the world now: High Quality Minerals 9% on an active crust / 3% quiet, Vast Mineral Deposits
  10% with plates / 4.5% without (and granted to every starting world), Extreme Weather 4% plus its `WeatherDrivers`
  (tidal lock, fast spin, warm seas, deep sky). Same draws in the same order.
- **`WorldModifierBadges`** — self-updating row of 22px framed icons, border = the index colour the
  modifier bends. Nothing before a survey, "?" at level 1, the real icon + description at level 2.
  Shown across the top of the Inspector Overview globe and under the Planet View map's bottom-right
  corner, level with the tabs.
- Art: `tools/make-modifier-icons.mjs` draws every badge **and the Fertility index's apple tree**
  (`Index_Fertile.png`) from character grids, plus `Art/_review/modifier-icons.png`.

### SurfaceBuilding.cs + SurfaceBuildManager.cs
- `SurfaceBuildingType` / `SurfaceBuildingInfo` — footprint `shape`, driving `index`, cost, output.
  Mine = L, Farm = 2×3, Geothermal = O, Solar = I, Wind = T, Habitat = S, Factory/Spaceport = blocks.
- **`Rotated / CellsOf / Footprint`** — 90° rotation, re-normalized so the piece stays under the cursor.
- **`CanPlace / Place / Demolish`** (60% refund), `Occupied`, `Density`. `CanPlace` asks `CanPlaceType`
  for every world/empire gate (one copy, 2026-10-11) and adds the footprint, pending sites and price.
  `Place` is now only the two placements that settle a world (founding capitol, colony landing);
  spaceports and shipyards queue via **`SurfaceBuildQueue.EnqueueFixed`** (list price once, one
  structure's Labor, completes through `ForcePlace` as a rotated record, never a drawn shape).
  Unique classes count queued jobs (`SurfaceBuildQueue.CountQueued`). `ShortfallText` is the one
  have-vs-need wording (and flags a price over the storage ceiling); `MinimumCost` prices the smallest
  legal building; `PowerWarning` is the amber off-grid note. `tools/build-placement-audit.mjs` sweeps
  every class against these rules.
- **`EfficiencyAt`** — the average of the driving index across the footprint, **locked in at placement**.
  A mine on a seam pays forever; one on dead rock is a permanent mistake. `TickOutput` scales by it.
- `TickOutput` / `ResearchPerSec` pay through `SurfaceOutput` (siting × size × tech × condition × power ×
  synergy), then feed the factories (`Industry.Tick`). `AdjacencyBonus` = `SurfaceSynergy.Bonus`.
  `PredictedYield` adds the synergy and power-route preview for the piece in hand.
- `requiredTech` — gates a structure on research (`TerraformProjectInfo.requiredTech`'s twin). Checked in
  `CanPlace`; Fission needs F1, Fusion needs F2.
- **Dev Mode builds instantly** (2026-10-11), on top of being free: `SurfaceBuildQueue.FinishNow` on enqueue/unpause,
  `UnitManager.CompleteDevBuilds`, `TerraformManager.CompleteDevProjects` and `ColonyManager.CompleteDevConstruction`
  (city founding, yard/lab tiers), each through its normal completion path; `GameMode.SetDev(true)` finishes
  everything already queued (`FinishAllConstruction`). Paused jobs stay paused. Build-time readouts go through
  `GameMode.BuildTimeText`, which says "Dev: instant build".
- `allowMultiple` — opts out of `OneOfEachPerWorld`. Power infrastructure only: a grid you may build one
  relay for is not a grid. `uniquePerWorld` still wins over it.

### PowerGrid.cs  *(adjacency power with a hop limit, 2026-10-11)*
**Electricity as a place rather than a number.** Plants project a disc (2-4 tiles × tier); a Power Node
projects 7 once it is itself energised (a repeater); every other building **conducts by contact, up to
`MaxContactHops` (3) buildings**. Hop 0 = a plant, a live pylon, or anything with a tile in their disc;
hop k = touching a hop-(k-1) building. Past the limit a building is **too far**: off-grid, tagged and
listed as such. A grid = a connected component of energised buildings (every one contains a plant).
- **Derived, memoised across frames.** `Build` runs rounds of BFS (pylons switch on as they are reached),
  then union-find. The topology is cached per world and rebuilt only when the buildings' structural
  signature changes (records, types, tiers, cells) or `Invalidate()` is called; the numbers (generation,
  draw, served) are refreshed once a frame without allocating. Every mutation still calls `Invalidate()`.
- **Draw scales with size**: `DrawOf(p)` = powerDraw × LevelMult × `BuildScaling.CostMultiplier(tiles)` for
  a drawn footprint. Per-tile draws were cut to match (Mine 0.4, Farm 0.15, Factory 0.8, Refinery 0.6, Lab
  0.5, Centre 1.0; Shipyard 1.5 and Spaceport 1.5 are whole fixed structures).
- **Shortfall is shared NEAREST FIRST** (`Allocate`): hop-0 consumers are served in full before hop 1 gets
  anything, and so on — a short grid browns out from the ends of its chains. `ServedOf(b, p)` is a
  consumer's own share; `PowerFactor` = `Lerp(UnpoweredFactor, 1, ServedOf)`.
- `UnpoweredFactor` is the **emergency backup, 0.15** (was 0.35) — the same floor off-grid and on a starved
  grid, so connecting is never worse than not, and a collapsed colony still trickles metal.
- Queries: `NetOf`, `LinkOf` / `HopOf` / `TooFar` (hop, the neighbour it is fed through, the disc that
  lights it), `NeighboursOf` / `OwnerAt` (the cached contact graph SurfaceSynergy also uses),
  **`ProspectFor` / `ProspectText`** (the placement preview: lit directly, via N buildings with K hops left,
  or too far), `CanPlantNodeAt` (respects the limit), `ReachText` (the card's words), `GridColor(index)`.
- `served` vs `SteadyServed` — Tick runs ~1/s, the UI every frame, so served is derived from state.
- `Dead` — nothing generating; `Failed` — dead and the bank is empty. Charge lives on `PlacedBuilding.stored`.
- `PowerNet.projectors` is now only plants + live relays (it used to hold every member, so "pylon" counts
  counted farms); `members` is everything on the grid; `consumers` is sorted nearest first.

### Industry.cs + SurfaceSynergy.cs  *(the industrial chain and placement synergies, 2026-10-11)*
- **Mine** extracts metal (Mineral Index). **Refinery** reads no index any more: it smelts the ore of the mines
  it touches (+25% each, cap +50%, one refinery tile covers 2 mine tiles shared across them) and gets +25% per
  touching mine itself; 0.4 metal/s of its own. **Factory** makes no metal: it makes **Industry** = 1 × its
  output multiplier, worth +15% construction speed on its world per point (cap +100%), eating 0.25 metal/s per
  useful point only while the world's build queue has a running job (`Industry.Tick` from `TickOutput`;
  `SurfaceBuildQueue.Tick`/`Eta` multiply by `Industry.SpeedMult`).
- **`SurfaceOutput`** is the one set of production formulas (Metal/Water/Research/Energy/Storage) — the tick,
  `WorldEconomy`, the card all read it. `Storage` scales a depot drawn bigger than its authored shape.
- **`SurfaceSynergy.Bonus(b, p)`** is the one adjacency function (`SurfaceBuildManager.AdjacencyBonus` now
  forwards to it). Rules: Refinery→Mine, Mine→Refinery, Refinery→Factory, Depot→industry +10%, housing→
  industry +5% each (cap 15%), Lab↔Centre +10% (cap 30%), Power Distribution→plants +30%; total cap +75%;
  a source contributes in proportion to its own power factor and condition. `Parts`/`Gives` itemise it for
  the card; `PreviewText`/`PlacementSummary` say it under the ghost and on the confirm panel.
- `tools/industry-power-check.mjs` ports the hop rule, the sharing, the synergy caps, the opening balance and
  Industry, reading every constant from the C#.

### PowerGridBadges.cs  *(UI — which grid is everything on)*
While the power overlay is up: a tag on every building in its grid's colour (`2` direct, `2+1` fed through one
building, red `x4` too far — its tiles washed red — and `!` unreached), and a **grid list** in the map's
bottom-left corner (health, supply/demand, plants/pylons/users/caps). Clicking a row highlights that grid's
buildings. Wired in PlanetViewWindow next to the building card; `Covers()` is part of `OverMapChrome`.
SurfaceFx's power flow draws a thin **adjacency link** from each contact-fed building to the neighbour it takes
power from.

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

## Tooling (`tools/`)

**`Check-Scripts.ps1`** — a structural tripwire over `Assets/Scripts`. Not a compiler; Unity is the
real check and always will be.

```
powershell -ExecutionPolicy Bypass -File tools/Check-Scripts.ps1
```

It exists for the case Unity cannot cover — work done on a machine with no editor and no .NET SDK,
where the first sign a file is broken is the next person opening the project. Nine checks:

* **HEAD** — the first non-blank line of a file must look like the top of a C# file. Catches text
  dropped in above the `using` directives.
* **BALANCE** — braces and parens must balance and never dip below zero on the way. Catches a
  truncated or duplicated block anywhere.
* **ENUMS** — every `SomeEnum.Member` reference must name a real member (2,100-odd of them, across 38
  enums).
* **STRING** — every regular string literal must close on the line it opens. Delegated to
  `tools/check-string-literals.mjs` so there is one implementation of the rule rather than two that
  drift apart.
* **STATIC** — every `SomeType.Member` reference onto a project type must name a real member. This is
  the ENUMS check applied to classes, and it is where a refactor actually bites: rename or delete a
  static method and every call site still looks perfectly well-formed. Delegated to
  `tools/check-static-refs.mjs`, which is deliberately conservative — it skips any type it cannot see
  the whole of (ambiguous names, and anything inheriting from outside the project).
* **MEMBER** — the same question for a **local** whose type can be resolved: `var b = Type.Member;`
  and then, two hundred lines later, something that is not on it. `tools/check-member-refs.mjs`. It
  does not track scopes, so it counts every declaration of a name and throws the name away if there is
  more than one — C# code reuses `d` and `m` constantly, and the first version reported 26 findings
  that were all one variable being mistaken for another.
* **FOREACH** — whatever a `foreach` iterates has to *be* iterable. `tools/check-foreach.mjs`. This is
  `CS1579`, and it reached `main` once: `foreach (var b in SystemContext.Galaxy)`, where a `Galaxy` is
  a bag of *systems* and the flattened walk is `SystemContext.AllBodies()`. Nothing else could see it
  — the expression is balanced, correctly quoted, and every identifier in it is real.
* **UNITY** — no `??`, `??=` or `?.` on a `UnityEngine.Object`. `tools/check-unity-null.mjs`. The odd
  one out: it catches a *runtime* fault rather than a compile error, and it is here because that fault
  reads exactly like a compile error in the console. Unity overloads `operator ==` so a component whose
  native half has been destroyed compares equal to null — and `??` and `?.` are C# *language* operators
  that never run the overload. So `GetComponent<LODGroup>() ?? AddComponent<LODGroup>()` hands back the
  DEAD component and the next call throws `MissingComponentException: There is no 'LODGroup' attached
  to the "Model_Scout 1" game object`. That reached `main`, and the check found four more of the same
  shape the first time it ran. The fix is always `x = GetComponent<T>(); if (x == null) x = ...`,
  which is what `UIFactory.Ensure<T>` already is.
* **SCOPE** — no local may shadow one from an *enclosing* scope (`CS0136`). `tools/check-scope.mjs`.
  Every other check here asks about a **name**: does this member exist, does this string close. This
  one asks where a name is **live**, which is the thing a regex over `Type.Member` cannot see — and it
  is here because one got past all eight of the others on 2026-08-23. `SolarSystemGenerator` counted
  filled rings in `placed`, then counted asteroids in a second `placed` four hundred lines further in.
  Both readable, both correct alone, illegal together.

  It models scope as **extents** rather than as a brace stack, because a `for` header's variable is
  scoped to its loop and not to the block the loop sits in. The stack version reported 59 findings
  across code Unity compiles daily; every one was a sequential `for (int i …)`. It deliberately does
  not claim same-scope redeclaration (`CS0128`, equally loud from the compiler) or anything across a
  **lambda boundary**, where the language rule has moved between versions and this repo has no
  compiler to ask.

  **What it does not cover:** `CS0103`, an identifier used where it is not declared — which shipped in
  the same commit, `ClimateCoherence` reading a `body` that only its neighbours have. A check for it
  was written and thrown away at 239 findings, essentially all of them declaration forms the parser
  had missed (`int w = …, h = …;` alone accounted for dozens). Catching it needs a resolver, and a
  check that cries wolf gets ignored and then gets deleted.

The third earns its keep because the other two cannot see a *truncated identifier*. The stray line
that once reached `main` above PlanetViewWindow's usings read
`... activeIndex == SurfaceIndexKind.Minera ||` — perfectly balanced, and wrong. Comments, strings,
verbatim strings and char literals are stripped before counting, with newlines preserved so reported
line numbers are true. Exit code 0 clean, 1 with findings.

The fourth earns its keep the same way, and for the same reason it had to be added: a scripted edit
put **real newlines where `\n` escapes belonged** in three FleetCommandBar tooltips, and the first
three checks all reported clean, because a stray newline inside a string leaves the head line, the
braces, the parens and every enum reference perfectly intact. The first anyone knew was eighteen
compiler errors in Unity, seventeen of them spurious and none pointing at the fault.

**A skipped check exits 1, not 0.** The last six need `node` on `PATH`; if it is missing the script
reports them as `DID NOT RUN` and fails, because this script's whole job is to stand in for a compiler
that is not here — "Clean." has to mean all nine checks ran, or it is a claim it cannot back.

**None of this is a type checker.** It will not catch a wrong argument count, a wrong return type, or
an assignment to a get-only property. It catches the classes that have actually reached `main`, which
is a different and much smaller claim. Unity is still the real check.

**`verify-wiring.mjs`** — is every ship part of the game? Since 2026-10-11 it prints the full **wiring
matrix** and FAILS on any hole: for all 29 UnitTypes × 5 civilizations, its own mesh resolves; a UnitInfo
exists; a UnitModelLibrary fallback exists; exactly one ShipyardWindow category offers it (the catalogue is
the ONLY way to obtain a ship — no starting fleet, FactionAI builds none, tech gates no class); its shipyard
and Empire Tech gates are reachable; and every civ mesh has baked hardpoints with guns. Plus the older
checks (manifest lines, orphans, Ships/ vs Stations/ placement).

**`bake-ship-hardpoints.mjs`** — bakes `Resources/SpaceAssets/Ships/ship-hardpoints.json` (engines + guns per
hull, Unity mesh space) from the .glb files under the ship-meshes.txt rotations, and prints the orientation
table with a confidence column (`ok` / `??` the sharpness and bulk measures disagree / `LOW` both say the bow
is behind — check those by eye). `--check` writes nothing and fails if the json is stale. Re-run after any
ship-meshes.txt rotation change, or those hulls fall back to their bounds.

**`ship-orientation-sheet.mjs`** — renders `Art/_review/orientation-<Civ>.png`: every hull top-down as the
game flies it (glTFast X negation and the manifest rotation applied): top and side views, bow up, emission in orange, baked nozzles cyan, guns red.

**`world-placement-check.mjs`** — does every world make sense where it orbits?

```
node tools/world-placement-check.mjs [--seeds 2000]
```

Ports `InsolationRules`' band, `PlacementRings.RingForHeat`, `Habitability.PlacementZone`, the cradle and
second-option choice and `WorldConsistency`'s rules (constants read from the C#). Asserts the law runs
hotter inward and nothing inside the zone's inner edge is temperate; rebuilds Mirfak from the 2026-10-10
screenshots (Mirfak II's 35 °C is exactly a red dwarf's habitable ring, and 262-317 °C on the G sun it
actually orbits); then sweeps thousands of home systems per species and difficulty, OLD pipeline against
NEW. The layout is simplified — which rings fill is plain odds — because what it checks is where the
forced worlds go and what climate they get. Since 2026-10-11 it also pushes a share of worlds outward
after their climate is rolled (OrbitSafety's backstop) and asserts they were re-derived — the Mirfak Eta
II bug it used to miss — holds every species' cradle to its own band at its ideal, and (section 4) gives
the runaway-greenhouse distribution by ring.

**`world-causes-check.mjs`** (2026-10-11) — the whole natural-world pipeline ported (rings, lanes, ApplyWorldPipeline,
classifier, biosphere, AmplifyBiome, EnsureHabitableWorld, modifiers), run OLD and NEW over the same seeds: share of
every world class per ring and per star class, tidal lock by zone band and star, modifier rates, the CAUSE of every
paradoxical-sounding world (frozen inside the zone, hot giants, sterile water worlds) and the flags — worlds no rule
explains. Fails if the NEW pipeline produces any.

**`habitability-audit.mjs`** (2026-10-11) — every species × star class, BEFORE and AFTER the one zone
rule: band in xR and units, rings covered, cradle ring, type, °C and own-species rating; then the
temperature chain (a rocky world on each ring under 0-60 atm: °C, liquid or not, pole swing). Fails if a
cradle leaves its band, misses its ideal by more than its tolerance or rates under `ExcellentRating`.

**`terrain-relief-check.mjs`** — how rough is a dead world from one tile to the next, and does a small
moon read as a small moon?

```
node tools/terrain-relief-check.mjs         # writes Art/_review/terrain-relief.png
```

Ports the variation pass and builds dead-world grids at four real masses, measuring the mean height
step between neighbouring tiles (which IS the contour-line density) and the 5–95% spread, before and
after the three things `SampleNormalized` now does by grid size: caps the base frequency so a feature is
never under `MinBasePeriodCells` wide (a 15-cell moon had five three-cell "continents"), caps the
variation octaves at `MinVariationPeriodCells` (octaves under a cell are static, not terrain), and
scales relief by cells-per-feature (`ReliefScale`, floor `SmallBodyReliefFloor`). Measured: a tiny moon
went from a line on 87% of its cell edges and 3.5 km of spread to 33% and 0.9 km; an Earth-mass world is
essentially unchanged. The zero the metres are read from is `DatumShift`: the waterline when the world
has a sea, a fixed level when it does not — the old waterline datum sat 0.74 units under a dry moon's
mid-line, which is why every tile on one read 11,000–17,000 m.

**`terrain-elevation-check.mjs`** — does a biome still secretly mean *"how high"*, and do the contour
lines read as contours?

```
node tools/terrain-elevation-check.mjs      # writes Art/_review/terrain-elevation.png
```

Two requests, one tool, because they are halves of one change: taking altitude out of the biome names
removes the only thing that showed relief, so the 500 m contours have to put it back. Checking either
alone passes a map that is either flat or striped.

The probe **holds `ridge`, moisture and temperature fixed and sweeps elevation alone**, at five
roughness levels per classifier. Under a correct classifier that sweep is nearly silent — nothing
about the ground changed except its height — so every extra type it produces is an altitude band
wearing a biome's name, and the count is the measurement. It is a harsher test than a rendered world:
on a real map elevation and roughness correlate, so an elevation-driven band hides inside a
roughness-driven one, and here they are decoupled by construction.

**It caught the band that reading the code had missed** — `Volcanic`'s `elev > 0.62f -> LavaRock`,
which capped every volcanic world's high ground in one type whatever the ground was like up there. The
same sweep confirmed the two that were fixed on purpose (`Barren` and `Airless`'s Metallic Crust).

The contour half draws a cone **on the longitude seam**, which is the whole reason for its position:
centred instead, its rings run parallel to the seam and nothing ever crosses it, so a broken wrap
passes unnoticed — which is exactly what the first version of this probe did.

**`verify-civ-marks.mjs`** — does every empire crest the game will ask for actually exist, and does
the mark reach the map?

```
node tools/verify-civ-marks.mjs
```

The command-icon lesson applied to a second directory. `CivEmblem.Mask` builds a Resources path out of
the `Symbols` array and `Resources.Load` returns **null** for a name that is not there — not an
exception. So a crest renamed by one character takes the mark off every colony marker, every system
marker and every hull in the game, and the only symptom is an absence.

Both directions, like `verify-command-ui`: a name with no file is a missing mark, and a file no name
reaches is either a typo in the array or a crest nobody can pick. It also asserts that `CivMarkBadge`
is actually *attached* at both sites rather than merely existing — a badge nothing calls is exactly as
invisible as a missing PNG, with the added trap that it looks built — and that the player-only gate is
still in place, since the player's crest must never be drawn over a rival's ground.

**`make-script-metas.mjs`** — for a `.cs` file that has no sidecar **and no Unity anywhere to make one**.

> **Check whether an editor has already seen the file first.** A sidecar generated here for a script
> a working Unity install has already imported does not merge — it lands as *"untracked working tree
> files would be overwritten by merge"* and the pull aborts. That happened on 2026-08-22 for 27 files
> and the answer was to take them back out: **where an editor's sidecars exist, they win**, because
> they are the ones its Library database is already keyed to. This tool is for the other case.

```
node tools/make-script-metas.mjs            # report what is missing
node tools/make-script-metas.mjs --write    # create them
```

Unity identifies an asset by the GUID in its `.meta` sidecar, not by its path. A script without one is
not broken — the editor writes one on import — but every checkout that imports the project invents a
*different* GUID for the same file, and from then on they disagree about the identity of every script
in the list. Drag one onto a prefab and that reference is a GUID nobody else has. 27 had accumulated
by 2026-08-22.

The GUID is the **MD5 of the asset path**, so the tool is deterministic: two runs agree, and so do two
machines. It only ever writes a sidecar that is *missing*, never touches one that exists, and refuses
to write at all if a GUID it generated is already in use. Scripts only — art and meshes have importer
blocks whose settings matter, and inventing those is a different and riskier job.

**The sidecars in this repo come from the editor.** Every `.meta` under `Assets/` should be committed,
including the ones for the ship meshes, the command icons and the symbols: they carry real importer
settings, and a sidecar that is never committed is regenerated differently by every checkout. Commit
them from the machine that has Unity; do not generate them anywhere else.
