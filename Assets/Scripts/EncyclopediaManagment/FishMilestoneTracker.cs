using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum MilestoneCategory
{
    Species,
    SizeClass,
    Weather,
    Total
}

public struct MilestoneEvent
{
    public MilestoneCategory category;
    public string key;
    public int count;
    public CaughtFish fish;
}

public class FishMilestoneTracker : GenericSingleton<FishMilestoneTracker>
{
    public static readonly int[] Milestones = { 1, 10, 25, 50, 100, 500, 1000, 2000, 5000 };

    public static event Action<MilestoneEvent> OnMilestoneReached;

    // Fires once for EVERY secured catch (after counts are updated), unlike OnMilestoneReached which
    // only fires at the tier thresholds. This is the authoritative "a fish was actually caught and
    // kept" signal — listeners that care about every catch (e.g. achievements for landing a
    // legendary or completing the encyclopedia) hook this rather than a hook-time fishing event,
    // which would fire before the catch is secured.
    public static event Action<CaughtFish> OnCatchRegistered;

    [Serializable]
    private class CountEntry
    {
        public string key;
        public int count;
    }

    [Serializable]
    private class SaveData
    {
        public List<CountEntry> species = new List<CountEntry>();
        public List<CountEntry> sizeClass = new List<CountEntry>();
        public List<CountEntry> weather = new List<CountEntry>();
        public int total;
    }

    private readonly Dictionary<string, int> speciesCounts = new Dictionary<string, int>();
    private readonly Dictionary<string, int> sizeClassCounts = new Dictionary<string, int>();
    private readonly Dictionary<string, int> weatherCounts = new Dictionary<string, int>();
    private int totalCount;

    private string SavePath => Path.Combine(Application.persistentDataPath, "fish_milestones.json");

    // This tracker is the source of the milestone/catch events the achievement system listens to,
    // but it was never placed in any scene or prefab — so GenericSingleton.Instance stayed null,
    // PlayerInventory.AddFish's null-check skipped RegisterCatch, and no catch ever reached the
    // milestone pipeline (the encyclopedia still updated because its manager IS in the scene).
    // Self-bootstrap into a persistent object so the tracker always exists, mirroring how the
    // partner AchievementManager installs itself. It has no inspector-configured state, so a
    // code-created instance is fully functional.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject(nameof(FishMilestoneTracker));
        go.AddComponent<FishMilestoneTracker>();
        DontDestroyOnLoad(go);
    }

    protected override void Awake()
    {
        base.Awake();
        if (Instance == this)
        {
            Load();
        }
    }

    public void RegisterCatch(CaughtFish fish)
    {
        if (fish == null || fish.preset == null) return;

        string speciesKey = fish.preset.fishName;
        string sizeKey = SizeClassHelper.FromLengthCm(fish.lengthCm).ToString();
        // Time of day this fish was actually caught (Day/Night), not a fixed per-species trait.
        string timeOfDayKey = (WorldStateManager.Instance != null && WorldStateManager.Instance.IsNight)
            ? "Night" : "Day";

        int speciesCount = Increment(speciesCounts, speciesKey);
        int sizeCount = Increment(sizeClassCounts, sizeKey);
        int timeOfDayCount = Increment(weatherCounts, timeOfDayKey);
        totalCount++;

        Save();

        OnCatchRegistered?.Invoke(fish);

        TryFireMilestone(MilestoneCategory.Species, speciesKey, speciesCount, fish);
        TryFireMilestone(MilestoneCategory.SizeClass, sizeKey, sizeCount, fish);
        TryFireMilestone(MilestoneCategory.Weather, timeOfDayKey, timeOfDayCount, fish);
        TryFireMilestone(MilestoneCategory.Total, "All", totalCount, fish);
    }

    private static int Increment(Dictionary<string, int> dict, string key)
    {
        dict.TryGetValue(key, out int current);
        current++;
        dict[key] = current;
        return current;
    }

    private static void TryFireMilestone(MilestoneCategory category, string key, int count, CaughtFish fish)
    {
        if (Array.IndexOf(Milestones, count) < 0) return;
        OnMilestoneReached?.Invoke(new MilestoneEvent
        {
            category = category,
            key = key,
            count = count,
            fish = fish
        });
    }

    public int GetSpeciesCount(string speciesName) => speciesCounts.TryGetValue(speciesName, out var c) ? c : 0;
    public int GetSizeClassCount(SizeClass sizeClass) => sizeClassCounts.TryGetValue(sizeClass.ToString(), out var c) ? c : 0;
    public int GetTimeOfDayCount(bool isNight) => weatherCounts.TryGetValue(isNight ? "Night" : "Day", out var c) ? c : 0;
    public int GetTotalCount() => totalCount;

    private void Save()
    {
        var data = new SaveData { total = totalCount };
        foreach (var kv in speciesCounts) data.species.Add(new CountEntry { key = kv.Key, count = kv.Value });
        foreach (var kv in sizeClassCounts) data.sizeClass.Add(new CountEntry { key = kv.Key, count = kv.Value });
        foreach (var kv in weatherCounts) data.weather.Add(new CountEntry { key = kv.Key, count = kv.Value });
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
    }

    private void Load()
    {
        if (!File.Exists(SavePath)) return;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data == null) return;
            totalCount = data.total;
            LoadInto(data.species, speciesCounts);
            LoadInto(data.sizeClass, sizeClassCounts);
            LoadInto(data.weather, weatherCounts);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to load fish milestones: {e.Message}");
        }
    }

    private static void LoadInto(List<CountEntry> entries, Dictionary<string, int> dict)
    {
        dict.Clear();
        if (entries == null) return;
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.key)) continue;
            dict[entry.key] = entry.count;
        }
    }
}
