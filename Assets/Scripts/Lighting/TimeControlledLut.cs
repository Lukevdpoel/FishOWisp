using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;

[ExecuteAlways] // Runs in Editor without hitting Play!
public class TimeControlledLUT : MonoBehaviour
{
    [Header("Setup")]
    public Volume globalVolume;

    [Header("Timing Settings")]
    public float nightStartHour = 18f; // 6 PM
    public float nightEndHour = 6f;    // 6 AM
    public float transitionTime = 1f;

    [Header("Debug Controls")]
    [Tooltip("Check this to manually control time with the slider below")]
    public bool enableTestMode = false;

    [Range(0f, 24f)]
    [Tooltip("Slide this to simulate the time of day")]
    public float testTimeOfDay = 12f;

    private ColorLookup _colorLookup;

    void Update()
    {
        if (globalVolume == null || globalVolume.profile == null) return;

        // Try to get the Color Lookup override
        if (!globalVolume.profile.TryGet(out _colorLookup)) return;

        // 1. Determine which time to use (Real or Test)
        float currentHour = GetCurrentTime();

        // 2. Calculate Intensity
        float intensity = CalculateNightIntensity(currentHour);

        // 3. Apply to the Volume Slider
        _colorLookup.contribution.value = intensity;
    }

    float GetCurrentTime()
    {
        if (enableTestMode)
        {
            return testTimeOfDay;
        }
        else
        {
            // Get real system time
            DateTime now = DateTime.Now;
            return now.Hour + (now.Minute / 60f) + (now.Second / 3600f);
        }
    }

    float CalculateNightIntensity(float time)
    {
        // Inside Night Zone (e.g., 20:00 or 02:00)
        if (time >= nightStartHour + transitionTime || time <= nightEndHour - transitionTime)
            return 1f;

        // Inside Day Zone (e.g., 12:00)
        if (time > nightEndHour && time < nightStartHour)
            return 0f;

        // Dusk Transition (Day -> Night)
        if (time >= nightStartHour)
        {
            float t = (time - nightStartHour) / transitionTime;
            return Mathf.SmoothStep(0f, 1f, t);
        }

        // Dawn Transition (Night -> Day)
        if (time <= nightEndHour)
        {
            float t = (nightEndHour - time) / transitionTime; // Invert for sunrise
            return Mathf.SmoothStep(0f, 1f, t);
        }

        return 0f;
    }
}