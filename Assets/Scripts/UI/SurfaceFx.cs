using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================================
// THE LIVING SURFACE — building animations, areas of effect, and power flow (2026-10-10)
//
// One component on the Planet View's map, drawing on its own layers above the buildings. Three jobs:
//
//   ANIMATIONS    Small effects that say what each building is DOING: smoke from combustion stacks
//                 and factories, steam from turbines, reactors and geothermal vents, wind-turbine blades
//                 turning, sparks at the mine head and on the shipyard slips, a little machine working
//                 the farm rows, panels glinting, water running past the hydro dam, windows twinkling
//                 in the habitats and the city, and blinking beacons on masts, labs, the spaceport and
//                 the capitol. A building that wants power and is not getting it stands STILL and shows
//                 a slow red warning blink instead — the animation is a status readout, not decoration.
//
//   AREA OF EFFECT  Hover a utility, select it, or hold one while placing it, and its reach is drawn
//                 on the ground: a power plant's or pylon's lit area (PowerGrid.CoverageOf — the real
//                 rule, not a picture of it), and a Power Distribution hub's boost ring (the generators
//                 touching it run better; SurfaceBuildManager.AdjacencyBonus).
//
//   POWER FLOW    Click anything standing on a grid and the grid shows itself working: its
//                 transmission spans drawn between plants and pylons, a feed line to every building it
//                 powers, and bright pulses running out from the generators along every one of them —
//                 quick and gold on a healthy grid, slow and amber when it is short, red when it has
//                 failed (PowerGrid.SupplyColor).
//
// Everything is drawn in the map's own normalised space, so it pans and zooms with the terrain; below a
// few pixels per tile the animations step aside, because at that zoom they would only be noise.
// ============================================================================================
public class SurfaceFx : MonoBehaviour
{
    // ---- Wiring from the Planet View -----------------------------------------------------------
    public System.Func<CelestialBody> Body;
    public System.Func<bool> Visible;                       // window open and the host map showing
    public System.Func<Vector2Int?> Hover;                  // the host-map cell under the cursor
    public System.Func<SurfaceBuildingType?> Held;          // a structure in hand, if any
    public System.Func<int> HeldRotation;

    RectTransform root, content, aoeLayer, flowLayer, animLayer;   // `content` is what hides — never root,
                                                                    // or LateUpdate would stop with it

    public static SurfaceFx Create(RectTransform mapRT)
    {
        var go = UIFactory.NewUI(mapRT, "SurfaceFx");
        var fx = go.AddComponent<SurfaceFx>();
        fx.root = go.GetComponent<RectTransform>();
        UIFactory.Stretch(fx.root);
        fx.content = UIFactory.NewUI(fx.root, "Content").GetComponent<RectTransform>();
        UIFactory.Stretch(fx.content);
        fx.aoeLayer = fx.Layer("AreaOfEffect");
        fx.flowLayer = fx.Layer("PowerFlow");
        fx.animLayer = fx.Layer("Animations");
        return fx;
    }

    RectTransform Layer(string name)
    {
        var rt = UIFactory.NewUI(content, name).GetComponent<RectTransform>();
        UIFactory.Stretch(rt);
        return rt;
    }

    // ---- Geometry --------------------------------------------------------------------------------

    int W => cur != null && cur.surface != null ? cur.surface.width : 1;
    int H => cur != null && cur.surface != null ? cur.surface.height : 1;
    float TilePx => root.rect.width / Mathf.Max(1, W);

    /// Grid coordinates (cell units, cell centre = x + 0.5) -> local pixels in the layer.
    Vector2 Local(float gx, float gy)
    {
        var r = root.rect;
        return new Vector2((gx / W - 0.5f) * r.width, (gy / H - 0.5f) * r.height);
    }

    static Vector2 Centre(List<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0) return Vector2.zero;
        float sx = 0f, sy = 0f;
        foreach (var c in cells) { sx += c.x + 0.5f; sy += c.y + 0.5f; }
        return new Vector2(sx / cells.Count, sy / cells.Count);
    }

    static void Bounds(List<Vector2Int> cells, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue); max = new Vector2(float.MinValue, float.MinValue);
        foreach (var c in cells)
        {
            min = Vector2.Min(min, new Vector2(c.x, c.y));
            max = Vector2.Max(max, new Vector2(c.x + 1, c.y + 1));
        }
    }

    // ---- Tiny procedural sprites -----------------------------------------------------------------

    static Texture2D softDot, bladeTex, squareTex;

    static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int n = 32;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                byte a = (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255f);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        softDot.SetPixels32(px); softDot.Apply(false, true);
        return softDot;
    }

    static Texture2D Blades()
    {
        if (bladeTex != null) return bladeTex;
        const int n = 32;
        bladeTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
        var px = new Color32[n * n];
        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.PI * 2f / 3f + Mathf.PI / 2f;
            for (float t = 0; t < 14f; t += 0.25f)
            {
                int x = Mathf.RoundToInt(16 + Mathf.Cos(a) * t), y = Mathf.RoundToInt(16 + Mathf.Sin(a) * t);
                for (int w = -1; w <= 0; w++)
                {
                    int xx = Mathf.Clamp(x + w, 0, n - 1), yy = Mathf.Clamp(y, 0, n - 1);
                    px[yy * n + xx] = new Color32(245, 248, 252, 255);
                }
            }
        }
        for (int y = 14; y < 18; y++) for (int x = 14; x < 18; x++) px[y * n + x] = new Color32(200, 205, 212, 255);
        bladeTex.SetPixels32(px); bladeTex.Apply(false, true);
        return bladeTex;
    }

    static Texture2D Square()
    {
        if (squareTex != null) return squareTex;
        squareTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        squareTex.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
        squareTex.Apply(false, true);
        return squareTex;
    }

    RawImage Sprite(RectTransform parent, Texture2D tex, Color c)
    {
        var img = UIFactory.NewUI(parent, "fx").AddComponent<RawImage>();
        img.texture = tex; img.color = c; img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        return img;
    }

    // ==============================================================================================
    // ANIMATIONS
    // ==============================================================================================

    enum Kind { Puff, Spin, Blink, Spark, Mover, Glint, Flow, Twinkle, Warning }

    class Emitter
    {
        public Kind kind;
        public PlacedBuilding p;
        public Vector2 at;           // grid units
        public Vector2 min, max;     // footprint bounds, grid units
        public Color color;
        public float period, phase, size, timer;
        public RawImage img;         // for persistent effects (spin, blink, mover, glint, warning)
    }

    class Particle
    {
        public RawImage img;
        public Vector2 at, vel;      // grid units, grid units / s
        public float age, life, size0, size1;
        public Color color;
    }

    readonly List<Emitter> emitters = new List<Emitter>();
    readonly List<Particle> particles = new List<Particle>();
    CelestialBody cur;
    string animSig;
    float nextSigCheck;
    const int MaxParticles = 260;
    const float MinAnimTilePx = 7f;

    static readonly Color Smoke = new Color(0.42f, 0.42f, 0.44f, 0.75f);
    static readonly Color Steam = new Color(0.95f, 0.97f, 1f, 0.7f);
    static readonly Color Spark = new Color(1f, 0.8f, 0.35f, 1f);
    static readonly Color Beacon = new Color(1f, 0.25f, 0.2f, 1f);
    static readonly Color Lamp = new Color(1f, 0.9f, 0.55f, 1f);

    string BuildSignature(CelestialBody b)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(b != null ? b.GetHashCode() : 0).Append('|');
        if (b != null)
            foreach (var p in SurfaceBuildManager.On(b)) sb.Append(p.type).Append(',').Append(p.x).Append(',').Append(p.y).Append(',').Append(p.rotation).Append(';');
        return sb.ToString();
    }

    void RebuildEmitters(CelestialBody b)
    {
        foreach (var e in emitters) if (e.img != null) Destroy(e.img.gameObject);
        emitters.Clear();
        foreach (var pt in particles) if (pt.img != null) Destroy(pt.img.gameObject);
        particles.Clear();
        if (b?.surface == null) return;

        foreach (var p in SurfaceBuildManager.On(b))
        {
            var cells = SurfaceBuildingDatabase.Footprint(p);
            if (cells == null || cells.Count == 0) continue;
            Bounds(cells, out var mn, out var mx);
            var top = new Vector2(mx.x - 0.35f, mx.y - 0.25f);            // a stack / mast near the top right
            float ph = (p.x * 0.37f + p.y * 0.71f) % 1f;                     // so a row of masts doesn't blink in unison

            switch (p.Type)
            {
                case SurfaceBuildingType.CombustionPlant:
                    Add(Kind.Puff, p, top, mn, mx, Smoke, 0.55f, ph, 0.7f);
                    break;
                case SurfaceBuildingType.Factory:
                case SurfaceBuildingType.Refinery:
                    Add(Kind.Puff, p, top, mn, mx, Smoke, 0.8f, ph, 0.6f);
                    Add(Kind.Spark, p, Centre(cells), mn, mx, Spark, 0.5f, ph, 0.18f);
                    break;
                case SurfaceBuildingType.SteamTurbine:
                case SurfaceBuildingType.GeothermalPlant:
                    Add(Kind.Puff, p, top, mn, mx, Steam, 0.45f, ph, 0.8f);
                    break;
                case SurfaceBuildingType.FissionReactor:
                    Add(Kind.Puff, p, new Vector2((mn.x + mx.x) / 2f, mx.y - 0.2f), mn, mx, Steam, 0.35f, ph, 1f);
                    Add(Kind.Blink, p, top, mn, mx, Beacon, 1.6f, ph, 0.16f);
                    break;
                case SurfaceBuildingType.FusionReactor:
                    Add(Kind.Twinkle, p, Centre(cells), mn, mx, new Color(0.5f, 0.9f, 1f, 1f), 0.25f, ph, 0.22f);
                    break;
                case SurfaceBuildingType.WindFarm:
                    foreach (var c in cells) Add(Kind.Spin, p, new Vector2(c.x + 0.5f, c.y + 0.62f), mn, mx, Color.white, 1f, ph, 0.85f);
                    break;
                case SurfaceBuildingType.Mine:
                    foreach (var c in cells) Add(Kind.Spark, p, new Vector2(c.x + 0.5f, c.y + 0.25f), mn, mx, Spark, 0.7f, ph + c.x * 0.13f, 0.14f);
                    break;
                case SurfaceBuildingType.Farm:
                    Add(Kind.Mover, p, Centre(cells), mn, mx, new Color(0.95f, 0.75f, 0.2f, 1f), 7f, ph, 0.22f);
                    break;
                case SurfaceBuildingType.SolarArray:
                    Add(Kind.Glint, p, Centre(cells), mn, mx, new Color(1f, 1f, 1f, 0.85f), 4.5f, ph, 0.5f);
                    break;
                case SurfaceBuildingType.HydroPlant:
                    Add(Kind.Flow, p, Centre(cells), mn, mx, new Color(0.7f, 0.9f, 1f, 0.9f), 0.25f, ph, 0.14f);
                    break;
                case SurfaceBuildingType.Habitat:
                case SurfaceBuildingType.Settlement:
                case SurfaceBuildingType.Town:
                case SurfaceBuildingType.City:
                    Add(Kind.Twinkle, p, Centre(cells), mn, mx, Lamp, 0.35f, ph, 0.12f);
                    break;
                case SurfaceBuildingType.PowerNode:
                    Add(Kind.Blink, p, new Vector2(mn.x + 0.5f, mx.y - 0.2f), mn, mx, Beacon, 1.2f, ph, 0.18f);
                    break;
                case SurfaceBuildingType.ResearchOutpost:
                case SurfaceBuildingType.ResearchCenter:
                    Add(Kind.Blink, p, top, mn, mx, new Color(0.4f, 0.9f, 1f, 1f), 1.4f, ph, 0.16f);
                    break;
                case SurfaceBuildingType.Spaceport:
                    Add(Kind.Blink, p, new Vector2(mn.x + 0.3f, mn.y + 0.3f), mn, mx, Lamp, 0.9f, ph, 0.14f);
                    Add(Kind.Blink, p, new Vector2(mx.x - 0.3f, mn.y + 0.3f), mn, mx, Lamp, 0.9f, ph + 0.5f, 0.14f);
                    Add(Kind.Blink, p, top, mn, mx, Beacon, 1.5f, ph, 0.16f);
                    break;
                case SurfaceBuildingType.SurfaceShipyard:
                    Add(Kind.Spark, p, Centre(cells), mn, mx, new Color(0.7f, 0.9f, 1f, 1f), 0.25f, ph, 0.16f);
                    Add(Kind.Blink, p, top, mn, mx, Beacon, 1.3f, ph, 0.16f);
                    break;
                case SurfaceBuildingType.PlanetCapitol:
                case SurfaceBuildingType.ColonyShipBase:
                    Add(Kind.Blink, p, top, mn, mx, new Color(0.45f, 1f, 0.6f, 1f), 1.8f, ph, 0.2f);
                    Add(Kind.Twinkle, p, Centre(cells), mn, mx, Lamp, 0.5f, ph, 0.1f);
                    break;
                case SurfaceBuildingType.Capacitor:
                    Add(Kind.Blink, p, Centre(cells), mn, mx, new Color(0.4f, 0.85f, 1f, 1f), 2.4f, ph, 0.2f);
                    break;
            }

            // A building that WANTS power and isn't getting it gets a slow red warning instead of its
            // working animation (ApplyWorking below hides the rest).
            if (p.Info.powerDraw > 0f)
                Add(Kind.Warning, p, new Vector2(mn.x + 0.3f, mx.y - 0.3f), mn, mx, new Color(1f, 0.2f, 0.15f, 1f), 1.1f, 0f, 0.3f);
        }
    }

    void Add(Kind k, PlacedBuilding p, Vector2 at, Vector2 mn, Vector2 mx, Color c, float period, float phase, float size)
    {
        var e = new Emitter { kind = k, p = p, at = at, min = mn, max = mx, color = c, period = period, phase = phase, size = size, timer = phase * period };
        switch (k)
        {
            case Kind.Spin: e.img = Sprite(animLayer, Blades(), c); break;
            case Kind.Blink:
            case Kind.Warning: e.img = Sprite(animLayer, SoftDot(), c); break;
            case Kind.Mover: e.img = Sprite(animLayer, Square(), c); break;
            case Kind.Glint: e.img = Sprite(animLayer, SoftDot(), c); break;
        }
        emitters.Add(e);
    }

    bool Working(PlacedBuilding p)
        => p.Info.powerDraw <= 0f || PowerGrid.PowerFactor(cur, p) >= 0.999f;

    void TickAnimations(float dt, float t)
    {
        bool show = TilePx >= MinAnimTilePx;
        animLayer.gameObject.SetActive(show);
        if (!show) return;
        float px = TilePx;

        foreach (var e in emitters)
        {
            bool working = Working(e.p);
            if (e.kind == Kind.Warning)
            {
                bool on = !working && Mathf.Repeat(t + e.phase, e.period) < e.period * 0.5f;
                Place(e.img, e.at, e.size * px, e.color, on ? 1f : 0f);
                continue;
            }
            if (!working) { if (e.img != null) e.img.enabled = false; continue; }

            switch (e.kind)
            {
                case Kind.Puff:
                    e.timer -= dt;
                    if (e.timer <= 0f)
                    {
                        e.timer = e.period * Random.Range(0.8f, 1.2f);
                        Emit(e.at + new Vector2(Random.Range(-0.08f, 0.08f), 0f), new Vector2(Random.Range(0.05f, 0.18f), Random.Range(0.45f, 0.65f)),
                             2.2f, e.size * 0.35f, e.size, e.color);
                    }
                    break;
                case Kind.Spark:
                    e.timer -= dt;
                    if (e.timer <= 0f)
                    {
                        e.timer = e.period * Random.Range(0.5f, 1.5f);
                        var at = e.at + new Vector2(Random.Range(-0.25f, 0.25f), Random.Range(-0.15f, 0.15f));
                        for (int i = 0; i < 3; i++)
                            Emit(at, new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.2f, 0.9f)), 0.35f, e.size, e.size * 0.4f, e.color);
                    }
                    break;
                case Kind.Twinkle:
                    e.timer -= dt;
                    if (e.timer <= 0f)
                    {
                        e.timer = e.period * Random.Range(0.5f, 1.6f);
                        var at = new Vector2(Random.Range(e.min.x + 0.15f, e.max.x - 0.15f), Random.Range(e.min.y + 0.15f, e.max.y - 0.15f));
                        Emit(at, Vector2.zero, 0.9f, e.size, e.size, e.color);
                    }
                    break;
                case Kind.Flow:
                    e.timer -= dt;
                    if (e.timer <= 0f)
                    {
                        e.timer = e.period * Random.Range(0.6f, 1.4f);
                        var at = new Vector2(e.min.x + 0.1f, Random.Range(e.min.y + 0.2f, e.max.y - 0.2f));
                        Emit(at, new Vector2(Mathf.Max(0.6f, (e.max.x - e.min.x) / 1.6f), 0f), 1.6f, e.size, e.size, e.color);
                    }
                    break;
                case Kind.Spin:
                    Place(e.img, e.at, e.size * px, e.color, 1f);
                    e.img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -(t * 160f + e.phase * 360f));
                    break;
                case Kind.Blink:
                {
                    float k = Mathf.Repeat(t / e.period + e.phase, 1f);
                    float a = k < 0.18f ? 1f : Mathf.Max(0f, 0.25f - (k - 0.18f));
                    Place(e.img, e.at, e.size * px * (0.8f + 0.4f * a), e.color, a);
                    break;
                }
                case Kind.Mover:
                {
                    // Back and forth across the field, stepping up a row each pass — a harvester working rows.
                    float span = Mathf.Max(0.01f, e.max.x - e.min.x - 0.3f);
                    float rows = Mathf.Max(1f, e.max.y - e.min.y);
                    float pass = (t / e.period + e.phase) * 2f;
                    int lane = Mathf.FloorToInt(pass) % Mathf.Max(1, Mathf.RoundToInt(rows));
                    float u = Mathf.PingPong(pass, 1f);
                    var at = new Vector2(e.min.x + 0.15f + u * span, e.min.y + 0.5f + lane);
                    Place(e.img, at, e.size * px, e.color, 1f);
                    break;
                }
                case Kind.Glint:
                {
                    float k = Mathf.Repeat(t / e.period + e.phase, 1f);
                    bool on = k < 0.18f;
                    var at = Vector2.Lerp(e.min + new Vector2(0.2f, 0.2f), e.max - new Vector2(0.2f, 0.2f), k / 0.18f);
                    Place(e.img, at, e.size * px, e.color, on ? Mathf.Sin(k / 0.18f * Mathf.PI) : 0f);
                    break;
                }
            }
        }

        // Particles: drift, grow, fade.
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var pt = particles[i];
            pt.age += dt;
            if (pt.age >= pt.life || pt.img == null)
            {
                if (pt.img != null) Destroy(pt.img.gameObject);
                particles.RemoveAt(i);
                continue;
            }
            float k = pt.age / pt.life;
            pt.at += pt.vel * dt;
            Place(pt.img, pt.at, Mathf.Lerp(pt.size0, pt.size1, k) * px, pt.color, pt.color.a * (1f - k));
        }
    }

    void Emit(Vector2 at, Vector2 vel, float life, float s0, float s1, Color c)
    {
        if (particles.Count >= MaxParticles) return;
        particles.Add(new Particle { img = Sprite(animLayer, SoftDot(), c), at = at, vel = vel, life = life, size0 = s0, size1 = s1, color = c });
    }

    void Place(RawImage img, Vector2 gridAt, float sizePx, Color c, float alpha)
    {
        if (img == null) return;
        img.enabled = alpha > 0.01f;
        if (!img.enabled) return;
        var rt = img.rectTransform;
        rt.anchoredPosition = Local(gridAt.x, gridAt.y);
        rt.sizeDelta = new Vector2(sizePx, sizePx);
        img.color = new Color(c.r, c.g, c.b, alpha);
    }

    // ==============================================================================================
    // AREA OF EFFECT
    // ==============================================================================================

    string aoeSig;
    readonly List<RawImage> aoeTiles = new List<RawImage>();
    readonly List<Vector2Int> aoeCells = new List<Vector2Int>();
    Color aoeColor;

    static readonly Color PowerReach = new Color(0.25f, 0.72f, 1f, 1f);     // the power overlay's electric blue
    static readonly Color HubReach = new Color(0.45f, 0.85f, 1f, 1f);

    /// The tiles this structure affects, and the colour to draw them in. False if it has no area.
    bool AreaOf(SurfaceBuildingType t, List<Vector2Int> footprint, PlacedBuilding placed, HashSet<Vector2Int> into, out Color col)
    {
        col = PowerReach;
        var info = SurfaceBuildingDatabase.Get(t);
        if (info == null || cur?.surface == null) return false;

        if (PowerGrid.Projects(info))
        {
            if (placed != null) { foreach (var c in PowerGrid.CoverageOf(cur, placed)) into.Add(c); }
            else
            {
                // Not built yet: the same disc CoverageOf would light, around every footprint cell.
                float r = info.powerRange;
                int ri = Mathf.CeilToInt(r);
                foreach (var f in footprint)
                    for (int dy = -ri; dy <= ri; dy++)
                        for (int dx = -ri; dx <= ri; dx++)
                        {
                            if (dx * dx + dy * dy > r * r) continue;
                            int x = ((f.x + dx) % W + W) % W, y = f.y + dy;
                            if (y >= 0 && y < H) into.Add(new Vector2Int(x, y));
                        }
            }
            return into.Count > 0;
        }

        if (info.adjacencyPowerBonus > 0f)
        {
            // A distribution hub boosts the generators that TOUCH it: its ring of edge-neighbours.
            col = HubReach;
            var mine = new HashSet<Vector2Int>(footprint);
            foreach (var f in footprint)
                foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    var n = new Vector2Int(((f.x + d.x) % W + W) % W, f.y + d.y);
                    if (n.y >= 0 && n.y < H && !mine.Contains(n)) into.Add(n);
                }
            return into.Count > 0;
        }
        return false;
    }

    void TickArea(float t)
    {
        // What to show: the structure in hand at the cursor, else the hovered building, else the selection.
        string sig = "none";
        SurfaceBuildingType? type = null;
        List<Vector2Int> footprint = null;
        PlacedBuilding placed = null;

        var held = Held != null ? Held() : null;
        var hover = Hover != null ? Hover() : null;
        if (held.HasValue && hover.HasValue)
        {
            type = held.Value;
            footprint = SurfaceBuildingDatabase.Footprint(held.Value, hover.Value.x, hover.Value.y, HeldRotation != null ? HeldRotation() : 0);
            sig = $"h{(int)held.Value}:{hover.Value.x},{hover.Value.y}";
        }
        else
        {
            PlacedBuilding target = null;
            if (hover.HasValue) target = SurfaceBuildManager.At(cur, hover.Value.x, hover.Value.y);
            if (target == null && SurfaceSelection.Body == cur) target = SurfaceSelection.Selected;
            if (target != null)
            {
                placed = target; type = target.Type;
                footprint = SurfaceBuildingDatabase.Footprint(target);
                sig = $"p{target.GetHashCode()}:{target.level}";
            }
        }

        if (sig != aoeSig)
        {
            aoeSig = sig;
            foreach (var q in aoeTiles) if (q != null) Destroy(q.gameObject);
            aoeTiles.Clear(); aoeCells.Clear();
            if (type.HasValue && footprint != null)
            {
                var set = new HashSet<Vector2Int>();
                if (AreaOf(type.Value, footprint, placed, set, out aoeColor))
                    foreach (var c in set)
                    {
                        aoeCells.Add(c);
                        aoeTiles.Add(Sprite(aoeLayer, Square(), aoeColor));
                    }
            }
        }

        if (aoeTiles.Count == 0) return;
        // A slow breathing pulse, so the area reads as an influence rather than a painted region.
        float a = 0.13f + 0.07f * Mathf.Sin(t * 3f);
        float px = TilePx;
        for (int i = 0; i < aoeTiles.Count; i++)
        {
            var q = aoeTiles[i]; if (q == null) continue;
            var c = aoeCells[i];
            q.rectTransform.anchoredPosition = Local(c.x + 0.5f, c.y + 0.5f);
            q.rectTransform.sizeDelta = new Vector2(px - 1f, px - 1f);
            q.color = new Color(aoeColor.r, aoeColor.g, aoeColor.b, a);
        }
    }

    // ==============================================================================================
    // POWER FLOW
    // ==============================================================================================

    struct Span { public Vector2 a, b; }
    readonly List<Span> spans = new List<Span>();
    readonly List<Image> spanLines = new List<Image>();
    readonly List<Particle> pulses = new List<Particle>();
    string flowSig;
    PowerNet flowNet;
    float pulseTimer;

    void TickFlow(float dt, float t)
    {
        PlacedBuilding sel = SurfaceSelection.Body == cur ? SurfaceSelection.Selected : null;
        PowerNet net = sel != null ? PowerGrid.NetOf(cur, sel) : null;
        string sig = net == null ? "none" : $"{net.index}:{net.projectors.Count}:{net.consumers.Count}:{net.capacitors.Count}:{SurfaceBuildManager.On(cur).Count}";

        if (sig != flowSig)
        {
            flowSig = sig;
            flowNet = net;
            BuildSpans(net);
        }
        if (flowNet == null) return;

        // Re-fetch: the net object is rebuilt every frame by PowerGrid's cache.
        var live = sel != null ? PowerGrid.NetOf(cur, sel) : null;
        Color col = live != null ? PowerGrid.SupplyColor(live) : PowerReach;
        float served = live != null ? live.SteadyServed : 0f;

        // The lines.
        for (int i = 0; i < spans.Count && i < spanLines.Count; i++)
        {
            var s = spans[i]; var img = spanLines[i];
            var a = Local(s.a.x, s.a.y); var b = Local(s.b.x, s.b.y);
            var d = b - a;
            var rt = img.rectTransform;
            rt.anchoredPosition = a;
            rt.sizeDelta = new Vector2(d.magnitude, Mathf.Max(1.5f, TilePx * 0.08f));
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            img.color = new Color(col.r, col.g, col.b, 0.55f);
        }

        // The pulses: out from the generators along every span, faster on a healthy grid.
        if (live != null && !live.Failed)
        {
            pulseTimer -= dt;
            if (pulseTimer <= 0f)
            {
                pulseTimer = Mathf.Lerp(0.9f, 0.35f, served);
                foreach (var s in spans)
                {
                    float len = Vector2.Distance(s.a, s.b);
                    float speed = Mathf.Lerp(2f, 6f, served);
                    pulses.Add(new Particle
                    {
                        img = Sprite(flowLayer, SoftDot(), col), at = s.a,
                        vel = (s.b - s.a) / Mathf.Max(0.01f, len) * speed,
                        life = len / speed, size0 = 0.45f, size1 = 0.3f, color = Color.Lerp(col, Color.white, 0.4f)
                    });
                }
            }
        }
        float px = TilePx;
        for (int i = pulses.Count - 1; i >= 0; i--)
        {
            var pt = pulses[i];
            pt.age += dt;
            if (pt.age >= pt.life || pt.img == null || pulses.Count > 400)
            {
                if (pt.img != null) Destroy(pt.img.gameObject);
                pulses.RemoveAt(i);
                continue;
            }
            pt.at += pt.vel * dt;
            Place(pt.img, pt.at, Mathf.Lerp(pt.size0, pt.size1, pt.age / pt.life) * px, pt.color, 1f);
        }
    }

    /// The grid's wiring: a spanning tree over its plants and pylons (each linked to its nearest
    /// in-reach neighbour, so a chain of pylons draws as the chain it is), oriented outward from the
    /// generators, plus a feed from the nearest projector to every building the grid powers.
    void BuildSpans(PowerNet net)
    {
        foreach (var l in spanLines) if (l != null) Destroy(l.gameObject);
        spanLines.Clear(); spans.Clear();
        foreach (var pt in pulses) if (pt.img != null) Destroy(pt.img.gameObject);
        pulses.Clear();
        if (net == null || cur == null) return;

        var proj = new List<PlacedBuilding>(net.projectors);
        if (proj.Count == 0) return;
        var pos = new Dictionary<PlacedBuilding, Vector2>();
        foreach (var p in proj) pos[p] = Centre(SurfaceBuildingDatabase.Footprint(p));

        // Prim's tree from the generators outward: every step adds the closest projector to the tree,
        // which both connects it and gives its span a direction (parent -> child = the way power flows).
        var inTree = new HashSet<PlacedBuilding>();
        foreach (var g in net.generators) if (pos.ContainsKey(g)) inTree.Add(g);
        if (inTree.Count == 0) inTree.Add(proj[0]);
        while (inTree.Count < proj.Count)
        {
            PlacedBuilding bestA = null, bestB = null; float best = float.MaxValue;
            foreach (var a in inTree)
                foreach (var b in proj)
                {
                    if (inTree.Contains(b)) continue;
                    float d = (pos[a] - pos[b]).sqrMagnitude;
                    if (d < best) { best = d; bestA = a; bestB = b; }
                }
            if (bestB == null) break;
            inTree.Add(bestB);
            AddSpan(pos[bestA], pos[bestB]);
        }

        // Feeds to everything the grid powers.
        var fed = new List<PlacedBuilding>(net.consumers);
        fed.AddRange(net.capacitors);
        foreach (var c in fed)
        {
            if (pos.ContainsKey(c)) continue;
            var cp = Centre(SurfaceBuildingDatabase.Footprint(c));
            PlacedBuilding near = null; float best = float.MaxValue;
            foreach (var p in proj) { float d = (pos[p] - cp).sqrMagnitude; if (d < best) { best = d; near = p; } }
            if (near != null) AddSpan(pos[near], cp);
        }
    }

    void AddSpan(Vector2 a, Vector2 b)
    {
        // Spans that wrap the date line would be drawn across the whole map; skip those.
        if (Mathf.Abs(a.x - b.x) > W * 0.5f) return;
        spans.Add(new Span { a = a, b = b });
        var img = UIFactory.NewUI(flowLayer, "span").AddComponent<Image>();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        spanLines.Add(img);
    }

    // ==============================================================================================

    void LateUpdate()
    {
        bool visible = Visible == null || Visible();
        var b = Body != null ? Body() : null;
        if (!visible || b?.surface == null)
        {
            if (content.gameObject.activeSelf) content.gameObject.SetActive(false);
            return;
        }
        if (!content.gameObject.activeSelf) content.gameObject.SetActive(true);

        if (b != cur) { cur = b; animSig = null; aoeSig = null; flowSig = null; }
        if (Time.unscaledTime >= nextSigCheck)
        {
            nextSigCheck = Time.unscaledTime + 0.5f;
            string sig = BuildSignature(cur);
            if (sig != animSig) { animSig = sig; RebuildEmitters(cur); aoeSig = null; flowSig = null; }
        }

        float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
        TickAnimations(dt, t);
        TickArea(t);
        TickFlow(dt, t);
    }
}
