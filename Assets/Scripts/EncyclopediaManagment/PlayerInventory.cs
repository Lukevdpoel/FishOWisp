using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }

    [Header("Data")]
    public int inventorySize = 24;
    public int currentCurrency = 100;
    public List<CaughtFish> caughtFishes = new List<CaughtFish>();

    // --- All UI and Physical references have been removed ---

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void AddFish(FishPreset preset)
    {
        if (preset == null) return;
        CaughtFish newFish = new CaughtFish(preset);
        AddFish(newFish); // Call the simplified AddFish method
    }

    public void AddFish(CaughtFish fishToAdd, GameObject physicalPrefab = null) // physicalPrefab argument is ignored
    {
        FishEncyclopediaManager.Instance.RegisterCaughtFish(fishToAdd);
        if (caughtFishes.Count >= inventorySize)
        {
            Debug.Log("Inventory is full! Cannot add fish.");
            return;
        }
        if (fishToAdd == null) return;

        // Add to data list
        caughtFishes.Add(fishToAdd);
    }

    public void SellFish(CaughtFish fishToSell)
    {
        if (fishToSell != null && caughtFishes.Contains(fishToSell))
        {
            currentCurrency += fishToSell.GetValue();
            caughtFishes.Remove(fishToSell);

            // --- All UI update calls have been removed ---
        }
    }
}