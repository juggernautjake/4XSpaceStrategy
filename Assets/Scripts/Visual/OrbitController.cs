using System.Collections.Generic;
using UnityEngine;

// Drives a body's orbit around a parent and draws its orbit ring.
//
// KEY FIX vs the original: the orbit ring used to be parented to the (scaled) star/planet
// transform, so the ring inherited that scale and never lined up with the body. The ring is now
// parented to the unscaled system container and positioned in world space each frame, so it always
// matches the body — for planets AND moving moons.
//
// Adds a full set of live-editable parameters: radius, speed, starting phase, direction,
// inclination (tilt), eccentricity (elliptical orbits), vertical offset, and ring visibility/colour.
public class OrbitController : MonoBehaviour
{
    [Header("Orbit")]
    public Transform parentBody;
    public float orbitRadius = 10f;   // semi-major axis
    public float orbitSpeed = 8f;     // angular speed in DEGREES PER SECOND (physically defaulted)
    public float phase = 0f;          // starting angle, degrees
    public int direction = 1;         // +1 CCW, -1 CW
    public float inclination = 0f;    // tilt of orbit plane, degrees
    [Range(0f, 0.7f)] public float eccentricity = 0f;
    public float verticalOffset = 0f;
    public float spinSpeed = 0f;      // axial rotation, degrees per second

    [Header("Ring")]
    public int segments = 128;
    public float lineWidth = 0.08f;
    public bool ringVisible = true;
    public Color ringColor = new Color(0.4f, 0.7f, 1f, 0.6f);

    float currentAngle;
    LineRenderer orbitRing;
    LineRenderer habitableRing;
    LineRenderer ownerRing;
    Transform Container => transform.parent; // unscaled system container

    // ---- Setup ----

    public void Setup(Transform parent, float radius, float speed)
    {
        parentBody = parent;
        orbitRadius = radius;
        orbitSpeed = speed;
        currentAngle = phase != 0f ? phase : Random.Range(0f, 360f);
        BuildRing();
        UpdatePosition();
    }

    // Preferred setup: pull every orbital parameter from the data model.
    public void SetupFromData(Transform parent, CelestialBody data)
    {
        parentBody = parent;
        orbitRadius = data.orbitRadius;
        orbitSpeed = data.orbitSpeed;
        phase = data.orbitPhase;
        direction = data.orbitDirection == 0 ? 1 : data.orbitDirection;
        inclination = data.inclination;
        eccentricity = data.eccentricity;
        verticalOffset = data.verticalOffset;
        spinSpeed = data.spinSpeed;
        ringVisible = data.showRing;
        currentAngle = phase;
        BuildRing();
        UpdatePosition();
    }

    void BuildRing()
    {
        if (orbitRing == null)
        {
            var go = new GameObject(gameObject.name + "_OrbitRing");
            go.transform.SetParent(Container, false);
            orbitRing = go.AddComponent<LineRenderer>();
            orbitRing.useWorldSpace = false;
            orbitRing.loop = true;
            orbitRing.material = new Material(Shader.Find("Sprites/Default"));
            orbitRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orbitRing.receiveShadows = false;
        }
        orbitRing.startWidth = orbitRing.endWidth = lineWidth;
        ApplyRingColor();
        DrawEllipse(orbitRing, orbitRadius, SemiMinor(orbitRadius, eccentricity));
        ApplyRingEnabled();
        Live.Add(this);
    }

    // ---- The single answer to "should this ring be drawn" ----
    //
    // THREE independent gates, and every one of them has a legitimate claim: the player's own per-body
    // intent (ringVisible, from the orbit panel and CelestialBody.showRing), the loading finale's global
    // reveal (RevealAlpha), and concealment (VisibilityService — this body or its whole system hidden,
    // cloaked or undiscovered). They were being ANDed by hand at three separate call sites, which is
    // exactly how a fourth gate gets forgotten at one of them: SetRevealAlpha would have switched a
    // cloaked planet's orbit line straight back on at the end of every load.
    bool ringConcealed;

    // ALL THREE LINE RENDERERS, not just the orbit ring.
    //
    // The habitable-zone highlight and the owner ring hang off the same unscaled system container as the
    // orbit ring does (see SetHabitableHighlight / SetOwnerHighlight below), so ConcealBinding's sweep of
    // the BODY's subtree reaches none of them. Concealing a claimed world without this left a coloured
    // faction ring tracking an invisible planet around its star every frame — a labelled marker saying
    // exactly where the thing you just hid is.
    //
    // The two highlights track the BODY's concealment rather than the orbit line's: hiding just the
    // orbit line is a statement about the line, and has no business removing the world's owner ring.
    bool bodyConcealed;
    bool habitableWanted;
    bool ownerWanted;

    void ApplyRingEnabled()
    {
        if (orbitRing != null) orbitRing.enabled = ringVisible && !ringConcealed && RevealAlpha > 0.001f;
        if (habitableRing != null) habitableRing.enabled = habitableWanted && !bodyConcealed;
        if (ownerRing != null) ownerRing.enabled = ownerWanted && !bodyConcealed;
    }

    /// Concealment's handle on everything this controller draws. Kept apart from `ringVisible` and from
    /// the highlights' own on/off state so revealing a world restores exactly what the player (and the
    /// habitable-zone overlay, and whoever owns it) had set, rather than switching things on.
    ///
    /// `orbitLine` is passed separately because it can be concealed on its own — see
    /// VisibilityService.ReasonForOrbitLine, which already folds the body's own concealment into it.
    public void SetConcealed(bool body, bool orbitLine)
    {
        if (bodyConcealed == body && ringConcealed == orbitLine) return;
        bodyConcealed = body;
        ringConcealed = orbitLine;
        ApplyRingEnabled();
    }

    // ============================================================================================
    // THE GLOBAL REVEAL — orbit lines as the "you have control now" cue
    //
    // Rings are built eagerly inside GameManager.Visualize(), which runs at roughly the halfway point of
    // the load. So by the time the loading panel dissolves they already exist at full brightness, and
    // the galaxy arrives complete — every orbit drawn, nothing left to happen. The finale instead holds
    // them at zero and fades them in once the welcome message has gone, which is the moment the player
    // is handed control: the lines appearing IS the signal that the game is live.
    //
    // A global MULTIPLIER rather than a global on/off, so per-body intent (CelestialBody.showRing, the
    // Orbit panel's toggle, the star tab's system-wide toggle) is untouched and comes back exactly as
    // the player left it. `ringColor` keeps the authored colour; only what is written to the
    // LineRenderer is scaled — the same non-destructive pattern FadeGroup uses.
    // ============================================================================================
    public static float RevealAlpha { get; private set; } = 1f;

    // Every live controller, so the reveal can reach rings that already exist. Registered in BuildRing
    // (the one place a ring comes into being) and dropped in OnDestroy.
    static readonly List<OrbitController> Live = new List<OrbitController>();

    /// Set the global ring reveal. 0 = fully hidden, 1 = as authored.
    public static void SetRevealAlpha(float a)
    {
        RevealAlpha = Mathf.Clamp01(a);
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            var oc = Live[i];
            if (oc == null) { Live.RemoveAt(i); continue; }   // destroyed without OnDestroy running
            oc.ApplyRingColor();
            oc.ApplyRingEnabled();
        }
    }

    void ApplyRingColor()
    {
        if (orbitRing == null) return;
        var c = ringColor;
        c.a *= RevealAlpha;
        orbitRing.startColor = orbitRing.endColor = c;
    }

    float SemiMinor(float a, float e) => a * Mathf.Sqrt(Mathf.Max(0.0001f, 1f - e * e));

    void DrawEllipse(LineRenderer lr, float a, float b)
    {
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float ang = i * Mathf.PI * 2f / segments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(ang) * a, 0f, Mathf.Sin(ang) * b));
        }
    }

    // ---- Per-frame motion ----

    void Update()
    {
        if (parentBody == null) return;
        // orbitSpeed is now an angular speed in deg/sec (Kepler-defaulted, dev-overridable).
        currentAngle += direction * orbitSpeed * Time.deltaTime;
        if (spinSpeed != 0f) transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        UpdatePosition();
    }

    public void SetSpin(float v) { spinSpeed = v; }

    public void UpdatePosition()
    {
        if (parentBody == null) return;

        float a = orbitRadius;
        float b = SemiMinor(orbitRadius, eccentricity);
        float rad = currentAngle * Mathf.Deg2Rad;

        Vector3 local = new Vector3(Mathf.Cos(rad) * a, 0f, Mathf.Sin(rad) * b);
        Quaternion tilt = Quaternion.Euler(inclination, 0f, 0f);
        Vector3 offset = tilt * local + Vector3.up * verticalOffset;

        transform.position = parentBody.position + offset;

        // Keep the orbit ring glued to (and tilted like) the orbit, following moving parents.
        if (orbitRing != null)
        {
            orbitRing.transform.position = parentBody.position + Vector3.up * verticalOffset;
            orbitRing.transform.rotation = tilt;
        }
        // Highlight rings sit on the body itself.
        if (habitableRing != null)
            habitableRing.transform.position = transform.position;
        if (ownerRing != null)
            ownerRing.transform.position = transform.position;
    }

    // Predicts where this body will be `futureSeconds` from now (used for fleet intercept). Accounts
    // for a MOVING parent too (a moon orbits a planet that is itself orbiting), by recursively
    // predicting the parent's future position rather than using its current one.
    public Vector3 PredictWorldPosition(float futureSeconds)
    {
        if (parentBody == null) return transform.position;

        Vector3 parentFuture = parentBody.position;
        var parentOrbit = parentBody.GetComponent<OrbitController>();
        if (parentOrbit != null) parentFuture = parentOrbit.PredictWorldPosition(futureSeconds);

        float futureAngle = currentAngle + direction * orbitSpeed * futureSeconds;
        float a = orbitRadius, b = SemiMinor(orbitRadius, eccentricity);
        float rad = futureAngle * Mathf.Deg2Rad;
        Vector3 local = new Vector3(Mathf.Cos(rad) * a, 0f, Mathf.Sin(rad) * b);
        Quaternion tilt = Quaternion.Euler(inclination, 0f, 0f);
        return parentFuture + tilt * local + Vector3.up * verticalOffset;
    }

    // ---- Live setters (used by the orbit control panel for real-time editing) ----

    public void SetRadius(float v)      { orbitRadius = Mathf.Max(1f, v); RedrawRing(); UpdatePosition(); }
    public void SetSpeed(float v)       { orbitSpeed = v; }
    public void SetPhase(float v)       { phase = v; currentAngle = v; UpdatePosition(); }
    public void SetDirection(int d)     { direction = d >= 0 ? 1 : -1; }
    public void SetInclination(float v) { inclination = v; RedrawRing(); UpdatePosition(); }
    public void SetEccentricity(float v){ eccentricity = Mathf.Clamp(v, 0f, 0.7f); RedrawRing(); UpdatePosition(); }
    public void SetVerticalOffset(float v){ verticalOffset = v; UpdatePosition(); }

    // ANDed with the global reveal AND with concealment (see ApplyRingEnabled): during the loading
    // finale every ring is held at zero, and a per-body toggle must not be able to switch one back on
    // ahead of the cue — or to un-cloak a world's orbit.
    public void SetRingVisible(bool v)  { ringVisible = v; ApplyRingEnabled(); }
    // Through ApplyRingColor, not straight to the LineRenderer: writing the colour directly would light
    // this one ring at full alpha ahead of the loading finale's reveal.
    public void SetRingColor(Color c)   { ringColor = c; ApplyRingColor(); }

    public void RedrawRing()
    {
        if (orbitRing != null)
            DrawEllipse(orbitRing, orbitRadius, SemiMinor(orbitRadius, eccentricity));
    }

    public void ForceRingRedraw() => RedrawRing();

    public void RestoreParent(Transform parent)
    {
        parentBody = parent;
        UpdatePosition();
    }

    // ---- Green habitable-zone highlight ring around the body ----

    public void SetHabitableHighlight(bool on)
    {
        // Recorded as INTENT and then ANDed with concealment (ApplyRingEnabled). Writing `.enabled`
        // straight through would light a concealed world's ring the next time the habitable-zone
        // overlay was toggled.
        habitableWanted = on;
        if (on)
        {
            if (habitableRing == null)
            {
                var go = new GameObject(gameObject.name + "_HabitableRing");
                go.transform.SetParent(Container, false);
                habitableRing = go.AddComponent<LineRenderer>();
                habitableRing.useWorldSpace = false;
                habitableRing.loop = true;
                habitableRing.material = new Material(Shader.Find("Sprites/Default"));
                habitableRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Color green = new Color(0.25f, 1f, 0.35f, 0.95f);
                habitableRing.startColor = habitableRing.endColor = green;
                float r = Mathf.Max(1.2f, transform.lossyScale.x * 1.4f);
                habitableRing.startWidth = habitableRing.endWidth = 0.12f;
                DrawEllipse(habitableRing, r, r);
            }
            ApplyRingEnabled();
            UpdatePosition();
        }
        else if (habitableRing != null)
        {
            habitableRing.enabled = false;
        }
    }

    // Coloured ring showing which faction owns this body (kept slightly larger than the green
    // habitable ring so they can both show).
    public void SetOwnerHighlight(Color c, bool on)
    {
        ownerWanted = on;   // intent; concealment gates it — see SetHabitableHighlight
        if (on)
        {
            if (ownerRing == null)
            {
                var go = new GameObject(gameObject.name + "_OwnerRing");
                go.transform.SetParent(Container, false);
                ownerRing = go.AddComponent<LineRenderer>();
                ownerRing.useWorldSpace = false;
                ownerRing.loop = true;
                ownerRing.material = new Material(Shader.Find("Sprites/Default"));
                ownerRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float r = Mathf.Max(1.6f, transform.lossyScale.x * 1.9f);
                ownerRing.startWidth = ownerRing.endWidth = 0.14f;
                DrawEllipse(ownerRing, r, r);
            }
            ownerRing.startColor = ownerRing.endColor = c;
            ApplyRingEnabled();
            UpdatePosition();
        }
        else if (ownerRing != null)
        {
            ownerRing.enabled = false;
        }
    }

    void OnDestroy()
    {
        Live.Remove(this);
        if (orbitRing != null) Destroy(orbitRing.gameObject);
        if (habitableRing != null) Destroy(habitableRing.gameObject);
        if (ownerRing != null) Destroy(ownerRing.gameObject);
    }
}
