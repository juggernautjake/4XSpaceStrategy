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
        if (!targeting) { PassivePreview(); HandleRightClickMove(); return; }   // deselect handled by BoxSelectController
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

        // Colour the path by whether the fleet can actually reach it (range gate).
        bool reach = UnitManager.Instance == null || UnitManager.Instance.CanReachBody(fleet, hovered, out _);
        line.startColor = line.endColor = reach ? new Color(0.6f, 0.85f, 1f, 0.9f) : new Color(1f, 0.45f, 0.4f, 0.95f);

        line.SetPosition(0, origin);
        line.SetPosition(1, endPoint);

        if (Input.GetMouseButtonDown(0) &&
            !(UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
        {
            if (!reach)
            {
                NotificationManager.Instance?.Push($"Out of range — {hovered.name}",
                    "Your fleet can't reach it yet. Upgrade drives or build a relay to extend your range.", null, NotifKind.Danger);
                return;
            }
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

        bool reach = UnitManager.Instance == null || UnitManager.Instance.CanReach(sel, endPoint, out _);
        line.startColor = line.endColor = reach ? new Color(0.6f, 0.85f, 1f, 0.9f) : new Color(1f, 0.45f, 0.4f, 0.9f);
        line.enabled = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, endPoint);
        // No cursor-following tooltip: just the dashed trajectory. Right-click brings up the confirm menu.
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

        // NOTHING SELECTED -> right-click is an OBJECT menu instead of an order.
        //
        // The two never collide, because "send my ships there" is only a meaningful thing to say when you
        // have ships selected to send. With none, right-click had no meaning at all and did nothing.
        if (group.Count == 0) { ShowObjectMenu(Input.mousePosition); return; }

        bool queue = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        string verb = queue ? "Queue" : "Send";
        Vector2 mp = Input.mousePosition;
        var body = RaycastBody();
        var mgr = UnitManager.Instance;

        if (body != null)
        {
            // Lock the pulsing indicator onto the body — it travels with it as it orbits.
            TargetIndicator.Instance?.ShowAtBody(body);
            bool reach = mgr == null || mgr.CanReachBody(group, body, out _);
            var options = new List<ContextMenu.Option>();
            if (reach)
                options.Add(new ContextMenu.Option($"{verb}: move to {body.name}", () => mgr?.IssueMove(group, body, queue)));
            else
                options.Add(new ContextMenu.Option($"Out of range — {body.name}",
                    () => NotificationManager.Instance?.Push($"Out of range — {body.name}",
                        "Your fleet can't reach it yet. Upgrade drives or build a relay to extend your range.", null, NotifKind.Danger)));
            bool canSurvey = reach && !body.Surveyed && Any(group, u => u.Info.canExplore);
            bool canResearch = reach && body.Surveyed && Any(group, u => u.Info.canResearch);
            bool canColonize = reach && body.owner != FactionManager.Player && Any(group, u => u.Info.canColonize);
            // Sending a terraformer to a world is now a first-class map order (it used to live only in the
            // ship panel). Offered when a terraformer is present, the world isn't already terraforming, and
            // it's a world you can actually work — a gas giant has no surface to grind. ToggleTerraform
            // still has the final say on feasibility when the ship arrives.
            bool canTerraform = reach && body.type != CelestialBodyType.GasGiant && !body.terraforming &&
                                Any(group, u => u.Info.canTerraform);
            if (canSurvey) options.Add(new ContextMenu.Option($"{verb}: survey {body.name} on arrival", () => mgr?.IssueAction(group, OrderKind.Survey, body, queue)));
            if (canResearch) options.Add(new ContextMenu.Option($"{verb}: research {body.name} on arrival", () => mgr?.IssueAction(group, OrderKind.Research, body, queue)));
            if (canColonize) options.Add(new ContextMenu.Option($"{verb}: colonize {body.name} on arrival", () => mgr?.IssueAction(group, OrderKind.Colonize, body, queue)));
            if (canTerraform) options.Add(new ContextMenu.Option($"{verb}: terraform {body.name} on arrival", () => mgr?.IssueAction(group, OrderKind.Terraform, body, queue)));
            options.Add(new ContextMenu.Option("Cancel", () => TargetIndicator.Instance?.Hide()));
            ContextMenu.Instance?.Show(mp, options);
        }
        else
        {
            Vector3 pt = RaycastPlane();
            TargetIndicator.Instance?.ShowAtPoint(pt);
            bool reach = mgr == null || mgr.CanReach(group, pt, out _);
            var opts = new List<ContextMenu.Option>();
            if (reach)
                opts.Add(new ContextMenu.Option($"{verb}: move here (deep space)", () => mgr?.IssueMovePoint(group, pt, queue)));
            else
                opts.Add(new ContextMenu.Option("Out of range — deep space",
                    () => NotificationManager.Instance?.Push("Out of range",
                        "Your fleet can't reach that far yet. Upgrade drives or build a relay.", null, NotifKind.Danger)));
            opts.Add(new ContextMenu.Option("Cancel", () => TargetIndicator.Instance?.Hide()));
            ContextMenu.Instance?.Show(mp, opts);
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

    // ============================================================================================
    // RIGHT-CLICK AN OBJECT (with no ships selected) -> what can I do with this?
    //
    // Works on anything you can see: planets, moons, stars and ships. They're four different components
    // with four different notions of "size", so the menu is built per kind — but Focus is on every one of
    // them, because "take me to this and keep me there" is the thing you want from any of them.
    // ============================================================================================
    void ShowObjectMenu(Vector2 screenPos)
    {
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 5000f)) return;   // empty space: nothing to offer

        var opts = new List<ContextMenu.Option>();

        var token = hit.collider.GetComponent<UnitToken>();
        if (token != null && token.Unit != null)
        {
            var u = token.Unit;
            opts.Add(Focus($"Focus on {u.name}", hit.collider.transform, 3f));
            opts.Add(new ContextMenu.Option($"Select {u.name}", () => UnitSelection.SelectOnly(u)));
            opts.Add(new ContextMenu.Option("Ship info", () =>
                InspectorWindow.Instance?.Inspect(InspectorTarget.Of(u), resetTrail: true)));
        }
        else if (hit.collider.GetComponent<PlanetClick>() is PlanetClick pc && pc.data != null)
        {
            var b = pc.data;
            opts.Add(Focus($"Focus on {b.name}", b.visualObject != null ? b.visualObject.transform : hit.collider.transform, b.surfaceSize));
            // The old "info" pop-up (the tabbed body Inspector) is retired — its tabs are folded into the
            // Planet View. Both the info and the build/survey context options now open that one window.
            opts.Add(new ContextMenu.Option($"Planet View — {b.name}",
                () => PlanetViewWindow.Instance?.ShowFor(b)));

            // Claiming is the one action worth offering straight off the map — it's the thing you do to a
            // world you just found, and it's a single click that would otherwise mean opening a panel.
            if (Claim.CanClaim(b, out string whyClaim))
                opts.Add(new ContextMenu.Option($"Claim ({Claim.BeaconMetal(b)}m {Claim.BeaconEnergy(b)}e)",
                    () => Claim.DoClaim(b)));
            else if (!Claim.IsMine(b) && b.owner == null)
                opts.Add(new ContextMenu.Option($"Can't claim — {whyClaim}", null, false));
        }
        else if (hit.collider.GetComponent<StarInteraction>() is StarInteraction si && si.star != null)
        {
            var s = si.star; var sys = si.system;
            opts.Add(Focus($"Focus on {s.name}", hit.collider.transform, hit.collider.transform.lossyScale.x));
            opts.Add(new ContextMenu.Option($"{s.name} info", () =>
                InspectorWindow.Instance?.Inspect(InspectorTarget.Of(s, sys), resetTrail: true)));
            if (sys != null)
                opts.Add(new ContextMenu.Option($"Focus system {sys.name}", () => GameManager.Instance?.SetFocus(sys)));
        }

        if (opts.Count == 0) return;
        opts.Add(new ContextMenu.Option("Cancel", null));
        ContextMenu.Instance?.Show(screenPos, opts);
    }

    /// Focus = zoom onto it AND follow it. Following is the point: a planet is a moving target, so a
    /// zoom that doesn't track just watches it drift out of frame.
    static ContextMenu.Option Focus(string label, Transform t, float sizeHint)
        => new ContextMenu.Option(label, () =>
        {
            if (t != null) CameraController.Instance?.FocusAndZoom(t, sizeHint, true);
        }, t != null);

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
