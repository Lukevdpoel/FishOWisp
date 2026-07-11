using System;
using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;

public class PlayerInventory : GenericSingleton<PlayerInventory>
{
    [Header("Data")]
    public int inventorySize = 24;
    public int currentCurrency = 100;
    public List<CaughtFish> caughtFishes = new List<CaughtFish>();

    [Header("Debug")]
    [Tooltip("Amount used by the +/- buttons and the 'Set Coins' button.")]
    [SerializeField] private int debugCoinAmount = 100;

    [Button("Add Coins", ButtonSizes.Medium), HorizontalGroup("DebugCoins"), GUIColor(0.5f, 1f, 0.5f)]
    private void DebugAddCoins() => SetCurrency(currentCurrency + debugCoinAmount);

    [Button("Subtract Coins", ButtonSizes.Medium), HorizontalGroup("DebugCoins"), GUIColor(1f, 0.6f, 0.6f)]
    private void DebugSubtractCoins() => SetCurrency(currentCurrency - debugCoinAmount);

    [Button("Set Coins To Amount", ButtonSizes.Medium)]
    private void DebugSetCoins() => SetCurrency(debugCoinAmount);

    [Button("Reset Coins To 0", ButtonSizes.Small)]
    private void DebugResetCoins() => SetCurrency(0);

    private void SetCurrency(int value)
    {
        currentCurrency = Mathf.Max(0, value);
        if (Application.isPlaying) SaveInventory();
        OnInventoryChanged?.Invoke();
    }

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

        if (FishMilestoneTracker.Instance != null)
        {
            FishMilestoneTracker.Instance.RegisterCatch(fishToAdd);
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

    public bool TrySpendCurrency(int amount)
    {
        if (amount <= 0) return false;
        if (currentCurrency < amount) return false;
        currentCurrency -= amount;
        SaveInventory();
        OnInventoryChanged?.Invoke();
        return true;
    }

    private void SaveInventory()
    {
        // Persist caught fish by fishName, NOT by object reference. JsonUtility writes a
        // UnityEngine.Object as {"instanceID": N}, and instance IDs are reassigned every run, so in a
        // player build they resolve to unrelated objects (baits/lures/bobbers) — the same bug that
        // poisoned the encyclopedia. See FishEncyclopediaManager.SaveEncyclopedia.
        var data = new InventorySaveData { currency = currentCurrency };
        foreach (var f in caughtFishes)
        {
            if (f == null || f.preset == null) continue;
            data.fishes.Add(new CaughtFishRecord { fishName = f.preset.fishName, lengthCm = f.lengthCm });
        }
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
    }

    private void LoadInventory()
    {
        if (!File.Exists(SavePath)) return;

        try
        {
            InventorySaveData data = JsonUtility.FromJson<InventorySaveData>(File.ReadAllText(SavePath));
            if (data == null) return;

            currentCurrency = data.currency;
            caughtFishes = new List<CaughtFish>();
            if (data.fishes == null) return;

            foreach (var rec in data.fishes)
            {
                // Re-link to a real FishPreset by stable name. Unknown/legacy records (no fishName)
                // are dropped so a build can never resurrect them as bait/lure garbage.
                FishPreset preset = FishEncyclopediaManager.Instance != null
                    ? FishEncyclopediaManager.Instance.GetPresetByName(rec.fishName) : null;
                if (preset == null) continue;
                caughtFishes.Add(new CaughtFish(preset) { lengthCm = rec.lengthCm });
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to load inventory: {e.Message}");
        }
    }

    [Serializable]
    private class InventorySaveData
    {
        public int currency;
        public List<CaughtFishRecord> fishes = new List<CaughtFishRecord>();
    }

    [Serializable]
    private class CaughtFishRecord
    {
        public string fishName;
        public float lengthCm;
    }
}