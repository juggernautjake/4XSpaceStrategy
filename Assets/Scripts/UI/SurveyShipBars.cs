using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// A PROGRESS BAR OVER EVERY SHIP THAT IS SURVEYING (2026-10-11)
//
// "There should also be a loading bar above the ship in space too."
//
// Screen-space, like NameplateManager, on its own canvas just under the nameplates: a small bordered
// bar a few pixels above the hull, cyan for a Survey and green for a Deep Survey (the Planet View
// strip's colours), segmented into ten pixel cells so it reads as part of the game's pixel-art UI
// rather than as a smooth web progress bar, with the percent beside it.
//
// Shown only while the ship is actually working a survey, only at system zoom (galaxy zoom hides it,
// as it hides nameplates), never during the intro, and never for a hull that is not drawn — the ship's
// own ConcealBinding is the authority (VisibilityService.ShownToPlayer), so the fog of war, a Dev hide
// and the system concealment all hide the bar for free.
//
// CHEAP: the bars are POOLED and handed out in order each frame; nothing is allocated per frame except
// the percent string, and that only when the whole-number percent of a bar changes.
//
// Also hosts the Dev Mode hotkey Ctrl+Shift+U ("survey this world fully now"): it needs a component
// that is always alive, and this one is.
// ============================================================================================
public class SurveyShipBars : MonoBehaviour
{
    public static SurveyShipBars Instance;

    const float BarW = 46f, BarH = 7f;
    const int Cells = 10;
    static readonly Color Frame = new Color(0.02f, 0.03f, 0.05f, 0.92f);
    static readonly Color Track = new Color(0.07f, 0.10f, 0.15f, 0.95f);
    static readonly Color SurveyCol = new Color(0.34f, 0.78f, 1.00f, 1f);
    static readonly Color DeepCol = new Color(0.32f, 1.00f, 0.50f, 1f);

    class Bar
    {
        public RectTransform rt;
        public Image border;
        public RectTransform fill;
        public Image fillImg;
        public TMP_Text pct;
        public int shownPct = -1;
        public bool shownDeep;
    }

    Canvas canvas;
    Camera cam;
    readonly List<Bar> pool = new List<Bar>();

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("SurveyShipBars").AddComponent<SurveyShipBars>();
    }

    void Awake()
    {
        Instance = this;
        canvas = UIFactory.CreateCanvas("SurveyBarCanvas", 44);   // just under NameplateManager's 45
        canvas.transform.SetParent(transform, false);
        var gr = canvas.GetComponent<GraphicRaycaster>();
        if (gr != null) Destroy(gr);                               // labels, never targets
    }

    Bar Make()
    {
        var b = new Bar();
        b.border = UIFactory.Panel(canvas.transform, "SurveyBar", Frame);
        b.border.raycastTarget = false;
        b.rt = b.border.rectTransform;
        b.rt.pivot = new Vector2(0.5f, 0f);
        b.rt.sizeDelta = new Vector2(BarW + 2f, BarH + 2f);

        var track = UIFactory.Panel(b.rt, "Track", Track);
        track.raycastTarget = false;
        UIFactory.Stretch(track.rectTransform, 1, 1, 1, 1);

        b.fillImg = UIFactory.Panel(track.transform, "Fill", SurveyCol);
        b.fillImg.raycastTarget = false;
        b.fill = b.fillImg.rectTransform;
        b.fill.anchorMin = Vector2.zero; b.fill.anchorMax = new Vector2(0f, 1f);
        b.fill.offsetMin = Vector2.zero; b.fill.offsetMax = Vector2.zero;

        // Pixel cells: dark one-pixel dividers over the fill, so it advances in visible steps.
        for (int i = 1; i < Cells; i++)
        {
            var d = UIFactory.Panel(track.transform, "Cell", Frame);
            d.raycastTarget = false;
            var drt = d.rectTransform;
            float x = i / (float)Cells;
            drt.anchorMin = new Vector2(x, 0f); drt.anchorMax = new Vector2(x, 1f);
            drt.offsetMin = new Vector2(-0.5f, 0f); drt.offsetMax = new Vector2(0.5f, 0f);
        }

        b.pct = UIFactory.Text(b.rt, "", 10, SurveyCol, TextAlignmentOptions.Left);
        b.pct.raycastTarget = false;
        b.pct.textWrappingMode = TextWrappingModes.NoWrap;
        b.pct.fontStyle = FontStyles.Bold;
        b.pct.outlineWidth = 0.25f;                 // TMP ignores uGUI Outline/Shadow
        b.pct.outlineColor = new Color32(0, 0, 0, 220);
        var prt = b.pct.rectTransform;
        prt.anchorMin = new Vector2(1f, 0f); prt.anchorMax = new Vector2(1f, 1f);
        prt.pivot = new Vector2(0f, 0.5f);
        prt.offsetMin = new Vector2(3f, -4f); prt.offsetMax = new Vector2(40f, 4f);

        b.rt.gameObject.SetActive(false);
        pool.Add(b);
        return b;
    }

    void LateUpdate()
    {
        DevHotkey();

        if (cam == null) cam = Camera.main;
        var um = UnitManager.Instance;
        bool hideAll = cam == null || um == null || GenesisSequence.Running || GenesisCamera.Active || !GalaxyLOD.SystemMode;
        if (canvas.enabled == hideAll) canvas.enabled = !hideAll;

        int used = 0;
        if (!hideAll)
        {
            var units = um.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!Survey.IsWorking(u) || u.location == null) continue;
                if (!VisibilityService.ShownToPlayer(u)) continue;
                var t = UnitVisuals.TransformOf(u);
                if (t == null || !t.gameObject.activeInHierarchy) continue;

                Vector3 c = cam.WorldToScreenPoint(t.position);
                if (c.z <= 0f || c.x < -60f || c.x > Screen.width + 60f || c.y < -60f || c.y > Screen.height + 60f) continue;

                var bar = used < pool.Count ? pool[used] : Make();
                used++;
                Place(bar, t, c);
                Fill(bar, u);
            }
        }

        for (int i = used; i < pool.Count; i++)
            if (pool[i].rt.gameObject.activeSelf) pool[i].rt.gameObject.SetActive(false);
    }

    void Place(Bar bar, Transform t, Vector3 c)
    {
        // Lift by the hull's on-screen size so the bar clears the ship at any zoom.
        float radius = Mathf.Max(0.05f, t.lossyScale.x * 0.6f);
        Vector3 top = cam.WorldToScreenPoint(t.position + cam.transform.up * radius);
        float lift = Mathf.Clamp(Mathf.Abs(top.y - c.y), 6f, 80f) + 6f;
        // Whole pixels, so the cells and the border stay crisp.
        bar.rt.position = new Vector3(Mathf.Round(c.x), Mathf.Round(c.y + lift), 0f);
        if (!bar.rt.gameObject.activeSelf) bar.rt.gameObject.SetActive(true);
    }

    static void Fill(Bar bar, Unit u)
    {
        bool deep = u.status == UnitStatus.Researching;
        float p = Survey.Progress(u.location, deep);
        int pct = Mathf.FloorToInt(p * 100f);

        if (deep != bar.shownDeep || bar.shownPct < 0)
        {
            bar.shownDeep = deep;
            var col = deep ? DeepCol : SurveyCol;
            bar.fillImg.color = col;
            bar.pct.color = col;
            bar.shownPct = -1;
        }
        if (pct != bar.shownPct)
        {
            bar.shownPct = pct;
            bar.fill.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
            bar.pct.text = deep ? $"D {pct}%" : $"{pct}%";
        }
    }

    // ---- Dev Mode: Ctrl+Shift+U surveys the world in front of you fully ----------------------
    static void DevHotkey()
    {
        if (!GameMode.DevMode) return;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!ctrl || !shift || !Input.GetKeyDown(KeyCode.U)) return;

        // The Planet View's world if it is open, otherwise the selected world.
        var pv = PlanetViewWindow.Instance;
        CelestialBody b = pv != null && pv.IsOpen ? pv.Body : PlanetUI.Selected;
        if (b == null) return;
        bool did = UnitManager.Instance != null && UnitManager.Instance.DevCompleteSurvey(b);
        if (!did)
            NotificationManager.Instance?.Push($"{b.name} is already fully surveyed", "Dev Mode: nothing left to complete.", null, NotifKind.Info);
    }
}
