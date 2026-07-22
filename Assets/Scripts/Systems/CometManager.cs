using System.Collections.Generic;
using UnityEngine;

// Sends comets across the galaxy from time to time, each from a different edge on a different heading, so
// there is always the occasional bright visitor to notice. When one sweeps through a system you hold, a
// tantalising notice fires. With the right instruments (empire tech) you can study one in flight and catch
// whatever it's carrying — usually nothing, sometimes salvage or lost technology, rarely a Vael echo, and
// every so often something ridiculous.
public class CometManager : MonoBehaviour
{
    public static CometManager Instance;

    const int MaxActive = 2;
    const float MinGap = 25f, MaxGap = 70f;   // seconds between spawns

    Transform root;
    readonly List<Comet> _comets = new List<Comet>();
    Galaxy builtFor;
    float _spawnIn;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("CometManager").AddComponent<CometManager>();
    }

    void Awake() { Instance = this; }

    /// The comet root was destroyed underneath us — re-establish it on the next frame.
    /// See DerelictRenderer.RebuildNow for the full reason; without this, Spawn's `root == null` guard
    /// would silently stop every comet in the session after the first in-place galaxy rebuild.
    public static void RebuildNow()
    {
        if (Instance != null) Instance.builtFor = null;
    }

    void Update()
    {
        var g = SystemContext.Galaxy;
        if (g != builtFor) Reset(g);
        if (builtFor == null) return;

        float dt = Time.deltaTime;

        // Spawn cadence.
        _spawnIn -= dt;
        if (_spawnIn <= 0f && _comets.Count < MaxActive)
        {
            Spawn(g);
            _spawnIn = Random.Range(MinGap, MaxGap);
        }

        // Fly the active comets; retire the ones that have crossed clean out of the galaxy.
        float bound = CameraController.GalaxyRadius() * 1.6f + 200f;
        for (int i = _comets.Count - 1; i >= 0; i--)
        {
            var c = _comets[i];
            if (c == null || c.visual == null) { _comets.RemoveAt(i); continue; }
            c.pos += c.dir * c.speed * dt;
            c.visual.transform.localPosition = c.pos;
            Announce(g, c);
            if (c.pos.sqrMagnitude > bound * bound) { Destroy(c.visual); _comets.RemoveAt(i); }
        }
    }

    void Reset(Galaxy g)
    {
        foreach (var c in _comets) if (c != null && c.visual != null) Destroy(c.visual);
        _comets.Clear();
        if (root != null) Destroy(root.gameObject);
        root = null;
        builtFor = g;
        var parent = SystemContext.SystemParent;
        if (g == null || parent == null) { builtFor = null; return; }
        root = new GameObject("Comets").transform;
        root.SetParent(parent, false);
        _spawnIn = Random.Range(4f, 14f);   // first visitor comes fairly soon
    }

    void Spawn(Galaxy g)
    {
        if (root == null) return;
        float edge = CameraController.GalaxyRadius() * 1.25f + 60f;

        // Enter from a random edge point, aim across the galaxy toward the far side with a random offset so
        // no two paths look the same.
        float inAng = Random.Range(0f, Mathf.PI * 2f);
        Vector3 start = new Vector3(Mathf.Cos(inAng), 0f, Mathf.Sin(inAng)) * edge;
        Vector3 target = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * (edge * 0.5f);
        Vector3 dir = (target - start);
        if (dir.sqrMagnitude < 0.01f) dir = -start;
        dir.Normalize();

        var c = new Comet
        {
            pos = start,
            dir = dir,
            size = Random.Range(1.4f, 3.8f),
            speed = Random.Range(18f, 40f),
        };
        Comet.Roll(c);
        c.visual = BuildVisual(c);
        c.visual.transform.SetParent(root, false);
        c.visual.transform.localPosition = c.pos;
        _comets.Add(c);
    }

    // Fire the "a great comet is passing through" notice the first time a comet sweeps near a system the
    // player holds — worded to tantalise, since some comets are duds and you can't tell from here.
    void Announce(Galaxy g, Comet c)
    {
        if (g.systems == null) return;
        for (int i = 0; i < g.systems.Count; i++)
        {
            var sys = g.systems[i];
            if (sys == null || !PlayerHolds(sys)) continue;
            if (c.announced.Contains(i)) continue;
            float reach = OuterReach(sys) * 2f + 40f;
            if ((c.pos - sys.galaxyPosition).sqrMagnitude <= reach * reach)
            {
                c.announced.Add(i);
                var cap = c;
                NotificationManager.Instance?.Push(
                    $"A comet is passing through {sys.name}",
                    $"{c.SizeWord} comet is sweeping through {sys.name} space, burning bright in the dark. Its " +
                    "coma catches the starlight strangely — there may be more to it than ice. Click to look closer.",
                    () => AnomalyWindow.Instance?.ShowComet(cap),
                    NotifKind.Discovery);
            }
        }
    }

    static bool PlayerHolds(StarSystemData sys)
    {
        if (sys.owner == FactionManager.Player || sys.isHome) return true;
        if (sys.bodies != null)
            foreach (var b in sys.bodies)
                if (b != null && b.owner == FactionManager.Player) return true;
        return false;
    }

    static float OuterReach(StarSystemData sys)
    {
        float outer = 12f;
        if (sys != null && sys.bodies != null)
            foreach (var b in sys.bodies)
                if (b != null && b.parentBody == null) outer = Mathf.Max(outer, b.orbitRadius);
        return outer;
    }

    // ---- Study (called from the AnomalyWindow) ----
    public void Study(Comet c)
    {
        if (c == null || c.studied) return;
        c.studied = true;

        switch (c.payload)
        {
            case Comet.Payload.Nothing:
                if (c.research > 0) ResearchManager.AddPoints(c.research);
                Notify("The comet was only ice and dust", c.flavor, NotifKind.Info);
                break;
            case Comet.Payload.Materials:
                PlayerEconomy.Add(ResourceType.Metal, c.metal);
                PlayerEconomy.Add(ResourceType.Energy, c.energy);
                ResearchManager.AddPoints(c.research);
                Notify("Comet caught — a rich haul",
                    $"+{c.metal} metal, +{c.energy} energy, +{c.research} research.\n{c.flavor}", NotifKind.Discovery);
                break;
            case Comet.Payload.Technology:
                ResearchManager.AddPoints(c.research);
                Notify("Comet caught — lost technology", $"+{c.research} research.\n{c.flavor}", NotifKind.Discovery);
                break;
            case Comet.Payload.EasterEgg:
                if (c.metal > 0) PlayerEconomy.Add(ResourceType.Metal, c.metal);
                Notify("Comet caught — well, that's new", c.flavor, NotifKind.Discovery);
                break;
            case Comet.Payload.Lore:
                Notify("An echo of the Vael, adrift", c.flavor, NotifKind.Ancient);
                break;
        }
    }

    static void Notify(string title, string msg, NotifKind kind)
        => NotificationManager.Instance?.Push(title, msg, null, kind);

    // ---- Visual ----
    GameObject BuildVisual(Comet c)
    {
        var go = new GameObject("Comet");

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.transform.SetParent(go.transform, false);
        head.transform.localScale = Vector3.one * c.size;
        var rend = head.GetComponent<Renderer>();
        var icy = new Color(0.75f, 0.9f, 1f);
        rend.material = UnlitBright(icy);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var hc = head.GetComponent<Collider>(); if (hc != null) Destroy(hc);

        var sc = go.AddComponent<SphereCollider>();
        sc.radius = c.size * 1.3f;
        go.AddComponent<CometClick>().Init(c);

        // Tail: a fading streak trailing opposite the direction of travel (dir is constant, so set once).
        var tailGo = new GameObject("Tail");
        tailGo.transform.SetParent(go.transform, false);
        var lr = tailGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = false;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        int n = 10;
        float len = c.size * 9f;
        lr.positionCount = n;
        for (int i = 0; i < n; i++)
            lr.SetPosition(i, -c.dir * (len * i / (n - 1)));
        lr.startWidth = c.size * 1.1f; lr.endWidth = 0.02f;
        lr.startColor = new Color(icy.r, icy.g, icy.b, 0.85f);
        lr.endColor = new Color(icy.r, icy.g, icy.b, 0f);

        return go;
    }

    static Material UnlitBright(Color c)
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
}

// Click a comet to open the study window.
public class CometClick : MonoBehaviour
{
    Comet c;
    public void Init(Comet comet) { c = comet; }

    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        SimpleAudio.Instance?.PlaySelect();
        AnomalyWindow.Instance?.ShowComet(c);
    }
}
