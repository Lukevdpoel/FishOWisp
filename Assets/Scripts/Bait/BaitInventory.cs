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

    [Tooltip("Fallback bait auto-equipped whenever a regular bobber would otherwise have an empty " +
             "hook (startup, switching back from a lure, or casting with no bait). Use an infinite " +
             "bait (isAlwaysAvailable) so the hook is never empty. If unset, the first available " +
             "infinite bait is used.")]
    [SerializeField] private BaitItem defaultBait;

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

    // True if the fish preset will engage the hook given the currently-equipped bait.
    //   - No bait (empty hook): only species flagged FishPreset.bitesWithoutBait engage (and even
    //     they're far less eager — see FishingZone.noBaitInterestMultiplier). Empty hook is a valid,
    //     infinite default now, so this is the gate that keeps most fish off a baitless hook.
    //   - A bait equipped: an empty preferred-baits list means "any bait"; otherwise the equipped
    //     bait must be in the species' preferred list.
    public static bool PresetAcceptsSelectedBait(FishPreset preset)
    {
        if (preset == null) return false;
        BaitItem equipped = Instance != null ? Instance.SelectedBait : null;
        if (equipped == null) return preset.bitesWithoutBait;
        var prefs = preset.preferredBaits;
        if (prefs == null || prefs.Count == 0) return true;
        return prefs.Contains(equipped);
    }

    // True when a regular bobber has an EMPTY hook (no bait selected, not a lure) AND this species
    // won't engage a baitless hook. Used to keep such fish from even hovering around the bobber —
    // the visual gather should match the bite gate (PresetAcceptsSelectedBait returns the same for
    // the empty-hook case). A bait IS selected? Never rejects here: wrong-bait fish still gather
    // visually by design (only the bite/lead promotion in FishingZone is gated by the bait list).
    public static bool EmptyHookRejects(FishPreset preset)
    {
        if (preset == null) return false;
        if (BobberInventory.IsLureEquipped) return false;
        BaitItem equipped = Instance != null ? Instance.SelectedBait : null;
        if (equipped != null) return false;
        return !preset.bitesWithoutBait;
    }

    private string SavePath => Path.Combine(Application.persistentDataPath, "bait.json");

    private void Start()
    {
        if (!LoadBait()) GrantStartingBaits();
        EnsureBaitSelected();   // drop a depleted selection to "no bait" (a valid empty-hook default)
        OnBaitChanged?.Invoke();
    }

    /// <summary>
    /// Keep the equipped bait valid for a regular bobber. "No bait" (an empty hook) is itself a valid,
    /// infinite default now, so this no longer forces a fallback bait — it only drops a now-unusable
    /// selection (a finite bait that ran out / was shelved) back to null. No-op when a lure is equipped.
    /// </summary>
    public void EnsureBaitSelected()
    {
        if (BobberInventory.IsLureEquipped) return;
        if (selectedBait != null && !IsUsable(selectedBait))
        {
            selectedBait = null;
            OnSelectedBaitChanged?.Invoke(null);
        }
    }

    private bool IsUsable(BaitItem bait)
    {
        if (bait == null || !bait.isAvailable) return false;
        return bait.isAlwaysAvailable || GetCount(bait) > 0;
    }

    // The configured defaultBait if usable, else any infinite bait (so the fallback can't run dry),
    // else the first bait actually in stock.
    private BaitItem PickDefaultBait()
    {
        if (IsUsable(defaultBait)) return defaultBait;
        foreach (BaitItem b in registeredBaits)
            if (b != null && b.isAvailable && b.isAlwaysAvailable) return b;
        foreach (BaitItem b in registeredBaits)
            if (IsUsable(b)) return b;
        return null;
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
