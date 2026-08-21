# Indexes on the map, block surveying, solar rules, gas giants — 2026-08-21

Requested by Jacob, with `Index_icons_16x16/resource_icons_16x16/` (five 16x16 PNGs) and a screenshot
of the game booting to nothing but a black hole.

Everything below is verbatim intent, broken into slices. Ticked items are built and pushed.

---

## A. The boot crash — done first, because nothing else could be tested

- [x] **A1.** `Can't add 'VerticalLayoutGroup' to Content` → `NullReferenceException` →
      **no start menu, no HUD, no escape menu, no generation menu.** `UIFactory.ScrollView` already
      puts a `VerticalLayoutGroup` on the content it returns, and `FleetRosterPanel.Build` added a
      second to change the spacing to 3. Unity permits one `LayoutGroup` per object and `AddComponent`
      does not throw — it logs and returns **null**, which the next line dereferences
- [x] **A2.** The exception unwound out of `FleetRosterPanel.Create` and aborted `GameBootstrap.Init`
      **four fifths of the way down its list**. `StartMenu`, `EscapeMenu`, `GenerationMenu` and the
      whole `GameHUD` are created after that line, so none of them ever existed. That is the entire
      explanation for the screenshot
- [x] **A3.** `UIFactory.VerticalLayout` now reuses an existing group instead of adding a second
- [x] **A4.** Every one of the 59 constructions in `Init` is isolated, logged by name on failure. They
      are genuinely independent, so a broken panel costs one panel and not the game
- [x] **A5.** Audited the other nine `AddComponent<...LayoutGroup>` calls in the tree — the rest add
      to freshly created objects and are fine

---

## B. Indexes become map furniture, not a tab

> *"I want to put them as Icons lining the top right corner of planet Grid Maps... My goal is to have
> all the Icons be the buttons themselves, and can be toggled on and off by clicking on the icons."*

- [x] **B1.** Use the supplied art. Five icons map cleanly onto five of the six index kinds:
      `Minerals→Mineral`, `Geothermal→Geothermal`, `Fertility→Fertile`, `Weather→Wind`, `Solar→Solar`.
      **There is no Water icon** — one is drawn to match (same 16x16 palette-limited style, deep blue
      droplet), because Water is a real index on any world with surface liquid and a bar with a hole
      in it is worse than a bar with one drawn icon
- [x] **B2.** Icons line the **top right corner of the grid map**, and they ARE the buttons
- [x] **B3.** Click to toggle. **Multiple active at once**, highlights allowed to overlap
- [x] **B4.** **All index highlights drop to 40% opacity** so overlaps stay readable
- [x] **B5.** An active icon gets a **square border in that index's brightest highlight colour**
- [x] **B6.** Only shown if the body has had a **level 2 survey, or is part-way through one** and is
      currently unveiling that index's highlights
- [x] **B7.** **Indexes not present on a world stay invisible** — no icon at all, unchanged rule
- [x] **B8.** **Moons get their own bar** in the moon view panes, on the moon's own survey state
- [x] **B9.** The survey tab is no longer the way to switch an index on

---

## C. Level 1 stops crawling row by row

> *"Currently by going one line at a time it creates a lot of lag trying to update all the black grids
> disappearing in real time."*

- [x] **C1.** Reveal a **block at a time** instead of a cell at a time
- [x] **C2.** **Scout 5x5, 4.0s per block. Science ship 7x7, 3.5s per block.** Science ships always
      keep the advantage over other hulls
- [x] **C3.** Block size is the **technology hook** — better survey tech means a bigger block, and
      later possibly a shorter block time
- [x] **C4.** Order: run **right**, wrap to the far left, finish that block row; then take the row
      **above or below at 50/50**, and alternate above/below from there
- [x] **C5.** Keep the white "being surveyed" highlight but **size it to the block**, and make it
      **pulsate** (alpha up and down over time)
- [x] **C6.** Smaller bodies still finish faster, larger ones stop lagging
- [x] **C7.** Indexes switch to the same block method
- [x] **C8.** Skip indexes a body does not have (already true; must stay true under the new scheme)

---

## D. The veil

> *"Lets also revert from having a solid black grid covering the whole map and instead go with a black
> transparent grid like before."*

- [x] **D1.** Back to a **transparent** veil rather than the solid blackout
- [x] **D2.** On bodies with an atmosphere, the veil **takes the atmosphere's colour**
- [x] **D3.** **Thicker atmosphere → higher opacity** (terrestrial worlds only)
- [x] **D4.** The block currently being surveyed **fades out over the survey time** — a cloudy veil
      losing opacity across the full 3.5s (or 4.0s), so the block is clear by the moment the sweep
      moves on
- [x] **D5.** Gas giants get a veil too, matched to their own colour

---

## E. Solar index, rewritten

- [x] **E1.** **Gas giants carry no index information at all**
- [x] **E2.** **Solar does not exist on terrestrial worlds with atmosphere 3 or above**
- [x] **E3.** At atmosphere ~2, solar appears as **clustered, focused, uncommon spots**, and is
      **better toward the poles** on high-atmosphere worlds
- [x] **E4.** Solar quality **falls with distance from the host star(s)**
- [x] **E5.** At atmosphere 1 and below solar spans **much larger areas** — the lower the atmosphere
      the more of the surface it covers, and a near-airless world close to its star can be **almost
      entirely covered at values up to 100**

---

## F. Gas giants

- [x] **F1.** A transparent veil matched to the giant's own atmosphere colour
- [x] **F2.** **Colour variants** — they are all the same tan-orange today. Add deep blue hues, darker
      red hues, and **rarely** purple

---

## G. What the measurements changed

Two of the numbers in this request could not be taken literally, and both were caught by porting the
model to Node and looking at the output rather than by reading the source. See
`tools/survey-check.mjs`, which prints the table below and draws the reveal order as a heat map.

**A literal 5x5 block cannot cost 4 seconds on every world.** Grids here run from 40x20 to 640x320 —
a 256x range of area. At a literal 5x5 and 4s, a 200x100 world is 800 blocks, which is **53 minutes**,
against the 10-90 seconds the rest of the game is balanced on. So the block time is exactly what was
asked for and the block GROWS with the world instead, anchored so the ratio between the two hulls is
preserved everywhere — a science ship's patch is always visibly bigger than a scout's, which is the
part the player actually reads.

| world | grid | scout block | blocks | scout | science block | science |
|---|---|---|---|---|---|---|
| small moon | 40x20 | 10x10 | 8 | 32s | 14x14 | 21s |
| large moon | 80x40 | 17x17 | 15 | 60s | 24x24 | 28s |
| small planet | 120x60 | 23x23 | 18 | 72s | 32x32 | 28s |
| typical world | 200x100 | 32x32 | 28 | 112s | 45x45 | 53s |
| large world | 400x200 | 51x51 | 32 | 128s | 72x72 | 63s |
| gas giant | 640x320 | 72x72 | 45 | 180s | 100x100 | 98s |

**Surveys are now longer than they were**, and that is inherent in the request rather than a mistake:
3.5-4 seconds a block is a slower pace than the old per-cell crawl, which finished a typical world in
26 seconds. A typical world is now 112 seconds for a scout and 53 for a science ship. That reads as
paced and watchable rather than instant — which is what the request is describing — but it IS a
change of about 4x, and if it plays as too slow the single knob is `TargetBlocks` in `Survey.cs`.

**Science ships come out 1.5x-2.6x faster than scouts** on every world, which is the 7x7-at-3.5s
against 5x5-at-4s ratio surviving the scaling.

## H. Notes and open questions

- **Nothing here is compiled.** `tools/Check-Scripts.ps1` is a structural tripwire, not a compiler.
- The block-reveal state is deliberately kept in the **existing per-row fill array** rather than a new
  per-block structure. A block reveal is still a contiguous run along each of its rows, so the saved
  shape, the save format and the migration path are all unchanged — and two ships with different block
  sizes on one world cannot desynchronise, because fills only ever increase.


## I. Not done, and why

- **The white marker does not follow a ship that is mid-flight to the block.** It frames the block
  being worked, which is what was asked for; there is no separate travelling indicator.
- **Gas giant colour variants are a tint, not new banding.** The generator's bands, storms and great
  spot are structure and survive untouched — only the hue moves. Five variants, weighted so
  ammonia-tan stays the common case and violet is one in twenty.
- **Two ships with different block sizes on one world** work separate bands and may briefly overlap at
  a boundary. Harmless by construction: fills only ever increase, so the slower ship finds ground
  already uncovered and moves past it. Nothing can un-reveal.
