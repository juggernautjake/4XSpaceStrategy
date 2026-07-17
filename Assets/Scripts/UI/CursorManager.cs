using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// ============================================================================================
// CUSTOM CURSOR — a stylized, context-aware pointer, drawn procedurally in code (no imported art, in
// keeping with the rest of the game). The cursor changes shape to tell you what a click will DO:
//
//   • POINTER  — the default: a stylized arrow with a cyan edge.
//   • SELECT   — a bracket reticle, shown while hovering something clickable in the world (a planet,
//                star or ship).
//   • SEND     — a green "move here" chevron-over-ring, shown while a fleet is selected / being aimed,
//                so dispatching ships has its own unmistakable look.
//   • BUSY     — a spinning ring, shown while something is loading (galaxy generation, etc.). Any system
//                can raise it with `CursorManager.Busy = true`.
//
// Textures are built once at start. The cursor is only re-applied when the STATE changes (or, for the
// spinner, on its animation tick), never blindly every frame.
// ============================================================================================
public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance;

    // Loading flag any system can raise (e.g. while generating a galaxy) to show the spinner.
    public static bool Busy;

    enum Kind { Pointer, Select, Send, Busy }

    Texture2D pointerTex, selectTex, sendTex;
    Texture2D[] spinnerFrames;
    Vector2 pointerHot, centreHot;

    Kind current = (Kind)(-1);
    int spinnerFrame = -1;
    float spinTimer;
    const int SpinnerCount = 12;
    const float SpinFps = 14f;

    Camera cam;

    public static void Create()
    {
        if (Instance != null) return;
        new GameObject("CursorManager").AddComponent<CursorManager>();
    }

    void Awake()
    {
        Instance = this;
        BuildTextures();
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;

        Kind want = Resolve();

        if (want == Kind.Busy)
        {
            // Advance the spinner and re-apply on each animation tick.
            spinTimer += Time.unscaledDeltaTime;
            int frame = Mathf.FloorToInt(spinTimer * SpinFps) % SpinnerCount;
            if (want != current || frame != spinnerFrame)
            {
                spinnerFrame = frame;
                Cursor.SetCursor(spinnerFrames[frame], centreHot, CursorMode.Auto);
            }
            current = want;
            return;
        }

        if (want == current) return;
        current = want;
        spinnerFrame = -1;

        switch (want)
        {
            case Kind.Select: Cursor.SetCursor(selectTex, centreHot, CursorMode.Auto); break;
            case Kind.Send:   Cursor.SetCursor(sendTex, centreHot, CursorMode.Auto); break;
            default:          Cursor.SetCursor(pointerTex, pointerHot, CursorMode.Auto); break;
        }
    }

    // What the cursor should be RIGHT NOW, in priority order.
    Kind Resolve()
    {
        if (Busy) return Kind.Busy;

        // Over UI: always the plain pointer (buttons, sliders, windows).
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return Kind.Pointer;

        // Dispatching a fleet — either actively aiming, or simply having movable ships selected (the
        // passive send-preview state) — gets the "send" cursor over the world.
        bool aiming = FleetMovementController.Instance != null && FleetMovementController.Instance.IsTargeting;
        if (aiming || HasSendableSelection()) return Kind.Send;

        // Hovering something you can click in the world -> the select reticle.
        if (OverClickableWorldObject()) return Kind.Select;

        return Kind.Pointer;
    }

    static bool HasSendableSelection()
    {
        var sel = UnitSelection.Selected;
        if (sel == null) return false;
        foreach (var u in sel) if (u != null && u.status != UnitStatus.Traveling) return true;
        return sel.Count > 0;   // even traveling ships can be redirected
    }

    bool OverClickableWorldObject()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 5000f)) return false;
        var go = hit.collider.gameObject;
        return go.GetComponent<PlanetClick>() != null
            || go.GetComponent<StarInteraction>() != null
            || go.GetComponent<UnitToken>() != null;
    }

    // ---- Texture construction ----------------------------------------------------------------

    static readonly Color Fill    = new Color(0.92f, 0.97f, 1.00f, 1f);   // near-white body
    static readonly Color Edge    = new Color(0.03f, 0.06f, 0.10f, 1f);   // dark outline
    static readonly Color Cyan    = new Color(0.34f, 0.78f, 1.00f, 1f);   // accent
    static readonly Color Green   = new Color(0.40f, 1.00f, 0.55f, 1f);   // "go / send"
    static readonly Color Clear   = new Color(0, 0, 0, 0);

    void BuildTextures()
    {
        pointerTex = BuildPointer();
        pointerHot = new Vector2(1, 1);        // tip is at the top-left

        selectTex = BuildSelect();
        sendTex = BuildSend();
        centreHot = new Vector2(16, 16);       // 32x32 reticles are centred on the cursor point

        spinnerFrames = new Texture2D[SpinnerCount];
        for (int i = 0; i < SpinnerCount; i++)
            spinnerFrames[i] = BuildSpinner(i * (360f / SpinnerCount));
    }

    // A stylized arrow, tip at the top-left, with a cyan inner accent and a dark outline. Drawn as a
    // filled polygon (arrow silhouette) then outlined, in image space (y down from the top-left).
    Texture2D BuildPointer()
    {
        int w = 22, h = 26;
        var px = New(w, h);
        Vector2[] poly =
        {
            new Vector2(0, 0), new Vector2(0, 17), new Vector2(4, 13.5f), new Vector2(7.5f, 20.5f),
            new Vector2(10.5f, 19f), new Vector2(7f, 12.5f), new Vector2(12.5f, 12.5f)
        };
        FillPolygon(px, w, h, poly, Fill);
        // A cyan sheen down the leading edge.
        DrawLineImg(px, w, h, 1, 1, 1, 15, Cyan);
        OutlinePolygon(px, w, h, poly, Edge);
        return Finish(px, w, h);
    }

    // A four-corner bracket reticle (targeting brackets) around the cursor point.
    Texture2D BuildSelect()
    {
        int s = 32;
        var px = New(s, s);
        int m = 5, len = 8, cx = s / 2, cy = s / 2, gap = 6;
        // Four L-shaped brackets pulled out from the centre by `gap`.
        Bracket(px, s, s, cx - gap, cy - gap, -1, -1, len, Cyan);
        Bracket(px, s, s, cx + gap, cy - gap, 1, -1, len, Cyan);
        Bracket(px, s, s, cx - gap, cy + gap, -1, 1, len, Cyan);
        Bracket(px, s, s, cx + gap, cy + gap, 1, 1, len, Cyan);
        // A small centre dot.
        Disc(px, s, s, cx, cy, 1.5f, Fill);
        return Finish(px, s, s);
    }

    // A green downward chevron ("move here") sitting above a soft ring — the "send fleet" cursor.
    Texture2D BuildSend()
    {
        int s = 32;
        var px = New(s, s);
        int cx = s / 2, cy = s / 2;
        Ring(px, s, s, cx, cy, 9f, 11f, Green);           // target ring
        // Downward chevron centred on the point.
        DrawLineImg(px, s, s, cx - 5, cy - 4, cx, cy + 2, Green);
        DrawLineImg(px, s, s, cx + 5, cy - 4, cx, cy + 2, Green);
        DrawLineImg(px, s, s, cx - 5, cy - 3, cx, cy + 3, Green);
        DrawLineImg(px, s, s, cx + 5, cy - 3, cx, cy + 3, Green);
        Disc(px, s, s, cx, cy, 1.4f, Fill);
        return Finish(px, s, s);
    }

    // A spinning ring with a bright leading arc, rotated by `deg`, for the loading state.
    Texture2D BuildSpinner(float deg)
    {
        int s = 32;
        var px = New(s, s);
        int cx = s / 2, cy = s / 2;
        Ring(px, s, s, cx, cy, 8f, 10f, new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));   // faint full ring
        // A bright ~110-degree leading arc.
        for (float a = 0; a < 110f; a += 3f)
        {
            float rad = (deg + a) * Mathf.Deg2Rad;
            int x = cx + Mathf.RoundToInt(9f * Mathf.Cos(rad));
            int y = cy + Mathf.RoundToInt(9f * Mathf.Sin(rad));
            Disc(px, s, s, x, y, 1.4f, Cyan);
        }
        return Finish(px, s, s);
    }

    // ---- Low-level pixel drawing (image space: y grows DOWNWARD from the top-left) ----------

    static Color[] New(int w, int h)
    {
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = Clear;
        return px;
    }

    // Write in image space; the texture is flipped vertically in Finish so (0,0) reads as the top-left.
    static void SetImg(Color[] px, int w, int h, int x, int y, Color c)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        px[y * w + x] = c;
    }

    static Texture2D Finish(Color[] px, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        // Flip Y: image space is top-left origin, Texture2D is bottom-left.
        var flipped = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                flipped[(h - 1 - y) * w + x] = px[y * w + x];
        tex.SetPixels(flipped);
        tex.Apply();
        return tex;
    }

    static void DrawLineImg(Color[] px, int w, int h, int x0, int y0, int x1, int y1, Color c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            SetImg(px, w, h, x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    static void Bracket(Color[] px, int w, int h, int x, int y, int dirX, int dirY, int len, Color c)
    {
        DrawLineImg(px, w, h, x, y, x + dirX * len, y, c);
        DrawLineImg(px, w, h, x, y, x, y + dirY * len, c);
    }

    static void Disc(Color[] px, int w, int h, int cx, int cy, float r, Color c)
    {
        int ri = Mathf.CeilToInt(r);
        for (int y = -ri; y <= ri; y++)
            for (int x = -ri; x <= ri; x++)
                if (x * x + y * y <= r * r) SetImg(px, w, h, cx + x, cy + y, c);
    }

    static void Ring(Color[] px, int w, int h, int cx, int cy, float rInner, float rOuter, Color c)
    {
        int ro = Mathf.CeilToInt(rOuter);
        float i2 = rInner * rInner, o2 = rOuter * rOuter;
        for (int y = -ro; y <= ro; y++)
            for (int x = -ro; x <= ro; x++)
            {
                float d2 = x * x + y * y;
                if (d2 >= i2 && d2 <= o2) SetImg(px, w, h, cx + x, cy + y, c);
            }
    }

    static void FillPolygon(Color[] px, int w, int h, Vector2[] poly, Color c)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in poly) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
        int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(maxY));
        var xs = new List<float>();
        for (int y = y0; y <= y1; y++)
        {
            xs.Clear();
            float fy = y + 0.5f;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                if ((a.y <= fy && b.y > fy) || (b.y <= fy && a.y > fy))
                    xs.Add(a.x + (fy - a.y) / (b.y - a.y) * (b.x - a.x));
            }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                int xa = Mathf.RoundToInt(xs[i]), xb = Mathf.RoundToInt(xs[i + 1]);
                for (int x = xa; x <= xb; x++) SetImg(px, w, h, x, y, c);
            }
        }
    }

    static void OutlinePolygon(Color[] px, int w, int h, Vector2[] poly, Color c)
    {
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            DrawLineImg(px, w, h, Mathf.RoundToInt(a.x), Mathf.RoundToInt(a.y), Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.y), c);
        }
    }
}
