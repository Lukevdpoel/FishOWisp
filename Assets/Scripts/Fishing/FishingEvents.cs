using System;
using UnityEngine;

public static class FishingEvents
{
    // Casting Events
    public static Action OnStartCharging;
    public static Action OnCancelCharging;
    public static Action<Vector3, float> OnThrowBobber;
    // Fired when a charge is abandoned without throwing (let go of RT mid-charge). Distinct from
    // OnCancelFishing so the cast animation can snap straight back to idle without disturbing the
    // fish-escape/fail path, which plays its own animation.
    public static Action OnChargeCanceled;

    // Reeling & Catching Events
    public static Action OnStartReeling;
    public static Action OnReelingCompleted;
    public static Action OnCancelFishing;

    // Bobber & Fish State Events
    public static Action<BobberController> OnBobberLandedInWater;
    public static Action<BobberController> OnFishNibble;
    public static Action<BobberController> OnFishBite;
    public static Action<CaughtFish> OnFishHooked;

    public static Action OnHookFishSuccess;

    // UI Events
    public static Action<bool> OnToggleChargeUI;
    public static Action<float, float> OnUpdateChargeUI;

    // Charge progress as a normalized 0..1 value, used to drive the camera zoom/pitch.
    public static Action<float> OnChargeProgressNormalized;

    public static Action<FishPreset> OnFishFightBegin;
    public static Action<float, float> OnFishFightProgressUpdate;
    public static Action<bool> OnFishFightEnd;

    // Rod direction during fish fight (-1 = left, 0 = center, 1 = right)
    public static Action<float> OnRodDirectionUpdate;

    // Fired when the player actively reels during a fish fight's calm phase.
    public static Action OnStartReelingDuringFight;
    public static Action OnStopReelingDuringFight;

    // Aim Mode Events
    public static Action OnStartAiming;
    public static Action OnStopAiming;

    // Fish Attraction Events
    public static Action OnAttractFish;
    public static Action OnFishScared;
    public static Action<BobberController> OnBiteImminent;

    // Fired on each lure yank (A/D press) so the line can visibly snap taut with the tug.
    public static Action OnLureTugged;

    // TP-style lure grab. A visible fish has charged in and clamped onto the lure: the player
    // now has a brief window (the float, in seconds) to set the hook before the fish lets go.
    // This is the lure path's reaction window — it happens on the real fish, BEFORE any HookFish,
    // so a miss simply releases the fish instead of spawning/fading a hooked-fish stand-in.
    public static Action<BobberController, float> OnLureGrabbed;
    // The grab lapsed with no response: the fish released the lure and is swimming off. Closes
    // the window opened by OnLureGrabbed (the bobber is the one the fish let go of).
    public static Action<BobberController> OnLureGrabReleased;
    // The player reacted in time during a lure grab — commit the bite. The zone turns the
    // gripping fish into the hooked fish (BobberController.HookFish) and the fight begins.
    public static Action OnHookLureGrab;

    // Fired when the player starts (true) / stops (false) cranking the reel on a lure in the
    // water. Drives sustained line tension and the "loud retrieve" scare radius around the lure.
    public static Action<bool> OnLureReelChanged;

    // Fired when a hooked fish leaps out of the water during a fight (true = airborne,
    // false = splashed back down). The rope listens to slacken the taut fight line for the
    // arc and pull it straight again after the landing.
    public static Action<bool> OnHookedFishJumpChanged;
}