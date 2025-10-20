using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }
    public InventoryUI inventoryUI;

    [Header("Inventory Settings")]
    public int inventorySize = 24;
    public int currentCurrency = 100; // Set a starting currency

    public List<CaughtFish> caughtFishes = new List<CaughtFish>();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void AddFish(CaughtFish fishToAdd)
    {
        if (caughtFishes.Count >= inventorySize)
        {
            Debug.Log("Inventory is full! Cannot add fish.");
            return;
        }
        if (fishToAdd == null) return;
        caughtFishes.Add(fishToAdd);
    }

    public void AddFish(FishPreset preset)
    {
        if (preset == null) return;
        CaughtFish newFish = new CaughtFish(preset);
        AddFish(newFish);
    }

    // --- NEW SELLING LOGIC ---
    public void SellFish(CaughtFish fishToSell)
    {
        if (fishToSell != null && caughtFishes.Contains(fishToSell))
        {
            currentCurrency += fishToSell.GetValue();
            caughtFishes.Remove(fishToSell);
            // After selling, tell the UI to refresh
            if (inventoryUI != null)
            {
                inventoryUI.UpdateDisplay();
            }
        }
    }

    public void SellAllFish()
    {
        if (caughtFishes.Count == 0) return;

        int totalValue = 0;
        foreach (CaughtFish fish in caughtFishes)
        {
            totalValue += fish.GetValue();
        }
        currentCurrency += totalValue;
        caughtFishes.Clear();

        if (inventoryUI != null)
        {
            inventoryUI.UpdateDisplay();
        }
    }
}

