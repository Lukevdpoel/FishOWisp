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

    // Event to notify UI when inventory changes
    public event Action OnInventoryChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    // Overload 1: Create a fresh fish from a preset (used for debugging/testing)
    public void AddFish(FishPreset preset)
    {
        if (preset == null) return;
        CaughtFish newFish = new CaughtFish(preset);
        AddFish(newFish);
    }

    // Overload 2: Add an existing fish instance (used by actual fishing)
    public void AddFish(CaughtFish fishToAdd, GameObject physicalPrefab = null)
    {
        // Update Encyclopedia (Data tracking)
        if (FishEncyclopediaManager.Instance != null)
        {
            FishEncyclopediaManager.Instance.RegisterCaughtFish(fishToAdd);
        }

        // Check Capacity
        if (caughtFishes.Count >= inventorySize)
        {
            Debug.Log("Inventory is full! Cannot add fish.");
            return;
        }

        if (fishToAdd == null) return;

        // Add to data list
        caughtFishes.Add(fishToAdd);

        // Notify UI
        OnInventoryChanged?.Invoke();
    }

    public void SellFish(CaughtFish fishToSell)
    {
        if (fishToSell != null && caughtFishes.Contains(fishToSell))
        {
            currentCurrency += fishToSell.GetValue();
            caughtFishes.Remove(fishToSell);

            // Notify UI
            OnInventoryChanged?.Invoke();
        }
    }
}