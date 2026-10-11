using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// SELECTING A SHIP — one path for every way of pointing at one (2026-10-11)
//
// A ship can be clicked as a billboard token, as a 3D hull, as its screen-space marker (ShipMarkers),
// through a planet that handed the click over (ClickPriority), or from a row in a list. Each used to
// carry its own copy of "shift adds, play the sound, open the panel", and the copies had drifted. They
// all come here now.
//
// WHAT A CLICK DOES
//   click            select the ship (Shift: add it / take it out of the selection)
//   double-click     select every one of YOUR ships of the same class on screen
//   click a stack    select the stack (your ships in it; a rival stack shows its first ship)
//
// A click no longer flies the camera. It used to (FocusUnit on every token click), which made Shift-
// clicking a second ship impossible — the first click had already moved it out from under the cursor —
// and made a double-click land on empty space. Focusing is its own gesture: F (or Q) frames the
// selection, Tab walks your ships and frames each one, and every list row frames what it selects.
//
// ONE CLICK PER FRAME. A marker click can be seen both by ShipMarkers' own poll and by a planet's
// OnMouseDown that defers to it (ClickPriority). Whichever arrives second is told "already handled",
// which consumes it without counting it twice — a doubled call would otherwise read as a double-click.
// ============================================================================================
public static class ShipSelect
{
    const float DoubleClickWindow = 0.35f;

    static int handledFrame = -1;
    static Unit lastClicked;
    static float lastClickTime = -10f;

    /// A map click on one ship. Returns true if the click was consumed.
    public static bool Click(Unit u) => Click(u, null);

    /// A map click on a ship, or on a stack of them (`stack` non-null: every ship the marker stands for,
    /// `u` its lead). Returns true if the click was consumed.
    public static bool Click(Unit u, List<Unit> stack)
    {
        if (u == null) return false;
        if (Time.frameCount == handledFrame) return true;
        if (FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting) return false;
        handledFrame = Time.frameCount;

        bool additive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool isDouble = lastClicked == u && Time.unscaledTime - lastClickTime <= DoubleClickWindow;
        lastClicked = isDouble ? null : u;       // a third click starts over rather than reading as another double
        lastClickTime = Time.unscaledTime;

        if (isDouble && !additive && u.owner == FactionManager.Player)
        {
            var same = SameClassOnScreen(u);
            if (same.Count > 1)
            {
                UnitSelection.Set(same);
                SimpleAudio.Instance?.PlayUnitSelect(u.type);
                return true;
            }
        }

        if (stack != null && stack.Count > 1)
        {
            // Your ships in the stack, or — a rival's stack — just its lead, to look at.
            var mine = new List<Unit>();
            foreach (var s in stack) if (s != null && s.owner == FactionManager.Player) mine.Add(s);
            if (mine.Count > 0)
            {
                if (additive) foreach (var s in mine) { if (!UnitSelection.IsSelected(s)) UnitSelection.Select(s, true); }
                else UnitSelection.Set(mine);
                SimpleAudio.Instance?.PlayUnitSelect(mine[0].type);
                if (mine.Count == 1) UnitInfoPanel.Instance?.Show(mine[0]);
                return true;
            }
        }

        if (additive) UnitSelection.Select(u, true);
        else UnitSelection.SelectOnly(u);
        SimpleAudio.Instance?.PlayUnitSelect(u.type);
        UnitInfoPanel.Instance?.Show(u);
        return true;
    }

    /// Select one ship and fly the camera to it — what a list row and the Tab key do.
    public static void SelectAndFocus(Unit u)
    {
        if (u == null) return;
        bool additive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (additive) { UnitSelection.Select(u, true); SimpleAudio.Instance?.PlayUnitSelect(u.type); return; }
        UnitSelection.SelectOnly(u);
        SimpleAudio.Instance?.PlayUnitSelect(u.type);
        UnitInfoPanel.Instance?.Show(u);
        Focus(u);
    }

    /// Fly the camera to a ship and follow it. Ships are followed by identity (see CameraController).
    public static void Focus(Unit u)
    {
        if (u == null || CameraController.Instance == null) return;
        CameraController.Instance.FocusUnit(u, true);
    }

    /// The player's ships of this class that are on screen right now. Drawn ships only — the same rule
    /// as the box select, so a double-click can never reach a concealed hull.
    static List<Unit> SameClassOnScreen(Unit like)
    {
        var r = new List<Unit>();
        var um = UnitManager.Instance;
        var cam = Camera.main;
        if (um == null || cam == null) return r;
        foreach (var u in um.Units)
        {
            if (u == null || u.IsDestroyed || u.owner != FactionManager.Player || u.type != like.type) continue;
            if (VisibilityService.IsHidden(u)) continue;
            var t = UnitVisuals.TransformOf(u);
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            Vector3 sp = cam.WorldToScreenPoint(t.position);
            if (sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) continue;
            r.Add(u);
        }
        if (!r.Contains(like)) r.Insert(0, like);
        return r;
    }
}

// ============================================================================================
// FINDING YOUR SHIPS FROM THE KEYBOARD
//
//   Tab / Shift+Tab   next / previous ship of yours (build order), selected and framed
//   . (period)        next IDLE ship — at rest with no orders; the one that needs telling what to do
//
// F (and Q) already frame the selection (CameraController), so between them a ship lost behind a
// planet on the far side of the galaxy is one key away. Space stays the game-speed toggle.
// Not while typing, not over the pause or start menu, not while the full-screen Planet View is up.
// ============================================================================================
public class ShipHotkeys : MonoBehaviour
{
    public static ShipHotkeys Instance;

    public const KeyCode CycleKey = KeyCode.Tab;
    public const KeyCode NextIdleKey = KeyCode.Period;

    public static void Create()
    {
        if (Instance != null) return;
        Instance = new GameObject("ShipHotkeys").AddComponent<ShipHotkeys>();
    }

    void Awake() { Instance = this; }

    void Update()
    {
        if (UIFactory.IsTypingInField()) return;
        if (GenesisSequence.Running || GenesisCamera.Active) return;
        if (EscapeMenu.Instance != null && EscapeMenu.Instance.IsOpen) return;
        if (StartMenu.Instance != null && StartMenu.Instance.IsOpen) return;
        if (PlanetViewWindow.Instance != null && PlanetViewWindow.Instance.IsOpen) return;

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (ctrl || alt) return;   // Alt+Tab belongs to the OS

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKeyDown(CycleKey)) Cycle(shift ? -1 : 1, false);
        else if (Input.GetKeyDown(NextIdleKey)) Cycle(1, true);
    }

    static void Cycle(int dir, bool idleOnly)
    {
        var all = ShipRoster.PlayerShips();
        var pool = new List<Unit>();
        foreach (var u in all) if (!idleOnly || ShipRoster.IsIdle(u)) pool.Add(u);

        if (pool.Count == 0)
        {
            NotificationManager.Instance?.Push(idleOnly ? "No idle ships" : "No ships",
                idleOnly ? "Every ship you have is busy." : "You have no ships yet.", null, NotifKind.Info);
            return;
        }

        // Step from the current selection's place in the WHOLE list, so Tab and "." agree about order
        // and "." after Tab finds the next idle ship after the one on screen, not the first in the list.
        var cur = UnitSelection.Selected.Count > 0 ? UnitSelection.Selected[0] : null;
        int curId = cur != null ? cur.id : (dir > 0 ? int.MinValue : int.MaxValue);

        Unit pick = null;
        if (dir > 0)
        {
            foreach (var u in pool) if (u.id > curId) { pick = u; break; }
            if (pick == null) pick = pool[0];
        }
        else
        {
            for (int i = pool.Count - 1; i >= 0; i--) if (pool[i].id < curId) { pick = pool[i]; break; }
            if (pick == null) pick = pool[pool.Count - 1];
        }

        UnitSelection.SelectOnly(pick);
        SimpleAudio.Instance?.PlayUnitSelect(pick.type);
        UnitInfoPanel.Instance?.Show(pick);
        ShipSelect.Focus(pick);
    }
}
