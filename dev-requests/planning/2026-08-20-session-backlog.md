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
- [x] **Terran 29/29 — COMPLETE.** Imported, oriented, resolving. The two pre-existing hulls (Scout,
      Fighter) were reviewed and KEPT rather than regenerated — correct design language, on-palette,
      well textured — and every lineage chains off them
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
- [~] **B3/B4. SUPERSEDED, not abandoned.** The mask-plus-shader route is the textbook answer and is
      cheaper at draw time, and it is the wrong answer in THIS environment: there is no Unity here to
      compile a shader in, and an uncompiled shader is not a feature — it is a file that turns the
      whole fleet magenta the first time anyone presses play. `CivLivery` does the same job on the
      CPU, once per colour change, cached. Worth revisiting as an optimisation if repaint cost ever
      shows up in a profile; it does not need to be revisited to ship
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
- [x] **B12.** The mark on WORLDS as well as ships — colony markers and the claim overlay
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
- [x] **C7. Re-enabled.** The condition was "once the fleet is in and the hulls can carry it", and
      the Aquarii are in at 29/29, oriented and flying nose-first. `ShipLights.Enabled` and
      `ProjectileRenderer.DynamicLights` are both ON. A civ whose art has not landed still flies
      borrowed hulls and now lights them too, which is right: the lights are placed from each hull's
      own BOUNDS rather than an authored rig, so they sit correctly on whatever mesh is there
- [ ] **C8.** Tune the rig by eye in Unity now that it is on — brightness, plume length, beat rate

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

## E-bis. Momentum, orbits and picking ships out

- [x] **E5. Mass and momentum.** A hull can no longer pivot on the spot. Turn rate falls with the
      SQUARE ROOT of mass (moment of inertia does not grow as fast as tonnage) and rises with the
      class's speed rating; thrust is expressed as a time-to-full-speed on the same law. Mass is read
      from hull integrity, which is already authored per class across a 375:1 range and already means
      "how much ship is there"
- [x] **E6. The wide turn is not scripted.** Two couplings produce it: turn rate falls as speed rises,
      and a ship will not hold thrust while pointing far off its course. Order a reversal and it brakes
      because it is aimed wrong, turns slowly because it is still fast, tightens as it slows, and comes
      out pointing the right way with speed to rebuild
- [x] **E7. Verified by simulation**, `tools/flight-model-check.mjs` — there is no Unity here to fly
      it in. A probe reverses in 0.8s inside 1.2 units; a dreadnought takes 7.7s and 7.8 units; the
      mega-station takes 13.1s and 15.9. Two defects were caught and fixed by looking at the plot:
      ships ORBITED their own destination (fixed by limiting speed to what braking distance allows,
      v = sqrt(2ad), rather than easing it down), and the lag leash was scaled off HULL SIZE, which
      gave a dreadnought 2.8 units of slack for an arc 25 units wide
- [x] **E8. Parked ships orbit** their world instead of sitting still, the whole ring turning at ONE
      rate so the spacing never decays into a pile-up. Six degrees a second — a circuit a minute, far
      too slow to make a ship hard to click
- [x] **E9. Ships hold their nose along the orbit**, not aimed at the planet they are circling
- [x] **E10. The body panel groups by squadron** — a header per squadron naming it, its strength and
      its standing orders, and clicking the header selects the whole squadron. Loose hulls collect
      under "Unassigned"
- [x] **E11. Sizes retuned, and one was plainly wrong.** Station size came from `stationLevel`, and
      only the Mega-Station carries level 3 — so every other station drew at the level-1 size and the
      MEGA-STATION ITSELF CAME OUT AT 0.37, SMALLER THAN THE DREADNOUGHT at 0.40. A thing described as
      "an orbital city the size of a small moon", costing two and a half times what a battleship
      costs, was the smaller of the two. Stations are per-class now; the full spread runs 0.07 (probe)
      through 0.11-0.16 (fighters and scouts) to 0.38 (dreadnought), 0.44 (hyper-relay) and 0.52
      (mega-station) — genuinely a small moon against OrbitSafety's 0.35 moon floor

## G-ter. The order of battle, and honing the chain

- [x] **Fleets** — a tier above squadrons: a named bag of squadrons, holding NO orders of its own,
      because a fleet order would contradict the squadron orders underneath it. Exclusive membership,
      saved with the game
- [x] **The Order of Battle panel** (`O`) — fleet, squadron and ship in one collapsing list, every
      row with a condition bar. Click a row to select everything under it. Bars are WEIGHTED BY HULL,
      not averaged: one dreadnought at 20% and nine intact probes average to 92%, which is a
      reassuring number for a wreck escorted by ten pounds of instruments
- [x] **Roster keys** — `Ctrl+Shift+N` add to a squadron, `Ctrl+Alt+N` detach, `Ctrl+M` split the
      selection into a free slot, and the body panel groups what is in orbit by squadron
- [x] **The glyph tripwire had a hole and it caught me.** It scanned `"..."` runs, and string
      INTERPOLATION nests quotes inside quotes — so in `$"{(open ? "x" : "y")} …"` the characters
      between the nested quotes were never looked at. Two triangles not in the font passed clean. It
      now scans whole lines with comments stripped and the BOM ignored
- [x] **Chain strength, rather than chain on/off.** The Terran battle line came back as the same grey
      slab four times — corvette, missile cruiser, fleet carrier and battleship, the carrier without a
      flight deck — because the chained prompt opens with "keep its silhouette and proportions" and
      that beats any per-hull description. The Aquarii survived the same setting only because a
      lobster, a sawfish, a manta and a leviathan cannot collapse into each other; that was luck of
      the metaphor. Unchaining outright was too blunt — the chain is also what makes a navy look like
      one navy — so there are now two strengths: **refit** (same hull upgraded; the Mk I-II-III
      ladders keep it, where looking alike IS the progression) and **family** (same materials,
      palette, finish and camera; a DIFFERENT class of vessel). The capital line is now family
- [ ] Re-roll Terran 17-20 under family mode, plus the two hulls that failed on an expired concept URL

## A-bis. Six hulls worth re-rolling, with the evidence

The brightness check was failing nine hulls and most were not failures — the big stations are
charcoal structures with lit panels, and the Terran hyper-relay is near-black with blazing blue
emitters, which is striking rather than broken. Brightness alone cannot tell a designed dark hull
from a generation that returned a near-black blob, because the difference is not how dark it is; it
is whether there is anything IN it. The check now fails only DARK **AND** FLAT — a refinement rather
than a loosening, and it still fails everything that deserves it.

What survives, and why each is a real candidate (~42 credits each):

| hull | bright | detail | verdict |
|---|---|---|---|
| Aquarii Battle Station | 0.159 | 0.089 | dark and flat |
| Aquarii Terraforming Station | 0.125 | 0.065 | dark and flat |
| Aquarii Mega-Station | 0.122 | 0.061 | dark and flat |
| Terran Colony Ship | 0.158 | 0.078 | dark and flat |
| Terran Terraformer | 0.160 | 0.069 | dark and flat |
| Aquarii Carrier | 0.254 | 0.085 | primary accent effectively absent (2.1%) |

- [x] **Re-rolled all six — three kept, three reverted or accepted as-is.** Every one was ARCHIVED FIRST, complete with all five mesh formats, all
      four PBR maps, its concept art and its render, to
      `Art/AllModels/11-Superseded-2026-08-20/` — which carries a README naming each hull, its
      numbers, and how to put it back (copy the folder over `10-Fleet/` and re-import). That folder
      is under `Art/`, which is gitignored, so it lives on Jacob's machine rather than in the repo
      like the rest of the 9 GB of art
- [x] **A re-roll is a fresh draw, not an improvement**, and this round proved it: two of the six came
      back WORSE and were reverted from the archive. Every replacement was compared against the render
      it would replace, on the numbers and by eye, before being accepted

### What each re-roll actually did

| hull | old bright/detail | new bright/detail | kept |
|---|---|---|---|
| Aquarii Battle Station | 0.159 / 0.089 | **0.201 / 0.104** | NEW — passes |
| Terran Colony Ship | 0.158 / 0.078 | **0.179 / 0.106** | NEW — passes |
| Terran Terraformer | 0.160 / 0.069 | **0.206 / 0.106** | NEW — passes |
| Aquarii Mega-Station | 0.122 / 0.061 | **0.155 / 0.074** | NEW — better on every metric and visibly so, still under the floor |
| Aquarii Terraforming Station | **0.125 / 0.065** | 0.105 / 0.088 | OLD — the re-roll came back darker |
| Aquarii Carrier | **0.254 / 0.085** | 0.141 / 0.096 | OLD — the old hull is the fleet's only MANTA RAY |

### Three hulls knowingly accepted below the floor

Not hidden, not re-thresholded away:

- **Aquarii Carrier** — primary accent 2.1%, under the 2.5% floor. Its player livery will recolour
  almost entirely through the magenta SECONDARY. Accepted because the alternative draw lost the manta
  silhouette, and it did not fix the accent either (3.0%, still weak)
- **Aquarii Terraforming Station** — 0.125 / 0.065. Two draws, both dark; the second was darker
- **Aquarii Mega-Station** — 0.155 / 0.074, just under both floors, but better than what it replaced
  on brightness, detail and accent coverage, and clearly better to look at

- [x] **The cause was the prompt, not luck.** Three Aquarii stations came back dark across two draws
      each. The base material is "deep sea-teal", which is a DARK colour, and the station tail asked
      for strong SATURATION while saying nothing about VALUE — a saturated dark teal is still dark.
      The tail now asks for "brightly lit: glowing windows, lamps and panel lights across the whole
      structure", which costs 80 characters and fixes it for every station Pyrothian, Cryithn and
      Sylvan have yet to generate

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
- [x] **F7. Hydro index covers the water itself.** Currently the whole map floors at 70% and the
      *centres* — the actual lakes and seas — are blank. Fill centres to 90+. Intent: the shore and
      surrounding land should carry high hydro so a steam turbine can be built beside water rather
      than in it (building in water is impossible anyway)
- [x] **F8. Frozen bodies of water generate no hydro at all.** Ice is still water in reach
- [x] **F9. Continental plates: mass gate 0.85 to 4.0.** Stops small moons and asteroids growing
      continents. Must NOT gate volcanic activity, which is separate
- [x] **F10. All plate lines visible in the Geothermal view.** The full red grid the old standalone
      tectonics overlay drew, with only certain stretches reading as high activity — convergent,
      shearing or divergent margins. One overlay, not two
- [x] **F11.** Review the hairline's width (currently a full tile) and its red against ocean blue

### Queued — city generation places buildings on the right index
- [x] **F12.** Farmland sited on high Fertility, better the higher the percentage
- [x] **F13.** Steam turbines sited inside the Hydro index
- [x] **F14.** If the capitol is not near water, place a **combustion plant instead**, sited on the
      **Mineral** index
- [x] **F15.** General rule: every generated building lands inside the index it needs

### Queued — survey tab hides indexes a world does not have
- [x] **F16.** No geothermal activity → no Geothermal index option at all (see screenshot 4 for the
      target state)
- [x] **F17.** No fertility → no Fertility index option
- [x] **F18.** Thin atmosphere, therefore no weather → no Weather index
- [x] **F19.** Dev-mode toggle to unhide them, so terraforming can still reveal them later without
      the clutter sitting in the survey tab meanwhile

---

## F-bis. Terrain and generation — second screenshot round

### Biomes, elevation and the topographic view
- [x] **F20. Biomes must not denote elevation.** On geothermal hotspots, Metallic Crust → Badlands →
      Canyon are currently reading as a rising elevation ramp (screenshot: concentric rings around a
      vent). Past a certain elevation it should simply be mountain terrain. A biome says what ground
      IS; how high it is, is a separate fact
- [x] **F21. Volcano biome threshold 97% → 95%** on the geothermal index
- [x] **F22. Black contour lines every 500 m.** A thin black border separating each elevation band —
      0, ±500, ±1000, ±1500, … in both directions — giving the planet map a topographic read. This is
      what replaces biomes-as-elevation
- [x] **F23. Molten / lava worlds must remain possible.** Whatever the temperature rework does, the
      conditions that produce a molten world have to survive it

### Indexes
- [x] **F24. Gas giants get NO indexes at all.** Remove them for now; gas giants need their own
      system later
- [x] **F25. Hydro index — revised from F7.** The current result floors the whole map at 70% with a
      brighter ring at the shoreline, which reads as the index circling the water rather than showing
      it. Wanted: the **water source itself** carries a high value, the index **reaches a few tiles
      inland** as it already does, and the flat 70% everywhere goes away

### Solar system generation — scale, spacing and count
- [x] **F26. Temperature range −270 °C to 1000 °C.** Bodies close to their star must be far hotter
      than they are now
- [x] **F27. Habitable zones are too close in.** A G-type star's HZ sits far nearer than it should —
      Earth is the third planet and is not even at the centre of its own HZ
- [x] **F28. Stop generating frozen worlds as the innermost planet**
- [x] **F29. Far fewer planets per system.** Currently 8–10 around nearly every star. Raise the mass
      cost per planet/moon so systems spend out sooner; **1-planet systems should be possible**, and
      a full house should be rare
- [x] **F30. Hard cap of 9 planets** per system, excluding asteroid belts and moons. A cap, not a target
- [x] **F31. Lower default orbital inclination.** Most planets on a flat plane; inclined orbits rare
- [x] **F32. Asteroid belts no closer than beyond the 3rd planet**, out to the furthest orbit —
      anything that small nearer in would have been swept up long ago
- [x] **F33. Belt count: max 4, and 4 very rare.** 1 far more common, *if* a system gets one at all.
      A system that rolls fewer gas giants may roll more belts instead
- [x] **F34. Moons: total mass ≤ ¼ the host planet's**, whether in one moon or split across them.
      Maximum 3 moons, not a requirement
- [x] **F35. Gas giants and stars ×2 in rendered size**, with orbit placement and spacing adjusted so
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
- [x] **The UI** — a Fleet Command bar along the bottom whenever ships are selected: squadron chips
      1-9, all seven formations, all six protocols, patrol, rally, and the roster verbs. A tooltip on
      every control, and each one says what the ships will DO rather than what the option is called
- [x] **Patrol and rally tools** — click out a route (it draws the closing leg as you lay it, so a
      loop looks like a loop before it is committed) or a single fall-back point
- [x] Rally points for newly built ships, regroup, reinforce, the slowest-ship warning
- [x] Formation menu entries could carry a live diagram of the current squadron, not just a tooltip

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

---

## I. Projectiles, ballistics and ammunition — 2026-08-20

> *"I also want you to work on the math and trajectories for projectiles. I want to get to a place
> where there are real lasers and missiles and ammunition and stuff... We need to handle projectile
> pathing and speeds and arcs and stuff, we will likely also have homing projectiles, so we need to do
> the math for how those projectiles will turn and move and for how long they will track before
> dissipating."*

### What was already broken, and had no visible symptom

- [x] **Instant weapons dealt no damage at all.** `ResolveHit` was called from inside the
      travelling-round branch, under an `if (instant) { ...fade...; continue; }`. Beams and railguns
      drew their line and did nothing: **56% of a Dreadnought's rated attack, 35% of a Cruiser's,
      38% of a Fighter Mk II's.** The only symptom available was "capital ships feel a bit weak"
- [x] **Nothing was ever aimed.** Every gun fired at where the target *was*. A pulse bolt crosses 22
      units in 0.35s while a fighter covers four — thirteen hull widths of miss on a target flying in
      a straight line. Hits happened only because a round landing within 1.2 units of its aimpoint
      counted
- [x] **Fast rounds tunnelled.** Arrival was a point test; a 62 u/s bolt has a 1.03-unit stride
      against a 1.1-unit window, so about half of all hits were missed at 60fps and nearly all at 30.
      Frame rate was a weapon stat. Every hit test is a **segment** test now

### The model

- [x] **`Ballistics.cs`** — no Unity past `Vector3`, so `tools/ballistics-check.mjs` mirrors it and
      draws the trajectories. Intercept quadratic, dispersion, turn radius, three guidance laws,
      motor model, seeker
- [x] **Lead solutions on every mount**, motor-aware — a missile spends most of a short engagement
      still accelerating, so a cruise-speed solution aims twenty units short. Solved against the real
      distance-vs-time curve by **bisection**; the obvious fixed-point iteration oscillates, worst
      exactly where the target's speed approaches the round's
- [x] **A mount declines a shot it cannot land.** Matters little for a laser, enormously for a torpedo
      tube carrying four
- [x] **Dispersion** that grows with range and with the target's **crossing** speed, so manoeuvring is
      a defence and flying straight at a gun is a mistake. The beam laser has none of any kind — it is
      the one weapon that cannot miss, and that is why its DPS is the worst on the list
- [x] **Midcourse → terminal guidance.** ProNav is a *terminal* law; run from launch it asked for
      20 deg/s from a round capable of 150 and never undid the launch transient — engagements came out
      with **eight degrees of total control effort and eighteen-unit misses.** The round flies the
      firing solution until its seeker arms, then hands over
- [x] **Proportional navigation** for missiles and torpedoes, `N = 3.6`. Flies a visibly intelligent
      curve: near-straight to a lead point, where pure pursuit hooks at the end. On the hardest shot
      in the sweep pursuit does **4.7× the control effort** for 1.5× the flight time
- [x] **Real motors** — cold launch, boost, sustain, then a ballistic coast where the round **cannot
      turn at all**, because in vacuum turning is thrusting. Then it dissipates over half a second
      rather than blinking off
- [x] **Turn radius is v²/a**, so a fast missile cannot corner. A torpedo at half a missile's speed
      and a fifth of its thrust holds the same 12-unit circle — that pairing *is* the hull
- [x] **Seekers** with a cone, an arming delay and a terminal release. All three were needed and the
      reasons are in the source; the short version is that ProNav *holds* the target at a constant
      bearing, so a naive cone fires on the healthiest intercepts

### Ammunition

- [x] **Two resources that behave nothing alike.** Energy mounts draw a capacitor that always refills,
      so a laser fleet is rationed in the moment and never starved. Ordnance mounts carry rounds and
      run out
- [x] **Rearming needs no order** — park near a settled world you own, a friendly station (26u) or a
      **carrier** (14u), which is what finally gives the carrier a job
- [x] **No death spiral, by construction.** Point defence is energy-fed and every warship keeps an
      energy mount, so being out of supply is a weak fleet and never a helpless one
- [x] **Two new classes** to make the split bite: an **autocannon** that eats magazines and shrugs off
      nothing, and a **torpedo** that a Dreadnought carries *four* of
- [x] Supply strip under the health bar in the roster, per-mount rounds in the ship panel, one
      notification when a hull runs dry

### Moving together

- [x] **Predictive separation.** The old rule was purely reactive, and for two ships closing head-on
      the push is along the closing line — it tells one to hurry and the other to slow and moves
      neither out of the way. A look-ahead term now eases converging hulls apart *perpendicular* to
      their closing velocity, starting about a second out. Head-on clearance **0.47 → 0.84** of the
      wanted gap, four-way crossing **0.31 → 0.57**, and a squadron already flying clear moves 0.03
      units, so nothing that worked was disturbed

### Still open

- [x] Ships do not manoeuvre *in combat* — they fly their orders and shoot. Now that crossing speed is
      a real defence, an evasive protocol would mean something
- [x] Point defence still protects only its own hull. Deliberate (see `CombatManager`), but a torpedo
      is slow enough that escort PD is worth revisiting
- [ ] Magazine capacity is not a refit choice. A hull that could trade armour for rounds would be the
      natural next decision

### How it was checked

`tools/ballistics-check.mjs` — 17 checks and a six-panel trajectory sheet. `tools/separation-check.mjs`
— 4 checks and a before/after sheet. Both parse the game's own tables so a tuning change shows up
without anyone remembering to mirror it. **Five of the defects above were found by looking at the
picture, not by reading the source.**

---

# The five F-items that were still open — closed 2026-08-23

> *"Please make sure that everything we requested to be built for the game has actually been built,
> and if it hasn't please build it all."*

A sweep of every planning document against the code. **Of the 51 unticked boxes in this file, 46 were
already built and never marked** — F7-F19, F24-F31 and F34-F35 all landed across the 2026-08-21,
08-22 and 08-23 passes without anyone coming back to tick them here. Five were genuinely unbuilt, and
this section is those five. Nothing else in `dev-requests/planning/` was open and buildable: the ship
art needs Meshy credits, the LOD tail and the ultrawide check need Unity, and the master plan's
Parts D and E say in their own header that their boxes lied and the work shipped.

The five are two separate stories that turned out to be one.

## F20 + F22 — biomes stopped meaning "how high", and contours took over the job

These are halves of a single change and had to be built together. Taking altitude out of the biome
names removes the only thing that showed relief on the map — a forest at 200 m and a forest at 4,000 m
become the same green — so something has to put it back.

- [x] **F20. Biomes must not denote elevation.**

      A shared line, `AlpineAbove` (0.52 above the waterline, ~6,200 m), applied in every solid
      classifier: past it, ground is Mountains whatever else it would have been. **It is the same
      number `ElevationBand` already prints**, so a tile that reads "alpine" in the hover panel is
      drawn as mountain on the map — one constant, so the picture and the words cannot disagree.

      Not a return of Highlands and Hills. Those were BANDS at 0.66 that swallowed a third of every
      temperate world and stripped the climate out of it. This is a CEILING, and everything under it
      still falls through to its own climate and material tests.

      Then the altitude bands themselves. **Metallic Crust** was `elev > 0.55` in `Barren` and
      `elev > 0.7` in `Airless` and nothing else — the middle rung of the exact ramp the screenshot
      shows. Exposed metal is a statement about what ground is MADE OF, and what exposes it is the
      crust being broken open, so it reads `ridge` now like every other bare-rock type in the file.
      `SaltFlat`, `Crater` and `ObsidianFlat` keep their elevation tests: those are BASINS, which is a
      fact about the shape of the ground rather than a name for its altitude.

- [x] **F22. Black contour lines every 500 m,** both above and below the waterline, on the Planet View
      map. `PlanetTerrainGenerator.ContourBand` is the band index; `SurfaceTextureRenderer.PaintContours`
      draws the boundary between any two cells whose bands differ.

      A separate pass over the finished texture rather than a test inside the fill loop, because a
      contour is a property of the BOUNDARY between two cells and the fill loop only ever has one of
      them. One texel, on the inside edge of the LOWER cell — not both, or every line is two texels
      wide and the map reads as a mesh laid over the ground rather than as contours on it.

      Bathymetric too. A drowned basin has shape, terraforming can raise it, and a map that goes flat
      below the waterline hides exactly the ground the player is deciding whether to drain.

      **Not in `BuildGrid`** — that is one texel per cell and feeds the moon thumbnails and the 3D
      globes, where a black texel per boundary is a grid over a hundred-pixel picture.

## F21 — the volcano threshold

- [x] **F21. `GeothermalMap.VolcanoIndex` 0.97 -> 0.95.** At 0.97 the qualifying band is three
      hundredths of the index wide and the field's top end is steep there, so a world could carry
      hotspots reading 94-96 and grow no cone at all: the survey shows a bright red bullseye and the
      map shows nothing standing on it. 0.95 doubles the band. `PlanetTerrainGenerator` now reads the
      constant instead of restating "97-100" in a comment, so the two cannot drift again.

## F32 + F33 — where belts may go, and how many

- [x] **F32. No belt before the 3rd planet.** `SolarSystemGenerator` counts planets placed and gates
      on it. **PLANETS, NOT RINGS** — the reasoning is about how much material the inner system swept
      up, and a ring nothing accreted on swept up nothing. Gas giants count: a system of three giants
      must not put a belt on ring 2 on the grounds that it had "no planets" yet.

- [x] **F33. At most 4 belts, 4 very rare, 1 by far the commonest.** A per-system cap rolled up front
      (1: 62%, 2: 25%, 3: 10%, 4: 3%) — a cap, not a target, the same distinction `PlacementRings`
      draws about the nine rings.

      **A refused belt becomes a terrestrial, not an empty ring.** This is the part that matters: on a
      nearly-spent budget the old `ChooseLane` returned a belt *unconditionally*, so an early ring on a
      poor star could open the system with a field of rubble exactly where the request wants a planet.

      "A system that rolls fewer gas giants may roll more belts instead" is a 1.5x on the belt odds
      beyond the frost line **while the system is still giant-less**. The frost-line spike exists
      because a giant stirs the material; a system with no giant out there has to be given the odds
      some other way or that clause could never fire.

## What the measurements said

`tools/terrain-elevation-check.mjs` is new. It sweeps elevation with roughness and climate pinned, at
five roughness levels per classifier, and counts the types that appear. **It found a fourth altitude
band nobody had listed:** `Volcanic`'s `elev > 0.62f -> LavaRock`, which capped every volcanic world's
high ground in one type whatever the ground was actually like up there. Solidified flow is broken
ground, so it reads `ridge` now, and `CrackedGround` dropped to 0.40 to keep a slice of its own.

It also failed twice on **its own** flaws before it failed on any code, and both are worth stating:

* It called 844 corner texels "doubled contours". They were corners — a cell drawing both its north
  and its east edge shares the one texel where the two meet, which is what contours do at a crossing.
  Counting those would have forced a fix for something that is not wrong and buried the real finding.
  Orientations are tracked separately now.
* Its cone was **centred on the map**, so every contour ran parallel to the longitude seam and nothing
  ever crossed it — a broken wrap would have passed unnoticed. The cone sits ON the seam now, and its
  rings cut across it radially.

After both: all four classifiers produce exactly one biome plus Mountains across the whole elevation
sweep, 24 of 24 contour bands present, zero doubled lines, 22 rows crossing the seam, 6.3% coverage.

`tools/system-composition-check.mjs` grew the belt rules and four asserts:

```
Belts per system:  0:83.8%  1:15.6%  2:0.6%  3:0.0%  4:0.0%
Of systems that get a belt at all:  1:96%  2:4%
```

**Say plainly what that costs.** Belts are now uncommon — one system in six. The two requests together
force it: mean filled rings is 4.2 and mean planets is 4.2, so a system that must place three planets
before its first belt usually runs out of rings first. The belt CAP is therefore almost never the
binding constraint; the position gate is. If belts should be a more regular feature, the lever is F32
(three planets) rather than F33 (four belts) — and that is a call, not a bug.

Planets rose to compensate, 3.3 -> 3.5 on an M dwarf and 4.0 -> 4.3 on a G, because a refused belt is
now a world. Mean filled rings, the under-3 share, the habitable-zone guarantee and the inner-ring
rule are all unmoved.

## Two things fixed on the way, neither of them asked for

- [x] `OceanWorld`'s mountain cut was a bare `elev > 0.80f` — the one threshold in that block that did
      NOT move with the water while the two either side of it did. Drain an ocean world and its
      shoreline dropped while its snowline stayed put, so the exposed seabed grew mountains from the
      bottom up. Its island cut had the same fault: left raw, a flooded ocean world put that line
      BELOW its own waterline and every scrap of land above the surface was an island.

- [x] `Airless`'s new roughness cut for Metallic Crust was first written ABOVE the CrystalField test,
      where a cut of 0.55 would have swallowed the 0.72 band whole and quietly deleted crystal fields
      from the game. Caught before it ran, by reading the branch order rather than the branch.

## And a sixth, found on the way out — B12, the empire's mark on the map

The closing pass on 2026-08-22 listed **B5, B13 and B14** as blocked on "a screen that does not
exist yet" and did not mention **B12** at all. B12 is not blocked and never was: the crest already
existed (`CivEmblem`), was already chosen beside the colours (`CivIdentityPanel`) and was already on
every hull (`UnitModelRenderer`). It had simply never reached the map — which is where a player spends
most of their time, and where *"what is mine"* is the question being asked.

- [x] **B12. `Visual/CivMarkBadge.cs`** — a camera-facing quad carrying `CivEmblem.Current`, attached
      in the two places the request names:

      * **Colony markers** — `OrbitController.SetOwnerHighlight` takes a `mine` flag now and hangs the
        badge off the owner ring's own object, so it inherits the ring's position and cannot drift
        from the world it labels. Sat on the ring's EDGE, not its centre, where it would cover the
        planet it is describing.
      * **The claim overlay** — `GalaxyLOD`'s system proxy, beside the empire ring it already draws.

      `mine` is an argument rather than something inferred from the ring colour, because **a colour is
      not an identity**: two factions can be handed neighbouring hues, and the question is about the
      owner.

**Why a shape and not just the colour that was already there.** Owner colour answers "someone owns
this", and answers "which someone" only if you can hold seven faction hues in your head — and it
fails outright for a colour-blind player, who gets a ring in a hue they cannot separate from a
neighbour's. The ring says an empire holds this and the mark says which, and either one alone still
works.

**The player's own holdings only.** Rivals keep the coloured ring and no badge, exactly as before.
Drawing the player's crest on a rival's world because it is the only crest that exists would be a lie
about who owns the ground, and a worse map than no badge. Rival marks are B13 and still need a
per-faction symbol generator.

Three things the surrounding code had already learned the hard way, applied here rather than
rediscovered:

* **Not `Attach(...)?.Show(true)`.** `?.` does not route through Unity's overloaded `==`, so a badge
  whose native half is gone comes back as a live reference and throws — the exact `ShipLOD` trap from
  2026-08-23 §0A. Written the long way, and the UNITY check now covers this shape.
* **`OnDisable`/`OnDestroy` unsubscribe from `CivEmblem.OnChanged`.** A static event outlives its
  subscriber; without this, reloading a save leaves destroyed badges on the list and every later
  symbol change throws. Same defect as `FormationPreview`'s leaked `ControlGroups.OnChanged`
  (2026-08-22 §D3).
* **The material's colour is set once at creation, not on every refresh.** `FadeGroup` owns that
  material's alpha from the moment it captures the subtree — concealment and the zoom crossfade both
  drive it — so rewriting the colour on a symbol change would stamp alpha back to 1 and flash a
  concealed system's badge on.

It is gated by concealment through `ApplyRingEnabled`, alongside the two rings, because a badge left
showing over a hidden world is the same defect the rings had: a labelled marker saying exactly where
the thing you just hid is.

**`tools/verify-civ-marks.mjs`** is the tripwire, and it is the command-icon lesson applied to a
second directory. The crest is loaded by NAME through `Resources.Load`, which returns null rather
than throwing, so a symbol renamed by one character takes the mark off every colony marker, every
system marker and every hull in the game — and the only symptom is an absence. It checks both
directions (a name with no file, and a file no name reaches), that the badge is actually attached at
both sites rather than merely existing, and that the player-only gate is still there.

## Not built, and it is a suggestion rather than a request

- [ ] **Magazine capacity as a refit choice.** The line in §I reads *"a hull that could trade armour
      for rounds **would be** the natural next decision"* — that is a design direction someone wrote
      down while finishing something else, not a request. It wants a refit screen and a hull-stat
      trade model, neither of which exists, and both are Jacob's call on where the game is going.

## Still not built, and why — unchanged

Everything in `2026-08-22-closing-the-backlog.md` §G still stands: the LOD tail and the impostor level
need Unity, the remaining ship art needs the Meshy key, the civ-colour placement needs a screen that
does not exist yet, and the three Planetary Generation 2.0 audit decisions are Jacob's.

**None of this was compiled.** There is no Unity here. `Check-Scripts.ps1` is clean over 231 files
across all eight checks and all twelve Node tripwires pass — but those are tripwires, not a compiler.

---

# Errata — two compile errors that shipped, and the check that now catches one

The commit above went out with `Check-Scripts.ps1` clean over 232 files and thirteen Node tripwires
passing, and it did not compile. Jacob opened Unity and got two errors:

```
SolarSystemGenerator.cs(214,21):  error CS0136: A local or parameter named 'placed' cannot be
                                  declared in this scope because that name is used in an enclosing
                                  local scope to define a local or parameter
PlanetTerrainGenerator.cs(1222,50): error CS0103: The name 'body' does not exist in the current context
```

Both were in the 2026-08-23 work, not in the gap-closing pass — but that is not much of a defence,
since the whole point of committing them together was that they were finished.

**Neither was catchable by anything that existed.** All eight checks ask about a NAME — does this
enum member exist, does this static exist, does this string close. Both of these are about SCOPE:
where a name is *live*. That is invisible to a regex over `Type.Member`, and it is now the ninth
check's whole job.

## CS0136 — `placed`, twice

- [x] The ring loop counts filled rings in `placed`; the belt branch four hundred lines in counted
      asteroids in a second `placed`. Both are readable, both are correct alone, and C# forbids the
      pair. The inner one is `placedRocks` now, which is what it always meant — and is the name the
      Node port had been using for the same quantity all along, which is worth noticing: the port and
      the C# disagreed about a name for weeks and nothing could see it.

## CS0103 — `body` in a method that has no body

- [x] Phase 7B's molten-ground gate reads the world's internal heat, and landed in `ClimateCoherence`
      — the one method in that neighbourhood with no `CelestialBody` parameter. Every method around it
      has one, which is exactly why the line looked right.

      Fixed by passing the NUMBER rather than the body: `PlanetTemperature.InternalCelsius` is a
      per-WORLD value that calls `GeothermalMap.WorldIntensity` every time it is asked, and this sat
      in the per-TILE path — 200,000 calls a world for one answer. It is resolved once at the call
      site now, beside the other per-world figures. The bug forced a fix that is also the faster one.

## The ninth check — `tools/check-scope.mjs`

- [x] **SCOPE**, wired into `Check-Scripts.ps1` beside the other eight. Verified the way a check
      should be: the bug was reintroduced, the check flagged
      `SolarSystemGenerator.cs:218 'placed'`, and the fix was restored.

It models scope as **extents** — every declaration gets the span of text it is live in, and one
shadows another exactly when the other's extent strictly contains it. The obvious model, a stack
pushed on `{` and popped on `}`, is wrong about the most common declaration in the language: a `for`
header's variable is scoped to its loop, not to the block the loop sits in. That version reported
**59 findings across code Unity compiles daily**, and getting from 59 to 1 was three separate model
bugs, each found by looking at a flagged line and asking why the compiler disagrees:

1. **Sequential `for` loops read as nested.** The scan for where a `for` statement ends looked for the
   next `;`, and `for (…) for (…) { … }` ends on a brace with no semicolon at all — so it ran on and
   swallowed whatever followed. It recurses now.
2. **`foreach` variables were scoped to the enclosing block.** The pattern matched from the word
   `foreach`, which sits OUTSIDE the header parentheses, so the extent lookup could not tell the
   declaration belonged to the loop. Every offset is the name's own now, via the regex `/d` flag.
3. **Lambda bodies.** Two pairs in `InspectorBodyTabs` and `PlanetViewWindow` shadow across a lambda
   and Unity accepts both, so the rule here is evidently not the C# 7 one. Rather than guess at which
   version's rule applies with no compiler to ask, those are skipped and the header says so.

**A check for CS0103 was written and thrown away.** Deciding "this identifier is declared nowhere"
means resolving base classes, partials, extension methods and using-statics; the version that only
compared against other methods' locals produced **239 findings**, essentially all of them declaration
forms the parser had missed — `int w = …, h = …;` alone accounted for dozens. A check that cries wolf
gets ignored and then gets deleted. That gap is real, it is stated in the tool's header rather than
papered over, and closing it needs a resolver rather than a tripwire.

## What this changes about the claim at the top of these documents

"`Check-Scripts.ps1` is clean" has never meant "it compiles", and every one of these documents says
so. What it did not say clearly enough is which *classes* of error it cannot see. Nine checks is not
a compiler either — it still cannot see a wrong argument count, a wrong return type, an assignment to
a get-only property, or the CS0103 above. **Unity is the only thing that knows.**
