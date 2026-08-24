using UnityEngine;

// ============================================================================================
// DETAIL THAT FOLLOWS THE CAMERA
//
// Every hull used to be one mesh serving every view, and the arithmetic says that cannot work. At
// 1080p, with the camera framing a system, a dreadnought is NINE PIXELS tall. At the free-look floor
// it is over a THOUSAND. That is a 120:1 range, and no single asset is the right answer at both ends
// — one that looks crisp up close is a hundred times more mesh and texture than a nine-pixel speck
// can use, and one sized for the speck is what the player was seeing when they said "blobby".
//
// So the importer emits three levels per hull and this assembles them:
//
//     name_hi.glb     ~24,000 tris, no textures     the hull filling the screen
//     name.glb         ~9,000 tris, ALL the textures  the ordinary view, and the fallback
//     name_lo.glb      ~2,200 tris, no textures     a speck among many
//
// ---- ONE TEXTURE SET, SHARED BY ALL THREE -----------------------------------------------------
//
// The textures live on the MID file and the other two levels are handed its materials here. Three
// copies of a 1024 normal map would triple the fleet's texture memory to store the same image three
// times — and, worse, would reintroduce the exact artefact that LOD is supposed to hide: the moment
// the levels swapped, the resolution would visibly change. Sharing means the geometry swaps and
// nothing else does, which is the only kind of LOD transition nobody notices.
//
// ---- IT IS ENTIRELY OPTIONAL, AND THAT IS DELIBERATE ------------------------------------------
//
// If the LOD siblings are absent — a civilisation not yet re-imported, an import that half-finished,
// a checkout with older art — Attach does nothing at all and the ship draws from the base file
// exactly as it always has. The base file carries the textures precisely so that it can stand alone.
// A rendering optimisation that can break the game when a file is missing is not an optimisation.
// ============================================================================================
public static class ShipLOD
{
    // ---- Where the levels change over -------------------------------------------------------
    //
    // Unity measures LOD transitions as SCREEN-RELATIVE HEIGHT: the fraction of the viewport's height
    // the object's bounding sphere covers. That is the right unit here because it is resolution- and
    // field-of-view-independent — the same numbers behave the same way on any monitor, which a
    // distance in world units would not.
    //
    // Calibrated against tools/inspect-ship-lod.mjs, which measures a 0.40-unit dreadnought at 1080p:
    //
    //     camera height 1     374 px    0.35 of the screen    high detail earns its keep
    //     camera height 4      94 px    0.087                 mid
    //     camera height 40      9 px    0.008                 low
    //
    /// Above this fraction of screen height, the high-detail mesh.
    const float HighAbove = 0.18f;

    /// Above this, the mid mesh; below it, the low one.
    const float MidAbove = 0.030f;

    /// And below THIS, nothing is drawn at all.
    ///
    /// Deliberately tiny. Culling a ship the player can still see would be a bug, and the honest way
    /// to stop drawing a fleet at galaxy zoom is MapTierVisibility, which already hides ships by map
    /// tier rather than by pixel size. This is a backstop for the genuinely sub-pixel case.
    const float CullBelow = 0.0012f;

    /// Suffixes the importer writes. Kept here so the two ends of the pipeline agree in one place.
    public const string HiSuffix = "_hi";
    public const string LoSuffix = "_lo";

    /// Build a LOD group on an already-instantiated hull.
    ///
    /// `root` must be the instantiated MID prefab, already scaled and oriented — the extra levels are
    /// parented under it at identity, so everything the caller has already done to position, scale and
    /// recolour the hull applies to them for free. Call it AFTER the livery and the faction tint, so
    /// those run once on the base's materials rather than three times on three copies.
    public static void Attach(GameObject root, string resourcePath)
    {
        if (root == null || string.IsNullOrEmpty(resourcePath)) return;

        var hi = Spawn(root, resourcePath + HiSuffix);
        var lo = Spawn(root, resourcePath + LoSuffix);
        if (hi == null && lo == null) return;      // no siblings: the base stands alone, as designed

        // The base's own renderers, captured BEFORE the new children were parented so the LOD levels
        // do not include each other. Order matters: Spawn already ran, so this has to filter.
        var baseRenderers = Collect(root, hi, lo);
        if (baseRenderers.Length == 0) return;

        // ---- one material set, shared ----
        if (hi != null) Adopt(hi, baseRenderers);
        if (lo != null) Adopt(lo, baseRenderers);

        // ---- the group ----
        var levels = new System.Collections.Generic.List<LOD>(3);
        if (hi != null) levels.Add(new LOD(HighAbove, hi.GetComponentsInChildren<Renderer>()));
        levels.Add(new LOD(hi != null ? MidAbove : HighAbove, baseRenderers));
        if (lo != null) levels.Add(new LOD(CullBelow, lo.GetComponentsInChildren<Renderer>()));
        else
        {
            // No low mesh: the mid one carries on down to the cull threshold rather than the group
            // ending at MidAbove and the ship vanishing the moment it got small.
            levels[levels.Count - 1] = new LOD(CullBelow, baseRenderers);
        }

        // ---- NOT `??`. ----
        //
        // `GetComponent<LODGroup>() ?? AddComponent<LODGroup>()` is the shape this used to be, and it is
        // the single most reliable way to produce a MissingComponentException in Unity. `??` is a C#
        // operator and it tests the REFERENCE; Unity's `== null` is an OVERLOAD that also reports true for
        // a component whose native half has been destroyed. So a destroyed-but-still-referenced LODGroup —
        // which is what a re-used or pooled `Model_Scout` root carries — sails straight through `??`, gets
        // handed back as though it were live, and throws on the first call:
        //
        //     MissingComponentException: There is no 'LODGroup' attached to the "Model_Scout 1" game
        //     object, but a script is trying to access it.
        //
        // UIFactory.Ensure is the same two lines written once, and it uses `== null` so the overload runs.
        var group = UIFactory.Ensure<LODGroup>(root);
        if (group == null) return;
        group.SetLODs(levels.ToArray());

        // CROSS-FADE rather than a hard swap. A hull popping between two silhouettes is the one
        // artefact that makes LOD noticeable, and it is most noticeable exactly where these ships live
        // — a slow zoom, where the swap happens while the player is looking straight at it.
        //
        // If the material's shader has no LOD_FADE_CROSSFADE keyword this degrades to a hard swap,
        // which is what the game does today anyway. Nothing to lose.
        group.fadeMode = LODFadeMode.CrossFade;
        group.animateCrossFading = true;

        group.RecalculateBounds();
    }

    /// Load and parent one LOD sibling, or null if the file is not there.
    static GameObject Spawn(GameObject root, string path)
    {
        var prefab = UnitModelLibrary.Prefab(path, quiet: true);
        if (prefab == null) return null;

        var go = Object.Instantiate(prefab, root.transform);
        go.name = path.Substring(path.LastIndexOf('/') + 1);

        // Identity under the already-scaled, already-oriented base. All three levels come from one
        // source mesh, so they share a pivot and an orientation, and anything else here would put the
        // high-detail hull somewhere the mid one is not.
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // Colliders come from the base only. Three overlapping pick boxes on one ship would make
        // ClickPriority's nearest-hit test a coin flip between three copies of the same hull.
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);

        return go;
    }

    /// The root's own renderers — everything not belonging to a LOD child.
    static Renderer[] Collect(GameObject root, GameObject hi, GameObject lo)
    {
        var all = root.GetComponentsInChildren<Renderer>();
        var keep = new System.Collections.Generic.List<Renderer>(all.Length);
        foreach (var r in all)
        {
            if (hi != null && r.transform.IsChildOf(hi.transform)) continue;
            if (lo != null && r.transform.IsChildOf(lo.transform)) continue;
            keep.Add(r);
        }
        return keep.ToArray();
    }

    /// Hand the base's materials to a LOD level, and match its shadow behaviour.
    ///
    /// By INDEX where the counts line up, and by the first material otherwise. All three levels are
    /// simplifications of one source mesh so their primitive order matches in practice — but the
    /// simplifier can drop a primitive whose triangles all collapse, and a level that came out with
    /// one fewer submesh should end up slightly wrong rather than throwing.
    static void Adopt(GameObject level, Renderer[] baseRenderers)
    {
        var mine = level.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < mine.Length; i++)
        {
            var src = baseRenderers[Mathf.Min(i, baseRenderers.Length - 1)];
            var mats = src.sharedMaterials;
            var want = mine[i].sharedMaterials;

            if (mats.Length == want.Length) mine[i].sharedMaterials = mats;
            else
            {
                for (int k = 0; k < want.Length; k++) want[k] = mats.Length > 0 ? mats[0] : null;
                mine[i].sharedMaterials = want;
            }

            // Only one level is ever visible, so a level whose shadow settings differ from the base's
            // would make the ship's shadow flicker on and off as the camera moved through a threshold.
            mine[i].shadowCastingMode = src.shadowCastingMode;
            mine[i].receiveShadows = src.receiveShadows;
        }
    }
}
