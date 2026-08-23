# Closing the backlog — 2026-08-22

> *"Please go ahead and build out anything else we have started or that is pending. I want everything
> to be built. Make sure the user controls for everything are exposed and are intuitive."*
>
> *"Please do whatever needs to be done with the 27 script files that have no .meta. I want to build
> this as professionally and as optimally as possible."*

Everything that was open and buildable in this environment is now built. What remains open at the
bottom of this document is open because it needs Unity, or a decision, and each says which.

---

## A. The seven fleet-command items

`2026-08-20-fleet-command.md` had 43 unticked boxes. Reading the code item by item found 36 of them
already built and never marked, and 7 genuinely missing — all of them the ergonomic tail the spec's
own build order put last. That doc now reads `complete`; this is what the seven took.

- [x] **A1. Naming, for squadrons AND fleets.** `Squadrons.Rename` and `Fleets.Rename` both existed
      with no caller — written, saved, loaded, and unreachable. `UI/NamePrompt.cs` is one modal prompt
      serving both tiers: Enter accepts, Escape abandons, the field is focused and fully selected on
      open so replacing "Squadron 3" is one keystroke, and clearing it goes back to the number.
- [x] **A2. Patrol: Loop or Shuttle.** `PatrolMode.PingPong` was implemented in `StepLeg` and
      serialized from the day patrols landed, and `PatrolTool` hardcoded `Loop`. It is now a control
      beside Patrol, switchable **mid-patrol**, and the route being drawn stops showing a closing leg
      when the mode is Shuttle — that leg is the entire difference between the two, and drawing one
      the squadron will never fly shows the player the wrong shape while they are choosing.
- [x] **A3. Reinforce, and the other half of the rally point.** `BuildOrder` carries a destination
      squadron, stamped when the order is QUEUED and saved with it. On rollout the hull joins that
      squadron and flies to its rally point. Stamped-at-queue rather than read-at-completion is the
      load-bearing choice: queue three fighters for the Home Guard, switch the yards to a survey wing,
      and the fighters must not follow the change.
- [x] **A4. The strength readout.** `Squadrons.StrengthOf` — attack and hull **summed**, speed and
      range **minimised**, because a group travels at its slowest ship and turns back at its
      shortest-ranged one. Four sums would be actively misleading.
- [x] **A5. The slowest-ship warning** falls out of A4 and is the reason it was worth building. The
      pace-setter is NAMED in amber rather than reported as a number, because the fix is a player
      action — detach it — and only when one hull is genuinely the outlier (under 75% of what the rest
      would make), so the line stays four quiet figures until there is something to say.
- [x] **A6. Regroup.** A move order to the squadron's own centre of mass. The formation does the rest
      for free: `FleetFormation.Offset` closes the stations up on arrival, so ordering a squadron to
      its own middle IS the order to re-form — without committing it to going anywhere, which every
      other way of tidying a scattered squadron would have done.

## B. The three from 2026-08-21 §T

- [x] **B1. The Screen's point ship.** The arc filled left-to-right and slots arrive cheapest-first,
      so the cheapest hull took a WINGTIP and a mid-priced one took the point — which is the station
      that meets the enemy first. The arc now fills from its middle outwards.

      **The Globe had the same fault** and nobody had noticed: its shell was walked from angle zero,
      which is the BACK of the formation, so the cheapest escort sat behind everything it was
      protecting. It starts at the front now. Found by looking at the picture
      `tools/formation-check.mjs` draws, not by reading the code — the gold slot-0 ring was at the
      bottom of the Globe cell and there is no way to see that in source.
- [x] **B2. Pinning the formation preview.** Right-click a formation button. The pin OUTRANKS the
      hover rather than replacing it — hovering another formation still previews that one, and leaving
      the button falls back to the pinned one — because comparing two of them is the only reason
      anybody pins the first. Selecting a different squadron drops the pin, or the map keeps drawing
      the last squadron's stations while the player commands this one.
- [x] **B3. The weave's handover.** A ship arriving under fire brakes from 10-16 u/s to zero, and
      crossing speed is the largest term in every firing solution against it. The weave was gated on a
      boolean — `status != Traveling` — so it began ramping in only once the ship had already stopped,
      leaving a hole at the exact moment a fleet arrives in a fight.

      It now cross-fades against the hull's own crossing speed: full weave below 1.5 u/s, off above 6.
      `flight-model-check.mjs` sweeps the transition and asserts both ends of it — that the total never
      dips below what the hull makes parked (worst point 4.27 u/s against 4.27 parked), and that it is
      fully off well below cruise so nothing is double-counted.

## C. The 27 scripts with no `.meta`

Unity identifies an asset by the GUID in its sidecar, not by its path. A script with no sidecar is not
broken — the editor writes one on import — but every checkout that imports the project invents a
DIFFERENT GUID for the same file, and from then on those checkouts disagree about the identity of
every script in the list. The moment one is dragged onto a prefab or a scene object, that reference is
a GUID nobody else has.

- [x] **C1.** `tools/make-script-metas.mjs`, because this will happen again — every script written in
      this environment arrives without one. The GUID is the **MD5 of the asset path**, so the tool is
      byte-deterministic: run it twice and the second run is a no-op, run it on two machines and they
      agree. A random GUID would mean two people fixing the same gap produce two different answers.
- [x] **C2.** Checked first that nothing referenced them. `SampleScene.unity` names ten project
      scripts by GUID and all ten already had sidecars, which is what made assigning fresh ones safe.
      A collision against a GUID already in the project is checked for rather than assumed away.
- [x] **C3.** Scripts only. Art and meshes have importer blocks whose SETTINGS matter — filter mode,
      compression, read/write — and inventing those from nothing is a different and much riskier job
      than stamping an identity on a source file. The command icons are still sidecar-less like the 25
      that preceded them, and that is fine: they are loaded by path through `Resources.Load`, so no
      GUID is involved.
- [x] **C4. And then they were taken straight back out, which is the actual lesson.** The sidecars
      landed here and Jacob's next pull aborted: *"untracked working tree files would be overwritten
      by merge"*. His Unity had ALREADY imported those 27 scripts and generated its own sidecars for
      them, untracked — and git will not clobber an untracked file.

      The check I ran before writing them was the right check and I ran it against the wrong tree. I
      confirmed nothing in `SampleScene.unity` or the prefabs referenced the new GUIDs, which was true
      and is why deleting either set is safe; what I did not establish is whether a working editor had
      already answered the question somewhere I cannot see. **This checkout has no Unity, so "no
      sidecar here" never meant "no sidecar anywhere."**

      Reverted the same day. The rule that came out of it: **where an editor's sidecars exist, they
      win.** They are what its Library database is keyed to, they carry real importer settings, and
      they are the ones that should be committed — from the machine that has Unity, for every asset
      under `Assets/`, not just the scripts. `make-script-metas.mjs` stays for the case it was written
      for: a script with no sidecar and no editor anywhere to make one.

## D. Optimisation

- [x] **D1. `ControlGroups.Members` was members x units.** For each member id it scanned the whole
      fleet. It is the hottest read in the fleet code — `FormationPreview` asks EVERY FRAME while a
      formation button is hovered, `SquadronAI` asks once a second for all nine squadrons, and the
      command bar asks nine times on every selection change. Ten ships against a late-game fleet of
      two hundred was two thousand comparisons to answer a question about ten ships, sixty times a
      second. One pass with a set lookup is units + members, and allocates nothing but the list it
      returns (the scratch collections are reused).
- [x] **D2. The preview cached its roster.** Even at O(n), asking sixty times a second for the same
      ten ships is waste. Re-read on a quarter-second interval, and immediately when the squadron
      changes — because hovering along a row of formation buttons must never show the last squadron's
      ships.
- [x] **D3. A leaked static subscription.** `FormationPreview` now unsubscribes from
      `ControlGroups.OnChanged` in `OnDestroy`. A static event outlives the object that subscribed to
      it, so without this, reloading a save leaves a destroyed MonoBehaviour on the event and every
      squadron change afterwards throws.

## E. Two defects found on the way

- [x] **E1. Typing in ANY text field fired the game's hotkeys.** Every fleet shortcut is a bare letter
      or digit — `1`..`9` recall squadrons, `T` concentrates fire, `H` holds position, `O` opens the
      roster — and `ControlGroupInput.Update` had no guard at all. **Naming a save file "Fleet 1"
      recalled squadron 1 and flew the camera to it.** This predates the rename prompt; the prompt
      would merely have made it constant.

      Two copies of the right test already existed, in `CameraController` and `PlanetGridVisualizer`,
      and the handler that most needed it had none. It is `UIFactory.IsTypingInField()` now, there is
      one of it, and the two copies were folded into it.
- [x] **E2. Escape would have dismissed the prompt AND opened the pause menu.** Both listen for it and
      Unity does not order the two `Update`s. `NamePrompt.SwallowsEscape` stays true for the remainder
      of the frame the prompt closes on, which is the half that matters — without it the bug fires
      about half the time and looks random.

## F. What the controls look like now

The command bar carries **30 icons across two rows**, split by urgency rather than by category:

* **Top — what you reach for under fire.** The nine squadron chips, the roster verbs (Form, Detach,
  Disband, **Name**), the battle orders (Focus, At will, Hold, Withdraw), and the fleet-wide versions
  of the same (Select, **Name**, All fire, Fleet out). The fleet block moved up here in this pass:
  "break off the whole fleet" belongs beside "break off this squadron", not beside the formation menu.
* **Bottom — what you set once and leave.** Seven formations, six protocols, and the standing orders:
  Patrol, **Loop/Shuttle**, **Regroup**, Rally, **Reinforce**.

Every one of them has a tooltip that says what the ships will DO, and `tools/verify-command-ui.mjs`
enforces both halves of that — every control has a symbol, every symbol exists as a file, and every
control explains itself. The five new symbols were drawn in the existing idiom and checked on the
contact sheet at **24 pixels as well as 72**, which caught the first version of `Order_Regroup`
sharing a silhouette with `Act_Disband`: four marks around a centre, in both cases. It has a hollow
middle and orthogonal arms now, against Disband's ring and diagonal ones — a plus and an X stay
distinguishable long after the marks inside them stop being readable.

Reinforce is exposed in **three places**, because it is one setting answering a question asked in
three contexts: on the squadron bar (thinking about squadrons), above the shipyard catalogue in both
the inspector tab and the full window (thinking about building), and on every queue row (which hull is
going where — two ships on one list can be bound for different squadrons).

---

## G. Still open, and why

Nothing here is buildable in this environment.

- [ ] **The LOD tail** — mesh quantization, baked tangents, and eyeballing the LOD thresholds. All
      three need Unity to confirm; see `2026-08-22-ship-lod-and-rendering.md` §K.
- [ ] **An impostor level below `_lo`** — a hull under a few pixels still draws 2,200 triangles.
- [ ] **The remaining ship art**: Pyrothian, Cryithn and Sylvan are 0/29, Terran 2/29 re-rolled. Needs
      the Meshy key and credits, which only Jacob has.
- [ ] **The 2026-08-20 session backlog's art items** — asteroids, derelicts, artifacts, enemy
      factions. Same blocker.
- [ ] **Civ colour choice at race selection (B5), rival empire marks (B13), mid-game recolour (B14)** —
      built as far as `CivLivery` and `CivIdentityPanel`; the remaining pieces are placement decisions
      about a screen that does not exist yet.
- [ ] **The three decisions from the Planetary Generation 2.0 audit** — the globe/map texture
      unification (20 MB), the 4-atmosphere boiling point contradiction, and what class the homeworld's
      orbital station should be. All three are Jacob's call, not mine, and are written up in
      `2026-08-19-planetary-generation-2.0-audit.md`.

**None of this was compiled.** There is no Unity here. `tools/Check-Scripts.ps1` ran clean over 228
files across all five of its checks, and glyphs, command-UI, wiring, ballistics, survey, separation,
formations, flight-model and ship-LOD all pass — but those are tripwires, not a compiler.
