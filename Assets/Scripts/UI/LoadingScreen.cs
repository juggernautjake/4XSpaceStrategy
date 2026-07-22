using System.Collections;          // the non-generic IEnumerator — coroutines here return it bare
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

        // Black to begin with — space fills in as the galaxy does. Built first so it sits behind
        // everything else on the panel.
        BuildSky(rt);

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
        // Roomy on purpose. The preview is a body FLOATING IN SPACE, and the thing that broke that
        // illusion was the frame: a star's corona ran past the edge of a 120px box and got sliced off
        // square, so the star read as sitting in a little window rather than out in the dark. The box is
        // bigger, the render target is bigger, and the camera (BuildPreview) sits far enough back that
        // the glow fades to nothing well inside the edges — nothing is ever cut.
        pv.sizeDelta = new Vector2(240f, 240f);
        pv.anchoredPosition = new Vector2(-34f, 0f);
        previewView = pv.gameObject.AddComponent<RawImage>();
        previewView.raycastTarget = false;
        // Remembered at BUILD time, so both Open and the finale's own cleanup can put the preview back
        // without either having to have observed where it started.
        previewHomePos = pv.anchoredPosition;
        previewHomeSize = pv.sizeDelta;
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

    // ---- The starfield ------------------------------------------------------------------------
    //
    // The panel starts BLACK and space fills in as the galaxy is built, so the backdrop is doing the same
    // thing the progress bar is describing: a universe being made. Stars accrue at a rate tied to actual
    // progress rather than to a timer — pause the generation and the sky stops filling too.
    //
    // Plain UI Images on the panel, not the 3D SpaceBackground: that one is parented to the game camera,
    // which is looking at a half-built galaxy behind a full-screen panel. Borrowing it would mean either
    // moving it or rendering the very scene this screen exists to hide.
    RectTransform skyLayer;
    readonly List<RectTransform> skyStars = new List<RectTransform>();
    readonly List<float> skyPhase = new List<float>();
    float shootTimer = 2f;

    const int SkyStarTarget = 220;   // how many stars a fully-generated sky holds

    void BuildSky(RectTransform parent)
    {
        skyLayer = UIFactory.NewUI(parent, "Sky").GetComponent<RectTransform>();
        UIFactory.Stretch(skyLayer);
        skyLayer.SetAsFirstSibling();   // behind the bar, the preview and every caption

        // The sky is the only part of the UI that moves every single frame, and this project has ONE
        // canvas for the whole game (UIFactory.CreateCanvas is the only AddComponent<Canvas>). Without
        // this, a couple of hundred drifting stars dirty that shared canvas each frame and every open
        // window rebatches with them. A nested Canvas confines the rebuild to the starfield.
        // No GraphicRaycaster: nothing in here is clickable (every star sets raycastTarget = false), and
        // no overrideSorting, so the layer keeps its place in hierarchy order — behind everything.
        skyLayer.gameObject.AddComponent<Canvas>();

        // Built here, ONCE, rather than fetched-or-added during the finale.
        //
        // That fetch-or-add was written as `GetComponent<CanvasGroup>() ?? AddComponent<CanvasGroup>()`,
        // which is the classic Unity trap: a missing component comes back as a FAKE null — a live C#
        // reference whose underlying object is gone — and `??` tests for real null, so it hands back the
        // fake instead of ever calling AddComponent. The next line then threw
        // MissingComponentException. Owning the reference from the start removes the question.
        skyGroup = UIFactory.Ensure<CanvasGroup>(skyLayer.gameObject);
        skyGroup.blocksRaycasts = false;
        skyGroup.interactable = false;
    }

    CanvasGroup skyGroup;

    /// The sky's fade group, re-created if it has gone missing.
    ///
    /// Belt and braces over the cached field. The screen is a singleton that outlives every game, and a
    /// cached component reference is only as good as the object under it — anything that destroyed the
    /// Sky object or stripped the component would leave a fake-null here, and the next `alpha =` would
    /// throw MissingComponentException in the middle of the hand-off, which is the worst possible place
    /// for it. Ensure re-adds rather than trusting; the null test is Unity's overloaded ==, which is
    /// what catches a fake null in the first place.
    CanvasGroup SkyGroup
    {
        get
        {
            if (skyGroup == null && skyLayer != null)
                skyGroup = UIFactory.Ensure<CanvasGroup>(skyLayer.gameObject);
            return skyGroup;
        }
    }

    /// Fill the sky to match how far generation has got.
    void TickSky(float progress, float dt)
    {
        if (skyLayer == null) return;

        int want = Mathf.RoundToInt(SkyStarTarget * Mathf.Clamp01(progress));
        var size = skyLayer.rect.size;

        // Add a few per frame rather than all at once on a threshold, so the sky thickens visibly instead
        // of appearing in blocks.
        int add = Mathf.Min(3, want - skyStars.Count);
        for (int i = 0; i < add; i++)
        {
            var go = UIFactory.NewUI(skyLayer, "Star");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            float s = Random.Range(1.2f, 2.9f);
            rt.sizeDelta = new Vector2(s, s);
            rt.anchoredPosition = new Vector2(Random.Range(-size.x * 0.5f, size.x * 0.5f),
                                              Random.Range(-size.y * 0.5f, size.y * 0.5f));
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            // Mostly white, occasionally warm or cool — a monochrome field reads as noise, not as stars.
            float r = Random.value;
            img.color = r < 0.72f ? new Color(1f, 1f, 1f, 0.85f)
                      : r < 0.88f ? new Color(0.72f, 0.82f, 1f, 0.8f)
                                  : new Color(1f, 0.85f, 0.7f, 0.8f);
            skyStars.Add(rt);
            skyPhase.Add(Random.Range(0f, 6.28f));
        }

        // Twinkle, and a slow drift so the field has depth rather than sitting flat behind the panel.
        int slice = Time.frameCount & 3;
        for (int i = 0; i < skyStars.Count; i++)
        {
            var rt = skyStars[i];
            if (rt == null) continue;
            // Re-tint a QUARTER of the field per frame. A colour write dirties the graphic and costs a
            // mesh rebuild; at 1.7 rad/s a star's alpha moves so little between frames that spreading the
            // work over four of them is invisible, and it cuts the per-frame rebuild count by 4x. The
            // drift below still runs on every star every frame — that one IS visible if it stutters.
            if ((i & 3) == slice)
            {
                float tw = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 1.7f + skyPhase[i]);
                var img = rt.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color; c.a = 0.35f + 0.5f * tw; img.color = c;
                }
            }
            // Parallax: the nearer (larger) stars drift faster than the small distant ones.
            float depth = Mathf.Clamp01((rt.sizeDelta.x - 1.2f) / 1.7f);
            rt.anchoredPosition += new Vector2(-dt * (2f + depth * 7f), 0f);
            if (rt.anchoredPosition.x < -size.x * 0.5f)
                rt.anchoredPosition = new Vector2(size.x * 0.5f, Random.Range(-size.y * 0.5f, size.y * 0.5f));
        }

        // Shooting stars, but only once there is a sky for them to cross.
        shootTimer -= dt;
        if (shootTimer <= 0f && skyStars.Count > 40)
        {
            shootTimer = Random.Range(1.8f, 4.5f);
            SpawnShootingStar(size);
        }
    }

    void SpawnShootingStar(Vector2 size)
    {
        var go = UIFactory.NewUI(skyLayer, "Shooting");
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Random.Range(34f, 70f), 2f);
        rt.anchoredPosition = new Vector2(Random.Range(-size.x * 0.4f, size.x * 0.2f),
                                          Random.Range(-size.y * 0.35f, size.y * 0.45f));
        rt.localRotation = Quaternion.Euler(0, 0, Random.Range(-28f, -12f));
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.color = new Color(0.9f, 0.95f, 1f, 0.9f);
        go.AddComponent<LoadingShootingStar>().Init(rt, Random.Range(620f, 1000f));
    }

    public void Open(string headlineText = null)
    {
        if (root == null) return;
        headlineBase = string.IsNullOrEmpty(headlineText) ? "Generating the universe" : headlineText;
        shown = 0f; goal = 0f; target = 0f; prevTarget = 0f; creepCeiling = 0f;

        // Empty the sky, so every load starts from black and builds again rather than opening on the
        // last game's finished starfield.
        //
        // Clear the whole LAYER, not just the tracked list: shooting stars are children of it too and are
        // deliberately untracked (they delete themselves after ~1s). Close() only deactivates the root, so
        // a streak that was mid-flight when the last load ended is frozen rather than gone, and would
        // resume across a sky that is supposed to be starting from black.
        for (int i = skyLayer != null ? skyLayer.childCount - 1 : -1; i >= 0; i--)
            Destroy(skyLayer.GetChild(i).gameObject);
        skyStars.Clear();
        skyPhase.Clear();
        shootTimer = 2f;
        ClearMoons();               // last game's moons are not this game's

        // A fresh load starts unaligned and airless, and the preview spins on its own again — the
        // finale turns all three off on its way out, and these are the fields it would otherwise leave
        // set for the next game.
        aligning = false;
        atmosphereOn = false;
        homeBody = null;
        RestoreIdleSpin(previewBody, 22f);
        if (previewBody != null)
        {
            var oldAir = previewBody.Find("Atmosphere");
            if (oldAir != null) Destroy(oldAir.gameObject);
            var oldCloud = previewBody.Find("Clouds");
            if (oldCloud != null) Destroy(oldCloud.gameObject);
        }

        // Defensive: the finale's own cleanup runs at the end of its coroutine, so anything that killed
        // that coroutine part-way (a scene load, StopAllCoroutines) would leave the singleton stuck in
        // the finale state — translucent panel, invisible sky, frozen bar — for every load afterwards.
        // Opening is the one moment it is unambiguously safe to assert the normal state.
        finaleRunning = false;
        // Orbit rings too: the finale is what turns them back on, so anything that stopped it part-way
        // would leave the galaxy with no orbit lines for the rest of the session. The generation routine
        // sets this straight back to 0 a moment later, so asserting it here costs nothing.
        OrbitController.SetRevealAlpha(1f);
        var panelImg0 = root.GetComponent<Image>();
        if (panelImg0 != null)
        {
            panelImg0.color = new Color(0.02f, 0.03f, 0.06f, 1f);
            // SwitchToLiveView clears this when the screen hands over to the real camera. Restoring it
            // matters: without it the second load of a session runs behind a panel that no longer blocks
            // anything, so the live scene underneath is clickable while the bar is still filling.
            panelImg0.raycastTarget = true;
        }
        { var sg = SkyGroup; if (sg != null) sg.alpha = 1f; }
        if (previewView != null)
        {
            // ACTIVE, not merely restyled. SwitchToLiveView and ShowGenesisTitles switch the preview OFF
            // — it has done its job once the real camera takes over — and nothing turned it back on. So
            // the second New Game of a session played its entire star phase against a black rectangle:
            // no home star, no binary pop-out, no named sun, just a bar.
            previewView.gameObject.SetActive(true);
            previewView.color = Color.white;
            previewView.rectTransform.anchoredPosition = previewHomePos;
            previewView.rectTransform.sizeDelta = previewHomeSize;
        }
        if (barTrack != null) { barTrack.gameObject.SetActive(true); barTrack.localScale = Vector3.one; }
        if (headline != null) headline.gameObject.SetActive(true);
        if (stageLabel != null) stageLabel.gameObject.SetActive(true);
        if (percentLabel != null) percentLabel.gameObject.SetActive(true);
        if (welcomeLabel != null) welcomeLabel.gameObject.SetActive(false);

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

    // ============================================================================================
    // THE FINALE — handing the screen over to the game without a cut
    //
    // The load does not end by switching a panel off. The bar collapses into itself and blinks out,
    // the homeworld and its moons travel to the middle of the screen where the bar used to be, and
    // the game is revealed underneath them already framing that same planet.
    //
    // The whole point is continuity: the planet the player watches generate IS the planet they are
    // about to play on, and nothing in between should suggest otherwise — no cut, no fade to black,
    // no loading screen "closing". What ends the load is the planet arriving where it belongs.
    // ============================================================================================
    const float SettleBeat = 1.4f;    // the finished world simply turning
    const float CollapseBeat = 0.45f; // the bar closing in on itself
    const float BlinkBeat = 0.18f;    // the flash as it goes
    const float TravelBeat = 1.1f;    // planet + moons moving to centre, alongside the collapse
    const float WelcomeFadeIn = 0.5f; // "Welcome to <world>" arriving
    const float HandoffBeat = 0.7f;   // the panel dissolving out from behind it
    const float WelcomeHold = 1.1f;   // the message over the live solar system
    const float AlignBeat = 0.6f;      // the preview turning to match the real planet
    const float WelcomeFadeOut = 0.6f; // and then completely gone, before anything else happens
    const float RingRevealGap = 0.25f; // a beat of empty sky, so the cue reads as its own event
    const float RingRevealBeat = 1.2f; // the orbits drawing in — the "you have control" cue
    const float GalaxyArrivalGap = 0.45f; // ...then the rest of the galaxy, a beat behind the rings

    /// How much bigger the preview gets as it travels to the centre.
    ///
    /// This is also what sets the ZOOM the game opens at, because HandoffScreenFraction reads it and
    /// the real camera is framed to match — so the planet is as present in the game as it was on the
    /// loading screen. At 1.35 it arrived reading as a thumbnail that had merely moved; the planet
    /// wants to be the subject of the shot by the time the panel goes.
    const float TravelGrow = 1.9f;

    RectTransform welcomeRT;
    TMP_Text welcomeLabel;
    Image blinkFlash;
    Vector2 previewHomePos, previewHomeSize;

    /// Run the closing sequence. `onReady` fires at the moment the real game should take over — after
    /// the planet has landed at centre and before the panel dissolves — so the caller can put the
    /// camera on the real homeworld while the screen still covers it.
    public System.Collections.IEnumerator Finale(string homeName, System.Action onReady)
    {
        if (root == null) yield break;

        // --- 1. SETTLE. The reveal is done; let the world turn for a moment before anything moves. ---
        //
        // Land the bar on a true 100% FIRST. `shown` is exponentially smoothed and so is always a little
        // short of its goal; the moment finaleRunning goes up, Update stops calling Apply, and whatever
        // fraction it had reached would be frozen there. The bar would sit at 97% for the whole settle
        // beat and then collapse — which is the one number a loading bar has to get right.
        target = goal = shown = creepCeiling = 1f;
        Apply(1f);

        finaleRunning = true;
        for (float e = 0f; e < SettleBeat; e += Time.unscaledDeltaTime) yield return null;

        // --- 2. COLLAPSE AND TRAVEL, TOGETHER. ---
        //
        // One movement, not two. The bar closes horizontally into its own midpoint while the homeworld
        // slides into that same spot and grows — so the planet is arriving as the bar is leaving, and
        // the space the bar occupied is handed straight over rather than sitting empty in between. The
        // planet keeps spinning and the moons keep orbiting throughout (Update drives both).
        if (percentLabel != null) percentLabel.gameObject.SetActive(false);

        var barGroup = barTrack;
        Vector3 startScale = barGroup != null ? barGroup.localScale : Vector3.one;

        var pv = previewView != null ? previewView.rectTransform : null;
        Vector2 from = pv != null ? pv.anchoredPosition : Vector2.zero;
        Vector2 fromSize = pv != null ? pv.sizeDelta : Vector2.zero;
        Vector2 toSize = fromSize * TravelGrow;
        // Where the bar's midpoint was, in the preview's own coordinates.
        //
        // Two corrections live in this one line. The preview is anchored to the column's LEFT EDGE
        // (anchorMin/Max 0, 0.5) — not its centre — so reaching the column's midpoint needs an explicit
        // half-column offset; and its pivot is its RIGHT edge, so landing the planet's CENTRE there
        // needs another half-width, measured at the size it will BE on arrival rather than the size it
        // started at. Without both, the planet stops about 300px short and the cross-fade that the whole
        // finale exists to make seamless is the exact frame it jumps sideways.
        Vector2 to = new Vector2(BarWidth * 0.5f + toSize.x * 0.5f, 0f);

        float move = Mathf.Max(CollapseBeat, TravelBeat);
        for (float e = 0f; e < move; e += Time.unscaledDeltaTime)
        {
            // The bar collapses on its own, SHORTER beat and finishes early — it should be gone before
            // the planet lands, not race it to the middle.
            float tb = Mathf.Clamp01(e / CollapseBeat);
            // Eased IN — slow to leave, then quick. A linear collapse reads as the bar being deleted;
            // this reads as it being drawn shut.
            float kb = tb * tb * tb;
            if (barGroup != null) barGroup.localScale = new Vector3(Mathf.Lerp(startScale.x, 0f, kb), startScale.y, 1f);

            float tp = Mathf.Clamp01(e / TravelBeat);
            float kp = 1f - Mathf.Pow(1f - tp, 3f);   // eased out — arrives gently
            if (pv != null)
            {
                pv.anchoredPosition = Vector2.Lerp(from, to, kp);
                pv.sizeDelta = Vector2.Lerp(fromSize, toSize, kp);
            }
            yield return null;
        }
        if (barGroup != null) barGroup.localScale = new Vector3(0f, startScale.y, 1f);

        // --- 3. BLINK. A bright point where the bar was, then gone. ---
        EnsureBlink();
        if (blinkFlash != null)
        {
            blinkFlash.gameObject.SetActive(true);
            for (float e = 0f; e < BlinkBeat; e += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(e / BlinkBeat);
                var c = blinkFlash.color;
                // Up fast, out slow: a spark, not a pulse.
                c.a = t < 0.3f ? Mathf.Lerp(0f, 1f, t / 0.3f) : Mathf.Lerp(1f, 0f, (t - 0.3f) / 0.7f);
                blinkFlash.color = c;
                yield return null;
            }
            blinkFlash.gameObject.SetActive(false);
        }
        if (barTrack != null) barTrack.gameObject.SetActive(false);
        if (headline != null) headline.gameObject.SetActive(false);
        if (stageLabel != null) stageLabel.gameObject.SetActive(false);

        // --- 4. THE CAMERA TAKES OVER, still hidden. ---
        //
        // Framed on the real homeworld while the panel is fully opaque, so that when the panel goes the
        // thing underneath is already the same planet at the same size in the same place. One frame's
        // grace so GalaxyLOD sees the new camera height and swaps the galaxy proxies out for real
        // system geometry before any of it is visible.
        onReady?.Invoke();
        yield return null;

        // --- 4b. TURN THE PREVIEW TO MATCH. ---
        //
        // The camera is now on the real planet, so the preview finally has something to line up WITH:
        // it swings to the same bearing, the sphere takes the real world's spin, the moons move to their
        // real positions relative to it, and the light comes from the real star. Eased over a beat so it
        // reads as the model settling into place rather than snapping.
        //
        // From here to the dissolve, AlignToReal runs every frame (see Update) — the planet is orbiting
        // and its moons are moving, so a one-off match would be stale within a second.
        aligning = true;
        for (float e = 0f; e < AlignBeat; e += Time.unscaledDeltaTime) yield return null;

        // --- 5. WELCOME, fading up over the still-opaque panel. ---
        EnsureWelcome();
        if (welcomeLabel != null)
        {
            welcomeLabel.text = $"Welcome to your homeworld, <color=#FFD24D>{homeName}</color>";
            welcomeLabel.gameObject.SetActive(true);
            for (float e = 0f; e < WelcomeFadeIn; e += Time.unscaledDeltaTime)
            {
                var c = welcomeLabel.color; c.a = Mathf.Clamp01(e / WelcomeFadeIn); welcomeLabel.color = c;
                yield return null;
            }
            var wc = welcomeLabel.color; wc.a = 1f; welcomeLabel.color = wc;
        }

        // --- 6. HANDOFF. Dissolve the panel out from behind the welcome message. ---
        //
        // The text stays up through this, so what the player watches is the loading screen becoming the
        // solar system around a planet that never moved — no cut, no black. The preview image is the
        // last thing to go, overlapping the real planet already drawn beneath it.
        var panelImg = root.GetComponent<Image>();
        Color panelStart = panelImg != null ? panelImg.color : Color.clear;
        for (float e = 0f; e < HandoffBeat; e += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(e / HandoffBeat);
            if (panelImg != null) { var c = panelStart; c.a = panelStart.a * (1f - t); panelImg.color = c; }
            { var sg = SkyGroup; if (sg != null) sg.alpha = 1f - t; }
            if (previewView != null)
            {
                var c = previewView.color;
                c.a = t < 0.66f ? 1f : 1f - (t - 0.66f) / 0.34f;
                previewView.color = c;
            }
            yield return null;
        }

        // A beat with the welcome message over the live solar system before it goes.
        for (float e = 0f; e < WelcomeHold; e += Time.unscaledDeltaTime) yield return null;

        // --- 7. THE WELCOME FADES, AND THE ORBITS DRAW IN. ---
        //
        // This is the moment the player is handed control, and the orbit lines are how they are told.
        // Everything up to here has been a performance they could not touch; the rings arriving is a
        // visual cue with a real meaning behind it — the system is live, and so are you.
        //
        // Held at zero since Visualize built them (see OrbitController.SetRevealAlpha), because rings
        // that were simply already there would make the galaxy arrive finished, with nothing left to
        // happen at the one moment something should.
        // The message goes FIRST, completely, before a single orbit line appears. Overlapping the two
        // made the cue ambiguous — the rings looked like part of the text's exit rather than the thing
        // that follows it. Gone, a beat of nothing, then the orbits.
        if (welcomeLabel != null)
        {
            Color w0 = welcomeLabel.color;
            for (float e = 0f; e < WelcomeFadeOut; e += Time.unscaledDeltaTime)
            {
                var c = w0; c.a = 1f - Mathf.Clamp01(e / WelcomeFadeOut); welcomeLabel.color = c;
                yield return null;
            }
            var done = w0; done.a = 0f; welcomeLabel.color = done;
            welcomeLabel.gameObject.SetActive(false);   // not merely transparent — gone
        }

        for (float e = 0f; e < RingRevealGap; e += Time.unscaledDeltaTime) yield return null;

        for (float e = 0f; e < RingRevealBeat; e += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(e / RingRevealBeat);
            // Eased out, so the lines swell in quickly and settle rather than ramping linearly.
            OrbitController.SetRevealAlpha(1f - Mathf.Pow(1f - t, 2f));
            yield return null;
        }
        OrbitController.SetRevealAlpha(1f);

        // --- 8. AND THE REST OF THE GALAXY ARRIVES. ---
        //
        // Every other system has been concealed since generation finished (GenesisReveal.Begin) —
        // running the whole time, simply not drawn. A beat after the orbit lines, so the two reveals read
        // as a sequence rather than as one event: your system draws itself, and then the galaxy it sits
        // in turns up around it.
        for (float e = 0f; e < GalaxyArrivalGap; e += Time.unscaledDeltaTime) yield return null;
        GenesisReveal.Finish();

        finaleRunning = false;
        Close();
        RestoreAfterFinale(panelStart, from, fromSize);
    }

    bool finaleRunning;

    /// Put everything the finale moved or hid back, so a SECOND generation in the same session opens on
    /// a clean screen rather than on the wreckage of the last one's ending.
    void RestoreAfterFinale(Color panelColor, Vector2 previewPos, Vector2 previewSize)
    {
        var panelImg = root != null ? root.GetComponent<Image>() : null;
        if (panelImg != null) panelImg.color = panelColor;
        { var sg = SkyGroup; if (sg != null) sg.alpha = 1f; }
        if (previewView != null)
        {
            previewView.color = Color.white;
            previewView.rectTransform.anchoredPosition = previewPos;
            previewView.rectTransform.sizeDelta = previewSize;
        }
        if (barTrack != null) { barTrack.gameObject.SetActive(true); barTrack.localScale = Vector3.one; }
        if (headline != null) headline.gameObject.SetActive(true);
        if (stageLabel != null) stageLabel.gameObject.SetActive(true);
        if (percentLabel != null) percentLabel.gameObject.SetActive(true);
        if (welcomeLabel != null) welcomeLabel.gameObject.SetActive(false);
    }

    void EnsureBlink()
    {
        if (blinkFlash != null || barTrack == null) return;
        var go = UIFactory.NewUI(barTrack.parent, "Blink");
        var rt = go.GetComponent<RectTransform>();
        // (0.5, 1), not barTrack's anchorMin. The track is horizontally STRETCHED — anchorMin (0,1) to
        // anchorMax (1,1) — so its anchoredPosition.x of 0 means "centred across the span". Copying only
        // the min would give the flash a point anchor at the column's top-LEFT corner and put the spark
        // 260px away from where the bar actually closed.
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(46f, 46f);
        rt.anchoredPosition = barTrack.anchoredPosition;
        blinkFlash = go.AddComponent<Image>();
        blinkFlash.sprite = null;
        blinkFlash.raycastTarget = false;
        blinkFlash.color = new Color(1f, 1f, 1f, 0f);
        go.SetActive(false);
    }

    // ============================================================================================
    // HAND THE SCREEN OVER TO THE LIVE GALAXY
    //
    // Up to this point the screen has been showing a PREVIEW — a private stage rendered to a texture,
    // because until the galaxy is visualized there is nothing real to point a camera at. From here on
    // there is, so the preview goes and the real camera shows through.
    //
    // The bar STAYS. That is the whole idea the request is built on: the loading screen stops being a
    // picture with a bar over it and becomes the game's own camera with a bar over it. Generation is
    // still running underneath — the remaining systems, the economy, the faction seeding — and the bar
    // goes on reporting real progress against real work while the player watches their world form.
    //
    // The panel's BACKGROUND is what gets cleared, not the panel itself: the bar and the captions live
    // inside it, so switching the panel off would take them with it.
    // ============================================================================================
    public void SwitchToLiveView()
    {
        if (root == null) return;

        // The preview stage, gone. Its camera would otherwise keep rendering a sphere nobody can see.
        if (previewView != null) previewView.gameObject.SetActive(false);
        if (previewStage != null) previewStage.gameObject.SetActive(false);

        // The screen's own procedural sky goes too — the real backdrop is behind the real camera now,
        // and two starfields at different parallaxes on top of each other reads as a smear.
        { var sg = SkyGroup; if (sg != null) sg.alpha = 0f; }

        var panel = root.GetComponent<Image>();
        if (panel != null)
        {
            panel.color = new Color(0f, 0f, 0f, 0f);
            // Stop eating clicks at the same moment it stops being visible. The player still has no
            // control (the sequence owns the camera and TimeControl is stopped), but leaving an
            // invisible pane of glass over a live game is the kind of thing that outlives its reason.
            panel.raycastTarget = false;
        }
    }

    // ============================================================================================
    // THE TITLES, FOR THE LIVE SEQUENCE
    //
    // GenesisSequence films the real world with the real camera, so it needs the two lines of text and
    // nothing else this screen does — no preview, no bar collapse, no cross-fade. This is that: hide
    // the loading furniture, show the message over the live galaxy, and get out of the way.
    //
    // The panel's own background goes fully transparent rather than the panel being switched off,
    // because the text lives inside it — turning it off would take the titles with it.
    // ============================================================================================
    public void ShowGenesisTitles(string homeName)
    {
        if (root == null) return;

        if (barTrack != null) barTrack.gameObject.SetActive(false);
        if (headline != null) headline.gameObject.SetActive(false);
        if (stageLabel != null) stageLabel.gameObject.SetActive(false);
        if (percentLabel != null) percentLabel.gameObject.SetActive(false);
        if (previewView != null) previewView.gameObject.SetActive(false);
        { var sg = SkyGroup; if (sg != null) sg.alpha = 0f; }

        var panel = root.GetComponent<Image>();
        if (panel != null) panel.color = new Color(0f, 0f, 0f, 0f);

        // Raycasts stop being blocked at the same moment the panel stops being visible, or the player
        // would be left clicking through a pane of glass they cannot see.
        if (panel != null) panel.raycastTarget = false;

        EnsureWelcome();
        if (welcomeLabel == null) return;

        welcomeLabel.text = string.IsNullOrWhiteSpace(homeName)
            ? "Your universe awaits..."
            : $"Welcome to your homeworld, <color=#FFD24D>{homeName}</color>";
        welcomeLabel.gameObject.SetActive(true);
        StartCoroutine(FadeTitles());
    }

    IEnumerator FadeTitles()
    {
        if (welcomeLabel == null) yield break;

        for (float e = 0f; e < WelcomeFadeIn; e += Time.unscaledDeltaTime)
        {
            var c = welcomeLabel.color; c.a = Mathf.Clamp01(e / WelcomeFadeIn); welcomeLabel.color = c;
            yield return null;
        }
        var full = welcomeLabel.color; full.a = 1f; welcomeLabel.color = full;

        for (float e = 0f; e < WelcomeHold; e += Time.unscaledDeltaTime) yield return null;

        for (float e = 0f; e < WelcomeFadeOut; e += Time.unscaledDeltaTime)
        {
            var c = welcomeLabel.color; c.a = 1f - Mathf.Clamp01(e / WelcomeFadeOut); welcomeLabel.color = c;
            yield return null;
        }
        welcomeLabel.gameObject.SetActive(false);   // gone, not merely transparent
        Close();
    }

    void EnsureWelcome()
    {
        if (welcomeLabel != null || barTrack == null) return;
        welcomeLabel = UIFactory.Text(barTrack.parent, "", 34, UITheme.Accent, TextAlignmentOptions.Center);
        welcomeRT = welcomeLabel.rectTransform;
        welcomeRT.anchorMin = new Vector2(0, 1); welcomeRT.anchorMax = new Vector2(1, 1);
        welcomeRT.pivot = new Vector2(0.5f, 1);
        welcomeRT.sizeDelta = new Vector2(0, 46);
        // Below the planet, which has just arrived at the column's centre.
        welcomeRT.anchoredPosition = new Vector2(0, -150f);
        welcomeLabel.gameObject.SetActive(false);
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

    // The corona quads that make a preview sun read as a light source rather than a coloured ball.
    //
    // Bloom cannot do this job here: the preview renders to a plain LDR RenderTexture with no post stack,
    // which the canvas composites afterwards — so the HDR emission the galaxy view relies on would clamp
    // to white instead of blooming (the same reason BuildStarMaterial keeps its colour in gamut). Real
    // additive geometry glows with no post-processing at all, which is exactly why SpaceMaterials.Glow
    // exists for the in-game stars; this is the same two-quad treatment GalaxyLOD gives them.
    readonly Transform[] sunGlow = new Transform[3];
    readonly Renderer[] sunGlowInner = new Renderer[3];
    readonly Renderer[] sunGlowOuter = new Renderer[3];
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
    // 96x48. Finer than it needs to be for a thumbnail, and that is the point: the reveal is the
    // showpiece of the second half of the load, and at 40x20 the "tiles" were chunky enough to read as
    // an effect rather than as a world being built. At this resolution the downsample below is close
    // enough to the real grid that what appears IS the planet you are about to play on.
    //
    // Still fixed and small relative to the real body (which can be 640x320): this reads the
    // ALREADY-GENERATED surface once and only ever repaints this tiny texture afterwards, so the reveal
    // costs the same whether the world is a 20x10 moon or a 640x320 gas giant.
    const int MorphW = 96, MorphH = 48;
    // Public so GameManager can hold the Planet subject on screen long enough to see the whole reveal —
    // see GenerateGalaxyRoutine, same reasoning as PopBeat/PopGrow above.
    public const float MorphDuration = 9f;

    /// How long each moon takes to reveal once the planet is finished. Quicker than the planet on
    /// purpose — the planet is the subject, the moons are the coda.
    public const float MoonMorphDuration = 2.6f;

    /// Gap between one moon finishing and the next appearing.
    public const float MoonBeat = 0.5f;

    // ============================================================================================
    // A TRANSFORMATION, NOT A REVEAL
    //
    // This used to uncover the finished world one tile at a time in a shuffled order. It read as a
    // picture being wiped in, because that is what it was: every tile that appeared was already in its
    // final state, and nothing ever CHANGED once it was on screen.
    //
    // Now the world is generated at a series of intermediate states and stepped through them. A wet
    // world starts drowned — one flat global ocean — and its continents rise out of it; a dry one starts
    // as bare, featureless rock and grows its relief, its mountains and its ice. Each tile passes
    // through several biomes on the way to what it ends up as, which is the difference between watching
    // a world be built and watching a photograph load.
    //
    // The mechanism is the one the request guessed at: it IS the terrain sliders being driven, just with
    // the panel hidden. Each stage is the SAME noise field (same seed, same continent frequency) sampled
    // with terrainParams lerped from a primordial state toward the world's real ones — so the continents
    // that grow are the continents that will be there, in the places they will be.
    //
    // Cost: (MorphStages - 1) x MorphW x MorphH samples, once, when the homeworld is handed over. About
    // 40k noise samples — a few tens of milliseconds on the frame the reveal is set up, inside a loading
    // screen that is about to spend nine seconds playing it. The per-frame cost afterwards is one array
    // write per texel, which is what it always was.
    // ============================================================================================
    const int MorphStages = 10;

    // Fewer octaves than the real grid (which uses six). These are intermediate frames at 96x48 shown
    // for under a second each; the fine coastline detail six octaves buys is invisible at this size and
    // costs a third more time per sample. The FINAL frame does not go through this path at all — it is
    // the real surface, so what the world settles into is exact.
    const int MorphOctaves = 4;

    Texture2D morphTex;
    Color[] morphPixels;     // the texture's CURRENT contents, mutated in place
    Color[] morphFinal;      // the REAL finished surface, downsampled once — the last stage, exactly
    Color[][] morphStages;   // [stage][texel] — the world at each point along its development
    float[] morphJitter;     // per-texel offset, so tiles change one by one rather than the map flipping
    float morphClock;
    bool morphActive;

    // ---- Building a world's development, shared by the planet and its moons ---------------------

    /// What a world looks like before it is a world.
    ///
    /// Two primordial states, and which one a body gets is decided by where it ENDS up: a world that
    /// finishes with real seas starts drowned under one flat global ocean and grows its continents out
    /// of it; a dry one starts as bare, featureless rock. Both are the "primordial orb with no features"
    /// the request describes, and both are reached by the same means — the world's own noise field with
    /// its relief flattened and its sea slid to one extreme.
    static PlanetTerrainGenerator.NoiseParams Primordial(PlanetTerrainGenerator.NoiseParams f)
    {
        bool wet = f.SeaLevelOrNeutral >= 0.30f;
        return new PlanetTerrainGenerator.NoiseParams
        {
            scale = f.scale,          // the SAME feature size, so the continents that grow are the real ones
            elevation = 0.12f,        // almost no relief — an orb, not a landscape
            moisture = wet ? f.moisture : 0.10f,
            heat = f.heat,            // where it orbits is a fact about the world, not something it develops
            ridge = 0.05f,            // no mountains yet
            seaLevel = wet ? 1f : 0f  // drowned, or bone dry
        };
    }

    static PlanetTerrainGenerator.NoiseParams LerpParams(
        PlanetTerrainGenerator.NoiseParams a, PlanetTerrainGenerator.NoiseParams b, float t)
        => new PlanetTerrainGenerator.NoiseParams
        {
            scale = Mathf.Lerp(a.scale, b.scale, t),
            elevation = Mathf.Lerp(a.elevation, b.elevation, t),
            moisture = Mathf.Lerp(a.moisture, b.moisture, t),
            heat = Mathf.Lerp(a.heat, b.heat, t),
            ridge = Mathf.Lerp(a.ridge, b.ridge, t),
            seaLevel = Mathf.Lerp(a.SeaLevelOrNeutral, b.SeaLevelOrNeutral, t)
        };

    /// The world at each point along its development, ending on the REAL finished surface.
    ///
    /// The last stage is `finalFrame` — the downsample of the actual generated grid — rather than
    /// another sample of the noise field. The surface is gameplay data (buildings get placed on it), so
    /// what the world settles into has to be exactly what the player then plays on, not a close match
    /// drawn with fewer octaves.
    /// One frame of a world's development. Stage `MorphStages - 1` is not built here — it is the real
    /// generated surface, supplied by the caller.
    static Color[] BuildStage(CelestialBody body, int w, int h, int stage)
    {
        var final = body.terrainParams;
        var p = LerpParams(Primordial(final), final, stage / (float)(MorphStages - 1));

        var frame = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                var smp = PlanetTerrainGenerator.SampleNormalized(body, (x + 0.5f) / w, v, p, MorphOctaves);
                Color c = TerrainColorMap.Get(smp.terrain);
                float b = Mathf.Lerp(0.86f, 1.12f, smp.shade);
                frame[y * w + x] = new Color(c.r * b, c.g * b, c.b * b, 1f);
            }
        }
        return frame;
    }

    /// The array a world's development will be built into, with only its first and last frames filled.
    ///
    /// BUILT ONE FRAME PER UPDATE, not all at once. Nine stages x 96x48 is ~41,000 noise samples, which
    /// is on the order of a tenth of a second on the main thread — and the frame it would land on is the
    /// exact moment the homeworld is handed over and the reveal begins. That is the one moment in the
    /// whole load where a stutter is guaranteed to be seen, and the surrounding code already goes out of
    /// its way to keep it clean (see the string built once outside the reveal loop in GameManager).
    ///
    /// Spreading it costs nothing in practice: a stage is on screen for MorphDuration/9 ≈ one second,
    /// and one is built per frame, so the builder runs about sixty times ahead of what is being shown.
    static Color[][] NewStages(CelestialBody body, int w, int h, Color[] finalFrame)
    {
        var stages = new Color[MorphStages][];
        stages[0] = BuildStage(body, w, h, 0);        // needed on the very first frame
        stages[MorphStages - 1] = finalFrame;         // the real surface, already known
        return stages;
    }

    /// Fill in the next unbuilt stage, if there is one. Cheap no-op once they all exist.
    static void BuildNextStage(CelestialBody body, int w, int h, Color[][] stages)
    {
        if (stages == null) return;
        for (int s = 1; s < MorphStages - 1; s++)
            if (stages[s] == null) { stages[s] = BuildStage(body, w, h, s); return; }
    }

    /// Per-texel offsets in stage units, so the map does not flip over all at once — each tile crosses
    /// into its next state at its own moment and the change reads as tiles forming rather than as a
    /// slideshow. From System.Random and never UnityEngine.Random, for the reason the old shuffled
    /// reveal order was: this is cosmetic, and it must not perturb the shared gameplay RNG stream that
    /// the generators and FactionAI keep drawing from while it plays.
    static float[] BuildJitter(int n, int seed)
    {
        var j = new float[n];
        var rng = new System.Random(seed);
        for (int i = 0; i < n; i++) j[i] = (float)rng.NextDouble();
        return j;
    }

    /// Write the frame for a body partway through its development. `t` is 0..1 across the whole morph.
    static void PaintStage(Color[][] stages, float[] jitter, Color[] into, float t)
    {
        float g = Mathf.Clamp01(t) * (MorphStages - 1);
        for (int i = 0; i < into.Length; i++)
        {
            // Hard switch rather than a colour blend between stages. Blending would smear two biomes
            // into a colour that is neither, which reads as a dissolve; switching keeps every texel a
            // real terrain colour at every instant, which is what makes it read as TILES.
            int s = Mathf.Clamp(Mathf.FloorToInt(g + jitter[i]), 0, MorphStages - 1);

            // Stages are built one per frame (see NewStages), so in principle one can be reached before
            // it exists. Walking back to the newest built frame keeps the world a beat behind rather
            // than throwing — and in practice the builder runs about sixty stages ahead, so this never
            // fires. It is here because "in practice" is not a guarantee on a stalled frame.
            while (s > 0 && stages[s] == null) s--;
            if (stages[s] == null) continue;

            into[i] = stages[s][i];
        }
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
            TintSunCorona(0, homeStar);
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
            if (i < stars.Count) TintSunCorona(i, stars[i]);
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
        // 0.28, not 0.55, because EmissionStrength's floor moved above 1.0 (see StarData). At the old
        // factor every class from G upward clamped to 1 and the preview lost all variety between a red
        // dwarf and a blue giant. This keeps a G star at the same ~0.54 it always had and lets the
        // hotter classes climb from there.
        float k = Mathf.Clamp(StarDatabase.EmissionStrength(star) * 0.28f, 0.30f, 1f);
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
        homeBody = planet;       // the real world this preview is standing in for
        atmosphereOn = false;    // a fresh reveal starts as bare rock and earns its sky
        EnsureMorphTexture();

        Color barren = TerrainColorMap.Get(TerrainType.Barren);

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
                    // No ore tint: deposits live on the Mineral Index overlay now, and this preview has
                    // no overlay. It matches what SurfaceTextureRenderer draws for the real map.
                }
                morphFinal[y * MorphW + x] = c;
            }
        }

        // Only the first frame and the last are built here; the rest fill in one per Update, so the
        // handover to the homeworld does not stutter. See NewStages.
        morphStages = NewStages(planet, MorphW, MorphH, morphFinal);
        PaintStage(morphStages, morphJitter, morphPixels, 0f);

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

    // ---- The homeworld's MOONS ------------------------------------------------------------------
    //
    // They appear only once the planet's own surface has fully revealed, one at a time, each running the
    // same tile-by-tile reveal at a quicker pace — and then orbit. A world arriving complete with its
    // moons already in place says nothing; a world finishing and then acquiring them says "and there is
    // more here than the one planet", which is the note the last stretch of the load wants to end on.
    //
    // Deliberately capped at three: past that they crowd the frame at this camera distance, and the
    // point is legibility rather than inventory.
    const int MaxPreviewMoons = 3;

    class MoonPreview
    {
        /// The real moon this stands for — so the preview can take its atmosphere and, at the hand-off,
        /// its actual position relative to the planet.
        public CelestialBody source;
        public Transform body;
        public Renderer rend;
        public Material mat;
        public Texture2D tex;
        // The REAL surface texture this moon takes on once its development finishes
        // (FinishMoonTexture). Tracked so ClearMoons can free it — it is built here, so nothing else
        // owns it, and a load leaks one per moon otherwise.
        public Texture2D realTex;
        public Color[] pixels, final;
        public Color[][] stages;   // this moon's own development, ending on `final`
        public float clock;
        public bool revealing, done;
        public float angle, radius, speed, size;
    }

    readonly List<MoonPreview> moons = new List<MoonPreview>();
    int moonsShown;        // how many have begun their reveal
    float moonTimer;       // counts down to the next one appearing

    /// Hand over the homeworld's real moons. Called with the same bodies the player will find in orbit,
    /// so what reveals here is what is actually there.
    public void SetHomeMoons(List<CelestialBody> list)
    {
        ClearMoons();
        if (list == null || previewStage == null) return;

        Color barren = TerrainColorMap.Get(TerrainType.Barren);
        int n = Mathf.Min(MaxPreviewMoons, list.Count);

        for (int i = 0; i < n; i++)
        {
            var src = list[i];
            if (src?.surface == null) continue;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "PreviewMoon" + i;
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            sphere.transform.SetParent(previewStage, false);

            var spin = sphere.AddComponent<SelfSpin>();
            spin.speed = 30f;
            spin.unscaled = true;

            var m = new MoonPreview
            {
                source = src,
                body = sphere.transform,
                rend = sphere.GetComponent<Renderer>(),
                // Sized against the planet, not against reality: a real moon at true relative scale is a
                // speck at this framing. Proportions between moons are preserved, so a big one still
                // reads as the big one.
                size = PreviewScale * Mathf.Lerp(0.16f, 0.30f, Mathf.Clamp01(src.mass / 1.5f)),
                // Kept well inside the frame. The camera sits at 4.6x PreviewScale with a 38-degree FOV,
                // so the visible half-extent at the stage centre is about 1.58x PreviewScale — and it is
                // SMALLER on the near side of an orbit, where a moon is closer to the lens. Radii past
                // ~1.45 swing off-screen for part of every lap.
                radius = PreviewScale * (0.85f + i * 0.30f),
                speed = 26f - i * 5f,
                angle = i * 120f
            };

            // Its own material and its own texture — every moon reveals independently, so they cannot
            // share the one the planet is mutating.
            m.tex = new Texture2D(MoonMorphW, MoonMorphH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            m.pixels = new Color[MoonMorphW * MoonMorphH];
            m.final = new Color[MoonMorphW * MoonMorphH];

            int sw = src.surface.width, sh = src.surface.height;
            for (int y = 0; y < MoonMorphH; y++)
            {
                int sy = Mathf.Clamp(y * sh / MoonMorphH, 0, sh - 1);
                for (int x = 0; x < MoonMorphW; x++)
                {
                    int sx = Mathf.Clamp(x * sw / MoonMorphW, 0, sw - 1);
                    var tile = src.surface.tiles[sx, sy];
                    Color c = barren;
                    if (tile != null)
                    {
                        c = TerrainColorMap.Get(tile.type);
                        float b = Mathf.Lerp(0.86f, 1.12f, tile.shade);
                        c = new Color(c.r * b, c.g * b, c.b * b, 1f);
                    }
                    m.final[y * MoonMorphW + x] = c;
                }
            }

            // The same development the planet gets, at the moons' own resolution — the request asks for
            // the identical transformation, and it is the identical code.
            m.stages = NewStages(src, MoonMorphW, MoonMorphH, m.final);
            PaintStage(m.stages, moonJitter, m.pixels, 0f);
            m.tex.SetPixels(m.pixels);
            m.tex.Apply();

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            m.mat = LitMat(lit, Color.white);
            m.mat.mainTexture = m.tex;
            if (m.mat.HasProperty("_BaseMap")) m.mat.SetTexture("_BaseMap", m.tex);
            if (m.rend != null) m.rend.sharedMaterial = m.mat;

            m.body.localScale = Vector3.one * m.size;
            sphere.SetActive(false);   // nothing until the planet has finished
            moons.Add(m);
        }

        moonsShown = 0;
        moonTimer = MoonBeat;
    }

    // The preview camera's framing, as constants so the handoff can do arithmetic with them rather than
    // guessing. Kept next to the values BuildPreview actually uses.
    const float PreviewCamDistance = PreviewScale * 4.6f;
    const float PreviewFov = 38f;

    /// What fraction of the viewport's HEIGHT the planet occupies at the end of the finale's travel.
    ///
    /// This is the number that makes the cross-fade seamless: the real camera is framed so the actual
    /// homeworld covers exactly this much of the screen, so the image being faded out and the scene
    /// being faded in are the same size as well as in the same place.
    ///
    /// Two factors multiplied. Inside the render target, the planet (diameter = PreviewScale) sits in a
    /// visible extent of 2 * distance * tan(fov/2). That target is then drawn into a box of
    /// previewHomeSize * 1.35 within a full-screen panel — so the second factor is that box's height
    /// against the panel's.
    ///
    /// Height, not width, deliberately: the render target is square and the screen is not, so a
    /// width-based solve would drift with aspect ratio.
    public float HandoffScreenFraction()
    {
        float visible = 2f * PreviewCamDistance * Mathf.Tan(PreviewFov * 0.5f * Mathf.Deg2Rad);
        if (visible <= 0.01f) return 0f;
        float shareOfTarget = PreviewScale / visible;

        var rootRT = root != null ? root.GetComponent<RectTransform>() : null;
        float panelH = rootRT != null ? rootRT.rect.height : 0f;
        if (panelH <= 1f) return 0f;                    // unmeasured canvas — let the caller fall back

        float boxH = previewHomeSize.y * TravelGrow;
        return shareOfTarget * (boxH / panelH);
    }

    // ---- Atmosphere, and matching the real world exactly ---------------------------------------
    //
    // The preview's whole job by this point is to be indistinguishable from the planet the player is
    // about to be looking at. So it does not approximate it — it is given the SAME atmosphere and cloud
    // shells the real body carries (PlanetAppearance builds both, from the same body data), and then
    // aligned to the real geometry: same viewing angle, same spin, same moon positions, same sunlight.
    CelestialBody homeBody;
    bool atmosphereOn;
    bool aligning;      // true once the camera is on the real world and the preview must track it

    /// Wrap the preview planet — and its moons — in the air they actually have.
    ///
    /// Called when the surface reveal finishes and the moons start forming, so the world gains its sky
    /// at the moment it stops being bare rock. A moon with no air gets none: PlanetAppearance skips the
    /// shell below atmosphereThickness 0.06, which is the same rule that decides it in the real system,
    /// so an airless rock looks airless in both places.
    void RevealAtmosphere()
    {
        if (atmosphereOn || homeBody == null || previewBody == null) return;
        atmosphereOn = true;

        // THE REAL TEXTURE, not the reveal's own.
        //
        // The morph runs on a deliberately tiny 96x48 point-filtered grid — that is what makes the
        // tile-by-tile reveal cost the same on a moon and a gas giant. But the real globe is the full
        // surface grid, hundreds of texels wide and bilinear-filtered, so cross-fading one into the
        // other would dissolve a hard-edged mosaic into a smooth detailed world and visibly re-shape
        // every coastline. Handing the preview the exact texture the real planet wears closes that gap
        // completely, and this is the right moment: the world "finishes" as it gains its sky.
        // Written onto the SHARED material we own, not through PlanetAppearance.RefreshTexture.
        //
        // RefreshTexture reads `rend.material`, which INSTANTIATES a clone — so the preview would end up
        // wearing a copy that nothing tracks. The next load's SetSubject reassigns sharedMaterial back to
        // the shared one, orphaning that clone and its full-size surface texture: one Material and one
        // Texture2D leaked per load. It also destroyed the morph texture as a side effect, which was
        // harmless only by luck.
        ApplyRealPlanetTexture();

        PlanetAppearance.AddAtmosphere(homeBody, previewBody.gameObject);
        PlanetAppearance.AddClouds(homeBody, previewBody.gameObject);

        for (int i = 0; i < moons.Count; i++)
        {
            if (moons[i].body == null || moons[i].source == null) continue;

            // NO RefreshTexture HERE — that is the planet's moment, not the moons'.
            //
            // This ran at the START of the moon phase, before a single moon had begun revealing, and
            // RefreshTexture DESTROYS the texture already on the material (PlanetAppearance.cs — it has
            // to, or terraforming would leak one surface texture per refresh). That texture is each
            // moon's morph texture. So every moon's reveal was painting into a destroyed Texture2D,
            // which is the MissingReferenceException out of SetPixels, once per moon per frame.
            //
            // A moon takes its real texture when IT finishes instead — see FinishMoonTexture.
            PlanetAppearance.AddAtmosphere(moons[i].source, moons[i].body.gameObject);
            PlanetAppearance.AddClouds(moons[i].source, moons[i].body.gameObject);
        }
    }

    /// The planet's twin of FinishMoonTexture — see the note at its call site for why it does not go
    /// through PlanetAppearance.RefreshTexture.
    Texture2D realPlanetTex;

    void ApplyRealPlanetTexture()
    {
        if (homeBody == null || subjectMats == null) return;
        var mat = subjectMats[(int)Subject.Planet];
        if (mat == null) return;

        var real = SurfaceTextureRenderer.BuildGrid(homeBody) ?? SurfaceTextureRenderer.Build(homeBody);
        if (real == null) return;

        real.wrapMode = TextureWrapMode.Repeat;
        real.filterMode = FilterMode.Bilinear;

        // Free the one from a PREVIOUS generation before replacing it — this screen is a singleton that
        // outlives any one game.
        if (realPlanetTex != null && realPlanetTex != real) Destroy(realPlanetTex);
        realPlanetTex = real;

        mat.mainTexture = real;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", real);
    }

    /// Hand a finished moon the REAL world's surface texture, and retire its morph texture.
    ///
    /// The planet's equivalent is the RefreshTexture call in RevealAtmosphere above, and for the same
    /// reason: the morph runs on a tiny point-filtered grid, and cross-fading that into the real
    /// bilinear globe would visibly re-shape every coastline. The difference is timing — a moon earns
    /// it when its own development completes, not when the planet's does.
    ///
    /// Written straight onto `m.mat`, the material we own, rather than through
    /// PlanetAppearance.RefreshTexture: that reads `rend.material`, which INSTANTIATES a clone — so the
    /// moon would end up wearing a copy nothing tracks, and ClearMoons would destroy the original while
    /// the clone and its texture leaked, once per moon per load.
    void FinishMoonTexture(MoonPreview m)
    {
        if (m == null || m.mat == null || m.source == null) return;

        var real = SurfaceTextureRenderer.BuildGrid(m.source) ?? SurfaceTextureRenderer.Build(m.source);
        if (real != null)
        {
            real.wrapMode = TextureWrapMode.Repeat;
            real.filterMode = FilterMode.Bilinear;
            m.mat.mainTexture = real;
            if (m.mat.HasProperty("_BaseMap")) m.mat.SetTexture("_BaseMap", real);
            m.realTex = real;      // tracked so ClearMoons can free it
        }

        // The morph texture has done its job. Nulled as well as destroyed, so the reveal loop's own
        // guard reads it as finished rather than as a live texture that happens to be dead.
        if (m.tex != null) { Destroy(m.tex); m.tex = null; }
    }

    /// Point the preview at the real world the same way the game camera is about to.
    ///
    /// This is what makes the hand-off invisible. Three things have to agree, and all three are read
    /// from the live scene rather than guessed:
    ///
    ///   * THE ANGLE. The game camera looks down at a fixed pitch from a rotatable yaw. Copying its
    ///     rotation onto the preview camera, and placing that camera along the same direction from the
    ///     stage centre, means the player is looking at the preview sphere from exactly the bearing they
    ///     will be looking at the real one.
    ///   * THE SPIN. The real body's visual has its own rotation, driven by SelfSpin since it was built.
    ///     Copying it puts the same continent under the same pixel.
    ///   * THE MOONS. Their real positions relative to the planet, scaled into preview space, so a moon
    ///     that is to the upper-left of the planet stays upper-left through the dissolve.
    ///
    /// And the light: aimed along the real star-to-planet vector, so the terminator falls in the same
    /// place. Without that the two images can be identically framed and still obviously different.
    void AlignToReal()
    {
        if (homeBody?.visualObject == null || previewCam == null || previewBody == null) return;

        var cam = CameraController.Instance;
        var gameCam = cam != null ? cam.GetComponent<Camera>() : Camera.main;
        if (gameCam == null) return;

        Vector3 planetPos = homeBody.visualObject.transform.position;
        Vector3 toCam = gameCam.transform.position - planetPos;
        if (toCam.sqrMagnitude < 0.0001f) return;

        // The preview's own idle spin has to stop, or it and the real rotation fight for the same
        // transform in an undefined component order and the sphere jitters. From here the real world
        // drives it.
        StopIdleSpin(previewBody);
        for (int i = 0; i < moons.Count; i++) StopIdleSpin(moons[i].body);

        // Same bearing, preview's own distance — the framing is set by HandoffScreenFraction, not by
        // however far the real camera happens to be.
        previewCam.transform.localPosition = toCam.normalized * PreviewCamDistance;
        previewCam.transform.localRotation = Quaternion.LookRotation(-toCam.normalized, gameCam.transform.up);

        // The sphere's own spin. SelfSpin drives the preview independently, so overwrite it outright
        // rather than nudging it — this is the frame where the two must simply be the same.
        previewBody.localRotation = homeBody.visualObject.transform.rotation;

        // The cloud layer's PHASE. Both shells carry their own SelfSpin at the same speed, but the real
        // one has been turning since Visualize and the preview's was created seconds ago, so they sit at
        // unrelated angles — over a bright ocean world that is the most legible mismatch left once the
        // texture matches. Freeze the preview's and copy the real angle outright.
        MatchChildRotation("Clouds");

        // Sunlight from where the star actually is, in the star's own colour — the terminator can land
        // perfectly and still read wrong if one image is lit white and the other amber.
        if (previewLight != null && homeBody.hostStar != null)
        {
            previewLight.color = homeBody.hostStar.color;
            Vector3 starPos = StarWorldPosition(homeBody);
            Vector3 toStar = starPos - planetPos;
            if (toStar.sqrMagnitude > 0.0001f)
                previewLight.transform.localPosition = toStar.normalized * (PreviewScale * 3f);
        }

        // Moons: ONE world-to-preview factor for both position AND size.
        //
        // These were previously normalised so the outermost moon always sat at a fixed framing radius,
        // while their sizes stayed cosmetic. That matched only their BEARINGS: the preview showed three
        // fat moons hugging the planet, and the real scene — framed on the planet alone — showed tiny
        // specks far outside the frame. The cross-fade cannot survive that.
        //
        // Deriving both from the same factor means a moon that is genuinely off-frame leaves the frame
        // in the preview too, which is the correct answer rather than a flattering one.
        float planetWorld = OrbitSafety.Scale(homeBody);
        if (planetWorld < 0.0001f) return;
        float k = PreviewScale / planetWorld;

        for (int i = 0; i < moons.Count; i++)
        {
            var m = moons[i];
            if (m.body == null || m.source?.visualObject == null) continue;
            m.body.localPosition = (m.source.visualObject.transform.position - planetPos) * k;
            m.body.localScale = Vector3.one * (OrbitSafety.Scale(m.source) * k);
            m.body.localRotation = m.source.visualObject.transform.rotation;
        }
    }

    /// Put a named shell (Clouds, Atmosphere) on the preview at the same angle as the real body's.
    void MatchChildRotation(string childName)
    {
        if (homeBody?.visualObject == null || previewBody == null) return;
        var real = homeBody.visualObject.transform.Find(childName);
        var prev = previewBody.Find(childName);
        if (real == null || prev == null) return;
        StopIdleSpin(prev);
        prev.rotation = real.rotation;
    }

    static void RestoreIdleSpin(Transform t, float speed)
    {
        if (t == null) return;
        var spin = t.GetComponent<SelfSpin>();
        if (spin == null) spin = t.gameObject.AddComponent<SelfSpin>();
        spin.speed = speed;
        spin.unscaled = true;
        spin.enabled = true;
        t.localRotation = Quaternion.identity;
    }

    static void StopIdleSpin(Transform t)
    {
        if (t == null) return;
        var spin = t.GetComponent<SelfSpin>();
        if (spin != null && spin.enabled) spin.enabled = false;
    }

    /// Where this world's star actually is in the scene.
    static Vector3 StarWorldPosition(CelestialBody b)
    {
        // The system pivot is the star's own position — bodies orbit it — so the system's render root
        // is the light source's location.
        var sys = b.system;
        return sys?.pivot != null ? sys.pivot.position : Vector3.zero;
    }

    /// How many moons are actually staged — which is not the same as how many the planet HAS, since one
    /// with no baked surface is skipped. GameManager sizes its closing hold off this, so the screen does
    /// not sit on a still frame waiting for a moon that was never built.
    public int StagedMoonCount => moons.Count;

    const int MoonMorphW = 40, MoonMorphH = 20;

    /// Per-texel stage offsets for the moons — see BuildJitter. A different seed from the planet's so
    /// three bodies developing in the same frame do not change in lockstep with each other.
    static readonly float[] moonJitter = BuildJitter(MoonMorphW * MoonMorphH, 20260721);

    void StepMoons(float dt)
    {
        // Nothing until the planet itself is finished — that is the whole staging.
        if (morphActive) return;

        // (There is deliberately NO `aligning` early-out here. An earlier version had one, on the theory
        // that AlignToReal was destroying the moons' morph textures — it is not; RevealAtmosphere was,
        // by handing every moon its real texture before any of them had begun developing. That is fixed
        // at the source now, in RevealAtmosphere and FinishMoonTexture. Returning early here would also
        // have skipped RevealAtmosphere below and left a moonless world cross-fading bare rock into a
        // planet wearing an atmosphere.)

        // The world gains its sky at exactly the moment the moons begin forming, so the two happen
        // together: the planet stops being bare rock as its moons arrive.
        //
        // BEFORE the moon-count guard, deliberately: a homeworld with NO moons still has air, and while
        // this sat after the early-out such a world revealed as bare rock and then cross-faded into a
        // real planet wearing both an atmosphere and a cloud layer.
        RevealAtmosphere();

        if (moons.Count == 0) return;

        if (moonsShown < moons.Count)
        {
            moonTimer -= dt;
            if (moonTimer <= 0f)
            {
                var next = moons[moonsShown];
                next.body.gameObject.SetActive(true);
                next.revealing = true;
                moonsShown++;
                moonTimer = MoonMorphDuration + MoonBeat;
            }
        }

        for (int i = 0; i < moons.Count; i++)
        {
            var m = moons[i];
            if (!m.body.gameObject.activeSelf) continue;

            // Orbit. Around the stage centre, which is where the planet sits. Runs for every ACTIVE
            // moon, revealing or finished — a moon that stopped moving the instant its surface was done
            // would freeze in place while its siblings kept going.
            m.angle += m.speed * dt;
            float rad = m.angle * Mathf.Deg2Rad;
            m.body.localPosition = new Vector3(Mathf.Cos(rad) * m.radius, 0f, Mathf.Sin(rad) * m.radius);

            if (!m.revealing) continue;

            if (m.stages == null) { m.revealing = false; m.done = true; continue; }

            // A finished moon has had its morph texture retired (FinishMoonTexture). Anything else that
            // takes it away is a bug — but this method throwing every frame for the rest of the load is
            // a worse way to find out about it than the moon quietly arriving early.
            if (m.tex == null) { m.revealing = false; m.done = true; continue; }

            // Same one-per-frame build as the planet. A moon's grid is 40x20, so its whole development
            // is under a fifth of the planet's cost — but it is built while the planet is still on
            // screen finishing, and a hitch there would land on the reveal it is meant to follow.
            BuildNextStage(m.source, MoonMorphW, MoonMorphH, m.stages);

            m.clock += dt;
            float mt = Mathf.Clamp01(m.clock / MoonMorphDuration);

            PaintStage(m.stages, moonJitter, m.pixels, mt);
            m.tex.SetPixels(m.pixels);
            m.tex.Apply();

            // Its development is complete — swap the mosaic for the real globe, exactly as the planet
            // does when it gains its sky.
            if (mt >= 1f) { m.revealing = false; m.done = true; FinishMoonTexture(m); }
        }
    }

    void ClearMoons()
    {
        foreach (var m in moons)
        {
            if (m.body != null) Destroy(m.body.gameObject);
            if (m.mat != null) Destroy(m.mat);
            if (m.tex != null) Destroy(m.tex);
            if (m.realTex != null) Destroy(m.realTex);
        }
        moons.Clear();
        moonsShown = 0;
        moonTimer = MoonBeat;
    }

    /// Put the Planet material back to its generic placeholder look. Called once per NEW generation
    /// (Open()), BEFORE that generation's own SetHomePlanet call — without this, a second galaxy in the
    /// same session would show the FIRST game's fully-morphed homeworld texture during its own early
    /// "Forming star system N" placeholder phase, since SetHomePlanet mutates this same persistent
    /// Material in place rather than replacing it.
    void ResetPlanetPlaceholder()
    {
        morphActive = false;
        // Dropped with the texture, for the same reason: this screen is a singleton that outlives any one
        // game, and holding the PREVIOUS galaxy's development frames would both leak them and risk a
        // second generation painting the last game's homeworld before its own stages are built.
        morphStages = null;

        // The previous generation's real surface texture goes the same way — this runs once per new game
        // (Open), and it is the point at which the last game's homeworld stops being anything this
        // screen needs.
        if (realPlanetTex != null) { Destroy(realPlanetTex); realPlanetTex = null; }

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
        morphJitter = BuildJitter(MorphW * MorphH, 20260720);
    }

    /// Advance the world's development. Driven from elapsed time rather than from a per-frame step, so a
    /// slow frame skips ahead rather than letting the animation fall behind real time.
    void StepMorph(float dt)
    {
        if (morphStages == null) { morphActive = false; return; }

        // One stage per frame, well ahead of what is being shown.
        BuildNextStage(homeBody, MorphW, MorphH, morphStages);

        morphClock += dt;
        float t = Mathf.Clamp01(morphClock / MorphDuration);

        PaintStage(morphStages, morphJitter, morphPixels, t);
        morphTex.SetPixels(morphPixels);
        morphTex.Apply();

        // At t == 1 every texel has been clamped to the last stage, which IS the real surface — so the
        // world the player is handed is exactly the one that was generated, not an approximation of it.
        if (t >= 1f) morphActive = false;
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
        // Sun 0's sphere is shared with every other subject, so its corona has to be switched off by hand
        // — otherwise the homeworld would be revealed inside a star's halo.
        if (sunGlow[0] != null) sunGlow[0].gameObject.SetActive(s == Subject.Star);
        // Moons belong to the homeworld and to nothing else — leaving Planet puts them away, so they can
        // never end up orbiting a star. Coming BACK to Planet restores the ones already revealed: their
        // slots are spent (moonsShown has passed them), so without this they would be hidden for good.
        for (int i = 0; i < moons.Count; i++)
            if (moons[i].body != null)
                moons[i].body.gameObject.SetActive(s == Subject.Planet && i < moonsShown);
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
        BuildSunCorona(0, previewBody);

        // Two more suns, built once and reused across every generation — only a binary/trinary home ever
        // activates them (SetHomeCluster), and only after they've popped out (StepSunCluster).
        for (int i = 1; i < sunBody.Length; i++)
        {
            var go = MakeCompanionSun(i);
            go.transform.SetParent(previewStage, false);
            sunBody[i] = go.transform;
            sunRend[i] = go.GetComponent<Renderer>();
            // Companion coronae need no toggling: the whole companion GameObject is only ever active
            // while the cluster is on screen, and its corona is a child of it.
            BuildSunCorona(i, sunBody[i]);
        }

        // The trinary inner pair's shared pivot — see SetHomeCluster/StepSunCluster.
        pairPivot = new GameObject("PreviewPairPivot").transform;
        pairPivot.SetParent(previewStage, false);

        var camGo = new GameObject("LoadingPreviewCam");
        camGo.transform.SetParent(previewStage, false);
        // Far enough back that the WHOLE corona fits with margin.
        //
        // At 2.9 the visible frame was about 200 units across while the outer halo is 2.6x a ~115-unit
        // sun — some 300 units — so the glow was clipped by the render target's edge and the star looked
        // boxed in. At 4.6 the frame is ~317 units: the halo fades out inside it, and the body sits in
        // open black with room around it.
        camGo.transform.localPosition = new Vector3(0f, 0f, -PreviewScale * 4.6f);
        previewCam = camGo.AddComponent<Camera>();
        previewCam.clearFlags = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCam.fieldOfView = 38f;
        previewCam.nearClipPlane = 0.05f * PreviewScale;
        previewCam.farClipPlane = 20f * PreviewScale;
        previewCam.enabled = false;      // driven by hand, one Render per frame

        previewRT = new RenderTexture(320, 320, 16) { name = "LoadingPreviewRT" };
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
        // No shadows on the preview stage either — the star subject would shadow itself, and a moon
        // passing the planet would drop a hard black disc across it mid-reveal. Matches the real system
        // (SystemVisualizer.CreateStarVisual), which is the point: these are the same objects.
        previewLight.shadows = LightShadows.None;

        BuildSubjectMaterials();
        previewBody.gameObject.SetActive(false);   // nothing on screen until a subject is reported
        previewStage.gameObject.SetActive(false);
    }

    /// Hang two additive corona quads on a preview sun: a tight bright one that reads as the star's own
    /// light, and a wide faint one that gives it presence. Same split, and the same reasoning, as
    /// GalaxyLOD's in-game stars — one quad has to choose between looking hot and looking big.
    ///
    /// Sizes are MULTIPLES of the sun's diameter, because these are children of the sun sphere and so
    /// inherit its scale — which is what keeps a small K dwarf's corona proportionally small too, for
    /// free, as ApplyStarScale resizes the parent.
    void BuildSunCorona(int index, Transform sun)
    {
        if (sun == null) return;

        var holder = new GameObject("Corona").transform;
        holder.SetParent(sun, false);
        sunGlow[index] = holder;

        sunGlowInner[index] = MakeCoronaQuad(holder, "Glow", 1.8f, CoronaInnerAlpha);
        sunGlowOuter[index] = MakeCoronaQuad(holder, "Halo", 2.6f, CoronaOuterAlpha);
    }

    static Renderer MakeCoronaQuad(Transform parent, string name, float mult, float alpha)
    {
        var go = SpaceMaterials.Glow(parent, name, mult, new Color(1f, 1f, 1f, alpha));

        // Glow() bills its quad at Camera.main, which here is the GAME camera looking at a half-built
        // galaxy 200,000 units away — it would leave every corona edge-on to the preview camera and so
        // invisible. The preview camera never rotates and neither does the stage, so a fixed identity
        // world rotation IS facing it; LoadingBillboard reasserts that each frame against the parent
        // sun's own spin (SelfSpin), which would otherwise carry the quads round with it.
        var face = go.GetComponent<FaceCamera>();
        if (face != null) Destroy(face);
        go.AddComponent<LoadingBillboard>();

        return go.GetComponent<Renderer>();
    }

    /// Tint one sun's corona to that star's own colour, so a red dwarf glows red and a blue giant blue.
    /// Alpha is authored per quad and preserved — only the hue is taken from the star.
    void TintSunCorona(int index, StarData star)
    {
        if (star == null) return;
        var c = star.color;
        TintQuad(sunGlowInner[index], c, CoronaInnerAlpha);
        TintQuad(sunGlowOuter[index], c, CoronaOuterAlpha);
    }

    // Authored here rather than read back off the material: Material.color maps to _Color, and the URP
    // shaders these quads use expose _BaseColor instead, so a read-back would return white and quietly
    // drive both quads to full strength.
    // The inner quad is only 1.8x the sun's diameter, so the opaque sphere hides everything inside
    // texture radius ~0.55 — which is most of a soft dot's energy. What is left is the outer annulus,
    // and it needs a high alpha to still read as a hot rim. (GalaxyLOD can use 0.85 at 3x because out
    // there the star's disc covers barely a sixth of its glow; here it covers half.)
    const float CoronaInnerAlpha = 0.9f;
    const float CoronaOuterAlpha = 0.18f;

    static void TintQuad(Renderer r, Color c, float alpha)
    {
        // sharedMaterial, not material: Glow() already assigned a per-renderer instance, so this mutates
        // that one in place. Reading `.material` here would instantiate ANOTHER copy on every call — and
        // this runs once per generation, for the life of a singleton that outlives every game.
        if (r == null || r.sharedMaterial == null) return;
        SpaceMaterials.ApplyColor(r.sharedMaterial, new Color(c.r, c.g, c.b, alpha));
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

        // Space fills in behind everything, paced by REAL progress rather than a clock — so the sky and
        // the bar are describing the same thing.
        TickSky(shown, dt);

        // ---- The dots ----
        //
        // Always three of them, FADING rather than being appended one at a time. Appending changes the
        // string's width every cycle, so a centred headline shifts left and right as it animates — read
        // as jitter, not motion. Three dots at varying alpha keeps the text metrics fixed and the wave
        // continuous instead of stepping through four discrete states.
        //
        // Driven purely by unscaled TIME, so its pace is completely independent of how fast the bar is
        // moving, of timeScale, and of whether any progress has been reported at all.
        // Silent during the finale: the bar is collapsing and the headline is already gone, and both
        // would otherwise be rewritten every frame underneath the closing animation.
        if (headline != null && !finaleRunning)
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
        // After the planet, its moons: they appear one at a time, reveal quicker, and orbit. StepMoons
        // holds off on its own until morphActive clears, so the staging lives in one place.
        if (subject == Subject.Planet) StepMoons(dt);

        // Once the finale has handed the camera over, the preview tracks the real world every frame —
        // the planet is orbiting and its moons are moving, so a one-off match goes stale immediately.
        // Runs AFTER StepMoons, which sets its own cosmetic orbit positions, so the real geometry wins.
        if (aligning) AlignToReal();

        if (previewCam != null && previewStage != null && previewStage.gameObject.activeSelf)
            previewCam.Render();

        shown = Mathf.Lerp(shown, goal, 1f - Mathf.Exp(-FillSmoothing * dt));

        // Snap the last sliver. Exponential smoothing approaches its goal but never arrives, so at the
        // end of a load the bar sits a few pixels short and the label reads 99% for the whole hold before
        // the screen closes — the one number a loading bar must get right.
        if (goal - shown < 0.004f) shown = goal;

        // Not while the finale is collapsing the bar — Apply rewrites the fill's width every frame, and
        // the closing animation is the only thing that should be touching it by then.
        if (!finaleRunning) Apply(shown);
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
        // The corona materials too — destroying previewStage takes the quads but not the runtime
        // materials hung off them.
        foreach (var r in sunGlowInner)
            if (r != null && r.sharedMaterial != null) Destroy(r.sharedMaterial);
        foreach (var r in sunGlowOuter)
            if (r != null && r.sharedMaterial != null) Destroy(r.sharedMaterial);
        ClearMoons();
        if (morphTex != null) Destroy(morphTex);
        if (realPlanetTex != null) Destroy(realPlanetTex);
        if (Instance == this) Instance = null;
    }
}

// A streak that crosses the loading sky and removes itself.
//
// Self-destructing rather than pooled: they spawn every couple of seconds for the length of one load and
// never again, so a pool would be more machinery than the thing it manages.
public class LoadingShootingStar : MonoBehaviour
{
    RectTransform rt;
    float speed, life = 1.1f, age;

    public void Init(RectTransform r, float px)
    {
        rt = r; speed = px;
    }

    void Update()
    {
        if (rt == null) { Destroy(gameObject); return; }
        age += Time.unscaledDeltaTime;

        // Travels along its own rotation, so the streak points the way it is going.
        rt.anchoredPosition += (Vector2)(rt.localRotation * Vector3.right) * speed * Time.unscaledDeltaTime;

        var img = GetComponent<UnityEngine.UI.Image>();
        if (img != null)
        {
            var c = img.color;
            c.a = Mathf.Clamp01(1f - age / life) * 0.9f;
            img.color = c;
        }
        if (age >= life) Destroy(gameObject);
    }
}

// Holds a loading-preview corona quad square-on to the preview camera.
//
// Not FaceCamera: that one bills at Camera.main, which during a load is the game camera pointed at a
// half-built galaxy 200,000 units from this stage — every corona would end up edge-on and invisible. The
// preview camera and its stage are both built unrotated and never turn, so identity world rotation is
// facing it. This has to reassert that every frame because the quads are children of the sun spheres,
// and SelfSpin would otherwise carry them round with the surface.
public class LoadingBillboard : MonoBehaviour
{
    // LateUpdate, so it runs after SelfSpin's Update has moved the parent this frame.
    void LateUpdate()
    {
        transform.rotation = Quaternion.identity;
    }

    // Also on enable, because the preview camera is rendered from Update — before any LateUpdate. On the
    // frame a corona is first switched on it would otherwise render at whatever rotation its parent sun
    // had spun to, which can be edge-on: one frame of the halo flickering out as the star appears.
    void OnEnable()
    {
        transform.rotation = Quaternion.identity;
    }
}
