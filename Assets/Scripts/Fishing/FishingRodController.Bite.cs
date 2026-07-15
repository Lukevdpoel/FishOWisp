using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

// Part of FishingRodController (partial class). Serialized fields live in FishingRodController.cs.
public partial class FishingRodController
{
    private void HandleFishBite(BobberController bobber)
    {
        // Lure path bites without going through WaitingForBite, so accept either pre-bite state.
        if ((currentState != FishingState.WaitingForBite && currentState != FishingState.LureReeling)
            || bobber != bobberInWater) return;

        // TP holds the lure in the fish's mouth for a size-scaled window — bigger fish spit
        // faster. Lure path only; bobber bites keep the flat reactionTime.
        float hookWindow = reactionTime;
        if (currentState == FishingState.LureReeling && bobber.HookedFish != null)
            hookWindow *= LureBiteBrain.ReactionWindowMultiplier(bobber.HookedFish.preset.sizeClass);

        currentState = FishingState.FishOnTheLine; activeBobber = bobber;
        // Hand the player transform over at the bite (not just at fight start) so the
        // hooked fish can face away from the player through the whole struggle.
        bobber.SetPlayerTransform(transform);
        if (playerController != null) playerController.LockOnFish(bobber.transform);
        fishEscapeCoroutine = StartCoroutine(FishEscapeTimer(hookWindow));
    }

    // ----- TP lure grab: the reaction window happens on the real, visible fish -----

    // A fish has dashed in and clamped onto the lure (FishingZone.OnLureGrabbed). Open the hook
    // window — but DON'T start an escape timer or hook anything: the FishRipple owns the timeout
    // and will fire OnLureGrabReleased if the player doesn't react. The fish keeps swimming on
    // with the lure during the window.
    private void HandleLureGrabbed(BobberController bobber, float windowSeconds)
    {
        if (bobber != bobberInWater) return;
        isLureGrabActive = true;
        activeBobber = bobber;
        currentState = FishingState.FishOnTheLine;
    }

    // The fish spat the lure with no response: no catch, no penalty — just drop back to reeling
    // the lure (it's still out there). The fish swims off on its own side.
    private void HandleLureGrabReleased(BobberController bobber)
    {
        if (!isLureGrabActive || bobber != bobberInWater) return;
        isLureGrabActive = false;
        if (playerController != null) playerController.UnlockFromFish();

        // Missing the reaction window is a true FAIL on BOTH tackle types now: the fish swims off (it
        // already spat the tackle in FishRipple.ReleaseGrab) and the player has to recast, rather than
        // the lure quietly dropping back to a free retry on the same cast. The only difference is a
        // bobber also loses its bait to the fish; a lure carries none, so it just fails the cast.
        // FailToHookFish runs the same cast-reset path (fail animation → cooldown → idle) for both.
        if (!BobberInventory.IsLureEquipped)
        {
            bobber.ConsumeEquippedBait();
        }
        FailToHookFish();
    }

    // The player reacted in time — commit the grab into a real bite and start the fight. The zone
    // turns the gripping fish into the hooked fish (HookFish) synchronously inside OnHookLureGrab,
    // so HookedFish is valid by the time we read it.
    private void ConfirmLureHook()
    {
        isLureGrabActive = false;

        BobberController bobber = activeBobber != null ? activeBobber : bobberInWater;
        if (bobber == null) { HandleLureGrabReleased(bobberInWater); return; }

        bobber.SetPlayerTransform(transform);
        FishingEvents.OnHookLureGrab?.Invoke();

        if (bobber.HookedFish == null)
        {
            // Race: the grip lapsed the same frame — treat it as a miss, no catch.
            HandleLureGrabReleased(bobber);
            return;
        }

        activeBobber = bobber;
        if (playerController != null) playerController.LockOnFish(bobber.transform);
        HookFishAndStartFight();
    }

    private IEnumerator FishEscapeTimer(float window) { yield return new WaitForSeconds(window); FailToHookFish(); }
    private void FailToHookFish() { if (fishEscapeCoroutine != null) StopCoroutine(fishEscapeCoroutine); StartCoroutine(FailRoutine()); }

    private IEnumerator FailRoutine()
    {
        FishingEvents.OnStopReelingDuringFight?.Invoke();
        if (playerController != null) playerController.UnlockFromFish();
        if (bobberInWater != null)
        {
            // The one that got away swims off and fades (BeginEscape unparents it first).
            // Never destroy the bobber here — it is the persistent instance owned by
            // FishingLine, which parks it back on the rod via OnCancelFishing below;
            // destroying it left every later cast with no bobber to launch.
            bobberInWater.ReleaseHookedFishToEscape();
            bobberInWater = null;
        }
        activeBobber = null;
        FishingEvents.OnCancelFishing?.Invoke();
        currentState = FishingState.Cooldown;
        if (playerAnimator != null) playerAnimator.SetTrigger(hashFail);
        yield return new WaitForSeconds(failCooldown);
        currentState = FishingState.Idle; danglingBobber?.SetActive(true);
        if (playerController != null) playerController.LockControls(false);
    }
}
