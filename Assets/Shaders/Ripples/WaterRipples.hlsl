#ifndef WATER_RIPPLES_INCLUDED
#define WATER_RIPPLES_INCLUDED

// Filled from C# via Shader.SetGlobalVectorArray("_Ripples", ...).
// xyz = world position of ripple source, w = start time (seconds, matches _Time.y).
float4 _Ripples[8];

// Per-ripple tuning. x = amplitude, y = radius (world units), z = lifetime (seconds), w = unused.
float4 _RippleStrengths[8];

// Global tuning shared by all active ripples. Set via Shader.SetGlobalFloat.
float _RippleFrequency;
float _RippleSpeed;

void ComputeRippleNormal_float(float3 worldPos, float3 baseNormal, out float3 outNormal)
{
    float2 grad = float2(0, 0);

    UNITY_LOOP
    for (int i = 0; i < 8; i++)
    {
        float4 ripple = _Ripples[i];
        float4 strength = _RippleStrengths[i];

        float amplitude = strength.x;
        float radius = strength.y;
        float lifetime = strength.z;

        if (amplitude <= 0 || radius <= 0 || lifetime <= 0) continue;

        float t = _Time.y - ripple.w;
        if (t < 0 || t > lifetime) continue;

        float2 offset = worldPos.xz - ripple.xz;
        float d = length(offset);

        // Skip fragments too far from this ripple to contribute meaningfully.
        if (d > radius * 3) continue;

        float phase = d * _RippleFrequency - t * _RippleSpeed;
        float spatialDecay = exp(-d / radius);
        float timeNormalized = saturate(t / lifetime);
        float timeDecay = (1.0 - timeNormalized) * (1.0 - timeNormalized);

        // Spatial gradient of A * sin(phase) is A * cos(phase) * (freq * offset/d).
        float2 dir = offset / max(d, 0.0001);
        float magnitude = amplitude * cos(phase) * _RippleFrequency * spatialDecay * timeDecay;

        grad += dir * magnitude;
    }

    // Surface normal of height field y = h(x,z) is (-dh/dx, 1, -dh/dz).
    float3 perturbation = float3(-grad.x, 0, -grad.y);
    outNormal = normalize(baseNormal + perturbation);
}

#endif
