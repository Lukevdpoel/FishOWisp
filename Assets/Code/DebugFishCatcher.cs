using UnityEngine;
using Sirenix.OdinInspector;

public class DebugFishCatcher : MonoBehaviour
{
    [Title("Fish Pool Debug")]
    [Required]
    public FishPool fishPool;

    [Button(ButtonSizes.Large)]
    public void CatchRandomFishFromPool()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0)
        {
            Debug.LogWarning("Fish pool is empty or not assigned.");
            return;
        }

        // Choose random fish
        FishPreset preset = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];
        float length = Random.Range(preset.minLengthCm, preset.maxLengthCm);

        // Register the catch
        FishEncyclopediaManager.Instance.RegisterCaughtFish(preset, length);

        Debug.Log($"Caught from {fishPool.poolName}: {preset.fishName} ({length:F1} cm)");
    }
}
