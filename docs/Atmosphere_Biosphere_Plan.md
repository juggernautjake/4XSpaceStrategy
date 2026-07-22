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

## Not done, and why

* **Gas-giant-specific buildings.** Noted in the spec as future work; nothing here assumes them.
* **Tectonic *intensity*.** The spec itself flags that per-world tectonic strength does not exist,
  so the atmosphere bonus is a 1..2 spread — but made **deterministic per world** from `terrainSeed`
  rather than a fresh random each call, or a world's stated maximum atmosphere would flicker
  every frame the UI drew it.

## Slice 10 — Reefs are alive — **BUILT**

The spec's "very high water levels ... can spawn algae in its oceans or coral reefs". Reef was
already a terrain type and already generated in warm shallows — but from **depth and temperature
alone**, so a sterile ocean world came out ringed with coral that nothing had ever grown. Reefs
are now gated on `biosphereActive`, the same flag that decides whether the land grows anything,
so the sea and the land tell the player the same story about whether a world is alive.
