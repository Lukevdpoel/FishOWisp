using System;
using UnityEngine;

// Holds a permanent time-of-day override that the player buys from the vendor.
// Auto = follow GameTimeManager (real-time clock). ForcedDay/ForcedNight = lock the
// effective hour so CrossFadeDayNight reads a fixed value. Persists across scene
// loads so leaving the vendor's room keeps the override active outdoors.
public class WorldStateManager : GenericSingleton<WorldStateManager>
{
    public enum TimeMode { Auto, ForcedDay, ForcedNight }

    [Header("Override Hours")]
    [Tooltip("Hour the CrossFadeDayNight should read while ForcedDay is active.")]
    [Range(0f, 24f)] public float forcedDayHour = 12f;
    [Tooltip("Hour the CrossFadeDayNight should read while ForcedNight is active.")]
    [Range(0f, 24f)] public float forcedNightHour = 23f;

    public TimeMode CurrentTimeMode { get; private set; } = TimeMode.Auto;

    public static event Action OnWorldStateChanged;

    protected override void Awake()
    {
        base.Awake();
        if (Application.isPlaying) DontDestroyOnLoad(gameObject);
    }

    public float GetEffectiveHour()
    {
        switch (CurrentTimeMode)
        {
            case TimeMode.ForcedDay:   return forcedDayHour;
            case TimeMode.ForcedNight: return forcedNightHour;
            default:
                return GameTimeManager.Instance != null
                    ? GameTimeManager.Instance.CurrentHour
                    : 12f;
        }
    }

    // Weather used for fish-spawn gating. ForcedNight is treated as Night; everything
    // else is treated as Sunny (the default-day condition).
    public WeatherType GetActiveFishingWeather()
    {
        return CurrentTimeMode == TimeMode.ForcedNight
            ? WeatherType.Night
            : WeatherType.Sunny;
    }

    public void SetTimeMode(TimeMode mode)
    {
        if (CurrentTimeMode == mode) return;
        CurrentTimeMode = mode;
        OnWorldStateChanged?.Invoke();
    }
}
