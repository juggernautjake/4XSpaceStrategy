using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// One tile in the low-res grid viewer. Colour comes from TerrainColorMap (single source of truth)
// modulated by the tile's per-tile shade for pixel detail. Hovering shows the terrain tooltip;
// clicking an ore tile discovers that ore.
public class SurfaceTileUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TerrainTile tileData;
    public Image image;

    Color baseColor;
    GameObject oreMarker;

    public void Init(TerrainTile tile)
    {
        tileData = tile;
        if (image == null) image = GetComponent<Image>();

        Color c = TerrainColorMap.Get(tile.type);
        float b = Mathf.Lerp(0.82f, 1.15f, tile.shade); // subtle per-tile brightness
        baseColor = new Color(c.r * b, c.g * b, c.b * b, 1f);
        image.color = baseColor;

        RefreshOreMarker();
    }

    void RefreshOreMarker()
    {
        if (tileData != null && tileData.HasOre)
        {
            if (oreMarker == null)
            {
                oreMarker = new GameObject("Ore", typeof(RectTransform));
                oreMarker.transform.SetParent(transform, false);
                var mi = oreMarker.AddComponent<Image>();
                mi.raycastTarget = false;
                var rt = mi.rectTransform;
                rt.anchorMin = new Vector2(0.25f, 0.25f);
                rt.anchorMax = new Vector2(0.75f, 0.75f);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
            oreMarker.GetComponent<Image>().color = OreDatabase.Get(tileData.ore).color;
            oreMarker.SetActive(true);
        }
        else if (oreMarker != null)
        {
            oreMarker.SetActive(false);
        }
    }

    public void OnClick()
    {
        if (tileData != null && tileData.HasOre)
            ResearchManager.Discover(tileData.ore);

        if (PlanetUI.Instance != null)
            PlanetUI.Instance.SelectTile(tileData, this);
    }

    public void SetHighlight(bool state)
    {
        image.color = state ? new Color(1f, 0.92f, 0.4f) : baseColor;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (PlanetUI.Instance != null) PlanetUI.Instance.ShowTerrainHover(tileData);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (PlanetUI.Instance != null) PlanetUI.Instance.HideTerrainHover();
    }
}
