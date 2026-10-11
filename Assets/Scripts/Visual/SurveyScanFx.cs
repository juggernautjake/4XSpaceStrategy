using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// WHAT A SURVEY LOOKS LIKE FROM SPACE (2026-10-11)
//
// "Render ongoing research on every kind of world, not just a progress bar."
//
// Every ship working a survey draws two things on the world it is working:
//
//   THE SWEEP RING — a band around the globe at the LATITUDE the survey is working right now, taken
//     from the same block maths the Planet View reveals with (Survey.FrontOf). Level 1 starts on the
//     equator and works outward band by band exactly as the map uncovers, so the ring visibly climbs
//     and drops as the survey proceeds. Level 1 is a COARSE ring (a 24-sided polygon, thick); a deep
//     survey is FINER (a 64-sided pair of hairlines, in the colour of the index being read — the same
//     colour its overlay draws in the Planet View).
//   THE SCAN FAN — a narrow V from the ship's hull to the ring on the ship's side of the world,
//     sweeping side to side on the fleet beat (FleetClock), so the hull reads as the thing doing the
//     work.
//
// PER WORLD TYPE, so each looks like what is being done to it:
//   rocky / barren / moon / asteroid  cyan, on the surface — ground mapping
//   ocean                             aqua, on the surface
//   ice                               pale ice-white
//   volcanic                          hot orange
//   runaway greenhouse                sulphur yellow, the ring lifted onto the cloud deck and dimmer —
//                                     the scan is going through haze, not across ground
//   gas giant                         amber, the ring lifted well above the disc and a second ring
//                                     below it: atmospheric probing of the cloud tops, not ground
//                                     mapping (a gas giant is surveyed — it has a grid — but there is no
//                                     ground to chart)
//
// PIXEL-ART CONSISTENT: flat colours, hard polygon rings, no glow textures, and the brightness steps
// in four levels on the beat rather than fading smoothly — the running lights' language.
//
// CHEAP: one pooled pair of LineRenderers per surveying ship, positions written in place with
// SetPosition (no arrays, no allocation per frame). Hidden at galaxy zoom, during the intro, and
// whenever the ship or the world is not drawn (their ConcealBindings: fog of war, Dev hides).
// ============================================================================================
public class SurveyScanFx : MonoBehaviour
{
    public static SurveyScanFx Instance;

    const int CoarseSides = 24, FineSides = 64;

    class Fx
    {
        public GameObject root;
        public LineRenderer ringA, ringB, fan;
    }

    readonly List<Fx> pool = new List<Fx>();
    Material mat;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("SurveyScanFx").AddComponent<SurveyScanFx>();
    }

    void Awake()
    {
        Instance = this;
        mat = new Material(Shader.Find("Sprites/Default"));
    }

    LineRenderer NewLine(Transform parent, string name, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;   // hard corners: the polygon reads as pixel geometry
        lr.textureMode = LineTextureMode.Stretch;
        return lr;
    }

    Fx Make()
    {
        var fx = new Fx();
        fx.root = new GameObject("Scan");
        fx.root.transform.SetParent(transform, false);
        fx.ringA = NewLine(fx.root.transform, "RingA", true);
        fx.ringB = NewLine(fx.root.transform, "RingB", true);
        fx.fan = NewLine(fx.root.transform, "Fan", false);
        fx.fan.positionCount = 3;
        // Wide at the world, a point at the hull.
        fx.fan.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.15f), new Keyframe(1f, 1f));
        fx.root.SetActive(false);
        pool.Add(fx);
        return fx;
    }

    void LateUpdate()
    {
        var um = UnitManager.Instance;
        bool hideAll = um == null || GenesisSequence.Running || GenesisCamera.Active || !GalaxyLOD.SystemMode;

        int used = 0;
        if (!hideAll)
        {
            var units = um.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!Survey.IsWorking(u)) continue;
                var b = u.location;
                if (b == null || b.visualObject == null || !b.visualObject.activeInHierarchy) continue;
                if (!VisibilityService.ShownToPlayer(u)) continue;
                var bind = b.visualObject.GetComponent<ConcealBinding>();
                if (bind != null && bind.Concealed) continue;
                var ship = UnitVisuals.TransformOf(u);
                if (ship == null) continue;
                if (!Survey.FrontOf(b, u, out float row01, out float along01)) continue;

                var fx = used < pool.Count ? pool[used] : Make();
                used++;
                Draw(fx, u, b, ship.position, row01, along01, i);
            }
        }

        for (int i = used; i < pool.Count; i++)
            if (pool[i].root.activeSelf) pool[i].root.SetActive(false);
    }

    void Draw(Fx fx, Unit u, CelestialBody b, Vector3 shipPos, float row01, float along01, int seed)
    {
        if (!fx.root.activeSelf) fx.root.SetActive(true);
        bool deep = u.status == UnitStatus.Researching;

        var t = b.visualObject.transform;
        Vector3 c = t.position;
        Vector3 up = t.up, e1 = t.right, e2 = t.forward;
        float r = Mathf.Max(0.05f, OrbitSafety.DiscRadius(b));

        // ---- what kind of world, and so where the scan sits and what colour it is ----
        bool gas = b.type == CelestialBodyType.GasGiant;
        bool haze = !gas && AtmosphereRules.IsRunawayGreenhouse(b);
        float lift = gas ? 1.10f : haze ? 1.06f : 1.015f;
        Color col = TypeColor(b, haze);
        if (deep)
        {
            var k = Survey.CurrentIndex(b);
            if (k != SurfaceIndexKind.None) col = SurfaceIndex.Outline(k);
            col.a = 1f;
        }

        // Four brightness steps on the beat, not a smooth fade — the running lights' language.
        float beat = FleetClock.Beats + seed * 0.25f;
        float step = Mathf.Floor(Mathf.Repeat(beat * 2f, 4f)) / 3f;            // 0, 1/3, 2/3, 1
        float alpha = (haze ? 0.45f : 0.75f) * Mathf.Lerp(0.6f, 1f, step);

        // ---- the ring, at the working latitude (row 0 is the bottom of the map: the south pole) ----
        float lat = (row01 - 0.5f) * Mathf.PI * 0.94f;                         // never quite the pole point
        float rr = r * lift;
        Vector3 centre = c + up * (rr * Mathf.Sin(lat));
        float ringR = Mathf.Max(rr * 0.08f, rr * Mathf.Cos(lat));

        int sides = deep ? FineSides : CoarseSides;
        float width = r * (deep ? 0.012f : 0.03f);
        Ring(fx.ringA, centre, e1, e2, ringR, sides, width, col, alpha);

        // A second ring: the deep survey's finer pair, or a gas giant's lower probing layer.
        if (deep || gas)
        {
            float lat2 = deep ? lat - 0.06f : lat;
            float rr2 = deep ? rr : r * 1.04f;
            Vector3 centre2 = c + up * (rr2 * Mathf.Sin(lat2));
            float ringR2 = Mathf.Max(rr2 * 0.08f, rr2 * Mathf.Cos(lat2));
            Ring(fx.ringB, centre2, e1, e2, ringR2, sides, width * (deep ? 1f : 0.6f), col, alpha * 0.7f);
        }
        else if (fx.ringB.enabled) fx.ringB.enabled = false;

        // ---- the fan, from the hull to the ring on the hull's side, sweeping on the beat ----
        Vector3 toShip = shipPos - centre;
        Vector3 flat = toShip - up * Vector3.Dot(toShip, up);
        if (flat.sqrMagnitude < 1e-6f) flat = e1;
        float baseAng = Mathf.Atan2(Vector3.Dot(flat, e2), Vector3.Dot(flat, e1));
        // The sweep rides the ring with the survey's own progress along the band, so it creeps forward
        // as the front advances rather than only wagging in place.
        float sweep = Mathf.Sin((FleetClock.Beats + seed) * Mathf.PI * 0.5f) * 0.45f + (along01 - 0.5f) * 0.3f;
        float half = deep ? 0.10f : 0.18f;
        Vector3 a = centre + (e1 * Mathf.Cos(baseAng + sweep - half) + e2 * Mathf.Sin(baseAng + sweep - half)) * ringR;
        Vector3 z = centre + (e1 * Mathf.Cos(baseAng + sweep + half) + e2 * Mathf.Sin(baseAng + sweep + half)) * ringR;
        if (!fx.fan.enabled) fx.fan.enabled = true;
        fx.fan.SetPosition(0, a);
        fx.fan.SetPosition(1, shipPos);
        fx.fan.SetPosition(2, z);
        fx.fan.widthMultiplier = width * 1.2f;
        var fc = col; fc.a = alpha * 0.55f;
        fx.fan.startColor = fc; fx.fan.endColor = fc;
    }

    static void Ring(LineRenderer lr, Vector3 centre, Vector3 e1, Vector3 e2, float radius, int sides, float width, Color col, float alpha)
    {
        if (!lr.enabled) lr.enabled = true;
        if (lr.positionCount != sides) lr.positionCount = sides;
        float stepA = Mathf.PI * 2f / sides;
        for (int i = 0; i < sides; i++)
        {
            float ang = i * stepA;
            lr.SetPosition(i, centre + (e1 * Mathf.Cos(ang) + e2 * Mathf.Sin(ang)) * radius);
        }
        lr.widthMultiplier = width;
        col.a = alpha;
        lr.startColor = col; lr.endColor = col;
    }

    static Color TypeColor(CelestialBody b, bool haze)
    {
        if (haze) return new Color(1.00f, 0.88f, 0.40f, 1f);
        switch (b.type)
        {
            case CelestialBodyType.GasGiant: return new Color(1.00f, 0.74f, 0.36f, 1f);
            case CelestialBodyType.OceanPlanet: return new Color(0.30f, 0.86f, 1.00f, 1f);
            case CelestialBodyType.IcePlanet: return new Color(0.82f, 0.95f, 1.00f, 1f);
            case CelestialBodyType.VolcanicPlanet: return new Color(1.00f, 0.52f, 0.28f, 1f);
            default: return new Color(0.45f, 0.90f, 1.00f, 1f);   // rocky, barren, moons, asteroids
        }
    }
}
