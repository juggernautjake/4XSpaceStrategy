# Session Backlog — everything asked for, and where it stands

**Opened:** 2026-08-20
**Companion to:** `2026-08-19-ship-art-and-vfx.md` (the detailed workstream doc)

A flat, checkable list of every request made across one long working session, so nothing is lost in
the scrollback. The other doc explains the *reasoning*; this one is the ledger.

Legend: **[x]** done · **[~]** partly done / in progress · **[ ]** queued · **[!]** blocked

---

## A. The fleet — what must exist

### A1. The count
**145 civilization hulls** = 5 civilizations x 29 classes, plus a faction-neutral set.

The 29 classes, from `UnitType.cs` (the enum order is the canonical list — it is serialized in saves,
so it never gets reordered):

| # | Class | # | Class | # | Class |
|---|---|---|---|---|---|
| 01 | Scout | 11 | Mining Barge | 21 | Battle Station |
| 02 | Scout Mk II | 12 | Transport | 22 | Research Station |
| 03 | Scout Mk III | 13 | Terraformer | 23 | Relay Station |
| 04 | Explorer | 14 | Fighter | 24 | Supply Station |
| 05 | Probe | 15 | Fighter Mk II | 25 | Multi-Role Station |
| 06 | Research Ship | 16 | Fighter Mk III | 26 | Terraforming Station |
| 07 | Research Ship Mk II | 17 | Frigate | 27 | Deep-Space Station |
| 08 | Research Ship Mk III | 18 | Cruiser | 28 | Mega-Station |
| 09 | Science Vessel | 19 | Carrier | 29 | Hyper-Speed Relay |
| 10 | Colony Ship | 20 | Dreadnought | | |

Civilizations: **Terran, Aquarii, Pyrothian, Cryithn, Sylvan.**

### A2. The neutral set — asked for early, NOT yet generated
Specified in `tools/ship-generation-manifest.json`; 21 of these already exist as untextured Gen-1
meshes under `_Extras/` in the Downloads library.

- [ ] **Asteroids** — rocky small/large, metallic, icy, crystalline, volcanic, ore-rich, shattered,
      cometary, cluster (10 kinds, varying shape and size)
- [ ] **Derelicts** — broken freighter, gutted warship, dead station, frozen colony ship
- [ ] **Lost artifacts** — obelisk, orb, shard, codex, beacon, drive core
- [ ] **Ancient dormant civilization** — sentinel, warden, monolith, gate, vault, observatory
- [ ] **Enemy factions** — Marauder raider/corsair/brute/carrier/outpost, Swarm drone/ravager/hive

### A3. Per-hull identity — **[x] defined**, `tools/ship-design.json`
- [x] Each civ has an organising metaphor deep enough to name 29 distinct things
      (Aquarii cyborg sea creatures · Terran real aircraft and ships · Pyrothian foundry machinery ·
      Cryithn crystal and cathedral · Sylvan seeds, trees and fungi)
- [x] Aquarii bestiary includes the specifically-requested crab, lobster, sting ray, dolphin/whale,
      shark, jellyfish, sea snail and coral
- [x] Lineages escalate through one family — reef shark → hammerhead → megalodon; shrimp → lionfish →
      swordfish; ice shard → splinter-lance → glaive; winged seed → dart-pod
- [x] Tech ladder 1-5 (`techTiers`): later units carry visibly more armour, engines, weapons, lights
- [x] Size variance named per hull — a probe is a fist across, a mega-station a small moon

### A4. Generation progress
- [x] **Aquarii 29/29 — COMPLETE.** Every hull generated, downloaded in all five formats, decimated
      and imported. `verify-wiring.mjs` resolves 29/29
- [~] **Terran 4/29** — running. The two existing hulls (Scout, Fighter) were reviewed and KEPT:
      correct design language, on-palette steel-blue with orange trim, well textured. Every lineage
      chains off them
- [ ] **Terran 2/29** — started, then deprioritised to finish Aquarii first
- [ ] Pyrothian 0/29 · Cryithn 0/29 · Sylvan 0/29
- [x] **No longer blocked on hand-feeding tokens.** The open Meshy tab refreshes its own session
      cookie (`sb-auth-auth-token.*`); reading that back out of the tab and writing
      `tools/meshy-token.txt` keeps the batch fed without anyone pasting anything. A `msy_…` API key
      would still be simpler and never expire.

### A5. Downloads and formats — **[x]**
- [x] Every finished unit pulls down `.glb` (game), `.fbx` + `.obj` (DCC), `.stl` + `.3mf` (printing)
- [x] Plus concept art, Meshy render (the UI thumbnail), and albedo/normal/roughness/metallic
- [x] Nothing is deleted. `Art/AllModels/` keeps every generation in labelled folders, and
      `tools/meshy-archive-tasks.mjs` can re-pull everything from the Meshy account

### A7. Every design unique, suited to its job, true to its civ
- [x] Enforced by construction: the per-civ metaphor names a different thing for every hull, and the
      tech ladder makes each tier visibly more built-up than the last
- [x] **Two collisions caught before they were generated.** The Deep-Space Station was another
      JELLYFISH, which the Terraformer already is — it is now a deep-sea GLASS SPONGE lattice tower.
      The Hyper-Speed Relay was a ring of CEPHALOPOD ARMS sitting between the anemone ring and the
      kraken reef — it is now an OPEN five-armed BASKET STAR framing the aperture
- [~] Checked by eye per civ with `tools/contact-sheet.mjs` — tiling a whole civilization into one
      image is the only reliable way to spot two hulls that came back too similar. Done for the 16
      Aquarii landed so far (shrimp / lionfish / swordfish / sailfish / cuttlefish / octopus / squid /
      nautilus / turtle / crab / shark / hammerhead / megalodon / lobster / sawfish / manta — all
      distinct); must be repeated as each civilization completes

### A6. Quality control
- [x] `tools/verify-textures.mjs` — brightness, detail, and whether both livery accents landed
- [x] `tools/contact-sheet.mjs` — tile every render into one image for review
- [x] Thresholds calibrated against accepted art, not against the prompt's stated percentages
- [x] The Research Ship Mk II re-roll landed — it is no longer among the failures
- [ ] **Two Aquarii stations read dark**: Terraforming Station (0.125) and Mega-Station (0.122),
      against a ~0.15 floor. NOT re-rolled, and the call is deliberate: the whole station set scores
      below the ships because the checker was calibrated on hulls, and both look right beside their
      siblings on the contact sheet. Worth 84 credits only once every civilization has art at all —
      see the credit note in H

---

## B. Colour — player-selectable civ livery

- [x] **B1.** Palette per civ — `tools/civ-colors.json`. Base colour plus two accents, kept far apart
      in hue so the accents can be keyed out of a baked texture and recoloured
- [x] **B2.** Prompts place the accents on named surfaces (Meshy ignores percentages, follows places)
- [ ] **B3.** `tools/extract-color-masks.mjs` — key the two accent hues into an RGB mask
- [ ] **B4.** URP shader — recolour masked regions from the player's chosen primary/secondary
- [ ] **B5.** **Colours are chosen WHERE THE RACE IS CHOSEN.** Picking a civilization and picking its
      two colours is one decision made in one place, at the species screen, and the choice shows on
      the ships immediately. Persisted in the save
- [x] **B6.** Saturation is NOT uniform across civilizations and must not be forced to be. Aquarii art
      runs 64-85% coloured; Terran runs 12-17% and is *correct* — desaturated steel-blue with orange
      trim is that civilization's design language. Variety between civs is wanted. The texture check
      now only fails a hull that is genuinely UNPAINTED (under 4% coloured), never a pale one

---

## B-bis. The empire's identity — colours AND a mark, chosen together

- [x] **B7. Ten geometric symbols**, generated by `tools/make-civ-symbols.mjs` as two-region masks:
      red = primary, green = secondary, alpha = coverage. Chevron, Star, Delta, Orbit, Cross, Talon,
      Eye, Anvil, Sunburst, Shield. One 256px file per symbol serves every colour pair — pre-rendering
      instead would be 10 x 12 x 12 = 1,440 textures to ship and keep in step
- [x] **B8. `CivEmblem`** composites the chosen colours through those channels and caches the result
- [x] **B9. `CivLivery`** repaints the ships themselves: the accent hues the art was generated with
      are keyed by hue and replaced, keeping each pixel's own BRIGHTNESS so panel lines, rivets and
      weathering survive. A flat fill would leave two plastic stickers on a hull
- [x] **B10. The chooser**, on the new-game screen directly under the species list, with a LIVE crest
      and every symbol swatch drawn in the current colours. Tooltips throughout
- [x] **B11. Saved and loaded.** A save predating this comes back in its generated colours rather than
      in defaults nobody picked
- [ ] **B12.** The mark on WORLDS as well as ships — colony markers and the claim overlay
- [ ] **B13.** Rival empires get their own generated mark and colours, so the map is not one crest
- [ ] **B14.** Change colours and mark mid-game from an empire screen, not only at the start

## C. Ship VFX — built, then parked at request

All behind `ShipLights.Enabled` and `ProjectileRenderer.DynamicLights`, both **off** while ships are
shown on bare models. One flag each to bring back.

- [x] **C1.** Nav lights on a shared `FleetClock` — integer-ratio periods, quarter-beat phases, so
      different rates stay in rhythm
- [x] **C2.** Thrusters — tapered flame from billboards astern, grows out of the nozzle with throttle,
      speed-scaled, spool-up on departure and brake-down on arrival
- [x] **C3.** Muzzle flash fired from the same call that spawns the round
- [x] **C4.** Projectile point lights (pooled, capped at 14) + plasma pulse on the fleet beat
- [x] **C5.** Impact sound scaled by damage past armour
- [x] **C6.** Class badges removed — the silhouettes identify themselves now
- [ ] **C7.** Re-enable and tune once the fleet is in and hulls can carry it

---

## D. Getting art into the project

- [x] **D1.** Decimation pipeline — 0.40 GB → 7.7 MB for 16 hulls, ~12k triangles each
- [x] **D2.** `com.unity.cloud.gltfast` added (the fleet ships as `.glb`)
- [x] **D3.** Mesh assigned by **owner + role** — player flies the chosen species, other factions map
      by faction id; missing art falls back to the shared hull so civs can land one at a time
- [x] **D4.** Orientation manifest generated from measured bounds, so the whole fleet agrees on its
      axis instead of the heuristic deciding per mesh
- [x] **D5.** `tools/verify-wiring.mjs` — every path the C# will build, checked against disk
- [x] **D8.** All 25 finished Aquarii hulls imported: 0.74 GB of source art -> 12 MB in the project,
      ~12k triangles each. `verify-wiring.mjs` resolves 25/29 for Aquarii, all four starting classes
      among them
- [ ] **D6.** Confirm in Unity that `.glb` imports and `Resources.Load<GameObject>` resolves
- [ ] **D7.** Correct the bow/stern 180° flips by eye (F10 hot-reloads the manifest). Five hulls are
      flagged AMBIGUOUS where the two longest axes are within 15%

---

## E. Flight

- [x] **E1.** Burn / coast / brake easing — 18% accelerate, 22% brake. Arrival TIME unchanged
- [x] **E2.** Banking into turns, hardest at the start of a course change
- [x] **E3.** Turn rate scales with the angle left to cover
- [ ] **E4.** Verify in the running game that hulls fly nose-first and turn responsively

---

## F. Worlds and terrain

### Done
- [x] **F1.** Swamp worlds were mostly ocean — classifier tested `water >= 0.65` first. Drowned worlds
      now name themselves for their water; swamp means a walkable coastline
- [x] **F2.** Archipelago worlds guaranteed to have land left to break up
- [x] **F3.** Terrain yields 41/41 for minerals, fertility and wind shelter (was 1/41). Biggest hole
      was water fertility — ocean/lake/river/reef scored *less than tundra*
- [x] **F4.** Geothermal field visible under water (was multiplied below the draw floor)
- [x] **F5.** Verified water tiles already type as Ocean/Lake, not drowned land
- [x] **F6.** **Green grassland band on a frozen moon.** The equator carries +15°C over the world
      average, so on a moon just under freezing the belt computed above it and grew grass from edge to
      edge. Ice worlds now cap their melt band at tundra, and a world-average gate stops any globally
      frozen world growing lush ground however warm one belt gets

### Queued — from the latest screenshots
- [ ] **F7. Hydro index covers the water itself.** Currently the whole map floors at 70% and the
      *centres* — the actual lakes and seas — are blank. Fill centres to 90+. Intent: the shore and
      surrounding land should carry high hydro so a steam turbine can be built beside water rather
      than in it (building in water is impossible anyway)
- [ ] **F8. Frozen bodies of water generate no hydro at all.** Ice is still water in reach
- [ ] **F9. Continental plates: mass gate 0.85 to 4.0.** Stops small moons and asteroids growing
      continents. Must NOT gate volcanic activity, which is separate
- [ ] **F10. All plate lines visible in the Geothermal view.** The full red grid the old standalone
      tectonics overlay drew, with only certain stretches reading as high activity — convergent,
      shearing or divergent margins. One overlay, not two
- [ ] **F11.** Review the hairline's width (currently a full tile) and its red against ocean blue

### Queued — city generation places buildings on the right index
- [ ] **F12.** Farmland sited on high Fertility, better the higher the percentage
- [ ] **F13.** Steam turbines sited inside the Hydro index
- [ ] **F14.** If the capitol is not near water, place a **combustion plant instead**, sited on the
      **Mineral** index
- [ ] **F15.** General rule: every generated building lands inside the index it needs

### Queued — survey tab hides indexes a world does not have
- [ ] **F16.** No geothermal activity → no Geothermal index option at all (see screenshot 4 for the
      target state)
- [ ] **F17.** No fertility → no Fertility index option
- [ ] **F18.** Thin atmosphere, therefore no weather → no Weather index
- [ ] **F19.** Dev-mode toggle to unhide them, so terraforming can still reveal them later without
      the clutter sitting in the survey tab meanwhile

---

## F-bis. Terrain and generation — second screenshot round

### Biomes, elevation and the topographic view
- [ ] **F20. Biomes must not denote elevation.** On geothermal hotspots, Metallic Crust → Badlands →
      Canyon are currently reading as a rising elevation ramp (screenshot: concentric rings around a
      vent). Past a certain elevation it should simply be mountain terrain. A biome says what ground
      IS; how high it is, is a separate fact
- [ ] **F21. Volcano biome threshold 97% → 95%** on the geothermal index
- [ ] **F22. Black contour lines every 500 m.** A thin black border separating each elevation band —
      0, ±500, ±1000, ±1500, … in both directions — giving the planet map a topographic read. This is
      what replaces biomes-as-elevation
- [ ] **F23. Molten / lava worlds must remain possible.** Whatever the temperature rework does, the
      conditions that produce a molten world have to survive it

### Indexes
- [ ] **F24. Gas giants get NO indexes at all.** Remove them for now; gas giants need their own
      system later
- [ ] **F25. Hydro index — revised from F7.** The current result floors the whole map at 70% with a
      brighter ring at the shoreline, which reads as the index circling the water rather than showing
      it. Wanted: the **water source itself** carries a high value, the index **reaches a few tiles
      inland** as it already does, and the flat 70% everywhere goes away

### Solar system generation — scale, spacing and count
- [ ] **F26. Temperature range −270 °C to 1000 °C.** Bodies close to their star must be far hotter
      than they are now
- [ ] **F27. Habitable zones are too close in.** A G-type star's HZ sits far nearer than it should —
      Earth is the third planet and is not even at the centre of its own HZ
- [ ] **F28. Stop generating frozen worlds as the innermost planet**
- [ ] **F29. Far fewer planets per system.** Currently 8–10 around nearly every star. Raise the mass
      cost per planet/moon so systems spend out sooner; **1-planet systems should be possible**, and
      a full house should be rare
- [ ] **F30. Hard cap of 9 planets** per system, excluding asteroid belts and moons. A cap, not a target
- [ ] **F31. Lower default orbital inclination.** Most planets on a flat plane; inclined orbits rare
- [ ] **F32. Asteroid belts no closer than beyond the 3rd planet**, out to the furthest orbit —
      anything that small nearer in would have been swept up long ago
- [ ] **F33. Belt count: max 4, and 4 very rare.** 1 far more common, *if* a system gets one at all.
      A system that rolls fewer gas giants may roll more belts instead
- [ ] **F34. Moons: total mass ≤ ¼ the host planet's**, whether in one moon or split across them.
      Maximum 3 moons, not a requirement
- [ ] **F35. Gas giants and stars ×2 in rendered size**, with orbit placement and spacing adjusted so
      the system still reads correctly at that scale

## G-bis. Fleet command — asked for mid-session, planned in full

Its own document: `2026-08-20-fleet-command.md`. Summary of state:

- [x] **Squadrons** — control groups 1-9 now carry standing orders (formation, protocol, rally point,
      patrol route), saved with the game. Membership is EXCLUSIVE
- [x] **Roster verbs** — bind, add, detach, split into a free slot, disband
- [x] **Six formations** — Wedge, Line Abreast, Line Astern, Echelon, Screen, Globe, plus Free
- [x] **The screening rule** — slots handed out cheapest-first by what a ship costs to LOSE, so Screen
      and Globe put the expendable hulls between the enemy and the expensive ones
- [x] **Protocols** — Aggressive, Defensive, Hold Fire, Evade-and-Report, Escort, Withdraw-If-Hurt
- [x] **Patrol** — a standing route, loop or ping-pong, that re-issues itself until cancelled
- [x] **Local avoidance** — ships ease off station to keep clear and settle back; the cheaper hull
      yields; capped so nothing abandons its formation to avoid a graze
- [ ] **The UI** — squadron chips, formation menu with live diagrams, protocol menu, patrol tool,
      rally point, and a tooltip on every one of them
- [ ] Rally points for newly built ships, regroup, reinforce, the slowest-ship warning

## G. Fixed along the way

- [x] TextMeshPro console spam — `❚❚` (U+275A) is not in LiberationSans SDF and TMP logged a warning
      on every text rebuild. Now reads `PAUSED`, with `tools/check-ui-glyphs.mjs` as a tripwire
- [x] 5xx from Meshy retried with backoff (one transient error was killing a whole lineage)
- [x] Batch survives token expiry — parks, waits for a refreshed token file, resumes

---

## H. Standing constraints

- **Nothing here is compiled.** There is no Unity in this environment.
  `tools/Check-Scripts.ps1` is a structural tripwire, not a compiler. Build before playing.
- **Credits: ~4,105, and that is NOT enough for all five civilizations.** 117 hulls remain at ~42
  each = ~4,900. The shortfall is roughly 800 credits, or 19 hulls. On the current order — Terran,
  then Pyrothian, Cryithn, Sylvan — that lands four complete civilizations and about a third of
  Sylvan. Buying credits, or accepting that the last civilization flies borrowed hulls for now, is a
  decision for Jacob.
- **Everything is pushed to `origin/main`** as it lands.
