using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// ¡LA FIESTA! — a surprise for the playtesters, right after the loading screen (2026-10-09)
//
// Every time the loading screen finishes, Smiguel arrives: he slams onto the screen over a blaring
// trumpet stinger, the mariachi band strikes up (looping), maracas spin and twirl across the screen,
// papel picado flutters along the top and the zarape stripes scroll behind him. It cannot be dismissed
// for the first SecondsLocked seconds; after that any click or key ends the fiesta and the game goes on.
//
// Assets (Resources/Prank/):
//   smiguel_el_magnifico.png  — the man himself. If it is missing, a sombrero and moustache stand in.
//   mariachi.mp3              — "Mariachi Snooze", Kevin MacLeod (incompetech.com), CC BY 3.0 — credited
//                               on screen, as the licence requires (see MARIACHI_CREDITS.txt).
// The trumpet stinger and the maraca shakes are synthesised here, so they need no files at all.
//
// Set Enabled = false to retire it.
// ============================================================================================
public class LoadingFiesta : MonoBehaviour
{
    public const bool Enabled = true;
    const float SecondsLocked = 10f;

    public static bool Active => instance != null;
    static LoadingFiesta instance;

    /// Start the fiesta (called when a loading screen finishes). Safe to call twice; the second is ignored.
    public static void Play()
    {
        if (!Enabled || instance != null) return;
        instance = new GameObject("LoadingFiesta").AddComponent<LoadingFiesta>();
    }

    Canvas canvas;
    RectTransform smiguel, stripesRT;
    RawImage stripes;
    TMP_Text shout, prompt;
    readonly List<RectTransform> maracas = new List<RectTransform>();
    readonly List<float> maracaPhase = new List<float>();
    readonly List<RectTransform> flags = new List<RectTransform>();
    AudioSource music, shaker, stinger;
    float started;
    bool closing;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        canvas = UIFactory.CreateCanvas("FiestaCanvas", 32000);   // above everything, loading screen included
        canvas.transform.SetParent(transform, false);
        var root = (RectTransform)canvas.transform;

        // The backdrop doubles as the click-catcher, so nothing behind the fiesta can be touched.
        stripes = UIFactory.NewUI(root, "Zarape").AddComponent<RawImage>();
        stripes.texture = Own(ZarapeTexture());
        stripes.raycastTarget = true;
        stripesRT = stripes.rectTransform;
        UIFactory.Stretch(stripesRT);

        BuildPapelPicado(root);

        // ---- SMIGUEL ----
        var tex = Resources.Load<Texture2D>("Prank/smiguel_el_magnifico");
        var img = UIFactory.NewUI(root, "Smiguel").AddComponent<RawImage>();
        img.raycastTarget = false;
        smiguel = img.rectTransform;
        smiguel.anchorMin = smiguel.anchorMax = new Vector2(0.5f, 0.45f);
        smiguel.pivot = new Vector2(0.5f, 0.5f);
        if (tex != null)
        {
            img.texture = tex;
            float aspect = tex.width / (float)Mathf.Max(1, tex.height);
            smiguel.sizeDelta = new Vector2(760f * aspect, 760f);
        }
        else
        {
            img.texture = Own(FallbackSmiguel());
            img.texture.filterMode = FilterMode.Point;
            smiguel.sizeDelta = new Vector2(640f, 640f);
        }
        smiguel.localScale = Vector3.one * 0.05f;

        // ---- MARACAS, spinning and twirling round the edges ----
        var maracaTex = Own(MaracaTexture());
        for (int i = 0; i < 8; i++)
        {
            var m = UIFactory.NewUI(root, "Maraca").AddComponent<RawImage>();
            m.texture = maracaTex;
            m.raycastTarget = false;
            var rt = m.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.3f);
            rt.sizeDelta = new Vector2(110f, 220f);
            maracas.Add(rt);
            maracaPhase.Add(i / 8f * Mathf.PI * 2f);
        }

        shout = UIFactory.Text(root, "¡¡¡AY AY AY!!!", 96, new Color(1f, 0.85f, 0.1f), TextAlignmentOptions.Center);
        shout.fontStyle = FontStyles.Bold;
        shout.raycastTarget = false;
        var srt = shout.rectTransform;
        srt.anchorMin = new Vector2(0f, 0.86f); srt.anchorMax = new Vector2(1f, 0.98f);
        srt.offsetMin = srt.offsetMax = Vector2.zero;
        // TMP's own outline — uGUI's Outline effect does nothing to TextMeshPro text.
        shout.outlineWidth = 0.3f;
        shout.outlineColor = new Color32(110, 0, 20, 255);

        prompt = UIFactory.Text(root, "", 34, Color.white, TextAlignmentOptions.Center);
        prompt.fontStyle = FontStyles.Bold;
        prompt.raycastTarget = false;
        var prt = prompt.rectTransform;
        prt.anchorMin = new Vector2(0f, 0.02f); prt.anchorMax = new Vector2(1f, 0.1f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        prompt.outlineWidth = 0.3f;
        prompt.outlineColor = new Color32(0, 0, 0, 255);

        // CC BY 3.0 asks for credit where the music plays.
        var credit = UIFactory.Text(root, "Music: \"Mariachi Snooze\" Kevin MacLeod (incompetech.com) — CC BY 3.0",
                                    13, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.BottomRight);
        credit.raycastTarget = false;
        var crt = credit.rectTransform;
        crt.anchorMin = new Vector2(0.5f, 0f); crt.anchorMax = new Vector2(1f, 0.03f);
        crt.offsetMin = new Vector2(0f, 2f); crt.offsetMax = new Vector2(-8f, 0f);

        // ---- SOUND ----
        stinger = gameObject.AddComponent<AudioSource>();
        stinger.clip = Own(TrumpetStinger());
        stinger.volume = 1f;
        stinger.ignoreListenerPause = true;

        music = gameObject.AddComponent<AudioSource>();
        music.clip = Resources.Load<AudioClip>("Prank/mariachi");
        music.loop = true;
        music.volume = 1f;
        music.ignoreListenerPause = true;

        shaker = gameObject.AddComponent<AudioSource>();
        shaker.clip = Own(MaracaShakes());
        shaker.loop = true;
        shaker.volume = 0.7f;
        shaker.ignoreListenerPause = true;

        // A listener has to exist for any of this to be heard; the main camera normally carries one.
        if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

        started = Time.unscaledTime;
        stinger.Play();
        if (music.clip != null) music.Play();
        shaker.Play();
    }

    void Update()
    {
        float t = Time.unscaledTime - started;

        // THE JUMP: from a dot to larger than life in a fifth of a second, then a little overshoot back.
        float pop = t < 0.18f ? Mathf.Lerp(0.05f, 1.25f, t / 0.18f)
                  : t < 0.35f ? Mathf.Lerp(1.25f, 1f, (t - 0.18f) / 0.17f)
                  : 1f + 0.04f * Mathf.Sin(t * 10.9f);   // bobbing to the beat (~165 bpm / 2)
        if (!closing) smiguel.localScale = Vector3.one * pop;
        // Screen shake for the first second, then a lazy sway.
        float shake = t < 1f ? (1f - t) * 28f : 0f;
        smiguel.anchoredPosition = new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake));
        smiguel.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 5.45f) * 6f);

        // The zarape scrolls.
        stripes.uvRect = new Rect(0f, t * 0.35f, 1f, 1f);

        // Maracas: each orbits the screen on its own ellipse, twirling as it goes.
        var area = ((RectTransform)canvas.transform).rect;
        for (int i = 0; i < maracas.Count; i++)
        {
            float a = maracaPhase[i] + t * (0.9f + 0.15f * (i % 3));
            float rx = area.width * 0.40f, ry = area.height * 0.36f;
            maracas[i].anchoredPosition = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a * (i % 2 == 0 ? 1f : -1f)) * ry);
            maracas[i].localRotation = Quaternion.Euler(0f, 0f, t * (i % 2 == 0 ? 540f : -420f) + i * 45f);
            maracas[i].localScale = Vector3.one * (0.85f + 0.2f * Mathf.Sin(t * 8f + i));
        }

        // Papel picado flutter.
        for (int i = 0; i < flags.Count; i++)
            flags[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 6f + i * 0.7f) * 9f);

        // The shout flashes colours.
        shout.color = Color.HSVToRGB(Mathf.Repeat(t * 0.8f, 1f), 0.9f, 1f);
        shout.rectTransform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t * 14f));

        float left = SecondsLocked - t;
        if (left > 0f)
            prompt.text = $"¡La fiesta continúa! ({Mathf.CeilToInt(left)})";
        else
        {
            prompt.text = "Click or press any key to escape Smiguel... if you can";
            if (!closing && (Input.anyKeyDown || Input.GetMouseButtonDown(0)))
                StartCoroutine(Close());
        }
    }

    IEnumerator Close()
    {
        closing = true;
        float t0 = Time.unscaledTime, dur = 0.6f;
        var group = canvas.gameObject.AddComponent<CanvasGroup>();
        while (Time.unscaledTime - t0 < dur)
        {
            float k = (Time.unscaledTime - t0) / dur;
            group.alpha = 1f - k;
            music.volume = shaker.volume = 1f - k;
            smiguel.localScale = Vector3.one * (1f - k * 0.9f);
            yield return null;
        }
        instance = null;
        Destroy(gameObject);
    }

    // Everything this fiesta made for itself goes with it: the generated textures and the synthesised
    // clips belong to no asset and would otherwise pile up one set per new game.
    readonly List<Object> owned = new List<Object>();
    T Own<T>(T o) where T : Object { if (o != null) owned.Add(o); return o; }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (var o in owned) if (o != null) Destroy(o);
        owned.Clear();
    }

    // ---- Decorations -----------------------------------------------------------------------------

    void BuildPapelPicado(RectTransform root)
    {
        var colors = new[] { new Color(0.95f, 0.15f, 0.45f), new Color(1f, 0.6f, 0f), new Color(1f, 0.9f, 0.1f),
                             new Color(0.1f, 0.75f, 0.3f), new Color(0.1f, 0.55f, 0.95f), new Color(0.6f, 0.2f, 0.85f) };
        var flagTex = Own(FlagTexture());
        const int n = 16;
        for (int i = 0; i < n; i++)
        {
            var f = UIFactory.NewUI(root, "Picado").AddComponent<RawImage>();
            f.texture = flagTex;
            f.color = colors[i % colors.Length];
            f.raycastTarget = false;
            var rt = f.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2((i + 0.5f) / n, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(90f, 110f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            flags.Add(rt);
        }
    }

    // ---- Procedural textures -----------------------------------------------------------------------

    static Texture2D ZarapeTexture()
    {
        // A Mexican serape: bold bands of colour with thin bright pinstripes between them.
        var bands = new[]
        {
            new Color32(200, 20, 60, 255), new Color32(255, 140, 0, 255), new Color32(255, 220, 30, 255),
            new Color32(20, 160, 70, 255), new Color32(0, 110, 200, 255), new Color32(120, 30, 160, 255),
            new Color32(230, 40, 120, 255), new Color32(255, 100, 20, 255)
        };
        const int h = 256;
        var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var px = new Color32[4 * h];
        for (int y = 0; y < h; y++)
        {
            int band = (y / 32) % bands.Length;
            Color32 c = bands[band];
            int inBand = y % 32;
            if (inBand == 0 || inBand == 31) c = new Color32(255, 255, 255, 255);
            else if (inBand == 3 || inBand == 28) c = new Color32(20, 20, 20, 255);
            for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Texture2D MaracaTexture()
    {
        // Upright maraca: a striped oval head over a wooden handle, transparent around it.
        const int w = 32, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        var stripes = new[] { new Color32(230, 30, 60, 255), new Color32(255, 200, 0, 255), new Color32(20, 170, 80, 255), new Color32(0, 120, 220, 255) };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 c = new Color32(0, 0, 0, 0);
                float dx = (x - 15.5f) / 14f, dy = (y - 44f) / 18f;          // head centred high (y up)
                if (dx * dx + dy * dy <= 1f)
                {
                    c = stripes[(y / 5) % stripes.Length];
                    if (dx * dx + dy * dy > 0.82f) c = new Color32(30, 20, 10, 255);   // dark rim
                }
                else if (y < 28 && x >= 13 && x <= 18) c = (x == 13 || x == 18) ? new Color32(70, 40, 15, 255) : new Color32(150, 95, 45, 255);
                px[y * w + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Texture2D FlagTexture()
    {
        // A papel picado flag: white (tinted by the RawImage) with punched-out holes and a zigzag hem.
        const int w = 30, h = 36;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool hem = y < 6 && (Mathf.Abs((x % 6) - 3) > y / 2);
                bool hole = ((x / 4 + y / 4) % 3 == 0) && x > 3 && x < w - 4 && y > 8 && y < h - 6;
                px[y * w + x] = hem || hole ? new Color32(0, 0, 0, 0) : new Color32(255, 255, 255, 255);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Texture2D FallbackSmiguel()
    {
        // In case the picture is missing: a sombrero and a magnificent moustache on a pale face.
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Color32 c = new Color32(0, 0, 0, 0);
                float fx = (x - 32f) / 13f, fy = (y - 26f) / 16f;
                if (fx * fx + fy * fy <= 1f) c = new Color32(225, 215, 190, 255);                      // face
                if (y >= 40 && y <= 43 && x >= 4 && x <= 60) c = new Color32(205, 160, 90, 255);       // brim
                if (y > 43 && y < 56 && Mathf.Abs(x - 32) < 12 - (y - 44) / 2) c = new Color32(215, 175, 100, 255); // crown
                if (y >= 20 && y <= 22 && Mathf.Abs(x - 32) <= 10 && Mathf.Abs(x - 32) >= 2) c = new Color32(30, 20, 15, 255); // moustache
                if (y == 30 && (x == 27 || x == 37)) c = new Color32(10, 10, 10, 255);                // eyes
                px[y * n + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ---- Synthesised sound ---------------------------------------------------------------------------

    const int Rate = 44100;

    /// A blaring brass chord — the jump in the jumpscare. Sawtooth stack (D major) with a fast swell.
    static AudioClip TrumpetStinger()
    {
        float dur = 1.1f;
        int n = (int)(Rate * dur);
        var data = new float[n];
        float[] freqs = { 293.66f, 369.99f, 440f, 587.33f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1f, t / 0.03f) * (t > 0.8f ? Mathf.Max(0f, 1f - (t - 0.8f) / 0.3f) : 1f);
            float vib = 1f + 0.006f * Mathf.Sin(t * 2f * Mathf.PI * 6f);
            float s = 0f;
            foreach (var f in freqs) s += 2f * Mathf.Repeat(t * f * vib, 1f) - 1f;
            data[i] = Mathf.Clamp(s / freqs.Length * 1.6f, -1f, 1f) * env;
        }
        var clip = AudioClip.Create("FiestaStinger", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// One bar of maraca shakes at the track's 165 bpm: short bursts of filtered noise on the eighths,
    /// accented on the beat. Loops seamlessly alongside the band.
    static AudioClip MaracaShakes()
    {
        float beat = 60f / 165f;
        int n = (int)(Rate * beat * 4f);
        var data = new float[n];
        var rng = new System.Random(1824);
        float lp = 0f;
        for (int k = 0; k < 8; k++)
        {
            int start = (int)(k * beat * 0.5f * Rate);
            float accent = k % 2 == 0 ? 1f : 0.6f;
            int len = (int)(0.07f * Rate);
            for (int i = 0; i < len && start + i < n; i++)
            {
                float env = Mathf.Exp(-i / (0.018f * Rate)) * accent;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.55f;                // a little softening so it rattles, not hisses
                data[start + i] += (noise - lp) * env * 0.9f;
            }
        }
        var clip = AudioClip.Create("FiestaMaracas", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
