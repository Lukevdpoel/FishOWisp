#ifndef FUR_SHELL_PARAM_HLSL
#define FUR_SHELL_PARAM_HLSL

int _ShellAmount;
float _ShellStep;
float _AlphaCutout;
float _Occlusion;
float _FurScale;
float4 _BaseMove;
float4 _WindFreq;
float4 _WindMove;
float3 _AmbientColor;
float _FaceViewProdThresh;

TEXTURE2D(_FurMap); 
SAMPLER(sampler_FurMap);
float4 _FurMap_ST;

TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);
float4 _NormalMap_ST;
float _NormalScale;

// The albedo of the surface the moss sits on (same UVs as the baked overlay mesh). The flat base
// blends toward this so the opaque moss edge fades into the surface below. Set per-object via a
// MaterialPropertyBlock (ShellMeshBaker does this from the base object's material).
TEXTURE2D(_UnderMap);
SAMPLER(sampler_UnderMap);
float4 _UnderMap_ST;
float4 _UnderColor;

// Polybrush-painted moss mask.
// _MossMaskChannel picks which vertex-color channel(s) drive the moss (dot product).
// _MossMaskInvert flips it (for meshes whose color stream defaults to white).
float4 _MossMaskChannel;
float _MossMaskInvert;
float _MossMaskSoftness;

inline float ComputeMossMask(float4 vertexColor)
{
    // No channel selected (e.g. shaders/materials that don't expose the mask) => masking off.
    if (all(_MossMaskChannel == 0.0)) return 1.0;
    float m = saturate(dot(vertexColor, _MossMaskChannel));
    return lerp(m, 1.0 - m, saturate(_MossMaskInvert));
}

// Coverage of the moss at a fragment: the painted mask, but with its edge broken up by
// the fur noise so the boundary dissolves instead of forming a hard line.
// _MossMaskSoftness == 0 reproduces the old hard behavior (and keeps a solid base where fully painted).
inline float MossCoverage(float mask, float furR)
{
    return saturate(mask * (1.0 + _MossMaskSoftness) - _MossMaskSoftness * (1.0 - furR));
}

// How much further the flat base layer (shell 0) reaches than the 3D shells, by surviving at a
// lower coverage threshold. Lets the flat moss albedo lead the shells and feather out for a soft
// rock->moss transition. 0 = base has the same footprint as the shells (old behavior).
float _BaseSpread;

// Tiny outward lift applied to the whole shell stack so the flat base doesn't z-fight the surface
// it overlays. Keep small (a few mm in world units).
float _BaseLift;

// Width (in mask units) of the soft alpha fade at the flat base's edge. 0 = hard edge.
float _BaseFeather;

// Breaks up the flat base's edge with world-space noise so the boundary wanders organically instead
// of following the mesh's triangle edges. _BaseEdgeNoise = strength, _BaseEdgeNoiseScale = frequency.
// Sampled from a dedicated grayscale _BaseNoiseMap (default "gray" = 0.5 = no effect until assigned).
float _BaseEdgeNoise;
float _BaseEdgeNoiseScale;
float _BaseEdgeNoiseWidth; // how wide a mask band the noise rounds over (independent of the fade)
TEXTURE2D(_BaseNoiseMap);
SAMPLER(sampler_BaseNoiseMap);

// The painted mask perturbed by world-space noise, gated to the transition band. Shared by the
// forward AND depth passes so the shells' noisy footprint matches in every pass (otherwise the depth
// prepass and colour pass disagree and the shell edges punch through to nothing).
inline float ComputeNoisyMask(float mask, float3 positionWS)
{
    float baseEdge = max(_AlphaCutout - _BaseSpread, 0.001);
    float gateA = saturate((mask - baseEdge) / max(_BaseEdgeNoiseWidth, 1e-4));
    float edgeWeight = gateA * (1.0 - gateA) * 4.0;
    float noise = SAMPLE_TEXTURE2D(_BaseNoiseMap, sampler_BaseNoiseMap, positionWS.xz * _BaseEdgeNoiseScale).r - 0.5;
    return mask + noise * _BaseEdgeNoise * edgeWeight;
}

// --- Light-decal projected onto the moss ---------------------------------------------------------
// Emissive URP decals are drawn before the transparent queue, so they can't land on the transparent
// moss. Instead we let the moss sample the decal's projection itself and add its light. A companion
// component (LightDecalToMoss) feeds the projector's world->box matrix, texture and colour as globals.
#define MOSS_DECAL_MAX 16
float4x4 _MossDecalMatrices[MOSS_DECAL_MAX]; // world -> [0,1]^3 projector box, per decal
float4 _MossDecalColors[MOSS_DECAL_MAX];     // rgb tint, a = intensity, per decal
int _MossDecalCount;                         // active decals (0 = feature off, the default)
TEXTURE2D(_MossDecalTex);
SAMPLER(sampler_MossDecalTex);

// Returns the summed additive light contribution of all projected decals at a world position.
// MossDecalManager uploads the arrays each frame from the scene's LightDecalToMoss components.
inline float3 SampleMossDecal(float3 positionWS)
{
    float3 sum = 0.0;
    [loop] for (int i = 0; i < _MossDecalCount; i++)
    {
        float3 uvw = mul(_MossDecalMatrices[i], float4(positionWS, 1.0)).xyz;
        if (any(uvw < 0.0) || any(uvw > 1.0))
            continue; // outside this projector's box — skip the sample
        // Soft border fade in the projection plane. LOD 0 sample: safe inside dynamic flow control.
        float2 border = smoothstep(0.0, 0.1, uvw.xy) * smoothstep(1.0, 0.9, uvw.xy);
        float3 tex = SAMPLE_TEXTURE2D_LOD(_MossDecalTex, sampler_MossDecalTex, uvw.xy, 0).rgb;
        sum += tex * _MossDecalColors[i].rgb * _MossDecalColors[i].a * (border.x * border.y);
    }
    return sum;
}

// Soft alpha for the flat base layer: 0 at its outer reach (_AlphaCutout - _BaseSpread), ramping to
// 1 over _BaseFeather. Lets the solid moss albedo fade out instead of ending on a hard line.
inline float MossBaseAlpha(float mask)
{
    float edge = max(_AlphaCutout - _BaseSpread, 0.001);
    return saturate((mask - edge) / max(_BaseFeather, 1e-4));
}

// Alpha-test for a shell fragment.
// Layer 0 (the flat base) is a SOLID sheet of moss albedo, gated by the painted mask alone (no fur
// noise) so it reads as continuous ground moss rather than the shells' holey pattern smeared flat.
// It extends _BaseSpread past where the shells start. Outer shells still use the fur-noise coverage
// and thin out with height.
inline bool MossAlphaClip(float layer, float mask, float furR)
{
    if (layer == 0.0)
        return mask < max(_AlphaCutout - _BaseSpread, 0.001);
    float cov = MossCoverage(mask, furR);
    return (furR * (1.0 - layer) * cov) < _AlphaCutout;
}

#endif