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

        // ============================================================================================
        // HOW BIG EVERYTHING IS DRAWN
        //
        // The scale these numbers live on is set by the worlds they sit next to: OrbitSafety draws a
        // MOON at surfaceSize * 0.05 with a floor of 0.35, and a PLANET at * 0.08 with a floor of 0.6.
        // So a world is 0.35 to 2.2 units across, and anything here has to be a few tenths or it
        // dwarfs the thing it is orbiting.
        //
        // Ship sizes are COMPRESSED, deliberately and heavily. A fighter really is about a
        // thousandth of a battleship's length, and a fighter drawn to that scale beside one is a
        // sub-pixel smudge that no player could see, let alone click. What the sizes preserve instead
        // is the ORDER and the sense of class: a fighter is unmistakably small, a dreadnought is the
        // biggest thing under way, and the gap between them is legible at system zoom.
        //
        // THE STATIONS USED TO BE COMPUTED FROM stationLevel, and it produced a straightforward lie:
        // only the Mega-Station carries level 3, so every other station came out at the level-1 size
        // of 0.23 — and the Mega-Station itself landed at 0.37, SMALLER THAN THE DREADNOUGHT at 0.40.
        // A thing whose own description is "an orbital city the size of a small moon", and which costs
        // two and a half times what a battleship costs, was being drawn as the smaller of the two.
        //
        // They are per-class now. The Mega-Station is 0.52, which really is a small moon on this scale
        // (a moon of middling surfaceSize draws at about 0.5), and it is comfortably the largest thing
        // any civilization fields.
        void Station(UnitType t, float size)
        {
            map[t] = new Entry
            {
                path = "SpaceAssets/Stations/LP Space Station",
                size = size,
                motion = Motion.OrbitHost,
                spin = 14f
            };
        }

        Station(UnitType.RelayStation, 0.24f);      // a mast and a dish
        Station(UnitType.SupplyStation, 0.26f);     // tanks and drums round a spine
        Station(UnitType.ResearchStation, 0.28f);
        Station(UnitType.BattleStation, 0.30f);
        Station(UnitType.DeepSpaceStation, 0.30f);
        Station(UnitType.TerraformStation, 0.36f);  // a processor ring, and they are not small
        Station(UnitType.MultiStation, 0.38f);
        Station(UnitType.HyperRelay, 0.44f);        // a gate a fleet flies through
        Station(UnitType.MegaStation, 0.52f);       // the little moon its description promises

        // Anything flagged isStation that the list above missed still gets drawn rather than skipped —
        // a new station class should appear at a sane size on the day it is added, not vanish until
        // somebody remembers this table.
        foreach (var info in UnitDatabase.All)
            if (info != null && info.isStation && !map.ContainsKey(info.type))
                Station(info.type, 0.30f);

        // The colony ship — the one hull big and characterful enough to be worth a mesh. It's also the
        // ship you watch most closely, since it's what founds a world.
        map[UnitType.ColonyShip] = new Entry
        {
            path = "SpaceAssets/Ships/LP Colony Ship",
            size = 0.33f,
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
        map[UnitType.ResearchShip] = new Entry { path = sciencePath, size = 0.20f, motion = Motion.Freeflying, modelRotation = sciRot };
        map[UnitType.ResearchShipII] = new Entry { path = sciencePath, size = 0.23f, motion = Motion.Freeflying, modelRotation = sciRot };
        map[UnitType.ResearchShipIII] = new Entry { path = sciencePath, size = 0.26f, motion = Motion.Freeflying, modelRotation = sciRot };
        // The Science Vessel is the top of that line — a dedicated deep-survey laboratory, and the
        // largest of them.
        map[UnitType.ScienceVessel] = new Entry { path = sciencePath, size = 0.30f, motion = Motion.Freeflying, modelRotation = sciRot };

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
        Ship(UnitType.Scout, 0.12f, sciencePath, sciRot);
        Ship(UnitType.ScoutII, 0.14f, sciencePath, sciRot);
        Ship(UnitType.ScoutIII, 0.16f, sciencePath, sciRot);
        Ship(UnitType.Explorer, 0.22f, sciencePath, sciRot);
        Ship(UnitType.Probe, 0.07f, sciencePath, sciRot);

        // Combat, escalating. Nothing here is a fighter-shaped mesh yet; size is doing the work.
        Ship(UnitType.Fighter, 0.11f, sciencePath, sciRot);
        Ship(UnitType.FighterII, 0.13f, sciencePath, sciRot);
        Ship(UnitType.FighterIII, 0.15f, sciencePath, sciRot);
        Ship(UnitType.Frigate, 0.19f, colonyPath, colRot);
        Ship(UnitType.Cruiser, 0.27f, colonyPath, colRot);
        Ship(UnitType.Carrier, 0.34f, colonyPath, colRot);
        Ship(UnitType.Dreadnought, 0.38f, colonyPath, colRot);

        // Bulk hulls — the colony frame is the heavier one, which suits them.
        Ship(UnitType.Miner, 0.21f, colonyPath, colRot);
        Ship(UnitType.Transport, 0.25f, colonyPath, colRot);
        Ship(UnitType.Terraformer, 0.31f, colonyPath, colRot);
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

        /// How far this hull has stood off its station to keep clear of its neighbours, carried
        /// between frames so the correction eases in and out rather than snapping. See Separate().
        public Vector3 sep;

        // ---- Momentum. See ShipPhysics. -----------------------------------------------------------
        /// Where the hull actually IS, as opposed to where the simulation says its marker is. The two
        /// differ through a turn and converge out of it.
        public Vector3 flightPos;
        /// Which way it is going and how fast, in world units per second.
        public Vector3 flightVel;
        /// False until the first frame has somewhere to start from.
        public bool flightReady;

        /// How far round its anchorage a parked ship has walked, in degrees. Separate from `phase`,
        /// which stations use and which is seeded randomly per unit — the anchorage ring must advance
        /// UNIFORMLY or the spacing it was given decays into a pile-up.
        public float parkPhase;
    }

    /// How fast a parked fleet walks its anchorage. Six degrees a second is a full circuit a minute —
    /// clearly alive, and slow enough that clicking a ship is never a moving-target problem.
    const float ParkedOrbitDegPerSec = 6f;

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

        // The player's chosen livery, painted onto the accent surfaces the art was generated with.
        // Player ships only: the other empires wear the colours their art shipped in, and a galaxy in
        // which every fleet had been recoloured to somebody's taste would lose the thing the accents
        // are for. Silent no-op until the player actually chooses. See CivLivery.
        if (u.owner == FactionManager.Player)
            CivLivery.Apply(go, SpeciesManager.CurrentIndex);

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

        // ...and then everybody gets out of everybody else's way. Runs AFTER the tick loop because it
        // needs every hull's station for this frame before it can tell who is standing on whom.
        Separate(dt);

        // AFTER the hulls have moved and turned, so a badge is never a frame behind the ship it belongs
        // to — at system zoom a badge lagging its hull reads as two separate objects.
        TickBadges();
    }

    // A station: orbits whatever it's deployed at, exactly as that world orbits its star.
    // ============================================================================================
    // KEEPING CLEAR — the last few tenths of a unit, which no formation can plan for
    //
    // Formations and the anchorage ring hand every ship a station and those stations do not overlap.
    // That settles the plan and not the reality, because a fleet is never only its plan: ships arrive
    // from different courses, a squadron sits down on top of one already parked, a station orbits
    // through where a freighter is loading, and a fleet mid-turn is briefly nowhere near its wedge.
    // Whenever two hulls end a frame in the same place, the thing on screen is one confused shape.
    //
    // So this is a LOCAL correction on top of the plan, and it is deliberately soft:
    //
    //   * it only acts inside the sum of two hulls' radii — ships are left alone the instant they are
    //     clear, rather than being held apart by an invisible cushion
    //   * the correction is CARRIED and eased, not applied outright. A hard push computed per frame
    //     jitters, because the push that fixes the overlap removes the reason for the push
    //   * it is CAPPED at roughly a hull's width. A ship that would have to abandon its station to be
    //     clear stays on station and overlaps a little instead. Brief overlap is a much smaller lie
    //     than a fleet with no formation at all
    //   * the CHEAPER hull yields. A scout gets out of a dreadnought's way and not the reverse, which
    //     is both what an admiral would order and what makes the capital ships read as the anchor of
    //     the formation
    //   * when nothing is near, the offset decays back to zero, so ships return to station rather than
    //     wandering off wherever the last shove left them
    //
    // O(n^2) over DRAWN models, which is tens — the map's hundreds of units are drawn as tokens, not
    // meshes, and never reach this. If that ever stops being true this wants a grid, not a rewrite.
    // ============================================================================================

    /// How much of a hull's drawn size counts as its personal space.
    const float ClearanceRadius = 0.55f;

    /// How fast a ship slides off station to keep clear, and how fast it drifts back when clear.
    const float YieldSpeed = 1.6f, ReturnSpeed = 0.9f;

    /// The furthest a ship will ever stand off its station, as a multiple of its own drawn size.
    const float MaxStandoff = 1.1f;

    // Reused between frames — this runs every LateUpdate and must not allocate.
    static readonly List<Model> separating = new List<Model>();
    static readonly List<Unit> sepUnits = new List<Unit>();
    static readonly List<Vector3> stations = new List<Vector3>();
    static readonly List<Vector3> pushes = new List<Vector3>();

    void Separate(float dt)
    {
        separating.Clear(); sepUnits.Clear(); stations.Clear(); pushes.Clear();

        foreach (var kv in models)
        {
            var m = kv.Value;
            if (m?.go == null) continue;
            separating.Add(m);
            sepUnits.Add(kv.Key);
            // The tick loop has just written each hull's station into its transform, and `sep` is last
            // frame's correction — so the station is the transform MINUS that correction.
            stations.Add(m.go.transform.position - m.sep);
            pushes.Add(Vector3.zero);
        }

        int n = separating.Count;
        for (int i = 0; i < n; i++)
        {
            var mi = separating[i];
            float ri = Mathf.Max(0.02f, mi.entry.size * ClearanceRadius);

            for (int j = i + 1; j < n; j++)
            {
                var mj = separating[j];
                float rj = Mathf.Max(0.02f, mj.entry.size * ClearanceRadius);
                float want = ri + rj;

                // Compare where they are ACTUALLY drawn, not where their stations are — two ships
                // already eased apart are not overlapping and should not be pushed again.
                Vector3 d = (stations[j] + mj.sep) - (stations[i] + mi.sep);
                float sq = d.sqrMagnitude;
                if (sq >= want * want) continue;

                // Dead centre on each other: pick a repeatable direction from the pair rather than a
                // random one, or the two of them jitter against each other forever.
                Vector3 dir;
                if (sq < 1e-8f)
                {
                    int seed = mi.go.GetInstanceID() ^ mj.go.GetInstanceID();
                    float a = (seed & 1023) / 1023f * Mathf.PI * 2f;
                    dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                }
                else dir = d / Mathf.Sqrt(sq);

                float overlap = want - Mathf.Sqrt(Mathf.Max(0f, sq));

                // The cheaper ship does most of the moving. Both values zero (two identical hulls, or
                // two with no info) falls back to sharing it evenly.
                float vi = FleetFormation.ProtectionValue(sepUnits[i]);
                float vj = FleetFormation.ProtectionValue(sepUnits[j]);
                float total = vi + vj;
                float shareI = total > 0.001f ? vj / total : 0.5f;

                pushes[i] -= dir * (overlap * shareI);
                pushes[j] += dir * (overlap * (1f - shareI));
            }
        }

        for (int i = 0; i < n; i++)
        {
            var m = separating[i];
            Vector3 want = pushes[i];

            float cap = Mathf.Max(0.05f, m.entry.size * MaxStandoff);
            if (want.sqrMagnitude > cap * cap) want = want.normalized * cap;

            // Off station quickly, back to station gently: getting clear is urgent and returning is not.
            float rate = want.sqrMagnitude > m.sep.sqrMagnitude ? YieldSpeed : ReturnSpeed;
            m.sep = Vector3.MoveTowards(m.sep, want, rate * dt);

            m.go.transform.position = stations[i] + m.sep;
        }
    }

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

    // ============================================================================================
    // CHASING THE MARKER — the momentum integrator
    //
    // `marker` is where UnitManager says this ship is: authoritative, on schedule, and the thing every
    // other system reads. This returns where a hull WITH MASS would be while trying to get there.
    //
    // Three rules, and between them they produce a turn nobody had to script:
    //
    //   1. the hull may only rotate its velocity at ShipPhysics.TurnRateAt, which FALLS as it goes
    //      faster — so the quicker it is moving the wider the arc it can hold;
    //   2. it only holds thrust while it is roughly pointing where it wants to go, and brakes when it
    //      is more than a right angle out;
    //   3. it eases off as it closes, so it settles onto the marker instead of oscillating past it.
    //
    // THE LAG IS CAPPED. A ship may trail its own marker through a turn — that IS the turn — but never
    // by more than a few hull lengths, because a ship separated from the icon the player is tracking
    // has stopped being that ship. When the cap bites, the hull is pulled in and keeps its velocity, so
    // the correction never shows as a jump in heading.
    // ============================================================================================

    // ---- The leash ----------------------------------------------------------------------------
    //
    // The first version scaled this off the HULL'S SIZE, which sounded reasonable and was useless: a
    // dreadnought is 0.40 units across, so seven hull lengths is 2.8 units — and the simulated reversal
    // it is meant to allow swings 24.7 units wide. The leash would have dominated the flight model
    // completely and the momentum nobody could see would have been momentum that was not there.
    //
    // A ship's arc is set by its SPEED, not by how big it is, so the leash is now a couple of seconds
    // of travel. That is a deliberate compromise rather than a physical quantity: the pure model would
    // let a mega-station wander fifty units from its own icon, and a ship that far from the marker the
    // player is tracking has stopped being that ship. Sixteen units is about as far as a hull can stray
    // and still read as the thing the icon belongs to.
    const float LagSeconds = 1.5f, MinLagUnits = 3f, MaxLagUnits = 16f;

    Vector3 Steer(Unit u, Model m, Vector3 marker, float dt)
    {
        if (dt <= 0f) return marker;

        if (!m.flightReady)
        {
            m.flightPos = marker;
            m.flightVel = Vector3.zero;
            m.flightReady = true;
            return marker;
        }

        Vector3 toMarker = marker - m.flightPos;
        float distance = toMarker.magnitude;

        // The pace the simulation is actually moving the marker at. Taking the top speed from this
        // rather than from the class's raw `speed` stat keeps the hull honest about the ETA: a fleet
        // limited by its slowest ship has every hull in it flying at the fleet's pace, not its own.
        float sim = u.travelDuration > 0.01f
            ? Vector3.Distance(u.travelFrom, u.travelTo) / u.travelDuration
            : Mathf.Max(1, u.Info.speed);
        float topSpeed = Mathf.Max(0.5f, sim * 1.6f);   // headroom to catch up after a turn

        float accel = ShipPhysics.Acceleration(u, topSpeed);
        float speed = m.flightVel.magnitude;

        Vector3 want = distance > 0.0001f ? toMarker / distance : Vector3.zero;
        Vector3 heading = speed > 0.0001f ? m.flightVel / speed : want;
        if (want == Vector3.zero) want = heading;

        // ---- rule 1: turn, at a rate this hull and this speed allow ----
        float turn = ShipPhysics.TurnRateAt(u, speed) * Mathf.Deg2Rad * dt;
        heading = Vector3.RotateTowards(heading, want, turn, 0f);
        if (heading.sqrMagnitude < 0.0001f) heading = want;
        heading.Normalize();

        // ---- rule 2: thrust only while roughly aimed, brake when badly aimed ----
        float off = Vector3.Angle(heading, want);
        float throttle = ShipPhysics.ThrottleFor(off);

        // ---- rule 3: never go faster than it can still stop from ----
        float target = Mathf.Min(topSpeed * throttle, ShipPhysics.ApproachSpeed(accel, distance));

        speed = target > speed
            ? Mathf.MoveTowards(speed, target, accel * dt)
            : Mathf.MoveTowards(speed, target, accel * ShipPhysics.BrakeFactor * dt);

        m.flightVel = heading * speed;
        m.flightPos += m.flightVel * dt;

        // ---- the leash ----
        float cap = Mathf.Clamp(topSpeed * LagSeconds, MinLagUnits, MaxLagUnits);
        Vector3 lag = m.flightPos - marker;
        if (lag.sqrMagnitude > cap * cap)
        {
            // Position only. Keeping the velocity means the pull-in never shows up as the nose
            // snapping round — the ship is still flying the way it was, just not as far out.
            m.flightPos = marker + lag.normalized * cap;
        }

        return m.flightPos;
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

            // MOMENTUM. The marker is where the simulation says the ship is; this is where a thing with
            // mass would actually be, chasing that marker under a turn rate and an acceleration it
            // cannot exceed. Order a reversal and the hull carries on the old way while it hauls its
            // nose round, swings wide, and only builds speed again once it is pointing somewhere useful.
            // See ShipPhysics. Returns the marker itself for anything with no meaningful momentum.
            pos = Steer(u, m, pos, dt);

            // The hull points where it is GOING, not down the straight line between its endpoints —
            // through a turn those are different directions, and the difference is the turn.
            if (m.flightVel.sqrMagnitude > 0.0004f) dir = m.flightVel;

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
            // Docked, so the momentum state is stale: the next departure starts from rest at the
            // anchorage rather than carrying whatever velocity this ship had when it arrived.
            m.flightReady = false;
            m.flightVel = Vector3.zero;

            // Parked at a world: stand off it a little so it isn't buried in the planet, and look at it.
            var host = u.location.visualObject.transform;
            float standoff = host.lossyScale.x * 0.5f + 0.4f;
            int idx = u.location.units != null ? Mathf.Max(0, u.location.units.IndexOf(u)) : 0;
            int count = u.location.units != null ? Mathf.Max(1, u.location.units.Count) : 1;

            // The anchorage GROWS with the number of ships in it — see FleetFormation.AnchorOffset.
            // A fixed-radius ring divided by the count packs twenty ships a tenth of a unit apart, and
            // hulls are up to 0.40 across, so a well-defended world drew as one solid ring of
            // interpenetrating geometry instead of as a fleet standing off it.
            Vector3 anchor = FleetFormation.AnchorOffset(idx, count, standoff, 0.34f);

            // ============================================================================================
            // PARKED SHIPS ORBIT — slowly, and on purpose
            //
            // A ship at a world used to sit on a fixed point of a ring, which reads as parked in the
            // car-park sense rather than as being in orbit. It now walks the ring.
            //
            // THE WHOLE RING TURNS AT ONE RATE rather than each ship carrying its own phase, and that
            // matters: the spacing FleetFormation worked out stays exactly as worked out, so ships never
            // drift into one another however long they sit there. Per-ship rates would have them slowly
            // bunch and pass through each other, which is the very thing the anchorage exists to prevent.
            //
            // The rate is deliberately tiny. At a typical standoff a full circuit takes about a minute —
            // under a tenth of a world unit per second, which is visible as motion and far too slow to
            // make a hull hard to click. A ship you cannot reliably click is worse than a ship that does
            // not move.
            m.parkPhase += ParkedOrbitDegPerSec * dt;
            if (m.parkPhase > 360f) m.parkPhase -= 360f;
            anchor = Quaternion.Euler(0f, m.parkPhase, 0f) * anchor;

            pos = host.position + Vector3.up * 0.35f + anchor;

            // Nose ALONG the orbit rather than at the world. Anything flying a circle points around it;
            // aimed at the planet it is circling, a ship reads as one about to fly into it.
            Vector3 along = Vector3.Cross(Vector3.up,
                anchor.sqrMagnitude > 0.0001f ? anchor.normalized : Vector3.right);
            if (along.sqrMagnitude > 0.0001f)
            {
                m.go.transform.rotation = Quaternion.Slerp(m.go.transform.rotation,
                    Quaternion.LookRotation(along.normalized, Vector3.up) * m.entry.modelRotation, 2f * dt);
            }
            else m.go.transform.rotation = Quaternion.Slerp(m.go.transform.rotation,
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
