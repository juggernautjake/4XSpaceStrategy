using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Generation")]
    public SolarSystemGenerator solarSystemGenerator;

    [Header("Visualization")]
    public SystemVisualizer systemVisualizer;

    [Header("Scene Setup")]
    public Transform systemParent;

    public bool isEditMode = false;

    public Galaxy Galaxy { get; private set; }
    public StarSystemData FocusedSystem { get; private set; }

    static readonly List<CelestialBody> _empty = new List<CelestialBody>();

    // Back-compat views onto the currently-focused system.
    public List<CelestialBody> CurrentBodies => FocusedSystem != null ? FocusedSystem.bodies : _empty;
    public StarData CurrentStar => FocusedSystem != null ? FocusedSystem.combinedStar : null;
    public List<StarData> Stars => FocusedSystem != null ? FocusedSystem.stars : new List<StarData>();
    public bool IsBlackHole => FocusedSystem != null && FocusedSystem.isBlackHole;

    void Awake() { Instance = this; }

    void Start()
    {
        // Launch into the main menu instead of auto-generating a galaxy.
        if (StartMenu.Instance != null) StartMenu.Instance.Open();
        else GenerateStartingSystem();
    }

    // Default new game — a small galaxy (fallback / used by the R debug key).
    public void GenerateStartingSystem() => GenerateGalaxy(5, 4);

    /// Generate a galaxy with a loading screen, a system at a time.
    ///
    /// Same work as GenerateGalaxy, spread across frames so the bar can actually repaint. Every value it
    /// reports is a step that really completed — the generator is phased (Begin / AddSystem / Finish) for
    /// exactly this reason, rather than the screen guessing against a timer.
    ///
    /// Systems are ~70% of the budget because they are ~70% of the work: each one rolls a star, lays out
    /// its worlds, and generates a full terrain grid for every one of them.
    bool generating;

    /// A galaxy is being built right now. Read by anything that must not act on a half-built game —
    /// the pause menu, above all, whose Save button would otherwise capture the galaxy mid-cinematic.
    public bool IsGenerating => generating;

    public void GenerateGalaxyAsync(int systemCount, int avgPlanets, System.Action onDone = null)
    {
        // One at a time. SolarSystemGenerator keeps its working state on the component itself —
        // _idCounter, stars, currentStar, currentSystemName, isBlackHole — and Finalise reads several of
        // those AFTER the per-body yields. Before generation was stepped a system was built atomically
        // inside one frame, so two overlapping runs could not interleave; now they can, and the result is
        // corruption rather than a clean failure: bodies typed against another system's star, colliding
        // ids, and systems finalised with the wrong name. Nothing enforced this before — the menu merely
        // made a second click hard to reach, which is not the same thing.
        if (generating) return;
        generating = true;
        StartCoroutine(GenerateGalaxyRoutine(systemCount, avgPlanets, onDone));
    }

    /// The real backstop, and it has to be OUT HERE to be one.
    ///
    /// The genesis sequence conceals the entire galaxy and relies on something giving it back. Putting
    /// that "something" at the end of the generation coroutine looked like a safety net and was not: the
    /// loading finale is driven by `while (fin.MoveNext())` on this same stack, so an exception anywhere
    /// inside it propagates out, Unity stops the coroutine at the throw, and every line after it —
    /// including the reveal — is simply never reached. The result would be a galaxy the player can never
    /// see AND a latched `generating` flag, so New Game silently does nothing forever after.
    ///
    /// `yield return` inside a try/finally is legal in a C# iterator (only a `catch` is not), and Unity
    /// disposes the enumerator when it stops a coroutine — so this runs on the normal path, on an
    /// exception, and on a StopCoroutine alike.
    System.Collections.IEnumerator GenerateGalaxyRoutine(int systemCount, int avgPlanets, System.Action onDone)
    {
        var inner = GenerateGalaxyBody(systemCount, avgPlanets, onDone);
        try
        {
            while (inner.MoveNext()) yield return inner.Current;
        }
        finally
        {
            // Idempotent: Finish() early-returns once it has run, and the finale already called it on its
            // last beat. This is only here for the paths where it did not.
            GenesisReveal.Finish();
            generating = false;
        }
    }

    System.Collections.IEnumerator GenerateGalaxyBody(int systemCount, int avgPlanets, System.Action onDone)
    {
        if (solarSystemGenerator == null)
        {
            Debug.LogError("Assign SolarSystemGenerator in Inspector!");
            // Clear the guard on the way out, or one bad-config attempt latches it and every later
            // Start Game silently does nothing.
            generating = false;
            onDone?.Invoke();
            yield break;
        }

        var screen = LoadingScreen.Instance;
        screen?.Open("Generating the universe");
        // One frame before any work, so the screen is actually on-screen before the main thread is busy.
        yield return null;

        ResearchManager.NewGame();
        EmpireTech.Reset();
        TechManager.Reset();
        AncientLore.Reset();
        AncientClues.Reset();

        int count = GalaxyGenerator.ClampSystems(systemCount);
        var galaxy = GalaxyGenerator.Begin(solarSystemGenerator, avgPlanets);
        // Subject.None, not Galaxy: this frame is immediately followed by the first Subject.Star report,
        // so showing the galaxy model here means one frame of it appearing and vanishing — a flash, which
        // is the exact artifact the star/planet split below exists to remove.
        screen?.Report(0.03f, "Seeding " + galaxy.name, LoadingScreen.Subject.None);
        yield return null;

        // The home star CLUSTER itself was rolled in Begin — the whole reason that roll moved out of
        // ForceHomeWorld, which runs last. So the preview can show the real star(s) from the very first
        // frame — including the pop-out if the home turns out to be a binary or trinary — and only the
        // NAME has to wait for system 0 to be built.
        screen?.SetHomeCluster(galaxy.homeStars);

        // The load is split in HALF by subject, deliberately: stars for the first 50%, the homeworld
        // forming for the second. Alternating star/planet per system meant neither ever held the screen
        // long enough to watch — the pop-out and the tile reveal are both several seconds of animation,
        // and both were being interrupted by the next system before they finished.
        const float SystemsShare = 0.47f;

        // A floor on how long the star half lasts, in wall-clock seconds.
        //
        // The bar is split evenly by PROGRESS, but progress and time are not the same thing: a small
        // galaxy can generate every system in a second or two, and the star half would flash past before
        // the pop-out finished — or, on a single-sun game, before the player had really looked at it.
        // Padding to a floor makes the two halves feel even as well as measure even.
        const float MinStarPhase = 6f;
        float starPhaseBegan = Time.unscaledTime;
        for (int i = 0; i < count; i++)
        {
            // Announce the subject BEFORE the work, so the preview shows what is about to be built rather
            // than what has just finished — during a long step the caption and the model would otherwise
            // both be describing the previous system.
            screen?.Report(0.03f + SystemsShare * (i / (float)count),
                           $"Forming star system  {i + 1} / {count}", LoadingScreen.Subject.Star);
            yield return null;

            // Stepped: this yields once per WORLD, not once per system, so a system with six planets and
            // their moons renders a dozen frames instead of one. That is what lets the bar move and the
            // dots animate during the part of the load that actually takes the time.
            var step = GalaxyGenerator.AddSystemStepped(galaxy, solarSystemGenerator, i, count);
            while (step.MoveNext()) yield return step.Current;

            // A binary/trinary home needs real time on screen to show its pop-out (LoadingScreen.
            // StepSunCluster) — a companion sun after a beat, a third after another — and system
            // generation alone may finish in well under that on a small galaxy. Held HERE, while the
            // Subject is still Star, rather than after the caption below switches to Subject.Planet and
            // the pop-out is no longer even on screen to hold.
            if (i == 0 && galaxy.homeStars.Count > 1)
            {
                float popDuration = LoadingScreen.PopBeat * (galaxy.homeStars.Count - 1) + LoadingScreen.PopGrow + 0.5f;
                yield return new WaitForSecondsRealtime(popDuration);
            }

            // The home system's star gets NAMED on screen. Passing it as the stage string rather than
            // having SetHomeStar write the caption is deliberate: Report always sets the caption, so a
            // caption written anywhere else is overwritten by the very next report — which is exactly
            // what happened the first time this was wired up, and the name never appeared at all.
            // STILL Subject.Star. The whole first half belongs to the suns — switching to Planet here is
            // what used to cut the pop-out short and leave the placeholder planet on screen for most of
            // the load, before the real homeworld even existed to show.
            string caption = (i == 0 && galaxy.systems.Count > 0)
                ? $"{galaxy.systems[0].name} — your home star"
                : $"Forming star system  {i + 1} / {count}";

            screen?.Report(0.03f + SystemsShare * ((i + 1) / (float)count),
                           caption, LoadingScreen.Subject.Star);
            // Give the screen a couple of frames to actually animate in.
            //
            // One `yield return null` per system means one rendered frame per system, and a bar cannot
            // look smooth when it is only drawn eight times across the whole load — the fade on the dots
            // and the easing on the fill both need frames to happen in. A handful of idle frames costs a
            // few milliseconds against work measured in hundreds.
            yield return null;
            yield return null;

            // Hold on the home star long enough to actually read its name.
            //
            // Without this it survives about two frames: the next iteration reports "Forming star
            // system 2" before its own yield, so on a twelve-system galaxy the one caption the player is
            // meant to remember is on screen for ~30ms. A beat here is the difference between naming
            // their home star and technically having displayed it.
            if (i == 0 && count > 1) yield return new WaitForSecondsRealtime(1.1f);
        }

        // Let the star half run its full time before handing over, so the suns are actually watched
        // rather than glimpsed. Costs nothing on a large galaxy, where generation already outlasts it.
        while (Time.unscaledTime - starPhaseBegan < MinStarPhase)
        {
            screen?.Report(0.03f + SystemsShare, "The system settles", LoadingScreen.Subject.Star);
            yield return null;
        }

        // ---- Second half: the homeworld ----
        screen?.Report(0.50f, "Settling the home world", LoadingScreen.Subject.Star);
        yield return null;
        GalaxyGenerator.Finish(galaxy, SpeciesManager.Current, count);

        Galaxy = galaxy;
        FocusedSystem = Galaxy.Home;

        // The remaining engine work FIRST, so the reveal that follows is uninterrupted.
        //
        // Visualize and the economy setup are quick but not free, and a frame that stalls partway through
        // the tile reveal is exactly the kind of hitch the reveal exists to avoid. Getting them out of the
        // way means the whole second half of the bar is nothing but the planet forming.
        // Orbit rings OFF before anything is drawn. They are built eagerly inside Visualize, so without
        // this they would already be on screen at full brightness the instant the loading panel
        // dissolves — and the finale saves them for its last beat, where their arrival is what tells the
        // player they have control. Restored by the finale itself.
        OrbitController.SetRevealAlpha(0f);

        Visualize();

        var homePlanet = FindHomePlanet();
        PlayerEconomy.NewGame(homePlanet, SpeciesManager.Current);
        UnitManager.Instance?.NewGame(homePlanet);
        FactionAI.NewGame(Galaxy);

        // AND NOW HIDE ALMOST ALL OF IT.
        //
        // The galaxy is fully generated and fully running from this line onward — orbits turning,
        // economy ticking, factions thinking — but only the home system's sun(s) are drawn. The
        // homeworld joins them when the camera takes over, and the rest of the galaxy arrives at the
        // very end, after the orbit lines.
        //
        // AFTER FactionAI.NewGame, and that ordering is load-bearing rather than tidy. FactionAI won't
        // plant a capital on a concealed world (a rival empire nobody can see or attack), so conceal the
        // galaxy first and it finds NO candidate anywhere — the game generates with no rival
        // civilisations at all. Everything that chooses worlds has to have chosen them before the lights
        // go out.
        GenesisReveal.Begin();

        // A new galaxy replaces whatever Dev Mode was holding a baseline for — see DevCheats. After
        // PlayerEconomy.NewGame, so the starting resources are what leaving Dev Mode restores.
        DevCheats.OnGameReplaced();
        // ...and whatever was in the object bin belonged to the PREVIOUS galaxy. Restoring one of those
        // would splice a system out of a dead galaxy into this one.
        GalaxyTrash.OnGameReplaced();
        yield return null;

        // The homeworld's surface only exists once Finish has run, which is why the star held the screen
        // until now. Hand the real world over — barren rock morphing tile by tile toward its finished
        // terrain — and walk the bar across the second half at the reveal's own pace, so the progress
        // the player sees IS the world appearing rather than a number racing ahead of it.
        if (homePlanet != null)
        {
            screen?.SetHomePlanet(homePlanet);
            // The real moons, revealed after the planet finishes and then left orbiting it.
            screen?.SetHomeMoons(homePlanet.moons);

            // Built once: the loop below runs for MorphDuration seconds at frame rate, and rebuilding an
            // identical string several hundred times is several hundred allocations during the one stretch
            // of the load where a GC hitch would show as a stutter in the reveal.
            string homeCaption = $"{homePlanet.name} — your homeworld";
            // Reads correctly now that ForceHomeWorld no longer renames the world to the literal
            // "Homeworld" — that was producing "Homeworld — your homeworld" for the whole reveal.

            float reveal = LoadingScreen.MorphDuration;
            for (float e = 0f; e < reveal; e += Time.unscaledDeltaTime)
            {
                screen?.Report(Mathf.Lerp(0.52f, 0.94f, e / reveal), homeCaption, LoadingScreen.Subject.Planet);
                yield return null;
            }

            // Let the finished world simply turn for a moment before anything else happens.
            //
            // The reveal ends on its last tile and the screen used to close almost immediately after —
            // so the one thing the whole sequence was building toward, the completed planet, was the
            // thing the player never actually got to look at.
            screen?.Report(0.97f, homeCaption, LoadingScreen.Subject.Planet);

            // Long enough for the moons to arrive and be seen orbiting, not just to flash into place:
            // each takes its own reveal plus a beat, and then they need a moment simply turning. Sized
            // off the real moon count so a world with none does not sit on a still frame for six seconds.
            int shownMoons = screen != null ? screen.StagedMoonCount : 0;
            float moonHold = shownMoons > 0
                ? LoadingScreen.MoonBeat + shownMoons * (LoadingScreen.MoonMorphDuration + LoadingScreen.MoonBeat) + 1.4f
                : 2.2f;
            yield return new WaitForSecondsRealtime(moonHold);
        }

        // THE HANDOFF. The load does not end by switching the panel off — the bar collapses into itself
        // and blinks out, the homeworld and its moons travel to where it was, and the real game is
        // revealed underneath them already framing that same planet.
        //
        // The camera move happens in the onReady callback, which the finale fires while the panel is
        // still covering the screen. So by the time anything dissolves, what is behind it matches.
        screen?.Report(1f, "Generation complete", LoadingScreen.Subject.Planet);

        if (screen != null)
        {
            // The world's REAL generated name. The fallback exists only for the impossible case of a
            // galaxy with no home planet at all — if it ever shows up on screen, that is the bug.
            string welcome = homePlanet != null && !string.IsNullOrWhiteSpace(homePlanet.name)
                ? homePlanet.name
                : "an unnamed world";
            // The apparent size the real planet has to match, solved from the preview's own framing.
            float frac = screen.HandoffScreenFraction();
            var fin = screen.Finale(welcome, () => SnapCameraToHome(homePlanet, frac));
            while (fin.MoveNext()) yield return fin.Current;
        }
        else
        {
            // No screen, so no finale to restore the rings — put them back by hand rather than leaving
            // the galaxy permanently without orbit lines.
            SnapCameraToHome(homePlanet, 0f);
            OrbitController.SetRevealAlpha(1f);
        }

        // (The reveal backstop and the `generating` reset both live in GenerateGalaxyRoutine's finally —
        // see the note there for why they cannot live here.)
        onDone?.Invoke();
    }

    /// Put the camera on the real homeworld at PLANETARY zoom, instantly, so the player begins where
    /// the loading screen left them — and can immediately zoom out through the system to the galaxy.
    static void SnapCameraToHome(CelestialBody home, float screenFraction)
    {
        if (home?.visualObject == null) return;

        // The homeworld and its moons come out of hiding HERE — the moment the camera is framed on
        // them and while the panel still covers everything. The brief's requirement is that the world
        // is unhidden by the time the camera brings it into view, and this is that instant: any later
        // and the dissolve would reveal an empty frame, any earlier is invisible anyway.
        GenesisReveal.RevealHomeworld(home);

        CameraController.Instance?.SnapFocus(home.visualObject.transform, true, screenFraction);

        // START THE CLOCK HERE, not when the loading screen finally closes.
        //
        // Generation runs with timeScale at 0, and orbits advance on scaled time — so with the clock
        // still stopped the planet would arrive at the centre of the screen frozen in place, sitting
        // dead in its system while the welcome message played over it. Resuming at the moment the camera
        // takes over means that by the time the panel dissolves the system is genuinely running: the
        // homeworld is orbiting its star, its moons are orbiting it, and zooming out shows a live system
        // rather than a still. The player still cannot CLICK anything until the panel goes — it blocks
        // raycasts to the end — so this starts the world moving without handing over control early.
        //
        // SnapFocus was given follow:true for the same reason: the planet is now moving, and a camera
        // that merely pointed at where it used to be would drift off it within a second.
        // Set(1f), NOT Resume(). TimeControl.last is a process-wide static that survives a new game, so
        // Resume would restore whatever speed the player left the LAST session on — start a new galaxy
        // after playing at 5x and the remaining few seconds of finale would run twenty simulation
        // seconds of economy and faction AI before the player could touch anything. A new game starts
        // at normal speed.
        TimeControl.Set(1f);
    }

    public void GenerateGalaxy(int systemCount, int avgPlanets)
    {
        if (solarSystemGenerator == null)
        {
            Debug.LogError("Assign SolarSystemGenerator in Inspector!");
            return;
        }

        ResearchManager.NewGame();
        EmpireTech.Reset();
        TechManager.Reset();
        AncientLore.Reset();
        AncientClues.Reset();   // a fresh galaxy re-scatters the ten Vael fragments (SeedGalaxy, in Generate)
        Galaxy = GalaxyGenerator.Generate(solarSystemGenerator, systemCount, avgPlanets, SpeciesManager.Current);
        FocusedSystem = Galaxy.Home;

        // (Silenced to keep the console clean — this was a one-time generation confirmation.)
        // Debug.Log($"Generated galaxy: {Galaxy.systems.Count} systems; home = {(Galaxy.Home != null ? Galaxy.Home.name : "?")}.");
        Visualize();

        // Player economy + starting fleet (home planet is rendered by Visualize above).
        var homePlanet = FindHomePlanet();
        PlayerEconomy.NewGame(homePlanet, SpeciesManager.Current);
        UnitManager.Instance?.NewGame(homePlanet);

        // Seed the rival civilisations: give each non-player faction a race + personality and a homeworld,
        // from which it grows and expands on its own. After Visualize() so the seeded worlds' visuals exist.
        FactionAI.NewGame(Galaxy);
    }

    CelestialBody FindHomePlanet()
    {
        var home = Galaxy != null ? Galaxy.Home : null;
        if (home == null) return null;
        foreach (var b in home.bodies) if (b.owner == FactionManager.Player) return b;
        return home.bodies.Count > 0 ? home.bodies[0] : null;
    }

    public void LoadGalaxy(Galaxy g)
    {
        Galaxy = g;
        FocusedSystem = g.Home;
        // A loaded game has no finale to turn them back on, and the reveal alpha is a static that
        // survives whatever the last new-game left it at.
        OrbitController.SetRevealAlpha(1f);
        Visualize();
    }

    /// Focus a system — but only one this galaxy actually contains.
    ///
    /// StarOverview.Open focuses whatever system it was handed, and a window can outlive the system it
    /// is showing (delete it from the object panel while its overview is open). Focusing a system that
    /// is no longer in the galaxy leaves the habitable-zone band being built around a pivot that was
    /// destroyed, and the next rebuild framing a system that is not there.
    public void SetFocus(StarSystemData sys)
    {
        if (sys == null) return;
        if (Galaxy != null && !Galaxy.systems.Contains(sys)) return;
        FocusedSystem = sys;
    }

    /// Re-draw the galaxy from the data as it stands now.
    ///
    /// For structural changes the visualizer cannot patch in place — a system deleted out of the galaxy,
    /// or restored back into it (see GalaxyTrash). It is the same full rebuild a save load does, which is
    /// why it is safe: every consumer already has to survive VisualizeGalaxy replacing every GameObject
    /// underneath it, because loading a save does exactly that.
    ///
    /// The focus is re-pointed FIRST. VisualizeGalaxy builds the habitable-zone band around
    /// FocusedSystem, and a system that has just been deleted is still sitting in that field — reading it
    /// would build the band around a system that is no longer in the galaxy.
    public void RebuildVisuals()
    {
        if (Galaxy == null) return;
        if (FocusedSystem == null || !Galaxy.systems.Contains(FocusedSystem)) FocusedSystem = Galaxy.Home;

        // Whatever the camera was locked onto is about to be destroyed and replaced. Remembered as a
        // BODY rather than a Transform, because the body survives the rebuild and its GameObject does
        // not — a follow target left pointing at the old one goes quietly fake-null and the camera stops
        // tracking the planet the player deliberately chose to watch.
        var cam = CameraController.Instance;
        CelestialBody following = null;
        if (cam != null && cam.IsFollowing && cam.followTarget != null)
            foreach (var b in SystemContext.AllBodies())
                if (b.visualObject != null && b.visualObject.transform == cam.followTarget) { following = b; break; }

        Visualize();

        // Three things are parented under SystemParent but built by somebody OTHER than the visualizer,
        // and VisualizeGalaxy destroys every child of it. Each of these only rebuilds when the Galaxy
        // OBJECT changes — which an in-place rebuild is not — so without these calls the galaxy-zoom
        // proxies go stale, every derelict hull in the game disappears for good, and comets stop
        // spawning for the rest of the session.
        GalaxyLOD.RebuildNow();
        DerelictRenderer.RebuildNow();
        CometManager.RebuildNow();

        // Re-point the camera and the floating label at the NEW GameObjects.
        //
        // The FIELD, not FocusAndZoom. FocusAndZoom only skips re-framing when it is already following
        // this exact Transform — and the Transform has just been replaced, so it would fall through to
        // its "frame the whole neighbourhood" branch and throw away whatever zoom the player had set.
        // Deleting an unrelated planet in another system would visibly pull the camera off the world
        // they were watching. Writing the target directly keeps the height exactly where it was;
        // LateUpdate re-centres from there.
        if (following != null && following.visualObject != null)
        {
            cam.followTarget = following.visualObject.transform;
            cam.SetFollow(true);
        }
        else if (cam != null && cam.IsFollowing)
        {
            // The followed world is the one that just left. Release properly rather than leaving
            // `following` true with a destroyed target — which reads as "Follow: on" in the HUD while
            // nothing is being followed, and quietly changes the zoom floor.
            cam.ClearFocus();
        }

        // (Concealment is re-applied by VisualizeGalaxy itself, at the end.)
        var sel = PlanetUI.Selected;
        if (sel != null && sel.visualObject != null) ObjectLabelManager.Instance?.ShowForBody(sel);
        else ObjectLabelManager.Instance?.Hide();
    }

    void Visualize()
    {
        if (systemVisualizer == null) { Debug.LogWarning("SystemVisualizer not assigned!"); return; }
        systemVisualizer.solarSystemGenerator = solarSystemGenerator;
        systemVisualizer.VisualizeGalaxy(Galaxy);
    }
}
