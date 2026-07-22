# Survey once, then three tiers of Deep Research

**Source:** Jacob, 2026-07-21.
**Status:** Planned, not started.

## What was asked

- A basic survey may be run **once** per world.
- "Deep survey" is renamed **Deep Research**, and it may also be run once.
- Empire progress unlocks **Deep Research II** and later **Deep Research III**.
- Each tier reveals more about a world. *(What exactly, was left to me — below.)*

## What exists now

| | Today |
|---|---|
| Basic survey | `explorationProgress` 0→1, driven by `UnitManager.Explore`. Already effectively once — `Explore` returns early on `b.Surveyed` — but nothing says so, and the UI keeps offering it. |
| Deep survey | `bool deepSurveyed`. **Repeatable**: `UnitInfoPanel` literally offers *"Deep Survey (again)"*, and `DoDeepResearch` re-runs. |
| What it gates | `SurfaceIndex.Unlocked`: Mineral needs a survey, **everything else** needs `deepSurveyed`. So one action unlocks five of the six overlays at once. |

That last line is the real design problem. Six indexes exist — Mineral, Heat, Fertile, Wind, Solar, Water
— and five of them arrive together in a single step. There is nothing left to earn.

## The ladder

The indexes are the backbone: each tier hands over the overlays that answer the question that tier is
for. Everything else at that tier is chosen to match that question.

### Stage 0 — Visited *(a ship has arrived)*
Name, type, size class, mass, orbit. The low-resolution surface map.
**The question it answers:** is this worth stopping for?

### Stage 1 — Surveyed *(once, any survey-capable ship)*
- Full terrain map and the detailed texture
- **Mineral index** — where the seams are
- Points of interest **located** (not yet identified)
- Bulk Metal / Energy / Water totals
- Habitability rating and the °C climate readout
- Unlocks claiming

**The question:** should I claim this? — mining and ownership.

### Stage 2 — Deep Research I *(once; available from the start)*
- **Heat index** and **Fertile index**
- Atmosphere thickness
- Biosphere status
- Whether the world has active plate tectonics
- The terraform diagnosis — what is actually wrong with it for your species

**The question:** where do I put things, and can this world be fixed? Heat and Fertile are precisely the
two that decide where a geothermal plant and a farm go, which is why they land together.

### Stage 3 — Deep Research II *(once; tech `deep_research_2`, Empire Level 4)*
- **Wind index** and **Solar index** — the two power-siting overlays
- Exact per-tile ore richness (Stage 1 shows *that* there is a seam; this shows how rich)
- Point-of-interest **contents** revealed before you excavate
- The terraform **ceiling** — how good this world could ever become
- Fault lines and earthquake risk

**The question:** how do I power and industrialise it, and is it worth the terraforming bill?

### Stage 4 — Deep Research III *(once; tech `deep_research_3`, Empire Level 7)*
- **Water index** — the last overlay
- Vael clue fragments become readable *(currently gated on `deepSurveyed`)*
- Anomalies and hidden features on the world
- Projected climate **after** each terraform project, rather than only the current one
- Subsurface deposits — ore that orbital and ground survey both miss

**The question:** what is left here that nobody else has found?

Rationale for the split: 1 overlay → 2 → 2 → 1. Every tier is a real capability increase, the two
"siting" tiers are the meaty ones, and the last tier is where the secrets live — which is what makes a
late-game research ship worth building rather than a formality.

## Slices

### 1. `researchLevel` replaces `deepSurveyed`
- [ ] `CelestialBody.researchLevel` (0–3, where 0 = surveyed only)
- [ ] `deepSurveyed` kept as `=> researchLevel >= 1` so the 15 existing call sites keep compiling, then
      migrated one at a time
- [ ] Save/load: old `deepSurveyed == true` loads as `researchLevel = 1`; new field written alongside

### 2. Once, and only once
- [ ] Basic survey states plainly that it is complete and is never offered again
- [ ] `UnitInfoPanel`'s "Deep Survey (again)" becomes the next available tier, or a reason it is not
- [ ] `DoDeepResearch` advances exactly one tier and refuses if the tier is already held or locked

### 3. Rename to Deep Research
- [ ] Every user-visible "deep survey" string → "Deep Research I / II / III"
- [ ] `SurfaceIndex.LockReason` says *which tier* unlocks the overlay being looked at

### 4. The gates
- [ ] Two tech nodes, `deep_research_2` (Empire 4) and `deep_research_3` (Empire 7), in the Science branch
- [ ] `SurfaceIndex.Unlocked` reads `researchLevel` per index rather than one bool for five of them

### 5. The reveals
- [ ] Each item in the ladder above actually gated at its tier — richness, POI contents, terraform
      ceiling, fault lines, clue fragments, projections, subsurface
- [ ] The Planet View's Survey tab shows the ladder: what you have, what the next tier gives, what it needs

## Notes

- The homeworld and birthright moons start fully surveyed; they should also start at max research level,
  or the capital will be missing overlays it has always had.
- `AncientClues.Reveal` currently fires on `deepSurveyed` — it moves to Stage 4, which makes the ten
  fragments a genuine late-game hunt rather than a side effect of an early ship order.
- Nothing here is compiled by the agent that writes it. Build before playing.
