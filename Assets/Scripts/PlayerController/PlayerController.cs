using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
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
    [Tooltip("Gamepad sprint latch threshold: one left-stick click latches sprint on, and it " +
             "stays on while the stick stays deflected past this fraction. Ease off below it " +
             "(or stop) and the latch drops. Keyboard sprint (Shift) is still hold-to-sprint.")]
    [SerializeField, Range(0.1f, 1f)] private float sprintMaintainThreshold = 0.5f;
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

    [Header("Airborne Tumble")]
    [SerializeField] private float ballSpinDegPerUnitSpeed = 30f;
    [SerializeField, Range(0f, 1f)] private float ballSpinAxisJitter = 0.35f;
    [SerializeField, Range(0f, 0.5f)] private float ballSpinSpeedJitter = 0.2f;

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

    private CharacterController characterController;
    private Vector3 targetVelocity;
    private float idleTimer;
    private bool allowSitdown = true;
    private bool isSprinting = false;
    // Gamepad sprint session: a left-stick click "arms" sprintLatched; it becomes sprintEngaged
    // once the stick actually pushes past the maintain threshold, and the session ends (latch
    // cleared) the moment an engaged sprint slows below that threshold — so it survives clicking
    // L3 a hair before/after pushing the stick, but still needs a fresh click after you stop.
    private bool sprintLatched = false;
    private bool sprintEngaged = false;
    private bool hadMovementInputLast;
    private Quaternion targetModelRotation;
    private bool isLockedOnFish;
    private Transform fishLockTarget;

    private int hashSpeed;
    private int hashJump;
    private int hashSitdown;
    private int hashBallIn;
    private int hashBallOut;

    private readonly PlayerAirborneTumble airborneTumble = new PlayerAirborneTumble();
    private readonly PlayerSquashStretch squashStretch = new PlayerSquashStretch();
    private readonly ChargeJumpController chargeJump = new ChargeJumpController();

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

        airborneTumble.Init(normalMeshRoot);
        squashStretch.Init(playerModel, normalMeshRoot);
        chargeJump.Init(characterController, animator, hashBallIn, hashBallOut,
                        airborneTumble, squashStretch, NotifyOfAction);

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

        bool isAirborneJump = chargeJump.Phase == ChargeJumpController.JumpPhase.Launched
                           || chargeJump.Phase == ChargeJumpController.JumpPhase.Bounced;
        airborneTumble.Tick(isAirborneJump, characterController != null && characterController.isGrounded);

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
            Vector3 horiz = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            horiz = Vector3.MoveTowards(horiz, Vector3.zero, deceleration * Time.deltaTime);
            targetVelocity = new Vector3(horiz.x, yVelocity, horiz.z);
            return;
        }

        if (jumpPhase == ChargeJumpController.JumpPhase.Launched || jumpPhase == ChargeJumpController.JumpPhase.Bounced)
        {
            // Trajectory locked once launched — gravity still applies via HandleGravity.
            return;
        }

        bool inputDisabled = InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen || areControlsLocked || isLockedOnFish;

        float h = inputDisabled ? 0f : Input.GetAxisRaw("Horizontal");
        float v = inputDisabled ? 0f : Input.GetAxisRaw("Vertical");

        // Keyboard wins when both devices are active; otherwise the left stick drives, and
        // its deflection scales the target speed so small tilts walk slower than full tilt.
        Vector2 moveInput = Vector2.ClampMagnitude(new Vector2(h, v), 1f);
        if (!inputDisabled && moveInput.sqrMagnitude < 0.01f) moveInput = GamepadInput.Move;

        bool hasMovementInput = moveInput.magnitude > 0.1f;

        // Gamepad sprint is a latch: one left-stick click arms it, the stick crossing the
        // maintain threshold engages the run, and slowing back below the threshold ends the
        // session (needs another click to sprint again). No hold-to-sprint on the pad; keyboard
        // Shift stays a plain hold.
        if (inputDisabled)
        {
            sprintLatched = false;
            sprintEngaged = false;
        }
        else
        {
            if (GamepadInput.SprintTogglePressed) { sprintLatched = true; sprintEngaged = false; }
            if (sprintLatched)
            {
                if (moveInput.magnitude >= sprintMaintainThreshold) sprintEngaged = true;
                else if (sprintEngaged) { sprintLatched = false; sprintEngaged = false; }
            }
        }

        isSprinting = hasMovementInput && !inputDisabled
                      && (Input.GetKey(KeyCode.LeftShift) || (sprintLatched && sprintEngaged));

        float currentSpeed = (isSprinting ? sprintSpeed : walkSpeed) * Mathf.Clamp01(moveInput.magnitude);

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

        Vector3 moveDirection = (camForward * moveInput.y + camRight * moveInput.x).normalized;
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

        if (chargeJump.Phase != ChargeJumpController.JumpPhase.None)
        {
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetModelRotation, rotationSpeed * Time.deltaTime);
            return;
        }

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

        var cfg = new ChargeJumpController.JumpConfig
        {
            chargeJumpKey = chargeJumpKey,
            maxChargeTime = maxChargeTime,
            minLaunchCharge = minLaunchCharge,
            forwardByCharge = forwardByCharge,
            upwardByCharge = upwardByCharge,
            bounceForwardMultiplier = bounceForwardMultiplier,
            bounceUpwardMultiplier = bounceUpwardMultiplier,
            tumbleDegPerUnitSpeed = ballSpinDegPerUnitSpeed,
            tumbleAxisJitter = ballSpinAxisJitter,
            tumbleSpeedJitter = ballSpinSpeedJitter,
        };

        chargeJump.Tick(ref targetVelocity, modelForward, transform.forward, inputDisabled, in cfg);
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
