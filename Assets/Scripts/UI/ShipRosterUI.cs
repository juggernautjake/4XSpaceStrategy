using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// ONE SHIP ROW, EVERYWHERE A SHIP IS LISTED (2026-10-11)
//
// The Inspector's Orbit tab, the Planet View's Orbit tab and the Fleet list all show the same thing —
// a ship — and used to draw three different rows for it. One row now, so a ship reads the same wherever
// you meet it:
//
//   [glyph]  Name  [squadron]                              ████░░  82%   [Send to…]
//            Class · Owner · Status (where)
//
// The glyph is the same class glyph, in the same owner colour, as the ship's marker on the map
// (ShipGlyphs), so the list and the map are visibly about the same objects. Status and health are live.
// Clicking the row selects the ship and flies the camera to it; Shift-click adds it to the selection
// without moving the camera. Your own ships get a "Send to…" button (the destination list).
// ============================================================================================
public static class ShipRosterUI
{
    const float RowHeight = 36f;

    /// Build one row under `parent`. `where` is an extra place note ("at Phobos"); `onSelect` replaces
    /// the default click (select and focus) when a window needs to do more — the Inspector keeps its
    /// breadcrumb, for example.
    public static GameObject Row(Transform parent, Unit u, LiveSet live, string where = null,
                                 System.Action onSelect = null, bool orders = true)
    {
        var cap = u;
        var bg = UIFactory.Panel(parent, "ShipRow", UITheme.RowBg);
        var row = bg.gameObject;
        UIFactory.AddLayout(row, RowHeight, RowHeight);

        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(6, 6, 3, 3);
        h.spacing = 6;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;

        // ---- class glyph in the owner's colour, exactly as on the map ----
        var gGo = UIFactory.NewUI(row.transform, "Glyph");
        var glyph = gGo.AddComponent<RawImage>();
        glyph.texture = ShipGlyphs.For(u);
        glyph.color = FactionManager.OwnerColor(u.owner);
        glyph.raycastTarget = false;
        var gle = gGo.AddComponent<LayoutElement>();
        gle.preferredWidth = gle.minWidth = 18; gle.preferredHeight = gle.minHeight = 18; gle.flexibleWidth = 0;

        // ---- name, and class · owner · status ----
        var text = UIFactory.Text(row.transform, "", UITheme.SmallSize, UITheme.Text, TextAlignmentOptions.Left);
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        var tle = text.gameObject.AddComponent<LayoutElement>();
        tle.flexibleWidth = 1; tle.minWidth = 60;
        bool mine = u.owner == FactionManager.Player;
        string ownerPart = mine ? "" :
            $" · <color=#{ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(u.owner))}>{FactionManager.OwnerName(u.owner)}</color>";
        string wherePart = string.IsNullOrEmpty(where) ? "" : $" <color=#7F8FA3>({where})</color>";
        live.Text(text, () =>
        {
            int g = mine ? ControlGroups.GroupOf(cap) : 0;
            string badge = g > 0 ? $" <color=#5AB4F0>[{g}]</color>" : "";
            string status = ColorUtility.ToHtmlStringRGB(ShipRoster.StatusColor(cap));
            return $"<b>{cap.name}</b>{badge}\n<size=10><color=#9FB4C8>{cap.Info.name}</color>{ownerPart}" +
                   $" · <color=#{status}>{ShipRoster.StatusWord(cap)}</color>{wherePart}</size>";
        });

        // ---- hull condition ----
        var hp = UIFactory.NewUI(row.transform, "Hull");
        var hle = hp.AddComponent<LayoutElement>();
        hle.preferredWidth = hle.minWidth = 58; hle.preferredHeight = 26; hle.flexibleWidth = 0;
        var track = UIFactory.Panel(hp.transform, "Track", new Color(0f, 0f, 0f, 0.45f));
        track.raycastTarget = false;
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(1f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(0f, 6f); trt.anchoredPosition = new Vector2(0f, 5f);
        var fill = UIFactory.Panel(track.transform, "Fill", UITheme.Good);
        fill.raycastTarget = false;
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(1f, 1f);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        var pct = UIFactory.Text(hp.transform, "", 10, UITheme.SubText, TextAlignmentOptions.Center);
        pct.raycastTarget = false;
        var prt = pct.rectTransform;
        prt.anchorMin = new Vector2(0f, 0f); prt.anchorMax = new Vector2(1f, 0.5f);
        prt.offsetMin = Vector2.zero; prt.offsetMax = new Vector2(0f, 2f);
        live.Bar(fill, () =>
        {
            float f = cap.HealthFraction;
            return (f, $"{f * 100f:F0}%", Fleets.ConditionColor(f));
        }, pct);

        // ---- shield, a thin strip riding on top of the hull bar (Shields.cs) ----
        if (cap.MaxShield > 0)
        {
            var sTrack = UIFactory.Panel(hp.transform, "ShieldTrack", new Color(0f, 0f, 0f, 0.45f));
            sTrack.raycastTarget = false;
            var strt = sTrack.rectTransform;
            strt.anchorMin = new Vector2(0f, 0.5f); strt.anchorMax = new Vector2(1f, 0.5f);
            strt.pivot = new Vector2(0.5f, 0.5f);
            strt.sizeDelta = new Vector2(0f, 3f); strt.anchoredPosition = new Vector2(0f, 10.5f);
            var sFill = UIFactory.Panel(sTrack.transform, "Fill", Shields.BarColour(1f));
            sFill.raycastTarget = false;
            var sfrt = sFill.rectTransform;
            sfrt.anchorMin = Vector2.zero; sfrt.anchorMax = new Vector2(1f, 1f);
            sfrt.offsetMin = Vector2.zero; sfrt.offsetMax = Vector2.zero;
            live.Bar(sFill, () =>
            {
                float f = cap.ShieldFraction;
                return (f, "", Shields.BarColour(f));
            });
        }

        // ---- quick order, own ships only ----
        if (orders && mine && !u.Info.isStation)
        {
            var send = UIFactory.Button(row.transform, "Send to…", () =>
            {
                UnitSelection.SelectOnly(cap);
                SendToWindow.Instance?.Open(new List<Unit> { cap });
            }, 24);
            var sle = send.GetComponent<LayoutElement>();
            sle.preferredWidth = sle.minWidth = 70; sle.flexibleWidth = 0;
            var st = send.GetComponentInChildren<TMP_Text>();
            if (st != null) st.fontSize = 11;
            live.Button(send, () =>
            {
                bool can = cap.location != null || cap.inSpace || cap.status == UnitStatus.Traveling;
                return (can, "Send to…");
            });
            UIFactory.Tooltip(send.gameObject, $"Pick a destination for {cap.name} from the list of known places.");
        }

        // ---- the row itself: select (F focuses — see ShipSelect) ----
        var btn = row.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = btn.colors;
        colors.normalColor = Color.white; colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white; colors.pressedColor = new Color(0.8f, 0.9f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(() =>
        {
            if (onSelect != null) onSelect();
            else ShipSelect.SelectRow(cap);
        });

        // Selected ships are lit, so the list shows what the map's brackets show.
        live.Custom(() =>
        {
            if (bg == null) return;
            var want = UnitSelection.IsSelected(cap) ? new Color(0.13f, 0.24f, 0.34f, 0.95f) : UITheme.RowBg;
            if (bg.color != want) bg.color = want;
        });

        UIFactory.Tooltip(row, $"{cap.HoverText()}\n{ShipRoster.StatusLine(cap)}\n\n" +
                               "Click to select it; press F to focus the camera on it.\nShift-click adds it to the selection.");
        return row;
    }

    /// A section of rows for every ship ShipRoster lists at this world — the shared body of both Orbit
    /// tabs. Stations and ships are listed apart; a station is infrastructure, not a fleet. Returns how
    /// many rows were drawn.
    public static int OrbitSections(Transform parent, CelestialBody b, LiveSet live,
                                    System.Action<Transform, string> header,
                                    System.Action<Transform, string> note,
                                    System.Action<Unit> onSelect = null)
    {
        var entries = new List<ShipRoster.Entry>();
        ShipRoster.CollectAt(b, entries);

        int mineN = 0, theirsN = 0, stationsN = 0;
        foreach (var e in entries)
        {
            if (e.unit.Info.isStation) stationsN++;
            else if (e.unit.owner == FactionManager.Player) mineN++;
            else theirsN++;
        }

        header(parent, mineN > 0 ? $"YOUR SHIPS ({mineN})" : "YOUR SHIPS");
        if (mineN == 0) note(parent, "None of your ships are docked here or in this world's orbit.");
        foreach (var e in entries)
            if (!e.unit.Info.isStation && e.unit.owner == FactionManager.Player) Add(parent, e, live, onSelect);

        if (theirsN > 0)
        {
            header(parent, $"OTHER SHIPS ({theirsN})");
            foreach (var e in entries)
                if (!e.unit.Info.isStation && e.unit.owner != FactionManager.Player) Add(parent, e, live, onSelect);
        }

        header(parent, stationsN > 0 ? $"STATIONS ({stationsN})" : "STATIONS");
        if (stationsN == 0) note(parent, "No stations deployed here.");
        foreach (var e in entries)
            if (e.unit.Info.isStation) Add(parent, e, live, onSelect);

        return entries.Count;
    }

    static void Add(Transform parent, ShipRoster.Entry e, LiveSet live, System.Action<Unit> onSelect)
    {
        string where = e.where != null ? $"at {e.where}" : null;
        var u = e.unit;
        Row(parent, u, live, where, onSelect != null ? () => onSelect(u) : (System.Action)null);
    }
}
