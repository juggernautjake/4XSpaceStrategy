using UnityEngine;

// ============================================================================================
// THE EMPIRE'S MARK, ON THE GROUND IT HOLDS
//
// "The mark on WORLDS as well as ships — colony markers and the claim overlay."
//
// The crest already exists (CivEmblem), it is already chosen at the same moment as the colours
// (CivIdentityPanel), and it is already painted on every hull (UnitModelRenderer). The one place it
// was not was the map — which is where a player spends most of their time and where "what is mine"
// is the question being asked. An empire whose identity appears only on its ships is an identity you
// see when you are looking at something else.
//
// ---- WHY A BADGE AND NOT A TINT ---------------------------------------------------------------
//
// Owner COLOUR was already carried, by the ring around a held world and a held system. That answers
// "someone owns this" and it answers "which someone" only if you can hold seven faction colours in
// your head at once — and it fails outright for a colour-blind player, who gets a ring in a hue they
// cannot separate from a neighbour's. A SHAPE is the redundant channel: the ring says an empire holds
// this, and the mark inside it says which, and either one alone still works.
//
// ---- THE PLAYER'S OWN HOLDINGS ONLY -----------------------------------------------------------
//
// Rival empires do not have generated marks yet — that is B13, and it needs a symbol generator that
// picks a crest per faction and stores it on the faction rather than in a static. Until then a rival's
// world keeps its coloured ring and no badge, which is exactly what it had before. Drawing the
// PLAYER's crest on a rival's world because it is the only crest that exists would be a lie about who
// owns the ground, and a worse map than no badge at all.
//
// ---- IT REDRAWS ITSELF ------------------------------------------------------------------------
//
// Subscribed to CivEmblem.OnChanged, because the mark is chosen on the new-game screen and B14 will
// let it be changed mid-game from an empire screen. A badge that captured the texture once would keep
// flying the old crest over every colony until the scene reloaded — and the moment that is most
// visible is the moment the player has just changed it and is looking for the difference.
// ============================================================================================
[DisallowMultipleComponent]
public class CivMarkBadge : MonoBehaviour
{
    /// The badge's world size at its authored scale, before the on-screen floor below.
    public float baseSize = 1f;

    /// Floor on apparent size, as a fraction of viewport height. The same idea as MinScreenWidthLine,
    /// and for the same reason: which empire holds a world is INFORMATION, and at the galaxy's widest
    /// zoom a badge scaled purely by distance is a smudge two pixels across. ~0.018 is about 19px at
    /// 1080p — small enough to read as a marker on one world, large enough to read as a shape.
    public float minScreenFraction = 0.018f;

    /// Ceiling on apparent size, so flying right up to a colony does not fill the screen with a crest.
    /// Without this the badge is the largest thing in the system view at close zoom, sitting on top of
    /// the world it is supposed to be labelling.
    public float maxScreenFraction = 0.055f;

    /// Where the crest sits relative to the thing it labels, in the parent's local space.
    ///
    /// BESIDE THE WORLD, NOT ON IT. Centred, the badge covers the planet it is describing — and the
    /// planet is the thing the player is actually looking at. Offset along local +X rather than "up",
    /// because the map is drawn in the XZ plane at a fixed camera pitch and there is no direction that
    /// reads as up in it; sideways is unambiguous under any projection.
    public Vector3 offset = Vector3.zero;

    Renderer quad;
    Material mat;
    Camera cam;
    bool subscribed;

    // ============================================================================================
    /// Attach (or update) a badge on `parent`. Returns null when there is no crest to draw — a missing
    /// symbol texture is not an error here, it just means the caller keeps whatever it had.
    public static CivMarkBadge Attach(Transform parent, float size, Vector3 where)
    {
        if (parent == null) return null;
        if (CivEmblem.Current == null) return null;

        // UIFactory.Ensure, not `GetComponent() ?? AddComponent()` — see ShipLOD.Attach and the UNITY
        // check in Check-Scripts.ps1. A badge on a pooled or rebuilt map object comes back as a fake
        // null and `??` hands the dead reference straight back.
        var badge = UIFactory.Ensure<CivMarkBadge>(parent.gameObject);
        badge.baseSize = size;
        badge.offset = where;
        badge.Rebuild();
        return badge;
    }

    void Rebuild()
    {
        var tex = CivEmblem.Current;
        if (tex == null) { if (quad != null) quad.enabled = false; return; }

        if (quad == null)
        {
            var go = SpaceMaterials.Primitive(PrimitiveType.Quad, transform, name + "_CivMark");
            go.AddComponent<FaceCamera>();
            quad = go.GetComponent<Renderer>();

            // Sprites/Default rather than Unlit: the crest is a cut-out with a transparent surround,
            // and an opaque material would draw it as a square tile of background over the map.
            mat = new Material(SpaceMaterials.SpriteShader());
            // Full white, ONCE, at creation. The crest already carries the empire's two colours,
            // composited by CivEmblem, so a tint would multiply the livery by itself and mud both hues.
            //
            // Set here and not in the refresh below, because FadeGroup owns this material's ALPHA from
            // the moment it captures the subtree — concealment and the galaxy's zoom crossfade both
            // drive it. Rewriting the colour on every symbol change would stamp alpha back to 1 and
            // flash a concealed system's badge on until the next frame corrected it.
            mat.color = Color.white;
            quad.material = mat;

            // OFF by default until Show() says otherwise, so attaching a badge never reveals a world
            // the visibility rules are concealing.
            quad.enabled = false;
        }

        mat.mainTexture = tex;
        quad.transform.localPosition = offset;
    }

    void OnEnable()
    {
        if (!subscribed) { CivEmblem.OnChanged += Rebuild; subscribed = true; }
    }

    void OnDisable()
    {
        // A static event outlives the object that subscribed to it, so without this a destroyed badge
        // stays on the list and every later symbol change throws. Same defect as FormationPreview's
        // leaked ControlGroups.OnChanged — see 2026-08-22 §D3.
        if (subscribed) { CivEmblem.OnChanged -= Rebuild; subscribed = false; }
    }

    void OnDestroy()
    {
        if (subscribed) { CivEmblem.OnChanged -= Rebuild; subscribed = false; }
        if (mat != null) Destroy(mat);
    }

    /// Concealment and ownership, in one call. The caller owns the question of WHETHER a badge should
    /// show; this owns how it is drawn.
    public void Show(bool on)
    {
        if (quad != null) quad.enabled = on && quad.sharedMaterial != null && mat.mainTexture != null;
    }

    void LateUpdate()
    {
        if (quad == null || !quad.enabled) return;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        float dist = Vector3.Distance(cam.transform.position, transform.position);
        if (dist <= 0.001f) return;

        // The world size that subtends a given fraction of the viewport at this distance.
        float worldPerViewport = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        // Divided back out of the parent's scale for the same reason MinScreenWidthLine does it: these
        // badges hang off map objects that the zoom ramp scales, so a size computed in world units
        // would have the zoom applied to it twice and the crest would swell as the camera pulled away.
        float s = Mathf.Max(0.0001f, transform.lossyScale.x);
        float lo = worldPerViewport * minScreenFraction / s;
        float hi = worldPerViewport * maxScreenFraction / s;

        float size = Mathf.Clamp(baseSize, lo, hi);
        quad.transform.localScale = new Vector3(size, size, size);
    }
}
