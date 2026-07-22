using System.Collections.Generic;
using UnityEngine;

// Draws the galaxy's ancient derelict stations (Galaxy.derelicts) as small broken hulls that drift at their
// odd orbits — far out past a system's planets, dead still in the void, skimming a sun, or circling the
// black hole. Each tumbles slowly and wears a faint glyph ring so it reads as "something is out there."
// Click one to open the study window (AnomalyWindow). Kept under SystemContext.SystemParent so it hides with
// the detailed view exactly like the planets do (the galaxy overview shows the star proxies instead).
public class DerelictRenderer : MonoBehaviour
{
    public static DerelictRenderer Instance;

    static readonly Color Hull = new Color(0.16f, 0.17f, 0.19f);
    static readonly Color Glow = new Color(0.42f, 0.90f, 0.82f);   // Vael teal
    static readonly Color GlowDim = new Color(0.30f, 0.34f, 0.32f);

    class Rendered
    {
        public Derelict d;
        public GameObject go;
        public Vector3 center;    // local-to-SystemParent centre it orbits (or sits at)
        public float radius;
        public float angle;
        public bool orbiting;
        public LineRenderer ring;
        public bool concealed;    // what we last told ConcealBinding, so it isn't re-applied every frame
    }

    Transform root;
    readonly List<Rendered> _rendered = new List<Rendered>();
    Galaxy builtFor;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("DerelictRenderer").AddComponent<DerelictRenderer>();
    }

    void Awake() { Instance = this; }

    /// The hulls were destroyed underneath us — rebuild them on the next frame.
    ///
    /// This root is parented to SystemContext.SystemParent, and VisualizeGalaxy destroys every child of
    /// that transform. Until now that only ever happened when the Galaxy OBJECT changed, so the
    /// `g != builtFor` test below was a complete answer. GameManager.RebuildVisuals re-visualizes the
    /// SAME galaxy (a system was deleted or restored), and without this every derelict in the game would
    /// vanish for good the first time anyone used that tool — silently, with no error.
    public static void RebuildNow()
    {
        if (Instance != null) Instance.builtFor = null;
    }

    void Update()
    {
        var g = SystemContext.Galaxy;
        if (g != builtFor) Rebuild(g);
        // Advance the orbiting hulls (scaled time, so they pause with the game).
        float dt = Time.deltaTime;
        foreach (var r in _rendered)
        {
            if (r == null || r.go == null) continue;

            // A HULL IN A CONCEALED SYSTEM IS CONCEALED.
            //
            // A derelict is not a child of any body, so ConcealBinding's subtree sweep never reaches it —
            // the same gap that left owner rings drawing around hidden worlds. Without this, hiding a
            // system leaves its derelicts orbiting the empty space where its star was, drawn and
            // clickable, which is a marker pointing straight at what was just hidden. It also matters at
            // the genesis handoff, whose whole premise is that the homeworld arrives into empty space.
            // Only on CHANGE: ConcealBinding re-sweeps its subtree on every repeat call, which is right
            // for a planet whose atmosphere shell can be rebuilt underneath it, and pure waste for a hull
            // that nothing ever adds to. During genesis every derelict in the galaxy is concealed at once.
            bool wantConcealed = ConcealedSystem(g, r.d);
            if (wantConcealed != r.concealed)
            {
                r.concealed = wantConcealed;
                ConcealBinding.Set(r.go, wantConcealed);
            }

            if (r.orbiting)
            {
                r.angle += r.d.orbitSpeed * dt;
                Position(r);
            }
        }
    }

    // A derelict belongs to a SYSTEM (by index), or to no system at all — dead space and the galactic
    // core are outside every system, and those are concealed with the galaxy rather than with a system.
    static bool ConcealedSystem(Galaxy g, Derelict d)
    {
        if (g == null || d == null) return false;
        if (d.systemIndex >= 0 && d.systemIndex < g.systems.Count)
            return g.systems[d.systemIndex].hideReason != HideReason.None;
        return g.center != null && g.center.hideReason != HideReason.None;
    }

    void Rebuild(Galaxy g)
    {
        Clear();
        builtFor = g;
        var parent = SystemContext.SystemParent;
        if (g == null || g.derelicts == null || parent == null) { builtFor = null; return; }

        root = new GameObject("Derelicts").transform;
        root.SetParent(parent, false);

        foreach (var d in g.derelicts)
        {
            if (d == null) continue;
            var r = new Rendered { d = d, angle = d.orbitPhase };
            Resolve(g, d, r);
            r.go = BuildHull(d, out r.ring);
            r.go.transform.SetParent(root, false);
            r.go.AddComponent<DerelictClick>().Init(d);
            d.visual = r.go;
            if (d.studied) SetStudiedLook(r);
            _rendered.Add(r);
            Position(r);
        }
    }

    // Work out what each derelict circles, and how far out, in SystemParent-local space (galaxy positions
    // are already local to it).
    void Resolve(Galaxy g, Derelict d, Rendered r)
    {
        switch (d.orbit)
        {
            case Derelict.Orbit.DeadSpace:
                r.center = d.deadSpacePos; r.radius = 0f; r.orbiting = false;
                break;
            case Derelict.Orbit.BlackHole:
                r.center = g.centerPosition; r.radius = Mathf.Max(20f, d.orbitRadius); r.orbiting = true;
                break;
            case Derelict.Orbit.StarHugging:
            {
                var sys = SysOf(g, d.systemIndex);
                r.center = sys != null ? sys.galaxyPosition : Vector3.zero;
                float starR = sys != null && sys.combinedStar != null ? sys.combinedStar.visualScale * 0.5f : 3f;
                r.radius = starR + 2.5f; r.orbiting = true;
                break;
            }
            default: // FarOut
            {
                var sys = SysOf(g, d.systemIndex);
                r.center = sys != null ? sys.galaxyPosition : Vector3.zero;
                r.radius = OuterReach(sys) * 1.6f + 8f; r.orbiting = true;
                break;
            }
        }
    }

    static StarSystemData SysOf(Galaxy g, int i)
        => (g != null && g.systems != null && i >= 0 && i < g.systems.Count) ? g.systems[i] : null;

    static float OuterReach(StarSystemData sys)
    {
        float outer = 12f;
        if (sys != null && sys.bodies != null)
            foreach (var b in sys.bodies)
                if (b != null && b.parentBody == null) outer = Mathf.Max(outer, b.orbitRadius);
        return outer;
    }

    void Position(Rendered r)
    {
        if (r.go == null) return;
        if (r.orbiting)
        {
            float a = r.angle * Mathf.Deg2Rad;
            r.go.transform.localPosition = r.center + new Vector3(Mathf.Cos(a) * r.radius, 0f, Mathf.Sin(a) * r.radius);
        }
        else r.go.transform.localPosition = r.center;
    }

    GameObject BuildHull(Derelict d, out LineRenderer ring)
    {
        var go = new GameObject("Derelict" + d.id);

        // A broken hull: a main block and a snapped-off strut, tilted at odd angles.
        var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hull.transform.SetParent(go.transform, false);
        hull.transform.localScale = new Vector3(1.2f, 0.5f, 0.8f);
        hull.transform.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));
        Tint(hull, Hull);

        var strut = GameObject.CreatePrimitive(PrimitiveType.Cube);
        strut.transform.SetParent(go.transform, false);
        strut.transform.localScale = new Vector3(0.22f, 0.22f, 1.5f);
        strut.transform.localPosition = new Vector3(0.35f, 0.1f, 0f);
        strut.transform.localRotation = Quaternion.Euler(12f, 34f, 16f);
        Tint(strut, Hull);

        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        var sc = go.AddComponent<SphereCollider>();
        sc.radius = 2.2f;   // generous local pick radius (the hull is small)

        ring = MakeRing(go.transform, "Glyph", 1.6f, Glow, 0.05f, 56);

        go.transform.localScale = Vector3.one * 0.5f;
        go.AddComponent<SelfSpin>().speed = 8f;   // slow tumble
        return go;
    }

    public void MarkStudied(Derelict d)
    {
        foreach (var r in _rendered)
            if (r != null && r.d == d) { SetStudiedLook(r); return; }
    }

    static void SetStudiedLook(Rendered r)
    {
        if (r.ring != null) r.ring.startColor = r.ring.endColor = GlowDim;   // the glyph goes dark once salvaged
    }

    static void Tint(GameObject go, Color c)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        var m = rend.material;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.3f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static LineRenderer MakeRing(Transform parent, string name, float radius, Color color, float width, int seg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = true; lr.positionCount = seg;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = color;
        lr.startWidth = lr.endWidth = Mathf.Max(0.02f, width);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        return lr;
    }

    void Clear()
    {
        foreach (var r in _rendered) if (r != null && r.go != null) Destroy(r.go);
        _rendered.Clear();
        if (root != null) Destroy(root.gameObject);
        root = null;
    }
}

// Click a derelict hull to open the study window.
public class DerelictClick : MonoBehaviour
{
    Derelict d;
    public void Init(Derelict derelict) { d = derelict; }

    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        SimpleAudio.Instance?.PlaySelect();
        AnomalyWindow.Instance?.ShowDerelict(d);
    }
}
