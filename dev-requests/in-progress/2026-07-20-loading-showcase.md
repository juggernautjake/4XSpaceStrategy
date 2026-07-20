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
- [ ] **2 — Binary/trinary home systems.** Allow the home cluster to roll 2 or 3 suns at a low rate.
      This is a **gameplay change, not a cosmetic one**: `ForceHomeWorld` guarantees a ≥85% habitable
      cradle, and habitability is computed from the combined star. Needs the guarantee re-verified
      against a cluster's combined luminosity, and the home world re-placed against the cluster's zone.
      `[!]` If that guarantee cannot be held for a trinary, cap the home system at binary rather than
      shipping a home world the difficulty setting promised and generation cannot deliver.
- [x] **3 — Star showcase in the preview.** Replace the placeholder sphere for `Subject.Star` with the
      real cluster: correct colour and relative size per spectral class, emissive, slowly spinning.
      Show the star's name under the preview.
- [ ] **4 — The pop-out.** Second sun emerges from the first after a beat, third after another, then the
      cluster orbits its barycentre. Reuses `StarCluster.Layout`, which already models exactly this
      (binary about a mass-split barycentre; trinary as a close inner pair plus a third), so the loading
      screen shows the same arrangement the system view will.
- [ ] **5 — Homeworld morph.** Show the home world as barren grey rock, then interpolate its surface
      toward the real generated one over the planet phase. Terrain is deterministic from `terrainSeed`,
      so the end state is known up front; the morph is a per-tile reveal from a barren baseline toward
      it, not a second generator.
- [ ] **6 — Species correctness.** Slice 1 carries the selected species into the plan, so the world that
      forms is the one that will exist. Verify across all five species — the cradle's type and climate
      come from `BestTypeFor(species)` and `species.idealTemp`.

## Notes and risks

- **Nothing here is compiled.** There is no Unity in this environment.
- Slice 2 is the one that changes the GAME rather than the loading screen. It should be built and
  reviewed on its own, because a home system that is occasionally binary changes early-game habitability,
  the habitable-zone visualiser, and the "home always has a stable zone" assumption other code may hold.
- Slice 5's cost matters: the home world's grid can be 200×100+ cells, and the morph must not re-run
  terrain generation per frame. The reveal should read from the finished surface, not regenerate it.
