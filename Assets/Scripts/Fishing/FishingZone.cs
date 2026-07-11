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

    [Header("Multi-Fish Attract")]
    [Tooltip("Maximum number of follower fish that join the lead fish on each attract call. Set 0 to disable followers.")]
    public int maxFollowers = 4;
    [Tooltip("Optional radius cap (in world units) for how far away a fish can be and still join as a follower. Set ≤ 0 to ignore distance and just take the maxFollowers closest wandering fish in the zone.")]
    public float attractCallRadius = 0f;
    [Tooltip("Bait-side equivalent of the lure brain's Max Hover Fish below: caps how many fish may be " +
             "self-attracted (curious/hovering) around a regular bobber AT ONCE, enforced every frame — " +
             "not just after a lead is already chosen (which is all Max Followers above governs). The " +
             "nearest this-many stay eligible; everyone else is told to keep clear, so a bobber cast near " +
             "a school never gathers more than this many interested fish waiting to take a turn. Since " +
             "candidates for Max Followers are drawn from this same pool, keeping this at or below Max " +
             "Followers makes it the effective ceiling. Set 0 to leave hovering uncapped (old behavior).")]
    public int maxBaitHoverFish = 2;

    [Header("TP Lure Brain")]
    [Tooltip("Twilight-Princess-style attraction/bite logic for the lure path. Bobber (bait) fishing is unaffected. See LureBiteBrain for the TP mapping.")]
    public LureBiteBrain.Settings lureBrainSettings = new LureBiteBrain.Settings
    {
        movingRadiusMultiplier = 1.5f,
        movedDurationPerYank = 0.5f,
        crankMovedTopUp = 0.15f,
        directHitRadius = 0.8f,
        maxHoverFish = 2,
        maxActiveChasers = 2,
        ringDistance = 2.5f,
        arrivalBiteDelayMin = 0.3f,
        arrivalBiteDelayMax = 1.5f,
        rearmBiteDelayMin = 0.25f,
        rearmBiteDelayMax = 0.7f,
        patienceMin = 2.5f,
        patienceMax = 5.5f,
        boredCooldown = 8f,
        postMissCooldown = 1.5f,
        responseCooldown = 2f,
        baseBiteChance = 0.15f,
        movingBiteChance = 0.3f,
        nightChanceMultiplier = 1.5f,
        neutralCatchProbability = 0.5f,
        biteCommitChance = 0.2f,   // mostly tease dashes (~80%); a committed bite is the rarer payoff
        popperPreferenceRadiusMultiplier = 1.5f,
        popperPreferenceBiteMultiplier = 2f,
    };

    [Header("Auto-Nibble Timer (Passive Fishing)")]
    [Tooltip("Minimum seconds after the bobber lands before a fish auto-investigates and starts nibbling on its own.")]
    public float autoNibbleMin = 4f;
    [Tooltip("Maximum seconds after the bobber lands before a fish auto-investigates and starts nibbling on its own.")]
    public float autoNibbleMax = 12f;
    [Tooltip("Seconds to wait between retries if the timer fires but no fish has approached the bobber yet.")]
    public float autoNibbleRetryDelay = 1.5f;
    [Tooltip("With NO bait equipped (empty hook), multiply the auto-nibble delay by this — the few " +
             "species that bite baitless (FishPreset.bitesWithoutBait) still come, just far less eagerly. " +
             "1 = same as baited, higher = slower/rarer.")]
    public float noBaitInterestMultiplier = 2.5f;

    [Header("Water Detection")]
    [Tooltip("Tag used to find the water collider for surface height.")]
    public string waterTag = "Water";
    [Tooltip("Optional: drag the water surface object (or any marker sitting exactly on the waterline) here to override collider-based detection. Use this when the auto-detected height is wrong — e.g. zones at different heights, or no water collider overlapping the zone (which silently falls back to the zone trigger's TOP). Fish ride exactly at this object's Y.")]
    public Transform waterSurfaceMarker;

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

    private static bool MatchesEquippedTackleAndBait(FishRipple fish)
    {
        return fish != null
            && BobberInventory.PresetRespondsToEquippedTackle(fish.preset)
            && BaitInventory.PresetAcceptsSelectedBait(fish.preset);
    }

    private FishRipple currentlyAttractedFish;
    private List<FishRipple> followerFish = new List<FishRipple>();
    private float respawnTimer;
    private List<float> attractTimestamps = new List<float>();

    // Rebuilt every tick by EnforceBaitHoverCap — the fish currently allowed to be self-attracted
    // to a regular bobber. Mirrors LureBiteBrain's hoverSet for the bait path.
    private readonly List<FishRipple> baitHoverSet = new List<FishRipple>();

    // True while the player is cranking the reel on a lure (OnLureReelChanged).
    private bool isLureReelActive = false;

    // TP-style attraction/bite brain for the lure path.
    private LureBiteBrain lureBrain;

    // The fish currently gripping the lure during a TP grab (the reaction window). Non-null only
    // between OnLureGrabbed and its resolution (caught, spat out, or scared off).
    private FishRipple grabbingFish;

    // Auto-nibble timer state
    private float autoNibbleTimer = -1f;
    // True between OnFishBite and the matching reel-in / cancel — blocks any second fish
    // from getting promoted to lead while the player is reacting / fighting / catching.
    private bool isCatchingFish = false;

    void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        zoneCollider.isTrigger = true;
        lureBrain = new LureBiteBrain(HandleLureGrabStart, HandleLureGrabReleased);
    }

    void Start()
    {
        FindWaterSurface();
        SpawnInitialFish();
        ResetRespawnTimer();
    }

    private void FindWaterSurface()
    {
        if (waterSurfaceMarker != null)
        {
            waterSurfaceY = waterSurfaceMarker.position.y;
            return;
        }

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
        FishingEvents.OnLureReelChanged += HandleLureReelChanged;
        FishingEvents.OnLureTugged += HandleLureTugged;
        FishingEvents.OnBobberLandedInWater += HandleBobberLanded;
        FishingEvents.OnHookLureGrab += HandleHookLureGrab;
        FishingEvents.OnBiteImminent += HandleBiteImminent;
    }

    private void OnDisable()
    {
        FishingEvents.OnAttractFish -= HandleAttract;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnStartReeling -= HandleReelIn;
        FishingEvents.OnCancelFishing -= HandleReelIn;
        FishingEvents.OnLureReelChanged -= HandleLureReelChanged;
        FishingEvents.OnLureTugged -= HandleLureTugged;
        FishingEvents.OnBobberLandedInWater -= HandleBobberLanded;
        FishingEvents.OnHookLureGrab -= HandleHookLureGrab;
        FishingEvents.OnBiteImminent -= HandleBiteImminent;
        isLureReelActive = false;
    }

    private void HandleLureTugged()
    {
        if (currentBobber != null && BobberInventory.IsLureEquipped)
            lureBrain.OnLureTugged(in lureBrainSettings);
    }

    private void HandleLureReelChanged(bool active)
    {
        isLureReelActive = active;
    }

    void Update()
    {
        CleanupNullFish();

        // Bait-side hover cap: unlike the lure brain (which only runs while a lure is equipped),
        // regular bobber self-attraction had no continuous cap at all — every matching wandering
        // fish in range/cone could go Attracted the instant the bobber landed. Enforced every
        // frame so it also holds BEFORE any lead is chosen, not just after (that's Max Followers).
        // Suspended while a fish is being caught/fought (isCatchingFish): the hover cap would
        // otherwise clear avoidance on the nearest fish every frame, undoing the bite's
        // SetOtherFishAvoidance(true) and letting the school notice/investigate the dragged
        // bobber mid-fight. During a catch every other fish stays told to keep clear.
        if (currentBobber != null && !BobberInventory.IsLureEquipped && !isCatchingFish)
        {
            EnforceBaitHoverCap();
        }

        // (The lure crank used to continuously scare nearby fish here — removed: fish now only
        // scare when they're already interested and the player resets the cast, or on an attract
        // that abuses a fish already holding interest. isLureReelActive still feeds the brain.)

        // Check if currently attracted fish was scared or otherwise reset. Striking/Grabbing are
        // the bobber-bite dash-and-carry — the lead is mid-bite, so keep it (clearing here would
        // drop the grab in progress and let the auto-nibble timer promote a second fish during the
        // carry).
        if (currentlyAttractedFish != null)
        {
            if (currentlyAttractedFish.CurrentState != FishRipple.FishState.Attracted
                && currentlyAttractedFish.CurrentState != FishRipple.FishState.Nibbling
                && currentlyAttractedFish.CurrentState != FishRipple.FishState.Striking
                && currentlyAttractedFish.CurrentState != FishRipple.FishState.Grabbing)
            {
                currentlyAttractedFish = null;
                ScatterFollowers();
                SetOtherFishAvoidance(false);
                ResetAutoNibbleTimer();
            }
        }

        // The nibbling lead has teased the bobber enough and is ready to commit. Launch the bite
        // through the SAME grab callbacks the lure uses, so a bobber bite gives the identical
        // dash-grab-and-carry with the react-while-the-fish-holds-it window (HandleLureGrabStart →
        // OnLureGrabbed window; react → HandleHookLureGrab; miss → HandleLureGrabReleased). Bait
        // path only — lures drive their bites through lureBrain.
        if (!BobberInventory.IsLureEquipped && !isCatchingFish
            && currentlyAttractedFish != null && currentlyAttractedFish.NibbleReadyToBite)
        {
            currentlyAttractedFish.StartBobberBiteStrike(HandleLureGrabStart, HandleLureGrabReleased);
        }

        CleanupFollowers();
        UpdateAutoNibbleTimer();

        // TP lure brain — drives notice/hover/bite-roll/strike for the lure path. The bait
        // path keeps its attract/auto-nibble flow untouched.
        if (BobberInventory.IsLureEquipped && currentBobber != null && !isCatchingFish
            && currentBobber.IsInWater)
        {
            lureBrain.Tick(Time.deltaTime, activeFish, currentBobber, isLureReelActive, BobberInventory.IsPopperEquipped, in lureBrainSettings);
        }

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

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<BobberController>(out var bobber))
        {
            Rigidbody bobberRb = bobber.GetComponent<Rigidbody>();
            if (bobberRb != null && bobberRb.isKinematic)
                return;

            // Adopt only — no splashdown here. A bobber that is already floating drifted in
            // across the seam from a neighboring zone, and the handoff must be seamless (no
            // scare, no fake lure-splash). A genuine landing is handled by HandleBobberLanded.
            AdoptBobber(bobber);
        }
    }

    private void AdoptBobber(BobberController bobber)
    {
        if (currentBobber == bobber) return;

        Debug.Log($"Bobber entered pool: {fishPool?.poolName}");
        currentBobber = bobber;
        ResetAutoNibbleTimer();

        // Wire the bobber onto every fish — the brain's direct-hit strike needs it.
        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] != null)
                activeFish[i].SetBobberTransform(bobber.transform);
        }
    }

    private void HandleBobberLanded(BobberController bobber)
    {
        if (bobber == null) return;

        // Trigger order on landing isn't guaranteed — the water-enter event can fire before
        // this zone's own OnTriggerEnter. If we haven't adopted the bobber yet, do so now,
        // but only when it actually landed inside this zone.
        if (currentBobber != bobber)
        {
            Rigidbody bobberRb = bobber.GetComponent<Rigidbody>();
            if (bobberRb != null && bobberRb.isKinematic) return;
            if (!ContainsPoint(bobber.transform.position)) return;
            AdoptBobber(bobber);
        }

        Vector3 splashPos = bobber.transform.position;

        // Splashdown: counts as lure movement, and a lure landing directly on a fish
        // (within directHitRadius) makes that fish strike instantly.
        // (Landing near fish no longer scares them — a splash close by is a dinner bell, not a
        // threat. The only scares left are reset-with-interest and attract-abuse, per design.)
        if (BobberInventory.IsLureEquipped)
            lureBrain.OnSplashdown(in lureBrainSettings, activeFish, splashPos);
    }

    private bool ContainsPoint(Vector3 point)
    {
        return (zoneCollider.ClosestPoint(point) - point).sqrMagnitude < 0.01f;
    }

    private void OnTriggerStay(Collider other)
    {
        if (waterSurfaceMarker == null && other.CompareTag(waterTag))
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
            // A committed bite is being fought/reeled — the fight legitimately drags the bobber
            // out of the zone (FishingBobberPull). Clearing here would null bobberTransform and
            // lift the pond's lifetime/predator freeze mid-fight, so a wandering fish despawns or
            // gets hunted while the player is still fighting. Leave the freeze in place; the catch
            // resolves through HandleReelIn (OnStartReeling / OnCancelFishing), which clears cleanly.
            if (isCatchingFish) return;

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
