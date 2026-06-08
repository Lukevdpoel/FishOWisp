Shader "Custom/BackdropKuwahara"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        _KernelSize ("Kernel Size", Range(1, 8)) = 4
        _Intensity ("Intensity (0 = sharp, 1 = full Kuwahara)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        ZTest LEqual
        ZWrite On
        Cull Back

        Pass
        {
            Name "BackdropKuwahara"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseMap_TexelSize;
                float _KernelSize;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 texelSize = _BaseMap_TexelSize.xy;

                half4 sharp = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);

                // Skip the heavy work entirely when intensity is effectively zero.
                if (_Intensity <= 0.001)
                    return sharp;

                int radius = (int)round(_KernelSize);

                float3 mean[4];
                float3 variance[4];
                for (int i = 0; i < 4; i++)
                {
                    mean[i] = float3(0, 0, 0);
                    variance[i] = float3(0, 0, 0);
                }

                int sampleCount = (radius + 1) * (radius + 1);

                // Quadrant 0: top-left
                for (int x0 = -radius; x0 <= 0; x0++)
                for (int y0 = -radius; y0 <= 0; y0++)
                {
                    float3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(x0, y0) * texelSize).rgb;
                    mean[0] += col;
                    variance[0] += col * col;
                }

                // Quadrant 1: top-right
                for (int x1 = 0; x1 <= radius; x1++)
                for (int y1 = -radius; y1 <= 0; y1++)
                {
                    float3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(x1, y1) * texelSize).rgb;
                    mean[1] += col;
                    variance[1] += col * col;
                }

                // Quadrant 2: bottom-left
                for (int x2 = -radius; x2 <= 0; x2++)
                for (int y2 = 0; y2 <= radius; y2++)
                {
                    float3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(x2, y2) * texelSize).rgb;
                    mean[2] += col;
                    variance[2] += col * col;
                }

                // Quadrant 3: bottom-right
                for (int x3 = 0; x3 <= radius; x3++)
                for (int y3 = 0; y3 <= radius; y3++)
                {
                    float3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(x3, y3) * texelSize).rgb;
                    mean[3] += col;
                    variance[3] += col * col;
                }

                float minVariance = 1e+10;
                float3 kuwaharaResult = float3(0, 0, 0);
                for (int q = 0; q < 4; q++)
                {
                    mean[q] /= sampleCount;
                    variance[q] = variance[q] / sampleCount - mean[q] * mean[q];
                    float totalVariance = variance[q].r + variance[q].g + variance[q].b;
                    if (totalVariance < minVariance)
                    {
                        minVariance = totalVariance;
                        kuwaharaResult = mean[q];
                    }
                }

                half3 final = lerp(sharp.rgb, kuwaharaResult, saturate(_Intensity));
                return half4(final, 1.0);
            }
            ENDHLSL
        }
    }
}
