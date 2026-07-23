using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// THE OBJECT PANEL — every object in the galaxy, on one list, with a switch and a bin
//
// This is the tool that makes Visibility.cs and GalaxyTrash.cs usable. Without it the feature is an API
// nobody can reach: "hide that planet" would mean finding the planet, selecting it, and hoping some
// other window happened to expose the control.
//
// Every row is one OBJECT: a system, one of its suns, a planet, a moon, or a planet's orbit line. Each
// carries the same two controls — hide and delete — because the request was universal rather than a
// list of special cases, and because a tree where some rows can be hidden and others cannot is a tree
// you have to remember the rules for.
//
// GROUPED BY SYSTEM, and a system row acts on everything under it. That is what makes "delete an entire
// solar system" one click rather than fifteen.
//
// Dev Mode only, and unlike the terrain sandbox this window CLOSES itself when Dev Mode goes off rather
// than merely greying out — it deletes things, and a delete button left reachable in normal play is a
// way to lose a save.
// ============================================================================================
public class ObjectVisibilityWindow : MonoBehaviour
{
    public static ObjectVisibilityWindow Instance;

    GameObject root;
    RectTransform listContent;
    TMP_InputField filterField;
    TMP_Text summary;

    string filter = "";
    // Which systems are expanded. Collapsed by DEFAULT: twelve systems of six worlds with moons and
    // orbit lines is several hundred rows, and a list that opens showing all of them is a wall rather
    // than a tool. Kept by system NAME rather than by reference so it survives a delete/restore.
    readonly HashSet<string> expanded = new HashSet<string>();

    // The reason applied by the hide buttons. Dev by default — the other two are what a cloaking tech
    // and world generation set — but selectable, because testing "what does a cloaked planet look like
    // in the inspector" needs a way to make one.
    HideReason paintReason = HideReason.Dev;

    bool showTrash;
    bool rebuildQueued;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("ObjectVisibilityWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<ObjectVisibilityWindow>();
        Instance.Build(parent);
    }

    public bool IsOpen => root != null && root.activeSelf;

    public void Toggle()
    {
        if (root == null) return;
        bool on = !root.activeSelf;
        if (on && !GameMode.DevMode) return;   // never opens outside Dev Mode
        root.SetActive(on);
        if (on) { root.transform.SetAsLastSibling(); Rebuild(); }
    }

    public void Hide() { if (root != null) root.SetActive(false); }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Objects", new Vector2(440, 620), out root, out _);
        UIFactory.VerticalLayout(content, 6);

        UIFactory.Label(content,
            "<color=#FFBF4D>Dev Mode.</color> Hide or delete anything in the galaxy. Hidden objects keep " +
            "orbiting and keep being simulated — they are invisible, not gone.",
            UITheme.SmallSize, UITheme.SubText, 44);

        // ---- Reason picker ----
        UIFactory.Label(content, "HIDE AS", UITheme.SmallSize, UITheme.SubText, 18);
        var reasonRow = UIFactory.NewUI(content, "ReasonRow");
        UIFactory.AddLayout(reasonRow, 30, 30);
        var rh = reasonRow.AddComponent<HorizontalLayoutGroup>();
        rh.spacing = 4; rh.childControlWidth = true; rh.childForceExpandWidth = true;
        ReasonButton(reasonRow.transform, HideReason.Dev, "Dev",
                     "A developer tucked it away. No in-fiction meaning.");
        ReasonButton(reasonRow.transform, HideReason.Cloaked, "Cloaked",
                     "Concealed by technology. What a late-game cloaking tech will set, on ships as well as worlds.");
        ReasonButton(reasonRow.transform, HideReason.Undiscovered, "Undiscovered",
                     "Out there, and nobody has found it yet. What generation sets on the rare hidden world.");

        // ---- Global actions ----
        var actions = UIFactory.NewUI(content, "Actions");
        UIFactory.AddLayout(actions, 30, 30);
        var ah = actions.AddComponent<HorizontalLayoutGroup>();
        ah.spacing = 4; ah.childControlWidth = true; ah.childForceExpandWidth = true;
        UIFactory.Button(actions.transform, "Reveal all", () => { VisibilityService.RevealAll(); Rebuild(); }, 28);
        UIFactory.Button(actions.transform, "Hide all", () => { VisibilityService.HideAll(paintReason); Rebuild(); }, 28);
        UIFactory.Button(actions.transform, "Bin", () => { showTrash = !showTrash; Rebuild(); }, 28);

        // ---- Filter ----
        filterField = UIFactory.InputField(content, "Filter by name…");
        // Queued rather than immediate: every keystroke would otherwise rebuild several hundred rows
        // synchronously, and typing a system name into a twelve-system galaxy would stutter.
        filterField.onValueChanged.AddListener(v => { filter = v ?? ""; QueueRebuild(); });

        summary = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.SubText, 18);

        // ---- The list ----
        // preferredHeight is deliberately MODEST and flexibleHeight does the work. A VerticalLayoutGroup
        // hands out preferred height first and only then distributes what is left over to flexible
        // children — so asking for more than the window can spare gets this clamped back toward its
        // minimum instead of growing. Ask small, flex to fill.
        var listHolder = UIFactory.NewUI(content, "ListHolder");
        UIFactory.AddLayout(listHolder, 220, 160);
        var le = listHolder.GetComponent<LayoutElement>();
        le.flexibleHeight = 1;
        UIFactory.ScrollView(listHolder.transform, out listContent);

        root.SetActive(false);

        // Anything that changes what is hidden or what exists refreshes the list — including changes made
        // from somewhere else entirely, which is the point of listening rather than rebuilding on click.
        VisibilityService.OnChanged += QueueRebuild;
        GalaxyTrash.OnChanged += QueueRebuild;
        GameMode.OnChanged += OnDevModeChanged;
        SystemContext.OnSystemChanged += OnGalaxyChanged;
    }

    // A NEW galaxy invalidates the tree's own state.
    //
    // `expanded` is keyed by system NAME — unique within a galaxy (NameGenerator keeps a registry), but
    // that registry is RESET per galaxy, and the name catalogue is small enough that reuse across
    // galaxies is likely. Carried over, a fresh galaxy opens with a scattering of systems already
    // expanded for no reason the player can see. The bin view goes too: its contents were purged with
    // the old galaxy, so leaving it up would show an empty panel with no explanation.
    Galaxy lastSeen;

    void OnGalaxyChanged()
    {
        var g = SystemContext.Galaxy;
        if (!ReferenceEquals(g, lastSeen))
        {
            lastSeen = g;
            expanded.Clear();
            showTrash = false;
            filter = "";
            if (filterField != null) filterField.SetTextWithoutNotify("");
        }
        QueueRebuild();
    }

    void OnDevModeChanged()
    {
        if (!GameMode.DevMode) Hide();
    }

    // Coalesced to one rebuild per frame. A single "hide this system" fires OnChanged once, but
    // RevealAll and a galaxy rebuild both fire it alongside a SystemContext change — and rebuilding a
    // few hundred rows two or three times in one frame is visible as a hitch.
    void QueueRebuild()
    {
        if (!IsOpen) return;
        rebuildQueued = true;
    }

    void Update()
    {
        if (!rebuildQueued) return;
        rebuildQueued = false;
        Rebuild();
    }

    void ReasonButton(Transform parent, HideReason r, string label, string tip)
    {
        var b = UIFactory.Button(parent, label, null, 28);
        b.onClick.AddListener(() => { paintReason = r; Rebuild(); });
        UIFactory.Tooltip(b.gameObject, tip);
        // Re-tinted on every rebuild (see PaintReasonButtons) so the current choice is obvious.
        var tag = b.gameObject.AddComponent<ReasonTag>();
        tag.reason = r;
    }

    // ---- Building the list ---------------------------------------------------------------------

    void Rebuild()
    {
        if (listContent == null || !IsOpen) return;

        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);

        PaintReasonButtons();

        var g = SystemContext.Galaxy;
        if (g == null)
        {
            UIFactory.Label(listContent, "No galaxy generated.", UITheme.BodySize, UITheme.SubText, 24);
            if (summary != null) summary.text = "";
            return;
        }

        if (showTrash) { BuildTrashList(); return; }

        int hidden = VisibilityService.ListHidden().Count;
        if (summary != null)
            summary.text = $"{g.systems.Count} systems · <color=#FFBF4D>{hidden}</color> hidden · " +
                           $"<color=#FF8080>{GalaxyTrash.Items.Count}</color> in the bin";

        // The galactic core sits above the systems, because it is not one.
        if (g.center != null && Matches(g.center.name))
            StarRow(null, g.center, g.center.name + "  <size=10><color=#7F8C9B>galactic core</color></size>", 0, false);

        foreach (var sys in g.systems) BuildSystemRows(sys);
    }

    void BuildSystemRows(StarSystemData sys)
    {
        bool open = expanded.Contains(sys.name);

        // A filter that matched nothing in this system hides the whole branch — but a filter matching the
        // SYSTEM's own name keeps it, and opens it, so typing a system name is how you jump to one.
        bool nameHit = Matches(sys.name);
        if (!string.IsNullOrEmpty(filter) && !nameHit && !SystemHasMatch(sys)) return;
        if (!string.IsNullOrEmpty(filter)) open = true;

        var row = Row(0);
        // ASCII disclosure markers. The runtime font (LiberationSans SDF) is the minimal Latin atlas —
        // it has no glyph for the triangles U+25B8/U+25BE (nor ➤, as an earlier panel found), so they
        // rendered as tofu boxes and TMP logged a "character not found" warning on every layout pass.
        var toggle = UIFactory.Button(row, open ? "-" : "+", () =>
        {
            if (expanded.Contains(sys.name)) expanded.Remove(sys.name); else expanded.Add(sys.name);
            Rebuild();
        }, 26);
        Fixed(toggle.gameObject, 26);

        string tag = sys.isHome ? "  <size=10><color=#4DFF6E>home</color></size>"
                   : sys.isBlackHole ? "  <size=10><color=#9FB4C8>black hole</color></size>" : "";
        Name(row, $"<b>{sys.name}</b>{tag}", sys.hideReason);
        HideButton(row, sys.hideReason, on =>
        {
            if (on) VisibilityService.HideSystem(sys, paintReason); else VisibilityService.RevealSystem(sys);
            Rebuild();
        });
        DeleteButton(row, () =>
        {
            if (!GalaxyTrash.DeleteSystem(sys, out string why))
                NotificationManager.Instance?.Push("Objects", $"Can't delete {sys.name} — {why}.", null);
        });

        if (!open) return;

        // Suns. A black-hole system renders from combinedStar rather than from the stars list, so it is
        // listed from there — otherwise the one object in it would have no row.
        if (sys.isBlackHole && sys.combinedStar != null)
        {
            if (Matches(sys.combinedStar.name)) StarRow(sys, sys.combinedStar, sys.combinedStar.name, 1, false);
        }
        else
        {
            foreach (var s in sys.stars)
                if (s != null && Matches(s.name))
                    StarRow(sys, s, s.name, 1, sys.stars.Count > 1);
        }

        foreach (var b in sys.bodies)
        {
            bool bodyHit = Matches(b.name);
            bool moonHit = false;
            foreach (var m in b.moons) if (Matches(m.name)) { moonHit = true; break; }
            if (!bodyHit && !moonHit) continue;

            if (bodyHit) BodyRows(b, 1);
            foreach (var m in b.moons)
                if (Matches(m.name)) BodyRows(m, 2);
        }
    }

    void StarRow(StarSystemData sys, StarData s, string label, int depth, bool deletable)
    {
        var row = Row(depth);
        // Asterisk, not U+2605 — the star glyph is not in the runtime font and tofu'd. The warm colour
        // is what actually reads as "star" here; the character is just a marker.
        Name(row, $"<color=#FFD9A0>*</color> {label}", VisibilityService.ReasonFor(s, sys));
        HideButton(row, VisibilityService.ReasonFor(s, sys), on =>
        {
            if (on) VisibilityService.Hide(s, paintReason); else VisibilityService.Reveal(s);
            Rebuild();
        });

        if (deletable)
            DeleteButton(row, () =>
            {
                if (!GalaxyTrash.DeleteStar(sys, s, out string why))
                    NotificationManager.Instance?.Push("Objects", $"Can't delete {s.name} — {why}.", null);
            });
        else
            // A single sun, or the galactic core: no delete, and the button says why rather than being
            // silently absent. (Deleting the last star would leave Combine to roll a replacement.)
            DeadButton(row, "X", sys == null
                ? "The galactic core is scenery, not a system object — it cannot be deleted."
                : "A system's only star can't be deleted on its own — delete the system.");
    }

    void BodyRows(CelestialBody b, int depth)
    {
        var row = Row(depth);
        // ASCII markers, not U+25E6/U+25CF — those geometric discs are not in the runtime font. The
        // COLOUR carries planet-vs-moon; the glyph is only a bullet, and a lowercase 'o' for a moon vs an
        // 'O' for a planet keeps the same small-vs-large read without a character that tofus. (These rows
        // only render under an EXPANDED system, which is why they escaped the first tofu report.)
        string glyph = b.parentBody != null ? "<color=#9FB4C8>o</color>" : "<color=#8FD2FF>O</color>";
        var effectiveBody = VisibilityService.ReasonFor(b);

        // A moon goes with its planet (VisibilityService.ReasonFor), so its own switch cannot bring it
        // back while the planet is hidden. Say so rather than offering a control that appears not to work.
        bool byPlanet = b.parentBody != null && VisibilityService.ReasonFor(b.parentBody) != HideReason.None;

        Name(row, $"{glyph} {b.name}  <size=10><color=#7F8C9B>{b.type}</color></size>", effectiveBody);
        if (byPlanet)
            DeadButton(row, "Hidden", "Concealed along with the planet it orbits. Reveal the planet to get it back.");
        else
            HideButton(row, effectiveBody, on =>
            {
                if (on) VisibilityService.Hide(b, paintReason); else VisibilityService.Reveal(b);
                Rebuild();
            });
        DeleteButton(row, () =>
        {
            if (!GalaxyTrash.DeleteBody(b, out string why))
                NotificationManager.Instance?.Push("Objects", $"Can't delete {b.name} — {why}.", null);
        });

        // The orbit line is its own object, with its own row, as asked. Its hide state is separate from
        // the world's — but hiding the world conceals the line too, so the row reads as hidden and says
        // so rather than showing a switch that appears not to work.
        var lineRow = Row(depth + 1);
        var effective = VisibilityService.ReasonForOrbitLine(b);
        // Whatever conceals the WORLD conceals its line — its own flag, its system's, or (for a moon)
        // its planet's. ReasonFor already folds all three together, which is exactly the question here.
        bool byBody = effectiveBody != HideReason.None;
        Name(lineRow, byBody
            ? "<color=#5F6C7B>orbit line  <size=10>(hidden with the world)</size></color>"
            : "<color=#5F8FBF>o</color> orbit line", effective);

        if (byBody)
            DeadButton(lineRow, "Hidden", "Concealed along with the world it circles. Reveal the world to get it back.");
        else
            HideButton(lineRow, b.ringHideReason, on =>
            {
                if (on) VisibilityService.HideOrbitLine(b, paintReason); else VisibilityService.RevealOrbitLine(b);
                Rebuild();
            });

        // Deliberately no delete on an orbit line: it is drawn FROM the body's orbit, so "delete the
        // line" and "hide the line" are the same act — there is no separate object to remove. Hiding it
        // is permanent until something reveals it, which is what deleting it would have meant.
        DeadButton(lineRow, "—", "An orbit line is drawn from the world's orbit — hide it rather than delete it.");
    }

    void BuildTrashList()
    {
        if (summary != null)
            summary.text = $"<color=#FF8080>{GalaxyTrash.Items.Count}</color> deleted — newest first. " +
                           "Restore puts it back where it was.";

        var head = Row(0);
        UIFactory.Button(head, "< Back to the galaxy", () => { showTrash = false; Rebuild(); }, 28);
        if (GalaxyTrash.Items.Count > 0)
            UIFactory.Button(head, "Empty the bin", () => GalaxyTrash.PurgeAll(), 28);

        if (GalaxyTrash.Items.Count == 0)
        {
            UIFactory.Label(listContent, "Nothing deleted.", UITheme.BodySize, UITheme.SubText, 24);
            return;
        }

        // Copied before iterating: Restore and Purge both mutate the live list, and the button handlers
        // run while this list is what the UI is built from.
        var items = new List<GalaxyTrash.Entry>(GalaxyTrash.Items);
        foreach (var e in items)
        {
            var row = Row(0);
            Name(row, e.Describe(), HideReason.None);
            var restore = UIFactory.Button(row, "Restore", () =>
            {
                // A restore CAN legitimately fail — the system or planet it belonged to may have been
                // deleted after it was. Say which, or the button reads as broken.
                if (!GalaxyTrash.Restore(e, out string why))
                    NotificationManager.Instance?.Push("Objects", $"Can't restore {e.label} — {why}.", null);
            }, 26);
            Fixed(restore.gameObject, 74);
            var forever = UIFactory.Button(row, "Delete forever", () => GalaxyTrash.Purge(e), 26);
            Fixed(forever.gameObject, 106);
            UIFactory.Tooltip(forever.gameObject, "Drops it from the bin. There is no way back after this.");
        }
    }

    // ---- Row furniture -------------------------------------------------------------------------

    Transform Row(int depth)
    {
        var go = UIFactory.NewUI(listContent, "Row");
        UIFactory.AddLayout(go, 26, 26);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4;
        h.padding = new RectOffset(depth * 16, 0, 0, 0);   // indent = depth in the tree
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;
        return go.transform;
    }

    void Name(Transform row, string label, HideReason reason)
    {
        var t = UIFactory.Text(row, label, UITheme.SmallSize, HideReasons.Tint(reason), TextAlignmentOptions.Left);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1; le.minWidth = 80;
        if (reason != HideReason.None)
        {
            // UIFactory.Text builds its labels with raycastTarget OFF, so a tooltip on one would never
            // fire — the pointer passes straight through it. Turned back on for this row only.
            t.raycastTarget = true;
            UIFactory.Tooltip(t.gameObject, HideReasons.Label(reason) + " — still orbiting and still simulated, just not drawn.");
        }
    }

    void HideButton(Transform row, HideReason current, System.Action<bool> set)
    {
        bool hidden = current != HideReason.None;
        var b = UIFactory.Button(row, hidden ? HideReasons.Label(current) : "Hide", () => set(!hidden), 26);
        Fixed(b.gameObject, 96);
        var lbl = b.GetComponentInChildren<TMP_Text>();
        if (lbl != null) lbl.color = HideReasons.Tint(current);
        UIFactory.Tooltip(b.gameObject, hidden ? "Click to reveal." : $"Hide as {HideReasons.Label(paintReason)}.");
    }

    void DeleteButton(Transform row, System.Action onClick)
    {
        // Capital X — U+2715 (multiplication X) is not in the runtime font. Same tofu as the triangles
        // and the star above.
        var b = UIFactory.Button(row, "X", onClick, 26);
        Fixed(b.gameObject, 30);
        var lbl = b.GetComponentInChildren<TMP_Text>();
        if (lbl != null) lbl.color = new Color(1f, 0.55f, 0.55f);
        UIFactory.Tooltip(b.gameObject, "Delete. Goes to the bin — restore it from there.");
    }

    // A control that is deliberately unavailable AND says why. A greyed button with no explanation is a
    // dead end (the same rule PlanetViewWindow.TabAvailable follows).
    void DeadButton(Transform row, string label, string why)
    {
        var b = UIFactory.Button(row, label, null, 26);
        b.interactable = false;
        Fixed(b.gameObject, label.Length > 2 ? 74 : 30);
        UIFactory.Tooltip(b.gameObject, why);
    }

    static void Fixed(GameObject go, float width)
    {
        var le = UIFactory.Ensure<LayoutElement>(go);
        le.preferredWidth = width; le.minWidth = width; le.flexibleWidth = 0;
    }

    void PaintReasonButtons()
    {
        if (root == null) return;
        foreach (var tag in root.GetComponentsInChildren<ReasonTag>(true))
        {
            var lbl = tag.GetComponentInChildren<TMP_Text>();
            if (lbl == null) continue;
            lbl.color = tag.reason == paintReason ? HideReasons.Tint(tag.reason) : UITheme.SubText;
            lbl.fontStyle = tag.reason == paintReason ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    bool Matches(string name)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        return !string.IsNullOrEmpty(name) &&
               name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    bool SystemHasMatch(StarSystemData sys)
    {
        foreach (var s in sys.stars) if (s != null && Matches(s.name)) return true;
        if (sys.combinedStar != null && Matches(sys.combinedStar.name)) return true;
        foreach (var b in sys.AllBodies()) if (Matches(b.name)) return true;
        return false;
    }

    void OnDestroy()
    {
        VisibilityService.OnChanged -= QueueRebuild;
        GalaxyTrash.OnChanged -= QueueRebuild;
        GameMode.OnChanged -= OnDevModeChanged;
        SystemContext.OnSystemChanged -= OnGalaxyChanged;
        if (Instance == this) Instance = null;
    }
}

/// Marks one of the three reason buttons so PaintReasonButtons can find and re-tint it.
public class ReasonTag : MonoBehaviour { public HideReason reason; }
