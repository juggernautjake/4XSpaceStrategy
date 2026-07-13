using UnityEngine;
using UnityEngine.UI;

public class PlanetGridVisualizer : MonoBehaviour
{
    public GameObject tilePrefab;
    public float tileSize = 32f;
    public GameObject gridWindow; // Assign in Inspector (the Grid Window GameObject)

    PlanetSurface surface;
    SurfaceTileUI selected;

    public void ShowSurface(PlanetSurface surface)
    {
        this.surface = surface;
        ClearGrid();
        BuildGrid();

        // Dynamic sizing for the grid container
        if (gridWindow != null)
        {
            RectTransform gridRect = gridWindow.GetComponent<RectTransform>();
            if (gridRect != null)
            {
                GridLayoutGroup gridLayout = GetComponent<GridLayoutGroup>();
                if (gridLayout != null)
                {
                    float horizontalPadding = 20f;
                    float verticalPadding = 20f;
                    float totalWidth = surface.width * tileSize +
                                      gridLayout.spacing.x * (surface.width - 1) +
                                      horizontalPadding;
                    float totalHeight = surface.height * tileSize +
                                       gridLayout.spacing.y * (surface.height - 1) +
                                       verticalPadding;
                    gridRect.sizeDelta = new Vector2(totalWidth, totalHeight);
                    Debug.Log($"Set GridWindow size to {totalWidth:F0}x{totalHeight:F0} for {surface.width}x{surface.height} grid");
                }
            }
        }

    }

    void BuildGrid()
    {
        GridLayoutGroup grid = GetComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = surface.width;
        grid.spacing = Vector2.zero; // No gaps
        grid.cellSize = new Vector2(tileSize, tileSize); // Ensure exact size

        for (int y = surface.height - 1; y >= 0; y--)
        {
            for (int x = 0; x < surface.width; x++)
            {
                GameObject tileObj = Instantiate(tilePrefab, transform);

                Image img = tileObj.GetComponent<Image>();
                img.color = GetColor(surface.tiles[x, y].type);

                SurfaceTileUI tileUI = tileObj.GetComponent<SurfaceTileUI>();
                tileUI.Init(surface.tiles[x, y]);

                Button btn = tileObj.GetComponent<Button>();
                btn.onClick.AddListener(tileUI.OnClick);
            }
        }
    }

    public void SelectTile(TerrainTile tile, SurfaceTileUI ui)
    {
        if (selected != null)
            selected.SetHighlight(false);

        selected = ui;
        selected.SetHighlight(true);

        PlanetUI.Instance.ShowTileInfo(tile);
    }

    void ClearGrid()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }

    Color GetColor(TerrainType type)
    {
        switch (type)
        {
            case TerrainType.Plains: return Color.green;
            case TerrainType.Mountains: return Color.gray;
            case TerrainType.Forest: return new Color(0f, 0.6f, 0f);
            case TerrainType.Ocean: return Color.blue;
            case TerrainType.Ice: return Color.cyan;
            case TerrainType.Volcano: return new Color(1f, 0.3f, 0.1f);
            case TerrainType.MagmaField: return new Color(1f, 0.4f, 0.1f); // Bright orange-red for lava/magma
            case TerrainType.Desert: return new Color(1f, 0.9f, 0.5f);
            case TerrainType.Crater: return new Color(0.5f, 0.5f, 0.5f);
        }
        return type == TerrainType.MagmaField
    ? new Color(1f, 0.4f, 0.1f)
    : Color.magenta;
    }
}