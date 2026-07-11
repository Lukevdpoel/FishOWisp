// Mirrors the platform guard in SteamManager.cs / SteamAchievements.cs so this compiles to
// harmless no-ops on any target that doesn't ship the Steam API (and so the Steamworks namespace
// is only referenced when it actually exists).
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using System.Collections.Generic;
using UnityEngine;
#if !DISABLESTEAMWORKS
using System.IO;
using Steamworks;
#endif

/// <summary>
/// Turns one of the game's abstract <see cref="GamepadControl"/> bindings into the correct
/// on-screen button glyph for the controller the player is actually holding — Xbox, DualSense,
/// Switch Pro, Steam Deck, Steam Controller, or any pad Steam Input manages — and even reflects
/// remaps the player has made in Steam's own controller-config UI.
///
/// === Which Steam Input integration level this is ===
/// This is the *glyph-only* path. The game keeps reading input through Unity's Input System
/// (<see cref="GamepadInput"/>); Steam Input is used ONLY to fetch matching artwork. That means we
/// do NOT need an In-Game Actions (IGA) file or action handles — we translate a standard Xbox
/// origin (e.g. "the A button") to the connected device's real action origin with
/// <c>GetActionOriginFromXboxOrigin</c>, then ask Steam for that origin's glyph. Valve documents
/// this exact chain as the future-proof way to show glyphs for games that use XInput/standard input.
///
/// === Availability ===
/// Every call is a graceful no-op (returns false / null) unless ALL of these hold:
///   * the build ships the Steam API and was launched through Steam (<see cref="SteamManager"/>),
///   * <see cref="SteamInputManager"/> has initialized Steam Input and found a connected controller,
///   * that controller is one Steam Input is managing (see the setup notes in SteamInputManager).
/// UI should therefore always fall back to <see cref="ButtonPrompts"/> text when a glyph is null,
/// which is what <see cref="GamepadGlyph"/> does.
///
/// Loaded glyph <see cref="Texture2D"/>s and <see cref="Sprite"/>s are cached per action origin and
/// reused; the cache is cleared whenever the active controller (and thus its glyph set) changes.
/// </summary>
public static class SteamInputGlyphs
{
    /// <summary>True when a glyph request can actually resolve to artwork right now.</summary>
    public static bool Available
    {
        get
        {
#if DISABLESTEAMWORKS
            return false;
#else
            return SteamInputManager.HasController;
#endif
        }
    }

    /// <summary>
    /// Raised whenever the active controller or its Steam configuration changes, so any on-screen
    /// prompts can re-fetch their glyphs. Fired by <see cref="SteamInputManager"/>.
    /// </summary>
    public static event System.Action OnGlyphsChanged;

    internal static void RaiseGlyphsChanged() => OnGlyphsChanged?.Invoke();

    /// <summary>
    /// Best-effort glyph texture for a bound control on the connected controller. Returns false and
    /// a null texture when Steam Input isn't available or the control has no glyph — callers should
    /// fall back to a text label in that case.
    /// </summary>
    public static bool TryGetGlyphTexture(GamepadControl control, out Texture2D texture)
    {
        texture = null;
#if DISABLESTEAMWORKS
        return false;
#else
        if (!SteamInputManager.HasController) return false;
        if (!TryGetOrigin(control, out EInputActionOrigin origin)) return false;

        if (textureCache.TryGetValue(origin, out texture))
            return texture != null;

        // GetGlyphPNGForActionOrigin writes the PNG to Steam's cache on disk and hands back an
        // absolute file path. Small = 32px; bump to Medium/Large if prompts render bigger. Flags 0
        // = the default "knockout" style (white glyph on transparent), which tints cleanly.
        string path = SteamInput.GetGlyphPNGForActionOrigin(
            origin, ESteamInputGlyphSize.k_ESteamInputGlyphSize_Small, 0);

        texture = LoadPng(path);
        textureCache[origin] = texture;   // cache even null so we don't re-hit the disk every frame
        return texture != null;
#endif
    }

    /// <summary>
    /// Best-effort UI <see cref="Sprite"/> wrapping the glyph texture, ready to drop onto an
    /// <c>Image</c>. Same fallback contract as <see cref="TryGetGlyphTexture"/>.
    /// </summary>
    public static bool TryGetGlyphSprite(GamepadControl control, out Sprite sprite)
    {
        sprite = null;
#if DISABLESTEAMWORKS
        return false;
#else
        if (!TryGetOrigin(control, out EInputActionOrigin origin)) return false;
        if (spriteCache.TryGetValue(origin, out sprite))
            return sprite != null;

        if (TryGetGlyphTexture(control, out Texture2D tex) && tex != null)
        {
            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            sprite.name = origin.ToString();
        }
        spriteCache[origin] = sprite;
        return sprite != null;
#endif
    }

    // Wipe cached artwork; called by the manager when the controller/config changes so the next
    // request re-fetches glyphs for the new device.
    internal static void ClearCache()
    {
#if !DISABLESTEAMWORKS
        foreach (var sprite in spriteCache.Values)
            if (sprite != null) Object.Destroy(sprite);
        foreach (var tex in textureCache.Values)
            if (tex != null) Object.Destroy(tex);
        spriteCache.Clear();
        textureCache.Clear();
#endif
    }

#if !DISABLESTEAMWORKS
    private static readonly Dictionary<EInputActionOrigin, Texture2D> textureCache = new();
    private static readonly Dictionary<EInputActionOrigin, Sprite> spriteCache = new();

    // Translate our abstract binding into the connected device's real origin. We express the request
    // in Xbox terms (A/B/X/Y, bumpers, triggers, ...) and let Steam remap it to whatever the player
    // is holding — this is what makes a DualSense show Cross and a Switch pad show its B, plus honors
    // any in-Steam rebinds. Returns false for controls with no standard glyph (e.g. GamepadControl.None).
    private static bool TryGetOrigin(GamepadControl control, out EInputActionOrigin origin)
    {
        origin = EInputActionOrigin.k_EInputActionOrigin_None;
        if (!SteamInputManager.TryGetControllerHandle(out InputHandle_t handle)) return false;
        if (!TryGetXboxOrigin(control, out EXboxOrigin xbox)) return false;

        origin = SteamInput.GetActionOriginFromXboxOrigin(handle, xbox);
        return origin != EInputActionOrigin.k_EInputActionOrigin_None;
    }

    private static bool TryGetXboxOrigin(GamepadControl control, out EXboxOrigin origin)
    {
        switch (control)
        {
            // Face buttons are addressed by PHYSICAL POSITION here (GamepadControl mirrors Unity's
            // Input System), and the Xbox origins are the physical-position anchors Steam remaps
            // per device — so South->A gives the bottom button's glyph on every pad.
            case GamepadControl.ButtonSouth:      origin = EXboxOrigin.k_EXboxOrigin_A; return true;
            case GamepadControl.ButtonEast:       origin = EXboxOrigin.k_EXboxOrigin_B; return true;
            case GamepadControl.ButtonWest:       origin = EXboxOrigin.k_EXboxOrigin_X; return true;
            case GamepadControl.ButtonNorth:      origin = EXboxOrigin.k_EXboxOrigin_Y; return true;
            case GamepadControl.LeftShoulder:     origin = EXboxOrigin.k_EXboxOrigin_LeftBumper; return true;
            case GamepadControl.RightShoulder:    origin = EXboxOrigin.k_EXboxOrigin_RightBumper; return true;
            case GamepadControl.LeftTrigger:      origin = EXboxOrigin.k_EXboxOrigin_LeftTrigger_Pull; return true;
            case GamepadControl.RightTrigger:     origin = EXboxOrigin.k_EXboxOrigin_RightTrigger_Pull; return true;
            case GamepadControl.LeftStickButton:  origin = EXboxOrigin.k_EXboxOrigin_LeftStick_Click; return true;
            case GamepadControl.RightStickButton: origin = EXboxOrigin.k_EXboxOrigin_RightStick_Click; return true;
            case GamepadControl.Start:            origin = EXboxOrigin.k_EXboxOrigin_Menu; return true;   // Start
            case GamepadControl.Select:           origin = EXboxOrigin.k_EXboxOrigin_View; return true;   // Back
            case GamepadControl.DpadUp:           origin = EXboxOrigin.k_EXboxOrigin_DPad_North; return true;
            case GamepadControl.DpadDown:         origin = EXboxOrigin.k_EXboxOrigin_DPad_South; return true;
            case GamepadControl.DpadLeft:         origin = EXboxOrigin.k_EXboxOrigin_DPad_West; return true;
            case GamepadControl.DpadRight:        origin = EXboxOrigin.k_EXboxOrigin_DPad_East; return true;
            default:                              origin = default; return false; // None / non-glyph controls
        }
    }

    private static Texture2D LoadPng(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            // Size is overwritten by LoadImage; mipChain off keeps small UI glyphs crisp.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false) { name = Path.GetFileName(path) };
            if (tex.LoadImage(bytes))
            {
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            Object.Destroy(tex);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SteamInputGlyphs] Failed to load glyph PNG '{path}': {e.Message}");
        }
        return null;
    }
#endif
}
