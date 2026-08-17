using UnityEngine;

// ============================================================================================
// THE DETECTION THRESHOLD, DRAWN — A DEV TOOL
//
// SystemPresence.DetectionRadius is the circle a ship crosses to identify what is in a system, and
// until you can SEE it the only way to tune it is to fly at a system and guess whether the reveal
// happened where you meant it to. So Dev Mode draws it: one flat ring around the system, in the same
// idiom as the habitable-zone band, with the slider on the star's Overview tab moving it live.
//
// DEV MODE ONLY, and that is the whole reason it can be this plain. It is an instrument, not part of
// the fiction — a player is never meant to see a labelled boundary around a system they have not
// visited, because knowing exactly how close to get is most of what the fog is hiding.
//
// It polls rather than being pushed at: the radius moves from a slider, from a body being added or
// removed in the sandbox, and from Dev Mode being toggled, and a poll a few times a second covers all
// three without three separate notifications that each have to remember to fire.
// ============================================================================================
[RequireComponent(typeof(LineRenderer))]
public class DetectionRingVisual : MonoBehaviour
{
    public StarSystemData system;

    LineRenderer line;
    float shownRadius = -1f;
    float nextPoll;

    const int Segments = 96;
    const float PollSeconds = 0.2f;

    /// Attach (or find) the ring for a system. Safe to call repeatedly.
    public static DetectionRingVisual Ensure(StarSystemData sys)
    {
        if (sys?.pivot == null) return null;

        var existing = sys.pivot.GetComponentInChildren<DetectionRingVisual>(true);
        if (existing != null) { existing.system = sys; return existing; }

        var go = new GameObject("DetectionRing");
        go.transform.SetParent(sys.pivot, false);
        var ring = go.AddComponent<DetectionRingVisual>();
        ring.system = sys;
        return ring;
    }

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.widthMultiplier = 0.35f;
        line.positionCount = 0;

        // Amber, and translucent. Distinct from the habitable zone's green and from an owner ring's
        // faction colour, so three flat circles around one star are still three different facts.
        var c = new Color(1f, 0.72f, 0.25f, 0.5f);
        line.startColor = line.endColor = c;
    }

    void Update()
    {
        nextPoll -= Time.unscaledDeltaTime;
        if (nextPoll > 0f) return;
        nextPoll = PollSeconds;

        bool want = GameMode.DevMode && system != null;
        if (line.enabled != want) line.enabled = want;
        if (!want) return;

        float r = SystemPresence.DetectionRadius(system);
        if (Mathf.Approximately(r, shownRadius)) return;
        shownRadius = r;
        Rebuild(r);
    }

    void Rebuild(float r)
    {
        // Flat on the orbital plane, like every other ring in the system — a sphere would read as an
        // object rather than as a boundary, and the game is played looking down at the plane anyway.
        line.positionCount = Segments;
        for (int i = 0; i < Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
    }
}
