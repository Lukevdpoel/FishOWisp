using UnityEngine;

[ExecuteAlways]
public class GameTimeManager : GenericSingleton<GameTimeManager>
{
    [Header("Time Settings")]
    [Tooltip("Real-time seconds per in-game hour. 45 = a full 24h day takes 18 real minutes.")]
    public float secondsPerGameHour = 45f;

    [Tooltip("Hour the in-game clock starts at when play begins (0-24).")]
    [Range(0f, 24f)] public float startingHour = 6f;

    [Header("Debug (Editor only)")]
    [Tooltip("Editor preview only: scrub the hour in the Scene view with the slider below. " +
             "Ignored once the game is playing, so a forgotten toggle can never freeze the running clock.")]
    public bool useSimulatedTime = false;
    [Range(0f, 24f)] public float simulatedTime = 12f;

    public float CurrentHour { get; private set; }

    protected override void Awake()
    {
        // A clock from a previous scene already persists. Returning to a scene that contains a
        // GameTimeManager spawns a fresh copy; quietly yield to the original so the in-game time
        // carries over instead of resetting (enter the cave at 10:00 and it's still 10:00 inside).
        // Done before base.Awake so we skip GenericSingleton's stray-duplicate error on every
        // return to the home scene.
        if (Application.isPlaying && Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        base.Awake();
        if (Instance != this) return;

        CurrentHour = startingHour;

        // Keep this single clock alive across scene loads so time never resets on a transition.
        // DontDestroyOnLoad only works on a root object, and this manager sits nested under a
        // scene container, so detach to root first. WorldStateManager persists the same way.
        if (Application.isPlaying)
        {
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }
    }

    void Update()
    {
#if UNITY_EDITOR
        // Edit-mode preview only. useSimulatedTime scrubs the hour in the Scene view without
        // entering play; it is deliberately ignored once the game is running so a forgotten
        // toggle can never freeze the live clock (that exact mistake stuck the clock once).
        if (!Application.isPlaying)
        {
            CurrentHour = useSimulatedTime ? simulatedTime : startingHour;
            return;
        }
#endif

        if (secondsPerGameHour <= 0f) return;

        CurrentHour += Time.deltaTime / secondsPerGameHour;
        if (CurrentHour >= 24f) CurrentHour -= 24f;
    }

    // Jump the in-game clock to a specific hour. Time keeps flowing from there, so this is a
    // one-time skip rather than a freeze. Used by the vendor's time-of-day purchase to fast
    // forward to the start of day/night without locking the clock.
    public void SetHour(float hour)
    {
        hour = Mathf.Repeat(hour, 24f);
        CurrentHour = hour;
        // Keep the debug scrubber in sync so the jump isn't overwritten next frame when
        // simulated time happens to be enabled.
        simulatedTime = hour;
    }
}
