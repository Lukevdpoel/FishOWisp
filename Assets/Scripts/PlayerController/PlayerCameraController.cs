using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    public struct CameraInput
    {
        public Transform playerModel;
        public bool areControlsLocked;
        public bool isFightingFish;
        public bool isBountyBoardActive;
        public bool isAiming;
        public Transform activeBountyBoard;
        public Transform activeBobberTransform;
    }

    [Header("Camera Reference")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform framingTarget;

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

    [Header("Camera Lerp Speeds")]
    [SerializeField] private float dialogueCameraLerpSpeed = 3f;
    [SerializeField] private float dialogueCameraYAngle = 20f;
    [SerializeField] private float catchCameraLerpSpeed = 5f;
    [SerializeField] private float fightCameraLerpSpeed = 2f;

    [Header("Camera Smoothing")]
    [SerializeField] private float cameraSmoothTime = 0.05f;

    [Header("Aim Camera Settings")]
    [SerializeField] private float aimZoomDistance = 3.5f;
    [SerializeField] private float aimYAngleOffset = -3f;
    [SerializeField] private float aimCameraLerpSpeed = 4f;

    [Header("Charge Camera Settings")]
    [Tooltip("How many units closer to the player the camera moves at FULL charge (subtracted from the normal/aim distance).")]
    [SerializeField] private float chargeZoomAmount = 3f;
    [Tooltip("Hard floor for camera distance during charge so it never crosses through the player.")]
    [SerializeField] private float chargeMinDistance = 0.6f;
    [Tooltip("Degrees the camera pitches UP at full charge (subtracted from yAngle).")]
    [SerializeField] private float chargePitchUpAngle = 22f;
    [Tooltip("SmoothDamp time (seconds) for the charge-driven camera offset. Higher = smoother / more lag. ~0.6–1.0 feels cinematic.")]
    [SerializeField] private float chargeSmoothTime = 0.7f;
    [Tooltip("Cap on how fast (per second) the smoothed charge value can change. Keep generous — it's just a safety on SmoothDamp.")]
    [SerializeField] private float chargeMaxSpeed = 4f;

    [Header("Camera Collision")]
    [SerializeField] private LayerMask collisionLayers;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float zoomDampTime = 0.1f;

    [Header("Bite Reaction Pitch")]
    [Tooltip("Degrees to pitch the camera down on each individual nibble. Positive = look down.")]
    public float nibblePitchOffset = 5f;
    [Tooltip("How long the nibble pitch is held before releasing back to neutral.")]
    public float nibblePitchHoldTime = 0.25f;
    [Tooltip("Degrees to pitch the camera down while a fish is biting (reaction window). Holds until success or failure.")]
    public float bitePitchOffset = 12f;
    [Tooltip("Lerp speed for the bite/nibble pitch offset to reach its target.")]
    public float bitePitchLerpSpeed = 6f;

    [Header("Static Camera Settings")]
    [SerializeField] private bool useStaticCamera = false;
    [SerializeField] private Transform staticCameraTarget;

    private bool isCatchCameraActive = false;
    private float startDistance, cameraXAngle, cameraYAngle, smoothXAngle, smoothYAngle, xVel, yVel;
    private Camera cam;
    private float currentCameraDistance, distanceVelocity;
    private Vector3 currentPivotPosition;
    private Vector3 pivotVelocity;

    private float nibbleHoldTimer = 0f;
    private bool isBitePitchActive = false;
    private float currentPitchOffset = 0f;

    private float chargeProgressTarget = 0f;
    private float chargeProgress = 0f;
    private float chargeProgressVel = 0f;

    public Transform CameraTransform => cameraTransform;

    public void SetCatchCamera(bool active) => isCatchCameraActive = active;

    private void OnEnable()
    {
        FishingEvents.OnFishNibble += HandleFishNibble;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnHookFishSuccess += HandleBitePitchRelease;
        FishingEvents.OnCancelFishing += HandleBitePitchRelease;

        FishingEvents.OnChargeProgressNormalized += HandleChargeProgress;
        FishingEvents.OnCancelCharging += HandleChargeEnded;
        FishingEvents.OnThrowBobber += HandleChargeReleased;
        FishingEvents.OnCancelFishing += HandleChargeEnded;
    }

    private void OnDisable()
    {
        FishingEvents.OnFishNibble -= HandleFishNibble;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnHookFishSuccess -= HandleBitePitchRelease;
        FishingEvents.OnCancelFishing -= HandleBitePitchRelease;

        FishingEvents.OnChargeProgressNormalized -= HandleChargeProgress;
        FishingEvents.OnCancelCharging -= HandleChargeEnded;
        FishingEvents.OnThrowBobber -= HandleChargeReleased;
        FishingEvents.OnCancelFishing -= HandleChargeEnded;
    }

    private void HandleChargeProgress(float t)
    {
        chargeProgressTarget = Mathf.Clamp01(t);
        if (!hasLoggedChargeEvent)
        {
            hasLoggedChargeEvent = true;
            Debug.Log($"[ChargeCam] First charge event received. zoomAmount={chargeZoomAmount}, pitchUp={chargePitchUpAngle}, startDistance={startDistance}");
        }
    }
    private void HandleChargeEnded() { chargeProgressTarget = 0f; }
    private void HandleChargeReleased(Vector3 dir, float force) { chargeProgressTarget = 0f; }
    private bool hasLoggedChargeEvent;

    private void HandleFishNibble(BobberController b) { nibbleHoldTimer = nibblePitchHoldTime; }
    private void HandleFishBite(BobberController b) { isBitePitchActive = true; nibbleHoldTimer = 0f; }
    private void HandleBitePitchRelease() { isBitePitchActive = false; }

    private void UpdateBitePitchOffset()
    {
        if (nibbleHoldTimer > 0f) nibbleHoldTimer -= Time.deltaTime;

        float target = 0f;
        if (isBitePitchActive) target = bitePitchOffset;
        else if (nibbleHoldTimer > 0f) target = nibblePitchOffset;

        currentPitchOffset = Mathf.Lerp(currentPitchOffset, target, Time.deltaTime * bitePitchLerpSpeed);
    }

    public void Initialize(Transform playerModel)
    {
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

    public void UpdateCamera(CameraInput input)
    {
        if (!cameraTransform || input.playerModel == null) return;
        if (InventoryUI.IsInventoryOpen) return;

        if (useStaticCamera)
        {
            if (staticCameraTarget != null) { cameraTransform.position = staticCameraTarget.position; cameraTransform.rotation = staticCameraTarget.rotation; }
            return;
        }

        if (float.IsNaN(xVel) || float.IsNaN(yVel) || float.IsNaN(distanceVelocity)) { xVel = 0f; yVel = 0f; distanceVelocity = 0f; }

        bool isDialogueCamera = input.areControlsLocked && DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive();
        bool isBoardCamera = input.areControlsLocked && input.isBountyBoardActive && input.activeBountyBoard != null;

        if (isDialogueCamera || isBoardCamera)
        {
            Transform target = isDialogueCamera ? DialogueManager.Instance.currentSpeaker : input.activeBountyBoard;
            if (target != null)
            {
                Vector3 directionToTarget = (target.position - input.playerModel.position).normalized;
                Quaternion targetRot = Quaternion.LookRotation(directionToTarget);

                cameraXAngle = Mathf.LerpAngle(cameraXAngle, targetRot.eulerAngles.y, Time.deltaTime * dialogueCameraLerpSpeed);
                cameraYAngle = Mathf.LerpAngle(cameraYAngle, dialogueCameraYAngle, Time.deltaTime * dialogueCameraLerpSpeed);
            }
        }
        else if (!input.isFightingFish && !isCatchCameraActive && !input.areControlsLocked)
        {
            float mouseX = Input.GetAxis("Mouse X") * cameraSpeed * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * cameraSpeed * Time.deltaTime;
            cameraXAngle += mouseX;
            cameraYAngle -= mouseY;
            cameraYAngle = Mathf.Clamp(cameraYAngle, cameraYClamp.x, cameraYClamp.y);
        }
        else if (isCatchCameraActive)
        {
            cameraYAngle = Mathf.Lerp(cameraYAngle, catchLookDownAngle, Time.deltaTime * catchCameraLerpSpeed);
        }
        else if (input.activeBobberTransform != null && input.isFightingFish)
        {
            Vector3 directionToBobber = (input.activeBobberTransform.position - input.playerModel.position).normalized;
            Quaternion targetRot = Quaternion.LookRotation(directionToBobber);
            cameraXAngle = Mathf.LerpAngle(cameraXAngle, targetRot.eulerAngles.y, Time.deltaTime * fightCameraLerpSpeed);
        }

        UpdateBitePitchOffset();

        chargeProgress = Mathf.SmoothDamp(chargeProgress, chargeProgressTarget, ref chargeProgressVel, chargeSmoothTime, chargeMaxSpeed, Time.deltaTime);
        float chargePitchOffset = -chargePitchUpAngle * chargeProgress;

        smoothXAngle = Mathf.SmoothDampAngle(smoothXAngle, cameraXAngle, ref xVel, cameraSmoothTime);
        smoothYAngle = Mathf.SmoothDampAngle(smoothYAngle, cameraYAngle + currentPitchOffset + chargePitchOffset, ref yVel, cameraSmoothTime);

        Quaternion rotation = Quaternion.Euler(smoothYAngle, smoothXAngle, 0f);
        Vector3 cameraDirection = -(rotation * Vector3.forward);

        Vector3 basePos = input.playerModel.position;
        Vector3 targetPivot;

        if (isCatchCameraActive)
        {
            targetPivot = basePos + Vector3.up * catchVerticalOffset;
            targetPivot += rotation * Vector3.right * catchHorizontalOffset;
            currentPivotPosition = Vector3.SmoothDamp(currentPivotPosition, targetPivot, ref pivotVelocity, pivotSmoothTime);
        }
        else
        {
            targetPivot = basePos + Vector3.up * pivotHeight;
            currentPivotPosition = targetPivot;
            pivotVelocity = Vector3.zero;
        }

        float targetDistance = startDistance;
        RaycastHit hit;

        if (isCatchCameraActive) targetDistance = catchZoomDistance;
        else if (input.isAiming) targetDistance = aimZoomDistance;

        if (!isCatchCameraActive && chargeProgress > 0.001f)
        {
            targetDistance = Mathf.Max(chargeMinDistance, targetDistance - chargeZoomAmount * chargeProgress);
        }

        if (!isCatchCameraActive && Physics.SphereCast(currentPivotPosition, collisionRadius, cameraDirection, out hit, targetDistance, collisionLayers))
            targetDistance = hit.distance;

        currentCameraDistance = Mathf.SmoothDamp(currentCameraDistance, targetDistance, ref distanceVelocity, zoomDampTime);
        Vector3 finalPos = currentPivotPosition + cameraDirection * currentCameraDistance;

        if (cam == null) cam = cameraTransform.GetComponent<Camera>();
        cameraTransform.position = finalPos;
        cameraTransform.rotation = rotation;
    }
}
