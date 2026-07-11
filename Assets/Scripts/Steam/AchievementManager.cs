using System.Collections;
using UnityEngine;

/// <summary>
/// Translates in-game progression events into Steam achievement unlocks.
///
/// This is a pure listener: it owns no gameplay state and only reacts to the existing event
/// backbone (<see cref="FishMilestoneTracker"/>, <see cref="FishEncyclopediaManager"/>,
/// <see cref="PlayerInventory"/>). It bootstraps itself into a persistent object on first scene
/// load, so there is nothing to wire into a prefab or scene.
///
/// === Steamworks setup ===
/// Each constant below is an achievement "API Name". Create a matching achievement (same string,
/// case-sensitive) under your App ID at https://partner.steamgames.com → Stats &amp; Achievements,
/// then publish. Renaming here without renaming there (or vice versa) means the unlock silently
/// fails — <see cref="SteamAchievements.Unlock"/> logs a warning when that happens.
/// </summary>
[DisallowMultipleComponent]
public class AchievementManager : MonoBehaviour
{
    // ---- Achievement API Names (must match the Steamworks backend exactly) -------------------
    private const string ACH_FIRST_CATCH   = "ACH_FIRST_CATCH";    // first fish ever
    private const string ACH_CATCH_10      = "ACH_CATCH_10";       // 10 total
    private const string ACH_CATCH_100     = "ACH_CATCH_100";      // 100 total
    private const string ACH_CATCH_1000    = "ACH_CATCH_1000";     // 1000 total
    private const string ACH_HUGE_FISH     = "ACH_HUGE_FISH";      // land a Huge size-class fish
    private const string ACH_LEGENDARY     = "ACH_LEGENDARY";      // land a legendary species
    private const string ACH_NIGHT_OWL     = "ACH_NIGHT_OWL";      // 25 fish caught at night
    private const string ACH_DEX_COMPLETE  = "ACH_DEX_COMPLETE";   // every species in the encyclopedia
    private const string ACH_WEALTHY       = "ACH_WEALTHY";        // hold 5000 coins at once
    private const string ACH_JUMP_CHAIN_10 = "ACH_JUMP_CHAIN_10";  // chain a jump 10 times in one run
    private const int WEALTHY_COIN_THRESHOLD = 5000;
    // 9 chain relaunches + the initial charged launch = 10 jumps in one chain.
    private const int JUMP_CHAIN_THRESHOLD = 9;

    // Tier unlocks keyed off FishMilestoneTracker.OnMilestoneReached. An empty Key matches any key
    // in that category (used for the catch-all Total counter); a set Key (e.g. "Huge", "Night")
    // matches only that bucket. Counts must be members of FishMilestoneTracker.Milestones to ever fire.
    private struct MilestoneAchievement
    {
        public MilestoneCategory Category;
        public string Key;     // "" = any key in the category
        public int Count;
        public string Id;

        public MilestoneAchievement(MilestoneCategory category, string key, int count, string id)
        {
            Category = category; Key = key; Count = count; Id = id;
        }
    }

    private static readonly MilestoneAchievement[] MilestoneAchievements =
    {
        new MilestoneAchievement(MilestoneCategory.Total,     "",      1,    ACH_FIRST_CATCH),
        new MilestoneAchievement(MilestoneCategory.Total,     "",      10,   ACH_CATCH_10),
        new MilestoneAchievement(MilestoneCategory.Total,     "",      100,  ACH_CATCH_100),
        new MilestoneAchievement(MilestoneCategory.Total,     "",      1000, ACH_CATCH_1000),
        new MilestoneAchievement(MilestoneCategory.SizeClass, "Huge",  1,    ACH_HUGE_FISH),
        new MilestoneAchievement(MilestoneCategory.Weather,   "Night", 25,   ACH_NIGHT_OWL),
    };

    private static AchievementManager instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject(nameof(AchievementManager));
        instance = go.AddComponent<AchievementManager>();
        DontDestroyOnLoad(go);
    }

    private void OnEnable()
    {
        FishMilestoneTracker.OnMilestoneReached += HandleMilestone;
        FishMilestoneTracker.OnCatchRegistered += HandleCatch;
        ChargeJumpController.OnJumpChainExtended += HandleJumpChain;
        StartCoroutine(WatchCurrency());
    }

    private void OnDisable()
    {
        FishMilestoneTracker.OnMilestoneReached -= HandleMilestone;
        FishMilestoneTracker.OnCatchRegistered -= HandleCatch;
        ChargeJumpController.OnJumpChainExtended -= HandleJumpChain;
    }

    // chainLength is the running number of chained jumps in the current run (see
    // ChargeJumpController.OnJumpChainExtended). Unlock is idempotent, so re-firing past the
    // threshold on longer chains is harmless.
    private void HandleJumpChain(int chainLength)
    {
        if (chainLength >= JUMP_CHAIN_THRESHOLD)
            SteamAchievements.Unlock(ACH_JUMP_CHAIN_10);
    }

    // PlayerInventory is a PER-SCENE singleton (recreated on every scene load) while this manager
    // persists across scenes, so we poll its currency rather than subscribe to OnInventoryChanged —
    // a captured event subscription would silently die the first time the scene reloaded. A coarse
    // poll is more than enough for a one-shot coin threshold. The static milestone/catch events,
    // by contrast, survive reloads and are safe to subscribe to directly.
    private IEnumerator WatchCurrency()
    {
        var wait = new WaitForSeconds(2f);
        while (true)
        {
            var inv = PlayerInventory.Instance;
            if (inv != null && inv.currentCurrency >= WEALTHY_COIN_THRESHOLD)
            {
                SteamAchievements.Unlock(ACH_WEALTHY);
                yield break; // earned for good — stop polling
            }
            yield return wait;
        }
    }

    private void HandleMilestone(MilestoneEvent evt)
    {
        foreach (var a in MilestoneAchievements)
        {
            if (a.Category != evt.category) continue;
            if (a.Count != evt.count) continue;
            if (!string.IsNullOrEmpty(a.Key) && a.Key != evt.key) continue;
            SteamAchievements.Unlock(a.Id);
        }
    }

    private void HandleCatch(CaughtFish fish)
    {
        if (fish != null && fish.preset != null && fish.preset.isLegendary)
            SteamAchievements.Unlock(ACH_LEGENDARY);

        if (IsEncyclopediaComplete())
            SteamAchievements.Unlock(ACH_DEX_COMPLETE);
    }

    // Complete = every known species has at least one recorded catch. Mirrors the "revealed" test
    // the encyclopedia UI uses (entry.hasCaught > 0).
    private static bool IsEncyclopediaComplete()
    {
        var manager = FishEncyclopediaManager.Instance;
        if (manager == null || manager.encyclopediaEntries == null) return false;

        int knownSpecies = 0;
        foreach (var entry in manager.encyclopediaEntries)
        {
            if (entry == null || entry.preset == null) continue;
            knownSpecies++;
            if (entry.hasCaught <= 0) return false;
        }
        return knownSpecies > 0;
    }
}
