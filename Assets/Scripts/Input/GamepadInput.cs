using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Central read-only wrapper around the current gamepad (new Input System). Call sites OR
/// these queries with their existing keyboard/mouse reads, so an absent controller is
/// always a clean no-op and both devices stay usable at the same time.
///
/// Which physical control each verb maps to is driven by an <see cref="InputBindings"/> asset
/// (auto-loaded from a Resources folder as "InputBindings"), with a separate table per brand so
/// Xbox and PlayStation pads can be laid out independently. With no asset present the built-in
/// defaults below apply, so the game runs unchanged until you create one.
///
/// Default mapping (Xbox / PS5):
///   Left stick           - walk (deflection scales speed); steer the lure; steer the cast
///                          marker while aiming
///   Left stick click     - hold to run; release to drop back to a walk (hold-to-sprint,
///                          mirrors keyboard Shift)
///   Right stick          - orbit camera / the whip gesture while aiming (push out, snap back
///                          the opposite way to throw) / rotate the fish model while the
///                          notebook is open
///   A / Cross            - interact (NPCs, doors, vendor, pots, bounty board);
///                          confirm/advance dialogue; finish catch inspection
///   B / Circle           - cancel / close menus; advance/skip dialogue
///   X / Square           - (unused)
///   Y / Triangle         - jump
///   LT / L2              - hold to aim the cast marker; release without a whip to put the
///                          rod away (mirrors holding LMB on mouse/keyboard)
///   RT / R2              - tap to attract fish while a bobber waits; press to react to a
///                          biting fish; hold to crank the lure in and to reel during the
///                          fish fight
///   RB / R1              - reset a cast already in the water; flips a page forward
///                          while the notebook is open
///   LB / L1              - hold to aim (mirrors right mouse button); flips a page
///                          backward while the notebook is open
///   D-pad                - cycle encyclopedia fish (notebook open) / inventory slots
///                          (inventory open) / dialogue choices
///   Start / Options      - toggle the notebook
///   Back / Create        - toggle the inventory
/// </summary>
public enum ActiveInputDevice { KeyboardMouse, Gamepad }

public enum GamepadKind { None, Xbox, PlayStation, Nintendo }

public static class GamepadInput
{
    public const float StickDeadZone = 0.15f;
    public const float TriggerPressPoint = 0.25f;

    private static Gamepad Pad => Gamepad.current;

    // --- Active device ---
    // Flipped by ActiveInputDeviceTracker the moment either device produces real input.
    // Gameplay reads stay OR-merged (both devices always work); this exists for anything
    // that needs to KNOW which device the player is on — prompt icons, UI hints, etc.
    public static ActiveInputDevice ActiveDevice { get; private set; } = ActiveInputDevice.KeyboardMouse;
    public static bool IsGamepadActive => ActiveDevice == ActiveInputDevice.Gamepad;

    /// <summary>
    /// Brand of the pad the player last used. Defaults to Xbox so prompt art has a sane
    /// fallback before any controller input arrives; flips to PlayStation when a DualShock/
    /// DualSense is the active device, or to Nintendo for a Switch Pro Controller. Each brand
    /// has its own bindings table so layouts can differ — this also lets prompt icons show the
    /// right glyph (Cross / A button) for the active pad.
    /// </summary>
    public static GamepadKind ActiveGamepadKind { get; private set; } = GamepadKind.Xbox;

    /// <summary>Brand of the currently connected pad, regardless of what's being used.</summary>
    public static GamepadKind CurrentGamepadKind
    {
        get
        {
            Gamepad pad = Pad;
            if (pad == null) return GamepadKind.None;
            if (pad is UnityEngine.InputSystem.DualShock.DualShockGamepad)
                return GamepadKind.PlayStation;
            // Switch Pro Controllers (and most Switch-style third-party pads) register as
            // SwitchProControllerHID. NOTE: Unity maps their face buttons by physical POSITION,
            // so buttonEast is the 'A'/confirm button and buttonSouth is 'B' — mirrored vs. the
            // Nintendo labels. The dedicated nintendo table exists to account for that.
            if (pad is UnityEngine.InputSystem.Switch.SwitchProControllerHID)
                return GamepadKind.Nintendo;
            return GamepadKind.Xbox;
        }
    }

    /// <summary>
    /// True when the connected pad's shoulder triggers are digital (on/off) rather than
    /// pressure-sensitive — the Switch Pro Controller's ZL/ZR have no analog travel and read
    /// only 0 or 1. Casting no longer depends on this (every pad now time-ramps the charge and
    /// casts on release), but it's kept for anything that needs to know a pad can't report
    /// partial trigger pulls.
    /// </summary>
    public static bool HasDigitalTriggers => CurrentGamepadKind == GamepadKind.Nintendo;

    /// <summary>Fired once per switch, with the device the player just moved to. Also fires
    /// when the pad brand changes mid-session (e.g. Xbox pad swapped for a DualSense).</summary>
    public static event System.Action<ActiveInputDevice> OnActiveDeviceChanged;

    internal static void SetActiveDevice(ActiveInputDevice device)
    {
        GamepadKind kind = device == ActiveInputDevice.Gamepad ? CurrentGamepadKind : ActiveGamepadKind;
        if (ActiveDevice == device && ActiveGamepadKind == kind) return;
        ActiveDevice = device;
        ActiveGamepadKind = kind;
        OnActiveDeviceChanged?.Invoke(device);
    }

    // Reset statics before any Awake of a new play session — required with Reload Domain
    // disabled, otherwise the event keeps stale subscribers from the previous play.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveDevice = ActiveInputDevice.KeyboardMouse;
        ActiveGamepadKind = GamepadKind.Xbox;
        OnActiveDeviceChanged = null;
        // Drop the cached asset so a binding asset created/changed between play sessions (with
        // Reload Domain disabled) is picked up fresh next time it is read.
        bindings = null;
    }

    // --- Bindings ---
    private static InputBindings bindings;

    /// <summary>
    /// The active binding tables. Auto-loaded from Resources/InputBindings the first time it's
    /// read; if no asset exists, an in-memory instance with the built-in defaults is used so the
    /// game keeps working. Assign explicitly (e.g. from a bootstrap) to override the lookup.
    /// </summary>
    public static InputBindings Bindings
    {
        get
        {
            if (bindings == null)
            {
                bindings = Resources.Load<InputBindings>("InputBindings");
                if (bindings == null) bindings = ScriptableObject.CreateInstance<InputBindings>();
            }
            return bindings;
        }
        set => bindings = value;
    }

    // The table for the brand of the connected pad, so PlayStation and Xbox layouts can differ.
    private static GamepadBindings Table => Bindings.ForKind(CurrentGamepadKind);

    /// <summary>
    /// The bound gamepad control for a prompt verb on the ACTIVE brand's table (so it tracks any
    /// remap). Returns <see cref="GamepadControl.None"/> for verbs that don't sit on a rebindable
    /// control (cycle = d-pad / stick). Used by <see cref="ButtonPrompts"/> to draw on-device hints;
    /// resolved off ActiveGamepadKind so the control and its rendered label stay brand-consistent.
    /// </summary>
    public static GamepadControl ControlFor(PromptVerb v)
    {
        GamepadBindings t = Bindings.ForKind(ActiveGamepadKind);
        switch (v)
        {
            case PromptVerb.Cast: return t.castAim;
            case PromptVerb.Reel: return t.reel;
            case PromptVerb.Attract: return t.attract;
            case PromptVerb.Aim: return t.aim;
            case PromptVerb.ResetCast: return t.resetCast;
            case PromptVerb.Hook: return t.reel;   // react-to-bite shares the reel button (RT)
            case PromptVerb.Confirm: return t.interact;   // finish catch inspection (A)
            case PromptVerb.Interact: return t.interact;
            case PromptVerb.Inventory: return t.inventoryToggle;
            case PromptVerb.Notebook: return t.notebookToggle;
            case PromptVerb.Jump: return t.jump;
            default: return GamepadControl.None; // Walk / CycleLeftRight live on the sticks & d-pad
        }
    }

    // Map a bound control to the live ButtonControl on the current pad. ButtonControl derives
    // from AxisControl, so ReadValue() yields the analog pull for triggers and 0/1 for buttons.
    private static ButtonControl Resolve(GamepadControl c)
    {
        Gamepad pad = Pad;
        if (pad == null) return null;
        switch (c)
        {
            case GamepadControl.ButtonSouth: return pad.buttonSouth;
            case GamepadControl.ButtonNorth: return pad.buttonNorth;
            case GamepadControl.ButtonEast: return pad.buttonEast;
            case GamepadControl.ButtonWest: return pad.buttonWest;
            case GamepadControl.LeftShoulder: return pad.leftShoulder;
            case GamepadControl.RightShoulder: return pad.rightShoulder;
            case GamepadControl.LeftTrigger: return pad.leftTrigger;
            case GamepadControl.RightTrigger: return pad.rightTrigger;
            case GamepadControl.LeftStickButton: return pad.leftStickButton;
            case GamepadControl.RightStickButton: return pad.rightStickButton;
            case GamepadControl.Start: return pad.startButton;
            case GamepadControl.Select: return pad.selectButton;
            case GamepadControl.DpadUp: return pad.dpad.up;
            case GamepadControl.DpadDown: return pad.dpad.down;
            case GamepadControl.DpadLeft: return pad.dpad.left;
            case GamepadControl.DpadRight: return pad.dpad.right;
            default: return null;
        }
    }

    private static bool Pressed(GamepadControl c) { ButtonControl b = Resolve(c); return b != null && b.wasPressedThisFrame; }
    private static bool HeldButton(GamepadControl c) { ButtonControl b = Resolve(c); return b != null && b.isPressed; }
    private static bool ReleasedButton(GamepadControl c) { ButtonControl b = Resolve(c); return b != null && b.wasReleasedThisFrame; }
    private static float Analog(GamepadControl c) { ButtonControl b = Resolve(c); return b != null ? b.ReadValue() : 0f; }

    // --- Movement / camera (sticks aren't rebindable) ---
    public static Vector2 Move => Pad == null ? Vector2.zero : ApplyRadialDeadZone(Pad.leftStick.ReadValue());
    public static Vector2 Look => Pad == null ? Vector2.zero : ApplyRadialDeadZone(Pad.rightStick.ReadValue());
    // Sprint is hold-to-run: keep the bound control held to turn walking into running,
    // release it to drop back to a walk (mirrors keyboard Shift).
    public static bool SprintHeld => HeldButton(Table.sprintToggle);

    // --- Jump ---
    public static bool JumpPressed => Pressed(Table.jump);
    public static bool JumpHeld => HeldButton(Table.jump);

    // --- UI / world verbs ---
    // Interact and confirm share one control: touching the world, casting a charged throw, and
    // confirming/advancing menus and dialogue are all the same button. (Reacting to a biting fish
    // moved onto the reel control — see ReelPressed — so hooking and reeling share one button.)
    public static bool InteractPressed => Pressed(Table.interact);
    public static bool ConfirmPressed => Pressed(Table.interact);
    public static bool CancelPressed => Pressed(Table.cancel);

    // --- Fishing ---
    // Casting lives on the castAim control (default LT): holding it past the press point drops
    // the aim marker (LEFT stick steers it), and the throw is the whip gesture on the RIGHT
    // stick — push it out and snap it back the opposite way while this is held. Releasing
    // without a whip abandons the aim. Mirrors holding LMB on mouse/keyboard.
    public static bool CastAimHeld => Analog(Table.castAim) > TriggerPressPoint;
    public static bool ResetCastPressed => Pressed(Table.resetCast);
    public static bool AimPressed => Pressed(Table.aim);
    public static bool AimReleased => ReleasedButton(Table.aim);
    // Tap to attract fish while a bobber waits for a bite.
    public static bool AttractPressed => Pressed(Table.attract);
    // Held cranks the lure in (lure flow) and reels during the fish fight.
    public static bool LureReelHeld => Analog(Table.reel) > TriggerPressPoint;
    public static bool ReelHeld => Analog(Table.reel) > TriggerPressPoint;
    // Press edge of the reel control — used to react to a biting fish, so hooking the bite shares
    // the SAME button as reeling (default RT). Mirrors keyboard/mouse, where LMB's down-edge hooks
    // the bite and holding LMB reels.
    public static bool ReelPressed => Pressed(Table.reel);

    // --- Notebook / Inventory ---
    public static bool NotebookTogglePressed => Pressed(Table.notebookToggle);
    public static bool InventoryTogglePressed => Pressed(Table.inventoryToggle);
    // Hold/release variants for the loadout selector: holding the inventory button keeps the
    // gear menu open and the bobber-framing camera live; releasing closes it.
    public static bool InventoryToggleHeld => HeldButton(Table.inventoryToggle);
    public static bool InventoryToggleReleased => ReleasedButton(Table.inventoryToggle);
    public static bool PageNextPressed => Pressed(Table.pageNext);
    public static bool PagePrevPressed => Pressed(Table.pagePrev);
    // The small in-page flip through the encyclopedia fish list, distinct from the big
    // pageNext/pagePrev physical page turn above. Default RT/LT — free while the notebook
    // is open because all fishing input is gated behind NoteMenu.IsNotebookOpen.
    public static bool FishListNextPressed => Pressed(Table.fishListNext);
    public static bool FishListPrevPressed => Pressed(Table.fishListPrev);

    // --- D-pad (menu navigation, brand-agnostic, not rebindable) ---
    public static bool DpadUpPressed => Pad != null && Pad.dpad.up.wasPressedThisFrame;
    public static bool DpadDownPressed => Pad != null && Pad.dpad.down.wasPressedThisFrame;
    public static bool DpadLeftPressed => Pad != null && Pad.dpad.left.wasPressedThisFrame;
    public static bool DpadRightPressed => Pad != null && Pad.dpad.right.wasPressedThisFrame;
    public static bool DpadNextPressed => Pad != null && (Pad.dpad.right.wasPressedThisFrame || Pad.dpad.down.wasPressedThisFrame);
    public static bool DpadPrevPressed => Pad != null && (Pad.dpad.left.wasPressedThisFrame || Pad.dpad.up.wasPressedThisFrame);

    // Radial dead zone: reading the stick control directly bypasses the Input System's
    // binding-level processors, so the zone has to be applied here. Output is rescaled so
    // speed ramps from 0 at the zone edge instead of jumping.
    private static Vector2 ApplyRadialDeadZone(Vector2 v)
    {
        float mag = v.magnitude;
        if (mag < StickDeadZone) return Vector2.zero;
        float scaled = Mathf.Min(1f, (mag - StickDeadZone) / (1f - StickDeadZone));
        return v * (scaled / mag);
    }
}
