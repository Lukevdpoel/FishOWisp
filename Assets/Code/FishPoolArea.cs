using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FishPoolArea : MonoBehaviour
{
    public FishPool fishPool;

    private bool playerInRange = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            Debug.Log($"Player entered pool: {fishPool.poolName}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            Debug.Log($"Player left pool: {fishPool.poolName}");
        }
    }

    public void TryCatchFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0)
        {
            Debug.LogWarning("Fish pool is empty or not assigned.");
            return;
        }

        var preset = fishPool.availableFish[Random.Range(0, fishPool.availableFish.Count)];
        float length = Random.Range(preset.minLengthCm, preset.maxLengthCm);

        FishEncyclopediaManager.Instance.RegisterCaughtFish(preset, length);

        Debug.Log($"Caught {preset.fishName} ({length:F1} cm) from {fishPool.poolName}");
    }
}
