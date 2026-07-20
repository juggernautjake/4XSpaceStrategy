using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Full-screen progress panel shown while a new galaxy is generated.
//
// The bar tracks REAL work. Generation is a synchronous call that used to block for as long as it took —
// which is why there was nothing to show: a bar cannot repaint inside a loop that never yields. The
// generator is now split into phases (GalaxyGenerator.Begin / AddSystem / Finish) and driven a system at
// a time by GameManager's coroutine, so every step this reports is a step that actually happened. No
// timed fake fill.
public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance;

    GameObject root;
    TMP_Text headline;
    TMP_Text stageLabel;
    TMP_Text percentLabel;
    RectTransform barFill;
    RectTransform barTrack;

    string headlineBase = "Generating the universe";

    float shown;          // eased display value, so the bar glides rather than jumping between steps
    float goal;           // where `shown` is heading right now (target, plus any creep)
    float target;         // the last progress actually reported
    float prevTarget;     // the one before it — gives the size of a typical step
    float creepCeiling;   // how far the goal may drift ahead of `target` between reports

    // How fast the fill converges on its target, as a rate constant rather than units-per-second.
    // Exponential smoothing is used instead of MoveTowards because the frames during generation are
    // wildly uneven — a frame that spans a whole star system can be 300ms — and a fixed rate either
    // crawls on long frames or overshoots on short ones. exp(-k*dt) is correct at any dt.
    const float FillSmoothing = 6f;

    // While waiting for the next report the fill creeps on at this fraction of the last step per second,
    // so it never looks frozen during a long one. Bounded by creepCeiling.
    const float CreepRate = 0.35f;

    const float BarWidth = 520f;
    const float BarHeight = 14f;

    public bool IsOpen => root != null && root.activeSelf;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("LoadingScreen");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<LoadingScreen>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        // A plain full-bleed panel rather than a UIFactory.Window: this is not a window. It has no title
        // bar, cannot be dragged, moved or closed, and must cover everything behind it — a half-finished
        // galaxy popping in around the edges of a floating box would undo the point of showing it at all.
        var panel = UIFactory.Panel(parent, "LoadingScreen", new Color(0.02f, 0.03f, 0.06f, 1f));
        root = panel.gameObject;
        var rt = panel.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        // Centred stack: headline, bar, stage line.
        var col = UIFactory.NewUI(rt, "Column").GetComponent<RectTransform>();
        col.anchorMin = col.anchorMax = new Vector2(0.5f, 0.5f);
        col.pivot = new Vector2(0.5f, 0.5f);
        col.sizeDelta = new Vector2(BarWidth, 150f);
        col.anchoredPosition = Vector2.zero;

        // The preview sits to the LEFT of the column rather than inside it, so it can be vertically
        // centred against the whole stack without being one more row the layout has to make space for.
        var pv = UIFactory.NewUI(col, "Preview").GetComponent<RectTransform>();
        pv.anchorMin = new Vector2(0f, 0.5f); pv.anchorMax = new Vector2(0f, 0.5f);
        pv.pivot = new Vector2(1f, 0.5f);
        pv.sizeDelta = new Vector2(120f, 120f);
        pv.anchoredPosition = new Vector2(-28f, 0f);
        previewView = pv.gameObject.AddComponent<RawImage>();
        previewView.raycastTarget = false;
        BuildPreview(parent);
        if (previewRT != null) previewView.texture = previewRT;

        headline = UIFactory.Text(col, headlineBase, 30, UITheme.Accent, TextAlignmentOptions.Center);
        var hrt = headline.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1); hrt.sizeDelta = new Vector2(0, 40);
        hrt.anchoredPosition = Vector2.zero;

        // Bar track.
        var track = UIFactory.Panel(col, "Track", new Color(1f, 1f, 1f, 0.10f));
        barTrack = track.rectTransform;
        barTrack.anchorMin = new Vector2(0, 1); barTrack.anchorMax = new Vector2(1, 1);
        barTrack.pivot = new Vector2(0.5f, 1);
        barTrack.sizeDelta = new Vector2(0, BarHeight);
        barTrack.anchoredPosition = new Vector2(0, -58f);

        // Fill, anchored left so only its WIDTH changes — scaling would squash the rounded ends and
        // stretch any future texture on it.
        var fill = UIFactory.Panel(barTrack, "Fill", UITheme.Accent);
        barFill = fill.rectTransform;
        barFill.anchorMin = new Vector2(0, 0); barFill.anchorMax = new Vector2(0, 1);
        barFill.pivot = new Vector2(0, 0.5f);
        barFill.sizeDelta = new Vector2(0, 0);
        barFill.anchoredPosition = Vector2.zero;

        percentLabel = UIFactory.Text(col, "0%", 13, UITheme.SubText, TextAlignmentOptions.Right);
        var prt = percentLabel.rectTransform;
        prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(1, 1);
        prt.pivot = new Vector2(0.5f, 1); prt.sizeDelta = new Vector2(0, 18);
        prt.anchoredPosition = new Vector2(0, -76f);

        stageLabel = UIFactory.Text(col, "", 14, UITheme.SubText, TextAlignmentOptions.Center);
        var srt = stageLabel.rectTransform;
        srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
        srt.pivot = new Vector2(0.5f, 1); srt.sizeDelta = new Vector2(0, 22);
        srt.anchoredPosition = new Vector2(0, -100f);

        root.SetActive(false);
    }

    public void Open(string headlineText = null)
    {
        if (root == null) return;
        headlineBase = string.IsNullOrEmpty(headlineText) ? "Generating the universe" : headlineText;
        shown = 0f; goal = 0f; target = 0f; prevTarget = 0f; creepCeiling = 0f;
        SetSubject(Subject.None);
        if (previewStage != null) previewStage.gameObject.SetActive(true);
        SetStage("");
        root.SetActive(true);
        // In front of every window that may already be open behind it.
        root.GetComponent<RectTransform>().SetAsLastSibling();
        Apply(0f);
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);
        if (previewStage != null) previewStage.gameObject.SetActive(false);
    }

    /// What the generator is working on, so the screen can show it.
    public enum Subject { None, Star, Planet, Moon, Galaxy }

    Subject subject = Subject.None;
    Transform previewStage, previewBody;
    Camera previewCam;
    RenderTexture previewRT;
    RawImage previewView;
    Renderer previewRend;
    Light previewLight;

    // Far past the main camera's far plane, for the same reason PlanetGlobeWindow's stage is: it needs
    // to be invisible to the game camera without claiming a layer from project settings.
    static readonly Vector3 PreviewOrigin = new Vector3(-200000f, 0f, 0f);
    const float PreviewScale = 100f;

    public void Report(float t, string stage, Subject what)
    {
        SetSubject(what);
        Report(t, stage);
    }

    Material[] subjectMats;

    /// Build one material per subject, once.
    ///
    /// LIT, not unlit — and that is the difference between a spinning ball and a coloured circle. An
    /// unlit material ignores lighting entirely, so the sphere renders as a flat disc of uniform colour:
    /// the key light contributes nothing and the spin has no visible surface feature to move, making both
    /// pure decoration that does nothing. A lit material gives it a terminator, which is what reads as a
    /// three-dimensional body turning. The star is the exception and stays unlit, because a star emits
    /// rather than reflects.
    void BuildSubjectMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) lit = Shader.Find("Standard");

        subjectMats = new Material[5];
        subjectMats[(int)Subject.None] = null;
        // IN GAMUT, not HDR. The preview camera renders to a plain LDR RenderTexture with no
        // post-processing, and the canvas composites that image after URP's post stack — so an HDR colour
        // here does not bloom, it simply clamps per channel. (3.2, 2.6, 1.4) would arrive as pure white
        // with the warm tint destroyed: worse than a colour that fits.
        subjectMats[(int)Subject.Star] = SpaceMaterials.Unlit(new Color(1f, 0.90f, 0.62f));
        subjectMats[(int)Subject.Planet] = LitMat(lit, new Color(0.30f, 0.52f, 0.76f));
        subjectMats[(int)Subject.Moon] = LitMat(lit, new Color(0.62f, 0.60f, 0.58f));
        subjectMats[(int)Subject.Galaxy] = LitMat(lit, new Color(0.62f, 0.42f, 0.92f));
    }

    static Material LitMat(Shader sh, Color c)
    {
        if (sh == null) return SpaceMaterials.Unlit(c);
        var m = new Material(sh);
        SpaceMaterials.ApplyColor(m, c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
        return m;
    }

    void SetSubject(Subject s)
    {
        // Deliberately does NOT record `s` when the preview is missing. Recording it would make the
        // matching later call a no-op via the equality guard below, so the subject would be permanently
        // stuck on whatever was requested while the preview did not exist.
        if (previewRend == null || previewBody == null) return;
        if (s == subject) return;
        subject = s;

        previewBody.gameObject.SetActive(s != Subject.None);
        if (s == Subject.None) return;

        // sharedMaterial, and from a prebuilt set. Assigning `.material` instantiates a fresh copy every
        // time and never frees the last one — and the subject changes twice per system, so a twelve-system
        // load would leak two dozen materials that live for the rest of the process.
        if (subjectMats != null) previewRend.sharedMaterial = subjectMats[(int)s];

        float scale = s == Subject.Star ? 1.15f
                    : s == Subject.Moon ? 0.62f
                    : s == Subject.Galaxy ? 1.3f : 1f;
        previewBody.localScale = Vector3.one * PreviewScale * scale;
    }

    void BuildPreview(Transform parent)
    {
        previewStage = new GameObject("LoadingPreviewStage").transform;
        previewStage.position = PreviewOrigin;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "PreviewBody";
        sphere.transform.SetParent(previewStage, false);
        var col = sphere.GetComponent<Collider>();
        if (col != null) Destroy(col);
        previewBody = sphere.transform;
        previewBody.localScale = Vector3.one * PreviewScale;
        previewRend = sphere.GetComponent<Renderer>();

        // Spins in place. Unscaled, because the whole screen exists while nothing else is running.
        var spin = sphere.AddComponent<SelfSpin>();
        spin.speed = 22f;
        spin.unscaled = true;

        var camGo = new GameObject("LoadingPreviewCam");
        camGo.transform.SetParent(previewStage, false);
        camGo.transform.localPosition = new Vector3(0f, 0f, -PreviewScale * 2.9f);
        previewCam = camGo.AddComponent<Camera>();
        previewCam.clearFlags = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCam.fieldOfView = 38f;
        previewCam.nearClipPlane = 0.05f * PreviewScale;
        previewCam.farClipPlane = 20f * PreviewScale;
        previewCam.enabled = false;      // driven by hand, one Render per frame

        previewRT = new RenderTexture(192, 192, 16) { name = "LoadingPreviewRT" };
        previewCam.targetTexture = previewRT;

        // A point light with a short range — a directional one would light the whole game scene behind
        // the loading screen, which is the mistake the globe viewer already made once.
        var lightGo = new GameObject("PreviewKey");
        lightGo.transform.SetParent(previewStage, false);
        lightGo.transform.localPosition = new Vector3(-PreviewScale, PreviewScale * 0.8f, -PreviewScale * 1.6f);
        previewLight = lightGo.AddComponent<Light>();
        previewLight.type = LightType.Point;
        previewLight.range = PreviewScale * 8f;
        previewLight.intensity = 1.5f;

        BuildSubjectMaterials();
        previewBody.gameObject.SetActive(false);   // nothing on screen until a subject is reported
        previewStage.gameObject.SetActive(false);
    }

    public void Report(float t, string stage)
    {
        float next = Mathf.Clamp01(t);
        if (next > target)
        {
            prevTarget = target;
            target = next;
            // Allow the fill to drift most of the way toward where the NEXT step will land, but never
            // past it. Without this the bar reaches each milestone and then sits dead still for the whole
            // of the following step — which is exactly the part that takes longest and most needs to look
            // like something is happening.
            float step = Mathf.Max(0.01f, target - prevTarget);
            // Never below where the goal has already crept to. A step smaller than the last one would
            // otherwise lower the ceiling under the current goal and the bar would visibly run backwards.
            creepCeiling = Mathf.Max(goal, Mathf.Min(1f, target + step * 0.8f));
        }
        if (stage != null) SetStage(stage);
    }

    void SetStage(string s)
    {
        if (stageLabel != null) stageLabel.text = s;
    }

    void Update()
    {
        if (!IsOpen) return;

        float dt = Time.unscaledDeltaTime;

        // ---- The dots ----
        //
        // Always three of them, FADING rather than being appended one at a time. Appending changes the
        // string's width every cycle, so a centred headline shifts left and right as it animates — read
        // as jitter, not motion. Three dots at varying alpha keeps the text metrics fixed and the wave
        // continuous instead of stepping through four discrete states.
        //
        // Driven purely by unscaled TIME, so its pace is completely independent of how fast the bar is
        // moving, of timeScale, and of whether any progress has been reported at all.
        if (headline != null)
        {
            var sb = new System.Text.StringBuilder(headlineBase.Length + 40);
            sb.Append(headlineBase);
            for (int i = 0; i < 3; i++)
            {
                // Each dot trails the one before it by a third of a cycle.
                float phase = Time.unscaledTime * 2.2f - i * 0.55f;
                float wave = (Mathf.Sin(phase) + 1f) * 0.5f;          // 0..1, smooth
                int a = Mathf.RoundToInt(Mathf.Lerp(45f, 255f, wave));
                // The '>' is not optional. Without it TMP opens tag mode at the first '<', scans for a
                // closing '>' that only appears at the very end, parses the whole run as one malformed
                // tag and renders it as literal text — the raw markup on screen instead of three dots.
                sb.Append("<alpha=#").Append(a.ToString("X2")).Append(">.");
            }
            sb.Append("<alpha=#FF>");   // don't leak the fade into anything appended later
            headline.text = sb.ToString();
        }

        // ---- The fill ----
        //
        // Exponential smoothing, which is correct at any frame time. Generation frames are wildly uneven
        // — one frame can span an entire star system — and a fixed units-per-second rate either crawls
        // through the long ones or overshoots the short ones.
        // The goal creeps from the last reported value toward the ceiling; `shown` chases the goal.
        // Two separate quantities on purpose — folding the creep into `shown` makes the smoothing chase a
        // target derived from its own output, which either stalls or runs away depending on the rates.
        float creepSpeed = Mathf.Max(0f, creepCeiling - target) * CreepRate;
        goal = Mathf.Min(creepCeiling, Mathf.Max(goal, target) + creepSpeed * dt);

        if (previewCam != null && previewStage != null && previewStage.gameObject.activeSelf)
            previewCam.Render();

        shown = Mathf.Lerp(shown, goal, 1f - Mathf.Exp(-FillSmoothing * dt));

        // Snap the last sliver. Exponential smoothing approaches its goal but never arrives, so at the
        // end of a load the bar sits a few pixels short and the label reads 99% for the whole hold before
        // the screen closes — the one number a loading bar must get right.
        if (goal - shown < 0.004f) shown = goal;

        Apply(shown);
    }

    void Apply(float t)
    {
        if (barFill == null || barTrack == null) return;
        float w = Mathf.Max(0f, barTrack.rect.width) * Mathf.Clamp01(t);
        barFill.sizeDelta = new Vector2(w, 0f);
        if (percentLabel != null) percentLabel.text = Mathf.RoundToInt(Mathf.Clamp01(t) * 100f) + "%";
    }

    void OnDestroy()
    {
        if (previewStage != null) Destroy(previewStage.gameObject);
        if (previewRT != null) { previewRT.Release(); Destroy(previewRT); }
        if (subjectMats != null)
            foreach (var m in subjectMats)
                if (m != null) Destroy(m);
        if (Instance == this) Instance = null;
    }
}
