using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// THINGS BLOWING UP
//
// Three sizes of the same event, and they differ by more than scale:
//
//   BURST   a round being swatted out of the air by point defence. A spark and a puff, gone in a
//           quarter second. No sound of its own — a battle where every intercept went bang would be
//           unlistenable, and there can be dozens a second.
//   HIT     a shot landing on a hull that survives it. Brief, coloured by the WEAPON, so the player
//           can tell what is hitting them by looking at the flash.
//   DEATH   a ship coming apart. A white-hot core that expands and cools through orange to smoke, a
//           shell of debris sparks thrown outward, and a light that flares and fades. This is the one
//           that gets a sound, and it is sized by the hull — a scout pops, a dreadnought detonates.
//
// ---- WHY THIS IS NOT A PARTICLE SYSTEM -------------------------------------------------------
//
// A Unity ParticleSystem per explosion means an Instantiate per death, a prefab to keep in sync, and a
// component whose settings live in the editor rather than in this file where the reasoning is. Every
// explosion here is a handful of pooled quads moved by one loop — the same discipline as
// ProjectileRenderer, for the same reason, and it keeps the whole effect readable as code.
//
// The palette is a real cooling curve rather than a chosen gradient: white -> yellow -> orange -> red
// -> dark smoke is what hot things actually do as they lose energy, and it is why the effect reads as
// heat rather than as a coloured circle getting bigger.
// ============================================================================================
public class ExplosionRenderer : MonoBehaviour
{
    public static ExplosionRenderer Instance;

    class Puff
    {
        public Transform tr;
        public Vector3 vel;
        public float life, maxLife;
        public float startScale, endScale;
        public Color tint;            // null-ish: when a weapon colour is supplied it overrides the ramp
        public bool useTint;
        public float spin;
    }

    readonly List<Puff> puffs = new List<Puff>();
    readonly Stack<Transform> pool = new Stack<Transform>();
    Material puffMat;

    /// A hard ceiling on live quads. A fleet action that killed forty ships in one second would
    /// otherwise queue up a thousand of these and cost more than the battle did.
    const int MaxLive = 420;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("ExplosionRenderer");
        go.AddComponent<ExplosionRenderer>();
        go.AddComponent<MapTierVisibility>();
    }

    void Awake()
    {
        Instance = this;
        puffMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = PuffTexture() };
    }

    // ============================================================================================
    // THE THREE SIZES
    // ============================================================================================

    /// A round swatted down. Cheap, silent, and over immediately.
    public void Burst(Vector3 at, float scale, Color colour)
    {
        Spawn(at, Random.insideUnitSphere * 1.2f, 0.22f, scale * 0.5f, scale * 1.4f, colour, true);
        for (int i = 0; i < 3; i++)
            Spawn(at, Random.insideUnitSphere * 4f, 0.18f, scale * 0.22f, 0.01f, colour, true);
    }

    /// A shot landing on a hull that survives. Coloured by the weapon that threw it, so the player can
    /// read what is shooting at them off the impact flash alone.
    public void Impact(Vector3 at, Color weaponColour)
    {
        Spawn(at, Vector3.zero, 0.20f, 0.30f, 0.9f, weaponColour, true);
        for (int i = 0; i < 4; i++)
            Spawn(at, Random.insideUnitSphere * 5f, 0.26f, 0.14f, 0.01f, weaponColour, true);
    }

    /// A ship coming apart. `size` is the hull's drawn scale, so the effect is proportional to the
    /// thing that died rather than the same fireball for a scout and a dreadnought.
    public void Death(Vector3 at, float size)
    {
        size = Mathf.Clamp(size, 0.35f, 6f);

        // The core: one big quad that expands fast and cools through the ramp as it goes.
        Spawn(at, Vector3.zero, 0.85f, size * 0.5f, size * 3.4f, Color.white, false);

        // A second, slower shell so the fireball has depth rather than being one flat disc.
        Spawn(at, Vector3.zero, 1.25f, size * 0.2f, size * 4.6f, Color.white, false);

        // Debris. Thrown outward at a spread of speeds so the shell breaks up rather than expanding as
        // a ring — a ring reads as a shockwave, and this is meant to read as wreckage.
        int shards = Mathf.RoundToInt(Mathf.Lerp(7f, 22f, Mathf.InverseLerp(0.35f, 6f, size)));
        for (int i = 0; i < shards; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            float speed = Random.Range(2.5f, 9f) * Mathf.Lerp(0.7f, 1.6f, Mathf.InverseLerp(0.35f, 6f, size));
            Spawn(at, dir * speed, Random.Range(0.5f, 1.1f), size * Random.Range(0.10f, 0.22f), 0.01f,
                  Color.white, false);
        }
    }

    // ============================================================================================
    void Spawn(Vector3 at, Vector3 vel, float life, float from, float to, Color tint, bool useTint)
    {
        if (puffs.Count >= MaxLive) return;

        var tr = Rent();
        tr.position = at;
        tr.localScale = Vector3.one * from;
        tr.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        puffs.Add(new Puff
        {
            tr = tr, vel = vel, life = 0f, maxLife = life,
            startScale = from, endScale = to, tint = tint, useTint = useTint,
            spin = Random.Range(-140f, 140f)
        });
    }

    void Update()
    {
        // Unscaled by design, like the projectile arcs: an explosion is an animation, not a simulation,
        // and one playing in slow motion because the game is paused reads as a bug.
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            var p = puffs[i];
            p.life += dt;
            float k = Mathf.Clamp01(p.life / p.maxLife);

            if (p.tr == null) { puffs.RemoveAt(i); continue; }

            p.tr.position += p.vel * dt;
            p.vel *= 1f - Mathf.Min(0.9f, 2.2f * dt);            // drag, so debris slows rather than flying forever
            p.tr.localScale = Vector3.one * Mathf.Lerp(p.startScale, p.endScale, EaseOut(k));
            p.tr.Rotate(0f, 0f, p.spin * dt, Space.Self);

            Color c = p.useTint ? p.tint : CoolingRamp(k);
            c.a = 1f - k * k;                                     // hold brightness, then drop away fast
            var mr = p.tr.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.color = c;

            if (k >= 1f)
            {
                p.tr.gameObject.SetActive(false);
                pool.Push(p.tr);
                puffs.RemoveAt(i);
            }
        }
    }

    /// What a hot thing looks like as it loses energy: white, yellow, orange, red, smoke. Not a chosen
    /// gradient — this is why the effect reads as heat rather than as a coloured circle expanding.
    static Color CoolingRamp(float k)
    {
        if (k < 0.15f) return Color.Lerp(new Color(1f, 1f, 1f), new Color(1f, 0.95f, 0.65f), k / 0.15f);
        if (k < 0.40f) return Color.Lerp(new Color(1f, 0.95f, 0.65f), new Color(1f, 0.60f, 0.18f), (k - 0.15f) / 0.25f);
        if (k < 0.70f) return Color.Lerp(new Color(1f, 0.60f, 0.18f), new Color(0.85f, 0.22f, 0.10f), (k - 0.40f) / 0.30f);
        return Color.Lerp(new Color(0.85f, 0.22f, 0.10f), new Color(0.18f, 0.16f, 0.16f), (k - 0.70f) / 0.30f);
    }

    /// Fast out of the gate, settling at the end — a detonation, not a balloon inflating.
    static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    public void ClearAll()
    {
        foreach (var p in puffs) if (p.tr != null) { p.tr.gameObject.SetActive(false); pool.Push(p.tr); }
        puffs.Clear();
    }

    Transform Rent()
    {
        if (pool.Count > 0)
        {
            var t = pool.Pop();
            t.gameObject.SetActive(true);
            return t;
        }
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Puff";
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        go.transform.SetParent(transform, false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(puffMat);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    /// A soft round falloff. Squared rather than linear so the centre stays hot while the edge goes
    /// thin — a linear falloff reads as a flat disc with a hard rim.
    static Texture2D PuffTexture()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                float a = 1f - d;
                a = a * a;
                px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(a * 255f, 0f, 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
