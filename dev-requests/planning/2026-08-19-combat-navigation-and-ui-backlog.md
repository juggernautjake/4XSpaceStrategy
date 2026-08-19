# Backlog — combat, navigation, UI, indexes

**Opened:** 2026-08-19
**Source:** Jacob's requests of 2026-08-19 (the combat/navigation/UI message, and the survey-index
message that followed it).

This is the live checklist. Nothing here is finished until it is ticked AND Jacob has seen it in a
build — none of it has been compiled (see `tools/Check-Scripts.ps1` for what *has* been verified).

Status key: **[ ]** not started · **[~]** in progress · **[x]** built, unverified in-engine · **[?]** blocked on a decision

---

## A. Combat

| # | Request | Status |
|---|---|---|
| A1 | Working projectiles | [~] |
| A2 | Projectile tracking/movement works well | [~] |
| A3 | Varying laser types and colours | [~] |
| A4 | Different weapon types (not just lasers) | [~] |
| A5 | Various explosion noises | [~] |
| A6 | Ship-destroyed noises | [~] |
| A7 | An explosion whenever a ship is destroyed | [~] |

**Built so far (A1–A7, unwired at time of writing):**
`Data/Weaponry.cs` — six weapon classes (pulse laser, beam laser, plasma cannon, railgun, missile,
point defence), each with its own colour, travel speed, turn rate, penetration and per-hull loadouts.
`Visual/ProjectileRenderer.cs` — pooled bolts and beams, rate-limited homing so missiles can miss.
`Visual/ExplosionRenderer.cs` — burst / impact / death at three scales, on a real cooling ramp.
`Systems/CombatManager.cs` — proximity engagement, threat-weighted targeting, armour vs penetration,
point defence, death and credit. `SimpleAudio` — six weapon cues and three explosion sizes.

**Still to do on this slice:** wire `Create()` into `GameBootstrap`, reset on galaxy change, show
ship health somewhere in the UI, and confirm hostility rules against the faction roster.

## B. Navigation and fleets

| # | Request | Status |
|---|---|---|
| B1 | Click a ship, then click a destination — make this flow work really well | [ ] |
| B2 | The dashed direction line: consistent, good-looking, correct | [ ] |
| B3 | Ships can travel as a group **or** as individuals | [ ] |
| B4 | Select several ships and assign them to a group (squads) | [ ] |
| B5 | Escort/guard: warships protecting transports, research vessels, anything | [ ] |

Notes: `ControlGroups` (Ctrl+1..9) already exists and covers much of **B4** — check what is actually
missing before building anything new. `FleetMovementController` already draws a dashed passive preview
and handles right-click sends; **B1/B2** are likely polish and consistency rather than new systems.
**B5** needs a real order kind (`Escort`) that keeps a ship near another and inherits its movement.

## C. Hostiles and encounters

| # | Request | Status |
|---|---|---|
| C1 | Some planets/asteroids have ancient defence systems | [ ] |
| C2 | Those trigger once a player ship interacts with the body | [ ] |
| C3 | Chance of probes existing in any given system, fightable | [ ] |
| C4 | Beating them drops loot | [ ] |
| C5 | Probes that randomly fly through a system and may activate and attack | [ ] |

Depends on **A** being wired first — all of these are things that shoot.

## D. UI and audio

| # | Request | Status |
|---|---|---|
| D1 | General UI improvements | [ ] |
| D2 | Condense what can be condensed | [ ] |
| D3 | Upgrade the sound effects | [~] |

**D3** is partly served by the combat audio above. **D1/D2** are deliberately vague — worth asking
Jacob which screens annoy him most rather than guessing, since "condense" could mean the Planet View
side panel, the Inspector's seven tabs, or the HUD.

## E. Survey indexes (2026-08-19, second message)

| # | Request | Status |
|---|---|---|
| E1 | Geothermal Index must show the continental plate lines again — the red border grids are gone while the push arrows still draw | [x] |
| E2 | Cause is likely the global 70% visibility floor; make an exception for this index | [x] |
| E3 | Lowest visible Geothermal value becomes **40%** — the plate-line grids | [x] |
| E4 | Shorten the Geothermal Index description — enough to understand its purpose, no more | [x] |
| E5 | Show text inside the Geothermal Index, in red, when the world has continental plates | [x] |
| E6 | Remove the Geothermal Index from the Survey tab entirely when the body has no volcanoes and no geothermal activity | [x] |
| E7 | Remove the Hydro/Water Index when the body has no water at all — restore it if terraforming adds water later | [x] |
| E8 | Remove the Fertility Index when there is no biosphere at all | [x] |
| E9 | **Remove, do not grey out.** They still exist for future use; they must not waste the player's time on a level-2 survey that can reveal nothing | [x] |

E1–E3 were one cause, and Jacob's diagnosis was right: `ShowFloor` (70%) answered both "what does this
ground yield" and "should this be painted", and a plate margin reads exactly 40. The two questions are
now separate — `SurfaceIndex.DrawFloor(kind)` is 40 for Geothermal and 70 for everything else, while
`ShowFloor` keeps deciding yield and buildability. The earthquake promise is untouched: quakes still
only damage structures on 70%+ ground.

E6–E9 also made the survey **shorter**. A level-2 sweep runs the indexes in order and spends real time
on each, so a dry, dead rock was spending a third of it mapping hydrology and farmland that cannot
exist. The running order is per-world now (`Survey.PresentCount` / `IndexSlot(b, k)`), so slots close
up: four usable indexes means each takes a quarter of the sweep instead of a sixth.

## F. Points of interest

| # | Request | Status |
|---|---|---|
| F1 | Stop spawning green **City** anomalies — deprecated. Cities are placed buildings on a body's surface grid now | [x] |

## H. Worlds and inhabitants (2026-08-19, third message)

| # | Request | Status |
|---|---|---|
| H1 | Probes may be **ancient-civilisation** machines **or space monsters** — give them an origin, not just stats | [ ] |
| H2 | New celestial body type: an **Infected world**. Water/organic-friendly at heart, overrun by an invasive parasitic infestation | [ ] |
| H3 | Infected worlds want a gnarly surface: ooze, tendrils, visibly diseased ground | [ ] |
| H4 | Occasionally a world carries a **dormant robot civilisation** | [ ] |
| H5 | Once woken it builds its own units and ships | [ ] |
| H6 | Fragile and easy to beat early | [ ] |
| H7 | Left alone long enough it researches tech, gathers resources, and becomes a real threat | [ ] |

Notes for H2/H3: `CelestialBodyType` is serialized **by ordinal** (`GameStateSerializer` writes the
enum as an int), so a new type must be **appended**, never inserted. It also needs: a
`TerrainColorMap` entry per new terrain, `TerrainTextureMap` grain generated to the same seamless
contrast ladder as the rest of the set (see `biome-tile-art` notes in `CODEBASE_GUIDE.md`), a
`WorldClassifier` route in, `Habitability`/`Terraformability` scoring, and an `AtmosphereRules` line.
The infestation is a good candidate for a real terrain family (ooze plains, tendril mats, spore
blooms) rather than a recolour.

Notes for H4–H7: this is a faction that grows, so it belongs alongside `FactionAI` rather than being
invented separately. The interesting design question is what "given enough time" is measured in —
`GameCalendar` years is the honest unit now that one exists.

---

## G. Carried over, blocked on Jacob

| # | Item | Needs |
|---|---|---|
| G1 | Grey-metal orbital station over the homeworld | Which station class? Every one carries a real passive aura (research +1.4, supply +0.9, terraform +4.5) and gates at Empire Level 2+. Either hand the player a level-2 bonus at turn one, or author a new neutral dock hull. |
| G2 | Fixed-footprint buildings (spaceport, shipyard, capitol, colony base) bypass the build queue | A `BuildScaling` balance call: routing a 9-tile spaceport through `PlaceDrawn` gives it cost ×10.8 and output ×12.6. |
| G3 | 3D globe draws terrain flat while the map draws it with biome grain | Memory budget: ~5 MB/body at 4 texels per cell, ~20 MB at 8, × ~20 bodies, reallocated ~1/sec while terraforming. |
| G4 | Liquid water at 4 atmospheres | The spec says both "up to 200 C" and "0 C to 144 C". Code took 144. Confirm. |
| G5 | Master plan C3 — verify framing on a 21:9 ultrawide | A running build; only Jacob can do it. |
