using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Lists all your ships with rank/location/task. Click to select (Shift adds to the selection) and
// inspect; Send Selected orders the selected fleet to a destination.
public class FleetWindow : MonoBehaviour
{
    public static FleetWindow Instance;

    GameObject root;
    RectTransform list;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("FleetWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<FleetWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Fleet", new Vector2(400, 520), out root, out _);
        root.GetComponent<RectTransform>().anchoredPosition = new Vector2(-200, 0);

        var holder = UIFactory.NewUI(content, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder, 0, 0, 0, 36);
        UIFactory.ScrollView(holder, out list);

        var send = UIFactory.Button(content, "Send Selected…", () =>
        {
            var fleet = new List<Unit>();
            foreach (var u in UnitSelection.Selected) if (u.location != null) fleet.Add(u);
            FleetMovementController.Instance?.Arm(fleet);
        }, 30);
        var srt = send.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(0.5f, 0); srt.sizeDelta = new Vector2(-8, 30); srt.anchoredPosition = new Vector2(0, 4);
        send.GetComponent<LayoutElement>().ignoreLayout = true;

        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged += Refresh;
        UnitSelection.OnChanged += Refresh;

        root.SetActive(false);
    }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show) { Refresh(); root.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    void Refresh()
    {
        if (root == null || !root.activeSelf) return;
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        if (UnitManager.Instance == null) return;
        foreach (var u in UnitManager.Instance.Units)
        {
            bool sel = UnitSelection.IsSelected(u);
            var row = UIFactory.Panel(list, "Row", sel ? new Color(0.12f, 0.20f, 0.28f) : UITheme.RowBg);
            UIFactory.AddLayout(row.gameObject, 40);
            var btn = row.gameObject.AddComponent<Button>();
            var captured = u;
            btn.onClick.AddListener(() =>
            {
                bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                UnitSelection.Select(captured, add);
                SimpleAudio.Instance?.PlayUnitSelect(captured.type);
                UnitInfoPanel.Instance?.Show(captured);
            });

            string loc = u.location != null ? u.location.name : (u.travelTarget != null ? $"→ {u.travelTarget.name}" : "—");
            var t = UIFactory.Text(row.transform, $"<b>{u.name}</b>  <color=#FFD24D>{u.RankName}</color>\n<size=11><color=#8FA4BE>{u.Info.name} · {u.status} · {loc}</color></size>",
                UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
            UIFactory.Stretch(t.rectTransform, 8, 8, 0, 0);
        }
    }

    void OnDestroy()
    {
        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged -= Refresh;
        UnitSelection.OnChanged -= Refresh;
    }
}
