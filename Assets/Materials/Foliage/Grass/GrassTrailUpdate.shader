Shader "Hidden/GrassTrailUpdate"
{
    Properties
    {
        _MainTex ("Previous Trail", 2D) = "black" {}
        _FadeAmount ("Fade Amount", Float) = 0.985
        _DeltaUV ("Player Delta UV", Vector) = (0,0,0,0)
        _StampRadiusUV ("Stamp Radius UV", Float) = 0.05
        _StampStrength ("Stamp Strength", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _FadeAmount;
            float4 _DeltaUV;
            float _StampRadiusUV;
            float _StampStrength;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float Frag(Varyings IN) : SV_Target
            {
                // Read previous frame, shifted to compensate for player movement
                // so the trail stays anchored in world space.
                float2 prevUV = IN.uv + _DeltaUV.xy;
                float prev = 0.0;
                if (prevUV.x > 0.0 && prevUV.x < 1.0 && prevUV.y > 0.0 && prevUV.y < 1.0)
                {
                    prev = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, prevUV).r;
                }
                prev *= _FadeAmount;

                // Stamp at the player position (always center of RT)
                float d = distance(IN.uv, float2(0.5, 0.5));
                float stamp = saturate(1.0 - d / max(_StampRadiusUV, 1e-4));
                stamp *= stamp;
                stamp *= _StampStrength;

                return saturate(max(prev, stamp));
            }
            ENDHLSL
        }
    }
}
