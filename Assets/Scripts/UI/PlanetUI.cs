using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class PlanetUI : MonoBehaviour
{
    public static PlanetUI Instance;

    [Header("Info Panel (Bottom Left)")]
    public GameObject infoPanel;
    public TMP_Text titleText;
    public TMP_Text infoText;

    [Header("Grid Window")]
    public GameObject gridWindow;
    public PlanetGridVisualizer gridVisualizer;
    public Button gridCloseButton;

    private bool justOpened = false;
    private CelestialBody currentBody;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (infoPanel != null) infoPanel.SetActive(false);
        if (gridWindow != null) gridWindow.SetActive(false);

        if (gridCloseButton != null)
            gridCloseButton.onClick.AddListener(CloseGridOnly);
    }

    void Update()
    {
        if (!infoPanel.activeSelf) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseAll();
            return;
        }

        if (Input.GetMouseButtonDown(0) && !justOpened)
        {
            if (!EventSystem.current.IsPointerOverGameObject())
            {
                CloseAll();
            }
        }

        if (justOpened) justOpened = false;
    }

    public void Show(CelestialBody body)
    {
        if (body == null) return;

        currentBody = body;

        Debug.Log("=== Showing Planet UI for " + body.type + " ===");

        titleText.text = body.name ?? body.type.ToString();

        float distance = body.visualObject != null
            ? Vector3.Distance(body.visualObject.transform.position, Vector3.zero)
            : 0f;

        infoText.text =
            $"Type: {body.type}\n" +
            $"Surface Size: {body.surfaceSize}x{body.surfaceSize}\n" +
            $"Distance from Star: {distance:F1} units";

        // Show Grid in separate window
        if (gridVisualizer != null && gridWindow != null)
        {
            gridVisualizer.ShowSurface(body.surface);
            gridWindow.SetActive(true);
        }

        infoPanel.SetActive(true);

        // Show the Terrain Editor panel for this planet
        if (TerrainEditorPanel.Instance != null)
        {
            TerrainEditorPanel.Instance.ShowForPlanet(body);
        }

        justOpened = true;

        Debug.Log("Info Panel and Grid Window opened.");

        if (SandboxEditorPanel.Instance != null)
            SandboxEditorPanel.Instance.ShowForBody(body);
    }

    public void CloseGridOnly()
    {
        if (gridWindow != null) gridWindow.SetActive(false);

        if (TerrainEditorPanel.Instance != null)
            TerrainEditorPanel.Instance.Hide();

        Debug.Log("Grid Window Closed");
    }

    public void CloseAll()
    {
        if (infoPanel != null) infoPanel.SetActive(false);
        if (gridWindow != null) gridWindow.SetActive(false);

        // Hide the Terrain Editor when closing the planet UI
        if (TerrainEditorPanel.Instance != null)
            TerrainEditorPanel.Instance.Hide();

        if (SandboxEditorPanel.Instance != null)
            SandboxEditorPanel.Instance.Hide();

        Debug.Log("All Planet UI Closed");
    }

    public void SelectTile(TerrainTile tile, SurfaceTileUI ui)
    {
        ShowTileInfo(tile);
    }

    public void ShowTileInfo(TerrainTile tile)
    {
        if (infoText != null)
        {
            infoText.text =
                $"Terrain: {tile.type}\n" +
                $"Occupied: {tile.occupied}";
        }
    }
}