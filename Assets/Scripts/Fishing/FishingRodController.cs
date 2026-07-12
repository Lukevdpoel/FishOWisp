using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

public class FishingRodController : MonoBehaviour
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

    private void HandleInput()
    {
        if (Time.timeScale == 0f) return;

        // The notebook owns the mouse while open (and the game does NOT pause — there is no
        // PauseManager in any scene). Without this gate, LMB starts charging and the no-bait
        // path pops the inventory over the notebook. A cast being aimed can't survive the
        // notebook taking the mouse either — abandon it cleanly instead of leaving the marker
        // aiming underneath.
        if (NoteMenu.IsNotebookOpen)
        {
            StopAiming();
            AbandonCharge();
            return;
        }

        // External locks (dialogue, shop, cutscene) block starting fishing — but once a flow is
        // in progress we hold the movement lock ourselves and must keep reading our own inputs.
        if (currentState == FishingState.Idle
            && playerController != null && playerController.areControlsLocked) return;

        // --- Aim Mode (RMB / LB) - works in any non-fight/non-inspection state ---
        if ((KeyInput.AimPressed || GamepadInput.AimPressed) && !isAiming)
        {
            isAiming = true;
            FishingEvents.OnStartAiming?.Invoke();
        }

        if ((KeyInput.AimReleased || GamepadInput.AimReleased) && isAiming)
        {
            StopAiming();
        }

        // --- Keyboard/mouse verbs, context-switched by fishing state. All four default to LMB
        // (one do-fishing button, like the old hardcoded Mouse0 switch) but each reads its own
        // binding so they can be split apart on the InputBindings asset. ---
        switch (currentState)
        {
            case FishingState.Idle:
                if (KeyInput.CastAimPressed) StartCharging();
                break;
            case FishingState.WaitingForBite:
                if (KeyInput.AttractPressed) FishingEvents.OnAttractFish?.Invoke();
                break;
            case FishingState.FishOnTheLine:
                if (KeyInput.HookPressed)
                {
                    if (isLureGrabActive) ConfirmLureHook();
                    else HookFishAndStartFight();
                }
                break;
            case FishingState.InspectingCatch:
                if (KeyInput.ConfirmPressed) TryFinishInspection();
                break;
        }

        // --- LT: hold to aim the cast marker, release to put the rod away — a mirror of holding
        // LMB on mouse/keyboard. The throw itself is the whip gesture (RodCasting): while the aim
        // button is held, yanking the right stick / mouse out and snapping it back the opposite
        // way fires the cast; releasing the aim button without a whip just abandons the aim (no
        // cooldown, nothing thrown). Only armed from Idle/Charging so the trigger keeps its
        // post-cast meanings without cross-firing here. ---
        bool castAimHeld = GamepadInput.CastAimHeld;
        if (currentState == FishingState.Idle && castAimHeld && !castAimHeldLast)
        {
            StartCharging();
        }
        else if (currentState == FishingState.Charging && castAimHeldLast && !castAimHeld)
        {
            AbandonCharge();
        }
        castAimHeldLast = castAimHeld;

        // --- RB: reset a cast already in the water (bobber or lure alike), or cut the line
        // mid-fight to give up the fish. ---
        if (GamepadInput.ResetCastPressed)
        {
            if (currentState == FishingState.WaitingForBite || currentState == FishingState.LureReeling)
                ReelLineBack();
            else if (currentState == FishingState.FightingFish)
                CutLineDuringFight();
        }

        // --- RT tap: attract fish toward the bobber while waiting for a bite. ---
        if (GamepadInput.AttractPressed && currentState == FishingState.WaitingForBite)
        {
            FishingEvents.OnAttractFish?.Invoke();
        }

        // --- A: confirm/finish catch inspection. (Casting is no longer on A — the whip gesture
        // throws while aiming.) ---
        if (GamepadInput.ConfirmPressed)
        {
            switch (currentState)
            {
                case FishingState.InspectingCatch:
                    TryFinishInspection();
                    break;
            }
        }

        // --- Reel button (default RT) press reacts to a biting fish — the SAME control the fight
        // reel uses (held), so hooking and reeling share one button on every scheme. Mirrors LMB on
        // keyboard/mouse, where the down-edge hooks the bite and the hold reels. ---
        if (GamepadInput.ReelPressed && currentState == FishingState.FishOnTheLine)
        {
            if (isLureGrabActive) ConfirmLureHook();
            else HookFishAndStartFight();
        }

        // --- Releasing the cast-aim key (LMB up) without a whip abandons the aim — nothing is
        // thrown; the whip gesture inside RodCasting is the only way to actually cast. ---
        if (KeyInput.CastAimReleased && currentState == FishingState.Charging)
        {
            AbandonCharge();
        }

        // --- Instant reset (default E) — keyboard mirror of the RB cast reset. The reel-in arc is
        // kinematic and flies over geometry, so this doubles as the escape hatch when the lure
        // snags behind terrain during the physics reel. Mid-fight it cuts the line instead. ---
        if (KeyInput.ResetCastPressed)
        {
            if (currentState == FishingState.WaitingForBite || currentState == FishingState.LureReeling)
                ReelLineBack();
            else if (currentState == FishingState.FightingFish)
                CutLineDuringFight();
        }
    }

    private void StopAiming()
    {
        if (!isAiming) return;
        isAiming = false;
        FishingEvents.OnStopAiming?.Invoke();
    }

    // The aim button was released without a whip: no cast, no cooldown — everything just returns
    // to idle. OnCancelCharging retires the marker/camera charge reaction; OnChargeCanceled snaps
    // the animator out of the windup and releases anything (inventory) locked for the charge.
    private void AbandonCharge()
    {
        if (currentState != FishingState.Charging) return;
        currentState = FishingState.Idle;
        FishingEvents.OnCancelCharging?.Invoke();
        FishingEvents.OnChargeCanceled?.Invoke();
        if (playerController != null) playerController.LockControls(false);
    }

    private void ReelLineBack()
    {
        Debug.Log("Reeling back the line.");
        currentState = FishingState.Reeling;
        StartCoroutine(ReelInBobberRoutine(null));
    }

    // Deliberately give up a fight: cut the line and let the fish go. Runs the exact same exit
    // as the line snapping (FishFightHandler.Outcome.Escaped) — the fish is released for good,
    // swims off, dives and fades out (HookedFishController.BeginEscape), and the cast resets
    // through the fail path.
    private void CutLineDuringFight()
    {
        Debug.Log("[FishFight] Line cut — the fish gets away.");
        fishFight.End(activeBobber);
        StartCoroutine(FailRoutine());
    }

    private InventoryUI GetInventoryUI()
    {
        if (cachedInventoryUI != null) return cachedInventoryUI;
        cachedInventoryUI = ResolveInventoryUI();
        return cachedInventoryUI;
    }

    private void StartCharging()
    {
        if (playerController != null && playerController.areControlsLocked)
        {
            Debug.Log("[FishFight] StartCharging BLOCKED — controls locked");
            return;
        }
        // Can't cast while jumping or otherwise airborne — must be planted on the ground.
        if (playerController != null && !playerController.IsGrounded)
        {
            Debug.Log("[FishFight] StartCharging BLOCKED — player is airborne");
            return;
        }
        if (bobberInWater != null || activeBobber != null)
        {
            Debug.Log($"[FishFight] StartCharging BLOCKED — bobberInWater: {(bobberInWater != null ? "exists" : "null")}, activeBobber: {(activeBobber != null ? "exists" : "null")}");
            return;
        }
        // Casting with an empty hook is allowed now (a baitless cast still draws bitesWithoutBait fish,
        // just slowly). This only drops a depleted bait selection back to "no bait"; it never pops the
        // gear menu. Lures use no bait, so it's a no-op for them.
        if (!BobberInventory.IsLureEquipped && BaitInventory.Instance != null)
        {
            BaitInventory.Instance.EnsureBaitSelected();
        }
        currentState = FishingState.Charging;
        if (playerController != null) playerController.LockControls(true);
        FishingEvents.OnStartCharging?.Invoke();
    }
    private void HandleThrow(Vector3 direction, float force)
    {
        danglingBobber?.SetActive(false);
        castOriginPosition = transform.position;
        castStartTime = Time.time;

        if (BobberInventory.IsLureEquipped)
        {
            lureReel.Reset();
            currentState = FishingState.LureReeling;
        }
        else
        {
            currentState = FishingState.WaitingForBite;
        }
    }
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
    private void HookFishAndStartFight()
    {
        if (fishEscapeCoroutine != null) StopCoroutine(fishEscapeCoroutine);
        FishingEvents.OnHookFishSuccess?.Invoke();
        currentState = FishingState.FightingFish;
        if (activeBobber != null)
        {
            // The fish fight drives the bobber rigidbody directly (FishFightHandler), so it
            // must be dynamic. The old self-swimming struggle (SetStruggleActive) stays off
            // for the whole fight — the player steers the fish now.
            Rigidbody rb = activeBobber.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            activeBobber.SetPlayerTransform(transform);
        }
        FishingEvents.OnFishFightBegin?.Invoke(activeBobber.HookedFish.preset);
        fishFight.Begin(activeBobber, activeBobber.HookedFish.preset, transform.position,
                        lineSlackBeforeBreak, in fightSettings);

        Vector3 toBank = activeBobber.transform.position - ComputeReelTarget(activeBobber);
        toBank.y = 0f;
        fightStartDistance = toBank.magnitude;
    }
    private void WinFishFight()
    {
        Debug.Log($"[FishFight] WinFishFight — activeBobber: {(activeBobber != null ? "valid" : "NULL")}, inspectionHandler: {(inspectionHandler != null ? "valid" : "NULL")}");
        currentState = FishingState.Reeling;
        if (playerController != null) playerController.UnlockFromFish();
        fishFight.End(activeBobber);
        caughtFishInstance = activeBobber.HookedFish;
        FishingEvents.OnFishFightEnd?.Invoke(true);
        if (activeBobber != null) activeBobber.SwapBobberForFishModel();

        StartCoroutine(ReelInBobberRoutine(caughtFishInstance));
    }
    private void CancelFishingAction()
    {
        if (currentState != FishingState.Cooldown)
        {
            StopAiming();
            // Bobber lifecycle is owned by FishingLine — it parks the persistent instance on OnCancelFishing.
            ResetFishingState();
        }
    }

    private IEnumerator ReelInBobberRoutine(CaughtFish fishToInventory)
    {
        FishingEvents.OnStartReeling?.Invoke();

        // activeBobber / bobberInWater are only set once a fish is on the line or the bobber has
        // landed. When the player resets mid-flight (cast but not yet splashed down) both are null,
        // so fall back to FishingLine's persistent bobber — otherwise there's nothing to arc and the
        // bobber just teleports back on the park.
        BobberController bobberToReelController =
            activeBobber != null ? activeBobber
            : bobberInWater != null ? bobberInWater
            : (fishingLine != null ? fishingLine.ActiveBobber : null);

        // A caught fish is hoisted out of the water to HANG from the line below the rod tip
        // (TP-style showcase) — the silhouette fish stays attached the whole way, so nothing is
        // re-instantiated. The line's park is held off (BeginCatchHold) until the inspection
        // confirms, otherwise OnReelingCompleted below would destroy the hanging fish.
        bool hangCatch = fishToInventory != null && inspectionHandler != null && bobberToReelController != null;
        if (hangCatch)
        {
            inspectionHandler.ConfigureHang(bobberToReelController,
                                            fishingLine != null ? fishingLine.rodTip : null);
            if (fishingLine != null) fishingLine.BeginCatchHold();
        }

        if (bobberToReelController != null)
        {
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null) bobberRb.isKinematic = true;

            // Don't destroy after the arc — FishingLine parks the persistent bobber when
            // OnReelingCompleted fires (or, for a hang catch, when the catch-hold ends).
            if (hangCatch)
            {
                yield return FishingReelInArc.Animate(bobberToReelController.transform,
                                                      inspectionHandler.GetHangPosition, null,
                                                      reelInDuration, reelInArcHeight);
            }
            else if (fishingLine != null && fishingLine.rodTip != null)
            {
                // Empty-line reel: arc the bobber straight to its dangle rest POSE (the point it
                // hangs at, in its upright rotation) rather than to the rod tip. FishingLine then
                // parks it onto the exact same pose, so the hand-off is a no-op — it smooths into
                // the dangle position instead of teleporting down from the rod tip.
                yield return FishingReelInArc.Animate(bobberToReelController.transform,
                                                      fishingLine.GetDangleRestPosition,
                                                      fishingLine.GetDangleRestRotation,
                                                      reelInDuration, reelInArcHeight);
            }
            else
            {
                // No line reference and no fish — fall back to the player/rod transform.
                Transform fallback = playerModel != null ? playerModel : transform;
                yield return FishingReelInArc.Animate(bobberToReelController.transform, fallback,
                                                      reelInDuration, reelInArcHeight);
            }
        }

        HandleReelingCompleted(fishToInventory);
    }

    private void HandleReelingCompleted(CaughtFish fishToInventory)
    {
        Debug.Log($"[FishFight] HandleReelingCompleted — fish: {(fishToInventory != null ? fishToInventory.GetDisplayName() : "NULL")}, inspectionHandler: {(inspectionHandler != null ? "valid" : "NULL")}");

        // FishingLine.ReelInBobberRoutine also fires this, but it can bail out early
        // (yield break when the bobber is destroyed mid-animation by ReelInBobberRoutine here),
        // leaving subscribers like BaitBarUI stuck thinking fishing is still active.
        // Fire it from the one path every reel funnels through so the bait UI never gets
        // locked out after a catch or after reeling back an empty line.
        FishingEvents.OnReelingCompleted?.Invoke();

        if (fishToInventory != null && inspectionHandler != null)
        {
            currentState = FishingState.InspectingCatch;
            // danglingBobber stays hidden — the fish is hanging from the real line right now;
            // ResetFishingState restores it once the inspection is confirmed.
            caughtFishInstance = fishToInventory;
            inspectionHandler.BeginInspection(fishToInventory, playerModel);
        }
        else
        {
            ResetFishingState();
        }
    }

    private void TryFinishInspection()
    {
        if (inspectionHandler == null) return;

        CaughtFish fish;
        if (inspectionHandler.TryFinishInspection(out fish))
        {
            caughtFishInstance = null;
            ResetFishingState();
        }
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
