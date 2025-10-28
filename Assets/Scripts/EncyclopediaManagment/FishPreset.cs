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
    public float minLengthCm;
    public float maxLengthCm;

    [Header("Rarity & Behavior")]
    public Rarity rarity;
    [Range(0f, 1f)]
    public float catchProbability = 0.5f;

    [Header("Economy & Conditions")]
    public int basePrice;
    public float pricePerCm; // The added value per cm above the minimum size
    public BaitType preferredBait;
    public WeatherType preferredWeather;
    public float cameraviewdistance = 1;

    public GameObject physicalModelPrefab;
}

// These enums define the possible options for the fields above.
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

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