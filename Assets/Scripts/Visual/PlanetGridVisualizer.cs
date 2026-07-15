using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// The low-res "general" surface viewer: a chunky tile grid. Colours come from SurfaceTileUI/
// TerrainColorMap. When a planet is selected you can walk a tile cursor with WASD/arrows (which is
// why the camera stops panning while a planet is selected).
public class PlanetGridVisualizer : MonoBehaviour
{
    public GameObject tilePrefab;
    public float tileSize = 26f;       // preferred; auto-reduced to fit large maps
    public float maxWindowWidth = 900f;
    public float maxWindowHeight = 460f;
    public float minTileSize = 7f;     // small bodies keep chunky, readable tiles
    public GameObject gridWindow;

    PlanetSurface surface;
    SurfaceTileUI selected;
    SurfaceTileUI[,] tileUIs;
    int cursorX, cursorY;
    float currentTileSize;

    public void ShowSurface(PlanetSurface surface)
    {
        if (surface == null) return;
        this.surface = surface;
        ClearGrid();
        BuildGrid();
        ResizeWindow();
        cursorX = surface.width / 2;
        cursorY = surface.height / 2;
    }

    /// Most cells this viewer will ever turn into GameObjects before giving up. See BuildGrid.
    const int MaxTileObjects = 4000;

    void BuildGrid()
    {
        // This spawns ONE GAMEOBJECT PER CELL, and a cell is now a detail texel rather than a chunky
        // block — a grid can be 240x120, so this would be nearly 30,000 GameObjects in a
        // GridLayoutGroup. That is a frozen frame, not a map.
        //
        // Nothing calls this today (PlanetUI.gridVisualizer is an Inspector-only field that is never
        // assigned, and the chunky mini map it drew was retired in favour of the Planet View). The guard
        // is here because "nothing calls it" is one Inspector drag away from being false, and the
        // failure mode is a hang rather than anything that would point at this file. Every map that IS
        // live renders to a Texture2D, which doesn't care how many cells there are.
        if (surface.width * surface.height > MaxTileObjects)
        {
            Debug.LogWarning($"[PlanetGridVisualizer] Refusing to build a {surface.width}x{surface.height} " +
                             $"grid as {surface.width * surface.height} GameObjects. Use the Planet View " +
                             $"(texture-based) instead — this viewer is retired.");
            return;
        }

        // Shared metric so the detailed map stays a consistent multiple of this mini map.
        currentTileSize = MapMetrics.MiniTile(surface.height);

        GridLayoutGroup grid = GetComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = surface.width;
        grid.spacing = Vector2.zero;
        grid.cellSize = new Vector2(currentTileSize, currentTileSize);

        tileUIs = new SurfaceTileUI[surface.width, surface.height];

        for (int y = surface.height - 1; y >= 0; y--)
        {
            for (int x = 0; x < surface.width; x++)
            {
                GameObject tileObj = Instantiate(tilePrefab, transform);
                var tileUI = tileObj.GetComponent<SurfaceTileUI>();
                tileUI.Init(surface.tiles[x, y]);
                tileUIs[x, y] = tileUI;

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

    // WASD / arrow keys move a tile cursor while a planet is selected and the grid is open.
    void Update()
    {
        if (surface == null || tileUIs == null) return;
        if (gridWindow == null || !gridWindow.activeSelf) return;
        if (PlanetUI.Selected == null) return;
        if (EscapeMenu.Instance != null && EscapeMenu.Instance.IsOpen) return;
        if (IsTypingInField()) return;

        int dx = 0, dy = 0;
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) dx = -1;
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) dx = 1;
        else if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) dy = 1;   // north = higher y
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) dy = -1;
        if (dx == 0 && dy == 0) return;

        cursorX = Mathf.Clamp(cursorX + dx, 0, surface.width - 1);
        cursorY = Mathf.Clamp(cursorY + dy, 0, surface.height - 1);
        var ui = tileUIs[cursorX, cursorY];
        if (ui != null) SelectTile(ui.tileData, ui);
    }

    static bool IsTypingInField()
    {
        var es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null) return false;
        var input = es.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
        return input != null && input.isFocused;
    }

    void ClearGrid()
    {
        selected = null;
        tileUIs = null;
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }
}
