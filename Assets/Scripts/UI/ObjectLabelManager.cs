using System.Text;
using UnityEngine;
using TMPro;

// Floating world-tracking labels for the selected object: name above it, category just below, and a
// habitability rating (for the current species) under that. Positions update every frame so the text
// follows the body as it orbits.
public class ObjectLabelManager : MonoBehaviour
{
    public static ObjectLabelManager Instance;

    Camera cam;
    RectTransform nameRT, catRT, habRT;
    TMP_Text nameT, catT, habT;

    Transform target;
    float worldRadius;
    CelestialBody body;      // when set, habitability updates live (e.g. on species change)

    public static void Create(Transform ignored)
    {
        if (Instance != null) return;
        new GameObject("ObjectLabelManager").AddComponent<ObjectLabelManager>();
    }

    void Awake()
    {
        Instance = this;
        cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

        var canvas = UIFactory.CreateCanvas("ObjectLabelCanvas", 50); // above 3D, below windows(100)
        canvas.transform.SetParent(transform, false);

        nameT = MakeLabel(canvas.transform, 18, FontStyles.Bold, UITheme.Text, out nameRT);
        catT = MakeLabel(canvas.transform, 12, FontStyles.Normal, UITheme.SubText, out catRT);
        habT = MakeLabel(canvas.transform, 13, FontStyles.Bold, UITheme.Good, out habRT);

        HideNow();
    }

    TMP_Text MakeLabel(Transform parent, int size, FontStyles style, Color color, out RectTransform rt)
    {
        var t = UIFactory.Text(parent, "", size, color, TextAlignmentOptions.Center);
        t.fontStyle = style;
        rt = t.rectTransform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(320, 26);
        // Soft shadow so text never disappears against bright bodies.
        var sh = t.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.85f);
        sh.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    public void ShowForBody(CelestialBody b)
    {
        if (b == null || b.visualObject == null) { Hide(); return; }
        body = b;
        target = b.visualObject.transform;
        worldRadius = Mathf.Max(0.3f, target.lossyScale.x * 0.5f);
        nameT.text = b.name;
        string ownerHex = "#" + ColorUtility.ToHtmlStringRGB(FactionManager.OwnerColor(b.owner));
        catT.text = Prettify(b.type.ToString()) + (b.isHabitable ? "  (Habitable)" : "")
                  + $"  <color={ownerHex}>[{FactionManager.OwnerName(b.owner)}]</color>";
        SetActive(true);
        UpdateHab();
    }

    public void ShowForStar(Transform starT, float radius, string name, string category)
    {
        body = null;
        target = starT;
        worldRadius = Mathf.Max(0.5f, radius);
        nameT.text = name;
        catT.text = category;
        habT.gameObject.SetActive(false);
        nameT.gameObject.SetActive(true);
        catT.gameObject.SetActive(true);
    }

    public void Hide() { target = null; body = null; HideNow(); }

    void HideNow()
    {
        if (nameT) nameT.gameObject.SetActive(false);
        if (catT) catT.gameObject.SetActive(false);
        if (habT) habT.gameObject.SetActive(false);
    }

    void SetActive(bool on)
    {
        nameT.gameObject.SetActive(on);
        catT.gameObject.SetActive(on);
        habT.gameObject.SetActive(on);
    }

    void UpdateHab()
    {
        if (body == null) return;
        string label = Habitability.Label(body.habitability, body.isHabitable);
        habT.text = $"Habitability: {body.habitability:F0}/100 ({label})";
        habT.color = Habitability.ScoreColor(body.habitability);
    }

    // True while the labels are being withheld because the thing they name has been concealed. Tracked
    // rather than re-applied every frame so this costs two component reads and a branch, and so it does
    // not fight ShowForStar (which deliberately leaves the habitability line off).
    bool suppressed;

    void LateUpdate()
    {
        // A DESTROYED TARGET MUST TAKE THE LABELS WITH IT.
        //
        // This used to be a bare `return`, which is fine while nothing ever destroys a body's visual out
        // from under a live selection — and the galaxy rebuild now does exactly that (a deleted system,
        // a restored one). The labels are separate GameObjects, so they simply stayed active at whatever
        // screen position they were last written to, frozen over empty space until something else
        // happened to call Hide.
        if (target == null) { if (body != null || nameT.gameObject.activeSelf) Hide(); return; }

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // A CONCEALED OBJECT MUST NOT KEEP ITS NAME BADGE.
        //
        // Concealment can happen to whatever is already selected — hide the selected world from the
        // object panel, or cloak the ship you are watching — and the labels track a transform rather
        // than a renderer, so they would go on floating over the empty space where it used to be. Read
        // off the binding rather than off the data, because that covers stars and bodies with one test
        // and needs no back-reference from a Transform to whatever it is drawing.
        var binding = target.GetComponent<ConcealBinding>();
        bool concealed = binding != null && binding.Concealed;
        if (concealed != suppressed)
        {
            suppressed = concealed;
            if (concealed) HideNow();
            else
            {
                nameT.gameObject.SetActive(true);
                catT.gameObject.SetActive(true);
                habT.gameObject.SetActive(body != null);   // stars have no habitability line
            }
        }
        if (concealed) return;

        Vector3 center = cam.WorldToScreenPoint(target.position);
        if (center.z < 0f)
        {
            // Behind the camera: park the labels off-screen this frame.
            Vector3 off = new Vector3(-2000, -2000, 0);
            nameRT.position = catRT.position = habRT.position = off;
            return;
        }

        Vector3 edge = cam.WorldToScreenPoint(target.position + cam.transform.up * worldRadius);
        float screenR = Mathf.Max(14f, Mathf.Abs(edge.y - center.y));

        nameRT.position = new Vector3(center.x, center.y + screenR + 20f, 0);
        catRT.position = new Vector3(center.x, center.y - screenR - 14f, 0);
        habRT.position = new Vector3(center.x, center.y - screenR - 34f, 0);

        if (body != null) UpdateHab();
    }

    static string Prettify(string enumName)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < enumName.Length; i++)
        {
            char c = enumName[i];
            if (i > 0 && char.IsUpper(c)) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
