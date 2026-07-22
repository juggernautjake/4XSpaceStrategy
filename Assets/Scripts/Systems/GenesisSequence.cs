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
    public const float StarHold = 2.2f;      // the sun alone, before anything moves
    public const float DriftToWorld = 3.4f;  // the camera sliding from star to homeworld
    public const float WorldForms = 9f;      // the signature beat: terrain developing
    public const float MoonBeat = 0.6f;      // gap before each moon starts
    public const float MoonForms = 2.6f;     // a moon's own development — quicker; the planet is the subject
    public const float SettleHold = 1.4f;    // the finished world simply turning
    public const float TravelToCentre = 1.1f;
    public const float TitleHold = 1.8f;
    public const float PullBack = 1.6f;

    // A moon is a fraction of its planet's size, so at the planet's framing it is a few dozen pixels.
    // The camera eases in slightly for the moon beat so their surfaces are legible — the brief's own
    // §5.4 conflict, solved by moving the camera rather than by lying about how big moons are.
    public const float MoonFraction = 0.16f;

    // Morph grids. The planet is the showpiece and gets the finer one; a moon is smaller on screen, so
    // a coarser grid keeps its TILES the same apparent size rather than degrading into noise.
    public const int PlanetMorphW = 96, PlanetMorphH = 48;
    public const int MoonMorphW = 40, MoonMorphH = 20;

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

        // --- The star, alone ---
        yield return Wait(StarHold);

        // --- Drift to the homeworld -------------------------------------------------------------
        //
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
