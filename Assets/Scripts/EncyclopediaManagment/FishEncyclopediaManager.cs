using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;

public class FishEncyclopediaManager : GenericSingleton<FishEncyclopediaManager>
{
    private const string SaveKey = "FishEncyclopedia";
    private string SavePath => Path.Combine(Application.persistentDataPath, "encyclopedia.json");

    private int currentIndex = 0;
#if UNITY_EDITOR
    [Title("Debug Options")]
    [ToggleLeft]
    public bool deleteOnStart = false;
#endif

    public List<FishPreset> allFishPresets;
    public List<FishEncyclopediaEntry> encyclopediaEntries = new List<FishEncyclopediaEntry>();

    protected override void Awake()
    {
        base.Awake();

        if (Instance == this)
        {
#if UNITY_EDITOR
            if (deleteOnStart)
            {
                DeleteEncyclopediaSave();
            }
#endif

            LoadEncyclopedia();
            UpdateUI();
        }
    }

    private void OnEnable()
    {
        UpdateUI();
    }

    public void RegisterCaughtFish(CaughtFish caughtFish)
    {
        var entry = GetOrCreateEntry(caughtFish.preset);
        entry.RegisterCatch(caughtFish.lengthCm);
        SaveEncyclopedia();
    }

    private FishEncyclopediaEntry GetOrCreateEntry(FishPreset preset)
    {
        // FIX: Added "e.preset != null" check to prevent crash if save data is corrupted
        var entry = encyclopediaEntries.Find(e => e.preset != null && e.preset.fishName == preset.fishName);
        if (entry == null)
        {
            entry = new FishEncyclopediaEntry { preset = preset };
            encyclopediaEntries.Add(entry);
        }
        return entry;
    }

    public void SaveEncyclopedia()
    {
        string json = JsonUtility.ToJson(new EncyclopediaDataWrapper(encyclopediaEntries), true);
        File.WriteAllText(SavePath, json);
    }

    public void LoadEncyclopedia()
    {
        if (File.Exists(SavePath))
        {
            string json = File.ReadAllText(SavePath);
            EncyclopediaDataWrapper wrapper = JsonUtility.FromJson<EncyclopediaDataWrapper>(json);
            encyclopediaEntries = wrapper.entries ?? new List<FishEncyclopediaEntry>();

            // FIX: Remove any entries that lost their ScriptableObject reference during load
            // This prevents the NullReferenceException later
            encyclopediaEntries.RemoveAll(e => e.preset == null);
        }
        else
        {
            encyclopediaEntries = new List<FishEncyclopediaEntry>();
        }

        // FIX: Removed the "return;" that was here. It was preventing the code below from running.

        foreach (var preset in allFishPresets)
        {
            if (preset == null) continue;

            // Add missing presets to encyclopedia
            // FIX: Added "e.preset != null" safety check
            if (!encyclopediaEntries.Exists(e => e.preset != null && e.preset.fishName == preset.fishName))
            {
                encyclopediaEntries.Add(new FishEncyclopediaEntry { preset = preset });
            }
        }
    }

    [System.Serializable]
    private class EncyclopediaDataWrapper
    {
        public List<FishEncyclopediaEntry> entries;

        public EncyclopediaDataWrapper(List<FishEncyclopediaEntry> entries)
        {
            this.entries = entries;
        }
    }

    [Button]
    public void NextEntry()
    {
        if (encyclopediaEntries.Count == 0) return;
        currentIndex = (currentIndex + 1) % encyclopediaEntries.Count;
        UpdateUI();
    }

    [Button]
    public void PreviousEntry()
    {
        if (encyclopediaEntries.Count == 0) return;
        currentIndex = (currentIndex - 1 + encyclopediaEntries.Count) % encyclopediaEntries.Count;
        UpdateUI();
    }

    private void UpdateUI()
    {
        FishEntryUI entryUI = GetComponentInChildren<FishEntryUI>(true);
        if (entryUI == null) return;

        if (encyclopediaEntries != null && encyclopediaEntries.Count > 0)
        {
            // Safety check for UI
            if (currentIndex >= encyclopediaEntries.Count) currentIndex = 0;
            if (encyclopediaEntries[currentIndex].preset != null)
            {
                entryUI.Populate(encyclopediaEntries[currentIndex]);
            }
        }
    }

    [Button(ButtonSizes.Medium)]
    public void DeleteEncyclopediaSave()
    {
        if (File.Exists(SavePath))
        {
            File.Delete(SavePath);
            Debug.Log("Fish encyclopedia save deleted.");
        }
        encyclopediaEntries.Clear();
        currentIndex = 0;

        // Reload to repopulate the empty list from presets
        LoadEncyclopedia();
    }
}