using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Self-bootstrapping runtime singleton that watches raw device activity every frame and
/// flips GamepadInput.ActiveDevice the moment the player uses the other device. No scene
/// setup needed — it spawns itself on play and survives scene loads.
///
/// Detection is actuation-based (sticks past the dead zone, triggers past the press point,
/// any button held / key pressed / real mouse movement) rather than device event timestamps,
/// because XInput pads emit state packets even when idle.
/// </summary>
public class ActiveInputDeviceTracker : MonoBehaviour
{
    // Real mouse motion threshold in pixels-per-frame, squared. Filters out sensor jitter
    // so a controller session isn't yanked back to keyboard by a nudged desk.
    private const float MouseDeltaSqrThreshold = 4f;

    private static ActiveInputDeviceTracker instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("[ActiveInputDeviceTracker]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ActiveInputDeviceTracker>();
    }

    private void Update()
    {
        // Gamepad checked first so simultaneous input prefers the controller — the player
        // resting a hand on the mouse shouldn't flicker prompts while they play on the pad.
        if (GamepadHasActivity()) GamepadInput.SetActiveDevice(ActiveInputDevice.Gamepad);
        else if (KeyboardMouseHasActivity()) GamepadInput.SetActiveDevice(ActiveInputDevice.KeyboardMouse);
    }

    private static bool GamepadHasActivity()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null) return false;

        if (pad.leftStick.ReadValue().magnitude > GamepadInput.StickDeadZone) return true;
        if (pad.rightStick.ReadValue().magnitude > GamepadInput.StickDeadZone) return true;
        if (pad.leftTrigger.ReadValue() > GamepadInput.TriggerPressPoint) return true;
        if (pad.rightTrigger.ReadValue() > GamepadInput.TriggerPressPoint) return true;
        if (pad.dpad.ReadValue() != Vector2.zero) return true;

        return pad.buttonSouth.isPressed || pad.buttonNorth.isPressed
            || pad.buttonEast.isPressed || pad.buttonWest.isPressed
            || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed
            || pad.leftStickButton.isPressed || pad.rightStickButton.isPressed
            || pad.startButton.isPressed || pad.selectButton.isPressed;
    }

    private static bool KeyboardMouseHasActivity()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.anyKey.isPressed) return true;

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed) return true;
            if (mouse.delta.ReadValue().sqrMagnitude > MouseDeltaSqrThreshold) return true;
            if (Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f) return true;
        }
        return false;
    }
}
