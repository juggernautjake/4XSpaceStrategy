# Atmosphere, BioSphere, Indexes & Moon Identity — Build Plan

Spec received 2026-07-22. Nine slices. Status is marked per slice as it lands.

**Standing caveat: there is no Unity install in this environment. Nothing below has been
compiled or run. Review is the only check any of it has had.**

---

## The one decision everything else hangs off

Atmosphere is currently `atmosphereThickness`, a 0..1 abstraction. The spec asks for
**Atmospheres** — a real quantity where Earth is 1 and a gas giant is ~10, driven by Mass.
Those are different units, and ~30 call sites read the old one.

Rather than rewrite all of them, `atmospheres` becomes the stored, player-facing truth and
`atmosphereThickness` becomes a **derived 0..1 property** over it:

```
thickness = clamp01(atmospheres / 7)
```

Calibration is not arbitrary. A mass-3 world (the new default planet mass) previously got
`0.15 + surfaceSize*0.03` = **0.42** thickness. Under the new model it has 3 atmospheres,
which maps to **0.43**. Every existing consumer — the greenhouse term, the terrain
classifier, the homeworld biosphere fix — keeps the tuning it already had. 7+ atmospheres
saturate, which is the right shape: a greenhouse does not keep scaling linearly forever.

---

## Slice 1 — Atmosphere as a real quantity — **BUILT**

* `CelestialBody.atmospheres` (float, stored). `atmosphereThickness` becomes derived.
* `CelestialBody.hasMagneticField` (bool, stored).
* `AtmosphereRules` rewritten around Mass:
  * ceiling = `mass`, **halved** with no magnetic field
  * active tectonics add **1..2** whole atmospheres (deterministic per body, from `terrainSeed`,
    so it survives save/load and the sandbox's Randomize)
  * heat drives **boil-off**, and tolerance scales with mass: mass 4+ holds its air to ~2.2 on
    the temperature slider; a mass-1 world starts losing it just past temperate
  * asteroids hold nothing, ever
* Magnetic field roll: mass >= 2 gets good odds (0.55 -> 0.90), below 2 much worse
  (0.02 -> 0.20). Gas giants always. Asteroids never.
* Save/load round-trip, with migration from thickness-only saves.
* `Atmospheres: N` on the Overview beside Mass; Dev sandbox slider + Magnetic Field toggle.

## Slice 2 — BioSphere ceiling — **BUILT**

The spec's formula, implemented literally:

```
temperatureTerm = 1 - |temperatureSlider - 1|      (clamped at 0)
ceiling         = (waterLevel + temperatureTerm) / 2
```

Worked example from the spec — water 0.7, temperature 1.8 -> term 0.2 -> **ceiling 0.45**.
This is a unit test in `AtmosphereRulesTests` reasoning, not just prose.

* Hard gate: **below 0.6 atmospheres there is no biosphere and no surface water**, whatever
  the sliders say. Both fade toward 0 as atmosphere falls under that line.
* The Dev sandbox BioSphere slider clamps to the ceiling and says which term is capping it.
* High water + liquid-water temperature seeds **algae/reef** tiles rather than land plants.

## Slice 3 — Solar Index — **BUILT**

* 1 atmosphere = 100% efficiency. Each atmosphere above removes 20% (2 atm -> 80%,
  4 atm -> 20%). At 5+ it is worthless.
* Below 1 atmosphere, every 0.1 under adds 10% — a 0.5-atm world runs at 150%.
* **Poles beat the equator** (inverted from the old model, which favoured the equator).
* Solar Array is **removed from the build menu** at 5+ atmospheres and returns when
  terraforming brings the air down to 4.

## Slice 4 — Weather Index — **BUILT**

* `SurfaceIndexKind.Wind` renamed **Weather** everywhere it is shown.
* Atmosphere drives it: no air, no weather. Severity scales with atmospheric mass, so a thick
  world is violently stormy and a thin one is dead calm.
* Overlay opacity now carries severity rather than being flat.

## Slice 5 — Moon identity — **BUILT**

Moons are named for what their surface actually is, with `Moon` as a suffix:
*Rocky Temperate Moon*, *Ocean Moon*, *Barren Moon*, *Volcanic Moon*, *Ice Moon*.
Driven by the moon's rolled surface type, which already exists — it was simply never shown.

## Slice 6 — Species atmosphere ranges — **BUILT**

* `Species.minAtmospheres` / `maxAtmospheres`.
* Terrans 1..4. Pyrothians 5..9. Aquarii 1..3. Cryithn 0.8..3.5. Ranges deliberately overlap.
* Habitability reads them, and the terraform diagnosis reports "air too thin / too thick"
  against the *current* species rather than a fixed ideal.

## Slice 7 — Magnetic field terraforming — **BUILT**

`Core Ignition` and `Magnetospheric Shield` already existed as projects hung off a
`NoMagnetosphere` problem that nothing ever set from real data. They now:

* set `hasMagneticField = true` on completion, which **doubles the atmosphere ceiling**
* are offered based on the actual flag rather than on body type

## Slice 8 — Moon terrain variety — **MOSTLY ALREADY PRESENT**

Honest accounting: `RollMoonType` already rolled moons through the same temperature +
Water Level draw planets use, gated on mass — a large moon in the temperate band could already
come out Ocean, Rocky or Barren, and a hot one Volcanic, a cold one Ice. What was missing was
that **nothing ever showed it** (Slice 5) and that a moon's **air was capped by a special moon
rule** rather than by its mass.

Both are now fixed. `AtmosphereRules.ForMoon` is deleted; a moon's atmosphere comes from its
mass on identical terms to a planet's, which is what lets a big moon of a gas giant end up
thicker-aired than Earth — the Titan case the spec asks for.

No further change was made to the type roll itself, because it already implements what the
spec describes.

## Slice 8b — Two ordering bugs found while wiring this — **FIXED**

Both pre-existing, both surfaced because atmosphere now depends on heat:

* `MakeBody` rolled atmosphere before `BiasHeat` set the world's real, distance-driven climate,
  so a scorching inner world kept air it should have boiled off. The roll moved to the caller.
* The **forced-habitable world** (the one each system is guaranteed) calls `SeedTerrain` after
  being retyped, which rebuilds `terrainParams` from scratch and **discarded the `BiasHeat`
  already applied**. The one world promised to be livable was the one world whose temperature
  ignored its own orbit. `BiasHeat` and `CaptureNatural` are now re-run after the re-seed.

## Slice 9 — Atmosphere consequences elsewhere — **BUILT**

* `Atmospheres` and `Magnetic field` rows on the planet Overview and the inspector, sitting
  directly beside Mass because Mass is what sets them.
* Habitability multiplies by the species' atmosphere fit — floored at 0.35 rather than zeroed,
  because a badly-pressured world is a domed-city problem, not a nonexistent place.
* Solar Array and Wind Farm descriptions rewritten to match the new rules. The Solar Array's
  also pointed at the **wrong index** ("check the Weather Index") — a pre-existing bug.
* Save migration handles all three save generations.

---

---

# WHAT IS ACTUALLY LEFT (as of 2026-07-22)

Four real items, in the order they matter.

## 1. Build Mode's UI — the biggest gap

The whole *foundation* is built and pushed: `SurfaceBuildQueue` (per-world queues, pause,
reorder, cancel, refunds), `SurfaceLabor` (Labor generated by Capitol/Housing/Depot, capped and
consumed per job), `BuildScaling` (5% cost/time and 10% output per extra block), and drawn
footprints stored per building (`cellsX/cellsY`, `SizeMult`).

**What does not exist is the interface to drive any of it.** There is no click-drag to paint a
footprint, and no queue widget to see or reorder jobs. Right now the systems are reachable only
from code. This lands in `PlanetViewWindow.cs`, which is ~4,900 lines, so it wants its own pass.

## 2. Retire the dead preview stage — ~700 lines in `LoadingScreen.cs`

`Finale`, `AlignToReal`, `RestoreAfterFinale`, `MoonPreview` and the RenderTexture rig are the
OLD intro: a private stage at (-200000, 0, 0) rendered to a texture and cross-faded into the real
game. `GenesisSequence` films the real bodies now, so none of it runs.

**Deliberately still there.** It is the fallback if the new intro does not boot on your buddy's
machine, and deleting it before the replacement is confirmed working would remove the only way
back. Delete it once the new intro is seen running.

## 3. `SurfaceBuildQueue` does not survive save/load

Currently `Clear()` refunds every in-flight job, so a player who saves mid-construction gets
their materials back but loses the queue. That is the honest interim — nobody is robbed — but it
is not the behaviour you want. Needs a DTO and round-trip.

## 4. Smaller Build Mode findings, from the earlier review

Each is real and none is urgent: Demolish and level-up costs do not scale with tile count;
the `queues` dictionary holds deleted worlds; `SurfaceLabor.Invalidate()` is missing at four
mutation sites; `MaxHealth` ignores `TileCount`; PowerGrid reach does not grow with a drawn
building's size.

---

## Not done, and why

* **Gas-giant-specific buildings.** Noted in the spec as future work; nothing here assumes them.
* **Tectonic *intensity*.** The spec itself flags that per-world tectonic strength does not exist,
  so the atmosphere bonus is a 1..2 spread — but made **deterministic per world** from `terrainSeed`
  rather than a fresh random each call, or a world's stated maximum atmosphere would flicker
  every frame the UI drew it.

## Slice 11 — Star scale, and finding the habitable world — **BUILT**

The complaint: a system's only habitable planet could sit ten times further out than its
siblings, off the edge of the screen and findable only through the menus.

**The cause was not the zone — it was that nothing else knew where the zone had gone.** The
habitable zone scaled with `sqrt(luminosity)`, correctly: an O-type is 50,000x the Sun, so its
zone sat at 223x Earth's orbit. But the planet LAYOUT did not scale with luminosity at all —
every system started a few units out and stepped ~20 at a time, regardless of its star. Around
a bright star the zone was simply past the outermost planet.

Fixed by giving both the same source of truth, `StarDatabase.FluxScale`:

* the exponent is compressed 0.5 -> **0.30** and clamped to **0.55 .. 3.0**, squeezing the range
  from 500:1 to about 5:1 — which is what reins in the O and B giants specifically
* the same scale multiplies **planet spacing**, so a star that pushes its zone out pushes its
  planets out with it, by construction
* every system is also spread **18% wider** regardless of star, floored so dim systems never
  come out tighter than before
* the zone band itself widened from 0.95–1.37 to **0.80–1.55** so it is visible and two
  neighbouring worlds can share it

Result, checked numerically: the zone now lands **inside** the planet range for every spectral
class, and the in-lane relocation `EnsureHabitableWorld` performs always has room to succeed.

Three other copies of the raw flux law — `TempReference`, `OrbitSafety.OrbitLimits`, and the Dev
star editor's cluster recombine — were each recomputing it independently and are now routed
through the same function.

## Slice 12 — Telling the player what kind of star they are looking at — **BUILT**

Aimed at players who do not know what an "O-type" is:

* **Deeper blue at the hot end.** A real O-type is only faintly blue-white, which on screen is
  indistinguishable from an A-type. Exaggerated for legibility, deliberately.
* **`SubtleTint` is now temperature-dependent.** A flat 50% pull to white was washing out exactly
  the stars that most needed to look different; Sun-like stars still go near-white, the extremes
  keep two-thirds of their saturation.
* **Size spread widened** from 2.4–5.0 to 1.9–8.0, and the per-star variance NARROWED from
  0.72–1.38 to 0.85–1.18 — previously a big M dwarf could out-size a small B giant, so size was
  noise rather than signal. The class bands no longer overlap.
* **A corona halo**, sized and faded by luminosity. Emission alone could not carry this: bloom has
  a threshold, can be switched off, and does not widen as the star shrinks, so zoomed out a giant
  and a dwarf collapsed into two dots differing only in tint.
  * Caught during the build: the halo is a child of the star transform, so a flat multiple grows
    in absolute terms with the star — at 3.1x an O-type's halo had a 24.8-unit radius against an
    innermost orbit of ~15, **swallowing its first two planets**. Now clamped to stay inside the
    clearance generation reserves, verified for every class.
* **A plain-language line** on the star panel — "a giant, blazing deep blue" — so the words and the
  picture teach each other.

## Slice 13 — Status line clipping — **FIXED**

`UISanityGuard` caught the Planet View status line needing 41px in a hard-coded 30px box, with
the bottom line cut off. It had grown to describe an index, the power grid and the world-wide
weather or solar ceiling at once. It now measures its own content and the map's bottom edge moves
with it — and only re-measures when the text actually changes, since it runs every frame.

## Slice 10 — Reefs are alive — **BUILT**

The spec's "very high water levels ... can spawn algae in its oceans or coral reefs". Reef was
already a terrain type and already generated in warm shallows — but from **depth and temperature
alone**, so a sterile ocean world came out ringed with coral that nothing had ever grown. Reefs
are now gated on `biosphereActive`, the same flag that decides whether the land grows anything,
so the sea and the land tell the player the same story about whether a world is alive.
