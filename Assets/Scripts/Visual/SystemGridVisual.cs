using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE SOLAR SYSTEM GRID (2026-10-11)
//
// One faint square grid per system, centred on its star and reaching exactly to the detection circle
// (SystemPresence.DetectionRadius — the threshold a ship crosses to identify what is in the system),
// with a slightly stronger ring drawn ON that circle so the system's edge reads clearly. Lines only:
// no infill, and the lines themselves are highly transparent. It is the floor the planets' own
// gravity-well grids (ZoomFieldRenderer) sit on, and it is drawn UNDER them.
//
// ---- UNDER, BY QUEUE, NOT BY HEIGHT -------------------------------------------------------------
// The grid lies in the system's plane (y = 0 at the pivot) and the wells lie in each planet's plane,
// which an inclined orbit tips above and below it — so "lower" is not a thing a height offset can
// promise. Both use Sprites/Default, which is ZWrite Off, so they cannot z-fight however the planes
// cross; the draw ORDER is then purely the render queue, and this one is set below the wells'
// (Queue < ZoomFieldRenderer.Queue), so a well always paints over the grid. Opaque worlds still
// depth-test against it normally.
//
// ---- SPACING AND MOIRÉ ---------------------------------------------------------------------------
// Lines are real line primitives (MeshTopology.Lines), one pixel wide at every zoom, which suits the
// game's pixel-art look and has no texture to alias. What CAN go wrong is density: pulled back, a
// regular grid packs its lines closer than a few pixels and turns into a shimmering haze. So each
// level fades out by its on-screen spacing — major lines (eight cells per radius) below ~14 px, and a
// minor subdivision (a quarter of that) that only fades IN once its own spacing is wide enough, i.e.
// close up among the planets where the major lines are too far apart to read motion against.
//
// ---- COST ---------------------------------------------------------------------------------------
// One mesh (two submeshes) and one LineRenderer per system, built once and rebuilt only if the
// detection radius changes (the Dev slider, or a body added or removed in the sandbox). Parented to
// the system's pivot, so galaxy zoom switches it off with everything else in the system.
//
// ---- WHEN IT IS SHOWN ---------------------------------------------------------------------------
// With the Gravity Wells switch (MapGrids.Show), never in an unexplored system (system fog — a player
// is not told where a system they have never visited begins), never while the system is concealed
// (the genesis sequence, the Dev object panel), and not while the intro is filming.
// ============================================================================================
public class SystemGridVisual : MonoBehaviour
{
    public StarSystemData system;

    /// Below ZoomFieldRenderer.Queue (3000), so every gravity well is drawn over this grid.
    public const int Queue = ZoomFieldRenderer.Queue - 10;

    const int MajorPerRadius = 8;        // cells from the star to the edge
    const int MinorPerMajor = 4;         // minor subdivision of each major cell
    const int RingSegments = 160;

    // Highly transparent lines; the edge ring a little stronger so the boundary reads.
    static readonly Color LineColor = new Color(0.72f, 0.84f, 1.00f, 1f);
    const float MajorAlpha = 0.11f;
    const float MinorAlpha = 0.05f;
    const float RingAlpha = 0.32f;
    const float RingWidthPx = 2f;

    // On-screen spacing (pixels) over which each level fades out (major) or in (minor).
    const float MajorFadeFromPx = 5f, MajorFadeToPx = 14f;
    const float MinorFadeFromPx = 10f, MinorFadeToPx = 26f;

    const float PollSeconds = 0.25f;

    MeshFilter mf;
    MeshRenderer mr;
    Material majorMat, minorMat, ringMat;
    LineRenderer ring;
    Mesh mesh;
    float builtRadius = -1f;
    float nextPoll;
    bool shown = true;

    /// Attach (or find) the grid for a system. Safe to call repeatedly — same pattern as
    /// DetectionRingVisual.Ensure, and built beside it by SystemVisualizer.
    public static SystemGridVisual Ensure(StarSystemData sys)
    {
        if (sys?.pivot == null) return null;

        var existing = sys.pivot.GetComponentInChildren<SystemGridVisual>(true);
        if (existing != null) { existing.system = sys; return existing; }

        var go = new GameObject("SystemGrid");
        go.transform.SetParent(sys.pivot, false);
        var grid = go.AddComponent<SystemGridVisual>();
        grid.system = sys;
        return grid;
    }

    void Awake()
    {
        mf = gameObject.AddComponent<MeshFilter>();
        mr = gameObject.AddComponent<MeshRenderer>();
        majorMat = NewMat(Queue);
        minorMat = NewMat(Queue);
        mr.sharedMaterials = new[] { majorMat, minorMat };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        var ringGo = new GameObject("Edge");
        ringGo.transform.SetParent(transform, false);
        ring = ringGo.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ringMat = NewMat(Queue + 1);
        ring.sharedMaterial = ringMat;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.startColor = ring.endColor = Color.white;
        ring.widthMultiplier = 0.3f;
        ring.positionCount = 0;

        nextPoll = 0f;
    }

    static Material NewMat(int queue)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { renderQueue = queue };
        m.color = new Color(LineColor.r, LineColor.g, LineColor.b, 0f);
        return m;
    }

    void Update()
    {
        nextPoll -= Time.unscaledDeltaTime;
        if (nextPoll <= 0f)
        {
            nextPoll = PollSeconds;
            bool want = system != null && MapGrids.Show && system.hideReason == HideReason.None
                        && !VisibilityService.Unexplored(system) && !GenesisCamera.Active;
            if (want)
            {
                float r = SystemPresence.DetectionRadius(system);
                if (r > 0f && !Mathf.Approximately(r, builtRadius)) Rebuild(r);
            }
            SetShown(want && builtRadius > 0f);
        }
        if (shown) Fade();
    }

    void SetShown(bool on)
    {
        if (on == shown) return;
        shown = on;
        mr.enabled = on;
        ring.enabled = on;
    }

    // Alpha by on-screen line spacing, and the ring's width held at a constant pixel size.
    void Fade()
    {
        var cam = Camera.main;
        if (cam == null || builtRadius <= 0f) return;

        // Pixels per world unit at the distance the camera is from the system's plane.
        float d = Mathf.Max(0.01f, Vector3.Distance(cam.transform.position, ClosestOnDisc(cam.transform.position)));
        float pxPerUnit = Screen.height / (2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

        float major = builtRadius / MajorPerRadius;
        float minor = major / MinorPerMajor;
        float aMajor = MajorAlpha * Mathf.InverseLerp(MajorFadeFromPx, MajorFadeToPx, major * pxPerUnit);
        float aMinor = MinorAlpha * Mathf.InverseLerp(MinorFadeFromPx, MinorFadeToPx, minor * pxPerUnit);

        SetAlpha(majorMat, aMajor);
        SetAlpha(minorMat, aMinor);
        SetAlpha(ringMat, RingAlpha);
        ring.widthMultiplier = RingWidthPx / Mathf.Max(0.0001f, pxPerUnit);
    }

    // The nearest point of the grid's disc to the camera — so "how far away is the grid" means the part
    // of it under the camera when the camera is over the system, and its rim when it is not.
    Vector3 ClosestOnDisc(Vector3 p)
    {
        Vector3 c = transform.position;
        Vector3 flat = new Vector3(p.x - c.x, 0f, p.z - c.z);
        if (flat.sqrMagnitude > builtRadius * builtRadius) flat = flat.normalized * builtRadius;
        return c + flat;
    }

    static void SetAlpha(Material m, float a)
    {
        if (m == null) return;
        var c = m.color;
        if (Mathf.Abs(c.a - a) < 0.002f) return;
        c.a = a;
        m.color = c;
    }

    void Rebuild(float r)
    {
        builtRadius = r;
        if (mesh == null)
        {
            mesh = new Mesh { name = "SystemGrid" };
            mesh.MarkDynamic();
        }
        mesh.Clear();

        var verts = new List<Vector3>();
        var major = new List<int>();
        var minorIdx = new List<int>();

        float step = r / (MajorPerRadius * MinorPerMajor);
        int n = MajorPerRadius * MinorPerMajor;
        // k = 0 is the line through the star; |k| = n would be a zero-length chord on the rim.
        for (int k = -(n - 1); k <= n - 1; k++)
        {
            float x = k * step;
            float half = Mathf.Sqrt(Mathf.Max(0f, r * r - x * x));
            var into = (k % MinorPerMajor == 0) ? major : minorIdx;
            Chord(verts, into, new Vector3(x, 0f, -half), new Vector3(x, 0f, half));   // along Z
            Chord(verts, into, new Vector3(-half, 0f, x), new Vector3(half, 0f, x));   // along X
        }

        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32
                                               : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.subMeshCount = 2;
        mesh.SetIndices(major.ToArray(), MeshTopology.Lines, 0);
        mesh.SetIndices(minorIdx.ToArray(), MeshTopology.Lines, 1);
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        ring.positionCount = RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
    }

    static void Chord(List<Vector3> verts, List<int> into, Vector3 a, Vector3 b)
    {
        into.Add(verts.Count); verts.Add(a);
        into.Add(verts.Count); verts.Add(b);
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (majorMat != null) Destroy(majorMat);
        if (minorMat != null) Destroy(minorMat);
        if (ringMat != null) Destroy(ringMat);
    }
}
