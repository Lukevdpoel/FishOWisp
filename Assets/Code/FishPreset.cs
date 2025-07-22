using UnityEngine;

[CreateAssetMenu(fileName = "NewFishPreset", menuName = "Fishing/Fish Preset")]
public class FishPreset : ScriptableObject
{
    [Header("Basic Info")]
    public string fishName;
    [TextArea]
    public string description;
    public Sprite fishImage;
    public GameObject fishprefab;

    [Header("Size")]
    public float minLengthCm;
    public float maxLengthCm;

    [Header("Rarity & Behavior")]
    public Rarity rarity;
    [Range(0f, 1f)]
    public float catchProbability = 0.5f;

    [Header("Economy & Conditions")]
    public int basePrice;
    public BaitType preferredBait;
    public WeatherType preferredWeather;
    public float cameraviewdistance = 1;
}

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
