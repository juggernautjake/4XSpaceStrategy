# Fleet Command — squadrons, formations, protocols and patrols

**Opened:** 2026-08-20
**Requested:** in session, alongside the Aquarii fleet work
**Companion to:** `2026-08-20-session-backlog.md` (the flat ledger) and `2026-08-19-ship-art-and-vfx.md`

The ask, in the requester's words: *different formations depending on which ships are selected; bind
groups of ships and quick-select them with the number keys; add ships to a group, break a group up,
break a single ship off, or promote a sub-selection into its own group; a formation menu with
defensive and offensive formations; formations where cheaper ships screen the larger and more
expensive ones; squadron protocols so fighters engage on sight, or a scout runs home at the first
sign of trouble and raises the alarm; patrol routes between two or more points; buttons and UI for
all of it, with tooltips explaining each.*

Legend: **[x]** built · **[~]** partly built · **[ ]** queued

---

## STATUS — 2026-08-22: **complete**

Every item in this document is built. It got there in two steps on the same day, and both are worth
recording because the second would not have happened without the first.

**Step one — the boxes were never ticked.** Most of this shipped across `9f45cdd` (the bar),
`d1364fd` (battle orders and the 25 symbols) and `a71d7e5` (the four open items), and nobody came back
to mark it. An unticked spec reads as a backlog: the next person to open this file would have rebuilt
a formation set that had been in the game for two days. Corrected by reading the code item by item
rather than trusting the doc or the commit messages — which is also how the real gaps were found,
because a claim of "built" checked against the source either holds or does not.

**Step two — the seven that were genuinely missing.** They were the ones this doc's own §8 ordered
last, and every one of them was an ergonomic addition rather than a load-bearing part:

| item | what it needed |
|---|---|
| **1.4** Rename | `Squadrons.Rename` and `Fleets.Rename` both existed with **no caller**. Now `UI/NamePrompt.cs` — one modal prompt serving both tiers, reached from a Name button on the squadron and fleet sections of the bar |
| **5.3** Ping-pong | Implemented in `StepLeg` and saved since day one, and unreachable: `PatrolTool` hardcoded `Loop`. Now a Loop/Shuttle control beside Patrol, switchable mid-patrol, and the route preview stops drawing the closing leg for a shuttle |
| **7.1** Rally on rollout | The retreat half was wired; the arrival half was not. `Reinforce()` in `AdvanceBuild` now puts a finished hull into its squadron and sends it to the rally point |
| **7.2** Strength readout | `Squadrons.StrengthOf` — attack and hull SUMMED, speed and range MINIMISED. A second header line on the command bar, and the squadron tooltip in the roster |
| **7.3** Regroup | `SquadronAI.Regroup` — a move order to the squadron's own centre of mass, which re-forms it without committing it to going anywhere |
| **7.4** Reinforce | `BuildOrder.squadron`, stamped at queue time and saved. A destination row above the catalogue in both shipyard UIs, and the destination shown on each queue row |
| **7.5** Slowest-ship warning | Falls out of 7.2: the pace-setter is NAMED in amber, and only when one hull is genuinely the outlier (under 75% of what the rest would make) |

Three further items from `2026-08-21-indexes-survey-and-solar.md` §T were closed in the same pass —
the Screen's slot 0, the pinnable formation preview, and the combat weave's handover. See
`2026-08-22-closing-the-backlog.md` for all of it, and for the two defects the work turned up.

---

## 0. What already exists, so it is not rebuilt

- [x] **Control groups 1-9** (`Systems/ControlGroups.cs`). `Ctrl+N` binds the selection, `N` selects
      and flies the camera to the group, `Shift+N` adds the group to the selection. Groups hold unit
      **ids** (so a dead ship cannot resurrect through a reused slot), prune dead members on read, and
      are saved and loaded with the game
- [x] **Selection** (`Systems/UnitSelection.cs`) — additive select, toggle-off, single-unit deselect
- [x] **Move orders and the targeting preview** (`Visual/FleetMovementController.cs`) — dashed path,
      live destination, `Shift` for the predicted intercept, right-click context menu, order queueing
- [x] **An order queue per ship** (`Unit.orders`, `OrderKind`), pausable
- [x] **Combat** (`Systems/CombatManager.cs`) — anything within `MaxEngagementRange` of a hostile
      opens fire and keeps firing until one side is gone or out of range
- [x] **The travelling wedge** (`Visual/FleetFormation.cs`) — one formation, applied to every fleet
- [x] **The anchorage ring** — ships parked at a world fan out around it

The gap is that a fleet has no IDENTITY beyond "the ships currently selected". A formation, a stance
and a patrol route are all properties of a STANDING GROUP, so that is the thing to build first.

---

## 1. Squadrons — a group that remembers what it is

Control groups become **squadrons**: the same 1-9 slots, plus per-slot state.

- [x] **1.1** Per-squadron state alongside the member list: `name`, `formation`, `protocol`,
      `patrolRoute`, `rallyPoint`. Saved and loaded with the group
- [x] **1.2** **Membership is exclusive.** A ship belongs to at most one squadron. Binding it into a
      new one removes it from its old one. Without this, "the squadron's formation" is ambiguous the
      moment a ship is in two squadrons at once, and every question below inherits the ambiguity
- [x] **1.3** Editing the roster, all of it available from both keys and buttons:
      - **Bind** — replace squadron N with the selection (`Ctrl+N`, exists)
      - **Add** — add the selection to squadron N without disturbing its other members
        (`Ctrl+Shift+N`)
      - **Detach** — remove the selection from whatever squadron it is in, leaving the rest
        (`Ctrl+Alt+N`, and a button); this covers "break a single ship off" — select one, detach
      - **Split** — promote the current sub-selection into the first free squadron slot (`Ctrl+M`).
        This is the "select a group of ships within a larger group and turn it into a different
        group" case, and it is one keystroke because it is the common one
      - **Disband** — empty the squadron, leaving the ships selected and unassigned
- [x] **1.4** Renaming a squadron, so "Home Guard" and "Survey Wing" beat "3" and "5"
- [x] **1.5** The squadron number keeps showing on the unit icon (already drawn from `GroupOf`)

---

## 2. Formations

A formation is a function from (slot, fleet composition, course, progress) to an offset. The existing
wedge becomes one entry in a set.

- [x] **2.1 Wedge / Arrowhead** *(exists, becomes the default)* — leader ahead, pairs sweeping back.
      Balanced; good for moving
- [x] **2.2 Line Abreast** — one rank, all beams bearing on what is ahead. **Offensive**: maximum
      guns on the target, maximum exposure
- [x] **2.3 Line Astern / Column** — single file. Narrow frontage through a contested lane; the ship
      in front takes what comes
- [x] **2.4 Echelon** — a diagonal stagger, port or starboard. For coming at something from an angle
      without masking your own fire
- [x] **2.5 Screen** — **the composition-aware one the request is really about.** Cheap hulls form an
      arc AHEAD of the line; the expensive ships sit behind it. See §3
- [x] **2.6 Globe** — escorts on a sphere around the capitals, above and below as well as around.
      **Defensive**: no open bearing, at the cost of concentrating nothing
- [x] **2.7 Free** — no formation; every ship flies its own line. The honest "get out of my way"
      option, and the fallback for a one-ship squadron
- [x] **2.8** Formations scale their spacing to the LARGEST hull in the squadron, so a dreadnought
      pair is not drawn interpenetrating at fighter spacing
- [x] **2.9** Every formation keeps the existing form-up / close-up ramp: a squadron leaves its
      anchorage as a knot, spreads on the crossing, and draws together as it brakes in

### 3. Who stands where — the screening rule

- [x] **3.1** Rank each ship by **what it costs to lose**: `costMetal + costEnergy`, raised for hulls
      the player cannot easily replace (`minShipyardLevel`, `minEmpireLevel`) and for the ones that
      cannot defend themselves (`attack == 0` — colony ships, terraformers, science vessels)
- [x] **3.2** Cheap and armed goes to the exposed slots; expensive, slow or unarmed goes to the
      protected ones. In **Screen** that is literally in front; in **Globe** it is the outer shell
- [x] **3.3** A squadron of one KIND of ship falls back to the plain geometric order, because there is
      nothing to protect and a screen of dreadnoughts by dreadnoughts is theatre
- [x] **3.4** Show it: the formation menu draws a live diagram of the CURRENT squadron in the
      highlighted formation, each dot sized and coloured by its role. This is the difference between
      a menu of six words and a menu a player can actually choose from

---

## 4. Protocols — what a squadron does without being told

The stance is asked once per squadron and then honoured every tick.

- [x] **4.1 Aggressive** — engage any hostile that comes into sensor range, and pursue it. What a
      fighter wing is for
- [x] **4.2 Defensive** *(the default)* — engage what comes to you; do not chase. Hold station
- [x] **4.3 Hold Fire** — never initiate. For slipping past something you cannot beat
- [x] **4.4 Evade and Report** — **the scout protocol.** On contact: break off, run for the rally
      point, and raise a notification naming what was seen and where. A scout's job is to come back
- [x] **4.5 Escort** — stay with a nominated squadron or ship, matching its speed and screening it
- [x] **4.6 Return When Damaged** — a hull-fraction threshold; below it a ship detaches and heads for
      the nearest friendly world to sit out the fight rather than dying for nothing
- [x] **4.7** Protocols are per-squadron with a per-ship override, because the one wounded cruiser
      that should go home is a per-ship decision

---

## 5. Patrols

- [x] **5.1** A patrol route: an ordered list of waypoints, each a body or a point in space
- [x] **5.2** Laid down by clicking waypoints in sequence with the patrol tool armed; the route draws
      as a closed dashed path while it is being built, exactly like the move preview does now
- [x] **5.3** **Loop** (…3→1→2→3→1…) or **ping-pong** (1→2→3→2→1→2…)
- [x] **5.4** A patrol is a standing order: it re-queues itself, so it runs until cancelled rather
      than falling off the end of the queue
- [x] **5.5** The protocol still applies on patrol — an Aggressive patrol hunts, an Evade-and-Report
      patrol is a picket line that raises the alarm and runs
- [x] **5.6** `OrderKind.Patrol` is **appended** to the enum, never inserted: the ordinal is serialized

---

## 6. The UI

- [x] **6.1 A Fleet Command bar**, shown whenever ships are selected: squadron chips 1-9, the
      formation menu, the protocol menu, patrol, and the roster buttons from §1.3
- [x] **6.2** Squadron chips show the number, the member count and the squadron's colour; the active
      one is lit. Click selects, and the roster buttons act on the clicked squadron
- [x] **6.3 The formation menu** — each entry a name, the live diagram from §3.4, and a tooltip
- [x] **6.4 The protocol menu** — same shape, each with a tooltip saying exactly what the ships will
      do without further orders
- [x] **6.5 A tooltip on every control**, since a formation menu whose entries are six unexplained
      words is a menu nobody touches twice
- [x] **6.6** Every keyboard binding is also a button, and every button names its key

---

## 7. Things not asked for that belong here anyway

- [x] **7.1 Rally point** — a per-squadron destination that newly built ships fly to on rollout, so a
      shipyard feeds the front instead of piling hulls over the capital
- [x] **7.2 A formation-strength readout** — total attack, total hull, the slowest ship's speed and
      the shortest range in the squadron, which is what actually governs the whole group
- [x] **7.3 "Regroup"** — one button that pulls a scattered squadron back into formation at its
      centre of mass, for after a fight
- [x] **7.4 Reinforce** — send newly built ships of a class straight into a named squadron
- [x] **7.5 The slowest-ship warning** — flag when one hull is holding an entire squadron back, since
      the shared travel time is computed from exactly that ship
- [x] **7.6 Formation and protocol are saved**, or a reloaded game quietly forgets how the fleet was
      told to fight

---

## 8. Order of build

1. Squadron state and the roster editing (§1) — everything else hangs off it
2. The formation set and the screening rule (§2, §3), validated by rendering the geometry to a PNG
   and looking at it, since there is no Unity in this environment
3. Protocols (§4), driven off the existing `CombatManager` contact test
4. Patrols (§5)
5. The Fleet Command bar and both menus, with tooltips (§6)
6. The extras in §7, in the order they turn out to matter

## 9. Standing constraints

- **Nothing here compiles in this environment.** `tools/Check-Scripts.ps1` is a structural tripwire,
  not a compiler
- `UnitType`, `OrderKind` and every other serialized enum is **append-only**
- Formation offsets are a DRAWING concern and stay out of the simulation: combat, range, arrival and
  the save all keep reading the fleet's one shared course
