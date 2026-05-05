// FishPreset.cs
// This ScriptableObject holds data for a single type of fish.
using UnityEngine;

[CreateAssetMenu(fileName = "NewFishPreset", menuName = "Fishing/Fish Preset")]
public class FishPreset : ScriptableObject
{
    [Header("Basic Info")]
    public string fishName;
    [TextArea]
    public string description;
    public Sprite fishImage;
    // The variable name is now corrected to 'fishPrefab' (capital P) to match the other scripts.
    public GameObject fishPrefab;

    [Header("Size")]
    public SizeClass sizeClass;
    public bool isLegendary;
    public float minLengthCm;
    public float maxLengthCm;

    [Header("Behavior")]
    [Range(0f, 1f)]
    public float catchProbability = 0.5f;

    [Header("Economy & Conditions")]
    public int basePrice;
    public float pricePerCm; // The added value per cm above the minimum size
    public BaitType preferredBait;
    public WeatherType preferredWeather;

    // --- MODIFIED: Removed the extra 'physicalModelPrefab' line ---
}

// These enums define the possible options for the fields above.
public enum BaitType
{
    Worm,
    Insect,
    Minnow,
    Bread,
    Synthetic
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