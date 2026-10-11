using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// ============================================================================================
// EVERY SHIP YOU CAN SEE HAS A FOOTPRINT YOU CAN SEE (2026-10-11)
//
// "If a ship is going around a planet, we should be able to easily locate and select it."
//
// At system zoom a hull is a few pixels (ShipLOD measures a dreadnought at nine; a scout is a third of
// that), and a ship on the far side of its orbit is behind the planet entirely. So every ship the player
// is shown gets a crisp screen-space MARKER: a 9x9 pixel-art class glyph in its owner's colour, drawn at
// an integer pixel scale so it stays sharp. As the hull grows on screen the marker fades out (between
// FadeFromPx and FadeToPx of hull) and the model takes over; zoom back out and it fades in again.
//
// STACKS. Twenty ships at one world at system zoom would be twenty glyphs on top of each other and on
// top of the world. When the ships docked at one world (per owner) would draw within CollapsePx of each
// other, they collapse into ONE badge set just beside the world, with a count — and the world itself
// stays clear to click. Ships in open space that overlap on screen (a fleet in formation, seen from far
// away) collapse the same way where they are. Zoom in and the stack opens back into its ships.
//
// SELECTION. A selected ship wears corner brackets — around its marker, or around the hull itself once
// the hull is big enough to have replaced the marker. Meshed hulls had no selection highlight at all
// before this; only billboard tokens grew when selected.
//
// PICKING. The markers take no raycasts (a GraphicRaycaster would make every marker "UI", and every
// click guard in the game — right-click orders, box select, the planet handlers — would start refusing
// clicks over ships). Instead `Pick` answers "which marker is under this point" geometrically, with a
// generous HitRadiusPx, and the click paths ask it: ClickPriority (so a ship beats the planet behind it),
// the box selector (so a click on a marker is not a click on empty space), and this component's own poll
// for markers over open space. See ClickPriority for the ship-vs-planet rule.
//
// FOG. Only ships VisibilityService.ShownToPlayer says are drawn — the same test the hulls, shots and
// explosions use — and only in system view (MapTierVisibility hides ships at galaxy zoom; so do these).
// ============================================================================================
[DefaultExecutionOrder(1000)]   // after the unit renderers have placed this frame's hulls
public class ShipMarkers : MonoBehaviour
{
    public static ShipMarkers Instance;

    /// Hull height on screen at which its marker starts to fade, and where it is gone.
    const float FadeFromPx = 22f, FadeToPx = 44f;
    /// Ships docked at one world collapse to a single badge when they would draw this close together.
    const float CollapsePx = 26f;
    /// Ships in open space this close on screen (same owner) collapse where they are.
    const float ClusterPx = 12f;
    /// Pick radius around a marker, and the tighter "core" a click needs when it is over a planet's disc.
    public const float HitRadiusPx = 14f, CoreRadiusPx = 7f;
    /// Glyph art is 9x9 texels drawn at this many canvas units per texel.
    const int TexelScale = 2;
    const float GlyphUnits = 9 * TexelScale;

    static readonly Color BracketSelected = new Color(0.55f, 0.92f, 1f, 1f);
    static readonly Color BracketHover = new Color(0.85f, 0.92f, 1f, 0.55f);
    static readonly Color PlateFill = new Color(0.03f, 0.05f, 0.08f, 0.88f);

    // ---- what was drawn this frame, for picking ----

    public struct Hit
    {
        public Unit unit;          // the ship, or the lead of a stack
        public List<Unit> stack;   // every ship a stack stands for; null for a single marker
        public bool core;          // the point is within CoreRadiusPx of the marker's centre
    }

    class Item
    {
        public Unit lead;
        public List<Unit> members;   // null = single
        public Vector2 pos;
        public float alpha;          // marker opacity (0 = hull is big enough; bracket only)
        public float boxHalf;        // bracket half-size
        public bool selected;
        public CelestialBody at;     // a docked stack's world, for its label
    }

    class Raw
    {
        public Unit u;
        public Vector2 pos;
        public float hullPx;
        public float alpha;
        public bool used;
    }

    class Marker
    {
        public RectTransform rt;
        public RawImage glyph;
        public RectTransform badge;
        public TMP_Text count;
        public readonly Image[] bars = new Image[8];
    }

    Canvas canvas;
    CanvasScaler scaler;
    Camera cam;
    readonly List<Marker> pool = new List<Marker>();
    readonly List<Item> items = new List<Item>();
    readonly List<Item> itemPool = new List<Item>();
    readonly List<Raw> raws = new List<Raw>();
    readonly List<Raw> rawPool = new List<Raw>();
    readonly Dictionary<CelestialBody, List<Raw>> docked = new Dictionary<CelestialBody, List<Raw>>();
    readonly Dictionary<UnitType, float> hullSize = new Dictionary<UnitType, float>();

    RectTransform labelPlate;
    TMP_Text labelText;
    Item hovered;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("ShipMarkers").AddComponent<ShipMarkers>();
    }

    void Awake()
    {
        Instance = this;
        // Above the world nameplates (45) so a ship is never buried under a planet's name, under the
        // selection labels (50), and far under the windows (100) — a window always covers the map.
        canvas = UIFactory.CreateCanvas("ShipMarkerCanvas", 47);
        canvas.transform.SetParent(transform, false);
        var gr = canvas.GetComponent<GraphicRaycaster>();
        if (gr != null) Destroy(gr);   // geometric picking only — see the header
        scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        BuildLabel();
    }

    // ---- public questions ------------------------------------------------------------------------

    /// The marker under a screen point (pixels), nearest first, or false. Markers that have faded out
    /// because the hull is big enough are not pickable — the hull's own collider is, then.
    public bool Pick(Vector2 screen, out Hit hit)
    {
        hit = default;
        if (canvas == null || !canvas.enabled) return false;
        float best = float.MaxValue;
        Item bestItem = null;
        float r2 = HitRadiusPx * Scale * HitRadiusPx * Scale;
        foreach (var it in items)
        {
            if (it.alpha < 0.2f) continue;
            float d = (it.pos - screen).sqrMagnitude;
            if (d > r2 || d >= best) continue;
            best = d; bestItem = it;
        }
        if (bestItem == null) return false;
        hit.unit = bestItem.lead;
        hit.stack = bestItem.members;
        hit.core = best <= CoreRadiusPx * Scale * CoreRadiusPx * Scale;
        return true;
    }

    /// Is there a ship marker under the mouse right now?
    public static bool AnyUnderCursor()
        => Instance != null && Instance.Pick(Input.mousePosition, out _);

    float Scale => canvas != null ? Mathf.Max(1f, canvas.scaleFactor) : 1f;

    // ---- clicks over open space ----------------------------------------------------------------

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (canvas == null || !canvas.enabled) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting) return;
        if (!Pick(Input.mousePosition, out var hit)) return;

        // A world or a star under the cursor resolves the click itself, through ClickPriority, which
        // knows whether the cursor is on the world as drawn. Everything else — open space, a ship's own
        // collider, a derelict — is this marker's.
        if (cam != null && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out RaycastHit rh, 5000f))
        {
            var col = rh.collider;
            if (col.GetComponentInParent<PlanetClick>() != null || col.GetComponentInParent<StarInteraction>() != null)
                return;
        }
        ShipSelect.Click(hit.unit, hit.stack);
    }

    // ---- every frame: where the markers go -----------------------------------------------------

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        bool show = cam != null && GalaxyLOD.SystemMode && !GenesisSequence.Running && !GenesisCamera.Active
                    && UnitManager.Instance != null;
        if (canvas.enabled != show) canvas.enabled = show;
        if (!show)
        {
            foreach (var it in items) { it.members = null; itemPool.Add(it); }
            items.Clear();
            hovered = null;
            return;
        }

        if (scaler != null)
        {
            float want = Mathf.Max(1, Mathf.RoundToInt(Screen.height / 1080f));
            if (!Mathf.Approximately(scaler.scaleFactor, want)) scaler.scaleFactor = want;
        }

        Gather();
        Group();
        Draw();
        UpdateHover();
    }

    void Gather()
    {
        foreach (var r in raws) rawPool.Add(r);
        raws.Clear();
        foreach (var kv in docked) kv.Value.Clear();

        float margin = 40f;
        foreach (var u in UnitManager.Instance.Units)
        {
            if (u == null || u.IsDestroyed) continue;
            var t = UnitVisuals.TransformOf(u);
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            if (!VisibilityService.ShownToPlayer(u)) continue;

            Vector3 sp = cam.WorldToScreenPoint(t.position);
            if (sp.z <= 0f) continue;
            if (sp.x < -margin || sp.y < -margin || sp.x > Screen.width + margin || sp.y > Screen.height + margin) continue;

            float size = HullSize(u);
            Vector3 top = cam.WorldToScreenPoint(t.position + cam.transform.up * size);
            float hullPx = Mathf.Abs(top.y - sp.y) + Mathf.Abs(top.x - sp.x);

            var raw = rawPool.Count > 0 ? rawPool[rawPool.Count - 1] : new Raw();
            if (rawPool.Count > 0) rawPool.RemoveAt(rawPool.Count - 1);
            raw.u = u;
            raw.pos = new Vector2(sp.x, sp.y);
            raw.hullPx = hullPx;
            raw.alpha = 1f - Mathf.Clamp01((hullPx / Scale - FadeFromPx) / (FadeToPx - FadeFromPx));
            raw.used = false;
            raws.Add(raw);

            if (u.location != null && raw.alpha > 0.05f)
            {
                if (!docked.TryGetValue(u.location, out var list)) docked[u.location] = list = new List<Raw>();
                list.Add(raw);
            }
        }
    }

    /// How big this ship's hull is drawn, in world units: the class's normalised mesh size, or a token's.
    float HullSize(Unit u)
    {
        bool meshed = UnitModelRenderer.Instance != null && UnitModelRenderer.Instance.TransformOf(u) != null;
        if (!meshed) return 0.8f;   // UnitTokenRenderer's billboard scale
        if (hullSize.TryGetValue(u.type, out float s)) return s;
        var e = UnitModelLibrary.For(u.type);
        s = e != null && e.size > 0f ? e.size : 0.3f;
        hullSize[u.type] = s;
        return s;
    }

    readonly List<Raw> scratchGroup = new List<Raw>();
    readonly List<Faction> scratchOwners = new List<Faction>();

    void Group()
    {
        foreach (var it in items) { it.members = null; itemPool.Add(it); }
        items.Clear();

        float collapse = CollapsePx * Scale, cluster = ClusterPx * Scale;

        // ---- docked at a world: one badge per owner beside it, when they would overlap ----
        foreach (var kv in docked)
        {
            var body = kv.Key; var list = kv.Value;
            if (list.Count < 2) continue;

            scratchOwners.Clear();
            foreach (var r in list) if (!scratchOwners.Contains(r.u.owner)) scratchOwners.Add(r.u.owner);
            scratchOwners.Sort((a, b) => Rank(a).CompareTo(Rank(b)));

            int slot = 0;
            foreach (var owner in scratchOwners)
            {
                scratchGroup.Clear();
                foreach (var r in list) if (r.u.owner == owner) scratchGroup.Add(r);
                if (scratchGroup.Count < 2) continue;

                Vector2 c = Vector2.zero;
                foreach (var r in scratchGroup) c += r.pos;
                c /= scratchGroup.Count;
                float spread = 0f;
                foreach (var r in scratchGroup) spread = Mathf.Max(spread, (r.pos - c).magnitude * 2f);
                if (spread >= collapse) continue;

                var it = NewItem();
                it.members = new List<Unit>(scratchGroup.Count);
                foreach (var r in scratchGroup) { r.used = true; it.members.Add(r.u); }
                it.lead = Lead(it.members);
                it.at = body;
                it.pos = DockAnchor(body, c, slot++);
                it.alpha = 1f;
                it.boxHalf = GlyphUnits * 0.5f + 3f;
                it.selected = AnySelected(it.members);
                items.Add(it);
            }
        }

        // ---- in open space: overlapping ships of one owner collapse where they are ----
        for (int i = 0; i < raws.Count; i++)
        {
            var a = raws[i];
            if (a.used || a.alpha <= 0.05f || a.u.location != null) continue;
            scratchGroup.Clear();
            scratchGroup.Add(a);
            for (int j = i + 1; j < raws.Count; j++)
            {
                var b = raws[j];
                if (b.used || b.alpha <= 0.05f || b.u.location != null || b.u.owner != a.u.owner) continue;
                if ((b.pos - a.pos).sqrMagnitude <= cluster * cluster) scratchGroup.Add(b);
            }
            if (scratchGroup.Count < 2) continue;

            var it = NewItem();
            it.members = new List<Unit>(scratchGroup.Count);
            Vector2 c = Vector2.zero;
            float alpha = 0f;
            foreach (var r in scratchGroup) { r.used = true; it.members.Add(r.u); c += r.pos; alpha = Mathf.Max(alpha, r.alpha); }
            it.lead = Lead(it.members);
            it.pos = c / scratchGroup.Count;
            it.alpha = alpha;
            it.boxHalf = GlyphUnits * 0.5f + 3f;
            it.selected = AnySelected(it.members);
            items.Add(it);
        }

        // ---- everything else is a single marker (or, for a big selected hull, just its brackets) ----
        foreach (var r in raws)
        {
            if (r.used) continue;
            bool sel = UnitSelection.IsSelected(r.u);
            if (r.alpha <= 0.05f && !sel) continue;
            var it = NewItem();
            it.lead = r.u;
            it.pos = r.pos;
            it.alpha = r.alpha;
            it.selected = sel;
            float hullHalf = r.hullPx / Scale * 0.6f + 4f;
            it.boxHalf = Mathf.Clamp(Mathf.Max(GlyphUnits * 0.5f + 3f, hullHalf), 12f, 140f);
            items.Add(it);
        }

        // ---- keep stacks from sitting on each other (a planet and its moon, two owners at one world) ----
        float minGap = (GlyphUnits + 4f) * Scale;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].members == null || items[i].at == null) continue;
            for (int guard = 0; guard < 6; guard++)
            {
                bool moved = false;
                for (int j = 0; j < i; j++)
                {
                    if (items[j].members == null) continue;
                    if ((items[j].pos - items[i].pos).sqrMagnitude < minGap * minGap)
                    { items[i].pos.y -= minGap; moved = true; }
                }
                if (!moved) break;
            }
        }
    }

    Item NewItem()
    {
        Item it;
        if (itemPool.Count > 0) { it = itemPool[itemPool.Count - 1]; itemPool.RemoveAt(itemPool.Count - 1); }
        else it = new Item();
        it.members = null; it.at = null; it.selected = false;
        return it;
    }

    static int Rank(Faction f) => f == FactionManager.Player ? -1 : (f != null ? f.id : 999);

    /// A stack's lead: its biggest hull, so the glyph shows the most significant ship in it.
    static Unit Lead(List<Unit> members)
    {
        Unit best = members[0];
        foreach (var u in members) if (u.Info.buildPower > best.Info.buildPower) best = u;
        return best;
    }

    static bool AnySelected(List<Unit> members)
    {
        foreach (var u in members) if (UnitSelection.IsSelected(u)) return true;
        return false;
    }

    /// Just to the right of a world's disc, so the badge never covers the world it is reporting on.
    Vector2 DockAnchor(CelestialBody b, Vector2 fallback, int slot)
    {
        if (b == null || b.visualObject == null) return fallback;
        var t = b.visualObject.transform;
        Vector3 c = cam.WorldToScreenPoint(t.position);
        if (c.z <= 0f) return fallback;
        Vector3 edge = cam.WorldToScreenPoint(t.position + cam.transform.right * Mathf.Max(0.05f, t.lossyScale.x * 0.5f));
        float r = Mathf.Max(4f * Scale, Mathf.Abs(edge.x - c.x));
        float off = r + (GlyphUnits * 0.5f + 6f) * Scale;
        return new Vector2(c.x + off, c.y - slot * (GlyphUnits + 4f) * Scale);
    }

    // ---- drawing ---------------------------------------------------------------------------------

    void Draw()
    {
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
        int n = 0;
        foreach (var it in items)
        {
            var m = n < pool.Count ? pool[n] : NewMarker();
            n++;
            if (!m.rt.gameObject.activeSelf) m.rt.gameObject.SetActive(true);
            m.rt.position = new Vector3(Mathf.Round(it.pos.x), Mathf.Round(it.pos.y), 0f);

            bool showGlyph = it.alpha > 0.01f;
            if (m.glyph.gameObject.activeSelf != showGlyph) m.glyph.gameObject.SetActive(showGlyph);
            if (showGlyph)
            {
                m.glyph.texture = ShipGlyphs.For(it.lead);
                var col = FactionManager.OwnerColor(it.lead.owner);
                col.a = it.alpha;
                m.glyph.color = col;
            }

            bool stack = it.members != null && it.members.Count > 1 && showGlyph;
            if (m.badge.gameObject.activeSelf != stack) m.badge.gameObject.SetActive(stack);
            if (stack)
            {
                string c = it.members.Count.ToString();
                if (m.count.text != c)
                {
                    m.count.text = c;
                    m.badge.sizeDelta = new Vector2(Mathf.Max(11f, m.count.GetPreferredValues(c).x + 5f), 11f);
                }
            }

            bool hover = it == hovered;
            bool bracket = it.selected || hover;
            Color bc = it.selected ? new Color(BracketSelected.r, BracketSelected.g, BracketSelected.b, pulse)
                                   : BracketHover;
            PlaceBracket(m, bracket, it.boxHalf, bc);
        }
        for (int i = n; i < pool.Count; i++)
            if (pool[i].rt.gameObject.activeSelf) pool[i].rt.gameObject.SetActive(false);
    }

    static void PlaceBracket(Marker m, bool on, float h, Color c)
    {
        const float len = 5f, th = 2f;
        h = Mathf.Round(h);
        for (int k = 0; k < 4; k++)
        {
            float sx = (k & 1) == 0 ? -1f : 1f, sy = (k & 2) == 0 ? -1f : 1f;
            var hz = m.bars[k * 2]; var vt = m.bars[k * 2 + 1];
            if (hz.gameObject.activeSelf != on) { hz.gameObject.SetActive(on); vt.gameObject.SetActive(on); }
            if (!on) continue;
            hz.color = c; vt.color = c;
            hz.rectTransform.sizeDelta = new Vector2(len, th);
            hz.rectTransform.anchoredPosition = new Vector2(sx * (h - len * 0.5f), sy * (h - th * 0.5f));
            vt.rectTransform.sizeDelta = new Vector2(th, len);
            vt.rectTransform.anchoredPosition = new Vector2(sx * (h - th * 0.5f), sy * (h - len * 0.5f));
        }
    }

    Marker NewMarker()
    {
        var m = new Marker();
        var go = UIFactory.NewUI(canvas.transform, "ShipMarker");
        m.rt = go.GetComponent<RectTransform>();
        m.rt.anchorMin = m.rt.anchorMax = Vector2.zero;
        m.rt.pivot = new Vector2(0.5f, 0.5f);
        m.rt.sizeDelta = Vector2.zero;

        var g = UIFactory.NewUI(go.transform, "Glyph");
        m.glyph = g.AddComponent<RawImage>();
        m.glyph.raycastTarget = false;
        Center(m.glyph.rectTransform, new Vector2(GlyphUnits, GlyphUnits), Vector2.zero);

        for (int i = 0; i < m.bars.Length; i++)
        {
            var bar = UIFactory.Panel(go.transform, "Bracket", Color.white);
            bar.raycastTarget = false;
            Center(bar.rectTransform, new Vector2(2, 2), Vector2.zero);
            bar.gameObject.SetActive(false);
            m.bars[i] = bar;
        }

        // The stack count: a small dark tab at the glyph's lower right, white figures.
        var badge = UIFactory.Panel(go.transform, "Count", PlateFill);
        badge.raycastTarget = false;
        m.badge = badge.rectTransform;
        m.badge.anchorMin = m.badge.anchorMax = new Vector2(0.5f, 0.5f);
        m.badge.pivot = new Vector2(0f, 1f);
        m.badge.anchoredPosition = new Vector2(GlyphUnits * 0.5f - 3f, -GlyphUnits * 0.5f + 5f);
        m.badge.sizeDelta = new Vector2(11, 11);
        m.count = UIFactory.Text(badge.transform, "", 10, Color.white, TextAlignmentOptions.Center);
        m.count.raycastTarget = false;
        m.count.fontStyle = FontStyles.Bold;
        m.count.overflowMode = TextOverflowModes.Overflow;
        m.count.textWrappingMode = TextWrappingModes.NoWrap;
        UIFactory.Stretch(m.count.rectTransform);
        badge.gameObject.SetActive(false);

        pool.Add(m);
        return m;
    }

    static void Center(RectTransform rt, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    // ---- hover label -----------------------------------------------------------------------------

    void BuildLabel()
    {
        var plate = UIFactory.Panel(canvas.transform, "ShipHoverLabel", PlateFill);
        plate.raycastTarget = false;
        labelPlate = plate.rectTransform;
        labelPlate.anchorMin = labelPlate.anchorMax = Vector2.zero;
        labelPlate.pivot = new Vector2(0f, 0.5f);
        labelText = UIFactory.Text(plate.transform, "", 12, UITheme.Text, TextAlignmentOptions.Left);
        labelText.raycastTarget = false;
        labelText.textWrappingMode = TextWrappingModes.NoWrap;
        labelText.overflowMode = TextOverflowModes.Overflow;
        UIFactory.Stretch(labelText.rectTransform, 6, 6, 2, 2);
        labelPlate.gameObject.SetActive(false);
    }

    void UpdateHover()
    {
        hovered = null;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (!overUI && Pick(Input.mousePosition, out var hit))
            foreach (var it in items)
                if (it.lead == hit.unit && it.members == hit.stack) { hovered = it; break; }

        if (hovered == null)
        {
            if (labelPlate.gameObject.activeSelf) labelPlate.gameObject.SetActive(false);
            return;
        }

        string text = LabelFor(hovered);
        if (labelText.text != text)
        {
            labelText.text = text;
            Vector2 pref = labelText.GetPreferredValues(text);
            labelPlate.sizeDelta = new Vector2(pref.x + 12f, pref.y + 4f);
        }
        if (!labelPlate.gameObject.activeSelf) labelPlate.gameObject.SetActive(true);
        labelPlate.SetAsLastSibling();
        float x = hovered.pos.x + (hovered.boxHalf + 6f) * Scale;
        labelPlate.position = new Vector3(Mathf.Round(x), Mathf.Round(hovered.pos.y), 0f);
    }

    static string LabelFor(Item it)
    {
        var u = it.lead;
        string hex = ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(u.owner));
        bool mine = u.owner == FactionManager.Player;
        if (it.members != null && it.members.Count > 1)
        {
            string where = it.at != null ? $" at {it.at.name}" : "";
            string whose = mine ? "" : $"  <color=#{hex}>{FactionManager.OwnerName(u.owner)}</color>";
            return $"<b>{it.members.Count} ships</b>{where}{whose}\n<size=10><color=#9FB4C8>" +
                   (mine ? "Click to select them · zoom in to split" : "Zoom in to split the stack") + "</color></size>";
        }
        string owner = mine ? "" : $" · <color=#{hex}>{FactionManager.OwnerName(u.owner)}</color>";
        return $"<b>{u.name}</b>\n<size=10><color=#9FB4C8>{u.Info.name} · {ShipRoster.StatusWord(u)}</color>{owner}</size>";
    }
}

// ============================================================================================
// THE CLASS GLYPHS — 7x7 pixel art with a generated 1-texel dark outline, in a 9x9 texture
//
// Drawn white and tinted by the RawImage, so one texture serves every owner: the white takes the
// owner's colour and the outline stays dark against any background. Point-filtered and built in code,
// like PixelIcons, so nothing needs import settings from a machine with Unity.
//
// One glyph per ROLE rather than per hull — twenty-nine classes would be twenty-nine shapes nobody can
// learn, and the question at system zoom is "what kind of ship is that", not "which mark is it".
// ============================================================================================
public static class ShipGlyphs
{
    enum Role { Warship, Scout, Science, Colony, Terraformer, Worker, Station, Probe }

    static readonly Dictionary<Role, Texture2D> cache = new Dictionary<Role, Texture2D>();

    public static Texture2D For(Unit u) => Get(RoleOf(u != null ? u.Info : null));

    public static Texture2D For(UnitType t) => Get(RoleOf(UnitDatabase.Get(t)));

    static Role RoleOf(UnitInfo i)
    {
        if (i == null) return Role.Scout;
        if (i.isStation) return Role.Station;
        if (i.isProbe) return Role.Probe;
        if (i.canColonize) return Role.Colony;
        if (i.canTerraform) return Role.Terraformer;
        if (i.isWorker) return Role.Worker;
        if (i.canResearch) return Role.Science;
        if (i.attack > 0) return Role.Warship;
        return Role.Scout;
    }

    static Texture2D Get(Role r)
    {
        if (cache.TryGetValue(r, out var t) && t != null) return t;
        t = Build(r.ToString(), Rows(r));
        cache[r] = t;
        return t;
    }

    static string[] Rows(Role r)
    {
        switch (r)
        {
            case Role.Warship: return new[]
            {
                "...#...",
                "..###..",
                "..###..",
                ".#####.",
                ".#####.",
                "##.#.##",
                "#.....#",
            };
            case Role.Science: return new[]
            {
                "...#...",
                "..###..",
                ".##.##.",
                "##...##",
                ".##.##.",
                "..###..",
                "...#...",
            };
            case Role.Colony: return new[]
            {
                "..###..",
                ".#####.",
                "#######",
                "###.###",
                "#######",
                ".#####.",
                "..###..",
            };
            case Role.Terraformer: return new[]
            {
                "..###..",
                ".#.#.#.",
                "#..#..#",
                "#######",
                "#..#..#",
                ".#.#.#.",
                "..###..",
            };
            case Role.Worker: return new[]
            {
                ".......",
                "#######",
                "#.....#",
                "#######",
                "#.....#",
                "#######",
                ".......",
            };
            case Role.Station: return new[]
            {
                "#######",
                "#.....#",
                "#.###.#",
                "#.###.#",
                "#.###.#",
                "#.....#",
                "#######",
            };
            case Role.Probe: return new[]
            {
                ".......",
                "...#...",
                "...#...",
                ".#####.",
                "...#...",
                "...#...",
                ".......",
            };
            default: return new[]   // Scout: a slim dart
            {
                "...#...",
                "...#...",
                "..###..",
                "..#.#..",
                ".##.##.",
                ".#...#.",
                ".......",
            };
        }
    }

    static Texture2D Build(string key, string[] rows)
    {
        const int N = 9;
        var body = new bool[N, N];
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
                body[x + 1, y + 1] = y < rows.Length && x < rows[y].Length && rows[y][x] == '#';

        var px = new Color32[N * N];
        var outline = new Color32(8, 12, 18, 235);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color32 c = new Color32(0, 0, 0, 0);
                if (body[x, y]) c = new Color32(255, 255, 255, 255);
                else
                {
                    for (int dy = -1; dy <= 1 && c.a == 0; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                            if (body[nx, ny]) { c = outline; break; }
                        }
                }
                // Texture rows run bottom-up; the grids are written top-down.
                px[(N - 1 - y) * N + x] = c;
            }

        var t = new Texture2D(N, N, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "ShipGlyph_" + key
        };
        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }
}
