using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PlayerInventory : GenericSingleton<PlayerInventory>
{
    [Header("Data")]
    public int inventorySize = 24;
    public int currentCurrency = 100;
    public List<CaughtFish> caughtFishes = new List<CaughtFish>();

    public event Action OnInventoryChanged;

    private string SavePath => Path.Combine(Application.persistentDataPath, "inventory.json");

    private void Start()
    {
        LoadInventory();
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
        SaveInventory();
        OnInventoryChanged?.Invoke();
    }

    public void RemoveFish(CaughtFish fishToRemove)
    {
        if (caughtFishes.Contains(fishToRemove))
        {
            caughtFishes.Remove(fishToRemove);
            SaveInventory();
            OnInventoryChanged?.Invoke();
        }
    }

    public void TransactionAddCurrency(int amount)
    {
        currentCurrency += amount;
        SaveInventory();
        OnInventoryChanged?.Invoke();
    }

    private void SaveInventory()
    {
        string json = JsonUtility.ToJson(new InventoryDataWrapper(currentCurrency, caughtFishes), true);
        File.WriteAllText(SavePath, json);
    }

    private void LoadInventory()
    {
        if (!File.Exists(SavePath)) return;

        try
        {
            string json = File.ReadAllText(SavePath);
            InventoryDataWrapper wrapper = JsonUtility.FromJson<InventoryDataWrapper>(json);
            if (wrapper != null)
            {
                currentCurrency = wrapper.currency;
                caughtFishes = wrapper.fishes ?? new List<CaughtFish>();
                caughtFishes.RemoveAll(f => f == null || f.preset == null);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to load inventory: {e.Message}");
        }
    }

    [Serializable]
    private class InventoryDataWrapper
    {
        public int currency;
        public List<CaughtFish> fishes;

        public InventoryDataWrapper(int currency, List<CaughtFish> fishes)
        {
            this.currency = currency;
            this.fishes = fishes;
        }
    }
}