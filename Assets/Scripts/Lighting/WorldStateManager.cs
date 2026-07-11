using System;
using UnityEngine;

// Tracks the world's time-of-day state. Auto = follow GameTimeManager (real-time clock).
// ForcedDay/ForcedNight lock the effective hour so CrossFadeDayNight reads a fixed value.
//
// The vendor's time-of-day purchase no longer locks the world: instead of a permanent
// override it calls SkipToTimeOfDay to fast forward the clock to the start of day/night and
// leaves the mode on Auto, so time keeps flowing afterwards. The ForcedDay/ForcedNight values
// are kept for the offer's day/night selector and as a defensive override path. Persists
// across scene loads so the clock state survives leaving the vendor's room.
public class WorldStateManager : GenericSingleton<WorldStateManager>
{
    public enum TimeMode { Auto, ForcedDay, ForcedNight }

    [Header("Override Hours")]
    [Tooltip("Hour the CrossFadeDayNight should read while ForcedDay is active.")]
    [Range(0f, 24f)] public float forcedDayHour = 12f;
    [Tooltip("Hour the CrossFadeDayNight should read while ForcedNight is active.")]
    [Range(0f, 24f)] public float forcedNightHour = 23f;

    [Header("Auto-Mode Night Window")]
    [Tooltip("In Auto mode, the real clock counts as Night for fish spawning at/after this hour. Keep in sync with CrossFadeDayNight.duskStart so fish match the visuals.")]
    [Range(0f, 24f)] public float autoNightStartHour = 19f;
    [Tooltip("In Auto mode, the real clock stops counting as Night at this hour. Keep in sync with CrossFadeDayNight.dayStart.")]
    [Range(0f, 24f)] public float autoNightEndHour = 6f;

    public TimeMode CurrentTimeMode { get; private set; } = TimeMode.Auto;

    public static event Action OnWorldStateChanged;

    protected override void Awake()
    {
        // A WorldStateManager from a previous scene already persists. Scene reloads spawn a fresh
        // copy; quietly yield to the original so the day/night state carries across scenes (and we
        // don't trip GenericSingleton's stray-duplicate error on every return to the home scene).
        if (Application.isPlaying && Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        base.Awake();
        if (Instance != this) return;

        // Persist across scene loads. DontDestroyOnLoad only works on a root object, and this sits
        // nested under a scene container, so detach to root first (previously this called
        // DontDestroyOnLoad on the nested child, which warns and fails to persist).
        if (Application.isPlaying)
        {
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }
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

    // Whether it's currently night for gameplay (fish spawning, bite chance). Forced modes
    // lock the result; Auto follows the real clock so a natural dusk transition flips to
    // night just like the visuals do (previously Auto was always treated as day, so night
    // fish only ever appeared after buying the Night Lantern).
    public bool IsNight
    {
        get
        {
            switch (CurrentTimeMode)
            {
                case TimeMode.ForcedNight: return true;
                case TimeMode.ForcedDay:   return false;
                default:
                    float hour = GameTimeManager.Instance != null
                        ? GameTimeManager.Instance.CurrentHour
                        : forcedDayHour;
                    return IsNightHour(hour);
            }
        }
    }

    // True when the given clock hour falls inside the Auto-mode night window. The window
    // wraps past midnight when start > end (e.g. 19:00 -> 06:00).
    private bool IsNightHour(float hour)
    {
        return autoNightStartHour < autoNightEndHour
            ? (hour >= autoNightStartHour && hour < autoNightEndHour)
            : (hour >= autoNightStartHour || hour < autoNightEndHour);
    }

    public void SetTimeMode(TimeMode mode)
    {
        if (CurrentTimeMode == mode) return;
        CurrentTimeMode = mode;
        OnWorldStateChanged?.Invoke();
    }

    // Start-of-period hours, derived from the Auto-mode night window so a skip lands exactly
    // where fish spawning flips and where CrossFadeDayNight begins its dusk/dawn transition.
    public float NightStartHour => autoNightStartHour;
    public float DayStartHour   => autoNightEndHour;

    // Fast forward the natural clock to the start of night (or day) without locking it. Unlike
    // the old forced modes this is a one-time skip: the mode is left on Auto so time keeps
    // flowing normally afterwards. Returns false if there's no clock to drive.
    public bool SkipToTimeOfDay(bool night)
    {
        if (GameTimeManager.Instance == null) return false;

        // Clear any leftover forced override so the clock actually drives time again.
        SetTimeMode(TimeMode.Auto);
        GameTimeManager.Instance.SetHour(night ? autoNightStartHour : autoNightEndHour);

        // SetTimeMode only fires when the mode actually changed (usually it's already Auto),
        // so signal here too to refresh anything keyed off the world state.
        OnWorldStateChanged?.Invoke();
        return true;
    }
}
