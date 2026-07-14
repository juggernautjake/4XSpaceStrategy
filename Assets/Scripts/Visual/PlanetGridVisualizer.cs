using UnityEngine;
using UnityEngine.UI;

// The low-res "general" surface viewer: a chunky tile grid. Colours come from SurfaceTileUI/
// TerrainColorMap (no more duplicated colour logic that caused magenta tiles). Tile size auto-fits
// so even large gas giants stay a manageable on-screen size.
public class PlanetGridVisualizer : MonoBehaviour
{
    public GameObject tilePrefab;
    public float tileSize = 24f;       // preferred; auto-reduced to fit large maps
    public float maxWindowWidth = 760f;
    public float maxWindowHeight = 420f;
    public GameObject gridWindow;

    PlanetSurface surface;
    SurfaceTileUI selected;
    float currentTileSize;

    public void ShowSurface(PlanetSurface surface)
    {
        if (surface == null) return;
        this.surface = surface;
        ClearGrid();
        BuildGrid();
        ResizeWindow();
    }

    void BuildGrid()
    {
        // Fit the whole map inside the viewer bounds.
        currentTileSize = Mathf.Clamp(
            Mathf.Min(tileSize, maxWindowWidth / surface.width, maxWindowHeight / surface.height),
            4f, tileSize);

        GridLayoutGroup grid = GetComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = surface.width;
        grid.spacing = Vector2.zero;
        grid.cellSize = new Vector2(currentTileSize, currentTileSize);

        // Rows top-to-bottom so the map reads north-up.
        for (int y = surface.height - 1; y >= 0; y--)
        {
            for (int x = 0; x < surface.width; x++)
            {
                GameObject tileObj = Instantiate(tilePrefab, transform);
                var tileUI = tileObj.GetComponent<SurfaceTileUI>();
                tileUI.Init(surface.tiles[x, y]);

                var btn = tileObj.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(tileUI.OnClick);
            }
        }
    }

    void ResizeWindow()
    {
        if (gridWindow == null) return;
        var gridRect = gridWindow.GetComponent<RectTransform>();
        if (gridRect == null) return;
        float w = surface.width * currentTileSize + 20f;
        float h = surface.height * currentTileSize + 40f;
        gridRect.sizeDelta = new Vector2(w, h);
    }

    public void SelectTile(TerrainTile tile, SurfaceTileUI ui)
    {
        if (selected != null) selected.SetHighlight(false);
        selected = ui;
        if (selected != null) selected.SetHighlight(true);
        if (PlanetUI.Instance != null) PlanetUI.Instance.ShowTileInfo(tile);
    }

    void ClearGrid()
    {
        selected = null;
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }
}
