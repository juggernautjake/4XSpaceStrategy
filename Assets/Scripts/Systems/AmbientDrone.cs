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

    const int Voices = 6;

    // C#1 — nearly an octave below the original A1. EVERY voice lives between here and roughly 130Hz,
    // which is two octaves below middle C: the whole chord is sub-bass and low bass, with nothing in
    // the register where a pad would normally sit.
    const float RootHzBase = 34.65f;
    const int RootMin = -5, RootMax = 5;  // semitone window the root wanders inside

    // The HIGHEST any voice may ever sound. A hard ceiling, enforced in Hz rather than trusted to fall
    // out of the octave maths, because that's exactly what went wrong before: the root was lowered to
    // 41Hz but a per-voice octave table simultaneously pushed four of the six voices up one or two
    // octaves, so voices ran to 330Hz and the bed came out HIGHER than the 55-110Hz it replaced. Lower
    // the floor and raise the ceiling and you have not made anything deeper. A ceiling can't drift.
    const float MaxVoiceHz = 132f;

    // ---- Register plan ----
    // Which octave a tone sits in is decided by the TONE, not by the voice's index — that was the bug.
    //
    // Only the root and a perfect fifth may sound at the very bottom: those two beat against each other
    // slowly and cleanly (a 3:2 ratio), so they read as one deep note rather than as mud. Colour tones —
    // thirds, sevenths, ninths — go up ONE octave, because a minor third stacked directly on a 35Hz root
    // is ~41Hz, a 6Hz difference the ear cannot resolve as harmony; it just hears a wobble.
    //
    // One octave, never two, and never above MaxVoiceHz. That keeps the entire chord inside roughly
    // 35-130Hz while still being a real chord.
    static int OctaveFor(int semi) => (semi == 0 || semi == 7) ? 0 : 1;

    // ---- Audio-thread state ----
    // Touched from OnAudioFilterRead. Only plain floats: no Unity API is legal on the audio thread.
    readonly float[] curFreq = new float[Voices];
    readonly float[] tgtFreq = new float[Voices];
    readonly float[] amp = new float[Voices];
    readonly double[] phase = new double[Voices];
    readonly float[] tremRate = new float[Voices];
    readonly float[] tremDepth = new float[Voices];
    readonly double[] tremPhase = new double[Voices];
    double subPhase;

    double sampleRate = 44100.0;
    volatile float masterGain = 0f;    // faded in on start so it never begins with a click

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

        rootSemi = 0;
        current = Pick(null);
        ApplyChord(instant: true);
        changeTimer = Random.Range(10f, 18f);
    }

    void Update()
    {
        // Ease the whole bed in over the first couple of seconds.
        float target = SimpleAudio.Instance != null ? SimpleAudio.Instance.HumVolume : 1f;
        masterGain = Mathf.MoveTowards(masterGain, BaseGain * target, Time.unscaledDeltaTime * 0.09f);

        // Unscaled: the music must not speed up when the player speeds up the simulation, and must not
        // stop when they pause.
        changeTimer -= Time.unscaledDeltaTime;
        if (changeTimer > 0f) return;

        // 10-30 seconds, as asked. The glide takes ~8s of that, so the bed is genuinely moving about a
        // third of the time and settled the rest — which is what makes the movement feel deliberate.
        changeTimer = Random.Range(10f, 30f);
        Shift();
    }

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

    /// The tone allowed to join the root at the very bottom of the chord: a PERFECT fifth, or nothing.
    ///
    /// Strict on purpose. A tritone (min7b5) or an augmented fifth voiced down at 60Hz is genuinely
    /// unpleasant — those intervals are meant to be heard as colour up top, not as the foundation. When
    /// the chord has no perfect fifth, the bottom just doubles the root and the chord's character comes
    /// entirely from the upper voices.
    static int LowCompanion(int[] tones)
    {
        foreach (var t in tones) if (t == 7) return 7;
        return tones[0];
    }

    // Write the new chord into the voices' TARGETS. The audio thread glides toward them; nothing is
    // re-triggered, so there's no edge to hear.
    void ApplyChord(bool instant)
    {
        float root = RootHzBase * Mathf.Pow(2f, rootSemi / 12f);
        var tones = current.tones;

        int fifth = LowCompanion(tones);

        for (int v = 0; v < Voices; v++)
        {
            // Bottom two voices: root and its fifth (or the root doubled, when the chord hasn't got a
            // clean fifth to lend). Everything above: the chord's own tones, cycled, placed by
            // OctaveFor — colour tones up exactly one octave, root and fifth left at the bottom.
            int semi = v == 0 ? tones[0]
                     : v == 1 ? fifth
                     : tones[(v - 2) % tones.Length];

            float f = root * Mathf.Pow(2f, (semi + OctaveFor(semi) * 12) / 12f);

            // Fold anything that still lands too high back down an octave at a time. A voice an octave
            // down is the same note, so this can never break the harmony — it only enforces the ceiling.
            while (f > MaxVoiceHz) f *= 0.5f;

            // Detune every other voice very slightly. Two near-identical sines beat against each other
            // at their difference frequency — a slow, organic shimmer no LFO reproduces.
            if (v % 2 == 1) f *= 1.0018f;

            tgtFreq[v] = f;
            if (instant) curFreq[v] = f;

            // Weighted hard toward the bottom: the two fundamentals carry the sound and the colour
            // tones only tint it. This is the other half of "deeper on average" — the low voices are
            // not merely lower than before, they're a bigger share of what you hear.
            amp[v] = Mathf.Lerp(1f, 0.16f, Mathf.Pow(v / (float)(Voices - 1), 0.75f));
            tremRate[v] = 0.05f + v * 0.031f;     // mutually prime-ish: never lines up, never pulses
            tremDepth[v] = 0.10f + (v % 3) * 0.03f;
        }

        // Publish for the chirps.
        var pub = new float[tones.Length];
        for (int i = 0; i < tones.Length; i++) pub[i] = root * Mathf.Pow(2f, tones[i] / 12f);
        CurrentTones = pub;
        CurrentSemitones = tones;
        CurrentRootSemi = rootSemi;
        CurrentChordName = current.name;
    }

    // ---- The synth ----
    void OnAudioFilterRead(float[] data, int channels)
    {
        double sr = sampleRate;
        float gain = masterGain;
        if (gain <= 0.0001f) return;

        // ~8 seconds to reach a new pitch. Per-sample exponential glide: slow enough to read as a bend
        // rather than a slide, and it lands softly instead of arriving.
        float glide = (float)(1.0 - System.Math.Exp(-1.0 / (sr * 8.0)));

        int frames = data.Length / channels;
        for (int i = 0; i < frames; i++)
        {
            float sample = 0f;
            float norm = 0f;

            for (int v = 0; v < Voices; v++)
            {
                curFreq[v] += (tgtFreq[v] - curFreq[v]) * glide;

                phase[v] += curFreq[v] / sr;
                if (phase[v] > 1.0) phase[v] -= 1.0;

                // Slow independent tremolo per voice, so the bed breathes. Advanced from its OWN phase
                // accumulator rather than Time.unscaledTime: this runs on the audio thread, where the
                // Unity API is off limits — reading Time here is a real (if quiet) violation.
                tremPhase[v] += tremRate[v] / sr;
                if (tremPhase[v] > 1.0) tremPhase[v] -= 1.0;

                float lfo = 1f - tremDepth[v] * 0.5f * (1f + Mathf.Sin((float)(tremPhase[v] * 6.2831853)));
                float s = Mathf.Sin((float)(phase[v] * 6.2831853));
                sample += s * amp[v] * lfo;
                norm += amp[v];
            }

            // A sub an octave below the root: felt more than heard.
            //
            // Quieter than the voices now, and deliberately so. With the root down at ~35Hz this sits
            // near 17Hz, which almost nothing actually reproduces — so every unit of level given to it
            // is level taken out of the normalisation below and returned as silence. It earns its keep
            // as weight on hardware that can move that much air, and costs little on hardware that
            // can't.
            subPhase += (curFreq[0] * 0.5f) / sr;
            if (subPhase > 1.0) subPhase -= 1.0;
            sample += Mathf.Sin((float)(subPhase * 6.2831853)) * 0.35f;
            norm += 0.35f;

            sample = sample / Mathf.Max(0.001f, norm) * gain;
            // Gentle soft-clip: keeps a chord whose partials line up from ever spiking.
            sample = Mathf.Clamp(sample - sample * sample * sample / 3f, -0.5f, 0.5f);

            for (int c = 0; c < channels; c++) data[i * channels + c] += sample;
        }
    }
}
