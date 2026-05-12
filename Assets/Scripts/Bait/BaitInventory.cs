using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class BaitInventory : GenericSingleton<BaitInventory>
{
    [Tooltip("All bait items the game knows about. Add every BaitItem asset here so save/load can resolve them by id.")]
    [SerializeField] private List<BaitItem> registeredBaits = new List<BaitItem>();

    private readonly Dictionary<string, int> counts = new Dictionary<string, int>();

    [Tooltip("The bait currently equipped on the hook. Null = no bait selected.")]
    [SerializeField] private BaitItem selectedBait;

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
        LoadBait();
        OnBaitChanged?.Invoke();
    }

    public int GetCount(BaitItem bait)
    {
        if (bait == null || string.IsNullOrEmpty(bait.id)) return 0;
        return counts.TryGetValue(bait.id, out int n) ? n : 0;
    }

    public void AddBait(BaitItem bait, int amount = 1)
    {
        if (bait == null || string.IsNullOrEmpty(bait.id) || amount <= 0) return;
        counts[bait.id] = GetCount(bait) + amount;
        SaveBait();
        OnBaitChanged?.Invoke();
        Debug.Log($"[BaitInventory] +{amount} {bait.displayName} (total {counts[bait.id]})");
    }

    public bool TryConsume(BaitItem bait, int amount = 1)
    {
        if (bait == null || amount <= 0) return false;
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

    private void SaveBait()
    {
        BaitSaveData data = new BaitSaveData();
        foreach (KeyValuePair<string, int> kvp in counts)
        {
            data.entries.Add(new BaitSaveEntry { id = kvp.Key, count = kvp.Value });
        }
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
    }

    private void LoadBait()
    {
        counts.Clear();
        if (!File.Exists(SavePath)) return;

        try
        {
            BaitSaveData data = JsonUtility.FromJson<BaitSaveData>(File.ReadAllText(SavePath));
            if (data?.entries == null) return;
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
