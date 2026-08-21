using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WHAT THE FORMATION WOULD ACTUALLY LOOK LIKE
//
// The command bar describes six formations in words and diagrams them in icons, and neither answers
// the question a player is really asking. A wedge of four and a wedge of eleven are different shapes.
// A screen depends on which of YOUR hulls are the cheap ones. A globe only means anything once you
// can see how wide it is against the world you are parked at. All of that is knowable before you
// commit, and none of it was shown — so choosing a formation was a guess followed by watching the
// fleet rearrange itself to find out.
//
// Hover a formation button and its stations appear on the map: one ring per ship, in the real
// positions, computed by the real function.
//
// ---- THE REAL FUNCTION, NOT AN ILLUSTRATION ---------------------------------------------------
//
// FleetFormation.PreviewStation is the same arithmetic Offset uses in flight, with the slot handed in
// rather than read off a Unit. Drawing an approximation instead would be a second implementation of
// the formations that nobody would remember to keep in step — and it would be wrong in exactly the
// way that matters, since the whole point of the preview is to be believed.
//
// ---- WHY THE LEADER IS MARKED ------------------------------------------------------------------
//
// Slot 0 is filled by the CHEAPEST hull (see FleetFormation.Assign), which is what makes Screen and
// Globe put the expendable ships in front without either formation knowing anything about ship
// classes. On a wedge that means the point of the wedge is your cheapest ship, which is a genuinely
// surprising fact worth showing rather than explaining.
// ============================================================================================
public class FormationPreview : MonoBehaviour
{
    public static FormationPreview Instance;

    const int Segments = 20;

    /// How big each station ring is drawn. Small — these are markers, not hulls, and a ring the size
    /// of a ship would hide the ship standing in it.
    const float RingRadius = 0.22f;

    class Ring
    {
        public LineRenderer lr;
    }

    readonly List<Ring> rings = new List<Ring>();
    readonly List<Vector3> stations = new List<Vector3>();
    Material mat;

    bool showing;
    int squadron;
    FleetFormationKind kind;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("FormationPreview");
        Instance = go.AddComponent<FormationPreview>();
        // Vanishes at galaxy zoom, like every other fleet visual — at that tier a squadron is a dot
        // and its formation is not a thing anybody is choosing.
        go.AddComponent<MapTierVisibility>();
    }

    void Awake()
    {
        Instance = this;
        mat = new Material(Shader.Find("Sprites/Default"));
    }

    /// Start showing what `kind` would do to squadron `g`.
    public void Show(int g, FleetFormationKind k)
    {
        squadron = g; kind = k; showing = g >= 1;
    }

    public void Hide() { showing = false; }

    void LateUpdate()
    {
        // AFTER the hulls have moved, so the rings sit on the ships rather than a frame behind them.
        int want = showing ? Build() : 0;

        for (int i = 0; i < rings.Count; i++)
        {
            bool live = i < want;
            if (rings[i].lr.enabled != live) rings[i].lr.enabled = live;
        }
    }

    /// Work out the stations and draw them. Returns how many rings are in use.
    int Build()
    {
        var members = ControlGroups.Members(squadron);
        if (members == null || members.Count < 2) return 0;

        var um = UnitManager.Instance;
        if (um == null) return 0;

        // ---- where the formation would sit, and which way it would face ----
        //
        // The squadron's own centre, and its own heading if anything in it is under way. A stationary
        // squadron has no course, so the preview faces the way the CAMERA does — which is the only
        // orientation a player looking at the screen can reason about, and far better than an
        // arbitrary world axis that would point the wedge off into a corner.
        Vector3 centre = Vector3.zero;
        Vector3 heading = Vector3.zero;
        int n = 0;
        foreach (var u in members)
        {
            if (u == null || u.IsDestroyed) continue;
            centre += CombatManager.PosOf(u);
            heading += UnitModelRenderer.VelocityOf(u);
            n++;
        }
        if (n == 0) return 0;
        centre /= n;

        if (heading.sqrMagnitude < 0.01f)
        {
            var cam = Camera.main;
            heading = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
        }
        heading.Normalize();

        // Spacing off the biggest hull present, exactly as the live formation does — a formation of
        // dreadnoughts is genuinely wider than one of fighters, and a preview that used one number for
        // both would misrepresent the shape it is there to show.
        float spacing = 0f;
        foreach (var u in members)
            if (u?.Info != null) spacing = Mathf.Max(spacing, 0.34f + u.Info.health / 2600f);

        stations.Clear();
        for (int slot = 0; slot < n && slot < 24; slot++)
            stations.Add(centre + FleetFormation.PreviewStation(kind, slot, n, heading, spacing));

        while (rings.Count < stations.Count) MakeRing();

        for (int i = 0; i < stations.Count; i++)
        {
            // Slot 0 is the cheapest hull and the one that ends up at the sharp end. Marked brighter,
            // because "the point of your wedge is your least valuable ship" is a real consequence of
            // how the slots are assigned and not at all obvious.
            var col = i == 0 ? new Color(1f, 0.86f, 0.35f, 0.95f)
                             : new Color(0.50f, 0.86f, 1f, 0.70f);
            Draw(rings[i].lr, stations[i], col);
        }
        return stations.Count;
    }

    void Draw(LineRenderer lr, Vector3 c, Color col)
    {
        for (int i = 0; i <= Segments; i++)
        {
            float a = (i / (float)Segments) * Mathf.PI * 2f;
            lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * RingRadius, 0f, Mathf.Sin(a) * RingRadius));
        }
        lr.startColor = lr.endColor = col;
    }

    void MakeRing()
    {
        var go = new GameObject("Station");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = false;                    // the extra segment closes it; loop double-draws the seam
        lr.positionCount = Segments + 1;
        lr.widthMultiplier = 0.035f;
        lr.material = new Material(mat);
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        rings.Add(new Ring { lr = lr });
    }
}
