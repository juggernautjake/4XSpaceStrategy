using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Transient notifications at the top-middle of the screen plus a persistent history window.
// Clicking a notification (toast or history row) runs its action — used to jump the camera to a
// completed research site and show its report.
public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance;

    class Entry { public string title, message, detail; public Action onClick; public NotifKind kind; }

    static Color KindColor(NotifKind k)
    {
        switch (k)
        {
            case NotifKind.Research: return new Color(0.55f, 0.8f, 1f);
            case NotifKind.Discovery: return UITheme.Good;
            case NotifKind.Danger: return UITheme.Bad;
            case NotifKind.Victory: return new Color(1f, 0.85f, 0.35f);
            case NotifKind.Defeat: return new Color(1f, 0.45f, 0.45f);
            default: return UITheme.Accent;
        }
    }

    RectTransform toastContainer;
    GameObject historyRoot;
    RectTransform historyList;
    readonly List<Entry> history = new List<Entry>();

    public static void Create(Transform canvas)
    {
        if (Instance != null) return;
        var go = new GameObject("NotificationManager");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<NotificationManager>();
        Instance.Build(canvas);
    }

    void Build(Transform canvas)
    {
        // Toast stack (top-centre).
        var holder = UIFactory.NewUI(canvas, "Toasts").GetComponent<RectTransform>();
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 1f);
        holder.pivot = new Vector2(0.5f, 1f);
        holder.sizeDelta = new Vector2(440, 0);
        holder.anchoredPosition = new Vector2(0, -48);
        var vlg = holder.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6; vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        toastContainer = holder;

        // History window (hidden until opened).
        var content = UIFactory.Window(canvas, "Notifications", new Vector2(500, 470), out historyRoot, out _);
        historyRoot.GetComponent<RectTransform>().anchoredPosition = new Vector2(300, 0);
        var listHolder = UIFactory.NewUI(content, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(listHolder);
        UIFactory.ScrollView(listHolder, out historyList);
        historyRoot.SetActive(false);
    }

    // message = short/simple line shown on the toast; detail = the fuller version shown when the
    // notification is expanded in the history (defaults to the simple message if not provided).
    public void Push(string title, string message, Action onClick, NotifKind kind = NotifKind.Info, string detail = null)
    {
        var e = new Entry { title = title, message = message, detail = string.IsNullOrEmpty(detail) ? message : detail, onClick = onClick, kind = kind };
        history.Insert(0, e);
        SimpleAudio.Instance?.PlayNotify(kind);
        SpawnToast(e);
        if (historyRoot != null && historyRoot.activeSelf) RefreshHistory();
    }

    void SpawnToast(Entry e)
    {
        var panel = UIFactory.Panel(toastContainer, "Toast", new Color(0.08f, 0.13f, 0.20f, 0.97f));
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = KindColor(e.kind);
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8); vlg.spacing = 2;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;

        var btn = panel.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { e.onClick?.Invoke(); Destroy(panel.gameObject); });

        UIFactory.WrapText(panel.transform, $"<b>{e.title}</b>", UITheme.BodySize, KindColor(e.kind));
        UIFactory.WrapText(panel.transform, e.message, UITheme.SmallSize, UITheme.Text);
        UIFactory.WrapText(panel.transform, "<size=10><i>(click to view)</i></size>", UITheme.SmallSize, UITheme.SubText);

        panel.gameObject.AddComponent<NotificationToast>().life = 6.5f;
    }

    public void ToggleHistory()
    {
        bool show = !historyRoot.activeSelf;
        historyRoot.SetActive(show);
        if (show) { RefreshHistory(); historyRoot.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    void RefreshHistory()
    {
        for (int i = historyList.childCount - 1; i >= 0; i--) Destroy(historyList.GetChild(i).gameObject);
        if (history.Count == 0) { UIFactory.WrapText(historyList, "No notifications yet.", UITheme.SmallSize, UITheme.SubText); return; }

        foreach (var e in history)
        {
            var row = UIFactory.Panel(historyList, "Row", UITheme.RowBg);
            var fitter = row.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var vlg = row.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 2;
            vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;

            UIFactory.WrapText(row.transform, $"<b>{e.title}</b>", UITheme.SmallSize, KindColor(e.kind));
            UIFactory.WrapText(row.transform, e.message, UITheme.SmallSize, UITheme.Text);

            // The fuller detail, hidden until you expand the row.
            var detailText = UIFactory.WrapText(row.transform, e.detail, UITheme.SmallSize, UITheme.SubText);
            bool hasExtra = !string.IsNullOrEmpty(e.detail) && e.detail != e.message;
            detailText.gameObject.SetActive(false);

            var hint = UIFactory.WrapText(row.transform,
                (hasExtra ? "<size=10><i>(click for details" : "<size=10><i>(click") + (e.onClick != null ? " · locate)</i></size>" : ")</i></size>"),
                UITheme.SmallSize, UITheme.Accent);

            var btn = row.gameObject.AddComponent<Button>();
            var captured = e;
            var dt = detailText.gameObject;
            var hn = hint.gameObject;
            btn.onClick.AddListener(() =>
            {
                if (hasExtra && !dt.activeSelf) { dt.SetActive(true); hn.SetActive(false); }   // first click expands
                captured.onClick?.Invoke();                                                     // and/or locates
            });
        }
    }
}

// Auto-dismisses a toast after `life` seconds (real time), fading out at the end.
public class NotificationToast : MonoBehaviour
{
    public float life = 6f;
    float age;
    CanvasGroup cg;

    void Awake() { cg = gameObject.AddComponent<CanvasGroup>(); }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        if (cg != null && life - age < 1f) cg.alpha = Mathf.Clamp01(life - age);
        if (age >= life) Destroy(gameObject);
    }
}
