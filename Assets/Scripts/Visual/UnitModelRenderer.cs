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
        //
        // Left at identity here means "work it out": either the artist has written a line in
        // ship-meshes.txt, or the bounds heuristic will guess. See UnitModelLibrary.Resolve.
        public Quaternion modelRotation = Quaternion.identity;

        /// Has Resolve() already settled this entry's rotation, scale and spin? Guards the lazy pass so
        /// the bounds heuristic runs once per entry rather than once per unit per frame.
        [System.NonSerialized] public bool oriented;

        // What Build() authored, before the manifest was applied on top. Kept so a reload starts from
        // the original values rather than compounding the manifest's scale on every pass.
        [System.NonSerialized] public bool baseCaptured;
        [System.NonSerialized] public float baseSize;
        [System.NonSerialized] public float baseSpin;
        [System.NonSerialized] public Quaternion baseRotation;
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

        // ============================================================================================
        // EVERY OTHER HULL, ON A BORROWED MESH
        //
        // There are three meshes in the project and twenty-odd classes. Until each has art of its own,
        // a hull is better served by a real ship at the wrong silhouette than by a flat billboard: it
        // sits in space properly, it turns to face its course, it catches the star's light, and it reads
        // as a VESSEL next to the stations and colony ships that already do.
        //
        // WHAT MAKES THEM TELLABLE APART IS THE BADGE, not the mesh (see UnitModelRenderer's class
        // marker). Three shared hulls with no marking would be worse than billboards — this is the trade
        // only because every modelled ship now carries its class symbol above it.
        //
        // Sized by role rather than by tier alone: a scout is small and a dreadnought is the largest
        // thing under way, so the fleet reads at a glance even before the badges resolve.
        void Ship(UnitType t, float size, string path, Quaternion rot)
            => map[t] = new Entry { path = path, size = size, motion = Motion.Freeflying, modelRotation = rot };

        const string colonyPath = "SpaceAssets/Ships/LP Colony Ship";
        var colRot = Quaternion.Euler(-90f, 0f, 0f);

        // Fast, light hulls — the science frame is the slimmer of the two.
        Ship(UnitType.Scout, 0.14f, sciencePath, sciRot);
        Ship(UnitType.ScoutII, 0.16f, sciencePath, sciRot);
        Ship(UnitType.ScoutIII, 0.18f, sciencePath, sciRot);
        Ship(UnitType.Explorer, 0.24f, sciencePath, sciRot);
        Ship(UnitType.Probe, 0.09f, sciencePath, sciRot);

        // Combat, escalating. Nothing here is a fighter-shaped mesh yet; size is doing the work.
        Ship(UnitType.Fighter, 0.13f, sciencePath, sciRot);
        Ship(UnitType.FighterII, 0.15f, sciencePath, sciRot);
        Ship(UnitType.FighterIII, 0.17f, sciencePath, sciRot);
        Ship(UnitType.Frigate, 0.20f, colonyPath, colRot);
        Ship(UnitType.Cruiser, 0.26f, colonyPath, colRot);
        Ship(UnitType.Carrier, 0.32f, colonyPath, colRot);
        Ship(UnitType.Dreadnought, 0.40f, colonyPath, colRot);

        // Bulk hulls — the colony frame is the heavier one, which suits them.
        Ship(UnitType.Miner, 0.22f, colonyPath, colRot);
        Ship(UnitType.Transport, 0.26f, colonyPath, colRot);
        Ship(UnitType.Terraformer, 0.30f, colonyPath, colRot);
    }

    // ============================================================================================
    // WHICH CIVILIZATION'S ART A SHIP FLIES IN
    //
    // Every hull now has art per civilization — an Aquarii scout is a shrimp, a Terran one is a
    // spyplane — so the mesh a unit uses depends on WHO OWNS IT, not just what class it is.
    //
    // Units carry a Faction, and species is a separate, global thing (SpeciesManager.Current is the
    // species the PLAYER picked). There is no per-unit species field and this does not invent one:
    //
    //   * the player's ships fly the species the player chose, which is the whole point of choosing;
    //   * every other faction is mapped to a species by its id, so a given empire always looks like
    //     itself from one session to the next rather than shuffling.
    //
    // FALLBACK IS THE POINT. Resources.Load returns null for art that is not there yet, and this then
    // hands back the shared mesh the class used before. A civilization whose fleet has not been
    // generated still flies — on borrowed hulls, exactly as it did — so the art can land one
    // civilization at a time without a broken build in between.
    // ============================================================================================

    /// Folder names under SpaceAssets, in SpeciesDatabase order.
    static readonly string[] CivFolders = { "Terran", "Aquarii", "Pyrothian", "Cryithn", "Sylvan" };

    static string CivFolderFor(Unit u)
    {
        int idx = (u?.owner == null || u.owner == FactionManager.Player)
            ? SpeciesManager.CurrentIndex
            : Mathf.Abs(u.owner.id) % CivFolders.Length;
        return CivFolders[Mathf.Clamp(idx, 0, CivFolders.Length - 1)];
    }

    /// The civ-specific Resources path for this unit, or null if that art does not exist.
    ///
    /// Stations live under Stations/ and hulls under Ships/, matching where the importer writes them;
    /// both are tried because `isStation` is the only thing that distinguishes them and it is cheap to
    /// ask. The result is cached by Prefab(), so the miss on a civ with no art costs one failed
    /// Resources.Load per class, once.
    public static string CivPath(Unit u)
    {
        if (u == null) return null;
        string civ = CivFolderFor(u);
        string file = $"{civ}_{u.type}";
        var info = UnitDatabase.Get(u.type);
        string folder = (info != null && info.isStation) ? "Stations" : "Ships";
        string path = $"SpaceAssets/{folder}/{civ}/{file}";
        return Prefab(path) != null ? path : null;
    }

    public static Entry For(UnitType t)
    {
        if (!built) Build();
        if (!map.TryGetValue(t, out var e) || e == null) return null;
        Resolve(e);
        return e;
    }

    // ============================================================================================
    // ORIENTATION IS RESOLVED ONCE, LAZILY, AND FROM DATA
    //
    // The `modelRotation` values written into Build() above are hand-found constants for the three
    // meshes that shipped with the project. They are still honoured — a hand-found value is a correct
    // value — but they are no longer the only way to get one, and they cannot be the way when there are
    // a hundred and forty-five meshes to import.
    //
    // The order of authority, most trusted first:
    //
    //   1. `ship-meshes.txt`. An artist wrote it, while looking at the ship, without a recompile. If it
    //      says a hull is rotated ninety degrees, it is.
    //   2. A non-identity `modelRotation` in Build(). The hand-found legacy constants.
    //   3. The bounds heuristic (ShipMeshManifest.AutoOrient), which is right for most conventional
    //      hulls and says out loud what it decided so a wrong guess is one pasted line from fixed.
    //
    // Resolved once per entry and cached, because AutoOrient walks every renderer on the prefab and the
    // answer cannot change while the prefab is loaded.
    /// The class's entry, re-pointed at a civilization-specific mesh and re-oriented for it.
    ///
    /// Size, motion and spin come from the CLASS — a dreadnought is drawn large and a probe small
    /// whoever built it, and that scale ladder is how the fleet reads at a glance. Only the mesh and
    /// its orientation are per-civilization, because a shrimp and the borrowed hull it replaces do not
    /// point the same way and must not share a rotation.
    ///
    /// Cached per path so the manifest lookup and the bounds heuristic run once per civ+class rather
    /// than once per ship — a fleet of forty scouts resolves one entry between them.
    static readonly Dictionary<string, Entry> civEntries = new Dictionary<string, Entry>();

    public static Entry EntryForPath(Entry classEntry, string path)
    {
        if (string.IsNullOrEmpty(path)) return classEntry;
        if (civEntries.TryGetValue(path, out var cached)) return cached;

        var e = new Entry
        {
            path = path,
            size = classEntry.size,
            motion = classEntry.motion,
            spin = classEntry.spin,
            // Identity, NOT the class's rotation: the class value was hand-found for the borrowed
            // mesh, and carrying it over would tell Resolve the new hull is already correct and skip
            // the manifest and the heuristic entirely.
            modelRotation = Quaternion.identity,
        };
        Resolve(e);
        civEntries[path] = e;
        return e;
    }

    static void Resolve(Entry e)
    {
        if (e.oriented) return;
        e.oriented = true;

        // The FIRST resolve records what Build() authored, and every later one starts from that record
        // rather than from the current values. Without this, ReloadOrientations would re-apply the
        // manifest's scale multiplier to an already-multiplied size and a ship would grow by 15% every
        // time the artist reloaded to check their work — which is exactly the kind of bug that looks
        // like the manifest is broken when the manifest is fine.
        if (!e.baseCaptured)
        {
            e.baseSize = e.size;
            e.baseSpin = e.spin;
            e.baseRotation = e.modelRotation;
            e.baseCaptured = true;
        }
        e.size = e.baseSize;
        e.spin = e.baseSpin;
        e.modelRotation = e.baseRotation;

        var authored = ShipMeshManifest.Authored(e.path);
        if (authored != null)
        {
            e.modelRotation = authored.rotation;
            e.size = e.baseSize * authored.scale;
            if (authored.forceSpin && e.spin <= 0f) e.spin = 12f;
            if (authored.forceNoSpin) e.spin = 0f;
            return;
        }

        // A hand-found constant from Build() counts as authored — do not second-guess it.
        if (e.modelRotation != Quaternion.identity) return;

        var prefab = Prefab(e.path);
        if (prefab == null) return;                 // no art; the class falls back to its billboard
        e.modelRotation = ShipMeshManifest.AutoOrient(prefab, ShipMeshManifest.LeafName(e.path), out _);
    }

    /// Re-read the orientation manifest and re-resolve every entry. For the Dev reload: fix a sideways
    /// ship in a text file, hit reload, see it corrected — which is the entire reason the manifest is a
    /// file rather than a table in this class.
    public static void ReloadOrientations()
    {
        ShipMeshManifest.Reload();
        // The per-civilization entries are cached by path and hold their own resolved rotation, so
        // clearing them is what actually makes F10 re-read the manifest for the generated fleet.
        // Leaving them would reload the file and change nothing visible — the failure mode that makes
        // an artist think the manifest is broken when it is fine.
        civEntries.Clear();
        if (!built) { Build(); return; }
        foreach (var e in map.Values) if (e != null) e.oriented = false;
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
    /// Is this unit drawn as a mesh, or as a flat billboard token?
    ///
    /// ASKS ABOUT THE UNIT'S OWN CIVILIZATION FIRST. Checking only the class entry was correct while
    /// every class shared one of three meshes, and became a latent bug the moment art went per-civ: a
    /// class whose shared fallback was missing would report "no model" and draw a token even though
    /// this particular ship's civilization had art sitting right there. It happens to be harmless
    /// today only because all three legacy meshes still exist — which is a coincidence, not a design.
    public static bool HasModel(Unit u)
    {
        if (u == null) return false;
        if (CivPath(u) != null) return true;
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
        public ShipLights lights;   // running lights, engines and muzzle flash; cached, not searched for
        public float bank;      // current roll into a turn, eased rather than tracked exactly
    }

    readonly Dictionary<Unit, Model> models = new Dictionary<Unit, Model>();

    /// F10 — re-read ship-meshes.txt and rebuild every model in place.
    ///
    /// This key is the reason the manifest is a text file at all. The import loop it exists for is:
    /// generate a mesh, drop it in, look at it flying sideways, type one line, press F10, watch it snap
    /// upright. Without a way to trigger a reload the file's promise — fix it without a recompile — is
    /// not true, and an artist would be restarting the game a hundred and forty-five times.
    const KeyCode ReloadOrientationsKey = KeyCode.F10;

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

    /// This unit's light rig, or null if it is drawn as a token rather than a mesh. Used by the
    /// projectile renderer to flash a muzzle on the frame a round is created — see ShipLights.
    public ShipLights LightsOf(Unit u)
        => u != null && models.TryGetValue(u, out var m) && m != null ? m.lights : null;

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

    /// How far above the hull the badge floats, as a multiple of the hull's own size, and how big it is
    /// relative to the hull. Both in the model's own space so a dreadnought's badge sits proportionally
    /// where a scout's does.
    const float BadgeLift = 1.15f, BadgeScale = 1.35f;

    /// Whether a modelled ship carries its class icon on a stick above it.
    ///
    /// OFF for the shipping game: the badges were a stand-in for silhouettes that did not exist yet,
    /// and now that each hull has its own art they are clutter between the camera and the model.
    /// Turn it on to tell apart hulls that are still sharing a borrowed mesh.
    public static bool ShowClassBadges = false;

    /// The badges, kept so LateUpdate can turn them to face the camera without a GetComponentInChildren
    /// per ship per frame.
    readonly List<Transform> badges = new List<Transform>();

    void BuildBadge(GameObject host, Unit u, float size)
    {
        // A holder that is NOT scaled by the hull's fit, so the badge keeps its size whatever the mesh
        // was authored at — and so billboarding it cannot inherit a squashed scale from the model.
        var holder = new GameObject("ClassBadge");
        holder.transform.SetParent(host.transform, false);
        holder.transform.localPosition = Vector3.up * (BadgeLift);
        // Undo the host's normalisation so the badge is sized in WORLD terms, not in hull terms.
        float lossy = Mathf.Max(0.0001f, host.transform.lossyScale.x);
        holder.transform.localScale = Vector3.one * (size * BadgeScale / lossy);

        // Owner colour behind, class shape in front — the same two-layer reading a token gives.
        Quad(holder.transform, UnitIconRenderer.Get(u.type), Color.white, Vector3.zero, 1f);

        badges.Add(holder.transform);
    }

    static GameObject Quad(Transform parent, Texture2D tex, Color tint, Vector3 localPos, float scale)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = q.GetComponent<Collider>();
        if (col != null) Destroy(col);          // the hull owns the click; the badge must not steal it
        q.transform.SetParent(parent, false);
        q.transform.localPosition = localPos;
        q.transform.localScale = Vector3.one * scale;
        var mr = q.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = tint };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return q;
    }

    /// Turn every badge to face the camera. Done here rather than with a per-badge Billboard component
    /// because it is one transform write per ship and a component would be an Update call per ship.
    void TickBadges()
    {
        // Nothing is built when the flag is off, so the list is empty and this is a wasted Camera.main
        // every frame. Cheap, but Camera.main is a lookup and this runs in LateUpdate.
        if (badges.Count == 0) return;

        var cam = Camera.main;
        if (cam == null) return;

        var rot = cam.transform.rotation;
        for (int i = badges.Count - 1; i >= 0; i--)
        {
            if (badges[i] == null) { badges.RemoveAt(i); continue; }   // its ship was destroyed
            badges[i].rotation = rot;
        }
    }

    Model Build(Unit u)
    {
        var entry = UnitModelLibrary.For(u.type);

        // This unit's OWN civilization's art, if it has been generated; otherwise the shared mesh the
        // class has always used. See UnitModelLibrary.CivPath — a civ with no art yet still flies.
        //
        // The orientation is resolved against whichever mesh actually loaded, not against the class
        // entry, because a shrimp and the borrowed hull it falls back to do not point the same way.
        string civPath = UnitModelLibrary.CivPath(u);
        var prefab = UnitModelLibrary.Prefab(civPath ?? entry.path);
        if (civPath != null) entry = UnitModelLibrary.EntryForPath(entry, civPath);
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

        // FLOORED, the same way a body's pick sphere is (SystemVisualizer.EnsureClickCollider).
        //
        // FitTo has already normalised the hull to 0.16-0.34 world units, so its true bounds give a pick
        // box a few PIXELS across at system zoom — against a body's pick sphere that never drops below
        // 1.5 units and grows to 13. Without a floor, a meshed ship docked at a world is essentially
        // unhittable, which is the exact case ClickPriority exists to rescue: it can only hand the click
        // over if the ray pierces this box in the first place.
        const float MinPickWorld = 0.8f;
        float lossy = Mathf.Max(0.0001f, go.transform.lossyScale.x);
        box.size = Vector3.Max(box.size, Vector3.one * (MinPickWorld / lossy));

        go.AddComponent<UnitModelClick>().Init(u);

        // ============================================================================================
        // THE CLASS BADGE — OFF, now that hulls have their own art
        //
        // The badge existed for one reason: there were three meshes and twenty-odd classes, so the
        // silhouette could not tell you what you were looking at and every modelled ship had to wear
        // its class icon on a stick to be identifiable at all.
        //
        // That reason is going away. Each civilization and hull now gets art of its own — a scout is a
        // shrimp and a dreadnought is a leviathan — and a silhouette that identifies itself makes the
        // floating symbol pure clutter sitting between the camera and the ship you paid for.
        //
        // Kept behind a flag rather than deleted, because the fallback is still real: any hull with no
        // art yet is still drawn on a borrowed mesh, and switching this back on is how you tell those
        // apart while the fleet is being finished. Unmodelled units are unaffected either way — they
        // are drawn by UnitTokenRenderer, whose whole job is the icon.
        if (ShowClassBadges) BuildBadge(go, u, entry.size);

        // ============================================================================================
        // RUNNING LIGHTS AND ENGINES
        //
        // Placed from the hull's own bounds rather than authored per model — there are a hundred and
        // forty of these and no rig could be hand-placed on each. ShipLights needs `modelRotation` to
        // recover ship space (bow +Z) from the root, which is already carrying that correction; see
        // its header for why that matters and what goes wrong without it.
        var lights = go.AddComponent<ShipLights>();
        lights.Init(u, entry.modelRotation, tint);

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
            animated = animated,
            lights = lights
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

    void Update()
    {
        // F10 — re-read the orientation manifest and rebuild. See ReloadOrientationsKey.
        if (Input.GetKeyDown(ReloadOrientationsKey))
        {
            UnitModelLibrary.ReloadOrientations();
            // Drop every built model so the next Rebuild picks the new rotations up. Cheap: there are
            // tens of these, not thousands, and this only happens when a key is pressed.
            foreach (var kv in models) if (kv.Value?.go != null) Destroy(kv.Value.go);
            models.Clear();
            badges.Clear();
            Rebuild();
            NotificationManager.Instance?.Push("Ship orientations reloaded",
                "Re-read ship-meshes.txt and rebuilt every model.", null, NotifKind.Info);
        }
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

        // AFTER the hulls have moved and turned, so a badge is never a frame behind the ship it belongs
        // to — at system zoom a badge lagging its hull reads as two separate objects.
        TickBadges();
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
            // ============================================================================================
            // FLYING, RATHER THAN SLIDING
            //
            // A ship used to snap toward its heading at a flat rate and hold a dead-level attitude the
            // whole way. Two things were missing and both are what the eye reads as "piloted":
            //
            // BANK INTO THE TURN. Anything that changes heading rolls into it — aircraft because they
            // have to, spacecraft in fiction because we expect it. The roll is taken from how far the
            // hull still has to turn, so it leans hardest at the start of a course change and levels
            // out as it settles. Without it a ship pivots like a compass needle: the nose comes round
            // and the body never acknowledges it.
            //
            // TURN HARDER WHEN THE TURN IS BIGGER. A fixed slerp rate takes the same time to correct a
            // two-degree drift as to swing through ninety, so departures looked sluggish and tiny
            // corrections looked twitchy. Rate now scales with the angle left to cover.
            //
            // Position easing lives in UnitManager.FlightEase — burn, coast, brake — so the ship is
            // already accelerating out and braking in. This is the attitude half of the same idea.
            Vector3 dir = u.travelTo - u.travelFrom;

            // Stand off the fleet's shared course line, so a flight of eight reads as eight ships
            // rather than as one that got heavier. Drawing only — see FleetFormation.
            pos += FleetFormation.Offset(u, dir, u.TravelProgress);

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);

                // How far off course the hull still is, measured BEFORE the correction is applied and
                // in the game's frame — so the hull's own orientation offset does not pollute it.
                Quaternion current = m.go.transform.rotation * Quaternion.Inverse(m.entry.modelRotation);
                float off = Quaternion.Angle(current, want);

                // Roll proportional to the turn still to make, capped so a ship never flies inverted.
                const float MaxBank = 32f;
                float bank = Mathf.Clamp(off, 0f, 90f) / 90f * MaxBank;

                // Which way to lean: positive when the target heading lies to starboard.
                Vector3 local = Quaternion.Inverse(want) * (current * Vector3.forward);
                if (local.x > 0f) bank = -bank;

                // Ease the roll in and out rather than tracking `off` exactly, so a ship settles level
                // instead of twitching as the angle noise crosses zero.
                m.bank = Mathf.MoveTowards(m.bank, bank, 90f * dt);

                Quaternion target = want * Quaternion.AngleAxis(m.bank, Vector3.forward) * m.entry.modelRotation;
                float rate = Mathf.Lerp(1.6f, 6f, Mathf.Clamp01(off / 90f));
                m.go.transform.rotation = Quaternion.Slerp(m.go.transform.rotation, target, rate * dt);
            }
        }
        else if (u.location != null && u.location.visualObject != null)
        {
            // Parked at a world: stand off it a little so it isn't buried in the planet, and look at it.
            var host = u.location.visualObject.transform;
            float standoff = host.lossyScale.x * 0.5f + 0.4f;
            int idx = u.location.units != null ? Mathf.Max(0, u.location.units.IndexOf(u)) : 0;
            int count = u.location.units != null ? Mathf.Max(1, u.location.units.Count) : 1;

            // The anchorage GROWS with the number of ships in it — see FleetFormation.AnchorOffset.
            // A fixed-radius ring divided by the count packs twenty ships a tenth of a unit apart, and
            // hulls are up to 0.40 across, so a well-defended world drew as one solid ring of
            // interpenetrating geometry instead of as a fleet standing off it.
            pos = host.position + Vector3.up * 0.35f
                + FleetFormation.AnchorOffset(idx, count, standoff, 0.34f);
            m.go.transform.rotation = Quaternion.Slerp(m.go.transform.rotation,
                Quaternion.LookRotation((host.position - pos).normalized, Vector3.up) * m.entry.modelRotation, 2f * dt);
        }

        if (!m.animated)
        {
            m.bob += dt;
            pos += Vector3.up * Mathf.Sin(m.bob * 1.4f) * 0.05f;   // gentle idle bob
        }
        m.go.transform.position = pos;

        // ---- how hard the engines are burning -------------------------------------------------
        //
        // Two things decide it. WHAT CLASS THIS IS — a Scout Mk III at speed 15 should visibly out-burn
        // a colony ship at 3, which is what "brighter the faster it goes" means when the sim moves
        // everything on a straight lerp — and WHERE IT IS IN THE TRIP.
        //
        // The trip ramp is what stops the plumes being a boolean. Engines light over the first tenth of
        // the journey and back off over the last fifth, so a departure looks like a departure and an
        // arrival looks like braking. Parked hulls keep a dim station-keeping glow rather than going
        // fully dark, because a completely unlit engine bell reads as a dead ship.
        if (m.lights != null)
        {
            const float Idle = 0.06f;
            float throttle = Idle;

            if (u.status == UnitStatus.Traveling)
            {
                // Info.speed spans roughly 3..15 across the whole roster (UnitDatabase).
                float classK = Mathf.InverseLerp(3f, 15f, u.Info.speed);
                float p = u.TravelProgress;
                float spool = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.10f));
                float brake = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - p) / 0.20f));
                throttle = Mathf.Lerp(0.35f, 1f, classK) * Mathf.Min(spool, brake);
            }

            m.lights.SetThrottle(Mathf.Max(Idle, throttle));
        }
    }
}

// Click a model to select its unit, matching how ship tokens behave.
public class UnitModelClick : MonoBehaviour
{
    Unit unit;
    public void Init(Unit u) { unit = u; }

    /// The ship this hull stands for, so a raycast can identify what it hit (see ClickPriority).
    public Unit Unit => unit;

    void OnMouseDown() => HandleClick();

    /// See UnitToken.HandleClick — a docked ship loses the nearest-hit test to its world's oversized
    /// pick sphere, so the body's handler forwards the click here.
    /// Returns whether the click was actually consumed — see ClickPriority.
    public bool HandleClick()
    {
        if (unit == null) return false;
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return false;
        if (FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting) return false;

        bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        UnitSelection.Select(unit, add);
        SimpleAudio.Instance?.PlayUnitSelect(unit.type);
        // Same follow behaviour as a token — a ship with a real hull should not behave differently from
        // one drawn as a billboard just because its class happens to have a model.
        CameraController.Instance?.FocusUnit(unit, CameraController.AutoFollow);
        UnitInfoPanel.Instance?.Show(unit);
        return true;
    }
}
