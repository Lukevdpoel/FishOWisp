void GetAtten_float(float3 WorldPos, out float ShadowAtten)
{
    #if SHADERGRAPH_PREVIEW
        ShadowAtten = 1.0f;
    #else
        float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);

        #if !defined(_MAIN_LIGHT_SHADOWS) || defined(_RECEIVE_SHADOWS_OFF)
            ShadowAtten = 1.0f;
        #endif

        ShadowSamplingData shadowSamplingData = GetMainLightShadowSamplingData();
        float shadowStrength = GetMainLightShadowStrength();
        ShadowAtten = SampleShadowmap(shadowCoord, TEXTURE2D_ARGS(_MainLightShadowmapTexture,
            sampler_MainLightShadowmapTexture),
            shadowSamplingData, shadowStrength, false);
    #endif
}