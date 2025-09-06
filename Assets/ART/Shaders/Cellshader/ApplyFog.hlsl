void Applyfog_float(float4 inColor, float3 positionWS, out float4 outColor)
{
    float4 color = inColor;
    
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
    float viewZ = -TransformWorldToView(positionWS).z;
    float nearZ0ToFarZ = max(viewZ - _ProjectionParams.y, 0);
    float density = 1.0f - ComputeFogIntensity(ComputeFogFactorZ0ToFar(nearZ0ToFarZ));

    outColor = lerp(color, unity_FogColor,  density);

#else
    outColor = color;
#endif
}