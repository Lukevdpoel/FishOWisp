using UnityEngine;
using System.Collections.Generic;

// Part of FishingZone (partial class). Serialized fields live in FishingZone.cs.
public partial class FishingZone
{
    private void HandleAttract()
    {
        if (currentBobber == null) return;
        // While a fish is being caught (post-bite), ignore further attract calls so we never
        // promote a second lead mid-fight.
        if (isCatchingFish) return;

        // Track spam
        float now = Time.time;
        attractTimestamps.RemoveAll(t => now - t > scareWindow);
        attractTimestamps.Add(now);

        // If a fish is already attracted or nibbling, keep interacting with it
        if (currentlyAttractedFish != null)
        {
            // Pressing attract during nibbling scares the fish away
            if (currentlyAttractedFish.CurrentState == FishRipple.FishState.Nibbling)
            {
                currentlyAttractedFish.Scare();
                currentlyAttractedFish = null;
                ScatterFollowers();
                attractTimestamps.Clear();
                SetOtherFishAvoidance(false);
            }
            // Spam check — scare the attracted fish
            else if (attractTimestamps.Count > maxAttractsBeforeScare)
            {
                currentlyAttractedFish.Scare();
                currentlyAttractedFish = null;
                ScatterFollowers();
                attractTimestamps.Clear();
                SetOtherFishAvoidance(false);
            }
            else
            {
                // Re-call attract — this handles the "too close = scare" check inside FishRipple
                currentlyAttractedFish.AttractToBobber();

                // If it got scared from being too close, clear it
                if (currentlyAttractedFish.CurrentState == FishRipple.FishState.Scared)
                {
                    currentlyAttractedFish = null;
                    ScatterFollowers();
                    attractTimestamps.Clear();
                    SetOtherFishAvoidance(false);
                }
            }
        }
        else
        {
            // No lead yet. Pool the candidates: any fish that's already curious (Attracted)
            // OR still wandering within the attract-call radius. Sort by distance and pick the
            // closest as the new lead, the rest become followers.
            Vector3 bobberPos = currentBobber.transform.position;
            List<FishRipple> candidates = new List<FishRipple>();
            for (int i = 0; i < activeFish.Count; i++)
            {
                FishRipple fish = activeFish[i];
                if (fish == null) continue;
                if (fish.CurrentState == FishRipple.FishState.Scared) continue;
                if (fish.CurrentState == FishRipple.FishState.Nibbling) continue;
                if (!MatchesEquippedTackleAndBait(fish)) continue;
                if (fish.CurrentState == FishRipple.FishState.Wandering)
                {
                    if (attractCallRadius > 0f
                        && HorizontalDistance(fish.transform.position, bobberPos) > attractCallRadius) continue;
                }
                candidates.Add(fish);
            }

            candidates.Sort((a, b) =>
                HorizontalDistance(a.transform.position, bobberPos)
                .CompareTo(HorizontalDistance(b.transform.position, bobberPos)));

            FishRipple lead = candidates.Count > 0 ? candidates[0] : null;
            if (lead != null)
            {
                lead.SetFollower(false);
                // Too-close scare only applies to a fish ALREADY holding interest — calling in a
                // wandering fish that happens to sit near the bobber must not spook it (it just
                // declines to attract this frame inside AttractToBobber).
                lead.AttractToBobber(lead.CurrentState == FishRipple.FishState.Attracted);
                if (lead.CurrentState == FishRipple.FishState.Attracted)
                {
                    currentlyAttractedFish = lead;
                    autoNibbleTimer = -1f; // pause the passive timer; we now have a lead
                    attractTimestamps.Clear();
                    attractTimestamps.Add(now);

                    followerFish.Clear();
                    int taken = 0;
                    for (int i = 1; i < candidates.Count && taken < maxFollowers; i++)
                    {
                        FishRipple follower = candidates[i];
                        follower.SetFollower(true);
                        // Same rule as the lead: no spooking a fish that wasn't interested yet.
                        follower.AttractToBobber(follower.CurrentState == FishRipple.FishState.Attracted);
                        if (follower.CurrentState == FishRipple.FishState.Attracted)
                        {
                            followerFish.Add(follower);
                            taken++;
                        }
                        else
                        {
                            // Didn't take (too close / declined) — clear the follower flag.
                            follower.SetFollower(false);
                        }
                    }

                    Debug.Log($"[Attract] Lead + {taken} follower(s) from {candidates.Count} candidates (E pressed).");

                    SetOtherFishAvoidance(true);
                }
            }
        }

        // Bobber jiggle feedback
        if (currentBobber != null)
        {
            currentBobber.PlayAttractJiggle();
        }
    }

    private void HandleReelIn()
    {
        if (currentBobber == null) return;

        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] == null) continue;
            // A fish that just let go of a missed bite is already coasting away on its release
            // follow-through — leave it be so it swims off calmly like the lure's spat fish,
            // rather than re-scaring it into a frantic flee (or an in-place jitter) right where
            // the bobber was. This is the bobber-miss path's own escaping fish.
            if (activeFish[i].IsRecoveringFromGrab) continue;
            // Only fish already INTERESTED in the tackle spook when it's yanked away — a reset
            // near an oblivious wanderer no longer scares it (the old proximity clause is gone).
            if (activeFish[i].CurrentState == FishRipple.FishState.Nibbling
                || activeFish[i].CurrentState == FishRipple.FishState.Attracted
                || activeFish[i].CurrentState == FishRipple.FishState.Striking
                || activeFish[i].CurrentState == FishRipple.FishState.Grabbing)
            {
                activeFish[i].Scare();
            }
        }

        // Reeling in is the authoritative "fishing ended" signal. Tear the bobber link down here
        // (currentBobber = null, bobberTransform/avoidance cleared on every fish) rather than
        // leaning on OnTriggerExit — during a fight the bobber already left the trigger and won't
        // fire another exit, so without this the pond's lifetime/predator freeze would never lift.
        ClearBobber();
    }

}
