using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
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
    [SerializeField] private KeyCode chargeJumpKey = KeyCode.Space;
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

    // Re-seeds the orbit camera from the gameplay camera's CURRENT transform. Used by the main-menu
    // handoff: after a Cinemachine blend leaves the camera at an over-the-shoulder pose, this lets
    // PlayerCameraController pick up smoothly from exactly that pose instead of snapping. Mirrors the
    // Initialize() call in Start(), so it's safe to call before this component's own Start has run.
    public void ReinitializeCamera()
    {
        // measureFromPivot:true so the orbit pose reconstructs exactly onto the current (blended)
        // camera pose instead of pulling back by the origin->pivot offset. See Initialize().
        if (cameraController != null) cameraController.Initialize(playerModel, true);
        cameraSeeded = true;
    }

    // Seeds the resting orbit pose for the title single-move handoff from the PlayerFollow vcam's
    // world pose (angles from its rotation, so the camera lands behind the player regardless of the
    // player's still-settling position). Marks the camera seeded so Start won't re-seed over it.
    public void SeedCameraFromMenuVcam(Vector3 vcamPos, Quaternion vcamRot)
    {
        if (cameraController != null) cameraController.SeedRestingPoseFromMenuVcam(playerModel, vcamPos, vcamRot);
        cameraSeeded = true;
    }

    // Eases the camera from its current pose into the live orbit pose over `duration` seconds.
    // The main menu calls this right after ReinitializeCamera so the takeover from the Cinemachine
    // blend doesn't snap. Call while the camera still sits at the blend's end pose.
    public void BeginCameraHandoffBlend(float duration)
    {
        if (cameraController != null) cameraController.BeginHandoffBlend(duration);
    }

    public void ZeroMovement()
    {
        targetVelocity = Vector3.zero;
        if (animator) animator.SetFloat(hashSpeed, 0f);
    }

    public void NotifyOfAction() { idleTimer = 0f; allowSitdown = true; }

    public void LockOnFish(Transform target)
    {
        isLockedOnFish = true;
        fishLockTarget = target;
        ZeroMovement();
    }

    public void UnlockFromFish()
    {
        isLockedOnFish = false;
        fishLockTarget = null;
    }

    public void SetCatchCamera(bool active)
    {
        if (cameraController != null) cameraController.SetCatchCamera(active);
    }

    // Forces the static-camera follow code to recompute its framing offset on the next LateUpdate.
    // Call before re-entering a shop so framing doesn't carry over from a previous visit.
    public void ResetStaticCameraOffset()
    {
        staticCameraOffsetCaptured = false;
    }

    // followPlayer=true keeps the legacy behavior (camera glides at a captured offset from the player).
    // followPlayer=false leaves the camera frozen wherever it was placed in the editor — used for shop
    // interiors where we want a truly fixed shot. Input is still remapped to this camera's forward/right
    // regardless, which is what gives WASD the 2D-ish plane.
    public void SetStaticCamera(bool active, Transform target, bool followPlayer = true)
    {
        useStaticCamera = active;
        staticCameraTarget = target;
        staticCameraFollowsPlayer = followPlayer;
        staticCameraOffsetCaptured = false;
    }

    // The transform of the camera the player controller drives — the one PlayerCameraController
    // snaps back to after a UI panel releases the view. UI that temporarily hijacks Camera.main
    // (e.g. the loadout framing camera) compares against this so it never grabs a fixed camera
    // (like a shop interior view) that nothing would restore, leaving it stranded mid-zoom.
    public Transform ActivePlayerCameraTransform =>
        cameraController != null ? cameraController.CameraTransform : null;

    private bool staticCameraFollowsPlayer = true;

    // External-drive mode: while active, HandleMovement is bypassed and the player walks toward driveTarget.
    // IsDriveComplete becomes true once within arriveDistance. Caller should LockControls(true) before
    // starting and LockControls(false) (+ StopDrive) after.
    public void StartDrive(Vector3 worldTarget, float speed, float arriveDistance = 0.15f)
    {
        isDriven = true;
        driveTarget = worldTarget;
        driveSpeed = speed;
        driveArriveDistance = arriveDistance;
        isDriveComplete = false;
    }

    public void StopDrive()
    {
        isDriven = false;
        isDriveComplete = false;
        targetVelocity = new Vector3(0f, targetVelocity.y, 0f);
    }

    public bool IsDriveComplete => isDriveComplete;

    private bool isDriven;
    private Vector3 driveTarget;
    private float driveSpeed;
    private float driveArriveDistance;
    private bool isDriveComplete;

    // Smoothly lerps the player to worldTarget over duration with the CharacterController disabled,
    // so the player can't snag on geometry. Used to reposition the player to a clean start point
    // (just inside / just outside the door) before walking through the scripted path.
    public Coroutine StartGlide(Vector3 worldTarget, float duration, Quaternion? faceRotation = null)
    {
        return StartCoroutine(GlideRoutine(worldTarget, duration, faceRotation));
    }

    private IEnumerator GlideRoutine(Vector3 worldTarget, float duration, Quaternion? faceRotation)
    {
        if (characterController != null) characterController.enabled = false;

        Vector3 startPos = transform.position;
        Quaternion startRot = playerModel ? playerModel.rotation : Quaternion.identity;
        Quaternion targetRot = faceRotation ?? startRot;

        if (animator) animator.SetFloat(hashSpeed, 0f);
        targetVelocity = Vector3.zero;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            transform.position = Vector3.Lerp(startPos, worldTarget, k);
            if (playerModel) playerModel.rotation = Quaternion.Slerp(startRot, targetRot, k);
            yield return null;
        }
        transform.position = worldTarget;
        if (playerModel) playerModel.rotation = targetRot;
        targetModelRotation = targetRot;

        if (characterController != null) characterController.enabled = true;
    }

    private void HandleDrive()
    {
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(driveTarget.x, 0f, driveTarget.z);
        Vector3 toTarget = flatTarget - flatPos;
        float dist = toTarget.magnitude;

        if (dist <= driveArriveDistance)
        {
            targetVelocity = new Vector3(0f, targetVelocity.y, 0f);
            if (animator) animator.SetFloat(hashSpeed, 0f, speedAnimDampTime, Time.deltaTime);
            isDriveComplete = true;
            return;
        }

        Vector3 dir = toTarget / dist;
        targetVelocity = new Vector3(dir.x * driveSpeed, targetVelocity.y, dir.z * driveSpeed);

        Quaternion lookRot = Quaternion.LookRotation(dir);
        if (playerModel)
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, lookRot, rotationSpeed * Time.deltaTime);
        targetModelRotation = lookRot;

        if (animator) animator.SetFloat(hashSpeed, driveSpeed, speedAnimDampTime, Time.deltaTime);
    }

    private void HandleStaticCameraFollow()
    {
        if (!useStaticCamera || staticCameraTarget == null) return;
        if (!staticCameraFollowsPlayer) return;

        if (!staticCameraOffsetCaptured)
        {
            // Compute an offset that places the player at the camera's forward-ray hit on the
            // player's Y plane. This keeps the player centered on screen regardless of where
            // they spawn, instead of preserving whatever framing existed at scene start.
            Vector3 camPos = staticCameraTarget.position;
            Vector3 camForward = staticCameraTarget.forward;
            float fy = camForward.y;
            if (Mathf.Abs(fy) > 1e-4f)
            {
                float t = (transform.position.y - camPos.y) / fy;
                staticCameraOffset = -camForward * t;
            }
            else
            {
                staticCameraOffset = camPos - transform.position;
            }
            staticCameraOffsetCaptured = true;
        }
        staticCameraTarget.position = transform.position + staticCameraOffset;
    }

    private void HandleMovement()
    {
        float yVelocity = targetVelocity.y;

        var jumpPhase = chargeJump.Phase;
        if (jumpPhase == ChargeJumpController.JumpPhase.Charging)
        {
            // Locked in place while charging — bleed off any horizontal momentum. The player can
            // still aim, though: HandleRotation steers the model from the move input, and the
            // launch fires along the model's forward.
            Vector3 horiz = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            horiz = Vector3.MoveTowards(horiz, Vector3.zero, deceleration * Time.deltaTime);
            targetVelocity = new Vector3(horiz.x, yVelocity, horiz.z);
            return;
        }

        if (jumpPhase == ChargeJumpController.JumpPhase.Launched || jumpPhase == ChargeJumpController.JumpPhase.Bounced)
        {
            // Trajectory mostly locked once launched — gravity still applies via HandleGravity,
            // and ChargeJumpController.Tick applies the slight air steering.
            return;
        }

        Vector3 moveDirection = GetMoveDirection(out float inputMagnitude);
        bool hasMovementInput = inputMagnitude > 0.1f;

        // Sprint is hold-to-run on both devices: keep Shift (keyboard) or the bound pad
        // control held to turn walking into running, release it to drop back to a walk.
        // (No need to gate on inputDisabled — GetMoveDirection already zeroes input then.)
        isSprinting = hasMovementInput
                      && (Input.GetKey(KeyCode.LeftShift) || GamepadInput.SprintHeld);

        float currentSpeed = (isSprinting ? sprintSpeed : walkSpeed) * Mathf.Clamp01(inputMagnitude);
        Vector3 desiredHorizontal = moveDirection * currentSpeed;

        Vector3 currentHorizontal = new Vector3(targetVelocity.x, 0f, targetVelocity.z);

        bool freshPress = hasMovementInput && !hadMovementInputLast;
        if (freshPress && currentHorizontal.magnitude < tapKickThreshold)
            currentHorizontal = moveDirection * tapKickSpeed;
        hadMovementInputLast = hasMovementInput;

        float rate = hasMovementInput ? acceleration : deceleration;
        Vector3 newHorizontal = Vector3.MoveTowards(currentHorizontal, desiredHorizontal, rate * Time.deltaTime);

        targetVelocity = new Vector3(newHorizontal.x, yVelocity, newHorizontal.z);
    }

    // Camera-relative horizontal direction of the current move input, shared by walking, the
    // charge-jump aim, and air steering. Returns Vector3.zero (and inputMagnitude 0) when input
    // is disabled or there's no meaningful deflection. Keyboard wins when both devices are active;
    // otherwise the left stick drives, and its deflection sets inputMagnitude so small tilts read
    // as slower input than a full push.
    private Vector3 GetMoveDirection(out float inputMagnitude)
    {
        inputMagnitude = 0f;

        bool inputDisabled = InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen || areControlsLocked || isLockedOnFish;
        if (inputDisabled) return Vector3.zero;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector2 moveInput = Vector2.ClampMagnitude(new Vector2(h, v), 1f);
        if (moveInput.sqrMagnitude < 0.01f) moveInput = GamepadInput.Move;

        inputMagnitude = moveInput.magnitude;
        if (inputMagnitude <= 0.1f) { inputMagnitude = 0f; return Vector3.zero; }

        Transform camTransform;
        if (useStaticCamera && staticCameraTarget != null)
            camTransform = staticCameraTarget;
        else
            camTransform = cameraController != null ? cameraController.CameraTransform : transform;

        Vector3 camForward = camTransform.forward;
        Vector3 camRight = camTransform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        return (camForward * moveInput.y + camRight * moveInput.x).normalized;
    }

    public void SetFacing(Quaternion worldRotation)
    {
        if (playerModel == null) return;
        Vector3 flatForward = worldRotation * Vector3.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) return;
        Quaternion flat = Quaternion.LookRotation(flatForward.normalized);
        playerModel.rotation = flat;
        targetModelRotation = flat;
    }

    private void HandleRotation()
    {
        if (areControlsLocked) return;

        if (chargeJump.Phase == ChargeJumpController.JumpPhase.Charging)
        {
            // While charging, the player is pinned in place but free to re-aim with the normal
            // directional input. Face the input direction so the launch (which fires along the
            // model's forward) goes where they point; hold the current aim when there's no input.
            Vector3 aimDir = GetMoveDirection(out _);
            if (aimDir.sqrMagnitude > 0.001f)
                targetModelRotation = Quaternion.LookRotation(aimDir);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        if (chargeJump.Phase == ChargeJumpController.JumpPhase.Launched)
        {
            if (enableBallTumble)
            {
                // Ball-form tumble is applied in LateUpdate (PlayerBallTumble.TickReset + ApplySpin) —
                // after squash/stretch and the Animator — so it spins about the mesh center without being
                // overwritten, and around the camera read so the view stays stable.
                return;
            }

            // Arc-look (initial flight only): face the travel heading and pitch the nose along the arc —
            // up while rising, down while falling — so the model arcs like a leaping slime. The pitch is
            // the velocity's elevation angle. Once it bounces, the branch below rights it back upright.
            Vector3 horizVel = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            float horizMag = horizVel.magnitude;
            if (horizMag > 0.01f)
            {
                Quaternion yaw = Quaternion.LookRotation(horizVel / horizMag, Vector3.up);
                if (enableJumpArcLook)
                {
                    // Negative pitch about local right = nose up (Unity's +X rotation pitches down).
                    float pitchDeg = Mathf.Atan2(targetVelocity.y, horizMag) * Mathf.Rad2Deg * jumpArcPitchScale;
                    targetModelRotation = yaw * Quaternion.AngleAxis(-pitchDeg, Vector3.right);
                }
                else
                {
                    targetModelRotation = yaw;
                }
            }
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        if (chargeJump.Phase != ChargeJumpController.JumpPhase.None)
        {
            // Bounced: the arc is done — reset to the default upright pose (facing the travel heading,
            // no pitch) and hold it, so the rebound and final landing settle on its feet instead of
            // arcing again. Clearing the pitch here also stops a pitched rotation lingering into the
            // grounded state if horizontal speed is low at touchdown.
            Vector3 horizVel = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            if (horizVel.sqrMagnitude > 0.01f)
                targetModelRotation = Quaternion.LookRotation(horizVel.normalized);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        // Grounded / no jump: re-arm the tumble so the next launch rolls a fresh axis.
        ballTumble.Disarm();

        if (isLockedOnFish && fishLockTarget != null)
        {
            Vector3 dirToFish = fishLockTarget.position - playerModel.position;
            dirToFish.y = 0f;
            if (dirToFish.sqrMagnitude > 0.001f)
                targetModelRotation = Quaternion.LookRotation(dirToFish.normalized);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        if (fishingAnimHandler != null && fishingAnimHandler.IsCasting)
        {
            targetModelRotation = playerModel.rotation;
            return;
        }

        if (new Vector3(targetVelocity.x, 0, targetVelocity.z).magnitude > 0.1f)
        {
            Vector3 lookDirection = new Vector3(targetVelocity.x, 0, targetVelocity.z);
            targetModelRotation = Quaternion.LookRotation(lookDirection);
        }

        playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
    }

    // Toggles the configured renderers off while a charge jump is active (ball form) and back on
    // when it ends. Only flips on state change. GameObjects/physics stay untouched.
    private void UpdateJumpVisuals()
    {
        // Charge-only sprite: hidden strictly while the charge is HELD, back the moment it's released
        // (launch) or cancelled — a tighter window than the group below, which spans the whole flight.
        // Polled like the rest so no release/cancel path can leave it stuck hidden.
        if (hideSpriteWhileCharging != null)
        {
            bool show = chargeJump.Phase != ChargeJumpController.JumpPhase.Charging;
            if (hideSpriteWhileCharging.enabled != show) hideSpriteWhileCharging.enabled = show;
        }

        // Hidden while balling up: morphing in / holding during the charge (Charging) and flying
        // (Launched). They reappear at the bounce (Bounced), where the morph-out animation plays.
        bool hide = chargeJump.Phase == ChargeJumpController.JumpPhase.Charging
                 || chargeJump.Phase == ChargeJumpController.JumpPhase.Launched;
        if (hide == jumpVisualsHidden) return;
        jumpVisualsHidden = hide;

        if (hideRenderersDuringJump != null)
            foreach (var r in hideRenderersDuringJump)
                if (r != null) r.enabled = !hide;

        // Let runtime-spawned visuals (the instantiated bobber + line) hide themselves.
        BallJumpVisibilityChanged?.Invoke(hide);
    }

    private void HandleGravity()
    {
        if (characterController.isGrounded && targetVelocity.y < 0f) targetVelocity.y = groundedDownForce;
        else targetVelocity.y += gravity * Time.deltaTime;
    }

    private void HandleAnimation()
    {
        if (animator)
        {
            float horizontalSpeed = new Vector3(targetVelocity.x, 0, targetVelocity.z).magnitude;

            if (areControlsLocked || InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen || isLockedOnFish)
            {
                horizontalSpeed = 0f;
            }

            animator.SetFloat(hashSpeed, horizontalSpeed, speedAnimDampTime, Time.deltaTime);
        }
    }

    private void HandleIdleAnimation()
    {
        bool isMoving = !areControlsLocked && !InventoryUI.IsInventoryOpen &&
                        (new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).magnitude > 0.1f
                         || GamepadInput.Move.magnitude > 0.1f);
        bool isRotatingCamera = !areControlsLocked && !InventoryUI.IsInventoryOpen
                                && (Input.GetMouseButton(1) || GamepadInput.Look.magnitude > 0.1f);
        if (InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen) { isMoving = false; isRotatingCamera = false; }

        if (isMoving || isRotatingCamera || isLockedOnFish) NotifyOfAction();
        else idleTimer += Time.deltaTime;

        if (idleTimer >= sitDownHoldTime && allowSitdown)
        {
            animator.SetTrigger(hashSitdown);
            allowSitdown = false;
        }
    }

    private void TickChargeJump()
    {
        bool inputDisabled = InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen || areControlsLocked || isLockedOnFish;

        // The press that closes a menu must not double as a jump: while input is disabled the latch
        // arms, and it only clears once the jump button is fully up afterwards.
        if (inputDisabled)
            jumpBlockedUntilRelease = true;
        else if (jumpBlockedUntilRelease && !Input.GetKey(chargeJumpKey) && !GamepadInput.JumpHeld)
            jumpBlockedUntilRelease = false;

        Vector3 modelForward = playerModel ? playerModel.forward : transform.forward;
        Vector3 steerDir = GetMoveDirection(out _);
        var cfg = BuildJumpConfig();
        chargeJump.Tick(ref targetVelocity, modelForward, transform.forward, steerDir,
                        inputDisabled || jumpBlockedUntilRelease, in cfg);
    }

    private ChargeJumpController.JumpConfig BuildJumpConfig()
    {
        return new ChargeJumpController.JumpConfig
        {
            chargeJumpKey = chargeJumpKey,
            maxChargeTime = maxChargeTime,
            minLaunchCharge = minLaunchCharge,
            forwardByCharge = forwardByCharge,
            upwardByCharge = upwardByCharge,
            bounceForwardMultiplier = bounceForwardMultiplier,
            bounceUpwardMultiplier = bounceUpwardMultiplier,
            airSteerAccel = airSteerAccel,
            wallBounceRestitution = wallBounceRestitution,
            wallBounceMaxSurfaceY = wallBounceMaxSurfaceY,
            chainJumpWindow = chainJumpWindow,
            chainJumpWindowAfterBounce = chainJumpWindowAfterBounce,
            chainFullChargeThreshold = chainFullChargeThreshold,
        };
    }

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
