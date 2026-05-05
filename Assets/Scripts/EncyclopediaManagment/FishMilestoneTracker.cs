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
        string weatherKey = fish.preset.preferredWeather.ToString();

        int speciesCount = Increment(speciesCounts, speciesKey);
        int sizeCount = Increment(sizeClassCounts, sizeKey);
        int weatherCount = Increment(weatherCounts, weatherKey);
        totalCount++;

        Save();

        TryFireMilestone(MilestoneCategory.Species, speciesKey, speciesCount, fish);
        TryFireMilestone(MilestoneCategory.SizeClass, sizeKey, sizeCount, fish);
        TryFireMilestone(MilestoneCategory.Weather, weatherKey, weatherCount, fish);
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
    public int GetWeatherCount(WeatherType weather) => weatherCounts.TryGetValue(weather.ToString(), out var c) ? c : 0;
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
