Shader "Hidden/RadialBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "RadialBlur"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

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

            // Parameters passed from C#
            float4 _Center;
            float _Intensity;
            float _BlurWidth;
            int _NumSamples;
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 center = _Center.xy;
                float2 blurVector = (center - uv) * _BlurWidth;
                blurVector /= _NumSamples;

                half4 color = half4(0,0,0,0);

                [unroll(32)]
                for(int j = 0; j < _NumSamples; j++)
                {
                    half4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                    sample *= 1.0f - ((float)j / _NumSamples); 
                    color += sample;
                    uv += blurVector;
                }

                return color * _Intensity;
            }
            ENDHLSL
        }
    }
}