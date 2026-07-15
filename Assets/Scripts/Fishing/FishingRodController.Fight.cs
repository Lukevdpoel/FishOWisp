using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

// Part of FishingRodController (partial class). Serialized fields live in FishingRodController.cs.
public partial class FishingRodController
{
    private void HookFishAndStartFight()
    {
        if (fishEscapeCoroutine != null) StopCoroutine(fishEscapeCoroutine);
        FishingEvents.OnHookFishSuccess?.Invoke();
        currentState = FishingState.FightingFish;
        if (activeBobber != null)
        {
            // The fish fight drives the bobber rigidbody directly (FishFightHandler), so it
            // must be dynamic. The old self-swimming struggle (SetStruggleActive) stays off
            // for the whole fight — the player steers the fish now.
            Rigidbody rb = activeBobber.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            activeBobber.SetPlayerTransform(transform);
        }
        FishingEvents.OnFishFightBegin?.Invoke(activeBobber.HookedFish.preset);
        fishFight.Begin(activeBobber, activeBobber.HookedFish.preset, transform.position,
                        lineSlackBeforeBreak, in fightSettings);

        Vector3 toBank = activeBobber.transform.position - ComputeReelTarget(activeBobber);
        toBank.y = 0f;
        fightStartDistance = toBank.magnitude;
    }
    private void WinFishFight()
    {
        Debug.Log($"[FishFight] WinFishFight — activeBobber: {(activeBobber != null ? "valid" : "NULL")}, inspectionHandler: {(inspectionHandler != null ? "valid" : "NULL")}");
        currentState = FishingState.Reeling;
        if (playerController != null) playerController.UnlockFromFish();
        fishFight.End(activeBobber);
        caughtFishInstance = activeBobber.HookedFish;
        FishingEvents.OnFishFightEnd?.Invoke(true);
        if (activeBobber != null) activeBobber.SwapBobberForFishModel();

        StartCoroutine(ReelInBobberRoutine(caughtFishInstance));
    }
    private void CancelFishingAction()
    {
        if (currentState != FishingState.Cooldown)
        {
            StopAiming();
            // Bobber lifecycle is owned by FishingLine — it parks the persistent instance on OnCancelFishing.
            ResetFishingState();
        }
    }

    private IEnumerator ReelInBobberRoutine(CaughtFish fishToInventory)
    {
        FishingEvents.OnStartReeling?.Invoke();

        // activeBobber / bobberInWater are only set once a fish is on the line or the bobber has
        // landed. When the player resets mid-flight (cast but not yet splashed down) both are null,
        // so fall back to FishingLine's persistent bobber — otherwise there's nothing to arc and the
        // bobber just teleports back on the park.
        BobberController bobberToReelController =
            activeBobber != null ? activeBobber
            : bobberInWater != null ? bobberInWater
            : (fishingLine != null ? fishingLine.ActiveBobber : null);

        // A caught fish is hoisted out of the water to HANG from the line below the rod tip
        // (TP-style showcase) — the silhouette fish stays attached the whole way, so nothing is
        // re-instantiated. The line's park is held off (BeginCatchHold) until the inspection
        // confirms, otherwise OnReelingCompleted below would destroy the hanging fish.
        bool hangCatch = fishToInventory != null && inspectionHandler != null && bobberToReelController != null;
        if (hangCatch)
        {
            inspectionHandler.ConfigureHang(bobberToReelController,
                                            fishingLine != null ? fishingLine.rodTip : null);
            if (fishingLine != null) fishingLine.BeginCatchHold();
        }

        if (bobberToReelController != null)
        {
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null) bobberRb.isKinematic = true;

            // Don't destroy after the arc — FishingLine parks the persistent bobber when
            // OnReelingCompleted fires (or, for a hang catch, when the catch-hold ends).
            if (hangCatch)
            {
                yield return FishingReelInArc.Animate(bobberToReelController.transform,
                                                      inspectionHandler.GetHangPosition, null,
                                                      reelInDuration, reelInArcHeight);
            }
            else if (fishingLine != null && fishingLine.rodTip != null)
            {
                // Empty-line reel: arc the bobber straight to its dangle rest POSE (the point it
                // hangs at, in its upright rotation) rather than to the rod tip. FishingLine then
                // parks it onto the exact same pose, so the hand-off is a no-op — it smooths into
                // the dangle position instead of teleporting down from the rod tip.
                yield return FishingReelInArc.Animate(bobberToReelController.transform,
                                                      fishingLine.GetDangleRestPosition,
                                                      fishingLine.GetDangleRestRotation,
                                                      reelInDuration, reelInArcHeight);
            }
            else
            {
                // No line reference and no fish — fall back to the player/rod transform.
                Transform fallback = playerModel != null ? playerModel : transform;
                yield return FishingReelInArc.Animate(bobberToReelController.transform, fallback,
                                                      reelInDuration, reelInArcHeight);
            }
        }

        HandleReelingCompleted(fishToInventory);
    }

    private void HandleReelingCompleted(CaughtFish fishToInventory)
    {
        Debug.Log($"[FishFight] HandleReelingCompleted — fish: {(fishToInventory != null ? fishToInventory.GetDisplayName() : "NULL")}, inspectionHandler: {(inspectionHandler != null ? "valid" : "NULL")}");

        // FishingLine.ReelInBobberRoutine also fires this, but it can bail out early
        // (yield break when the bobber is destroyed mid-animation by ReelInBobberRoutine here),
        // leaving subscribers like BaitBarUI stuck thinking fishing is still active.
        // Fire it from the one path every reel funnels through so the bait UI never gets
        // locked out after a catch or after reeling back an empty line.
        FishingEvents.OnReelingCompleted?.Invoke();

        if (fishToInventory != null && inspectionHandler != null)
        {
            currentState = FishingState.InspectingCatch;
            // danglingBobber stays hidden — the fish is hanging from the real line right now;
            // ResetFishingState restores it once the inspection is confirmed.
            caughtFishInstance = fishToInventory;
            inspectionHandler.BeginInspection(fishToInventory, playerModel);
        }
        else
        {
            ResetFishingState();
        }
    }

    private void TryFinishInspection()
    {
        if (inspectionHandler == null) return;

        CaughtFish fish;
        if (inspectionHandler.TryFinishInspection(out fish))
        {
            caughtFishInstance = null;
            ResetFishingState();
        }
    }

}
