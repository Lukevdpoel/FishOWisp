using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Object References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform playerModel;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform framingTarget;

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float sprintSpeed = 12f;
    [SerializeField] private float rotationSpeed = 15f;
    [SerializeField] private float gravity = -20f;

    [Header("Movement Animations")]
    // NEW: We replaced the booleans with a single float parameter name
    [SerializeField] private string speedAnimFloat = "Speed";

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
    [SerializeField] private float catchLookDownAngle = 25f;
    [SerializeField] private float catchZoomDistance = 1.2f;
    [SerializeField] private float catchVerticalOffset = 1.6f;
    [SerializeField] private float catchHorizontalOffset = 0f;
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
    private bool isBountyBoardActive = false;
    private Transform activeBountyBoard;

    private CharacterController characterController;
    private Vector3 targetVelocity;
    private float idleTimer;
    private bool allowSitdown = true;

    private bool isSprinting = false;

    private float startDistance, cameraXAngle, cameraYAngle, smoothXAngle, smoothYAngle, xVel, yVel;
    private Camera cam;
    private float currentCameraDistance, distanceVelocity;

    private Vector3 currentPivotPosition;
    private Vector3 pivotVelocity;

    private Transform activeBobberTransform;
    private Quaternion targetModelRotation;

    // ... (Keep your OnEnable, OnDisable, Start, Update, LateUpdate, Fishing Events exactly as they were) ...
    // Note: I omitted them here to save space, just replace the variables at the top and the methods below!

    private void OnDialogueStateChanged(bool isOpen)
    {
        areControlsLocked = isOpen;
        if (isOpen)
        {
            targetVelocity = Vector3.zero;
            if (animator && !string.IsNullOrEmpty(speedAnimFloat)) animator.SetFloat(speedAnimFloat, 0f);
        }
    }

    private void HandleBountyBoard(bool isOpen, Transform boardTransform)
    {
        areControlsLocked = isOpen;
        isBountyBoardActive = isOpen;
        activeBountyBoard = boardTransform;

        if (isOpen)
        {
            targetVelocity = Vector3.zero;
            if (animator && !string.IsNullOrEmpty(speedAnimFloat)) animator.SetFloat(speedAnimFloat, 0f);
        }
    }

    private void HandleMovement()
    {
        float yVelocity = targetVelocity.y;
        bool inputDisabled = InventoryUI.IsInventoryOpen || areControlsLocked;

        float h = inputDisabled ? 0f : Input.GetAxisRaw("Horizontal");
        float v = inputDisabled ? 0f : Input.GetAxisRaw("Vertical");

        bool hasMovementInput = new Vector2(h, v).magnitude > 0.1f;
        isSprinting = hasMovementInput && Input.GetKey(KeyCode.LeftShift) && !inputDisabled;

        float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;

        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;

        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDirection = (camForward * v + camRight * h).normalized;
        targetVelocity = moveDirection * currentSpeed;
        targetVelocity.y = yVelocity;
    }

    // NEW: HandleAnimation now passes the character's horizontal speed to the Animator
    private void HandleAnimation()
    {
        if (animator && !string.IsNullOrEmpty(speedAnimFloat))
        {
            // Calculate how fast the character is moving purely horizontally (ignoring gravity/falling)
            float horizontalSpeed = new Vector3(targetVelocity.x, 0, targetVelocity.z).magnitude;

            // If controls are locked or inventory is open, force speed to 0
            if (areControlsLocked || InventoryUI.IsInventoryOpen)
            {
                horizontalSpeed = 0f;
            }

            // Smoothly pass this value to the Animator
            animator.SetFloat(speedAnimFloat, horizontalSpeed, 0.1f, Time.deltaTime);
        }
    }

    // ... (Keep HandleRotation, HandleGravity, HandleIdleAnimation, HandleJumpInput, HandleCamera, HandleCursorLocking exactly as they were) ...
}