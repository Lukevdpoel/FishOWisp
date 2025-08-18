using System;

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
}
