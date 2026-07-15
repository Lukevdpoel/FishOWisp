using UnityEngine;
using System.Collections.Generic;

// Part of FishingZone (partial class). Serialized fields live in FishingZone.cs.
public partial class FishingZone
{
    private void CleanupNullFish()
    {
        activeFish.RemoveAll(f => f == null);
    }

    private void ClearBobber()
    {
        lureBrain.ResetState(activeFish);
        currentBobber = null;
        currentlyAttractedFish = null;
        grabbingFish = null;
        isCatchingFish = false;
        autoNibbleTimer = -1f;
        ScatterFollowers();
        attractTimestamps.Clear();
        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] != null)
            {
                activeFish[i].SetAvoidBobber(false);
                activeFish[i].ClearBobberTransform();
            }
        }
    }

    // Bait-side equivalent of LureBiteBrain.EnforceHoverCap: caps how many fish may be self-attracted
    // (Attracted) or already working the bobber (Nibbling) at once, enforced every frame rather than
    // only at the moment a lead/followers get promoted. Nearest fish win a slot, tiered so a fish
    // already interested (or already biting) never loses its spot to a newcomer of the same
    // distance; everyone left out is told to keep clear (SetAvoidBobber), which also blocks
    // FishRipple.UpdateWandering's passive self-attract check from ever letting them in.
    private void EnforceBaitHoverCap()
    {
        if (maxBaitHoverFish <= 0) return; // 0 = leave hovering uncapped (old behavior)

        Vector3 bobberPos = currentBobber.transform.position;
        baitHoverSet.Clear();
        FillBaitHoverTier(bobberPos, FishRipple.FishState.Nibbling);
        FillBaitHoverTier(bobberPos, FishRipple.FishState.Attracted);
        FillBaitHoverTier(bobberPos, FishRipple.FishState.Wandering);

        for (int i = 0; i < activeFish.Count; i++)
        {
            FishRipple fish = activeFish[i];
            if (fish == null) continue;
            FishRipple.FishState st = fish.CurrentState;
            // Scared/despawning fish are mid-exit; Striking/Grabbing is the committed bite
            // dash-and-carry (only ever one fish at a time on the bait path) — none of these
            // should be told to avoid the bobber they're already leaving or biting.
            if (st == FishRipple.FishState.Scared || st == FishRipple.FishState.Despawning
                || st == FishRipple.FishState.Striking || st == FishRipple.FishState.Grabbing) continue;

            fish.SetAvoidBobber(!baitHoverSet.Contains(fish));
        }
    }

    // Fills the hover set with the nearest fish currently in tierState, up to maxBaitHoverFish.
    private void FillBaitHoverTier(Vector3 bobberPos, FishRipple.FishState tierState)
    {
        while (baitHoverSet.Count < maxBaitHoverFish)
        {
            FishRipple best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < activeFish.Count; i++)
            {
                FishRipple fish = activeFish[i];
                if (fish == null || baitHoverSet.Contains(fish)) continue;
                if (fish.CurrentState != tierState) continue;

                float d = HorizontalDistance(fish.transform.position, bobberPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = fish;
                }
            }
            if (best == null) return;
            baitHoverSet.Add(best);
        }
    }

    private void SetOtherFishAvoidance(bool avoid)
    {
        for (int i = 0; i < activeFish.Count; i++)
        {
            FishRipple fish = activeFish[i];
            if (fish == null) continue;
            if (fish == currentlyAttractedFish) continue;
            if (followerFish.Contains(fish)) continue; // followers don't avoid the bobber
            fish.SetAvoidBobber(avoid);
        }
    }

    private void ScatterFollowers()
    {
        // Tracked followers (player-promoted explicit list).
        for (int i = 0; i < followerFish.Count; i++)
        {
            if (followerFish[i] != null)
            {
                followerFish[i].StopFollowing();
            }
        }
        followerFish.Clear();

        // Plus any *self-attracted* fish in the zone. They aren't in the followerFish list
        // because they auto-attracted via their own actionRadius — but they should also scatter
        // when the lead is lost / reeled / caught.
        for (int i = 0; i < activeFish.Count; i++)
        {
            FishRipple fish = activeFish[i];
            if (fish == null) continue;
            if (fish == currentlyAttractedFish) continue;
            if (fish.CurrentState == FishRipple.FishState.Attracted)
            {
                fish.StopFollowing();
            }
        }
    }

    private void CleanupFollowers()
    {
        for (int i = followerFish.Count - 1; i >= 0; i--)
        {
            FishRipple f = followerFish[i];
            if (f == null || f.CurrentState != FishRipple.FishState.Attracted)
            {
                if (f != null) f.SetFollower(false);
                followerFish.RemoveAt(i);
            }
        }
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ----- Passive auto-nibble timer -----

}
