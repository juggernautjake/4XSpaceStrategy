using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Left-click-drag on empty space to box-select your units (whether parked in space or docked at a
// body). A plain click on empty space clears the selection. Clicks that start on a token or body are
// left to those objects' own handlers.
public class BoxSelectController : MonoBehaviour
{
    public static BoxSelectController Instance;

    const float DragThreshold = 8f;

    Camera cam;
    RectTransform canvasRT;
    RectTransform box;
    Vector2 startScreen;
    bool down, dragging;
    bool startedOnCollider;   // the press began on a token/body, so a plain click belongs to that object

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
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting) return;

        // Track the press wherever it starts — including on a token or a planet. A drag is unambiguously
        // a box-select, and requiring it to begin on empty space made the box impossible to start in a
        // busy system, where most of the screen is covered by a body or its collider. A plain CLICK that
        // began on a collider is still left to that object's own handler (see OnUp).
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        startedOnCollider = Physics.Raycast(ray, out _, 5000f);

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
        else if (!startedOnCollider && UnitSelection.Selected.Count > 0)
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
                Vector3 sp = cam.WorldToScreenPoint(UnitManager.Instance.UnitPos(u));
                if (sp.z <= 0f) continue;
                if (sp.x >= min.x && sp.x <= max.x && sp.y >= min.y && sp.y <= max.y) picked.Add(u);
            }

        if (additive) foreach (var u in picked) UnitSelection.Select(u, true);
        else UnitSelection.Set(picked);

        if (picked.Count > 0) SimpleAudio.Instance?.PlayUnitSelect(picked[0].type);
    }
}
