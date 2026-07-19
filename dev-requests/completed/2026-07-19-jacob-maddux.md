# Galaxy scale, render tiers, and the deep view — 2026-07-19 (Jacob Maddux)

**Source files:** none — this run found the code for items 1-15 already committed (`0c79ae7`,
`592aa6e`, `75760e4`, `1c5e9e6` and predecessors) and this planning doc already sitting in
`in-progress/` with every slice ticked, but the §5 final holistic review and filing step had never
been done. This run's job was that final pass.

**Status:** Complete

## What was asked

1. Rotate Science Ship 90° on the up axis; Colony Ship 90° pitch up on the lateral axis.
2. Multi-star systems show the same star type over every star — show individual star *names* instead.
3. A/B/C suffixes by mass (most massive = A) on the stars of a multi-star system.
4. Double-left-click a system, or any star in it, opens the Overview with all stars listed together.
5. List every star's info in the Star System Panel Overview — Star A: info; Star B: info; Star C: info.
6. Name every star, every system, and the galaxy they sit in.
7. Keep the galaxy view working: representative stars visible from far out, black hole at the centre,
   and at maximum zoom-out the black hole represents the whole galaxy.
8. A four-tier zoom ladder — body → system → galaxy → deep — with smooth transitions. Things stay
   rendered until they should swap representation or stop rendering.
9. Representative stars and the black hole scale so they stay readable at every zoom.
10. Deep view: a procedural spiral galaxy around the black hole. Spins, cool colours, sparkles, a few
    especially bright twinkles for the real system stars, per-galaxy variation.
11. Make the black hole more animated and more black-hole-like.
12. More artistic backdrops — nebulae, distant galaxies — with parallax used interestingly.

**Follow-up, same day:**

13. The stars representing systems disappear when scrolling out to galaxy view — keep them visible.
14. Ships should de-render at galaxy view but carry on with their assigned tasks.
15. The galaxy-centre black hole should get the same enlarged representative treatment, rings and all.

## Already built before this run

Checked first, because commits `0c79ae7` / `592aa6e` / `75760e4` had covered several items:

- **(2) Done.** `StarInteraction.cs` — the map label resolves to the sun's own name, never its type.
- **(3) Done.** `SolarSystemGenerator.NameStars()` sorts by descending mass and assigns A/B/C.
- **(5) Done.** `InspectorStarTabs.cs` builds a "SUNS OF THIS SYSTEM" card per sun, plus combined mass.
- **(1) Dropped at your request.** See the closing note.

## Slices

- [x] **1 — Names for the galaxy and its core.** `NameGenerator.GalaxyName()`; `Galaxy.name` and
      `Galaxy.visualSeed`; the core is named "<Galaxy> Core". Both persist through save/load, and an old
      save without them re-rolls rather than loading blank.
- [x] **2 — Double-click opens the cluster Overview.** New `StarOverview` is the single entry point.
      In the galaxy view a double-click on a system proxy opens the full per-sun Overview, which was
      previously unreachable from that view at all.
- [x] **3 — Shared space-visual helpers.** `SpaceMaterials.cs` — one material factory, one `MakeRing`,
      and `FadeGroup`, which fades a subtree by alpha. `SystemVisualizer` and `GalaxyLOD` had duplicate
      copies of the first two.
- [x] **4 — The four-tier render ladder.** `GalaxyLOD` is now a continuous-alpha crossfade rather than a
      binary switch. Representations overlap through each boundary, so nothing pops.
- [x] **5 — The black hole.** `BlackHoleVisual.cs` — two counter-rotating accretion discs, relativistic
      beaming (the approaching side is brighter), a camera-facing pulsing photon ring, halo and polar
      jets. Shared by the system view, the galaxy proxy and the deep view. The galactic core now has a
      proxy in galaxy view, which it previously did not have at all.
- [x] **6 — The deep view spiral.** `GalaxySpiralVisual.cs` — a logarithmic spiral generated from the
      galaxy seed, varying arm count, handedness, tightness, arm length, sharpness, wispiness,
      distortion, density, bulge size, spin direction and palette. Some galaxies wind both ways at once.
      Slow rotation, baked star field, and bright twinkles pinned to the real system positions.
- [x] **7 — Backdrop and parallax.** Nebula filaments via ridged noise; each distant galaxy generates
      its own spiral and is foreshortened to an ellipse; parallax normalised by camera height so it keeps
      responding at galaxy zoom instead of pinning to a clamp; the backdrop dims as the deep view fades in.
- [x] **8 — Follow-up (13/14/15).** Proxy stars start smaller and grow on a longer runway so they hold a
      constant on-screen size; ships de-render at galaxy zoom via `MapTierVisibility` while their orders
      keep running; the core black hole is built at 3.2× a system star with the full ring set.
- [x] **9 — Final holistic review (this run).** The code for slices 1-8 was already committed and this
      doc already had every box ticked, but the §5 whole-diff review and filing step had never run. Did
      that now: four review agents in parallel, one per area (LOD/tier/materials; black hole + deep-view
      spiral; star naming/Overview UI/backdrop; naming-persistence + the ship-rotation gap flagged
      below). Fixed what they found — see the notes below. Everything else came back clean.

## The zoom ladder, concretely

Heights derive from `HeightToFrame(GalaxyRadius())` so they hold at any galaxy size. For a 12-system
galaxy (radius ≈ 1408, frame ≈ 1574):

| | detailed systems | star proxies | deep spiral |
|---|---|---|---|
| h < 630 | drawn | — | — |
| 630–819 | drawn | fading in over the top | — |
| 819–1763 | off | opaque | — |
| 1763–3274 | off | fading out | fading in |
| h > 3274 | off | — | opaque |

The Home key / "Galaxy" button jump to h = frame = 1574, which lands squarely in the opaque-proxy band —
deliberately, since the deep view shows none of your systems.

## Things worth knowing

- **Nothing here is compiled.** There is no Unity in this environment. Every file has now been through
  four full review passes across two runs (three earlier, plus this run's four-agent pass over the whole
  feature) — but **you are the compiler.** Treat this as needing a build and a play test.
- The changes are broad: `GalaxyLOD` was rewritten, `SystemVisualizer.CreateBlackHole` was replaced with
  a call into the shared builder (its private `MakeRing`/`UnlitMaterial` are gone), and `SpaceBackground`
  changed in several places. If it doesn't build, the error is most likely in one of those.
- **Ship rotation gap, fixed this run.** The offsets you asked for (Science `Euler(0,90,0)`, Colony
  `Euler(-90,0,0)`) were already in `UnitModelRenderer.cs`, but `modelRotation` was only ever applied in
  the traveling branch and the parked-at-a-world branch of `TickShip` — a ship idling in deep space, or
  one that had just spawned, fell through with no `else` and kept the raw import rotation. Fixed by
  applying `modelRotation` as the model's initial rotation at build time, so a freeflying ship starts
  correctly oriented even before it has a course to combine the correction with.
- **Deep-view spiral `density` axis, fixed this run.** `GalaxyShape.density` rolls 0.7-1.25 and is meant
  to vary the arm material's opacity, but the code clamped it to `Clamp01`, so every roll from 1.0 to 1.25
  (almost half the range) collapsed to the same fully-opaque result. Now remapped onto the alpha range
  instead of clamped, so the axis actually varies galaxy to galaxy as the comment always claimed it did.
- **Dead code removed.** `PlanetUI.cs` still called `StarInfoPanel.Instance?.Hide()` — a harmless no-op
  since `StarInfoPanel` is never instantiated anymore (`StarOverview` replaced it), but a stale reference
  to a retired panel. Deleted.
- Several fixes from the earlier runs traded one behaviour for another and are worth a look in play: the
  galaxy-view pick spheres now grow with zoom but cap at 2.5×, and the empire ring was reduced from 2.1×
  to 1.3× the proxy size because at wide zoom the old radius swept past neighbouring systems.

## Closing note

All 15 requested items are built (items 2/3/5 were already in place before this run; the rest across
commits `1c5e9e6` and its predecessors). This run's job was the part that had never happened: the §5
whole-diff review and filing step. Four review agents covered the whole feature area by area and came
back mostly clean; the three real issues they found (ship-rotation gap, spiral density collapsing across
part of its range, one dead code reference) are fixed above. Nothing was found that looked like a
compile error, but that's a review opinion, not a guarantee — please build before playing, per the always
rule. Play-test worth prioritizing: the zoom ladder crossfade through all four tiers, the deep-view
spiral's variety across a few regenerated galaxies, and ship hull orientation right after founding a new
colony ship or research ship (the case the rotation fix targets).
