using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class CrossFadeDayNight : MonoBehaviour
{
    [Header("Volumes")]
    public Volume nightVolume; // Blue LUT

    [Header("Timing Settings")]
    public float duskStart = 19f;   // 7:00 PM (Start fading to night)
    public float duskDuration = 1f;

    public float dayStart = 6f;     // 6:00 AM (Start fading to day)
    public float dayDuration = 1f;

    [Header("Shader Globals")]
    [Tooltip("Drives the _NightFactor shader property (0 = day, 1 = night). Any shader using _NightFactor will fade in/out with the night.")]
    public bool driveNightShaderGlobal = true;

    private static readonly int NightFactorID = Shader.PropertyToID("_NightFactor");

    void Update()
    {
        if (nightVolume == null) return;

        float time;
        if (WorldStateManager.Instance != null)
        {
            time = WorldStateManager.Instance.GetEffectiveHour();
        }
        else if (GameTimeManager.Instance != null)
        {
            time = GameTimeManager.Instance.CurrentHour;
        }
        else return;

        float nightWeight = ComputeNightWeight(time);

        nightVolume.weight = nightWeight;

        if (driveNightShaderGlobal)
        {
            Shader.SetGlobalFloat(NightFactorID, nightWeight);
        }
    }

    private float ComputeNightWeight(float time)
    {
        // Dusk crossfade: day -> night
        if (time >= duskStart && time < duskStart + duskDuration)
        {
            float t = (time - duskStart) / duskDuration;
            return Mathf.SmoothStep(0f, 1f, t);
        }

        // Day crossfade: night -> day
        if (time >= dayStart && time < dayStart + dayDuration)
        {
            float t = (time - dayStart) / dayDuration;
            return Mathf.SmoothStep(1f, 0f, t);
        }

        // Outside the crossfades: either fully day or fully night.
        // Night runs from end of dusk through midnight until dayStart.
        float duskEnd = duskStart + duskDuration;
        bool isNight = (duskStart < dayStart)
            ? (time >= duskEnd && time < dayStart)
            : (time >= duskEnd || time < dayStart); // wraps midnight

        return isNight ? 1f : 0f;
    }
}
