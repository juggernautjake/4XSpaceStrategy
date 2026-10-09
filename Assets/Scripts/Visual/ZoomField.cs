using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE PLANET ZOOM FIELD — a planet's own space (2026-10-09)
//
// A flat disc around every PLANET (never a moon, never a belt rock), a little wider than its outermost
// moon's orbit. It is three things at once, which is why it is one definition rather than three:
//
//   * THE CAMERA'S LOCK ZONE — zoom in on a point inside a field and the camera starts riding along
//     with the planet (CameraController.TryAutoLock), so a world moving round its sun doesn't slide
//     out of a close-up.
//   * THE PLANET'S ORBIT — every ship, station and moon inside it belongs in that planet's Orbit tab.
//     Docked ships and stations already ride with the planet; this adds anything parked in the field.
//   * WHERE NEW HULLS WAIT — a ship built at a planet's yard rolls out docked there, in its field, and
//     stays until it is given orders (UnitManager.YardFor).
//
// Drawn as a circle with a grid infill, 50% transparent: always in Dev Mode, and otherwise only when
// the planet's Orbit tab toggle is on or something is being built for its orbit (ForceShow). Colour
// and grid style are per-planet Dev settings (OrbitControlPanel) and are saved with the body.
// ============================================================================================
public static class ZoomFieldRules
{
    /// The field reaches this much past the outermost moon's orbit (or the planet's own edge).
    public const float ReachMargin = 1.35f;
    /// ...and never less than this many planet radii, so a moonless world still has room for ships.
    public const float MinPlanetRadii = 5f;

    public static readonly Color DefaultColor = new Color(0.85f, 0.87f, 0.90f, 0.5f);

    /// Grid styles the Dev window can pick between.
    public static readonly string[] GridNames = { "Square", "Fine square", "Polar", "None" };

    public static bool IsHost(CelestialBody b)
        => b != null && b.parentBody == null && b.type != CelestialBodyType.Asteroid && b.beltId == 0;

    public static float Radius(CelestialBody b)
    {
        if (b == null) return 0f;
        float r = Mathf.Max(OrbitSafety.SystemReach(b) * ReachMargin, OrbitSafety.DiscRadius(b) * MinPlanetRadii);
        return r * Mathf.Clamp(b.zoomFieldScale <= 0f ? 1f : b.zoomFieldScale, 0.5f, 3f);
    }

    public static Color ColorOf(CelestialBody b)
        => b != null && b.zoomFieldColorSet ? b.zoomFieldColor : DefaultColor;

    /// Is a world point inside this planet's field? Measured flat (x/z), because the field is a disc in
    /// the planet's orbital plane and a ship a little above or below it is still in orbit.
    public static bool Contains(CelestialBody b, Vector3 world)
    {
        if (!IsHost(b) || b.visualObject == null) return false;
        Vector3 c = b.visualObject.transform.position;
        float dx = world.x - c.x, dz = world.z - c.z;
        float r = Radius(b);
        return dx * dx + dz * dz <= r * r;
    }

    /// The planet whose field holds this point, nearest centre first, or null.
    public static CelestialBody HostAt(Vector3 world)
    {
        CelestialBody best = null; float bestD = float.MaxValue;
        foreach (var b in SystemContext.AllBodies())
        {
            if (!IsHost(b) || b.visualObject == null || !b.visualObject.activeInHierarchy) continue;
            Vector3 c = b.visualObject.transform.position;
            float dx = world.x - c.x, dz = world.z - c.z, d = dx * dx + dz * dz;
            float r = Radius(b);
            if (d <= r * r && d < bestD) { best = b; bestD = d; }
        }
        return best;
    }

    /// Ships and stations PARKED in open space inside this planet's field — the ones body.units does
    /// not already list because they are not docked. A snapshot: a parked ship holds a fixed point while
    /// the planet orbits on, so it leaves the field over time. Docking (body.units) is what rides along.
    public static void ParkedIn(CelestialBody b, List<Unit> into)
    {
        if (into == null || b == null || UnitManager.Instance == null) return;
        foreach (var u in UnitManager.Instance.Units)
            if (u != null && u.location == null && u.inSpace && u.owner == FactionManager.Player && Contains(b, u.parkPosition))
                into.Add(u);
    }
}

/// Draws every field that should be visible. One component for all of them, reconciled a few times a
/// second and positioned every frame, because a field must follow its planet round the sun and a
/// per-planet child would inherit the planet's own scale and spin.
public class ZoomFieldRenderer : MonoBehaviour
{
    public static ZoomFieldRenderer Instance;

    /// A planet whose field is shown regardless of its own toggle — while something is being built for
    /// its orbit. Times out on its own (ShowFor), so nothing has to remember to clear it.
    public static CelestialBody ForceShow;
    static float forceUntil;

    public static void ShowFor(CelestialBody b, float seconds)
    {
        ForceShow = b;
        forceUntil = Time.unscaledTime + seconds;
    }

    /// Orbital construction for this planet: take the camera to its field and light the field up, so the
    /// player sees where the new station will live.
    public static void FocusForConstruction(CelestialBody b)
    {
        if (!ZoomFieldRules.IsHost(b) || b.visualObject == null) return;
        ShowFor(b, 8f);
        CameraController.Instance?.FocusAndZoom(b.visualObject.transform, b.surfaceSize, true);
    }

    class Field { public GameObject go; public MeshRenderer mr; public Material mat; public LineRenderer rim; public int grid = -1; }

    readonly Dictionary<CelestialBody, Field> fields = new Dictionary<CelestialBody, Field>();
    readonly List<CelestialBody> drop = new List<CelestialBody>();
    readonly HashSet<CelestialBody> want = new HashSet<CelestialBody>();
    float nextReconcile;
    static Mesh quad;
    static readonly Dictionary<int, Texture2D> gridTex = new Dictionary<int, Texture2D>();

    public static void Create()
    {
        if (Instance != null) return;
        Instance = new GameObject("ZoomFieldRenderer").AddComponent<ZoomFieldRenderer>();
    }

    static bool Wanted(CelestialBody b)
        => ZoomFieldRules.IsHost(b) && b.visualObject != null && b.visualObject.activeInHierarchy
           && (GameMode.DevMode || b.showZoomField || ForceShow == b);

    void LateUpdate()
    {
        if (ForceShow != null && Time.unscaledTime >= forceUntil) ForceShow = null;
        if (Time.unscaledTime >= nextReconcile)
        {
            nextReconcile = Time.unscaledTime + 0.25f;
            want.Clear();
            foreach (var b in SystemContext.AllBodies()) if (Wanted(b)) want.Add(b);
            drop.Clear();
            foreach (var kv in fields) if (!want.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var b in drop)
            {
                var f = fields[b];
                // The two materials are this field's own instances; destroying the object leaves them.
                if (f.mat != null) Destroy(f.mat);
                if (f.rim != null && f.rim.material != null) Destroy(f.rim.material);
                if (f.go != null) Destroy(f.go);
                fields.Remove(b);
            }
            foreach (var b in want) if (!fields.ContainsKey(b)) fields[b] = Make();
        }

        foreach (var kv in fields)
        {
            var b = kv.Key; var f = kv.Value;
            if (f.go == null || b.visualObject == null) continue;
            // Concealed worlds keep their secrets: no field drawn round nothing.
            var bind = b.visualObject.GetComponent<ConcealBinding>();
            bool show = (bind == null || !bind.Concealed) && !GenesisCamera.Active;
            if (f.go.activeSelf != show) f.go.SetActive(show);
            if (!show) continue;

            float r = ZoomFieldRules.Radius(b);
            f.go.transform.position = b.visualObject.transform.position;
            f.go.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);

            int g = Mathf.Clamp(b.zoomFieldGrid, 0, ZoomFieldRules.GridNames.Length - 1);
            if (f.grid != g) { f.grid = g; f.mat.mainTexture = GridTexture(g); }
            Color c = ZoomFieldRules.ColorOf(b);
            f.mat.color = c;
            f.rim.startColor = f.rim.endColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a + 0.3f));
        }
    }

    Field Make()
    {
        var f = new Field();
        f.go = new GameObject("ZoomField");
        f.go.transform.SetParent(transform, false);
        // Lying flat in the orbital plane: the quad faces +Z, so tip it onto X/Z.
        f.go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var mf = f.go.AddComponent<MeshFilter>();
        mf.sharedMesh = Quad();
        f.mr = f.go.AddComponent<MeshRenderer>();
        f.mat = new Material(Shader.Find("Sprites/Default"));
        f.mr.sharedMaterial = f.mat;
        f.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        f.mr.receiveShadows = false;

        // The rim, in the quad's own unit space (radius 0.5) so it scales with it.
        var rimGo = new GameObject("Rim");
        rimGo.transform.SetParent(f.go.transform, false);
        f.rim = rimGo.AddComponent<LineRenderer>();
        f.rim.useWorldSpace = false;
        f.rim.loop = true;
        f.rim.material = new Material(Shader.Find("Sprites/Default"));
        f.rim.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        f.rim.startWidth = f.rim.endWidth = 0.004f;
        const int seg = 96;
        f.rim.positionCount = seg;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            f.rim.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f));
        }
        return f;
    }

    static Mesh Quad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "ZoomFieldQuad" };
        quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
        quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        // Both windings, so the disc reads from above and below the orbital plane.
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
        quad.RecalculateBounds();
        return quad;
    }

    /// The field's infill: white grid lines over a faint white wash, clipped to a circle. Tinted and
    /// faded by the material colour, so one texture per style serves every planet.
    static Texture2D GridTexture(int style)
    {
        if (gridTex.TryGetValue(style, out var t) && t != null) return t;
        const int N = 256;
        t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N * N];
        int cells = style == 1 ? 24 : 12;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v) * 2f;           // 0 centre .. 1 rim
                byte a = 0;
                if (r <= 1f)
                {
                    bool line = false;
                    if (style == 0 || style == 1)
                    {
                        float gx = Mathf.Repeat((u + 0.5f) * cells, 1f), gy = Mathf.Repeat((v + 0.5f) * cells, 1f);
                        float w = style == 1 ? 0.10f : 0.07f;
                        line = gx < w || gy < w;
                    }
                    else if (style == 2)
                    {
                        float ring = Mathf.Repeat(r * 6f, 1f);
                        float ang = Mathf.Repeat(Mathf.Atan2(v, u) / (Mathf.PI * 2f) * 16f, 1f);
                        line = ring < 0.06f || (ang < 0.04f * Mathf.Max(0.3f, 1f - r) + 0.02f);
                    }
                    // Lines strong, the wash between them faint, so the field reads as an area and the
                    // grid as its texture rather than as a solid plate.
                    a = (byte)(line ? 200 : 40);
                }
                px[y * N + x] = new Color32(255, 255, 255, a);
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        gridTex[style] = t;
        return t;
    }
}
