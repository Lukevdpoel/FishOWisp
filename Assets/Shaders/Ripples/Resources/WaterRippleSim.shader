Shader "Hidden/WaterRippleSim"
{
    // One step of the damped 2D wave equation, ping-ponged between two RGHalf render
    // textures by WaterRippleSimRenderer (same Graphics.Blit pattern as GrassTrailUpdate).
    // R = current surface height (signed), G = previous step's height (the wave equation
    // needs both). Impulses stamped in by gameplay propagate outward as expanding rings.
    Properties
    {
        _MainTex ("Previous State", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            float4 _DeltaUV;      // xy = anchor movement this step in UV, so the map stays world-anchored
            float  _WaveCoeff;    // (waveSpeed * dt / texelWorldSize)^2, clamped <= 0.49 C#-side for stability
            float  _VelocityDamp; // per-step wave velocity retention (energy loss)
            float  _HeightDamp;   // per-step height retention (kills standing residue)
            float  _BorderFadeUV; // width of the absorbing edge band, in UV
            float4 _Stamps[16];   // xy = uv position, z = radius in UV, w = impulse strength
            int    _StampCount;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // Previous-frame state, shifted to compensate for anchor movement. Outside the
            // map there is no water state: treat it as flat.
            float2 LoadState(float2 uv)
            {
                if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0) return float2(0.0, 0.0);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rg;
            }

            float2 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv + _DeltaUV.xy;
                float2 t = _MainTex_TexelSize.xy;

                float2 state = LoadState(uv);
                float h = state.x;
                float hPrev = state.y;

                float lap = LoadState(uv + float2(t.x, 0)).x
                          + LoadState(uv - float2(t.x, 0)).x
                          + LoadState(uv + float2(0, t.y)).x
                          + LoadState(uv - float2(0, t.y)).x
                          - 4.0 * h;

                // Verlet integration of the wave equation with damping.
                float hNew = h + (h - hPrev) * _VelocityDamp + lap * _WaveCoeff;
                hNew *= _HeightDamp;

                // Absorbing border: progressively kill waves in the outer band each step so
                // they fade out instead of reflecting off (or hard-clipping at) the map edge.
                float2 edge = min(IN.uv, 1.0 - IN.uv);
                float border = smoothstep(0.0, max(_BorderFadeUV, 1e-4), min(edge.x, edge.y));
                hNew *= lerp(0.85, 1.0, border);

                // Inject this step's impulses as smooth bumps; the wave equation turns them
                // into expanding rings on its own.
                for (int i = 0; i < _StampCount; i++)
                {
                    float4 s = _Stamps[i];
                    float d = distance(IN.uv, s.xy);
                    float a = saturate(1.0 - d / max(s.z, 1e-4));
                    hNew += s.w * a * a;
                }

                // Stability safety net: a burst of overlapping impulses can't blow the sim up.
                hNew = clamp(hNew, -2.0, 2.0);

                return float2(hNew, h);
            }
            ENDHLSL
        }
    }
}
