using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

// Level-of-detail for the galaxy. When you zoom far enough out, the fully-detailed systems (planets,
// moons, orbits) are de-rendered for performance and each system is represented instead by an ENLARGED
// 3D render of its host star — or its whole cluster, for a binary/ternary — floating at the system's
// position. The system DATA stays live the whole time; only the heavy visuals are toggled off.
//
// A system where your empire is present wears the same green owner ring planets and moons use, rather
// than a coloured UI square. Hovering a star opens a cursor-anchored info panel (name + masses); clicking
// it opens the system window with its details and a "Go to system" button.
public class GalaxyLOD : MonoBehaviour
{
    public static GalaxyLOD Instance;

    // The height at which detailed systems give way to the enlarged-star overview, derived from how big
    // the galaxy actually is rather than fixed, so the overview appears as the systems get too small to
    // read, at any galaxy size. Hysteresis (enter > exit) avoids flicker at the threshold. Lowered so the
    // star overview takes over SOONER as you zoom out (detailed systems hand off earlier).
    const float EnterFrac = 0.40f;
    const float ExitFrac = 0.30f;
    const float MinEnter = 200f;

    // Enlarged-star proxy size as a fraction of the galaxy's radius — its size at the moment the overview
    // kicks in. From there it GROWS with zoom (see UpdateProxyScales) to hold a roughly constant on-screen
    // size, so a system's star never shrinks to nothing however far you pull back. Tunable.
    const float ProxySizeFrac = 0.045f;
    const float ProxySizeMin = 3f;

    // How far a proxy may grow, as a fraction of the galaxy radius, before the cap kicks in. Generous, so
    // the stars stay a constant on-screen size out to very far zoom; past the cap they slowly shrink again
    // rather than growing without bound. (Near max zoom the proxies overlap — which just reads as the
    // galaxy's own glow, an acceptable "galaxy from far away" look.)
    const float ProxyMaxFracOfGalaxy = 0.9f;

    float EnterGalaxy
    {
        get
        {
            var cc = CameraController.Instance;
            float frame = cc != null ? cc.HeightToFrame(CameraController.GalaxyRadius()) : 0f;
            return Mathf.Max(MinEnter, frame * EnterFrac);
        }
    }

    float ExitGalaxy
    {
        get
        {
            var cc = CameraController.Instance;
            float frame = cc != null ? cc.HeightToFrame(CameraController.GalaxyRadius()) : 0f;
            return Mathf.Max(MinEnter * 0.76f, frame * ExitFrac);
        }
    }

    Camera cam;
    Transform proxyRoot;
    readonly List<GalaxyStarProxy> proxies = new List<GalaxyStarProxy>();
    Galaxy builtFor;
    bool galaxyView;
    float proxyBaseSize = 1f;   // the world size each proxy was built at (grown from here with zoom)

    public static void Create(Transform canvas)
    {
        if (Instance != null) return;
        var go = new GameObject("GalaxyLOD");
        go.transform.SetParent(canvas, false);
        Instance = go.AddComponent<GalaxyLOD>();
        Instance.Init();
    }

    void Awake() { Instance = this; }

    void Init()
    {
        cam = Camera.main;
        // World-space (NOT under the canvas): the proxies are real 3D stars at the systems' positions. A
        // separate root from SystemParent, since that one is toggled OFF in the overview.
        proxyRoot = new GameObject("GalaxyStarProxies").transform;
        proxyRoot.gameObject.SetActive(false);
    }

    void RebuildProxies(Galaxy g)
    {
        foreach (var m in proxies) if (m != null) Destroy(m.gameObject);
        proxies.Clear();
        builtFor = g;
        if (g == null) return;

        float size = Mathf.Max(ProxySizeMin, CameraController.GalaxyRadius() * ProxySizeFrac);
        proxyBaseSize = size;
        foreach (var sys in g.systems)
            proxies.Add(GalaxyStarProxy.Build(proxyRoot, sys, size));
    }

    // Grow every proxy linearly with camera height so its ON-SCREEN size stays roughly constant as you
    // zoom out (screen size ~ worldSize / height; worldSize ~ height keeps it fixed) — a system's star
    // never shrinks to a sub-pixel dot and "clips out of existence". Capped so a proxy never grows past a
    // fraction of the galaxy, which would turn extreme zoom into one overlapping blob. Cheap: a handful of
    // localScale writes per frame, and only while the overview is up.
    void UpdateProxyScales()
    {
        if (cam == null || proxies.Count == 0 || proxyBaseSize <= 0.0001f) return;
        float h = Mathf.Max(1f, cam.transform.position.y);
        float refH = Mathf.Max(1f, EnterGalaxy);
        float maxScale = Mathf.Max(4f, CameraController.GalaxyRadius() * ProxyMaxFracOfGalaxy / proxyBaseSize);
        float f = Mathf.Clamp(h / refH, 1f, maxScale);
        var s = Vector3.one * f;
        foreach (var m in proxies) if (m != null) m.transform.localScale = s;
    }

    void Update()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        var g = SystemContext.Galaxy;
        if (g != builtFor) RebuildProxies(g);
        if (g == null) return;

        float h = cam.transform.position.y;
        bool want = galaxyView ? (h > ExitGalaxy) : (h > EnterGalaxy);
        if (want != galaxyView) SetGalaxyView(want);

        if (galaxyView) UpdateProxyScales();   // keep the stars a constant on-screen size as you zoom
    }

    void SetGalaxyView(bool on)
    {
        galaxyView = on;
        // De-render the heavy detailed systems, show the enlarged-star overview — and vice versa.
        if (SystemContext.SystemParent != null) SystemContext.SystemParent.gameObject.SetActive(!on);
        if (proxyRoot != null) proxyRoot.gameObject.SetActive(on);
        if (!on && MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();
    }

    void OnDestroy()
    {
        if (proxyRoot != null) Destroy(proxyRoot.gameObject);
    }
}

// One enlarged 3D star (or cluster) standing in for a whole system in the galaxy overview. Carries the
// green empire ring, the cursor hover panel, and the click-to-open behaviour.
public class GalaxyStarProxy : MonoBehaviour
{
    public StarSystemData system;

    public static GalaxyStarProxy Build(Transform parent, StarSystemData sys, float size)
    {
        var root = new GameObject("Proxy_" + sys.name);
        root.transform.SetParent(parent, false);
        root.transform.position = sys.galaxyPosition;
        var proxy = root.AddComponent<GalaxyStarProxy>();
        proxy.system = sys;

        // The suns to draw: the individual members of a cluster, or the lone/combined star.
        var suns = (sys.stars != null && sys.stars.Count > 0)
            ? sys.stars
            : new List<StarData> { sys.combinedStar };
        int n = Mathf.Max(1, suns.Count);

        for (int i = 0; i < n; i++)
        {
            var sun = i < suns.Count ? suns[i] : null;
            if (sun == null) sun = sys.combinedStar;
            Color c = sys.isBlackHole ? new Color(0.05f, 0.02f, 0.08f)
                    : (sun != null ? sun.color : Color.white);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Sun" + i;
            sphere.transform.SetParent(root.transform, false);
            // Spread the members of a cluster so a binary/ternary reads as more than one dot.
            Vector3 off = Vector3.zero;
            if (n > 1)
            {
                float a = i * Mathf.PI * 2f / n;
                off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size * 0.9f;
            }
            sphere.transform.localPosition = off;
            sphere.transform.localScale = Vector3.one * size * (n > 1 ? 0.8f : 1f);

            var rend = sphere.GetComponent<Renderer>();
            rend.material = StarMat(c);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);   // one shared collider on the root instead
        }

        // One generous click/hover target covering the whole cluster.
        var sc = root.AddComponent<SphereCollider>();
        sc.center = Vector3.zero;
        sc.radius = size * (n > 1 ? 1.9f : 1.15f);

        // The green empire ring — the SAME idea planets and moons use — for any system your (or another)
        // empire holds; the home system always shows it.
        if (sys.isHome || sys.owner != null)
        {
            Color rc = sys.owner != null ? FactionManager.OwnerColor(sys.owner) : new Color(0.35f, 1f, 0.45f);
            MakeRing(root.transform, "OwnerRing", size * 2.1f, rc, size * 0.14f, 72);
        }

        return proxy;
    }

    void OnMouseOver()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        MapHoverPanel.Instance?.ShowAtCursor(HoverText());
    }

    void OnMouseExit()
    {
        if (MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();
    }

    void OnMouseDown()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        SimpleAudio.Instance?.PlaySelect();
        if (MapHoverPanel.Instance != null) MapHoverPanel.Instance.Hide();
        SystemSummaryWindow.Instance?.Show(system);
    }

    // Cursor-anchored summary: system name, class, each sun's mass (and the total for a cluster), owner.
    string HoverText()
    {
        var sb = new StringBuilder();
        sb.Append($"<b>{system.name}</b>");
        var star = system.combinedStar;
        sb.Append($"\n<color=#9FB4C8>{StarDatabase.SystemClass(star)}</color>");

        var suns = system.stars;
        if (!system.isBlackHole && suns != null && suns.Count > 1)
        {
            var ordered = new List<StarData>(suns);
            ordered.Sort((a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));
            float total = 0f;
            foreach (var sun in ordered)
            {
                if (sun == null) continue;
                total += sun.mass;
                sb.Append($"\n<color=#9FB4C8>{sun.name}:</color> {sun.mass:F2} solar");
            }
            sb.Append($"\n<color=#9FB4C8>Total mass:</color> <b>{total:F2}</b> solar");
        }
        else if (star != null)
            sb.Append($"\n<color=#9FB4C8>Mass:</color> {star.mass:F2} solar");

        string own = system.owner == FactionManager.Player ? "<color=#4DFF6E>Your empire</color>"
                   : system.owner != null ? FactionManager.OwnerName(system.owner)
                   : "Unclaimed";
        sb.Append($"\n<color=#9FB4C8>Owner:</color> {own}");
        return sb.ToString();
    }

    // An unlit material so the proxy shows its star colour regardless of scene lighting (the detailed
    // systems' star lights are off in the overview).
    static Material StarMat(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        m.color = c;
        return m;
    }

    // A flat looped ring in the XZ plane (the galaxy is viewed top-down), matching the owner-ring look.
    static void MakeRing(Transform parent, string name, float radius, Color color, float width, int seg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = true; lr.positionCount = seg;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = color;
        lr.startWidth = lr.endWidth = Mathf.Max(0.03f, width);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
    }
}
