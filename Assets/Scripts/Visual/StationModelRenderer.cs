using System.Collections.Generic;
using UnityEngine;

// Real 3D models for deployed space stations, replacing their flat billboard token.
//
// A station is infrastructure — it sits somewhere and stays there — so unlike a ship it's worth
// rendering as an actual object in the world rather than an icon facing the camera. Ships keep their
// tokens (see UnitTokenRenderer); only stations get a mesh.
//
// The model is loaded from Resources at runtime, exactly like the optional CC0 planet detail maps in
// AssetIntegration: if it isn't there, the game silently keeps the billboard token. That matters here
// because Unity can only import a .blend when BLENDER IS INSTALLED on the machine doing the importing
// — Unity shells out to Blender to convert it to FBX. On a machine without Blender the asset simply
// won't exist, Load returns null, and the game carries on with tokens rather than throwing.
public static class StationModel
{
    // Resources path (no extension). Any importable model at this path is used.
    public const string Path = "SpaceAssets/Stations/LP Space Station";

    static GameObject cached;
    static bool tried;

    /// The station prefab/model, or null if it isn't available (e.g. Blender isn't installed, so the
    /// .blend never imported). Callers must handle null.
    public static GameObject Prefab
    {
        get
        {
            if (tried) return cached;
            tried = true;
            cached = Resources.Load<GameObject>(Path);
            if (cached == null)
                Debug.Log($"[StationModel] No station model at Resources/{Path} — stations will use billboard tokens. " +
                          "If you expected a model: Unity can only import a .blend when Blender is installed on this machine; " +
                          "otherwise export the model to .fbx and drop that in beside it.");
            return cached;
        }
    }

    public static bool Available => Prefab != null;
}

public class StationModelRenderer : MonoBehaviour
{
    public static StationModelRenderer Instance;

    // One station's own orbit around whatever it's stationed at. Each gets a distinct phase, radius and
    // speed so several stations at the same world spread out into separate rings instead of stacking.
    class Orbit
    {
        public GameObject go;
        public float radius;      // world units from the body's centre
        public float speed;       // degrees per second
        public float phase;       // current angle, degrees
        public float height;      // vertical offset, so rings don't all sit in one plane
        public float spin;        // own axial spin, deg/sec
    }

    readonly Dictionary<Unit, Orbit> models = new Dictionary<Unit, Orbit>();

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("StationModelRenderer").AddComponent<StationModelRenderer>();
    }

    void Awake() { Instance = this; }

    void OnEnable() { if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged += Rebuild; }
    void OnDisable() { if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged -= Rebuild; }
    void Start() { Rebuild(); }

    /// Does this unit get a mesh instead of a token?
    public static bool UsesModel(Unit u) => u != null && u.Info.isStation && StationModel.Available;

    public void Rebuild()
    {
        if (!StationModel.Available) return;

        // Drop models for units that are gone.
        var live = new HashSet<Unit>();
        if (UnitManager.Instance != null)
            foreach (var u in UnitManager.Instance.Units) if (UsesModel(u)) live.Add(u);

        var stale = new List<Unit>();
        foreach (var kv in models) if (!live.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (var u in stale) { if (models[u]?.go != null) Destroy(models[u].go); models.Remove(u); }

        foreach (var u in live) if (!models.ContainsKey(u)) models[u] = Build(u);
    }

    // Station size, in world units, measured against the planets it has to sit next to.
    // SystemVisualizer scales a planet to surfaceSize * 0.08 (min 0.6) and a moon to * 0.05 (min 0.35),
    // so worlds are only ~0.6-2.2 units across. A station therefore has to be SMALL — a few tenths of a
    // unit — or it dwarfs the planet it's orbiting. Tier still matters: a mega-station is roughly moon
    // sized, which is exactly what its description promises.
    static float SizeFor(Unit u) => 0.16f + Mathf.Clamp(u.Info.stationLevel, 1, 3) * 0.07f;

    Orbit Build(Unit u)
    {
        var go = Instantiate(StationModel.Prefab, transform);
        go.name = "Station_" + u.name;

        // Normalize whatever scale the model was authored at: fit its largest dimension to our target so
        // the model doesn't need to have been built to any particular scale.
        FitTo(go, SizeFor(u));

        // Tint by owner so allegiance is readable at a glance, matching the token emblem.
        var tint = FactionManager.OwnerColor(u.owner);
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
        {
            foreach (var mat in mr.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.Lerp(Color.white, tint, 0.35f));
                else if (mat.HasProperty("_Color")) mat.color = Color.Lerp(Color.white, tint, 0.35f);
            }
        }

        // Clickable, like the tokens: reuse the same selection component so stations behave identically.
        var bounds = WorldBounds(go);
        var box = go.AddComponent<BoxCollider>();
        box.center = go.transform.InverseTransformPoint(bounds.center);
        box.size = bounds.size / Mathf.Max(0.0001f, go.transform.lossyScale.x);

        go.AddComponent<StationModelClick>().Init(u);

        // Give each station its own stable orbit. Seeded from the unit id so it never changes between
        // frames (or reloads) and two stations at the same world never share a ring.
        var rng = new System.Random(u.id * 7919);
        return new Orbit
        {
            go = go,
            radius = 0f,                                        // resolved per frame from the body's size
            speed = 10f + (float)rng.NextDouble() * 14f,        // deg/sec, leisurely
            phase = (float)rng.NextDouble() * 360f,
            height = ((float)rng.NextDouble() - 0.5f) * 0.5f,
            spin = 12f + (float)rng.NextDouble() * 18f
        };
    }

    // Scale a model so its largest dimension equals `target` world units.
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
        if (!StationModel.Available) return;
        var um = UnitManager.Instance;
        if (um == null) return;

        // Time.deltaTime is already scaled by the game speed, so stations slow down and speed up with
        // everything else — the same rule the planets' own orbits follow.
        float dt = Time.deltaTime;

        foreach (var kv in models)
        {
            var u = kv.Key; var o = kv.Value;
            if (o?.go == null) continue;

            if (u.status == UnitStatus.Traveling)
            {
                // Under way: fly the intercept line like any other ship, no orbiting.
                o.go.transform.position = um.UnitPos(u) + Vector3.up * 0.6f;
            }
            else if (u.location != null && u.location.visualObject != null)
            {
                // DEPLOYED AT A WORLD: actually orbit it, exactly as the world orbits its star. The ring
                // sits just clear of the surface and tracks the body as it moves along its own orbit.
                var host = u.location.visualObject.transform;
                float bodyRadius = host.lossyScale.x * 0.5f;
                o.radius = bodyRadius + 0.28f + SizeFor(u) * 0.5f;

                o.phase += o.speed * dt;
                if (o.phase > 360f) o.phase -= 360f;

                float rad = o.phase * Mathf.Deg2Rad;
                o.go.transform.position = host.position
                    + new Vector3(Mathf.Cos(rad) * o.radius, o.height, Mathf.Sin(rad) * o.radius);
            }
            else
            {
                // Parked in open space (deep-space stations): hold position and just turn.
                o.go.transform.position = um.UnitPos(u);
            }

            // A slow axial spin so a deployed station reads as powered rather than as scenery — the same
            // idle motion the planets have.
            o.go.transform.Rotate(Vector3.up, o.spin * dt, Space.World);
        }
    }
}

// Click a station's mesh to select it and open its Inspector, matching how ship tokens behave.
public class StationModelClick : MonoBehaviour
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
    }
}
