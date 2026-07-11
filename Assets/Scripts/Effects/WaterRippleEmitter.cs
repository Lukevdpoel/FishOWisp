using UnityEngine;

// RETIRED — safe to delete. This was the first ripple attempt (analytic rings via global
// shader arrays); no shader ever consumed _Ripples and nothing calls Emit anymore. Water
// ripples now run through WaterRippleSimRenderer + WaterRippleBinder (wave-sim render
// texture anchored to the bobber). Its shader half, Shaders/Ripples/WaterRipples.hlsl,
// is equally dead.
public static class WaterRippleEmitter
{
    public const int MaxRipples = 8;

    private static readonly Vector4[] positions = new Vector4[MaxRipples];
    private static readonly Vector4[] strengths = new Vector4[MaxRipples];
    private static int nextIndex = 0;

    private static readonly int PositionsID = Shader.PropertyToID("_Ripples");
    private static readonly int StrengthsID = Shader.PropertyToID("_RippleStrengths");
    private static readonly int FrequencyID = Shader.PropertyToID("_RippleFrequency");
    private static readonly int SpeedID = Shader.PropertyToID("_RippleSpeed");

    public static void Emit(Vector3 worldPos, float amplitude, float radius, float lifetime)
    {
        positions[nextIndex] = new Vector4(worldPos.x, worldPos.y, worldPos.z, Time.timeSinceLevelLoad);
        strengths[nextIndex] = new Vector4(amplitude, radius, lifetime, 0f);
        nextIndex = (nextIndex + 1) % MaxRipples;

        Shader.SetGlobalVectorArray(PositionsID, positions);
        Shader.SetGlobalVectorArray(StrengthsID, strengths);
    }

    public static void SetGlobalParams(float frequency, float speed)
    {
        Shader.SetGlobalFloat(FrequencyID, frequency);
        Shader.SetGlobalFloat(SpeedID, speed);
    }

    public static void ClearAll()
    {
        for (int i = 0; i < MaxRipples; i++)
        {
            positions[i] = Vector4.zero;
            strengths[i] = Vector4.zero;
        }
        Shader.SetGlobalVectorArray(PositionsID, positions);
        Shader.SetGlobalVectorArray(StrengthsID, strengths);
    }
}
