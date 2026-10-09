using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// NAMEPLATES — where the things you have found are (2026-10-09)
//
// "These nameplates will help the player to see and know where things are that they have already
// discovered and not have to search for them." Two kinds:
//
//   BODY PLATES   — over every SURVEYED planet, moon and asteroid, riding along as it orbits.
//   SYSTEM PLATES — over every DISCOVERED system (a ship has been there; SystemPresence.Known), pinned to
//                   the system's pivot. The pivot IS the barycentre, so a binary's plate stays still
//                   while its suns circle underneath it rather than chasing one of them.
//
// Each plate is a bordered box with the name, and the owner's mark on its left: the player's own crest
// (CivEmblem) on the player's ground, a swatch of the owner's colour on a rival's — rivals have no crest
// art yet, and the player's must never be drawn over somebody else's world — and nothing when unclaimed.
// The border takes the owner's colour too, so ownership reads at a glance even where the mark is small.
//
// Screen-space, like ObjectLabelManager, on its own canvas just under it (the selected object's labels
// win where they overlap). Nothing here takes a raycast: a plate over a planet must never eat the click
// meant for the planet. The plate SET is reconciled a few times a second; positions every frame.
// ============================================================================================
public class NameplateManager : MonoBehaviour
{
    public static NameplateManager Instance;

    const float ReconcileEvery = 0.5f;
    const float PlateHeight = 22f;
    const float EmblemSize = 16f;
    const int FontSize = 13;
    /// A moon's plate is dropped when it would sit this close to its planet's on screen — at system
    /// zoom every moon crowds its host and the names would just stack into an unreadable block.
    const float MoonCrowdPx = 34f;

    static readonly Color Fill = new Color(0.04f, 0.06f, 0.09f, 0.82f);
    static readonly Color Unowned = new Color(0.55f, 0.60f, 0.66f, 0.9f);

    class Plate
    {
        public RectTransform rt;
        public Image border;
        public RawImage emblem;
        public Image swatch;
        public TMP_Text label;
        public Faction shownOwner;
        public bool ownerSet;
        public string shownName;
    }

    Canvas canvas;
    Camera cam;
    float nextReconcile;
    readonly Dictionary<CelestialBody, Plate> bodyPlates = new Dictionary<CelestialBody, Plate>();
    readonly Dictionary<StarSystemData, Plate> systemPlates = new Dictionary<StarSystemData, Plate>();
    readonly List<CelestialBody> deadBodies = new List<CelestialBody>();
    readonly List<StarSystemData> deadSystems = new List<StarSystemData>();
    readonly HashSet<CelestialBody> wantBodies = new HashSet<CelestialBody>();
    readonly HashSet<StarSystemData> wantSystems = new HashSet<StarSystemData>();

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("NameplateManager").AddComponent<NameplateManager>();
    }

    void Awake()
    {
        Instance = this;
        canvas = UIFactory.CreateCanvas("NameplateCanvas", 45);   // under ObjectLabelManager's 50
        canvas.transform.SetParent(transform, false);
        // No GraphicRaycaster: plates are labels, never targets.
        var gr = canvas.GetComponent<GraphicRaycaster>();
        if (gr != null) Destroy(gr);
    }

    void OnEnable() { CivEmblem.OnChanged += ForceOwnerRefresh; }
    void OnDisable() { CivEmblem.OnChanged -= ForceOwnerRefresh; }

    void ForceOwnerRefresh()
    {
        foreach (var p in bodyPlates.Values) p.ownerSet = false;
        foreach (var p in systemPlates.Values) p.ownerSet = false;
    }

    // ---- The set of plates ------------------------------------------------------------------------

    void Reconcile()
    {
        wantBodies.Clear();
        wantSystems.Clear();
        var galaxy = SystemContext.Galaxy;
        if (galaxy != null && galaxy.systems != null)
            foreach (var sys in galaxy.systems)
            {
                if (sys == null) continue;
                if (sys.pivot != null && SystemPresence.Known(sys)) wantSystems.Add(sys);
                foreach (var b in sys.AllBodies())
                    if (b != null && b.visualObject != null && b.Surveyed) wantBodies.Add(b);
            }

        deadBodies.Clear();
        foreach (var kv in bodyPlates) if (!wantBodies.Contains(kv.Key)) deadBodies.Add(kv.Key);
        foreach (var b in deadBodies) { Kill(bodyPlates[b]); bodyPlates.Remove(b); }
        foreach (var b in wantBodies) if (!bodyPlates.ContainsKey(b)) bodyPlates[b] = MakePlate();

        deadSystems.Clear();
        foreach (var kv in systemPlates) if (!wantSystems.Contains(kv.Key)) deadSystems.Add(kv.Key);
        foreach (var s in deadSystems) { Kill(systemPlates[s]); systemPlates.Remove(s); }
        foreach (var s in wantSystems) if (!systemPlates.ContainsKey(s)) systemPlates[s] = MakePlate();
    }

    static void Kill(Plate p) { if (p != null && p.rt != null) Destroy(p.rt.gameObject); }

    Plate MakePlate()
    {
        var p = new Plate();
        // Border = the outer image; the dark fill sits one pixel inside it.
        p.border = UIFactory.Panel(canvas.transform, "Nameplate", Unowned);
        p.border.raycastTarget = false;
        p.rt = p.border.rectTransform;
        p.rt.pivot = new Vector2(0.5f, 0f);
        p.rt.sizeDelta = new Vector2(80f, PlateHeight);

        var fill = UIFactory.Panel(p.rt, "Fill", Fill);
        fill.raycastTarget = false;
        UIFactory.Stretch(fill.rectTransform, 1, 1, 1, 1);

        var row = fill.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(5, 7, 2, 2);
        row.spacing = 5;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true; row.childControlHeight = true;
        row.childForceExpandWidth = false; row.childForceExpandHeight = false;

        var eGo = UIFactory.NewUI(fill.transform, "Emblem");
        p.emblem = eGo.AddComponent<RawImage>();
        p.emblem.raycastTarget = false;
        var eLe = eGo.AddComponent<LayoutElement>();
        eLe.preferredWidth = eLe.minWidth = EmblemSize; eLe.preferredHeight = eLe.minHeight = EmblemSize;

        p.swatch = UIFactory.Panel(fill.transform, "Swatch", Color.white);
        p.swatch.raycastTarget = false;
        var sLe = p.swatch.gameObject.AddComponent<LayoutElement>();
        sLe.preferredWidth = sLe.minWidth = EmblemSize * 0.6f; sLe.preferredHeight = sLe.minHeight = EmblemSize * 0.6f;

        p.label = UIFactory.Text(fill.transform, "", FontSize, UITheme.Text, TextAlignmentOptions.Left);
        p.label.raycastTarget = false;
        p.label.overflowMode = TextOverflowModes.Overflow;
        p.label.fontStyle = FontStyles.Bold;
        return p;
    }

    void SetContent(Plate p, string name, Faction owner)
    {
        if (p.shownName != name)
        {
            p.shownName = name;
            p.label.text = name;
            // Width from the text itself: TMP's preferred width plus the row's padding, mark and gap.
            float w = p.label.GetPreferredValues(name).x + 14f + 2f + EmblemSize + 5f;
            p.rt.sizeDelta = new Vector2(Mathf.Max(48f, w), PlateHeight);
        }
        if (p.ownerSet && p.shownOwner == owner) return;
        p.ownerSet = true;
        p.shownOwner = owner;

        bool mine = owner != null && owner == FactionManager.Player;
        Texture2D crest = mine ? CivEmblem.Current : null;
        p.emblem.gameObject.SetActive(crest != null);
        p.emblem.texture = crest;
        // A rival (or the player with no crest art loaded) gets a swatch of their colour instead.
        p.swatch.gameObject.SetActive(owner != null && crest == null);
        if (owner != null) p.swatch.color = FactionManager.OwnerColor(owner);
        p.border.color = owner != null ? FactionManager.OwnerColor(owner) : Unowned;
    }

    // ---- Every frame: where they go ---------------------------------------------------------------

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextReconcile)
        {
            nextReconcile = Time.unscaledTime + ReconcileEvery;
            Reconcile();
        }

        if (cam == null) cam = Camera.main;
        bool hideAll = cam == null || GenesisSequence.Running || GenesisCamera.Active;
        canvas.enabled = !hideAll;
        if (hideAll) return;

        bool systemZoom = GalaxyLOD.SystemMode;

        foreach (var kv in systemPlates)
        {
            var sys = kv.Key; var p = kv.Value;
            bool show = sys.pivot != null && sys.hideReason == HideReason.None;
            float lift = sys.combinedStar != null ? OrbitSafety.StarRadius(sys.combinedStar) : 2f;
            if (show) show = Place(p, sys.pivot.position, lift);
            p.rt.gameObject.SetActive(show);
            if (show) SetContent(p, sys.name, sys.owner);
        }

        foreach (var kv in bodyPlates)
        {
            var b = kv.Key; var p = kv.Value;
            bool show = systemZoom && b.visualObject != null && b.visualObject.activeInHierarchy;
            if (show)
            {
                var bind = b.visualObject.GetComponent<ConcealBinding>();
                if (bind != null && bind.Concealed) show = false;
            }
            if (show)
            {
                var t = b.visualObject.transform;
                show = Place(p, t.position, Mathf.Max(0.2f, t.lossyScale.x * 0.5f));
                if (show && b.parentBody != null && b.parentBody.visualObject != null && cam != null)
                {
                    Vector3 a = cam.WorldToScreenPoint(t.position);
                    Vector3 h = cam.WorldToScreenPoint(b.parentBody.visualObject.transform.position);
                    if (((Vector2)a - (Vector2)h).sqrMagnitude < MoonCrowdPx * MoonCrowdPx) show = false;
                }
            }
            p.rt.gameObject.SetActive(show);
            if (show) SetContent(p, b.name, b.owner);
        }
    }

    /// Put a plate just above a world-space point with the given radius. False if it is behind the
    /// camera or off screen.
    bool Place(Plate p, Vector3 world, float radius)
    {
        Vector3 c = cam.WorldToScreenPoint(world);
        if (c.z <= 0f) return false;
        if (c.x < -100f || c.x > Screen.width + 100f || c.y < -100f || c.y > Screen.height + 100f) return false;
        Vector3 top = cam.WorldToScreenPoint(world + cam.transform.up * radius);
        float r = Mathf.Max(6f, Mathf.Abs(top.y - c.y));
        p.rt.position = new Vector3(c.x, c.y + r + 6f, 0f);
        return true;
    }
}
