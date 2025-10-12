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
    public int currentCurrency = 0; // The player's starting money

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
        Debug.Log($"Added {fishToAdd.GetDisplayName()} to inventory.");

        // We let the InventoryUI handle the update when it's opened.
    }

    public void AddFish(FishPreset preset)
    {
        if (preset == null) return;
        CaughtFish newFish = new CaughtFish(preset);
        AddFish(newFish);
    }

    // --- NEW CURRENCY METHODS ---
    public void AddCurrency(int amount)
    {
        currentCurrency += amount;
        // Notify the UI that the currency has changed
        if (inventoryUI != null)
        {
            inventoryUI.UpdateCurrencyDisplay();
        }
    }

    public bool RemoveCurrency(int amount)
    {
        if (currentCurrency >= amount)
        {
            currentCurrency -= amount;
            // Notify the UI that the currency has changed
            if (inventoryUI != null)
            {
                inventoryUI.UpdateCurrencyDisplay();
            }
            return true; // Transaction successful
        }
        else
        {
            Debug.Log("Not enough currency!");
            return false; // Transaction failed
        }
    }
}

