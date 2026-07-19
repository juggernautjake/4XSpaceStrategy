using UnityEngine;

// The one black hole builder, shared by every place one is drawn: the detailed system view, the galaxy
// overview proxy, and the deep view where the galactic core stands in for the whole galaxy.
//
// It used to live privately inside SystemVisualizer as a static disc of seven concentric rings, which read
// as a flat orange target from directly above — and the galaxy view drew no black hole at all, just a
// near-black sphere on a black background. What makes this look like a black hole rather than a dark ball:
//
//   * The disc is TWO counter-tilted layers turning at different rates. A single rigid disc rotating as
//     one body reads as a solid object; differential rotation is what sells "matter falling in".
//   * Relativistic BEAMING — the side of the disc turning toward the camera is brighter. This is the
//     single most recognisable feature of a real black hole image and costs one dot product.
//   * The photon ring pulses slightly and always faces the camera, so the bright rim survives the fact
//     that the game is locked to a 55-degree top-down pitch and a flat ring would foreshorten to a line.
//   * The horizon is pure black and UNLIT, so it stays a hole rather than becoming a shaded grey sphere.
//
// Everything is built from primitives and LineRenderers. The project has no custom shaders and no
// Resources folder to load one from, so a lensing shader is not available here.
public static class BlackHoleVisual
{
    /// Build a black hole under `parent`, sized to `scale` world units across the event horizon.
    /// `withLight` is off for the galaxy-view proxies, where dozens of point lights would be wasteful and
    /// the detailed systems' lights are switched off anyway.
    /// `clickable` keeps the event horizon's own collider, for the one caller that attaches a
    /// StarInteraction to it (the detailed system view). The galaxy proxy and the deep view leave it off:
    /// they carry a single collider on their root, and a stray uncollidered-but-solid sphere inside would
    /// intercept the raycast and steal the hover from the component that handles it.
    public static GameObject Build(Transform parent, float scale, bool withLight = true,
                                   string name = "BlackHole", float lightIntensity = 0.9f,
                                   bool clickable = false)
    {
        scale = Mathf.Max(0.01f, scale);

        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;

        // --- Event horizon: pure black, unlit, so it reads as an absence rather than an object. ---
        var horizon = SpaceMaterials.Primitive(PrimitiveType.Sphere, root.transform, "EventHorizon",
                                               keepCollider: clickable);
        horizon.transform.localScale = Vector3.one * scale;
        var hr = horizon.GetComponent<Renderer>();
        // Fadeable so the tier crossfade can dissolve it, but keepDepth so it still OCCLUDES: without a
        // depth write its own accretion rings and the far-side jet sort in front of it and the hole reads
        // as a translucent grey ball with rings visible through the middle.
        if (hr != null)
            hr.material = SpaceMaterials.Unlit(new Color(0.005f, 0.005f, 0.01f, 1f),
                                               fadeable: true, keepDepth: true);

        // --- Accretion disc: two counter-tilted layers at different speeds. ---
        // Tilts differ by ~16 degrees so the layers cross rather than nest, which is what gives the disc
        // visible thickness from the game's fixed overhead pitch.
        BuildDiscLayer(root.transform, scale, tilt: 74f, speed: 38f, rings: 7,
                       inner: 0.72f, outer: 1.85f, alphaScale: 1f, seedPhase: 0f);
        BuildDiscLayer(root.transform, scale, tilt: 58f, speed: -23f, rings: 5,
                       inner: 0.95f, outer: 2.25f, alphaScale: 0.55f, seedPhase: 40f);

        // --- Photon ring: the bright rim hugging the horizon. ---
        var photon = SpaceMaterials.MakeRing(root.transform, "PhotonRing", scale * 0.62f,
                                             new Color(0.8f, 0.9f, 1f, 0.95f), scale * 0.05f, 128, additive: true);
        var billboard = photon.gameObject.AddComponent<CameraFacingRing>();
        billboard.pulseAmplitude = 0.10f;
        billboard.pulseSpeed = 0.9f;
        billboard.baseWidth = scale * 0.05f;

        // --- Halo: a faint warm glow giving the whole thing depth against a black sky. ---
        SpaceMaterials.MakeRing(root.transform, "Halo", scale * 2.15f,
                                new Color(1f, 0.45f, 0.2f, 0.16f), scale * 0.5f, 96, additive: true);

        // --- Polar jets: thin bright spikes along the spin axis. ---
        // Real black holes with an accretion disc throw relativistic jets, and they read instantly as
        // "black hole" from a top-down camera where the disc itself is foreshortened.
        BuildJet(root.transform, scale, 1f);
        BuildJet(root.transform, scale, -1f);

        if (withLight)
        {
            var lightGo = new GameObject("BHLight");
            lightGo.transform.SetParent(root.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.3f);
            light.intensity = lightIntensity;
            light.range = Mathf.Max(60f, scale * 26f);
        }

        return root;
    }

    // One tilted, rotating layer of the accretion disc. Hot blue-white at the inner edge fading to
    // orange-red at the outer, which is the real temperature gradient of infalling matter.
    static void BuildDiscLayer(Transform parent, float scale, float tilt, float speed, int rings,
                               float inner, float outer, float alphaScale, float seedPhase)
    {
        var disc = new GameObject($"AccretionDisc_{tilt:F0}");
        disc.transform.SetParent(parent, false);
        disc.transform.localRotation = Quaternion.Euler(tilt, seedPhase, 0f);

        for (int i = 0; i < rings; i++)
        {
            float t = rings > 1 ? i / (float)(rings - 1) : 0f;
            float r = Mathf.Lerp(scale * inner, scale * outer, t);
            Color c = Color.Lerp(new Color(1f, 0.97f, 0.92f), new Color(1f, 0.28f, 0.06f), t);
            c.a = Mathf.Lerp(0.95f, 0.30f, t) * alphaScale;
            float w = Mathf.Lerp(scale * 0.09f, scale * 0.24f, t);
            var lr = SpaceMaterials.MakeRing(disc.transform, "Acc" + i, r, c, w, 96, additive: true);

            // Relativistic beaming: brighten the half of the ring rotating toward the viewer. Applied
            // per-vertex through the LineRenderer gradient, re-evaluated as the disc turns.
            var beam = lr.gameObject.AddComponent<DiscBeaming>();
            beam.baseColor = c;
            beam.spinSign = Mathf.Sign(speed);
        }

        var spin = disc.AddComponent<SelfSpin>();
        spin.speed = speed;
        spin.unscaled = true;   // a black hole is scenery, not simulation — it keeps turning while paused
    }

    // A jet: a thin cone stretched along the spin axis, brightest at the base.
    static void BuildJet(Transform parent, float scale, float dir)
    {
        var jet = SpaceMaterials.Primitive(PrimitiveType.Cylinder, parent, dir > 0 ? "JetUp" : "JetDown");
        // Unity's cylinder is 2 units tall, so a y-scale of 1.5*scale gives a jet 3*scale long; it is
        // offset by half that so its base sits at the horizon rather than passing through it.
        jet.transform.localScale = new Vector3(scale * 0.10f, scale * 1.5f, scale * 0.10f);
        jet.transform.localPosition = new Vector3(0f, dir * scale * 1.5f, 0f);
        var r = jet.GetComponent<Renderer>();
        if (r != null) r.material = SpaceMaterials.Additive(new Color(0.6f, 0.8f, 1f, 0.22f));
    }
}

// Keeps a ring facing the camera, and optionally breathes its width.
//
// The game's camera is locked to a 55-degree pitch, so a ring authored flat in XZ is always seen at a
// slant. For the photon ring — which is meant to be the circular bright rim of the hole — that slant is
// wrong: it should be a circle from every angle, because it is light bent around the sphere rather than a
// physical hoop lying in a plane.
public class CameraFacingRing : MonoBehaviour
{
    public float pulseAmplitude = 0f;
    public float pulseSpeed = 1f;
    public float baseWidth = 0.1f;

    LineRenderer lr;
    Camera cam;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        if (baseWidth <= 0f && lr != null) baseWidth = lr.startWidth;
    }

    void LateUpdate()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        // Face the camera. The ring's own plane is its local XZ, so its local UP must point at the camera.
        Vector3 toCam = cam.transform.position - transform.position;
        if (toCam.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.FromToRotation(Vector3.up, toCam.normalized);

        if (lr != null && pulseAmplitude > 0f)
        {
            float w = baseWidth * (1f + Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) * pulseAmplitude);
            lr.startWidth = lr.endWidth = Mathf.Max(0.001f, w);
        }
    }
}

// Relativistic beaming across one accretion ring.
//
// Matter on the side of the disc rotating TOWARD the camera is Doppler-boosted and appears far brighter
// than the receding side — the asymmetry that makes a black hole image unmistakable. The real effect goes
// as roughly the fourth power of the Doppler factor; this approximates it with a cosine ramp along the
// ring, which is enough to read correctly at game scale.
//
// Costs one gradient rebuild per ring per frame, on a handful of rings, and only while a black hole is on
// screen — the disc is destroyed with its system when the tier de-renders.
public class DiscBeaming : MonoBehaviour
{
    public Color baseColor = Color.white;
    public float spinSign = 1f;
    public float strength = 1.6f;      // how much brighter the approaching side gets

    // Tier-crossfade multiplier, driven by the FadeGroup above this ring.
    //
    // This has to live here rather than in FadeGroup because this component rewrites colorGradient every
    // LateUpdate, and colorGradient overrides startColor/endColor — so a FadeGroup setting those would be
    // silently discarded a frame later, and the disc would stay at full brightness through a fade-out.
    // FadeGroup finds these components and writes `fade` instead.
    [HideInInspector] public float fade = 1f;

    LineRenderer lr;
    Camera cam;
    Gradient grad;
    GradientColorKey[] ck;
    GradientAlphaKey[] ak;

    const int Keys = 8;   // Unity's Gradient caps at 8 colour keys

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        grad = new Gradient();
        ck = new GradientColorKey[Keys];
        ak = new GradientAlphaKey[Keys];
    }

    void LateUpdate()
    {
        if (lr == null) return;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        // Where the camera sits, expressed in the ring's own rotating frame. As the disc spins, this
        // sweeps around, which is what animates the bright side.
        Vector3 local = transform.InverseTransformPoint(cam.transform.position);
        float camAngle = Mathf.Atan2(local.z, local.x);

        for (int i = 0; i < Keys; i++)
        {
            float t = i / (float)(Keys - 1);
            float ringAngle = t * Mathf.PI * 2f;
            // Velocity at this point is tangential; it approaches the camera most strongly a quarter turn
            // from the camera's own bearing, with the sign set by which way the disc spins.
            float approach = Mathf.Sin(ringAngle - camAngle) * spinSign;
            float boost = Mathf.Lerp(0.35f, strength, (approach + 1f) * 0.5f);

            Color c = baseColor * boost;
            c.a = baseColor.a;
            ck[i] = new GradientColorKey(c, t);
            ak[i] = new GradientAlphaKey(
                Mathf.Clamp01(baseColor.a * Mathf.Lerp(0.5f, 1.3f, (approach + 1f) * 0.5f) * fade), t);
        }

        grad.SetKeys(ck, ak);
        lr.colorGradient = grad;
    }
}
