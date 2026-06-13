using System;
using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;

public class BaitInventory : GenericSingleton<BaitInventory>
{
    [Tooltip("All bait items the game knows about. Add every BaitItem asset here so save/load can resolve them by id.")]
    [SerializeField] private List<BaitItem> registeredBaits = new List<BaitItem>();

    [ShowInInspector, ReadOnly, DictionaryDrawerSettings(KeyLabel = "Bait ID", ValueLabel = "Count")]
    private readonly Dictionary<string, int> counts = new Dictionary<string, int>();

    [Tooltip("The bait currently equipped on the hook. Null = no bait selected.")]
    [SerializeField] private BaitItem selectedBait;

    [Tooltip("Bait granted automatically on a fresh save (no bait.json yet) so a new player starts stocked.")]
    [SerializeField] private List<StartingBait> startingBaits = new List<StartingBait>();

    [Serializable]
    public class StartingBait
    {
        public BaitItem bait;
        [Min(1)] public int count = 5;
    }

    public BaitItem SelectedBait => selectedBait;

    public event Action OnBaitChanged;
    public event Action<BaitItem> OnSelectedBaitChanged;

    public void SetSelectedBait(BaitItem bait)
    {
        if (selectedBait == bait) return;
        selectedBait = bait;
        OnSelectedBaitChanged?.Invoke(selectedBait);
    }

    // True if the fish preset accepts whatever bait the player currently has equipped.
    // Null bait or empty preference list both behave as "any bait accepted" so the game
    // remains playable before a bait-selection UI exists, and so fish without configured
    // preferences don't get silently filtered out.
    public static bool PresetAcceptsSelectedBait(FishPreset preset)
    {
        if (preset == null) return false;
        BaitItem equipped = Instance != null ? Instance.SelectedBait : null;
        if (equipped == null) return true;
        var prefs = preset.preferredBaits;
        if (prefs == null || prefs.Count == 0) return true;
        return prefs.Contains(equipped);
    }

    private string SavePath => Path.Combine(Application.persistentDataPath, "bait.json");

    private void Start()
    {
        if (!LoadBait()) GrantStartingBaits();
        OnBaitChanged?.Invoke();
    }

    // Fresh save: stock the configured starting bait so the player can fish right away.
    private void GrantStartingBaits()
    {
        bool granted = false;
        foreach (StartingBait grant in startingBaits)
        {
            if (grant == null || grant.bait == null || grant.count <= 0) continue;
            if (grant.bait.isAlwaysAvailable || string.IsNullOrEmpty(grant.bait.id)) continue;
            counts[grant.bait.id] = grant.count;
            granted = true;
        }
        if (granted) SaveBait();
    }

    public int GetCount(BaitItem bait)
    {
        if (bait == null || string.IsNullOrEmpty(bait.id)) return 0;
        if (bait.isAlwaysAvailable) return int.MaxValue;
        return counts.TryGetValue(bait.id, out int n) ? n : 0;
    }

    public void AddBait(BaitItem bait, int amount = 1)
    {
        if (bait == null || string.IsNullOrEmpty(bait.id) || amount <= 0) return;
        if (bait.isAlwaysAvailable) return;
        counts[bait.id] = GetCount(bait) + amount;
        SaveBait();
        OnBaitChanged?.Invoke();
        Debug.Log($"[BaitInventory] +{amount} {bait.displayName} (total {counts[bait.id]})");
    }

    public bool TryConsume(BaitItem bait, int amount = 1)
    {
        if (bait == null || amount <= 0) return false;
        if (bait.isAlwaysAvailable) return true;
        int have = GetCount(bait);
        if (have < amount) return false;
        counts[bait.id] = have - amount;
        SaveBait();
        OnBaitChanged?.Invoke();

        // Auto-deselect a depleted selection so the UI doesn't keep showing an unusable equipped bait.
        if (selectedBait == bait && counts[bait.id] <= 0)
        {
            selectedBait = null;
            OnSelectedBaitChanged?.Invoke(null);
        }
        return true;
    }

    public IReadOnlyList<BaitItem> RegisteredBaits => registeredBaits;

    public void SetCount(BaitItem bait, int amount)
    {
        if (bait == null || string.IsNullOrEmpty(bait.id)) return;
        if (bait.isAlwaysAvailable) return;
        amount = Mathf.Max(0, amount);
        counts[bait.id] = amount;
        SaveBait();
        OnBaitChanged?.Invoke();

        if (amount <= 0 && selectedBait == bait)
        {
            selectedBait = null;
            OnSelectedBaitChanged?.Invoke(null);
        }
    }

    [Button("Clear All Bait")]
    public void ClearAllBait()
    {
        counts.Clear();
        if (selectedBait != null)
        {
            selectedBait = null;
            OnSelectedBaitChanged?.Invoke(null);
        }
        SaveBait();
        OnBaitChanged?.Invoke();
        Debug.Log("[BaitInventory] Cleared all bait counts.");
    }

    private void SaveBait()
    {
        BaitSaveData data = new BaitSaveData();
        foreach (KeyValuePair<string, int> kvp in counts)
        {
            data.entries.Add(new BaitSaveEntry { id = kvp.Key, count = kvp.Value });
        }
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
    }

    // Returns true when a save file existed (even an empty/corrupt one — the player has
    // history, so the fresh-save starting grant must not re-stock them).
    private bool LoadBait()
    {
        counts.Clear();
        if (!File.Exists(SavePath)) return false;

        try
        {
            BaitSaveData data = JsonUtility.FromJson<BaitSaveData>(File.ReadAllText(SavePath));
            if (data?.entries == null) return true;
            foreach (BaitSaveEntry e in data.entries)
            {
                if (!string.IsNullOrEmpty(e.id) && e.count > 0)
                    counts[e.id] = e.count;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BaitInventory] Failed to load: {e.Message}");
        }
        return true;
    }

    [Serializable]
    private class BaitSaveData
    {
        public List<BaitSaveEntry> entries = new List<BaitSaveEntry>();
    }

    [Serializable]
    private class BaitSaveEntry
    {
        public string id;
        public int count;
    }
}
