using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// ============================================================================================
// SHIP PIXELATION — the hulls, and only the hulls, drawn at half resolution
//
// The brief: "keep the ship models unique and cool looking, just make them slightly pixelated". So the
// ship meshes are rendered into a half-size target with POINT filtering and composited back over the
// scene, depth-tested against it, while planets, stars, effects and UI stay sharp.
//
// ---- HOW --------------------------------------------------------------------------------------
//
//   1. Every hull renderer (ShipLights.HullRenderers, every LOD level) goes on layer `Layer` (8, which
//      nothing else in the project uses) while the effect is on.
//   2. Both URP renderer assets (Assets/Settings/PC_Renderer.asset, Mobile_Renderer.asset) leave that
//      layer OUT of their opaque layer mask, so URP's own opaque pass no longer draws the hulls.
//   3. ShipPixelPass, enqueued on every base game camera from RenderPipelineManager.beginCameraRendering
//      (no renderer-feature asset to wire, so nothing references a script GUID), draws the hull layer
//      from the camera's OWN cull results — so lighting, Forward+ light lists and LOD are exactly what
//      they were — into a half-res HDR colour + depth pair, then composites it onto the camera target
//      with a fullscreen triangle that point-samples both, clips the empty texels and writes the hull's
//      depth (SV_Depth) under a normal ZTest. Planets in front of a ship still hide it, and the
//      transparent effects drawn after it (plumes, shields, sparks) are still occluded by it.
//
// Forward+ (the PC renderer) finds its light clusters from the pixel position divided by
// _ScaledScreenParams, so that is set to the half-res size for the draw and put back after it.
//
// ---- WHY IT CANNOT LOSE THE FLEET ----------------------------------------------------------------
//
// Hulls are only moved onto the layer while this is ON. If the pass ever fails to run (a URP change, a
// shader that did not compile, an exception), a watchdog notices (60 queued renders that never ran), puts every hull
// back on Default, and switches itself off for the session with one warning in the console — so the
// worst case is sharp ships, never invisible ones. The Scene view and any non-main camera get a
// full-resolution draw of the same layer, so the editor still shows the fleet.
//
// Settings: "Pixelated ships" (SettingsWindow), on by default, PlayerPrefs "Graphics.ShipPixelation".
// ============================================================================================
public static class ShipPixelation
{
    /// The layer the hulls are moved to. Unnamed in TagManager, used by nothing else (checked
    /// 2026-10-11: scenes and prefabs use 0 and 5 only).
    public const int Layer = 8;

    /// Texels per pixel side. 2 = half resolution: chunky enough to read as pixel art on a hull 40 px
    /// long, fine enough that a fighter is still a shape.
    public const int Factor = 2;

    const string PrefKey = "Graphics.ShipPixelation";
    static bool loaded, on = true;

    /// The player's setting.
    public static bool On
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                try { on = PlayerPrefs.GetInt(PrefKey, 1) != 0; } catch { on = true; }
            }
            return on;
        }
    }

    public static void Set(bool value)
    {
        if (value == On) return;
        on = value;
        try { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); } catch { }
        Refresh();
    }

    /// True once the watchdog has given up on the pass for this session.
    public static bool Failed { get; private set; }

    /// Is the effect actually running: wanted, not failed, and the composite shader present.
    public static bool Active => On && !Failed && CompositeMaterial() != null;

    // ---- the hulls -----------------------------------------------------------------------------
    class Entry { public Renderer[] renderers; public int originalLayer; }
    static readonly List<Entry> tracked = new List<Entry>();
    static bool applied;

    /// Called by ShipLights.Init with the hull's own renderers. Puts them on the layer if the effect is
    /// running; remembers them either way so the setting can flip them later.
    public static void Track(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0) return;
        EnsureHooked();
        int orig = 0;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) { orig = renderers[i].gameObject.layer == Layer ? 0 : renderers[i].gameObject.layer; break; }
        var e = new Entry { renderers = renderers, originalLayer = orig };
        tracked.Add(e);
        SetLayer(e, Active);
        if (Active && !applied) { applied = true; appliedFrame = Time.frameCount; }
    }

    /// Re-apply the layer to every live hull (the setting changed, or the watchdog tripped).
    public static void Refresh()
    {
        bool want = Active;
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            if (!Alive(tracked[i])) { tracked.RemoveAt(i); continue; }
            SetLayer(tracked[i], want);
        }
        if (want && !applied) appliedFrame = Time.frameCount;
        applied = want;
    }

    static bool Alive(Entry e)
    {
        for (int i = 0; i < e.renderers.Length; i++) if (e.renderers[i] != null) return true;
        return false;
    }

    static void SetLayer(Entry e, bool pixel)
    {
        int layer = pixel ? Layer : e.originalLayer;
        for (int i = 0; i < e.renderers.Length; i++)
        {
            var r = e.renderers[i];
            if (r != null && r.gameObject.layer != layer) r.gameObject.layer = layer;
        }
    }

    // ---- the watchdog --------------------------------------------------------------------------
    // Counted in camera renders, not frames: a frame in which no camera draws (a loading screen, a
    // minimised window) proves nothing either way. What proves a failure is the pass being HANDED to
    // URP again and again and never once executing.
    static int enqueuedSinceRun;
    static int appliedFrame;

    /// Called from the pass's render functions: proof the hulls were drawn.
    public static void MarkRan() => enqueuedSinceRun = 0;

    /// Driven by ShipPixelationDriver once a frame.
    internal static void Watch()
    {
        if (!applied || Failed) return;
        // Pruning dead hulls here keeps `tracked` from growing across a long session of losses.
        if ((Time.frameCount & 255) == 0)
            for (int i = tracked.Count - 1; i >= 0; i--) if (!Alive(tracked[i])) tracked.RemoveAt(i);

        if (enqueuedSinceRun <= 60) return;

        Failed = true;
        Debug.LogWarning("[ShipPixelation] The ship pixelation pass was queued 60 times without running, so it has " +
                         "been switched off for this session and every hull is back on its normal layer " +
                         "(sharp, never invisible). Started on frame " + appliedFrame + ".");
        Refresh();
    }

    // ---- the pass, and when it is enqueued ------------------------------------------------------
    static bool hooked;
    static ShipPixelPass pass;
    static Material material;
    static bool materialTried;

    static Material CompositeMaterial()
    {
        if (material != null || materialTried) return material;
        materialTried = true;
        var sh = Resources.Load<Shader>("Shaders/ShipPixelComposite");
        if (sh == null) sh = Shader.Find("Hidden/ShipPixelComposite");
        if (sh == null || !sh.isSupported)
        {
            Debug.LogWarning("[ShipPixelation] Resources/Shaders/ShipPixelComposite is missing or unsupported; ships stay sharp.");
            return null;
        }
        material = new Material(sh) { name = "Ship Pixel Composite", hideFlags = HideFlags.DontSave };
        return material;
    }

    static void EnsureHooked()
    {
        if (hooked) return;
        hooked = true;
        pass = new ShipPixelPass();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        ShipPixelationDriver.Create();
    }

    static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (!applied || Failed || cam == null || pass == null) return;
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.renderType != CameraRenderType.Base) return;
        var renderer = data.scriptableRenderer;
        if (renderer == null) return;
        pass.composite = CompositeMaterial();
        renderer.EnqueuePass(pass);
        enqueuedSinceRun++;
    }

    /// Create the per-frame driver (GameBootstrap). Track does this too, lazily.
    public static void Create() => EnsureHooked();
}

/// One frame tick for the watchdog. A component rather than a static update so it exists only once
/// the effect does, and dies with the scene.
public class ShipPixelationDriver : MonoBehaviour
{
    static ShipPixelationDriver instance;

    public static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("ShipPixelation");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ShipPixelationDriver>();
    }

    void LateUpdate() => ShipPixelation.Watch();
}

/// The render pass itself. See ShipPixelation's header.
public class ShipPixelPass : ScriptableRenderPass
{
    public Material composite;

    class DrawData
    {
        public RendererListHandle list;
        public Vector4 half, full;
    }

    class CompositeData
    {
        public TextureHandle color, depth;
        public Material material;
    }

    static readonly List<ShaderTagId> Tags = new List<ShaderTagId>
    {
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly"),
        new ShaderTagId("SRPDefaultUnlit"),
    };

    static readonly int ScaledScreenParamsId = Shader.PropertyToID("_ScaledScreenParams");
    static readonly int DepthId = Shader.PropertyToID("_ShipPixelDepth");

    public ShipPixelPass()
    {
        // After the opaques (so planets are in the depth buffer to test against) and before the
        // skybox, the opaque-texture copy and every transparent effect.
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    // Lit hull materials sample the shadow maps and SSAO, so declare them as inputs (as URP RenderObjectsPass does).

    static void UseLightingInputs(IRasterRenderGraphBuilder builder, UniversalResourceData resources)

    {

        if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);

        if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);

        if (resources.ssaoTexture.IsValid()) builder.UseTexture(resources.ssaoTexture, AccessFlags.Read);

    }


    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var resources = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var lightData = frameData.Get<UniversalLightData>();

        var drawSettings = RenderingUtils.CreateDrawingSettings(Tags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
        var filter = new FilteringSettings(RenderQueueRange.opaque, 1 << ShipPixelation.Layer);
        var listParams = new RendererListParams(renderingData.cullResults, drawSettings, filter);
        RendererListHandle list = renderGraph.CreateRendererList(listParams);

        int fw = Mathf.Max(1, cameraData.scaledWidth);
        int fh = Mathf.Max(1, cameraData.scaledHeight);

        // Pixelate only the main game view, and only into an intermediate target (the half-res texture
        // and the camera target must share the same projection-flip convention). Anything else gets
        // the same hulls at full resolution, straight into its target.
        bool pixel = composite != null
                     && cameraData.cameraType == CameraType.Game
                     && !resources.isActiveTargetBackBuffer
                     && fw >= 8 && fh >= 8;

        if (!pixel)
        {
            using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Ships (full resolution)", out var data))
            {
                data.list = list;
                builder.UseRendererList(list);
                UseLightingInputs(builder, resources);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(d.list);
                    ShipPixelation.MarkRan();
                });
            }
            return;
        }

        int hw = Mathf.Max(1, fw / ShipPixelation.Factor);
        int hh = Mathf.Max(1, fh / ShipPixelation.Factor);

        var colorDesc = new TextureDesc(hw, hh)
        {
            format = GraphicsFormat.R16G16B16A16_SFloat,   // HDR (the hull glow must still bloom) WITH alpha
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            clearBuffer = true,
            clearColor = Color.clear,
            name = "ShipPixelColor",
        };
        var depthDesc = new TextureDesc(hw, hh)
        {
            format = GraphicsFormat.D32_SFloat,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            clearBuffer = true,
            name = "ShipPixelDepth",
        };
        TextureHandle color = renderGraph.CreateTexture(colorDesc);
        TextureHandle depth = renderGraph.CreateTexture(depthDesc);

        // ---- 1. the hulls, at half resolution ----
        using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Ships (pixel draw)", out var data))
        {
            data.list = list;
            data.half = new Vector4(hw, hh, 1f + 1f / hw, 1f + 1f / hh);
            data.full = new Vector4(fw, fh, 1f + 1f / fw, 1f + 1f / fh);
            builder.UseRendererList(list);
            UseLightingInputs(builder, resources);
            builder.SetRenderAttachment(color, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) =>
            {
                // Forward+ finds a pixel's light cluster from SV_Position / _ScaledScreenParams; at half
                // size the pixel positions are half as big, so the divisor has to be as well.
                ctx.cmd.SetGlobalVector(ScaledScreenParamsId, d.half);
                ctx.cmd.DrawRendererList(d.list);
                ctx.cmd.SetGlobalVector(ScaledScreenParamsId, d.full);
            });
        }

        // ---- 2. composite, point-sampled and depth-tested ----
        using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Ships (pixel composite)", out var data))
        {
            data.color = color;
            data.depth = depth;
            data.material = composite;
            builder.UseTexture(color, AccessFlags.Read);
            builder.UseTexture(depth, AccessFlags.Read);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((CompositeData d, RasterGraphContext ctx) =>
            {
                d.material.SetTexture(DepthId, (Texture)d.depth);
                Blitter.BlitTexture(ctx.cmd, (RTHandle)d.color, new Vector4(1f, 1f, 0f, 0f), d.material, 0);
                ShipPixelation.MarkRan();
            });
        }
    }
}
