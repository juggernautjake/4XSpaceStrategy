using UnityEngine;

/// The three ways of making the hum. Persisted as an int, so DON'T renumber these.
public enum DroneStyle
{
    Strata = 0,     // additive voices gliding between chords
    Monolith = 1,   // a fixed harmonic pillar the chord breathes through
    Tidal = 2       // detuned pairs whose interference is the movement
}

// ============================================================================================
// SHARED TUNING
//
// Every engine obeys these, so all three are the same DEPTH and the same harmony — they differ only in
// how they turn a chord into sound. Without a shared floor and ceiling, "three variants" would just be
// three different pitches, and switching would read as a volume/brightness change rather than a change
// of character.
// ============================================================================================
public static class DroneTuning
{
    /// C#1. Nearly an octave below the A1 this started at.
    public const float RootHzBase = 34.65f;

    /// The highest any voice, in any engine, may ever sound. Enforced in Hz rather than trusted to fall
    /// out of octave arithmetic — that's exactly what went wrong once already, when a lowered root and a
    /// per-voice octave table combined to push voices to 330Hz and the "deeper" bed came out higher.
    public const float MaxVoiceHz = 132f;

    /// Only the root and a perfect fifth may sound at the very bottom; every colour tone goes up one
    /// octave. A minor third stacked on a 35Hz root is a 6Hz difference — the ear hears a wobble, not
    /// harmony. Thirds, sevenths and ninths are legible an octave up, and only there.
    public static int OctaveFor(int semi)
    {
        int pc = ((semi % 12) + 12) % 12;
        return (pc == 0 || pc == 7) ? 0 : 1;
    }

    /// Chord tone -> Hz, placed in its proper register and folded under the ceiling. Folding by octaves
    /// can never break the harmony: a voice an octave down is the same note.
    public static float ToneHz(float rootHz, int semi)
    {
        float f = rootHz * Mathf.Pow(2f, (semi + OctaveFor(semi) * 12) / 12f);
        while (f > MaxVoiceHz) f *= 0.5f;
        while (f < 18f) f *= 2f;          // below hearing AND below reproduction: wasted level
        return f;
    }

    /// The tone allowed to join the root at the bottom: a PERFECT fifth, or nothing. A tritone or an
    /// augmented fifth voiced down at 60Hz is genuinely unpleasant — those are colour, not foundation.
    public static int LowCompanion(int[] tones)
    {
        foreach (var t in tones) if (((t % 12) + 12) % 12 == 7) return 7;
        return 0;
    }

    /// The n'th COLOUR tone of a chord — cycling through its tones while skipping the root.
    ///
    /// Skipping index 0 matters more than it looks. Every chord in the vocabulary starts with 0, and
    /// LowCompanion also returns 0 when there's no perfect fifth (augmented chords, by definition, never
    /// have one). So a naive `tones[n % length]` put the root on the bottom voice, the companion voice
    /// AND the first colour voice — on an augmented chord that was three of Tidal's four pairs playing
    /// the same note, and the chord all but disappeared exactly where it should be most unsettling.
    public static int ColourTone(int[] tones, int n)
    {
        if (tones.Length <= 1) return 0;
        return tones[1 + (((n % (tones.Length - 1)) + (tones.Length - 1)) % (tones.Length - 1))];
    }

    public static string Name(DroneStyle s)
    {
        switch (s)
        {
            case DroneStyle.Monolith: return "Monolith";
            case DroneStyle.Tidal:    return "Tidal";
            default:                  return "Strata";
        }
    }

    public static string Blurb(DroneStyle s)
    {
        switch (s)
        {
            case DroneStyle.Monolith:
                return "A single unmoving harmonic pillar. The chord swells through it rather than " +
                       "playing it — vast, still, cathedral-like.";
            case DroneStyle.Tidal:
                return "Every note is two notes, a fraction apart. Their interference is the movement: " +
                       "restless, shimmering, never quite settling.";
            default:
                return "Six voices that bend slowly from chord to chord. Smooth and drifting — the " +
                       "harmony is always going somewhere.";
        }
    }
}

// ============================================================================================
// The engine contract.
//
// Retune() runs on the MAIN thread and writes targets. Next() runs on the AUDIO thread and reads them.
// That split is the whole design: no Unity API and no allocation is legal inside Next(), so anything
// that needs Random, Time, or a `new` has to happen in Retune().
//
// The two threads race on the target fields by design. It's benign — a float written while the audio
// thread reads it yields either the old value or the new one, and both are valid pitches. Locking would
// risk the audio thread blocking on the main thread, which is a dropout: a far worse outcome than a
// voice arriving one buffer late.
// ============================================================================================
public abstract class DroneEngine
{
    protected double sr = 44100.0;
    public virtual void Init(double sampleRate) { sr = sampleRate; }

    /// New chord. `instant` skips the glide (used for the very first one).
    public abstract void Retune(float rootHz, int[] tones, bool instant);

    /// One mono sample, roughly -1..1. Called once per frame of audio.
    public abstract float Next();

    /// Seconds between chord changes for this style. Part of its character: Tidal is restless and
    /// changes often, Monolith is geological and barely moves.
    public abstract Vector2 ChangeInterval { get; }
}

// ============================================================================================
// STRATA — six additive sine voices that GLIDE between chords.
//
// The original, and the smoothest of the three. Nothing is ever re-triggered: the same six oscillators
// walk to new pitches over ~8 seconds, so a chord change is a bend rather than a cut. Each voice has
// its own slow tremolo at a rate that shares no common factor with the others, so the bed breathes
// without ever pulsing in step.
// ============================================================================================
public class StrataEngine : DroneEngine
{
    const int Voices = 6;

    readonly float[] curFreq = new float[Voices];
    readonly float[] tgtFreq = new float[Voices];
    readonly float[] amp = new float[Voices];
    readonly double[] phase = new double[Voices];
    readonly float[] tremRate = new float[Voices];
    readonly float[] tremDepth = new float[Voices];
    readonly double[] tremPhase = new double[Voices];
    double subPhase;
    float glide;

    public override Vector2 ChangeInterval => new Vector2(10f, 30f);

    public override void Init(double sampleRate)
    {
        base.Init(sampleRate);
        // ~8s to reach a new pitch. Per-sample exponential: slow enough to read as a bend rather than a
        // slide, and it lands softly instead of arriving.
        glide = (float)(1.0 - System.Math.Exp(-1.0 / (sampleRate * 8.0)));
    }

    public override void Retune(float rootHz, int[] tones, bool instant)
    {
        int fifth = DroneTuning.LowCompanion(tones);

        for (int v = 0; v < Voices; v++)
        {
            // Bottom two carry the fundamentals; everything above is the chord's COLOUR, cycled.
            int semi = v == 0 ? 0
                     : v == 1 ? fifth
                     : DroneTuning.ColourTone(tones, v - 2);

            float f = DroneTuning.ToneHz(rootHz, semi);

            // Detune every other voice a hair. Two near-identical sines beat at their difference
            // frequency — a slow organic shimmer no LFO reproduces.
            if (v % 2 == 1) f *= 1.0018f;

            tgtFreq[v] = f;
            if (instant) curFreq[v] = f;

            // Weighted hard toward the bottom: the fundamentals carry it, the colour only tints.
            amp[v] = Mathf.Lerp(1f, 0.16f, Mathf.Pow(v / (float)(Voices - 1), 0.75f));
            tremRate[v] = 0.05f + v * 0.031f;
            tremDepth[v] = 0.10f + (v % 3) * 0.03f;
        }
    }

    public override float Next()
    {
        float sample = 0f, norm = 0f;

        for (int v = 0; v < Voices; v++)
        {
            curFreq[v] += (tgtFreq[v] - curFreq[v]) * glide;

            phase[v] += curFreq[v] / sr;
            if (phase[v] > 1.0) phase[v] -= 1.0;

            // Advanced from its OWN sample counter, not Time.unscaledTime: this is the audio thread,
            // where the Unity API is off limits.
            tremPhase[v] += tremRate[v] / sr;
            if (tremPhase[v] > 1.0) tremPhase[v] -= 1.0;

            float lfo = 1f - tremDepth[v] * 0.5f * (1f + Mathf.Sin((float)(tremPhase[v] * 6.2831853)));
            sample += Mathf.Sin((float)(phase[v] * 6.2831853)) * amp[v] * lfo;
            norm += amp[v];
        }

        // A sub an octave under the root: felt more than heard. Quiet, because at ~17Hz most hardware
        // reproduces none of it, and every unit of level here is a unit taken out of the normalisation
        // below and returned as silence.
        subPhase += (curFreq[0] * 0.5f) / sr;
        if (subPhase > 1.0) subPhase -= 1.0;
        sample += Mathf.Sin((float)(subPhase * 6.2831853)) * 0.35f;
        norm += 0.35f;

        return sample / Mathf.Max(0.001f, norm);
    }
}

// ============================================================================================
// MONOLITH — a fixed harmonic pillar the chord breathes THROUGH.
//
// The method is inverted relative to Strata. There, pitch moves and level is fixed; here, the pitches
// barely move at all and LEVEL is the music.
//
// Three partials of the root — 1x, 2x, 3x, the bottom of the harmonic series — sound continuously and
// never change with the chord. Being a real harmonic series, they fuse into ONE enormous note rather
// than reading as an interval; that's the pillar. The chord's colour tones sit above it and swell in
// and out on very slow, mutually-unrelated envelopes, so a chord is something that emerges and recedes
// instead of something that is struck.
//
// Consequently it barely changes chord (40-75s) and glides for 14s when the root moves. It should feel
// geological.
// ============================================================================================
public class MonolithEngine : DroneEngine
{
    const int Pillars = 3;    // harmonic partials 1x, 2x, 3x
    const int Colours = 3;    // chord tones, swelling

    readonly float[] pillarCur = new float[Pillars];
    readonly float[] pillarTgt = new float[Pillars];
    readonly double[] pillarPhase = new double[Pillars];
    static readonly float[] PillarAmp = { 1f, 0.42f, 0.20f };

    readonly float[] colCur = new float[Colours];
    readonly float[] colTgt = new float[Colours];
    readonly double[] colPhase = new double[Colours];
    readonly double[] swellPhase = new double[Colours];
    readonly float[] swellRate = new float[Colours];

    float glide;

    public override Vector2 ChangeInterval => new Vector2(40f, 75f);

    public override void Init(double sampleRate)
    {
        base.Init(sampleRate);
        // ~14s: twice Strata's, so the root's movement is something you notice afterwards rather than
        // while it happens.
        glide = (float)(1.0 - System.Math.Exp(-1.0 / (sampleRate * 14.0)));

        // Swell periods of roughly 23, 31 and 41 seconds. Chosen as primes so the three envelopes never
        // line up — the moment they did, the pillar would pulse, and a pulse is a rhythm.
        swellRate[0] = 1f / 23f;
        swellRate[1] = 1f / 31f;
        swellRate[2] = 1f / 41f;
    }

    public override void Retune(float rootHz, int[] tones, bool instant)
    {
        // The pillar is the harmonic series of the root, NOT the chord. It is the same shape in every
        // chord, which is the point: the chord is weather, the pillar is the mountain.
        for (int i = 0; i < Pillars; i++)
        {
            float f = rootHz * (i + 1);
            while (f > DroneTuning.MaxVoiceHz) f *= 0.5f;
            pillarTgt[i] = f;
            if (instant) pillarCur[i] = f;
        }

        // Colour tones: the chord's own, skipping root and fifth (the pillar already says those).
        int found = 0;
        foreach (int t in tones)
        {
            if (found >= Colours) break;
            int pc = ((t % 12) + 12) % 12;
            if (pc == 0 || pc == 7) continue;
            float f = DroneTuning.ToneHz(rootHz, t);
            colTgt[found] = f;
            if (instant) colCur[found] = f;
            found++;
        }
        // A chord with nothing but root and fifth (sus-like, after filtering) leaves colour voices
        // unfilled — park them on the pillar's octave rather than at 0Hz, which would be a DC offset.
        for (; found < Colours; found++)
        {
            float f = DroneTuning.ToneHz(rootHz, 12);
            colTgt[found] = f;
            if (instant) colCur[found] = f;
        }
    }

    public override float Next()
    {
        float sample = 0f, norm = 0f;

        for (int i = 0; i < Pillars; i++)
        {
            pillarCur[i] += (pillarTgt[i] - pillarCur[i]) * glide;
            pillarPhase[i] += pillarCur[i] / sr;
            if (pillarPhase[i] > 1.0) pillarPhase[i] -= 1.0;
            sample += Mathf.Sin((float)(pillarPhase[i] * 6.2831853)) * PillarAmp[i];
            norm += PillarAmp[i];
        }

        for (int i = 0; i < Colours; i++)
        {
            colCur[i] += (colTgt[i] - colCur[i]) * glide;
            colPhase[i] += colCur[i] / sr;
            if (colPhase[i] > 1.0) colPhase[i] -= 1.0;

            swellPhase[i] += swellRate[i] / sr;
            if (swellPhase[i] > 1.0) swellPhase[i] -= 1.0;

            // 0..1 swell. Squared, so it spends most of its time near silent and only occasionally
            // blooms — a colour tone that was always half-present would just be a quieter chord.
            float s = 0.5f * (1f - Mathf.Cos((float)(swellPhase[i] * 6.2831853)));
            float env = s * s * 0.30f;

            sample += Mathf.Sin((float)(colPhase[i] * 6.2831853)) * env;
            norm += 0.30f * 0.5f;   // average, not peak: normalising to peak would leave it too quiet
        }

        return sample / Mathf.Max(0.001f, norm);
    }
}

// ============================================================================================
// TIDAL — every note is two notes, and their interference is the music.
//
// No LFO anywhere. Each chord tone is a PAIR of oscillators a fraction of a Hz apart, and two sines at
// f and f+b sum to a single tone at their mean amplitude-modulated at exactly b Hz. So the pulsing is
// not applied to the sound, it IS the sound — physically the same thing a real detuned unison does.
//
// The beat rates are irrational multiples of each other, so the four pairs never breathe together and
// the surface never settles into a pattern. That restlessness is the character, and it's why this one
// changes chord fastest (6-14s) and glides quickest (3.5s).
//
// A little second harmonic gives it a reedier edge than Strata's pure sines.
// ============================================================================================
public class TidalEngine : DroneEngine
{
    const int Pairs = 4;

    readonly float[] curFreq = new float[Pairs];
    readonly float[] tgtFreq = new float[Pairs];
    readonly double[] phaseA = new double[Pairs];
    readonly double[] phaseB = new double[Pairs];
    readonly float[] amp = new float[Pairs];

    // Beat rates in Hz. Deliberately not multiples of one another: 0.13, 0.29, 0.47 and 0.73 share no
    // common factor, so the four pairs realign only every few minutes.
    static readonly float[] Beat = { 0.13f, 0.29f, 0.47f, 0.73f };

    float glide;

    public override Vector2 ChangeInterval => new Vector2(6f, 14f);

    public override void Init(double sampleRate)
    {
        base.Init(sampleRate);
        glide = (float)(1.0 - System.Math.Exp(-1.0 / (sampleRate * 3.5)));
    }

    public override void Retune(float rootHz, int[] tones, bool instant)
    {
        int fifth = DroneTuning.LowCompanion(tones);

        for (int p = 0; p < Pairs; p++)
        {
            int semi = p == 0 ? 0
                     : p == 1 ? fifth
                     : DroneTuning.ColourTone(tones, p - 2);

            float f = DroneTuning.ToneHz(rootHz, semi);
            tgtFreq[p] = f;
            if (instant) curFreq[p] = f;

            amp[p] = Mathf.Lerp(1f, 0.28f, p / (float)(Pairs - 1));
        }
    }

    public override float Next()
    {
        float sample = 0f, norm = 0f;

        for (int p = 0; p < Pairs; p++)
        {
            curFreq[p] += (tgtFreq[p] - curFreq[p]) * glide;

            float fa = curFreq[p];
            float fb = curFreq[p] + Beat[p];      // the pair, b Hz apart -> beats at exactly b Hz

            phaseA[p] += fa / sr; if (phaseA[p] > 1.0) phaseA[p] -= 1.0;
            phaseB[p] += fb / sr; if (phaseB[p] > 1.0) phaseB[p] -= 1.0;

            float a = Mathf.Sin((float)(phaseA[p] * 6.2831853));
            float b = Mathf.Sin((float)(phaseB[p] * 6.2831853));

            // A touch of the second harmonic on one of the pair only — on both, it would beat too and
            // the result reads as buzz rather than reed.
            float h2 = Mathf.Sin((float)(phaseA[p] * 2.0 * 6.2831853)) * 0.14f;

            sample += (a + b + h2) * 0.5f * amp[p];
            norm += amp[p];
        }

        return sample / Mathf.Max(0.001f, norm);
    }
}
