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

    System.Collections.IEnumerator GenerateGalaxyRoutine(int systemCount, int avgPlanets, System.Action onDone)
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
        // A new galaxy replaces whatever Dev Mode was holding a baseline for — see DevCheats. After
        // PlayerEconomy.NewGame, so the starting resources are what leaving Dev Mode restores.
        DevCheats.OnGameReplaced();
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
            string welcome = homePlanet != null ? homePlanet.name : "your homeworld";
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

        generating = false;
        onDone?.Invoke();
    }

    /// Put the camera on the real homeworld at PLANETARY zoom, instantly, so the player begins where
    /// the loading screen left them — and can immediately zoom out through the system to the galaxy.
    static void SnapCameraToHome(CelestialBody home, float screenFraction)
    {
        if (home?.visualObject == null) return;
        CameraController.Instance?.SnapFocus(home.visualObject.transform, true, screenFraction);
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

    public void SetFocus(StarSystemData sys) { if (sys != null) FocusedSystem = sys; }

    void Visualize()
    {
        if (systemVisualizer == null) { Debug.LogWarning("SystemVisualizer not assigned!"); return; }
        systemVisualizer.solarSystemGenerator = solarSystemGenerator;
        systemVisualizer.VisualizeGalaxy(Galaxy);
    }
}
