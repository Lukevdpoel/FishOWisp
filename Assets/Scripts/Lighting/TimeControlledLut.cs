using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways] // Runs in Editor without hitting Play!
public class TimeControlledLUT : MonoBehaviour
{
    [Header("Setup")]
    public Volume globalVolume;

    [Header("Timing Settings")]
    public float nightStartHour = 18f; // 6 PM
    public float nightEndHour = 6f;    // 6 AM
    public float transitionTime = 1f;

    private ColorLookup _colorLookup;

    void Update()
    {
        if (globalVolume == null || globalVolume.profile == null) return;
        if (GameTimeManager.Instance == null) return;
        if (!globalVolume.profile.TryGet(out _colorLookup)) return;

        float currentHour = GameTimeManager.Instance.CurrentHour;
        float intensity = CalculateNightIntensity(currentHour);
        _colorLookup.contribution.value = intensity;
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