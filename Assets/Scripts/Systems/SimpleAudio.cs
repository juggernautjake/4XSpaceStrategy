using System.Collections.Generic;
using UnityEngine;

// Categories of alert, each with its own recognizable sound.
public enum NotifKind { Info, Research, Discovery, Danger, Victory, Defeat }

// Fully procedural audio (no sound assets needed):
//  * a constant, slightly-wavering deep space hum,
//  * occasional random beeps/boops for ambience,
//  * UI clicks/ticks and a research-complete chime.
// Master volume is controlled via AudioListener.volume, so one knob scales everything.
public class SimpleAudio : MonoBehaviour
{
    public static SimpleAudio Instance;

    AudioSource sfx;      // one-shot UI + event sounds (clean)
    AudioSource hum;      // looping ambient drone
    AudioSource ambient;  // occasional distant space chatter (on its own filtered child)
    AudioLowPassFilter ambientLP;
    AudioEchoFilter ambientEcho;

    AudioClip cDing, cClick, cTick, cSelect, cHum, cHover, cSave, cLoad;
    AudioClip cResearch, cDiscovery, cDanger, cVictory, cDefeat, cInfo;

    // ---- The space-noise library ----
    // Each entry carries its own SPACE, not just its own sound. "Some should be more echoy than others"
    // can't be a property of the clip alone — echo is a filter on the source — so every noise declares
    // how much of it it wants, and the send is set per shot just before it plays. Shots are seconds
    // apart and under two seconds long, so they never overlap and never fight over the filter.
    struct SpaceNoise
    {
        public AudioClip clip;
        public float echo;      // 0 = dry and close, 1 = ringing out across a canyon
        public float tuned;     // 0 = untuned (creaks, static), 1 = pitch it to the drone's chord
        public float level;     // per-family loudness trim
    }
    List<SpaceNoise> noises;
    AudioClip[] cUnitSelect; // per-unit-type selection cue
    AudioClip cDestroyed;

    public float MasterVolume { get; private set; } = 0.8f;
    public float EffectsVolume { get; private set; } = 0.9f;   // UI clicks/selection/notifications
    public float HumVolume { get; private set; } = 1.0f;       // the deep, constant space drone (its own knob)
    public float AmbientVolume { get; private set; } = 0.6f;   // occasional chirps / distant space chatter
    public bool Muted { get; private set; } = false;

    const float HumBase = 0.75f;   // hum's own mix level before the hum slider scales it (louder, comforting bed)

    float lastTick;
    float lastHover;
    float ambientTimer;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("SimpleAudio").AddComponent<SimpleAudio>();
    }

    void Awake()
    {
        Instance = this;

        if (FindFirstObjectByType<AudioListener>() == null)
        {
            var cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<AudioListener>();
            else gameObject.AddComponent<AudioListener>();
        }

        sfx = gameObject.AddComponent<AudioSource>(); sfx.playOnAwake = false;
        hum = gameObject.AddComponent<AudioSource>(); hum.playOnAwake = true; hum.loop = true;

        // Ambient lives on its own child so its echo/reverb/low-pass don't touch the clean UI sounds.
        var ambGO = new GameObject("Ambient");
        ambGO.transform.SetParent(transform, false);
        ambient = ambGO.AddComponent<AudioSource>(); ambient.playOnAwake = false;
        ambientLP = ambGO.AddComponent<AudioLowPassFilter>(); ambientLP.cutoffFrequency = 2200f; // muffled/distant
        ambientEcho = ambGO.AddComponent<AudioEchoFilter>(); ambientEcho.delay = 275f; ambientEcho.decayRatio = 0.58f; ambientEcho.wetMix = 0.75f; ambientEcho.dryMix = 0.8f;
        var reverb = ambGO.AddComponent<AudioReverbFilter>(); reverb.reverbPreset = AudioReverbPreset.Hangar;

        cDing = Chord(new[] { 880f, 1320f, 1760f }, 0.6f, 5f, 0.8f);
        cClick = Tone(1200f, 0.05f, 18f, 0.5f);
        cTick = Tone(760f, 0.03f, 26f, 0.35f);
        cHover = Tone(980f, 0.025f, 30f, 0.22f);
        cSelect = Sweep(600f, 950f, 0.12f, 8f, 0.5f);
        cSave = Seq(new[] { 660f, 880f }, 0.08f, 0.02f, 14f, 0.45f);
        cLoad = Seq(new[] { 880f, 660f }, 0.08f, 0.02f, 14f, 0.45f);
        cHum = MakeHum(4f);

        // Typed notification cues.
        cInfo = Tone(700f, 0.1f, 12f, 0.4f);
        cResearch = Seq(new[] { 660f, 880f, 1100f }, 0.09f, 0.02f, 10f, 0.5f);   // pleasant rising arpeggio
        cDiscovery = Chord(new[] { 1046f, 1568f, 2093f }, 0.5f, 6f, 0.5f);       // bright shimmer
        cDanger = Chord(new[] { 180f, 190f }, 0.5f, 3f, 0.6f);                   // low dissonant buzz
        cVictory = Seq(new[] { 523f, 659f, 784f, 1046f }, 0.11f, 0.01f, 6f, 0.5f); // triumphant rise
        cDefeat = Seq(new[] { 523f, 415f, 349f, 262f }, 0.13f, 0.01f, 6f, 0.5f);   // descending minor
        BuildSpaceNoises();

        // Per-unit selection cues (distinct character per class) + a destruction sound. Sized to the
        // enum so newer classes (Mk II variants, Terraformer) always have an entry.
        cUnitSelect = new AudioClip[System.Enum.GetValues(typeof(UnitType)).Length];
        cUnitSelect[(int)UnitType.Scout]        = Seq(new[] { 1200f, 1500f }, 0.05f, 0.01f, 20f, 0.4f);      // quick chirp
        cUnitSelect[(int)UnitType.ResearchShip] = Sweep(760f, 1180f, 0.14f, 8f, 0.42f);                       // warble
        cUnitSelect[(int)UnitType.Fighter]      = Seq(new[] { 320f, 260f }, 0.07f, 0.01f, 12f, 0.5f);         // low growl
        cUnitSelect[(int)UnitType.ColonyShip]   = Chord(new[] { 300f, 400f, 500f }, 0.28f, 6f, 0.45f);        // deep swell
        cUnitSelect[(int)UnitType.ScoutII]        = cUnitSelect[(int)UnitType.Scout];
        cUnitSelect[(int)UnitType.ResearchShipII] = cUnitSelect[(int)UnitType.ResearchShip];
        cUnitSelect[(int)UnitType.FighterII]      = cUnitSelect[(int)UnitType.Fighter];
        cUnitSelect[(int)UnitType.Terraformer]    = Sweep(420f, 700f, 0.2f, 5f, 0.45f);                       // deep rising engineering tone
        cDestroyed = Explosion(0.6f);

        // The old fixed-partial hum loop is retired: AmbientDrone replaces it with a live synth whose
        // voices glide between chords. Keeping both would just muddy the bed with a second, static
        // chord that never moves — and the whole point is that the harmony breathes.
        AmbientDrone.Create();

        ApplyVolume();
        ambientTimer = Random.Range(5f, 9f);
    }

    void Update()
    {
        ambientTimer -= Time.unscaledDeltaTime;
        if (ambientTimer <= 0f)
        {
            ambientTimer = NextGap();
            PlaySpaceNoise();
        }
    }

    // ---- When the next noise happens ----
    //
    // Random.Range(5f, 9f) is a uniform pick, and uniform is the least natural distribution there is:
    // every gap lands in a four-second band, so noises arrive with a metronome's regularity — you start
    // predicting them, and a sound you can predict has stopped being ambience.
    //
    // Real sporadic events are a Poisson process: gaps follow an exponential distribution, which is
    // mostly-short with an occasional long one, and crucially is MEMORYLESS — how long it's been quiet
    // tells you nothing about when the next one comes. That's what "you never know when" actually
    // sounds like. Sampled by inverse transform: -mean * ln(u).
    //
    // On top of that, chatter in real life clusters — a burst of traffic, then nothing. So sometimes a
    // noise answers the last one a beat later, as if something replied.
    float NextGap()
    {
        if (burstLeft > 0)
        {
            burstLeft--;
            return Random.Range(0.45f, 1.6f);        // a reply, close on the heels of the last
        }

        if (Random.value < 0.22f) burstLeft = Random.Range(1, 4);   // a short exchange is about to start

        float u = Mathf.Max(0.0001f, Random.value);
        float gap = -7.5f * Mathf.Log(u);            // exponential, mean 7.5s
        return Mathf.Clamp(gap, 2.5f, 40f);          // never machine-gun, never feel switched off
    }
    int burstLeft;

    void PlaySpaceNoise()
    {
        if (noises == null || noises.Count == 0) return;
        var n = noises[Random.Range(0, noises.Count)];

        // Distance. A near noise is bright and dry; a far one is muffled, quiet, and washed out — which
        // is most of them, because the far ones are what make the space feel big.
        bool near = Random.value < 0.28f;
        float dist = near ? Random.Range(0f, 0.35f) : Random.Range(0.55f, 1f);

        ambientLP.cutoffFrequency = Mathf.Lerp(5500f, 950f, dist);

        // Per-shot echo: the noise's own echoiness, opened up further the further away it is.
        float wet = Mathf.Clamp01(n.echo * Mathf.Lerp(0.75f, 1.25f, dist));
        ambientEcho.wetMix = Mathf.Lerp(0.05f, 0.95f, wet);
        ambientEcho.decayRatio = Mathf.Lerp(0.25f, 0.72f, wet);
        ambientEcho.delay = Mathf.Lerp(120f, 480f, wet) * Random.Range(0.85f, 1.15f);

        // Tuned noises land on the drone's current chord. Untuned ones (creaks, static bursts) have no
        // pitch worth tuning, so they only drift a little — forcing them onto a chord tone would just
        // transpose their noise band for no musical gain.
        ambient.pitch = n.tuned > 0.5f ? ChordPitch() : Random.Range(0.88f, 1.12f);

        ambient.PlayOneShot(n.clip, Mathf.Lerp(0.30f, 0.09f, dist) * n.level);
    }

    // Tune a chirp to the chord the drone is currently holding.
    //
    // This used to be Random.Range(0.8f, 1.15f) — a random detune, harmonically unrelated to anything,
    // which is why the chirps read as noise over the top of the bed rather than as part of it. Now the
    // pitch is a real musical interval: a tone of the current chord, transposed by the drone's current
    // root. So when the drone slides to a new chord, the chirps move with it into the new key.
    //
    // Shifting by SEMITONES rather than to an absolute frequency keeps this independent of whatever
    // pitch the chatter clips were synthesised at — each keeps its own character and simply lands on a
    // consonant interval.
    float ChordPitch()
    {
        var semis = AmbientDrone.CurrentSemitones;
        if (semis == null || semis.Length == 0) return Random.Range(0.9f, 1.1f);

        int tone = semis[Random.Range(0, semis.Length)];
        int semitone = ((AmbientDrone.CurrentRootSemi + tone) % 12 + 12) % 12;   // fold into one octave

        // Octave placement: mostly up, because high and thin is what makes a chirp sound otherworldly
        // rather than like a foghorn.
        float r = Random.value;
        float octave = r < 0.15f ? 0.5f : r < 0.6f ? 1f : 2f;

        // A few cents of drift so repeated chirps on the same tone aren't machine-identical.
        float cents = Random.Range(-0.012f, 0.012f);
        return Mathf.Pow(2f, semitone / 12f + cents) * octave;
    }

    // ---- Public triggers ----
    public void PlayClick()    { sfx.pitch = Random.Range(0.97f, 1.05f); sfx.PlayOneShot(cClick, 0.5f); }
    public void PlaySelect()   { sfx.pitch = 1f; sfx.PlayOneShot(cSelect, 0.6f); }
    public void PlayComplete() { sfx.pitch = 1f; sfx.PlayOneShot(cDing, 0.7f); }
    public void PlayTick()
    {
        if (Time.unscaledTime - lastTick < 0.045f) return;   // throttle slider spam
        lastTick = Time.unscaledTime;
        sfx.pitch = Random.Range(0.95f, 1.08f);
        sfx.PlayOneShot(cTick, 0.3f);
    }
    public void PlayHover()
    {
        if (Time.unscaledTime - lastHover < 0.03f) return;
        lastHover = Time.unscaledTime;
        sfx.pitch = 1f; sfx.PlayOneShot(cHover, 0.22f);
    }
    public void PlaySave() { sfx.pitch = 1f; sfx.PlayOneShot(cSave, 0.6f); }
    public void PlayLoad() { sfx.pitch = 1f; sfx.PlayOneShot(cLoad, 0.6f); }
    public void PlayNotify(NotifKind k) { sfx.pitch = 1f; sfx.PlayOneShot(NotifyClip(k), 0.6f); }

    public void PlayUnitSelect(UnitType t)
    {
        if (cUnitSelect == null) return;
        int i = (int)t;
        if (i < 0 || i >= cUnitSelect.Length || cUnitSelect[i] == null) return;
        sfx.pitch = 1f;
        sfx.PlayOneShot(cUnitSelect[i], 0.55f);
    }
    public void PlayUnitDestroyed() { sfx.pitch = 1f; sfx.PlayOneShot(cDestroyed, 0.7f); }

    AudioClip NotifyClip(NotifKind k)
    {
        switch (k)
        {
            case NotifKind.Research: return cResearch;
            case NotifKind.Discovery: return cDiscovery;
            case NotifKind.Danger: return cDanger;
            case NotifKind.Victory: return cVictory;
            case NotifKind.Defeat: return cDefeat;
            default: return cInfo;
        }
    }

    // ---- Volume control ----
    // Master scales everything (via AudioListener). Effects scales the clean UI/selection/alert
    // one-shots. Hum scales the deep constant drone; Ambient scales the occasional chirps/chatter —
    // two separate knobs so you can turn the hum up and the random noises down. Mute overrides all.
    public void SetVolume(float v) { MasterVolume = Mathf.Clamp01(v); ApplyVolume(); }
    public void SetEffectsVolume(float v) { EffectsVolume = Mathf.Clamp01(v); ApplyVolume(); }
    public void SetHumVolume(float v) { HumVolume = Mathf.Clamp01(v); ApplyVolume(); }
    public void SetAmbientVolume(float v) { AmbientVolume = Mathf.Clamp01(v); ApplyVolume(); }
    public void ToggleMute() { Muted = !Muted; ApplyVolume(); }
    public void SetMuted(bool m) { Muted = m; ApplyVolume(); }
    void ApplyVolume()
    {
        AudioListener.volume = Muted ? 0f : MasterVolume;
        if (sfx != null) sfx.volume = EffectsVolume;
        // The drone reads HumVolume itself each frame (it's synthesised, not a clip), so there's no
        // source volume to set here. The legacy hum source stays silent.
        if (hum != null) hum.volume = 0f;
        if (ambient != null) ambient.volume = AmbientVolume;
    }

    // ---- Clip generation ----
    static AudioClip Tone(float freq, float dur, float decay, float amp)
    {
        int sr = 44100, n = (int)(sr * dur); var d = new float[n];
        for (int i = 0; i < n; i++) { float t = i / (float)sr; d[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-decay * t) * amp; }
        var c = AudioClip.Create("t", n, 1, sr, false); c.SetData(d, 0); return c;
    }

    static AudioClip Chord(float[] freqs, float dur, float decay, float amp)
    {
        int sr = 44100, n = (int)(sr * dur); var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr, s = 0f;
            foreach (var f in freqs) s += Mathf.Sin(2f * Mathf.PI * f * t);
            d[i] = (s / freqs.Length) * Mathf.Exp(-decay * t) * amp;
        }
        var c = AudioClip.Create("c", n, 1, sr, false); c.SetData(d, 0); return c;
    }

    static AudioClip Sweep(float f0, float f1, float dur, float decay, float amp)
    {
        int sr = 44100, n = (int)(sr * dur); var d = new float[n]; float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr; float f = Mathf.Lerp(f0, f1, t / dur);
            phase += 2f * Mathf.PI * f / sr;
            d[i] = Mathf.Sin(phase) * Mathf.Exp(-decay * t) * amp;
        }
        var c = AudioClip.Create("s", n, 1, sr, false); c.SetData(d, 0); return c;
    }

    // ============================================================================================
    // THE SPACE-NOISE LIBRARY
    //
    // Six families rather than one. The old ambience was five variations on a single idea — a run of
    // pitch-bent blips — so however many clips there were, it always sounded like the same machine
    // talking. Variety has to come from different KINDS of sound, not different seeds of one kind.
    //
    // Every family declares its own echo, because echo is what places a sound in the world: a servo is
    // right here on the hull, a sonar ping is bouncing off something a very long way away. Mixing dry
    // and wet is most of what makes the space feel big.
    // ============================================================================================
    void BuildSpaceNoises()
    {
        noises = new List<SpaceNoise>();

        // Robot chatter — the old sound, kept: warbly compound blips, like something reporting in.
        for (int i = 0; i < 4; i++)
            noises.Add(new SpaceNoise { clip = Chatter(i), echo = 0.35f, tuned = 1f, level = 1f });

        // Telemetry — faster, tighter, more machine-like: a burst of data, not a sentence.
        for (int i = 0; i < 3; i++)
            noises.Add(new SpaceNoise { clip = Telemetry(i), echo = 0.20f, tuned = 1f, level = 0.85f });

        // Sonar — one long tone thrown into the dark. The most echoey thing here, by a distance.
        for (int i = 0; i < 3; i++)
            noises.Add(new SpaceNoise { clip = SonarPing(i), echo = 1f, tuned = 1f, level = 1f });

        // Servos — a motor turning somewhere close. Dry, because it's on your own hull.
        for (int i = 0; i < 3; i++)
            noises.Add(new SpaceNoise { clip = Servo(i), echo = 0.10f, tuned = 0f, level = 0.9f });

        // Hull groans — metal under load. Deep, slow, and unsettling; the ship complaining.
        for (int i = 0; i < 3; i++)
            noises.Add(new SpaceNoise { clip = HullGroan(i), echo = 0.55f, tuned = 1f, level = 1.1f });

        // Radio wash — a smear of filtered static drifting past. Pure texture, no pitch.
        for (int i = 0; i < 2; i++)
            noises.Add(new SpaceNoise { clip = RadioWash(i), echo = 0.75f, tuned = 0f, level = 0.7f });
    }

    // ---- The envelope every space noise wears ----
    //
    // Long, eased fades at BOTH ends. This is the single thing that decides whether a procedural sound
    // reads as "ambience" or as "a sound effect fired at me": an abrupt start has a click in it, and the
    // ear locates a click instantly and precisely. Bleed the attack in over a third of a second and
    // there's no edge to catch, so the sound seems to have been there all along and you only just
    // noticed it. Cubic easing rather than linear because linear still has a corner at each end, and a
    // corner is audible.
    //
    // The clip is padded first, so the fades have somewhere to happen — otherwise a long fade-in just
    // eats the front of the sound instead of easing into it.
    static void ShapeAmbient(List<float> d, int sr, float fadeInSec, float fadeOutSec, float tailSec)
    {
        // InsertRange, NOT a loop of Insert(0, ...): every single-element insert at the front shifts the
        // entire remaining buffer, so padding ~20,000 samples one at a time is quadratic and would stall
        // startup for seconds while it built these clips. One InsertRange shifts once.
        int leadIn = (int)(sr * fadeInSec);
        d.InsertRange(0, new float[leadIn]);

        int trail = (int)(sr * fadeOutSec);
        d.AddRange(new float[trail]);

        int total = d.Count;
        int fadeIn = (int)(sr * fadeInSec * 2f);
        fadeIn = Mathf.Min(fadeIn, total / 2);
        for (int i = 0; i < fadeIn; i++)
        {
            float k = i / (float)Mathf.Max(1, fadeIn);
            d[i] *= k * k * k;
        }

        int fadeLen = Mathf.Min((int)(sr * fadeOutSec * 2f), total / 2);
        int fadeStart = total - fadeLen;
        for (int i = fadeStart; i < total; i++)
        {
            float k = 1f - (i - fadeStart) / (float)Mathf.Max(1, fadeLen);
            d[i] *= k * k * k;
        }

        // Silent tail so the echo/reverb wet signal rings out past the dry sound instead of being cut
        // off with the clip.
        d.AddRange(new float[(int)(sr * tailSec)]);
    }

    static AudioClip Finish(List<float> d, string name, int sr)
    {
        var arr = d.ToArray();
        var c = AudioClip.Create(name, Mathf.Max(1, arr.Length), 1, sr, false);
        c.SetData(arr, 0);
        return c;
    }

    // Compound robot/space chatter: a run of short, pitch-bent, square-ish blips. Distinct timbre
    // from the clean sine UI sounds so the player never confuses ambience with functional cues.
    static AudioClip Chatter(int idx)
    {
        int sr = 44100;
        var rng = new System.Random(9173 + idx * 57);
        int segs = 4 + rng.Next(4);
        var d = new List<float>();
        for (int s = 0; s < segs; s++)
        {
            float dur = 0.05f + (float)rng.NextDouble() * 0.09f;
            int n = (int)(sr * dur);
            float f0 = 250f + (float)rng.NextDouble() * 1200f;
            float f1 = f0 * (0.6f + (float)rng.NextDouble() * 0.9f);   // pitch bend
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float f = Mathf.Lerp(f0, f1, i / (float)n);
                phase += 2f * Mathf.PI * f / sr;
                // Mostly sine with a touch of square -> gentle, warm, "comforting" not harsh.
                float tone = Mathf.Sin(phase) * 0.45f + Mathf.Sign(Mathf.Sin(phase)) * 0.10f;
                float env = Mathf.Sin(Mathf.PI * (i / (float)n));                            // per-blip fade
                d.Add(tone * env * 0.45f);
            }
            int gap = (int)(sr * 0.02f);
            for (int i = 0; i < gap; i++) d.Add(0f);
        }

        ShapeAmbient(d, sr, 0.30f, 0.45f, 0.8f);
        return Finish(d, "chatter", sr);
    }

    // Telemetry: a tight burst of very short blips on a rigid grid — data, not speech. Reads as
    // machine precisely because it ISN'T warbly: fixed pitches, fixed spacing, no bend.
    static AudioClip Telemetry(int idx)
    {
        int sr = 44100;
        var rng = new System.Random(4421 + idx * 131);
        int segs = 6 + rng.Next(8);
        var d = new List<float>();
        float baseF = 700f + (float)rng.NextDouble() * 900f;

        for (int s = 0; s < segs; s++)
        {
            // Pitches drawn from a small fixed set, so the burst sounds coded rather than random.
            float f = baseF * (1f + rng.Next(4) * 0.25f);
            int n = (int)(sr * 0.022f);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                phase += 2f * Mathf.PI * f / sr;
                float env = Mathf.Sin(Mathf.PI * (i / (float)n));
                d.Add(Mathf.Sin(phase) * env * 0.32f);
            }
            int gap = (int)(sr * 0.014f);
            for (int i = 0; i < gap; i++) d.Add(0f);
        }

        ShapeAmbient(d, sr, 0.22f, 0.35f, 0.6f);
        return Finish(d, "telemetry", sr);
    }

    // Sonar: one long, slowly-decaying tone with a touch of its own fifth. Nothing else in the set is
    // this sustained, so it carries the echo further than anything — it's the sound of the space the
    // rest of the noises are happening in.
    static AudioClip SonarPing(int idx)
    {
        int sr = 44100;
        var rng = new System.Random(881 + idx * 977);
        float f = 320f + (float)rng.NextDouble() * 340f;
        float dur = 0.55f + (float)rng.NextDouble() * 0.5f;
        int n = (int)(sr * dur);
        var d = new List<float>(n);

        float p1 = 0f, p2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            // A slight downward drift over the ping's life: something falling away from you.
            float ff = Mathf.Lerp(f, f * 0.94f, t);
            p1 += 2f * Mathf.PI * ff / sr;
            p2 += 2f * Mathf.PI * ff * 1.5f / sr;      // its fifth, quiet, for a hollow metallic ring
            float body = Mathf.Sin(p1) * 0.5f + Mathf.Sin(p2) * 0.12f;
            d.Add(body * Mathf.Exp(-2.2f * t) * 0.5f);
        }

        ShapeAmbient(d, sr, 0.35f, 0.6f, 1.2f);
        return Finish(d, "sonar", sr);
    }

    // Servo: a buzzing motor that spins up, holds, and spins down. Built from a slightly detuned pair
    // of saw-ish tones plus an amplitude flutter at the "rotation" rate, which is what sells it as a
    // thing that physically turns rather than a tone that happens to slide.
    static AudioClip Servo(int idx)
    {
        int sr = 44100;
        var rng = new System.Random(2287 + idx * 313);
        float dur = 0.35f + (float)rng.NextDouble() * 0.4f;
        int n = (int)(sr * dur);
        float f0 = 90f + (float)rng.NextDouble() * 70f;
        float f1 = f0 * (1.5f + (float)rng.NextDouble() * 1.2f);
        float flutter = 26f + (float)rng.NextDouble() * 22f;
        var d = new List<float>(n);

        float phase = 0f, fphase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            // Up then back down: a motor that starts, works, and stops.
            float f = Mathf.Lerp(f0, f1, Mathf.Sin(Mathf.PI * t));
            phase += 2f * Mathf.PI * f / sr;
            fphase += 2f * Mathf.PI * flutter / sr;

            // Saw via its first few harmonics — warmer and far less harsh than a raw ramp.
            float saw = Mathf.Sin(phase) * 0.5f + Mathf.Sin(phase * 2f) * 0.25f + Mathf.Sin(phase * 3f) * 0.12f;
            float am = 0.72f + 0.28f * Mathf.Sin(fphase);
            d.Add(saw * am * 0.30f);
        }

        ShapeAmbient(d, sr, 0.18f, 0.30f, 0.4f);
        return Finish(d, "servo", sr);
    }

    // Hull groan: metal under load. A very low tone that bends slightly, with a resonant partial and a
    // little filtered noise riding on it for the "grinding" edge. Slow, and the only family down in the
    // drone's own register — so it feels like the ship, not like something outside it.
    static AudioClip HullGroan(int idx)
    {
        int sr = 44100;
        var rng = new System.Random(6607 + idx * 449);
        float dur = 1.1f + (float)rng.NextDouble() * 1.0f;
        int n = (int)(sr * dur);
        float f = 48f + (float)rng.NextDouble() * 36f;
        float bend = 0.90f + (float)rng.NextDouble() * 0.2f;
        var d = new List<float>(n);

        float p1 = 0f, p2 = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float ff = Mathf.Lerp(f, f * bend, t);
            p1 += 2f * Mathf.PI * ff / sr;
            p2 += 2f * Mathf.PI * ff * 2.02f / sr;    // slightly sharp octave: beats against the root

            float noise = (float)(rng.NextDouble() * 2 - 1);
            lp += (noise - lp) * 0.012f;              // one-pole low-pass -> rumble, not hiss

            float body = Mathf.Sin(p1) * 0.55f + Mathf.Sin(p2) * 0.18f + lp * 0.5f;
            float env = Mathf.Sin(Mathf.PI * t);      // swells and subsides: a load coming on and off
            d.Add(body * env * 0.42f);
        }

        ShapeAmbient(d, sr, 0.45f, 0.7f, 1.0f);
        return Finish(d, "groan", sr);
    }

    // Radio wash: band-passed static that drifts open and closed, like a signal sweeping past and not
    // quite resolving. No pitch at all — it's the texture between the other sounds.
    static AudioClip RadioWash(int idx)
    {
        int sr = 44100;
        var rng = new System.Random(1543 + idx * 691);
        float dur = 0.8f + (float)rng.NextDouble() * 0.8f;
        int n = (int)(sr * dur);
        var d = new List<float>(n);

        float lp = 0f, hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float noise = (float)(rng.NextDouble() * 2 - 1);

            // The low-pass opens and shuts across the clip, which is what makes it "sweep past".
            float k = Mathf.Lerp(0.02f, 0.22f, Mathf.Sin(Mathf.PI * t));
            lp += (noise - lp) * k;

            // High-pass the result (subtract a slow follower) to leave a band rather than a rumble.
            hp = lp - prev; prev += (lp - prev) * 0.004f;

            float env = Mathf.Sin(Mathf.PI * t);
            d.Add(hp * env * 0.55f);
        }

        ShapeAmbient(d, sr, 0.40f, 0.55f, 0.9f);
        return Finish(d, "wash", sr);
    }

    // A short sequence of notes (for arpeggios / jingles).
    static AudioClip Seq(float[] freqs, float noteDur, float gap, float decay, float amp)
    {
        int sr = 44100;
        int noteN = (int)(sr * noteDur);
        int gapN = (int)(sr * gap);
        int n = (noteN + gapN) * freqs.Length;
        var d = new float[n];
        int idx = 0;
        foreach (var f in freqs)
        {
            for (int i = 0; i < noteN; i++)
            {
                float t = i / (float)sr;
                d[idx++] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-decay * t) * amp;
            }
            idx += gapN; // silence between notes
        }
        var c = AudioClip.Create("seq", n, 1, sr, false); c.SetData(d, 0); return c;
    }

    // A noisy descending boom for ship destruction.
    static AudioClip Explosion(float dur)
    {
        int sr = 44100, n = (int)(sr * dur); var d = new float[n];
        var rng = new System.Random(4242);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float env = Mathf.Exp(-6f * t);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float rumble = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(120f, 40f, t / dur) * t);
            d[i] = (noise * 0.6f + rumble * 0.5f) * env * 0.7f;
        }
        var c = AudioClip.Create("boom", n, 1, sr, false); c.SetData(d, 0); return c;
    }

    // A gravitational, Star-Wars-ish deep drone: a chord of complementing low pitches with an
    // undertone and overtones, plus detuned pairs that beat slowly and per-partial tremolos, so it
    // vibrates and subtly shifts. All frequencies/rates are multiples of 1/dur so it loops seamlessly.
    static AudioClip MakeHum(float dur)
    {
        int sr = 44100, n = (int)(sr * dur); var d = new float[n];

        // { frequency, amplitude, tremolo rate (Hz), tremolo depth }
        float[,] partials =
        {
            { 36.75f, 0.55f, 0.25f, 0.16f },  // deep undertone (gravitational rumble)
            { 55.00f, 0.60f, 0.50f, 0.12f },  // root
            { 55.50f, 0.34f, 0.75f, 0.10f },  // detuned root -> slow beat
            { 73.50f, 0.30f, 0.50f, 0.12f },  // low complement
            { 82.50f, 0.42f, 0.50f, 0.12f },  // fifth
            { 110.0f, 0.30f, 1.00f, 0.10f },  // octave
            { 110.5f, 0.18f, 0.75f, 0.10f },  // detuned octave -> shimmer beat
            { 165.0f, 0.15f, 1.00f, 0.10f },  // overtone (fifth above octave)
        };
        int P = partials.GetLength(0);
        float norm = 0f;
        for (int p = 0; p < P; p++) norm += partials[p, 1];

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float s = 0f;
            for (int p = 0; p < P; p++)
            {
                float f = partials[p, 0], a = partials[p, 1], tr = partials[p, 2], td = partials[p, 3];
                float trem = 1f - td * 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * tr * t)); // 0..1, integer cycles
                s += Mathf.Sin(2f * Mathf.PI * f * t) * a * trem;
            }
            d[i] = (s / norm) * 0.75f;
        }
        // stream:false — we fill the whole buffer up front with SetData (streamed clips reject SetData).
        var c = AudioClip.Create("hum", n, 1, sr, false); c.SetData(d, 0); return c;
    }
}
