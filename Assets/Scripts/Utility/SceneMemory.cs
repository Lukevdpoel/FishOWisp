using UnityEngine;

/// <summary>
/// Remembers which gameplay scene the player was last active in, so the game can boot back into it
/// (with that scene's own title-screen flythrough) instead of always starting in HUTBUILT.
///
/// Persisted to PlayerPrefs — the same lightweight approach the BountyBoard uses. The stored value
/// is the scene's build NAME (no path, no extension), which is exactly what SceneManager.LoadScene
/// expects.
///
/// Written whenever the player transitions into a scene (see <see cref="SceneTransitionManager.LoadScene"/>)
/// and read once at launch by <see cref="BootLoader"/>.
/// </summary>
public static class SceneMemory
{
    private const string Key = "LastActiveScene";

    /// <summary>Scene loaded on a brand-new game (nothing saved yet) and the fallback used if the
    /// saved scene is no longer present in the build settings.</summary>
    public const string DefaultScene = "HUTBUILT";

    /// <summary>The lightweight loader scene (build index 0). Never recorded as a "last active" scene
    /// so a relaunch can never get stuck booting into the empty loader.</summary>
    public const string BootScene = "Boot";

    /// <summary>Record the scene the player is moving into. No-ops for the loader scene.</summary>
    public static void Save(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || sceneName == BootScene) return;
        PlayerPrefs.SetString(Key, sceneName);
        PlayerPrefs.Save();
    }

    /// <summary>The last scene the player was active in, or <see cref="DefaultScene"/> if nothing is
    /// saved or the saved scene is no longer in the build.</summary>
    public static string Load()
    {
        string saved = PlayerPrefs.GetString(Key, DefaultScene);

        // Guard against a saved scene that was removed from / disabled in build settings (e.g. an old
        // save pointing at a scene you later dropped). Falling through to it would hard-fail the load.
        if (!Application.CanStreamedLevelBeLoaded(saved))
        {
            Debug.LogWarning($"[SceneMemory] Saved scene '{saved}' is not in the build settings; falling back to '{DefaultScene}'.");
            return DefaultScene;
        }
        return saved;
    }

    /// <summary>Forget the saved scene so the next launch starts at <see cref="DefaultScene"/> (used by
    /// the "wipe all progress / new game" debug path).</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }
}
