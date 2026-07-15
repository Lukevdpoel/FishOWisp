using System.Collections;
using UnityEngine;

public partial class PlayerController : MonoBehaviour
{
    // Raised when ball-form visuals should hide (true — while morphing in / flying as the ball) or
    // reappear (false — once the morph-out begins on the bounce). Lets systems that own runtime-
    // spawned visuals (e.g. FishingLine's instantiated bobber + line) toggle themselves, since those
    // can't be wired into hideRenderersDuringJump in the inspector.
    // (System.Action qualified to avoid a using System that would make Random ambiguous below.)
    public static event System.Action<bool> BallJumpVisibilityChanged;
    [Header("Object References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform playerModel;
    [SerializeField] private GameObject normalMeshRoot;

    [Header("Component References")]
    [SerializeField] private PlayerCameraController cameraController;
    [SerializeField] private PlayerFishingAnimHandler fishingAnimHandler;

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float sprintSpeed = 12f;
    [SerializeField] private float acceleration = 25f;
    [SerializeField] private float deceleration = 35f;
    [SerializeField] private float tapKickSpeed = 3f;
    [SerializeField] private float tapKickThreshold = 1.5f;
    [SerializeField] private float rotationSpeed = 15f;
    [SerializeField] private float gravity = -20f;

    [Header("Movement Animations")]
    [SerializeField] private string speedAnimFloat = "Speed";

    [Header("Jump Animation")]
    [SerializeField] private string jumpAnimTrigger = "Jump";

    [Header("Charge Jump")]
    // The jump key lives on the InputBindings asset (keyboardMouse.jump) — read via KeyInput.
    [SerializeField] private float maxChargeTime = 1.0f;
    [SerializeField] private float minLaunchCharge = 0.15f;
    [SerializeField] private AnimationCurve forwardByCharge = new AnimationCurve(new Keyframe(0f, 4f), new Keyframe(1f, 12f));
    [SerializeField] private AnimationCurve upwardByCharge = new AnimationCurve(new Keyframe(0f, 5f), new Keyframe(1f, 10f));
    [SerializeField, Range(0f, 1f)] private float bounceForwardMultiplier = 0.45f;
    [SerializeField, Range(0f, 1f)] private float bounceUpwardMultiplier = 0.5f;
    [Tooltip("Horizontal accel (units/s²) applied toward the move input while airborne. Lets the " +
             "player slightly curve the jump mid-flight; capped at the launch speed so it can't speed up. 0 = off.")]
    [SerializeField] private float airSteerAccel = 6f;
    [Tooltip("Velocity kept after ricocheting off a wall/obstacle mid-jump (1 = perfectly bouncy, 0 = dead stop).")]
    [SerializeField, Range(0f, 1f)] private float wallBounceRestitution = 0.6f;
    [Tooltip("A hit surface counts as a bounceable wall when its normal's Y is below this. Higher (flatter, " +
             "floor-like) surfaces are left to the landing bounce instead. ~0.5 separates walls from ground.")]
    [SerializeField, Range(0f, 1f)] private float wallBounceMaxSurfaceY = 0.5f;
    [Tooltip("Chain jump: tap jump within this many seconds BEFORE a fully-charged jump's landing " +
             "touchdown to relaunch at the same velocity instead of the weak rebound — no re-charging " +
             "needed. Repeatable for a perfect-bounce combo. 0 disables the pre-landing chain.")]
    [SerializeField] private float chainJumpWindow = 0.3f;
    [Tooltip("The tighter window AFTER the bounce: a press this many seconds past touchdown still " +
             "chains. Keep shorter than the pre-landing window so a late press is harder to land. 0 disables it.")]
    [SerializeField] private float chainJumpWindowAfterBounce = 0.15f;
    [Tooltip("Minimum charge (0..1) a jump must reach to be chainable. ~1 means only a fully-held charge counts.")]
    [SerializeField, Range(0f, 1f)] private float chainFullChargeThreshold = 0.95f;

    [Header("Squash & Stretch")]
    [SerializeField, Range(0.1f, 1f)] private float chargeMaxSquash = 0.55f;
    [SerializeField] private float scaleLerpSpeed = 18f;
    [Tooltip("Extra distance (world units) the flattened body sinks toward the ground at full charge, on top " +
             "of keeping its base planted. Raise this if the charged body still floats a little above the " +
             "surface; a small value that lets it clip slightly into the ground usually reads best. Eases in " +
             "with the charge, so 0 = base sits exactly at its resting height.")]
    [SerializeField] private float chargeGroundSink = 0.1f;
    // Cartoon bounce: drives Y-scale over a short window after BounceOnLand. Author this curve
    // so it dips below 1 (squash) then peaks above 1 (stretch) before returning to 1 (neutral).
    [SerializeField] private AnimationCurve bounceImpactCurve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.15f, 0.5f),
        new Keyframe(0.5f, 1.3f),
        new Keyframe(1f, 1f));
    [SerializeField] private float bounceImpactDuration = 0.35f;
    [Tooltip("Overall strength of the landing-bounce squash/stretch. Scales the whole bounce toward " +
             "neutral without redrawing the curve: 1 = full authored curve, 0 = no bounce.")]
    [SerializeField, Range(0f, 1f)] private float bounceImpactIntensity = 0.5f;

    [Header("Jump Velocity Stretch")]
    [Tooltip("Dragon-Quest-slime elastic launch: at takeoff the body stretches along its flight direction " +
             "(front leads, back trails, volume-preserved) scaled by launch speed, then springs back to its " +
             "normal shape during the arc — as if the fast launch stretched it. SoftBodyJiggle adds the wobble.")]
    [SerializeField] private bool enableJumpStretch = true;
    [Tooltip("Launch speed (u/s) that produces the maximum stretch. Set near your full-charge launch speed.")]
    [SerializeField] private float jumpStretchMaxSpeed = 12f;
    [Tooltip("Elongation along the flight axis at max launch speed. 0 = none, 0.6 = +60% length.")]
    [SerializeField, Range(0f, 1.5f)] private float jumpStretchAmount = 0.6f;
    [Tooltip("Spring-back rate (per second): how fast the launch stretch relaxes to the normal shape. " +
             "Higher returns to round earlier in the jump, for a snappier elastic feel.")]
    [SerializeField] private float jumpStretchDecay = 6f;
    [Tooltip("Scales the stretch impulse on the landing bounce relative to the initial launch. " +
             "Lower = gentler stretch when it rebounds off the ground. 1 = same intensity as launch.")]
    [SerializeField, Range(0f, 1f)] private float jumpStretchBounceScale = 0.4f;
    [Tooltip("How fast the stretch AXIS eases toward the flight direction (per second). Lower = smoother, " +
             "kills the mid-air 'ping-pong' wiggle while keeping the forward stretch; higher tracks the arc " +
             "more tightly (raise toward 30+ for near-instant snapping).")]
    [SerializeField] private float jumpStretchDirResponse = 10f;

    [Header("Tucked-Jump Transitions")]
    [SerializeField] private string ballTransitionInTrigger = "BallTransitionIn";
    [SerializeField] private string ballTransitionOutTrigger = "BallTransitionOut";
    [Tooltip("Fired on charge release to advance the held BallMorphIn into BallForm.")]
    [SerializeField] private string ballToFormTrigger = "BallToForm";

    [Header("Airborne Ball Tumble")]
    // While launched in ball form the model tumbles around a random axis. We spin playerModel (the
    // parent of normalMeshRoot, where the body Animator lives), so the rotation rides over the morph
    // clips instead of fighting them. A fresh random axis/speed is rolled at each launch; the model
    // rights itself back upright on the bounce + landing via HandleRotation.
    // Default OFF: the elastic launch stretch reads cleaner facing the travel direction than spinning.
    // Tick it back on to restore the tumble — HandleRotation rights the model upright, and the spin
    // itself lives in PlayerBallTumble (driven from LateUpdate). These fields feed into its TickReset.
    [SerializeField] private bool enableBallTumble = false;
    [SerializeField] private float ballTumbleMinSpeed = 220f; // deg/sec
    [SerializeField] private float ballTumbleMaxSpeed = 540f; // deg/sec
    [Tooltip("Subtrees excluded from the ball-tumble spin pivot's bounds-center calc. Add the fishing " +
             "rod (or anything held off to the side) here so it doesn't drag the pivot off the body center.")]
    [SerializeField] private Transform[] ballPivotExcludeRoots;

    [Header("Jump Arc Look")]
    [Tooltip("While airborne (with the ball tumble off), pitch the model to follow its jump arc — nosing " +
             "up on the way up and down on the way down, like a leaping slime. Flattens to upright on landing.")]
    [SerializeField] private bool enableJumpArcLook = true;
    [Tooltip("Scales how far the model pitches along the arc. 1 = the nose points straight along the velocity; " +
             "lower softens the tilt (0.5 = half), 0 = stays upright.")]
    [SerializeField, Range(0f, 1f)] private float jumpArcPitchScale = 1f;

    [Header("Jump Hidden Renderers")]
    [Tooltip("Optional persistent renderers switched off (renderer.enabled = false) while a charge " +
             "jump is active and back on when it ends — for anything that shouldn't show in ball form. " +
             "The runtime-spawned bobber + fishing line hide themselves via BallJumpVisibilityChanged, " +
             "so they don't need to be listed here. GameObjects stay active; only rendering toggles.")]
    [SerializeField] private Renderer[] hideRenderersDuringJump;
    [Tooltip("Optional sprite hidden ONLY while the jump charge is held, reappearing the moment the " +
             "charge is released — unlike the array above, which stays hidden through the whole flight. " +
             "Also reappears if the charge is cancelled (menu opened, controls locked).")]
    [SerializeField] private SpriteRenderer hideSpriteWhileCharging;

    [Header("Idle Animation")]
    [SerializeField] private string sitdownAnimTrigger = "Sitdown";
    [SerializeField] private float sitDownHoldTime = 4f;

    [Header("Static Camera")]
    [SerializeField] private bool useStaticCamera = false;
    [SerializeField] private Transform staticCameraTarget;

    [Header("Physics")]
    [SerializeField] private float groundedDownForce = -2f;
    [SerializeField] private float speedAnimDampTime = 0.1f;

    [HideInInspector] public bool areControlsLocked = false;

    // While true, LateUpdate skips driving the gameplay camera, but Update still runs (gravity,
    // settling, animation). The main menu sets this during the Cinemachine menu->player blend so the
    // freshly-enabled player can settle onto the ground off-camera WITHOUT its per-frame camera
    // writes fighting the CinemachineBrain that owns the camera during the blend. Cleared at handoff.
    [HideInInspector] public bool suppressCameraControl = false;

    // True once the orbit camera pose has been seeded — either by the menu handoff's
    // ReinitializeCamera (at the title "press to play") or by Start's default Initialize. Guards
    // against Start re-seeding (and clobbering) a pose the menu handoff already set, since the
    // player is enabled at the press and this component's Start therefore runs after that handoff.
    private bool cameraSeeded;

    public bool IsSprinting => isSprinting;

    // True only when the player is firmly on the ground and not in any phase of a charge jump.
    // Actions that shouldn't begin in midair (e.g. casting the fishing rod) gate on this.
    public bool IsGrounded =>
        characterController != null
        && characterController.isGrounded
        && chargeJump.Phase == ChargeJumpController.JumpPhase.None;

    // True during any phase of a charge jump — winding up the charge, launched, or rebounding.
    // Actions that shouldn't be reachable mid-jump (e.g. opening the gear/bait menu) gate on this.
    public bool IsJumping => chargeJump.Phase != ChargeJumpController.JumpPhase.None;

    // True only while winding up the charge (the phase where the body is squashed flat and widened,
    // before launch). SoftBodyJiggle gates its ground-conform pass on this, so the drape only runs
    // while the flattened disc is actually sitting on the ground. Charging always starts grounded.
    public bool IsChargingJump => chargeJump.Phase == ChargeJumpController.JumpPhase.Charging;

    // Static mirror of IsJumping for world interactables that hold no player reference
    // (signposts, NPCs, the bounty board, the vendor). Updated every frame in Update and
    // cleared in OnDisable so a suspended player (title sequence) can't leave it stuck true.
    public static bool IsPlayerJumping { get; private set; }

    private CharacterController characterController;
    private Vector3 targetVelocity;
    private float idleTimer;
    private bool allowSitdown = true;
    private bool isSprinting = false;
    private bool hadMovementInputLast;
    private Quaternion targetModelRotation;
    private bool isLockedOnFish;
    private Transform fishLockTarget;

    private int hashSpeed;
    private int hashJump;
    private int hashSitdown;
    private int hashBallIn;
    private int hashBallOut;
    private int hashBallToForm;

    private readonly PlayerSquashStretch squashStretch = new PlayerSquashStretch();
    private readonly ChargeJumpController chargeJump = new ChargeJumpController();
    private readonly PlayerBallTumble ballTumble = new PlayerBallTumble();

    private bool jumpVisualsHidden;

    // Armed while any menu/lock disables input; only cleared once the jump button is seen fully
    // released afterwards. Stops the press (or hold) that closed a menu from doubling as a jump
    // on the exit frame. Also armed in OnEnable so the "press any button" title handoff can't
    // carry a held jump button into a charge.
    private bool jumpBlockedUntilRelease;

    // Last frame's jump phase, so LateUpdate can fire the one-shot stretch impulse on the launch/bounce edge.
    private ChargeJumpController.JumpPhase prevJumpPhase = ChargeJumpController.JumpPhase.None;

    private Vector3 staticCameraOffset;
    private bool staticCameraOffsetCaptured;

    void Start()
    {
        hashSpeed = Animator.StringToHash(speedAnimFloat);
        hashJump = Animator.StringToHash(jumpAnimTrigger);
        hashSitdown = Animator.StringToHash(sitdownAnimTrigger);
        hashBallIn = Animator.StringToHash(ballTransitionInTrigger);
        hashBallOut = Animator.StringToHash(ballTransitionOutTrigger);
        hashBallToForm = Animator.StringToHash(ballToFormTrigger);

        characterController = GetComponent<CharacterController>();
        if (playerModel) targetModelRotation = playerModel.rotation;

        squashStretch.Init(playerModel, normalMeshRoot);
        chargeJump.Init(characterController, animator, hashBallIn, hashBallOut, hashBallToForm,
                        squashStretch, NotifyOfAction);

        // Caches the in-air tumble's bounds-center pivot renderers, excluding any ballPivotExcludeRoots
        // subtrees (e.g. the held fishing rod) so they can't pull the pivot off the body's center.
        ballTumble.Init(playerModel, transform, normalMeshRoot.transform, ballPivotExcludeRoots);

        // Skip if the camera was already seeded by the menu handoff (ReinitializeCamera). The player
        // is enabled at the title's "press to play", so this Start runs AFTER that single-move
        // handoff has seeded the orbit pose — re-seeding here from the prefab camera would clobber it.
        if (cameraController != null && !cameraSeeded) { cameraController.Initialize(playerModel); cameraSeeded = true; }
    }

    void Update()
    {
        IsPlayerJumping = IsJumping;

        if (isDriven)
        {
            HandleDrive();
            HandleGravity();
            HandleCursorLocking();
            if (characterController != null && characterController.enabled)
            characterController.Move(targetVelocity * Time.deltaTime);
            return;
        }

        TickChargeJump();
        UpdateJumpVisuals();
        HandleMovement();
        HandleRotation();
        HandleGravity();
        HandleAnimation();
        HandleIdleAnimation();
        HandleCursorLocking();
        if (characterController != null && characterController.enabled)
            characterController.Move(targetVelocity * Time.deltaTime);
    }

    void OnEnable()
    {
        jumpBlockedUntilRelease = true;
    }

    void OnDisable()
    {
        IsPlayerJumping = false;
    }

    void LateUpdate()
    {
        if (Time.timeScale == 0f || Time.deltaTime <= 0.0001f) return;

        // Run after Animator has evaluated so our scale/rotation writes aren't overwritten by clip curves.
        squashStretch.Tick(
            chargeJump.Phase == ChargeJumpController.JumpPhase.Charging,
            chargeJump.ChargeNormalized(maxChargeTime),
            chargeMaxSquash, scaleLerpSpeed,
            bounceImpactCurve, bounceImpactDuration, bounceImpactIntensity,
            chargeGroundSink);

        // Elastic slime stretch: a launch (or bounce) fires a one-shot stretch impulse scaled by launch
        // speed, which then springs back to round during the arc. SoftBodyJiggle adds the secondary wobble.
        // Detecting the phase edge here works because TickChargeJump (in Update) already advanced the phase
        // and set targetVelocity for this frame.
        var jumpPhase = chargeJump.Phase;
        bool launchEdge = jumpPhase == ChargeJumpController.JumpPhase.Launched
                          && prevJumpPhase != ChargeJumpController.JumpPhase.Launched;
        bool bounceEdge = jumpPhase == ChargeJumpController.JumpPhase.Bounced
                          && prevJumpPhase != ChargeJumpController.JumpPhase.Bounced;
        // A chain relaunch (Launched→Launched) has no phase edge, so the controller flags it directly.
        // Consume it unconditionally so the one-shot can't leak into a later frame.
        bool chainEdge = chargeJump.ConsumeChainStretch();
        if (enableJumpStretch && (launchEdge || bounceEdge || chainEdge))
        {
            float impulse = targetVelocity.magnitude / Mathf.Max(0.01f, jumpStretchMaxSpeed);
            // Gentler stretch on a landing rebound or a chain relaunch, so the bounce-impact squash
            // (fired by ChargeJumpController) still reads instead of being washed out by a full elongation.
            if (bounceEdge || chainEdge) impulse *= jumpStretchBounceScale;
            // Seed the stretch axis to the launch direction so it starts aligned; the ease in TickJumpStretch
            // then only smooths mid-air jitter, not this initial big rotation off the stale held axis.
            squashStretch.InjectJumpStretch(impulse, targetVelocity);
        }
        prevJumpPhase = jumpPhase;
        squashStretch.TickJumpStretch(targetVelocity, jumpStretchAmount, jumpStretchDecay, jumpStretchDirResponse);

        // Snap the model to its rest pose (if tumbling) BEFORE the camera reads playerModel.position,
        // so the camera follows the stable body point. The spin itself is applied after the camera.
        bool tumbling = ballTumble.TickReset(
            enableBallTumble,
            chargeJump.Phase == ChargeJumpController.JumpPhase.Launched,
            ballTumbleMinSpeed, ballTumbleMaxSpeed, rotationSpeed);

        HandleStaticCameraFollow();

        if (!suppressCameraControl && cameraController != null)
        {
            var input = new PlayerCameraController.CameraInput
            {
                playerModel = playerModel,
                areControlsLocked = areControlsLocked,
                isFightingFish = fishingAnimHandler != null && fishingAnimHandler.IsFightingFish,
                isBountyBoardActive = fishingAnimHandler != null && fishingAnimHandler.IsBountyBoardActive,
                isAiming = fishingAnimHandler != null && fishingAnimHandler.IsAiming,
                activeBountyBoard = fishingAnimHandler != null ? fishingAnimHandler.ActiveBountyBoard : null,
                activeBobberTransform = fishingAnimHandler != null ? fishingAnimHandler.ActiveBobberTransform : null,
                isSprinting = isSprinting,
            };
            cameraController.UpdateCamera(input);
        }

        // Apply the centered spin last — purely visual, so it never drags the camera pivot.
        if (tumbling) ballTumble.ApplySpin();
    }

    public void LockControls(bool locked)
    {
        areControlsLocked = locked;
        if (locked) ZeroMovement();
    }

    public void ZeroMovement()
    {
        targetVelocity = Vector3.zero;
        if (animator) animator.SetFloat(hashSpeed, 0f);
    }

    public void NotifyOfAction() { idleTimer = 0f; allowSitdown = true; }

    // Unity message: fires for every CharacterController contact during Move(). While airborne in
    // a charge jump, ChargeJumpController reflects the velocity off wall/overhang hits so the
    // player ricochets like a ball; floor contacts are ignored here and handled by the landing bounce.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        var cfg = BuildJumpConfig();
        chargeJump.HandleColliderHit(hit.normal, ref targetVelocity, in cfg);
    }

    private void HandleCursorLocking()
    {
        // Mouse-driven UIs own the cursor while open — they unlock it themselves, and
        // re-locking here every frame would make their buttons unclickable.
        if (InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen) return;

        // Menu -> gameplay handoff: the player is enabled but locked while the title's camera ease
        // runs (IsTitleSequenceActive stays true until MainMenuController.FinishStart; the menu's
        // fallback path additionally sets suppressCameraControl while the brain still owns the
        // camera). Without this, the generic areControlsLocked branch below would force the cursor
        // VISIBLE every frame, fighting ControllerCursorHider (which hides it on a pad) — the
        // pointer flickered across the whole transition. Keep it hidden + locked until handoff.
        if (suppressCameraControl || MainMenuController.IsTitleSequenceActive)
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
            return;
        }

        // Fishing owns the view for its whole loop — aiming, charging, the bobber-cam wait, the
        // fight (mouse drives rod direction), and the catch showcase. Keep the cursor hard-LOCKED
        // (hidden AND confined to the game window) the entire time so it never flashes on screen
        // and can't be dragged onto a second monitor. This must win over the generic
        // areControlsLocked branch below, which intentionally shows the cursor for genuine mouse
        // UIs (dialogue, shop, bounty board).
        bool fishingOwnsView =
            (cameraController != null && (cameraController.IsBobberCameraActive || cameraController.IsCatchCameraActive))
            || (fishingAnimHandler != null && (fishingAnimHandler.IsFightingFish
                                               || fishingAnimHandler.IsCasting
                                               || fishingAnimHandler.IsAiming
                                               || fishingAnimHandler.IsReeling));
        if (fishingOwnsView)
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
            return;
        }

        if (areControlsLocked)
        {
            // Non-fishing locked contexts (dialogue, shop, inspection menus) want a usable cursor —
            // but only on the mouse. On a pad these are navigated with the stick/d-pad, and forcing
            // the cursor VISIBLE every frame here just fights ControllerCursorHider's LateUpdate hide,
            // flickering the pointer (same Update-vs-LateUpdate fight as the suppressCameraControl case
            // above). Leave it hidden on a pad so the watchdog has nothing to undo; bumping the mouse
            // flips IsGamepadActive false and reveals it next frame.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !GamepadInput.IsGamepadActive;
            return;
        }

        if (Time.timeScale > 0f) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }
}
