using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// A free-look 3D globe of a body: drag to spin it in any direction, wheel to zoom.
//
// The planet already appears as a 3D sphere in the system view, but the camera there is locked to a
// 55-degree top-down pitch and cannot orbit — so a world's far side and both poles are simply not
// reachable. This window exists to look at the object itself rather than at its place in the system.
//
// HOW IT RENDERS. A dedicated Camera draws a private sphere into a RenderTexture, which a RawImage shows
// in the window. The alternative — a second Camera pointed at the real planet in the system — would fight
// the render tiers (the whole system de-renders at galaxy zoom, taking the subject with it) and would
// inherit the system's lighting and neighbours.
//
// WHERE THE STAGE LIVES. Far off at x = 500,000, not on a private layer.
//
// Layers would be the textbook answer and are the wrong one here: layer 8+ names are project settings, a
// script cannot reliably claim one, and silently rendering on a layer some other feature also uses is a
// bug that only shows up later. The main camera's far clip is `slant + GalaxyRadius*2.5 + 1000` — tens of
// thousands of units at most — so a stage half a million units away is beyond it by more than an order of
// magnitude and can never appear in the main view. The preview camera sits right next to the stage with a
// tight clip range, so it sees the globe and nothing else.
public class PlanetGlobeWindow : MonoBehaviour
{
    public static PlanetGlobeWindow Instance;

    // Far enough that the main camera's far plane cannot reach it at any zoom. That plane is
    // `slant + GalaxyRadius*2.5 + 1000`, which tops out around 36,000 with the current galaxy — so
    // 200,000 clears it by more than 5x while staying as close as precision allows.
    static readonly Vector3 StageOrigin = new Vector3(200000f, 0f, 0f);

    // The globe is built 100 units across rather than 1, and that is a float-precision decision.
    //
    // At 200,000 from the origin a float's spacing is ~0.024 units. On a 1-unit sphere with the camera a
    // few units away, that quantisation is a couple of percent of the subject — enough to shimmer visibly
    // as you drag. Scaling the whole stage up makes the same absolute error a rounding detail: 0.024
    // against a 100-unit globe is 0.02%. Everything else here is expressed in multiples of this, so the
    // framing is identical either way.
    const float GlobeScale = 100f;

    const int TexSize = 512;

    GameObject root;
    TMP_Text titleText;
    RawImage view;
    RenderTexture rt;

    Camera previewCam;
    Transform globe;
    Transform stage;
    Light keyLight;

    CelestialBody body;

    // Orbit state. Pitch is clamped just short of the poles: at exactly +/-90 the up vector degenerates
    // and the view rolls unpredictably as it passes through.
    float yaw = 20f;
    float pitch = 15f;
    float distance = 3.2f * GlobeScale;
    const float MinDistance = 1.6f * GlobeScale;
    const float MaxDistance = 8f * GlobeScale;
    const float PitchLimit = 88f;

    bool dragging;
    Vector3 lastPointer;

    public bool IsOpen => root != null && root.activeSelf;

    /// The live globe render, for anything that wants to show it without owning a stage of its own.
    public RenderTexture Texture => rt;

    // Frame-STAMPED rather than a bool, because MonoBehaviour LateUpdate order is undefined.
    //
    // With a boolean that this consumes, an embedded viewer whose LateUpdate happens to run after this
    // one has its request cleared before it is ever seen — the stage switches off for a frame and the
    // panel shows a stale render. A stamp tolerates either order: a request from this frame or the last
    // one counts, and it expires itself with no clearing step to get the order wrong.
    int wantFrame = -1;
    CelestialBody externalBody;

    /// Ask for the globe to be rendered this frame, for `b`, from somewhere other than this window.
    ///
    /// One stage serves every viewer rather than each building its own camera, sphere, light and
    /// RenderTexture. They all want the same thing — the selected body — so a second stage would be a
    /// second copy of the same picture at twice the cost. Callers must call this EVERY frame they are
    /// visible; the stage switches itself off on the first frame nobody asks, which is what stops it
    /// rendering behind a closed panel.
    public void RequestFrame(CelestialBody b)
    {
        if (b == null) return;
        wantFrame = Time.frameCount;
        externalBody = b;
    }

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("PlanetGlobeWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<PlanetGlobeWindow>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "Globe", new Vector2(460, 540), out root, out titleText);
        UIFactory.VerticalLayout(content, 6);

        // The render target.
        var holder = UIFactory.NewUI(content, "ViewHolder");
        UIFactory.AddLayout(holder, 400f);
        view = holder.AddComponent<RawImage>();
        view.color = Color.white;

        // Drag/scroll handling lives on the image itself, so it only claims input while the cursor is
        // actually over the globe — the rest of the window still scrolls and clicks normally.
        var input = holder.AddComponent<GlobeInput>();
        input.owner = this;

        UIFactory.Button(content, "Reset view", ResetView, 28);
        UIFactory.WrapText(content, "Drag to spin · wheel to zoom", UITheme.SmallSize, UITheme.SubText);

        BuildStage();
        root.SetActive(false);
    }

    void BuildStage()
    {
        stage = new GameObject("GlobeStage").transform;
        stage.position = StageOrigin;
        // Survives scene reloads alongside the window itself; destroyed in OnDestroy.
        stage.gameObject.hideFlags = HideFlags.DontSave;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Globe";
        sphere.transform.SetParent(stage, false);
        var col = sphere.GetComponent<Collider>();
        if (col != null) Destroy(col);
        globe = sphere.transform;

        var camGo = new GameObject("GlobePreviewCam");
        camGo.transform.SetParent(stage, false);
        previewCam = camGo.AddComponent<Camera>();
        previewCam.clearFlags = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
        previewCam.fieldOfView = 35f;
        previewCam.nearClipPlane = 0.05f * GlobeScale;
        previewCam.farClipPlane = 50f * GlobeScale;
        previewCam.enabled = false;          // rendered on demand, not every frame

        rt = new RenderTexture(TexSize, TexSize, 16) { name = "GlobeRT" };
        previewCam.targetTexture = rt;
        if (view != null) view.texture = rt;

        // A key light, so the globe reads as a lit sphere with a terminator rather than a flat disc —
        // the whole point of looking at it in 3D.
        //
        // POINT, not directional, and that is the fix for a real leak. A directional light has no
        // position: it lights every object in the scene regardless of distance, so a directional key here
        // would brighten the entire galaxy behind the window for as long as the window is open. A point
        // light has a finite range, and 6x the globe's own size cannot reach a main scene 200,000 units
        // away. Same lighting on the subject, no effect on anything else.
        var lightGo = new GameObject("GlobeKey");
        lightGo.transform.SetParent(stage, false);
        keyLight = lightGo.AddComponent<Light>();
        keyLight.type = LightType.Point;
        keyLight.intensity = 1.6f;
        keyLight.range = GlobeScale * 6f;
        keyLight.color = new Color(1f, 0.97f, 0.92f);

        stage.gameObject.SetActive(false);
    }

    public void Show(CelestialBody b)
    {
        if (b == null) return;
        body = b;
        if (root != null) root.SetActive(true);
        if (stage != null) stage.gameObject.SetActive(true);
        if (titleText != null) titleText.text = b.name;

        ApplyAppearance();
        ResetView();
    }

    public void Toggle()
    {
        if (IsOpen) { Hide(); return; }
        var sel = PlanetUI.Selected;
        if (sel != null) Show(sel);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        if (stage != null) stage.gameObject.SetActive(false);
    }

    void ApplyAppearance()
    {
        if (globe == null || body == null) return;
        // Reuse the same appearance the system view uses, so the globe is the SAME planet rather than a
        // second interpretation of it — same surface texture, same material treatment, same atmosphere.
        PlanetAppearance.Apply(body, globe.gameObject);
        globe.localScale = Vector3.one * GlobeScale;
    }

    public void ResetView()
    {
        yaw = 20f;
        pitch = 15f;
        distance = 3.2f * GlobeScale;
    }

    public void Orbit(float dYaw, float dPitch)
    {
        yaw += dYaw;
        pitch = Mathf.Clamp(pitch + dPitch, -PitchLimit, PitchLimit);
    }

    public void Zoom(float notches)
    {
        // Proportional, like the world camera: one notch feels the same at every distance.
        distance = Mathf.Clamp(distance * Mathf.Exp(-notches * 0.18f), MinDistance, MaxDistance);
    }

    void LateUpdate()
    {
        // Render if EITHER this window is open or an embedded viewer asked for a frame. The window's
        // built-in X deactivates `root` directly without going through Hide(), so the stage has to notice
        // on its own — otherwise the globe and its light stay alive and rendering behind a closed window.
        // A request from this frame or the previous one counts — see the note on wantFrame.
        bool wasExternal = wantFrame >= Time.frameCount - 1;
        bool want = IsOpen || wasExternal;

        if (!want)
        {
            if (stage != null && stage.gameObject.activeSelf) stage.gameObject.SetActive(false);
            return;
        }
        if (previewCam == null || globe == null) return;

        if (stage != null && !stage.gameObject.activeSelf) stage.gameObject.SetActive(true);

        // Last requester wins, whether or not this window is open.
        //
        // The alternative — letting the open window keep the subject — sounds more respectful of the
        // player's explicit choice and is worse: with one shared stage, the embedded globe in the
        // Inspector would then render a DIFFERENT planet than the stats printed underneath it. A viewer
        // showing the wrong world silently is a bug; a window whose title follows the selection is not.
        if (wasExternal && externalBody != null && externalBody != body)
        {
            body = externalBody;
            if (titleText != null) titleText.text = body.name;
            ApplyAppearance();
        }

        // Place the camera on its orbit around the globe and look inward.
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rot * new Vector3(0f, 0f, -distance);
        previewCam.transform.position = globe.position + offset;
        previewCam.transform.rotation = Quaternion.LookRotation(globe.position - previewCam.transform.position, Vector3.up);

        // Key light rides off the camera's shoulder, so there is always a visible terminator instead of a
        // fully-lit face (which is what a light sitting at the camera would give). A point light is
        // positional, so this places it rather than aiming it.
        if (keyLight != null)
        {
            Quaternion lightRot = Quaternion.Euler(pitch + 18f, yaw + 35f, 0f);
            keyLight.transform.position = globe.position + lightRot * new Vector3(0f, 0f, -GlobeScale * 2.2f);
        }

        previewCam.Render();
    }

    void OnDestroy()
    {
        if (stage != null) Destroy(stage.gameObject);
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (Instance == this) Instance = null;
    }
}

// Drag-to-spin and wheel-to-zoom, scoped to the globe image.
//
// Separate from the window so the handlers fire only while the cursor is over the render target. Putting
// them on the window root would swallow the wheel for the whole panel, and dragging the title bar to move
// the window would also spin the planet.
public class GlobeInput : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
{
    public PlanetGlobeWindow owner;

    const float DegPerPixel = 0.45f;

    public void OnPointerDown(PointerEventData e) { }
    public void OnPointerUp(PointerEventData e) { }

    public void OnDrag(PointerEventData e)
    {
        if (owner == null) return;
        // Vertical drag is inverted so dragging DOWN tips the north pole toward you, which is what
        // grabbing a physical globe does.
        owner.Orbit(-e.delta.x * DegPerPixel, e.delta.y * DegPerPixel);
    }

    public void OnScroll(PointerEventData e)
    {
        if (owner == null) return;
        owner.Zoom(e.scrollDelta.y);
    }
}
