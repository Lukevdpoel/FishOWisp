using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;

public class FishEncyclopediaManager : MonoBehaviour
{
    public static FishEncyclopediaManager Instance;

    private const string SaveKey = "FishEncyclopedia";
    private string SavePath => Path.Combine(Application.persistentDataPath, "encyclopedia.json");

    private int currentIndex = 0;
    [Title("Debug Options")]
    [ToggleLeft]
    public bool deleteOnStart = false;

    public List<FishPreset> allFishPresets; // Assign all available fish in the inspector or dynamically
    public List<FishEncyclopediaEntry> encyclopediaEntries = new List<FishEncyclopediaEntry>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            if (deleteOnStart)
            {
                DeleteEncyclopediaSave();
            }

            LoadEncyclopedia();
            UpdateUI();
        }
        else
        {
            Destroy(gameObject);
        }
    }


    private void OnEnable()
    {
        UpdateUI();
    }

    public void RegisterCaughtFish(FishPreset preset, float length)
    {
        var entry = GetOrCreateEntry(preset);
        entry.RegisterCatch(length);
        SaveEncyclopedia();
    }

    private FishEncyclopediaEntry GetOrCreateEntry(FishPreset preset)
    {
        var entry = encyclopediaEntries.Find(e => e.preset.fishName == preset.fishName);
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
            encyclopediaEntries = wrapper.entries;
        }
        else
        {
            encyclopediaEntries = new List<FishEncyclopediaEntry>();
        }

        foreach (var preset in allFishPresets)
        {
            // Add missing presets to encyclopedia
            if (!encyclopediaEntries.Exists(e => e.preset.fishName == preset.fishName))
            {
                encyclopediaEntries.Add(new FishEncyclopediaEntry { preset = preset });
            }

            // Re-link any entries where preset might have become null
            var entry = encyclopediaEntries.Find(e => e.preset != null && e.preset.fishName == preset.fishName);
            if (entry != null && entry.preset == null)
            {
                entry.preset = preset;
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
        if (encyclopediaEntries.Count == 0)
        {
            Debug.LogWarning("No entries in encyclopedia.");
            return;
        }

        currentIndex = (currentIndex + 1) % encyclopediaEntries.Count;
        UpdateUI();
    }
    [Button]
    public void PreviousEntry()
    {
        if (encyclopediaEntries.Count == 0)
        {
            Debug.LogWarning("No entries in encyclopedia.");
            return;
        }

        currentIndex = (currentIndex - 1 + encyclopediaEntries.Count) % encyclopediaEntries.Count;
        UpdateUI();
    }

    private void UpdateUI()
    {
        FishEntryUI entryUI = GetComponentInChildren<FishEntryUI>(true);
        if (entryUI == null)
        {
            Debug.LogWarning("FishEntryUI component not found in children.");
            return;
        }

        if(encyclopediaEntries != null && encyclopediaEntries.Count > 0)
            entryUI.Populate(encyclopediaEntries[currentIndex]);
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
    }

}
