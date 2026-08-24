# Orbits, gas giants, surveying and the index bar — 2026-08-23

Status: **BUILT.** All eight phases. Nothing is compiled — there is no Unity here — but the eight
script checks pass and the three generation systems are measured by Node ports rather than asserted.
What each measurement actually said is recorded at the bottom, including the four places the numbers
came back wrong and were changed because of it.

Nine separate requests arrived in one session, plus three console errors and three screenshots. They
touch five systems that mostly do not know about each other, so this document puts them in an order
where each phase leaves the game in a state you can actually look at, and where no phase has to undo
the phase before it.

**Read §0 and the two Open Decisions at the bottom before anything is built.** One request (a literal
fixed 7×7 survey block) cannot be honoured at the top end of the grid range without a second change
the request does not mention, and one (the distance scale) has a knock-on into camera framing.

---

## The requests, and where each one lands

| # | Asked for | Phase |
|---|---|---|
| 1 | Loading caption + bar to the top of the screen | 1 |
| 2 | 84-second single frame during "Forming star system" | 0 |
| 3 | `MissingComponentException: no 'LODGroup' … Model_Scout 1` | 0 |
| 4 | Habitable zone is broken; Earthlike is the innermost planet | 2 |
| 5 | Placement rings, up to 9, skip-or-fill | 2 |
| 6 | Rings must still work with the Dev orbit slider and terraforming | 2 |
| 7 | ~5 bodies per system, sometimes <3 | 3 |
| 8 | 1 Solar Mass = 70 Solar System Mass | 3 |
| 9 | Moon caps: terrestrial 2–3, giant 3–4; many planets with none | 3 |
| 10 | Terrestrial moon budget 25% of host; giant moons ≤ 1.5 mass | 3 |
| 11 | Orbital inclination far too common | 3 |
| 12 | Purple/pink giants much rarer | 4 |
| 13 | Giant atmosphere shell much closer to the planet sphere | 4 |
| 14 | Stars and gas giants ×2 in system view | 4 |
| 15 | Per-colour GasClouds/Storm grid variants; Storm default red-orange | 4 |
| 16 | Great-Red-Spot storm cells, clouds flowing round them | 4 |
| 17 | No elevation / no "no resource here" on a gas giant | 4 |
| 18 | Survey highlight to 20% opacity | 5 |
| 19 | Fixed survey block, not scaled by planet | 5 |
| 20 | Survey must start at map centre and stay inside the map | 5 |
| 21 | Index survey still row-by-row; jitter and lag | 5 |
| 22 | Index icons hidden behind the View button | 6 |
| 23 | Move "View: Moons" to the left, by the map tabs | 6 |
| 24 | Index toggles stack vertically | 6 |
| 25 | Border when selected; much lower fill opacity | 6 |
| 26 | Indexes toggling each other off | 6 |
| 27 | Stack index values vertically, each in its own colour | 6 |
| 28 | Geothermal still reading surface heat | 7 |
| 29 | Magma fields minimum 800 °C | 7 |

---

## Phase 0 — the two that make everything else hard to judge

Both are live errors in the screenshots. Neither is large, and both make the later phases harder to
evaluate: the exception spams every frame a scout is built, and an 84-second frame makes any timing
work on generation unreadable.

- [x] **0A. `ShipLOD.Attach` — the fake-null trap.**
      `Assets/Scripts/Visual/ShipLOD.cs:110`

      ```csharp
      var group = root.GetComponent<LODGroup>() ?? root.AddComponent<LODGroup>();
      ```

      `??` is C#'s null-coalescing operator and it does **not** use Unity's overloaded `==`. A
      `LODGroup` that has been destroyed — or one on a pooled/re-used `Model_Scout` root — comes back
      as a *fake null*: a live C# reference wrapping a dead native object. `??` sees a non-null
      reference, hands it straight back, and `SetLODs` throws exactly the message in the log:
      *"There is no 'LODGroup' attached to the "Model_Scout 1" game object."*

      Fix is the explicit form, which routes through Unity's operator:

      ```csharp
      var group = root.GetComponent<LODGroup>();
      if (group == null) group = root.AddComponent<LODGroup>();
      ```

      `UIFactory.Ensure<T>` already exists and does precisely this (it is used at
      `SystemVisualizer.cs:335`); use it if it is reachable from `Visual/`, otherwise inline the two
      lines.

- [x] **0B. An eighth check in `tools/Check-Scripts.ps1` for the same trap.**
      This is a whole *class* of bug — `GetComponent<T>() ?? AddComponent<T>()`,
      `GetComponent<T>()?.Foo`, and `x ?? y` where `x` is any `UnityEngine.Object` — and it is
      invisible until it throws at runtime, which is the worst possible place for a codebase with no
      compiler in the loop. A regex check over `Assets/Scripts/**.cs` for `??` and `?.` on the result
      of `GetComponent`, `GetComponentInChildren`, `FindObjectOfType` or `Instantiate` costs nothing
      and would have caught 0A before it shipped. Document it in `CODEBASE_GUIDE.md` alongside the
      other seven.

- [x] **0C. Find the 84-second frame before guessing at it.**
      The report is *"a single frame took 84.08s during 'Forming star system 7 / 7' (87.53s for the
      whole step)"* — so one unyielded span is essentially the entire system. It is **not** the
      terrain build: `PlanetTerrainGenerator.BuildStepped` holds a 6 ms budget
      (`StepBudgetMs`, `PlanetTerrainGenerator.cs:255`) and yields unconditionally twice more before
      it returns. It is also not `GalaxyGenerator.Finalise`, which is a handful of assignments.

      Rather than slice on suspicion, add a **yield-gap profiler** first: a small static that
      `GenerateSystemStepped` stamps at every `yield`, logging any span over ~50 ms with the label of
      the section it just left. Run one galaxy, read the label, then slice that one thing. This is
      cheap, it is reusable for every future load-time complaint, and it means the fix lands on the
      real culprit rather than on the most plausible one.

      The ranked suspects, so the profiler has something to confirm or kill:

      1. **A lazy `TectonicsMap` / `GeothermalMap` bake landing inside one terrain column.**
         `TectonicsMap.Get` builds on first ask (`TectonicsMap.cs:511`) and the raster walks every
         cell site per sample (`:912`). `BuildStepped` only checks its stopwatch *after* a whole
         column, so the entire bake plus that column lands in a single unyielded span. This fits the
         symptom better than anything else on the list.
         **Fix:** pre-bake the plate layout and the geothermal field *before* the sliced loop, in
         their own yielded step, so the cost is paid where it can be spread.
      2. `DespeckleTerrain` and `ApplyWaterAndShores` — one frame each, O(w·h), on a 640×320 world.
         Slice both on the same 6 ms budget.
      3. `OreGenerator.Populate` and `POIGenerator.Populate` — O(w·h) and unsliced, run per body.
      4. `OrbitSafety.EnforceSystem` + `Validate` — once per system, over ~30 bodies.

      Phase 3 reduces body counts substantially, which will shrink this too, but a 40× reduction in
      one frame is a fix and not an excuse.

---

## Phase 1 — the loading screen

Small and self-contained; worth doing early because it is the one visible win with no dependencies.

- [x] **1A. Move the caption stack to the top.**
      `Assets/Scripts/UI/LoadingScreen.cs:94-134`. The column `col` is anchored and pivoted at
      `(0.5, 0.5)` at `anchoredPosition = Vector2.zero`. Change to `anchorMin = anchorMax = (0.5, 1)`,
      `pivot = (0.5, 1)`, `anchoredPosition = (0, -48)`. Everything inside it is already laid out
      top-down from the column's own top edge (`hrt`, `barTrack`, `prt`, `srt` all anchor to `(_, 1)`
      with negative offsets), so nothing inside needs touching.

- [x] **1B. Leave the middle clear, and say so.**
      The reason for the move is that star and planet previews want the centre. Add a comment naming
      that, and check the two paths that strip this screen down —`ShowGenesisTitles` and the skip
      route around `LoadingScreen.cs:333` — still position their own content sensibly with the column
      no longer centred.

---

## Phase 2 — the distance scale, the habitable zone, and placement rings

This is the largest phase and the root of request #4. Everything else in it follows from one number.

### What is actually wrong

The reported figures diagnose it exactly:

* star base band **10.1 – 19.6** → `hzInner = 0.80·R`, `hzOuter = 1.55·R` (`StarData.cs:306`) with
  `R = FluxScale·AU ≈ 12.6`, i.e. a G-type at `luminosity ≈ 0.70`.
* Terran band **8.9 – 33.9**. `Habitability.GetZone` takes the star's band and then
  **shifts and inflates it**: `center = HzCenter · tempShift` where `tempShift` runs 1.95 → 0.5
  (`Habitability.cs:19`), and `half = HzWidth · tolerance · 1.15`, applied asymmetrically as
  `−0.85·half … +1.45·half`. For a Terran that is a centre pushed 22% outward and a band **2.3×
  wider than the star's own**. A "habitable zone" three times as wide as it is deep is not a zone.
* homeworld at **35.8** — outside even that inflated band, and the *innermost* body in the system.

That last one is the mechanism, and it is a fight between two systems:

1. `EnsureHabitableWorld` (`SolarSystemGenerator.cs:373`) moves the chosen world to a random radius
   inside the zone.
2. `OrbitSafety.EnforceSystem` then runs and pushes it back out to
   `starRadius + StarClearance + reach` (`OrbitSafety.cs:196`), because a habitable radius of 20 is
   *inside the clearance a modern, 2×-scaled star needs*.

So the promoted world lands wherever the clearance puts it, which is the innermost lane — and the
zone it was supposed to be in is behind it, closer to the star than any planet can physically go.

**`AU = 14` (`StarData.cs:53`) is the root cause.** A G-type's rendered radius is ~2.9 units, which
is **0.21 AU** on this scale; the real Sun is 0.005 AU. The star is forty times too big relative to
the distances, so its clearance eats the entire inner system — and request #14 asks to double it
again.

### The fix

- [x] **2A. Raise `AU` from 14 to 40.**
      `Assets/Scripts/Data/StarData.cs:53`. This is the one number that makes the habitable zone
      reachable, and it is load-bearing for the whole phase: `ReferenceDistance`, `TempReference`,
      `hzInner/hzOuter`, `SystemSpread` and every `rel` band test are all derived from it, so they all
      move together and none of the *ratios* change. Only the star's clearance stops being a
      significant fraction of the inner system.

      After 2A, a G-type (flux 1.0) has `R = 40`, `HZ = 32 … 62`, against a star reach of ~10.
      Before 2A it was `R = 14`, `HZ = 11 … 22`, against a star reach of ~10.

- [x] **2B. Stop `Habitability.GetZone` inventing its own band.**
      `Assets/Scripts/Data/Habitability.cs:11-27`. A species' preference should *shade* the star's
      zone, not relocate it. Proposed:

      * `tempShift`: `Lerp(1.25f, 0.80f, idealTemp)` instead of `Lerp(1.95f, 0.5f, …)`. A
        mid-temperature species now sits on the star's own centre (±0%) instead of 22% outside it;
        the extremes move ±25% instead of ±95%.
      * `half`: `star.HzWidth * 0.5f * Clamp(tolerance, 0.6f, 1.4f)` — the star's band as the
        baseline rather than 1.15× it.
      * Asymmetry: keep it, but far gentler — `inner = center − half·0.9`, `outer = center + half·1.2`.
        Real habitable zones do extend further out than in; they do not extend 70% further.

      A default Terran around a Sun-like star then gets ~34 … 58 against the star's 32 … 62 — a zone
      you can point at, that two adjacent rings can share, and that no longer claims a band four
      times deeper than the star's.

- [x] **2C. Placement rings — nine of them, off the star's own reference distance.**
      New: `Assets/Scripts/Generation/PlacementRings.cs`.

      The rings are defined as multiples of `R = StarDatabase.ReferenceDistance(star)`, on a ~1.4×
      geometric ladder chosen so that **rings 4 and 5 land inside the habitable zone and rings 1–3 do
      not** — which is request #4's own example, our solar system, expressed as a table:

      | ring | ×R | G-type (R=40) | in HZ (32–62)? | Sol analogue |
      |---|---|---|---|---|
      | 1 | 0.36 | 14.4 | no | Mercury (0.39 AU) |
      | 2 | 0.52 | 20.8 | no | Venus (0.72) |
      | 3 | 0.70 | 28.0 | no | — |
      | 4 | 0.95 | 38.0 | **yes** | Earth (1.00) |
      | 5 | 1.30 | 52.0 | **yes** | Mars (1.52) |
      | 6 | 1.80 | 72.0 | no | belt / frost line |
      | 7 | 2.55 | 102.0 | no | Jupiter (5.2) |
      | 8 | 3.60 | 144.0 | no | Saturn |
      | 9 | 5.10 | 204.0 | no | Uranus / Neptune |

      Two guarantees the class must enforce, in this order:

      1. **Clear the star.** Any ring inside `OrbitSafety.StarRadius(star) + OrbitSafety.StarClearance`
         plus room for the body that will sit on it is unusable. Rather than silently dropping it
         (which would cost a dim star its inner rings and shift its HZ ring count), shift the *whole
         ladder* outward by the deficit on ring 1. Worked example — an M dwarf: `baseScale 1.9 × 4 =
         7.6` visual scale, so a star radius of 3.8, `+ 4.5` clearance `+ ~1` for a small inner world
         = **9.3** required. Flux 0.45 gives `R = 18`, so ring 1 is 6.5 and the ladder shifts **2.8**
         units. Ring 4 moves 17.1 → 19.9, comfortably inside that star's 14.4 – 27.9 zone.

         The same check on the other extreme confirms no shift is needed there: an O-type is
         `8.0 × 4 = 32` visual scale → radius 16, `+ 4.5` = 20.5 required, and with flux 3.0 (`R = 120`)
         ring 1 sits at 43.2. A G-type needs 10.3 and its ring 1 is at 14.4.
      2. **At least one ring in the zone.** After the shift, assert that some ring falls inside
         `[hzInner, hzOuter]`; if none does, nudge the nearest one into the zone's centre. This is
         what lets `EnsureHabitableWorld` stop fighting `OrbitSafety` — it will be able to promote a
         world that is *already* at a legal radius rather than moving one to an illegal one.

      Rings are **generation-time only**. Nothing at runtime snaps to them.

- [x] **2D. Rebuild the lane loop as a ring loop.**
      `SolarSystemGenerator.GenerateSystemStepped`, `SolarSystemGenerator.cs:98-303`. The
      `currentRadius`/`prevOuterReach`/`SystemSpread` step-outward arithmetic
      (`SolarSystemGenerator.cs:289-303`) is replaced by iteration over the nine ring radii. `MaxLanes`
      (`:27`) becomes 9 and is renamed. `rel` per ring is still
      `radius / TempReference(star)`, so `ChooseLane`, `TerrestrialBandMax`, `SetBandWater` and
      `BiasHeat` all keep working unchanged — which is the point of keeping the ladder in units of `R`.

      **Skip-or-fill** (request #5) is now the loop's only decision per ring: see 3B.

- [x] **2E. `EnsureHabitableWorld` places on a ring, not at a random radius.**
      `SolarSystemGenerator.cs:373-420`. Replace `Random.Range(lo, hi)` with *the ring nearest the
      zone centre that is inside `[lo, hi]`*. If a body already sits on that ring, promote **that**
      body instead of moving anything. This removes the move/enforce fight entirely rather than
      papering over it, and it is why request #4's symptom disappears rather than shifting.

- [x] **2F. Dev orbit slider and terraforming are unaffected — verify, don't assume.**
      Request #6. `OrbitControlPanel.cs:176` and `TerraformManager.SetOrbitRadiusLive`
      (`TerraformManager.cs:625`) both write `orbitRadius` directly and are bounded by
      `OrbitSafety.ClampRadius` / `EnforceSystem`, neither of which knows about rings. So the
      requirement is met by *not* adding ring snapping anywhere at runtime, and the work is:
      * confirm both paths still clamp against `OrbitSafety` and not against a ring table;
      * set `naturalOrbitRadius` to the **ring radius** at generation
        (`GalaxyGenerator.Finish`, `GalaxyGenerator.cs:131-133` already stamps it from `orbitRadius`,
        so this is free), so Dev-mode "reset orbit" returns a planet to its ring;
      * check `HabitableZoneVisualizer` still draws the zone correctly at the new scale.

- [x] **2G. Camera framing at the new scale.** See **Open Decision 2**. A G-type's ring 9 is 204
      units out and an O-type's is ~600. `CameraController` / `GalaxyLOD` / `MapTierVisibility`
      minimum-and-maximum zooms need checking against that, and `OrbitSafety`'s `LaneGap` may be able
      to shrink now that the ring ladder — not accumulated padding — decides spacing.

---

## Phase 3 — how much a system contains

Requests #7–#11. The budget change and the ring change interact, so this phase follows Phase 2.

- [x] **3A. `SsmPerSolarMass` 100 → 70.** `Assets/Scripts/Generation/MassRules.cs:44`, and scale the
      clamps with it: `BudgetMin` 45 → **32**, `BudgetMax` 400 → **280** (`MassRules.cs:53`). The
      measured table in that file's comment block (`MassRules.cs:56-70`) becomes wrong the moment this
      lands and **must be re-measured, not edited by hand** — see Phase 8.

- [x] **3B. A target body count, and rings that skip.**
      The budget alone will not take a Sun-like system from 8.6 planets to ~5; it is a ceiling, and
      the lane loop spends it. So the ring loop gets an explicit target:

      ```
      targetBodies = 2 + round(Bell() * 5)      // 2..7, mode 5 (Bell() is MassRules' triangular roll)
      ```

      …clamped to what the budget can fund, and capped at 9. Rings are then chosen by picking
      `targetBodies` of the nine — weighted toward the middle rings so systems are not all rim or all
      core, and **always including a habitable-zone ring** when the star has a zone. Every unchosen
      ring is simply empty, which is what stops a five-body system piling up against the star
      (request #5's stated reason for wanting rings at all).

      With mode 5 and a `2 + …` floor, "fewer than 3 bodies" lands around 15–20% of systems —
      "not uncommon", as asked, without being the norm.

- [x] **3C. Moon counts.** `SolarSystemGenerator.cs:227-233`.
      * `maxMoons`: giant `5 → 4`, terrestrial `3 → 3` but see 3D — the *budget* is what will
        usually bind first.
      * The no-moon roll (`:225`) goes from `giant 0.15 / terrestrial 0.34` to
        **`giant 0.30 / terrestrial 0.62`**. Request #9's "some planets should be able to spawn by
        themselves with no moons" is currently true one time in three for a rocky world; at 0.62 it is
        the *common* case, which matches our own system (two of four terrestrials have none).
      * The early-stop roll (`:238`, currently 0.42) rises to **0.55**, so two moons is a real
        outcome and three is the tail.

- [x] **3D. Moon mass rules.** `MassRules.MoonBudget` (`MassRules.cs:180`).
      * Terrestrial: `hostMass * 0.5f` → **`hostMass * 0.25f`** (request #10, exactly as stated).
      * Gas giant: keep the tenth-of-host budget, and add a hard **per-moon cap of 1.5** (request
        #10's second half) — `RollMoon` gains a `maxOne` parameter, or the giant path clamps its
        result. A mass-40 giant therefore gets 4.0 to spend across at most 4 moons, none over 1.5,
        which is a retinue rather than a second system.

- [x] **3E. Inclination.** Request #11. Currently **every** planet gets
      `inclination = Random.Range(-7f, 7f)` (`SolarSystemGenerator.cs:206`) and every moon
      `Random.Range(-15f, 15f)` (`:281`) — so inclination is universal, not rare, and the "too many
      inclined planets" report is simply describing the code. Replace with a two-stage roll:

      ```csharp
      // Most worlds formed in the disc and stayed in it. An inclined orbit is a story about
      // something having happened to that world, so it has to be rare enough to notice.
      body.inclination = Random.value < 0.12f ? Random.Range(-9f, 9f) : Random.Range(-0.8f, 0.8f);
      ```

      …and **at most one per system**: track a `bool inclinedThisSystem` in the ring loop and refuse a
      second. Request #11 says "having one with an inclination should already be rare and not every
      solar system should generate one" — one-in-eight rings, capped at one per system, puts a
      visibly inclined world in roughly half of systems and never two. Moons get the same treatment at
      `Random.value < 0.15f ? ±12° : ±1.5°`.

      Belt members already force `inclination = 0f` (`:150`) and must stay that way — the comment
      there explains why, and it is still correct.

---

## Phase 4 — gas giants

Requests #12–#17. Independent of Phases 2–3 and can be built in parallel with them.

- [x] **4A. Violet much rarer.** `Assets/Scripts/Visual/GasGiantPalette.cs:56-61`. Currently
      Violet is `r ≥ 0.95` — one in twenty, which the file's own comment calls "deliberately rare".
      Request #12 says much rarer than that. New thresholds:
      `Ammonia < 0.50, Methane < 0.74, Cobalt < 0.88, Ember < 0.99, Violet ≥ 0.99` — one in a hundred.

- [x] **4B. The atmosphere shell hugs the sphere.**
      `Assets/Scripts/Visual/PlanetAppearance.cs:177`. `thickness = 1.14f` for `GasGiant` → **`1.02f`**.
      Request #13's reasoning is right and worth writing into the file: what you see of a gas giant
      *is* its atmosphere, so a visible shell standing off the surface is drawing a boundary that does
      not exist. Also skip `AddClouds` for giants (`PlanetAppearance.cs:102`) — a cloud shell at 1.03
      over an atmosphere at 1.02 over a surface that is already cloud is three coats of the same thing.

- [x] **4C. Stars and giants ×2.** Request #14.
      * Stars: `StarData.cs:287`, `s.visualScale = baseScale * 2f * …` → `* 4f *`. `visualScale` feeds
        `OrbitSafety.StarRadius`, `StarDatabase.DensityOf` and `CoronaScale`, so clearance, density and
        halo all follow automatically — this is safe **only after Phase 2A**, because at `AU = 14` it
        would make the clearance problem twice as bad.
      * Giants: `MassRules.VisualDiameter` (`MassRules.cs:255`) gains a gas-giant multiplier —
        `mass >= WorldClassifier.GasGiantMassFloor ? 2f : 1f` on the diameter. `OrbitSafety.Scale` reads
        `VisualDiameter`, so orbital spacing reserves the new size without a second edit.

- [x] **4D. Per-variant cloud and storm colours.** Request #15.
      Today `GasGiantPalette.Apply` (`GasGiantPalette.cs:110`) multiplies **one** tint over whatever
      `TerrainColorMap` returned, so a blue giant is the tan palette dyed blue and its storms are the
      tan storm colour dyed blue. Request #15 wants genuine per-colour variants of both tiles.

      * `TerrainColorMap.cs:63` — Storm's default `(0.60, 0.56, 0.66)` (a purple-grey) becomes a
        **reddish orange, `(0.78, 0.38, 0.24)`**, as asked.
      * `GasGiantPalette` gains `CloudColor(Variant)` and `StormColor(Variant)` — an explicit pair per
        variant, not a multiply — and `Apply` routes `GasClouds` and `Storm` through them while leaving
        every other terrain type on the existing tint path.
      * **Three call sites currently bypass `Apply` and must not.**
        `SurfaceTextureRenderer.cs:60-66` applies it; `:142` (the cached colour array) and `:163` and
        `:237` (the distant globe) do not. That is why a giant can look different in the map and on the
        globe. Fold `Apply` into all of them; for the cached array, build the array *per body*.

- [x] **4E. Great Red Spots.** Request #16, and the largest single piece of Phase 4.
      `PlanetTerrainGenerator.GasGiant` (`:1291-1297`) is three lines: a latitude band, and
      `elev > 0.78 → Storm`. The "great-spot style storm" comment on that line is aspirational — the
      elevation field is FBm, so what it actually produces is scattered speckle, not spots.

      Build a real one:

      1. **A deterministic spot list per world.** `GasGiantStorms.Of(body)` — 0–3 spots, seeded from
         `terrainSeed` and `id` with the same hash shape `GasGiantPalette.Of` uses, so it survives a
         save, a reload and a sandbox regenerate without a field. Each spot: centre `(u, v)`, radii
         `(ru, rv)` with `ru` 1.5–3× `rv` (spots are wider than they are tall, because the bands they
         sit in are), and a rotation. Sizes vary widely — one world's spot is a quarter of its width,
         another's is a freckle.
      2. **Spots sit in storm bands.** A spot's `v` is snapped to the centre of a band that
         `Mathf.Repeat((lat + …) * 6f, 1f) >= 0.5` — i.e. a Storm band, not a GasClouds one. Request
         #16 says exactly this: *"should generate within the bands of Storm that the grid generates"*.
      3. **The bands flow around them.** Inside a spot, `Storm`. Immediately outside — within ~35% of
         the spot's radius again — the band coordinate is *displaced tangentially* around the ellipse
         before `Mathf.Repeat` is taken, which is what makes the neighbouring cloud lanes bend round
         the spot instead of running straight through it. This is the detail that makes it read as
         Jupiter rather than as a sticker.
      4. **`elev > 0.78 → Storm` goes away**, replaced by the spot test. The speckle it produced is
         what a spot is meant to be, and keeping both would leave the real spot in a field of noise.

      Because `Classify` is shared by the grid build (`BuildStepped`), the map texture
      (`SurfaceTextureRenderer:60`) and the globe (`:237`), the spot appears in the system view for
      free — which is request #16's second half.

- [x] **4F. No elevation, no "no resource here", on a giant.** Request #17.
      `PlanetViewWindow.cs:7360-7370` (the elevation band + metres line) and
      `AppendIndexReadout`'s `listed == 0` fallback (`:7434-7435`). Both suppressed when
      `b.type == CelestialBodyType.GasGiant`. The reasoning is the request's own and belongs in a
      comment: there is no ground, so a height above a waterline that does not exist is a number about
      nothing, and an index that can never have a reading is not "empty", it is inapplicable.

      Worth checking while in there: `SurfaceIndex` already returns 0 for `GasClouds`/`Storm` in four
      places (`SurfaceIndex.cs:483, 560, 644, 804`), so the indexes are correctly absent — the tooltip
      was just reporting that absence as though it were news.

- [x] **4G. Note, not built: gas giants are not terrestrial worlds.**
      The request's framing — *"most systems for Terrestrial worlds should not apply to Gas Giants at
      all… Floating Cities and entirely new systems will need to be created"* — is a design direction
      rather than a change, and building it is a spec of its own. 4F is the part of it that is
      actionable now. Recorded here so it is not lost.

---

## Phase 5 — surveying

Requests #18–#21. **Read Open Decision 1 before building 5B.**

- [x] **5A. The white block at 20% opacity.** Request #18.
      `Assets/Scripts/Visual/SurveyVeil.cs:MarkerColor` currently pulses alpha `0.14 → 0.42`. The
      screenshot shows it near-opaque because the *fill* is drawn over an already-thinning veil and the
      two stack. Set the fill's pulse to `Lerp(0.12f, 0.20f, pulse)` — a 20% ceiling, as asked — and
      leave `MarkerEdgeColor` alone: the border is what says how big the block is, and the request is
      about seeing the veil fade *through* the block, which is the fill's job.

- [x] **5B. A fixed block, and the arithmetic that constrains it.** Requests #19 and #20.

      The report is right about both symptoms and they have the same cause. `Survey.CellsPerUnit`
      (`Survey.cs:490`) scales the block by world area, so:
      * a 10×5 asteroid gives `CellsPerUnit ≈ 0.35`, and `BlockCells` clamps to **2×2** — the reported
        science-ship-on-an-asteroid case;
      * a 400×200 world gives `CellsPerUnit ≈ 2`, and a 7-unit science hull gets **14×14** — the other
        reported case, and the one in the screenshot that hangs off the top of the map.

      **Fix 1 — the block is literally the survey units.** `BlockCells` becomes
      `Clamp(SurveyUnits(u), 2, Min(w, h))`. 5 for a scout, 7 for a science hull, +1 per Empire Tech
      tier. `CellsPerUnit`, `TargetBlocks` and `DefaultBlockCells`'s scaling all go. A 10×5 asteroid
      gets a block clamped to 5 and is done in two; a 40×20 world takes 6×3 = 18 blocks, which is the
      request's own worked example ("around 16").

      **Fix 2 — centre-anchored, and never off the map.** Both the band grid and the column grid are
      currently anchored at the origin: bands start at `y = band * blockCells` (`Survey.cs:672`), so the
      last band is a partial strip, and `ActiveBlocks` (`:846`) maps a rank back through `ColRank` with
      no bounds check on `y0 + h`. Replace with an explicit, centre-anchored block grid:

      ```
      nx = ceil(w / bc), ny = ceil(h / bc)
      bandY(i)  = clamp(centreRow - floor(ny/2)*bc + i*bc, 0, h - bc)
      blockX(j) = wraps — longitude is cyclic, so no clamp, but the grid origin is the centre column
      ```

      Every emitted `Block` is then fully inside the map by construction, and the first one is centred
      — which is request #20 in both halves. `ReachedAt`, `RowBlocks`, `BandForShip`, `BandFill` and
      `ActiveBlocks` all read the same grid helper so a boundary cannot fall in one place for the
      renderer and another for the veil (the hazard `RowBlockCells`' comment already warns about).

      **Fix 3 — the dwell time floats, and big worlds run several heads.** This is the part the request
      does not mention and cannot be avoided; the numbers are in Open Decision 1. In short:

      ```
      BlockSeconds = clamp(MaxSurveySeconds / blockCount, MinBlockSeconds, BaseBlockSeconds)
      Heads        = clamp(ceil(blockCount * BlockSeconds / MaxSurveySeconds), 1, 8)
      ```

      with `BaseBlockSeconds` 4.0 / 3.5 (unchanged, as originally specified), `MinBlockSeconds = 0.8`
      and `MaxSurveySeconds = 240`. `blockScratch` (`Survey.cs:936`) must grow from 8 to 8 × heads.

- [x] **5C. The index survey uses blocks too.** Request #21.
      Level 2 currently advances by `CellRank` — `RowRank(h, y) * w + ColRank(w, x)` (`Survey.cs:389`)
      — which is literally row-major, and `Survey.Reached` compares a per-cell fraction against it. That
      is the row-by-row crawl in the report, and it is also the lag: the front crosses a cell many times
      a second and every crossing rebuilds and re-uploads the whole overlay texture
      (`PlanetViewWindow.RefreshIndexOverlay` → `SetPixels32` + `Apply`).

      Change `Reached` to ask the **block grid** from 5B instead: a cell is reached when its block's
      index in running order is below `progress * blockCount`. Whole 7×7 blocks flip at once, so the
      texture is rebuilt on block boundaries rather than continuously — the jitter and the lag are the
      same fix. `PaintIndexInto`'s sweep-head test
      (`PlanetViewWindow.cs:4784`, `Survey.BeingSurveyed`) then frames the same block the level-1
      marker does, which is also more legible.

---

## Phase 6 — the index bar

Requests #22–#27. Independent of everything else.

- [x] **6A. The View button is sitting on the index bar.** Request #22.
      `IndexIconBar.Attach` pins the bar to `gridHolder` top-right at `(-6, -6)`
      (`IndexIconBar.cs:130-134`, called from `PlanetViewWindow.cs:540`).
      `BuildViewFormatButton` pins a 150×26 button to `gridHolder` top-right at `(-6, -6)` too
      (`PlanetViewWindow.cs:7605-7610`) — and it is created **later** (`:625`), so it is a later sibling
      and draws on top. That is the whole bug; nothing is conditional on the map being minimised, the
      button was just covering it.

- [x] **6B. Move "View: Moons" to the left.** Request #23. Re-anchor `viewFormatBtn` to `(0, 1)` with
      pivot `(0, 1)`, positioned *below* `moonTabStrip` — that strip is already top-left, vertical, and
      `ContentSizeFitter`-driven (`PlanetViewWindow.cs:630-640`), so the View button goes underneath it
      as another entry in the same column. That is literally "next to the Map Toggle buttons".

- [x] **6C. Index toggles stack vertically.** Request #24. `IndexIconBar.Rebuild`
      (`:196-200`) lays cells out along x at `i * (IconPx + GapPx)`. Swap to y:
      cells anchor `(0.5, 1)`, `anchoredPosition = (0, -i * (IconPx + GapPx))`, and the bar's
      `sizeDelta` becomes `(IconPx, tall)`.

- [x] **6D. Border when selected, and a much fainter plate.** Request #25. Two changes in the same
      method:
      * The plate (`IndexIconBar.cs:212`) is `rgba(0.04, 0.06, 0.09, 0.82)` and the report is that the
        icon can barely be seen when a button is on. Drop the **active** plate to alpha **0.28** and
        leave the inactive one darker (0.72) so an unlit icon still has something to sit on over
        terrain — the file's own comment explains why it needs a plate at all.
      * `Retint` (`:277-286`) already colours a four-edge frame with `SurfaceIndex.Outline(kind, 1f)`
        when active and `clear` when not — so the border exists. Raise `FramePx` from **2 → 3** so it
        reads as a selection outline rather than a hairline, which is what the request is describing.

- [x] **6E. Indexes must not toggle each other. Two separate causes.** Request #26.

      1. **A real one.** `MineralOverlayActive` (`PlanetViewWindow.cs:277-284`) keys on the legacy
         single-valued `activeIndex`, and when it is true `RefreshOverlays` calls
         `RefreshIndexOverlay(SurfaceIndexKind.Mineral)` and **`return`s** (`:4584-4588`) — throwing
         away every other active overlay. Switch Solar on, then Mineral, and Solar's wash genuinely
         disappears while its button still says "showing". Fix: delete `activeIndex` in favour of
         `IndexToggles`, and fold the named-ore-deposit drawing (the only thing the Mineral early-out
         was for) into `PaintIndexInto`'s existing Mineral branch (`:4791-4799`), which already draws
         them. The early-out then has no reason to exist.
      2. **A perceived one, which is 6F.** The *numbers* switch wholesale, which reads as the previous
         index going away. See below.

- [x] **6F. Stack the values, one line per index, each in its own colour.** Request #27.
      `YieldIndex()` (`PlanetViewWindow.cs:6242-6257`) deliberately returns a single kind — *"the
      numbers under the cursor follow the LAST index switched on"*. With Solar up and Mineral added,
      every number on screen changes to Mineral's (of which there are none on an asteroid), which is
      exactly the reported symptom.

      Replace with all active indexes: `RefreshYieldIcons` builds, per cell, a small vertical stack —
      one line per index that has a shown value there, in `SurfaceIndex.Outline(kind, t)`, which is
      already the per-index colour the request asks for. Guards to keep:
      * the `YieldIconMinTilePx` gate rises with the stack height (three lines need ~3× the room), or
        the cell fills with unreadable text;
      * the `Survey.RevealOf(...).complete` gate stays **per index** — a finished index may stack
        beside an unfinished one, and the unfinished one simply does not contribute a line;
      * the `sig` cache key must include the whole active set, not one kind, or the stack will not
        redraw when a second index is switched on.

---

## Phase 7 — geothermal and magma

Requests #28 and #29. The screenshot (Achernar I, `MagmaField`, `794 °C`, `Geothermal 95%` across the
entire map) shows both faults at once, and they are one causal chain.

- [x] **7A. `MagmaMinC` 650 → 800.** `Assets/Scripts/Generation/WorldClassifier.cs:86`. Request #29
      exactly. The screenshot's 794 °C tile is currently molten and will correctly demote to
      `LavaRock` — the reverse gate at `PlanetTerrainGenerator.cs:1115` already handles that, and it is
      why raising the constant is a one-line change rather than a classifier rewrite.

- [x] **7B. Molten ground requires *internal* heat, not starlight.** Request #28's actual cause.
      `PlanetTerrainGenerator.cs:1101`:

      ```csharp
      if (tileC >= WorldClassifier.MagmaMinC && !IsWater(t)) return TerrainType.MagmaField;
      ```

      `tileC` is the tile's total temperature, and `PlanetTemperature.BaseCelsius` builds that from
      **starlight + greenhouse + internal** (`PlanetTemperature.cs:120`). So a world baked to 800 °C by
      sitting close to its sun has liquid rock over its whole surface, with no internal heat involved
      at all. Gate the promotion on the internal term as well:

      ```csharp
      // Rock melts from below. A world baked from OUTSIDE is scorched, not molten — its ground is
      // LavaRock and AshWaste, and the heat under it is nobody's to tap.
      if (tileC >= MagmaMinC && PlanetTemperature.InternalCelsius(b) >= MagmaInternalMinC && !IsWater(t))
      ```

      `InternalC` (`PlanetTemperature.cs:95`) is currently private and volcanic-only; expose it, and
      set `MagmaInternalMinC` around 300 so a genuinely molten world still qualifies.

- [x] **7C. The Geothermal index stops reading the surface.** Request #28's stated symptom.
      `SurfaceIndex.Geothermal` (`SurfaceIndex.cs:507-513`) does
      `geo = Mathf.Max(geo, CrustHeat(f.terrain))`, and `CrustHeat(MagmaField) = 0.95`
      (`:543`) — so once 7B's bug has painted a world with magma, the index reads 95 everywhere,
      which is the screenshot. 7B removes most of it; this closes the rest.

      `CrustHeat` should be **evidence that raises a reading, weighted by whether the world has
      internal heat at all** — not a value that replaces it. Scale it:

      ```csharp
      geo = Mathf.Max(geo, CrustHeat(f.terrain) * GeothermalMap.WorldIntensity(b));
      ```

      A volcano on a world with a live interior still reads ~100. The same tile on a sun-baked rock
      with a cold core reads near zero, which is the truth: there is no heat there to build a plant on.
      The plate lines are drawn from the plate raster directly
      (`PlanetViewWindow.PaintPlateLines`) and are unaffected — the report confirms those are correct
      now, and this change must not disturb them.

---

## Phase 8 — validation

There is no Unity here. Nothing in this document is known to compile, and none of the numbers above
are known to be right until they are measured. Each of these is a gate on the phase above it.

- [x] **8A. `tools/Check-Scripts.ps1` clean** (eight checks after 0B) before every commit.
- [x] **8B. A Node port of the ring layout and the budget** — extend the existing pattern
      (`tools/audit-terrain-balance.mjs`, `tools/survey-check.mjs` both read constants straight out of
      the `.cs` so they cannot drift). Over 4,000 systems per star class, report: bodies per system,
      the fraction with <3, moons per planet, the fraction of planets with none, giants per system,
      inclined worlds per system, and **how many bodies land inside the habitable zone**. Gates
      Phases 2 and 3. The measured table in `MassRules.cs:56-70` is regenerated from its output.
- [x] **8C. Re-run `tools/survey-check.mjs`** after 5B — its whole purpose is the duration table, and
      that table is what Open Decision 1 turns on. Gates Phase 5.
- [x] **8D. A rendered PNG of a gas giant's grid** — the storm spots in 4E are exactly the sort of
      thing that reads correctly in source and comes out as a smear on screen. Port `GasGiant()` to
      Node, draw four worlds at four seeds, look at them. Gates 4E.
- [x] **8E. A screenshot pass** on the index bar, the survey block and the loading screen. Gates
      Phases 1, 5 and 6, and is the only check for any of them.

---

## Open decisions

### 1. A literal fixed 7×7 block cannot cover the whole grid range

Request #19 asks for a fixed block, "7x7", not scaled by planet. The block size can be made literal
everywhere. The **dwell time** cannot, and here is the arithmetic that says so.

Grids run 10×5 to 640×320 (`MapMetrics.WidthForMass`). An Earth-mass world is 200×100. At a literal
7×7 and the originally-specified 3.5 s per block:

| world | mass | blocks | at 3.5 s/block |
|---|---|---|---|
| 10×5 | 0.1 | 2 | 7 s |
| 50×25 | 0.5 | 32 | 112 s |
| 200×100 | 1 (Earth) | 435 | **25 min** |
| 560×280 | 4 | 3,200 | **3.1 hrs** |
| 640×320 | 40 (giant) | 4,232 | **4.1 hrs** |

This is the same wall the current `CellsPerUnit` scaling was built to avoid, and it is why that
scaling exists. Removing it without changing anything else replaces "the block is the wrong size"
with "the survey never ends".

**Proposed (what 5B assumes):** the block stays literally 7×7 everywhere; the ship's **dwell** on each
block shortens on large worlds, floored at 0.8 s so the marker never strobes; and where that floor
still leaves the survey too long, the ship works **several blocks at once** — up to eight — which
reads as a big job needing more coverage rather than as a bigger box.

| world | blocks | dwell | heads | total |
|---|---|---|---|---|
| 10×5 | 2 | 3.5 s | 1 | 7 s |
| 50×25 | 32 | 3.5 s | 1 | 112 s |
| 200×100 (Earth) | 435 | 0.8 s | 2 | 2.9 min |
| 560×280 | 3,200 | 0.8 s | 8 | 5.3 min |
| 640×320 (giant) | 4,232 | 0.8 s | 8 | 7.0 min |

Every block on screen is 7×7. A full planetary survey becomes a multi-minute job you watch cross the
map, which is arguably what the block rework was for. **If a ~3-minute Earth survey is too long,
the alternative is to let the block grow above ~200×100 only** — every settleable world keeps its
literal 7×7 and only the giants get a bigger box. Say which you prefer; 5B is written for the first.

### 2. `AU = 40` makes systems physically larger on screen

Phase 2A quadruples the distance scale, and 4C doubles star size on top of it. A G-type's outermost
ring lands ~204 units out; an O-type's (flux 3.0) around 610. The current layout, by comparison, tops
out near 90.

Ratios are unchanged, so nothing *inside* a system changes shape — but the camera has to frame it.
`CameraController`'s zoom limits, `GalaxyLOD`'s distance bands and `MapTierVisibility`'s thresholds all
need re-checking, and it is likely `OrbitSafety.LaneGap` (3.2) can shrink, since the ring ladder rather
than accumulated padding now decides spacing. **This is a real risk and it is why 2A and 2G are in the
same phase** — if framing turns out to be a problem, the fallback is `AU = 28` with the star scale at
×3 rather than ×4, which still clears the inner rings but keeps systems tighter.

### 3. Two things noted and deliberately not built

- **4G**, gas giants needing their own settlement systems (Floating Cities). A spec of its own.
- The **`explorationProgress` save format** survives Phase 5 unchanged — `surveyRows` is still one float
  per row and a block reveal is still a contiguous run along each row it covers. But the *block grid
  moves* (origin-anchored → centre-anchored), so a save taken mid-survey will resume with its revealed
  ground in a slightly different arrangement. Finished worlds and unstarted worlds are unaffected. This
  is judged acceptable; flag it if it is not.

---

## What the measurements actually said

Three Node ports were written, because there is no Unity here and every number below would otherwise
be an assertion. Four of them came back wrong, and the code changed rather than the claim.

### `tools/system-composition-check.mjs` — 4,000 systems per star class

```
star  budget   slots    <3       planets  giants   belts    moons    moonless incl     inZone   outerR
M     32       3.7      24%      3.3      0.6      0.4      2.1      55%      0.51     100%     101
K     54       4.1      20%      3.7      0.8      0.4      2.5      55%      0.53     100%     172
G     77       4.3      19%      4.0      0.9      0.3      2.6      55%      0.56     100%     247
F     108      4.4      19%      4.0      0.9      0.4      2.7      54%      0.57     100%     375
A     193      4.4      19%      4.0      0.9      0.4      2.7      54%      0.55     100%     607

G-type ladder (R=40, zone 32-62, star reach 10.3):
  1:14  2:21  3:28  4:38*  5:52*  6:72  7:102  8:144  9:204     (* = in zone)
```

Mean **4.2 filled rings** (a whole asteroid belt counts as one, which is the request's own unit),
**20% of systems under three**, **2.6 moons** per system against 11.7 before, **55% of planets with
none**, and **at most one inclined world**, in about half of systems.

**Three things it caught that reading the code would not have:**

1. **Nine bodies, not five.** The first pass counted every asteroid in a belt separately, so a system
   with one belt "had" ten bodies. The target is filled RINGS.
2. **An M dwarf put a world in its own habitable zone only 81% of the time.** The ring was always
   chosen — but the inner rings could spend the whole allowance first, and `ChooseLane` could roll it
   as a belt. Fixed by reserving a terrestrial's mass until that ring is built and never letting it
   be anything else. Then it was still 85%, because running out of budget `break`s the loop and the
   reserve was never spent; skipping the ring instead took it to 100%.
3. **Doubling the stars (Phase 4C) put ring 3 back inside the zone on dim stars.** The ladder was
   being shifted bodily outward to clear the bigger star, which moved ring 3 from 12.6 to 16.6 against
   a zone starting at 14.4 — a milder version of the exact bug this work exists to fix, reintroduced
   by the fix for it. The inner three rings are now COMPRESSED into the room available instead.

### `tools/gas-giant-check.mjs` — twelve rendered worlds

The picture is the point, and the first one was wrong in two ways no amount of reading the classifier
would have shown:

* **The spots were invisible.** They are made of `Storm` tiles and snapped into the middle of a `Storm`
  band, so a three-spot world was indistinguishable from a spotless one. Jupiter's answer is a pale
  **hollow** punched out of the belt around the storm, and one ring of cloud fixed it instantly.
* **The banding was wood grain** — six cycles over a mirrored latitude is twelve thin ribbons. Three
  and a half gives seven broad belts, which is what a gas giant looks like.
* Two more from the second render: spots up to **105% of the world wide** (`rv * aspect` was uncapped),
  and phantom lens-shaped "eyes" on spotless worlds, from a moisture jitter that was pinching the
  bands shut rather than wavering their edges.

Violet giants now **0.8%**, from 5%.

### `tools/survey-check.mjs`

```
world            grid       scout blk    n  dwell  hd   time     sci blk    n  dwell  hd   time
  small moon    40x20          5x5    32   3.81s  1   122s      7x7    18   3.08s  1    55s
  typical world 200x100        5x5   800   0.35s  2   273s      7x7   435   0.35s  2   124s
  gas giant     640x320        5x5  8192   0.35s  6   488s      7x7  4232   0.35s  7   222s
```

**The scout was beating the science ship.** On a 200×100 world the scout finished in 140s against the
science hull's 240s, because its smaller block gave it MORE blocks, which pushed it over the threshold
where a ship widens its sweep head, and the integer widening overshot. The class advantage had been an
emergent property of two other numbers, and that is exactly how a ladder inverts unnoticed.

It is one stated constant now (`ScienceAdvantage = 2.2`), the duration is the primary quantity
(`SurveySeconds`), and the sweep head is a **float** so `blocks × dwell ÷ speed` closes exactly on it.
The ratio is 2.20× on every world by construction. Only the marker COUNT is rounded.

**The trade this bought, stated plainly:** surveys are longer now. A fixed 7×7 makes a gas giant 4,232
bites where the old scaling made it 32, and no arrangement of dwell and head turns four thousand steps
into ninety seconds. A research hull now takes 1–4 minutes and a scout 2–8. That is the cost of the
block being literally 7×7, which is what was asked for.

---

## Not built, and why

- [!] **Gas giants needing their own settlement systems.** *"Most systems that we have for Terrestrial
      worlds should not apply to Gas Giants at all… Floating Cities and entirely new systems will need
      to be created for this."* This is a design direction rather than a change, and it is a spec of
      its own. §4F — no elevation readout, no "no resource here" — is the part of it that was
      actionable now.

## Worth knowing before you play it

- **Systems are physically much larger on screen.** A G-type's outermost ring is 204 units out and an
  O-type's about 610, against a previous maximum near 90. Ratios are unchanged, so nothing *inside* a
  system changes shape, but `CameraController`'s zoom limits, `GalaxyLOD`'s distance bands and
  `MapTierVisibility`'s thresholds were not re-tuned and may need to be. If framing is wrong, the
  fallback is `AU = 28` with the star scale at ×3 rather than ×4 — both are single constants.
- **A save taken mid-survey will resume with its revealed ground arranged slightly differently.** The
  format is unchanged (`surveyRows`, one float per row), but the block grid moved from origin-anchored
  to centre-anchored. Finished and unstarted worlds are unaffected.
- **The 84-second frame is instrumented, not proven fixed.** Two definite unyielded spans were sliced
  (the lazy tectonics/geothermal bake, which was landing inside the first terrain column, and the
  despeckle pass), and `GenProfiler` now names any span over 50 ms with the system it belongs to. If it
  recurs, the log will say where — which it could not before.
