// Same platform guard as the rest of the Steam wrappers, so this is inert on non-Steam targets.
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

/// <summary>
/// Boots and drives Steam Input purely so <see cref="SteamInputGlyphs"/> can hand out
/// controller-correct button artwork. It owns no gameplay input — the game still reads controllers
/// through Unity's Input System (<see cref="GamepadInput"/>). Like <see cref="AchievementManager"/>,
/// it bootstraps itself into a persistent object on first scene load, so there is nothing to wire
/// into a prefab or scene.
///
/// Responsibilities:
///   * Initialize Steam Input once (after <see cref="SteamManager"/> has brought up the Steam API).
///   * Poll the connected controllers and remember the active one's handle. Steam Input needs the
///     handle to translate origins into glyphs for the specific pad the player is holding.
///   * Clear the glyph cache and raise <see cref="SteamInputGlyphs.OnGlyphsChanged"/> when the
///     active controller (or its Steam config) changes, so on-screen prompts refresh.
///
/// === Steamworks setup (why a controller might be null in a real build) ===
/// GetConnectedControllers only returns pads Steam Input is actively managing. Steam Deck, PS, and
/// Switch controllers are managed by default; an Xbox / generic XInput pad only appears when the
/// player has "Xbox/Generic controller support" enabled in Steam Settings > Controller (or when you
/// ship an app-level Steam Input configuration that opts them in). When no handle is available the
/// glyph API stays a clean no-op and the UI falls back to <see cref="ButtonPrompts"/> text labels.
/// </summary>
[DisallowMultipleComponent]
public class SteamInputManager : MonoBehaviour
{
    // How often to re-scan connected controllers. Coarse is fine — this only drives which glyph set
    // to show, not input latency.
    private const float PollInterval = 0.5f;

    private static SteamInputManager instance;

#if !DISABLESTEAMWORKS
    private static bool steamInputStarted;
    private static InputHandle_t activeController;   // 0 == none

    private readonly InputHandle_t[] handleBuffer = new InputHandle_t[Constants.STEAM_INPUT_MAX_COUNT];
    private float nextPollTime;
#endif

    /// <summary>True when an active controller handle is available for glyph lookups.</summary>
    public static bool HasController
    {
        get
        {
#if DISABLESTEAMWORKS
            return false;
#else
            return steamInputStarted && activeController != default;
#endif
        }
    }

#if !DISABLESTEAMWORKS
    /// <summary>Hands the active controller handle to the glyph layer; false when none is connected.</summary>
    internal static bool TryGetControllerHandle(out InputHandle_t handle)
    {
        handle = activeController;
        return steamInputStarted && activeController != default;
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject(nameof(SteamInputManager));
        instance = go.AddComponent<SteamInputManager>();
        DontDestroyOnLoad(go);
    }

#if !DISABLESTEAMWORKS
    private void Update()
    {
        // Defer Init until the Steam API is up (SteamManager brings it online lazily via this call).
        if (!steamInputStarted)
        {
            if (!SteamManager.Initialized) return;
            // false => Steam Input's RunFrame is pumped automatically inside SteamAPI.RunCallbacks(),
            // which SteamManager already calls every frame, so we don't have to.
            steamInputStarted = SteamInput.Init(false);
            if (!steamInputStarted)
            {
                Debug.LogWarning("[SteamInputManager] SteamInput.Init failed; glyphs will fall back to text.");
                enabled = false;
                return;
            }
            Debug.Log("[SteamInputManager] Steam Input initialized; scanning for controllers.");
            nextPollTime = 0f; // scan immediately on the first initialized frame
        }

        if (Time.unscaledTime < nextPollTime) return;
        nextPollTime = Time.unscaledTime + PollInterval;
        RefreshActiveController();
    }

    private void RefreshActiveController()
    {
        int count = SteamInput.GetConnectedControllers(handleBuffer);
        // Keep the current pad if it's still connected (avoids thrashing between two pads); otherwise
        // adopt the first one reported. 0 controllers => clear.
        InputHandle_t chosen = default;
        for (int i = 0; i < count; i++)
        {
            if (handleBuffer[i] == activeController) { chosen = activeController; break; }
            if (chosen == default) chosen = handleBuffer[i];
        }

        if (chosen == activeController) return; // no change

        activeController = chosen;
        // Diagnostic: shows in the player log whether Steam Input is actually managing a pad. If
        // count stays 0 with a controller plugged in, Steam isn't managing it as a gamepad — enable
        // Steam Input for the game in the Steam client, or publish the app's Steam Input config.
        Debug.Log($"[SteamInputManager] Steam Input reports {count} managed controller(s); " +
                  $"active handle = {(chosen == default ? "none" : chosen.ToString())}.");
        SteamInputGlyphs.ClearCache();          // different pad => different glyph set
        SteamInputGlyphs.RaiseGlyphsChanged();
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        if (steamInputStarted)
        {
            SteamInput.Shutdown();
            steamInputStarted = false;
            activeController = default;
        }
    }
#endif
}
