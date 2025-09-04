using System;
using UnityEngine;

public static class FishingEvents
{
    // Casting Events
    public static Action OnStartCharging;
    public static Action OnCancelCharging;
    public static Action<Vector3, float> OnThrowBobber;

    // Reeling & Catching Events
    public static Action OnStartReeling; // This will now be used for the FINAL reel-in animation after winning the fight
    public static Action OnReelingCompleted;
    public static Action OnCancelFishing;

    // Bobber & Fish State Events
    public static Action<BobberController> OnBobberLandedInWater;
    public static Action<BobberController> OnFishNibble;
    public static Action<BobberController> OnFishBite; // This triggers the start of the fight
    public static Action<CaughtFish> OnFishHooked;

    // UI Events
    public static Action<bool> OnToggleChargeUI;
    public static Action<float, float> OnUpdateChargeUI;

    // --- NEW: Events for the Fishing Mini-Game ---
    public static Action<FishPreset> OnFishFightBegin; // Tells UI to show itself
    public static Action<float, float> OnFishFightProgressUpdate; // Updates the progress bar
    public static Action<bool> OnFishFightEnd; // Hides the UI, bool indicates if it was a success
}