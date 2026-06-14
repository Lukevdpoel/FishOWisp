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

    [Header("Squash & Stretch")]
    [SerializeField, Range(0.1f, 1f)] private float chargeMaxSquash = 0.55f;
    [SerializeField] private float scaleLerpSpeed = 18f;
    // Cartoon bounce: drives Y-scale over a short window after BounceOnLand. Author this curve
    // so it dips below 1 (squash) then peaks above 1 (stretch) before returning to 1 (neutral).
    [SerializeField] private AnimationCurve bounceImpactCurve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.15f, 0.5f),
        new Keyframe(0.5f, 1.3f),
        new Keyframe(1f, 1f));
    [SerializeField] private float bounceImpactDuration = 0.35f;

    [Header("Tucked-Jump Transitions")]
    [SerializeField] private string ballTransitionInTrigger = "BallTransitionIn";
    [SerializeField] private string ballTransitionOutTrigger = "BallTransitionOut";

    [Header("Airborne Ball Tumble")]
    // While launched in ball form the model tumbles around a random axis. We spin playerModel (the
    // parent of normalMeshRoot, where the body Animator lives), so the rotation rides over the morph
    // clips instead of fighting them. A fresh random axis/speed is rolled at each launch; the model
    // rights itself back upright on the bounce + landing via HandleRotation.
    [SerializeField] private bool enableBallTumble = true;
    [SerializeField] private float ballTumbleMinSpeed = 220f; // deg/sec
    [SerializeField] private float ballTumbleMaxSpeed = 540f; // deg/sec

    [Header("Jump Hidden Renderers")]
    [Tooltip("Optional persistent renderers switched off (renderer.enabled = false) while a charge " +
             "jump is active and back on when it ends — for anything that shouldn't show in ball form. " +
             "The runtime-spawned bobber + fishing line hide themselves via BallJumpVisibilityChanged, " +
             "so they don't need to be listed here. GameObjects stay active; only rendering toggles.")]
    [SerializeField] private Renderer[] hideRenderersDuringJump;

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

    public bool IsSprinting => isSprinting;

    // True only when the player is firmly on the ground and not in any phase of a charge jump.
    // Actions that shouldn't begin in midair (e.g. casting the fishing rod) gate on this.
    public bool IsGrounded =>
        characterController != null
        && characterController.isGrounded
        && chargeJump.Phase == ChargeJumpController.JumpPhase.None;

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

    private readonly PlayerSquashStretch squashStretch = new PlayerSquashStretch();
    private readonly ChargeJumpController chargeJump = new ChargeJumpController();

    // Random midair tumble state, re-seeded each launch (see TickBallTumbleReset).
    private Vector3 ballTumbleAxis = Vector3.right;
    private float ballTumbleSpeed;
    private float ballTumbleAngle;
    private bool ballTumbleSeeded;
    private Vector3 ballRestLocalPos;
    private Quaternion ballRestLocalRot = Quaternion.identity;
    private Vector3 ballPivotRootLocal;   // ball-center spin pivot, stored in player-root local space
    private Renderer[] bodyRenderers;
    private bool jumpVisualsHidden;

    private Vector3 staticCameraOffset;
    private bool staticCameraOffsetCaptured;

    void Start()
    {
        hashSpeed = Animator.StringToHash(speedAnimFloat);
        hashJump = Animator.StringToHash(jumpAnimTrigger);
        hashSitdown = Animator.StringToHash(sitdownAnimTrigger);
        hashBallIn = Animator.StringToHash(ballTransitionInTrigger);
        hashBallOut = Animator.StringToHash(ballTransitionOutTrigger);

        characterController = GetComponent<CharacterController>();
        if (playerModel) targetModelRotation = playerModel.rotation;

        squashStretch.Init(playerModel, normalMeshRoot);
        chargeJump.Init(characterController, animator, hashBallIn, hashBallOut,
                        squashStretch, NotifyOfAction);

        // Cached for the in-air tumble's bounds-center pivot.
        if (normalMeshRoot != null) bodyRenderers = normalMeshRoot.GetComponentsInChildren<Renderer>(true);

        if (cameraController != null) cameraController.Initialize(playerModel);
    }

    void Update()
    {
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

    void LateUpdate()
    {
        if (Time.timeScale == 0f || Time.deltaTime <= 0.0001f) return;

        // Run after Animator has evaluated so our scale/rotation writes aren't overwritten by clip curves.
        squashStretch.Tick(
            chargeJump.Phase == ChargeJumpController.JumpPhase.Charging,
            chargeJump.ChargeNormalized(maxChargeTime),
            chargeMaxSquash, scaleLerpSpeed,
            bounceImpactCurve, bounceImpactDuration);

        // Snap the model to its rest pose (if tumbling) BEFORE the camera reads playerModel.position,
        // so the camera follows the stable body point. The spin itself is applied after the camera.
        bool tumbling = TickBallTumbleReset();

        HandleStaticCameraFollow();

        if (cameraController != null)
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
        if (tumbling) ApplyBallTumbleSpin();
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

        if (chargeJump.Phase == ChargeJumpController.JumpPhase.Launched && enableBallTumble)
        {
            // Ball-form tumble is applied in LateUpdate (TickBallTumbleReset + ApplyBallTumbleSpin) —
            // after squash/stretch and the Animator — so it spins about the mesh center without being
            // overwritten, and around the camera read so the view stays stable.
            return;
        }

        if (chargeJump.Phase != ChargeJumpController.JumpPhase.None)
        {
            // Bounced (or Launched with tumble off): ease back toward the travel heading so the
            // un-balled character rights itself and lands on its feet rather than mid-tumble.
            Vector3 horizVel = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            if (horizVel.sqrMagnitude > 0.01f)
                targetModelRotation = Quaternion.LookRotation(horizVel.normalized);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

        // Grounded / no jump: re-arm the tumble so the next launch rolls a fresh axis.
        ballTumbleSeeded = false;

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
        // Hidden only while morphing into / flying as the ball (Charging + Launched). They reappear
        // at the bounce (Bounced), where the morph-out animation plays — symmetric with the morph-in.
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

    // First half of the in-air ball tumble, run BEFORE the camera in LateUpdate. Seeds a random
    // axis/speed/pivot on the first launched frame, advances the spin angle, and snaps playerModel
    // back to its rest pose. Resetting to rest here means the camera (which reads playerModel.position
    // with no smoothing) follows a stable point; the actual spin is applied afterward in
    // ApplyBallTumbleSpin. Returns true while a tumble is active this frame.
    private bool TickBallTumbleReset()
    {
        if (playerModel == null) return false;
        if (!enableBallTumble || chargeJump.Phase != ChargeJumpController.JumpPhase.Launched) return false;

        if (!ballTumbleSeeded)
        {
            ballTumbleAxis = Random.onUnitSphere;
            ballTumbleSpeed = Random.Range(ballTumbleMinSpeed, ballTumbleMaxSpeed);
            ballTumbleAngle = 0f;
            ballRestLocalPos = playerModel.localPosition;
            ballRestLocalRot = playerModel.localRotation;
            ballPivotRootLocal = transform.InverseTransformPoint(ComputeBallCenterWorld());
            ballTumbleSeeded = true;
        }

        ballTumbleAngle += ballTumbleSpeed * Time.deltaTime;
        playerModel.localPosition = ballRestLocalPos;
        playerModel.localRotation = ballRestLocalRot;
        return true;
    }

    // Second half, run AFTER the camera. Spins playerModel about the mesh's bounds-center pivot
    // (which sits at the visual middle, unlike playerModel's own origin). Applying it post-camera
    // keeps the orbit purely visual — the camera already framed the stable rest position.
    private void ApplyBallTumbleSpin()
    {
        playerModel.RotateAround(transform.TransformPoint(ballPivotRootLocal), ballTumbleAxis, ballTumbleAngle);
    }

    // World-space point the ball spins about: the combined bounds center of the body mesh (its
    // visual middle). Falls back to the model origin if no renderers were cached.
    private Vector3 ComputeBallCenterWorld()
    {
        if (bodyRenderers == null || bodyRenderers.Length == 0) return playerModel.position;

        bool has = false;
        Bounds b = new Bounds();
        foreach (var r in bodyRenderers)
        {
            // Skip hidden renderers (e.g. the bobber/line we just disabled this frame) so they
            // don't drag the pivot off the visible ball.
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return has ? b.center : playerModel.position;
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
        Vector3 modelForward = playerModel ? playerModel.forward : transform.forward;
        Vector3 steerDir = GetMoveDirection(out _);
        var cfg = BuildJumpConfig();
        chargeJump.Tick(ref targetVelocity, modelForward, transform.forward, steerDir, inputDisabled, in cfg);
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

        if (areControlsLocked)
        {
            // Fishing locks controls too, but while the bobber/lure sits in the water the
            // mouse is orbiting the camera — keep the cursor locked and hidden there. Every
            // other locked context (dialogue, shop, inspection) shows the cursor as before.
            bool bobberCameraOwnsView = cameraController != null && cameraController.IsBobberCameraActive;
            if (!bobberCameraOwnsView)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }
        }

        bool isFighting = fishingAnimHandler != null && fishingAnimHandler.IsFightingFish;
        if (isFighting)
        {
            // Keep cursor locked during fight — mouse controls rod direction
            if (Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            return;
        }
        if (Time.timeScale > 0f) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }
}
