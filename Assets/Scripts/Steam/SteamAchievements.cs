// Mirrors the platform guard in SteamManager.cs so this compiles to harmless no-ops on any
// target that doesn't ship the Steam API (and so the Steamworks namespace is only referenced
// when it actually exists).
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

/// <summary>
/// Thin, crash-proof wrapper around Steam's achievement API.
///
/// Every method is a graceful no-op when Steam isn't available — running in the editor without
/// the Steam client, a non-Steam build, or before <see cref="SteamManager"/> has initialized — so
/// gameplay code can fire achievements unconditionally without guarding each call site.
///
/// Achievement IDs are the "API Name" values configured per App ID in the Steamworks partner
/// backend (https://partner.steamgames.com → Stats &amp; Achievements). They MUST match exactly;
/// see <see cref="AchievementManager"/> for the canonical list this game uses.
/// </summary>
public static class SteamAchievements
{
    /// <summary>True when achievement calls will actually reach Steam.</summary>
    public static bool Available =>
#if DISABLESTEAMWORKS
        false;
#else
        SteamManager.Initialized;
#endif

    /// <summary>
    /// Unlock an achievement by its Steamworks API Name. Idempotent and cheap to re-call: an
    /// already-unlocked achievement skips the network store, and an empty id is ignored.
    /// </summary>
    public static void Unlock(string achievementId)
    {
        if (string.IsNullOrEmpty(achievementId)) return;
#if !DISABLESTEAMWORKS
        if (!SteamManager.Initialized)
        {
            Debug.LogWarning($"[SteamAchievements] Unlock('{achievementId}') ignored — Steam not initialized " +
                "(not launched through Steam, or no steam_appid.txt next to the build with the Steam client running).");
            return;
        }

        // GetAchievement only returns true once the current user's stats have arrived from Steam.
        // If it hasn't (false), we still fall through and SetAchievement — Steam stores it as soon
        // as stats are available — we just can't short-circuit the already-unlocked case yet.
        if (SteamUserStats.GetAchievement(achievementId, out bool alreadyUnlocked) && alreadyUnlocked)
            return;

        if (!SteamUserStats.SetAchievement(achievementId))
        {
            Debug.LogWarning(
                $"[SteamAchievements] SetAchievement('{achievementId}') failed. Confirm an " +
                "achievement with this exact API Name exists in the Steamworks backend for this App ID.");
            return;
        }

        // Flush to Steam so the toast pops and progress persists immediately.
        SteamUserStats.StoreStats();
        Debug.Log($"[SteamAchievements] Unlocked '{achievementId}' and stored stats.");
#endif
    }

    /// <summary>Whether the given achievement is already unlocked for the current user.</summary>
    public static bool IsUnlocked(string achievementId)
    {
#if !DISABLESTEAMWORKS
        if (SteamManager.Initialized &&
            SteamUserStats.GetAchievement(achievementId, out bool unlocked))
            return unlocked;
#endif
        return false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only testing helper: wipes every stat AND achievement for the current Steam user.
    /// Useful for re-testing first-time unlock toasts. There is no in-game caller.
    /// </summary>
    public static void ResetAllForTesting()
    {
#if !DISABLESTEAMWORKS
        if (!SteamManager.Initialized) return;
        SteamUserStats.ResetAllStats(true); // bAchievementsToo: true
        SteamUserStats.StoreStats();
        Debug.Log("[SteamAchievements] Reset all stats and achievements for the current user.");
#endif
    }
#endif
}
