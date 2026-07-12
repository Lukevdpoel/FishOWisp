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
    [Tooltip("Interact with the world, commit a charged cast, and confirm/advance menus & " +
             "dialogue — all the same button. (Reacting to a biting fish lives on 'reel' so it " +
             "shares a button with reeling.)")]
    public GamepadControl interact = GamepadControl.ButtonSouth;
    [Tooltip("Cancel / close menus; also advances/skips dialogue.")]
    public GamepadControl cancel = GamepadControl.ButtonEast;
    public GamepadControl notebookToggle = GamepadControl.Start;
    public GamepadControl inventoryToggle = GamepadControl.Select;
    [Tooltip("Flip the notebook forward / backward while it is open.")]
    public GamepadControl pageNext = GamepadControl.RightShoulder;
    public GamepadControl pagePrev = GamepadControl.LeftShoulder;

    [Header("Fishing")]
    [Tooltip("Hold to aim the cast marker (left stick steers it); release to put the rod away. " +
             "The throw itself is the whip gesture on the right stick while this is held.")]
    public GamepadControl castAim = GamepadControl.LeftTrigger;
    [Tooltip("LEGACY (unused): the old hold-to-charge cast control. Casting now lives on castAim " +
             "+ the right-stick whip. Kept so existing binding assets keep deserializing cleanly.")]
    public GamepadControl throwCharge = GamepadControl.RightTrigger;
    [Tooltip("Reset a cast already in the water (bobber or lure).")]
    public GamepadControl resetCast = GamepadControl.RightShoulder;
    [Tooltip("Hold to aim (mirrors right mouse button).")]
    public GamepadControl aim = GamepadControl.LeftShoulder;
    [Tooltip("Tap to attract fish toward a waiting bobber.")]
    public GamepadControl attract = GamepadControl.RightTrigger;
    [Tooltip("Hold to crank the lure in and to reel during the fish fight; a fresh press also " +
             "reacts to a biting fish (hooking shares this button with reeling).")]
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
    [Tooltip("Keyboard & mouse bindings. All keyboard/mouse gameplay reads go through this table " +
             "(via KeyInput), so remapping here works the same as the gamepad tables below.")]
    public KeyboardMouseBindings keyboardMouse = new KeyboardMouseBindings();

    [Tooltip("Layout used while an Xbox-style pad is connected.")]
    public GamepadBindings xbox = new GamepadBindings();

    [Tooltip("Layout used while a PlayStation (DualShock/DualSense) pad is connected. Defaults " +
             "match Xbox; change these to give PlayStation players a different layout.")]
    public GamepadBindings playStation = new GamepadBindings();

    [Tooltip("Layout used while a Nintendo Switch Pro Controller is connected. Defaults match " +
             "Xbox. Heads-up: Unity maps Switch face buttons by physical POSITION, so on a Pro " +
             "Controller ButtonEast is the 'A' (confirm) button and ButtonSouth is 'B' — the " +
             "opposite of the Nintendo labels. For the Nintendo-standard layout set " +
             "interact = ButtonEast and cancel = ButtonSouth here.")]
    public GamepadBindings nintendo = new GamepadBindings();

    /// <summary>The gamepad table for the brand currently connected (PlayStation / Nintendo / Xbox-or-other).</summary>
    public GamepadBindings ForKind(GamepadKind kind)
    {
        switch (kind)
        {
            case GamepadKind.PlayStation: return playStation;
            case GamepadKind.Nintendo: return nintendo;
            default: return xbox;
        }
    }
}

/// <summary>
/// Keyboard & mouse action→key table, the mirror of <see cref="GamepadBindings"/>. All gameplay
/// keyboard/mouse reads go through <see cref="KeyInput"/>, which resolves against this table, so
/// remapping here actually changes the game (and ButtonPrompts labels follow automatically).
/// KeyCode covers mouse buttons too (Mouse0..Mouse6), which is how the fishing verbs sit on the
/// mouse by default. Movement (WASD/arrows via the Horizontal/Vertical axes), camera orbit, and
/// menu-list navigation are not rebindable — same policy as the sticks/d-pad on gamepad.
/// </summary>
[Serializable]
public class KeyboardMouseBindings
{
    [Header("Movement")]
    [Tooltip("Hold to run while moving; release to drop back to a walk.")]
    public KeyCode sprint = KeyCode.LeftShift;
    public KeyCode jump = KeyCode.Space;

    [Header("World / Menus")]
    [Tooltip("Interact with the world (NPCs, doors, vendor, pots, bounty board) and " +
             "confirm/advance dialogue.")]
    public KeyCode interact = KeyCode.E;
    [Tooltip("Cancel / close menus (shop, bounty board).")]
    public KeyCode cancel = KeyCode.Escape;
    public KeyCode notebookToggle = KeyCode.Tab;
    [Tooltip("Hold to keep the gear/loadout menu open; release closes it.")]
    public KeyCode inventoryToggle = KeyCode.B;
    [Tooltip("Flip the notebook forward / backward while it is open. (E and Q also flip, fixed.)")]
    public KeyCode pageNext = KeyCode.RightArrow;
    public KeyCode pagePrev = KeyCode.LeftArrow;

    [Header("Fishing")]
    [Tooltip("Hold to aim the cast (the whip gesture throws); release without a whip to put the " +
             "rod away. Default Left Mouse.")]
    public KeyCode castAim = KeyCode.Mouse0;
    [Tooltip("Hold to crank the lure in and to reel during the fish fight; a fresh press also " +
             "hooks a biting fish. Default Left Mouse.")]
    public KeyCode reel = KeyCode.Mouse0;
    [Tooltip("Tap to attract fish toward a waiting bobber. Default Left Mouse.")]
    public KeyCode attract = KeyCode.Mouse0;
    [Tooltip("Confirm / finish the catch inspection. Default Left Mouse.")]
    public KeyCode confirm = KeyCode.Mouse0;
    [Tooltip("Hold to aim (mirrors the gamepad aim shoulder).")]
    public KeyCode aim = KeyCode.Mouse1;
    [Tooltip("Reset a cast already in the water; cuts the line mid-fight.")]
    public KeyCode lineReset = KeyCode.E;
}
