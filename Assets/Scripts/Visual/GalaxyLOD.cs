using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Level-of-detail for the galaxy. When you zoom far enough out, the fully-rendered systems are hidden
// and each system is represented by a clickable name/symbol marker floating at its position. Zooming
// back in (or using a marker's "Go to system") restores the detailed view. This keeps a many-system
// galaxy readable and cheap when viewed as a whole.
public class GalaxyLOD : MonoBehaviour
{
    public static GalaxyLOD Instance;

    const float EnterGalaxy = 1400f;   // above this height -> symbol view
    const float ExitGalaxy = 1050f;    // below this -> detailed view (hysteresis avoids flicker)

    Camera cam;
    RectTransform markerRoot;
    readonly List<SystemMarker> markers = new List<SystemMarker>();
    Galaxy builtFor;
    bool galaxyView;

    public static void Create(Transform canvas)
    {
        if (Instance != null) return;
        var go = new GameObject("GalaxyLOD");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<GalaxyLOD>();
        Instance.Init(canvas);
    }

    void Awake() { Instance = this; }

    void Init(Transform canvas)
    {
        cam = Camera.main;
        var rootGO = UIFactory.NewUI(canvas, "GalaxyMarkers");
        markerRoot = rootGO.GetComponent<RectTransform>();
        UIFactory.Stretch(markerRoot);
        markerRoot.gameObject.SetActive(false);
    }

    void RebuildMarkers(Galaxy g)
    {
        foreach (var m in markers) if (m != null) Destroy(m.gameObject);
        markers.Clear();
        builtFor = g;
        if (g == null) return;
        foreach (var sys in g.systems)
            markers.Add(SystemMarker.Build(markerRoot, sys));
    }

    void Update()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        var g = SystemContext.Galaxy;
        if (g != builtFor) RebuildMarkers(g);
        if (g == null) return;

        float h = cam.transform.position.y;
        bool want = galaxyView ? (h > ExitGalaxy) : (h > EnterGalaxy);
        if (want != galaxyView) SetGalaxyView(want);

        if (galaxyView) UpdateMarkerPositions();
    }

    void SetGalaxyView(bool on)
    {
        galaxyView = on;
        if (SystemContext.SystemParent != null) SystemContext.SystemParent.gameObject.SetActive(!on);
        markerRoot.gameObject.SetActive(on);
    }

    void UpdateMarkerPositions()
    {
        foreach (var m in markers)
        {
            if (m == null) continue;
            Vector3 sp = cam.WorldToScreenPoint(m.WorldPos);
            if (sp.z <= 0f) { m.gameObject.SetActive(false); continue; }
            m.gameObject.SetActive(true);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerRoot, sp, null, out Vector2 lp))
                m.rt.anchoredPosition = lp;
        }
    }
}

// A floating, clickable label + dot representing one star system in the galaxy view.
public class SystemMarker : MonoBehaviour
{
    public RectTransform rt;
    public Vector3 WorldPos;

    public static SystemMarker Build(Transform parent, StarSystemData sys)
    {
        var go = UIFactory.NewUI(parent, "SysMarker");
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(160, 44);

        // Click target background.
        var img = go.AddComponent<Image>();
        img.color = new Color(0.05f, 0.08f, 0.14f, 0.55f);

        var marker = go.AddComponent<SystemMarker>();
        marker.rt = rt;
        marker.WorldPos = sys.galaxyPosition;

        // Symbol dot (green for home, blue-white otherwise; dark for a black-hole core).
        Color dotColor = sys.isBlackHole ? new Color(0.5f, 0.35f, 0.7f)
            : sys.isHome ? new Color(0.35f, 1f, 0.45f) : new Color(0.8f, 0.86f, 1f);
        var dot = UIFactory.Panel(go.transform, "Dot", dotColor);
        dot.raycastTarget = false;
        var drt = dot.rectTransform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 1f); drt.pivot = new Vector2(0.5f, 1f);
        drt.sizeDelta = new Vector2(12, 12); drt.anchoredPosition = new Vector2(0, -2);

        var lbl = UIFactory.Text(go.transform, sys.name + (sys.isHome ? "  <color=#4DFF6E>(home)</color>" : ""),
            UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Center);
        var lrt = lbl.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1);
        lrt.offsetMin = new Vector2(0, 0); lrt.offsetMax = new Vector2(0, -14);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var captured = sys;
        btn.onClick.AddListener(() =>
        {
            SimpleAudio.Instance?.PlaySelect();
            SystemSummaryWindow.Instance?.Show(captured);
        });
        return marker;
    }
}
