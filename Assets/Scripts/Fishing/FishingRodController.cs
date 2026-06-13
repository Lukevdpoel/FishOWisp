using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

public class FishingRodController : MonoBehaviour
{
    public enum FishingState { Idle, Charging, WaitingForBite, LureReeling, FishOnTheLine, FightingFish, Reeling, InspectingCatch, Cooldown }

    [Header("State")]
    [SerializeField] public FishingState currentState = FishingState.Idle;

    [Header("Object References")]
    public GameObject danglingBobber;
    public Transform playerModel;
    public DirectionalFishingMinigame minigameUI;

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

    [Header("Fish Fight Settings")]
    public float maxFightProgress = 40f;
    public float initialFightProgress = 15f;
    public float fallbackFightProgressRate = 10f;

    [Header("Reel-In Pull")]
    [Tooltip("Horizontal distance from the player at which the fish counts as caught (once the minimum reel meter is also full). Player height doesn't matter — only XZ distance is checked.")]
    public float catchDistance = 3.0f;
    [Tooltip("How fast (m/s) the bobber is pulled horizontally toward the player while the player holds reel during a rest phase.")]
    public float reelPullSpeed = 2.0f;
    [Tooltip("Pulling stops once the bobber is this far INSIDE the catch radius — guarantees the catch check passes instead of stalling on the boundary.")]
    public float pullStopInset = 0.2f;

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
    };

    private Coroutine fishFightCoroutine;
    private Coroutine fishEscapeCoroutine;
    // True while a fish is gripping the lure and the player's reaction window is open. The real
    // fish (FishRipple) owns the timeout; the rod just routes the hook press.
    private bool isLureGrabActive;
    private BobberController bobberInWater;
    private BobberController activeBobber;
    private InventoryUI cachedInventoryUI;
    private FishingLine fishingLine;

    private float currentFightProgress;
    private CaughtFish caughtFishInstance = null;
    private PlayerController playerController;
    private bool wasReelingLastFrame;
    private bool isAiming;
    // Tracks the RT pressure-charge bracket frame-to-frame so we can detect the press edge
    // (start charging) and the release edge (cast) without a separate Input System press point.
    private bool throwHeldLast;
    private Vector3 castOriginPosition;
    private float castStartTime;

    private int hashFail;

    private readonly LureReelHandler lureReel = new LureReelHandler();

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        if (playerController == null) playerController = GetComponentInParent<PlayerController>();
        if (playerController == null) Debug.LogError("FishingRodController: PlayerController missing!");

        hashFail = Animator.StringToHash(failAnimationTrigger);

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
    // fixedDeltaTime, so the handler's decay math needs no change.
    private void FixedUpdate()
    {
        TickLure();
    }

    private void TickLure()
    {
        if (currentState != FishingState.LureReeling) return;
        if (bobberInWater == null) return;

        // The notebook owns LMB and RT (page flip) while open; without this gate cranking
        // continues underneath it. Both reads are state (not edge), so they're FixedUpdate-safe.
        bool inputBlocked = NoteMenu.IsNotebookOpen || Time.timeScale == 0f;
        bool reelHeld = !inputBlocked
            && (Input.GetKey(KeyCode.Mouse0) || GamepadInput.LureReelHeld);
        float lateral = inputBlocked
            ? 0f
            : Mathf.Clamp(Input.GetAxisRaw("Horizontal") + GamepadInput.Move.x, -1f, 1f);

        LureReelHandler.Outcome outcome = lureReel.Tick(
            bobberInWater, playerModel, reelHeld, lateral, in lureSettings);

        // Bites no longer come from here — the LureBiteBrain (per FishingZone) drives a real
        // fish to strike and calls BobberController.HookFish, which lands in HandleFishBite.
        if (outcome == LureReelHandler.Outcome.RetractRequested)
        {
            Debug.Log("[Lure] Reached catch range without a bite — retracting.");
            currentState = FishingState.Reeling;
            StartCoroutine(ReelInBobberRoutine(null));
        }
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
        // path pops the inventory over the notebook.
        if (NoteMenu.IsNotebookOpen)
        {
            StopAiming();
            return;
        }

        // External locks (dialogue, shop, cutscene) block starting fishing — but once a flow is
        // in progress we hold the movement lock ourselves and must keep reading our own inputs.
        if (currentState == FishingState.Idle
            && playerController != null && playerController.areControlsLocked) return;

        // --- Aim Mode (RMB / LB) - works in any non-fight/non-inspection state ---
        if ((Input.GetKeyDown(KeyCode.Mouse1) || GamepadInput.AimPressed) && !isAiming)
        {
            isAiming = true;
            FishingEvents.OnStartAiming?.Invoke();
        }

        if ((Input.GetKeyUp(KeyCode.Mouse1) || GamepadInput.AimReleased) && isAiming)
        {
            StopAiming();
        }

        // --- LMB actions (keyboard/mouse) ---
        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            switch (currentState)
            {
                case FishingState.Idle:
                    StartCharging();
                    break;
                case FishingState.WaitingForBite:
                    FishingEvents.OnAttractFish?.Invoke();
                    break;
                case FishingState.FishOnTheLine:
                    if (isLureGrabActive) ConfirmLureHook();
                    else HookFishAndStartFight();
                    break;
                case FishingState.InspectingCatch:
                    TryFinishInspection();
                    break;
            }
        }

        // --- RT: hold to charge. RodCasting maps the trigger pressure to throw force live, so
        // the player dials distance up or down by how hard they hold RT. Press A (below) while
        // still holding to cast at the dialed charge; letting RT go fully cancels the cast and
        // resets to idle. Only armed from Idle/Charging so RT keeps its post-cast meanings
        // (attract, lure crank, fight reel) without cross-firing here. ---
        bool throwHeld = GamepadInput.ThrowHeld;
        if (currentState == FishingState.Idle && throwHeld && !throwHeldLast)
        {
            StartCharging();
        }
        else if (currentState == FishingState.Charging && throwHeldLast && !throwHeld
                 && !GamepadInput.ConfirmPressed)
        {
            // Let go of the trigger without committing → cancel the charge back to idle. The
            // ConfirmPressed guard lets an A press landing on the same frame as the release win,
            // so thumbing A as your finger leaves the trigger still throws instead of cancelling.
            FishingEvents.OnCancelFishing?.Invoke();
            FishingEvents.OnChargeCanceled?.Invoke();  // snap the cast animation back to idle at once
        }
        throwHeldLast = throwHeld;

        // --- RB: reset a cast already in the water (bobber or lure alike). ---
        if (GamepadInput.ResetCastPressed
            && (currentState == FishingState.WaitingForBite || currentState == FishingState.LureReeling))
        {
            ReelLineBack();
        }

        // --- RT tap: attract fish toward the bobber while waiting for a bite. ---
        if (GamepadInput.AttractPressed && currentState == FishingState.WaitingForBite)
        {
            FishingEvents.OnAttractFish?.Invoke();
        }

        // --- A: cast the held charge, hook a biting fish, or confirm/finish catch inspection. ---
        if (GamepadInput.ConfirmPressed)
        {
            switch (currentState)
            {
                case FishingState.Charging:
                    FishingEvents.OnCancelCharging?.Invoke();  // commit the throw at the current charge
                    break;
                case FishingState.FishOnTheLine:
                    if (isLureGrabActive) ConfirmLureHook();
                    else HookFishAndStartFight();
                    break;
                case FishingState.InspectingCatch:
                    TryFinishInspection();
                    break;
            }
        }

        // --- Release the keyboard charge (LMB up) to cast. ---
        if (Input.GetKeyUp(KeyCode.Mouse0) && currentState == FishingState.Charging)
        {
            FishingEvents.OnCancelCharging?.Invoke();
        }

        // --- Instant reset (E key) — keyboard mirror of the RB cast reset. The reel-in arc is
        // kinematic and flies over geometry, so this doubles as the escape hatch when the lure
        // snags behind terrain during the physics reel. ---
        if (Input.GetKeyDown(KeyCode.E)
            && (currentState == FishingState.WaitingForBite || currentState == FishingState.LureReeling))
        {
            ReelLineBack();
        }
    }

    private void StopAiming()
    {
        if (!isAiming) return;
        isAiming = false;
        FishingEvents.OnStopAiming?.Invoke();
    }

    private void ReelLineBack()
    {
        Debug.Log("Reeling back the line.");
        currentState = FishingState.Reeling;
        StartCoroutine(ReelInBobberRoutine(null));
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
        if (bobberInWater != null || activeBobber != null)
        {
            Debug.Log($"[FishFight] StartCharging BLOCKED — bobberInWater: {(bobberInWater != null ? "exists" : "null")}, activeBobber: {(activeBobber != null ? "exists" : "null")}");
            return;
        }
        // Lures don't use bait — skip the bait gate entirely. Regular bobbers still require it.
        if (!BobberInventory.IsLureEquipped
            && BaitInventory.Instance != null && BaitInventory.Instance.SelectedBait == null)
        {
            Debug.Log("[FishFight] StartCharging BLOCKED — no bait equipped. Opening inventory so the player can pick one.");
            InventoryUI inv = GetInventoryUI();
            if (inv != null)
            {
                if (!InventoryUI.IsInventoryOpen) inv.OpenInventory();
            }
            else
            {
                Debug.LogError("[FishFight] No InventoryUI resolvable — bait-missing prompt skipped. Should not happen after the GenericSingleton ghost-spawn fix; if you see this, the InventoryUI's GameObject is genuinely missing from the scene.");
            }
            return;
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
        activeBobber = null;
        currentState = FishingState.LureReeling;
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
        if (minigameUI != null) minigameUI.Deactivate();
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
        FishingEvents.OnHookFishSuccess?.Invoke(); currentState = FishingState.FightingFish; currentFightProgress = initialFightProgress;
        if (activeBobber != null) { Rigidbody rb = activeBobber.GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = false; activeBobber.SetStruggleActive(true); } activeBobber.SetPlayerTransform(transform); }
        if (minigameUI != null) { minigameUI.Activate(); if (activeBobber != null) minigameUI.SetTrackingTarget(activeBobber.transform); }
        else { Debug.LogWarning("FishingRodController: minigameUI is NULL — fish fight will auto-fill! Reassign the DirectionalFishingMinigame reference in the Inspector."); }
        FishingEvents.OnFishFightBegin?.Invoke(activeBobber.HookedFish.preset); fishFightCoroutine = StartCoroutine(FishFightRoutine());
    }
    private void WinFishFight()
    {
        Debug.Log($"[FishFight] WinFishFight — activeBobber: {(activeBobber != null ? "valid" : "NULL")}, inspectionHandler: {(inspectionHandler != null ? "valid" : "NULL")}");
        currentState = FishingState.Reeling;
        if (playerController != null) playerController.UnlockFromFish();
        if (fishFightCoroutine != null) StopCoroutine(fishFightCoroutine);
        if (activeBobber != null) activeBobber.SetStruggleActive(false);
        if (minigameUI != null) minigameUI.Deactivate();
        caughtFishInstance = activeBobber.HookedFish;
        FishingEvents.OnFishFightEnd?.Invoke(true);
        if (activeBobber != null) activeBobber.SwapBobberForFishModel();

        StartCoroutine(ReelInBobberRoutine(caughtFishInstance));
    }
    private IEnumerator FishFightRoutine()
    {
        float lastProgress = currentFightProgress;
        wasReelingLastFrame = false;
        while (currentFightProgress > 0)
        {
            if (minigameUI != null)
            {
                if (activeBobber != null)
                {
                    minigameUI.SetFishDirectionFromVector(activeBobber.StruggleDirection);
                    activeBobber.SetStruggleActive(!minigameUI.IsResting);
                }
                currentFightProgress = minigameUI.UpdateMinigame(currentFightProgress, maxFightProgress);
            }
            else { currentFightProgress += Time.deltaTime * fallbackFightProgressRate; }

            bool isReelingNow = minigameUI != null ? minigameUI.IsReeling : currentFightProgress > lastProgress;
            if (isReelingNow != wasReelingLastFrame)
            {
                if (isReelingNow) FishingEvents.OnStartReelingDuringFight?.Invoke();
                else FishingEvents.OnStopReelingDuringFight?.Invoke();
                wasReelingLastFrame = isReelingNow;
            }

            // Physically pull the bobber toward the player while reeling — but only when the bobber
            // is still in water and not already inside the catch radius. That keeps the fish from
            // being dragged onto shore visually and from over-shooting into the player.
            if (isReelingNow) FishingBobberPull.PullToward(activeBobber, playerModel, reelPullSpeed, catchDistance, pullStopInset);

            lastProgress = currentFightProgress;
            FishingEvents.OnFishFightProgressUpdate?.Invoke(currentFightProgress, maxFightProgress);

            // Catch fires only when both gates pass: hidden minimum reel meter is full AND the
            // bobber is physically close enough. Close-hooked fish still need real reel time.
            if (currentFightProgress >= maxFightProgress
                && FishingBobberPull.IsWithinCatchRange(activeBobber, playerModel, catchDistance))
            {
                break;
            }

            yield return null;
        }
        FishingEvents.OnStopReelingDuringFight?.Invoke();
        if (currentFightProgress >= maxFightProgress
            && FishingBobberPull.IsWithinCatchRange(activeBobber, playerModel, catchDistance))
        {
            WinFishFight();
        }
        else
        {
            if (minigameUI != null) minigameUI.Deactivate();
            StartCoroutine(FailRoutine());
        }
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

        BobberController bobberToReelController = activeBobber != null ? activeBobber : bobberInWater;

        Transform reelTarget = null;
        if (fishToInventory != null && inspectionHandler != null)
        {
            Transform holdPoint = inspectionHandler.GetFishHoldPoint();
            if (holdPoint != null) reelTarget = holdPoint;
        }
        // Empty-line reel: arc straight back to the rod tip so the bobber lands at its dangling
        // position. Targeting playerModel here used to drag it to the player and then the parking
        // teleport in FishingLine snapped it to the rod — visible as a two-stage motion.
        if (reelTarget == null && fishingLine != null && fishingLine.rodTip != null)
        {
            reelTarget = fishingLine.rodTip;
        }
        if (reelTarget == null) reelTarget = playerModel;
        if (reelTarget == null) reelTarget = transform;

        if (bobberToReelController != null)
        {
            bobberToReelController.enabled = false;
            Rigidbody bobberRb = bobberToReelController.GetComponent<Rigidbody>();
            if (bobberRb != null) bobberRb.isKinematic = true;

            // Don't destroy after the arc — FishingLine parks the persistent bobber when
            // OnReelingCompleted fires.
            yield return FishingReelInArc.Animate(bobberToReelController.transform, reelTarget,
                                                  reelInDuration, reelInArcHeight);
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
            danglingBobber?.SetActive(true);
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

        // Never leave the grab flag hanging across a reset/cancel.
        isLureGrabActive = false;

        if (playerController != null)
        {
            playerController.SetCatchCamera(false);
            playerController.areControlsLocked = false;
            playerController.UnlockFromFish();
        }

        if (inspectionHandler != null) inspectionHandler.ForceCleanup();

        StopAiming();
        currentState = FishingState.Idle;
        danglingBobber?.SetActive(true);
        fishFightCoroutine = null;
        fishEscapeCoroutine = null;
        activeBobber = null;
        bobberInWater = null;
        caughtFishInstance = null;
        if (minigameUI != null) minigameUI.Deactivate();
    }
}
