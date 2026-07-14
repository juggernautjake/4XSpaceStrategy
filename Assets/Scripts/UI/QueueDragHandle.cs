using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Grab handle that lets the player drag a queue widget up or down to change the build/research order.
//
// The handle deliberately lives on its own small graphic rather than on the whole row: dragging the row
// body still scrolls the list (the ScrollRect gets the drag), and only the grip reorders. Unity routes a
// drag to the first handler above the pointer, so the two never fight.
//
// Reordering moves the MODEL and then re-sorts the existing row objects by sibling index — it never
// destroys and rebuilds them, because destroying the row under the cursor would cancel the drag.
public class QueueDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public RectTransform row;             // the widget this grip reorders
    public Func<int> currentIndex;        // where the row sits in the model right now
    public Action<int, int> onReorder;    // move(from, to) — updates the model
    public Action onDragStateChanged;     // lets the window suppress rebuilds mid-drag

    public static bool Dragging { get; private set; }

    CanvasGroup rowGroup;

    public void OnBeginDrag(PointerEventData e)
    {
        if (row == null) return;
        Dragging = true;
        rowGroup = row.GetComponent<CanvasGroup>() ?? row.gameObject.AddComponent<CanvasGroup>();
        rowGroup.alpha = 0.65f;           // visibly "lifted" while it's being carried
        onDragStateChanged?.Invoke();
    }

    // Work out which slot the pointer is now over and move the row there. The model is reordered live,
    // so the widget visibly swaps places with its neighbours as it passes them.
    public void OnDrag(PointerEventData e)
    {
        if (row == null || currentIndex == null || onReorder == null) return;
        var parent = row.parent as RectTransform;
        if (parent == null) return;

        int from = currentIndex();
        if (from < 0) return;

        // Count the sibling rows whose centre sits above the pointer: that's the slot it belongs in.
        int target = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || child == row) continue;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(child, e.position, e.pressEventCamera, out var world))
                continue;
            if (world.y < child.position.y) target++;   // pointer is below this row -> it stays above us
        }
        target = Mathf.Clamp(target, 0, parent.childCount - 1);

        if (target != from) onReorder(from, target);
    }

    public void OnEndDrag(PointerEventData e)
    {
        Dragging = false;
        if (rowGroup != null) rowGroup.alpha = 1f;
        onDragStateChanged?.Invoke();
    }

    void OnDisable()
    {
        // A row destroyed mid-drag (e.g. its ship finished) must not leave the flag stuck on.
        if (Dragging) { Dragging = false; onDragStateChanged?.Invoke(); }
    }

    // The "=" grip itself: a raycast target so it can receive the drag, sized for a comfortable grab.
    public static QueueDragHandle Attach(Transform parent, RectTransform row, Func<int> index,
                                         Action<int, int> reorder, Action dragStateChanged)
    {
        var go = UIFactory.NewUI(parent, "DragGrip");
        var img = go.AddComponent<Image>();
        img.color = new Color(UITheme.SubText.r, UITheme.SubText.g, UITheme.SubText.b, 0.18f);
        img.raycastTarget = true;
        var t = UIFactory.Text(go.transform, "=", UITheme.BodySize, UITheme.SubText, TMPro.TextAlignmentOptions.Center);
        UIFactory.Stretch(t.rectTransform);
        var le = UIFactory.AddLayout(go, 0);
        le.preferredWidth = 22; le.minWidth = 22; le.flexibleWidth = 0;

        var h = go.AddComponent<QueueDragHandle>();
        h.row = row; h.currentIndex = index; h.onReorder = reorder; h.onDragStateChanged = dragStateChanged;
        return h;
    }
}
