using UnityEngine;

// A pulsing round "target lock" ring shown where you right-click to send a fleet. If you clicked on
// (or near) a body it locks onto that body and travels with it as it orbits; otherwise it sits at the
// clicked point in space. Auto-fades after a few seconds.
public class TargetIndicator : MonoBehaviour
{
    public static TargetIndicator Instance;

    const int Seg = 48;
    LineRenderer ring;
    Transform followBody;
    Vector3 point;
    float hideAt;
    bool active;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("TargetIndicator").AddComponent<TargetIndicator>();
    }

    void Awake()
    {
        Instance = this;
        var go = new GameObject("Ring");
        go.transform.SetParent(transform, false);
        ring = go.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = Seg;
        ring.widthMultiplier = 0.35f;
        ring.numCapVertices = 2;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        ring.startColor = ring.endColor = new Color(0.4f, 1f, 0.6f, 0.9f);
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.enabled = false;
    }

    public void ShowAtBody(CelestialBody b)
    {
        followBody = (b != null && b.visualObject != null) ? b.visualObject.transform : null;
        if (b != null) point = BodyPos(b);
        Begin();
    }

    public void ShowAtPoint(Vector3 p) { followBody = null; point = p; Begin(); }

    void Begin() { active = true; hideAt = Time.unscaledTime + 3.5f; ring.enabled = true; }

    public void Hide() { active = false; ring.enabled = false; followBody = null; }

    static Vector3 BodyPos(CelestialBody b)
    {
        if (b.visualObject != null) return b.visualObject.transform.position;
        if (b.system != null) return b.system.galaxyPosition;
        return Vector3.zero;
    }

    void LateUpdate()
    {
        if (!active) return;
        if (Time.unscaledTime > hideAt) { Hide(); return; }

        Vector3 c = followBody != null ? followBody.position : point;
        float baseR = followBody != null ? Mathf.Max(1.2f, followBody.lossyScale.x * 0.9f) + 1.4f : 1.6f;
        float pulse = baseR + Mathf.Sin(Time.unscaledTime * 4f) * 0.45f;
        float alpha = Mathf.Lerp(0.2f, 0.95f, (hideAt - Time.unscaledTime) / 3.5f);
        var col = new Color(0.4f, 1f, 0.6f, alpha);
        ring.startColor = ring.endColor = col;
        for (int i = 0; i < Seg; i++)
        {
            float a = i * Mathf.PI * 2f / Seg;
            ring.SetPosition(i, c + new Vector3(Mathf.Cos(a) * pulse, 0.25f, Mathf.Sin(a) * pulse));
        }
    }
}
