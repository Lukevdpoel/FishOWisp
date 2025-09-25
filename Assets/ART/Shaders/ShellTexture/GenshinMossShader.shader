Shader "Gemini/GenshinMossShader"
{
    Properties
    {
        [Header(Moss Shape & Layers)]
        _LayerCount("Layer Count", Range(1, 64)) = 24
        _MossThickness("Moss Thickness", Range(0, 0.2)) = 0.05
        _MossPattern("Moss Pattern (Noise)", 2D) = "white" {}
        _MossPlacementMask("Moss Placement Mask (R)", 2D) = "white" {}

        [Header(Moss Coloring)]
        _LitColor("Lit Color", Color) = (0.5, 0.8, 0.2, 1.0)
        _ShadowColor("Shadow Color", Color) = (0.2, 0.4, 0.1, 1.0)
        _DeepColorFactor("Inner Shadow Factor", Range(0, 1)) = 0.5

        [Header(Lighting Style)]
        _CelShadeThreshold("Cel Shade Threshold", Range(0, 1)) = 0.6
        _RimColor("Rim Color", Color) = (0.8, 1.0, 0.5, 1.0)
        _RimPower("Rim Power", Range(1, 10)) = 3.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalRenderPipeline" }
        LOD 100
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct v2g
            {
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : NORMAL;
                float2 uv           : TEXCOORD1;
            };

            struct g2f
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : NORMAL;
                float2 uv           : TEXCOORD1;
                float  layerHeight  : TEXCOORD2;
            };

            int _LayerCount;
            float _MossThickness;
            sampler2D _MossPattern;
            sampler2D _MossPlacementMask;
            float4 _MossPattern_ST;
            float4 _MossPlacementMask_ST;
            float4 _LitColor;
            float4 _ShadowColor;
            float _DeepColorFactor;
            float _CelShadeThreshold;
            float4 _RimColor;
            float _RimPower;

            v2g vert (Attributes v)
            {
                v2g o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            [maxvertexcount(192)]
            void geom(triangle v2g i[3], inout TriangleStream<g2f> triStream)
            {
                for (int layer = 0; layer < _LayerCount; ++layer)
                {
                    float layerPercent = (float)layer / _LayerCount;
                    float offset = layerPercent * _MossThickness;

                    g2f o;
                    for (int v = 0; v < 3; ++v)
                    {
                        o.positionWS = i[v].positionWS + i[v].normalWS * offset;
                        o.positionCS = TransformWorldToHClip(o.positionWS);
                        o.normalWS = i[v].normalWS;
                        o.uv = i[v].uv;
                        o.layerHeight = layerPercent;
                        triStream.Append(o);
                    }
                    triStream.RestartStrip();
                }
            }
            
            float4 frag (g2f i) : SV_Target
            {
                float mask = tex2D(_MossPlacementMask, i.uv).r;
                clip(mask - 0.1);

                float pattern = tex2D(_MossPattern, i.uv * _MossPattern_ST.xy + _MossPattern_ST.zw).r;
                clip(pattern - i.layerHeight);
                
                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                i.normalWS = normalize(i.normalWS);

                float NdotL = saturate(dot(i.normalWS, lightDir));
                float cel = smoothstep(_CelShadeThreshold - 0.05, _CelShadeThreshold + 0.05, NdotL);
                float4 baseColor = lerp(_ShadowColor, _LitColor, cel);

                baseColor.rgb = lerp(baseColor.rgb * _DeepColorFactor, baseColor.rgb, i.layerHeight);

                float fresnel = 1.0 - saturate(dot(viewDir, i.normalWS));
                fresnel = pow(fresnel, _RimPower);
                float4 rim = fresnel * _RimColor * cel;

                float4 finalColor = baseColor + rim;
                return finalColor;
            }
            ENDHLSL
        }
    }
}