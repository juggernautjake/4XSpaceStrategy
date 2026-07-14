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

    AudioClip cDing, cClick, cTick, cSelect, cHum, cHover, cSave, cLoad;
    AudioClip cResearch, cDiscovery, cDanger, cVictory, cDefeat, cInfo;
    AudioClip[] cChatter;   // compound robot/space chatter for ambience (kept distinct from UI sounds)
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
        var echo = ambGO.AddComponent<AudioEchoFilter>(); echo.delay = 275f; echo.decayRatio = 0.58f; echo.wetMix = 0.75f; echo.dryMix = 0.8f; // deep, cavernous echo
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
        // Ambient robot/space chatter — deliberately warbly & compound so it never sounds like the
        // clean functional UI/alert tones.
        cChatter = new[] { Chatter(0), Chatter(1), Chatter(2), Chatter(3), Chatter(4) };

        // Per-unit selection cues (distinct character per class) + a destruction sound.
        cUnitSelect = new AudioClip[4];
        cUnitSelect[(int)UnitType.Scout]       = Seq(new[] { 1200f, 1500f }, 0.05f, 0.01f, 20f, 0.4f);       // quick chirp
        cUnitSelect[(int)UnitType.ResearchShip]= Sweep(760f, 1180f, 0.14f, 8f, 0.42f);                        // warble
        cUnitSelect[(int)UnitType.Fighter]     = Seq(new[] { 320f, 260f }, 0.07f, 0.01f, 12f, 0.5f);          // low growl
        cUnitSelect[(int)UnitType.ColonyShip]  = Chord(new[] { 300f, 400f, 500f }, 0.28f, 6f, 0.45f);         // deep swell
        cDestroyed = Explosion(0.6f);

        hum.clip = cHum; hum.volume = 0.5f; hum.Play();   // louder, but still a background bed

        ApplyVolume();
        ambientTimer = Random.Range(5f, 9f);
    }

    void Update()
    {
        // Very slowly waver the drone so the deep tones shift now and then.
        hum.pitch = 1f + Mathf.Sin(Time.unscaledTime * 0.15f) * 0.02f + Mathf.Sin(Time.unscaledTime * 0.06f) * 0.012f;

        ambientTimer -= Time.unscaledDeltaTime;
        if (ambientTimer <= 0f)
        {
            ambientTimer = Random.Range(5f, 9f);   // sporadic, a touch more frequent
            if (cChatter != null && cChatter.Length > 0)
            {
                // Occasionally a "closer/clearer" one; usually faint & muffled/distant.
                bool near = Random.value < 0.3f;
                ambientLP.cutoffFrequency = near ? Random.Range(3500f, 5500f) : Random.Range(1100f, 2400f);
                float vol = near ? Random.Range(0.20f, 0.28f) : Random.Range(0.09f, 0.16f);
                ambient.pitch = Random.Range(0.8f, 1.15f);
                ambient.PlayOneShot(cChatter[Random.Range(0, cChatter.Length)], vol);
            }
        }
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
        sfx.pitch = 1f;
        sfx.PlayOneShot(cUnitSelect[(int)t], 0.55f);
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
        if (hum != null) hum.volume = HumBase * HumVolume;
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

        int total = d.Count;

        // Smooth fade-IN across the first ~20% so the message drifts in rather than snapping on.
        int fadeIn = (int)(total * 0.20f);
        for (int i = 0; i < fadeIn; i++)
        {
            float k = i / (float)Mathf.Max(1, fadeIn);
            d[i] *= k * k;   // eased fade-in
        }

        // Smooth fade-OUT across the last ~45% so the message trails off instead of stopping hard.
        int fadeStart = (int)(total * 0.55f);
        for (int i = fadeStart; i < total; i++)
        {
            float k = 1f - (i - fadeStart) / (float)Mathf.Max(1, total - fadeStart);
            d[i] *= k * k;   // eased fade-out
        }
        // Long silent tail so the echo/reverb wet signal rings out naturally past the dry sound.
        int tail = (int)(sr * 0.8f);
        for (int i = 0; i < tail; i++) d.Add(0f);

        var arr = d.ToArray();
        var c = AudioClip.Create("chatter", arr.Length, 1, sr, false); c.SetData(arr, 0); return c;
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
