using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class FishingZone : MonoBehaviour
{
    public FishPool fishPool;

    [Header("Fish Spawning")]
    [Tooltip("Prefab for the fish ripple effect. Must have a FishRipple component.")]
    public GameObject fishRipplePrefab;
    [Tooltip("Prefab for the UI indicator shown above fish when aiming.")]
    public GameObject aimIndicatorPrefab;
    [Tooltip("Maximum number of fish that can be in the zone at once.")]
    public int maxFishCount = 5;

    [Header("Respawn Timing")]
    [Tooltip("Minimum seconds before a new fish spawns.")]
    public float respawnTimeMin = 10f;
    [Tooltip("Maximum seconds before a new fish spawns.")]
    public float respawnTimeMax = 30f;

    [Header("Bobber Splash Scare")]
    [Tooltip("Fish within this radius of the bobber landing spot get scared.")]
    public float splashScareRadius = 4f;

    [Header("Multi-Fish Attract")]
    [Tooltip("Maximum number of follower fish that join the lead fish on each attract call. Set 0 to disable followers.")]
    public int maxFollowers = 4;
    [Tooltip("Optional radius cap (in world units) for how far away a fish can be and still join as a follower. Set ≤ 0 to ignore distance and just take the maxFollowers closest wandering fish in the zone.")]
    public float attractCallRadius = 0f;

    [Header("Auto-Nibble Timer (Passive Fishing)")]
    [Tooltip("Minimum seconds after the bobber lands before a fish auto-investigates and starts nibbling on its own.")]
    public float autoNibbleMin = 4f;
    [Tooltip("Maximum seconds after the bobber lands before a fish auto-investigates and starts nibbling on its own.")]
    public float autoNibbleMax = 12f;
    [Tooltip("Seconds to wait between retries if the timer fires but no fish has approached the bobber yet.")]
    public float autoNibbleRetryDelay = 1.5f;

    [Header("Water Detection")]
    [Tooltip("Tag used to find the water collider for surface height.")]
    public string waterTag = "Water";

    private Collider zoneCollider;
    private Collider waterCollider;
    private float waterSurfaceY;
    private BobberController currentBobber;
    private List<FishRipple> activeFish = new List<FishRipple>();
    [Header("Scare Settings")]
    [Tooltip("Max attract presses allowed within the scare window before the fish is scared.")]
    public int maxAttractsBeforeScare = 3;
    [Tooltip("Time window in seconds for tracking attract spam.")]
    public float scareWindow = 2f;

    private static bool MatchesEquippedBait(FishRipple fish)
    {
        return fish != null && BaitInventory.PresetAcceptsSelectedBait(fish.preset);
    }

    private FishRipple currentlyAttractedFish;
    private List<FishRipple> followerFish = new List<FishRipple>();
    private float respawnTimer;
    private List<float> attractTimestamps = new List<float>();

    // Auto-nibble timer state
    private float autoNibbleTimer = -1f;
    // True between OnFishBite and the matching reel-in / cancel — blocks any second fish
    // from getting promoted to lead while the player is reacting / fighting / catching.
    private bool isCatchingFish = false;

    void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        zoneCollider.isTrigger = true;
    }

    void Start()
    {
        FindWaterSurface();
        SpawnInitialFish();
        ResetRespawnTimer();
    }

    private void FindWaterSurface()
    {
        Bounds zoneBounds = zoneCollider.bounds;
        Collider[] overlapping = Physics.OverlapBox(zoneBounds.center, zoneBounds.extents, Quaternion.identity);

        for (int i = 0; i < overlapping.Length; i++)
        {
            if (overlapping[i].CompareTag(waterTag))
            {
                waterCollider = overlapping[i];
                waterSurfaceY = waterCollider.bounds.max.y;
                return;
            }
        }

        Debug.LogWarning($"FishingZone '{fishPool?.poolName}': No collider tagged '{waterTag}' found. Using zone collider top as water surface.");
        waterSurfaceY = zoneBounds.max.y;
    }

    private void OnEnable()
    {
        FishingEvents.OnAttractFish += HandleAttract;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnStartReeling += HandleReelIn;
        FishingEvents.OnCancelFishing += HandleReelIn;
    }

    private void OnDisable()
    {
        FishingEvents.OnAttractFish -= HandleAttract;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnStartReeling -= HandleReelIn;
        FishingEvents.OnCancelFishing -= HandleReelIn;
    }

    void Update()
    {
        CleanupNullFish();

        // Check if currently attracted fish was scared or otherwise reset
        if (currentlyAttractedFish != null)
        {
            if (currentlyAttractedFish.CurrentState != FishRipple.FishState.Attracted
                && currentlyAttractedFish.CurrentState != FishRipple.FishState.Nibbling)
            {
                currentlyAttractedFish = null;
                ScatterFollowers();
                SetOtherFishAvoidance(false);
                ResetAutoNibbleTimer();
            }
        }

        CleanupFollowers();
        UpdateAutoNibbleTimer();

        // Respawn fish over time
        if (activeFish.Count < maxFishCount)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f)
            {
                SpawnOneFish();
                ResetRespawnTimer();
            }
        }
    }

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

        activeFish.Add(ripple);
    }

    // Strict gating: a fish only spawns when its preferredWeather matches the current
    // active fishing weather. WorldStateManager reports Night while ForcedNight is
    // active, otherwise Sunny — so Night-only fish appear only after the player buys
    // the Night time-of-day option at the vendor.
    private FishPreset PickEligibleFish()
    {
        WeatherType active = WorldStateManager.Instance != null
            ? WorldStateManager.Instance.GetActiveFishingWeather()
            : WeatherType.Sunny;

        List<FishPreset> eligible = new List<FishPreset>();
        for (int i = 0; i < fishPool.availableFish.Count; i++)
        {
            FishPreset f = fishPool.availableFish[i];
            if (f != null && f.preferredWeather == active) eligible.Add(f);
        }
        if (eligible.Count == 0) return null;
        return eligible[Random.Range(0, eligible.Count)];
    }

    private void ResetRespawnTimer()
    {
        respawnTimer = Random.Range(respawnTimeMin, respawnTimeMax);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber))
        {
            Rigidbody bobberRb = bobber.GetComponent<Rigidbody>();
            if (bobberRb != null && bobberRb.isKinematic)
                return;

            Debug.Log($"Bobber entered pool: {fishPool.poolName}");
            currentBobber = bobber;
            ResetAutoNibbleTimer();

            Vector3 splashPos = bobber.transform.position;
            for (int i = 0; i < activeFish.Count; i++)
            {
                if (activeFish[i] == null) continue;

                activeFish[i].SetBobberTransform(bobber.transform);

                float dist = HorizontalDistance(activeFish[i].transform.position, splashPos);
                if (dist < splashScareRadius)
                {
                    activeFish[i].Scare();
                }
            }
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag(waterTag))
        {
            waterSurfaceY = other.bounds.max.y;
            for (int i = 0; i < activeFish.Count; i++)
            {
                if (activeFish[i] != null)
                    activeFish[i].SetWaterSurfaceY(waterSurfaceY);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber) && bobber == currentBobber)
        {
            Debug.Log($"Bobber exited pool: {fishPool.poolName}");
            ClearBobber();
        }
    }

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
                // Notify the fish the player is still engaging — resets lose-interest timer
                currentlyAttractedFish.NotifyAttractInput();

                // Re-call attract — this handles the "too close = scare" check inside FishRipple
                currentlyAttractedFish.AttractToBobber();

                // Refresh interest for follower fish too, so the school keeps approaching
                for (int i = 0; i < followerFish.Count; i++)
                {
                    if (followerFish[i] != null && followerFish[i].CurrentState == FishRipple.FishState.Attracted)
                    {
                        followerFish[i].NotifyAttractInput();
                    }
                }

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
                if (!MatchesEquippedBait(fish)) continue;
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
                lead.AttractToBobber();
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
                        follower.AttractToBobber();
                        if (follower.CurrentState == FishRipple.FishState.Attracted)
                        {
                            followerFish.Add(follower);
                            taken++;
                        }
                        else
                        {
                            // AttractToBobber may have scared a too-close fish — clear follower flag.
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

        Vector3 bobberPos = currentBobber.transform.position;
        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] == null) continue;
            float dist = HorizontalDistance(activeFish[i].transform.position, bobberPos);
            if (dist < splashScareRadius || activeFish[i].CurrentState == FishRipple.FishState.Nibbling
                || activeFish[i].CurrentState == FishRipple.FishState.Attracted)
            {
                activeFish[i].Scare();
            }
        }

        currentlyAttractedFish = null;
        ScatterFollowers();
        SetOtherFishAvoidance(false);
        autoNibbleTimer = -1f;
        isCatchingFish = false;
    }

    private void HandleFishBite(BobberController bobber)
    {
        if (bobber != currentBobber) return;

        // Remove the fish that was nibbling (it got caught)
        if (currentlyAttractedFish != null)
        {
            RemoveFish(currentlyAttractedFish);
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

    private void CleanupNullFish()
    {
        activeFish.RemoveAll(f => f == null);
    }

    private void ClearBobber()
    {
        currentBobber = null;
        currentlyAttractedFish = null;
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

    private void ResetAutoNibbleTimer()
    {
        if (currentBobber == null)
        {
            autoNibbleTimer = -1f;
            return;
        }
        autoNibbleTimer = Random.Range(autoNibbleMin, autoNibbleMax);
    }

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
            if (!MatchesEquippedBait(fish)) continue;

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
