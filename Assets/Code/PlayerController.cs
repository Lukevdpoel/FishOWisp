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

    [Header("Fishing Animations")] // <-- NEW SECTION
    [SerializeField] private string startChargingAnim = "StartCharging";
    [SerializeField] private string throwAnim = "Throw";
    [SerializeField] private string reelInAnim = "ReelIn";

    [Header("Camera Orbit")]
    [SerializeField] private float cameraSpeed = 120f;
    [SerializeField] private Vector2 cameraYClamp = new Vector2(20f, 55f);
    [SerializeField] private float pivotHeight = 1.3f;
    [SerializeField, Range(0f, 1f)] private float screenYTarget = 0.25f;

    [Header("Idle Animation")]
    [SerializeField] private float sitDownHoldTime = 4f;

    [Header("Camera Smoothing")]
    [SerializeField] private float cameraSmoothTime = 0.05f;

    [Header("Camera Collision")]
    [SerializeField] private LayerMask collisionLayers;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float zoomDampTime = 0.1f;

    private CharacterController characterController;
    private Vector3 targetVelocity;
    private float idleTimer;
    private bool allowSitdown = true;
    private float startDistance, cameraXAngle, cameraYAngle, smoothXAngle, smoothYAngle, xVel, yVel;
    private Camera cam;
    private float currentCameraDistance, distanceVelocity;

    // --- ADDED OnEnable and OnDisable FOR EVENT SUBSCRIPTION ---
    private void OnEnable()
    {
        FishingEvents.OnStartCharging += PlayStartChargingAnim;
        FishingEvents.OnThrowBobber += PlayThrowAnim;
        FishingEvents.OnStartReeling += PlayReelInAnim;
    }

    private void OnDisable()
    {
        FishingEvents.OnStartCharging -= PlayStartChargingAnim;
        FishingEvents.OnThrowBobber -= PlayThrowAnim;
        FishingEvents.OnStartReeling -= PlayReelInAnim;
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
        }
        if (framingTarget == null) framingTarget = playerModel;
    }

    void Update()
    {
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
        HandleCamera();
    }

    // --- NEW METHODS TO PLAY ANIMATIONS ---
    private void PlayStartChargingAnim()
    {
        if (animator && !string.IsNullOrEmpty(startChargingAnim))
            animator.SetTrigger(startChargingAnim);
    }

    private void PlayThrowAnim(Vector3 direction, float force) // Needs to match event signature
    {
        if (animator && !string.IsNullOrEmpty(throwAnim))
            animator.SetTrigger(throwAnim);
    }

    private void PlayReelInAnim()
    {
        if (animator && !string.IsNullOrEmpty(reelInAnim))
            animator.SetTrigger(reelInAnim);
    }

    // --- (Rest of the script is unchanged) ---
    private void HandleMovement()
    {
        float yVelocity = targetVelocity.y;
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camForward.y = 0f; camRight.y = 0f;
        camForward.Normalize(); camRight.Normalize();
        Vector3 moveDirection = (camForward * v + camRight * h).normalized;
        targetVelocity = moveDirection * moveSpeed;
        targetVelocity.y = yVelocity;
    }

    private void HandleRotation()
    {
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
        bool isWalking = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).magnitude > 0.1f;
        animator.SetBool("Walk", isWalking);
    }

    private void HandleIdleAnimation()
    {
        bool isMoving = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).magnitude > 0.1f;
        bool isRotatingCamera = Input.GetMouseButton(1);
        if (isMoving || isRotatingCamera)
        {
            NotifyOfAction();
        }
        else
        {
            idleTimer += Time.deltaTime;
        }
        if (idleTimer >= sitDownHoldTime && allowSitdown)
        {
            animator.SetTrigger("Sitdown");
            allowSitdown = false;
        }
    }

    public void NotifyOfAction()
    {
        idleTimer = 0f;
        allowSitdown = true;
    }

    private void HandleCamera()
    {
        if (!cameraTransform || !playerModel) return;
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X") * cameraSpeed * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * cameraSpeed * Time.deltaTime;
            cameraXAngle += mouseX;
            cameraYAngle -= mouseY;
            cameraYAngle = Mathf.Clamp(cameraYAngle, cameraYClamp.x, cameraYClamp.y);
        }
        smoothXAngle = Mathf.SmoothDampAngle(smoothXAngle, cameraXAngle, ref xVel, cameraSmoothTime);
        smoothYAngle = Mathf.SmoothDampAngle(smoothYAngle, cameraYAngle, ref yVel, cameraSmoothTime);
        Quaternion rotation = Quaternion.Euler(smoothYAngle, smoothXAngle, 0f);
        Vector3 pivot = playerModel.position + Vector3.up * pivotHeight;
        Vector3 forwardDir = rotation * Vector3.forward;
        Vector3 cameraDirection = -forwardDir;
        float targetDistance = startDistance;
        RaycastHit hit;
        if (Physics.SphereCast(pivot, collisionRadius, cameraDirection, out hit, startDistance, collisionLayers))
        {
            targetDistance = hit.distance;
        }
        currentCameraDistance = Mathf.SmoothDamp(currentCameraDistance, targetDistance, ref distanceVelocity, zoomDampTime);
        Vector3 pos0 = pivot + cameraDirection * currentCameraDistance;
        if (cam == null) cam = cameraTransform.GetComponent<Camera>();
        if (cam)
        {
            Vector3 f = rotation * Vector3.forward;
            Vector3 u = rotation * Vector3.up;
            Vector3 target = (framingTarget ? framingTarget.position : playerModel.position);
            Vector3 v0 = target - pos0;
            float yCam0 = Vector3.Dot(v0, u);
            float zCam0 = Mathf.Max(0.0001f, Vector3.Dot(v0, f));
            float tanFov = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float yNdcTarget = Mathf.Clamp(screenYTarget, 0.05f, 0.95f) * 2f - 1f;
            float s = yCam0 - yNdcTarget * zCam0 * tanFov;
            cameraTransform.position = pos0 + u * s;
            cameraTransform.rotation = rotation;
        }
        else
        {
            cameraTransform.position = pos0;
            cameraTransform.rotation = rotation;
        }
    }

    private void HandleCursorLocking()
    {
        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (Input.GetMouseButtonUp(1))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}