using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// LAYING DOWN A PATROL ROUTE
//
// Armed from the fleet command bar. Left-click drops a waypoint, right-click finishes, Escape
// cancels. The route draws as it is built — including the closing leg back to the first point, so a
// loop LOOKS like a loop before it is committed rather than after.
//
// Points are picked on the same horizontal plane the fleets fly in, which is how every other click in
// the system view resolves a position (FleetMovementController does the same). A 4X camera looks down
// at a shallow angle, so a ray that hit "wherever the geometry happens to be" would put waypoints on
// planets and behind stars.
//
// TWO POINTS IS THE MINIMUM and it is enforced: a one-point patrol is a move order, and accepting one
// would leave a squadron with a patrol flag it could never advance.
// ============================================================================================
public class PatrolTool : MonoBehaviour
{
    public static PatrolTool Instance;

    Camera cam;
    LineRenderer line;
    int squadron;
    bool arming;
    readonly List<Vector3> points = new List<Vector3>();

    public bool IsArming => arming;

    public static void Create()
    {
        if (Instance != null) return;
        Instance = new GameObject("PatrolTool").AddComponent<PatrolTool>();
    }

    void Awake()
    {
        Instance = this;
        cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

        var go = new GameObject("PatrolLine");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.4f;
        line.numCapVertices = 2;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = new Color(1f, 0.82f, 0.4f, 0.9f);
        line.positionCount = 0;
        line.enabled = false;
    }

    /// The mode the route will be walked in, chosen on the command bar BEFORE the route is laid.
    ///
    /// Carried on the tool rather than read off the squadron at Finish() time, so that the choice the
    /// player made while looking at the map is the one that gets committed, even if something else
    /// touches the squadron's orders while they are still clicking waypoints.
    PatrolMode mode = PatrolMode.Loop;

    public void Arm(int g, PatrolMode m)
    {
        if (!Squadrons.Valid(g)) return;
        squadron = g;
        mode = m;
        arming = true;
        points.Clear();
        line.enabled = true;
        line.positionCount = 0;
        NotificationManager.Instance?.Push("Patrol route",
            mode == PatrolMode.Loop
                ? "Click two or more points to lay the route. It will be walked round and round. " +
                  "Right-click to finish, Escape to cancel."
                : "Click two or more points to lay the route. It will be walked up and back down " +
                  "again. Right-click to finish, Escape to cancel.",
            null, NotifKind.Info);
    }

    public void Cancel()
    {
        arming = false;
        points.Clear();
        line.positionCount = 0;
        line.enabled = false;
    }

    void Update()
    {
        if (!arming) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) { Cancel(); return; }

        if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }

        if (Input.GetMouseButtonDown(0) && PickOnPlane(out Vector3 p))
        {
            points.Add(p);
            Redraw();
        }

        if (Input.GetMouseButtonDown(1)) Finish();
    }

    void Finish()
    {
        if (points.Count < 2)
        {
            // A one-point patrol is a move order wearing the wrong hat, and a squadron holding a patrol
            // it can never advance would simply sit there looking broken.
            NotificationManager.Instance?.Push("Route too short",
                "A patrol needs at least two points. Nothing was set.", null, NotifKind.Danger);
            Cancel();
            return;
        }

        Squadrons.SetPatrol(squadron, points, mode);
        NotificationManager.Instance?.Push($"{Squadrons.NameOf(squadron)} on patrol",
            $"Walking {points.Count} points " +
            (mode == PatrolMode.Loop ? "on a loop" : "up and back down again") +
            ". It keeps its protocol while it patrols.",
            null, NotifKind.Info);
        SimpleAudio.Instance?.PlayClick();
        Cancel();
    }

    /// The point under the cursor on the fleet plane — the same y the squadron currently sits at, so a
    /// route laid around a world stays level with it rather than sloping off toward the camera.
    bool PickOnPlane(out Vector3 p)
    {
        p = Vector3.zero;
        var um = UnitManager.Instance;
        float y = 0f;
        if (um != null)
        {
            var members = ControlGroups.Members(squadron);
            if (members.Count > 0) y = um.UnitPos(members[0]).y;
        }

        var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!plane.Raycast(ray, out float d)) return false;
        p = ray.GetPoint(d);
        return true;
    }

    void Redraw()
    {
        // A LOOP gets its closing leg drawn, so it reads as a loop while it is being laid rather than
        // only once it is committed. A SHUTTLE deliberately does not: the whole difference between the
        // two is whether the squadron crosses back over the gap between the last point and the first,
        // and drawing that leg on a route that will never fly it would show the player the wrong shape
        // at exactly the moment they are deciding what shape they want.
        int n = points.Count;
        bool closes = n >= 2 && mode == PatrolMode.Loop;
        line.positionCount = closes ? n + 1 : n;
        for (int i = 0; i < n; i++) line.SetPosition(i, points[i] + Vector3.up * 0.4f);
        if (closes) line.SetPosition(n, points[0] + Vector3.up * 0.4f);
    }
}

// ============================================================================================
// SETTING A RALLY POINT
//
// One click, and the same plane rule as the patrol tool. Small enough that giving it its own file
// would be worse than keeping it beside the thing it is a simpler version of.
// ============================================================================================
public class RallyTool : MonoBehaviour
{
    public static RallyTool Instance;

    Camera cam;
    int squadron;
    bool arming;

    public static void Create()
    {
        if (Instance != null) return;
        Instance = new GameObject("RallyTool").AddComponent<RallyTool>();
    }

    void Awake()
    {
        Instance = this;
        cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
    }

    public void Arm(int g)
    {
        if (!Squadrons.Valid(g)) return;
        squadron = g;
        arming = true;
        NotificationManager.Instance?.Push("Rally point",
            "Click where this squadron should fall back to. Escape to cancel.", null, NotifKind.Info);
    }

    void Update()
    {
        if (!arming) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) { arming = false; return; }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { arming = false; return; }
        if (!Input.GetMouseButtonDown(0)) return;

        var um = UnitManager.Instance;
        float y = 0f;
        if (um != null)
        {
            var members = ControlGroups.Members(squadron);
            if (members.Count > 0) y = um.UnitPos(members[0]).y;
        }

        var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (plane.Raycast(ray, out float d))
        {
            Squadrons.SetRally(squadron, ray.GetPoint(d));
            NotificationManager.Instance?.Push($"{Squadrons.NameOf(squadron)} rally set",
                "Ships that break off or withdraw will head here.", null, NotifKind.Info);
            SimpleAudio.Instance?.PlayClick();
        }
        arming = false;
    }
}
