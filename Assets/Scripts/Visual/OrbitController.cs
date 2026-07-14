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
        orbitRing.startColor = orbitRing.endColor = ringColor;
        DrawEllipse(orbitRing, orbitRadius, SemiMinor(orbitRadius, eccentricity));
        orbitRing.enabled = ringVisible;
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

    // Predicts where this body will be `futureSeconds` from now (used for fleet intercept preview).
    public Vector3 PredictWorldPosition(float futureSeconds)
    {
        if (parentBody == null) return transform.position;
        float futureAngle = currentAngle + direction * orbitSpeed * futureSeconds;
        float a = orbitRadius, b = SemiMinor(orbitRadius, eccentricity);
        float rad = futureAngle * Mathf.Deg2Rad;
        Vector3 local = new Vector3(Mathf.Cos(rad) * a, 0f, Mathf.Sin(rad) * b);
        Quaternion tilt = Quaternion.Euler(inclination, 0f, 0f);
        return parentBody.position + tilt * local + Vector3.up * verticalOffset;
    }

    // ---- Live setters (used by the orbit control panel for real-time editing) ----

    public void SetRadius(float v)      { orbitRadius = Mathf.Max(1f, v); RedrawRing(); UpdatePosition(); }
    public void SetSpeed(float v)       { orbitSpeed = v; }
    public void SetPhase(float v)       { phase = v; currentAngle = v; UpdatePosition(); }
    public void SetDirection(int d)     { direction = d >= 0 ? 1 : -1; }
    public void SetInclination(float v) { inclination = v; RedrawRing(); UpdatePosition(); }
    public void SetEccentricity(float v){ eccentricity = Mathf.Clamp(v, 0f, 0.7f); RedrawRing(); UpdatePosition(); }
    public void SetVerticalOffset(float v){ verticalOffset = v; UpdatePosition(); }

    public void SetRingVisible(bool v)  { ringVisible = v; if (orbitRing != null) orbitRing.enabled = v; }
    public void SetRingColor(Color c)   { ringColor = c; if (orbitRing != null) orbitRing.startColor = orbitRing.endColor = c; }

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
            habitableRing.enabled = true;
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
            ownerRing.enabled = true;
            UpdatePosition();
        }
        else if (ownerRing != null)
        {
            ownerRing.enabled = false;
        }
    }

    void OnDestroy()
    {
        if (orbitRing != null) Destroy(orbitRing.gameObject);
        if (habitableRing != null) Destroy(habitableRing.gameObject);
        if (ownerRing != null) Destroy(ownerRing.gameObject);
    }
}
