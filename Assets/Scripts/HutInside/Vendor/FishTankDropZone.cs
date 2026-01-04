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

        // 1. Spawn the fish at the tank's center
        GameObject newFish = Instantiate(prefab, transform.position, Quaternion.identity);

        // --- Ensure fish is on Default layer so Raycast hits it ---
        newFish.layer = 0;

        // 2. Clean up physics/scripts meant for "Caught" state
        Rigidbody rb = newFish.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        var oldAI = newFish.GetComponent<RandomWanderAI>();
        if (oldAI != null) Destroy(oldAI);

        // 3. Attach Data Component for the Tooltip
        WorldFishData worldData = newFish.AddComponent<WorldFishData>();
        worldData.fishInfo = fishData;

        // --- FIX: Create a Generous Hitbox ---
        if (newFish.GetComponent<Collider>() == null)
        {
            BoxCollider col = newFish.AddComponent<BoxCollider>();

            // Try to calculate size based on the model
            Renderer rend = newFish.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                // Set the box size to 150% of the visual mesh size
                col.size = rend.bounds.size * 1.5f;
                // Optional: Adjust center if the pivot isn't in the middle
                // col.center = rend.bounds.center - newFish.transform.position;
            }
            else
            {
                // Fallback: Just a nice big box
                col.size = new Vector3(1.5f, 1.5f, 1.5f);
            }
        }

        // 4. Register with the Tank Manager
        manager.AddFish(newFish);
    }
}