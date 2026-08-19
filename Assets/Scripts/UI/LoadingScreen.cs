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
//
// ============================================================================================
// WHAT THIS CLASS IS NOT, ANY MORE
//
// It used to own a PREVIEW STAGE: a sphere, two corona quads, two companion suns, a point light and a
// camera, parked 200,000 units from the game at (-200000, 0, 0) and rendered to a RenderTexture that was
// composited into the panel as a RawImage. On top of that sat a whole second implementation of world
// generation's visuals — a binary/trinary pop-out, a tile-by-tile terrain morph, cosmetic moons on
// invented orbits, an atmosphere shell, and finally AlignToReal/MatchChildRotation, which existed purely
// to swing that fake into agreement with the real planet so the two could be cross-faded.
//
// GenesisSequence films the REAL bodies with the REAL camera now, so all of it is gone — about 1,200
// lines. The planet that forms is the homeworld, the moons are the moons, and there is nothing left to
// match or dissolve between. What remains here is what a loading screen is actually for: a starfield, a
// headline, a bar that reports real progress, a caption, a Skip button, and the welcome titles.
//
// The one thing that changed for the player: the first ~60% of the load, before the home system's
// visuals exist to film, is now the starfield and the bar rather than a spinning stand-in. There was
// never a real galaxy to point a camera at during that stretch — the old stage's whole reason for
// existing — and the honest version of "nothing to show yet" is not showing a stand-in for it.
// ============================================================================================
public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance;

    GameObject root;
    TMP_Text headline;
    TMP_Text stageLabel;
    TMP_Text percentLabel;
    RectTransform barFill;
    RectTransform barTrack;
    Button skipButton;

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

        // ---- Skip ---------------------------------------------------------------------------------
        //
        // Bottom right, understated, and live for the WHOLE load rather than only for the cinematic.
        // Generation itself cannot be skipped — the galaxy has to exist before anybody can be handed it —
        // so pressed during it this means "don't play the intro when you get there", which is exactly what
        // GenesisSequence does with a request already waiting when Play begins.
        //
        // Parented to the PANEL, not to the centre column: the titles beat switches the bar, headline and
        // captions off, and that should not take the skip with it.
        //
        // It keeps working after SwitchToLiveView clears the panel's own raycastTarget — that flag is per
        // graphic, so a transparent parent stops eating clicks without disabling its children.
        skipButton = UIFactory.Button(rt, "Skip intro", GenesisSequence.RequestSkip, 30f);
        var skrt = skipButton.GetComponent<RectTransform>();
        skrt.anchorMin = skrt.anchorMax = new Vector2(1f, 0f);
        skrt.pivot = new Vector2(1f, 0f);
        skrt.sizeDelta = new Vector2(112f, 30f);
        skrt.anchoredPosition = new Vector2(-24f, 24f);

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
        skyLayer.SetAsFirstSibling();   // behind the bar and every caption

        // The sky is the only part of the UI that moves every single frame, and this project has ONE
        // canvas for the whole game (UIFactory.CreateCanvas is the only AddComponent<Canvas>). Without
        // this, a couple of hundred drifting stars dirty that shared canvas each frame and every open
        // window rebatches with them. A nested Canvas confines the rebuild to the starfield.
        // No GraphicRaycaster: nothing in here is clickable (every star sets raycastTarget = false), and
        // no overrideSorting, so the layer keeps its place in hierarchy order — behind everything.
        skyLayer.gameObject.AddComponent<Canvas>();

        // Built here, ONCE, rather than fetched-or-added later.
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

        // The stall detector starts fresh with the screen. Without this the FIRST report of a load
        // measures from whenever the last one ended — which, if the player sat on the start menu for two
        // hours, is a two-hour "stall" logged against an empty caption. Seen exactly that.
        lastReportAt = -1f; worstFrame = 0f; lastStage = "(start)";

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

        // Orbit rings: an ASSERTION, not a fix. GenesisSequence holds them at zero and gives them back,
        // and GenerateGalaxyRoutine's finally gives them back on every abnormal exit too — but a session
        // whose previous load died between those two would otherwise open this one on a galaxy that can
        // never draw an orbit line again. The generation routine sets this straight back to 0 a moment
        // later, so asserting it costs nothing.
        OrbitController.SetRevealAlpha(1f);

        // Everything the previous load's ending switched off or made transparent, put back. SwitchToLiveView
        // and ShowGenesisTitles both strip this screen down to nothing on their way out, and none of it
        // comes back on its own — so without this the SECOND load of a session runs behind a panel that
        // hides nothing, with no bar and no headline.
        var panelImg = root.GetComponent<Image>();
        if (panelImg != null)
        {
            panelImg.color = new Color(0.02f, 0.03f, 0.06f, 1f);
            // Restoring this matters as much as the colour: without it the live scene underneath is
            // clickable while the bar is still filling.
            panelImg.raycastTarget = true;
        }
        { var sg = SkyGroup; if (sg != null) sg.alpha = 1f; }
        if (barTrack != null) { barTrack.gameObject.SetActive(true); barTrack.localScale = Vector3.one; }
        if (headline != null) headline.gameObject.SetActive(true);
        if (stageLabel != null) stageLabel.gameObject.SetActive(true);
        if (percentLabel != null) percentLabel.gameObject.SetActive(true);
        if (welcomeLabel != null) welcomeLabel.gameObject.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(true);

        SetStage("");
        root.SetActive(true);
        // In front of every window that may already be open behind it.
        root.GetComponent<RectTransform>().SetAsLastSibling();
        Apply(0f);
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);
    }

    // ============================================================================================
    // HAND THE SCREEN OVER TO THE LIVE GALAXY
    //
    // Up to this point there has been nothing real to point a camera at — the home system's visuals do not
    // exist until Visualize has run. From here they do, so the panel gets out of the way and the game's own
    // camera shows through.
    //
    // The bar STAYS. That is the whole idea the request is built on: the loading screen stops being a
    // picture with a bar over it and becomes the game's own camera with a bar over it. Generation is
    // still running underneath — the remaining systems, the economy, the faction seeding — and the bar
    // goes on reporting real progress against real work while the player watches their world form.
    //
    // The panel's BACKGROUND is what gets cleared, not the panel itself: the bar, the captions and the
    // Skip button live inside it, so switching the panel off would take them with it.
    // ============================================================================================
    public void SwitchToLiveView()
    {
        if (root == null) return;

        // The screen's own procedural sky goes — the real backdrop is behind the real camera now, and two
        // starfields at different parallaxes on top of each other reads as a smear.
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
    // nothing else this screen does. This is that: hide the loading furniture, show the message over the
    // live galaxy, and get out of the way.
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
        { var sg = SkyGroup; if (sg != null) sg.alpha = 0f; }

        var panel = root.GetComponent<Image>();
        if (panel != null)
        {
            panel.color = new Color(0f, 0f, 0f, 0f);
            // Raycasts stop being blocked at the same moment the panel stops being visible, or the player
            // would be left clicking through a pane of glass they cannot see.
            panel.raycastTarget = false;
        }

        EnsureWelcome();
        if (welcomeLabel == null) return;

        welcomeLabel.text = string.IsNullOrWhiteSpace(homeName)
            ? "Your universe awaits..."
            : $"Welcome to your homeworld, <color=#FFD24D>{homeName}</color>";
        welcomeLabel.gameObject.SetActive(true);
        StartCoroutine(FadeTitles());
    }

    const float WelcomeFadeIn = 0.5f * GenesisSequence.Pace;  // "Welcome to <world>" arriving
    const float WelcomeHold = 1.1f * GenesisSequence.Pace;    // the message over the live solar system
    const float WelcomeFadeOut = 0.6f * GenesisSequence.Pace; // and then completely gone

    /// How long the welcome titles occupy the screen, start to finish.
    ///
    /// Public and DERIVED because GenesisSequence has to wait exactly this long — its TitleHold beat is
    /// defined as this value. The two used to be independent numbers that happened to be close, and
    /// slowing the sequence broke the coincidence: TitleHold went to 4.0s while the titles still finished
    /// in 2.2s, leaving 1.8 seconds of a motionless shot with no text, no bar and no camera move between
    /// the message vanishing and the pull-back starting. Tied together, that cannot recur at any Pace.
    public const float WelcomeTotal = WelcomeFadeIn + WelcomeHold + WelcomeFadeOut;

    RectTransform welcomeRT;
    TMP_Text welcomeLabel;

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
        // Below the planet, which the sequence has just walked to the centre of the screen.
        welcomeRT.anchoredPosition = new Vector2(0, -150f);
        welcomeLabel.gameObject.SetActive(false);
    }

    // ============================================================================================
    // THE STALL DETECTOR — it measures the longest FRAME now, not the gap between reports
    //
    // It used to time from one Report to the next, and every one of the warnings that produced was
    // false. Generation is sliced: a system takes about a second, but that second is a hundred ordinary
    // frames with the bar moving and the dots animating throughout — which is exactly what "sliced"
    // means and exactly what the diagnostic exists to confirm. Worse, the cinematic half of the load
    // stops reporting once the bar is full and then legitimately WAITS out its remaining beats, so the
    // longest "stall" in the log was reliably the part of the load that is supposed to take time.
    //
    // What actually matters is whether any SINGLE frame blocked, because a blocked frame is the only
    // thing the player can see: the dots freeze, the bar stops, and the screen looks hung. So Update
    // tracks the worst frame since the last report, and Report warns on THAT — naming the caption that
    // was on screen when it happened, and how long the step took in total for context.
    //
    // The threshold is a third of a second rather than a half. At 0.5 a stall had to be bad enough to
    // read as a hang before it was reported; a third of a second is where the animation visibly stops.
    const float SlowFrameSeconds = 0.33f;

    float lastReportAt = -1f;
    float worstFrame;

    /// Called from Update while the screen is open. Cheap, and the only way to see a frame that blocked:
    /// by the time Report is called the long frame is already over.
    void TrackFrame(float dt)
    {
        if (dt > worstFrame) worstFrame = dt;
    }

    public void Report(float t, string stage)
    {
        float now = Time.realtimeSinceStartup;
        if (lastReportAt > 0f && worstFrame > SlowFrameSeconds)
            Debug.Log($"[Loading] a single frame took {worstFrame:F2}s during '{lastStage}' " +
                      $"({now - lastReportAt:F2}s for the whole step, now at {t * 100f:F0}%). " +
                      $"That frame is unsliced work — split it.");
        lastReportAt = now;
        worstFrame = 0f;

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

    /// The caption currently showing, kept so the slow-frame log can name what was on screen.
    string lastStage = "(start)";

    void SetStage(string s)
    {
        lastStage = s;
        if (stageLabel != null) stageLabel.text = s;
    }

    void Update()
    {
        if (!IsOpen) return;

        float dt = Time.unscaledDeltaTime;
        TrackFrame(dt);   // the stall detector — see Report

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
        if (headline != null && headline.gameObject.activeSelf)
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
