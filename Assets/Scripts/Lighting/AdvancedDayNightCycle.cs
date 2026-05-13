using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class CrossFadeDayNight : MonoBehaviour
{
    [Header("Volumes")]
    public Volume dawnVolume;  // Orange LUT / Tint
    public Volume nightVolume; // Blue LUT

    [Header("Timing Settings")]
    public float sunsetStart = 17f; // 5:00 PM (Start turning Orange)
    public float sunsetDuration = 1f;

    public float duskStart = 19f;   // 7:00 PM (Start turning Blue / End Orange)
    public float duskDuration = 1f;

    public float sunriseStart = 5f; // 5:00 AM (Start turning Orange / End Blue)
    public float sunriseDuration = 1f;

    public float dayStart = 7f;     // 7:00 AM (Start turning Day / End Orange)
    public float dayDuration = 1f;

    void Update()
    {
        if (dawnVolume == null || nightVolume == null) return;

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

        float dawnWeight = 0f;
        float nightWeight = 0f;

        // --- PHASE 1: SUNSET (Day -> Dawn) ---
        if (time >= sunsetStart && time < sunsetStart + sunsetDuration)
        {
            float t = (time - sunsetStart) / sunsetDuration;
            dawnWeight = Mathf.SmoothStep(0f, 1f, t);
            nightWeight = 0f;
        }
        // --- PHASE 2: GOLDEN HOUR (Pure Dawn) ---
        else if (time >= (sunsetStart + sunsetDuration) && time < duskStart)
        {
            dawnWeight = 1f;
            nightWeight = 0f;
        }
        // --- PHASE 3: DUSK (Dawn -> Night CROSSFADE) ---
        else if (time >= duskStart && time < duskStart + duskDuration)
        {
            float t = (time - duskStart) / duskDuration;

            // As Night goes UP, Dawn goes DOWN
            nightWeight = Mathf.SmoothStep(0f, 1f, t);
            dawnWeight = 1f - nightWeight; // Perfect trade-off
        }
        // --- PHASE 4: DEEP NIGHT (Pure Night) ---
        // Handles the midnight wrap-around (e.g. 21:00 to 05:00)
        else if (time >= (duskStart + duskDuration) || time < sunriseStart)
        {
            dawnWeight = 0f;
            nightWeight = 1f;
        }
        // --- PHASE 5: SUNRISE (Night -> Dawn CROSSFADE) ---
        else if (time >= sunriseStart && time < sunriseStart + sunriseDuration)
        {
            float t = (time - sunriseStart) / sunriseDuration;

            // As Dawn goes UP, Night goes DOWN
            dawnWeight = Mathf.SmoothStep(0f, 1f, t);
            nightWeight = 1f - dawnWeight;
        }
        // --- PHASE 6: MORNING (Dawn -> Day) ---
        else if (time >= (sunriseStart + sunriseDuration) && time < dayStart)
        {
            // Wait for full dawn...
            dawnWeight = 1f;
            nightWeight = 0f;
        }
        else if (time >= dayStart && time < dayStart + dayDuration)
        {
            float t = (time - dayStart) / dayDuration;
            dawnWeight = Mathf.SmoothStep(1f, 0f, t); // Fade out to Day
            nightWeight = 0f;
        }

        // Apply
        dawnVolume.weight = dawnWeight;
        nightVolume.weight = nightWeight;
    }

}