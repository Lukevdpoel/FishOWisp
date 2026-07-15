#ifndef FUR_SHELL_DEPTH_NORMALS_BAKED_HLSL
#define FUR_SHELL_DEPTH_NORMALS_BAKED_HLSL

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
#include "./Param.hlsl"
#include "./ShellBaked.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 shellData : TEXCOORD3;
    float4 color : COLOR;
};

struct Varyings
{
    float4 vertex : SV_POSITION;
    float2 uv : TEXCOORD0;
    float  layer : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float3 tangentWS : TEXCOORD3;
    float3 posWS : TEXCOORD4;
    float  mask : TEXCOORD5;
};

Varyings vert(Attributes input)
{
    Varyings output = (Varyings)0;
    ShellVertexOutput s = ComputeBakedShell(
        input.positionOS.xyz, input.normalOS, input.tangentOS, input.color, input.shellData);
    output.vertex = s.positionCS;
    output.posWS = s.positionWS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.layer = s.layer;
    output.normalWS = s.normalWS;
    output.tangentWS = s.tangentWS;
    output.mask = s.mask;
    return output;
}

float4 frag(Varyings input) : SV_Target
{
    float2 furUV = input.uv / _BaseMap_ST.xy * _FurScale;

    float4 furColor = SAMPLE_TEXTURE2D(_FurMap, sampler_FurMap, furUV);
    // Match LitBaked's footprint exactly: base = raw mask, shells = noisy mask.
    float m = (input.layer == 0.0) ? input.mask : ComputeNoisyMask(input.mask, input.posWS);
    if (MossAlphaClip(input.layer, m, furColor.r)) discard;

    float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.posWS);
    half3 normalTS = UnpackNormalScale(
        SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, furUV),
        _NormalScale);
    float3 bitangent = SafeNormalize(viewDirWS.y * cross(input.normalWS, input.tangentWS));
    float3 normalWS = SafeNormalize(TransformTangentToWorld(
        normalTS,
        float3x3(input.tangentWS, bitangent, input.normalWS)));

    return float4(NormalizeNormalPerPixel(normalWS), 0.0);
}

#endif
