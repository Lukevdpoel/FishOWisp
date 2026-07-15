#ifndef FUR_SHELL_LIT_BAKED_HLSL
#define FUR_SHELL_LIT_BAKED_HLSL

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "./Param.hlsl"
#include "../Common/Common.hlsl"
#include "./ShellBaked.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    float2 shellData : TEXCOORD3; // x: shell index, y: normalized layer
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 tangentWS : TEXCOORD2;
    float2 uv : TEXCOORD4;
    DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
    float4 fogFactorAndVertexLight : TEXCOORD6; // x: fogFactor, yzw: vertex light
    float  layer : TEXCOORD7;
    float  mask : TEXCOORD8;
};

Varyings vert(Attributes input)
{
    Varyings output = (Varyings)0;

    ShellVertexOutput s = ComputeBakedShell(
        input.positionOS.xyz, input.normalOS, input.tangentOS, input.color, input.shellData);

    output.positionWS = s.positionWS;
    output.positionCS = s.positionCS;
    output.normalWS = s.normalWS;
    output.tangentWS = s.tangentWS;
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.layer = s.layer;
    output.mask = s.mask;

    float3 vertexLight = VertexLighting(s.positionWS, s.normalWS);
    float fogFactor = ComputeFogFactor(s.positionCS.z);
    output.fogFactorAndVertexLight = float4(fogFactor, vertexLight);

    OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
    OUTPUT_SH(output.normalWS.xyz, output.vertexSH);
    return output;
}

float4 frag(Varyings input) : SV_Target
{
    float2 furUv = input.uv / _BaseMap_ST.xy * _FurScale;
    float4 furColor = SAMPLE_TEXTURE2D(_FurMap, sampler_FurMap, furUv);

    // World-space noise-perturbed mask (rounds the jagged per-vertex edge). Shared with the depth
    // passes via ComputeNoisyMask so the shell footprint matches everywhere.
    float noisyMask = ComputeNoisyMask(input.mask, input.positionWS);

    // OPAQUE. The flat base (layer 0) blends its albedo toward the surface underneath (_UnderMap,
    // sampled on the shared rock UVs) for a soft look while staying fully opaque — so decals land on
    // it. Base footprint uses the RAW mask (matches the depth passes); the soft, noisy edge lives in
    // the colour blend (coverage). Shells (layer>0) use the noisy footprint, hard alpha-tested.
    float coverage = 1.0;
    if (input.layer == 0.0)
    {
        if (MossAlphaClip(0.0, input.mask, furColor.r)) discard;
        coverage = MossBaseAlpha(noisyMask); // 0 at the edge -> 1 in the painted body
    }
    else
    {
        if (MossAlphaClip(input.layer, noisyMask, furColor.r)) discard;
    }

    float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);
    float3 normalTS = UnpackNormalScale(
        SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, furUv),
        _NormalScale);
    // At the base edge (coverage->0) flatten the moss normal so it reads like the surface underneath.
    if (input.layer == 0.0)
        normalTS = normalize(lerp(float3(0.0, 0.0, 1.0), normalTS, coverage));
    float3 bitangent = SafeNormalize(viewDirWS.y * cross(input.normalWS, input.tangentWS));
    float3 normalWS = SafeNormalize(TransformTangentToWorld(
        normalTS,
        float3x3(input.tangentWS, bitangent, input.normalWS)));

    SurfaceData surfaceData = (SurfaceData)0;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    surfaceData.occlusion = lerp(1.0 - _Occlusion, 1.0, input.layer);
    surfaceData.albedo *= surfaceData.occlusion;
    // Base: blend the moss albedo toward the surface underneath so the opaque edge fades into it.
    // Sample the atlas directly at the mesh UVs (overlay shares the rock's UVs). No _UnderColor
    // multiply and no _UnderMap_ST here on purpose: both serialize to 0 on existing materials and
    // would multiply the underlying albedo to black — which was the black-edge bug.
    if (input.layer == 0.0)
    {
        float3 under = SAMPLE_TEXTURE2D(_UnderMap, sampler_UnderMap, input.uv).rgb;
        // Tint the underlying albedo. Guard the (0,0,0,0) that materials serialize by default (which
        // would multiply to black) — pure black is treated as "no tint" (white).
        under *= all(_UnderColor.rgb == 0.0) ? float3(1.0, 1.0, 1.0) : _UnderColor.rgb;
        surfaceData.albedo = lerp(under, surfaceData.albedo, coverage);
        // Don't apply the moss occlusion to the underlying edge (fade to 1 = no occlusion).
        surfaceData.occlusion = lerp(1.0, surfaceData.occlusion, coverage);
    }

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = viewDirWS;
#if (defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)) && !defined(_RECEIVE_SHADOWS_OFF)
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
    inputData.fogCoord = input.fogFactorAndVertexLight.x;
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
    inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

    // Receive URP DBuffer decals (albedo/normal) if any are present.
#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    float4 color = UniversalFragmentPBR(inputData, surfaceData);

    // Moss-specific rim + flat ambient must only affect the MOSS, not the underlying-albedo edge, or
    // the faded edge never matches the real surface. coverage == 1 on the shells, -> 0 at the base edge.
    float3 preRim = color.rgb;
    ApplyRimLight(color.rgb, input.positionWS, viewDirWS, normalWS);
    color.rgb = lerp(preRim, color.rgb, coverage);
    color.rgb += _AmbientColor * coverage;
    color.rgb = MixFog(color.rgb, inputData.fogCoord);

    color.a = 1.0; // opaque
    return color;
}

#endif
