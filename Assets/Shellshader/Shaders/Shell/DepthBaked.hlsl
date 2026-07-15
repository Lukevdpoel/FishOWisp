#ifndef FUR_SHELL_DEPTH_BAKED_HLSL
#define FUR_SHELL_DEPTH_BAKED_HLSL

#include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"
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
    float  mask : TEXCOORD2;
    float3 positionWS : TEXCOORD3;
};

Varyings vert(Attributes input)
{
    Varyings output = (Varyings)0;
    ShellVertexOutput s = ComputeBakedShell(
        input.positionOS.xyz, input.normalOS, input.tangentOS, input.color, input.shellData);
    output.vertex = s.positionCS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.layer = s.layer;
    output.mask = s.mask;
    output.positionWS = s.positionWS;
    return output;
}

void frag(
    Varyings input,
    out float4 outColor : SV_Target,
    out float outDepth : SV_Depth)
{
    float4 furColor = SAMPLE_TEXTURE2D(_FurMap, sampler_FurMap, input.uv / _BaseMap_ST.xy * _FurScale);
    // Must match LitBaked's footprint exactly: base = raw mask, shells = noisy mask.
    float m = (input.layer == 0.0) ? input.mask : ComputeNoisyMask(input.mask, input.positionWS);
    if (MossAlphaClip(input.layer, m, furColor.r)) discard;

    outColor = outDepth = input.vertex.z / input.vertex.w;
}

#endif
