using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Object References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform playerModel;
    [SerializeField] private GameObject normalMeshRoot;
    [SerializeField] private GameObject ballMeshRoot;

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

    [Header("Ball Form Transitions")]
    [SerializeField] private string ballTransitionInTrigger = "BallTransitionIn";
    [SerializeField] private string ballTransitionOutTrigger = "BallTransitionOut";

    [Header("Ball Spin")]
    [SerializeField] private float ballSpinDegPerUnitSpeed = 30f;
    [SerializeField, Range(0f, 1f)] private float ballSpinAxisJitter = 0.35f;
    [SerializeField, Range(0f, 0.5f)] private float ballSpinSpeedJitter = 0.2f;

    [Header("Idle Animation")]
    [SerializeField] private string sitdownAnimTrigger = "Sitdown";
    [SerializeField] private float sitDownHoldTime = 4f;

    [Header("Physics")]
    [SerializeField] private float groundedDownForce = -2f;
    [SerializeField] private float speedAnimDampTime = 0.1f;

    [HideInInspector] public bool areControlsLocked = false;

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

    private enum JumpPhase { None, Charging, Launched, Bounced }
    private JumpPhase jumpPhase = JumpPhase.None;
    private float chargeTimer;
    private float launchHorizontalSpeed;
    private float launchUpwardSpeed;
    private Vector3 launchDirection = Vector3.forward;
    private bool airborneSinceLaunch;
    private Vector3 originalModelScale = Vector3.one;
    private Vector3 originalModelLocalPosition;
    private Vector3 originalNormalMeshScale = Vector3.one;
    private Vector3 originalBallMeshScale = Vector3.one;
    private Quaternion ballMeshRotationRelativeToPlayer = Quaternion.identity;
    private Vector3 ballSpinAxis = Vector3.right;
    private float ballSpinSpeedDeg;
    private float bounceImpactTimer = -1f; // -1 = inactive

    void Start()
    {
        hashSpeed = Animator.StringToHash(speedAnimFloat);
        hashJump = Animator.StringToHash(jumpAnimTrigger);
        hashSitdown = Animator.StringToHash(sitdownAnimTrigger);
        hashBallIn = Animator.StringToHash(ballTransitionInTrigger);
        hashBallOut = Animator.StringToHash(ballTransitionOutTrigger);

        characterController = GetComponent<CharacterController>();
        if (playerModel)
        {
            targetModelRotation = playerModel.rotation;
            originalModelScale = playerModel.localScale;
            originalModelLocalPosition = playerModel.localPosition;
        }
        if (normalMeshRoot) originalNormalMeshScale = normalMeshRoot.transform.localScale;
        if (ballMeshRoot)
        {
            originalBallMeshScale = ballMeshRoot.transform.localScale;
            // Capture how the ball mesh is oriented relative to the player's facing, so we can re-apply the
            // same relative orientation each jump regardless of which way the player is currently facing.
            if (playerModel)
                ballMeshRotationRelativeToPlayer = Quaternion.Inverse(playerModel.rotation) * ballMeshRoot.transform.rotation;
            else
                ballMeshRotationRelativeToPlayer = ballMeshRoot.transform.rotation;
        }
        SetBallMeshActive(false);

        if (cameraController != null) cameraController.Initialize(playerModel);
    }

    void Update()
    {
        HandleChargeJump();
        HandleMovement();
        HandleRotation();
        HandleGravity();
        HandleAnimation();
        HandleIdleAnimation();
        HandleCursorLocking();
        characterController?.Move(targetVelocity * Time.deltaTime);
    }

    void LateUpdate()
    {
        if (Time.timeScale == 0f || Time.deltaTime <= 0.0001f) return;

        // Run after Animator has evaluated so our scale/rotation writes aren't overwritten by clip curves.
        HandleSquashStretch();
        HandleBallSpin();

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

    private void HandleMovement()
    {
        float yVelocity = targetVelocity.y;

        if (jumpPhase == JumpPhase.Charging)
        {
            Vector3 horiz = new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            horiz = Vector3.MoveTowards(horiz, Vector3.zero, deceleration * Time.deltaTime);
            targetVelocity = new Vector3(horiz.x, yVelocity, horiz.z);
            return;
        }

        if (jumpPhase == JumpPhase.Launched || jumpPhase == JumpPhase.Bounced)
        {
            // Trajectory locked once launched — gravity still applies via HandleGravity.
            return;
        }

        bool inputDisabled = InventoryUI.IsInventoryOpen || areControlsLocked || isLockedOnFish;

        float h = inputDisabled ? 0f : Input.GetAxisRaw("Horizontal");
        float v = inputDisabled ? 0f : Input.GetAxisRaw("Vertical");

        bool hasMovementInput = new Vector2(h, v).magnitude > 0.1f;
        isSprinting = hasMovementInput && Input.GetKey(KeyCode.LeftShift) && !inputDisabled;

        float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;

        Transform camTransform = cameraController != null ? cameraController.CameraTransform : transform;
        Vector3 camForward = camTransform.forward;
        Vector3 camRight = camTransform.right;

        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDirection = (camForward * v + camRight * h).normalized;
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

    private void HandleRotation()
    {
        if (areControlsLocked) return;

        if (jumpPhase != JumpPhase.None)
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

            if (areControlsLocked || InventoryUI.IsInventoryOpen || isLockedOnFish)
            {
                horizontalSpeed = 0f;
            }

            animator.SetFloat(hashSpeed, horizontalSpeed, speedAnimDampTime, Time.deltaTime);
        }
    }

    private void HandleIdleAnimation()
    {
        bool isMoving = !areControlsLocked && !InventoryUI.IsInventoryOpen &&
                        (new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).magnitude > 0.1f);
        bool isRotatingCamera = !areControlsLocked && !InventoryUI.IsInventoryOpen && Input.GetMouseButton(1);
        if (InventoryUI.IsInventoryOpen) { isMoving = false; isRotatingCamera = false; }

        if (isMoving || isRotatingCamera || isLockedOnFish) NotifyOfAction();
        else idleTimer += Time.deltaTime;

        if (idleTimer >= sitDownHoldTime && allowSitdown)
        {
            animator.SetTrigger(hashSitdown);
            allowSitdown = false;
        }
    }

    private void HandleChargeJump()
    {
        bool inputDisabled = InventoryUI.IsInventoryOpen || areControlsLocked || isLockedOnFish;

        if (inputDisabled && jumpPhase == JumpPhase.Charging)
        {
            jumpPhase = JumpPhase.None;
            chargeTimer = 0f;
            return;
        }

        switch (jumpPhase)
        {
            case JumpPhase.None:
                if (!inputDisabled && characterController.isGrounded && Input.GetKeyDown(chargeJumpKey))
                {
                    jumpPhase = JumpPhase.Charging;
                    chargeTimer = 0f;
                    ResetBallMeshRotation();
                    if (animator) animator.SetTrigger(hashBallIn);
                    NotifyOfAction();
                }
                break;

            case JumpPhase.Charging:
                chargeTimer = Mathf.Min(chargeTimer + Time.deltaTime, maxChargeTime);
                if (Input.GetKeyUp(chargeJumpKey) || !Input.GetKey(chargeJumpKey))
                {
                    LaunchJump(chargeTimer / maxChargeTime);
                }
                break;

            case JumpPhase.Launched:
                if (!characterController.isGrounded) airborneSinceLaunch = true;
                else if (airborneSinceLaunch) BounceOnLand();
                break;

            case JumpPhase.Bounced:
                if (!characterController.isGrounded)
                {
                    airborneSinceLaunch = true;
                }
                else if (airborneSinceLaunch)
                {
                    jumpPhase = JumpPhase.None;
                    airborneSinceLaunch = false;
                    ResetBallMeshRotation();
                    if (animator) animator.SetTrigger(hashBallOut);
                }
                break;
        }
    }

    private void LaunchJump(float chargeNorm)
    {
        chargeNorm = Mathf.Clamp01(Mathf.Max(chargeNorm, minLaunchCharge));
        launchHorizontalSpeed = forwardByCharge.Evaluate(chargeNorm);
        launchUpwardSpeed = upwardByCharge.Evaluate(chargeNorm);

        Vector3 fwd = playerModel ? playerModel.forward : transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = transform.forward;
        launchDirection = fwd.normalized;

        Vector3 horiz = launchDirection * launchHorizontalSpeed;
        targetVelocity = new Vector3(horiz.x, launchUpwardSpeed, horiz.z);

        jumpPhase = JumpPhase.Launched;
        airborneSinceLaunch = false;
        SeedBallSpin();
        NotifyOfAction();
    }

    private void BounceOnLand()
    {
        Vector3 horiz = launchDirection * launchHorizontalSpeed * bounceForwardMultiplier;
        targetVelocity = new Vector3(horiz.x, launchUpwardSpeed * bounceUpwardMultiplier, horiz.z);
        jumpPhase = JumpPhase.Bounced;
        airborneSinceLaunch = false;
        ballSpinSpeedDeg *= bounceForwardMultiplier;
        bounceImpactTimer = 0f;
    }

    private void SeedBallSpin()
    {
        // Forward tumble around local X, with a small random tilt so it doesn't look mechanical.
        Vector3 jitter = new Vector3(
            0f,
            Random.Range(-ballSpinAxisJitter, ballSpinAxisJitter),
            Random.Range(-ballSpinAxisJitter, ballSpinAxisJitter));
        ballSpinAxis = (Vector3.right + jitter).normalized;

        float rate = launchHorizontalSpeed * ballSpinDegPerUnitSpeed;
        ballSpinSpeedDeg = rate * (1f + Random.Range(-ballSpinSpeedJitter, ballSpinSpeedJitter));

        ResetBallMeshRotation();
    }

    private void ResetBallMeshRotation()
    {
        if (!ballMeshRoot) return;
        Quaternion playerFacing = playerModel ? playerModel.rotation : Quaternion.identity;
        ballMeshRoot.transform.rotation = playerFacing * ballMeshRotationRelativeToPlayer;
    }

    private void HandleBallSpin()
    {
        if (!ballMeshRoot) return;
        if (jumpPhase != JumpPhase.Launched && jumpPhase != JumpPhase.Bounced) return;
        if (characterController.isGrounded) return;

        ballMeshRoot.transform.Rotate(ballSpinAxis, ballSpinSpeedDeg * Time.deltaTime, Space.Self);
    }

    // Public so animation events on the ball-in / ball-out clips can call it for frame-perfect mesh swap timing.
    // Toggles Renderers instead of GameObjects so the Animator keeps running — otherwise disabling the normal
    // mesh's GameObject would freeze its Animator mid-clip and the Morph Out animation event would never fire.
    public void SetBallMeshActive(bool active)
    {
        SetRenderersEnabled(normalMeshRoot, !active);
        SetRenderersEnabled(ballMeshRoot, active);
    }

    private static void SetRenderersEnabled(GameObject root, bool enabled)
    {
        if (!root) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            r.enabled = enabled;
    }

    private void HandleSquashStretch()
    {
        if (!playerModel) return;

        Vector3 scaleMul = Vector3.one;
        Vector3 targetPos = originalModelLocalPosition;
        bool driveDirectly = false;

        // One-shot bounce-impact curve: drives Y-scale through squash → stretch → neutral over
        // bounceImpactDuration after BounceOnLand fires. Set directly (no lerp) so the curve plays
        // exactly as authored — fast snaps land hard, that's the whole point.
        if (bounceImpactTimer >= 0f)
        {
            bounceImpactTimer += Time.deltaTime;
            if (bounceImpactTimer >= bounceImpactDuration)
            {
                bounceImpactTimer = -1f;
            }
            else
            {
                float bt = bounceImpactTimer / Mathf.Max(0.01f, bounceImpactDuration);
                float yScale = bounceImpactCurve.Evaluate(bt);
                float xz = VolumePreserveXZ(yScale);
                scaleMul = new Vector3(xz, yScale, xz);
                driveDirectly = true;
            }
        }

        // Charge squash takes priority if both overlap (e.g. user charges again immediately).
        if (jumpPhase == JumpPhase.Charging)
        {
            float t = Mathf.Clamp01(chargeTimer / maxChargeTime);
            float y = Mathf.Lerp(1f, chargeMaxSquash, t);
            float xz = VolumePreserveXZ(y);
            scaleMul = new Vector3(xz, y, xz);
            driveDirectly = false;
        }

        // playerModel's local axes align with player facing, so X/Y/Z scale = right/up/forward.
        Vector3 targetScale = Vector3.Scale(originalModelScale, scaleMul);
        if (driveDirectly)
            playerModel.localScale = targetScale;
        else
            playerModel.localScale = Vector3.Lerp(playerModel.localScale, targetScale, scaleLerpSpeed * Time.deltaTime);
        playerModel.localPosition = Vector3.Lerp(playerModel.localPosition, targetPos, scaleLerpSpeed * Time.deltaTime);

        // Belt-and-suspenders: keep mesh-root scales at their captured originals so stale scaling from a
        // previous code path can never compound with playerModel's scale.
        if (normalMeshRoot && normalMeshRoot.transform.localScale != originalNormalMeshScale)
            normalMeshRoot.transform.localScale = originalNormalMeshScale;
        if (ballMeshRoot && ballMeshRoot.transform.localScale != originalBallMeshScale)
            ballMeshRoot.transform.localScale = originalBallMeshScale;
    }

    private static float VolumePreserveXZ(float yScale)
    {
        return 1f / Mathf.Sqrt(Mathf.Max(0.01f, yScale));
    }

    private void HandleCursorLocking()
    {
        if (areControlsLocked) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; return; }

        bool isFighting = fishingAnimHandler != null && fishingAnimHandler.IsFightingFish;
        if (InventoryUI.IsInventoryOpen) return;
        if (isFighting)
        {
            // Keep cursor locked during fight — mouse controls rod direction
            if (Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            return;
        }
        if (Time.timeScale > 0f) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }
}
