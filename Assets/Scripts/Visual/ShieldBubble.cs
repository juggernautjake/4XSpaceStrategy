using UnityEngine;

// ============================================================================================
// THE SHIELD BUBBLE — what Shields.cs looks like
//
// An ellipsoid fitted round the hull's bounds (a little loose, so the hull never pokes through), drawn
// with FX/ShieldBubble (Resources/Shaders/ShieldBubble.shader) in the race's shield colour
// (RaceVfx.shieldColour). Five states, all driven from the unit's own shield numbers:
//
//   OFF       out of combat with a full screen: the renderer is disabled and costs nothing
//   REST      in a fight: a faint fresnel rim, so you can see which ships are screened
//   HIT       the shell brightens, the pixel-cell pattern shows through, and a ripple ring spreads from
//             the impact point (up to four at once, oldest replaced first)
//   COLLAPSE  the shell stutters out over a third of a second and breaks into shards (ExplosionRenderer)
//   REGEN     the screen comes back from the keel up behind a bright rising band
//
// Fog of war and concealment: ConcealBinding switches the renderer off with the rest of the hull, and
// this never switches it back on while VisibilityService says the ship is not shown. Galaxy zoom is
// handled by MapTierVisibility switching the whole hull off.
//
// Everything per frame goes through one MaterialPropertyBlock, so every bubble in the fleet shares one
// material and nothing allocates.
// ============================================================================================
public class ShieldBubble : MonoBehaviour
{
    static Material shared;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RestId = Shader.PropertyToID("_Rest");
    static readonly int FlashId = Shader.PropertyToID("_Flash");
    static readonly int RegenId = Shader.PropertyToID("_Regen");
    static readonly int FlickerId = Shader.PropertyToID("_Flicker");
    static readonly int CellsId = Shader.PropertyToID("_Cells");
    static readonly int HitsId = Shader.PropertyToID("_Hits");

    /// Settings toggle (SettingsWindow). Off hides every bubble; the mechanic is unaffected.
    public static bool Enabled = true;

    Unit unit;
    MeshRenderer mr;
    MaterialPropertyBlock block;
    Color colour;
    float radius;                 // the largest semi-axis, in WORLD units at build time scale
    readonly Vector4[] hits = new Vector4[4];
    int nextHit;
    float flash;
    float collapseT = -1f;        // seconds into a collapse, or <0
    float regen = 1f;             // 0..1 sweep; 1 = full shell
    float lastShield = -1f;
    float shownCheck;
    bool shown = true;
    float cells;

    /// Build a bubble round `host`'s hull. `localBounds` is the hull in the host's local space.
    public static ShieldBubble Attach(Transform host, Unit u, Bounds localBounds, Color shieldColour)
    {
        if (u == null || u.MaxShield <= 0) return null;
        var mat = SharedMaterial();
        if (mat == null) return null;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ShieldBubble";
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(host, false);
        go.transform.localPosition = localBounds.center;
        go.transform.localRotation = Quaternion.identity;
        // Loose round the hull, and never flatter than half its length, so a slim fighter gets an egg
        // rather than a needle and a disc station gets a lens rather than a plate.
        Vector3 s = localBounds.size;
        float longest = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        Vector3 axes = new Vector3(Mathf.Max(s.x, longest * 0.55f), Mathf.Max(s.y, longest * 0.45f), Mathf.Max(s.z, longest * 0.55f)) * 1.3f;
        go.transform.localScale = axes;

        var b = go.AddComponent<ShieldBubble>();
        b.unit = u;
        b.mr = go.GetComponent<MeshRenderer>();
        b.mr.sharedMaterial = mat;
        b.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        b.mr.receiveShadows = false;
        b.mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        b.mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        b.mr.enabled = false;
        b.block = new MaterialPropertyBlock();
        b.colour = shieldColour;
        b.radius = Mathf.Max(axes.x, Mathf.Max(axes.y, axes.z)) * 0.5f;
        // Bigger shells get more cells so a cell stays roughly the same size on screen across classes.
        b.cells = Mathf.Lerp(14f, 30f, Mathf.InverseLerp(0.1f, 0.5f, longest * host.lossyScale.x));
        for (int i = 0; i < b.hits.Length; i++) b.hits[i] = new Vector4(0f, 1f, 0f, -1f);
        b.lastShield = u.Shield;
        return b;
    }

    static Material SharedMaterial()
    {
        if (shared != null) return shared;
        var sh = Resources.Load<Shader>("Shaders/ShieldBubble");
        if (sh == null) sh = Shader.Find("FX/ShieldBubble");
        if (sh == null || !sh.isSupported) return null;
        shared = new Material(sh) { name = "Shield Bubble (shared)" };
        return shared;
    }

    /// Radius of the shell in world units, right now.
    public float WorldRadius => radius * Mathf.Max(0.0001f, transform.parent != null ? transform.parent.lossyScale.x : 1f);

    /// A round struck the shell. `inbound` is the round's direction of travel (world). Returns the point
    /// on the shell it struck, for the splash.
    public Vector3 Hit(Vector3 inbound, float strength, bool collapsed)
    {
        Vector3 toShooter = inbound.sqrMagnitude > 1e-8f ? -inbound.normalized : Random.onUnitSphere;
        Vector3 local = LocalDir(toShooter);

        hits[nextHit] = new Vector4(local.x, local.y, local.z, 0f);
        nextHit = (nextHit + 1) % hits.Length;
        flash = Mathf.Min(2.5f, flash + 0.6f + strength * 6f);

        if (collapsed)
        {
            collapseT = 0f;
            var er = ExplosionRenderer.Instance;
            if (er != null && FxKit.SystemView) er.ShieldShatter(transform.position, WorldRadius, colour);
        }
        return transform.TransformPoint(local * 0.5f);
    }

    /// The point on the shell facing `fromWorld`, without registering a hit — where a beam should stop.
    public Vector3 SurfacePoint(Vector3 fromWorld)
    {
        Vector3 d = fromWorld - transform.position;
        return transform.TransformPoint(LocalDir(d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.up) * 0.5f);
    }

    /// Object-space unit direction from the centre toward world direction `worldDir`. The shader works
    /// on the unit sphere, so the ellipsoid's scale is divided back out before normalising.
    Vector3 LocalDir(Vector3 worldDir)
    {
        Vector3 local = transform.InverseTransformDirection(worldDir);
        Vector3 s = transform.localScale;
        local = new Vector3(local.x / Mathf.Max(1e-4f, s.x), local.y / Mathf.Max(1e-4f, s.y), local.z / Mathf.Max(1e-4f, s.z));
        if (local.sqrMagnitude < 1e-8f) local = Vector3.up;
        return local.normalized;
    }

    void LateUpdate()
    {
        if (unit == null || mr == null) return;
        float dt = Time.deltaTime;

        // Visibility is asked a few times a second, not every frame: it walks to the hull's binding.
        shownCheck -= Time.unscaledDeltaTime;
        if (shownCheck <= 0f) { shownCheck = 0.25f; shown = VisibilityService.ShownToPlayer(unit); }

        float s = unit.Shield;
        int max = unit.MaxShield;

        // ---- coming back up: start the sweep the moment the shield leaves zero ----
        if (lastShield <= 0.01f && s > 0.01f) { regen = 0f; collapseT = -1f; }
        lastShield = s;
        if (regen < 1f) regen = Mathf.Min(1f, regen + dt * 1.1f);

        for (int i = 0; i < hits.Length; i++)
            if (hits[i].w >= 0f) { hits[i].w += dt; if (hits[i].w > 1.4f) hits[i].w = -1f; }
        flash = Mathf.MoveTowards(flash, 0f, dt * 3.5f);

        bool collapsing = collapseT >= 0f && collapseT < 0.4f;
        if (collapseT >= 0f) collapseT += dt;

        bool inFight = CombatManager.InCombat(unit);
        bool anyHit = flash > 0.01f || HitsLive();
        bool up = s > 0.01f && max > 0;
        bool want = Enabled && shown && FxKit.SystemView && (collapsing || (up && (inFight || anyHit || regen < 1f)));

        if (mr.enabled != want) mr.enabled = want;
        if (!want) return;

        float flicker = 1f;
        if (collapsing)
            // Stutter out: on/off at an uneven rate, dimming as it goes.
            flicker = (Mathf.Repeat(collapseT * 37f, 1f) > 0.45f ? 1f : 0.15f) * (1f - collapseT / 0.4f);
        else if (up && unit.ShieldFraction < 0.25f)
            // A weak screen shimmers unsteadily.
            flicker = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 9f, unit.id * 0.37f);

        float rest = up ? Mathf.Lerp(0.05f, 0.16f, unit.ShieldFraction) : 0f;

        block.Clear();
        block.SetColor(ColorId, new Color(colour.r * 1.6f, colour.g * 1.6f, colour.b * 1.6f, 1f));
        block.SetFloat(RestId, inFight ? rest : rest * 0.4f);
        block.SetFloat(FlashId, flash);
        block.SetFloat(RegenId, collapsing ? 1f : regen);
        block.SetFloat(FlickerId, flicker);
        block.SetFloat(CellsId, cells);
        block.SetVectorArray(HitsId, hits);
        mr.SetPropertyBlock(block);
    }

    bool HitsLive()
    {
        for (int i = 0; i < hits.Length; i++) if (hits[i].w >= 0f) return true;
        return false;
    }
}
