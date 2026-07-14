using System.Collections.Generic;
using UnityEngine;

// Handles "send fleet" targeting. When armed, it draws a dashed path from the fleet to the hovered
// destination's LIVE position (updating as the destination orbits) and shows the travel time.
// Hold SHIFT to instead preview the PREDICTED INTERCEPT — where the destination will actually be when
// the fleet arrives. Left-click a body to confirm; right-click or Esc to cancel.
public class FleetMovementController : MonoBehaviour
{
    public static FleetMovementController Instance;

    const float SpeedScale = 6f;          // must match UnitManager
    static readonly KeyCode PredictKey = KeyCode.LeftShift;

    Camera cam;
    LineRenderer line;
    GameObject marker;
    List<Unit> fleet;
    bool targeting;

    public bool IsTargeting => targeting;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("FleetMovementController").AddComponent<FleetMovementController>();
    }

    void Awake()
    {
        Instance = this;
        cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

        var lineGo = new GameObject("PathLine");
        lineGo.transform.SetParent(transform, false);
        line = lineGo.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = 0.5f;
        line.numCapVertices = 2;
        line.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = DashTexture() };
        line.textureMode = LineTextureMode.Tile;
        line.material.mainTextureScale = new Vector2(8f, 1f);
        line.startColor = line.endColor = new Color(0.6f, 0.85f, 1f, 0.9f);
        line.enabled = false;

        marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "InterceptMarker";
        var col = marker.GetComponent<Collider>(); if (col != null) Destroy(col);
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * 1.4f;
        var mr = marker.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.4f, 1f, 0.6f, 0.8f) };
        marker.SetActive(false);
    }

    public void Arm(List<Unit> selectedFleet)
    {
        if (selectedFleet == null || selectedFleet.Count == 0) return;
        fleet = new List<Unit>(selectedFleet);
        targeting = true;
        line.enabled = true;
    }

    public void Disarm()
    {
        targeting = false;
        line.enabled = false;
        marker.SetActive(false);
        TooltipManager.Instance.Hide();
    }

    void Update()
    {
        if (!targeting) { PassivePreview(); HandleRightClickMove(); HandleDeselect(); return; }
        if (cam == null) cam = Camera.main;
        if (cam == null || fleet == null || fleet.Count == 0) { Disarm(); return; }

        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { Disarm(); return; }

        Vector3 origin = FleetPos();
        CelestialBody hovered = RaycastBody();

        if (hovered == null) { line.enabled = false; marker.SetActive(false); return; }
        line.enabled = true;

        int slow = int.MaxValue;
        foreach (var u in fleet) slow = Mathf.Min(slow, Mathf.Max(1, u.Speed));

        Vector3 targetNow = BodyPos(hovered);
        float dist = Vector3.Distance(origin, targetNow);
        float dur = Mathf.Clamp(dist / (slow * SpeedScale), 3f, 120f);

        bool predict = Input.GetKey(PredictKey);
        Vector3 endPoint = targetNow;

        if (predict)
        {
            // Iterate: arrival time depends on where the target will be.
            var oc = hovered.visualObject != null ? hovered.visualObject.GetComponent<OrbitController>() : null;
            if (oc != null)
            {
                float t = dur;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = oc.PredictWorldPosition(t);
                    t = Mathf.Clamp(Vector3.Distance(origin, p) / (slow * SpeedScale), 3f, 120f);
                }
                endPoint = oc.PredictWorldPosition(t);
                dur = t;
            }
            marker.SetActive(true);
            marker.transform.position = endPoint;
        }
        else marker.SetActive(false);

        line.SetPosition(0, origin);
        line.SetPosition(1, endPoint);

        TooltipManager.Instance.ShowAtCursor(
            $"Send to <b>{hovered.name}</b>\nTravel: {dur:F0}s" +
            (predict ? "\n<color=#4DFF6E>predicted arrival point</color>" : "\n<size=10>hold Shift for predicted intercept</size>"));

        if (Input.GetMouseButtonDown(0) &&
            !(UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
        {
            bool queue = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            UnitManager.Instance?.IssueMove(fleet, hovered, queue);
            Disarm();
        }
    }

    // Passive path preview: whenever movable ships are selected (and we're not already armed via the
    // Send button), draw the dashed trajectory from the fleet to whatever the cursor is over and show
    // the travel time — NO key required. Holding Shift additionally previews the predicted intercept
    // point. To actually send, right-click and confirm.
    void PassivePreview()
    {
        var sel = SelectableFleet();
        if (sel.Count == 0) { line.enabled = false; marker.SetActive(false); return; }
        if (cam == null) cam = Camera.main;
        if (cam == null) { line.enabled = false; marker.SetActive(false); return; }
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        { line.enabled = false; marker.SetActive(false); return; }

        // Figure out what's under the cursor.
        CelestialBody hovered = null;
        bool overToken = false;
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f))
        {
            if (hit.collider.GetComponent<UnitToken>() != null) overToken = true;
            var pc = hit.collider.GetComponent<PlanetClick>();
            if (pc != null) hovered = pc.data;
        }
        // Don't fight the ship-token hover tooltip.
        if (overToken) { line.enabled = false; marker.SetActive(false); return; }

        Vector3 origin = FleetPosOf(sel);
        Vector3 targetNow = hovered != null ? BodyPos(hovered) : RaycastPlane();

        int slow = int.MaxValue;
        foreach (var u in sel) slow = Mathf.Min(slow, Mathf.Max(1, u.Speed));
        float dur = Mathf.Clamp(Vector3.Distance(origin, targetNow) / (slow * SpeedScale), 3f, 120f);

        bool predict = Input.GetKey(PredictKey);
        Vector3 endPoint = targetNow;
        if (predict && hovered != null)
        {
            var oc = hovered.visualObject != null ? hovered.visualObject.GetComponent<OrbitController>() : null;
            if (oc != null)
            {
                float t = dur;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = oc.PredictWorldPosition(t);
                    t = Mathf.Clamp(Vector3.Distance(origin, p) / (slow * SpeedScale), 3f, 120f);
                }
                endPoint = oc.PredictWorldPosition(t);
                dur = t;
            }
            marker.SetActive(true);
            marker.transform.position = endPoint;
        }
        else marker.SetActive(false);

        line.enabled = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, endPoint);

        string dest = hovered != null ? $"<b>{hovered.name}</b>" : "deep space";
        TooltipManager.Instance.ShowAtCursor(
            $"Right-click to send {sel.Count} ship(s) to {dest}\nTravel: {dur:F0}s" +
            (predict ? "\n<color=#4DFF6E>predicted arrival point</color>" : "\n<size=10>hold Shift for predicted intercept</size>"));
    }

    List<Unit> SelectableFleet()
    {
        var g = new List<Unit>();
        foreach (var u in UnitSelection.Selected) if (u.status != UnitStatus.Traveling) g.Add(u);
        return g;
    }

    // Right-click a body or empty space with ships selected -> confirm sending them there.
    void HandleRightClickMove()
    {
        if (!Input.GetMouseButtonDown(1)) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        // Selected ships that can still take orders (traveling ships CAN be redirected/queued too).
        var group = new List<Unit>(SelectableFleet());
        foreach (var u in UnitSelection.Selected)
            if (u.status == UnitStatus.Traveling && !group.Contains(u)) group.Add(u);
        if (group.Count == 0) return;

        bool queue = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        string verb = queue ? "Queue" : "Send";
        Vector2 mp = Input.mousePosition;
        var body = RaycastBody();
        var mgr = UnitManager.Instance;

        if (body != null)
        {
            var options = new List<ContextMenu.Option>
            {
                new ContextMenu.Option($"{verb}: move to {body.name}", () => mgr?.IssueMove(group, body, queue))
            };
            bool canSurvey = !body.Surveyed && Any(group, u => u.Info.canExplore);
            bool canResearch = body.Surveyed && Any(group, u => u.Info.canResearch);
            bool canColonize = body.owner != FactionManager.Player && Any(group, u => u.Info.canColonize);
            if (canSurvey) options.Add(new ContextMenu.Option($"{verb}: survey {body.name} on arrival", () => mgr?.IssueAction(group, OrderKind.Survey, body, queue)));
            if (canResearch) options.Add(new ContextMenu.Option($"{verb}: research {body.name} on arrival", () => mgr?.IssueAction(group, OrderKind.Research, body, queue)));
            if (canColonize) options.Add(new ContextMenu.Option($"{verb}: colonize {body.name} on arrival", () => mgr?.IssueAction(group, OrderKind.Colonize, body, queue)));
            options.Add(new ContextMenu.Option("Cancel", null));
            ContextMenu.Instance?.Show(mp, options);
        }
        else
        {
            Vector3 pt = RaycastPlane();
            ContextMenu.Instance?.Show(mp, new List<ContextMenu.Option>
            {
                new ContextMenu.Option($"{verb}: move here (deep space)", () => mgr?.IssueMovePoint(group, pt, queue)),
                new ContextMenu.Option("Cancel", null)
            });
        }
    }

    static bool Any(List<Unit> group, System.Func<Unit, bool> pred)
    {
        foreach (var u in group) if (pred(u)) return true;
        return false;
    }

    // Left-clicking empty space (no token, no body) drops the ship selection, so the send menu never
    // lingers on a world you were only inspecting. Clicks on tokens/bodies are handled by their own
    // colliders and leave selection intact (tokens) or inspect (bodies).
    void HandleDeselect()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (UnitSelection.Selected.Count == 0) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out _, 5000f)) UnitSelection.Clear();   // hit nothing -> deselect
    }

    Vector3 RaycastPlane()
    {
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Mathf.Abs(ray.direction.y) > 0.0001f)
        {
            float t = -ray.origin.y / ray.direction.y;   // intersect the y=0 orbital plane
            if (t > 0f) return ray.origin + ray.direction * t;
        }
        return ray.origin + ray.direction * 100f;
    }

    Vector3 FleetPos() => FleetPosOf(fleet);

    static Vector3 FleetPosOf(List<Unit> f)
    {
        var lead = f[0];
        var b = lead.location;
        if (b != null && b.visualObject != null) return b.visualObject.transform.position;
        if (b != null && b.system != null) return b.system.galaxyPosition;
        // mid-transit: use the moving token estimate
        return Vector3.Lerp(lead.travelFrom, lead.travelTo, lead.TravelProgress);
    }

    static Vector3 BodyPos(CelestialBody b)
    {
        if (b.visualObject != null) return b.visualObject.transform.position;
        if (b.system != null) return b.system.galaxyPosition;
        return Vector3.zero;
    }

    CelestialBody RaycastBody()
    {
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f))
        {
            var pc = hit.collider.GetComponent<PlanetClick>();
            if (pc != null) return pc.data;
        }
        return null;
    }

    static Texture2D DashTexture()
    {
        int w = 16;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
        var px = new Color[w];
        for (int i = 0; i < w; i++) px[i] = i < w / 2 ? Color.white : new Color(1, 1, 1, 0);
        tex.SetPixels(px); tex.Apply();
        return tex;
    }
}
