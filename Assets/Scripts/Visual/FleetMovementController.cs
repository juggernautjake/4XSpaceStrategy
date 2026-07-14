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
        if (!targeting) return;
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
            UnitManager.Instance?.SendUnits(fleet, hovered);
            Disarm();
        }
    }

    Vector3 FleetPos()
    {
        var b = fleet[0].location;
        if (b != null && b.visualObject != null) return b.visualObject.transform.position;
        if (b != null && b.system != null) return b.system.galaxyPosition;
        // mid-transit: use the moving token estimate
        return Vector3.Lerp(fleet[0].travelFrom, fleet[0].travelTo, fleet[0].TravelProgress);
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
