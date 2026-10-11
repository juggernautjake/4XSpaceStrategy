// The shield shell round a hull (ShieldBubble.cs).
//
//   rest      a faint fresnel rim (_Rest), only while the ship is in a fight
//   hit       the whole shell brightens (_Flash) and the PIXEL CELL pattern shows through, and up to
//             four ripple rings spread from impact points (_Hits: xyz = object-space direction, w = age)
//   collapse  _Flicker drops the shell out in stutters as it fails
//   regen     _Regen sweeps a bright band up the shell from the keel; above it nothing is drawn yet
//
// The cell pattern is a brick-offset grid in latitude/longitude, quantised to whole cells, so it reads
// as chunky pixels in the game's art style rather than as a smooth sci-fi hex mesh. Additive blend.
Shader "FX/ShieldBubble"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.4, 0.7, 1, 1)
        _Rest ("Rest alpha", Range(0, 1)) = 0.12
        _Flash ("Flash", Range(0, 4)) = 0
        _Regen ("Regen sweep", Range(0, 1)) = 1
        _Flicker ("Flicker", Range(0, 1)) = 1
        _Cells ("Cells round the equator", Float) = 18
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ShieldBubble"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Rest;
                half _Flash;
                half _Regen;
                half _Flicker;
                float _Cells;
            CBUFFER_END
            float4 _Hits[4];

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionHCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.dirOS = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dirOS);
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                half fres = pow(1.0h - saturate(dot(n, viewDir)), 3.0h);

                // ---- the pixel cells ----
                float lat = asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5;          // 0..1 keel to crown
                float lon = atan2(d.z, d.x) / (2.0 * PI) + 0.5;               // 0..1 round the waist
                float rows = max(4.0, floor(_Cells * 0.5));
                float row = floor(lat * rows);
                float2 cell = float2(lon * _Cells + fmod(row, 2.0) * 0.5, lat * rows);
                float2 f = frac(cell);
                half edge = (f.x < 0.14 || f.y < 0.14) ? 1.0h : 0.0h;       // a one-"pixel" border per cell
                half pattern = 0.35h + 0.65h * edge;

                // ---- ripples from the last few impacts ----
                half ripple = 0.0h, splash = 0.0h;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float4 h = _Hits[k];
                    // A mask rather than `continue`: an unrolled loop with an early-out is legal HLSL
                    // but not every backend's compiler likes it, and this costs nothing.
                    half live = (h.w >= 0.0 && h.w <= 1.2) ? 1.0h : 0.0h;
                    float age = max(h.w, 0.0);
                    float ang = acos(clamp(dot(d, normalize(h.xyz + 1e-5)), -1.0, 1.0));   // 0..PI
                    float r = age * 2.6;                                     // radians the ring has spread
                    float life = saturate(1.0 - age / 1.2);
                    float rr = (ang - r) / 0.16;
                    ripple += exp(-rr * rr) * life * live;
                    splash += exp(-ang * 7.0) * saturate(1.0 - age * 3.0) * live;
                }

                // ---- regen sweep ----
                half below = lat <= _Regen ? 1.0h : 0.0h;
                float bz = (lat - _Regen) / 0.05;   // squared by hand: pow() of a negative base is NaN on some GPUs
                half band = (_Regen < 0.999h) ? exp(-bz * bz) : 0.0h;

                half a = _Rest * fres
                       + _Flash * (0.35h * fres + 0.25h) * pattern
                       + ripple * (0.6h + 0.8h * pattern)
                       + splash * 1.6h;
                a = a * below + band * (0.9h * pattern + 0.3h);
                a *= _Flicker;
                return half4(_Color.rgb * a, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
