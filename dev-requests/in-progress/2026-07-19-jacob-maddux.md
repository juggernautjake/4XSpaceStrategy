# Galaxy scale, render tiers, and the deep view — 2026-07-19 (Jacob Maddux)

**Status: Built — needs a compile pass and a play test.**

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

- **Nothing here is compiled.** There is no Unity in this environment. Every file was reviewed hard —
  three full review passes, which found and fixed 20+ real defects including several I introduced while
  fixing earlier ones — but **you are the compiler.** Treat this as needing a build and a play test.
- The changes are broad: `GalaxyLOD` was rewritten, `SystemVisualizer.CreateBlackHole` was replaced with
  a call into the shared builder (its private `MakeRing`/`UnlitMaterial` are gone), and `SpaceBackground`
  changed in several places. If it doesn't build, the error is most likely in one of those.
- **Ship rotation not built.** The offsets you asked for are already in `UnitModelRenderer.cs` verbatim
  (Science `Euler(0,90,0)`, Colony `Euler(-90,0,0)`), so re-applying them is a no-op. Worth knowing:
  `modelRotation` is only applied in the traveling branch and the parked-at-a-world branch of `TickShip`.
  A ship idling in deep space, or one that just spawned, falls through with no `else` and keeps whatever
  rotation it had. If the hulls still look wrong, that gap is a likelier cause than the angle — a
  screenshot would settle it.
- Several fixes traded one behaviour for another and are worth a look in play: the galaxy-view pick
  spheres now grow with zoom but cap at 2.5×, and the empire ring was reduced from 2.1× to 1.3× the proxy
  size because at wide zoom the old radius swept past neighbouring systems.
