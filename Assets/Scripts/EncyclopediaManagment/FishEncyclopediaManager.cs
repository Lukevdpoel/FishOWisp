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

    // A fish was zoom-researched (right-mouse aim lock on a fish of this species). Advances the
    // species' zoom count — once per distinct fish instance; the caller (FishResearchScanner)
    // enforces that. Reveals bait prefs in the notebook (BaitRevealed) on the first zoom.
    // Returns true only on the zoom that FIRST reveals this species' bait, so the caller can pop
    // the "you learned this fish's bait preferences!" message exactly once per species.
    public bool RegisterFishZoom(FishPreset preset)
    {
        if (preset == null) return false;
        var entry = GetOrCreateEntry(preset);
        bool wasRevealed = BaitRevealed(entry);
        entry.RegisterZoom();
        SaveEncyclopedia();
        return !wasRevealed && BaitRevealed(entry);
    }

    // Bait preferences show in the notebook once the species has been EITHER zoom-researched at all
    // or caught at least once. Used by FishEntryUI in place of the old catch-only reveal.
    public static bool BaitRevealed(FishEncyclopediaEntry entry)
        => entry != null && (entry.zoomResearchCount > 0 || entry.hasCaught > 0);

    // Whether this species' bait/lure preferences are ALREADY revealed. The catch-inspection
    // indicator calls this BEFORE the catch is registered (RegisterCaughtFish runs when the
    // fish is added to the inventory at the end of inspection), so a false result means "this
    // catch is the one that first reveals the preferences" — the moment worth surfacing.
    public bool IsBaitRevealed(FishPreset preset)
    {
        if (preset == null) return false;
        var entry = encyclopediaEntries.Find(e => e.preset != null && e.preset.fishName == preset.fishName);
        return BaitRevealed(entry);
    }

    // Debug helper (Ctrl+T): mark every known species as caught once so the whole notebook
    // is revealed. Only previously-uncaught fish get a catch registered, so real progress
    // (largest/smallest, catch counts) on already-discovered fish is left untouched. Each
    // catch uses a representative length from the preset's size range. Saves and refreshes
    // the UI once at the end rather than per-fish.
    public void RevealAllFish()
    {
        if (allFishPresets == null) return;

        int revealed = 0;
        foreach (var preset in allFishPresets)
        {
            if (preset == null) continue;
            var entry = GetOrCreateEntry(preset);
            if (entry.hasCaught > 0) continue;
            entry.RegisterCatch(new CaughtFish(preset).lengthCm);
            revealed++;
        }

        SaveEncyclopedia();
        UpdateUI();
        Debug.Log($"[FishEncyclopediaManager] RevealAllFish — revealed {revealed} new species.");
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
        // Persist by fishName, NEVER by object reference. JsonUtility writes a UnityEngine.Object
        // field as {"instanceID": N}, and instance IDs are reassigned every run — in a player build
        // they resolve to whatever unrelated object (a bait/lure/bobber) happens to hold that ID,
        // which poisoned the encyclopedia with non-fish entries that crashed when opened.
        var data = new EncyclopediaSaveData();
        foreach (var e in encyclopediaEntries)
        {
            if (e == null || e.preset == null) continue;
            data.entries.Add(new EncyclopediaEntryData
            {
                fishName = e.preset.fishName,
                hasCaught = e.hasCaught,
                largestCaught = e.largestCaught,
                smallestCaught = e.smallestCaught,
                zoomResearchCount = e.zoomResearchCount
            });
        }
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
    }

    public void LoadEncyclopedia()
    {
        encyclopediaEntries = new List<FishEncyclopediaEntry>();

        // Restore saved progress, re-linking each record to a real FishPreset by stable name.
        // Anything that doesn't match a known fish is dropped, so a corrupted or legacy (instanceID-
        // based) save can never inject non-fish entries. Legacy saves have no fishName, so they
        // simply re-seed as fresh uncaught entries below.
        if (File.Exists(SavePath))
        {
            EncyclopediaSaveData data = JsonUtility.FromJson<EncyclopediaSaveData>(File.ReadAllText(SavePath));
            if (data != null && data.entries != null)
            {
                foreach (var rec in data.entries)
                {
                    FishPreset preset = GetPresetByName(rec.fishName);
                    if (preset == null) continue;
                    if (encyclopediaEntries.Exists(e => e.preset == preset)) continue;
                    encyclopediaEntries.Add(new FishEncyclopediaEntry
                    {
                        preset = preset,
                        hasCaught = rec.hasCaught,
                        largestCaught = rec.largestCaught,
                        smallestCaught = rec.smallestCaught,
                        zoomResearchCount = rec.zoomResearchCount
                    });
                }
            }
        }

        // Make sure every known fish has an entry (uncaught ones show as mystery slots).
        foreach (var preset in allFishPresets)
        {
            if (preset == null) continue;
            if (!encyclopediaEntries.Exists(e => e.preset != null && e.preset.fishName == preset.fishName))
            {
                encyclopediaEntries.Add(new FishEncyclopediaEntry { preset = preset });
            }
        }
    }

    // Stable lookup used by both the encyclopedia load and the inventory load to re-link saved
    // records to real fish by name, instead of by unstable instance IDs.
    public FishPreset GetPresetByName(string fishName)
    {
        if (string.IsNullOrEmpty(fishName) || allFishPresets == null) return null;
        return allFishPresets.Find(p => p != null && p.fishName == fishName);
    }

    [System.Serializable]
    private class EncyclopediaSaveData
    {
        public List<EncyclopediaEntryData> entries = new List<EncyclopediaEntryData>();
    }

    [System.Serializable]
    private class EncyclopediaEntryData
    {
        public string fishName;
        public int hasCaught;
        public float largestCaught;
        public float smallestCaught;
        public int zoomResearchCount;
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

#if UNITY_EDITOR
    // The notebook can only show species baked into allFishPresets, and that list must already exist
    // in the prefab before the build (AssetDatabase is editor-only). FishPresetEncyclopediaSync keeps
    // it in lockstep with the project automatically whenever a FishPreset asset is added, removed, or
    // renamed — this button (and the "Tools/FishOWisp/Resync Notebook Fish" menu item) just force a
    // manual re-scan if you ever want one.
    [Button(ButtonSizes.Medium), PropertyOrder(-1)]
    public void RefreshAllFishPresets()
    {
        allFishPresets = CollectSortedPresets();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"FishEncyclopediaManager: allFishPresets now holds {allFishPresets.Count} species.", this);
    }

    // Single source of truth for "every usable fish in the project, in a stable order" — shared by the
    // button above and by the automatic sync, so the two can never drift. Presets with a blank fishName
    // are skipped on purpose: the encyclopedia and every save file key fish by fishName (see
    // SaveEncyclopedia/GetPresetByName), so an unnamed preset would collide with any other unnamed one
    // and corrupt the notebook. Half-finished fish are logged so they get noticed and named.
    public static List<FishPreset> CollectSortedPresets()
    {
        var collected = new List<FishPreset>();
        var skipped = new List<string>();
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:FishPreset"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var preset = UnityEditor.AssetDatabase.LoadAssetAtPath<FishPreset>(path);
            if (preset == null) continue;
            if (string.IsNullOrWhiteSpace(preset.fishName)) { skipped.Add(path); continue; }
            if (!collected.Contains(preset)) collected.Add(preset);
        }
        collected.Sort((a, b) => string.Compare(a.fishName, b.fishName, System.StringComparison.Ordinal));

        if (skipped.Count > 0)
        {
            Debug.LogWarning(
                $"FishEncyclopediaManager: {skipped.Count} FishPreset asset(s) have no fishName and were " +
                "left out of the notebook (give them a fishName and they'll be added automatically):\n  " +
                string.Join("\n  ", skipped));
        }
        return collected;
    }
#endif

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