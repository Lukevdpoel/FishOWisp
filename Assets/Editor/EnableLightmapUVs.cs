using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class EnableLightmapUVs
{
    [MenuItem("Tools/Lightmap UVs/Enable On All Models")]
    public static void EnableOnAll()
    {
        ProcessModels(onlyMissing: false);
    }

    [MenuItem("Tools/Lightmap UVs/Enable On Models Missing It")]
    public static void EnableOnMissing()
    {
        ProcessModels(onlyMissing: true);
    }

    [MenuItem("Tools/Lightmap UVs/Enable On Selected Models")]
    public static void EnableOnSelected()
    {
        var guids = Selection.assetGUIDs;
        var paths = new List<string>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is ModelImporter)
                paths.Add(path);
            else if (AssetDatabase.IsValidFolder(path))
                paths.AddRange(FindModelsInFolder(path));
        }
        Process(paths, onlyMissing: false);
    }

    static void ProcessModels(bool onlyMissing)
    {
        var guids = AssetDatabase.FindAssets("t:Model");
        var paths = new List<string>(guids.Length);
        foreach (var guid in guids)
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        Process(paths, onlyMissing);
    }

    static List<string> FindModelsInFolder(string folder)
    {
        var result = new List<string>();
        var guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
        foreach (var guid in guids)
            result.Add(AssetDatabase.GUIDToAssetPath(guid));
        return result;
    }

    static void Process(List<string> paths, bool onlyMissing)
    {
        int changed = 0;
        int skipped = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Enabling Lightmap UVs",
                        $"{i + 1}/{paths.Count}: {path}",
                        (float)i / Mathf.Max(1, paths.Count)))
                    break;

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { skipped++; continue; }

                if (onlyMissing && importer.generateSecondaryUV)
                {
                    skipped++;
                    continue;
                }

                if (!importer.generateSecondaryUV)
                {
                    importer.generateSecondaryUV = true;
                    importer.SaveAndReimport();
                    changed++;
                }
                else
                {
                    skipped++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[EnableLightmapUVs] Updated {changed} model(s), skipped {skipped}.");
    }
}
