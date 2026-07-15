using UnityEngine;
using System.Collections.Generic;

// Part of FishingZone (partial class). Serialized fields live in FishingZone.cs.
public partial class FishingZone
{
    // The final nibble is about to become the bite (fires ~biteDelay before HookFish). The
    // nibbling fish still exists here — record the heading of its bite dash on the bobber, so
    // the hooked silhouette that replaces it spawns mid-follow-through instead of snapping to
    // a fresh direction. (By OnFishBite the visual is already spawned — this must run first.)
    private void HandleBiteImminent(BobberController bobber)
    {
        if (bobber != currentBobber || currentlyAttractedFish == null) return;
        Vector3 fwd = currentlyAttractedFish.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.001f)
            bobber.SetStrikeHeading(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
    }

    private void HandleFishBite(BobberController bobber)
    {
        if (bobber != currentBobber) return;

        // Remove the fish that was nibbling (it got caught) and spawn its replacement —
        // a caught fish counts as a despawn for the population cycle.
        if (currentlyAttractedFish != null)
        {
            RemoveFish(currentlyAttractedFish);
            SpawnOneFish();
            currentlyAttractedFish = null;
            ScatterFollowers();
            // Force every remaining fish to give the bobber space for the duration of the catch —
            // this stops them from auto-attracting via their actionRadius and triggering a second bite.
            SetOtherFishAvoidance(true);
            autoNibbleTimer = -1f;
            isCatchingFish = true;
        }
    }

    public void RemoveFish(FishRipple fish)
    {
        activeFish.Remove(fish);
        if (fish != null)
            Destroy(fish.gameObject);
    }

    // A dashing fish reached the lure and clamped on (TP grab). Open the reaction window on the
    // rod and freeze further bite rolls/promotions while the lure is held — but DON'T hook yet.
    // The visible fish stays on the line; the bite is only committed if the player reacts in time.
    private void HandleLureGrabStart(FishRipple fish)
    {
        if (fish == null || currentBobber == null || isCatchingFish) return;

        grabbingFish = fish;
        isCatchingFish = true;
        ScatterFollowers();
        SetOtherFishAvoidance(true);
        autoNibbleTimer = -1f;

        // The float is how long the fish will hold on — the player's window to set the hook.
        FishingEvents.OnLureGrabbed?.Invoke(currentBobber, fish.GrabHoldRemaining);
    }

    // The grip lapsed with no response: the fish spat the lure and is coasting off (it never
    // disappears). Re-open the lure to bites and close the rod's reaction window.
    private void HandleLureGrabReleased(FishRipple fish)
    {
        if (fish != grabbingFish) return; // stale / already resolved

        grabbingFish = null;
        isCatchingFish = false;
        autoNibbleTimer = -1f;

        // Startle the school: no other chaser may roll a new strike for a beat, so a missed bite
        // isn't instantly punished by a second fish pouncing the moment this one spits the lure.
        lureBrain.OnBiteMissed(in lureBrainSettings);

        // Let the whole school resume normal behaviour — including the spitter, which is held off
        // the lure by its own re-engage cooldown (FishRipple) rather than the avoid flag, so it
        // coasts through its follow-through instead of veering away.
        SetOtherFishAvoidance(false);

        FishingEvents.OnLureGrabReleased?.Invoke(currentBobber);
    }

    // The player reacted while the fish gripped the lure — commit the bite. TP-style: the fish
    // you watched dash and grab is the fish on the line. ConfirmGrab() guards the race where the
    // grip lapses the same frame the player presses.
    private void HandleHookLureGrab()
    {
        if (grabbingFish == null || currentBobber == null) return;
        if (!grabbingFish.ConfirmGrab()) return;

        FishPreset preset = grabbingFish.preset;

        // Record the direction the grabber was carrying the lure in, so the hooked silhouette
        // that replaces it spawns mid-follow-through (same hand-off as the bobber path's
        // bite-imminent capture).
        Vector3 grabFwd = grabbingFish.transform.forward;
        grabFwd.y = 0f;
        if (grabFwd.sqrMagnitude > 0.001f)
            currentBobber.SetStrikeHeading(Mathf.Atan2(grabFwd.x, grabFwd.z) * Mathf.Rad2Deg);

        RemoveFish(grabbingFish);
        grabbingFish = null;
        // A bobber bite makes the nibbling lead the grabber, so clear that reference too — it's
        // about to be the hooked fish. (Already null on the lure path.)
        currentlyAttractedFish = null;
        // The caught fish's replacement — same population cycle as the lifetime despawn.
        SpawnOneFish();
        lureBrain.ResetState(activeFish);
        ScatterFollowers();
        SetOtherFishAvoidance(true);
        autoNibbleTimer = -1f;
        isCatchingFish = true;

        currentBobber.HookFish(preset);
    }

    private void ResetAutoNibbleTimer()
    {
        if (currentBobber == null)
        {
            autoNibbleTimer = -1f;
            return;
        }
        float delay = Random.Range(autoNibbleMin, autoNibbleMax);
        if (NoBaitEquipped()) delay *= Mathf.Max(1f, noBaitInterestMultiplier);
        autoNibbleTimer = delay;
    }

    // No bait on a regular bobber = an empty hook. Only FishPreset.bitesWithoutBait species engage it
    // (gated in MatchesEquippedTackleAndBait), and the multiplier above makes them slow about it.
    private static bool NoBaitEquipped()
        => !BobberInventory.IsLureEquipped
           && BaitInventory.Instance != null
           && BaitInventory.Instance.SelectedBait == null;

    private void UpdateAutoNibbleTimer()
    {
        // No bobber, nothing to count toward.
        if (currentBobber == null)
        {
            autoNibbleTimer = -1f;
            return;
        }
        // A fish has already bitten — don't promote another one until the catch resolves.
        if (isCatchingFish)
        {
            autoNibbleTimer = -1f;
            return;
        }
        // Lures use their own reel-based attraction mechanic — the passive nibble timer
        // would otherwise race the lure's hidden attraction meter and produce a "free" bite.
        if (BobberInventory.IsLureEquipped)
        {
            autoNibbleTimer = -1f;
            return;
        }
        // Already have a lead — pause the timer until the lead clears.
        if (currentlyAttractedFish != null)
        {
            return;
        }
        // First tick after a clear/land: roll a fresh random delay.
        if (autoNibbleTimer < 0f)
        {
            ResetAutoNibbleTimer();
            return;
        }

        autoNibbleTimer -= Time.deltaTime;
        if (autoNibbleTimer > 0f) return;

        // Time's up — try to promote a fish that already noticed the bobber.
        if (TryAutoPromoteLead())
        {
            autoNibbleTimer = -1f; // lead is set; timer pauses until lead clears
        }
        else
        {
            // Nobody nearby yet — peek again shortly.
            autoNibbleTimer = autoNibbleRetryDelay;
        }
    }

    private bool TryAutoPromoteLead()
    {
        if (currentBobber == null) return false;

        Vector3 bobberPos = currentBobber.transform.position;
        FishRipple chosen = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < activeFish.Count; i++)
        {
            FishRipple fish = activeFish[i];
            if (fish == null) continue;
            if (fish == currentlyAttractedFish) continue;
            if (fish.CurrentState != FishRipple.FishState.Attracted) continue;
            if (!MatchesEquippedTackleAndBait(fish)) continue;

            float d = HorizontalDistance(fish.transform.position, bobberPos);
            if (d < bestDist)
            {
                bestDist = d;
                chosen = fish;
            }
        }

        if (chosen == null) return false;

        // Promote: clear follower flag so UpdateAttracted will let it transition into Nibbling
        // once it gets within nibbleRange of the bobber.
        chosen.SetFollower(false);
        currentlyAttractedFish = chosen;
        attractTimestamps.Clear();

        // Anything else that was already in the school becomes a follower (capped to maxFollowers).
        followerFish.Clear();
        for (int i = 0; i < activeFish.Count && followerFish.Count < maxFollowers; i++)
        {
            FishRipple fish = activeFish[i];
            if (fish == null || fish == chosen) continue;
            if (fish.CurrentState != FishRipple.FishState.Attracted) continue;
            fish.SetFollower(true);
            followerFish.Add(fish);
        }

        SetOtherFishAvoidance(true);
        Debug.Log($"[AutoNibble] Promoted fish at dist {bestDist:F2} to lead. Followers: {followerFish.Count}.");
        return true;
    }
}
