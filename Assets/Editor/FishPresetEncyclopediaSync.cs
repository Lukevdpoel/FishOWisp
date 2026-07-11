using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Keeps every FishEncyclopediaManager's baked allFishPresets list in lockstep with the FishPreset
// assets in the project. The notebook can only show species that were baked into the prefab before
// the build (AssetDatabase is editor-only), and that list used to be maintained entirely by hand — so
// a freshly added fish silently never appeared as a notebook silhouette. This watcher rebuilds the
// list automatically whenever a FishPreset is added, deleted, or renamed, and writes it into every
// prefab that hosts a FishEncyclopediaManager (PlayerMaster is the live one; GameplayUI also carries
// a copy). Use Tools/FishOWisp/Resync Notebook Fish to force a pass by hand.
public class FishPresetEncyclopediaSync : AssetPostprocessor
{
    // Managers live under Prefabs/; limiting the scan there keeps unrelated prefabs untouched.
    private const string ManagerPrefabFolder = "Assets/Prefabs";

    // Re-entrancy guard: SaveAsPrefabAsset queues a reimport of the .prefab, which fires
    // OnPostprocessAllAssets again. That pass won't see a changed FishPreset so it bails anyway, but
    // the flag makes the no-loop guarantee explicit.
    private static bool _syncing;

    private static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (_syncing || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!FishPresetsTouched(imported, deleted, moved)) return;
        SyncAllManagers(verbose: false);
    }

    [MenuItem("Tools/FishOWisp/Resync Notebook Fish")]
    private static void ResyncMenu() => SyncAllManagers(verbose: true);

    // Cheap pre-filter so we don't scan prefabs on every unrelated import. Imported/moved assets can be
    // type-checked directly; a deleted asset can't be loaded to inspect its type, so any deleted .asset
    // triggers a re-check (harmless — the sync is a no-op when nothing actually changed).
    private static bool FishPresetsTouched(string[] imported, string[] deleted, string[] moved)
    {
        foreach (var p in imported) if (IsFishPreset(p)) return true;
        foreach (var p in moved) if (IsFishPreset(p)) return true;
        foreach (var p in deleted)
            if (p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsFishPreset(string path) =>
        path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
        AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(FishPreset);

    private static void SyncAllManagers(bool verbose)
    {
        _syncing = true;
        try
        {
            List<FishPreset> presets = FishEncyclopediaManager.CollectSortedPresets();
            var updated = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ManagerPrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Leave archived/backup prefabs alone so editing a fish doesn't churn "-old" copies.
                if (path.IndexOf("-old", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var mgr = asset != null ? asset.GetComponentInChildren<FishEncyclopediaManager>(true) : null;
                if (mgr == null) continue;
                if (SameSequence(mgr.allFishPresets, presets)) continue; // already correct, skip the write

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var target = root.GetComponentInChildren<FishEncyclopediaManager>(true);
                    if (target != null)
                    {
                        target.allFishPresets = new List<FishPreset>(presets);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        updated.Add(path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (updated.Count > 0)
            {
                Debug.Log(
                    $"FishPresetEncyclopediaSync: notebook fish list now holds {presets.Count} species; " +
                    $"updated {updated.Count} prefab(s):\n  " + string.Join("\n  ", updated));
            }
            else if (verbose)
            {
                Debug.Log($"FishPresetEncyclopediaSync: notebook prefabs already up to date ({presets.Count} species).");
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static bool SameSequence(List<FishPreset> current, List<FishPreset> target)
    {
        int currentCount = current != null ? current.Count : 0;
        if (currentCount != target.Count) return false;
        for (int i = 0; i < target.Count; i++)
            if (current[i] != target[i]) return false;
        return true;
    }
}
