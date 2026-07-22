using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// SEND TO — pick a destination from a list, instead of hunting for it on the map
//
// The existing way to move a fleet is to arm targeting and click the place (FleetMovementController).
// That is fine when the destination is on screen and terrible when it is not: a world three systems
// over means zooming out to the galaxy, finding the system, zooming back in, and clicking a planet the
// size of a few pixels that is also moving.
//
// So this lists every place the player actually KNOWS ABOUT and lets them pick one. Both routes stay —
// click-to-target is still the fast one for somewhere you can see, and this is the reliable one for
// somewhere you cannot.
//
// ONLY PLACES YOU HAVE FOUND. The list is built from worlds that have been visited or surveyed (plus
// anything you own). Listing the whole galaxy would hand the player a map they have not earned, and
// would make exploration pointless — the point of a survey is that it puts somewhere ON this list.
//
// EVERY SEND IS CONFIRMED. A misclick in a scrolling list is far easier than a misclick on the map,
// and a long-haul journey is minutes of game time; the confirmation says where, how far, and roughly
// how long, so the answer to "did I mean that" is on screen rather than in the player's head.
// ============================================================================================
public class SendToWindow : MonoBehaviour
{
    public static SendToWindow Instance;

    GameObject root;
    RectTransform list;
    TMP_Text header;

    // The ships this send will apply to, captured when the window opens. Held rather than re-read from
    // the live selection each frame: the list is scrollable and the confirmation is a second click, so
    // the selection can change underneath it — and "send whatever happens to be selected when you
    // finally click confirm" is how the wrong fleet gets sent somewhere.
    readonly List<Unit> fleet = new List<Unit>();

    CelestialBody pending;      // chosen, awaiting confirmation
    GameObject confirmRow;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("SendToWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<SendToWindow>();
        Instance.Build(parent);
    }

    public bool IsOpen => root != null && root.activeSelf;

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Send to", new Vector2(420, 560), out root, out _);
        UIFactory.VerticalLayout(content, 6);

        header = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.SubText, 34);

        var holder = UIFactory.NewUI(content, "ListHolder");
        UIFactory.AddLayout(holder, 220, 160);
        holder.GetComponent<LayoutElement>().flexibleHeight = 1;
        UIFactory.ScrollView(holder.transform, out list);

        root.SetActive(false);
    }

    /// Open for the given ships. Called from the ship panel's "Send to…" button.
    public void Open(List<Unit> ships)
    {
        if (root == null) return;

        fleet.Clear();
        if (ships != null)
            foreach (var u in ships)
                if (u != null && (u.location != null || u.inSpace)) fleet.Add(u);

        if (fleet.Count == 0) { Hide(); return; }

        pending = null;
        root.SetActive(true);
        root.transform.SetAsLastSibling();
        Rebuild();
    }

    public void Hide()
    {
        pending = null;
        if (root != null) root.SetActive(false);
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Open(new List<Unit>(UnitSelection.Selected));
    }

    void Rebuild()
    {
        if (list == null) return;
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
        confirmRow = null;

        if (header != null)
            header.text = fleet.Count == 1
                ? $"Send <b>{fleet[0].name}</b> to…"
                : $"Send <b>{fleet.Count} ships</b> to…";

        var g = SystemContext.Galaxy;
        if (g == null) { UIFactory.Label(list, "No galaxy.", UITheme.BodySize, UITheme.SubText, 24); return; }

        Vector3 from = UnitManager.Instance != null && fleet.Count > 0
            ? UnitManager.Instance.UnitPos(fleet[0]) : Vector3.zero;

        int listed = 0;
        foreach (var sys in g.systems)
        {
            if (sys == null) continue;

            // A system is worth a heading only if something in it is known. Printing every system with
            // "(nothing surveyed)" underneath would bury the handful of real destinations.
            var known = new List<CelestialBody>();
            foreach (var b in sys.AllBodies())
                if (IsKnown(b)) known.Add(b);
            if (known.Count == 0) continue;

            UIFactory.Label(list, $"<b>{sys.name}</b>", UITheme.SmallSize, UITheme.Accent, 20);

            foreach (var b in known)
            {
                var target = b;   // captured per row
                listed++;

                float dist = UnitManager.Instance != null
                    ? Vector3.Distance(from, UnitManager.Instance.WorldPos(target)) : 0f;

                string kind = target.parentBody != null ? $"moon of {target.parentBody.name}" : target.type.ToString();
                string owner = target.owner != null ? $" · {FactionManager.OwnerLabel(target.owner)}" : "";

                var b2 = UIFactory.Button(list, "", () => Choose(target), 30);
                var lbl = b2.GetComponentInChildren<TMP_Text>();
                if (lbl != null)
                {
                    lbl.alignment = TextAlignmentOptions.Left;
                    lbl.text = $"  {target.name}   <size=10><color=#9FB4C8>{kind}{owner} · {dist:F0} away</color></size>";
                }
            }
        }

        if (listed == 0)
            UIFactory.Label(list,
                "Nowhere known yet. Survey a world and it appears here.",
                UITheme.SmallSize, UITheme.SubText, 40);
    }

    /// Somewhere you have actually been or charted. Owning it counts too — the home world and its moons
    /// are yours from turn one and are obviously known.
    static bool IsKnown(CelestialBody b)
        => b != null && (b.Visited || b.Surveyed || b.owner == FactionManager.Player);

    void Choose(CelestialBody target)
    {
        pending = target;
        Rebuild();
        ShowConfirm();
    }

    void ShowConfirm()
    {
        if (pending == null || list == null) return;

        // Built at the TOP of the list so it cannot be scrolled off screen — a confirmation the player
        // has to go looking for is a confirmation they will click through blind.
        var card = UIFactory.Panel(list, "Confirm", UITheme.HeaderBg);
        card.rectTransform.SetAsFirstSibling();
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(10, 10, 8, 8); v.spacing = 5;
        v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true;
        UIFactory.AddLayout(card.gameObject, 108, 108);
        confirmRow = card.gameObject;

        string who = fleet.Count == 1 ? fleet[0].name : $"{fleet.Count} ships";
        UIFactory.WrapText(card.transform, $"Send <b>{who}</b> to <b>{pending.name}</b>?",
                           UITheme.BodySize, UITheme.Text);

        var eta = UIFactory.Text(card.transform, EtaLine(), UITheme.SmallSize, UITheme.SubText,
                                 TextAlignmentOptions.Left);
        UIFactory.AddLayout(eta.gameObject, 18);

        var row = UIFactory.NewUI(card.transform, "Buttons");
        UIFactory.AddLayout(row, 30, 30);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.childControlWidth = true; h.childForceExpandWidth = true;
        UIFactory.Button(row.transform, "Confirm", Confirm, 28);
        UIFactory.Button(row.transform, "Cancel", () => { pending = null; Rebuild(); }, 28);
    }

    string EtaLine()
    {
        if (pending == null || UnitManager.Instance == null || fleet.Count == 0) return "";

        Vector3 from = UnitManager.Instance.UnitPos(fleet[0]);
        float dist = Vector3.Distance(from, UnitManager.Instance.WorldPos(pending));

        // The SLOWEST ship sets the pace, matching UnitManager.SendUnits — a fleet arrives together.
        int slow = int.MaxValue;
        foreach (var u in fleet) slow = Mathf.Min(slow, Mathf.Max(1, u.Speed));

        float seconds = dist / Mathf.Max(1, slow);
        return $"{dist:F0} units away · about {seconds:F0}s at the fleet's slowest speed";
    }

    void Confirm()
    {
        if (pending == null || fleet.Count == 0) { Hide(); return; }

        UnitManager.Instance?.SendUnits(new List<Unit>(fleet), pending);

        // Keep watching whatever the player was watching. If the camera is following one of these ships,
        // it follows it to the destination — which is the whole point of asking for a send from a list
        // rather than by clicking a place you can already see.
        var cam = CameraController.Instance;
        if (cam != null && cam.followUnit != null && !fleet.Contains(cam.followUnit))
            cam.FocusUnit(fleet[0]);

        Hide();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
