using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// What the Inspector is currently looking at. A single window inspects every kind of thing in the
// game, so the "subject" is a small tagged union rather than a class hierarchy.
public enum InspectorKind { None, Body, Unit, Fleet, City, Shipyard, ResearchCenter, Structure, Star }

public struct InspectorTarget : IEquatable<InspectorTarget>
{
    public InspectorKind kind;
    public CelestialBody body;      // Body / City / Shipyard / ResearchCenter / Structure
    public Unit unit;               // Unit
    public BuildingType structure;  // Structure
    public StarData star;           // Star
    public StarSystemData system;   // Star (the system it anchors)

    public static InspectorTarget Of(CelestialBody b) => new InspectorTarget { kind = InspectorKind.Body, body = b };
    public static InspectorTarget Of(Unit u) => new InspectorTarget { kind = InspectorKind.Unit, unit = u };
    public static InspectorTarget FleetTarget() => new InspectorTarget { kind = InspectorKind.Fleet };
    public static InspectorTarget CityOf(CelestialBody b) => new InspectorTarget { kind = InspectorKind.City, body = b };
    public static InspectorTarget ShipyardOf(CelestialBody b) => new InspectorTarget { kind = InspectorKind.Shipyard, body = b };
    public static InspectorTarget LabOf(CelestialBody b) => new InspectorTarget { kind = InspectorKind.ResearchCenter, body = b };
    public static InspectorTarget StructureOf(CelestialBody b, BuildingType t)
        => new InspectorTarget { kind = InspectorKind.Structure, body = b, structure = t };
    public static InspectorTarget Of(StarData s, StarSystemData sys)
        => new InspectorTarget { kind = InspectorKind.Star, star = s, system = sys };

    public bool Equals(InspectorTarget o)
        => kind == o.kind && body == o.body && unit == o.unit && structure == o.structure && star == o.star;

    public bool IsValid
    {
        get
        {
            switch (kind)
            {
                case InspectorKind.None: return false;
                case InspectorKind.Unit: return unit != null;
                case InspectorKind.Fleet: return UnitSelection.Selected.Count > 0;
                case InspectorKind.Star: return star != null;
                default: return body != null;
            }
        }
    }
}

// One tab: a title and the builder that fills the content area with it.
public class InspectorTab
{
    public string title;
    public Action<Transform> build;
    public Func<bool> visible;   // null = always shown

    public InspectorTab(string title, Action<Transform> build, Func<bool> visible = null)
    { this.title = title; this.build = build; this.visible = visible; }
}

// ============================================================================================
// THE INSPECTOR — one tabbed window for everything you can click on.
//
// Click a planet, a moon, a ship, a station or a fleet and this is what you get: a header, a row of
// tabs, and a content area. Tabs let you page through everything known about the subject without a
// dozen separate windows fighting for screen space.
//
// It also DRILLS DOWN. A planet's Production tab lists its shipyard and research centre as rows; click
// one and the Inspector re-targets to that facility, with a breadcrumb back to the planet. That is how
// "select the planet's shipyard and build ships there" works — the shipyard is a subject in its own
// right, not a sub-panel.
//
// Rebuild discipline matches the rest of the UI: widgets are rebuilt only when the SUBJECT or the TAB
// changes; every value inside them refreshes in place each frame via a LiveSet. Rebuilding on the
// economy tick is what made the buttons strobe.
// ============================================================================================
public partial class InspectorWindow : MonoBehaviour
{
    public static InspectorWindow Instance;

    GameObject root;
    TMP_Text titleText, subtitleText;
    RectTransform tabStrip, content, breadcrumbRow;

    InspectorTarget target;
    readonly List<InspectorTab> tabs = new List<InspectorTab>();
    int activeTab;

    // Where we came from, so drilling into a facility can walk back to its planet.
    readonly List<InspectorTarget> trail = new List<InspectorTarget>();

    readonly LiveSet live = new LiveSet();
    string lastSig = null;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("InspectorWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<InspectorWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var win = UIFactory.Window(parent, "Inspector", new Vector2(560, 700), out root, out titleText);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-16, 0);

        // Header: subtitle + breadcrumb.
        subtitleText = UIFactory.Text(win, "", UITheme.SmallSize, UITheme.SubText, TextAlignmentOptions.Left);
        var srt = subtitleText.rectTransform;
        srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
        srt.pivot = new Vector2(0.5f, 1); srt.sizeDelta = new Vector2(0, 18); srt.anchoredPosition = Vector2.zero;

        breadcrumbRow = UIFactory.NewUI(win, "Breadcrumb").GetComponent<RectTransform>();
        breadcrumbRow.anchorMin = new Vector2(0, 1); breadcrumbRow.anchorMax = new Vector2(1, 1);
        breadcrumbRow.pivot = new Vector2(0.5f, 1); breadcrumbRow.sizeDelta = new Vector2(0, 22);
        breadcrumbRow.anchoredPosition = new Vector2(0, -20);
        var bh = breadcrumbRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        bh.spacing = 4; bh.childControlWidth = true; bh.childControlHeight = true;
        bh.childForceExpandWidth = false; bh.childAlignment = TextAnchor.MiddleLeft;

        // Tab strip.
        tabStrip = UIFactory.NewUI(win, "Tabs").GetComponent<RectTransform>();
        tabStrip.anchorMin = new Vector2(0, 1); tabStrip.anchorMax = new Vector2(1, 1);
        tabStrip.pivot = new Vector2(0.5f, 1); tabStrip.sizeDelta = new Vector2(0, 26);
        tabStrip.anchoredPosition = new Vector2(0, -44);
        var th = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 3; th.childControlWidth = true; th.childControlHeight = true;
        th.childForceExpandWidth = true; th.childAlignment = TextAnchor.MiddleLeft;

        // Content.
        var holder = UIFactory.NewUI(win, "ContentHolder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder, 0, 0, 74, 0);
        UIFactory.ScrollView(holder, out content);

        // Follow every kind of selection the game can make.
        PlanetUI.OnBodySelected += OnBodySelected;
        PlanetUI.OnClosed += OnSelectionCleared;
        UnitSelection.OnChanged += OnUnitSelectionChanged;

        root.SetActive(false);
    }

    void OnDestroy()
    {
        PlanetUI.OnBodySelected -= OnBodySelected;
        PlanetUI.OnClosed -= OnSelectionCleared;
        UnitSelection.OnChanged -= OnUnitSelectionChanged;
        ClearStarThumbs();
    }

    // ---- Selection routing ----
    // Single-clicking a body now opens the tabbed Inspector ON it — the fleshed-out readout the user asked
    // to keep — instead of the retired compact panel. The full-screen Planet View is the DOUBLE-click /
    // "Open Planet View" action instead. The Inspector is right-edge anchored, so it never sits over the
    // clicked world and can't eat the second click of a double-click.
    void OnBodySelected(CelestialBody b) { if (b != null) Inspect(InspectorTarget.Of(b), resetTrail: true); }

    void OnSelectionCleared()
    {
        if (target.kind == InspectorKind.Body || target.kind == InspectorKind.City ||
            target.kind == InspectorKind.Shipyard || target.kind == InspectorKind.ResearchCenter ||
            target.kind == InspectorKind.Structure)
            Hide();
    }

    // A single selected ship inspects that ship; several inspect the fleet. Selecting nothing leaves a
    // body inspection alone — clicking a planet then clearing your ships shouldn't close the planet.
    void OnUnitSelectionChanged()
    {
        int n = UnitSelection.Selected.Count;
        if (n == 1) Inspect(InspectorTarget.Of(UnitSelection.Selected[0]), resetTrail: true);
        else if (n > 1) Inspect(InspectorTarget.FleetTarget(), resetTrail: true);
        else if (target.kind == InspectorKind.Unit || target.kind == InspectorKind.Fleet) Hide();
    }

    // Fly the camera to whatever the Inspector is currently looking at — the global "focus selection" (F)
    // gesture routes here for a selected star / facility / etc. (planets go through PlanetUI.Selected).
    // Resolves the subject to its on-screen transform per kind; a subject with no single transform (a
    // fleet, or a unit whose token this window doesn't cache) is simply skipped.
    public void FocusCurrent()
    {
        if (!target.IsValid) return;
        Transform tr = null;
        float hint = 3f;
        switch (target.kind)
        {
            case InspectorKind.Body:
            case InspectorKind.City:
            case InspectorKind.Shipyard:
            case InspectorKind.ResearchCenter:
            case InspectorKind.Structure:
                if (target.body != null && target.body.visualObject != null)
                { tr = target.body.visualObject.transform; hint = target.body.surfaceSize; }
                break;
            case InspectorKind.Star:
                tr = StarInteraction.TransformOf(target.star);
                if (tr != null) hint = tr.lossyScale.x;
                break;
        }
        if (tr != null) CameraController.Instance?.FocusAndZoom(tr, hint, true);
    }

    public void Inspect(InspectorTarget t, bool resetTrail = false)
    {
        if (!t.IsValid) return;
        if (resetTrail) trail.Clear();
        target = t;
        activeTab = 0;
        lastSig = null;                       // force a rebuild
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    // Drill into a sub-subject (a facility on a planet), remembering where we came from.
    public void Drill(InspectorTarget t)
    {
        if (!t.IsValid) return;
        trail.Add(target);
        target = t;
        activeTab = 0;
        lastSig = null;
    }

    void Back()
    {
        if (trail.Count == 0) return;
        target = trail[trail.Count - 1];
        trail.RemoveAt(trail.Count - 1);
        activeTab = 0;
        lastSig = null;
    }

    public void Hide() { if (root != null) root.SetActive(false); }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        if (show && !target.IsValid)
        {
            // Nothing inspected yet: fall back to whatever is currently selected.
            if (UnitSelection.Selected.Count == 1) target = InspectorTarget.Of(UnitSelection.Selected[0]);
            else if (UnitSelection.Selected.Count > 1) target = InspectorTarget.FleetTarget();
            // A selected BODY opens the Planet View instead — the body Inspector is retired (see
            // OnBodySelected). This keeps the HUD "Inspect" button useful with a planet selected rather
            // than reopening the window we just retired.
            else if (PlanetUI.Selected != null) { PlanetViewWindow.Instance?.ShowFor(PlanetUI.Selected); return; }
            else return;
            lastSig = null;
        }
        root.SetActive(show);
        if (show) root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    // ---- Frame loop ----
    // The signature covers everything that changes the SHAPE of the window: which subject, which tab,
    // and the structural facts a tab's widgets depend on. Values (population, progress, costs) are
    // deliberately absent — those are LiveSet's job.
    string Signature()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append((int)target.kind).Append('|').Append(activeTab).Append('|').Append(trail.Count).Append('|');
        sb.Append(SpeciesManager.CurrentIndex).Append('|').Append(EmpireTech.Level).Append('|').Append(GameMode.DevMode ? 1 : 0).Append('|');

        var b = target.body;
        if (b != null)
        {
            sb.Append(b.id).Append('|').Append((int)b.type).Append('|').Append(b.Surveyed ? 1 : 0).Append('|');
            sb.Append(b.shipyardLevel).Append('|').Append(b.researchCenterLevel).Append('|').Append(b.cities).Append('|');
            sb.Append(b.owner != null ? b.owner.id : -1).Append('|');
            foreach (int id in b.buildings) sb.Append(id).Append(',');
            sb.Append('|').Append(b.units != null ? b.units.Count : 0).Append('|');
            sb.Append(b.moons != null ? b.moons.Count : 0).Append('|');
            if (b.terraformProjects != null) foreach (int p in b.terraformProjects) sb.Append(p).Append(',');
        }
        if (target.unit != null) sb.Append('|').Append(target.unit.id).Append('|').Append((int)target.unit.type);
        if (target.star != null)
        {
            sb.Append('|').Append(target.star.name).Append('|').Append((int)target.star.type).Append('|');
            sb.Append(target.system != null ? target.system.name : "").Append('|');
            sb.Append(SystemBodies().Count);
        }
        if (target.kind == InspectorKind.Fleet)
        {
            sb.Append('|');
            foreach (var u in UnitSelection.Selected) sb.Append(u.id).Append(',');
        }
        if (target.kind == InspectorKind.Shipyard)
            sb.Append('|').Append(Colony.PlayerMaxShipyardLevel()).Append('|').Append(BuildPower.PlayerTotal());
        if (target.kind == InspectorKind.ResearchCenter)
        {
            sb.Append('|').Append(TechManager.TotalCapacity).Append('|');
            foreach (var t in TechDatabase.All) if (TechManager.IsResearched(t.id)) sb.Append(t.id);
        }
        return sb.ToString();
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;

        // The subject can evaporate underneath us (a ship dies, a colony ship is consumed founding a
        // city). Close rather than throw.
        if (!target.IsValid) { Hide(); return; }

        string sig = Signature();
        if (sig != lastSig) { lastSig = sig; Rebuild(); }

        live.Tick();
    }

    // ---- Rebuild ----
    void Rebuild()
    {
        live.Clear();
        tabs.Clear();
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        for (int i = tabStrip.childCount - 1; i >= 0; i--) Destroy(tabStrip.GetChild(i).gameObject);
        for (int i = breadcrumbRow.childCount - 1; i >= 0; i--) Destroy(breadcrumbRow.GetChild(i).gameObject);

        BuildHeader();
        CollectTabs();

        // A tab can vanish between rebuilds (a planet loses its shipyard) — clamp rather than break.
        var shown = VisibleTabs();
        if (shown.Count == 0) return;
        activeTab = Mathf.Clamp(activeTab, 0, shown.Count - 1);

        BuildTabStrip(shown);
        shown[activeTab].build(content);
    }

    List<InspectorTab> VisibleTabs()
    {
        var shown = new List<InspectorTab>();
        foreach (var t in tabs) if (t.visible == null || t.visible()) shown.Add(t);
        return shown;
    }

    void BuildHeader()
    {
        titleText.text = TitleFor(target);
        subtitleText.text = SubtitleFor(target);

        // Breadcrumb: how we got here, so a drill-down is never a one-way trip.
        if (trail.Count > 0)
        {
            var back = UIFactory.Button(breadcrumbRow, "<- " + TitleFor(trail[trail.Count - 1]), Back, 20);
            var le = back.GetComponent<LayoutElement>();
            le.preferredWidth = 190; le.minWidth = 120; le.flexibleWidth = 0;
        }
    }

    static string TitleFor(InspectorTarget t)
    {
        switch (t.kind)
        {
            case InspectorKind.Body: return t.body != null ? t.body.name : "World";
            case InspectorKind.Unit: return t.unit != null ? t.unit.name : "Ship";
            case InspectorKind.Fleet: return $"Fleet ({UnitSelection.Selected.Count} ships)";
            case InspectorKind.City: return t.body != null ? $"{t.body.name} City" : "City";
            case InspectorKind.Shipyard: return t.body != null ? $"{t.body.name} Shipyard" : "Shipyard";
            case InspectorKind.ResearchCenter: return t.body != null ? $"{t.body.name} Research Centre" : "Research Centre";
            case InspectorKind.Structure: return BuildingDatabase.Get(t.structure).name;
            case InspectorKind.Star:
                if (t.star == null) return "Star";
                if (!string.IsNullOrEmpty(t.star.name)) return t.star.name;
                return t.star.isBlackHole ? "Black Hole" : $"{t.star.type}-type Star";
            default: return "Inspector";
        }
    }

    string SubtitleFor(InspectorTarget t)
    {
        switch (t.kind)
        {
            case InspectorKind.Body:
                return t.body == null ? "" :
                    $"{TerraformDiagnosis.Pretty(t.body.type)} · {FactionManager.OwnerLabel(t.body.owner)}" +
                    (t.body.parentBody != null ? $" · moon of {t.body.parentBody.name}" : "");
            case InspectorKind.Unit:
                return t.unit == null ? "" : $"{t.unit.Info.name} · {t.unit.RankName} · {FactionManager.OwnerName(t.unit.owner)}";
            case InspectorKind.Fleet: return "Multiple ships selected";
            case InspectorKind.City: return t.body == null ? "" : $"Colony city on {t.body.name}";
            case InspectorKind.Shipyard: return t.body == null ? "" : $"Orbital construction yard on {t.body.name}";
            case InspectorKind.ResearchCenter: return t.body == null ? "" : $"Research laboratory on {t.body.name}";
            case InspectorKind.Structure: return t.body == null ? "" : $"On {t.body.name}";
            case InspectorKind.Star:
                if (t.star == null) return "";
                string kind = t.star.isBlackHole ? "Black hole"
                    : t.star.starCount >= 3 ? "Ternary system"
                    : t.star.starCount == 2 ? "Binary system"
                    : $"{t.star.type}-type star";
                return t.system != null ? $"{kind} · {t.system.name} · {FactionManager.OwnerLabel(t.system.owner)}" : kind;
            default: return "";
        }
    }

    void BuildTabStrip(List<InspectorTab> shown)
    {
        for (int i = 0; i < shown.Count; i++)
        {
            int idx = i;
            bool active = i == activeTab;
            var btn = UIFactory.Button(tabStrip, shown[i].title, () => { activeTab = idx; lastSig = null; }, 22);

            // The active tab is the one place a persistent highlight is correct: it's state, not hover.
            var colors = btn.colors;
            colors.normalColor = active ? UITheme.ButtonActive : UITheme.ButtonBg;
            colors.highlightedColor = colors.normalColor;
            colors.selectedColor = colors.normalColor;
            btn.colors = colors;

            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.fontSize = UITheme.SmallSize;
                label.color = active ? Color.white : UITheme.SubText;
            }
        }
    }

    // Which tabs this subject has. Implemented in the InspectorTabs partials.
    void CollectTabs()
    {
        switch (target.kind)
        {
            case InspectorKind.Body: CollectBodyTabs(); break;
            case InspectorKind.Unit: CollectUnitTabs(); break;
            case InspectorKind.Fleet: CollectFleetTabs(); break;
            case InspectorKind.City: CollectCityTabs(); break;
            case InspectorKind.Shipyard: CollectShipyardTabs(); break;
            case InspectorKind.ResearchCenter: CollectLabTabs(); break;
            case InspectorKind.Structure: CollectStructureTabs(); break;
            case InspectorKind.Star: CollectStarTabs(); break;
        }
    }

    // ---- Shared building blocks used by every tab ----
    internal Transform Card(Transform parent, Color? bg = null)
    {
        var card = UIFactory.Panel(parent, "Card", bg ?? UITheme.RowBg);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 3;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return card.transform;
    }

    internal void Header(Transform parent, string text)
        => UIFactory.WrapText(parent, $"<b>{text}</b>", UITheme.SmallSize, UITheme.Accent);

    // A "name — value" line whose value is recomputed every frame.
    internal void Stat(Transform parent, string label, Func<string> value)
    {
        var t = UIFactory.WrapText(parent, "", UITheme.SmallSize, UITheme.Text);
        live.Text(t, () => $"<color=#9FB4C8>{label}:</color> {value()}");
    }

    internal void Note(Transform parent, string text)
        => UIFactory.WrapText(parent, text, UITheme.SmallSize, UITheme.SubText);

    // A row that drills into another subject — the mechanism behind "click the shipyard to open it".
    internal void DrillRow(Transform parent, string label, InspectorTarget to, Func<string> detail = null)
    {
        var card = Card(parent);
        var text = UIFactory.WrapText(card, "", UITheme.SmallSize, UITheme.Text);
        live.Text(text, () => detail != null ? $"<b>{label}</b>  <size=10><color=#9FB4C8>{detail()}</color></size>" : $"<b>{label}</b>");
        UIFactory.Button(card, "Open »", () => Drill(to), 22);
    }

    // A progress bar bound to live values.
    internal Image Bar(Transform parent, Func<(float t, string text, Color color)> eval)
    {
        var holder = UIFactory.NewUI(parent, "Bar");
        UIFactory.AddLayout(holder, 16);
        var track = UIFactory.Panel(holder.transform, "Track", UITheme.TrackBg);
        UIFactory.Stretch(track.rectTransform);
        var fill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
        var frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        var label = UIFactory.Text(holder.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Center);
        UIFactory.Stretch(label.rectTransform);
        live.Bar(fill, eval, label);
        return fill;
    }

    internal LiveSet Live => live;
}
