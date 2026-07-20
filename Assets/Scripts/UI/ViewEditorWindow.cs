using UnityEngine;
using TMPro;

// The camera control panel: pan, zoom, spin, and the three named zoom levels.
//
// Every one of these is already reachable from the keyboard or the mouse — WASD pans, the wheel zooms,
// middle-drag spins, Home frames the galaxy. This window exists because none of that is DISCOVERABLE, and
// because the camera now has a degree of freedom (yaw) that a player has no reason to guess at.
//
// Available in normal play, NOT gated behind Dev Mode. It contains no cheats and edits no world state —
// it only moves the camera, which the player can already do. Gating it would hide the only place the
// rotate control is written down.
public class ViewEditorWindow : MonoBehaviour
{
    public static ViewEditorWindow Instance;

    GameObject root;
    TMP_Text bearingText;
    TMP_Text heightText;

    // Held-button state: the rotate arrows spin continuously while pressed rather than stepping once
    // per click, which is what makes fine framing possible.
    float holdDir;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("ViewEditorWindow");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<ViewEditorWindow>();
        Instance.Build(parent);
    }

    public bool IsOpen => root != null && root.activeSelf;
    public void Toggle() { if (root != null) root.SetActive(!root.activeSelf); }
    public void Hide() { if (root != null) root.SetActive(false); }

    void Build(Transform parent)
    {
        var content = UIFactory.Window(parent, "View", new Vector2(300, 340), out root, out _);
        UIFactory.VerticalLayout(content, 6);

        // ---- Named zoom levels ----
        UIFactory.Label(content, "ZOOM LEVEL", UITheme.SmallSize, UITheme.SubText, 18);

        UIFactory.Button(content, "Planet", () => CameraController.Instance?.ViewPlanet(), 30);
        UIFactory.Button(content, "System", () => CameraController.Instance?.ViewSystem(), 30);
        UIFactory.Button(content, "Galaxy", () => CameraController.Instance?.ViewWholeGalaxy(), 30);

        // ---- Zoom nudge ----
        UIFactory.Label(content, "ZOOM", UITheme.SmallSize, UITheme.SubText, 18);
        var zoomRow = UIFactory.NewUI(content, "ZoomRow");
        UIFactory.AddLayout(zoomRow, 30);
        var zh = zoomRow.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        zh.spacing = 6; zh.childControlWidth = true; zh.childForceExpandWidth = true;
        UIFactory.Button(zoomRow.transform, "− Out", () => CameraController.Instance?.NudgeZoom(1), 28);
        UIFactory.Button(zoomRow.transform, "+ In", () => CameraController.Instance?.NudgeZoom(-1), 28);

        // ---- Rotation ----
        UIFactory.Label(content, "ROTATE", UITheme.SmallSize, UITheme.SubText, 18);
        var rotRow = UIFactory.NewUI(content, "RotRow");
        UIFactory.AddLayout(rotRow, 30);
        var rh = rotRow.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        rh.spacing = 6; rh.childControlWidth = true; rh.childForceExpandWidth = true;

        // Press-and-hold rather than click-to-step: HoldButton reports whether it is currently down, and
        // Update spins while it is. A per-click step can't frame a shot.
        var left = UIFactory.Button(rotRow.transform, "⟲ Left", null, 28);
        var right = UIFactory.Button(rotRow.transform, "Right ⟳", null, 28);
        AttachHold(left.gameObject, -1f);
        AttachHold(right.gameObject, +1f);

        UIFactory.Button(content, "Reset rotation", () => CameraController.Instance?.ResetRotation(), 28);

        bearingText = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.Text, 18);
        heightText = UIFactory.Label(content, "", UITheme.SmallSize, UITheme.SubText, 18);

        UIFactory.WrapText(content,
            "WASD pans · wheel zooms · middle-drag spins the view",
            UITheme.SmallSize, UITheme.SubText);

        root.SetActive(false);
    }

    void AttachHold(GameObject go, float dir)
    {
        var hold = go.AddComponent<ViewHoldButton>();
        hold.onDown = () => holdDir = dir;
        hold.onUp = () => { if (Mathf.Approximately(holdDir, dir)) holdDir = 0f; };
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        var cc = CameraController.Instance;
        if (cc == null) return;

        if (!Mathf.Approximately(holdDir, 0f))
            cc.RotateBy(holdDir * CameraController.RotateSpeed * Time.unscaledDeltaTime);

        // Compass bearing, so the readout means something. 0 is "north" (world +Z away from the camera).
        if (bearingText != null) bearingText.text = $"Bearing: {cc.Yaw:F0}°";
        if (heightText != null) heightText.text = $"Height: {cc.transform.position.y:F0}";
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}

// A button that reports press and release rather than only click, so the rotate arrows can spin the view
// for as long as they are held. UIFactory.Button gives a click-only Button; this rides alongside it.
public class ViewHoldButton : MonoBehaviour,
    UnityEngine.EventSystems.IPointerDownHandler,
    UnityEngine.EventSystems.IPointerUpHandler,
    UnityEngine.EventSystems.IPointerExitHandler
{
    public System.Action onDown;
    public System.Action onUp;

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => onDown?.Invoke();
    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) => onUp?.Invoke();

    // Releasing outside the button must also stop the spin, or dragging off it leaves the view turning
    // with nothing held down.
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => onUp?.Invoke();
}
