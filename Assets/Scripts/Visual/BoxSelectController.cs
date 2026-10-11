using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Left-click-drag to box-select your units (whether parked in space or docked at a body). A plain click
// on empty space clears the selection. Clicks that start on a token or body are left to those objects'
// own handlers.
//
// CTRL + DRAG (2026-10-11): "we need a click and drag feature for selecting ships... maybe hold down
// control and click and drag". Holding Ctrl makes the press a box-select no matter what is under it —
// a planet, a star, a ship — and those objects ignore the press (SwallowsClick), so dragging out from
// a crowded world does not open its window first. Shift adds to the selection, with or without Ctrl.
// Ctrl-click while aiming a fleet is still the "queue this move" gesture; the box steps aside then.
//
// The "is the pointer over UI" test ignores full-screen pass-through layers (ship markers, nameplates,
// labels): a raycast-target that covers the whole screen is not a window, and treating it as one would
// stop the box from ever starting.
public class BoxSelectController : MonoBehaviour
{
    public static BoxSelectController Instance;

    /// Ctrl held, and not aiming a fleet: the press belongs to the box, and clickable objects must not
    /// act on it. Read by PlanetClick, StarInteraction, the ship token/model handlers and ShipMarkers.
    public static bool SwallowsClick =>
        (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        && !(FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting)
        && !UIFactory.IsTypingInField();

    /// True while a box is being dragged out.
    public static bool Dragging => Instance != null && Instance.dragging;

    static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    /// Over a real window or control — not merely over a screen-filling overlay.
    static bool OverBlockingUI(Vector2 screen)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        var ped = new PointerEventData(es) { position = screen };
        uiHits.Clear();
        es.RaycastAll(ped, uiHits);
        float screenArea = (float)Screen.width * Screen.height;
        foreach (var h in uiHits)
        {
            var rt = h.gameObject.transform as RectTransform;
            if (rt == null) return true;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var canvas = h.gameObject.GetComponentInParent<Canvas>();
            Camera ccam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(ccam, c[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(ccam, c[2]);
            float area = Mathf.Abs(b.x - a.x) * Mathf.Abs(b.y - a.y);
            if (area < screenArea * 0.85f) return true;   // a window, a button, a panel
        }
        return false;
    }

    const float DragThreshold = 8f;

    Camera cam;
    RectTransform canvasRT;
    RectTransform box;
    Vector2 startScreen;
    bool down, dragging;
    bool startedOnCollider;
    bool ctrlPress;           // the press was a Ctrl-press: a box or nothing, never a deselect   // the press began on a token/body, so a plain click belongs to that object

    public static void Create(Transform canvas)
    {
        if (Instance != null) return;
        var go = new GameObject("BoxSelect");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<BoxSelectController>();
        Instance.Init(canvas);
    }

    void Awake() { Instance = this; }

    void Init(Transform canvas)
    {
        cam = Camera.main;
        canvasRT = canvas as RectTransform;

        var b = UIFactory.NewUI(canvas, "SelectBox");
        box = b.GetComponent<RectTransform>();
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0.5f, 0.5f);
        var img = b.AddComponent<Image>();
        img.color = new Color(0.4f, 0.8f, 1f, 0.15f);
        img.raycastTarget = false;
        var outline = b.AddComponent<Outline>();
        outline.effectColor = new Color(0.5f, 0.85f, 1f, 0.9f);
        outline.effectDistance = new Vector2(1, -1);
        box.gameObject.SetActive(false);
    }

    void Update()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        if (Input.GetMouseButtonDown(0)) OnDown();
        if (down && Input.GetMouseButton(0)) OnDrag();
        if (down && Input.GetMouseButtonUp(0)) OnUp();
    }

    void OnDown()
    {
        if (OverBlockingUI(Input.mousePosition)) return;
        if (FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting) return;
        if (PlanetViewWindow.Instance != null && PlanetViewWindow.Instance.IsOpen) return;   // its map owns drags

        // Track the press wherever it starts — including on a token or a planet. A drag is unambiguously
        // a box-select, and requiring it to begin on empty space made the box impossible to start in a
        // busy system, where most of the screen is covered by a body or its collider. A plain CLICK that
        // began on a collider is still left to that object's own handler (see OnUp).
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        // A ship's screen-space marker counts as something to click, too: it has no collider, and a click
        // on one in open space must not read as a click on empty space and clear the selection it made.
        // With Ctrl held the press is the box's whatever is under it, so it never counts as "on" something.
        startedOnCollider = !SwallowsClick && (Physics.Raycast(ray, out _, 5000f) || ShipMarkers.AnyUnderCursor());

        ctrlPress = SwallowsClick;
        down = true; dragging = false; startScreen = Input.mousePosition;
    }

    void OnDrag()
    {
        Vector2 cur = Input.mousePosition;
        if (!dragging && Vector2.Distance(cur, startScreen) > DragThreshold)
        {
            dragging = true;
            box.gameObject.SetActive(true);
        }
        if (dragging) UpdateBox(startScreen, cur);
    }

    void OnUp()
    {
        if (dragging)
        {
            SelectInBox(startScreen, Input.mousePosition);
            box.gameObject.SetActive(false);
        }
        else if (!startedOnCollider && !ctrlPress && UnitSelection.Selected.Count > 0)
        {
            UnitSelection.Clear();   // plain click on empty space -> deselect
        }
        down = false; dragging = false; startedOnCollider = false;
    }

    void UpdateBox(Vector2 a, Vector2 b)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, a, null, out Vector2 la);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, b, null, out Vector2 lb);
        box.anchoredPosition = (la + lb) * 0.5f;
        box.sizeDelta = new Vector2(Mathf.Abs(lb.x - la.x), Mathf.Abs(lb.y - la.y));
    }

    void SelectInBox(Vector2 a, Vector2 b)
    {
        Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
        bool additive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        var picked = new List<Unit>();
        if (UnitManager.Instance != null)
            foreach (var u in UnitManager.Instance.Units)
            {
                if (u.owner != FactionManager.Player) continue;   // you can only command your own ships
                // A box drag projects positions rather than raycasting, so disabling a concealed ship's
                // collider does not protect it here — a drag across apparently empty space would select
                // the cloaked ships sitting in it.
                if (VisibilityService.IsHidden(u)) continue;
                Vector3 sp = cam.WorldToScreenPoint(UnitManager.Instance.UnitPos(u));
                if (sp.z <= 0f) continue;
                if (sp.x >= min.x && sp.x <= max.x && sp.y >= min.y && sp.y <= max.y) picked.Add(u);
            }

        if (additive) foreach (var u in picked) UnitSelection.Select(u, true);
        else UnitSelection.Set(picked);

        if (picked.Count > 0) SimpleAudio.Instance?.PlayUnitSelect(picked[0].type);
    }
}
