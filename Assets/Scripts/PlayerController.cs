using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Object References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform playerModel;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform framingTarget;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float rotationSpeed = 15f;
    [SerializeField] private float gravity = -20f;

    [Header("Jump Animation")]
    [SerializeField] private string jumpAnimTrigger = "Jump";

    [Header("Fishing Animations")]
    [SerializeField] private string startChargingAnim = "StartCharging";
    [SerializeField] private string throwAnim = "Throw";
    [SerializeField] private string reelInAnim = "ReelIn";
    [SerializeField] private string isFightingAnimBool = "IsFighting";
    [SerializeField] private string isReelingDuringFightAnimBool = "IsReelingDuringFight";

    [Header("Camera Orbit")]
    [SerializeField] private float cameraSpeed = 120f;
    [SerializeField] private Vector2 cameraYClamp = new Vector2(20f, 55f);
    [SerializeField] private float pivotHeight = 1.3f;
    [SerializeField, Range(0f, 1f)] private float screenYTarget = 0.25f;

    [Header("Catch Camera Settings")]
    [Tooltip("The vertical angle (pitch) to look down at the fish.")]
    [SerializeField] private float catchLookDownAngle = 25f;

    [Tooltip("Distance from the player when showing the fish.")]
    [SerializeField] private float catchZoomDistance = 1.2f;

    [Tooltip("Vertical offset from the player's feet. Adjust to frame the chest/head.")]
    [SerializeField] private float catchVerticalOffset = 1.6f;

    [Tooltip("Horizontal offset (Screen Left/Right). Use this to center the fish if it's held to the side.")]
    [SerializeField] private float catchHorizontalOffset = 0f; // <--- NEW SETTING

    [Tooltip("How smoothly the camera moves to the focus point.")]
    [SerializeField] private float pivotSmoothTime = 0.25f;

    [Header("Idle Animation")]
    [SerializeField] private float sitDownHoldTime = 4f;

    [Header("Camera Smoothing")]
    [SerializeField] private float cameraSmoothTime = 0.05f;

    [Header("Camera Collision")]
    [SerializeField] private LayerMask collisionLayers;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float zoomDampTime = 0.1f;

    [Header("Static Camera Settings")]
    [SerializeField] private bool useStaticCamera = false;
    [SerializeField] private Transform staticCameraTarget;

    [HideInInspector] public bool areControlsLocked = false;

    private bool isFightingFish = false;
    private bool isCasting = false;
    private bool isCatchCameraActive = false;

    private CharacterController characterController;
    private Vector3 targetVelocity;
    private float idleTimer;
    private bool allowSitdown = true;

    private float startDistance, cameraXAngle, cameraYAngle, smoothXAngle, smoothYAngle, xVel, yVel;
    private Camera cam;
    private float currentCameraDistance, distanceVelocity;

    private Vector3 currentPivotPosition;
    private Vector3 pivotVelocity;

    private Transform activeBobberTransform;

    private void OnEnable()
    {
        FishingEvents.OnStartCharging += PlayStartChargingAnim;
        FishingEvents.OnThrowBobber += PlayThrowAnim;
        FishingEvents.OnHookFishSuccess += PlayReelInAnim;
        FishingEvents.OnFishFightBegin += StartFightingAnimation;
        FishingEvents.OnCancelFishing += StopFightingAnimation;
        FishingEvents.OnFishFightEnd += OnFishFightEnd;
        FishingEvents.OnStartReeling += HandleSuccessfulCatchAnimation;
        FishingEvents.OnStartCharging += OnCastStart;
        FishingEvents.OnCancelCharging += OnCastEnd;
        FishingEvents.OnCancelFishing += OnCastEnd;
        FishingEvents.OnThrowBobber += OnThrowBobber;
        FishingEvents.OnStartReelingDuringFight += StartReelingDuringFightAnim;
        FishingEvents.OnStopReelingDuringFight += StopReelingDuringFightAnim;
        FishingEvents.OnBobberLandedInWater += OnBobberLanded;
    }

    private void OnDisable()
    {
        FishingEvents.OnStartCharging -= PlayStartChargingAnim;
        FishingEvents.OnThrowBobber -= PlayThrowAnim;
        FishingEvents.OnHookFishSuccess -= PlayReelInAnim;
        FishingEvents.OnFishFightBegin -= StartFightingAnimation;
        FishingEvents.OnCancelFishing -= StopFightingAnimation;
        FishingEvents.OnFishFightEnd -= OnFishFightEnd;
        FishingEvents.OnStartReeling -= HandleSuccessfulCatchAnimation;
        FishingEvents.OnStartCharging -= OnCastStart;
        FishingEvents.OnCancelCharging -= OnCastEnd;
        FishingEvents.OnCancelFishing -= OnCastEnd;
        FishingEvents.OnThrowBobber -= OnThrowBobber;
        FishingEvents.OnStartReelingDuringFight -= StartReelingDuringFightAnim;
        FishingEvents.OnStopReelingDuringFight -= StopReelingDuringFightAnim;
        FishingEvents.OnBobberLandedInWater -= OnBobberLanded;
    }

    public void SetCatchCamera(bool active)
    {
        isCatchCameraActive = active;
    }

    private void OnCastStart() => isCasting = true;
    private void OnCastEnd() { isCasting = false; isFightingFish = false; }
    private void OnThrowBobber(Vector3 direction, float force) => isCasting = false;
    private void OnBobberLanded(BobberController bobber) { activeBobberTransform = bobber.transform; }
    private void OnFishFightEnd(bool success) { isFightingFish = false; StopFightingAnimation(); }

    private void StartReelingDuringFightAnim()
    {
        if (animator && !string.IsNullOrEmpty(isReelingDuringFightAnimBool))
            animator.SetBool(isReelingDuringFightAnimBool, true);
    }

    private void StopReelingDuringFightAnim()
    {
        if (animator && !string.IsNullOrEmpty(isReelingDuringFightAnimBool))
            animator.SetBool(isReelingDuringFightAnimBool, false);
    }

    private void PlayReelInAnim()
    {
        if (animator && !string.IsNullOrEmpty(reelInAnim))
            animator.SetTrigger(reelInAnim);
    }

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        if (cameraTransform && playerModel)
        {
            startDistance = Vector3.Distance(cameraTransform.position, playerModel.position);
            currentCameraDistance = startDistance;
            Vector3 initialCameraAngles = cameraTransform.eulerAngles;
            cameraXAngle = initialCameraAngles.y;
            cameraYAngle = initialCameraAngles.x;
            smoothXAngle = cameraXAngle;
            smoothYAngle = cameraYAngle;
            cam = cameraTransform.GetComponent<Camera>();
            currentPivotPosition = playerModel.position + Vector3.up * pivotHeight;
        }
        if (framingTarget == null) framingTarget = playerModel;
    }

    void Update()
    {
        HandleMovement();
        HandleRotation();
        HandleGravity();
        HandleJumpInput();
        HandleAnimation();
        HandleIdleAnimation();
        HandleCursorLocking();
        characterController?.Move(targetVelocity * Time.deltaTime);
    }

    void LateUpdate()
    {
        if (Time.timeScale == 0f || Time.deltaTime <= 0.0001f) return;
        HandleCamera();
    }

    // [Animation Helpers Unchanged]
    private void PlayStartChargingAnim() { if (animator && !string.IsNullOrEmpty(startChargingAnim)) animator.SetTrigger(startChargingAnim); }
    private void PlayThrowAnim(Vector3 direction, float force) { if (animator && !string.IsNullOrEmpty(throwAnim)) animator.SetTrigger(throwAnim); }

    private void StartFightingAnimation(FishPreset fish)
    {
        isFightingFish = true;
        if (animator && !string.IsNullOrEmpty(isFightingAnimBool))
            animator.SetBool(isFightingAnimBool, true);
    }

    private void StopFightingAnimation()
    {
        isFightingFish = false;
        if (animator && !string.IsNullOrEmpty(isFightingAnimBool))
            animator.SetBool(isFightingAnimBool, false);
    }

    private void HandleSuccessfulCatchAnimation() { StopFightingAnimation(); PlayReelInAnim(); }

    private void HandleMovement()
    {
        float yVelocity = targetVelocity.y;
        bool inputDisabled = InventoryUI.IsInventoryOpen || areControlsLocked;

        float h = inputDisabled ? 0f : Input.GetAxisRaw("Horizontal");
        float v = inputDisabled ? 0f : Input.GetAxisRaw("Vertical");

        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;

        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDirection = (camForward * v + camRight * h).normalized;
        targetVelocity = moveDirection * moveSpeed;
        targetVelocity.y = yVelocity;
    }

    private void HandleRotation()
    {
        if (areControlsLocked) return;

        if (new Vector3(targetVelocity.x, 0, targetVelocity.z).magnitude > 0.1f)
        {
            Vector3 lookDirection = new Vector3(targetVelocity.x, 0, targetVelocity.z);
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            playerModel.rotation = Quaternion.Slerp(playerModel.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private void HandleGravity()
    {
        if (characterController.isGrounded && targetVelocity.y < 0f)
            targetVelocity.y = -2f;
        else
            targetVelocity.y += gravity * Time.deltaTime;
    }

    private void HandleAnimation()
    {
        bool isWalking = !areControlsLocked && !InventoryUI.IsInventoryOpen &&
                         (new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).magnitude > 0.1f);
        animator.SetBool("Walk", isWalking);
    }

    private void HandleIdleAnimation()
    {
        bool isMoving = !areControlsLocked && !InventoryUI.IsInventoryOpen &&
                        (new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).magnitude > 0.1f);
        bool isRotatingCamera = !areControlsLocked && !InventoryUI.IsInventoryOpen && Input.GetMouseButton(1);

        if (InventoryUI.IsInventoryOpen) { isMoving = false; isRotatingCamera = false; }

        if (isMoving || isRotatingCamera) NotifyOfAction();
        else idleTimer += Time.deltaTime;

        if (idleTimer >= sitDownHoldTime && allowSitdown)
        {
            animator.SetTrigger("Sitdown");
            allowSitdown = false;
        }
    }

    public void NotifyOfAction() { idleTimer = 0f; allowSitdown = true; }

    private void HandleJumpInput()
    {
        if (InventoryUI.IsInventoryOpen || areControlsLocked) return;
        if (characterController.isGrounded && Input.GetButtonDown("Jump"))
        {
            if (animator && !string.IsNullOrEmpty(jumpAnimTrigger))
            {
                animator.SetTrigger(jumpAnimTrigger);
                NotifyOfAction();
            }
        }
    }

    private void HandleCamera()
    {
        if (!cameraTransform || !playerModel) return;
        if (areControlsLocked && !isCatchCameraActive) return;
        if (InventoryUI.IsInventoryOpen) return;

        if (useStaticCamera)
        {
            if (staticCameraTarget != null)
            {
                cameraTransform.position = staticCameraTarget.position;
                cameraTransform.rotation = staticCameraTarget.rotation;
            }
            return;
        }

        if (float.IsNaN(xVel) || float.IsNaN(yVel) || float.IsNaN(distanceVelocity))
        {
            xVel = 0f; yVel = 0f; distanceVelocity = 0f;
        }

        // --- ANGLES ---
        if (!isFightingFish && !isCatchCameraActive)
        {
            float mouseX = Input.GetAxis("Mouse X") * cameraSpeed * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * cameraSpeed * Time.deltaTime;
            cameraXAngle += mouseX;
            cameraYAngle -= mouseY;
            cameraYAngle = Mathf.Clamp(cameraYAngle, cameraYClamp.x, cameraYClamp.y);
        }
        else if (isCatchCameraActive)
        {
            // FORCE LOOK DOWN
            cameraYAngle = Mathf.Lerp(cameraYAngle, catchLookDownAngle, Time.deltaTime * 5f);
        }
        else if (activeBobberTransform != null && isFightingFish)
        {
            Vector3 directionToBobber = (activeBobberTransform.position - playerModel.position).normalized;
            Quaternion targetRot = Quaternion.LookRotation(directionToBobber);
            float targetYAngle = targetRot.eulerAngles.y;
            cameraXAngle = Mathf.LerpAngle(cameraXAngle, targetYAngle, Time.deltaTime * 2f);
        }

        smoothXAngle = Mathf.SmoothDampAngle(smoothXAngle, cameraXAngle, ref xVel, cameraSmoothTime);
        smoothYAngle = Mathf.SmoothDampAngle(smoothYAngle, cameraYAngle, ref yVel, cameraSmoothTime);

        Quaternion rotation = Quaternion.Euler(smoothYAngle, smoothXAngle, 0f);
        Vector3 cameraDirection = -(rotation * Vector3.forward);

        // --- PIVOT LOGIC ---
        Vector3 basePos = playerModel.position;
        Vector3 targetPivot;

        if (isCatchCameraActive)
        {
            // CATCH MODE: Apply offsets
            targetPivot = basePos + Vector3.up * catchVerticalOffset;

            // Add Horizontal Offset (Screen Space Right)
            // 'rotation * Vector3.right' gives us the camera's Right direction.
            targetPivot += rotation * Vector3.right * catchHorizontalOffset;

            currentPivotPosition = Vector3.SmoothDamp(currentPivotPosition, targetPivot, ref pivotVelocity, pivotSmoothTime);
        }
        else
        {
            // NORMAL MODE: Snap Instantly
            targetPivot = basePos + Vector3.up * pivotHeight;
            currentPivotPosition = targetPivot;
            pivotVelocity = Vector3.zero;
        }

        float targetDistance = startDistance;
        RaycastHit hit;

        if (isCatchCameraActive)
        {
            targetDistance = catchZoomDistance;
        }
        else if (Physics.SphereCast(currentPivotPosition, collisionRadius, cameraDirection, out hit, startDistance, collisionLayers))
        {
            targetDistance = hit.distance;
        }

        currentCameraDistance = Mathf.SmoothDamp(currentCameraDistance, targetDistance, ref distanceVelocity, zoomDampTime);

        Vector3 finalPos = currentPivotPosition + cameraDirection * currentCameraDistance;

        if (cam == null) cam = cameraTransform.GetComponent<Camera>();
        cameraTransform.position = finalPos;
        cameraTransform.rotation = rotation;
    }

    private void HandleCursorLocking()
    {
        if (areControlsLocked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (InventoryUI.IsInventoryOpen || isFightingFish)
        {
            if (isFightingFish && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }

        if (Time.timeScale > 0f)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}