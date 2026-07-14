using UnityEngine;

// Gives a 3D body the look of its own surface: wraps the generated surface texture around the
// sphere (so the globe matches the map view exactly) and adds a sensible atmosphere shell for
// bodies that should have one. Airless worlds (moons, asteroids, barren rock) get no atmosphere.
public static class PlanetAppearance
{
    // Updates ONLY the surface texture (no atmosphere churn) — used for live terrain editing.
    public static void RefreshTexture(CelestialBody body, GameObject go)
    {
        if (go == null) return;
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        Texture2D tex = SurfaceTextureRenderer.Build(body);
        tex.wrapMode = TextureWrapMode.Repeat;
        var m = rend.material;
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
    }

    public static void Apply(CelestialBody body, GameObject go)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            Texture2D tex = SurfaceTextureRenderer.Build(body);
            tex.wrapMode = TextureWrapMode.Repeat; // wraps cleanly around longitude
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

    static void AddAtmosphere(CelestialBody body, GameObject go)
    {
        // Remove any previous atmosphere (e.g. when re-applying).
        var existing = go.transform.Find("Atmosphere");
        if (existing != null) Object.Destroy(existing.gameObject);

        if (!HasAtmosphere(body.type, out Color color, out float thickness)) return;

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
