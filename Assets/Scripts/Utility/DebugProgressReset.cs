using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Debug wipe for every piece of saved progress, in one button:
//   - inventory.json        (PlayerInventory: coins + caught fish)
//   - encyclopedia.json     (FishEncyclopediaManager: caught-species registry)
//   - bait.json             (BaitInventory: bait counts)
//   - bobbers.json          (BobberInventory: owned tackle + equipped selection)
//   - fish_milestones.json  (FishMilestoneTracker: catch-count milestones)
//   - Bounty* PlayerPrefs   (BountyBoard: today's bounties + delivery progress)
//
// In play mode the active scene reloads after the wipe, so every manager restarts
// against the empty save exactly like a brand-new game: default coins, the starting
// bait grant, an all-??? encyclopedia and freshly rolled bounties. Outside play mode
// the files are just deleted — the next play session starts fresh.
//
// Ctrl+R goes one step further: it wipes everything AND drops back to the startup title
// screen (RestartToBootMenu), i.e. a true "restart from the bootup menu". It also zeroes the
// coin purse (the new-game default is 60), so the restart is a literal clean slate rather than
// a fresh-game start. That shortcut is gated to the editor and development builds so a shipped
// release can never nuke a player's save on a stray key combo.
public class DebugProgressReset : MonoBehaviour
{
    [Tooltip("Scene that hosts the startup title screen (MainMenuController). Ctrl+R loads this " +
             "scene and replays the title, so a full restart drops you back at the boot menu.")]
    [SerializeField] private string bootMenuScene = "HUTBUILT";

    [Button("Wipe All Progress (new game)", ButtonSizes.Large), GUIColor(1f, 0.45f, 0.45f)]
    public void WipeAllProgress()
    {
        WipeSaveFiles();

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

    // Full restart from the bootup menu: wipe every save, then load the title-screen scene and
    // force the title to replay (it normally shows only once per play session). Unlike the "Wipe
    // All Progress" button (which emulates a brand-new game and keeps the 60-coin starting balance),
    // this hard restart drops the player to 0 coins for a true clean slate.
    [Button("Restart To Boot Menu (Ctrl+R)", ButtonSizes.Large), GUIColor(1f, 0.6f, 0.45f)]
    public void RestartToBootMenu()
    {
        WipeSaveFiles();
        SeedZeroCurrencyInventory();
        Debug.Log("[DebugProgressReset] Ctrl+R — wiped all progress (0 coins), restarting at the boot menu.");

        // The notebook pause writes Time.timeScale directly and it survives scene loads; clear it
        // or the freshly loaded menu scene comes up frozen.
        Time.timeScale = 1f;
        MainMenuController.RequestShowOnNextLoad();
        SceneManager.LoadScene(bootMenuScene);
    }

    // PlayerInventory.currentCurrency defaults to 60 (the new-game starting purse) when no save
    // exists, so simply deleting inventory.json would leave the restart at 60. Seed a fresh save
    // with an empty fish list and 0 coins instead, so the reloaded PlayerInventory reads exactly 0.
    // Shape must match PlayerInventory.InventorySaveData ({ currency, fishes }).
    private static void SeedZeroCurrencyInventory()
    {
        string path = Path.Combine(Application.persistentDataPath, "inventory.json");
        File.WriteAllText(path, "{\"currency\":0,\"fishes\":[]}");
        Debug.Log("[DebugProgressReset] Seeded inventory.json with 0 coins.");
    }

    // Ctrl+R = wipe-and-restart-to-menu. Read through the Input System (Keyboard.current) to match
    // the rest of the project.
    //
    // NOTE: this was previously gated to `#if UNITY_EDITOR || DEVELOPMENT_BUILD` so it could never
    // fire in a shipped (non-development) build. That gate is why the shortcut "disappeared" in the
    // build — a release build compiled this Update out. The gate has been removed by request so the
    // shortcut works in every build. The trade-off: a stray Ctrl+R in a shipped release will now
    // wipe the player's save, so re-add the gate (or remove this component) before public release.
    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.ctrlKey.isPressed && kb.rKey.wasPressedThisFrame)
            RestartToBootMenu();

        // Ctrl+T = reveal the whole notebook (catch every species once). Debug convenience for
        // checking encyclopedia art/layout without grinding catches.
        if (kb.ctrlKey.isPressed && kb.tKey.wasPressedThisFrame)
            RevealAllFish();
    }

    [Button("Reveal All Fish (Ctrl+T)", ButtonSizes.Large), GUIColor(0.45f, 0.8f, 1f)]
    public void RevealAllFish()
    {
        if (FishEncyclopediaManager.Instance == null)
        {
            Debug.LogWarning("[DebugProgressReset] Ctrl+T — no FishEncyclopediaManager in the scene; nothing revealed.");
            return;
        }
        FishEncyclopediaManager.Instance.RevealAllFish();
        Debug.Log("[DebugProgressReset] Ctrl+T — revealed all fish in the notebook.");
    }

    private static void WipeSaveFiles()
    {
        DeleteSaveFile("inventory.json");
        DeleteSaveFile("encyclopedia.json");
        DeleteSaveFile("bait.json");
        DeleteSaveFile("bobbers.json");
        DeleteSaveFile("fish_milestones.json");
        DeleteBountyPrefs();

        // A wiped save is a new game, so forget the last-active scene too — the next launch should
        // start back at the default boot scene rather than wherever the player happened to quit.
        SceneMemory.Clear();
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
