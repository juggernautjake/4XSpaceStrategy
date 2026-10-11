using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// THE FLEET LIST — every ship you own, where it is, and what it is doing (2026-10-11)
//
// "We should be able to easily locate and select it." This was a flat list of every unit in the game in
// build order — rivals' included, which the fog of war should never have allowed — with a raw enum for a
// status. It is now grouped the way a player looks for a ship:
//
//   SYSTEM                          (click to fold)
//     World / In transit / Deep space   (click to select everything there)
//       ship rows                   (ShipRosterUI: glyph, name, class, status, hull, Send to…)
//
// Filters along the top — All, Idle, Moving, Working — with live counts, because "which of my ships
// has nothing to do" is the question this window is opened to answer more often than any other.
// Clicking a ship selects it and flies the camera to it; Shift-click adds without moving the camera.
// Tab / Shift+Tab and "." walk the same ships from the keyboard (ShipHotkeys).
//
// The Order of Battle (O) is the other view of the same fleet — by squadron rather than by place — and
// is one click away from the header.
// ============================================================================================
public class FleetWindow : MonoBehaviour
{
    public static FleetWindow Instance;

    enum Filter { All, Idle, Moving, Working }

    GameObject root;
    RectTransform list;
    TMP_Text titleText;
    readonly LiveSet live = new LiveSet();
    readonly Button[] filterButtons = new Button[4];
    Filter filter = Filter.All;
    readonly HashSet<string> folded = new HashSet<string>();
    string lastSig;
    float sigIn;

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
        var content = UIFactory.Window(parent, "Fleet", new Vector2(480, 560), out root, out titleText);
        root.GetComponent<RectTransform>().anchoredPosition = new Vector2(-200, 0);

        // ---- filter strip ----
        var strip = UIFactory.NewUI(content, "Filters");
        var srt = strip.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
        srt.pivot = new Vector2(0.5f, 1);
        srt.sizeDelta = new Vector2(-12, 26); srt.anchoredPosition = new Vector2(0, -4);
        var sh = strip.AddComponent<HorizontalLayoutGroup>();
        sh.spacing = 4; sh.childControlWidth = true; sh.childControlHeight = true;
        sh.childForceExpandWidth = true; sh.childForceExpandHeight = true;
        for (int i = 0; i < 4; i++)
        {
            var f = (Filter)i;
            var b = UIFactory.Button(strip.transform, f.ToString(), () => { filter = f; lastSig = null; }, 26);
            var t = b.GetComponentInChildren<TMP_Text>();
            if (t != null) t.fontSize = UITheme.SmallSize;
            filterButtons[i] = b;
        }
        UIFactory.Tooltip(filterButtons[0].gameObject, "Every ship you own.");
        UIFactory.Tooltip(filterButtons[1].gameObject, "At rest with no orders — the ships waiting to be told what to do. Press \".\" to jump to the next one.");
        UIFactory.Tooltip(filterButtons[2].gameObject, "Under way.");
        UIFactory.Tooltip(filterButtons[3].gameObject, "Surveying, researching or founding a colony.");

        // ---- the list ----
        var holder = UIFactory.NewUI(content, "Holder").GetComponent<RectTransform>();
        UIFactory.Stretch(holder, 0, 0, 34, 40);
        UIFactory.ScrollView(holder, out list);
        var vlg = list.GetComponent<VerticalLayoutGroup>();
        if (vlg != null) { vlg.spacing = 3; vlg.padding = new RectOffset(8, 8, 6, 6); }

        // ---- footer ----
        var foot = UIFactory.NewUI(content, "Footer");
        var frt = foot.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(1, 0);
        frt.pivot = new Vector2(0.5f, 0);
        frt.sizeDelta = new Vector2(-8, 30); frt.anchoredPosition = new Vector2(0, 4);
        var fh = foot.AddComponent<HorizontalLayoutGroup>();
        fh.spacing = 6; fh.childControlWidth = true; fh.childControlHeight = true;
        fh.childForceExpandWidth = true; fh.childForceExpandHeight = true;

        var send = UIFactory.Button(foot.transform, "Send selected to…", () =>
        {
            var fleet = SelectedMovable();
            if (fleet.Count > 0) SendToWindow.Instance?.Open(fleet);
        }, 30);
        // Bound once, in its own set: the list's LiveSet is cleared on every rebuild and the footer is not
        // part of the list.
        footerLive.Button(send, () =>
        {
            int n = SelectedMovable().Count;
            return (n > 0, n > 0 ? $"Send {n} selected to…" : "Send selected to…");
        });
        UIFactory.Button(foot.transform, "Order of Battle (O)", () => FleetRosterPanel.Instance?.Toggle(), 30);

        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged += MarkDirty;

        root.SetActive(false);
    }

    readonly LiveSet footerLive = new LiveSet();

    void MarkDirty() => lastSig = null;

    static List<Unit> SelectedMovable()
    {
        var fleet = new List<Unit>();
        foreach (var u in UnitSelection.Selected)
            if (u != null && u.owner == FactionManager.Player && !u.Info.isStation) fleet.Add(u);
        return fleet;
    }

    public void Toggle()
    {
        bool show = !root.activeSelf;
        root.SetActive(show);
        if (show) { lastSig = null; root.GetComponent<RectTransform>().SetAsLastSibling(); }
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;

        sigIn -= Time.unscaledDeltaTime;
        if (lastSig == null || sigIn <= 0f)
        {
            sigIn = 0.25f;
            string sig = Signature();
            if (sig != lastSig) { lastSig = sig; Rebuild(); }
        }
        live.Tick();
        footerLive.Tick();
    }

    // ---- what goes where ----

    static bool Passes(Unit u, Filter f)
    {
        switch (f)
        {
            case Filter.Idle: return ShipRoster.IsIdle(u);
            case Filter.Moving: return u.status == UnitStatus.Traveling || u.status == UnitStatus.Returning;
            case Filter.Working:
                return u.status == UnitStatus.Exploring || u.status == UnitStatus.Researching
                    || u.status == UnitStatus.Colonizing;
        }
        return true;
    }

    static string SystemName(StarSystemData s) => s != null ? s.name : "Between the stars";

    static string PlaceName(Unit u, out CelestialBody place)
    {
        place = ShipRoster.PlaceOf(u);
        if (place != null) return place.name;
        if (u.status == UnitStatus.Traveling || u.status == UnitStatus.Returning) return "In transit";
        return "Deep space";
    }

    /// Which ships are listed where, under the current filter — the window's SHAPE. Statuses that do not
    /// move a ship between groups (a hull taking damage) are live text, not structure.
    string Signature()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append((int)filter).Append('|');
        foreach (var f in folded) sb.Append(f).Append(';');
        sb.Append('|');
        foreach (var u in ShipRoster.PlayerShips(true))
        {
            if (!Passes(u, filter)) continue;
            sb.Append(u.id).Append(':').Append(SystemName(ShipRoster.SystemOf(u))).Append('/')
              .Append(PlaceName(u, out _)).Append(',');
        }
        return sb.ToString();
    }

    void Rebuild()
    {
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
        live.Clear();

        var ships = ShipRoster.PlayerShips(true);

        // Filter counts on the buttons.
        for (int i = 0; i < 4; i++)
        {
            int n = 0;
            foreach (var u in ships) if (Passes(u, (Filter)i)) n++;
            var t = filterButtons[i].GetComponentInChildren<TMP_Text>();
            bool on = (int)filter == i;
            if (t != null) t.text = on ? $"<b>{(Filter)i} ({n})</b>" : $"{(Filter)i} ({n})";
            var img = filterButtons[i].targetGraphic as Image;
            if (img != null) img.color = on ? UITheme.ButtonActive : UITheme.ButtonBg;
            var cb = filterButtons[i].colors;
            cb.normalColor = cb.highlightedColor = cb.selectedColor = on ? UITheme.ButtonActive : UITheme.ButtonBg;
            filterButtons[i].colors = cb;
        }

        // Group: system -> place -> ships. Systems sorted with the home system's first ship's system first
        // by build order, which in practice puts home at the top.
        var systems = new List<string>();
        var bySystem = new Dictionary<string, List<Unit>>();
        foreach (var u in ships)
        {
            if (!Passes(u, filter)) continue;
            string s = SystemName(ShipRoster.SystemOf(u));
            if (!bySystem.TryGetValue(s, out var l)) { bySystem[s] = l = new List<Unit>(); systems.Add(s); }
            l.Add(u);
        }

        int total = 0;
        foreach (var s in systems) total += bySystem[s].Count;
        titleText.text = $"Fleet ({ships.Count})";

        if (total == 0)
        {
            UIFactory.WrapText(list, ships.Count == 0
                ? "<color=#7F8FA3>You have no ships yet. Build them at a shipyard.</color>"
                : $"<color=#7F8FA3>No ships match \"{filter}\".</color>", UITheme.SmallSize, UITheme.Text);
            return;
        }

        foreach (var s in systems)
        {
            var inSys = bySystem[s];
            bool isFolded = folded.Contains(s);
            SystemHeader(s, inSys, isFolded);
            if (isFolded) continue;

            var places = new List<string>();
            var byPlace = new Dictionary<string, List<Unit>>();
            foreach (var u in inSys)
            {
                string p = PlaceName(u, out _);
                if (!byPlace.TryGetValue(p, out var l)) { byPlace[p] = l = new List<Unit>(); places.Add(p); }
                l.Add(u);
            }
            // Worlds first (alphabetical), then the ships under way, then those holding in deep space.
            places.Sort((a, b) => PlaceRank(a).CompareTo(PlaceRank(b)) != 0
                ? PlaceRank(a).CompareTo(PlaceRank(b)) : string.CompareOrdinal(a, b));

            foreach (var p in places)
            {
                var here = byPlace[p];
                PlaceHeader(p, here);
                foreach (var u in here) ShipRosterUI.Row(list, u, live);
            }
        }
    }

    static int PlaceRank(string p) => p == "In transit" ? 1 : p == "Deep space" ? 2 : 0;

    void SystemHeader(string name, List<Unit> ships, bool isFolded)
    {
        var bg = UIFactory.Panel(list, "System", UITheme.HeaderBg);
        UIFactory.AddLayout(bg.gameObject, 24, 24);
        var t = UIFactory.Text(bg.transform, $"{(isFolded ? "+" : "-")}  <b>{name}</b>  <size=10><color=#9FB4C8>{ships.Count} ship(s)</color></size>",
                               UITheme.SmallSize, UITheme.Accent, TextAlignmentOptions.Left);
        t.raycastTarget = false;
        UIFactory.Stretch(t.rectTransform, 8, 8, 0, 0);
        var btn = bg.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(() => { if (!folded.Remove(name)) folded.Add(name); lastSig = null; });
        UIFactory.Tooltip(bg.gameObject, isFolded ? "Click to show this system's ships." : "Click to fold this system away.");
    }

    void PlaceHeader(string name, List<Unit> ships)
    {
        var bg = UIFactory.Panel(list, "Place", new Color(0f, 0f, 0f, 0f));
        UIFactory.AddLayout(bg.gameObject, 20, 20);
        var t = UIFactory.Text(bg.transform, $"<b>{name}</b>  <size=10>({ships.Count})</size>",
                               11, UITheme.SubText, TextAlignmentOptions.Left);
        t.raycastTarget = false;
        UIFactory.Stretch(t.rectTransform, 4, 4, 0, 0);
        var captured = new List<Unit>(ships);
        var btn = bg.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(() =>
        {
            UnitSelection.Set(captured);
            if (captured.Count > 0)
            {
                SimpleAudio.Instance?.PlayUnitSelect(captured[0].type);
                ShipSelect.Focus(captured[0]);
            }
        });
        UIFactory.Tooltip(bg.gameObject, $"Select all {ships.Count} ship(s) here.");
    }

    void OnDestroy()
    {
        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged -= MarkDirty;
    }
}
