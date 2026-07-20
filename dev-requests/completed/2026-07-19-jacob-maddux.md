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
      Slow rotation and a baked star field. (The separate per-system sparkle field this originally had
      was removed in slice 10 — galaxy mode's real enlarged stars now stay visible in front of the
      spiral, at their true positions, so sparkles would have doubled every system.)
- [x] **7 — Backdrop and parallax.** Nebula filaments via ridged noise; each distant galaxy generates
      its own spiral and is foreshortened to an ellipse; parallax normalised by camera height so it keeps
      responding at galaxy zoom instead of pinning to a clamp; the backdrop dims as the deep view fades in.
- [x] **8 — Follow-up (13/14/15).** Proxy stars start smaller and grow on a longer runway so they hold a
      constant on-screen size; ships de-render at galaxy zoom via `MapTierVisibility` while their orders
      keep running; the core black hole is built at 3.2× a system star with the full ring set.
- [x] **9 — Final holistic review (dev-requests bot run).** Four review agents in parallel, one per area
      (LOD/tier/materials; black hole + deep-view spiral; star naming/Overview UI/backdrop;
      naming-persistence + the ship-rotation gap flagged below). Fixed what they found — see the notes
      below. Everything else came back clean.
- [x] **10 — Two exclusive view modes (follow-up).** System mode and Galaxy mode never render at the same
      time — a system is drawn either as itself or as a marker, never both. The switch has hysteresis, and
      the enlarged stars dissolve in over 0.3s on a TIMER rather than over a height band (a height-based
      ramp leaves a band where detail is off and proxies are still invisible, and one scroll notch can
      park you in it). The enlarged stars now stay lit at full alpha inside the deep view, so the widest
      zoom shows the galaxy and where you are in it at once.
- [x] **11 — Layout and zoom range (follow-up).** Systems moved out and spread: inner ring 900 units
      (was 170), step 260 (was 95). The wheel's ceiling is now derived from the galaxy (2.4× its framing
      height) instead of the 120,000 backstop, so "fully zoomed out" is a composed shot rather than a
      dot in a void.
- [x] **12 — `Mathf.SmoothStep` misuse (follow-up).** Unity's `Mathf.SmoothStep(a, b, t)` is a smoothed
      LERP between `a` and `b`, not GLSL's `smoothstep(edge0, edge1, x)`. Four call sites passed edges to
      it. The spiral's rim fade was the worst: it attenuated the entire disc to ≤18% and inverted
      `armLength`'s meaning, so a longer-arm roll drew fainter arms. Replaced with a documented `Ramp()`.

## The zoom ladder, concretely

Heights derive from `HeightToFrame(GalaxyRadius())` so they hold at any galaxy size. For a 12-system
galaxy under the new layout (radius ≈ 3760, frame ≈ 4204):

| height | system mode | galaxy mode | deep spiral |
|---|---|---|---|
| < 1682 | drawn | — | — |
| 1682 – 4709 | off | stars dissolve in over 0.3s, then opaque | — |
| 4709 – 8746 | off | opaque | fading in |
| 8746 – 10091 (ceiling) | off | opaque | opaque |

The two modes never overlap. Home / the "Galaxy" button jump to h = 4204, which sits in clean galaxy mode
below the deep band — deliberately, since you press it to look at your systems.

The enlarged stars hold a constant on-screen size (~1.75% of screen height) from the mode switch all the
way to the ceiling, and never overlap: the closest two systems are 1339 units apart and a star is 226
units across at its largest.

- [x] **13 — Grid size, visual size, black hole, view editor (follow-up).**
      - Surface grids now derive from MASS: width ≈ 100 × mass, height always half. Mass 0.5 → 50×25,
        mass 2 → 200×100. The old formula clamped to `[96, 384]`, and the *lower* clamp was doing the
        damage — every body under mass ~2.7 got the same 96×48 grid, so a moon was routinely issued the
        same map as the planet it orbits. Moons are additionally capped at half their host's dimensions.
      - Visual size also comes from mass now (`MassRules.VisualDiameter`), via a square root so the small
        end stays readable. It used to run through `surfaceSize`, which rounds and clamps at 3 — that is
        why every moon under mass ~2.3 rendered identically.
      - Black hole simplified back to ONE accretion disc. The second counter-tilted layer read as separate
        ring sets sliding through each other, and the polar jets read as a bar across the screen.
      - Empire rings hold a minimum on-screen thickness so they stay legible at the widest zoom.
      - Systems pushed further out and further apart: inner ring 1400 (was 900), step 420 (was 260).
      - **New View Editor** (HUD "View", available in normal play): Planet / System / Galaxy zoom levels,
        zoom nudge, and rotate. The camera now has YAW — middle-drag or the panel's arrows spin the view,
        orbiting what is at screen centre rather than pivoting about the camera. Pitch stays fixed at 55°
        deliberately: it is what `HeightToFrame` solves against, and every render-tier boundary derives
        from that, so a tiltable pitch would move the zoom thresholds as you tilted.

## Things worth knowing

- **Nothing here is compiled.** There is no Unity in this environment. Every file has now been through
  many full review passes across three runs — three on the original build, the bot's four-agent pass
  over the whole feature, and three more on the two-mode rework — but **you are the compiler.** Treat
  this as needing a build and a play test.
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
- **[!] The grid top end is capped at 640 wide, not the 1000×500 you asked for at mass 10.** The mapping
  is exact up to mass 4 (400×200) and then tapers, so a mass-13 gas giant lands ~640×320 rather than
  1300×650. Not a taste call — `TerrainTile` is a *class*, so each cell is a separate heap object
  (~48 bytes), surfaces are generated eagerly for every body, and four things fall over past ~400:
  ore is saved per-cell (`GameStateSerializer.cs:145`), so a literal mass-10 world is ~25k saved ore
  cells per body through JsonUtility; `SurfaceBuildManager.FindSpot` is O((2r+1)²) per ring and runs for
  every body on every load; `CityGrowth.FindSettlementSpot`'s `tries > 4000` guard only breaks its inner
  loop; and the Survey/Power overlays are full-grid passes, the Power one repainting 4×/second.
  Raising `MapMetrics.KneeWidth` / `MaxWidth` is a one-line change once those four are fixed — they are
  the only thing holding the ceiling down. Even at 640 this is roughly a 10× rise in typical grid size,
  so watch generation and load times.
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
