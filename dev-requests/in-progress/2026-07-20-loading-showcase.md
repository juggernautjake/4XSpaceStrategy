# Loading screen showcase: the real home star(s) and homeworld forming — 2026-07-20

**Status: In progress**

## What was asked

While the galaxy generates, the loading-screen preview should stop being a generic placeholder and
instead show **the actual home system being born**:

1. **Stars.** Generate a star with effects, then show its NAME. This is the real star the home system
   ends up with — not a stand-in.
2. **Multiples.** If the home system is binary, a second star pops out of the first after a few seconds
   and the two begin orbiting each other. If trinary, a third pops out too and all three orbit.
   Only occasionally — most home systems stay single.
3. **Homeworld.** The planet phase shows the actual homeworld, starting as a barren grey rock and
   morphing tile by tile into its final surface — as if the terrain sliders were being applied live,
   slowly, so the player watches the world develop.
4. **Every species.** Whatever the player picked, the world that forms is the one hospitable to them.

## The ordering problem, which decides everything else

The home system's identity is currently settled **last**, not first:

- `GalaxyGenerator.Finish` → `ForceHomeWorld` is what actually assigns the home star and rebuilds the
  home world, and `Finish` runs *after every system has been generated* (`GameManager` reports it at 78%).
- So at the moment the preview wants to show "the star being born", the real answer does not exist yet.
- Worse, `ForceHomeWorld` hard-assigns **one** sun (`home.stars = new List<StarData> { Get(G or K) }`)
  with the comment "a pleasant single sun so the home always has a stable habitable zone". There is no
  binary or trinary home system to show today — that has to be *added*, not just displayed.

Everything below follows from that: the home system has to be **decided early**, published somewhere the
loading screen can read, and then honoured by `Finish` rather than overwritten by it.

## Slices

- [x] **1 — Decide the home system up front.** Extract the home-star roll out of `ForceHomeWorld` into a
      `HomePlan` produced during `Begin`: the star cluster (1–3 suns, named), and the species the world
      must suit. `ForceHomeWorld` consumes the plan instead of rolling its own sun, so what the loading
      screen shows and what the player lands in are the same object by construction, not by coincidence.
- [x] **2 — Binary/trinary home systems.** Allow the home cluster to roll 2 or 3 suns at a low rate.
      This is a **gameplay change, not a cosmetic one**: `ForceHomeWorld` guarantees a ≥85% habitable
      cradle, and habitability is computed from the combined star. Needs the guarantee re-verified
      against a cluster's combined luminosity, and the home world re-placed against the cluster's zone.
      `[!]` If that guarantee cannot be held for a trinary, cap the home system at binary rather than
      shipping a home world the difficulty setting promised and generation cannot deliver.
- [x] **3 — Star showcase in the preview.** Replace the placeholder sphere for `Subject.Star` with the
      real cluster: correct colour and relative size per spectral class, emissive, slowly spinning.
      Show the star's name under the preview.
- [x] **4 — The pop-out.** Second sun emerges from the first after a beat, third after another, then the
      cluster orbits its barycentre. Reuses `StarCluster.Layout`, which already models exactly this
      (binary about a mass-split barycentre; trinary as a close inner pair plus a third), so the loading
      screen shows the same arrangement the system view will.
- [x] **5 — Homeworld morph.** Show the home world as barren grey rock, then interpolate its surface
      toward the real generated one over the planet phase. Terrain is deterministic from `terrainSeed`,
      so the end state is known up front; the morph is a per-tile reveal from a barren baseline toward
      it, not a second generator.
- [x] **6 — Species correctness.** Slice 1 carries the selected species into the plan, so the world that
      forms is the one that will exist. Verify across all five species — the cradle's type and climate
      come from `BestTypeFor(species)` and `species.idealTemp`.

## Notes and risks

- **Nothing here is compiled.** There is no Unity in this environment.
- Slice 2 is the one that changes the GAME rather than the loading screen. It should be built and
  reviewed on its own, because a home system that is occasionally binary changes early-game habitability,
  the habitable-zone visualiser, and the "home always has a stable zone" assumption other code may hold.

### Slice 2 findings

`Galaxy.homeStar` became `Galaxy.homeStars` (a list), with `homeStar` kept as a read-only "first sun"
convenience getter so existing callers (the loading screen's single-sphere preview) didn't need to
change this slice. `GalaxyGenerator.Begin` now rolls a `RollHomeCluster()` — ~86% single / ~10% binary /
~4% trinary, same shape as an ordinary system's cluster odds — and `ForceHomeWorld` consumes the whole
list, combining it with `StarDatabase.Combine` (the same call any multi-star system already makes) and
naming the suns A/B/C by mass exactly like `SolarSystemGenerator.NameStars` does elsewhere.

**Why the `[!]` cap-at-binary fallback wasn't needed:** the real load-bearing guarantee is that the home
world's `habitability` number is **force-set from difficulty** (`GameConfig.HomeHabitability()`,
locked via `habitabilityLocked`, and `Recompute` skips locked bodies) rather than computed from
`Habitability.Rate` against the star, so the guarantee never depended on the star's luminosity to begin
with — it holds by construction for any cluster size. The G/K-only restriction on home suns is still
worth keeping (a home cradle orbiting a lethal O/B giant would read as a narrative non-sequitur even
though nothing mechanically breaks), but it is not what protects `hasHabitableZone`: `StarDatabase.Combine`
sets `hasHabitableZone = true` unconditionally for any multi-star cluster regardless of spectral type,
same as it already does for an ordinary system's binary/trinary. Orbit clearance already generalizes too: `OrbitSafety.StarRadius` reads
`combinedStar.clusterRadius` (set by `StarCluster.Layout`, the same geometry the renderer uses), and
`ForceHomeWorld` already re-runs `OrbitSafety.EnforceSystem` after resizing the home world, so a wider
cluster reach is accounted for whether it's 1, 2, or 3 suns. Verified by reading `StarDatabase.Combine`,
`Habitability.GetZone`, `OrbitSafety.EnforceSystem/StarRadius`, and the save/load path
(`GameStateSerializer` already loops `sys.stars` generically) — none of them special-case a single-star
home system, so none needed to change.

**Not touched, out of scope:** the home planet's `orbitSpeed` is set once during the system's original
(non-home) generation and is only recomputed by `EnforceSystem` if its radius actually has to move to
clear the new cluster. A home planet that already had enough clearance keeps an `orbitSpeed` computed
against the system's original star roll, not the forced home cluster. This is a pre-existing inconsistency
(it predates this slice — the single forced G/K sun could already differ from the system's original roll)
and is not something slice 2 was asked to fix.

### Slice 4 findings

`LoadingScreen` now takes the whole cluster (`SetHomeCluster`, called with `galaxy.homeStars` from
`GameManager`) instead of one star. Sun 0 reuses the existing single-sphere path; two more sphere
GameObjects (built once in `BuildPreview`, reused across every generation) only ever activate for a
binary/trinary home. Position/orbit math reuses `StarCluster.Layout` — the SAME geometry the real
in-game system view builds its stars from — scaled by one `clusterScale` factor (derived from
`layout.reach`) so the whole cluster fits the tiny preview regardless of how wide-set the real orbit
is. A trinary's inner pair (`StarCluster`'s own `stars[0]`/`stars[1]` convention) orbits a shared
`pairPivot` Transform that itself swings around the barycentre — Unity's own transform-hierarchy
composition does the "orbit the orbit" math for free.

`GameManager` now holds the Star subject open (`WaitForSecondsRealtime`, sized from `LoadingScreen.
PopBeat`/`PopGrow`) right after the home system's bodies are generated, BEFORE the caption switches to
Subject.Planet — otherwise the pop-out would have no guaranteed real time to actually play before the
screen moved on.

**Two real bugs caught by review, both fixed and re-verified:**
1. `pairPivot` (the trinary inner pair's shared orbit centre) was never reset to zero when leaving the
   Star subject, so once it drifted, every OTHER subject's sphere (which reuses the same GameObject,
   parented under `pairPivot` for a trinary) rendered off-centre for the rest of the load. Fixed by
   reparenting sun 0 back onto `previewStage` directly and zeroing `pairPivot` in `ResetSunCluster`,
   and re-establishing the pairPivot parenting only when Star is re-entered (`ApplyClusterParenting`,
   called from `BeginSunCluster`).
2. `Subject.Star` is reported once per star system, not just the home system — so the pop-out reveal
   was replaying (mostly truncated) on every later system's turn instead of playing once. Fixed with a
   `clusterRevealed` flag: the first time it plays out fully, later entries into the Star subject show
   the cluster already fully popped and resume its orbit from wherever it was, rather than restarting.

Both fixes were re-verified by a follow-up review pass (see commit body for review-agent count).

**What I couldn't verify:** exact on-screen pixel sizing of a trinary's suns in the 120x120 preview —
`ClusterFrameRadius` (62 preview-units) is derived from real formulas and checked against the camera's
frustum mathematically, but I can't render this to see how large the smallest sun in a tight trinary
actually reads at that scale. May want a look in-editor and a `ClusterFrameRadius` tweak if it reads
too small.

### Slice 5 findings

`LoadingScreen.SetHomePlanet(CelestialBody)` builds a small FIXED 40x20 texture (`MorphW`/`MorphH`) —
independent of the real body's own grid, which can be 200x100+ — by downsampling the ALREADY-GENERATED
`surface.tiles` (a nearest-tile lookup, never a re-sample of the noise field). It starts every tile at
`TerrainColorMap.Get(TerrainType.Barren)` and reveals `MorphDuration` (2.4s) worth of tiles toward the
real finish, in a FIXED shuffled order (`morphOrder`, built once via `System.Random` — deliberately never
`UnityEngine.Random`, so this purely cosmetic reveal can't perturb the shared gameplay RNG stream
`FactionAI`/the generators keep drawing from immediately afterward). `GameManager` calls it right after
`GalaxyGenerator.Finish()` — the earliest point the real homeworld surface exists — and holds the Planet
subject open long enough (`MorphDuration + 0.4f`) to actually see the reveal finish, mirroring the star
cluster's own hold.

**One real bug caught by review, fixed:** `SetHomePlanet` mutates the Planet subject's persistent, shared
Material in place (real texture + white tint) and nothing ever reverted it. Since `LoadingScreen` is a
singleton that outlives any one game, starting a SECOND galaxy in the same session would show the FIRST
game's finished homeworld texture during the second game's own early "generic placeholder" phase, before
its own `SetHomePlanet` call replaces it again near 78%. Fixed with `ResetPlanetPlaceholder()` — puts the
Planet material back to its flat placeholder colour with no texture — called once at the start of every
new generation (`Open()`), before the new generation's placeholder phase can ever be seen. Re-verified by
a follow-up review pass (see commit body for the count).

**Noted, not a bug:** the fixed `morphOrder` reveal SEQUENCE is the same for every planet shown, in every
game, all session long (only the revealed COLOURS differ, since those come from the real terrain) — a
deliberate tradeoff for keeping this off the gameplay RNG stream, not a correctness issue.

### Slice 6 findings — pure verification, no code change

Traced by hand for all five species (`SpeciesDatabase.Build`):

| Species | `BestTypeFor` result | Why | `idealTemp` → heat (`Lerp(0.55,1.7,idealTemp)`) |
|---|---|---|---|
| Terrans | RockyPlanet | affinity 1.0, highest | 0.50 → 1.13 (temperate) |
| Aquarii | OceanPlanet | affinity 1.0, highest | 0.42 → 1.03 (mild) |
| Pyrothians | VolcanicPlanet | affinity 1.0, highest | 0.85 → 1.53 (hottest of the five) |
| Cryithn | IcePlanet | affinity 1.0, highest | 0.16 → 0.73 (coldest of the five) |
| Sylvans | OceanPlanet | tied 0.9 with Rocky; Ocean wins on array order (checked first in `BestTypeFor`'s candidate list) | 0.55 → 1.18 |

Every species lands on the body type its own habitat text describes, and the resulting heat correctly
tracks hot-to-cold across the five in the same order their descriptions imply (Cryithn coldest, Pyrothians
hottest). The Sylvans tie is a deterministic array-order tie-break, not a bug — their habitat text names
both rocky and watery worlds, so either outcome is textually defensible, and the code is consistent
(same tie resolves the same way every time, not a coin flip).

Confirmed the species itself can't be stale or wrong by the time `ForceHomeWorld` reads it:
`GenerationMenu.StartGame()` calls `SpeciesManager.Select(selectedSpecies)` synchronously, BEFORE
`GameManager.GenerateGalaxyAsync` even starts the coroutine, and nothing else calls `SpeciesManager.
Select`/mutates `CurrentIndex` during generation — so `SpeciesManager.Current`, read fresh by
`GalaxyGenerator.Finish(galaxy, SpeciesManager.Current, count)` after the whole system loop, is
guaranteed to be the same species the player actually picked, for every one of the five.
