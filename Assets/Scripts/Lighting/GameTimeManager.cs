using UnityEngine;

[ExecuteAlways]
public class GameTimeManager : GenericSingleton<GameTimeManager>
{
    [Header("Time Settings")]
    [Tooltip("Real-time seconds per in-game hour. 45 = a full 24h day takes 18 real minutes.")]
    public float secondsPerGameHour = 45f;

    [Tooltip("Hour the in-game clock starts at when play begins (0-24).")]
    [Range(0f, 24f)] public float startingHour = 6f;

    [Header("Debug")]
    [Tooltip("Pause the clock and scrub time manually with the slider below.")]
    public bool useSimulatedTime = false;
    [Range(0f, 24f)] public float simulatedTime = 12f;

    public float CurrentHour { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        CurrentHour = startingHour;
    }

    void Update()
    {
        if (useSimulatedTime)
        {
            CurrentHour = simulatedTime;
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            CurrentHour = startingHour;
            return;
        }
#endif

        if (secondsPerGameHour <= 0f) return;

        CurrentHour += Time.deltaTime / secondsPerGameHour;
        if (CurrentHour >= 24f) CurrentHour -= 24f;
    }
}
