using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildVersionIncrementer : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        string current = PlayerSettings.bundleVersion;
        string[] parts = current.Split('.');

        int major = 0, minor = 0, patch = 0;
        if (parts.Length >= 1) int.TryParse(parts[0], out major);
        if (parts.Length >= 2) int.TryParse(parts[1], out minor);
        if (parts.Length >= 3) int.TryParse(parts[2], out patch);

        patch++;
        string newVersion = $"{major}.{minor}.{patch}";
        PlayerSettings.bundleVersion = newVersion;

        Debug.Log($"[BuildVersionIncrementer] Version incremented: {current} -> {newVersion}");
    }
}
