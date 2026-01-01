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

    // Event to notify UI (Inventory Grid AND Currency HUD) when data changes
    public event Action OnInventoryChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    private void Start()
    {
        // Trigger initial update so UI matches data on load
        OnInventoryChanged?.Invoke();
    }

    public void AddFish(CaughtFish fishToAdd)
    {
        if (FishEncyclopediaManager.Instance != null)
        {
            FishEncyclopediaManager.Instance.RegisterCaughtFish(fishToAdd);
        }

        if (caughtFishes.Count >= inventorySize)
        {
            Debug.Log("Inventory is full! Cannot add fish.");
            return;
        }

        if (fishToAdd == null) return;

        caughtFishes.Add(fishToAdd);
        OnInventoryChanged?.Invoke();
    }

    public void RemoveFish(CaughtFish fishToRemove)
    {
        if (caughtFishes.Contains(fishToRemove))
        {
            caughtFishes.Remove(fishToRemove);
            OnInventoryChanged?.Invoke();
        }
    }

    /// <summary>
    /// Adds currency directly (used by Vendor script).
    /// </summary>
    public void TransactionAddCurrency(int amount)
    {
        currentCurrency += amount;
        OnInventoryChanged?.Invoke();
    }
}