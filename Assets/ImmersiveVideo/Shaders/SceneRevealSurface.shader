// TASK-10 scene-scan-reveal intro. Single shader shared across mesh/plane/proxy
// reveal surfaces. Samples the 360 video equirectangularly from a fixed
// center-eye origin so revealed surfaces show exactly what the equirect
// composition layer will show once it fades in underneath them.
Shader "ImmersiveVideo/SceneRevealSurface"
{
    Properties
    {
        _VideoTex ("Video", 2D) = "black" {}
        _GridColor ("Grid Color", Color) = (0.6, 0.85, 1, 1)
        _GridScale ("Grid Scale", Float) = 4.0
        _GridWidth ("Grid Line Width", Range(0.001, 0.5)) = 0.02
        _ScanBandWidth ("Scan Band Width", Float) = 0.6
        _RevealBandWidth ("Reveal Band Width", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SceneRevealSurface"

            // Premultiplied alpha. With camera background alpha 0, the compositor
            // blends this Default Scene Layer against passthrough using its alpha
            // channel; classic SrcAlpha/OneMinusSrcAlpha accumulates alpha as
            // A^2 + A_dst*(1-A) here and leaves passthrough behind translucent
            // surfaces looking dim/dirty. `col * a` in the fragment return is
            // required for this to be correct - do not drop it.
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_VideoTex);
            SAMPLER(sampler_VideoTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _GridColor;
                float _GridScale;
                float _GridWidth;
                float _ScanBandWidth;
                float _RevealBandWidth;
            CBUFFER_END

            // Driven from C# via Shader.SetGlobalX, shared across every reveal-surface
            // instance regardless of how many exist - intentionally declared outside
            // UnityPerMaterial so the global override actually reaches these variables.
            // _SampleOrigin/_ScanOrigin/_ScanRadius/_RevealRadius/_GlobalAlpha update every
            // frame during the scan (ScanRevealDirector); _VideoYawOffsetDegrees only
            // changes once, whenever the video is (re)centered on the user's gaze
            // (ImmersiveVideoDirector.RecenterVideoToCurrentGaze) - keeping this surface's
            // manual equirect sampling in sync with the actual video layer's rotation
            // instead of a stale fixed calibration angle.
            float4 _SampleOrigin;
            float4 _ScanOrigin;
            float _ScanRadius;
            float _RevealRadius;
            float _GlobalAlpha;
            float _VideoYawOffsetDegrees;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float3 worldPos = IN.positionWS;

                // 1) Sample the video equirectangularly, from a fixed center-eye
                // origin so both eyes sample identically (see shader doc comment
                // in the spec: stereo parallax against an infinite equirect layer
                // would cause a visible jump at the Phase 3 handoff otherwise).
                float3 dir = normalize(worldPos - _SampleOrigin.xyz);

                // Rotate around Y before computing UVs, to align our manual mapping with
                // whatever convention the native equirect layer's compositor actually uses.
                float yawRad = radians(_VideoYawOffsetDegrees);
                float sinYaw, cosYaw;
                sincos(yawRad, sinYaw, cosYaw);
                float3 rotatedDir = float3(
                    dir.x * cosYaw - dir.z * sinYaw,
                    dir.y,
                    dir.x * sinYaw + dir.z * cosYaw);

                // Negated z: without this, longitude runs the opposite way around Y from
                // the native equirect layer's convention, so as the user turns their head
                // the reveal surface's video content pans backwards relative to the real
                // video underneath - a horizontal mirror, not fixable by any yaw offset.
                float2 uv;
                uv.x = atan2(-rotatedDir.z, rotatedDir.x) * (1.0 / (2.0 * PI)) + 0.5;
                uv.y = asin(clamp(rotatedDir.y, -1.0, 1.0)) * (1.0 / PI) + 0.5;
                float3 videoCol = SAMPLE_TEXTURE2D_LOD(_VideoTex, sampler_VideoTex, uv, 0).rgb;

                // 2) Triplanar world-space grid mask, independent of mesh topology.
                float3 g = abs(frac(worldPos * _GridScale) - 0.5);
                float lineMask = 1.0 - smoothstep(0.0, _GridWidth, min(min(g.x, g.y), g.z));

                // 3) Expanding scan wave: bright band at the wavefront, faint
                // grid left behind once the wave has passed.
                float d = distance(worldPos, _ScanOrigin.xyz);
                float scanBand = 1.0 - saturate(abs(d - _ScanRadius) / _ScanBandWidth);
                float scanned = step(d, _ScanRadius);
                float gridAlpha = lineMask * max(scanBand, scanned * 0.35);

                // 4) Video fill mask: a second, slower-expanding sphere.
                float reveal = saturate((_RevealRadius - d) / _RevealBandWidth);

                // 5) Combine (premultiplied).
                float3 col = lerp(_GridColor.rgb, videoCol, reveal);
                float a = max(gridAlpha, reveal) * _GlobalAlpha;

                return float4(col * a, a);
            }
            ENDHLSL
        }
    }
}
