using UnityEngine;

/// <summary>
/// Central keyboard/mouse reads, the mirror of <see cref="GamepadInput"/>. Every query resolves
/// through the <see cref="KeyboardMouseBindings"/> table on the shared InputBindings asset
/// (<see cref="GamepadInput.Bindings"/>), so remapping the asset changes the game — and
/// ButtonPrompts keyboard labels track the same table, so on-screen hints stay correct.
///
/// Call sites OR these with their GamepadInput counterparts, exactly like the old hardcoded
/// KeyCode reads they replace: both devices stay live at the same time.
///
/// Reads use the legacy Input class because KeyCode is the only Unity key identifier that spans
/// keyboard AND mouse buttons in one enum (Mouse0..Mouse6) — which is what lets the fishing verbs
/// default to the mouse in the same table. The project runs with Active Input Handling = Both.
/// </summary>
public static class KeyInput
{
    private static KeyboardMouseBindings T => GamepadInput.Bindings.keyboardMouse;

    // --- Movement ---
    public static bool SprintHeld => Input.GetKey(T.sprint);
    public static bool JumpPressed => Input.GetKeyDown(T.jump);
    public static bool JumpHeld => Input.GetKey(T.jump);

    // --- World / menus ---
    public static bool InteractPressed => Input.GetKeyDown(T.interact);
    public static bool CancelPressed => Input.GetKeyDown(T.cancel);
    public static bool NotebookTogglePressed => Input.GetKeyDown(T.notebookToggle);
    public static bool InventoryTogglePressed => Input.GetKeyDown(T.inventoryToggle);
    public static bool InventoryToggleReleased => Input.GetKeyUp(T.inventoryToggle);
    public static bool PageNextPressed => Input.GetKeyDown(T.pageNext);
    public static bool PagePrevPressed => Input.GetKeyDown(T.pagePrev);
    // The small in-page flip through the encyclopedia fish list, distinct from the big
    // pageNext/pagePrev physical page turn above.
    public static bool FishListNextPressed => Input.GetKeyDown(T.fishListNext);
    public static bool FishListPrevPressed => Input.GetKeyDown(T.fishListPrev);
    public static bool ConfirmPressed => Input.GetKeyDown(T.confirm);

    // --- Fishing ---
    // Hold to aim the cast; releasing without a whip abandons it (see FishingRodController).
    public static bool CastAimPressed => Input.GetKeyDown(T.castAim);
    public static bool CastAimReleased => Input.GetKeyUp(T.castAim);
    public static bool AimPressed => Input.GetKeyDown(T.aim);
    public static bool AimReleased => Input.GetKeyUp(T.aim);
    public static bool AttractPressed => Input.GetKeyDown(T.attract);
    // Press edge hooks a biting fish; the hold cranks the lure / reels the fight — same key, so
    // hooking and reeling share one button like the gamepad's reel trigger.
    public static bool HookPressed => Input.GetKeyDown(T.reel);
    public static bool ReelHeld => Input.GetKey(T.reel);
    public static bool ResetCastPressed => Input.GetKeyDown(T.lineReset);
}
