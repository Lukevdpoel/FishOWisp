using UnityEngine;
using System.Collections.Generic;

// Part of FishingZone (partial class). Serialized fields live in FishingZone.cs.
public partial class FishingZone
{
    private void SpawnInitialFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0 || fishRipplePrefab == null) return;

        // Spawn the zone fully populated so the player sees a believable school of multiple fish.
        for (int i = 0; i < maxFishCount; i++)
        {
            SpawnOneFish();
        }
    }

    private void SpawnOneFish()
    {
        if (fishPool == null || fishPool.availableFish.Count == 0 || fishRipplePrefab == null) return;
        if (activeFish.Count >= maxFishCount) return;

        GameObject rippleObj = Instantiate(fishRipplePrefab, transform);
        FishRipple ripple = rippleObj.GetComponent<FishRipple>();

        if (ripple == null)
        {
            Debug.LogError("FishRipple prefab is missing the FishRipple component!");
            Destroy(rippleObj);
            return;
        }

        FishPreset chosen = PickEligibleFish();
        if (chosen == null)
        {
            // No fish in this pool match the active world weather (e.g. only Night-only fish
            // listed but it's currently Sunny). Skip this spawn; the respawn timer will try again.
            Destroy(rippleObj);
            return;
        }
        ripple.preset = chosen;
        ripple.Initialize(zoneCollider, waterSurfaceY, aimIndicatorPrefab);

        if (currentBobber != null)
            ripple.SetBobberTransform(currentBobber.transform);

        // A fish that spawns while a catch is in progress (the caught fish's replacement, or a
        // predator-despawn replacement mid-fight) must keep clear of the bobber like the rest of
        // the frozen-out school — EnforceBaitHoverCap is suspended during a catch and won't set
        // this for us, so an un-flagged newcomer would otherwise self-attract and pop its
        // notice/investigate indicators at the dragged bobber mid-fight.
        if (isCatchingFish)
            ripple.SetAvoidBobber(true);

        ripple.OnDespawnStarted += HandleFishDespawnStarted;

        activeFish.Add(ripple);
        // Shared list reference: each fish steers clear of its schoolmates (separation).
        ripple.SetSchool(activeFish);
    }

    // A fish hit its lifetime and is swimming off. It's done interacting — no longer
    // biteable — so drop it from the roster (the fade-out runs on its own) and bring in
    // its replacement right away, keeping the pond population steady.
    private void HandleFishDespawnStarted(FishRipple fish)
    {
        activeFish.Remove(fish);
        SpawnOneFish();
    }

    // Day/night gating: each fish has a separate spawn weight for day and night. A fish
    // only competes when its weight for the current time of day is non-zero. Night falls
    // naturally on the in-game clock, or instantly via the vendor's Night Lantern.
    private FishPreset PickEligibleFish()
    {
        bool isNight = WorldStateManager.Instance != null && WorldStateManager.Instance.IsNight;

        // Active fish feed bends the spawn roll: a SummonSpecies feed forces its target species
        // (overriding day/night gating — that's the point of summoning it), and a RepelPredators
        // feed drops every predator out of the eligible set so none can spawn while it lasts.
        FishFeedController feed = FishFeedController.Instance;
        if (feed != null && feed.SummonActive) return feed.SummonTarget;
        bool repelPredators = feed != null && feed.RepelPredatorsActive;

        List<FishPreset> eligible = new List<FishPreset>();
        float totalWeight = 0f;
        for (int i = 0; i < fishPool.availableFish.Count; i++)
        {
            FishPreset f = fishPool.availableFish[i];
            if (f == null) continue;
            if (repelPredators && f.IsPredator) continue;
            // Weight 0 = never spawns at this time of day; excluded up front so the weighted
            // roll below can never land on it (a roll of exactly 0 would otherwise pick it).
            float weight = f.GetSpawnWeight(isNight);
            if (weight > 0f)
            {
                eligible.Add(f);
                totalWeight += weight;
            }
        }
        if (eligible.Count == 0) return null;

        // Weighted roll: each fish's share of the spawn is its weight / totalWeight.
        float roll = Random.Range(0f, totalWeight);
        for (int i = 0; i < eligible.Count; i++)
        {
            roll -= eligible[i].GetSpawnWeight(isNight);
            if (roll <= 0f) return eligible[i];
        }
        return eligible[eligible.Count - 1];
    }

    // Scare off every predator currently in the water — used by a RepelPredators fish feed the
    // moment it's activated. Non-predators and already-scared fish are left alone.
    public void ScarePredators()
    {
        for (int i = 0; i < activeFish.Count; i++)
        {
            FishRipple f = activeFish[i];
            if (f == null || f.preset == null) continue;
            if (!f.preset.IsPredator) continue;
            if (f.CurrentState == FishRipple.FishState.Scared) continue;
            f.Scare();
        }
    }

    private void ResetRespawnTimer()
    {
        respawnTimer = Random.Range(respawnTimeMin, respawnTimeMax);
    }

}
