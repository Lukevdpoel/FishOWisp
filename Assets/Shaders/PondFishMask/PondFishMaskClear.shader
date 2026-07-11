// Companion to PondFishMask.shader. Where PondFishMask STAMPS the pond mask (stencil bit 64)
// over the visible water, this one CLEARS that bit again wherever an underwater occluder prop
// (plants, rocks) is the visible surface — so swimming fish are lifted over the seafloor but
// still get occluded by props between them and the camera.
//
// Used as the "Override Material" of a Render Objects feature on the underwater-props layer,
// ordered AFTER "Pond Water Mask" and BEFORE "Fish Over Floor". Writes no colour (ColorMask 0);
// only touches stencil bit 64.
//
// Depth handling — the tricky part. The clear MUST be depth-aware: a long plant that pokes
// THROUGH the floor has a buried portion the terrain hides, and that buried portion must NOT
// clear the mask (otherwise it punches a plant-shaped hole in the fish where the plant is
// actually underground). So ZTest LEqual — clear only where the prop is in front of what's
// already drawn. But a plain LEqual flickers: this pass redraws the prop at its STATIC rest
// pose while the real prop sways (wind/vertex anim), so the two silhouettes drift in and out of
// each other frame to frame.
//
// Fix: bias the clear a few centimetres TOWARD the camera (_DepthBias, in metres) before the
// depth test. The bias only has to be:
//   * LARGER than the prop's sway depth wobble  -> the above-floor part passes LEqual stably (no flicker)
//   * SMALLER than how deep the prop buries     -> the buried part still fails LEqual (stays hidden)
// A long buried plant gives a wide safe gap between those two, so 0.15 m is a fine default;
// raise it if a windy plant still shimmers, lower it if a buried tip starts biting the fish.
Shader "FishOWisp/Pond Fish Mask Clear"
{
    Properties
    {
        _DepthBias("Depth Bias (m)", Float) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "PondMaskClear"
            Tags { "LightMode" = "UniversalForward" }

            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Off

            Stencil
            {
                Ref 0
                WriteMask 64
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _DepthBias;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 toCam = GetCameraPositionWS() - posWS;
                float dist = max(length(toCam), 1e-4);
                posWS += (toCam / dist) * _DepthBias; // nudge toward camera to absorb sway jitter
                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
