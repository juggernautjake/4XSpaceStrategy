# Buildings, Cities and Society — design + slice plan

Filed 2026-07-20. Answers Jacob's request: more buildings and more kinds of them, tech gating,
per-planet conditions and caps, species-specific buildings and bonuses, resource requirements,
jobs and satisfaction, multiple cities, city stages, and an ecumenopolis end state.

---

## 0. What already exists (and the one structural problem)

There are **two parallel building systems** modelling the same idea:

| | Abstract colony facilities | Surface-grid structures |
|---|---|---|
| Enum | `BuildingType` (6) | `SurfaceBuildingType` (24) |
| Stored on body | `buildings` (`List<int>`) | `placedBuildings` (`List<PlacedBuilding>`) |
| Placement | none — abstract | tetromino footprints on x/y |
| Manager | `ColonyManager` | `SurfaceBuildManager` |

`ColonyFacilities` exists only to reconcile them. **This plan commits to the surface grid as the
real system** and reduces the abstract one to a compatibility shim. Reason: everything the request
asks for — placement conditions, adjacency, jobs, city growth, an ecumenopolis — is spatial, and
only the surface grid is spatial.

Three existing facts this plan is built on:

- `SurfaceBuildManager.OneOfEachPerWorld = true` (a `const bool`, explicitly labelled temporary) is
  the single biggest lever. It is what stops you building more of anything. **Slice 1 removes it**
  and replaces it with real caps.
- Power is already local, spatial and derived (`PowerGrid`), and works well. Jobs should copy its
  shape: derived per frame, never stored.
- `PlacedBuilding` is serialized *as itself*, so any field added to it persists automatically.
  Adding fields is cheap; removing them breaks saves.

---

## 1. Building caps — what replaces "one of each"

Three limits, in order of how often they bind:

1. **Land.** The grid is finite (mass-derived, 10x5 up to 1000x500). This is the real cap and needs
   no code — a tetromino either fits or it doesn't.
2. **Jobs.** A building needs workers (§4). Build more than your population can staff and the
   surplus runs at reduced output. Self-limiting, no hard refusal.
3. **Per-type cap**, only where fiction demands it:
   - `uniquePerWorld` — one seat of government, one surface shipyard. Unchanged.
   - **New: `perCityCap`** — buildings licensed by urban tier rather than by count. A world may hold
     `sum over settlements of (tier x n)` of a type. This is what makes founding a second city a
     *building* decision, not just a population milestone.
   - Everything else: unlimited, land- and job-bound.

**New field on `SurfaceBuildingInfo`:** `int perCityCap = 0` (0 = unlimited).

---

## 2. The building roster — from 24 to ~55

Categories stay (`Government, Harvesting, Industry, Military, Electrical`) plus **two new**:
`Society` (housing, health, culture, security) and `Orbital` (elevators, defence, yards).

New buildings, grouped by what they answer:

**Society (new category)** — the jobs/happiness layer the game currently has no buildings for.
| Building | Cells | Gate | Effect |
|---|---|---|---|
| Housing Block | 4 | — | +housing, no jobs |
| Arcology | 9 | X4 | large housing, needs power |
| Clinic | 4 | S2 | +satisfaction, reduces starvation |
| Hospital | 6 | S4 | as clinic, larger radius |
| School | 4 | S1 | +research, +growth |
| University | 9 | S5 | large research, needs population |
| Amphitheatre | 4 | — | +satisfaction radius |
| Holo-Arena | 6 | I4 | large satisfaction, high power |
| Garrison | 4 | W1 | +stability, suppresses unrest |
| Constabulary | 3 | W2 | +stability |
| Water Reclaimer | 4 | X2 | water on dry worlds |
| Atmosphere Processor | 9 | X5 | habitability over time |

**Harvesting** — Deep Core Mine (ore-gated), Gas Extractor (giants), Ice Harvester, Hydroponics
Dome (works with no fertile ground), Aquafarm, Fishery.

**Industry** — Foundry, Assembly Yard, Fabricator, Recycling Plant, Ore Enrichment (ore-gated),
Nanoforge (late).

**Electrical** — Geothermal Tap, Orbital Solar Receiver, Antimatter Plant (late), Grid Substation.

**Orbital (new)** — Space Elevator (unique, huge, needs a City underneath), Orbital Defence Array,
Mass Driver.

**Government** — Regional Capitol (second/third city seat), Planetary Administration (unlocks a
fourth+ city), Ecumenopolis Core (§6).

That lands at ~55 placeable types. Ordinals are append-only.

---

## 3. Gating — four independent gates

A building is placeable when **all four** pass. Each has its own player-facing reason string, so a
greyed card always says which one failed.

**a) Technology** — existing `requiredTech`. Currently 2 buildings gated; this plan gates ~35.
Mapped onto existing branches: `F` foundations/materials, `I` industry, `S` science, `X` expansion,
`W` warfare, `E` exploration. Roughly: tier 1 buildings ungated, tier 2 at empire level 2-3, tier 3
at 4-5, plus the Ancients branch for three exotic buildings.

**b) Empire level** — **new field `minEmpireLevel`**, mirroring `Tech.minEmpireLevel`. Stops a
level-1 empire building an arcology just because it stumbled onto the tech.

**c) Planet conditions** — **new struct `SiteConditions`** on `SurfaceBuildingInfo`:
```
minIndex          (exists) - the tile's index score
requiredOre       - a specific OreType must be present ON THIS BODY
minHabitability   - e.g. farms need a world that can grow things
maxHabitability   - e.g. some industry only on dead worlds
allowedBodyTypes  - gas giant / asteroid / moon restrictions
requiresAtmosphere
requiresWater     - currently declared and never read; wire it up
minPopulation     - a university needs somebody to teach
requiresAdjacent  - e.g. Refinery must touch a Mine
```
`requiredOre` is the first real link between the ore system and buildings, and it is the one that
makes the deep survey and the Mineral Index matter for construction rather than flavour.

**d) Resources** — costs stay metal+energy (adding a fourth `ResourceType` is a wide refactor —
`PlayerEconomy.Spend/CanAfford` take two hard-coded ints — and is deliberately **out of scope**).
Instead: **`requiredOre` gates access**, and ore richness scales output. Same design payoff, no
refactor.

---

## 4. Society: jobs, satisfaction, growth

Today: population grows off summed `popGrowthPerSec`; satisfaction is derived from eight factors;
surface buildings do **not** scale with population at all. Jobs do not exist.

**Jobs — new derived system, modelled on `PowerGrid`:** static class `Workforce`, computed per
world per frame, never stored.

- `SurfaceBuildingInfo.jobs` (new int) — how many population units a building wants staffed.
- `Workforce.Demand(b)` = sum of `jobs x LevelMult` over placed buildings.
- `Workforce.Supply(b)` = `population x EmployableFraction(species)` — most of the population works;
  a species trait shifts it.
- `Workforce.Ratio(b)` = clamp(supply/demand). Feeds output as a multiplier, exactly like
  `PowerGrid.PowerFactor`, with the same floor-not-zero rule (understaffed = 0.4, never 0 — a
  building that silently stops is an undiagnosable bug for the player).
- Unemployment (supply >> demand) is a **satisfaction penalty**, which is what makes industry
  buildings socially valuable and not just economically.

**Satisfaction** gains new factors: Employment, Health (clinics), Culture (amphitheatres),
Security (garrison vs. crowding), and Urban Blight (large cities without services).

**Growth** already reads satisfaction via `Population.BirthRate`. No change needed — the new
buildings feed it through existing paths, which is the point of not inventing a parallel system.

**Fix while here:** `ColonyFacilities.IsPower()` misses every Electrical generator, so
satisfaction's Power factor currently lies on a reactor-powered world.

---

## 5. Species

Today species affect growth and capacity but touch **nothing** about buildings, and `Species.iq`
has no mechanical effect at all.

**a) Per-species unique buildings.** New field `Species.uniqueBuildings` (array of
`SurfaceBuildingType`), and a new gate: a building with `speciesOnly` set is placeable only by that
species. One or two each, drawn from what already defines them:
- **Terrans** (intelligence) — *Grand Archive*: research scaling with number of colonies.
- **Aquarii** (fertility) — *Spawning Basin*: large growth, requires water adjacency.
- **Pyrothians** (durability) — *Magma Tap*: geothermal that works on hostile worlds, ignores the
  usual habitability floor.
- **Cryithn** (longevity) — *Cryo-Vault*: population never starves; huge housing, very slow growth.
- **Sylvans** (adaptability) — *Living Canopy*: habitability rises over time, no power draw.

**b) Per-species modifiers on ordinary buildings.** New table `SpeciesBuildingBonus`:
`(species, category) -> output multiplier`. E.g. Terrans +15% research buildings, Pyrothians +20%
industry, Aquarii +20% farms. This finally gives `iq` a job: it scales research-building output.

**c) Conditions interact with species.** Habitability is already species-relative, so a
`minHabitability` condition automatically means different worlds for different species — no extra
code, and it is the most interesting version of the mechanic.

---

## 6. Cities: stages, multiples, and the ecumenopolis

Today `CityGrowth` grows `Settlement -> Town -> City` organically, capped at 160 per world, with
`UrbanFraction` already labelling worlds up to "Ecumenopolis" cosmetically.

**The ladder becomes five tiers, and the top two are player achievements rather than drift:**

| Tier | Name | How reached |
|---|---|---|
| 1 | Settlement | grows |
| 2 | Town | grows |
| 3 | City | grows |
| 4 | **Metropolis** | grows, but only with services present (clinic + culture + security in radius) |
| 5 | **Arcology Block** | requires an Ecumenopolis Core on the world |

**Multiple cities** — a *city* in the founding sense is a **seat**: Colony Base, Regional Capitol,
Planetary Administration. Founding another requires, all at once:
- population above a threshold that scales with the number of existing seats,
- satisfaction >= 55 (an unhappy world does not expand),
- the tech (`X3` for the second seat, `X6` for the third),
- resources, and free land at distance from existing seats.

Each seat raises `perCityCap` allowances and extends the growth radius. That is the answer to
"do we just need satisfaction and resources and population" — yes, plus tech and land, and the
reward is *permission to build more*, not just a bigger number.

**The ecumenopolis.** When a world reaches `UrbanFraction >= 0.45`, population near its housing cap,
satisfaction >= 70, and at least three seats, the **Ecumenopolis Core** unlocks (unique, 3x3, very
expensive, late tech). Placing it:
- promotes every settlement to Arcology Block over time,
- converts the world's remaining buildable land to urban,
- multiplies housing capacity, collapses food production to near zero (the world imports),
- flips the planet's visual identity — a city-lights night side.

That last item makes the whole ladder legible at a glance from the galaxy view, which is what makes
it feel like an achievement rather than a stat.

---

## 7. Slices

Each slice compiles and ships on its own.

**Slice 1 — caps and the roster foundation.** Remove `OneOfEachPerWorld`; add `perCityCap`,
`minEmpireLevel`, `jobs`, `speciesOnly` to `SurfaceBuildingInfo`; extend `CanPlace` with the new
gates and their reason strings. No new buildings yet — this is the frame.

**Slice 2 — `SiteConditions`.** The condition struct, `requiredOre`, wiring `requiresWater`,
per-condition reason strings, and the Build-tab card showing *why* something is unavailable.

**Slice 3 — the Workforce system.** `Workforce` static class, `jobs` on every existing building,
output multiplier, unemployment satisfaction factor. Fix `ColonyFacilities.IsPower()`.

**Slice 4 — the roster.** ~30 new buildings across the new and existing categories, tech-gated per
§3, with costs and outputs balanced against the workforce multiplier from slice 3.

**Slice 5 — species.** `uniqueBuildings`, `SpeciesBuildingBonus`, five unique buildings, and giving
`iq` a mechanical effect.

**Slice 6 — city seats and multiple cities.** Regional Capitol / Planetary Administration, the
founding conditions, `perCityCap` allowances, growth radius per seat.

**Slice 7 — city stages.** Metropolis and Arcology Block tiers, service requirements for promotion,
`CityGrowth` changes.

**Slice 8 — the ecumenopolis.** Ecumenopolis Core, the conversion process, housing/food rebalance,
and the night-side city-lights visual.

**Slice 9 — save/load and balance.** DTO extensions, load-time backfills for every new field, and a
balance pass across the whole economy with the new multipliers in play.

---

## 8. Known risks

- **Balance is the real work.** Removing the one-of-each cap multiplies every economy number on a
  developed world. Slice 9 exists for this and should not be skipped.
- **`SurfaceIndex.statsCache` is keyed on `b.id`**, which `PowerGrid` documents as *not unique
  across a galaxy*. That is a latent overlay-percentile bug that new index-gated buildings will make
  much more visible. Fix it in slice 2.
- **`InspectorFacilityTabs` displays `popMult = 0.5 + population/200f` while the simulation uses
  `/25f`** — the UI already disagrees with the sim. Fix in slice 3.
- Adding a fourth `ResourceType` is explicitly out of scope; `requiredOre` covers the design intent
  without the refactor.
