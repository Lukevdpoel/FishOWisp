using System;
using UnityEngine;

/// <summary>
/// Physical controls on a gamepad, brand-agnostic. Unity's Gamepad abstraction already unifies
/// Xbox and PlayStation pads, so ButtonSouth is A/Cross, ButtonEast is B/Circle, and so on —
/// keeping separate Xbox/PlayStation tables in <see cref="InputBindings"/> only matters when you
/// WANT a different layout per brand (e.g. swapping confirm/cancel for a PlayStation player).
/// </summary>
public enum GamepadControl
{
    None,
    ButtonSouth, ButtonNorth, ButtonEast, ButtonWest,
    LeftShoulder, RightShoulder,
    LeftTrigger, RightTrigger,
    LeftStickButton, RightStickButton,
    Start, Select,
    DpadUp, DpadDown, DpadLeft, DpadRight,
}

/// <summary>
/// One gamepad's action→control table. Field defaults mirror the hardcoded layout the game
/// shipped with, so a freshly created asset (or a missing one) behaves exactly as before.
/// </summary>
[Serializable]
public class GamepadBindings
{
    [Header("Movement")]
    [Tooltip("Single click latches sprint on; it holds while moving and drops when you slow/stop.")]
    public GamepadControl sprintToggle = GamepadControl.LeftStickButton;

    [Header("World / Menus")]
    public GamepadControl jump = GamepadControl.ButtonNorth;
    [Tooltip("Interact with the world, hook a biting fish, commit a charged cast, and confirm/" +
             "advance menus & dialogue — all the same button.")]
    public GamepadControl interact = GamepadControl.ButtonSouth;
    [Tooltip("Cancel / close menus; also advances/skips dialogue.")]
    public GamepadControl cancel = GamepadControl.ButtonEast;
    public GamepadControl notebookToggle = GamepadControl.Start;
    public GamepadControl inventoryToggle = GamepadControl.Select;
    [Tooltip("Flip the notebook forward / backward while it is open.")]
    public GamepadControl pageNext = GamepadControl.RightShoulder;
    public GamepadControl pagePrev = GamepadControl.LeftShoulder;

    [Header("Fishing")]
    [Tooltip("Hold to charge a cast (force tracks the analog pull); commit with interact, let go " +
             "to cancel. Best bound to a trigger so charge pressure works.")]
    public GamepadControl throwCharge = GamepadControl.RightTrigger;
    [Tooltip("Reset a cast already in the water (bobber or lure).")]
    public GamepadControl resetCast = GamepadControl.RightShoulder;
    [Tooltip("Hold to aim (mirrors right mouse button).")]
    public GamepadControl aim = GamepadControl.LeftShoulder;
    [Tooltip("Tap to attract fish toward a waiting bobber.")]
    public GamepadControl attract = GamepadControl.RightTrigger;
    [Tooltip("Hold to crank the lure in and to reel during the fish fight.")]
    public GamepadControl reel = GamepadControl.RightTrigger;
}

/// <summary>
/// Per-device keybinding tables, edited in the Inspector and read at runtime by GamepadInput
/// (gamepad) and — in a later pass — the keyboard/mouse call sites.
///
/// Create one via Assets ▸ Create ▸ FishOWisp ▸ Input Bindings and drop it in a folder named
/// "Resources" (any one in the project) as "InputBindings.asset" so GamepadInput auto-loads it.
/// If none is found the game falls back to the built-in defaults, which match the shipped layout.
/// </summary>
[CreateAssetMenu(fileName = "InputBindings", menuName = "FishOWisp/Input Bindings")]
public class InputBindings : ScriptableObject
{
    [Tooltip("Keyboard & mouse bindings. NOTE: not yet wired to the game's keyboard reads — this " +
             "table is here so the screen is complete; the keyboard pass will route reads through it.")]
    public KeyboardMouseBindings keyboardMouse = new KeyboardMouseBindings();

    [Tooltip("Layout used while an Xbox-style pad is connected.")]
    public GamepadBindings xbox = new GamepadBindings();

    [Tooltip("Layout used while a PlayStation (DualShock/DualSense) pad is connected. Defaults " +
             "match Xbox; change these to give PlayStation players a different layout.")]
    public GamepadBindings playStation = new GamepadBindings();

    /// <summary>The gamepad table for the brand currently connected (PlayStation vs Xbox/other).</summary>
    public GamepadBindings ForKind(GamepadKind kind)
        => kind == GamepadKind.PlayStation ? playStation : xbox;
}

/// <summary>
/// Keyboard & mouse table. Present so the bindings screen shows all three devices; the scattered
/// keyboard reads across the gameplay scripts are migrated onto this in a follow-up pass, so
/// changing these does nothing yet.
/// </summary>
[Serializable]
public class KeyboardMouseBindings
{
    [Header("Not yet wired — coming in the keyboard pass")]
    public KeyCode interact = KeyCode.E;
    public KeyCode jump = KeyCode.Space;
    public KeyCode sprint = KeyCode.LeftShift;
    public KeyCode cancel = KeyCode.Escape;
    public KeyCode lineReset = KeyCode.E;
    public KeyCode notebookToggle = KeyCode.Tab;
    public KeyCode pageNext = KeyCode.RightArrow;
    public KeyCode pagePrev = KeyCode.LeftArrow;
}
