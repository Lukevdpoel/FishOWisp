using UnityEngine;

// What a tossed fish-feed does to the water for its duration.
public enum FishFeedEffect
{
    None,
    // Predators stop spawning, and any predators already in the water get scared off.
    RepelPredators,
    // Spawns are forced to a single target species.
    SummonSpecies,
}

// Holds the one active fish-feed effect and its expiry. For now "buying" the feed is what
// activates it (see FishFeedShopOffer) — there's no inventory or throwing yet. FishingZone
// consults the live effect in its spawn roll (FishingZone.PickEligibleFish); RepelPredators
// additionally scares the predators already swimming the moment it's activated.
//
// Plain scene singleton (GenericSingleton): drop one on a manager object. Every read is
// null-guarded so the spawn path works fine in a scene that has no FishFeedController.
public class FishFeedController : GenericSingleton<FishFeedController>
{
    public FishFeedEffect ActiveEffect { get; private set; } = FishFeedEffect.None;
    public FishPreset SummonTarget { get; private set; }

    // Expiry stamp in UNSCALED real seconds. Time.unscaledTime is used (not Time.time) so the feed
    // runs on true wall-clock minutes — unaffected by the in-game day/night clock, by Time.timeScale,
    // or by any pause. 8 minutes of feed = 8 real minutes.
    private float expiry;

    public bool IsActive => ActiveEffect != FishFeedEffect.None && Time.unscaledTime < expiry;
    public bool RepelPredatorsActive => IsActive && ActiveEffect == FishFeedEffect.RepelPredators;
    public bool SummonActive => IsActive && ActiveEffect == FishFeedEffect.SummonSpecies && SummonTarget != null;

    /// <summary>Real seconds left on the current effect (0 when nothing is active).</summary>
    public float SecondsRemaining => IsActive ? Mathf.Max(0f, expiry - Time.unscaledTime) : 0f;

    /// <summary>
    /// Start a feed effect for <paramref name="seconds"/>. Re-activating replaces whatever was
    /// running (and refreshes the timer). RepelPredators immediately clears the current predators.
    /// </summary>
    public void Activate(FishFeedEffect effect, FishPreset target, float seconds)
    {
        if (effect == FishFeedEffect.None || seconds <= 0f) return;

        ActiveEffect = effect;
        SummonTarget = effect == FishFeedEffect.SummonSpecies ? target : null;
        expiry = Time.unscaledTime + seconds;

        if (effect == FishFeedEffect.RepelPredators)
        {
            FishingZone[] zones = FindObjectsByType<FishingZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
                if (zones[i] != null) zones[i].ScarePredators();
        }
    }

    private void Update()
    {
        if (ActiveEffect != FishFeedEffect.None && Time.unscaledTime >= expiry)
        {
            ActiveEffect = FishFeedEffect.None;
            SummonTarget = null;
        }
    }
}
