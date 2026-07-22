using System.Collections.Generic;
using UnityEngine;

// Which unit classes have a real 3D model instead of a flat billboard token, where that model lives,
// and how it should behave once it's in the world.
//
// Everything is optional-with-fallback (the same pattern as AssetIntegration's CC0 detail maps): if a
// model isn't importable or simply isn't there, Resources.Load returns null and that class quietly
// keeps its billboard token. Art is never load-bearing.
public static class UnitModelLibrary
{
    // How a model carries itself once placed.
    public enum Motion
    {
        OrbitHost,   // a station: circles whatever it's deployed at, and turns on its own axis
        Freeflying   // a ship: faces where it's going, with a gentle idle roll
    }

    public class Entry
    {
        public string path;        // Resources path, no extension
        public float size;         // largest dimension, in world units, after normalisation
        public Motion motion;
        public float spin;         // degrees/sec of idle axial rotation
        // A fixed orientation correction applied AFTER the ship is pointed along its course, in the model's
        // own local frame — so an imported hull that faces the wrong way sits right relative to travel.
        public Quaternion modelRotation = Quaternion.identity;
    }

    // Sizes are measured against the worlds these things sit next to. SystemVisualizer scales a planet
    // to surfaceSize * 0.08 (min 0.6) and a moon to * 0.05 (min 0.35), so a world is only ~0.6-2.2 units
    // across. Anything here has to be a few TENTHS of a unit or it dwarfs the planet it's orbiting.
    static readonly Dictionary<UnitType, Entry> map = new Dictionary<UnitType, Entry>();
    static bool built;

    static void Build()
    {
        built = true;

        // Every station class shares the one station model for now.
        foreach (var info in UnitDatabase.All)
        {
            if (info == null || !info.isStation) continue;
            map[info.type] = new Entry
            {
                path = "SpaceAssets/Stations/LP Space Station",
                // Tier matters: a Mega-Station should read as the "little moon" its description promises,
                // which is right about the size of an actual small moon (0.35).
                size = 0.16f + Mathf.Clamp(info.stationLevel, 1, 3) * 0.07f,
                motion = Motion.OrbitHost,
                spin = 14f
            };
        }

        // The colony ship — the one hull big and characterful enough to be worth a mesh. It's also the
        // ship you watch most closely, since it's what founds a world.
        map[UnitType.ColonyShip] = new Entry
        {
            path = "SpaceAssets/Ships/LP Colony Ship",
            size = 0.34f,
            motion = Motion.Freeflying,
            spin = 0f,      // it points where it's going; a spinning colony ship would look broken
            // Pitch the hull up 90° about its lateral axis so it sits the right way up.
            modelRotation = Quaternion.Euler(-90f, 0f, 0f)
        };

        // The whole research line shares the science hull. They're the same silhouette conceptually —
        // a mobile laboratory — and each tier is visibly bigger than the last, which is the cheapest
        // honest way to show that a Mk III is a more serious ship than a Mk I.
        const string sciencePath = "SpaceAssets/Ships/LP Science Ship";
        // Yaw the science hull 90° about its up axis so it faces the right way.
        var sciRot = Quaternion.Euler(0f, 90f, 0f);
        map[UnitType.ResearchShip] = new Entry { path = sciencePath, size = 0.22f, motion = Motion.Freeflying, modelRotation = sciRot };
        map[UnitType.ResearchShipII] = new Entry { path = sciencePath, size = 0.26f, motion = Motion.Freeflying, modelRotation = sciRot };
        map[UnitType.ResearchShipIII] = new Entry { path = sciencePath, size = 0.30f, motion = Motion.Freeflying, modelRotation = sciRot };
        // The Science Vessel is the top of that line — a dedicated deep-survey laboratory, and the
        // largest of them.
        map[UnitType.ScienceVessel] = new Entry { path = sciencePath, size = 0.34f, motion = Motion.Freeflying, modelRotation = sciRot };
    }

    public static Entry For(UnitType t)
    {
        if (!built) Build();
        return map.TryGetValue(t, out var e) ? e : null;
    }

    // ---- Prefab cache ----
    static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

    public static GameObject Prefab(string path)
    {
        if (prefabs.TryGetValue(path, out var p)) return p;
        p = Resources.Load<GameObject>(path);
        if (p == null)
            Debug.Log($"[UnitModel] No model at Resources/{path} — that class will use its billboard token. " +
                      "Drop an .fbx there to enable it (a .blend only imports if Blender is installed on this machine).");
        prefabs[path] = p;
        return p;
    }

    /// Does this unit render as a mesh? False whenever the art is missing, which is what keeps the
    /// game running on a checkout with no models.
    public static bool HasModel(Unit u)
    {
        if (u == null) return false;
        var e = For(u.type);
        return e != null && Prefab(e.path) != null;
    }
}

public class UnitModelRenderer : MonoBehaviour
{
    public static UnitModelRenderer Instance;

    // One unit's model plus the motion state that drives it.
    class Model
    {
        public GameObject go;
        public UnitModelLibrary.Entry entry;
        public float radius;    // orbit distance from the host body's centre
        public float speed;     // orbital degrees/sec
        public float phase;     // current orbital angle
        public float height;    // vertical offset of the orbital ring
        public float bob;       // free-flyer idle bob phase
        public bool animated;   // the FBX brought its own clip, so don't add procedural motion
    }

    readonly Dictionary<Unit, Model> models = new Dictionary<Unit, Model>();

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("UnitModelRenderer");
        go.AddComponent<UnitModelRenderer>();
        // De-render the hulls once the camera pulls back to the galaxy overview. Visuals only — the ships
        // carry on with whatever they were doing. See MapTierVisibility.
        go.AddComponent<MapTierVisibility>();
    }

    void Awake() { Instance = this; }
    void OnEnable() { if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged += Rebuild; }
    void OnDisable() { if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged -= Rebuild; }
    void Start() { Rebuild(); }

    /// The token renderer asks this so it never double-renders something that has a mesh.
    public static bool UsesModel(Unit u) => UnitModelLibrary.HasModel(u);

    /// Where this unit is drawn, or null if it isn't drawn here. See UnitVisuals.TransformOf — a ship is
    /// rendered by EITHER this or UnitTokenRenderer, so neither can answer the question alone.
    public Transform TransformOf(Unit u)
        => u != null && models.TryGetValue(u, out var m) && m != null && m.go != null ? m.go.transform : null;

    public void Rebuild()
    {
        var live = new HashSet<Unit>();
        if (UnitManager.Instance != null)
            foreach (var u in UnitManager.Instance.Units) if (UsesModel(u)) live.Add(u);

        var stale = new List<Unit>();
        foreach (var kv in models) if (!live.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (var u in stale) { if (models[u]?.go != null) Destroy(models[u].go); models.Remove(u); }

        foreach (var u in live) if (!models.ContainsKey(u)) models[u] = Build(u);

        // Re-assert concealment on the freshly built meshes — see the matching note in
        // UnitTokenRenderer.Rebuild.
        foreach (var kv in models) VisibilityService.Apply(kv.Key);
    }

    Model Build(Unit u)
    {
        var entry = UnitModelLibrary.For(u.type);
        var prefab = UnitModelLibrary.Prefab(entry.path);
        if (prefab == null) return null;

        var go = Instantiate(prefab, transform);
        go.name = "Model_" + u.name;

        // Apply the hull's orientation correction up front, facing forward by default. Without this a
        // ship that hasn't yet travelled or parked (just spawned, or idling with no course and no dock)
        // keeps the raw import rotation instead — TickShip only re-applies modelRotation once it has a
        // real heading to combine it with.
        if (entry.motion == UnitModelLibrary.Motion.Freeflying)
            go.transform.rotation = entry.modelRotation;

        // Normalise whatever scale the artist authored at, so a model never has to be built to a
        // particular size to look right here.
        FitTo(go, entry.size);

        // Tint toward the owner's colour so allegiance reads at a glance, matching the token emblem.
        var tint = FactionManager.OwnerColor(u.owner);
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
            foreach (var mat in mr.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.Lerp(Color.white, tint, 0.3f));
                else if (mat.HasProperty("_Color")) mat.color = Color.Lerp(Color.white, tint, 0.3f);
            }

        // If the FBX shipped with its own animation, let it play and don't fight it with procedural
        // motion. These models don't appear to have clips, but this costs nothing and means dropping in
        // an animated replacement Just Works.
        bool animated = false;
        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null) animated = true;
        var legacy = go.GetComponentInChildren<Animation>();
        if (legacy != null && legacy.clip != null) { legacy.wrapMode = WrapMode.Loop; legacy.Play(); animated = true; }

        // Clickable, exactly like a token.
        var bounds = WorldBounds(go);
        var box = go.AddComponent<BoxCollider>();
        box.center = go.transform.InverseTransformPoint(bounds.center);
        box.size = bounds.size / Mathf.Max(0.0001f, go.transform.lossyScale.x);
        go.AddComponent<UnitModelClick>().Init(u);

        // Seeded from the unit id so an orbit is stable across frames and reloads, and two stations at
        // one world never share a ring.
        var rng = new System.Random(u.id * 7919);
        return new Model
        {
            go = go,
            entry = entry,
            speed = 10f + (float)rng.NextDouble() * 14f,
            phase = (float)rng.NextDouble() * 360f,
            height = ((float)rng.NextDouble() - 0.5f) * 0.5f,
            bob = (float)rng.NextDouble() * 10f,
            animated = animated
        };
    }

    static void FitTo(GameObject go, float target)
    {
        var b = WorldBounds(go);
        float largest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (largest <= 0.0001f) return;
        go.transform.localScale *= target / largest;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    void LateUpdate()
    {
        var um = UnitManager.Instance;
        if (um == null) return;

        // Scaled deltaTime, so models speed up and slow down with the game clock exactly like the
        // planets' own orbits do.
        float dt = Time.deltaTime;

        foreach (var kv in models)
        {
            var u = kv.Key; var m = kv.Value;
            if (m?.go == null) continue;

            if (m.entry.motion == UnitModelLibrary.Motion.OrbitHost) TickStation(um, u, m, dt);
            else TickShip(um, u, m, dt);
        }
    }

    // A station: orbits whatever it's deployed at, exactly as that world orbits its star.
    void TickStation(UnitManager um, Unit u, Model m, float dt)
    {
        if (u.status == UnitStatus.Traveling)
        {
            m.go.transform.position = um.UnitPos(u) + Vector3.up * 0.6f;
        }
        else if (u.location != null && u.location.visualObject != null)
        {
            var host = u.location.visualObject.transform;
            float bodyRadius = host.lossyScale.x * 0.5f;
            m.radius = bodyRadius + 0.28f + m.entry.size * 0.5f;

            m.phase += m.speed * dt;
            if (m.phase > 360f) m.phase -= 360f;

            float rad = m.phase * Mathf.Deg2Rad;
            m.go.transform.position = host.position
                + new Vector3(Mathf.Cos(rad) * m.radius, m.height, Mathf.Sin(rad) * m.radius);
        }
        else
        {
            m.go.transform.position = um.UnitPos(u);   // parked in open space
        }

        if (!m.animated && m.entry.spin > 0f)
            m.go.transform.Rotate(Vector3.up, m.entry.spin * dt, Space.World);
    }

    // A ship: sits where the unit is, points along its course, and idles with a slow bob so it reads
    // as alive rather than as a prop.
    void TickShip(UnitManager um, Unit u, Model m, float dt)
    {
        Vector3 pos = um.UnitPos(u);

        if (u.status == UnitStatus.Traveling)
        {
            // Face the way it's actually flying (with the hull's own orientation correction on top).
            Vector3 dir = u.travelTo - u.travelFrom;
            if (dir.sqrMagnitude > 0.0001f)
                m.go.transform.rotation = Quaternion.Slerp(m.go.transform.rotation,
                    Quaternion.LookRotation(dir.normalized, Vector3.up) * m.entry.modelRotation, 3f * dt);
        }
        else if (u.location != null && u.location.visualObject != null)
        {
            // Parked at a world: stand off it a little so it isn't buried in the planet, and look at it.
            var host = u.location.visualObject.transform;
            float standoff = host.lossyScale.x * 0.5f + 0.4f;
            int idx = u.location.units != null ? Mathf.Max(0, u.location.units.IndexOf(u)) : 0;
            int count = u.location.units != null ? Mathf.Max(1, u.location.units.Count) : 1;
            float ang = idx * Mathf.PI * 2f / count;
            pos = host.position + new Vector3(Mathf.Cos(ang) * standoff, 0.35f, Mathf.Sin(ang) * standoff);
            m.go.transform.rotation = Quaternion.Slerp(m.go.transform.rotation,
                Quaternion.LookRotation((host.position - pos).normalized, Vector3.up) * m.entry.modelRotation, 2f * dt);
        }

        if (!m.animated)
        {
            m.bob += dt;
            pos += Vector3.up * Mathf.Sin(m.bob * 1.4f) * 0.05f;   // gentle idle bob
        }
        m.go.transform.position = pos;
    }
}

// Click a model to select its unit, matching how ship tokens behave.
public class UnitModelClick : MonoBehaviour
{
    Unit unit;
    public void Init(Unit u) { unit = u; }

    void OnMouseDown()
    {
        if (unit == null) return;
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        UnitSelection.Select(unit, add);
        SimpleAudio.Instance?.PlayUnitSelect(unit.type);
        UnitInfoPanel.Instance?.Show(unit);
    }
}
