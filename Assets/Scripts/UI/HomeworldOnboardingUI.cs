using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The opening's screens (see HomeworldOnboarding): the instruction banner across the top of the screen,
// "Are you sure you want <world>?", and the naming window. Built in code like every other window.
public class HomeworldOnboardingUI : MonoBehaviour
{
    public static HomeworldOnboardingUI Instance;

    /// True while either dialog is up, so hotkeys and the camera's wheel know a text box may be live.
    public static bool DialogOpen => Instance != null &&
        ((Instance.confirmRoot != null && Instance.confirmRoot.activeSelf) ||
         (Instance.nameRoot != null && Instance.nameRoot.activeSelf));

    /// Open, or closed on this very frame — so the Escape that closes a dialog does not also open the
    /// pause menu. Same reason as NamePrompt.SwallowsEscape.
    public static bool SwallowsEscape => Instance != null && (DialogOpen || Instance.closedOnFrame == Time.frameCount);
    int closedOnFrame = -1;

    Transform canvas;
    GameObject banner;
    TMP_Text bannerText;
    Button bannerButton;
    TMP_Text bannerButtonText;

    GameObject catcher;
    GameObject confirmRoot;
    TMP_Text confirmTitle, confirmBody;

    GameObject nameRoot;
    TMP_InputField nameField;

    CelestialBody pending;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("HomeworldOnboardingUI");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<HomeworldOnboardingUI>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        canvas = parent;

        // ---- Banner ----
        // Along the BOTTOM of the screen: the top is the HUD and the notification stack, and a banner
        // there drew over the very toasts that tell the player what to do next. It takes no clicks
        // itself (only its button does), so the map under it stays usable.
        var bimg = UIFactory.Panel(parent, "OnboardingBanner", new Color(0.05f, 0.09f, 0.14f, 0.94f));
        bimg.raycastTarget = false;
        banner = bimg.gameObject;
        var brt = bimg.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0f, 110f);
        brt.sizeDelta = new Vector2(760f, 44f);
        var outline = banner.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        var row = banner.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 8, 6, 6);
        row.spacing = 10;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true; row.childControlHeight = true;
        row.childForceExpandWidth = false; row.childForceExpandHeight = true;

        bannerText = UIFactory.Text(banner.transform, "", UITheme.SmallSize + 1, new Color(1f, 0.92f, 0.55f), TextAlignmentOptions.Left);
        bannerText.raycastTarget = false;
        var tle = bannerText.gameObject.AddComponent<LayoutElement>(); tle.flexibleWidth = 1f;

        bannerButton = UIFactory.Button(banner.transform, "Open", OpenWorld, 30f);
        var ble = bannerButton.GetComponent<LayoutElement>();
        if (ble == null) ble = bannerButton.gameObject.AddComponent<LayoutElement>();
        ble.preferredWidth = 170f; ble.flexibleWidth = 0f;
        bannerButtonText = bannerButton.GetComponentInChildren<TMP_Text>();
        banner.SetActive(false);

        // ---- Modal backdrop ----
        catcher = UIFactory.Panel(parent, "OnboardingCatcher", new Color(0f, 0f, 0f, 0.45f)).gameObject;
        UIFactory.Stretch(catcher.GetComponent<RectTransform>());
        catcher.SetActive(false);

        // ---- "Are you sure?" ----
        var c = UIFactory.Window(parent, "Starting World", new Vector2(420, 210), out confirmRoot, out _, closeButton: false);
        Centre(confirmRoot);
        UIFactory.VerticalLayout(c, 8);
        confirmTitle = UIFactory.WrapText(c, "", UITheme.SmallSize + 3, UITheme.Text);
        confirmBody = UIFactory.WrapText(c, "", UITheme.SmallSize, UITheme.SubText);
        var crow = ButtonRow(c);
        UIFactory.Button(crow, "No", CancelConfirm, 28f);
        UIFactory.Button(crow, "Yes", AcceptConfirm, 28f);
        confirmRoot.SetActive(false);

        // ---- Naming ----
        var n = UIFactory.Window(parent, "Name Your Home World", new Vector2(420, 210), out nameRoot, out _, closeButton: false);
        Centre(nameRoot);
        UIFactory.VerticalLayout(n, 8);
        nameField = UIFactory.InputField(n, "World name", "", 34f);
        nameField.characterLimit = 28;
        // The request's wording, uneditable, BELOW the box.
        UIFactory.WrapText(n, "What would you like your Home World to be called?", UITheme.SmallSize, UITheme.SubText);
        var nrow = ButtonRow(n);
        UIFactory.Button(nrow, "Back", BackToChoosing, 28f);
        UIFactory.Button(nrow, "Confirm", ConfirmName, 28f);
        nameRoot.SetActive(false);
    }

    static void Centre(GameObject root)
    {
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    static Transform ButtonRow(Transform parent)
    {
        var row = UIFactory.NewUI(parent, "Buttons");
        UIFactory.AddLayout(row, 30f);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8;
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
        return row.transform;
    }

    // ---- Picking ----

    /// A world was clicked while the opening is asking for one.
    public void Pick(CelestialBody b)
    {
        if (!HomeworldOnboarding.Choosing || DialogOpen) return;
        if (!HomeworldOnboarding.Eligible(b, out string why))
        {
            NotificationManager.Instance?.Push("Not that one", $"{b?.name}: {why}.", null, NotifKind.Info);
            return;
        }
        pending = b;
        confirmTitle.text = $"Are you sure you want <b>{b.name}</b>?";
        string hex = Habitability.ScoreColorHex(b.habitability);
        string kind = b.parentBody != null ? $"a moon of {b.parentBody.name}" : TerraformDiagnosis.Pretty(b);
        confirmBody.text = $"{kind} · {MassRules.Format(b.mass)} Earths · {b.atmospheres:0.#} atm\n" +
                           $"Habitability for {SpeciesManager.Current.name}: <color={hex}><b>{b.habitability:F0}%</b></color>";
        Show(confirmRoot);
    }

    void AcceptConfirm()
    {
        Hide(confirmRoot);
        if (pending == null) return;
        nameField.text = pending.name;
        Show(nameRoot);
        nameField.Select();
        nameField.ActivateInputField();
        nameField.selectionAnchorPosition = 0;
        nameField.selectionFocusPosition = nameField.text.Length;
    }

    void CancelConfirm() { Hide(confirmRoot); pending = null; }

    void BackToChoosing() { if (nameField != null) nameField.DeactivateInputField(); Hide(nameRoot); pending = null; }

    void ConfirmName()
    {
        if (pending == null) { Hide(nameRoot); return; }
        string name = (nameField.text ?? "").Trim();
        if (name.Length == 0) name = pending.name;
        var b = pending;
        pending = null;
        nameField.DeactivateInputField();
        Hide(nameRoot);
        HomeworldOnboarding.Claim(b, name);
    }

    void Show(GameObject root)
    {
        catcher.SetActive(true);
        catcher.GetComponent<RectTransform>().SetAsLastSibling();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();
    }

    void Hide(GameObject root)
    {
        if (root != null && root.activeSelf) closedOnFrame = Time.frameCount;
        if (root != null) root.SetActive(false);
        if (!DialogOpen && catcher != null) catcher.SetActive(false);
    }

    void OpenWorld()
    {
        var w = HomeworldOnboarding.World;
        if (w != null) PlanetViewWindow.Instance?.ShowFor(w, PlanetViewWindow.Tab.Build);
    }

    // ---- Every frame ----

    void Update()
    {
        HomeworldOnboarding.FrameIfPending();
        HomeworldOnboarding.Tick();

        var step = HomeworldOnboarding.Step;
        bool showBanner = step >= OnboardingStep.ChooseWorld && step < OnboardingStep.Done
                          && !GenesisSequence.Running && !GenesisCamera.Active;
        if (banner.activeSelf != showBanner)
        {
            banner.SetActive(showBanner);
            // Brought forward once when it appears, not every frame — it must not cover menus opened later.
            if (showBanner) banner.GetComponent<RectTransform>().SetAsLastSibling();
        }
        if (showBanner)
        {
            bannerText.text = HomeworldOnboarding.Instruction();
            bool canOpen = HomeworldOnboarding.World != null;
            bannerButton.gameObject.SetActive(canOpen);
            if (canOpen && bannerButtonText != null) bannerButtonText.text = $"Open {HomeworldOnboarding.World.name}";
        }

        if (DialogOpen)
        {
            if (nameRoot.activeSelf && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) ConfirmName();
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (nameRoot.activeSelf) BackToChoosing(); else CancelConfirm();
            }
        }
    }
}

/// A flashing yellow "!" pinned to the top-right corner of a UI element — the opening's "look here".
/// Ignores its parent's layout so it can sit on a tab button or a card's title row without moving them.
public class AlertMarker : MonoBehaviour
{
    TMP_Text text;

    public static AlertMarker Attach(RectTransform parent)
    {
        if (parent == null) return null;
        var go = UIFactory.NewUI(parent, "Alert");
        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-2f, 2f);
        rt.sizeDelta = new Vector2(16f, 18f);

        var m = go.AddComponent<AlertMarker>();
        m.text = UIFactory.Text(go.transform, "!", 16, new Color(1f, 0.85f, 0.1f), TextAlignmentOptions.Center);
        m.text.fontStyle = FontStyles.Bold;
        m.text.raycastTarget = false;
        UIFactory.Stretch(m.text.rectTransform);
        var sh = m.text.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
        sh.effectDistance = new Vector2(1f, -1f);
        return m;
    }

    void Update()
    {
        if (text == null) return;
        // A blink rather than a fade: about twice a second, unscaled so it flashes while paused.
        float a = Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.6f ? 1f : 0.15f;
        var c = text.color; c.a = a; text.color = c;
    }
}
