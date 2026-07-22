using UnityEngine;

// ============================================================================================
// THE REAL WORLD, FORMING
//
// Drives an actual CelestialBody's own material through TerrainDevelopment's stages. Attached to the
// body's visualObject, so the thing developing on screen IS the world the player is about to be handed
// — not a stand-in that has to be matched to it afterwards.
//
// That is the whole difference from what the loading screen used to do. A private preview stage could
// be made to LOOK like the homeworld, but it could never BE it: its moons were at invented radii, its
// light came from a fixed key rather than from the world's own star, and the handover was a cross-fade
// between two different objects. Morphing the real body removes the seam by removing the second object.
//
// Self-contained and self-destructing: it owns its texture, ticks itself, and on completion hands the
// body back its real surface and removes itself. Nothing outside has to remember to clean it up, which
// matters because the sequence can be skipped or abandoned partway through.
// ============================================================================================
[DisallowMultipleComponent]
public class TerrainMorph : MonoBehaviour
{
    CelestialBody body;
    Renderer rend;

    Texture2D tex;
    Color[] pixels;
    Color[][] stages;
    float[] jitter;

    int w, h;
    float clock, duration;
    bool running;

    /// Has the world finished forming?
    public bool Done => !running;

    /// Start a world developing on screen. Returns null if the body has nothing to develop into yet —
    /// its surface is generated before this is ever called, so that means something upstream is wrong
    /// rather than that we should invent a substitute.
    public static TerrainMorph Begin(CelestialBody b, float seconds, int width, int height, int jitterSeed)
    {
        if (b?.visualObject == null || b.surface == null) return null;

        var m = b.visualObject.GetComponent<TerrainMorph>();
        if (m == null) m = b.visualObject.AddComponent<TerrainMorph>();

        // Already primed for this body? Just start its clock — re-initialising would rebuild the stages
        // and throw away the texture the world is currently wearing, which is a visible flash.
        if (m.primed && m.body == b) { m.duration = Mathf.Max(0.1f, seconds); m.clock = 0f; m.running = true; return m; }

        m.Init(b, seconds, width, height, jitterSeed);
        return m;
    }

    /// Put the world into its PRIMORDIAL state without starting it developing.
    ///
    /// THE REASON THIS EXISTS. A body's visual is built wearing its REAL finished surface — the
    /// homeworld and its moons are player-owned and therefore Surveyed, so SystemVisualizer applies the
    /// full appearance immediately. Reveal one and then start its morph a few seconds later, and the
    /// player watches a completed world sit there and then VISIBLY REVERT to a featureless orb before
    /// re-forming. The moons were worse: revealed finished, then popping back to primordial one at a
    /// time, up to thirteen seconds later.
    ///
    /// So a world is primed before it is revealed, and revealed already looking like the beginning of
    /// itself.
    public static TerrainMorph Prime(CelestialBody b, int width, int height, int jitterSeed)
    {
        if (b?.visualObject == null || b.surface == null) return null;

        var m = b.visualObject.GetComponent<TerrainMorph>();
        if (m == null) m = b.visualObject.AddComponent<TerrainMorph>();
        m.Init(b, 1f, width, height, jitterSeed);
        m.running = false;      // stage 0 is on the material; the clock does not start until Begin
        m.primed = true;
        return m;
    }

    bool primed;

    void Init(CelestialBody b, float seconds, int width, int height, int jitterSeed)
    {
        body = b;
        rend = GetComponent<Renderer>();
        if (rend == null) { running = false; return; }

        w = Mathf.Max(8, width);
        h = Mathf.Max(4, height);
        duration = Mathf.Max(0.1f, seconds);
        clock = 0f;

        tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            // Point filtering, deliberately: the whole point is that the player sees TILES resolving.
            // Bilinear would smooth them into a soft gradient and the mechanic would be invisible.
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat   // wraps cleanly around the sphere's longitude
        };

        pixels = new Color[w * h];
        jitter = TerrainDevelopment.BuildJitter(w * h, jitterSeed);
        stages = TerrainDevelopment.NewStages(body, w, h);

        TerrainDevelopment.Paint(stages, jitter, pixels, 0f);
        tex.SetPixels(pixels);
        tex.Apply();

        // Straight onto the material the body is already wearing. `rend.material` instantiates this
        // renderer's own instance, which is what we want — this is one world's transient state, not
        // something to share.
        var mat = rend.material;
        mat.mainTexture = tex;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        // White, so the placeholder tint of an unsurveyed world cannot multiply into the terrain colours.
        mat.color = Color.white;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

        running = true;
    }

    void Update()
    {
        if (!running) return;

        // UNSCALED. Generation runs with the clock stopped, and a world that only formed while the
        // simulation was running would sit frozen for the whole load.
        float dt = Time.unscaledDeltaTime;

        // One stage per frame, well ahead of what is being shown — a stage is on screen for roughly
        // duration/7, and this builds one every frame.
        TerrainDevelopment.BuildNext(body, w, h, stages);

        clock += dt;
        float t = Mathf.Clamp01(clock / duration);

        TerrainDevelopment.Paint(stages, jitter, pixels, t);
        tex.SetPixels(pixels);
        tex.Apply();

        if (t >= 1f) Finish();
    }

    /// Hand the world its real surface and get out of the way.
    ///
    /// The morph runs on a small point-filtered grid so it costs the same on a moon and a gas giant; the
    /// real globe is the full surface grid, hundreds of texels wide and bilinear-filtered. Ending on the
    /// morph texture would leave the player looking at a coarse mosaic of the world they just watched
    /// form, and the first thing that re-applied the world's appearance would silently swap it.
    public void Finish()
    {
        running = false;

        // RefreshTexture destroys whatever texture is on the material before installing the real one —
        // which is exactly our morph texture, so this frees it for us. Set to null first regardless, so
        // a second call can never double-destroy.
        var doomed = tex;
        tex = null;
        if (body != null && rend != null) PlanetAppearance.RefreshTexture(body, gameObject);
        if (doomed != null) Destroy(doomed);

        stages = null;
        pixels = null;
        jitter = null;

        Destroy(this);
    }

    void OnDestroy()
    {
        // Abandoned partway — the sequence was skipped, or the galaxy was rebuilt underneath us. The
        // texture is ours alone, so nothing else will free it.
        if (tex != null) { Destroy(tex); tex = null; }
    }
}
