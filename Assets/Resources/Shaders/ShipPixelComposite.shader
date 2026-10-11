// Composites the half-resolution ship layer (ShipPixelation.cs) back over the scene.
//
// Point-samples the hull colour and the hull DEPTH at the same texel, discards empty texels, and writes
// the hull's depth so the normal ZTest decides what is in front: a planet nearer the camera still hides
// a ship, and every transparent effect drawn afterwards is still occluded by the hull. Drawn with
// Blitter's fullscreen triangle (Blit.hlsl: Vert, _BlitTexture, _BlitScaleBias).
Shader "Hidden/ShipPixelComposite"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ShipPixelComposite"
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_FLOAT(_ShipPixelDepth);

            struct FragOut
            {
                half4 color : SV_Target;
                float depth : SV_Depth;
            };

            FragOut Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                clip(c.a - 0.5h);

                FragOut o;
                o.color = half4(c.rgb, 1.0h);
                o.depth = SAMPLE_TEXTURE2D(_ShipPixelDepth, sampler_PointClamp, uv).r;
                return o;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
