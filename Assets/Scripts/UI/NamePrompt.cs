using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================================================
// TYPING A NAME
//
// `Squadrons.Rename` and `Fleets.Rename` were both written, both saved and loaded, and both
// unreachable: nothing anywhere called either of them. A squadron was "3" and a fleet was "Fleet 2"
// forever, which is exactly the state naming exists to fix — by the mid-game a player has a home
// guard, a strike wing and a survey arm, and remembering which of nine numbers is which is the work
// the name was supposed to do for them.
//
// One prompt serves both tiers, because "ask for a short piece of text" is one job and two dialogs
// that drift apart is two.
//
// ---- IT IS MODAL, AND THAT IS THE POINT ----------------------------------------------------
//
// A full-screen catcher sits behind the panel. Not for looks: while this is open the player is
// typing, and every letter they type is also a game hotkey — `1`..`9` recall squadrons, `T` focuses
// fire, `H` holds position. Without something to swallow the clicks and a flag the hotkeys respect,
// naming a squadron "Third Fleet" would recall squadron 3 and order the selection to hold.
//
// `IsTyping` is that flag, and every keyboard handler in the game checks it (see ControlGroupInput
// and FleetCommandBar). One static bool rather than a focus system, because there is exactly one
// text field in this game and inventing an input stack for it would be building for a problem
// nobody has.
//
// ---- ENTER AND ESCAPE --------------------------------------------------------------------------
//
// Both, because a prompt this small should never need the mouse. Enter accepts, Escape abandons, and
// the field is focused the moment it opens so the player can simply start typing.
// ============================================================================================
public class NamePrompt : MonoBehaviour
{
    public static NamePrompt Instance;

    /// True while the prompt is open.
    public static bool IsOpen => Instance != null && Instance.root != null && Instance.root.activeSelf;

    /// True while the prompt is open AND for the rest of the frame it closes on.
    ///
    /// The second half is the whole reason this exists. EscapeMenu also listens for Escape, Unity does
    /// not define which Update runs first, and without this, dismissing the prompt would open the
    /// pause menu about half the time — a bug that would look random and be miserable to chase.
    public static bool SwallowsEscape
        => Instance != null && (IsOpen || Instance.closedOnFrame == Time.frameCount);

    int closedOnFrame = -1;

    /// The longest name accepted. Long enough for "Second Survey Wing", short enough that it still
    /// fits the roster row and the command bar header it will be drawn into.
    public const int MaxLength = 28;

    GameObject root;
    GameObject catcher;
    TMP_Text titleText;
    TMP_InputField field;
    System.Action<string> onAccept;

    public static void Create(Transform parent)
    {
        if (Instance != null) return;
        var go = new GameObject("NamePrompt");
        go.transform.SetParent(parent, false);
        Instance = go.AddComponent<NamePrompt>();
        Instance.Build(parent);
    }

    void Build(Transform parent)
    {
        // The catcher first, so it sits UNDER the panel in draw order. Nearly transparent rather than
        // invisible: a dialog that dims what is behind it says "answer me" without a word.
        catcher = UIFactory.Panel(parent, "NamePromptCatcher", new Color(0f, 0f, 0f, 0.45f)).gameObject;
        UIFactory.Stretch(catcher.GetComponent<RectTransform>());
        catcher.SetActive(false);

        // 186 tall, not 150: UIFactory.Window insets its content by 42 at the top and 16 at the bottom,
        // so a 150-tall window leaves 92 for a field, a button row and a line of help that together
        // want about 104. The layout group would have quietly squeezed them.
        var content = UIFactory.Window(parent, "Name", new Vector2(360, 186), out root, out titleText,
                                       closeButton: false);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        UIFactory.VerticalLayout(content, 8);

        field = UIFactory.InputField(content, "Name this squadron…", "", 32f);
        field.characterLimit = MaxLength;

        var row = UIFactory.NewUI(content, "Buttons");
        UIFactory.AddLayout(row, 28f);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6;
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;

        UIFactory.Button(row.transform, "Cancel", Close, 26f);
        UIFactory.Button(row.transform, "Rename", Accept, 26f);

        UIFactory.WrapText(content, "<color=#8FA3B5>Enter to accept, Escape to cancel. Leave it empty " +
                                    "to go back to the number.</color>",
                           UITheme.SmallSize, UITheme.SubText);

        root.SetActive(false);
    }

    /// Ask for a name. `current` pre-fills the field and is selected, so typing replaces it.
    public void Ask(string title, string placeholder, string current, System.Action<string> accept)
    {
        onAccept = accept;
        titleText.text = title;
        if (field.placeholder is TMP_Text ph) ph.text = placeholder;
        field.text = current ?? "";

        catcher.SetActive(true);
        catcher.GetComponent<RectTransform>().SetAsLastSibling();
        root.SetActive(true);
        root.GetComponent<RectTransform>().SetAsLastSibling();

        // Focused and fully selected, so the common case — replacing "Squadron 3" outright — is one
        // keystroke rather than a hunt for the end of the line.
        field.Select();
        field.ActivateInputField();
        field.selectionAnchorPosition = 0;
        field.selectionFocusPosition = field.text.Length;
    }

    void Accept()
    {
        var cb = onAccept;
        string value = (field.text ?? "").Trim();
        Close();
        cb?.Invoke(value);
    }

    void Close()
    {
        onAccept = null;
        closedOnFrame = Time.frameCount;
        if (field != null) field.DeactivateInputField();
        if (root != null) root.SetActive(false);
        if (catcher != null) catcher.SetActive(false);
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;

        // Both Return and the numeric keypad's Enter, because a player who has just typed a name with
        // the number row is as likely to be on one as the other.
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Accept(); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }
}
