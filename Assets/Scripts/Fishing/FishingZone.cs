using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public partial class FishingZone : MonoBehaviour
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

}
