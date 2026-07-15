using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Scene-wired hub for the selected body: bottom-left info panel + the low-res grid window.
// Also broadcasts selection so the runtime tools (orbit controls, detailed map) can react, and
// drives the terrain hover tooltip above the viewer.
public class PlanetUI : MonoBehaviour
{
    public static PlanetUI Instance;

    // Broadcast so runtime-built tools can follow the current selection without scene wiring.
    public static event Action<CelestialBody> OnBodySelected;
    public static event Action OnClosed;
    public static CelestialBody Selected { get; private set; }

    [Header("Info Panel (Bottom Left)")]
    public GameObject infoPanel;
    public TMP_Text titleText;
    public TMP_Text infoText;

    [Header("Grid Window")]
    public GameObject gridWindow;
    public PlanetGridVisualizer gridVisualizer;
    public Button gridCloseButton;

    bool justOpened;

    void Awake() { Instance = this; }

    void Start()
    {
        if (infoPanel != null) infoPanel.SetActive(false);
        if (gridWindow != null) gridWindow.SetActive(false);
        if (gridCloseButton != null) gridCloseButton.onClick.AddListener(CloseGridOnly);

        // Live-refresh the open info panel when the viewing species or game mode changes.
        SpeciesManager.OnSpeciesChanged += RefreshInfo;
        GameMode.OnChanged += RefreshInfo;

        RestyleSceneUI();
    }

    // Brings the old scene-wired info panel + grid window in line with the runtime UI theme.
    void RestyleSceneUI()
    {
        if (infoPanel != null)
        {
            var img = infoPanel.GetComponent<Image>();
            if (img != null) img.color = UITheme.PanelBg;
            if (infoPanel.GetComponent<Outline>() == null)
            {
                var o = infoPanel.AddComponent<Outline>();
                o.effectColor = UITheme.AccentDim; o.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }
        if (titleText != null)
        {
            titleText.color = UITheme.Accent; titleText.fontStyle = FontStyles.Bold;
            if (titleText.fontSize < 18) titleText.fontSize = 18;
        }
        if (infoText != null)
        {
            infoText.color = UITheme.Text; infoText.richText = true;
            if (infoText.fontSize < 13) infoText.fontSize = 13;
        }
        if (gridWindow != null)
        {
            var gimg = gridWindow.GetComponent<Image>();
            if (gimg != null) gimg.color = UITheme.PanelBg;
            if (gridWindow.GetComponent<Outline>() == null)
            {
                var o = gridWindow.AddComponent<Outline>();
                o.effectColor = UITheme.AccentDim;
            }
        }
    }

    void OnDestroy()
    {
        SpeciesManager.OnSpeciesChanged -= RefreshInfo;
        GameMode.OnChanged -= RefreshInfo;
    }

    void RefreshInfo()
    {
        if (Selected != null && infoPanel != null && infoPanel.activeSelf && infoText != null)
            infoText.text = BuildBodyInfo(Selected);
    }

    void Update()
    {
        if (infoPanel == null || !infoPanel.activeSelf) return;

        // Esc is handled by EscapeMenu (pause menu); planet UI closes via click-outside or the X button.
        if (Input.GetMouseButtonDown(0) && !justOpened && EventSystem.current != null &&
            !EventSystem.current.IsPointerOverGameObject())
        {
            CloseAll();
        }
        if (justOpened) justOpened = false;
    }

    public void Show(CelestialBody body)
    {
        if (body == null) return;
        Selected = body;

        // Clear any lingering UI focus so WASD drives the mini-map tile cursor, not a stray button.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        if (titleText != null) titleText.text = body.name ?? body.type.ToString();
        if (infoText != null) infoText.text = BuildBodyInfo(body);

        // The low-res mini map is retired. It showed the same continents as the detailed map at a
        // quarter of the fidelity, and everything that used to need it — surveying, building, the index
        // overlays — now lives on the detailed grid in the Planet View. Keeping both only split the
        // player's attention between two views of one world.
        if (gridWindow != null) gridWindow.SetActive(false);

        if (infoPanel != null) infoPanel.SetActive(true);

        justOpened = true;
        SimpleAudio.Instance?.PlaySelect();

        // Zoom in on the selection (smaller bodies zoom closer) and float its labels.
        if (CameraController.Instance != null && body.visualObject != null)
            CameraController.Instance.FocusAndZoom(body.visualObject.transform, body.surfaceSize, CameraController.AutoFollow);
        ObjectLabelManager.Instance?.ShowForBody(body);
        BodyUnitsPanel.Instance?.ShowFor(body);   // ships present here, as selectable icons

        // Runtime tools (orbit + terrain panels) react to this.
        OnBodySelected?.Invoke(body);
    }

    string BuildBodyInfo(CelestialBody body)
    {
        var star = body.hostStar != null ? body.hostStar
            : (GameManager.Instance != null ? GameManager.Instance.CurrentStar : null);
        bool isMoon = body.parentBody != null;
        float primaryMass = isMoon
            ? Mathf.Max(0.2f, OrbitalMechanics.BodyMass(body.parentBody))
            : (star != null ? star.mass : 1f);

        float period = OrbitalMechanics.PeriodSeconds(body.orbitSpeed);
        float vel = OrbitalMechanics.OrbitalVelocity(primaryMass, body.orbitRadius);

        var sb = new StringBuilder();
        sb.AppendLine($"Type: {body.type}");
        string ownerHex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(body.owner));
        sb.AppendLine($"Owner: <color={ownerHex}>{FactionManager.OwnerLabel(body.owner)}</color>");
        sb.AppendLine($"Distance from star: {body.distanceFromStar:F1}");
        sb.AppendLine($"Orbit: r={body.orbitRadius:F1}  period={period:F1}s");

        if (!body.Surveyed)
        {
            sb.AppendLine("\n<color=#FFBF4D><b>Unexplored world</b></color>");
            sb.AppendLine("<color=#9FB4C8>Known:</color> name, type, owner, orbit and host star.");
            sb.AppendLine("<color=#FFBF4D>Send a ship to survey it to reveal its habitability, resources, ore deposits and any points of interest.</color>");
            sb.AppendLine($"Survey progress: {body.explorationProgress * 100f:F0}%");
        }
        else
        {
            sb.AppendLine($"Surface: {MapMetrics.SurfW(body.surfaceSize)}x{MapMetrics.SurfH(body.surfaceSize)}");
            string habLabel = Habitability.Label(body.habitability, body.isHabitable);
            string habColor = Habitability.ScoreColorHex(body.habitability);
            sb.AppendLine($"Habitability ({SpeciesManager.Current.name}): <color={habColor}><b>{body.habitability:F0}/100</b> ({habLabel})</color>");
            if (!body.isHabitable && body.type != CelestialBodyType.GasGiant)
            {
                string tColor = Habitability.ScoreColorHex(body.terraformability);
                string tNote = body.terraformability >= 55f ? "could be made livable" : body.terraformability >= 30f ? "hard to terraform" : "near-impossible to terraform";
                sb.AppendLine($"Terraformability ({SpeciesManager.Current.name}): <color={tColor}>{body.terraformability:F0}/100</color> — {tNote}");
            }
            if (body.moons.Count > 0) sb.AppendLine($"Moons: {body.moons.Count}");

            if (body.resources != null && body.resources.resources.Count > 0)
            {
                sb.Append("Resources: ");
                foreach (var kv in body.resources.resources) sb.Append($"{kv.Key} {kv.Value:F0}  ");
                sb.AppendLine();
            }
            var ores = OreGenerator.OresOnBody(body);
            if (ores.Count > 0) sb.AppendLine($"<color=#8FD0FF>Ore deposits: {ores.Count} type(s)</color>");
            if (body.pointsOfInterest.Count > 0) sb.AppendLine($"Points of interest: {body.pointsOfInterest.Count}");
        }

        // Colonization progress.
        if (body.claimProgress > 0f && body.owner != FactionManager.Player)
            sb.AppendLine($"<color=#4DFF6E>Colonizing: {body.claimProgress * 100f:F0}%</color>");

        // Units present.
        if (body.units != null && body.units.Count > 0)
        {
            sb.AppendLine($"\n<color=#8FD0FF>Ships here ({body.units.Count}):</color>");
            foreach (var u in body.units) sb.AppendLine($"  • {u.name} ({u.Info.name}, {u.RankName})");
        }

        return sb.ToString();
    }

    public void ShowTerrainHover(TerrainTile tile)
    {
        if (tile == null) return;
        var sb = new StringBuilder();
        sb.Append($"<b>{tile.type}</b>\n{TerrainColorMap.Describe(tile.type)}");
        if (tile.HasOre)
        {
            var info = OreDatabase.Get(tile.ore);
            bool known = ResearchManager.IsDiscovered(tile.ore);
            sb.Append(known
                ? $"\n<color=#8FD0FF>Ore: {info.displayName}</color> (click to survey)"
                : "\n<color=#8FD0FF>Unidentified ore — click to discover</color>");
        }
        var target = gridWindow != null ? gridWindow.GetComponent<RectTransform>() : null;
        TooltipManager.Instance.ShowAboveRect(target, sb.ToString());
    }

    public void HideTerrainHover() => TooltipManager.Instance.Hide();

    public void CloseGridOnly()
    {
        if (gridWindow != null) gridWindow.SetActive(false);
        if (TerrainEditorPanel.Instance != null) TerrainEditorPanel.Instance.Hide();
    }

    public void CloseAll()
    {
        if (infoPanel != null) infoPanel.SetActive(false);
        if (gridWindow != null) gridWindow.SetActive(false);
        TooltipManager.Instance.Hide();
        ObjectLabelManager.Instance?.Hide();
        BodyUnitsPanel.Instance?.ShowFor(null);
        CameraController.Instance?.ClearFocus();
        Selected = null;
        OnClosed?.Invoke();
    }

    public void SelectTile(TerrainTile tile, SurfaceTileUI ui) => ShowTileInfo(tile);

    public void ShowTileInfo(TerrainTile tile)
    {
        if (infoText == null || tile == null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"Terrain: {tile.type}");
        sb.AppendLine(TerrainColorMap.Describe(tile.type));

        if (tile.HasOre)
        {
            var info = OreDatabase.Get(tile.ore);
            sb.AppendLine($"\n<color=#8FD0FF>Ore: {info.displayName}</color> (Tier {info.tier}, {info.baseValue}cr)");
            if (ResearchManager.IsResearched(tile.ore))
            {
                sb.AppendLine($"Uses: {info.uses}");
                sb.AppendLine($"Refining: {info.refining}");
            }
            else if (ResearchManager.IsDiscovered(tile.ore))
                sb.AppendLine("<color=#FFBF4D>Discovered — research it in the Codex to learn its uses.</color>");
        }
        infoText.text = sb.ToString();
    }
}
