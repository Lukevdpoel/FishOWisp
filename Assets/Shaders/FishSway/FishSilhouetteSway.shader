// Unlit flat-color silhouette with procedural swim sway (see FishSway.hlsl).
// Drop-in replacement for the plain URP/Unlit black material on swimming zone fish:
// assign to FishRipple.silhouetteMaterial. FishModelVisual instantiates a copy per fish,
// fills in the spine bounds from the mesh, and drives _ScriptTime/_BendAmount each frame.
//
// Pass layout mirrors URP's Unlit.shader: the color pass is untagged (SRPDefaultUnlit) and
// DepthOnly + DepthNormalsOnly prepasses are REQUIRED in this project — the PC renderer runs
// DBuffer decals (depth+normals prepass) with Forced depth priming, which draws opaques at
// ZTest Equal. A shader missing the prepass passes never matches depth and renders nothing.
Shader "FishOWisp/Fish Silhouette Sway"
{
    Properties
    {
        _BaseColor("Silhouette Color", Color) = (0, 0, 0, 1)

        // Render-state knobs so a fish can fade out: scripts switch these to alpha blending
        // (SrcAlpha / OneMinusSrcAlpha, ZWrite off, transparent queue) for the escape fade.
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1

        [Header(Swim Cycle)]
        _Frequency("Tail Beat Frequency (Hz)", Float) = 1.8
        _BodyWaves("Waves Along Body", Float) = 0.7
        _WaveAmplitude("Wave Amplitude (x body length)", Range(0, 0.3)) = 0.07
        _SideAmplitude("Side Drift (x body length)", Range(0, 0.1)) = 0.015
        _PivotAmount("Yaw Wobble (radians)", Range(0, 0.5)) = 0.06
        _MaskStart("Rigid Head Portion", Range(0, 1)) = 0.35
        _PhaseOffset("Phase Offset", Float) = 0

        [Header(Spine. Auto set from mesh bounds at runtime)]
        _SpineMinZ("Spine Min Z", Float) = -0.5
        _SpineMaxZ("Spine Max Z", Float) = 0.5
        [ToggleUI] _HeadAtMinZ("Head At Min Z", Float) = 1

        [Header(Script driven. FishModelVisual sets these per fish)]
        [ToggleUI] _UseScriptTime("Use Script Time", Float) = 0
        _ScriptTime("Script Time", Float) = 0
        [ToggleUI] _UseChain("Use Trailing Chain (script-driven)", Float) = 0
        _ChainVerticalAmount("Chain Vertical Amount (leap arch)", Range(0, 1)) = 0
        _BendAmount("Turn Bend (x body length, no-chain fallback)", Float) = 0
        _BendTail("Turn Bend Tail Lag (no-chain fallback)", Float) = 0

        // Spawn-in / swim-off dissolve. The fish stays OPAQUE and clips pixels with an ordered
        // dither instead of alpha-blending: the pond water renders at queue 3000 and fakes its
        // transparency by sampling the camera opaque texture, so a swimming fish is only visible
        // BECAUSE it's in that opaque texture (queue Geometry). An alpha fade drops it out of the
        // texture and pops a transparent ghost onto the surface — the dither keeps it opaque so it
        // dissolves cleanly underneath the water. 1 = fully visible, 0 = gone. Script-driven.
        _FadeAmount("Fade Amount (dither dissolve)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
            "Queue" = "Geometry"
        }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "FishSway.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Frequency;
            float _BodyWaves;
            float _WaveAmplitude;
            float _SideAmplitude;
            float _PivotAmount;
            float _MaskStart;
            float _PhaseOffset;
            float _SpineMinZ;
            float _SpineMaxZ;
            float _HeadAtMinZ;
            float _UseScriptTime;
            float _ScriptTime;
            float _UseChain;
            float _ChainVerticalAmount;
            float _BendAmount;
            float _BendTail;
            float _FadeAmount;
        CBUFFER_END

        // Ordered 4x4 Bayer dither used by every pass to dissolve the silhouette while keeping it
        // opaque. Clipping (not blending) means the fish stays in the camera opaque texture the
        // water samples, so it fades underneath the surface instead of ghosting on top of it.
        // Thresholds are (M + 0.5) / 16 so the pattern never fully clips at fade == 1 nor fully
        // survives at fade == 0. The same screen-space decision runs in the colour pass and the
        // depth/depth-normals prepasses, so depth priming (ZTest Equal) stays consistent.
        static const float _FishDitherBayer[16] =
        {
             0.5/16,  8.5/16,  2.5/16, 10.5/16,
            12.5/16,  4.5/16, 14.5/16,  6.5/16,
             3.5/16, 11.5/16,  1.5/16,  9.5/16,
            15.5/16,  7.5/16, 13.5/16,  5.5/16
        };

        void FishFadeClip(float2 positionCS, float fade)
        {
            if (fade >= 1.0) return;
            int2 p = (int2)fmod(positionCS, 4.0);
            clip(fade - _FishDitherBayer[p.y * 4 + p.x]);
        }

        // Trailing-chain spine points in object space, set per fish through a
        // MaterialPropertyBlock (arrays can't live in the SRP-batcher cbuffer; the MPB
        // drops these renderers to the unbatched path, which is fine at pond scale).
        float4 _ChainPoints[FISH_CHAIN_COUNT];

        // Two clocks: _Time.y for set-and-forget materials (tank fish), the script-driven
        // clock for zone fish — accumulating phase on the CPU lets the wiggle speed change
        // with fish state without the phase pop a raw time*speed multiply would cause.
        //
        // Two body modes: chain skinning (zone fish — FishModelVisual simulates a trailing
        // spine and uploads it per fish) or the analytic wave+bend displacement (fallback
        // for materials nothing drives).
        //
        // Every pass MUST displace through this same function: depth priming compares the
        // prepass depth against the color pass at ZTest Equal, so the positions have to match.
        float3 SwayPositionOS(float3 positionOS)
        {
            float t = lerp(_Time.y, _ScriptTime, saturate(_UseScriptTime)) + _PhaseOffset;

            if (_UseChain > 0.5)
            {
                float bodyLength = max(_SpineMaxZ - _SpineMinZ, 1e-4);
                float dirZ = (_HeadAtMinZ < 0.5) ? -1.0 : 1.0;
                float headZ = (_HeadAtMinZ < 0.5) ? _SpineMaxZ : _SpineMinZ;
                float s = saturate((positionOS.z - headZ) * dirZ / bodyLength);
                float phase = t * _Frequency * TWO_PI;
                float waveLateral = FishSwayLateralWave(s, phase, bodyLength, _BodyWaves,
                                                        _WaveAmplitude, _SideAmplitude,
                                                        _PivotAmount, _MaskStart);
                return FishChainSkin(positionOS, waveLateral, _ChainPoints,
                                     _SpineMinZ, _SpineMaxZ, _HeadAtMinZ, _ChainVerticalAmount);
            }

            return FishSwayDisplace(
                positionOS, t, _SpineMinZ, _SpineMaxZ, _HeadAtMinZ,
                _Frequency, _BodyWaves, _WaveAmplitude, _SideAmplitude,
                _PivotAmount, _MaskStart, _BendAmount, _BendTail);
        }
        ENDHLSL

        // Color pass. Deliberately untagged: passes without a LightMode resolve to
        // SRPDefaultUnlit, which URP draws in both Forward and Deferred opaque passes.
        Pass
        {
            Name "Unlit"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex UnlitVert
            #pragma fragment UnlitFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings UnlitVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(SwayPositionOS(input.positionOS.xyz));
                return output;
            }

            half4 UnlitFrag(Varyings input) : SV_Target
            {
                FishFadeClip(input.positionCS.xy, _FadeAmount);
                return _BaseColor;
            }
            ENDHLSL
        }

        // "Lift over floor" pass — a screen-masked copy of the colour pass that draws the
        // silhouette ON TOP of the pond floor so the seafloor mesh never clips into it. A Render
        // Objects feature runs this AFTER the opaque pass, once PondFishMask.shader has stamped
        // stencil bit 64 over the visible pond water (hills cut themselves out of that stamp, so
        // they still occlude fish):
        //   ZTest Always   -> ignore the seafloor's depth; the floor can't clip the fish
        //   Stencil == 64  -> but only where water is actually visible, never over the hills
        // The fish keep their normal opaque render too (this only OVERDRAWS inside the mask), so a
        // fish that leaves the water — no water behind it, bit unset — simply falls back to normal
        // depth sorting with nothing extra. Same SwayPositionOS + dither as the colour pass, so the
        // silhouette and any spawn/despawn dissolve stay identical. ZWrite Off: we don't disturb
        // the depth buffer (the water's depth fade reads the prepass depth, captured earlier).
        Pass
        {
            Name "FishOverFloor"
            Tags { "LightMode" = "FishOverFloor" }
            ZWrite Off
            ZTest Always
            Cull Back

            Stencil
            {
                Ref 64
                ReadMask 64
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex UnlitVert
            #pragma fragment UnlitFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings UnlitVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(SwayPositionOS(input.positionOS.xyz));
                return output;
            }

            half4 UnlitFrag(Varyings input) : SV_Target
            {
                FishFadeClip(input.positionCS.xy, _FadeAmount);
                return _BaseColor;
            }
            ENDHLSL
        }

        // Depth prepass (no normals needed, e.g. depth texture only).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(SwayPositionOS(input.positionOS.xyz));
                return output;
            }

            half DepthOnlyFrag(Varyings input) : SV_Target
            {
                FishFadeClip(input.positionCS.xy, _FadeAmount);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth+normals prepass — this is the one the DBuffer decal feature triggers every
        // frame, and the pass depth priming primes from. The normal is the undisplaced
        // geometric normal; nothing lighting-critical reads it for a flat black silhouette.
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(SwayPositionOS(input.positionOS.xyz));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                FishFadeClip(input.positionCS.xy, _FadeAmount);
                return half4(normalize(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
