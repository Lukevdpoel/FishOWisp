// FishPreset.cs
// This ScriptableObject holds data for a single type of fish.
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "NewFishPreset", menuName = "Fishing/Fish Preset")]
public class FishPreset : ScriptableObject
{
    [Header("Basic Info")]
    public string fishName;
    [TextArea]
    public string description;
    public Sprite fishImage;
    public GameObject fishPrefab;

    [Header("Size")]
    public SizeClass sizeClass;
    public bool isLegendary;
    public float minLengthCm;
    public float maxLengthCm;

    [Header("Behavior")]
    [Tooltip("Relative spawn weight within a pool: a fish at 100 spawns twice as often as one at 50. " +
             "0 = never spawns. Only fish matching the current weather compete for the roll.")]
    [Range(0f, 100f)]
    public float spawnChance = 50f;
    [Range(0f, 1f)]
    public float catchProbability = 0.5f;
    [Tooltip("Which tackle this species responds to. Bobber fish care about the preferred-baits list below; " +
             "Lure fish ignore bait entirely. Both = responds to either.")]
    public TackleAttraction attractedTo = TackleAttraction.Both;
    [Tooltip("Radius (m) at which this species notices a bobber/lure in the water and swims over. " +
             "The lure brain multiplies this by its movingRadiusMultiplier (default 1.5) while the lure is moving. " +
             "Set 0 to fall back to the FishRipple prefab's actionRadius.")]
    public float awarenessRadius = 8f;

    [Header("Swim Animation")]
    [Tooltip("Tail-beat rate in beats/second while calmly swimming. Drives the procedural sway shader only — movement speed is unaffected.")]
    public float swimFrequency = 1.8f;
    [Tooltip("How many wave cycles fit along the body. Stiff swimmers (carp, pike) ~0.6, full-body undulators (eel, lamprey) ~1.8.")]
    public float swimBodyWaves = 0.7f;
    [Tooltip("How far the tail swings, as a fraction of body length.")]
    [Range(0f, 0.3f)]
    public float swimWaveAmplitude = 0.07f;
    [Tooltip("Whole-body side-to-side drift, as a fraction of body length.")]
    [Range(0f, 0.1f)]
    public float swimSideAmplitude = 0.015f;
    [Tooltip("Yaw wobble around the body center, in radians. High values read as hovering/wobbling (puffer, sunfish).")]
    [Range(0f, 0.5f)]
    public float swimPivotAmount = 0.06f;
    [Tooltip("Portion of the body (from the head) that stays rigid. 0 = the whole body flexes (eels), 0.5 = only the rear half moves.")]
    [Range(0f, 1f)]
    public float swimMaskStart = 0.35f;

    [Header("Economy & Conditions")]
    public int basePrice;
    public float pricePerCm;
    [Tooltip("Bait this fish is attracted to. Drag BaitItem assets from ScriptableObjects/Bait_Scriptable here. " +
             "Only used when the fish responds to bobbers — hidden for lure-only species.")]
    [ShowIf(nameof(RespondsToBobber))]
    public List<BaitItem> preferredBaits = new List<BaitItem>();
    public WeatherType preferredWeather;

    public bool RespondsToBobber => attractedTo != TackleAttraction.Lure;
    public bool RespondsToLure => attractedTo != TackleAttraction.Bobber;
}

public enum TackleAttraction
{
    Bobber,
    Lure,
    Both
}

public enum WeatherType
{
    Sunny,
    Rainy,
    Cloudy,
    Stormy,
    Night
}

public enum SizeClass
{
    Tiny,
    Small,
    Medium,
    Large,
    Huge
}

public static class SizeClassHelper
{
    // Absolute length brackets in cm. Adjust as gameplay tuning evolves.
    public static SizeClass FromLengthCm(float lengthCm)
    {
        if (lengthCm < 10f) return SizeClass.Tiny;
        if (lengthCm < 30f) return SizeClass.Small;
        if (lengthCm < 60f) return SizeClass.Medium;
        if (lengthCm < 120f) return SizeClass.Large;
        return SizeClass.Huge;
    }

    // Camera view distance used by ModelViewer when displaying a caught fish.
    // Smaller value = camera closer to the fish.
    public static float GetCameraViewDistance(SizeClass sizeClass)
    {
        switch (sizeClass)
        {
            case SizeClass.Tiny:   return 0.5f;
            case SizeClass.Small:  return 0.8f;
            case SizeClass.Medium: return 1.2f;
            case SizeClass.Large:  return 2.0f;
            case SizeClass.Huge:   return 3.5f;
            default:               return 1f;
        }
    }
}