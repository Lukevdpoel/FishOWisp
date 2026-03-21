using System;
using UnityEngine;

[ExecuteAlways]
public class GameTimeManager : MonoBehaviour
{
    public static GameTimeManager Instance { get; private set; }

    [Header("Debug")]
    public bool useSimulatedTime = false;
    [Range(0f, 24f)] public float simulatedTime = 12f;

    public float CurrentHour { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (useSimulatedTime)
        {
            CurrentHour = simulatedTime;
        }
        else
        {
            DateTime now = DateTime.Now;
            CurrentHour = now.Hour + (now.Minute / 60f) + (now.Second / 3600f);
        }
    }
}
