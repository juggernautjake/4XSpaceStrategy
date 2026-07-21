# The loading handoff — from bar to homeworld, without a cut

Filed 2026-07-20. Covers Jacob's request: when generation finishes, the bar collapses and blinks out,
the planet and its moons slide to where the bar was, the text reads "Welcome to <homeworld>", that
fades, and the game begins already centred on the real homeworld at planetary zoom.

The stated goal underneath it: **the planet on the loading screen should BE the planet in the game.**
Not a matching one — the same model, handed over without a cut.

---

## 0. What we have, and the one hard problem

Today the loading screen's preview is a private stage: a sphere, two corona quads, a key light and a
camera at `(-200000, 0, 0)`, rendered to a `RenderTexture` and shown in a `RawImage`. The real galaxy
is built by `SystemVisualizer` in world space and framed by `CameraController`. They are two separate
renderings of two separate objects, and `Close()` currently just switches one off.

**The hard problem is that these are different projections.** The preview is an orthographic-feeling
close-up at a fixed 4.6x distance with its own light; the game camera sits wherever
`CameraController` puts it, looking at a body whose scale is set by `SystemVisualizer`. You cannot
tween one into the other by moving a RawImage, because at the end of the move the pixels have to
become a real object at a real world position.

Two candidate approaches:

**A. Cross-fade at the moment of alignment.** Move the RawImage to screen centre, and simultaneously
drive the real camera to frame the real homeworld so that the real planet lands at the same screen
position and the same apparent size. Then fade the RawImage out over ~0.3s while the real planet is
already drawn underneath it. The handoff is invisible because for a few frames both are on screen,
identical, in the same place.

**B. Reparent the preview stage into the world.** Physically move the preview sphere to the real
body's position and scale, then swap it for the real one.

**A is the right answer.** B fights `SystemVisualizer` for ownership of the body and has to reconcile
two lighting setups; A needs only that the two images agree for a few frames, which is a framing
calculation we control on both sides. A also degrades gracefully: if the alignment is slightly off,
the result is a soft dissolve rather than a visible jump.

The preview and the real body already share their appearance path — both take terrain from the same
`body.surface` via `TerrainColorMap` — so "the same planet" is already true in substance. This plan
makes it true in presentation.

---

## 1. The sequence

Seven beats, all on unscaled time, all interruptible by nothing (the player has no input yet).

| # | Beat | Duration | What happens |
|---|---|---|---|
| 1 | **Settle** | 1.4s | Reveal is finished; planet and moons simply turn. Bar sits at 100%. |
| 2 | **Collapse** | 0.45s | The bar's fill and track scale horizontally to zero from the centre, easing in. Percentage label fades. |
| 3 | **Blink** | 0.18s | A one-frame bright flash at the collapsed point, then the whole bar assembly's alpha snaps to zero. |
| 4 | **Travel** | 0.9s | The preview RawImage moves from its left-hand slot to screen centre — specifically to where the bar's midpoint was — easing out, growing ~1.35x. Moons keep orbiting throughout. |
| 5 | **Welcome** | 1.6s | "Welcome to <homeworld>" fades in beneath the planet. Headline dots are gone by now. |
| 6 | **Handoff** | 0.5s | The game camera is already framing the real homeworld. The welcome text and the RawImage cross-fade out; the real scene is revealed underneath, identical. |
| 7 | **Release** | — | `TimeControl.Resume`, input enabled, `LoadingScreen.Close()`. |

Total tail: ~5.0s after the moons have finished, which replaces the current "Generation complete" /
"Entering solar system" pair.

---

## 2. Camera: landing on the homeworld at planetary zoom

`CameraController` needs a "be here, now, no tween" entry point plus the existing focus behaviour:

- `CameraController.SnapFocus(Transform target, float distance)` — set position and rotation directly,
  no smoothing, so beat 6 has something stable to reveal.
- Distance chosen so the real planet's apparent size matches the preview's at the end of beat 4. This
  is a computed value, not a magic number: given the preview camera's FOV and distance and the
  RawImage's final on-screen size, solve for the world distance that puts the real body at the same
  screen height. Put that solve in one function with the derivation in a comment, because it is the
  one number that makes or breaks the illusion.
- The zoom tier must be **planetary** (`MapTierVisibility` / `GalaxyLOD` in system mode, not galaxy
  mode) so the player can immediately zoom out through system → galaxy.

The camera must be positioned **before** beat 6 begins, during beats 4–5, while the loading panel
still covers the screen. Nothing about the move should be visible.

---

## 3. What has to change

- **`LoadingScreen`** — new `Finale(CelestialBody home, System.Action onDone)` coroutine driving beats
  1–6; bar collapse/blink animation; preview travel; the welcome label; alpha fade of the panel that
  leaves the preview until last.
- **`LoadingScreen.Close()`** — must no longer be the thing that ends the load; `Finale` owns the tail
  and calls `Close` at the end.
- **`GameManager.GenerateGalaxyRoutine`** — replaces the current closing captions and holds with a
  call to `Finale`, and moves `TimeControl.Resume` / the `onDone` callback to after it.
- **`CameraController`** — `SnapFocus`, and the apparent-size solve.
- **`SystemVisualizer` / `MapTierVisibility`** — guarantee the home system is in system-tier LOD and
  the homeworld's visual exists before beat 6.
- **`GenerationMenu`** — it currently passes `TimeControl.Resume` as the completion callback; that
  still works, it just fires later.

---

## 4. Slices

**Slice 1 — the bar collapse and blink.** Beats 1–3 only. The screen still closes the old way
afterwards. Small, self-contained, immediately visible.

**Slice 2 — the travel and the welcome.** Beats 4–5. The preview moves to centre and the welcome text
appears; the screen then closes as before. Still no camera work — this is pure UI and can be judged
on its own.

**Slice 3 — the camera solve.** `SnapFocus` plus the apparent-size derivation, positioning the real
camera on the real homeworld during beats 4–5. Verified by closing the screen instantly at the end of
beat 5 and checking the planet is already where the preview was.

**Slice 4 — the cross-fade handoff.** Beat 6. The two images overlap and dissolve. This is the slice
where the illusion either works or doesn't, and slices 1–3 exist so that this one has nothing else in
it.

**Slice 5 — release and polish.** Beat 7, LOD tier guarantees, and a pass over easing curves and
timings with the whole sequence running end to end.

---

## 5. Risks

- **Aspect ratio.** The preview render target is square; the screen is not. The apparent-size solve
  must work from viewport height, not width, or the alignment drifts on ultrawide.
- **Lighting mismatch.** The preview has its own key light at a fixed angle; the real planet is lit by
  its actual star. At the moment of cross-fade the terminator may sit differently. Mitigation: aim the
  preview key light along the real star→planet vector once `SetHomePlanet` is called, so the two
  agree before they ever have to match.
- **The moons.** The preview's moons are cosmetic spheres at cosmetic radii; the real ones are at real
  orbital positions. They will not line up. Either accept that the moons pop into their true orbits
  under cover of the dissolve, or drive the preview moons to the real relative geometry during beat 4.
  Recommend the latter, deferred to slice 5 — the planet is what the eye tracks.
- **Load time floor.** This tail adds ~5s on top of an already ~20s showcase. Worth confirming that is
  wanted on a small galaxy.
