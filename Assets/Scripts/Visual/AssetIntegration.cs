using UnityEngine;

// Optional hook for free CC0 art packs. Drop textures into Assets/Resources/SpaceAssets/ (see
// SPACE_ASSETS_SETUP.md) and the game auto-detects and uses them — with no code changes and a clean
// fallback to the procedural look when nothing is present.
//
// IMPORTANT: planet *albedo* stays procedural on purpose (so the 3D globe keeps matching the map
// view). External packs are layered as DETAIL maps only, which enrich the surface without breaking
// that match. Skyboxes/backdrops are opt-in via SpaceBackground.
public static class AssetIntegration
{
    // Applies an optional detail albedo / normal for a body type, if the dev has supplied one at
    // Resources/SpaceAssets/Detail/{Type} and /{Type}_normal.
    public static void ApplyPlanetDetail(Material m, CelestialBodyType type)
    {
        if (m == null) return;

        var detail = Resources.Load<Texture2D>($"SpaceAssets/Detail/{type}");
        if (detail != null && m.HasProperty("_DetailAlbedoMap"))
        {
            m.SetTexture("_DetailAlbedoMap", detail);
            m.EnableKeyword("_DETAIL_MULX2");
        }

        var normal = Resources.Load<Texture2D>($"SpaceAssets/Detail/{type}_normal");
        if (normal != null && m.HasProperty("_DetailNormalMap"))
        {
            m.SetTexture("_DetailNormalMap", normal);
            m.EnableKeyword("_DETAIL_MULX2");
        }
    }

    // Optional skybox material at Resources/SpaceAssets/Skybox (a Material asset). Null if absent.
    public static Material LoadSkybox() => Resources.Load<Material>("SpaceAssets/Skybox");
}
