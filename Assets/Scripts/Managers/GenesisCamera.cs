using UnityEngine;

// ============================================================================================
// THE SEQUENCE FILMS THE REAL WORLD
//
// This is the piece the intro was missing. Until now the loading screen showed a PRIVATE STAGE — a
// sphere, two corona quads and a key light parked at (-200000, 0, 0), rendered to a RenderTexture and
// shown as a UI image — and cross-faded that into the real game at the end. It was a careful illusion,
// and it was still an illusion: the planet you watched form was not the planet you then played, and
// the moons were cosmetic spheres at invented radii rather than your real moons on their real orbits.
//
// So they could never be "in perfect orbit sequence" with the game, because they were not the same
// objects. Matching them more closely was always going to be chasing the problem rather than fixing it.
//
// This class points the REAL camera at the REAL bodies. That single change settles three things at
// once, none of which needed any further code:
//
//   * The planet forming IS the homeworld. Not a match — the same GameObject.
//   * The moons ARE the moons, at their generated radii, speeds and phases, so the arrangement you
//     watch is the arrangement you are handed.
//   * The world gets a TERMINATOR — a real day/night line, because it is lit by its real star. That is
//     the main cue that a planet is moving at all when the camera tracks it (the brief's own §5.5
//     warning), and it was impossible on a stage with a fixed key light.
//
// WHAT IT DOES NOT DO. It does not fight CameraController. It takes exclusive ownership for the length
// of the sequence and hands back cleanly, because two things easing the same transform toward different
// targets is a fight neither wins.
// ============================================================================================
public class GenesisCamera : MonoBehaviour
{
    public static GenesisCamera Instance;

    Camera cam;
    CameraController rig;

    /// True while the sequence owns the camera. CameraController checks this and stands down.
    public static bool Active { get; private set; }

    // ---- Framing spec (the brief's Part 5, in one place so it can be tuned by eye) ---------------
    //
    // The anchor is expressed as a FRACTION OF VIEWPORT HEIGHT rather than in pixels, so "about the size
    // of a dollar coin next to the loading bar" holds at any resolution and any aspect ratio. A US
    // dollar coin is 26.5mm across and a 24-inch 1080p monitor is ~299mm tall, hence ~9%.
    public const float HomeworldScreenFraction = 0.09f;

    /// Where the subject sits across the screen. 0 = left edge, 0.5 = centre, 1 = right edge. The bar
    /// owns the middle, so the subject sits left of it.
    public const float SubjectAnchorX = 0.35f;

    /// THE ANGLE IS FIXED FOR THE WHOLE SEQUENCE, AND IT IS THE GAME'S OWN ANGLE.
    ///
    /// Pitch comes from CameraController.Pitch — the same 55° the world camera uses in play — so nothing
    /// tilts when control is handed over. Yaw is pinned to one value for the duration rather than read
    /// from wherever the camera happened to be pointing, because the sequence starts from an arbitrary
    /// pose: whatever the main menu, a previous game, or the player's last session left behind. Read
    /// live, the star would be framed from one bearing, the planet from another, and the "same world,
    /// different moment" illusion would break on the first cut.
    ///
    /// Zero is deliberate and not arbitrary: it is the bearing CameraController.ResetRotation returns to,
    /// so the intro ends looking the way the game's own "reset view" looks. Every beat therefore shares
    /// one angle, and the handover is a continuation rather than a correction.
    public const float SequenceYaw = 0f;

    /// Everything keeps its TRUE relative size within one framing — a bigger homeworld really does look
    /// bigger. But the true ratios are unusable: a real star is ~100x a planet and a real moon is a
    /// fraction of a percent of its world. So the ladder is compressed ONCE, here, and everything
    /// derives from it, which is what makes "consistently relative to each other" actually hold.
    public const float StarSizeRelativeToHome = 1.9f;   // a sun reads "a bit bigger", per the brief

    // The closing move: the homeworld swells as it travels to centre, then settles back as the camera
    // pulls out and the galaxy arrives.
    public const float CentreGrowth = 1.30f;

    Transform subject;          // what we are framing right now
    float subjectFraction;      // how much of the viewport height it should fill
    float anchorX = SubjectAnchorX;

    // Eased, not snapped. Everything in this sequence moves with a crescendo and a decrescendo — a
    // linear move reads as a machine panning, which is the one thing the intro must not look like.
    float easeSeconds;
    float easeClock;
    Vector3 fromPos;
    Quaternion fromRot;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("GenesisCamera");
        Instance = go.AddComponent<GenesisCamera>();
    }

    void Awake() { Instance = this; }

    /// Take the camera. Player input and CameraController's own easing stand down until Release.
    public void Begin()
    {
        cam = Camera.main;
        rig = CameraController.Instance;
        if (cam == null) return;

        Active = true;
        aliveClock = 0f;
        // Drop any follow the player had — otherwise CameraController's LateUpdate keeps re-centring on
        // whatever they were watching and drags the shot off its mark every frame.
        rig?.ClearFocus();

        // Put the rig on the sequence's bearing before the first shot is composed. The rig keeps its own
        // yaw (KeepCameraAngle rebuilds the rotation from it every frame in play), so handing control
        // back on a different bearing than the rig believes it has would snap the view the instant the
        // player moves. Setting it here means the angle is consistent through the sequence AND across
        // the handover.
        if (rig != null) rig.Yaw = SequenceYaw;
    }

    /// Hand the camera back, leaving the player exactly where the sequence left them.
    ///
    /// The rig is SYNCED before it is re-enabled rather than after: CameraController's zoom easing works
    /// from `targetHeight`, and if that still held a value from before the sequence, the first frame of
    /// player control would glide the camera away from the shot the intro just finished composing.
    public void Release(Transform focusOn)
    {
        Active = false;
        aliveClock = 0f;
        if (rig == null || cam == null) return;

        // The FRACTION matters. Without it SnapFocus defaults to 0 and falls through to its
        // "frame the whole neighbourhood" branch — which for a homeworld means its moons' orbits too, so
        // the planet would jump to a different size and slide to centre in a single frame, at exactly
        // the moment this is supposed to leave the player where the sequence left them.
        if (focusOn != null) rig.SnapFocus(focusOn, true, subjectFraction);
        else rig.SyncToCurrentPose();
    }

    // ---- Framing ---------------------------------------------------------------------------------

    /// Frame `t` so it occupies `screenFraction` of the viewport HEIGHT, anchored at `anchor` across the
    /// width. Snaps immediately — use for the first shot of the sequence.
    public void Frame(Transform t, float screenFraction, float anchor)
    {
        // `cam` too, not just `t` — Begin() early-returns when Camera.main is missing, and ApplyPose
        // dereferences cam.transform. Without this the very first shot of the intro throws.
        if (t == null || cam == null) return;
        subject = t; subjectFraction = screenFraction; anchorX = anchor;
        easeSeconds = 0f;
        ApplyPose(SolvePose(t, screenFraction, anchor));
    }

    /// Ease to a new subject / framing over `seconds`. This is Beat 4 — the camera sliding off the star
    /// and onto the homeworld while generation carries on behind it.
    public void EaseTo(Transform t, float screenFraction, float anchor, float seconds)
    {
        if (cam == null) return;
        fromPos = cam.transform.position;
        fromRot = cam.transform.rotation;
        subject = t; subjectFraction = screenFraction; anchorX = anchor;
        easeSeconds = Mathf.Max(0.01f, seconds);
        easeClock = 0f;
    }

    public bool Easing => easeClock < easeSeconds;

    void LateUpdate()
    {
        if (!Active || cam == null || subject == null) return;

        // Re-solved EVERY FRAME, not once at the start of the ease. The subject is orbiting — that is
        // the entire point — so a pose computed once would be stale before the ease finished, and the
        // planet would drift off its mark as it travelled.
        var target = SolvePose(subject, subjectFraction, anchorX);

        if (easeClock < easeSeconds)
        {
            easeClock += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(easeClock / easeSeconds);
            // Smoothstep in and out. The brief asks for crescendo and decrescendo on every move; this is
            // the cheapest honest version of it, and the curve is the same on every beat so the whole
            // sequence feels like one hand rather than several.
            k = k * k * (3f - 2f * k);
            cam.transform.position = Vector3.Lerp(fromPos, target.pos, k);
            cam.transform.rotation = Quaternion.Slerp(fromRot, target.rot, k);
        }
        else ApplyPose(target);

        // THE VIEW VOLUME HAS TO FOLLOW THE CAMERA.
        //
        // CameraController.Update normally does this every frame, and it is standing down for us — so
        // without this the near and far planes stay wherever the player last left them. A player who
        // pressed Home before starting a new game leaves a near plane of ~10 units; the intro then films
        // a homeworld from ~10 units away, entirely inside it, and renders nothing at all. The far plane
        // fails the same way in reverse on the closing pull-back, clipping the galaxy away during the
        // exact beat it is meant to arrive.
        //
        // The rig derives the planes from max(position.y, targetHeight), so targetHeight is kept in step
        // with where the sequence has actually put the camera — a stale high value would re-introduce
        // the near-plane failure it is there to prevent.
        if (rig != null)
        {
            rig.SyncToCurrentPose();
            rig.UpdateClipPlanes();
        }

        // WATCHDOG. `Active` disables the player's camera outright, and it is a process-wide static: if
        // the driving coroutine is stopped or throws between Begin and Release — and StopAllCoroutines
        // is a real thing that happens to the loading singleton — the camera would stay frozen for the
        // rest of the session with no way back. An intro that overruns this has failed anyway.
        aliveClock += Time.unscaledDeltaTime;
        if (aliveClock > MaxSequenceSeconds)
        {
            Debug.LogWarning("[GenesisCamera] Sequence overran its watchdog; returning control.");
            Release(subject);
        }
    }

    /// Longest the sequence may hold the camera before control is handed back regardless.
    const float MaxSequenceSeconds = 180f;
    float aliveClock;

    void ApplyPose((Vector3 pos, Quaternion rot) p)
    {
        cam.transform.position = p.pos;
        cam.transform.rotation = p.rot;
    }

    // ============================================================================================
    // THE SOLVE
    //
    // Two requirements at once: the subject must fill a given fraction of the viewport HEIGHT, and sit
    // at a given fraction across its WIDTH.
    //
    // SIZE fixes the distance. A sphere of radius r subtends the fraction f of a vertical FOV V when
    //     d = r / tan(f * V / 2)
    //
    // POSITION is then a sideways offset at that distance. Half the view's width in world units at
    // distance d is  d * tan(V/2) * aspect, so moving the camera laterally by
    //     lateral = halfWidth * (0.5 - anchorX) * 2
    // slides the subject to `anchorX` across the screen.
    //
    // Height-based deliberately: the render target's aspect changes with the window, and anchoring the
    // SIZE to width would make the planet grow and shrink as the player resized. Height is the stable
    // axis, which is also why the brief's dollar-coin measure is expressed against screen height.
    // ============================================================================================
    (Vector3 pos, Quaternion rot) SolvePose(Transform t, float screenFraction, float anchor)
    {
        float vfov = cam.fieldOfView * Mathf.Deg2Rad;
        float r = WorldRadius(t);
        float d = DistanceForFraction(r, vfov, screenFraction);

        // The sequence keeps the world camera's authored pitch so that when control is handed over
        // nothing tilts. Only the distance and the lateral offset are ours.
        // One fixed angle for every beat — see SequenceYaw. NOT the camera's live yaw: reading that
        // would let the shot's bearing depend on wherever the previous session left the camera.
        Quaternion rot = Quaternion.Euler(CameraController.Pitch, SequenceYaw, 0f);
        Vector3 forward = rot * Vector3.forward;

        float halfHeightWorld = d * Mathf.Tan(vfov * 0.5f);
        float halfWidthWorld = halfHeightWorld * Mathf.Max(0.1f, cam.aspect);
        float lateral = halfWidthWorld * (0.5f - anchor) * 2f;
        Vector3 right = rot * Vector3.right;

        Vector3 pos = t.position - forward * d + right * lateral;
        return (pos, rot);
    }

    // ============================================================================================
    // HOW FAR AWAY A BODY MUST BE TO FILL A GIVEN FRACTION OF THE SCREEN
    //
    // PERSPECTIVE IS LINEAR IN tan, NOT IN THE ANGLE. That is the whole subtlety, and getting it wrong
    // is invisible in code and obvious on screen.
    //
    // The tempting version — "the body should subtend fraction f of the FOV, so d = r / tan(f·V/2)" —
    // treats screen height as proportional to angle. It is not. A point at camera-space (y, z) lands at
    // NDC y' = (y/z) / tan(V/2), so the viewport's world half-height at depth d is d·tan(V/2), and a
    // sphere of radius r covers
    //
    //     f = r / (√(d² − r²) · tan(V/2))
    //
    // Solving for d, with u = f·tan(V/2):
    //
    //     d = r·√(1 + u²) / u
    //
    // The naive form is short by a factor of (V/2)/tan(V/2) — 0.907 at a 60° FOV, so the subject renders
    // about 9% smaller than asked for, and worse as the FOV widens (0.79 at 90°). It is exact only at
    // f = 1, which is why it survives casual testing.
    //
    // CameraController.SnapFocus carries the identical solve, and the two MUST agree: the loading
    // handoff matches an on-screen size across a cut, so a 9% disagreement is a visible pop.
    // ============================================================================================
    public static float DistanceForFraction(float radius, float vfovRadians, float screenFraction)
    {
        float t2 = Mathf.Tan(Mathf.Max(0.01f, vfovRadians) * 0.5f);
        float u = Mathf.Max(1e-4f, Mathf.Clamp01(screenFraction)) * t2;
        return radius * Mathf.Sqrt(1f + u * u) / u;
    }

    /// The body's real drawn radius. Renderer bounds first — that is what the player is looking at —
    /// falling back to the transform's scale for anything with no renderer of its own yet.
    static float WorldRadius(Transform t)
    {
        var r = t.GetComponentInChildren<Renderer>();
        if (r != null && r.bounds.extents.sqrMagnitude > 0.0000001f)
            return Mathf.Max(r.bounds.extents.x, r.bounds.extents.y, r.bounds.extents.z);
        return Mathf.Max(0.05f, t.lossyScale.x * 0.5f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Active = false;
    }
}
