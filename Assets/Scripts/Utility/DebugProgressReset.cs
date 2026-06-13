using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

// Debug wipe for every piece of saved progress, in one button:
//   - inventory.json        (PlayerInventory: coins + caught fish)
//   - encyclopedia.json     (FishEncyclopediaManager: caught-species registry)
//   - bait.json             (BaitInventory: bait counts)
//   - fish_milestones.json  (FishMilestoneTracker: catch-count milestones)
//   - Bounty* PlayerPrefs   (BountyBoard: today's bounties + delivery progress)
//
// In play mode the active scene reloads after the wipe, so every manager restarts
// against the empty save exactly like a brand-new game: default coins, the starting
// bait grant, an all-??? encyclopedia and freshly rolled bounties. Outside play mode
// the files are just deleted — the next play session starts fresh.
public class DebugProgressReset : MonoBehaviour
{
    [Button("Wipe All Progress (new game)", ButtonSizes.Large), GUIColor(1f, 0.45f, 0.45f)]
    public void WipeAllProgress()
    {
        DeleteSaveFile("inventory.json");
        DeleteSaveFile("encyclopedia.json");
        DeleteSaveFile("bait.json");
        DeleteSaveFile("fish_milestones.json");
        DeleteBountyPrefs();

        if (!Application.isPlaying)
        {
            Debug.Log("[DebugProgressReset] All saves wiped — the next play session starts as a new game.");
            return;
        }

        // Reload rather than resetting the managers in place: live UI (encyclopedia raster,
        // bait bar, bounty board) holds references into the old data, and a fresh scene
        // boot is the one path guaranteed to rebuild all of it consistently.
        //
        // The notebook pauses by writing Time.timeScale directly, and timeScale survives
        // scene loads — restore it or the fresh scene comes up frozen.
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private static void DeleteSaveFile(string fileName)
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!File.Exists(path)) return;
        File.Delete(path);
        Debug.Log($"[DebugProgressReset] Deleted {fileName}");
    }

    private static void DeleteBountyPrefs()
    {
        int count = PlayerPrefs.GetInt("BountyCount", 0);
        for (int i = 0; i < count; i++)
        {
            PlayerPrefs.DeleteKey($"BountyFish_{i}");
            PlayerPrefs.DeleteKey($"BountyReq_{i}");
            PlayerPrefs.DeleteKey($"BountyDel_{i}");
            PlayerPrefs.DeleteKey($"BountyReward_{i}");
            PlayerPrefs.DeleteKey($"BountyDone_{i}");
        }
        PlayerPrefs.DeleteKey("BountyCount");
        PlayerPrefs.DeleteKey("BountyDate");
        PlayerPrefs.Save();
    }
}
