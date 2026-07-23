using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THE GENESIS SEQUENCE — the intro, filmed live
//
// The beats, in order, all on the REAL camera looking at the REAL galaxy:
//
//   1  The home star ignites, framed left of the loading bar.
//   2  A binary or trinary's companions emerge and settle into their orbits. (Already true: the real
//      cluster is built by SystemVisualizer from the same StarCluster layout the game uses.)
//   3  The camera slides off the star and onto the homeworld, which is where the star's companions
//      leave the frame — the zoom is close enough to hold just the world and the bar.
//   4  The homeworld forms: a featureless orb growing continents, mountains and ice.
//   5  Its moons arrive and form the same way, on their own real orbits.
//   6  The bar completes and collapses as the camera walks the world to centre, growing a little.
//   7  Titles.
//   8  The camera eases back; the orbit lines draw in; the rest of the galaxy arrives.
//   9  Control.
//
// WHY THIS EXISTS AS ITS OWN CLASS. The beats are a story, and stories belong in one readable place.
// GameManager's job is to generate a galaxy; LoadingScreen's job is to draw a bar and some text. Neither
// wants to also know that the camera eases off a star at 47% and that moons arrive one beat apart.
//
// EVERY DURATION IS UNSCALED. Generation runs with Time.timeScale at 0, so a sequence on scaled time
// would simply never advance.
// ============================================================================================
public class GenesisSequence : MonoBehaviour
{
    public static GenesisSequence Instance;

    // ---- Pacing. Tuned by eye; every one of these is meant to be adjusted while watching it. --------
    //
    // THE ONE KNOB. Every duration below is a base value times this, so the whole sequence can be sped
    // up or slowed down without changing the RELATIVE weight of the beats — which is the part that was
    // actually tuned. Raise it and every move, hold and morph stretches together; the crescendo still
    // lands in the same place, just later.
    //
    // At 1.0 the whole thing ran about 21 seconds for a moonless world and 31 with three moons, and the
    // camera moves in particular read as hurried: the drift from the star to the homeworld is the shot
    // that establishes the entire scene and it was over in 3.4s. Everything below is slower in its own
    // right AND multiplied by this.
    public const float Pace = 1.25f;

    public const float StarHold = 3.2f * Pace;      // the sun alone, before anything moves
    public const float DriftToWorld = 5.5f * Pace;  // the camera sliding from star to homeworld
    public const float WorldForms = 14f * Pace;     // the signature beat: terrain developing
    public const float MoonBeat = 1.0f * Pace;      // gap before each moon starts
    public const float MoonForms = 4.0f * Pace;     // a moon's own development — quicker; the planet is the subject
    public const float SettleHold = 2.4f * Pace;    // the finished world simply turning
    public const float TravelToCentre = 2.2f * Pace;
    // NOT an independent number. This beat exists to wait out the welcome titles, so it IS their
    // duration — see LoadingScreen.WelcomeTotal. Set by hand it drifted the moment the pacing changed,
    // and the sequence sat on a dead, motionless shot for the difference.
    public const float TitleHold = LoadingScreen.WelcomeTotal;
    public const float PullBack = 3.2f * Pace;

    // A moon is a fraction of its planet's size, so at the planet's framing it is a few dozen pixels.
    // The camera eases in slightly for the moon beat so their surfaces are legible — the brief's own
    // §5.4 conflict, solved by moving the camera rather than by lying about how big moons are.
    public const float MoonFraction = 0.16f;

    // Morph grids. The planet is the showpiece and gets the finer one; a moon is smaller on screen, so
    // a coarser grid keeps its TILES the same apparent size rather than degrading into noise.
    public const int PlanetMorphW = 96, PlanetMorphH = 48;
    public const int MoonMorphW = 40, MoonMorphH = 20;

    /// How long Play will take for this world, so the loading bar can walk across it honestly.
    ///
    /// Derived from the same constants Play uses rather than guessed, and it takes the BODY because the
    /// moon beats depend on how many it has — a world with three moons runs ten seconds longer than one
    /// with none, and a bar sized for the average would visibly stall or race on both.
    public static float TotalSeconds(CelestialBody home)
    {
        int moons = home?.moons != null ? home.moons.Count : 0;

        // Measured to the moment the BAR COMPLETES, not to the end of the sequence — the bar is done
        // when the world is built, and the beats after that (travel to centre, titles, pull-back) are
        // the coda. Counting them in would leave the bar at ~92% when it is forced to 100%, an
        // eight-point snap at exactly the beat that is supposed to read as completion.
        //
        // The camera push before the moons is deliberately NOT counted: EaseTo does not block, so Play
        // never waits for it.
        return StarHold
             + DriftToWorld
             + WorldForms + 0.2f
             + moons * (MoonBeat + MoonForms)
             + SettleHold;
    }

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("GenesisSequence");
        Instance = go.AddComponent<GenesisSequence>();
    }

    void Awake() { Instance = this; }

    /// True while the intro is playing. The player has no control and nothing should act on input.
    public static bool Running { get; private set; }

    /// Frame the home star. Called as soon as the home system's visuals exist, which is long before the
    /// homeworld's surface is generated — the star is what the first half of the load has to look at.
    public void FrameHomeStar(StarSystemData home)
    {
        var cam = GenesisCamera.Instance;
        if (cam == null || home == null) return;

        var t = StarTransform(home);
        if (t == null) return;

        Running = true;
        cam.Begin();
        // A star reads "a bit bigger" than the homeworld will — the compressed ladder, not the real
        // 100:1, which would leave the planet a speck.
        cam.Frame(t, GenesisCamera.HomeworldScreenFraction * GenesisCamera.StarSizeRelativeToHome,
                  GenesisCamera.SubjectAnchorX);
    }

    /// The rest of the sequence, once the homeworld exists.
    public IEnumerator Play(CelestialBody home, System.Action onBarComplete)
    {
        var cam = GenesisCamera.Instance;
        if (cam == null || home?.visualObject == null) { Running = false; yield break; }

        Running = true;

        // ============================================================================================
        // START THE CLOCK. THE WORLD HAS TO BE MOVING.
        //
        // Generation runs with Time.timeScale at 0 (StartMenu pauses on open), and OrbitController
        // advances on SCALED time. So without this every planet and moon sits perfectly still for the
        // whole sequence — and three of the things this intro is for would silently not happen:
        //
        //   * the moons would not be orbiting, so the arrangement the player watches would not be the
        //     arrangement they are handed a moment later;
        //   * the homeworld would not travel along its own orbit, so there would be no TERMINATOR
        //     sweeping across it — the one cue that a planet the camera is tracking is moving at all;
        //   * the star cluster of a binary home would hang motionless instead of turning about its
        //     barycentre.
        //
        // The player still has no control: the camera belongs to GenesisCamera, CameraController is
        // standing down, and the click handlers refuse while Running. What starts here is the WORLD, not
        // the player's turn. Set(1f) rather than Resume(), because TimeControl.last is a process-wide
        // static — Resume would restore whatever speed the LAST session ended on, and a new galaxy
        // opening at 5x would run twenty seconds of economy under the cinematic.
        // ============================================================================================
        TimeControl.Set(1f);

        // --- The star, alone ---
        yield return Wait(StarHold);

        // --- Drift to the homeworld -------------------------------------------------------------
        //
        // PRIME FIRST, REVEAL SECOND. The homeworld and its moons are player-owned, so their visuals
        // were built wearing their REAL finished surfaces. Revealing them and starting the morph later
        // would show the player a completed world that then visibly reverts to a featureless orb — and
        // each moon popping back to primordial one at a time as its beat arrived. Priming installs the
        // stage-0 texture without starting the clock, so what appears is already the beginning of itself.
        TerrainMorph.Prime(home, PlanetMorphW, PlanetMorphH, 20260720);
        if (home.moons != null)
            for (int i = 0; i < home.moons.Count; i++)
                TerrainMorph.Prime(home.moons[i], MoonMorphW, MoonMorphH, 20260721 + i);

        // The world is revealed BEFORE the camera arrives, not after: the move takes seconds, and a
        // planet that popped into being at the end of it would undo the whole point of travelling there.
        GenesisReveal.RevealHomeworld(home);

        cam.EaseTo(home.visualObject.transform, GenesisCamera.HomeworldScreenFraction,
                   GenesisCamera.SubjectAnchorX, DriftToWorld);
        yield return Wait(DriftToWorld);

        // --- The world forms --------------------------------------------------------------------
        var morph = TerrainMorph.Begin(home, WorldForms, PlanetMorphW, PlanetMorphH, 20260720);
        yield return Wait(WorldForms + 0.2f);
        if (morph != null && !morph.Done) morph.Finish();   // never leave a half-formed world on screen

        // --- Moons ------------------------------------------------------------------------------
        //
        // The real moons, on their real orbits. They are already there and already moving; what happens
        // here is that each in turn develops its surface, exactly as the planet did.
        var moons = home.moons;
        if (moons != null && moons.Count > 0)
        {
            // Ease in a little so a moon's tiles are legible. The planet stays in frame — this is a
            // push, not a move.
            cam.EaseTo(home.visualObject.transform, GenesisCamera.HomeworldScreenFraction * 1.35f,
                       GenesisCamera.SubjectAnchorX, MoonBeat);

            for (int i = 0; i < moons.Count; i++)
            {
                yield return Wait(MoonBeat);
                var mm = TerrainMorph.Begin(moons[i], MoonForms, MoonMorphW, MoonMorphH, 20260721 + i);
                yield return Wait(MoonForms);
                if (mm != null && !mm.Done) mm.Finish();
            }
        }

        // --- Settle ------------------------------------------------------------------------------
        yield return Wait(SettleHold);

        // --- The bar closes as the world walks to centre ------------------------------------------
        //
        // Together, deliberately: the space the bar occupied is handed straight to the planet rather
        // than sitting empty in between.
        onBarComplete?.Invoke();
        cam.EaseTo(home.visualObject.transform,
                   GenesisCamera.HomeworldScreenFraction * GenesisCamera.CentreGrowth,
                   0.5f, TravelToCentre);
        yield return Wait(TravelToCentre);

        // --- Titles -------------------------------------------------------------------------------
        LoadingScreen.Instance?.ShowGenesisTitles(home.name);
        yield return Wait(TitleHold);

        // --- Pull back; the orbits draw in; the galaxy arrives ------------------------------------
        cam.EaseTo(home.visualObject.transform, GenesisCamera.HomeworldScreenFraction,
                   0.5f, PullBack);

        // The orbit lines arriving IS the signal that the world is live — held at zero since Visualize
        // built them precisely so they have somewhere to arrive from.
        for (float e = 0f; e < PullBack; e += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(e / PullBack);
            OrbitController.SetRevealAlpha(1f - Mathf.Pow(1f - k, 2f));
            yield return null;
        }
        OrbitController.SetRevealAlpha(1f);

        // ...and then everything that has been hidden since generation finished.
        GenesisReveal.Finish();

        // --- Control ------------------------------------------------------------------------------
        Running = false;
        cam.Release(home.visualObject.transform);
        TimeControl.Set(1f);
    }

    /// Abandon the sequence wherever it is and hand the player a finished, visible galaxy.
    ///
    /// Every step is idempotent, because this can fire at any point — including in the middle of a beat
    /// that has already done half its work.
    public void Abort(CelestialBody home)
    {
        StopAllCoroutines();
        Running = false;

        if (home != null)
        {
            GenesisReveal.RevealHomeworld(home);
            FinishMorph(home);
            if (home.moons != null) foreach (var m in home.moons) FinishMorph(m);
        }

        GenesisReveal.Finish();
        OrbitController.SetRevealAlpha(1f);
        GenesisCamera.Instance?.Release(home?.visualObject != null ? home.visualObject.transform : null);
        TimeControl.Set(1f);
    }

    static void FinishMorph(CelestialBody b)
    {
        if (b?.visualObject == null) return;
        var m = b.visualObject.GetComponent<TerrainMorph>();
        if (m != null) m.Finish();
    }

    static Transform StarTransform(StarSystemData sys)
    {
        if (sys == null) return null;
        // The combined star for a black hole; the first sun otherwise. StarInteraction owns the only
        // mapping from StarData to the transform drawing it.
        var s = sys.isBlackHole || sys.stars == null || sys.stars.Count == 0 ? sys.combinedStar : sys.stars[0];
        return StarInteraction.TransformOf(s) ?? sys.pivot;
    }

    static IEnumerator Wait(float seconds)
    {
        for (float e = 0f; e < seconds; e += Time.unscaledDeltaTime) yield return null;
    }
}
