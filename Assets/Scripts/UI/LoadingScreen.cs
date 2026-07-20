using System.Collections.Generic;
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
        ResetPlanetPlaceholder();
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

    // ---- The home star CLUSTER (the "pop-out") ----
    //
    // sunBody[0]/sunRend[0] ARE previewBody/previewRend — the single sun every other Subject already
    // reuses. sunBody[1]/[2] are two extra spheres that only ever appear while showing the real home
    // system, and only once it turns out to be a binary or trinary (StarCluster.Layout.orbits.Length > 1).
    List<StarData> homeStars;
    StarCluster homeLayout;              // null for a single-sun home — StepSunCluster then never runs
    readonly Transform[] sunBody = new Transform[3];
    readonly Renderer[] sunRend = new Renderer[3];
    readonly Material[] sunMat = new Material[3];   // [0] is unused — sun 0 reads subjectMats[Star]
    Transform pairPivot;                 // trinary's close inner pair orbits this; unused otherwise
    readonly float[] sunAngle = new float[3];
    readonly bool[] sunActive = new bool[3];
    readonly float[] sunGrow = new float[3];   // 0..1 "popping out" scale-in for a newly revealed sun
    float pairAngle;
    float sunRevealClock;                // seconds since the Star subject last came on screen
    bool clusterMoving;                  // true once the FIRST pop has happened — before that the whole
                                          // cluster sits frozen at the centre, reading as one lone star
    bool clusterRevealed;                // true once every companion has fully popped — Subject.Star is
                                          // reported again for every LATER system in the load, and this
                                          // is what stops the reveal replaying (truncated) each time
    float clusterScale = 1f;             // real orbit-units -> preview-local units, for a cluster home only
    // Public so GameManager can size its own hold on the Star subject to match — see GenerateGalaxyRoutine.
    public const float PopBeat = 1.3f;   // seconds between each additional sun popping out
    public const float PopGrow = 0.45f;  // seconds a freshly-popped sun takes to reach full size
    // How much of the preview's local space (same units as PreviewScale) the WHOLE cluster's reach may
    // fill. Roughly matches a lone sun's own radius so a binary/trinary home doesn't suddenly look
    // cramped or oversized next to the single-star case just because it has company.
    const float ClusterFrameRadius = 62f;

    // ---- The homeworld morph: barren rock -> its real finished surface, tile by tile ----
    //
    // Fixed and small ON PURPOSE, independent of the real body's grid (which can be 200x100+): this reads
    // the ALREADY-GENERATED surface once (a nearest-tile downsample, not a re-sample of the noise field)
    // and only ever repaints this tiny texture afterwards, so the reveal costs the same whether the real
    // world is a 20x10 moon or a 220x110 planet.
    const int MorphW = 40, MorphH = 20;
    // Public so GameManager can hold the Planet subject on screen long enough to see the whole reveal —
    // see GenerateGalaxyRoutine, same reasoning as PopBeat/PopGrow above.
    public const float MorphDuration = 2.4f;

    Texture2D morphTex;
    Color[] morphPixels;     // the texture's CURRENT (partially revealed) contents, mutated in place
    Color[] morphFinal;      // the finished surface, sampled once — never touched again after that
    int morphRevealed;       // how many of morphOrder's tiles have already been swapped to morphFinal
    float morphClock;
    bool morphActive;

    // A FIXED reveal order, shared by every planet this session — built once from System.Random (never
    // UnityEngine.Random), so this purely cosmetic animation can never perturb the shared gameplay RNG
    // stream that FactionAI and the generators keep drawing from while the morph plays out in the
    // background over the next couple of seconds.
    static readonly int[] morphOrder = BuildMorphOrder();

    static int[] BuildMorphOrder()
    {
        int n = MorphW * MorphH;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        var rng = new System.Random(20260720);
        for (int i = n - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    // Far past the main camera's far plane, for the same reason PlanetGlobeWindow's stage is: it needs
    // to be invisible to the game camera without claiming a layer from project settings.
    static readonly Vector3 PreviewOrigin = new Vector3(-200000f, 0f, 0f);
    const float PreviewScale = 100f;

    public void Report(float t, string stage, Subject what)
    {
        SetSubject(what);
        Report(t, stage);
    }

    /// Show the REAL home star rather than the generic placeholder.
    ///
    /// Called once the home star has been decided (GalaxyGenerator.Begin), which is why that roll was
    /// moved out of ForceHomeWorld: ForceHomeWorld runs at the very end, so while the loading screen is
    /// up there was no answer to "which star does the player's home have". Now there is, and the star on
    /// screen is the one the player will actually be orbiting.
    public void SetHomeStar(StarData star)
    {
        // Takes the STAR, not its type. StarDatabase.Get re-rolls colour, temperature and size on every
        // call, so looking it up here from a StarType would have produced a different-looking star than
        // the one the player ends up orbiting — the same spectral class and nothing more.
        if (star == null) return;
        homeStar = star;

        if (subjectMats != null)
        {
            // Free the material this replaces. The screen is a singleton that outlives any one game, so
            // overwriting across repeated generations without destroying would leak steadily.
            var old = subjectMats[(int)Subject.Star];
            if (old != null) Destroy(old);
            subjectMats[(int)Subject.Star] = BuildStarMaterial(homeStar);
            if (subject == Subject.Star && previewRend != null)
            {
                previewRend.sharedMaterial = subjectMats[(int)Subject.Star];
                ApplyStarScale();   // the new star has its own size; don't keep the previous one's
            }
        }
    }

    /// Show the whole home CLUSTER rather than just its first sun — called with the same list
    /// GalaxyGenerator.ForceHomeWorld will consume, so a binary/trinary home shows the real suns before
    /// they've been named. Sun 0 goes through the existing single-star path (SetHomeStar) unchanged;
    /// suns 1-2, if the cluster has them, only ever appear via the pop-out in StepSunCluster.
    public void SetHomeCluster(List<StarData> stars)
    {
        if (stars == null || stars.Count == 0) return;
        SetHomeStar(stars[0]);

        homeStars = stars;
        // StarCluster.Layout is the SAME geometry the system view builds its stars from (binary about a
        // mass-split barycenter; trinary as a close inner pair plus a third) — so the loading screen's
        // pop-out ends in the exact arrangement the player will actually see once they're in-system.
        homeLayout = stars.Count > 1 ? StarCluster.Layout(stars) : null;
        // One factor shrinks (or grows) the cluster's REAL orbit-unit geometry to fit ClusterFrameRadius,
        // so a wide-set binary and a tight one both end up framed the same — position and every sun's
        // size are scaled by the same factor, so their real proportions to each other are preserved.
        clusterScale = homeLayout != null ? ClusterFrameRadius / Mathf.Max(0.5f, homeLayout.reach) : 1f;

        for (int i = 1; i < sunMat.Length; i++)
        {
            if (sunMat[i] != null) Destroy(sunMat[i]);
            sunMat[i] = i < stars.Count ? BuildStarMaterial(stars[i]) : null;
            if (sunRend[i] != null) sunRend[i].sharedMaterial = sunMat[i];
            if (sunBody[i] != null) sunBody[i].gameObject.SetActive(false);
        }

        clusterRevealed = false;
        if (subject == Subject.Star) BeginSunCluster();
    }

    /// Trinary: suns [0]/[1] are StarCluster's own "close inner pair" convention, orbiting a shared pivot
    /// that itself swings around the system barycenter; sun [2] orbits the barycenter directly. Binary
    /// and single homes keep sun 0 (and sun 1, for a binary) directly under previewStage.
    ///
    /// Called both when the cluster is first set AND every time the Star subject is (re-)entered — not
    /// just once — because ResetSunCluster (leaving Star) reparents sun 0 back under previewStage so
    /// every OTHER subject's sphere sits at the stage centre regardless of where pairPivot last drifted
    /// to. Reparenting under pairPivot means that pivot's own per-frame motion (StepSunCluster) carries
    /// both inner suns with it for free, exactly as Unity's transform hierarchy already does for any
    /// other parent/child pair — no separate "orbit the orbit" bookkeeping needed.
    void ApplyClusterParenting()
    {
        bool trinaryPair = homeLayout != null && homeLayout.hasPair;
        Transform innerParent = trinaryPair && pairPivot != null ? pairPivot : previewStage;
        if (sunBody[0] != null) sunBody[0].SetParent(innerParent, false);
        if (sunBody[1] != null) sunBody[1].SetParent(innerParent, false);
        if (sunBody[2] != null) sunBody[2].SetParent(previewStage, false);
    }

    /// Same brightness curve the real star uses in the system view, kept in gamut because the preview
    /// renders to an LDR target composited after post — HDR here clamps rather than blooms. Shared by
    /// the primary sun (SetHomeStar) and every companion sun in a binary/trinary home (SetHomeCluster),
    /// so a cluster's suns are coloured by the identical rule a lone home star already was.
    static Material BuildStarMaterial(StarData star)
    {
        var c = star.color;
        float k = Mathf.Clamp(StarDatabase.EmissionStrength(star) * 0.55f, 0.35f, 1f);
        return SpaceMaterials.Unlit(new Color(Mathf.Min(1f, c.r + k * 0.4f), Mathf.Min(1f, c.g + k * 0.3f),
                                               Mathf.Min(1f, c.b + k * 0.2f)));
    }

    /// Size the preview to the real star's own visual scale, so a dim K dwarf reads visibly smaller than
    /// a bright G — the same distinction the system view makes.
    void ApplyStarScale()
    {
        if (previewBody == null) return;
        previewBody.localScale = Vector3.one * SunVisualSize(homeStar);
    }

    // Same clamp/curve ApplyStarScale already used for the single-sun case, factored out so every
    // companion sun in a cluster is sized by the identical rule.
    static float SunVisualSize(StarData s)
    {
        float scale = 1.15f;
        if (s != null) scale *= Mathf.Clamp(s.visualScale / 2f, 0.75f, 1.35f);
        return PreviewScale * scale;
    }

    /// Entering the Star subject. The home cluster's pop-out is a ONE-TIME reveal for the whole
    /// generation, not a per-system animation: Subject.Star is reported again for every later system in
    /// the loop, and without this guard the "one lone star, then a companion pops out" sequence would
    /// restart (and mostly get cut off) on every single one of them. So: the FIRST time this generation,
    /// start from "just sun 0" and let StepSunCluster reveal the rest on schedule. Every time after that
    /// (clusterRevealed already true), show the cluster already fully popped and resume its orbit from
    /// wherever it was — no replay.
    void BeginSunCluster()
    {
        ApplyClusterParenting();   // re-establish pairPivot parenting that ResetSunCluster undid on exit

        if (clusterRevealed)
        {
            clusterMoving = true;
            for (int i = 1; i < sunBody.Length; i++)
            {
                bool has = homeLayout != null && i < homeLayout.orbits.Length;
                sunActive[i] = has;
                sunGrow[i] = has ? 1f : 0f;
                if (sunBody[i] != null) sunBody[i].gameObject.SetActive(has);
            }
            return;
        }

        sunRevealClock = 0f;
        clusterMoving = false;
        sunAngle[0] = homeLayout != null && homeLayout.orbits.Length > 0 ? homeLayout.orbits[0].phase : 0f;
        pairAngle = homeLayout != null ? homeLayout.pairPhase : 0f;
        if (pairPivot != null) pairPivot.localPosition = Vector3.zero;
        if (previewBody != null) previewBody.localPosition = Vector3.zero;
        for (int i = 1; i < sunBody.Length; i++)
        {
            sunActive[i] = false;
            sunGrow[i] = 0f;
            if (i < (homeLayout?.orbits.Length ?? 0)) sunAngle[i] = homeLayout.orbits[i].phase;
            if (sunBody[i] != null) sunBody[i].gameObject.SetActive(false);
        }
    }

    /// Leaving the Star subject: put sun 0 back at the stage centre under previewStage directly — NOT
    /// still hanging off pairPivot, which keeps drifting even while nothing is displaying it — and hide
    /// the companions, rather than leaving a trinary's inner pair mid-orbit under a different subject's
    /// model (which previously left, say, the Planet placeholder rendering off-centre for the rest of the
    /// load, since it was still parented under a pivot nothing was resetting).
    void ResetSunCluster()
    {
        if (sunBody[0] != null) sunBody[0].SetParent(previewStage, false);
        if (sunBody[1] != null) sunBody[1].SetParent(previewStage, false);
        if (previewBody != null) previewBody.localPosition = Vector3.zero;
        if (pairPivot != null) pairPivot.localPosition = Vector3.zero;
        for (int i = 1; i < sunBody.Length; i++)
            if (sunBody[i] != null) sunBody[i].gameObject.SetActive(false);
    }

    /// Advance the cluster's orbits and, on schedule, pop the next sun out of the first. Only ever
    /// called while homeLayout != null (a binary/trinary home) — a single-sun home never touches this
    /// and previewBody just sits at the stage centre exactly as it always did.
    void StepSunCluster(float dt)
    {
        sunRevealClock += dt;
        int n = homeLayout.orbits.Length;

        // Reveal the second sun after a beat, the third after another. It "pops out of the first" by
        // starting its grow-in at 0 scale on the orbit it will already be following, rather than fading
        // in in place or appearing at full size.
        for (int i = 1; i < n && i < sunBody.Length; i++)
        {
            if (!sunActive[i] && sunRevealClock >= PopBeat * i)
            {
                sunActive[i] = true;
                sunGrow[i] = 0f;
                clusterMoving = true;   // the FIRST pop is also the cue for sun 0 to start orbiting
                if (sunBody[i] != null) sunBody[i].gameObject.SetActive(true);
            }
            if (sunActive[i] && sunGrow[i] < 1f) sunGrow[i] = Mathf.Min(1f, sunGrow[i] + dt / PopGrow);
        }

        // Once every companion has popped and finished growing, the reveal is done for the whole
        // generation — BeginSunCluster reads this to stop replaying it for every later system's turn.
        if (!clusterRevealed && clusterMoving)
        {
            bool allGrown = true;
            for (int i = 1; i < n && i < sunBody.Length; i++)
                if (!sunActive[i] || sunGrow[i] < 1f) { allGrown = false; break; }
            if (allGrown) clusterRevealed = true;
        }

        // Sizes are correct — and consistent with each other, since they share one clusterScale — from
        // the very first frame, even before the pop starts. Only ORBITAL MOTION waits on clusterMoving
        // below, so the primary sun doesn't visibly resize the instant its companion appears.
        for (int i = 0; i < n && i < sunBody.Length; i++)
        {
            if (sunBody[i] == null || (i > 0 && !sunActive[i])) continue;
            float grow = i == 0 ? 1f : sunGrow[i];
            sunBody[i].localScale = Vector3.one * homeStars[i].visualScale * clusterScale * grow;
        }

        // Still reading as a single star: nothing has popped yet, so nothing should move — the cluster
        // stays parked at the stage centre until the first companion actually appears.
        if (!clusterMoving) return;

        for (int i = 0; i < n && i < sunAngle.Length; i++)
            sunAngle[i] += homeLayout.orbits[i].speed * dt;
        if (homeLayout.hasPair) pairAngle += homeLayout.pairSpeed * dt;

        if (homeLayout.hasPair && pairPivot != null)
        {
            float pr = pairAngle * Mathf.Deg2Rad;
            pairPivot.localPosition = new Vector3(Mathf.Cos(pr), 0f, Mathf.Sin(pr)) * (homeLayout.pairRadius * clusterScale);
        }

        for (int i = 0; i < n && i < sunBody.Length; i++)
        {
            if (sunBody[i] == null || (i > 0 && !sunActive[i])) continue;
            var o = homeLayout.orbits[i];
            float rad = sunAngle[i] * Mathf.Deg2Rad;
            sunBody[i].localPosition = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * (o.radius * clusterScale);
        }
    }

    /// Show the REAL homeworld — barren rock morphing tile by tile into its finished surface — rather
    /// than the generic Planet placeholder. Called once ForceHomeWorld has actually built the world (its
    /// terrain is deterministic from terrainSeed, so the finished state is already fully known; this only
    /// reveals it), so unlike SetHomeCluster this can't run before the thing it's showing exists.
    public void SetHomePlanet(CelestialBody planet)
    {
        if (planet?.surface == null || subjectMats == null) return;
        EnsureMorphTexture();

        Color barren = TerrainColorMap.Get(TerrainType.Barren);
        for (int i = 0; i < morphPixels.Length; i++) morphPixels[i] = barren;

        // A nearest-tile downsample of the FINISHED grid — never a re-sample of the noise field, and
        // never at the real body's own resolution — so this costs the same fixed, tiny amount whether
        // the world is a small moon or a 220x110 planet.
        int sw = planet.surface.width, sh = planet.surface.height;
        for (int y = 0; y < MorphH; y++)
        {
            int sy = Mathf.Clamp(y * sh / MorphH, 0, sh - 1);
            for (int x = 0; x < MorphW; x++)
            {
                int sx = Mathf.Clamp(x * sw / MorphW, 0, sw - 1);
                var tile = sw > 0 && sh > 0 ? planet.surface.tiles[sx, sy] : null;
                Color c = barren;
                if (tile != null)
                {
                    c = TerrainColorMap.Get(tile.type);
                    float b = Mathf.Lerp(0.86f, 1.12f, tile.shade);
                    c = new Color(c.r * b, c.g * b, c.b * b, 1f);
                    if (tile.HasOre) c = Color.Lerp(c, OreDatabase.Get(tile.ore).color, 0.35f);
                }
                morphFinal[y * MorphW + x] = c;
            }
        }

        morphRevealed = 0;
        morphClock = 0f;
        morphActive = true;

        morphTex.SetPixels(morphPixels);
        morphTex.Apply();

        // Same idiom PlanetAppearance.Apply uses for the real in-game globe: write the texture under
        // every property name a shader might expose it as, and reset the tint to white so the placeholder's
        // flat blue doesn't multiply into the real terrain colours.
        var mat = subjectMats[(int)Subject.Planet];
        if (mat != null)
        {
            mat.mainTexture = morphTex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", morphTex);
            mat.color = Color.white;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (subject == Subject.Planet && previewRend != null) previewRend.sharedMaterial = mat;
        }
    }

    /// Put the Planet material back to its generic placeholder look. Called once per NEW generation
    /// (Open()), BEFORE that generation's own SetHomePlanet call — without this, a second galaxy in the
    /// same session would show the FIRST game's fully-morphed homeworld texture during its own early
    /// "Forming star system N" placeholder phase, since SetHomePlanet mutates this same persistent
    /// Material in place rather than replacing it.
    void ResetPlanetPlaceholder()
    {
        morphActive = false;
        if (subjectMats == null) return;
        var mat = subjectMats[(int)Subject.Planet];
        if (mat == null) return;
        mat.mainTexture = null;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
        mat.color = PlanetPlaceholderColor;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", PlanetPlaceholderColor);
    }

    void EnsureMorphTexture()
    {
        if (morphTex != null) return;
        morphTex = new Texture2D(MorphW, MorphH, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,     // blocky reveal reads as discrete TILES, not a soft blur
            wrapMode = TextureWrapMode.Repeat  // wraps cleanly around the sphere's longitude
        };
        morphPixels = new Color[MorphW * MorphH];
        morphFinal = new Color[MorphW * MorphH];
    }

    /// Advance the reveal: how many tiles SHOULD be shown by now is derived from elapsed time, so a
    /// slow frame reveals a batch at once rather than the animation falling behind real time.
    void StepMorph(float dt)
    {
        morphClock += dt;
        int total = morphOrder.Length;
        int target = Mathf.Min(total, Mathf.FloorToInt(total * Mathf.Clamp01(morphClock / MorphDuration)));
        if (target <= morphRevealed)
        {
            if (target >= total) morphActive = false;
            return;
        }

        for (int i = morphRevealed; i < target; i++)
        {
            int idx = morphOrder[i];
            morphPixels[idx] = morphFinal[idx];
        }
        morphRevealed = target;

        morphTex.SetPixels(morphPixels);
        morphTex.Apply();

        if (morphRevealed >= total) morphActive = false;
    }

    StarData homeStar;


    Material[] subjectMats;

    /// Build one material per subject, once.
    ///
    /// LIT, not unlit — and that is the difference between a spinning ball and a coloured circle. An
    /// unlit material ignores lighting entirely, so the sphere renders as a flat disc of uniform colour:
    /// the key light contributes nothing and the spin has no visible surface feature to move, making both
    /// pure decoration that does nothing. A lit material gives it a terminator, which is what reads as a
    /// three-dimensional body turning. The star is the exception and stays unlit, because a star emits
    /// rather than reflects.
    // Shared with ResetPlanetPlaceholder, which puts the Planet material back to exactly this look at
    // the start of every new generation — otherwise a second game reuses the first one's finished
    // homeworld texture (SetHomePlanet mutates this same Material in place) during the early "generic
    // placeholder" phase, before the new SetHomePlanet call replaces it again near 78%.
    static readonly Color PlanetPlaceholderColor = new Color(0.30f, 0.52f, 0.76f);

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
        subjectMats[(int)Subject.Planet] = LitMat(lit, PlanetPlaceholderColor);
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
        bool leavingStar = subject == Subject.Star;
        subject = s;

        previewBody.gameObject.SetActive(s != Subject.None);
        if (s == Subject.None)
        {
            if (leavingStar) ResetSunCluster();
            return;
        }

        // sharedMaterial, and from a prebuilt set. Assigning `.material` instantiates a fresh copy every
        // time and never frees the last one — and the subject changes twice per system, so a twelve-system
        // load would leak two dozen materials that live for the rest of the process.
        if (subjectMats != null) previewRend.sharedMaterial = subjectMats[(int)s];

        float scale = s == Subject.Star ? 1.15f
                    : s == Subject.Moon ? 0.62f
                    : s == Subject.Galaxy ? 1.3f : 1f;

        if (s == Subject.Star) { ApplyStarScale(); BeginSunCluster(); return; }

        if (leavingStar) ResetSunCluster();
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

        sunBody[0] = previewBody;
        sunRend[0] = previewRend;

        // Two more suns, built once and reused across every generation — only a binary/trinary home ever
        // activates them (SetHomeCluster), and only after they've popped out (StepSunCluster).
        for (int i = 1; i < sunBody.Length; i++)
        {
            var go = MakeCompanionSun(i);
            go.transform.SetParent(previewStage, false);
            sunBody[i] = go.transform;
            sunRend[i] = go.GetComponent<Renderer>();
        }

        // The trinary inner pair's shared pivot — see SetHomeCluster/StepSunCluster.
        pairPivot = new GameObject("PreviewPairPivot").transform;
        pairPivot.SetParent(previewStage, false);

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

    // A second or third sun for a binary/trinary home. No click collider and no StarInteraction — this
    // is decoration on a loading screen, not a clickable body — but the same self-spin every sun gets.
    static GameObject MakeCompanionSun(int index)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "PreviewCompanionSun" + index;
        var col = sphere.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var spin = sphere.AddComponent<SelfSpin>();
        spin.speed = 22f;
        spin.unscaled = true;
        sphere.SetActive(false);
        return sphere;
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

        if (subject == Subject.Star && homeLayout != null) StepSunCluster(dt);
        if (subject == Subject.Planet && morphActive) StepMorph(dt);

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
        foreach (var m in sunMat)
            if (m != null) Destroy(m);
        if (morphTex != null) Destroy(morphTex);
        if (Instance == this) Instance = null;
    }
}
