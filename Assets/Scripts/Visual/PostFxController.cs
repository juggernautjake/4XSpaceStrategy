using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Adds cinematic post-processing at runtime using URP's BUILT-IN effects — no downloads required.
// Bloom makes the star, atmospheres and orbit rings glow; a gentle vignette + ACES tonemapping keep
// the foreground readable against the dark of space. All created in code and guarded so it degrades
// gracefully if the pipeline can't support it.
public class PostFxController : MonoBehaviour
{
    public static void Create()
    {
        if (FindObjectOfType<PostFxController>() != null) return;
        new GameObject("PostFX").AddComponent<PostFxController>();
    }

    void Start()
    {
        try
        {
            var cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = true;
            }

            var vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = profile;

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(1.05f);   // only bright things bloom -> keeps contrast
            bloom.scatter.Override(0.6f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.26f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(Color.black);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.1f);
            color.saturation.Override(6f);

            Debug.Log("[PostFX] URP post-processing enabled (Bloom, Vignette, ACES).");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[PostFX] Post-processing unavailable, continuing without it: " + e.Message);
        }
    }
}
