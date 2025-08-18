using System;
using UnityEngine;

public static class FishingEvents
{
    public static Action OnStartCharging;
    public static Action OnCancelCharging;
    public static Action<Vector3, float> OnThrowBobber;
    public static Action OnStartReeling;
    public static Action OnReelingCompleted;
    public static Action OnCancelFishing;

    public static Action<BobberController> OnBobberLandedInWater;
    public static Action<BobberController> OnFishBite;
    public static Action<CaughtFish> OnFishHooked;

    public static Action<bool> OnToggleChargeUI;
    public static Action<float, float> OnUpdateChargeUI;
}