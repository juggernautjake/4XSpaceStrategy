# UI Consolidation & Full-Screen Planetary Viewer — Jacob Maddux — 2026-07-16

**Source:** Direct request in a Claude Code session. Grounded in a 5-agent audit of all ~42 UI files.
**Status:** Planning → building. Kept in `dev-requests/planning/` so it doesn't trigger the Dev Requests bot.

> **Cannot be compiled here** (Unity, no CI Editor). Every slice is reviewed by subagents against the
> real declarations before its box is ticked; commits say "not compiled — please build before playing."

---

## Goal

Move away from floating pop-up windows that clutter the view, toward cohesive UI that never needlessly
blocks the map. The centrepiece is ONE full-screen **Planetary Viewer** that holds everything about a
world. Then the rest of the UI (HUD, empire/ship windows, menus) gets deduped and made consistent.

## The audit in one paragraph

`PlanetViewWindow` is already a large tabbed planet window (Overview/Build/Survey/Orbit/Terrain + moon
tabs) that splits map + side panel — ~75% of the target. `ColonyWindow` and `AssociatedObjectsWindow`
are already retired; `InspectorBodyTabs` (a full 7-tab parallel planet UI) is dead code unreachable from
selection (`InspectorWindow.cs:162` no-ops body selection). Terraforming is rendered three times
(`TerraformWindow` + dead Inspector tab + PlanetView Survey). Selection funnels through
`PlanetUI.Show` (`PlanetUI.cs:110`) → `OnBodySelected` (`:142`) → `PlanetViewWindow.ShowFor`, and that
last hop currently makes a single click auto-open the whole full-screen view — the clutter to fix.

All UI is built in code (`UIFactory` vocabulary, `UITheme` palette, `UILive` refresh discipline,
`InspectorWindow` is the tab + signature-gated-rebuild reference). Styling is **flat RGBA color, zero
sprites/textures** — so a space/tech restyle has two chokepoints: `UIFactory.Panel`/`Button` (add
sprites/9-slice/glow) and `UITheme` (palette).

---

## The full-screen Planetary Viewer — layout

```
┌───────────────────────────────────────────────────────────────┐
│  [Overview][Resources][Sites][Infrastructure][Orbit][Terraform]   [✕ Close] │  tab strip + close, above the map
├──────────────────────────────────────────────┬────────────────┤
│                                                │                │
│                                                │   RIGHT PANEL  │
│              MAP  (most of the screen)         │  context by    │
│         + moon sub-tabs, zoom / pan            │  active tab:   │
│                                                │  options,      │
│                                                │  menus,        │
│                                                │  buttons,      │
├────────────────────────────────────────────────┤  queues,       │
│  BOTTOM PANEL — info for whatever the cursor is │  loaders       │
│  over: a tile (biome, temp, resources), a piece │                │
│  of infrastructure, or a point of interest      │                │
└──────────────────────────────────────────────┴────────────────┘
```

- **Tabs** sit side by side ABOVE the map. **Close (✕)** button top-right closes the whole viewer.
- **Map** is the large centre: the surface grid (with moon sub-tabs, zoom/pan already built).
- **Bottom panel** shows live info for whatever the cursor hovers — tile (biome/temp/resources), an
  infrastructure piece, or a POI — replacing the floating `MapHoverPanel` with a docked panel.
- **Right panel** carries all the active tab's options/menus/controls/queues.

### Tabs (the grouping asked for)

1. **Overview** — identity, climate/temperature, habitability, society/population, ownership actions
   (claim/settle/establish city).
2. **Resources & Overlays** — resources + ores + the index overlays (mineral/heat/fertile/wind/power);
   toggling an overlay repaints the map; per-tile readout flows to the bottom panel.
3. **Sites** — points of interest, selectable; details in the bottom panel on hover.
4. **Infrastructure & Build** — the standing infrastructure list AND the build/place tray in ONE tab:
   click a structure on the map to select it (info in bottom panel) and **upgrade / demolish / build
   units** from the right panel; pick a new structure to ghost-place on the grid. The shipyard and
   research-centre build/queue live here too (unit-building represented with progress loaders).
4b. Unit building: the shipyard queue (build power, per-ship progress bars, pause/cancel) is shown in
    this tab's right panel with live loaders/animations, not a separate window.
5. **Orbit & Fleet** — ships in orbit, inbound traffic, stations, and the orbit controls (incl. the new
   real-time orbit shifting); dev orbit editor folds in here (Dev Mode).
6. **Terraform** — the single terraforming console (diagnosis, ceiling, projects, live jobs) — folds in
   the standalone `TerraformWindow` so it stops being a floating window.
7. **Terrain** (Dev Mode only) — the noise sandbox.

### Interaction model (de-clutter the click)

- **Single click a planet** → select it + centre/zoom the camera (`CameraController.FocusAndZoom`, already
  wired) + show a **compact right-side info panel** (name/type/habitability/owner + an "Open Planetary
  View" button). It does NOT throw open the full-screen viewer.
- **Double-click the planet, or the panel's button** → open the full-screen Planetary Viewer.
- **Close (✕)** → back to the compact panel (selection kept), or fully deselect on empty-space click.
- Seam: stop `PlanetViewWindow.OnBodySelected` from auto-opening (`PlanetViewWindow.cs:339`); add a
  double-click timer in `PlanetClick.OnMouseDown` (`PlanetClick.cs:7`); add the compact panel as a small
  component subscribing to `PlanetUI.OnBodySelected`/`OnClosed`.

### Restyle (space/tech, no external assets)

The game builds everything procedurally (even the sky) — so the restyle is **procedural textures**, not
imported art: generate 9-slice panel/button sprites and soft glow borders in code, and retune `UITheme`.
Two chokepoints only (`UIFactory.Panel`/`Button` and the window `Outline`), so every panel and button
inherits the look with no per-window edits. Keep the cyan accent; add subtle panel gradient/scanline
texture, glowing accent borders on the active tab and primary buttons, and translucent overlays so the
map stays readable.

### Loaders & animations

Reuse the existing progress-bar/`LiveSet` discipline for build/research/terraform queues shown in the
viewer; add lightweight animated loaders (spinner/fill pulse) via a small reusable helper so unit
building, upgrades and terraforming all read as "in progress" consistently.

---

## Dead code & dedupe (fold the pop-ups away)

- **Delete** `InspectorBodyTabs.cs` (dead parallel planet UI) and `ColonyWindow.cs` (un-instantiated),
  after confirming the live planet view covers their content.
- **Fold** `TerraformWindow` into the Terraform tab; remove its HUD button (or repoint it to open the
  viewer's Terraform tab). Fold `BodyUnitsPanel` (ships-here) into Orbit & Fleet, `OrbitControlPanel`
  into Orbit & Fleet (Dev), `MapHoverPanel` into the docked bottom panel.
- **HUD** (`GameHUD`, 17 buttons): collapse Surface/Map/Terrain into one "Planet" action (the viewer);
  fix the "Ore Codex"→ResearchWindow label; drop the Orbit/Terrain HUD buttons (now tabs).
- **Menus**: EscapeMenu Save+Load are duplicate toggles of one window; StartMenu/EscapeMenu overlap —
  dedupe. `TooltipManager`/`MapHoverPanel` are near-parallel — unify.

---

## Build slices

### A. Full-screen layout scaffold
- [ ] Restructure `PlanetViewWindow` into the fixed 4-zone full-screen layout (tab strip + close top,
      big map centre, docked bottom hover panel, right control panel). Fill-canvas anchor; drop
      drag/resize/grip for this window.
- [ ] Add the **Close (✕)** button that hides the viewer.

### B. Decoupled interaction + compact panel
- [ ] Stop single-click auto-opening the full viewer; add the compact right-side info panel on select
      (with camera focus already wired) and an "Open Planetary View" button.
- [ ] Add double-click-to-open in `PlanetClick`.

### C. Docked bottom hover panel
- [ ] Replace the floating `MapHoverPanel` usage in the viewer with the docked bottom panel; feed it
      tile / infrastructure / POI info depending on what the cursor is over.

### D. Tab reorganization
- [ ] Reorganize the tabs to Overview / Resources & Overlays / Sites / Infrastructure & Build /
      Orbit & Fleet / Terraform / Terrain(Dev), moving existing Survey/Overview/Orbit content into them.

### E. Infrastructure interactions + unit building
- [ ] Click infrastructure on the map → select + bottom-panel info + right-panel upgrade/demolish; build
      units (shipyard queue with loaders) from the Infrastructure & Build tab.
- [ ] Fold the shipyard/research build+queue representation (progress loaders/animations) into the tab.

### F. Fold terraforming in
- [ ] Make the Terraform tab the single terraforming console; remove the standalone `TerraformWindow`
      HUD entry (repoint to the tab).

### G. Restyle (space/tech)
- [ ] Procedural 9-slice panel/button sprites + glow borders in `UIFactory`; retune `UITheme`; active-tab
      and primary-button glow. Verify readability over the map (translucency).

### H. Dead-code removal + HUD/menu dedupe
- [ ] Delete `InspectorBodyTabs.cs`, `ColonyWindow.cs`; fold `BodyUnitsPanel`/`OrbitControlPanel`;
      collapse HUD Surface/Map/Terrain; fix labels; dedupe menus and the two hover panels.

### I. Whole-diff review
- [ ] Review the full change; confirm no window opens on plain selection, the viewer covers every folded
      surface, save/load and Dev Mode gating still hold, and nothing regressed for ships/stars/empire UI.

Order: A → B → C → D → E → F, then G (restyle) and H (cleanup), then I. Each slice committed AND pushed
to the fork's `main` as a standalone checkpoint. This is separate from the terraforming overhaul (slices
1 & 4 shipped, rest parked) — the two share `PlanetViewWindow`, so terraforming's `RefreshIfShowing`
hook must keep working through the restructure.

## Build log

- **Slice B — decouple the click — built 2026-07-16.** Single-click a world = select + camera focus +
  new right-edge `CompactBodyPanel` (type/owner/habitability/resources + "Open Planetary View" button);
  it no longer throws the full viewer open. Double-click the world, or the panel button, opens the full
  viewer. `PlanetViewWindow.OnBodySelected` now only remembers the world (repaints if already open) and
  gained `IsOpen`; `PlanetClick` got an unscaled-time double-click timer. Reviewed clean by a subagent
  (all UIFactory/UITheme/PlanetUI APIs verified; double-click state machine correct). Follow-ups noted:
  Dev-Mode `OrbitControlPanel` still auto-opens on select; closing the full viewer still deselects
  entirely (to become "back to compact panel" in the layout slice). Not compiled (no Unity in this env).
