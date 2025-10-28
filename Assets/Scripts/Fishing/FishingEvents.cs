using System;
using UnityEngine;

public static class FishingEvents
{
    // Casting Events
    public static Action OnStartCharging;
    public static Action OnCancelCharging;
    public static Action<Vector3, float> OnThrowBobber;

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

    public static Action<FishPreset> OnFishFightBegin;
    public static Action<float, float> OnFishFightProgressUpdate;
    public static Action<bool> OnFishFightEnd;

    // Fired when the player actively reels during a fish fight's calm phase.
    public static Action OnStartReelingDuringFight;
    public static Action OnStopReelingDuringFight;
}