using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

public partial class FishingRodController : MonoBehaviour
{
    public enum FishingState { Idle, Charging, WaitingForBite, LureReeling, FishOnTheLine, FightingFish, Reeling, InspectingCatch, Cooldown }

    [Header("State")]
    [SerializeField] public FishingState currentState = FishingState.Idle;

    // True while a cast is out — a bobber/lure is in the water, a fish is on the line, or a
    // fight/reel-in is in progress. The research scanner reads this to refuse "analyzing" fish
    // while you're actively fishing: research is meant for observing free-swimming fish, not the
    // ones your cast is already working. Aim/zoom itself is unaffected.
    public bool IsCastActive =>
        currentState == FishingState.WaitingForBite
        || currentState == FishingState.LureReeling
        || currentState == FishingState.FishOnTheLine
        || currentState == FishingState.FightingFish
        || currentState == FishingState.Reeling;

    [Header("Object References")]
    public GameObject danglingBobber;
    public Transform playerModel;

    [Header("Component References")]
    [SerializeField] private CatchInspectionHandler inspectionHandler;
    [Tooltip("Optional: drag the InventoryUI here. If left empty, falls back to a scene-wide " +
             "search at Awake (includes inactive GameObjects). The drag-assigned reference is " +
             "safer because it survives runtime activation/deactivation cycles.")]
    [SerializeField] private InventoryUI inventoryUIRef;

    [Header("Animation Settings")]
    public Animator playerAnimator;
    public string failAnimationTrigger = "FailHook";

    [Header("Cooldowns & Timing")]
    public float failCooldown = 2.0f;
    public float reelInDuration = 0.8f;
    public float reelInArcHeight = 5.0f;
    public float reactionTime = 1.5f;

    [Header("Auto-Reel Settings")]
    [Tooltip("If the player walks further than this from where they threw, the line auto-reels back in.")]
    public float maxDistanceFromCast = 15f;
    [Tooltip("Cosine threshold of the player's facing vs. the direction to the bobber. Below this, the line auto-reels. Default -0.05 ≈ slightly past 90° (sideways).")]
    [Range(-1f, 1f)]
    public float turnAwayDotThreshold = -0.05f;
    [Tooltip("Grace time after casting before turn-away auto-reel can trigger, so the cast animation can settle.")]
    public float turnAwayGracePeriod = 0.5f;

    [Header("Fish Fight (play as the fish)")]
    [Tooltip("Tunables for the fish-control fight: tank-steer the hooked fish, reel to drag it to the bank, ride out its fight bursts. See FishFightHandler.")]
    public FishFightHandler.Settings fightSettings = new FishFightHandler.Settings
    {
        swimSpeed = 1.0f,
        fightSwimSpeed = 2.5f,
        steerTurnRate = 140f,
        fightTurnRate = 220f,
        fightSteerAuthority = 0.3f,
        fightArcDegrees = 75f,
        calmDurationRange = new Vector2(1.5f, 3.0f),
        fightDurationRange = new Vector2(0.5f, 1.1f),
        sizeIntensity = 0.5f,
        minFightSeconds = 1.5f,
    };
    [Tooltip("Extra line (m) beyond the hook distance the fish can take before the line snaps and it escapes. The break distance is fixed at hook time, so every fight has the same stakes whether the cast was long or short.")]
    public float lineSlackBeforeBreak = 8f;

    [Header("Reel-In Pull")]
    [Tooltip("Horizontal distance from the WATERLINE (where the line player→bobber crosses onto the water) at which the fish counts as caught. Measuring to the shore — not the player — lets the catch close even when the player stands well back from the bank. Height doesn't matter; only XZ distance is checked.")]
    public float catchDistance = 3.0f;
    [Tooltip("Extra speed (m/s) added along the fish's FACING while the player holds reel — cranking the line hauls the fish head-first wherever it points. Toward the bank when steered right; straight into its escape when it isn't, which is how the line gets snapped.")]
    public float reelPullSpeed = 2.0f;
    [Tooltip("Collider layer of the water-surface mesh (WaterColliderFish) used to locate the waterline " +
             "the catch/retract reels to. catchDistance & the lure's retractDistance are measured from " +
             "the point where the line player→bobber crosses this water surface, so a player standing back " +
             "from the bank can still complete the reel-in. Leave at WaterColliderFish; if 0/unset it's " +
             "resolved by name at Awake, and if that fails the old measure-to-the-player behavior is used.")]
    public LayerMask waterlineMask;

    [Header("Lure Settings")]
    [Tooltip("Tunables for the Twilight-Princess-style lure reel physics. Bites are decided by " +
             "the LureBiteBrain on each FishingZone, not here.")]
    public LureReelHandler.Settings lureSettings = new LureReelHandler.Settings
    {
        reelHoldForce = 30.0f,
        reelAlignRate = 6.0f,
        yankPullSpeed = 2.5f,
        pullDecayRate = 2.5f,
        yankArcAngleDeg = 30.0f,
        maxArcAngleDeg = 45.0f,
        headingRecenterRate = 2.0f,
        yankAlignmentImpulse = 0.6f,
        lureAngularDamping = 1.5f,
        retractDistance = 3.0f,
        intensityDecayRate = 4.0f,
        visualPitchAngleDeg = 10.0f,
        visualBobAmplitude = 0.0f,
        visualBobFrequencyHz = 3.0f,
        popperBounceAmplitudeDeg = 6.0f,
        popperBounceFrequencyHz = 5.0f,
        popperSplashInterval = 0.4f,
    };

    private Coroutine fishEscapeCoroutine;
    // True while a fish is gripping the lure and the player's reaction window is open. The real
    // fish (FishRipple) owns the timeout; the rod just routes the hook press.
    private bool isLureGrabActive;
    private BobberController bobberInWater;
    private BobberController activeBobber;
    private InventoryUI cachedInventoryUI;
    private FishingLine fishingLine;

    private CaughtFish caughtFishInstance = null;
    private PlayerController playerController;
    private bool isAiming;
    // Tracks the LT cast-aim bracket frame-to-frame so we can detect the press edge (start
    // aiming) and the release edge (abandon the aim) without a separate Input System press point.
    private bool castAimHeldLast;
    private Vector3 castOriginPosition;
    private float castStartTime;

    private int hashFail;

    private readonly LureReelHandler lureReel = new LureReelHandler();
    private readonly FishFightHandler fishFight = new FishFightHandler();
    // Hook-time distance from fish to the bank, for the (hidden-by-default) HUD progress bar.
    private float fightStartDistance;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        if (playerController == null) playerController = GetComponentInParent<PlayerController>();
        if (playerController == null) Debug.LogError("FishingRodController: PlayerController missing!");

        hashFail = Animator.StringToHash(failAnimationTrigger);

        // If the mask wasn't assigned in the inspector, resolve it by name so the waterline
        // measurement still works out of the box. Falls back to the old player-distance behavior
        // (handled in FishingBobberPull) when the layer doesn't exist in this project.
        if (waterlineMask.value == 0)
        {
            waterlineMask = LayerMask.GetMask("WaterColliderFish");
            if (waterlineMask.value == 0)
                Debug.LogWarning("FishingRodController: no 'WaterColliderFish' layer found and waterlineMask unset — reel-in distance will measure to the player, not the waterline.");
        }

        fishingLine = GetComponentInChildren<FishingLine>(includeInactive: true);
        if (fishingLine == null) fishingLine = FindFirstObjectByType<FishingLine>(FindObjectsInactive.Include);

        // Resolution chain: static Instance (set by InventoryUI's own Awake) → inspector
        // ref → prefab-root sibling search → scene-wide include-inactive find. The static
        // path is the primary one; the rest are defense-in-depth for unusual setups.
        cachedInventoryUI = ResolveInventoryUI();
    }

    private InventoryUI ResolveInventoryUI()
    {
        if (InventoryUI.Instance != null) return InventoryUI.Instance;
        if (inventoryUIRef != null) return inventoryUIRef;

        Transform root = transform.root != null ? transform.root : transform;
        var fromRoot = root.GetComponentInChildren<InventoryUI>(includeInactive: true);
        if (fromRoot != null) return fromRoot;

        return FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        FishingEvents.OnThrowBobber += HandleThrow;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnCancelFishing += CancelFishingAction;
        FishingEvents.OnBobberLandedInWater += HandleBobberLanded;
        FishingEvents.OnLureGrabbed += HandleLureGrabbed;
        FishingEvents.OnLureGrabReleased += HandleLureGrabReleased;
    }

    private void OnDisable()
    {
        FishingEvents.OnThrowBobber -= HandleThrow;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnCancelFishing -= CancelFishingAction;
        FishingEvents.OnBobberLandedInWater -= HandleBobberLanded;
        FishingEvents.OnLureGrabbed -= HandleLureGrabbed;
        FishingEvents.OnLureGrabReleased -= HandleLureGrabReleased;
    }

    private void HandleBobberLanded(BobberController bobber)
    {
        bobberInWater = bobber;

        // Lures land in a random tumble. Reset to a natural "fish trailing the line" pose
        // (LineAttachPoint side facing the rod) so the player has a predictable starting
        // direction for tank steering.
        if (BobberInventory.IsLureEquipped && currentState == FishingState.LureReeling)
        {
            Transform rodRef = fishingLine != null && fishingLine.rodTip != null
                ? fishingLine.rodTip : playerModel;
            LureReelHandler.OrientForInitialPose(bobber, rodRef);
        }
    }

    void Update()
    {
        HandleInput();
        CheckCastDistance();

        if (currentState == FishingState.InspectingCatch && inspectionHandler != null)
        {
            inspectionHandler.UpdateInspection(playerModel);
        }
    }

    // The lure handler drives the rigidbody with AddForce/AddTorque, and forces accumulated
    // outside FixedUpdate only persist for the NEXT physics step — ticking it from Update made
    // the reel-in speed scale with frame rate (slow at low fps, fast at high fps). At fixed
    // rate it's exactly one application per step; Time.deltaTime inside FixedUpdate returns
    // fixedDeltaTime, so the handler's decay math needs no change. The fish fight drives the
    // same rigidbody, so it ticks here too.
    private void FixedUpdate()
    {
        TickLure();
        TickFishFight();
    }

    // The fish-control fight: the player IS the fish now. Tank-steer with A/D / left stick;
    // holding reel (LMB/RT) hauls the fish along its FACING — bankward when aligned, into its
    // escape when not. The handler runs the fish's own fight bursts and the snap tension.
    private void TickFishFight()
    {
        if (currentState != FishingState.FightingFish) return;
        if (activeBobber == null) return;

        // Same input gate as the lure crank: the notebook owns LMB/RT while open. Both reads
        // are state (not edge), so they're FixedUpdate-safe.
        bool inputBlocked = NoteMenu.IsNotebookOpen || Time.timeScale == 0f;
        bool reelHeld = !inputBlocked
            && (KeyInput.ReelHeld || GamepadInput.ReelHeld);
        float steer = inputBlocked
            ? 0f
            : Mathf.Clamp(Input.GetAxisRaw("Horizontal") + GamepadInput.Move.x, -1f, 1f);

        Vector3 reelTarget = ComputeReelTarget(activeBobber);
        FishFightHandler.Outcome outcome = fishFight.Tick(
            activeBobber, reelTarget, transform.position, reelHeld, steer,
            in fightSettings, reelPullSpeed, catchDistance, waterlineMask);

        // Feed the (hidden-by-default) HUD bar a normalized approach: 0 at the hook spot,
        // 1 at the bank.
        Vector3 toBank = activeBobber.transform.position - reelTarget;
        toBank.y = 0f;
        float span = Mathf.Max(0.01f, fightStartDistance - catchDistance);
        FishingEvents.OnFishFightProgressUpdate?.Invoke(
            Mathf.Clamp01(1f - (toBank.magnitude - catchDistance) / span), 1f);

        if (outcome == FishFightHandler.Outcome.Caught)
        {
            WinFishFight();
        }
        else if (outcome == FishFightHandler.Outcome.Escaped)
        {
            Debug.Log("[FishFight] Fish took all the line slack — it got away.");
            fishFight.End(activeBobber);
            StartCoroutine(FailRoutine());
        }
    }

    private void TickLure()
    {
        if (currentState != FishingState.LureReeling) return;
        if (bobberInWater == null) return;
        // Don't probe the waterline (or tick the handler) until the lure is actually floating —
        // the handler no-ops out of water anyway, so skipping here keeps the raycasts in
        // ComputeReelTarget from running during the cast's flight or if the lure ever beaches.
        if (!bobberInWater.IsInWater) return;

        // The notebook owns LMB and RT (page flip) while open; without this gate cranking
        // continues underneath it. Both reads are state (not edge), so they're FixedUpdate-safe.
        bool inputBlocked = NoteMenu.IsNotebookOpen || Time.timeScale == 0f;
        bool reelHeld = !inputBlocked
            && (KeyInput.ReelHeld || GamepadInput.LureReelHeld);
        float lateral = inputBlocked
            ? 0f
            : Mathf.Clamp(Input.GetAxisRaw("Horizontal") + GamepadInput.Move.x, -1f, 1f);

        LureReelHandler.Outcome outcome = lureReel.Tick(
            bobberInWater, ComputeReelTarget(bobberInWater), reelHeld, lateral, BobberInventory.IsPopperEquipped, in lureSettings);

        // Bites no longer come from here — the LureBiteBrain (per FishingZone) drives a real
        // fish to strike and calls BobberController.HookFish, which lands in HandleFishBite.
        if (outcome == LureReelHandler.Outcome.RetractRequested)
        {
            Debug.Log("[Lure] Reached catch range without a bite — retracting.");
            currentState = FishingState.Reeling;
            StartCoroutine(ReelInBobberRoutine(null));
        }
    }

    // The reel/retract reference point: the waterline where the line from the player out to the
    // bobber crosses onto the water (or the player themselves when they're at/over the water).
    // Both the lure retract and the fish-fight catch measure their completion distance to this
    // point so a player standing back from the bank can still finish the reel-in.
    private Vector3 ComputeReelTarget(BobberController bobber)
    {
        Vector3 playerPos = playerModel != null ? playerModel.position : transform.position;
        if (bobber == null) return playerPos;
        return FishingBobberPull.NearestWaterlinePoint(playerPos, bobber.transform.position, waterlineMask);
    }

    private void CheckCastDistance()
    {
        if (currentState != FishingState.WaitingForBite) return;

        var reason = FishingAutoReel.Check(
            transform.position, castOriginPosition,
            bobberInWater != null ? bobberInWater.transform : null,
            playerModel,
            castStartTime,
            maxDistanceFromCast, turnAwayDotThreshold, turnAwayGracePeriod);

        if (reason == FishingAutoReel.Reason.None) return;

        Debug.Log(reason == FishingAutoReel.Reason.WalkedTooFar
            ? "Walked too far from cast — auto-reeling."
            : "Turned away from bobber — auto-reeling.");
        currentState = FishingState.Reeling;
        StartCoroutine(ReelInBobberRoutine(null));
    }

    private void ResetFishingState()
    {
        Debug.Log($"[FishFight] ResetFishingState — currentState: {currentState}, bobberInWater: {(bobberInWater != null ? "valid" : "NULL")}, activeBobber: {(activeBobber != null ? "valid" : "NULL")}");
        if (currentState == FishingState.Idle) return;

        // Never leave the grab flag hanging across a reset/cancel, and close out any fight
        // still in flight so listeners aren't left mid reeling/struggle state.
        isLureGrabActive = false;
        fishFight.End(activeBobber);

        if (playerController != null)
        {
            playerController.SetCatchCamera(false);
            playerController.areControlsLocked = false;
            playerController.UnlockFromFish();
        }

        if (inspectionHandler != null) inspectionHandler.ForceCleanup();

        // A confirmed catch ends here with the fish still hanging from the held line — release
        // the hold so FishingLine parks the bobber (which also disposes the hanging fish visual
        // and restores the bobber's own visuals via ResetForCast). No-op when nothing was held;
        // the cancel path clears the hold inside FishingLine itself.
        if (fishingLine != null) fishingLine.EndCatchHold();

        StopAiming();
        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        fishEscapeCoroutine = null;
        activeBobber = null;
        bobberInWater = null;
        caughtFishInstance = null;
    }
}
