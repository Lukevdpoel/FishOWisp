using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drop-in prompt widget that shows the Steam Input button glyph for a <see cref="PromptVerb"/> on
/// the controller the player is actually holding, and gracefully falls back to a text label
/// otherwise. Put it on a UI object with an <see cref="Image"/> (the glyph) and, optionally, a
/// <see cref="TMP_Text"/> (the fallback label). It re-resolves whenever the active device changes
/// or Steam reports a new controller/config, so it always matches what the player is using.
///
/// Resolution order for a single verb:
///   1. Gamepad active AND a Steam glyph resolves  -> show the glyph image, hide the label.
///   2. Otherwise                                  -> hide the image, show <see cref="ButtonPrompts"/>
///                                                    text (device-correct: "RT", "E", "Left Click"...).
///
/// This means the game works with no glyphs at all (text everywhere), lights up with real button
/// art the moment a Steam-managed controller is connected, and never shows a blank prompt.
/// </summary>
[DisallowMultipleComponent]
public class GamepadGlyph : MonoBehaviour
{
    [Tooltip("Which action this prompt represents. The physical button is resolved per-device at runtime.")]
    public PromptVerb verb = PromptVerb.Cast;

    [Tooltip("Image that displays the button glyph. Defaults to an Image on this object.")]
    public Image glyphImage;

    [Tooltip("Optional text shown when no glyph is available (keyboard, or a controller Steam Input " +
             "isn't managing). Defaults to a TMP_Text on this object or its children.")]
    public TMP_Text fallbackLabel;

    private void Awake()
    {
        if (glyphImage == null) glyphImage = GetComponent<Image>();
        if (fallbackLabel == null) fallbackLabel = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnEnable()
    {
        GamepadInput.OnActiveDeviceChanged += HandleDeviceChanged;
        SteamInputGlyphs.OnGlyphsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        GamepadInput.OnActiveDeviceChanged -= HandleDeviceChanged;
        SteamInputGlyphs.OnGlyphsChanged -= Refresh;
    }

    private void HandleDeviceChanged(ActiveInputDevice _) => Refresh();

    /// <summary>Re-resolve the prompt for the current device. Safe to call any time.</summary>
    public void Refresh()
    {
        Sprite glyph = null;
        if (GamepadInput.IsGamepadActive && glyphImage != null)
            SteamInputGlyphs.TryGetGlyphSprite(GamepadInput.ControlFor(verb), out glyph);

        if (glyph != null)
        {
            glyphImage.sprite = glyph;
            glyphImage.enabled = true;
            if (fallbackLabel != null) fallbackLabel.enabled = false;
            return;
        }

        if (glyphImage != null) glyphImage.enabled = false;
        if (fallbackLabel != null)
        {
            fallbackLabel.enabled = true;
            fallbackLabel.text = ButtonPrompts.For(verb);
        }
    }
}
