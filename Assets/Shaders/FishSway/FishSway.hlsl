#ifndef FISH_SWAY_INCLUDED
#define FISH_SWAY_INCLUDED

// ABZU-style procedural swim: a travelling sine wave displaces vertices in object space,
// masked so the head stays rigid and the tail swings widest. No rig, no animation clips —
// works on the static fish OBJs as-is. (Reference: Matt Nava, "Creating the Art of ABZU",
// GDC 2017; same construction as Godot's "Animating Thousands of Fish" sample.)
//
// Conventions:
//  - The spine runs along object-space Z (the fish models face -Z, head at min Z).
//  - All amplitudes are fractions of body length, so the same numbers read the same on a
//    10 cm Stiphodon and a 1 m Pike.
//  - s = 0 at the head, 1 at the tail.

#ifndef TWO_PI
#define TWO_PI 6.28318530718
#endif

#define FISH_CHAIN_COUNT 8

// Shared lateral swim offset: travelling wave (tail swings widest), body side drift, and a
// yaw-wobble shear around the body centre. Pure lateral x in the cross-section frame.
// The head guard pins the snout: real fish recoil — lateral motion builds from near zero at
// the head toward the tail, so the drift/wobble terms fade in over the front third.
float FishSwayLateralWave(float s, float phase, float bodyLength, float bodyWaves,
                          float waveAmplitude, float sideAmplitude, float pivotAmount,
                          float maskStart)
{
    float mask = smoothstep(maskStart, 1.0, s);
    float headGuard = smoothstep(0.0, 0.35, s);
    return sin(phase - s * bodyWaves * TWO_PI) * waveAmplitude * bodyLength * mask
         + (cos(phase) * sideAmplitude
            + sin(phase) * pivotAmount * (s - 0.5)) * bodyLength * headGuard;
}

// Skins the vertex onto a trailing chain of spine points (object space, xz only — fish swim
// strictly horizontally). The chain is simulated on the CPU (FishModelVisual): the head
// point is pinned to the steered head and the body points trail it by distance constraints,
// so follow-through, turn lag and whip all EMERGE from the simulation instead of being
// estimated from yaw rates. waveLateral rides in the cross-section frame, so the tail beat
// follows the bent body. With a straight chain this reproduces the rest pose exactly.
// verticalAmount (0..1) gates the chain's HEIGHT into the skin. At 0 this is the original
// strictly-horizontal skin (xz from the chain, y straight from the vertex) that every swimming
// fish uses — bit-identical, and a flat chain stays identical even at 1. When a hooked fish
// leaps, FishModelVisual feeds the chain real heights and ramps this up: the spine rises and
// falls along the chain and each cross-section pitches to the chain's local slope, so the head
// angles up out of the water, the body trails and arches, and it flattens back on the descent.
float3 FishChainSkin(float3 positionOS, float waveLateral, float4 chain[FISH_CHAIN_COUNT],
                     float spineMinZ, float spineMaxZ, float headAtMinZ, float verticalAmount)
{
    float bodyLength = max(spineMaxZ - spineMinZ, 1e-4);
    float dirZ = (headAtMinZ < 0.5) ? -1.0 : 1.0;
    float headZ = (headAtMinZ < 0.5) ? spineMaxZ : spineMinZ;
    float s = saturate((positionOS.z - headZ) * dirZ / bodyLength);

    float u = s * (FISH_CHAIN_COUNT - 1);
    int seg = min((int)u, FISH_CHAIN_COUNT - 2);
    float t = u - seg;

    float2 c = lerp(chain[seg].xz, chain[seg + 1].xz, t);
    float2 tangentXZ = chain[seg + 1].xz - chain[seg].xz;
    float len = length(tangentXZ);
    float2 tdir = (len > 1e-5) ? tangentXZ / len : float2(0.0, dirZ);
    float2 side = float2(tdir.y, -tdir.x) * dirZ;

    float lateral = positionOS.x + waveLateral;
    float3 result = float3(c.x + side.x * lateral, positionOS.y, c.y + side.y * lateral);

    if (verticalAmount > 1e-4)
    {
        float cy = lerp(chain[seg].y, chain[seg + 1].y, t); // spine height along the chain
        float headY = chain[0].y;
        float slope = (chain[seg + 1].y - chain[seg].y) / max(len, 1e-4); // rise over horizontal run
        float pitch = atan(slope);
        float sinP = sin(pitch);
        float cosP = cos(pitch);

        float3 lifted = result;
        lifted.y  += (cy - headY);                    // spine follows the chain's height
        lifted.xz += tdir * (-sinP * positionOS.y);   // pitch the cross-section along the slope
        lifted.y  += (cosP - 1.0) * positionOS.y;     // foreshorten the upright as it tilts

        result = lerp(result, lifted, saturate(verticalAmount));
    }

    return result;
}

float3 FishSwayDisplace(
    float3 positionOS,
    float  time,          // swim clock in seconds (any phase offset already added)
    float  spineMinZ,     // object-space Z extents of the whole fish (set from mesh bounds)
    float  spineMaxZ,
    float  headAtMinZ,    // 1 = head sits at min Z (project default), 0 = flipped model
    float  frequency,     // tail-beat rate in Hz
    float  bodyWaves,     // wave cycles along the body: stiff swimmers ~0.6, eels ~1.8
    float  waveAmplitude, // tail swing, fraction of body length
    float  sideAmplitude, // whole-body side drift, fraction of body length
    float  pivotAmount,   // yaw wobble around the body centre, radians
    float  maskStart,     // s where the body starts flexing (everything before stays rigid)
    float  bendAmount,    // turn-arc curvature near the head, fraction of body length (script-driven)
    float  bendTail)      // time-lagged copy of bendAmount for the rear body (script-driven)
{
    float bodyLength = max(spineMaxZ - spineMinZ, 1e-4);
    float s = saturate((positionOS.z - spineMinZ) / bodyLength);
    if (headAtMinZ < 0.5) s = 1.0 - s;

    float phase = time * frequency * TWO_PI;

    float3 p = positionOS;
    p.x += FishSwayLateralWave(s, phase, bodyLength, bodyWaves, waveAmplitude,
                               sideAmplitude, pivotAmount, maskStart);

    // Turn-follow bend, as a true arc deformer: the spine is wrapped onto the circle the
    // head is carving. bendAmount is the tail's lateral offset as a fraction of body length,
    // so the heading change across the whole body is 2*bend radians. Unlike a plain lateral
    // offset, this rotates each cross-section too — the tail fin visibly sweeps through
    // turns. bendTail is a time-lagged copy for the rear body, so when the head straightens
    // out of a turn the curve drains toward the tail.
    float bend = lerp(bendAmount, bendTail, smoothstep(0.25, 1.0, s));
    float headZ = (headAtMinZ < 0.5) ? spineMaxZ : spineMinZ;
    float dirZ = (headAtMinZ < 0.5) ? -1.0 : 1.0;
    float d = (p.z - headZ) * dirZ;            // arc distance behind the head
    float k = 2.0 * bend / bodyLength;         // curvature: bend angle per object-space unit
    if (abs(k) > 1e-3)
    {
        float theta = k * d;
        float r = 1.0 / k;
        float sinT, cosT;
        sincos(theta, sinT, cosT);
        float lateral = r - (r - p.x) * cosT;  // = x + k*d^2/2 for small angles
        float along = (r - p.x) * sinT;
        p.x = lateral;
        p.z = headZ + dirZ * along;
    }
    else
    {
        p.x += 0.5 * k * d * d;
    }

    return p;
}

// Shader Graph Custom Function wrapper, for adding the same sway to lit/cel graphs
// (tank and aquarium fish). In the graph's vertex stage:
//   Custom Function node -> Type: File, Source: this file, Name: FishSway
//   Inputs:  PositionOS (Vector3, from a Position node set to Object space),
//            Time (Float, from a Time node), then the floats below in order.
//   Output:  OutPositionOS (Vector3) -> Vertex Position block.
void FishSway_float(float3 PositionOS, float Time, float SpineMinZ, float SpineMaxZ,
                    float HeadAtMinZ, float Frequency, float BodyWaves, float WaveAmplitude,
                    float SideAmplitude, float PivotAmount, float MaskStart, float BendAmount,
                    float BendTail, out float3 OutPositionOS)
{
    OutPositionOS = FishSwayDisplace(PositionOS, Time, SpineMinZ, SpineMaxZ, HeadAtMinZ,
                                     Frequency, BodyWaves, WaveAmplitude, SideAmplitude,
                                     PivotAmount, MaskStart, BendAmount, BendTail);
}

#endif
