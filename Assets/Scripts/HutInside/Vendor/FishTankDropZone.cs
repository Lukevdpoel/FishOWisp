using UnityEngine;

[RequireComponent(typeof(FishTankManager))]
public class FishTankDropZone : MonoBehaviour
{
    private FishTankManager manager;

    void Awake()
    {
        manager = GetComponent<FishTankManager>();
    }

    /// <summary>
    /// Called by InventoryUI when a fish is dropped here.
    /// </summary>
    public void ReceiveFish(CaughtFish fishData)
    {
        if (fishData == null || fishData.preset == null) return;

        GameObject prefab = fishData.preset.fishPrefab;
        if (prefab == null) return;

        // 1. Spawn the fish at the tank's center (or slightly offset)
        GameObject newFish = Instantiate(prefab, transform.position, Quaternion.identity);

        // 2. Clean up physics/scripts meant for "Caught" state if necessary
        // (e.g., if your prefab has a Rigidbody for the bucket, we might want to disable gravity)
        Rigidbody rb = newFish.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        // 3. Remove any old AI scripts if they exist (like RandomWanderAI) to avoid conflict
        var oldAI = newFish.GetComponent<RandomWanderAI>();
        if (oldAI != null) Destroy(oldAI);

        // 4. Register with the Tank Manager
        manager.AddFish(newFish);

        // Optional: Scale fish based on size
        // float sizeScale = fishData.lengthCm / 50f; // Example scaling
        // newFish.transform.localScale = Vector3.one * sizeScale;
    }
}