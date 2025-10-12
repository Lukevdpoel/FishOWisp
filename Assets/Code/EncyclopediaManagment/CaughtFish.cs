using System;
using UnityEngine;

[Serializable]
public class CaughtFish
{
    public FishPreset preset;
    public float lengthCm;

    public CaughtFish(FishPreset preset)
    {
        this.preset = preset;
        lengthCm = UnityEngine.Random.Range(preset.minLengthCm, preset.maxLengthCm);
    }

    public string GetDisplayName()
    {
        return $"{preset.fishName} ({lengthCm:F1} cm)";
    }

    /// <summary>
    /// Calculates the total currency value of this specific fish.
    /// </summary>
    /// <returns>The total value in coins.</returns>
    public int GetValue()
    {
        // Calculate the bonus value from the fish's size
        float sizeBonus = (lengthCm - preset.minLengthCm) * preset.pricePerCm;

        // The total value is the base price plus the size bonus, rounded to a whole number
        int totalValue = Mathf.RoundToInt(preset.basePrice + sizeBonus);

        return totalValue;
    }
}