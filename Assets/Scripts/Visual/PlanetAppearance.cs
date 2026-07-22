using UnityEngine;

// Gives a 3D body the look of its own surface: wraps the generated surface texture around the
// sphere (so the globe matches the map view exactly) and adds a sensible atmosphere shell for
// bodies that should have one. Airless worlds (moons, asteroids, barren rock) get no atmosphere.
public static class PlanetAppearance
{
    // Updates ONLY the surface texture (no atmosphere churn) — used for live terrain editing and, now,
    // for every terraforming morph tick (so the orbiting 3D model reflects terraforming exactly as the
    // 2D map does — both go through SurfaceTextureRenderer/SampleNormalized on body.terrainParams).
    public static void RefreshTexture(CelestialBody body, GameObject go)
    {
        if (go == null) return;
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        // Read the GRID tiles (BuildGrid) rather than re-sampling the noise (Build): the grid carries the
        // neighbour-aware clean-up — connected oceans, inland lakes, coastal beaches — that per-pixel
        // sampling can't reproduce, so the 3D globe now shows exactly the map the Planet View does. Falls
        // back to the noise render only if the surface grid somehow isn't built yet.
        Texture2D tex = SurfaceTextureRenderer.BuildGrid(body) ?? SurfaceTextureRenderer.Build(body);
        if (tex == null) return;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;   // smooth the globe so it isn't blocky up close (the flat map keeps its own crisp texture)
        var m = rend.material;
        // Build() allocates a fresh Texture2D every call, and terraforming refreshes the globe ~once a
        // second for minutes — so the previous surface texture has to be released or it leaks steadily
        // (the 2D map path already destroys its old texture; this one didn't). The old texture is always
        // one we made here on a prior call (or null on first apply), never a shared asset.
        var old = m.mainTexture as Texture2D;
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (old != null && old != tex) Object.Destroy(old);
    }

    public static void Apply(CelestialBody body, GameObject go)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            // Grid-based (BuildGrid) so the globe matches the Planet View map exactly — same connected
            // oceans, lakes and beaches — instead of a noise render that skips the grid clean-up.
            Texture2D tex = SurfaceTextureRenderer.BuildGrid(body) ?? SurfaceTextureRenderer.Build(body);
            if (tex == null) return;
            tex.wrapMode = TextureWrapMode.Repeat; // wraps cleanly around longitude
            tex.filterMode = FilterMode.Bilinear;  // smooth on the sphere (the 2D map keeps its own crisp point-filtered texture)
            var m = rend.material;
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.color = Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", body.type == CelestialBodyType.OceanPlanet ? 0.5f : 0.15f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

            // Volcanic worlds glow faintly.
            if (body.type == CelestialBodyType.VolcanicPlanet && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.6f, 0.15f, 0.05f));
            }

            // Optionally layer in a dropped-in CC0 detail pack (no-op if none present).
            AssetIntegration.ApplyPlanetDetail(m, body.type);
        }

        AddAtmosphere(body, go);
        AddClouds(body, go);

        // RE-ASSERT CONCEALMENT LAST.
        //
        // The two calls above DESTROY and REBUILD the atmosphere and cloud shells, and the new ones
        // arrive with their renderers enabled — so a world that is hidden, cloaked or undiscovered would
        // sprout a glowing haze and a cloud ball the next time anything re-applied its appearance. That
        // happens more often than it sounds: RefreshFog runs on every Dev Mode toggle, BodyFog runs when
        // a survey completes, and UnitManager runs it when a probe reveals a world. Cheap and idempotent
        // on a visible body — ConcealBinding.Set returns immediately when there is nothing to conceal.
        VisibilityService.Apply(body);
    }

    // A slowly-drifting cloud shell for worlds with real air, sat just above the surface and below the
    // atmosphere haze. Deterministic (seeded from the world's terrain seed) so it's stable across
    // re-renders and reloads, and it rotates a touch faster than the planet so the weather visibly moves.
    /// Public for the same reason as AddAtmosphere: the preview and the real world must agree.
    public static void AddClouds(CelestialBody body, GameObject go)
    {
        var existing = go.transform.Find("Clouds");
        if (existing != null)
        {
            // Free the cloud texture + material too (destroying the GameObject alone would orphan them).
            var oldMr = existing.GetComponent<MeshRenderer>();
            if (oldMr != null && oldMr.sharedMaterial != null)
            {
                if (oldMr.sharedMaterial.mainTexture != null) Object.Destroy(oldMr.sharedMaterial.mainTexture);
                Object.Destroy(oldMr.sharedMaterial);
            }
            Object.Destroy(existing.gameObject);
        }

        // Airless bodies get none; a gas giant's whole surface is already cloud; a world with almost no air
        // stays clear.
        if (body.type == CelestialBodyType.GasGiant) return;
        if (body.atmosphereThickness < 0.14f) return;

        var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "Clouds";
        var col = shell.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        shell.transform.SetParent(go.transform, false);
        shell.transform.localScale = Vector3.one * 1.03f;   // hugs the surface, under the atmosphere shell

        var mr = shell.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));   // reliable unlit transparent
        // Volcanic worlds get a dirty ash-grey haze; everything else, white water cloud.
        mat.color = body.type == CelestialBodyType.VolcanicPlanet
            ? new Color(0.55f, 0.5f, 0.48f, 1f)
            : Color.white;
        mat.mainTexture = CloudTexture(body);
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        shell.AddComponent<SelfSpin>().speed = 3.5f;   // gentle weather drift on top of the planet's own spin
    }

    // A wrapping cloud texture: soft FBm noise as white with a varying alpha, thresholded so there are real
    // gaps of clear sky. Thicker air -> more coverage. Sized modestly (clouds are soft, so they don't need
    // the surface's resolution) and bilinear so they read as soft weather rather than pixels.
    static Texture2D CloudTexture(CelestialBody body)
    {
        const int w = 128, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };
        float seed = body.terrainSeed * 0.137f + 13f;
        float coverage = Mathf.Clamp01(body.atmosphereThickness) * 0.55f + 0.12f;   // 0.12..0.67
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w, vv = (float)y / h;
                // Two octaves of Perlin, stretched 2:1 like the surface so bands read east-west.
                float n = CloudFBm(u * 5f + seed, vv * 2.5f + seed);
                // Fade cloud out toward the poles a little so caps aren't permanently overcast.
                float polar = 1f - Mathf.Pow(Mathf.Abs(vv - 0.5f) * 2f, 3f) * 0.35f;
                float a = Mathf.Clamp01((n - (1f - coverage)) * 3.2f) * polar;
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 205f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static float CloudFBm(float x, float y)
    {
        float a = Mathf.PerlinNoise(x, y);
        float b = Mathf.PerlinNoise(x * 2.3f + 40f, y * 2.3f + 40f) * 0.5f;
        float c = Mathf.PerlinNoise(x * 4.7f + 90f, y * 4.7f + 90f) * 0.25f;
        return (a + b + c) / 1.75f;
    }

    static bool HasAtmosphere(CelestialBodyType t, out Color color, out float thickness)
    {
        switch (t)
        {
            case CelestialBodyType.OceanPlanet:   color = new Color(0.40f, 0.62f, 1.00f, 0.28f); thickness = 1.10f; return true;
            case CelestialBodyType.RockyPlanet:   color = new Color(0.55f, 0.72f, 1.00f, 0.20f); thickness = 1.08f; return true;
            case CelestialBodyType.IcePlanet:     color = new Color(0.65f, 0.88f, 1.00f, 0.18f); thickness = 1.07f; return true;
            case CelestialBodyType.VolcanicPlanet:color = new Color(1.00f, 0.45f, 0.22f, 0.20f); thickness = 1.08f; return true;
            case CelestialBodyType.GasGiant:      color = new Color(0.92f, 0.78f, 0.52f, 0.34f); thickness = 1.14f; return true;
            default: color = Color.clear; thickness = 1f; return false; // Moon / Asteroid / Barren: airless
        }
    }

    /// Public so the loading screen can give its preview sphere the SAME atmosphere the real body
    /// gets — the transition is only seamless if both are the same shell built by the same rule.
    public static void AddAtmosphere(CelestialBody body, GameObject go)
    {
        // Remove any previous atmosphere (e.g. when re-applying).
        var existing = go.transform.Find("Atmosphere");
        if (existing != null) Object.Destroy(existing.gameObject);

        if (!HasAtmosphere(body.type, out Color color, out float thickness)) return;

        // Honor the body's actual atmosphereThickness attribute, not just its type. A small moon can now
        // roll an ice/volcanic/rocky SURFACE (see RollMoonType) while its mass holds essentially no air
        // (AtmosphereRules.ForMoon) — such a moon should look airless, so skip the shell when the attribute
        // says there's nothing there. Planets always sit well above this floor, so they're unaffected.
        if (body.atmosphereThickness < 0.06f) return;

        var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "Atmosphere";
        var col = shell.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        shell.transform.SetParent(go.transform, false);
        shell.transform.localScale = Vector3.one * thickness;

        var mr = shell.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default")); // reliable unlit transparent
        mat.color = color;
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }
}
