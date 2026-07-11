using UnityEngine;

/// <summary>
/// Drop-in for any interaction prompt: assign a keyboard variant (e.g. the existing "E"
/// icon) and a gamepad variant (e.g. an "X" button icon), and this swaps them whenever
/// GamepadInput.ActiveDevice changes. Put it on the prompt's parent so it stays enabled
/// while the variants toggle underneath it.
/// </summary>
public class DevicePromptSwitcher : MonoBehaviour
{
    [Tooltip("Shown while the player is on keyboard/mouse (e.g. the 'E' icon).")]
    public GameObject keyboardPrompt;
    [Tooltip("Shown while the player is on an Xbox-style controller (e.g. an 'X' button icon). " +
             "Also the fallback for PlayStation/Nintendo pads when no brand variant is assigned.")]
    public GameObject gamepadPrompt;
    [Tooltip("Optional: shown instead of the gamepad prompt while a PlayStation pad is active " +
             "(e.g. a 'Square' icon). Leave empty to reuse the gamepad prompt for both brands.")]
    public GameObject playstationPrompt;
    [Tooltip("Optional: shown instead of the gamepad prompt while a Nintendo Switch Pro " +
             "Controller is active (e.g. a 'Y' button icon). Leave empty to reuse the gamepad " +
             "prompt for this brand.")]
    public GameObject nintendoPrompt;

    private void OnEnable()
    {
        Apply(GamepadInput.ActiveDevice);
        GamepadInput.OnActiveDeviceChanged += Apply;
    }

    private void OnDisable()
    {
        GamepadInput.OnActiveDeviceChanged -= Apply;
    }

    private void Apply(ActiveInputDevice device)
    {
        bool onKeyboard = device == ActiveInputDevice.KeyboardMouse;
        GamepadKind kind = GamepadInput.ActiveGamepadKind;
        bool onPlayStation = !onKeyboard && kind == GamepadKind.PlayStation && playstationPrompt != null;
        bool onNintendo = !onKeyboard && kind == GamepadKind.Nintendo && nintendoPrompt != null;

        if (keyboardPrompt != null) keyboardPrompt.SetActive(onKeyboard);
        if (gamepadPrompt != null) gamepadPrompt.SetActive(!onKeyboard && !onPlayStation && !onNintendo);
        if (playstationPrompt != null) playstationPrompt.SetActive(onPlayStation);
        if (nintendoPrompt != null) nintendoPrompt.SetActive(onNintendo);
    }
}
