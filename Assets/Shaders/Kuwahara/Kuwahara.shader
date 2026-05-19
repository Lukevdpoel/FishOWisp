Shader "Hidden/Kuwahara"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        ENDHLSL

        Pass
        {
            Name "KuwaharaFilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment fragKuwahara

            int _KernelSize;

            half4 fragKuwahara(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelSize = _BlitTexture_TexelSize.xy;
                int radius = _KernelSize;

                float3 mean[4];
                float3 variance[4];

                for (int i = 0; i < 4; i++)
                {
                    mean[i] = float3(0, 0, 0);
                    variance[i] = float3(0, 0, 0);
                }

                int sampleCount = (radius + 1) * (radius + 1);

                for (int x = -radius; x <= 0; x++)
                {
                    for (int y = -radius; y <= 0; y++)
                    {
                        float3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(x, y) * texelSize).rgb;
                        mean[0] += col;
                        variance[0] += col * col;
                    }
                }

                for (int x1 = 0; x1 <= radius; x1++)
                {
                    for (int y1 = -radius; y1 <= 0; y1++)
                    {
                        float3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(x1, y1) * texelSize).rgb;
                        mean[1] += col;
                        variance[1] += col * col;
                    }
                }

                for (int x2 = -radius; x2 <= 0; x2++)
                {
                    for (int y2 = 0; y2 <= radius; y2++)
                    {
                        float3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(x2, y2) * texelSize).rgb;
                        mean[2] += col;
                        variance[2] += col * col;
                    }
                }

                for (int x3 = 0; x3 <= radius; x3++)
                {
                    for (int y3 = 0; y3 <= radius; y3++)
                    {
                        float3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(x3, y3) * texelSize).rgb;
                        mean[3] += col;
                        variance[3] += col * col;
                    }
                }

                float minVariance = 1e+10;
                float3 result = float3(0, 0, 0);

                for (int q = 0; q < 4; q++)
                {
                    mean[q] /= sampleCount;
                    variance[q] = variance[q] / sampleCount - mean[q] * mean[q];
                    float totalVariance = variance[q].r + variance[q].g + variance[q].b;

                    if (totalVariance < minVariance)
                    {
                        minVariance = totalVariance;
                        result = mean[q];
                    }
                }

                return half4(result, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment fragComposite

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_KuwaharaTex);
            SAMPLER(sampler_KuwaharaTex);

            float _FocalDistance;
            float _NearRange;
            float _FarRange;
            float _MaxBlend;

            half4 fragComposite(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                half4 sharp = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                half4 kuwahara = SAMPLE_TEXTURE2D(_KuwaharaTex, sampler_KuwaharaTex, uv);

                float depth = SampleSceneDepth(uv);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);

                float diff = linearDepth - _FocalDistance;
                float coc;
                if (diff < 0.0)
                    coc = saturate(-diff / max(_NearRange, 0.001));
                else
                    coc = saturate(diff / max(_FarRange, 0.001));

                coc *= _MaxBlend;

                return lerp(sharp, kuwahara, coc);
            }
            ENDHLSL
        }
    }
}
