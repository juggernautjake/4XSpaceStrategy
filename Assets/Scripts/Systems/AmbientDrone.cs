using UnityEngine;

// ============================================================================================
// THE AMBIENT DRONE — a slow, dark harmonic bed, synthesised live.
//
// The old hum was a 4-second loop of fixed partials: one frozen chord, forever. This replaces it with
// a real additive synth on the audio thread whose voices GLIDE between chords, so a change is a slow
// bend rather than a cut. Nothing is ever re-triggered; the same six voices simply walk to new pitches
// over ~8 seconds. That's what makes the shifts feel gentle rather than sudden.
//
// The harmony is deliberately unresolved: minor, minor-major 7ths, augmented and suspended chords, so
// the bed always feels like it's leaning somewhere. Roughly one chord in six is major — arriving at
// one after a run of augmented chords reads as a small, unearned moment of relief, which is exactly
// the effect asked for. It never sits there: the next change pulls it back into the dark.
//
// Voice leading keeps it subtle. The next chord is chosen to share tones with the current one and to
// move its root by a small interval, so most voices barely move and only one or two actually travel.
// A random walk through chords would lurch; this drifts.
//
// AmbientChirps reads CurrentTones so the chirps land on the chord that's actually playing.
// ============================================================================================
public class AmbientDrone : MonoBehaviour
{
    public static AmbientDrone Instance;

    // ---- Chord vocabulary, as semitones above the root ----
    // Weighted toward the unresolved. `major` marks the ones that resolve.
    class ChordType
    {
        public string name;
        public int[] tones;
        public float weight;
        public bool major;
    }

    static readonly ChordType[] Vocabulary =
    {
        // The dark core.
        new ChordType { name = "min",      tones = new[] { 0, 3, 7, 12, 15, 19 }, weight = 1.4f },
        new ChordType { name = "min7",     tones = new[] { 0, 3, 7, 10, 15, 22 }, weight = 1.5f },
        new ChordType { name = "min9",     tones = new[] { 0, 3, 10, 14, 19, 26 }, weight = 1.1f },
        // Minor-major 7th: the most unsettled chord here — a minor triad with a leading tone shoving
        // against it. Used sparingly because it's strong.
        new ChordType { name = "minMaj7",  tones = new[] { 0, 3, 7, 11, 14, 19 }, weight = 0.6f },
        // Augmented: no perfect fifth at all, so the ear can't find the floor. The "wrong" feeling.
        new ChordType { name = "aug",      tones = new[] { 0, 4, 8, 12, 16, 20 }, weight = 0.9f },
        new ChordType { name = "augMaj7",  tones = new[] { 0, 4, 8, 11, 16, 23 }, weight = 0.5f },
        // Suspended: hollow and open, neither major nor minor — good breathing room between the others.
        new ChordType { name = "sus2",     tones = new[] { 0, 2, 7, 12, 14, 19 }, weight = 1.0f },
        new ChordType { name = "sus4",     tones = new[] { 0, 5, 7, 12, 17, 19 }, weight = 0.9f },
        // Half-diminished: tense, but softer than a full diminished.
        new ChordType { name = "min7b5",   tones = new[] { 0, 3, 6, 10, 15, 18 }, weight = 0.5f },
        // The rare resolutions. Low weights on purpose: relief only lands if it's uncommon.
        new ChordType { name = "maj",      tones = new[] { 0, 4, 7, 12, 16, 19 }, weight = 0.45f, major = true },
        new ChordType { name = "maj9",     tones = new[] { 0, 4, 7, 14, 16, 23 }, weight = 0.35f, major = true },
        new ChordType { name = "maj7",     tones = new[] { 0, 4, 11, 14, 19, 23 }, weight = 0.3f, major = true },
    };

    // Root movement, in semitones. Small steps are common; a fifth is the biggest jump allowed. Weighted
    // so the key drifts rather than jumps.
    static readonly (int step, float weight)[] RootMoves =
    {
        (-7, 0.5f), (-5, 0.9f), (-3, 1.0f), (-2, 1.2f), (-1, 0.6f),
        (0, 0.3f),
        (1, 0.6f), (2, 1.2f), (3, 1.0f), (4, 0.7f), (5, 0.9f), (7, 0.5f),
    };

    const int RootMin = -5, RootMax = 5;  // semitone window the root wanders inside

    // ---- The engine ----
    // This class owns the HARMONY — the chord vocabulary, the root's wander, the voice leading — and an
    // engine owns the SOUND. All three engines get the same chord at the same moment and the same depth
    // limits (DroneTuning), so switching changes the character of the bed without changing what it's
    // playing or how low it sits.
    DroneEngine engine;
    DroneStyle style = DroneStyle.Strata;
    public static DroneStyle Style => Instance != null ? Instance.style : DroneStyle.Strata;

    double sampleRate = 44100.0;
    volatile float masterGain = 0f;    // faded in on start so it never begins with a click

    // ---- Switching styles without a click ----
    // Swapping the engine mid-buffer would cut from one waveform straight to another at whatever
    // amplitude each happened to be at — a step discontinuity, which is a click, and a loud one on a
    // signal this low. So the bed fades out first, swaps at silence, and fades back in.
    //
    // This also makes the swap thread-safe for free: at the moment `engine` is reassigned, switchFade is
    // 0 and the audio thread is multiplying everything by it, so it cannot matter which reference it
    // reads.
    volatile float switchFade = 1f;
    DroneStyle pendingStyle;
    bool switching;
    const float SwitchFadeRate = 3.5f;   // ~0.3s each way

    // ---- Main-thread state ----
    int rootSemi;
    ChordType current;
    float changeTimer;

    /// The chord currently sounding, in Hz. Read by AmbientChirps so its notes belong to this chord.
    public static float[] CurrentTones = { 34.65f, 41.2f, 51.9f };  // C#1 minor, until the first chord lands
    /// Semitone offsets of the current chord (root-relative), and the root's own offset from A.
    public static int[] CurrentSemitones = { 0, 3, 7 };
    public static int CurrentRootSemi;
    public static string CurrentChordName = "min";

    // Much quieter than the old hum: this is a bed you notice when it stops, not a sound you listen to.
    // The old drone ran at 0.75 base * 0.5 source = ~0.37. This is roughly a third of that.
    const float BaseGain = 0.13f;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("AmbientDrone");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<AmbientDrone>();
    }

    void Awake()
    {
        Instance = this;
        sampleRate = AudioSettings.outputSampleRate;

        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = true;
        src.loop = true;
        src.spatialBlend = 0f;
        src.volume = 1f;
        // A clip of silence: OnAudioFilterRead only runs on a source that is actually playing.
        src.clip = AudioClip.Create("droneCarrier", 1024, 1, (int)sampleRate, false);
        src.Play();

        style = (DroneStyle)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, (int)DroneStyle.Strata),
                                        0, 2);
        engine = Make(style);
        engine.Init(sampleRate);

        rootSemi = 0;
        current = Pick(null);
        ApplyChord(instant: true);
        // From the engine, not a hardcoded 10-18s — otherwise a session that starts on Monolith (which
        // is meant to be geological, 40-75s) would move the harmony within ten seconds and give exactly
        // the wrong first impression of it.
        var iv0 = engine.ChangeInterval;
        changeTimer = Random.Range(iv0.x, iv0.y);
    }

    const string PrefKey = "audio.droneStyle";

    static DroneEngine Make(DroneStyle s)
    {
        switch (s)
        {
            case DroneStyle.Monolith: return new MonolithEngine();
            case DroneStyle.Tidal:    return new TidalEngine();
            default:                  return new StrataEngine();
        }
    }

    /// Pick a hum. Takes effect over ~0.6s (out, swap, in) rather than instantly — see switchFade.
    public static void SetStyle(DroneStyle s)
    {
        if (Instance == null) return;
        if (Instance.style == s && !Instance.switching) return;
        Instance.pendingStyle = s;
        Instance.switching = true;
        PlayerPrefs.SetInt(PrefKey, (int)s);
    }

    void Update()
    {
        // Ease the whole bed in over the first couple of seconds.
        float target = SimpleAudio.Instance != null ? SimpleAudio.Instance.HumVolume : 1f;
        masterGain = Mathf.MoveTowards(masterGain, BaseGain * target, Time.unscaledDeltaTime * 0.09f);

        if (TickSwitch()) return;   // mid-swap: don't also change chord under it

        // Unscaled: the music must not speed up when the player speeds up the simulation, and must not
        // stop when they pause.
        changeTimer -= Time.unscaledDeltaTime;
        if (changeTimer > 0f) return;

        // How often the harmony moves is part of a style's character, so the engine sets it: Tidal is
        // restless at 6-14s, Monolith is geological at 40-75s.
        var iv = engine.ChangeInterval;
        changeTimer = Random.Range(iv.x, iv.y);
        Shift();
    }

    /// Runs the fade-out / swap / fade-in. Returns true while a swap is in progress.
    bool TickSwitch()
    {
        if (!switching)
        {
            // Recover from a fade-out that was interrupted (style set twice in quick succession).
            if (switchFade < 1f)
                switchFade = Mathf.MoveTowards(switchFade, 1f, Time.unscaledDeltaTime * SwitchFadeRate);
            return false;
        }

        switchFade = Mathf.MoveTowards(switchFade, 0f, Time.unscaledDeltaTime * SwitchFadeRate);
        if (switchFade > 0.0001f) return true;

        // At silence. Safe to swap.
        style = pendingStyle;
        var next = Make(style);
        next.Init(sampleRate);
        // Hand it the chord that's already playing, instantly — it has to come back up on the harmony we
        // faded out on, not glide in from wherever its arrays happened to start (which is 0 Hz, i.e. DC).
        next.Retune(RootHz(), current.tones, instant: true);
        engine = next;

        switching = false;
        changeTimer = Random.Range(engine.ChangeInterval.x, engine.ChangeInterval.y);
        return true;
    }

    float RootHz() => DroneTuning.RootHzBase * Mathf.Pow(2f, rootSemi / 12f);

    void Shift()
    {
        int move = WeightedRootMove();
        rootSemi = Mathf.Clamp(rootSemi + move, RootMin, RootMax);
        current = Pick(current);
        ApplyChord(instant: false);
    }

    // Choose the next chord: weighted by the vocabulary, but biased toward chords that SHARE TONES with
    // the one playing. Shared tones mean voices that don't have to move, which is the whole of why this
    // drifts instead of lurching.
    ChordType Pick(ChordType from)
    {
        float total = 0f;
        var scores = new float[Vocabulary.Length];
        for (int i = 0; i < Vocabulary.Length; i++)
        {
            var c = Vocabulary[i];
            float s = c.weight;
            if (from != null)
            {
                if (c == from) s *= 0.15f;                 // don't sit on the same chord
                s *= 1f + SharedTones(from, c) * 0.35f;    // prefer common ground
            }
            scores[i] = s; total += s;
        }

        float r = Random.value * total;
        for (int i = 0; i < Vocabulary.Length; i++)
        {
            r -= scores[i];
            if (r <= 0f) return Vocabulary[i];
        }
        return Vocabulary[0];
    }

    static int SharedTones(ChordType a, ChordType b)
    {
        int n = 0;
        foreach (int x in a.tones)
            foreach (int y in b.tones)
                if (((x % 12) + 12) % 12 == ((y % 12) + 12) % 12) { n++; break; }
        return n;
    }

    static int WeightedRootMove()
    {
        float total = 0f;
        foreach (var m in RootMoves) total += m.weight;
        float r = Random.value * total;
        foreach (var m in RootMoves) { r -= m.weight; if (r <= 0f) return m.step; }
        return 0;
    }

    // Hand the new chord to the engine, and publish it for the chirps.
    //
    // The register plan (which tone sits in which octave, and the hard Hz ceiling) lives in DroneTuning
    // and is applied by the engines, not here — otherwise each of the three would need its own copy of
    // it, and the copies would drift. That drift is exactly how the bed once ended up HIGHER after being
    // "lowered".
    void ApplyChord(bool instant)
    {
        float root = RootHz();
        var tones = current.tones;

        engine.Retune(root, tones, instant);

        // Publish for the chirps. These are the raw chord tones, un-octaved — ChordPitch only wants the
        // pitch classes, and it places its own octaves (chirps want to be high; the bed doesn't).
        var pub = new float[tones.Length];
        for (int i = 0; i < tones.Length; i++) pub[i] = root * Mathf.Pow(2f, tones[i] / 12f);
        CurrentTones = pub;
        CurrentSemitones = tones;
        CurrentRootSemi = rootSemi;
        CurrentChordName = current.name;
    }

    // ---- The synth ----
    // The host does the mixing; the engine does the sound. Everything legal here is a plain float
    // operation: no Unity API and no allocation is safe on the audio thread.
    void OnAudioFilterRead(float[] data, int channels)
    {
        var eng = engine;                     // read ONCE: the main thread may swap it mid-buffer
        if (eng == null) return;

        float gain = masterGain * switchFade;
        if (gain <= 0.0001f) return;

        int frames = data.Length / channels;
        for (int i = 0; i < frames; i++)
        {
            float sample = eng.Next() * gain;

            // Gentle soft-clip: keeps a chord whose partials line up from ever spiking.
            sample = Mathf.Clamp(sample - sample * sample * sample / 3f, -0.5f, 0.5f);

            for (int c = 0; c < channels; c++) data[i * channels + c] += sample;
        }
    }
}
