using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class SteamBuild : EditorWindow
{
    private const string APP_ID = "4002170"; // Replace with your game's App ID
    private const string PLAYTEST_APP_ID = "4002170"; // Replace with your playtest App ID
    private const string ITCH_GAME_URL_SLUG = "YOUR_GAME_ID"; // e.g., "https://your-name.itch.io/your-game" -> "your-name/your-game"

    private string version = string.Empty;
    private BuildType buildType = BuildType.SteamMain;
    private bool clearPreviousBuildFirst = false;

    public enum BuildType
    {
        SteamMain,
        SteamPlaytest,
        Itch,
    }

    [MenuItem("Build/Build for Platforms...")]
    public static void ShowWindow()
    {
        var window = GetWindow<SteamBuild>("Platform Build");
        window.minSize = new Vector2(300, 120);
    }

    private void OnEnable()
    {
        version = Application.version;
    }

    private void OnGUI()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("Build & Upload Automator", EditorStyles.boldLabel);

        buildType = (BuildType)EditorGUILayout.EnumPopup("Build Type", buildType);

        version = EditorGUILayout.TextField("Version", version);

        clearPreviousBuildFirst = EditorGUILayout.Toggle("Clear Previous Build", clearPreviousBuildFirst);

        GUILayout.Space(10);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Upload Only", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Confirm Upload",
                    $"Are you sure you want to upload version {version} to the '{buildType}' branch? This will be made public.",
                    "Yes, Upload", "No"))
            {
                ApplyVersion();
                string steamRoot = FindSteamRoot();
                string builderRoot = Path.Combine(steamRoot, "sdk", "tools", "ContentBuilder");
                string productionSubFolder = buildType switch
                {
                    BuildType.SteamMain => "SteamProduction_Main",
                    BuildType.SteamPlaytest => "SteamProduction_Playtest",
                    BuildType.Itch => "ItchProduction",
                    _ => throw new System.ArgumentOutOfRangeException(nameof(buildType), buildType, null)
                };
                DeleteBurstDebugFolder(Path.Combine(builderRoot, "content", productionSubFolder));
                LaunchBat(builderRoot);
            }
        }

        if (GUILayout.Button("Build & Upload", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Confirm Build & Upload",
                $"Are you sure you want to build and upload version {version} to the '{buildType}' branch? This will be made public.",
                "Yes, Build and Upload", "No"))
            {
                BuildAndUpload();
            }
        }

        GUILayout.EndVertical();
    }

    private void BuildAndUpload()
    {
        ApplyVersion();
        Debug.Log($"Building {buildType}. Version: {version}");

        string steamRoot = FindSteamRoot();
        Debug.Log($"Using Steam root at: {steamRoot}");

        string builderRoot = Path.Combine(steamRoot, "sdk", "tools", "ContentBuilder");
        string productionSubFolder = buildType switch
        {
            BuildType.SteamMain => "SteamProduction_Main",
            BuildType.SteamPlaytest => "SteamProduction_Playtest",
            BuildType.Itch => "ItchProduction",
            _ => throw new System.ArgumentOutOfRangeException(nameof(buildType), buildType, null)
        };
        string steamProductionPath = Path.Combine(builderRoot, "content", productionSubFolder);

        if (clearPreviousBuildFirst && Directory.Exists(steamProductionPath))
        {
            var di = new DirectoryInfo(steamProductionPath);
            foreach (var file in di.GetFiles()) file.Delete();
            foreach (var dir in di.GetDirectories()) dir.Delete(true);
            Debug.Log($"Cleared previous build in: {steamProductionPath}");
        }

        Directory.CreateDirectory(steamProductionPath);

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        string exeName = Application.productName + ".exe";
        string buildPath = Path.Combine(steamProductionPath, exeName);

        var buildOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Debug.Log($"Starting Unity player build to: {buildPath}");
        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Player build failed: {report.summary.result}");
            return;
        }

        DeleteBurstDebugFolder(steamProductionPath);

        Debug.Log("Player build succeeded. Launching batch...");
        LaunchBat(builderRoot);
    }

    private void ApplyVersion()
    {
        PlayerSettings.bundleVersion = version;
        Debug.Log($"Project version updated to {version}");
    }

    private void LaunchBat(string builderRoot)
    {
        if (buildType == BuildType.Itch)
            UpdateItchBatVersion(builderRoot);
        else
            UpdateVdfDesc(builderRoot);

        string batchFileName = GetBatchFileName(buildType);
        string batchFilePath = Path.Combine(builderRoot, batchFileName);
        Debug.Log($"Looking for batch at: {batchFilePath}");

        Debug.LogError($"ATTEMPTING TO RUN: {batchFilePath}");

        if (!File.Exists(batchFilePath))
        {
            Debug.LogError($"Batch file not found at: {batchFilePath}");
            return;
        }

        if (buildType != BuildType.Itch && !ValidateSteamCmd(builderRoot))
        {
            return;
        }

        string url = GetWeblink(buildType);
        var startInfo = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(batchFilePath))
        {
            WorkingDirectory = builderRoot,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        System.Diagnostics.Process proc = System.Diagnostics.Process.Start(startInfo);
        if (proc != null)
        {
            proc.EnableRaisingEvents = true;
            proc.Exited += (sender, args) =>
            {
                Debug.Log($"Upload finished. Opening URL: {url}");
                EditorApplication.delayCall += () => Application.OpenURL(url);
            };
            Debug.Log("Batch file started successfully.");
        }
        else
        {
            Debug.LogError("Failed to start batch process.");
        }
    }

    private static bool ValidateSteamCmd(string builderRoot)
    {
        string steamCmdPath = Path.Combine(builderRoot, "builder", "steamcmd.exe");
        if (File.Exists(steamCmdPath))
        {
            return true;
        }

        string message =
            $"SteamCMD was not found at:\n{steamCmdPath}\n\n" +
            "The sdk/tools/ContentBuilder/builder folder is ignored by version control, so the Windows SteamCMD files must be installed locally before Steam uploads can run.";
        Debug.LogError(message);
        EditorUtility.DisplayDialog("SteamCMD Missing", message, "OK");
        return false;
    }

    private void UpdateItchBatVersion(string builderRoot)
    {
        string batPath = Path.Combine(builderRoot, GetBatchFileName(BuildType.Itch));
        string text = File.ReadAllText(batPath);
        string pattern = @"--userversion\s+("".*?""|\S+)";
        string replacement = $"--userversion \"{version}\"";
        text = Regex.Replace(text, pattern, replacement);
        File.WriteAllText(batPath, text);
        Debug.Log($"Patched itch batch → version = {version}");
    }

    private static string FindSteamRoot()
    {
        string projectDir = Path.GetDirectoryName(Application.dataPath);

        //var sdkPath = Path.Combine(projectDir, "sdk"); 
        if (Directory.Exists(projectDir))
        {
            return projectDir;
        }

        Debug.LogError("SDK folder not found. Please ensure a folder named 'sdk' exists inside your Unity project folder.");
        return string.Empty;
    }

    private static string GetBatchFileName(BuildType buildType) => buildType switch
    {
        BuildType.SteamMain => "run_build_Main.bat",
        BuildType.SteamPlaytest => "run_build_Playtest.bat",
        BuildType.Itch => "run_build_Itch.bat",
        _ => string.Empty
    };

    private static string GetWeblink(BuildType buildType) => buildType switch
    {
        BuildType.SteamMain => $"https://partner.steamgames.com/apps/builds/{APP_ID}",
        BuildType.SteamPlaytest => $"https://partner.steamgames.com/apps/builds/{PLAYTEST_APP_ID}",
        BuildType.Itch => $"https://itch.io/game/edit/{ITCH_GAME_URL_SLUG}",
        _ => string.Empty
    };

    private static string GetVdfFileName(BuildType buildType) => buildType switch
    {
        BuildType.SteamMain => $"app_build_{APP_ID}.vdf",
        BuildType.SteamPlaytest => $"app_build_{PLAYTEST_APP_ID}.vdf",
        _ => throw new System.ArgumentOutOfRangeException(nameof(buildType), buildType, null)
    };

    private static void DeleteBurstDebugFolder(string buildOutputPath)
    {
        if (!Directory.Exists(buildOutputPath)) return;

        foreach (var dir in Directory.GetDirectories(buildOutputPath, "*_BurstDebugInformation_DoNotShip", SearchOption.TopDirectoryOnly))
        {
            Directory.Delete(dir, true);
            Debug.Log($"Deleted Burst debug folder: {dir}");
        }
    }

    private void UpdateVdfDesc(string builderRoot)
    {
        string scriptsFolder = Path.Combine(builderRoot, "scripts");
        string vdfName = GetVdfFileName(buildType);
        string vdfPath = Path.Combine(scriptsFolder, vdfName);

        if (!File.Exists(vdfPath))
        {
            Debug.LogError($"VDF script not found: {vdfPath}");
            return;
        }

        var attrs = File.GetAttributes(vdfPath);
        if (attrs.HasFlag(FileAttributes.ReadOnly))
        {
            File.SetAttributes(vdfPath, attrs & ~FileAttributes.ReadOnly);
        }

        string text = File.ReadAllText(vdfPath);
        string pattern = @"(?m)(?<=^\s*""desc""\s*"")[^""]*(?="")";
        string newText = Regex.Replace(text, pattern, version, RegexOptions.Multiline | RegexOptions.IgnoreCase);

        File.WriteAllText(vdfPath, newText);
        Debug.Log($"Patched {vdfName} → desc = {version}");
    }
}