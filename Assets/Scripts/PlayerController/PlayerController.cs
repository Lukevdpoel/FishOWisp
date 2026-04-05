using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Object References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform playerModel;

    [Header("Component References")]
    [SerializeField] private PlayerCameraController cameraController;
    [SerializeField] private PlayerFishingAnimHandler fishingAnimHandler;

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float sprintSpeed = 12f;
    [SerializeField] private float rotationSpeed = 15f;
    [SerializeField] private float gravity = -20f;

    [Header("Movement Animations")]
    [SerializeField] private string speedAnimFloat = "Speed";

    [Header("Jump Animation")]
    [SerializeField] private string jumpAnimTrigger = "Jump";

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
    private Quaternion targetModelRotation;
    private bool isLockedOnFish;
    private Transform fishLockTarget;

    private int hashSpeed;
    private int hashJump;
    private int hashSitdown;

    void Start()
    {
        hashSpeed = Animator.StringToHash(speedAnimFloat);
        hashJump = Animator.StringToHash(jumpAnimTrigger);
        hashSitdown = Animator.StringToHash(sitdownAnimTrigger);

        characterController = GetComponent<CharacterController>();
        if (playerModel) targetModelRotation = playerModel.rotation;

        if (cameraController != null) cameraController.Initialize(playerModel);
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
        targetVelocity = moveDirection * currentSpeed;
        targetVelocity.y = yVelocity;
    }

    private void HandleRotation()
    {
        if (areControlsLocked) return;

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

    private void HandleJumpInput()
    {
        if (InventoryUI.IsInventoryOpen || areControlsLocked || isLockedOnFish) return;
        if (characterController.isGrounded && Input.GetButtonDown("Jump"))
        {
            if (animator)
            {
                animator.SetTrigger(hashJump);
                NotifyOfAction();
            }
        }
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
