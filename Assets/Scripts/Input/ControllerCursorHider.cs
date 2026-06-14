using UnityEngine;

/// <summary>
/// While the player is on a controller, the mouse pointer is never shown — you don't aim
/// with it on a pad, so a stray cursor parked over the screen is just clutter.
///
/// Cursor visibility is set in a dozen scattered places (PlayerController, the inventory,
/// the notebook, dialogue, the shop, catch inspection...), several of which force it visible
/// for mouse-driven menus. Rather than thread a "but not on gamepad" check through every one,
/// this self-bootstrapping watchdog runs in LateUpdate — after all those Update() calls — and
/// simply re-hides the cursor whenever the active device is the gamepad. LateUpdate always
/// gets the last word, so it doesn't matter who tried to reveal the pointer earlier in the frame.
///
/// "Active device" (not merely "plugged in") is the trigger: a pad left connected while the
/// player uses the mouse keeps the cursor; it vanishes the instant they touch the controller
/// and comes back the instant they bump the mouse. ActiveInputDeviceTracker drives that flip.
///
/// Mirrors ActiveInputDeviceTracker: spawns itself on play, survives scene loads, no scene setup.
/// </summary>
public class ControllerCursorHider : MonoBehaviour
{
    private static ControllerCursorHider instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("[ControllerCursorHider]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ControllerCursorHider>();
    }

    private void OnEnable() => GamepadInput.OnActiveDeviceChanged += OnDeviceChanged;
    private void OnDisable() => GamepadInput.OnActiveDeviceChanged -= OnDeviceChanged;

    private void LateUpdate()
    {
        // Runs after every gameplay/UI Update that might have shown the cursor, so on a pad the
        // pointer stays gone no matter who tried to reveal it this frame. Lock state is left
        // alone — only visibility was asked for, and some systems read the mouse delta.
        if (GamepadInput.IsGamepadActive && Cursor.visible)
            Cursor.visible = false;
    }

    private void OnDeviceChanged(ActiveInputDevice device)
    {
        if (device != ActiveInputDevice.KeyboardMouse) return;

        // Switching back to the mouse: the per-frame cursor logic (PlayerController's
        // HandleCursorLocking) re-shows the pointer for dialogue/shop/inspection on its own,
        // and re-hides it during normal play. But the inventory and notebook set the cursor
        // visible only once when they open — which we overrode while on the pad — so hand the
        // cursor back here when one of those is still open, or it'd be stuck invisible.
        if (InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
