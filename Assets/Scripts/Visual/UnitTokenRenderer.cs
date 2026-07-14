using System.Collections.Generic;
using UnityEngine;

// Draws a small billboarded token for every ship: its class icon plus a faction emblem. Tokens
// hover by their current body (or home in on a moving destination while travelling). The emblem is
// solid on worlds the owner controls and semi-transparent where the owner merely has a presence.
// Tokens are clickable (select + zoom + stats) and show a tooltip on hover.
public class UnitTokenRenderer : MonoBehaviour
{
    public static UnitTokenRenderer Instance;

    Camera cam;
    readonly Dictionary<Unit, UnitToken> tokens = new Dictionary<Unit, UnitToken>();
    Texture2D emblem;
    Texture2D redX;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("UnitTokenRenderer").AddComponent<UnitTokenRenderer>();
    }

    void Awake()
    {
        Instance = this;
        cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        emblem = CircleTexture();
        redX = RedXTexture();
    }

    // Brief red ✕ that lingers ~0.7s at a destroyed unit's position, then vanishes.
    public void FlashDestroy(Vector3 worldPos)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
        q.name = "DestroyFlash";
        q.transform.SetParent(transform, false);
        q.transform.position = worldPos;
        q.transform.localScale = Vector3.one * 3f;
        var mr = q.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = redX, color = new Color(1f, 0.2f, 0.2f, 1f) };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        q.AddComponent<DestroyFlash>().Init(cam, 0.7f);
    }

    void OnEnable()
    {
        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged += Rebuild;
    }
    void OnDisable()
    {
        if (UnitManager.Instance != null) UnitManager.Instance.OnUnitsChanged -= Rebuild;
    }

    void Start() { Rebuild(); }

    public void Rebuild()
    {
        // Remove tokens for units that no longer exist.
        var live = new HashSet<Unit>();
        if (UnitManager.Instance != null) foreach (var u in UnitManager.Instance.Units) live.Add(u);

        var toRemove = new List<Unit>();
        foreach (var kv in tokens) if (!live.Contains(kv.Key)) toRemove.Add(kv.Key);
        foreach (var u in toRemove) { if (tokens[u] != null) Destroy(tokens[u].gameObject); tokens.Remove(u); }

        // Add tokens for new units.
        if (UnitManager.Instance != null)
            foreach (var u in UnitManager.Instance.Units)
                if (!tokens.ContainsKey(u)) tokens[u] = BuildToken(u);
    }

    UnitToken BuildToken(Unit u)
    {
        var go = new GameObject("Token_" + u.name);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * 0.8f;   // ship symbols 50% smaller relative to the view

        var icon = MakeQuad(go.transform, UnitIconRenderer.Get(u.type), Color.white, new Vector3(0, 0, 0), 1f);
        var em = MakeQuad(go.transform, emblem, FactionManager.OwnerColor(u.owner), new Vector3(0.55f, 0.55f, -0.01f), 0.5f);

        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(1.2f, 1.2f, 0.2f);

        var t = go.AddComponent<UnitToken>();
        t.Init(u, em.GetComponent<MeshRenderer>());
        return t;
    }

    GameObject MakeQuad(Transform parent, Texture2D tex, Color tint, Vector3 localPos, float scale)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
        q.transform.SetParent(parent, false);
        q.transform.localPosition = localPos;
        q.transform.localScale = Vector3.one * scale;
        var mr = q.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = tint };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return q;
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        foreach (var kv in tokens)
        {
            var u = kv.Key; var tok = kv.Value;
            if (tok == null) continue;

            Vector3 pos;
            float emblemAlpha;
            if (u.location != null)
            {
                Vector3 basePos = u.location.visualObject != null ? u.location.visualObject.transform.position
                                : (u.location.system != null ? u.location.system.galaxyPosition : Vector3.zero);
                int idx = u.location.units != null ? u.location.units.IndexOf(u) : 0;
                int count = u.location.units != null ? Mathf.Max(1, u.location.units.Count) : 1;
                float ring = (u.location.visualObject != null ? u.location.visualObject.transform.lossyScale.x * 0.7f : 1f) + 1.6f;
                float ang = idx * Mathf.PI * 2f / count;
                pos = basePos + new Vector3(Mathf.Cos(ang) * ring, u.location.visualObject != null ? u.location.visualObject.transform.lossyScale.x * 0.6f + 1.2f : 1.2f, Mathf.Sin(ang) * ring);
                emblemAlpha = (u.location.owner == u.owner) ? 0.95f : 0.4f;   // transparent = presence, not ownership
            }
            else if (u.status == UnitStatus.Traveling)
            {
                // In transit: fly the straight intercept line computed at launch.
                pos = Vector3.Lerp(u.travelFrom, u.travelTo, u.TravelProgress) + Vector3.up * 1.2f;
                emblemAlpha = 0.85f;
            }
            else
            {
                // Parked in deep space.
                pos = u.parkPosition + Vector3.up * 1.2f;
                emblemAlpha = 0.85f;
            }

            tok.transform.position = pos;
            tok.transform.rotation = cam.transform.rotation;   // billboard
            tok.SetEmblemAlpha(emblemAlpha);
            tok.SetSelected(UnitSelection.IsSelected(u));
        }
    }

    static Texture2D RedXTexture()
    {
        int s = 32; var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1, 1, 1, 0);
        for (int i = 0; i < s; i++)
            for (int t = -2; t <= 2; t++)
            {
                int a = Mathf.Clamp(i + t, 0, s - 1);
                px[i * s + a] = Color.white;             // '\' diagonal
                px[i * s + Mathf.Clamp(s - 1 - i + t, 0, s - 1)] = Color.white;   // '/' diagonal
            }
        tex.SetPixels(px); tex.Apply(); return tex;
    }

    static Texture2D CircleTexture()
    {
        int s = 32; var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s]; Vector2 c = new Vector2(s / 2f, s / 2f);
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c) / (s * 0.5f);
            px[y * s + x] = d < 0.9f ? Color.white : new Color(1, 1, 1, 0);
        }
        tex.SetPixels(px); tex.Apply(); return tex;
    }
}

// The clickable token behaviour.
public class UnitToken : MonoBehaviour
{
    Unit unit;
    MeshRenderer emblem;
    float baseScale;

    public void Init(Unit u, MeshRenderer emblem)
    {
        unit = u; this.emblem = emblem; baseScale = transform.localScale.x;
    }

    public void SetEmblemAlpha(float a)
    {
        if (emblem == null) return;
        var c = emblem.material.color; c.a = a; emblem.material.color = c;
    }

    public void SetSelected(bool sel)
    {
        transform.localScale = Vector3.one * (sel ? baseScale * 1.4f : baseScale);
    }

    void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        if (FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting) return;

        // Shift-click adds to the current fleet selection; a plain click selects just this ship.
        bool additive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (additive) UnitSelection.Select(unit, true);
        else UnitSelection.SelectOnly(unit);
        SimpleAudio.Instance?.PlayUnitSelect(unit.type);
        CameraController.Instance?.FocusAndZoom(transform, 3f, false);
        UnitInfoPanel.Instance?.Show(unit);
    }

    void OnMouseEnter()
    {
        TooltipManager.Instance.ShowAtCursor(unit.HoverText());
    }

    void OnMouseExit()
    {
        TooltipManager.Instance.Hide();
    }
}

// A brief billboarded red ✕ that fades out then self-destructs (unit destruction feedback).
public class DestroyFlash : MonoBehaviour
{
    Camera cam;
    float life, age;

    public void Init(Camera c, float life) { cam = c; this.life = life; }

    void LateUpdate()
    {
        if (cam != null) transform.rotation = cam.transform.rotation;
        age += Time.unscaledDeltaTime;
        var mr = GetComponent<MeshRenderer>();
        if (mr != null) { var c = mr.material.color; c.a = Mathf.Clamp01(1f - age / life); mr.material.color = c; }
        if (age >= life) Destroy(gameObject);
    }
}
