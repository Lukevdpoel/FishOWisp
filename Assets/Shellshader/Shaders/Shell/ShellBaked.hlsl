#ifndef FUR_SHELL_BAKED_HLSL
#define FUR_SHELL_BAKED_HLSL

// Shared vertex logic for the geometry-shader-free ("baked mesh") shell path.
// The mesh is pre-duplicated into N stacked copies (see ShellMeshBaker); each vertex carries
// its shell index in UV3.x and the normalized layer (index / shellCount) in UV3.y.
// This mirrors AppendShellVertex() in the geometry-shader passes, but runs once per vertex
// in the vertex stage instead of amplifying triangles on the GPU.

#include "./Param.hlsl"

struct ShellVertexOutput
{
    float4 positionCS;
    float3 positionWS;
    float3 normalWS;
    float3 tangentWS;
    float  layer;   // normalized 0..1 (== index / shellCount)
    float  mask;    // painted moss mask
};

ShellVertexOutput ComputeBakedShell(
    float3 positionOS, float3 normalOS, float4 tangentOS, float4 color, float2 shellData)
{
    ShellVertexOutput o = (ShellVertexOutput)0;

    float shellIndex = shellData.x;   // 0 .. shellCount-1
    float layer01    = shellData.y;   // shellIndex / shellCount
    float mask = ComputeMossMask(color);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(positionOS);
    VertexNormalInputs normalInput = GetVertexNormalInputs(normalOS, tangentOS);

    float moveFactor = pow(abs(layer01), _BaseMove.w);
    float3 windAngle = _Time.w * _WindFreq.xyz;
    float3 windMove = moveFactor * _WindMove.xyz * sin(windAngle + positionOS * _WindMove.w);
    float3 move = moveFactor * _BaseMove.xyz;
    float3 shellDir = normalize(normalInput.normalWS + move + windMove);

    o.positionWS = vertexInput.positionWS + shellDir * (_BaseLift + _ShellStep * shellIndex * mask);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.normalWS = normalInput.normalWS;
    o.tangentWS = normalInput.tangentWS;
    o.layer = layer01;
    o.mask = mask;
    return o;
}

#endif
