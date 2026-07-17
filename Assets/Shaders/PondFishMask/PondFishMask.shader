// Stamps the VISIBLE pond-water region into a dedicated stencil bit, so swimming fish can be
// drawn on top of the seafloor WITHOUT also drawing over the foreground hills.
//
// Used as the "Override Material" of a Render Objects feature that re-draws the Water layer at
// AfterRenderingOpaques. Because it runs ZTest LEqual against the already-rendered opaque scene,
// any water pixel hidden behind a hill is depth-rejected and never stamps the bit — so the
// stencil ends up marking exactly "water the camera can actually see." It writes NO colour
// (ColorMask 0); its only job is the stencil stamp.
//
// Stencil bit 64 is isolated with ReadMask/WriteMask so it can't disturb URP's own low-bit
// stencil usage or the DBuffer decal feature already running in this renderer. The matching
// read of this bit lives in the "FishOverFloor" pass of FishSilhouetteSway.shader.
Shader "FishOWisp/Pond Fish Mask"
{
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
            Name "PondMaskStamp"
            Tags { "LightMode" = "UniversalForward" }

            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Off

            Stencil
            {
                Ref 64
                WriteMask 64
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // The GPU Resident Drawer batches the water planes this feature re-draws, so the
            // override pass must have a DOTS-instanced variant or BRG draws it with the error shader.
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
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
